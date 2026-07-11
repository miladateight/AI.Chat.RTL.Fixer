# Security

## CDP and loopback-only communication

AI Chat RTL Fixer communicates with supported Electron apps using the **Chrome DevTools Protocol (CDP)** over **local loopback (127.0.0.1) only**.

- The tool never opens a debug port or changes another process. It only considers a port explicitly advertised in a matched process command line.
- The adapter **verifies** the discovered WebSocket URL is loopback before connecting. If the endpoint is not bound to `127.0.0.1` / `localhost`, attachment is refused and the profile is not marked Stable.
- No external network calls are made. A loopback-only HTTP proxy guard is used in the discovery client as defence in depth.

## No external network calls

No telemetry, analytics, cloud, account access or API keys. The tool does not send clipboard data or chat content anywhere. All processing is local.

## No process lifecycle control

The tool never closes, kills, launches or restarts a target application. A process without an existing local endpoint is reported as waiting and remains untouched.

## Runtime-only

No binary patching. No permanent modification of target app files. All changes are in-memory DOM modifications that vanish when the target app is restarted normally or when AI Chat RTL Fixer is disabled/quits.

## Risks

Running any application with a remote debugging port means another local process could potentially access that page's content while the port is open. AI Chat RTL Fixer refuses non-loopback HTTP and WebSocket addresses, but users remain responsible for how the target application endpoint was enabled.
