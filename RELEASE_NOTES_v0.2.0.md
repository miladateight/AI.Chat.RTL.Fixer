# AI Chat RTL Fixer v0.2.0

**Fixes right-to-left (RTL) text rendering inside AI desktop chat apps — chat surface only; code, paths and commands stay LTR.**

This release focuses on making the relaunch flow reliable, adding OpenCode, and
making direction detection and the settings window behave correctly across apps.

## Highlights

**Reliable relaunch (the big one)**
- Attaching now succeeds based on the debug endpoint actually coming up on
  `127.0.0.1`, instead of gating on debug arguments observed on the spawned
  process. Packaged/MSIX Electron apps (Claude, ChatGPT/Codex) frequently
  re-exec into a different PID, which used to make relaunch silently give up.
- Relaunch now waits only for the **main** process to exit before reopening.
  Auxiliary processes (the crash handler, a slow renderer) no longer block it,
  fixing the case where clicking *Relaunch* only closed the app and never
  reopened it.
- Added a shell-execute fallback and a one-shot re-start if the fresh instance
  is forwarded to a surviving single-instance lock and exits immediately.

**Detection & profiles**
- New profile: **OpenCode** desktop (`@opencode-aidesktop`).
- When an app exposes several page targets, the main chat window is preferred
  over hotkey/popup/overlay windows.

**Smarter RTL**
- A line is now right-to-left when it is RTL-heavy **or** simply begins with an
  RTL letter (first-strong, like `dir="auto"`). This fixes Persian-first
  headings whose Latin product names/paths pulled the ratio under the threshold.
- Direction is applied via an inline `direction` + `unicode-bidi: isolate` in
  addition to the `dir` attribute, so app stylesheets can't override it; the
  scan also covers leaf `div`/`td` text blocks.

**Settings window**
- All app profiles are visible without scrolling (OpenCode/ZCode used to be
  hidden below the fold, so they looked unsupported).
- The auto-relaunch option moved into *General* with correct spacing; the
  window is taller so nothing is clipped.

## Verified on a real install

Detected → relaunched → RTL injected, confirmed over CDP on this machine:
**Codex**, **ChatGPT (classic)**, **ZCode**, **OpenCode**. Claude Desktop is
Electron and honors the debug flag as well.

## Downloads

| File | Notes |
|------|-------|
| `AIChatRTLFixerSetup-0.2.0.exe` | Installer (recommended). Installs to Program Files, Start Menu shortcut, standard uninstaller. Bundles .NET 8 — no prerequisites. |
| `AIChatRTLFixerSetup-0.2.0.exe.sha256` | SHA-256 checksum. |

**SHA-256:** `1e621984782f748b121c09ad64b78d0253c3662d89f1cd730ffacfe91bddb91d`

## Validation

`dotnet build` pass · `dotnet test` 68/68 pass · both portable publishes pass · Inno Setup compile pass · SHA-256 generated.

---

<div dir="rtl">

# ای‌چت RTL فیکسر نسخهٔ ۰.۲.۰

**راست‌چین‌سازی متن در برنامه‌های چت هوش مصنوعی دسکتاپ — فقط ناحیهٔ چت؛ کد، مسیر و دستورها چپ‌چین می‌مانند.**

## مهم‌ترین تغییرها

- **ری‌لانچ قابل‌اعتماد:** اتصال حالا بر اساس بالا آمدن واقعی پورت دیباگ روی `127.0.0.1` موفق می‌شود، نه بررسی آرگومان روی یک PID خاص (که در برنامه‌های بسته‌بندی‌شده باعث می‌شد ری‌لانچ بی‌سروصدا شکست بخورد). همچنین باگ «ری‌لانچ فقط برنامه را می‌بندد و باز نمی‌کند» رفع شد.
- **پروفایل جدید OpenCode** اضافه شد.
- **تشخیص جهت هوشمندتر:** یک خط وقتی راست‌چین می‌شود که نسبت RTL بالا باشد **یا** با حرف فارسی/عربی شروع شود (مثل `dir=auto`). این مشکل تیترهای فارسیِ حاوی نام لاتین را حل می‌کند.
- **پنجرهٔ تنظیمات:** همهٔ برنامه‌ها بدون اسکرول دیده می‌شوند (ZCode/OpenCode دیگر پنهان نیستند) و چیدمان گزینه‌ها اصلاح شد.

## تأییدشده روی نصب واقعی

Codex، ChatGPT کلاسیک، ZCode، OpenCode — تشخیص، ری‌لانچ و تزریق RTL از طریق CDP بررسی شد.

</div>
