# Microsoft Store listing

Everything Partner Center asks for, written out so the submission is copy-and-paste rather than composed in a browser form at midnight. Screenshots are the one thing that cannot be prepared here — see the end.

The listing is English only. The app's interface is not: it ships Persian, Arabic, Hebrew, Urdu and English, and the package declares all five. That is a deliberate split — the package tells the truth about what the app speaks, while the store description stays in one language until there is a reason to translate it.

---

## Product name

```text
AI RTL Fixer
```

## Category

**Productivity** · subcategory *Personal assistant* if prompted, otherwise leave the default.

## Short description

Used for the search result and the top of the product page.

```text
Persian, Arabic, Hebrew and Urdu text comes out backwards in desktop AI chat apps. AI RTL Fixer sits in the tray and fixes it, while code, commands, paths and English stay left to right.
```

## Description

The first paragraph discloses the dependency, because store policy requires a dependency on other software to be stated at the start of the description rather than discovered after install.

```text
AI RTL Fixer works alongside a desktop AI chat application you already have installed. It does not include one, and it does nothing on its own.

Type Persian, Arabic, Hebrew or Urdu into a desktop AI chat app and the reply often arrives visually broken: paragraphs aligned to the wrong side, punctuation flung to the wrong end of the sentence, a line that reads correctly only if you already know what it was meant to say. The text is fine. The rendering is not.

AI RTL Fixer sits in the notification area and corrects the rendering in the app you are already using. Right-to-left paragraphs get right-to-left direction, alignment and a font that was drawn for them. Code blocks, commands, file paths, URLs and English text stay exactly as they were — left to right, and safe to copy.

WHAT IT DOES

- Fixes paragraph direction and alignment for Persian, Arabic, Hebrew and Urdu
- Leaves code, commands, paths, URLs and English text untouched
- Copies out cleanly: choose whether copied text carries invisible bidi markers, or none at all
- Per-application control — enable it only where you want it
- An interface in Persian, Arabic, Hebrew, Urdu and English, mirrored for right-to-left languages

WHAT IT DOES NOT DO

There is no account, no server and no analytics. Your conversations are never read, stored or transmitted; the app changes how text is displayed on your own screen and nothing else. It talks only to the chat application on your own machine, over a local connection that cannot leave it.

The full privacy policy, and the source code, are linked below. This is a free and open-source project under the MIT licence.
```

## Key features

Partner Center takes these as separate lines.

```text
Fixes right-to-left rendering in desktop AI chat apps
Persian, Arabic, Hebrew and Urdu
Code, commands, paths and English stay left to right
Copy out with or without invisible bidi markers
Per-application control from the tray
Interface in five languages, mirrored for RTL
No account, no server, no analytics
Free and open source, MIT licensed
```

## Search terms

Seven at most, 45 characters each. These are invisible to users and exist only for search, so they carry the words a listing written in English cannot.

```text
RTL
راست به چپ
عربي
فارسی
bidi
right to left
Persian Arabic Hebrew Urdu
```

## System requirements

```text
Windows 10 version 1809 (build 17763) or later, 64-bit.
A supported desktop AI chat application.
```

## URLs

| Field | Value |
|---|---|
| Privacy policy | `https://github.com/miladateight/AI.RTL.Fixer/blob/main/docs/PRIVACY.md` |
| Support contact | `https://github.com/miladateight/AI.RTL.Fixer/issues` |
| Website | `https://ateight.xyz/AI-Chat-RTL-Fixer/` |

## Age rating

The IARC questionnaire should come out at the lowest rating. The answers that matter: no violence, no controlled substances, no gambling, no in-app purchases, no user-to-user communication, no sharing of location or personal information, no user-generated content. The app collects nothing.

## Notes for certification

Paste this into the submission's notes. A reviewer sees a restricted capability on a tool that modifies another application's window; if you do not explain it, they will guess.

```text
This is a packaged Win32 desktop app (runFullTrust). It is an accessibility and
readability tool for right-to-left languages.

How it works: the app attaches to a supported Electron-based AI chat application
through that application's Chrome DevTools Protocol endpoint on local loopback
(127.0.0.1) and injects a script that changes text direction, alignment and font
in that app's own window. A discovered endpoint whose host is not loopback is
rejected. The app opens no network listener and connects to nothing remote.

Consent: relaunching a target application with its debugging endpoint enabled
requires explicit user consent, with a warning about unsaved work. Nothing
happens to an application the user has not enabled.

Data: conversation content is restyled in place. It is not read out, stored or
transmitted. There is no telemetry, no account and no analytics. The only
network request the app can make is an optional update check against the public
GitHub Releases API, and that is disabled in this packaged build because the
Store handles updates.

Dependency: the app requires a supported desktop AI chat application, which it
does not install. This is disclosed in the first line of the description.

Source code: https://github.com/miladateight/AI.RTL.Fixer (MIT)
```

## Screenshots

Not preparable here — these have to come from the app running on a real desktop. At least one is required; four or more makes the listing look considered.

The most convincing one is before and after: the same reply in a chat window rendered backwards, then rendered correctly, with a code block still left to right in both. That single image explains the product faster than the description does.

Worth capturing:

1. A broken right-to-left reply, before.
2. The same reply, after.
3. The Settings window in Persian, showing the mirrored interface.
4. The tray menu with per-application toggles.
