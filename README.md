# Quick Net Switcher

A lightweight Windows 11 utility to quickly toggle network adapters on and off from the system tray.

![.NET 8](https://img.shields.io/badge/.NET-8.0-blue)
![Windows](https://img.shields.io/badge/platform-Windows%2011-blue)
![License](https://img.shields.io/badge/license-MIT-green)

## Features

**Adapters tab**
- View all physical network adapters with real-time status, speed, MAC address, IP/CIDR, gateway, DNS suffix, and interface metric
- Toggle adapters on/off with a single click
- Drag-to-reorder the adapter list — order is remembered between launches
- Edit an adapter's interface metric (1–9999) via a dialog

**Route Table tab**
- View the system route table with friendly adapter names
- Filter by route type

**Firewall tab**
- Toggle Windows Firewall on/off per profile (Domain, Private, Public)

**General**
- System tray icon — minimize to tray and keep it running in the background
- Pin to desktop — keep the window on the desktop layer behind other apps, like a widget (on by default)
- Start with Windows — optional auto-launch via registry Run key
- Settings are persisted between sessions (minimize-to-tray, pin-to-desktop, start-with-Windows)
- Custom app icon and Windows 11-inspired UI
- Runs as administrator (required to enable/disable adapters and change firewall/metric settings)
- Single-file self-contained executable — no .NET runtime install needed

## Screenshot

<img width="615" height="342" alt="image" src="https://github.com/user-attachments/assets/8e77fe59-fff6-41b2-a38e-7815014a7144" />
<img width="608" height="345" alt="image" src="https://github.com/user-attachments/assets/4a75528e-6c74-428e-a010-45addb2b1135" />
<img width="614" height="414" alt="image" src="https://github.com/user-attachments/assets/ea87e862-0dd3-47fa-94d0-cc03aa8587ed" />




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

- **Adapters:** WMI (`Win32_NetworkAdapter`) discovers physical adapters and calls the `Enable()`/`Disable()` methods to toggle them. Adapter display order is persisted to a JSON file in `%LOCALAPPDATA%`. Interface metric is set via `netsh interface ipv4/ipv6 set interface`.
- **Route Table:** WMI queries the system route table and resolves adapter indexes to friendly names for display.
- **Firewall:** Profile state is read and set with `netsh advfirewall set <profile>profile state on|off`.
- **Pin to desktop:** Uses Win32 interop to parent the window to the desktop's WorkerW layer, placing it behind all other windows.
- **Start with Windows:** Manages an `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` registry entry pointing to the app executable.
- **Settings:** All toolbar toggles are persisted to `%LOCALAPPDATA%/QuickNetSwitcher/settings.json`.
- The UI is built with WPF and uses Windows Forms interop for the system tray icon. The app requests administrator elevation on launch since adapter, metric, and firewall changes all require it.

## Version History

### v1.1.0 — 2026-09-08
- Pin to desktop — window sits on the desktop layer behind other apps (default on)
- Start with Windows — optional auto-launch via HKCU Run registry key
- Settings persistence — minimize-to-tray, pin-to-desktop, and start-with-Windows saved to JSON
- All toolbar settings remembered between sessions

### v1.0.0 — 2026-09-06
- Initial release
- List all physical network adapters with status, speed, MAC, IP/CIDR, gateway, DNS suffix, and metric
- Toggle adapters on/off
- Drag-to-reorder adapter list with persisted order
- Edit interface metric via dialog
- Route table viewer with type filtering and friendly adapter names
- Windows Firewall profile toggle (Domain/Private/Public)
- System tray icon with minimize-to-tray
- Custom app icon and Windows 11-inspired UI styling
- Single-file self-contained publish support
- CI/CD with GitHub Actions (build on PR, release on tag)

## License

MIT

## Repository

https://github.com/darthrater78/windows_quick_net_switcher

## Release Notes

https://github.com/darthrater78/windows_quick_net_switcher/releases/tag/v1.1.0
