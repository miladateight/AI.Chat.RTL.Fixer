# AI Chat RTL Fixer 0.4.0

A performance and footprint release. It keeps the attach-only runtime introduced
in 0.3.0 (the fixer never launches, closes or restarts a target app) and makes the
injected fix noticeably lighter on busy chat windows.

## Highlights

- Smarter right-alignment: a message rendered as a plain `<div>`/`<td>` that
  holds only inline formatting (a bold word, a link, a code span) is now
  recognized as real text and flipped, instead of being skipped as a layout
  container. This fixes Persian/Arabic sentences — especially user bubbles and
  streamed answers — that previously stayed left-aligned. Genuine layout
  containers (a `<div>` wrapping block children) are still left untouched.
- Smoother streaming: the injected script caches the chat-container lookup and
  reuses one precomputed selector, so a streaming answer no longer runs a DOM
  query on the target app's main thread for every mutation. Heavy AI chats stay
  responsive.
- Lighter footprint: globalization (ICU) data is dropped and the tray runs on a
  workstation, non-concurrent garbage collector, lowering memory use.
- Safer shutdown: the CDP socket close is time-bounded, so a wedged endpoint can
  never freeze application exit.
- Awesomer installer: stronger LZMA2/ultra64 compression for a smaller download,
  plus an optional "start with Windows" choice during setup.

## Validation

- Release build: 0 warnings, 0 errors.
- Automated tests: 71 passed (8 core, 42 rules, 21 browser/integration), including
  end-to-end runs of the real injected script (inline-child text blocks, layout
  containers, streaming, chat-root replacement, composer replacement, copy,
  restore).

## Safety

The application connects only to loopback (127.0.0.1) HTTP/WebSocket endpoints
advertised by an already-running matched process. It never enables a debug
endpoint. If a target was started without one, it is left untouched and the fixer
shows a waiting status. No built-in profile is marked Stable until verified against
a real installed application version.

---

## یادداشت انتشار فارسی

نسخهٔ 0.4.0 یک انتشار کارایی و سبک‌سازی است. همان رفتار «فقط اتصال» نسخهٔ پیشین
حفظ شده (ابزار هیچ برنامهٔ هدفی را اجرا، بسته یا دوباره راه‌اندازی نمی‌کند) و فیکس
تزریق‌شده روی پنجره‌های پرترافیک محسوس‌تر سبک شده است.

- راست‌چینِ هوشمندتر: پیامی که به‌صورت یک `<div>`/`<td>` ساده با فقط قالب‌بندی inline
  (یک کلمهٔ bold، یک لینک، یک code span) نمایش داده می‌شود حالا به‌عنوان متن واقعی
  شناسایی و راست‌چین می‌شود، نه اینکه به‌عنوان ظرفِ چیدمان نادیده گرفته شود. این همان
  جمله‌های فارسی/عربی — به‌ویژه پیام‌های خودِ کاربر و پاسخ‌های streamشده — را که پیش‌تر
  چپ‌چین می‌ماندند درست می‌کند. ظرف‌های واقعیِ چیدمان (divای که فرزندِ block دارد)
  همچنان دست‌نخورده می‌مانند.
- روان‌تر هنگام streaming: اسکریپت تزریقی، جست‌وجوی ریشهٔ گفتگو را کش می‌کند و از یک
  selector از پیش‌محاسبه‌شده استفاده می‌کند؛ در نتیجه پاسخِ در حال پخش دیگر برای هر
  تغییر DOM یک query روی ریسمان اصلی برنامهٔ هدف اجرا نمی‌کند. چت‌های سنگین هوش
  مصنوعی پاسخ‌گو می‌مانند.
- سبک‌تر: دادهٔ globalization (ICU) حذف شده و tray روی garbage collector از نوع
  workstation و غیرهم‌روند اجرا می‌شود؛ مصرف حافظه کاهش می‌یابد.
- خروج امن‌تر: بستنِ سوکت CDP دارای مهلت زمانی است، بنابراین یک endpoint گیرکرده
  هرگز نمی‌تواند خروج برنامه را قفل کند.

همهٔ ۶۹ تست خودکار (شامل اجرای end-to-end اسکریپت واقعی تزریق) با موفقیت اجرا شدند.
ابزار فقط به endpointهای محلی (127.0.0.1) یک فرایندِ از پیش در حال اجرا متصل می‌شود و
هیچ‌گاه خودش debug endpoint فعال نمی‌کند.
