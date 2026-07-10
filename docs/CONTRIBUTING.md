# Contributing to AI Chat RTL Fixer

Thank you for your interest. This project is small and focused, so contributions are welcome but must respect the project's strict scope.

## Project structure

```
src/
  AI.ChatRTLFixer.Core        — models, enums, interfaces, constants, settings
  AI.ChatRTLFixer.Diagnostics — privacy-safe logger (redaction)
  AI.ChatRTLFixer.Rules       — shared rule engine loader (canonical JS embedded)
  AI.ChatRTLFixer.Profiles    — AppProfile + builtin profiles
  AI.ChatRTLFixer.Fonts       — bundled Vazirmatn + @font-face builder
  AI.ChatRTLFixer.Clipboard   — copy-mode payload builders
  AI.ChatRTLFixer.Injectors   — CDP client, CSS/script builders, adapter
  AI.ChatRTLFixer.Win32       — process watcher, port picker, relaunch, startup
  AI.ChatRTLFixer.Tray        — WinForms tray app, orchestrator
tests/
  AI.ChatRTLFixer.Rules.Tests       — unit tests via Jint (canonical JS)
  AI.ChatRTLFixer.Core.Tests        — settings, redaction, port, profiles
  AI.ChatRTLFixer.IntegrationTests  — Playwright mock-DOM end-to-end
assets/rules/rtlfixer.rules.js     — the single canonical rule engine (shared)
assets/rules/rule-engine.shared.json — ranges/thresholds data
```

## The shared rule engine (important)

The classification logic lives in **one file**: `assets/rules/rtlfixer.rules.js`. It is:

1. embedded into `AI.ChatRTLFixer.Rules` as a resource,
2. injected verbatim into target chat pages at runtime,
3. executed under Jint in unit tests.

**Never re-implement classification logic in C#.** If you add a rule, add it to `rtlfixer.rules.js` and add a test in `Rules.Tests`. This prevents the C# tests and the JS runtime from diverging.

## Adding a profile

1. Add a method to `BuiltinProfiles` returning an `AppProfile`.
2. Scope selectors to the **chat surface only** — never sidebar, title bar, menus, settings, file tree, code editor or terminal.
3. Set `Status = Planned` until you have verified the selectors against a real installed app version.
4. Only set `Status = Stable` after setting `TestedAppVersion` and `LastVerifiedDate` with real values.
5. Add a row to the Supported Apps table in `README.md`.
6. Do not claim support for an app you have not tested.

## Privacy and logging

- Never log chat text or clipboard content.
- Use `SafeLogger.Redact(...)` for any DOM-derived string.
- Developer mode (short truncated text samples) is opt-in only.

## Non-goals (do not add)

- Browser extension / Chrome extension.
- Terminal or CLI support.
- VS Code extension.
- Full-app RTL / UI translation / sidebar movement.
- Exe patching or permanent modification.
- Telemetry / analytics / external network calls.
- Storing chats or clipboard content.

## Running tests

```
dotnet test
```

Integration tests use Playwright; on first run install the browser:

```
dotnet run --project tests/AI.ChatRTLFixer.IntegrationTests -- install chromium
```

(or run any test once and follow the Playwright prompt).

## License

Code: MIT. Bundled Vazirmatn font: SIL Open Font License 1.1.