# Security and privacy

## Target-app communication

AI Chat RTL Fixer communicates with supported Electron target apps only through Chrome DevTools Protocol (CDP) endpoints on local loopback (`127.0.0.1` / `localhost`).

- The adapter rejects a discovered WebSocket URL unless its host is loopback.
- Relaunch arguments always bind the debugging endpoint to `127.0.0.1`.
- The first target-app relaunch requires explicit user consent and warns about unsaved work.
- The tool does not expose a network listener or connect to arbitrary remote CDP endpoints.

## Update checks

When enabled, the update check sends a single HTTPS `GET` request to the project's public GitHub Releases API endpoint. It reads only the release tag and GitHub release page URL.

- No automatic download or installation occurs.
- The release page must be an HTTPS URL on `github.com` before the app opens it.
- No chat content, clipboard content, account identifier, device identifier, diagnostics or usage data is attached to the request.
- Users can disable update checks in Settings.

## Local data

- Settings and metadata-only logs are stored under `%AppData%\AIChatRTLFixer\`.
- Chat history and clipboard content are never stored.
- Telemetry, analytics, cloud accounts and API keys are not used.
