<div align="center">

<img src="assets/branding/app-logo.png" alt="AI Chat RTL Fixer" width="120" />

# اِی‌آی چت آر‌تی‌ال فیکسر (AI Chat RTL Fixer)

**اصلاح نمایش متن راست‌به‌چپ (RTL) داخل برنامه‌های چت هوش مصنوعی روی ویندوز — فقط ناحیه‌ی چت؛ کد و دستورها چپ‌به‌راست می‌مانند.**

[English](README.md) - [فارسی](README.fa.md)

[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0a1622)](#پیشنیازها)
[![Version](https://img.shields.io/badge/version-0.5.0--pre-7855ff)](https://github.com/miladateight/AI.Chat.RTL.Fixer/releases/tag/v0.5.0)
[![License](https://img.shields.io/badge/license-MIT-2ea043)](LICENSE)

</div>

---

<div dir="rtl">

**AI Chat RTL Fixer** یک ابزار رایگان و متن‌باز ویندوزی است که در نوار وظیفه (system tray) اجرا می‌شود و نمایش متن راست‌به‌چپ را داخل برنامه‌های چت هوش مصنوعی بهتر می‌کند. این ابزار برای کاربران فارسی، عربی، عبری، اردو و دیگر زبان‌های RTL ساخته شده که از برنامه‌های گرافیکی چت AI روی ویندوز (Electron / WebView2) استفاده می‌کنند و می‌خواهند پیام‌های چت راست‌به‌چپ خوانده شود — در حالی که بلوک‌های کد، مسیر فایل‌ها، URLها، دستورها و متن انگلیسی چپ‌به‌راست و سالم برای کپی باقی می‌مانند.

> **محدوده:** فقط ناحیه‌ی چت. سایدبار، نوار عنوان، منوها، تنظیمات، درخت فایل، ادیتور کد و ترمینال هیچ‌وقت تغییر نمی‌کنند. همه‌چیز **runtime-only** است — با بستن یا غیرفعال‌کردن ابزار، همه‌ی تغییرات حذف می‌شوند و ری‌استارت عادی برنامه‌ی هدف همیشه آن را به حالت تمیز برمی‌گرداند.

> ⚠️ **نسخه‌ی 0.5.0 یک pre-release / نسخه‌ی چارچوبی است.** هنوز هیچ پروفایل برنامه‌ای *Stable* علامت نخورده. تشخیص یک برنامه **به‌معنای** فیکسِ تأییدشده نیست — بخش [برنامه‌های پشتیبانی‌شده](#برنامه‌های-پشتیبانی‌شده) را ببینید.

## دانلود

- نصب‌کننده: [AIChatRTLFixerSetup-0.5.0.exe](https://github.com/miladateight/AI.Chat.RTL.Fixer/releases/download/v0.5.0/AIChatRTLFixerSetup-0.5.0.exe)
- SHA256: [AIChatRTLFixerSetup-0.5.0.exe.sha256](https://github.com/miladateight/AI.Chat.RTL.Fixer/releases/download/v0.5.0/AIChatRTLFixerSetup-0.5.0.exe.sha256)
- همه‌ی نسخه‌ها: [GitHub Releases](https://github.com/miladateight/AI.Chat.RTL.Fixer/releases)

رایگان و متن‌باز. بدون نیاز به حساب کاربری، لایسنس یا دسترسی به اینترنت.

## نکات مهم

- برنامه‌ی سبک در **system tray** — بدون پنجره‌ی سنگین، منوی راست‌کلیک، دابل‌کلیک برای تنظیمات.
- اصلاح **نمایش RTL** برای پیام‌های فارسی، عربی، عبری و اردو.
- حفظ **بلوک کد، مسیر، دستور، URL و متن انگلیسی** به‌صورت چپ‌به‌راست و سالم برای کپی.
- فونت **Vazirmatn** فقط روی ناحیه‌ی چت اعمال می‌شود.
- **فقط ناحیه‌ی چت** — سایدبار، منوها، ادیتور و ترمینال دست‌نخورده می‌مانند.
- **Runtime-only**: با غیرفعال‌کردن یا بستن، همه‌ی تغییرات پاک می‌شود.
- **حریم خصوصی اول**: بدون telemetry، بدون analytics، بدون تماس اینترنتی.
- گزینه‌ی اختیاری **«اجرا هنگام شروع ویندوز»** فقط از تنظیماتِ خودِ برنامه کنترل می‌شود.
- عرضه به‌صورت **installer ویندوزی** به‌همراه دو نسخه‌ی **portable**.

## چطور کار می‌کند

برای برنامه‌های مبتنی بر Electron، این ابزار فقط به **endpoint موجودِ Chrome DevTools Protocol (CDP)** روی loopback محلی (`127.0.0.1`) متصل می‌شود. ابزار هیچ برنامهٔ هدفی را نمی‌بندد، اجرا نمی‌کند و دوباره راه‌اندازی نمی‌کند. به‌محض شناسایی endpoint امن، CSS محدود، فونت Vazirmatn و دسته‌بند جهت متن فوراً اعمال می‌شوند و پس از refresh صفحه نیز فعال می‌مانند. اگر برنامه بدون endpoint محلی اجرا شده باشد، دست‌نخورده باقی می‌ماند و ابزار با مصرف کم منتظر می‌ماند.

## برنامه‌های پشتیبانی‌شده

| برنامه | فناوری UI | وضعیت | توضیح |
|---|---|---|---|
| Claude Desktop | Electron | Planned | selectorها placeholder هستند و منتظر تأیید روی نصب واقعی‌اند. |
| ChatGPT Desktop | Electron | Planned | selectorها placeholder هستند و منتظر تأیید روی نصب واقعی‌اند. |
| Codex Desktop | نامشخص | Unsupported | هنوز تشخیص/تست نشده. |
| ZCode | نامشخص | Unsupported | هنوز تشخیص/تست نشده. |
| بقیه (LM Studio، AnythingLLM، ...) | نامشخص | Unsupported | فناوری UI باید قبل از نوشتن profile تأیید شود. |

**معنی وضعیت‌ها:** *Stable* = تأییدشده روی نسخه‌ی نصب‌شده‌ی واقعی · *Experimental* = کار می‌کند ولی ممکن است بعد از آپدیت برنامه بشکند · *Planned* = شناخته/تشخیص داده می‌شود ولی هنوز تزریق امن ندارد · *Unsupported* = هنوز روش امنی پیدا نشده.

> **در نسخه‌ی 0.5.0 هیچ profile‌ای Stable نیست.** تشخیص به‌معنای پشتیبانی نیست.

## حریم خصوصی و امنیت

- بدون telemetry، بدون analytics، بدون سرویس ابری، بدون حساب کاربری، بدون API key.
- **بدون هیچ تماس اینترنتی** — فقط loopback محلی (`127.0.0.1`) با برنامه‌های هدفِ debug-enabled.
- برای هر session یک **پورت آزادِ تصادفی** انتخاب می‌شود؛ endpoint فقط به `127.0.0.1` بایند می‌شود و آدرس loopback قبل از اتصال تأیید می‌شود.
- تاریخچه‌ی چت و محتوای clipboard هیچ‌وقت ذخیره نمی‌شوند. لاگ‌ها **فقط metadata امن دارند — هیچ متن چتی ثبت نمی‌شود**.
- تنظیمات و لاگ‌ها در `%AppData%\AIChatRTLFixer\` قرار دارند.

## نصب و اجرا

**installer (پیشنهادی):** فایل `AIChatRTLFixerSetup-0.5.0.exe` را اجرا کنید. در `C:\Program Files\AI Chat RTL Fixer` نصب می‌شود، یک shortcut در Start Menu می‌سازد (desktop shortcut اختیاری) و یک uninstaller استاندارد دارد. **«اجرا هنگام شروع ویندوز» را به‌زور فعال نمی‌کند.** هنگام حذف، فایل‌های نصب‌شده پاک می‌شوند و — فقط اگر تأیید کنید — تنظیمات/لاگ‌های شما.

**portable (بدون نصب):** فایل `AI.ChatRTLFixer.Tray.exe` را از یکی از این دو نسخه اجرا کنید:

- **Self-contained (win-x64)** — بدون هیچ پیش‌نیاز (خودش .NET 8 را دارد).
- **Framework-dependent** — سبک؛ به [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) نیاز دارد.

آیکن در ناحیه‌ی اعلان ظاهر می‌شود. دابل‌کلیک برای تنظیمات؛ راست‌کلیک برای منو.

## پیش‌نیازها

- ویندوز 10 یا 11، نسخه‌ی 64 بیت.
- فقط برای نسخه‌ی framework-dependent: ‏.NET 8 Desktop Runtime.

## ساخت و بسته‌بندی از سورس

پیش‌نیازها: **‏.NET 8 SDK** و برای installer **Inno Setup 6** (کامپایلر را می‌توان در `.tools\InnoSetup\` گذاشت یا از <https://jrsoftware.org/isdl.php> نصب کرد).

```powershell
# build + test
dotnet build AI.ChatRTLFixer.sln -c Release
dotnet test  AI.ChatRTLFixer.sln -c Release

# یک دستور: build، test، branding، هر دو نسخه‌ی portable، installer
powershell -ExecutionPolicy Bypass -File scripts\build-all.ps1
```

خروجی‌ها زیر `dist\` ساخته می‌شوند:

```text
dist\portable-framework-dependent\AI.ChatRTLFixer.Tray.exe
dist\portable-self-contained-win-x64\AI.ChatRTLFixer.Tray.exe
dist\installer\AIChatRTLFixerSetup-0.5.0.exe
```

جزئیات در [docs/RELEASE.md](docs/RELEASE.md) و مستندات کامل در [docs/README.md](docs/README.md).

نقشهٔ فعلی پروژه به‌صورت AST-only در [گراف تعاملی](graphify-out/graph.html) و [گزارش ساختاری](graphify-out/GRAPH_REPORT.md) موجود است.

## وضعیت اعتبارسنجی (v0.5.0)

- ‏`dotnet build` (Release): pass — بدون warning و error
- ‏`dotnet test`: pass — ‏71/71 (Core 8، Rules 42، Integration 21 شامل Playwright)
- publish نسخه‌ی framework-dependent: pass
- publish نسخه‌ی self-contained win-x64: pass
- ساخت installer با Inno Setup: pass
- تولید SHA256: pass

## لایسنس فونت

فونت **Vazirmatn** (Regular) تحت **SIL Open Font License 1.1** همراه است. کد پروژه تحت لایسنس **MIT** است. فونت فقط روی ناحیه‌ی چت اعمال می‌شود، نه بقیه‌ی UI برنامه.

## تماس

- تلگرام: [@MiladAteight](https://t.me/MiladAteight)
- ایمیل: ateight088@gmail.com

## لایسنس

Copyright (c) 2026 Milad AT8. این نرم‌افزار تحت **لایسنس MIT** منتشر شده — فایل [LICENSE](LICENSE) را ببینید.

</div>
