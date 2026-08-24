<div align="center">

<img src="assets/branding/app-logo.png" alt="AI Chat RTL Fixer" width="120" />

# AI Chat RTL Fixer

**RTL text for desktop AI chats — chat text is readable; code, commands, paths and English stay LTR.**

[English](README.md) · [فارسی](README.fa.md)

[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0a1622)](#system-requirements)
[![Version](https://img.shields.io/badge/version-1.1.1-7855ff)](https://github.com/miladateight/AI.Chat.RTL.Fixer/releases/tag/v1.1.1)
[![License](https://img.shields.io/badge/license-MIT-2ea043)](LICENSE)

</div>

AI Chat RTL Fixer is a lightweight Windows tray app for Persian, Arabic, Hebrew, Urdu and other RTL-language users of graphical AI chat applications. It changes only the chat surface; sidebars, menus, settings, file trees, editors and terminals are never modified. All target-app changes are runtime-only and are removed when the fixer is disabled or exits.

## Download

- Installer: [AIChatRTLFixerSetup-1.1.1.exe](https://github.com/miladateight/AI.Chat.RTL.Fixer/releases/download/v1.1.1/AIChatRTLFixerSetup-1.1.1.exe)
- SHA-256: [AIChatRTLFixerSetup-1.1.1.exe.sha256](https://github.com/miladateight/AI.Chat.RTL.Fixer/releases/download/v1.1.1/AIChatRTLFixerSetup-1.1.1.exe.sha256)
- [All releases](https://github.com/miladateight/AI.Chat.RTL.Fixer/releases)

## What 1.1.1 does

- Starts quietly in the system tray; the installer selects **Start with Windows** by default.
- Detects supported desktop apps at startup and when they open.
- After one explicit per-app approval, remembered apps are automatically relaunched with a loopback-only CDP endpoint and fixed without another prompt.
- Reattaches after page navigation or a CDP disconnect.
- Keeps code blocks, URLs, commands, file paths and English text left-to-right and copy-safe.
- Checks GitHub Releases at startup (or on demand from the tray). It never installs an update automatically; it opens the official release page only after confirmation.
- Adds the Traycer desktop profile while excluding the separate Traycer CLI.
- Keeps browser targeting off by default. The optional advanced setting makes browser detection and relaunch an explicit choice.
- Makes setup easier: the main settings view now focuses on turning the fixer on, choosing usable apps and selecting chat appearance; technical options stay in a collapsed Advanced section.
- Simplifies the tray menu by grouping update and diagnostic tools under Advanced, and shows clearer ready/paused/working status messages.
- Hides planned or unsupported profiles from the selectable app list while retaining them for safe detection and diagnostics.
- **Fixes a false-positive relaunch:** window-title detection no longer matches an unrelated window (e.g. a photo or document whose file name contains an app's name); a profile is only ever acted on once the user has explicitly enabled it under Settings → Choose your apps.
- **Fixes the Settings window not responding to clicks** when opened from the tray icon.
- **Improves reliability:** malformed settings are normalized safely, relaunch arguments are sanitized consistently, fragmented UTF-8 CDP messages are decoded correctly, and cancelled or failed connections are cleaned up.

## How it works

For Electron desktop apps, the fixer connects only to a Chrome DevTools Protocol endpoint on local loopback (`127.0.0.1`). The first time an app needs such an endpoint, the tray asks for explicit permission to close and reopen that app with loopback-only debugging arguments. The approval can be remembered per app, so later launches are handled automatically.

There is no safe universal way to modify every Windows application or to inject into an app already running without a CDP endpoint. WebView2, Tauri, Qt, WPF and native apps require dedicated, verified adapters.

## Included desktop profiles

| App | Runtime status | Notes |
|---|---|---|
| ChatGPT Desktop | Experimental | Electron profile with app-specific selectors and automatic reattach. |
| Codex Desktop | Experimental | Desktop GUI only; Codex CLI is intentionally excluded. |
| Claude Desktop | Experimental | Electron profile with consent-gated relaunch. |
| ZCode | Experimental | Electron profile with generic chat fallback. |
| Traycer | Experimental | Desktop profile; the separate CLI is excluded. |
| Other Electron desktop AI apps | Experimental | OpenCode, OpenClaw, Hermes, LM Studio, AnythingLLM, Jan, Cherry Studio, Msty and GitHub Copilot profiles are included. |

Experimental means a profile must still be verified against the installed target-app version; app updates can change their DOM. Claude Code and Codex CLI are command-line tools rather than desktop chat surfaces, so they are intentionally not injected into.

## Privacy and safety

- No telemetry, analytics, cloud service, account access or API keys.
- Target-app communication is restricted to verified loopback CDP endpoints.
- Update checks are enabled by default but can be disabled in Settings. They make one HTTPS request to GitHub Releases only and send no chat, account, device, diagnostic or usage data.
- The initial relaunch always needs explicit confirmation because it can close an app with unsaved work.
- Browser windows are ignored by default. Enabling browser targets requires an additional settings confirmation and each relaunch still needs its normal confirmation.
- Chat history and clipboard content are not stored; logs contain safe metadata only.

## Install and run

Run `AIChatRTLFixerSetup-1.1.1.exe`. It installs to `C:\Program Files\AI Chat RTL Fixer`, creates a Start Menu shortcut and a standard uninstaller. "Start with Windows" is selected in setup and remains controllable in Settings. Portable framework-dependent and self-contained `win-x64` builds are also produced under `dist\`.

## Build and package from source

Requires .NET 8 SDK and Inno Setup 6.

```powershell
# Full validation and package build
powershell -ExecutionPolicy Bypass -File scripts\build-all.ps1

# Package without tests
powershell -ExecutionPolicy Bypass -File scripts\build-all.ps1 -SkipTests
```

Outputs:

```text
dist\portable-framework-dependent\AI.ChatRTLFixer.Tray.exe
dist\portable-self-contained-win-x64\AI.ChatRTLFixer.Tray.exe
dist\installer\AIChatRTLFixerSetup-1.1.1.exe
dist\installer\AIChatRTLFixerSetup-1.1.1.exe.sha256
```

See the [changelog](CHANGELOG.md) and the [packaging documentation](docs/RELEASE.md).

## License

The app is MIT licensed. It bundles Vazirmatn under the SIL Open Font License 1.1.
