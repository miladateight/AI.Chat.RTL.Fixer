# Changelog

All notable changes to AI RTL Fixer, newest first.
Each entry matches the corresponding [GitHub release](https://github.com/miladateight/AI.RTL.Fixer/releases).

## [1.0.6](https://github.com/miladateight/AI.RTL.Fixer/releases/tag/v1.0.6) — 2026-08-24

Two changes aimed squarely at making the app understandable to the people it is
for.

### It speaks your language

The interface is now available in Persian, Arabic, Hebrew, Urdu and English.
Persian is the default. This tool exists for people who read right-to-left, so
shipping it only in English asked exactly the wrong audience to read the wrong
direction to configure it.

The first run opens a language picker before anything else. Every language is
listed in its own script — «فارسی», «العربية», «עברית», «اردو» — because someone
who cannot read the current interface language still has to recognise their own,
and "Persian" written in English does not help them. The choice can be changed
any time from the top of Settings.

Choosing a right-to-left language mirrors the interface itself, not just the
text: labels, checkboxes and buttons all move to the right side of the window.

### It tells you when it cannot work, and fixes it on the spot

An app that is already open cannot be joined — it only offers its local
connection when it starts — so the fix needs one relaunch. Until now the app
said nothing about this on screen: the only way to discover it was to open the
tray menu and find "Relaunch with RTL Fix", which is not where anyone looks when
something simply appears not to work.

Now:

- A notification appears when an app is detected that cannot be fixed yet,
  naming it. Clicking it opens Settings.
- Settings shows a banner at the top, above everything else, that names the app,
  explains *why* a relaunch is needed rather than just demanding one, and
  carries the **Relaunch now** button itself.
- The banner appears and disappears on its own as apps come and go.

The confirmation still names the app and still warns about unsaved work. This is
a shorter path to the existing consent flow, not a way around it.

### Verification

150 automated tests pass, up from 137. The 13 new tests check that every
language defines every key, that none defines a key English does not, that
`{0}`-style placeholders match English in all five languages, that an unknown
language code falls back to Persian, and that no translation was left as a copy
of the English source.

## [1.1.2](https://github.com/miladateight/AI.RTL.Fixer/releases/tag/v1.1.2) — 2026-08-24

Starting the fixer no longer closes a chat you are already in.

### Remembered consent was being treated as standing permission

Approving a relaunch once set a flag that let the app relaunch that same target
automatically from then on, so it would not have to ask again. In practice that
meant the fixer, on every start, would immediately close and reopen any approved
app that happened to be open — including one the user was reading or typing in.
No click was involved; simply launching the tray was enough.

Remembered consent now applies only to an app that is opened *after* the fixer
is already running. An app that was already open when the fixer started is one
somebody is using right now, and a click from a previous session is not
permission to close it. Those apps wait for a fresh confirmation, exactly as an
app with no remembered consent does.

The flag defaults to "not already running", so a wiring mistake fails towards
asking rather than towards closing.

### Verification

173 automated tests pass, up from 171.

## [1.1.1](https://github.com/miladateight/AI.RTL.Fixer/releases/tag/v1.1.1) — 2026-08-24

Tables are now fixed properly.

### Table headings were never touched

The scanner classifies the elements it selects, and that list contained `td` but
not `th`. A table's body cells were therefore right-aligned while its heading
row silently was not, which is exactly what a Persian table looks like when it
seems half-fixed. `h6`, `caption`, `figcaption`, `dt` and `summary` were missing
for the same reason and are now included.

### A right-to-left table now reads right-to-left

Column order is decided by the table's own direction, not by its cells. Fixing
every cell individually still leaves the first column on the left, so the table
reads backwards no matter how well each cell is aligned. A table whose content
is right-to-left prose is now mirrored as a whole.

Only its direction is set. Alignment stays with the individual cells, so a
column of numbers, identifiers or code is not dragged along with the heading
text — `Claude pid 1852` stays left-to-right in a table that is otherwise
mirrored. Protected content and code tables are untouched, and the original
`dir` is recorded and restored exactly like every other element the fixer
changes.

### Verification

171 automated tests pass, up from 162. The engine was also run against the exact
table that prompted this — three headings, six cells mixing Persian prose with
Latin identifiers — inside the live chat app: the table and all three headings
classified right-to-left, the Persian cells right-to-left, and the two cells
holding process identifiers left-to-right.

## [1.1.0](https://github.com/miladateight/AI.RTL.Fixer/releases/tag/v1.1.0) — 2026-08-24

A correctness release. Three defects made 1.0.6 unusable for anyone with more
than one chat window open, and the right-to-left interface it had just gained
was itself laid out wrongly.

### It no longer closes an app it cannot bring back

Relaunching an app could leave it shut. The report was blunt and accurate: "it
closes the app but does not reopen it — and then I could not even open Claude
again." Three separate faults combined to produce it.

**Helper processes were mistaken for apps.** An Electron app runs a main process
plus renderer, GPU, utility and crashpad helpers, all identified by `--type=` on
their command line. That filter returned *false* whenever the command line could
not be read — routine for Store-installed apps and for any process that is
shutting down. Every unreadable helper became a target app of its own, so
closing the real main process turned its dying children into a cascade of new
entries each asking to be relaunched. The filter now fails closed: a process
that cannot be identified is never something to close and restart.

**A second window silently defeated the relaunch.** Electron holds a
single-instance lock. With two windows open, closing one and starting a
replacement just hands the launch back to the survivor — which has no debugging
endpoint, so the app looks like it never came back. The relaunch now counts the
other main processes first and refuses, changing nothing, with a message saying
to close the other windows and try again.

**Success was reported before it was known.** A relaunch counted as successful
the moment the process started. A launch that died a second later still looked
like it had worked. The app must now still be alive six seconds on; if it is
not, and nothing of it is running, the package is activated the way the Start
menu would open it, so the user gets their app back — without the fix, which is
the right trade against being left with nothing.

### The right-to-left interface was laid out left-to-right

1.0.6 introduced Persian, Arabic, Hebrew and Urdu, then positioned every control
with hand-written `Location = new Point(14, y)` arithmetic. That is a
left-to-right assumption: under a mirrored layout those coordinates flip and the
controls land past the edge of the window, unreachable. English was unaffected,
which is exactly why it shipped.

Sections now lay their children out with a flow panel, which mirrors itself.
Long sentences wrap instead of overflowing — a Persian label is several times
the length of its English original. The window is resizable rather than a fixed
dialog, and the action buttons sit in a strip pinned to the bottom, so no
quantity of content can push them out of reach. Each language is now rendered
and inspected as an image during development rather than reasoned about.

### Verification

162 automated tests pass, up from 150. Beyond the suite, this build was checked
against the live setup that produced the original report: two Claude windows and
one ChatGPT window. Both Claude windows were correctly refused and left running;
ChatGPT attached and injected without any relaunch; every process id was
unchanged afterwards.

## [1.0.5](https://github.com/miladateight/AI.RTL.Fixer/releases/tag/v1.0.5) — 2026-08-23

A patch release on top of 1.0.4. Everything 1.0.4 introduced is unchanged; this
fixes a version-reporting bug in that build and makes one message honest.

### The app reported the wrong version

1.0.4 carried its version as a literal in the source. It was not bumped with the
rest of the release, so the built 1.0.4 app told the update checker it was
1.0.3, compared that against the newest published tag, and concluded an update
was available — every single time it started. Anyone running 1.0.4 would have
been offered 1.0.4 as an upgrade, forever.

The version is now read from the assembly at runtime, so it comes from the one
place it is declared and cannot drift from the installer or the tag again. The
About dialog, the launch log line and the update check all follow from it.

The old test asserted the constant against a matching literal, which is why it
passed while the shipped build was wrong: it agreed with itself. It now compares
against the built assembly's own stamp, and a second test asserts the value is
parseable — an unparseable version silently disables update checking altogether.

This was the third instance of the same mistake. 1.0.4 already fixed two others,
in the Windows and macOS packaging scripts, where a hardcoded version meant a
release published the *previous* build's checksum and stamped the wrong version
into `Info.plist`. No version is written down twice anywhere now.

### Store-installed apps get an honest answer

"Attach automatically from now on" works by putting the loopback flags on the
shortcut an app is launched from. An app installed from the Microsoft Store as
an MSIX package has no such shortcut: Windows starts it through package
activation, and pinning it creates an app-list entry rather than a shortcut
carrying arguments. ChatGPT and Claude are commonly installed this way.

1.0.4 answered "No shortcut found — pin the app first, then try again", which
sent people after something that cannot exist. The setup now recognises a
packaged app and says what is actually true: start-up flags cannot be attached
to it, and it needs "Relaunch with RTL Fix" once per session, as before.

The feature works as documented for ordinarily installed apps.

### Verification

137 automated tests pass, up from 130. Beyond the suite, this build was
installed and checked against a live chat session: it attached over the loopback
endpoint and injected without restarting anything, 57 blocks were re-aligned
with `dir=rtl` and `unicode-bidi: isolate`, no code element was touched, and the
shortcut setup was confirmed to write, stay idempotent on a second run, and
restore the original arguments exactly on removal.

## [1.0.4](https://github.com/miladateight/AI.RTL.Fixer/releases/tag/v1.0.4) — 2026-08-23

Version 1.0.4 fixes the mistake behind most wrongly aligned answers, and removes
the need to close and reopen a chat app on every session.

### A block is now read to its end

Alignment used to be decided largely by which script had more characters, plus
the first strong letter. An answer that opened with English — a model name, a
file path, an API identifier, a few words of English prose — stayed
left-aligned even when the sentence that followed was clearly Persian. That is
the single most common thing people reported.

Direction is now decided by looking for a **run of consecutive RTL words**
anywhere in the block, however much Latin text surrounds it:

- `OpenAI GPT-5 Turbo Preview Enterprise Workspace: این پاسخ فارسی است.` → right-aligned
- `You should call the initialize method before running the worker thread تا درست کار کند.` → right-aligned
- `src/app/widgets/ocr_panel.py — OCR mismatch را درست کرد` → right-aligned

A single quoted RTL word inside an English sentence is still a quoted term, not
a clause, so `The Persian word for hello is سلام.` stays left-aligned.

### Code is protected more strictly than before

Lowering the bar for RTL text meant raising it for everything that is not prose.
Unfenced source code is now recognised on its own — several lines that end in
`;`, `{` or `}`, open with a comment marker, or start with a language keyword —
so a snippet keeps its direction **even when its comments are Persian**:

```
// این مقدار را تغییر بده
const maxRetries = 5;
```

Multi-line shell command blocks are now protected the same way. A Persian
sentence that merely mentions a keyword (`اگر مقدار درست بود return کن`) is
still prose and is still right-aligned.

### Attach without closing and reopening the app

An Electron app binds its local debugging endpoint once, at startup, from its
command line. Nothing can switch that on for a process that is already
running — which is why every session used to need a relaunch.

**Advanced → Attach automatically from now on** puts the loopback flags on the
shortcuts you launch the app from (Start menu, Desktop, taskbar). From then on
the app starts with the endpoint already enabled and the fixer simply attaches:
no closing, no reopening, no prompt. The current session still needs one final
relaunch, and after that never again.

- Off by default and per app; setting it up asks for confirmation first and
  states exactly how many shortcuts change.
- The endpoint stays bound to 127.0.0.1 and is never reachable from off the machine.
- Fully reversible from the same menu, which restores the original arguments.
- System-wide shortcuts under ProgramData are reported and left untouched rather
  than silently modified, since changing them would affect every account on the PC.

**On macOS** the same menu installs a per-user login item under
`~/Library/LaunchAgents` that starts the app with the flags, since macOS has no
equivalent of a Windows shortcut carrying arguments — Launch Services opens an
app bundle without argv, so there is nothing on the Dock icon to edit. From your
next login onward the fixer attaches on its own. One honest limit: quitting the
app and reopening it from the Dock mid-session starts it without the flag, so
that session still needs a relaunch. Closing that gap would mean rewriting the
target app's bundle to point at a wrapper binary, which breaks its code
signature — this app does not modify other applications' bundles.

### Fixes

- The macOS packaging script hardcoded the version, so a bumped release stamped
  the previous one into `Info.plist` and into the `.pkg` filename. It now reads
  `Directory.Build.props`, the single place the version is declared.
- Thresholds in `rule-engine.shared.json` are read for real: `rtlRunWords`,
  `scatteredRtlRatio` and `codeLineRatio` join the existing ones instead of the
  engine falling back to hard-coded values.
- A settings file that names a persistent port outside 1–65535 no longer leaves
  an app marked as permanently attached to a port nothing listens on.

### Verification

130 automated tests pass, up from 93. The 37 new tests cover the alignment
cases above, code blocks carrying Persian comments, the persistent-launch
argument handling (stable port, idempotent apply, clean removal) and the macOS
login-item plist (argument order, XML escaping, stable labels).

## [1.0.3](https://github.com/miladateight/AI.RTL.Fixer/releases/tag/v1.0.3) — 2026-08-16

Version 1.0.3 makes the app easier to understand while keeping its explicit opt-in and relaunch safety guarantees.

### Easier everyday use

- The main Settings view now focuses on three tasks: turn the fixer on, choose supported apps and adjust chat appearance.
- Browser targeting, remembered relaunch approval and update controls live in a collapsed Advanced section.
- Update and diagnostic tools are grouped under Advanced in the tray/menu-bar menu.
- Planned and unsupported profiles no longer appear as selectable apps.
- Status messages now clearly distinguish paused, waiting, ready and actively working states.

### Reliability fixes

- Right-align Persian/Arabic/Hebrew prose even when it starts with multiple English product or model names.
- Keep bullets and numbered-list markers on the RTL side, including in apps whose own list CSS uses `!important`.
- Normalize malformed or partially missing settings before runtime use.
- Sanitize relaunch arguments consistently and preserve safe macOS quoting and launch-agent XML.
- Decode fragmented UTF-8 Chrome DevTools Protocol messages correctly.
- Dispose failed or cancelled adapters so reconnects do not leak stale connections.
- Restore startup checkboxes when Windows or macOS rejects a requested startup change.

### Safety and privacy

- App profiles remain opt-in.
- The first relaunch of an app still requires explicit confirmation and warns about unsaved work.
- Browser targeting remains off by default and requires separate confirmation.
- No telemetry or analytics were added; target-app traffic remains loopback-only.

## [1.0.2](https://github.com/miladateight/AI.RTL.Fixer/releases/tag/v1.0.2) — 2026-07-26

### Highlights

- **macOS support (new).** A menu bar app for macOS, packaged as a single universal `.pkg` installer that runs natively on both Apple Silicon and Intel Macs, with the same detection and RTL-fixing engine as Windows: font, copy-mode and per-app profile settings, relaunch-with-consent flow, update checker, and detection report export. Ad-hoc signed but not notarized yet (no Apple Developer ID) — see "Installing on macOS" below.
- **Fixes a false-positive relaunch.** Window-title detection (the fallback used when a process name doesn't match a known profile) previously matched *any* window whose title merely contained an app's name — including an unrelated file opened in an image/document viewer whose file name happened to contain, e.g., "ChatGPT". Combined with remembered relaunch consent, this could silently close and reopen an unrelated app. Window-title matching now requires the app name to appear as its own token in the title, and rejects titles that end in a common document/media file extension.
- **App profiles are now strictly opt-in.** A profile the user has never explicitly enabled under Settings → App profiles (or consented to relaunch) is never acted on, even if a process happens to match its detection rules. Profiles you have already used continue to work exactly as before — only ones you've never touched now start disabled instead of enabled by default.
- **Fixes the Settings window not responding to clicks.** Opening Settings from the tray icon (menu item or double-click) could leave the window visible but inert until you manually clicked its title bar, due to a known Windows foreground-focus quirk for windows opened from a tray icon. The window now activates itself immediately. Reopening Settings while it's already open now brings the existing window forward instead of stacking a duplicate.

### Installing on macOS

Download `AIChatRTLFixer-1.0.2-macos.pkg` — one file, works on both Apple Silicon and Intel Macs — and double-click it to install into `/Applications`, same as any other macOS installer.

The app is ad-hoc signed but not notarized yet (no Apple Developer ID), so macOS Gatekeeper will block the very first install with an "unidentified developer" warning:

1. Right-click the `.pkg` → **Open** → confirm **Open** in the dialog (only needed once), then continue through the installer.
2. If that's not offered, go to **System Settings → Privacy & Security** and click **Open Anyway** next to the blocked-app notice, then re-run the installer.

### Privacy and safety

- No telemetry or analytics.
- Target-app communication is limited to verified local loopback CDP endpoints.
- Browser targets remain opt-in and never enabled by default.
- The first relaunch of every target still requires explicit confirmation because unsaved work can be lost if an app is closed.

## [1.0.1](https://github.com/miladateight/AI.RTL.Fixer/releases/tag/v1.0.1) — 2026-07-22

### Highlights

- Adds the Traycer desktop profile and explicitly excludes the separate Traycer CLI from detection.
- Adds an advanced **Enable browser targets** setting. It is disabled by default, so regular browser windows are ignored.
- Browser detection, attachment and relaunch only become available after the user enables that setting and confirms the normal per-relaunch warning.
- Turning browser targets off restores any active browser-side runtime changes before the browser is removed from tracking.

### Privacy and safety

- No telemetry or analytics.
- Target-app communication is limited to verified local loopback CDP endpoints.
- Browser targets are opt-in and never enabled by default.
- The first relaunch of every target requires explicit confirmation because unsaved work can be lost if an app is closed.

## [1.0.0](https://github.com/miladateight/AI.RTL.Fixer/releases/tag/v1.0.0) — 2026-07-21

### Highlights

- Starts quietly in the system tray and the installer selects **Start with Windows** by default.
- Detects target desktop apps at startup and when they launch. After one explicit per-app approval, remembered apps are relaunched with a loopback-only CDP endpoint and fixed automatically.
- Reattaches after target-page navigation or a CDP disconnect, so the RTL fix survives normal app activity without manually restarting the fixer.
- Adds a safe GitHub Releases update check at startup and a manual **Check for updates** tray action. It never downloads or installs a release automatically; it opens the official GitHub release page only after user confirmation.
- Synchronizes the version across the application, portable builds, installer and release documentation.
- Treats a single leading English product/model word as a label when the following prose is RTL, preventing Persian sentences from being left-aligned just because they begin with an English name.

### Privacy and safety

- No telemetry or analytics.
- Target-app communication is limited to verified local loopback CDP endpoints.
- Update checks can be disabled in Settings and contact only the public GitHub Releases API. They send no chat, account, device, diagnostic or usage data.
- The first relaunch of a target app always requires explicit confirmation because unsaved work can be lost if an app is closed.

### Compatibility

ChatGPT Desktop, Codex Desktop, Claude Desktop and ZCode are included as Electron runtime profiles. Profiles remain Experimental until they have been verified against each real app version. CLI-only tools such as Claude Code and Codex CLI intentionally remain out of scope because they do not provide a desktop chat surface to modify.

## [0.5.0](https://github.com/miladateight/AI.RTL.Fixer/releases/tag/v0.5.0) — 2026-07-14

This release fixes the fixer: the app could get permanently stuck "waiting for a
local endpoint" and, even when attached, frequently touched nothing because a
profile's CSS selectors didn't match the app's real DOM. Both are addressed.

### Highlights

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

### Safety

Relaunching a target app is never automatic on first use: it always requires an
explicit click through the tray menu and a Yes on the confirmation dialog, which
names the app and reminds you to save unsaved work first. The fixer still never
enables a debug endpoint by itself and connects only to loopback (127.0.0.1).

### Validation

- Release build: 0 warnings, 0 errors.
- Automated tests: 72 passed (8 core, 42 rules, 22 browser/integration),
  including a new regression test that a mismatched `ChatContainer` selector no
  longer prevents Persian text from being flipped.

---

### یادداشت انتشار فارسی

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

### ایمنی

Relaunch یک برنامهٔ هدف هرگز در اولین بار خودکار نیست: همیشه به یک کلیک صریح روی
منوی tray و تأیید Yes در دیالوگ هشدار نیاز دارد که نام برنامه را می‌آورد و یادآوری
می‌کند اول کار ذخیره‌نشده را ذخیره کنید. ابزار هنوز هرگز خودش debug endpoint فعال
نمی‌کند و فقط به loopback (127.0.0.1) وصل می‌شود.

### اعتبارسنجی

همهٔ ۷۲ تست خودکار (۸ core، ۴۲ rules، ۲۲ browser/integration) با موفقیت اجرا
شدند، شامل یک تست جدید که تضمین می‌کند selector اشتباه دیگر مانع راست‌چین‌شدن متن
فارسی نمی‌شود.

## [0.4.0](https://github.com/miladateight/AI.RTL.Fixer/releases/tag/v0.4.0) — 2026-07-12 (pre-release)

A performance and footprint release. It keeps the attach-only runtime introduced
in 0.3.0 (the fixer never launches, closes or restarts a target app) and makes the
injected fix noticeably lighter on busy chat windows.

### Highlights

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

### Validation

- Release build: 0 warnings, 0 errors.
- Automated tests: 71 passed (8 core, 42 rules, 21 browser/integration), including
  end-to-end runs of the real injected script (inline-child text blocks, layout
  containers, streaming, chat-root replacement, composer replacement, copy,
  restore).

### Safety

The application connects only to loopback (127.0.0.1) HTTP/WebSocket endpoints
advertised by an already-running matched process. It never enables a debug
endpoint. If a target was started without one, it is left untouched and the fixer
shows a waiting status. No built-in profile is marked Stable until verified against
a real installed application version.

---

### یادداشت انتشار فارسی

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

## [0.3.0](https://github.com/miladateight/AI.RTL.Fixer/releases/tag/v0.3.0) — 2026-07-12 (pre-release)

This release removes target-application relaunching and focuses on immediate,
low-overhead attachment to an existing safe local endpoint.

### Highlights

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

### Safety and limitations

The application connects only to loopback HTTP/WebSocket endpoints advertised
by an already-running matched process. It does not enable a debug endpoint. If
the target was started without one, the target remains untouched and the fixer
shows a waiting status.

No built-in profile is marked Stable until it has been verified against a real
installed application version.

### Validation

- Release build: 0 warnings, 0 errors.
- Automated tests: 69 passed (8 core, 42 rules, 19 browser/integration).
- Added coverage for streaming text-node changes, composer replacement and
  complete chat-root replacement.

---

### یادداشت انتشار فارسی

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

## [0.2.0](https://github.com/miladateight/AI.RTL.Fixer/releases/tag/v0.2.0) — 2026-07-11 (pre-release)

**Fixes right-to-left (RTL) text rendering inside AI desktop chat apps — chat surface only; code, paths and commands stay LTR.**

This release focuses on making the relaunch flow reliable, adding OpenCode, and
making direction detection and the settings window behave correctly across apps.

### Highlights

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

### Verified on a real install

Detected → relaunched → RTL injected, confirmed over CDP on this machine:
**Codex**, **ChatGPT (classic)**, **ZCode**, **OpenCode**. Claude Desktop is
Electron and honors the debug flag as well.

### Downloads

| File | Notes |
|------|-------|
| `AIChatRTLFixerSetup-0.2.0.exe` | Installer (recommended). Installs to Program Files, Start Menu shortcut, standard uninstaller. Bundles .NET 8 — no prerequisites. |
| `AIChatRTLFixerSetup-0.2.0.exe.sha256` | SHA-256 checksum. |

**SHA-256:** `1e621984782f748b121c09ad64b78d0253c3662d89f1cd730ffacfe91bddb91d`

### Validation

`dotnet build` pass · `dotnet test` 68/68 pass · both portable publishes pass · Inno Setup compile pass · SHA-256 generated.

---

<div dir="rtl">

## ای‌چت RTL فیکسر نسخهٔ ۰.۲.۰

**راست‌چین‌سازی متن در برنامه‌های چت هوش مصنوعی دسکتاپ — فقط ناحیهٔ چت؛ کد، مسیر و دستورها چپ‌چین می‌مانند.**

### مهم‌ترین تغییرها

- **ری‌لانچ قابل‌اعتماد:** اتصال حالا بر اساس بالا آمدن واقعی پورت دیباگ روی `127.0.0.1` موفق می‌شود، نه بررسی آرگومان روی یک PID خاص (که در برنامه‌های بسته‌بندی‌شده باعث می‌شد ری‌لانچ بی‌سروصدا شکست بخورد). همچنین باگ «ری‌لانچ فقط برنامه را می‌بندد و باز نمی‌کند» رفع شد.
- **پروفایل جدید OpenCode** اضافه شد.
- **تشخیص جهت هوشمندتر:** یک خط وقتی راست‌چین می‌شود که نسبت RTL بالا باشد **یا** با حرف فارسی/عربی شروع شود (مثل `dir=auto`). این مشکل تیترهای فارسیِ حاوی نام لاتین را حل می‌کند.
- **پنجرهٔ تنظیمات:** همهٔ برنامه‌ها بدون اسکرول دیده می‌شوند (ZCode/OpenCode دیگر پنهان نیستند) و چیدمان گزینه‌ها اصلاح شد.

### تأییدشده روی نصب واقعی

Codex، ChatGPT کلاسیک، ZCode، OpenCode — تشخیص، ری‌لانچ و تزریق RTL از طریق CDP بررسی شد.

</div>

## [0.1.0](https://github.com/miladateight/AI.RTL.Fixer/releases/tag/v0.1.0) — 2026-07-10 (pre-release)

**Fixes right-to-left (RTL) text rendering inside AI desktop chat apps — chat surface only; code, paths and commands stay LTR.**

> ⚠️ **Pre-release / framework build.** No app profile is marked *Stable* yet.
> Detecting an app is **not** the same as a verified fix. This build is meant for
> testing and for verifying profiles against real, installed apps.

### What's in this release

- Windows **system-tray** app (Electron target apps via Chrome DevTools Protocol, loopback only).
- Fixes RTL for Persian / Arabic / Hebrew / Urdu chat text; keeps code, paths, URLs and English LTR.
- Bundled **Vazirmatn** font on the chat surface only.
- **Runtime-only**: disabling or quitting removes all changes.
- **Privacy-first**: no telemetry, no analytics, no external network calls.
- Ships as a **Windows installer** + two **portable** builds (self-contained and framework-dependent).

### Downloads

| File | Notes |
|------|-------|
| `AIChatRTLFixerSetup-0.1.0.exe` | Installer (recommended). Installs to Program Files, Start Menu shortcut, standard uninstaller. Bundles .NET 8 — no prerequisites. |
| `AIChatRTLFixerSetup-0.1.0.exe.sha256` | SHA-256 checksum. |

**SHA-256:** `5e3cfc05560c64f01d2e6b470a69f5f9ddde3203a9031c3485edf9247f8a6417`

### Supported apps (honest status)

- **Claude Desktop**, **ChatGPT Desktop** — *Planned* (selectors pending verification on a real install).
- **Codex**, **ZCode**, and others — *Unsupported* (not detected/tested yet).
- **No profile is Stable in v0.1.0.** Detection does not mean support.

### Validation

`dotnet build` pass · `dotnet test` 66/66 pass · both portable publishes pass · Inno Setup compile pass · SHA-256 generated.

---

<div dir="rtl">

## نسخه‌ی ۰.۱.۰ (pre-release)

**اصلاح نمایش متن راست‌به‌چپ داخل برنامه‌های چت هوش مصنوعی روی ویندوز — فقط ناحیه‌ی چت؛ کد، مسیر و دستورها چپ‌به‌راست می‌مانند.**

> ⚠️ **نسخه‌ی pre-release / چارچوبی.** هنوز هیچ profile‌ای *Stable* نیست. تشخیص یک
> برنامه به‌معنای فیکسِ تأییدشده نیست. این نسخه برای تست و تأیید profileها روی
> برنامه‌های واقعیِ نصب‌شده است.

### محتوای این نسخه

- برنامه‌ی **system-tray** ویندوزی (برنامه‌های هدف Electron از طریق CDP، فقط loopback).
- اصلاح RTL برای فارسی/عربی/عبری/اردو؛ حفظ کد، مسیر، URL و انگلیسی به‌صورت LTR.
- فونت **Vazirmatn** فقط روی ناحیه‌ی چت.
- **Runtime-only**: با غیرفعال‌کردن یا بستن، همه‌ی تغییرات پاک می‌شود.
- **حریم خصوصی اول**: بدون telemetry، analytics یا تماس اینترنتی.
- عرضه به‌صورت **installer** + دو نسخه‌ی **portable**.

### دانلودها

- `AIChatRTLFixerSetup-0.1.0.exe` — installer (پیشنهادی؛ بدون پیش‌نیاز، خودش .NET 8 را دارد).
- `AIChatRTLFixerSetup-0.1.0.exe.sha256` — checksum.

**SHA-256:** `5e3cfc05560c64f01d2e6b470a69f5f9ddde3203a9031c3485edf9247f8a6417`

### برنامه‌های پشتیبانی‌شده (وضعیت صادقانه)

- **Claude Desktop** و **ChatGPT Desktop** — *Planned* (منتظر تأیید روی نصب واقعی).
- **Codex**، **ZCode** و بقیه — *Unsupported* (هنوز تشخیص/تست نشده).
- **در v0.1.0 هیچ profile‌ای Stable نیست.** تشخیص به‌معنای پشتیبانی نیست.

### اعتبارسنجی

‏`dotnet build` pass · ‏`dotnet test` ‏66/66 pass · publish هر دو portable pass · ساخت installer pass · تولید SHA-256.

</div>
