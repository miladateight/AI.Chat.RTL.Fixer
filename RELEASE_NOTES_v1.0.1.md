# AI Chat RTL Fixer 1.0.1

## Highlights

- Adds the Traycer desktop profile and explicitly excludes the separate Traycer CLI from detection.
- Adds an advanced **Enable browser targets** setting. It is disabled by default, so regular browser windows are ignored.
- Browser detection, attachment and relaunch only become available after the user enables that setting and confirms the normal per-relaunch warning.
- Turning browser targets off restores any active browser-side runtime changes before the browser is removed from tracking.

## Privacy and safety

- No telemetry or analytics.
- Target-app communication is limited to verified local loopback CDP endpoints.
- Browser targets are opt-in and never enabled by default.
- The first relaunch of every target requires explicit confirmation because unsaved work can be lost if an app is closed.
