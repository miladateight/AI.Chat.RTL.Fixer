# AI Chat RTL Fixer 1.0.2

## Highlights

- **Fixes a false-positive relaunch.** Window-title detection (the fallback used when a process name doesn't match a known profile) previously matched *any* window whose title merely contained an app's name — including an unrelated file opened in an image/document viewer whose file name happened to contain, e.g., "ChatGPT". Combined with remembered relaunch consent, this could silently close and reopen an unrelated app. Window-title matching now requires the app name to appear as its own token in the title, and rejects titles that end in a common document/media file extension.
- **App profiles are now strictly opt-in.** A profile the user has never explicitly enabled under Settings → App profiles (or consented to relaunch) is never acted on, even if a process happens to match its detection rules. Profiles you have already used continue to work exactly as before — only ones you've never touched now start disabled instead of enabled by default.
- **Fixes the Settings window not responding to clicks.** Opening Settings from the tray icon (menu item or double-click) could leave the window visible but inert until you manually clicked its title bar, due to a known Windows foreground-focus quirk for windows opened from a tray icon. The window now activates itself immediately. Reopening Settings while it's already open now brings the existing window forward instead of stacking a duplicate.

## Privacy and safety

- No telemetry or analytics.
- Target-app communication is limited to verified local loopback CDP endpoints.
- Browser targets remain opt-in and never enabled by default.
- The first relaunch of every target still requires explicit confirmation because unsaved work can be lost if an app is closed.
