# Test Plan — AI Chat RTL Fixer v0.1

## Unit tests

### Rules (via Jint, running the canonical rtlfixer.rules.js)

- [x] Persian-only message -> RTL
- [x] Arabic-only message -> RTL
- [x] Hebrew-only message -> RTL
- [x] Urdu-only message -> RTL
- [x] English-only message -> LTR
- [x] Mixed Persian + English -> RTL
- [x] Persian with numbers -> RTL
- [x] Persian with Windows path -> RTL block, winPath token detected
- [x] Persian with Linux path -> RTL block, linuxPath token detected
- [x] Persian with URL -> RTL block, url token detected
- [x] Persian with inline code -> RTL block
- [x] Persian with command -> RTL block
- [x] Markdown heading (Persian) -> RTL
- [x] Markdown bullet list (Persian) -> RTL
- [x] Markdown numbered list (Persian) -> RTL
- [x] Markdown blockquote (Persian) -> RTL
- [x] Code block (fenced) -> Protected + LTR
- [x] Code block with Persian comment -> Protected + LTR
- [x] JSON block -> Protected + LTR
- [x] YAML block -> Protected + LTR
- [x] XML block -> Protected + LTR
- [x] TOML block -> Protected + LTR
- [x] INI block -> Protected + LTR
- [x] env block -> Protected + LTR
- [x] Stack trace -> Protected + LTR
- [x] Diff block -> Protected + LTR
- [x] Log block -> Protected + LTR
- [x] Node inside protected selector -> always Protected + LTR
- [x] Version number token detected
- [x] URL token detected
- [x] Windows path token detected
- [x] Copy: Original passes through
- [x] Copy: RtlReadable adds RLM around RTL text
- [x] Copy: RtlReadableNoMarkers keeps text clean
- [x] Copy: code block plain text has no bidi markers
- [x] Copy: path plain text has no bidi markers
- [x] Copy: URL plain text has no bidi markers
- [x] Copy: HTML RTL uses isolate span
- [x] Copy: HTML escapes special chars

### Core

- [x] SafeLogger.Redact removes all content, keeps metadata
- [x] PortPicker returns a free port in range
- [x] PortPicker rejects invalid ranges
- [x] ProfileRegistry matches known process
- [x] ProfileRegistry does not match unknown process
- [x] No builtin profile is Stable without a verified TestedAppVersion

## Integration tests (Playwright, mock DOM in real headless Chromium)

These run the REAL script built by ScriptBuilder (which embeds the canonical rtlfixer.rules.js) against a mock chat surface:

- [x] Script applies RTL to Persian messages
- [x] Script leaves code blocks LTR
- [x] Script does not touch the sidebar
- [x] Script processes new messages via MutationObserver (simulated streaming)
- [x] Restore removes all data-rtlfixer attributes
- [x] Copy interceptor overrides clipboard (RtlReadable adds RLM)

## Manual verification checklist (v0.1)

To be run against a real installed app once a profile is marked Stable:

- [ ] Process detection finds the target app.
- [ ] Tray shows the app with correct status and "requires relaunch".
- [ ] Relaunch with RTL Fix works with user consent; original args preserved.
- [ ] CDP connect succeeds on 127.0.0.1; bind verified loopback.
- [ ] Style + font + script injected.
- [ ] Loaded chat history is processed on attach.
- [ ] Streaming responses are processed (debounced, no heavy flicker).
- [ ] Composer direction flips while typing RTL.
- [ ] Persian messages: RTL, right-aligned, with Vazirmatn font.
- [ ] Arabic/Hebrew/Urdu messages render correctly.
- [ ] English-only messages stay LTR.
- [ ] Code blocks stay LTR and copy verbatim.
- [ ] Paths and commands are not visually broken.
- [ ] Copy behavior matches selected copy mode.
- [ ] Disable removes all runtime changes (no `data-rtlfixer`, no `#rtlfixer-css`).
- [ ] Exit cleans up.
- [ ] Logs contain no chat text (verify redaction).
- [ ] No sidebar/title/menu/settings changes observed.
- [ ] App does not crash.
- [ ] CPU usage stays within budget (idle ~0%, streaming < 5% one core then < 1%).
- [ ] No external network calls (verify via netstat: only 127.0.0.1).
- [ ] Restarting the target app normally returns it to a clean state.

## Acceptance criteria

- No change visible in sidebar or any UI outside the chat surface.
- Persian chat messages are readable, RTL, right-aligned, with the correct font.
- Arabic/Hebrew/Urdu messages also render correctly.
- English-only chat messages remain LTR.
- Code blocks remain LTR and copy-safe.
- Paths and commands are not visually broken.
- Loaded history is fixed.
- New and streaming messages are fixed without heavy flicker.
- Disable removes runtime changes (DOM verified).
- Logs contain no chat text (redaction verified).
- App does not crash.
- CPU stays within budget.
- No external network calls (netstat verified).
- Normal app restart returns to a fully clean state (runtime-only proven).