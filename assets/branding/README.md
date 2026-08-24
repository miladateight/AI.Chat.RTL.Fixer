# Branding assets

Two **source** logos live here. Everything else is generated from them by
`scripts/make-branding.ps1` — do not hand-edit the generated files.

## Source files (you provide these)

| File | What it is | Requirements |
|------|------------|--------------|
| `app-logo.png` | The **AI RTL Fixer** application logo (the dark chat-bubble mark) | Square PNG, transparent background, **512×512 or larger** |
| `brand-logo.png` | The **Milad AT8** brand mark (the black/yellow/red "A8") | Square PNG (shipped: 512×512) |

`brand-logo.png` is already present (copied from the AT8 brand asset).

> ⚠️ **`app-logo.png` must be added by you.** Save the application logo you
> provided in chat to `assets/branding/app-logo.png`. Until then, the build
> falls back to the brand mark as a **stand-in** and prints a warning, so the
> compiled icon/installer is not final.

## Generated files (produced by the build)

Two sources, two roles:
- **App icon** comes from `app-logo.png` (the application's own mark).
- **Installer wizard images** — what the user sees **during installation** —
  come from `brand-logo.png` (the Milad AT8 brand).

| File | Source | Purpose |
|------|--------|---------|
| `app-logo.ico` | `app-logo.png` | Multi-size icon (16→256) for the exe, tray, Start Menu shortcut and Setup.exe |
| `installer-sidebar.bmp` | `brand-logo.png` | Inno Setup `WizardImageFile` (164×314, welcome/finish pages) |
| `installer-small.bmp` | `brand-logo.png` | Inno Setup `WizardSmallImageFile` (55×55, header) |

## Regenerate

```powershell
powershell -ExecutionPolicy Bypass -File scripts\make-branding.ps1
```
