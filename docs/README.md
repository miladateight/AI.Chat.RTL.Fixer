# AI Chat RTL Fixer documentation

This folder contains release, security, test-plan and contribution documentation for AI Chat RTL Fixer 1.1.0.

- [Product overview and downloads](../README.md)
- [Release and packaging](RELEASE.md)
- [Security and privacy](SECURITY.md)
- [Test plan](TESTPLAN.md)
- [Contributing app profiles](CONTRIBUTING.md)

The app is a Windows tray tool for graphical desktop AI chats. It modifies only the target chat surface at runtime. Electron support uses local loopback CDP; other UI technologies need dedicated adapters and real-install verification.

The updater checks the public GitHub Releases endpoint only when the user leaves the setting enabled. It does not automatically download or install releases.
