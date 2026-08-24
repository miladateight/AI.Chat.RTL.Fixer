# Release and packaging

AI RTL Fixer 1.1.2 produces three release artifacts under `dist\`:

| Output | Path | Notes |
|---|---|---|
| Portable, framework-dependent | `dist\portable-framework-dependent\` | Requires .NET 8 Desktop Runtime. |
| Portable, self-contained | `dist\portable-self-contained-win-x64\` | No runtime prerequisite. |
| Windows installer | `dist\installer\AIChatRTLFixerSetup-1.1.2.exe` | Inno Setup; bundles the self-contained build. |

The installer SHA-256 is written beside the installer as `AIChatRTLFixerSetup-1.1.2.exe.sha256`.

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

The version is declared in **two** places, and only these two are edited by hand:

1. `Directory.Build.props` (`Version`, `AssemblyVersion`, `FileVersion`) — the
   source of truth. The app reads it back from its own assembly at runtime, and
   `scripts\package-mac.ps1` reads it for `Info.plist` and the `.pkg` name.
2. `installer\AI.ChatRTLFixer.iss` (`MyAppVersion`) — Inno Setup needs it at
   compile time. `scripts\package-installer.ps1` reads it back from there for
   the checksum, and `scripts\build-all.ps1` reports whichever installer was
   actually produced.

Everything else derives from those. Do not reintroduce a version literal
anywhere: three separate release bugs came from one — a stale checksum published
against the previous installer, the wrong version stamped into `Info.plist`, and
a build that reported itself as the previous release and so offered itself as an
update forever.

Then update `CHANGELOG.md` (newest entry first) and the README download links.

Before publishing, verify both portable executables, the installer, its SHA-256 file, and the installer upgrade/uninstall path on a Windows machine.
