# Quick Net Switcher

A lightweight Windows 11 utility to quickly toggle network adapters on and off from the system tray.

![.NET 8](https://img.shields.io/badge/.NET-8.0-blue)
![Windows](https://img.shields.io/badge/platform-Windows%2011-blue)
![License](https://img.shields.io/badge/license-MIT-green)

## Features

- View all physical network adapters with real-time status
- Toggle adapters on/off with a single click
- System tray icon — minimize to tray and keep it running in the background
- Shows connection speed, adapter type, and MAC address
- Clean Windows 11-inspired UI
- Runs as administrator (required to enable/disable adapters)
- Single-file self-contained executable — no .NET runtime install needed

## Screenshot

> *Coming soon — build and run the app to see it in action.*

## Download

Download the latest release from the [Releases page](https://github.com/darthrater78/windows_quick_net_switcher/releases/latest).

## Requirements

- Windows 10 or Windows 11
- Administrator privileges (the app requests elevation on launch)

## Download Size

The release exe is ~60–150 MB because it is published as a **self-contained
single-file** binary — the entire .NET 8 runtime is bundled so you don't need
to install .NET on the target machine. No installer required: just download,
right-click, and run as administrator.

## Building from Source

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows (WPF requires Windows)

### Build

```powershell
cd windows_quick_net_switcher
dotnet build
```

### Run

```powershell
dotnet run --project QuickNetSwitcher
```

### Publish Single-File Executable

```powershell
dotnet publish QuickNetSwitcher/QuickNetSwitcher.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

The output will be a single `QuickNetSwitcher.exe` in the `publish/` folder.

## How It Works

The app uses WMI (`Win32_NetworkAdapter`) to discover and control physical network adapters. It calls the `Enable()` and `Disable()` WMI methods, which require administrator privileges. The UI is built with WPF and uses Windows Forms interop for the system tray icon.

## Version History

### v1.0.0 — 2026-09-06
- Initial release
- List all physical network adapters with status
- Toggle adapters on/off
- System tray icon with minimize-to-tray
- Windows 11-inspired UI styling
- Single-file self-contained publish support
- CI/CD with GitHub Actions (build on PR, release on tag)

## License

MIT

## Repository

https://github.com/darthrater78/windows_quick_net_switcher

## Release Notes

https://github.com/darthrater78/windows_quick_net_switcher/releases/tag/v1.0.0
