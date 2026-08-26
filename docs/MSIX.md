# MSIX packaging for the Microsoft Store

The Store build is the same application as the installer, wrapped in an MSIX package. It is a **packaged desktop app**, not a UWP app: it keeps its own process, its Win32 APIs and its loopback connection to the target chat application. `runFullTrust` is what says so.

Microsoft signs the package during certification, which is why the Store build needs no code-signing certificate of its own. The installer published on GitHub is a separate artifact with its own signing story — see [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md).

## Building

```powershell
# Test package for this machine: self-signed, installable, not for distribution
powershell -ExecutionPolicy Bypass -File scripts\package-msix.ps1 -SelfSign

# Store package: unsigned, identity from Partner Center
powershell -ExecutionPolicy Bypass -File scripts\package-msix.ps1 `
    -IdentityName        "<Package/Identity/Name>" `
    -IdentityPublisher   "<Package/Identity/Publisher>" `
    -PublisherDisplayName "<Publisher display name>"
```

Output lands in `dist\msix\`. Add `-SkipPublish` to reuse an existing self-contained build instead of republishing.

The version comes from `Directory.Build.props`, like every other artifact, with a fourth part appended: `1.1.2` becomes `1.1.2.0`. The Store requires that last part to be zero.

### The three values only Partner Center can give you

Reserve the app name in Partner Center, then open **Product management → Product identity**. It shows exactly three things this build needs:

| Manifest field | Partner Center label |
|---|---|
| `Identity/@Name` | Package/Identity/Name |
| `Identity/@Publisher` | Package/Identity/Publisher |
| `Properties/PublisherDisplayName` | Publisher display name |

A package whose identity does not match the reservation is rejected at upload, so `package-msix.ps1` refuses to build a Store package while the placeholders are still in place. That refusal is the point: finding out at upload time is slow.

### Installing the test package

The self-signed certificate has to be trusted once, per machine. It is a test artifact and must never be shipped:

```powershell
Import-PfxCertificate -FilePath dist\msix\ateight-test-signing.pfx `
    -CertStoreLocation Cert:\LocalMachine\TrustedPeople `
    -Password (ConvertTo-SecureString 'test' -AsPlainText -Force)
Add-AppxPackage dist\msix\AIRTLFixer-1.1.2.0-test.msix
```

## What behaves differently inside the package

Two things cannot work the same way in a package, and the app detects which build it is (`PackageContext.IsPackaged`) rather than shipping two code paths.

**Start with Windows.** A packaged app's writes to the `Run` registry key land in a virtualised copy that the Windows startup scan never reads. The setting would report success and do nothing. The package declares a `windows.startupTask` instead, so the entry appears under **Settings → Apps → Startup** like every other packaged app, and the in-app checkbox is not shown.

*Open question:* wiring the in-app checkbox back up means calling the `StartupTask` WinRT API, which means moving the tray project from `net8.0-windows` to a Windows-10-versioned target framework. That pulls the SDK projection into every build, including the installer's, for one checkbox. Not done yet, deliberately.

**Update checking.** The Store keeps its copy up to date, and a Store app that sends users elsewhere to fetch an installer is both confusing and against store policy. The packaged build does not offer the update check at all.

## Known gaps

- **Settings do not carry over from an installer-based install.** Under MSIX, writes to `%AppData%` are redirected into the package's own container, so a user who switches from the installer to the Store version starts with defaults: enabled apps, font, copy mode, language. A one-time import on first packaged run would fix it and has not been written.
- **Both builds can be installed at once.** They share the `Local\AIChatRTLFixer` mutex, so only one runs and the second exits quietly — no duplicate tray icons, but also no explanation to the user. Worth handling before the Store listing goes live.
- **The packaged build has not been run end to end.** The package builds, signs and passes `makeappx` validation; attaching to a real chat application from inside the container, relaunching a target app, and the clipboard path all still need to be exercised on a machine with the package installed.

## Certification notes

Store review sees a restricted capability (`runFullTrust`) on a tool that modifies another application's window, which deserves a straight explanation in the submission's notes rather than a reviewer guessing. The essentials:

- The app attaches to supported Electron chat applications through their Chrome DevTools Protocol endpoint on `127.0.0.1` only, and rejects any endpoint that is not loopback.
- It injects a script that changes text direction, alignment and font. It does not read, store or transmit conversation content.
- Relaunching a target application with debugging enabled requires explicit user consent, with a warning about unsaved work.
- It depends on the user already having a supported chat application installed. That dependency must be disclosed at the start of the listing description, per store policy.
