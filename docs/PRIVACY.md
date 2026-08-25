# Privacy policy

AI RTL Fixer fixes right-to-left text rendering inside desktop AI chat applications. To do that it has to sit very close to your conversations, so it is worth being precise about what it touches and what leaves your machine.

**Nothing you type or read is uploaded anywhere.** The app has no server, no account, no analytics and no API keys. The only network request it ever makes is an optional update check, described below.

## What it does with your chat

The app attaches to a supported Electron chat application through its Chrome DevTools Protocol endpoint on local loopback (`127.0.0.1`) and injects a script that changes how that app renders text — paragraph direction, alignment and font — while leaving code blocks, commands, paths and English text left-to-right.

- The endpoint is only ever loopback. A discovered WebSocket URL whose host is not loopback is rejected, and relaunch arguments always bind the debugging endpoint to `127.0.0.1`.
- The app does not open a network listener and does not connect to remote debugging endpoints.
- Chat content is read and restyled in the target application's own process. It is not copied out, not stored, and not transmitted.
- The first relaunch of a target application asks for explicit consent and warns about unsaved work.

## Clipboard

When you copy from a chat, the app can rewrite what lands on the clipboard so right-to-left text stays readable when it is pasted elsewhere. Which behaviour applies is your choice in Settings:

| Copy mode | What lands on the clipboard |
|---|---|
| Original | Exactly what the application put there; the app does not touch it. |
| RTL readable | The same text with invisible Unicode bidi markers around right-to-left passages. |
| RTL readable, no markers | The same text without any invisible markers. |

Clipboard content is transformed in memory and handed straight back. It is never written to disk and never logged.

## What is stored on your computer

```text
Windows:  %AppData%\AIChatRTLFixer\
macOS:    ~/.config/AIChatRTLFixer/
```

That folder holds your settings — enabled applications, font choice, copy mode, language, startup preference — and, if logging is on, log files.

Chat history and clipboard content are never stored.

## Logging

Logging writes structured metadata: which application was detected, whether an injection succeeded, timings, error categories. Text is redacted to a short summary rather than recorded.

There is one exception, and it is worth reading: **Developer mode** allows short, truncated text samples into the log, so that a rendering bug can be reproduced from a report. It is off by default, has to be turned on deliberately in Settings, and the log stays on your machine either way — the app never sends it anywhere. If you are about to share a log file, turn Developer mode off, reproduce the problem, and share that log instead.

Log files rotate and are capped in size.

## Update check

When enabled, the app sends a single HTTPS `GET` to the public GitHub Releases API for this project and reads two fields from the answer: the latest release tag and the release page URL.

- No chat content, clipboard content, account identifier, device identifier, diagnostics or usage data is attached to the request.
- Nothing is downloaded or installed automatically. If a newer version exists you are told, and opening the release page is your click.
- The release page must be an HTTPS URL on `github.com` before the app will open it.
- It can be turned off in Settings.

## What the app never does

- It does not send your conversations, prompts, replies, clipboard or files anywhere.
- It does not collect telemetry, analytics or usage statistics.
- It does not require an account, a licence key or an API key.
- It does not read chats from applications you have not enabled.

## Third-party applications

The chat applications this tool attaches to are separate products with their own privacy policies. AI RTL Fixer changes how their text is displayed on your screen; it has no influence over what those applications themselves send to their own services.

## Questions

Open an issue at <https://github.com/miladateight/AI.RTL.Fixer/issues>, or follow [SECURITY.md](../.github/SECURITY.md) for anything security-sensitive.
