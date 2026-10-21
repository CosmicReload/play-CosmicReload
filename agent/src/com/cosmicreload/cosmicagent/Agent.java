package com.cosmicreload.cosmicagent;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.lang.instrument.ClassFileTransformer;
import java.lang.instrument.Instrumentation;
import java.security.ProtectionDomain;
import java.util.jar.JarEntry;
import java.util.jar.JarFile;
import java.util.jar.JarOutputStream;

import org.objectweb.asm.ClassReader;
import org.objectweb.asm.ClassVisitor;
import org.objectweb.asm.ClassWriter;
import org.objectweb.asm.Label;
import org.objectweb.asm.MethodVisitor;
import org.objectweb.asm.Opcodes;

/**
 * Java agent that lets the unmodified Cosmic Client use its features on CosmicReload.
 *
 * Two patches, applied as the client's classes load (nothing on disk is changed):
 * 1. Server check: the client only enables its features when the server's IP is in a fixed list of ranges. The
 *    method doing that check ({@code static boolean x(byte, SocketAddress, long)}) gets a prefix that returns true
 *    when the server is one of the hosts passed to the agent. Every other server keeps the client's own check.
 * 2. Auth: the client asks auth.cosmicclient.com for a ticket on the game thread, and that server now hangs for
 *    ~20 s before failing, freezing the game. Opening a connection to that host fails at once instead; CosmicReload
 *    starts the session without a ticket.
 *
 * Usage: -javaagent:cosmic-agent.jar=play.cosmicreload.com[,other.host,...]
 */
public final class Agent {
    static final String HOOKS = "com/cosmicreload/cosmicagent/Hooks";
    static final String CHECK_DESC = "(BLjava/net/SocketAddress;J)Z";

    private Agent() {
    }

    public static void premain(String args, Instrumentation inst) throws Exception {
        // the client loads its classes in its own class loader; Hooks must be reachable from it, so it goes on the
        // boot class path on its own (the rest of the agent stays in one loader)
        File hooks = File.createTempFile("cosmic-agent-hooks", ".jar");
        hooks.deleteOnExit();
        JarOutputStream out = new JarOutputStream(new FileOutputStream(hooks));
        try {
            out.putNextEntry(new JarEntry(HOOKS + ".class"));
            InputStream in = Agent.class.getResourceAsStream("/" + HOOKS + ".class");
            try {
                byte[] buf = new byte[8192];
                for (int n; (n = in.read(buf)) > 0; ) out.write(buf, 0, n);
            } finally {
                in.close();
            }
        } finally {
            out.close();
        }
        inst.appendToBootstrapClassLoaderSearch(new JarFile(hooks));
        Class.forName("com.cosmicreload.cosmicagent.Hooks", true, null)
                .getMethod("init", String.class).invoke(null, args == null ? "" : args);
        inst.addTransformer(new Transformer());
    }

    static final class Transformer implements ClassFileTransformer {
        @Override
        public byte[] transform(ClassLoader loader, String name, Class<?> redefined, ProtectionDomain domain,
                                byte[] bytes) {
            if (name == null || !name.startsWith("cosmicclient/")) return null;
            try {
                ClassReader reader = new ClassReader(bytes);
                ClassWriter writer = new ClassWriter(ClassWriter.COMPUTE_MAXS);
                Patcher patcher = new Patcher(writer, name);
                reader.accept(patcher, ClassReader.EXPAND_FRAMES);
                return patcher.changed ? writer.toByteArray() : null;
            } catch (Throwable t) {
                System.err.println("[CosmicAgent] could not patch " + name + ": " + t);
                return null;
            }
        }
    }

    static final class Patcher extends ClassVisitor {
        final String owner;
        boolean changed;

        Patcher(ClassVisitor next, String owner) {
            super(Opcodes.ASM9, next);
            this.owner = owner;
        }

        @Override
        public MethodVisitor visitMethod(int access, final String name, String desc, String signature,
                                         String[] exceptions) {
            MethodVisitor mv = super.visitMethod(access, name, desc, signature, exceptions);
            final boolean check = (access & Opcodes.ACC_STATIC) != 0 && CHECK_DESC.equals(desc);
            return new MethodVisitor(Opcodes.ASM9, mv) {
                @Override
                public void visitCode() {
                    super.visitCode();
                    if (!check) return;
                    // if (Hooks.allowed(address)) return true;
                    Label original = new Label();
                    super.visitVarInsn(Opcodes.ALOAD, 1);
                    super.visitMethodInsn(Opcodes.INVOKESTATIC, HOOKS, "allowed", "(Ljava/net/SocketAddress;)Z", false);
                    super.visitJumpInsn(Opcodes.IFEQ, original);
                    super.visitInsn(Opcodes.ICONST_1);
                    super.visitInsn(Opcodes.IRETURN);
                    super.visitLabel(original);
                    super.visitFrame(Opcodes.F_NEW, 3,
                            new Object[] {Opcodes.INTEGER, "java/net/SocketAddress", Opcodes.LONG}, 0, new Object[0]);
                    changed = true;
                    System.out.println("[CosmicAgent] server check patched: " + owner + "." + name);
                }

                @Override
                public void visitMethodInsn(int opcode, String insnOwner, String insnName, String insnDesc,
                                            boolean itf) {
                    if (opcode == Opcodes.INVOKEVIRTUAL && "java/net/URL".equals(insnOwner)
                            && "openConnection".equals(insnName) && "()Ljava/net/URLConnection;".equals(insnDesc)) {
                        super.visitMethodInsn(Opcodes.INVOKESTATIC, HOOKS, "openConnection",
                                "(Ljava/net/URL;)Ljava/net/URLConnection;", false);
                        changed = true;
                        return;
                    }
                    super.visitMethodInsn(opcode, insnOwner, insnName, insnDesc, itf);
                }
            };
        }
    }
}
