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
- Simple view — strip the adapter list down to connection names and their toggles, and hide the header and status bar
- Settings are persisted between sessions (minimize-to-tray, pin-to-desktop, simple-view)
- Custom app icon and Windows 11-inspired UI
- Status bar links to the project on GitHub and to the running version's release notes
- Runs as administrator (required to enable/disable adapters and change firewall/metric settings)
- Single-file self-contained executable — no .NET runtime install needed

## Screenshot

<img width="627" height="345" alt="image" src="https://github.com/user-attachments/assets/3a877358-cce4-485e-9aac-a2658d7e0ceb" />
<img width="608" height="345" alt="image" src="https://github.com/user-attachments/assets/4a75528e-6c74-428e-a010-45addb2b1135" />
<img width="614" height="414" alt="image" src="https://github.com/user-attachments/assets/ea87e862-0dd3-47fa-94d0-cc03aa8587ed" />




## Download

Download the latest release from the [Releases page](https://github.com/darthrater78/windows_quick_net_switcher/releases/latest).

## Requirements

- Windows 10 or Windows 11
- Administrator privileges (the app requests elevation on launch)

## Security Model

This app asks for administrator rights, so it should be able to explain exactly
what it does with them. This section describes the actual behaviour of the code,
including the parts that are worth knowing before you run it elevated.

### Why it needs administrator

Three operations are gated behind admin rights by Windows itself:

| Operation | Mechanism | Why elevation is required |
|---|---|---|
| Enable/disable an adapter | WMI `Win32_NetworkAdapter.Enable()` / `.Disable()` | Changing device state is an administrative operation |
| Change an interface metric | `netsh interface ipv4 set interface` | Modifies the system routing configuration |
| Toggle a firewall profile | `netsh advfirewall set <profile>profile state` | Modifies Windows Firewall policy |

Everything else the app does — listing adapters, reading the route table,
reading firewall state — is read-only.

### Scope of elevation

The manifest (`Properties/app.manifest`) requests `requireAdministrator` with
`uiAccess="false"`, which elevates **the entire process**. There is no split
broker/UI design: the WPF interface, the WMI calls, and the `netsh` calls all
run inside one elevated process. The practical consequence is that the trust
decision is about the binary as a whole, not about one privileged component.

Two things are deliberately kept *out* of that elevated scope:

- **Opening links.** Status-bar URLs are handed to `explorer.exe` rather than
  shell-executed. A direct `ShellExecute` from an elevated process would launch
  your default browser as administrator; delegating to the already-running
  user-level shell keeps the browser unelevated.
- **Stored settings.** Both JSON files live under `%LOCALAPPDATA%`, per-user, not
  in a machine-wide or world-writable location.

### Why there is no "start with Windows"

Versions 1.1.0 through 1.2.1 offered a start-with-Windows checkbox that wrote a
value under `HKCU\...\CurrentVersion\Run`. It never worked, and could not: the
shell launches Run entries **unelevated**, this app is manifested
`requireAdministrator`, and UAC has no interactive desktop to prompt on that
early in logon — so Windows discarded the entry every time, silently. The value
was created and the app never appeared.

The option was removed in v1.3.0 rather than reimplemented, because the
mechanism that *does* work — a scheduled task registered at
`RunLevel=HighestAvailable` with a logon trigger — is a silent elevation path.
Task Scheduler would start this process as administrator at every logon with no
prompt, so anyone able to overwrite the executable would get administrator on
the next logon. That is easy while the app lives in `Downloads` or any other
folder a standard user can write to, which is the normal case for a portable
single `.exe`, and there is no code signature to make the substitution visible.
Trading a permanent unprompted elevation path for a convenience toggle is not a
good deal, so the app does not auto-start at all. Launch it when you need it and
leave it in the tray.

On first run, v1.3.0 deletes the leftover `Run` value from earlier versions.

### Input handling at privileged boundaries

Every value that reaches a privileged call is constrained before it gets there:

| Privileged call | Where the input comes from | Guard |
|---|---|---|
| WMI `Enable`/`Disable` | Adapter `DeviceID`, originally read from WMI | `int.TryParse` must succeed before the value is interpolated into the WQL query, so a non-numeric value is rejected rather than embedded |
| `netsh interface ipv4 set interface` | The metric dialog — the only free-text input in the app | Validated twice, in the dialog and again in the service: integer, 1–9999. The interface alias comes from WMI, not from typed input |
| `netsh advfirewall set` | Firewall profile name | Limited to the three literals the parser produces (`Domain`, `Private`, `Public`); never free text |

All three `Process.Start` calls set `UseShellExecute = false`, so arguments are
passed directly to the target process — no shell is involved and no shell
metacharacters are interpreted.

The executables themselves are launched by **absolute path**, resolved once in
`SystemPaths.cs`, rather than by bare name. This matters because `CreateProcess`
searches the calling application's own directory before `System32`: a bare
`netsh` would run an attacker-planted `netsh.exe` sitting beside the app — with
administrator rights, since the process is elevated. Resolving from the Windows
directory, which only administrators can write to, removes that path.

### What the app does not do

- **No network I/O.** There is no HTTP client, socket, or listener anywhere in
  the codebase. No telemetry, no analytics, no crash reporting, no update check.
  The only outbound action is handing a `github.com` URL to `explorer.exe` when
  you click a status-bar link.
- **No credentials or secrets.** Nothing is authenticated and nothing is stored
  that could be one.
- **No machine-wide changes outside the three operations above.** No services,
  scheduled tasks, drivers, or `HKLM` writes.
- **No auto-start.** The app registers no logon task and no `Run` entry; it runs
  only when you launch it. See
  [Why there is no "start with Windows"](#why-there-is-no-start-with-windows).

### Local state

| File | Contents |
|---|---|
| `%LOCALAPPDATA%\QuickNetSwitcher\settings.json` | Three booleans: minimize-to-tray, pin-to-desktop, simple-view |
| `%LOCALAPPDATA%\QuickNetSwitcher\adapter_order.json` | A list of adapter ID strings used for display order |

Both are read with `System.Text.Json` into concrete types (`AppSettings` and
`List<string>`), with no polymorphic or type-name handling — a tampered file
cannot cause arbitrary types to be constructed. The values are also never
forwarded to a privileged call: adapter IDs from the order file are used solely
as sort keys for the list. Unreadable or corrupt files fall back to defaults.

### Known limitations and hardening notes

Stated plainly rather than left for you to discover:

- **Releases are not code-signed.** The published `.exe` carries no Authenticode
  signature, so SmartScreen will warn on first run. Download only from the
  [official releases page](https://github.com/darthrater78/windows_quick_net_switcher/releases),
  and treat a copy from anywhere else as untrusted.
- **Firewall status parsing is English-only.** Profile detection matches the
  literal strings `Domain Profile` / `Private Profile` / `Public Profile` and
  `State` in `netsh` output, and a failed toggle is detected by looking for the
  word `Error`. On a non-English Windows install the firewall tab may show no
  profiles, and a failure may be reported as success.
- **The pinned window is reparented into the shell.** "Pin to desktop" uses
  `SetParent` to place this elevated window underneath a window owned by the
  unelevated desktop shell. It is an unusual arrangement; turn the setting off
  if you would rather not have it.
- **Some failures are silent.** Settings writes swallow their exceptions, so a
  write that fails does so without surfacing an error.
- **Exception text is shown in the UI.** Error messages from WMI and `netsh` are
  written to the status bar verbatim, which can expose internal detail. For a
  local single-user utility this is informative rather than sensitive.

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

- **Adapters:** WMI (`Win32_NetworkAdapter`) discovers physical adapters and calls the `Enable()`/`Disable()` methods to toggle them. Adapter display order is persisted to a JSON file in `%LOCALAPPDATA%`. Interface metric is set via `netsh interface ipv4 set interface`.
- **Route Table:** WMI queries the system route table and resolves adapter indexes to friendly names for display.
- **Firewall:** Profile state is read and set with `netsh advfirewall set <profile>profile state on|off`.
- **Pin to desktop:** Uses Win32 interop to parent the window to the desktop's WorkerW layer, placing it behind all other windows.
- **Simple view:** A flag on each `AdapterViewModel` collapses the status, address and DNS rows of the adapter template, leaving the name and its toggle; the window's header and status bar are collapsed alongside them. The toolbar stays visible, since it carries the switch back out.
- **Settings:** All toolbar toggles are persisted to `%LOCALAPPDATA%/QuickNetSwitcher/settings.json`.
- **Status bar links:** URLs are passed to `explorer.exe` rather than shell-executed directly. Because the app runs elevated, a direct `ShellExecute` would launch the default browser as administrator; handing the URL to explorer delegates it to the user-level shell instead. The release notes URL is built from the assembly version, so it always points at the running build's own release.
- The UI is built with WPF and uses Windows Forms interop for the system tray icon. The app requests administrator elevation on launch since adapter, metric, and firewall changes all require it.

## Architecture

A single WPF project with no external dependencies beyond `System.Management`
(the WMI client). Roughly 1,800 lines of C# and XAML in total.

```
QuickNetSwitcher.sln
└── QuickNetSwitcher/
    ├── Properties/app.manifest     Elevation request (requireAdministrator)
    ├── App.xaml / App.xaml.cs      Application entry point
    ├── MainWindow.xaml(.cs)        The single window: three tabs, tray icon, all event handling
    ├── MetricDialog.xaml(.cs)      Modal dialog for editing an interface metric
    │
    ├── NetworkAdapterService.cs    WMI adapter discovery; enable/disable; metric via netsh
    ├── RouteTableService.cs        WMI route table query and interface-name resolution
    ├── FirewallService.cs          Firewall profile read/write via netsh advfirewall
    ├── LegacyStartupCleanup.cs     Removes the dead pre-1.3.0 Run key entry
    ├── DesktopPinService.cs        user32 interop for the desktop-layer pin
    ├── SettingsService.cs          settings.json load/save
    ├── AdapterOrderService.cs      adapter_order.json load/save, order application
    ├── SystemPaths.cs              Absolute paths to netsh.exe and explorer.exe
    │
    ├── AdapterViewModel.cs         Display model for an adapter row
    └── FirewallViewModel.cs        Display model for a firewall profile row
```

### Layering

The code splits into three layers with a deliberately simple shape:

- **Services** own every interaction with the operating system — WMI, `netsh`,
  the registry, the filesystem, and Win32 interop. They are `static` classes
  that return plain records, and they are the only place a privileged call is
  made. Keeping them separate is what makes the privileged surface small enough
  to audit in one sitting: the table in the [Security Model](#security-model)
  section is the complete list.
- **View models** (`AdapterViewModel`, `FirewallViewModel`) are plain display
  objects that map a service record to formatted strings (`IpDisplay`,
  `MetricDisplay`) and visibility flags (`HasIp`, `HasGateway`) for binding.
  They are not full MVVM — there is no commanding, and the list is rebuilt on
  refresh rather than updated in place. `AdapterViewModel` implements
  `INotifyPropertyChanged` for exactly one property, `SimpleView`, which has to
  change on an item that is already bound.
- **`MainWindow`** holds the UI and all event handlers, and is the only place
  that coordinates between services and the view. At ~550 lines it is by far the
  largest file in the project.

### Threading

The three state-changing operations — adapter toggle, firewall toggle, and
metric change — run on a background thread via `Task.Run` so a slow WMI or
`netsh` call cannot freeze the UI, with the result marshalled back to update the
status bar. The read paths (`LoadAdapters`, `LoadRoutes`, `LoadFirewall`) run
synchronously on the UI thread, so a slow WMI query can briefly hitch the window
on refresh.

### Notes on the current shape

- There are **no automated tests** and no test project. Services are `static`
  with no interfaces, so there is no seam to substitute a fake WMI or `netsh`
  at present. CI builds the project but does not test it.
- There is **no dependency injection or configuration layer**; services are
  called directly by name.
- The project targets `net8.0-windows` and publishes as a self-contained,
  single-file `win-x64` binary, which is why the release is large (see
  [Download Size](#download-size)).

## Version History

### v1.3.0 — 2026-09-08
- Removed "Start with Windows". It never worked — the shell launches `HKCU\...\Run` entries unelevated, and this app is manifested `requireAdministrator`, so Windows discarded the entry at every logon without an error. The registry value was written and the app never started
- It was removed rather than fixed: the mechanism that works is a scheduled task at `RunLevel=HighestAvailable`, which would start this process as administrator at every logon with no UAC prompt. Anyone able to overwrite the unsigned executable — trivial while it sits in `Downloads` — would get administrator on the next logon. See [Why there is no "start with Windows"](#why-there-is-no-start-with-windows)
- The leftover `Run` value from v1.1.0–v1.2.1 is deleted on first run of this version
- New "Simple view" toggle: collapses each adapter row to its connection name and toggle, and hides the header and status bar. The setting is remembered between sessions
- Toolbar checkboxes now reflow instead of clipping when the window is narrow

### v1.2.1 — 2026-09-08
- Security: `netsh` and `explorer.exe` are now launched by absolute path instead of by bare name. Windows searches the application's own directory before `System32`, so a `netsh.exe` planted beside the app would previously have been run with administrator rights — a privilege escalation path for anything already running as the user. Paths are resolved once in the new `SystemPaths` helper
- Documentation: new Security Model section covering why the app needs administrator rights, the scope of elevation, how input is constrained at each privileged boundary, local state, and known limitations
- Documentation: new Architecture section covering project layout, layering, threading, and current gaps
- Corrected the How It Works description of the metric change: it uses `netsh interface ipv4`, not `ipv4/ipv6`

### v1.2.0 — 2026-09-08
- Status bar links to the project's GitHub page and to the running version's release notes
- Release notes link is derived from the assembly version, so it always points at the build's own release
- Links open unelevated — the app runs as administrator, so URLs are handed to the user-level shell rather than launching the browser with admin rights
- App icon rebuilt at 16/24/32/48/64/128/256 px; it previously shipped a single 16x16 frame that Windows upscaled, making the desktop, taskbar and Start menu icon look blurry

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

https://github.com/darthrater78/windows_quick_net_switcher/releases/tag/v1.3.0
