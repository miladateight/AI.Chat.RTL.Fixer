# AI Chat RTL Fixer 1.0.4

Version 1.0.4 fixes the mistake behind most wrongly aligned answers, and removes
the need to close and reopen a chat app on every session.

## A block is now read to its end

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

## Code is protected more strictly than before

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

## Attach without closing and reopening the app

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

## Fixes

- The macOS packaging script hardcoded the version, so a bumped release stamped
  the previous one into `Info.plist` and into the `.pkg` filename. It now reads
  `Directory.Build.props`, the single place the version is declared.
- Thresholds in `rule-engine.shared.json` are read for real: `rtlRunWords`,
  `scatteredRtlRatio` and `codeLineRatio` join the existing ones instead of the
  engine falling back to hard-coded values.
- A settings file that names a persistent port outside 1–65535 no longer leaves
  an app marked as permanently attached to a port nothing listens on.

## Verification

130 automated tests pass, up from 93. The 37 new tests cover the alignment
cases above, code blocks carrying Persian comments, the persistent-launch
argument handling (stable port, idempotent apply, clean removal) and the macOS
login-item plist (argument order, XML escaping, stable labels).
