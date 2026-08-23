# AI Chat RTL Fixer 1.0.5

A patch release on top of 1.0.4. Everything 1.0.4 introduced is unchanged; this
fixes a version-reporting bug in that build and makes one message honest.

## The app reported the wrong version

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

## Store-installed apps get an honest answer

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

## Verification

137 automated tests pass, up from 130. Beyond the suite, this build was
installed and checked against a live chat session: it attached over the loopback
endpoint and injected without restarting anything, 57 blocks were re-aligned
with `dir=rtl` and `unicode-bidi: isolate`, no code element was touched, and the
shortcut setup was confirmed to write, stay idempotent on a second run, and
restore the original arguments exactly on removal.
