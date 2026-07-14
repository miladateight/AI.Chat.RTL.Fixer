# AI Chat RTL Fixer 0.5.0

This release fixes the fixer: the app could get permanently stuck "waiting for a
local endpoint" and, even when attached, frequently touched nothing because a
profile's CSS selectors didn't match the app's real DOM. Both are addressed.

## Highlights

- **Relaunch with RTL Fix (opt-in, consent-gated) is back.** Since v0.3.0 the
  fixer only attached to an app that *already* had a Chrome DevTools debug port
  open — which essentially never happens on its own for Claude Desktop,
  ChatGPT/Codex, ZCode, etc., so the tray sat on "waiting for local endpoint"
  indefinitely. A detected app without a debug port now shows up under the tray's
  new "Relaunch with RTL Fix…" menu. Clicking it shows a clear warning (save your
  work first) and only proceeds on an explicit Yes — the app is then closed and
  reopened with `--remote-debugging-port` on loopback only. Once you've approved
  an app once, it's remembered (toggle in Settings) so it reattaches automatically
  next time you open that app, without asking again.
- **The fixer no longer goes silent when a profile's selectors are wrong.**
  Every built-in app profile's CSS selectors are best-effort guesses, and if the
  chat-container selector didn't match an app's actual DOM, the injected script
  scanned nothing at all — leaving every message at the browser's native
  first-strong-character text direction (which is exactly why a message reading
  as LTR the instant it started with an English word was possible even though
  the fixer was "attached"). The scanner now falls back to scanning the whole
  page when the configured selector doesn't match, so real chat text still gets
  found and flipped instead of being silently skipped.
- **Fixed a bogus process match for ZCode.** The ZCode profile also matched
  process names `Zed`/`zed`, which is a completely unrelated, unsupported native
  code editor — removed.

## Safety

Relaunching a target app is never automatic on first use: it always requires an
explicit click through the tray menu and a Yes on the confirmation dialog, which
names the app and reminds you to save unsaved work first. The fixer still never
enables a debug endpoint by itself and connects only to loopback (127.0.0.1).

## Validation

- Release build: 0 warnings, 0 errors.
- Automated tests: 72 passed (8 core, 42 rules, 22 browser/integration),
  including a new regression test that a mismatched `ChatContainer` selector no
  longer prevents Persian text from being flipped.

---

## یادداشت انتشار فارسی

این نسخه خودِ مشکل اصلیِ ابزار را برطرف می‌کند: قبلاً ممکن بود برنامه برای همیشه
روی «در انتظار endpoint محلی» بماند، و حتی وقتی متصل هم می‌شد، اغلب هیچ اثری
نمی‌گذاشت چون selectorهای CSS یک پروفایل با DOM واقعیِ آن برنامه match نمی‌شدند.
هر دو مشکل برطرف شده‌اند.

- **«Relaunch with RTL Fix» (با رضایت صریح کاربر) برگشت.** از نسخهٔ 0.3.0 ابزار
  فقط به برنامه‌ای وصل می‌شد که از قبل یک debug port باز داشته باشد — که برای
  Claude Desktop، ChatGPT/Codex، ZCode و... تقریباً هرگز به‌طور خودکار اتفاق
  نمی‌افتد، پس ابزار برای همیشه روی «در انتظار endpoint محلی» می‌ماند. حالا
  برنامهٔ شناسایی‌شده‌ای که debug port ندارد، زیر منوی جدید «Relaunch with RTL
  Fix…» در tray نمایش داده می‌شود. کلیک روی آن یک هشدار واضح نشان می‌دهد (اول کار
  ذخیره‌نشده را ذخیره کنید) و فقط با تأیید صریح شما ادامه پیدا می‌کند — سپس برنامه
  بسته و با `--remote-debugging-port` فقط روی loopback دوباره باز می‌شود. وقتی
  یک‌بار برنامه‌ای را تأیید کردید، این تأیید به خاطر سپرده می‌شود (قابل تغییر در
  Settings) تا دفعهٔ بعد که آن برنامه را باز می‌کنید، بدون سؤال دوباره وصل شود.
- **وقتی selectorهای یک پروفایل اشتباه باشند، ابزار دیگر ساکت نمی‌ماند.**
  selectorهای CSS همهٔ پروفایل‌های داخلی حدسیاتی بهترین‌تلاش هستند؛ اگر selector
  ظرف گفتگو با DOM واقعیِ برنامه match نمی‌شد، اسکریپت تزریقی اصلاً چیزی اسکن
  نمی‌کرد — یعنی هر پیام با جهت پیش‌فرض native مرورگر (بر اساس اولین حرف قوی از
  نظر جهت) نمایش داده می‌شد؛ دقیقاً همان چیزی که باعث می‌شد پیامی که با یک کلمهٔ
  انگلیسی شروع می‌شود چپ‌چین بماند، حتی وقتی ابزار «متصل» بود. حالا اگر selector
  تنظیم‌شده match نشود، اسکنر روی کل صفحه fallback می‌کند تا متن واقعیِ گفتگو
  پیدا و راست‌چین شود، نه اینکه بی‌صدا نادیده گرفته شود.
- **یک match اشتباه برای ZCode رفع شد.** پروفایل ZCode همچنین نام‌های پردازش
  `Zed`/`zed` را match می‌کرد که یک ویرایشگر کد native کاملاً بی‌ربط و پشتیبانی‌نشده
  است — حذف شد.

## ایمنی

Relaunch یک برنامهٔ هدف هرگز در اولین بار خودکار نیست: همیشه به یک کلیک صریح روی
منوی tray و تأیید Yes در دیالوگ هشدار نیاز دارد که نام برنامه را می‌آورد و یادآوری
می‌کند اول کار ذخیره‌نشده را ذخیره کنید. ابزار هنوز هرگز خودش debug endpoint فعال
نمی‌کند و فقط به loopback (127.0.0.1) وصل می‌شود.

## اعتبارسنجی

همهٔ ۷۲ تست خودکار (۸ core، ۴۲ rules، ۲۲ browser/integration) با موفقیت اجرا
شدند، شامل یک تست جدید که تضمین می‌کند selector اشتباه دیگر مانع راست‌چین‌شدن متن
فارسی نمی‌شود.
