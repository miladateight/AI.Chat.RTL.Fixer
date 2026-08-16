# AI Chat RTL Fixer 1.0.3

Version 1.0.3 makes the app easier to understand while keeping its explicit opt-in and relaunch safety guarantees.

## Easier everyday use

- The main Settings view now focuses on three tasks: turn the fixer on, choose supported apps and adjust chat appearance.
- Browser targeting, remembered relaunch approval and update controls live in a collapsed Advanced section.
- Update and diagnostic tools are grouped under Advanced in the tray/menu-bar menu.
- Planned and unsupported profiles no longer appear as selectable apps.
- Status messages now clearly distinguish paused, waiting, ready and actively working states.

## Reliability fixes

- Right-align Persian/Arabic/Hebrew prose even when it starts with multiple English product or model names.
- Keep bullets and numbered-list markers on the RTL side, including in apps whose own list CSS uses `!important`.
- Normalize malformed or partially missing settings before runtime use.
- Sanitize relaunch arguments consistently and preserve safe macOS quoting and launch-agent XML.
- Decode fragmented UTF-8 Chrome DevTools Protocol messages correctly.
- Dispose failed or cancelled adapters so reconnects do not leak stale connections.
- Restore startup checkboxes when Windows or macOS rejects a requested startup change.

## Safety and privacy

- App profiles remain opt-in.
- The first relaunch of an app still requires explicit confirmation and warns about unsaved work.
- Browser targeting remains off by default and requires separate confirmation.
- No telemetry or analytics were added; target-app traffic remains loopback-only.
