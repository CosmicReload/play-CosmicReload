# Play CosmicReload

Play [CosmicReload](https://cosmicreload.com) with the official **Cosmic Client**, with its item skins and other
features switched on.

1. Install the [Cosmic Client](https://cosmicclient.com) as usual.
2. Install **Play CosmicReload** (`PlayCosmicReloadSetup.exe` from [Releases](https://github.com/CosmicReload/play-CosmicReload/releases/latest)), open it and press **Play**.
3. The Cosmic Client opens. Join `play.cosmicreload.com`.

## What it does

The Cosmic Client only turns its features on for a fixed list of server IP addresses, and it asks
`auth.cosmicclient.com` for a login ticket that the (now offline) server never answers, which freezes the game for
up to 30 seconds. This tool starts the official Cosmic Client with a small Java agent that changes exactly
two things, in memory, while the game runs:

| | Normally | With the agent |
|---|---|---|
| Server check | Features only on the client's built-in IP list | Also on `play.cosmicreload.com` (and `localhost`); every other server unchanged |
| Auth ticket | Waits on `auth.cosmicclient.com` until it times out | Skipped at once; CosmicReload starts the session itself |

Nothing else is touched:

- No files of the Cosmic Client are changed, downloaded or redistributed. The agent patches the client's classes as
  they load and only while you play through this tool. Starting the Cosmic Client normally runs it unmodified.
- Your account login stays inside the Cosmic Client. This tool never sees it.
- No admin rights, no hosts file or network changes. The installer is per-user (`%LOCALAPPDATA%\Programs`), and
  the app's only other file is `%LOCALAPPDATA%\CosmicReload\cosmic-agent.jar`.
- The agent is passed with `JAVA_TOOL_OPTIONS`, set only for the Cosmic Client process this tool starts (not
  system-wide).

The whole thing is two Java files (`agent/src`), one C# file (`app/PlayCosmicReload.cs`) and the installer script
(`installer/installer.nsi`).

## Verifying a download

Every release is built by GitHub Actions from the source at its tag ([`release.yml`](.github/workflows/release.yml)),
never on someone's PC, and comes with a signed [build provenance attestation](https://docs.github.com/actions/security-for-github-actions/using-artifact-attestations).
With the [GitHub CLI](https://cli.github.com) you can check that your copy is exactly what that build produced:

```
gh attestation verify PlayCosmicReloadSetup.exe --repo CosmicReload/play-CosmicReload
```

The download on cosmicreload.com is a redirect to the latest release here.

## Updates

When a new version is out, the app asks first: an "Update available" bar with **Update**, **Later** (asked again
next time) and a link to the release notes. Turn on **Auto-update** in the footer to install updates without
being asked (saved in `%LOCALAPPDATA%\CosmicReload\settings.txt`). The one exception is a release that sets a
`minimum` version (`Config.MinimumVersion`): copies older than that install it straight away. That is reserved for
fixes players need to keep playing, such as after a Cosmic Client update breaks the agent.

Updates can only come from a release published in this repository:

- It reads the update manifest straight from this repository's latest GitHub release, not from cosmicreload.com,
  so the website (or whoever controls its domain or hosting) cannot push an update.
- It only accepts an installer at exactly
  `https://github.com/CosmicReload/play-CosmicReload/releases/download/v<version>/PlayCosmicReloadSetup.exe`
  (see `Updater.Check` in `app/PlayCosmicReload.cs`), and only when the download's SHA-256 matches the manifest.
- Every release is public, is built by GitHub Actions from the tagged source and carries an attestation (above).
  An update is therefore always code you can read here first, and the release list is the full history.

What this does not protect against: whoever controls this GitHub repository can publish a release, as with any
auto-updating app. It would still be public and built from public code.

## Building

Double-click `Build.bat` (or run `build.ps1`). Needs a JDK 8 (`COSMIC_JAVA8` or `JAVA_HOME`) and NSIS 3
(`NSIS_HOME`; otherwise a normal install or the copy electron-builder caches). ASM 9.7 is downloaded from Maven
Central and checked against a pinned SHA-256. The app is compiled with the C# compiler built into Windows.
Output: `dist\PlayCosmicReloadSetup.exe` and `dist\play-cosmicreload.json` (the update manifest).

To release: set `Version` in `app/PlayCosmicReload.cs`, commit, and push a matching tag (`v1.2.3`).

## Licence

MIT (see `LICENSE`). The agent bundles [ASM](https://asm.ow2.io) (BSD, see `LICENSE-ASM.txt`).
Not affiliated with Cosmic Client or CosmicPvP.
