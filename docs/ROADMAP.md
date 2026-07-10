# Roadmap

## v0.1 (current)

- Core: shared rule engine (canonical JS, tested via Jint), direction logic, technical-text protection, clipboard modes, font handling, restore, privacy-safe logging, profile system, tray app.
- Electron CDP adapter (loopback-only, random port, verified bind, user-consented relaunch, no infinite retry).
- At least one Stable profile after real verification; others Planned/Unsupported (no false support claims).
- Copy behavior (Original / RTL-readable / RTL-readable without markers).
- Vazirmatn bundled font (OFL) applied to chat surface only.
- Runtime-only modifications with full restore.
- 53 automated tests (unit + Playwright integration).
- Two publish profiles (framework-dependent + self-contained win-x64).

**The core built in v0.1 is designed NOT to require a rewrite in v0.2.**

## v0.2

- More app profiles (after real verification).
- Bugfix and performance tuning.
- Selector improvements for updated app versions.
- Better copy modes and streaming support.

## v0.3

- WebView2 adapter — **only after real verification** against a WebView2 app.
- Tauri/Qt support if feasible and tested.
- Profile import/export.
- User-created profiles.
- Advanced detection.

## v1.0

- Stable multi-app support.
- Clean installer.
- Optional auto-update.
- Optional signed release.
- Full documentation.

> Adapter implementations for untested UI technologies (WebView2, Tauri, Qt, WPF, native) are deliberately deferred until they can be verified against real apps. No support is claimed for untested adapters.