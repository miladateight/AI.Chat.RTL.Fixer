# AI Chat RTL Fixer 0.3.0

This release removes target-application relaunching and focuses on immediate,
low-overhead attachment to an existing safe local endpoint.

## Highlights

- The fixer never closes, kills, launches or restarts a target application.
- Process detection is event-driven, with a low-frequency reconciliation scan
  only as a fallback.
- Expensive process metadata queries are limited to likely target processes.
- Injection is registered for future page navigations and automatically
  reconnects after a local endpoint disconnects.
- Streaming character-data changes, replaced composers and replaced chat roots
  are handled without scanning unrelated interface areas.
- DOM work is debounced, ancestor-collapsed and capped during large render bursts.
- Copy handling uses stable document-level delegation while remaining scoped to
  the configured chat surface.
- Unused relaunch, port-selection and model-name classification code was removed.
- Settings schema upgraded to version 3; obsolete relaunch settings are ignored.

## Safety and limitations

The application connects only to loopback HTTP/WebSocket endpoints advertised
by an already-running matched process. It does not enable a debug endpoint. If
the target was started without one, the target remains untouched and the fixer
shows a waiting status.

No built-in profile is marked Stable until it has been verified against a real
installed application version.

## Validation

- Release build: 0 warnings, 0 errors.
- Automated tests: 69 passed (8 core, 42 rules, 19 browser/integration).
- Added coverage for streaming text-node changes, composer replacement and
  complete chat-root replacement.

---

## یادداشت انتشار فارسی

نسخهٔ 0.3.0 مسیر بستن و اجرای دوبارهٔ برنامه‌های هدف را به‌طور کامل حذف می‌کند
و روی اتصال سریع، امن و کم‌مصرف به endpoint محلی موجود تمرکز دارد.

- ابزار هیچ برنامهٔ هدفی را نمی‌بندد، اجرا نمی‌کند و دوباره راه‌اندازی نمی‌کند.
- تشخیص فرایند رویدادمحور شده و اسکن دوره‌ای فقط پشتیبان کم‌تکرار است.
- تزریق پس از refresh صفحه پایدار می‌ماند و قطع اتصال محلی را بازیابی می‌کند.
- پاسخ‌های streamشونده، تعویض composer و تعویض کامل ریشهٔ گفتگو پوشش داده شده‌اند.
- صف پردازش DOM محدود و تجمیع شده تا هنگام render سنگین کندی ایجاد نکند.
- کدهای بلااستفادهٔ relaunch، انتخاب پورت و تشخیص نام مدل حذف شده‌اند.
- schema تنظیمات به نسخهٔ 3 ارتقا یافته است.

اگر برنامهٔ هدف endpoint محلی موجود نداشته باشد، ابزار به آن دست نمی‌زند و فقط
وضعیت انتظار را نمایش می‌دهد. همهٔ 69 تست خودکار با موفقیت اجرا شده‌اند.
