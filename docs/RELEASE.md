# Release & packaging — AI Chat RTL Fixer

This project ships in three forms, all produced under `dist\`:

| Output | Path | Notes |
|--------|------|-------|
| Portable, framework-dependent | `dist\portable-framework-dependent\` | Small (~0.8 MB exe). Needs the .NET 8 Desktop Runtime. |
| Portable, self-contained | `dist\portable-self-contained-win-x64\` | ~63 MB exe. No prerequisites. |
| Windows installer | `dist\installer\AIChatRTLFixerSetup-0.4.0.exe` | Inno Setup, bundles the self-contained build. |

## Prerequisites

- **.NET 8 SDK** (build/test/publish).
- **Inno Setup 6** for the installer. A compiler is bundled at
  `.tools\InnoSetup\ISCC.exe`; otherwise install from
  <https://jrsoftware.org/isdl.php> (`iscc` on PATH or the default location is
  auto-detected).
- Playwright browser for the integration tests:
  `tests\AI.ChatRTLFixer.IntegrationTests\bin\Release\net8.0\playwright.ps1 install chromium`.

## One-command build

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-all.ps1
```

Runs: build → test → branding → publish (both) → installer.
Use `-SkipTests` to skip the test step.

## Individual scripts

```powershell
powershell -File scripts\make-branding.ps1        # app-logo.ico + installer wizard images
powershell -File scripts\publish.ps1              # both portable outputs
powershell -File scripts\publish.ps1 -SelfContainedOnly
powershell -File scripts\package-installer.ps1     # publish self-contained + compile installer
powershell -File scripts\package-installer.ps1 -SkipPublish   # compile installer only
```

## Rebuild the installer only

```powershell
powershell -ExecutionPolicy Bypass -File scripts\package-installer.ps1
```

(or compile the script directly:
`.tools\InnoSetup\ISCC.exe installer\AI.ChatRTLFixer.iss`)

## Branding

Source logos live in `assets\branding\` (`app-logo.png`, `brand-logo.png`).
`make-branding.ps1` regenerates `app-logo.ico` and the installer wizard BMPs
from them. See [../assets/branding/README.md](../assets/branding/README.md).

> If `app-logo.png` is absent, the build uses `brand-logo.png` as a stand-in and
> prints a warning — the icons are then **not final**. Drop the real app logo in
> and re-run.

## Version bump

Keep the version identical in all four places:

1. `Directory.Build.props` — `<Version>`, `<AssemblyVersion>`, `<FileVersion>`.
2. `installer\AI.ChatRTLFixer.iss` — `#define MyAppVersion`.
3. `scripts\package-installer.ps1` — the expected installer filename.
4. Source strings — `Program.cs` and `TrayApplicationContext.ShowAbout()`.
