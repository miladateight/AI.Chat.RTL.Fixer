# AI Chat RTL Fixer 1.0.0

## Highlights

- Starts quietly in the system tray and the installer selects **Start with Windows** by default.
- Detects target desktop apps at startup and when they launch. After one explicit per-app approval, remembered apps are relaunched with a loopback-only CDP endpoint and fixed automatically.
- Reattaches after target-page navigation or a CDP disconnect, so the RTL fix survives normal app activity without manually restarting the fixer.
- Adds a safe GitHub Releases update check at startup and a manual **Check for updates** tray action. It never downloads or installs a release automatically; it opens the official GitHub release page only after user confirmation.
- Synchronizes the version across the application, portable builds, installer and release documentation.
- Treats a single leading English product/model word as a label when the following prose is RTL, preventing Persian sentences from being left-aligned just because they begin with an English name.

## Privacy and safety

- No telemetry or analytics.
- Target-app communication is limited to verified local loopback CDP endpoints.
- Update checks can be disabled in Settings and contact only the public GitHub Releases API. They send no chat, account, device, diagnostic or usage data.
- The first relaunch of a target app always requires explicit confirmation because unsaved work can be lost if an app is closed.

## Compatibility

ChatGPT Desktop, Codex Desktop, Claude Desktop and ZCode are included as Electron runtime profiles. Profiles remain Experimental until they have been verified against each real app version. CLI-only tools such as Claude Code and Codex CLI intentionally remain out of scope because they do not provide a desktop chat surface to modify.
