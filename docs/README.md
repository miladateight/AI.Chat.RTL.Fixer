# AI Chat RTL Fixer

AI Chat RTL Fixer is a free and open-source Windows tray tool that improves RTL text rendering inside AI desktop chat applications. It focuses only on the chat area and keeps code, commands, paths and English text left-to-right.

It is designed for Persian, Arabic, Hebrew, Urdu and other RTL-language users who use graphical Windows AI desktop apps (Electron, WebView2, etc.) and want chat text to be readable from right to left, while code blocks, file paths, URLs, commands and technical snippets stay left-to-right and copy-safe.

> **Scope:** chat surface only. The sidebar, title bar, menus, settings, file tree, code editor and terminal panels are never modified.

## How it works (v0.1)

For Electron-based apps, AI Chat RTL Fixer attaches only to a CDP endpoint already exposed on **local loopback (127.0.0.1)** by a matched process. It never closes, starts or restarts target apps. The injected bootstrap is scoped to the chat surface and persists across page navigation.

Everything is **runtime-only**: closing AI Chat RTL Fixer (or disabling it) removes all modifications. Restarting the target app normally always returns it to a clean state.

## Supported Apps

| App | UI technology | Status | Notes |
|---|---|---|---|
| Claude Desktop | Electron | Planned | Selectors are placeholders pending verification against a real installed version. |
| ChatGPT Desktop | Electron | Planned | Selectors are placeholders pending verification against a real installed version. |
| Codex Desktop | Unknown | Unsupported | Not detected/tested yet. |
| ZCode | Unknown | Unsupported | Not detected/tested yet. |
| OpenClaw | Unknown | Unsupported | Not detected/tested yet. |
| Hermes Agent | Unknown | Unsupported | Not detected/tested yet. |
| LM Studio | Unknown | Unsupported | UI tech must be confirmed before a profile is written. |
| AnythingLLM Desktop | Unknown | Unsupported | Not detected/tested yet. |

### Status meanings

- **Stable** — tested and reliable against a real, installed app version. A profile is only ever marked Stable after its selectors have been verified and `TestedAppVersion`/`LastVerifiedDate` are set.
- **Experimental** — works but may break after app updates; selectors not finalized.
- **Planned** — the app is detected/known, but no safe injection is implemented yet.
- **Unsupported** — no safe method found yet.

> **No profile is marked Stable in v0.1 without a verified installed version.** Detection does not mean support.

## CDP and security

This tool uses Chrome DevTools Protocol to inject CSS/font/script into the chat surface of Electron apps. Security model:

- No port is opened by the tool; only an endpoint already advertised by the matched process is considered.
- The debug endpoint is bound to **`127.0.0.1` only** (`--remote-debugging-address=127.0.0.1`).
- The adapter **verifies** the WebSocket URL is loopback before connecting.
- **No external network calls. Only local loopback communication with debug-enabled target apps.**

Running any app with a remote debugging port carries a risk: another local process could potentially access the page content. AI Chat RTL Fixer limits its own connection to verified loopback-only HTTP and WebSocket addresses and never controls the target process lifecycle.

## Privacy

- No telemetry.
- No analytics.
- No external network calls.
- No cloud service, no account access, no API keys.
- No storing of chat history or clipboard content.
- Logs contain only safe metadata (app detected, profile loaded, injection success/failure, error codes, selector names, performance metrics). **No chat text is ever written to logs.**
- Developer mode (opt-in, with a clear warning) is the only way short, truncated text samples may appear in logs, and is off by default.

## How to enable / disable

- **Enable:** right-click the tray icon and check "Enable AI Chat RTL Fixer (global)", or open Settings and toggle per-app.
- **Disable:** uncheck the global toggle, or disable a specific app in Settings. All runtime modifications are removed immediately.

## How to restore

- Disabling or quitting AI Chat RTL Fixer removes all injected styles, attributes and event handlers.
- If cleanup is incomplete, the tool can (with your consent) soft-reload the chat page as a last resort.
- Restarting the target app normally always returns it to a clean, unmodified state — this is the ultimate fallback and a core guarantee of the runtime-only design.

## Known limitations (v0.1)

- Only Electron apps via CDP are supported; WebView2/Tauri/Qt/WPF adapters are Planned and not implemented until tested.
- No profile is Stable yet — selectors must be collected from real installed app versions before any app is marked Stable.
- The tool never closes, launches or restarts a target app.
- Electron single-instance lock may reject a second instance launched with debug args; in that case the profile is reported as Experimental/Unsupported and manual reopen is advised.

## How to report a broken app profile after an app update

App updates can change DOM selectors. If the fix stops working after an update:

1. Open Settings and check the detected app's status (it may show injection failure).
2. Open the logs at `%AppData%\AIChatRTLFixer\logs\rtlfixer.log` (metadata only — no chat text).
3. File an issue at the GitHub link below with: app name, app version, OS version, and the relevant log lines. Do **not** paste chat content.

## How to contribute a profile

See [CONTRIBUTING.md](CONTRIBUTING.md). A profile is an `AppProfile` with selectors scoped to the chat surface only. Before marking a profile Stable, verify its selectors against a real installed version and set `TestedAppVersion`/`LastVerifiedDate`.

## Font license note

AI Chat RTL Fixer bundles **Vazirmatn** (Regular) for chat text. Vazirmatn is licensed under the **SIL Open Font License 1.1 (OFL)** — see the OFL terms at https://openfontlicense.org. The project code itself is MIT licensed (see [LICENSE](LICENSE)). The bundled font is applied only to the chat surface and composer, never to the rest of the app UI.

## Install

Two builds are produced:

- **Framework-dependent** (~small): requires the .NET 8 Desktop Runtime installed on your machine.
- **Self-contained win-x64** (~larger): bundles the runtime; no prerequisites.

Download the `AI.ChatRTLFixer.Tray.exe` for your preferred build and run it. The tray icon appears in the system notification area.

## Build from source

Requirements: .NET 8 SDK.

```
dotnet restore
dotnet build
dotnet test
dotnet publish src/AI.ChatRTLFixer.Tray -p:PublishProfile=framework-dependent
dotnet publish src/AI.ChatRTLFixer.Tray -p:PublishProfile=self-contained-win-x64
```

GitHub: https://github.com/miladateight/AI.Chat.RTL.Fixer
