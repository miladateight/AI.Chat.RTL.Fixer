# Test plan — 1.0.1

Run this checklist before publishing a GitHub release. Do not mark an app profile Stable until its real installed version has passed the app-specific checks.

## Automated validation

```powershell
dotnet restore AI.ChatRTLFixer.sln
dotnet build AI.ChatRTLFixer.sln -c Release --no-restore
dotnet test AI.ChatRTLFixer.sln -c Release --no-build
```

- Rule engine: RTL natural language; LTR English; protected code, URLs, commands, paths and clipboard modes.
- Integration: injected script applies to existing content, streaming content, replaced chat/composer roots and restores cleanly.
- Core: process/profile matching, loopback validation, settings migration and runtime state transitions.

## Manual runtime checks

- Enable Start with Windows, sign out/restart and confirm the tray app starts once without showing a main window.
- Start each supported desktop app both before and after the fixer. Confirm discovery and the status shown in the tray.
- On first use, verify relaunch requires an explicit confirmation and warns about unsaved work.
- Approve a relaunch once, reopen the same app and verify remembered approval applies the fix without another prompt.
- Verify CDP is bound only to `127.0.0.1` and that a non-loopback WebSocket target is rejected.
- Verify the fixer survives page navigation, chat streaming and a CDP reconnect.
- Verify the chat area becomes RTL as appropriate while sidebars, menus, editors, terminals and code blocks remain untouched/LTR.
- Disable the global switch and exit the app; confirm runtime changes are restored.

## Update checks

- With update checks enabled, use the tray menu and verify only the official GitHub Releases API is contacted.
- Confirm an available update offers the official GitHub release page and does not download or run an installer automatically.
- Disable update checks in Settings, restart the app and confirm no GitHub request is made.

## Packaging checks

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-all.ps1
```

- Verify both portable executables start and show the correct `1.0.1` version.
- Verify `dist\installer\AIChatRTLFixerSetup-1.0.1.exe` and its `.sha256` file exist.
- Install over the preceding version, preserving user settings; then test uninstall and the optional deletion prompt for `%AppData%\AIChatRTLFixer`.
- Confirm setup offers Start with Windows as selected by default and Settings accurately reflects the resulting Windows Run entry.
