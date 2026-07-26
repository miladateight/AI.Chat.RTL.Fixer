# AI Chat RTL Fixer 1.0.2

## Highlights

- **macOS support (new).** A menu bar app for macOS (Apple Silicon and Intel), packaged as a standard `.pkg` installer, with the same detection and RTL-fixing engine as Windows: font, copy-mode and per-app profile settings, relaunch-with-consent flow, update checker, and detection report export. Unsigned/unnotarized for now (no Apple Developer ID yet) — see "Installing on macOS" below.
- **Fixes a false-positive relaunch.** Window-title detection (the fallback used when a process name doesn't match a known profile) previously matched *any* window whose title merely contained an app's name — including an unrelated file opened in an image/document viewer whose file name happened to contain, e.g., "ChatGPT". Combined with remembered relaunch consent, this could silently close and reopen an unrelated app. Window-title matching now requires the app name to appear as its own token in the title, and rejects titles that end in a common document/media file extension.
- **App profiles are now strictly opt-in.** A profile the user has never explicitly enabled under Settings → App profiles (or consented to relaunch) is never acted on, even if a process happens to match its detection rules. Profiles you have already used continue to work exactly as before — only ones you've never touched now start disabled instead of enabled by default.
- **Fixes the Settings window not responding to clicks.** Opening Settings from the tray icon (menu item or double-click) could leave the window visible but inert until you manually clicked its title bar, due to a known Windows foreground-focus quirk for windows opened from a tray icon. The window now activates itself immediately. Reopening Settings while it's already open now brings the existing window forward instead of stacking a duplicate.

## Installing on macOS

Download the `.pkg` matching your Mac — **apple-silicon** for M-series Macs, **intel** for Intel Macs — and double-click it to install into `/Applications`, same as any other macOS installer. A portable `.zip` of the same build is also provided for anyone who prefers not to use the installer.

The app isn't code-signed or notarized yet (no Apple Developer ID), so macOS Gatekeeper will block the very first install with an "unidentified developer" warning:

1. Right-click the `.pkg` → **Open** → confirm **Open** in the dialog (only needed once), then continue through the installer.
2. If that's not offered, go to **System Settings → Privacy & Security** and click **Open Anyway** next to the blocked-app notice, then re-run the installer.

The `.zip` variant needs the equivalent one-time step on the `.app` itself: right-click → **Open**, or `xattr -cr "/path/to/AI Chat RTL Fixer.app"` from Terminal.

## Privacy and safety

- No telemetry or analytics.
- Target-app communication is limited to verified local loopback CDP endpoints.
- Browser targets remain opt-in and never enabled by default.
- The first relaunch of every target still requires explicit confirmation because unsaved work can be lost if an app is closed.
