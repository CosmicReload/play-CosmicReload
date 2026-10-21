package com.cosmicreload.cosmicagent;

import java.io.IOException;
import java.net.ConnectException;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.SocketAddress;
import java.net.URL;
import java.net.URLConnection;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

/**
 * Called from the patched client code. Loaded from the boot class path so the client's class loader sees it; it
 * must stay a single class (no inner or anonymous classes), since only Hooks.class is copied there.
 */
public final class Hooks implements Runnable {
    static final String AUTH_HOST = "auth.cosmicclient.com";
    static final long REFRESH_MS = 60000L;

    private static final List<String> hosts = new ArrayList<String>();
    private static volatile Set<String> addresses = Collections.emptySet();
    private static volatile boolean authLogged;

    private Hooks() {
    }

    public static void init(String args) {
        for (String host : args.split(",")) {
            if (!host.trim().isEmpty()) hosts.add(host.trim().toLowerCase());
        }
        System.out.println("[CosmicAgent] Cosmic features allowed on: " + hosts);
        // resolve off the game thread (the server check runs on the network thread and must not wait on DNS)
        Thread resolver = new Thread(new Hooks(), "CosmicAgent resolver");
        resolver.setDaemon(true);
        resolver.start();
    }

    @Override
    public void run() {
        while (true) {
            resolve();
            try {
                Thread.sleep(REFRESH_MS);
            } catch (InterruptedException e) {
                return;
            }
        }
    }

    static void resolve() {
        Set<String> found = new HashSet<String>();
        for (String host : hosts) {
            try {
                for (InetAddress a : InetAddress.getAllByName(host)) found.add(a.getHostAddress());
            } catch (IOException e) {
                System.err.println("[CosmicAgent] could not resolve " + host + ": " + e.getMessage());
            }
        }
        if (!found.isEmpty() || addresses.isEmpty()) addresses = found;
    }

    /** True when the connected server is one of the agent's hosts. */
    public static boolean allowed(SocketAddress address) {
        if (!(address instanceof InetSocketAddress)) return false;
        InetAddress ip = ((InetSocketAddress) address).getAddress();
        return ip != null && addresses.contains(ip.getHostAddress());
    }

    /** URL.openConnection, except the dead auth server fails at once instead of hanging the game. */
    public static URLConnection openConnection(URL url) throws IOException {
        if (AUTH_HOST.equalsIgnoreCase(url.getHost())) {
            if (!authLogged) {
                authLogged = true;
                System.out.println("[CosmicAgent] skipped " + AUTH_HOST + " (the server starts the session itself)");
            }
            throw new ConnectException(AUTH_HOST + " skipped by CosmicAgent");
        }
        return url.openConnection();
    }
}
