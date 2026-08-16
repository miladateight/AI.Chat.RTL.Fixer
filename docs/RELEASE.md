# Release and packaging

AI Chat RTL Fixer 1.0.3 produces three release artifacts under `dist\`:

| Output | Path | Notes |
|---|---|---|
| Portable, framework-dependent | `dist\portable-framework-dependent\` | Requires .NET 8 Desktop Runtime. |
| Portable, self-contained | `dist\portable-self-contained-win-x64\` | No runtime prerequisite. |
| Windows installer | `dist\installer\AIChatRTLFixerSetup-1.0.3.exe` | Inno Setup; bundles the self-contained build. |

The installer SHA-256 is written beside the installer as `AIChatRTLFixerSetup-1.0.3.exe.sha256`.

## Prerequisites

- .NET 8 SDK.
- Inno Setup 6 (`.tools\InnoSetup\ISCC.exe`, `iscc` on `PATH`, or a standard Inno Setup installation).
- Playwright Chromium only when running integration tests.

## Build commands

```powershell
# Full release validation and packaging
powershell -ExecutionPolicy Bypass -File scripts\build-all.ps1

# Build, publish and package without tests
powershell -ExecutionPolicy Bypass -File scripts\build-all.ps1 -SkipTests
```

Individual steps:

```powershell
powershell -File scripts\make-branding.ps1
powershell -File scripts\publish.ps1
powershell -File scripts\package-installer.ps1
```

## Version bump checklist

Keep the version aligned in all of these locations:

1. `Directory.Build.props` (`Version`, `AssemblyVersion`, `FileVersion`).
2. `src\AI.ChatRTLFixer.Core\Constants.cs` (`AppVersion`).
3. `installer\AI.ChatRTLFixer.iss` (`MyAppVersion`).
4. `scripts\build-all.ps1` and `scripts\package-installer.ps1` (expected installer filename).
5. Release notes and README download links.

Before publishing, verify both portable executables, the installer, its SHA-256 file, and the installer upgrade/uninstall path on a Windows machine.
