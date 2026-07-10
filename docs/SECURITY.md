# Security

## CDP and loopback-only communication

AI Chat RTL Fixer communicates with supported Electron apps using the **Chrome DevTools Protocol (CDP)** over **local loopback (127.0.0.1) only**.

- A **random free TCP port** is chosen per session from a high range. No fixed port is used.
- The debug endpoint is bound to `127.0.0.1` explicitly (`--remote-debugging-address=127.0.0.1`).
- The adapter **verifies** the discovered WebSocket URL is loopback before connecting. If the endpoint is not bound to `127.0.0.1` / `localhost`, attachment is refused and the profile is not marked Stable.
- No external network calls are made. A loopback-only HTTP proxy guard is used in the discovery client as defence in depth.

## No external network calls

No telemetry, analytics, cloud, account access or API keys. The tool does not send clipboard data or chat content anywhere. All processing is local.

## Relaunch and consent

The tool never closes or restarts a target app without explicit user consent. Before relaunch, a warning prompts the user to review unsaved work, in-flight messages or sensitive sessions. If a safe relaunch is not possible, the tool advises manual reopen and marks the profile Experimental/Unsupported.

## Runtime-only

No binary patching. No permanent modification of target app files. All changes are in-memory DOM modifications that vanish when the target app is restarted normally or when AI Chat RTL Fixer is disabled/quits.

## Risks

Running any application with a remote debugging port means any local process could potentially access that page's content while the port is open. AI Chat RTL Fixer mitigates this with a random port and loopback binding. Users should only relaunch apps through this tool and restart them normally when finished.