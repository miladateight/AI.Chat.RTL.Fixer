# AI Chat RTL Fixer

AI Chat RTL Fixer is a free and open-source Windows tray tool that improves RTL text rendering inside AI desktop chat applications. It focuses only on the chat area and keeps code, commands, paths and English text left-to-right.

See [docs/README.md](docs/README.md) for the full documentation.

## Quick links

- [Supported apps & status](docs/README.md#supported-apps)
- [CDP and security](docs/SECURITY.md)
- [Privacy](docs/README.md#privacy)
- [How to enable/disable/restore](docs/README.md#how-to-enable--disable)
- [Known limitations](docs/README.md#known-limitations-v01)
- [Roadmap](docs/ROADMAP.md)
- [Contributing](docs/CONTRIBUTING.md)
- [Test plan](docs/TESTPLAN.md)
- [License](docs/LICENSE)

## Build

```
dotnet restore
dotnet build
dotnet test
dotnet publish src/AI.ChatRTLFixer.Tray -p:PublishProfile=framework-dependent
dotnet publish src/AI.ChatRTLFixer.Tray -p:PublishProfile=self-contained-win-x64
```

GitHub: https://github.com/placeholder/ai-chat-rtl-fixer