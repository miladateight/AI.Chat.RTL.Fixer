# AI Chat RTL Fixer v0.1.0 (pre-release)

**Fixes right-to-left (RTL) text rendering inside AI desktop chat apps — chat surface only; code, paths and commands stay LTR.**

> ⚠️ **Pre-release / framework build.** No app profile is marked *Stable* yet.
> Detecting an app is **not** the same as a verified fix. This build is meant for
> testing and for verifying profiles against real, installed apps.

## What's in this release

- Windows **system-tray** app (Electron target apps via Chrome DevTools Protocol, loopback only).
- Fixes RTL for Persian / Arabic / Hebrew / Urdu chat text; keeps code, paths, URLs and English LTR.
- Bundled **Vazirmatn** font on the chat surface only.
- **Runtime-only**: disabling or quitting removes all changes.
- **Privacy-first**: no telemetry, no analytics, no external network calls.
- Ships as a **Windows installer** + two **portable** builds (self-contained and framework-dependent).

## Downloads

| File | Notes |
|------|-------|
| `AIChatRTLFixerSetup-0.1.0.exe` | Installer (recommended). Installs to Program Files, Start Menu shortcut, standard uninstaller. Bundles .NET 8 — no prerequisites. |
| `AIChatRTLFixerSetup-0.1.0.exe.sha256` | SHA-256 checksum. |

**SHA-256:** `5e3cfc05560c64f01d2e6b470a69f5f9ddde3203a9031c3485edf9247f8a6417`

## Supported apps (honest status)

- **Claude Desktop**, **ChatGPT Desktop** — *Planned* (selectors pending verification on a real install).
- **Codex**, **ZCode**, and others — *Unsupported* (not detected/tested yet).
- **No profile is Stable in v0.1.0.** Detection does not mean support.

## Validation

`dotnet build` pass · `dotnet test` 66/66 pass · both portable publishes pass · Inno Setup compile pass · SHA-256 generated.

---

<div dir="rtl">

# نسخه‌ی ۰.۱.۰ (pre-release)

**اصلاح نمایش متن راست‌به‌چپ داخل برنامه‌های چت هوش مصنوعی روی ویندوز — فقط ناحیه‌ی چت؛ کد، مسیر و دستورها چپ‌به‌راست می‌مانند.**

> ⚠️ **نسخه‌ی pre-release / چارچوبی.** هنوز هیچ profile‌ای *Stable* نیست. تشخیص یک
> برنامه به‌معنای فیکسِ تأییدشده نیست. این نسخه برای تست و تأیید profileها روی
> برنامه‌های واقعیِ نصب‌شده است.

## محتوای این نسخه

- برنامه‌ی **system-tray** ویندوزی (برنامه‌های هدف Electron از طریق CDP، فقط loopback).
- اصلاح RTL برای فارسی/عربی/عبری/اردو؛ حفظ کد، مسیر، URL و انگلیسی به‌صورت LTR.
- فونت **Vazirmatn** فقط روی ناحیه‌ی چت.
- **Runtime-only**: با غیرفعال‌کردن یا بستن، همه‌ی تغییرات پاک می‌شود.
- **حریم خصوصی اول**: بدون telemetry، analytics یا تماس اینترنتی.
- عرضه به‌صورت **installer** + دو نسخه‌ی **portable**.

## دانلودها

- `AIChatRTLFixerSetup-0.1.0.exe` — installer (پیشنهادی؛ بدون پیش‌نیاز، خودش .NET 8 را دارد).
- `AIChatRTLFixerSetup-0.1.0.exe.sha256` — checksum.

**SHA-256:** `5e3cfc05560c64f01d2e6b470a69f5f9ddde3203a9031c3485edf9247f8a6417`

## برنامه‌های پشتیبانی‌شده (وضعیت صادقانه)

- **Claude Desktop** و **ChatGPT Desktop** — *Planned* (منتظر تأیید روی نصب واقعی).
- **Codex**، **ZCode** و بقیه — *Unsupported* (هنوز تشخیص/تست نشده).
- **در v0.1.0 هیچ profile‌ای Stable نیست.** تشخیص به‌معنای پشتیبانی نیست.

## اعتبارسنجی

‏`dotnet build` pass · ‏`dotnet test` ‏66/66 pass · publish هر دو portable pass · ساخت installer pass · تولید SHA-256.

</div>
