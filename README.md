# AI Chat RTL Fixer

AI Chat RTL Fixer is a free and open-source Windows tray tool that improves RTL text rendering inside AI desktop chat applications. It focuses only on the chat area and keeps code, commands, paths and English text left-to-right.

See [docs/README.md](docs/README.md) for the full documentation.

## Quick links

- [Supported apps & status](docs/README.md#supported-apps)
- [CDP and security](docs/SECURITY.md)
- [Privacy](docs/README.md#privacy)
- [How to enable/disable/restore](docs/README.md#how-to-enable--disable)
- [Known limitations](docs/README.md#known-limitations-v01)
- [Roadmap](docs/ROADMAP.md)
- [Contributing](docs/CONTRIBUTING.md)
- [Test plan](docs/TESTPLAN.md)

## Startup detection and safety

At startup the tray app takes a complete process snapshot and then reconciles it
every 2–5 seconds. Applications that were already open are therefore shown in
the tray; detection does not mean that runtime injection is supported.

Apps already exposing a loopback CDP port are attached automatically when
enabled. An app without a CDP port is shown as **Requires Relaunch**. It is
never closed without explicit consent. Optional `AutoRelaunchAfterConsent`
records that consent for future startup relaunches; failed relaunches enter a
cooldown and do not loop. **Export Detection Report** creates a privacy-safe
local JSON snapshot for diagnosing unmatched applications.
- [License](docs/LICENSE)

## Install & run

**Installer (recommended):** run `dist\installer\AIChatRTLFixerSetup-0.1.0.exe`.
It installs to `C:\Program Files\AI Chat RTL Fixer`, adds a Start Menu shortcut
(and an optional desktop shortcut), and registers a standard uninstaller in
*Settings → Apps*. "Start with Windows" is **not** forced by the installer — turn
it on from the app's own Settings if you want it. Uninstalling removes the
installed files and, only if you confirm, your settings/logs in `%AppData%`.

**Portable (no install):** unzip either folder and run `AI.ChatRTLFixer.Tray.exe`.
- `dist\portable-self-contained-win-x64\` — no prerequisites (bundles .NET 8).
- `dist\portable-framework-dependent\` — small, needs the
  [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

After launch, the app lives in the tray (bottom-right). Double-click it to open
Settings; right-click for the menu. Settings live in
`%AppData%\AIChatRTLFixer\settings.json`, logs in
`%AppData%\AIChatRTLFixer\logs\rtlfixer.log`.

## Build

```
dotnet restore
dotnet build AI.ChatRTLFixer.sln
dotnet test  AI.ChatRTLFixer.sln
```

## Packaging (portable + installer)

One command builds, tests, publishes both portable outputs and compiles the
installer into `dist\`:

```
powershell -ExecutionPolicy Bypass -File scripts\build-all.ps1
```

Individual steps:

```
powershell -File scripts\make-branding.ps1       # app-logo.ico + wizard images
powershell -File scripts\publish.ps1             # both portable outputs -> dist\
powershell -File scripts\package-installer.ps1    # Inno Setup installer -> dist\installer\
```

The installer is built with **Inno Setup 6**. A compiler is bundled in
`.tools\InnoSetup\ISCC.exe`; otherwise install Inno Setup 6 from
<https://jrsoftware.org/isdl.php>. See [docs/RELEASE.md](docs/RELEASE.md) and
[assets/branding/README.md](assets/branding/README.md).

GitHub: https://github.com/miladateight/ai-chat-rtl-fixer
