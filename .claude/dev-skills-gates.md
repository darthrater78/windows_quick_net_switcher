# Dev Skills gate state
Track: release sequence
Version: 1.3.0
Updated: 2026-09-08 (theme + sizing work on top of green CI)
Branch: claude/windows-registry-adapter-simple-view-xftg6g

🔢 VERSION    ✅ csproj, app.manifest, MainWindow.xaml title, README release URL
              all at 1.3.0; v1.2.1 tagged on remote (previous release shipped);
              RepositoryUrl present; in-app release-notes link derives from the
              assembly version, so it follows automatically
🔨 BUILD      ⏳ was ✅ on f8ac987; theme + sizing changes since then are NOT
              compiled. Re-verify on CI before this is ✅ again.
              Prior pass: build.yml (windows-latest) succeeded on f8ac987 in 74s,
              run 34278563923. This was the first ever compile of the branch:
              no dotnet SDK in the session container (Linux, net8.0-windows WPF
              target), so the pre-push pass was by inspection only (notes below).
              Release artifact is still built by CI at tag time, not locally.
🔒 SECURITY   ✅ 0 Critical, 0 High — net reduction in attack surface
📄 DOCS       ✅ v1.3.0 changelog entry; new "Why there is no start with Windows"
              section; feature list, How It Works, local-state table, known
              limitations, architecture tree and layering notes all corrected
📦 RELEASE    ⏳ PR #6 open — https://github.com/darthrater78/windows_quick_net_switcher/pull/6
              commits 240229e + f8ac987; session subscribed to PR activity
🚀 SHIP       ⬜ PR #6 is green and mergeable_state=clean. Remaining: merge,
              then tag v1.3.0 to fire release.yml. Tag push goes to the user (container
              creds are commonly denied on refs/tags/*)

## What changed

**1. "Start with Windows" removed (was broken, and unfixable safely).**
The v1.1.0–v1.2.1 implementation wrote `HKCU\...\CurrentVersion\Run`. That can
never work here: the shell launches Run entries unelevated, the app is manifested
`requireAdministrator`, and UAC has no interactive desktop to prompt on during
logon — Windows discards the entry silently. Hence "the registry entry does get
created" and nothing starts.

The working mechanism is a scheduled task at `RunLevel=HighestAvailable` with a
logon trigger. That was implemented, reviewed, and then dropped on the user's
call: it is a permanent silent elevation path. Task Scheduler would launch this
process as administrator at every logon with no prompt, so anyone who can
overwrite the exe — trivial while it sits in a non-admin-writable folder such as
`Downloads`, which is the normal case for a portable single `.exe` — gets
administrator on the next logon. No code signature makes the swap visible.

- QuickNetSwitcher/StartupService.cs        (deleted)
- QuickNetSwitcher/LegacyStartupCleanup.cs  (new) removes the dead Run value
- MainWindow.xaml                           checkbox removed
- MainWindow.xaml.cs                        handler removed, cleanup called at start
- SettingsService.cs                        StartWithWindows key removed

**2. Simple view.**
Collapses each adapter row to the connection name and its toggle, and hides the
header, the status bar and the minimize-to-tray and pin-to-desktop checkboxes --
leaving the simple-view checkbox and Refresh. The checkbox stays because it is
the only way back out. Persisted in settings.json.

Hiding a checkbox does not change its IsChecked state, so minimize-to-tray and
pin-to-desktop keep working: SaveSettings, Window_StateChanged and
Window_Closing all read them while collapsed.

- AdapterViewModel.cs      SimpleView + ShowDetails/ShowIpRow/ShowDnsRow, INPC
- MainWindow.xaml          three detail rows bound to the new flags; toggle
                           re-centres via DataTrigger; toolbar is a WrapPanel
- MainWindow.xaml.cs       ApplySimpleView, SimpleView_Click
- SettingsService.cs       SimpleView key

**3. Dark theme + simple-view sizing (second round, user-requested).**
Palette moved out of App.xaml into Themes/Light.xaml + Themes/Dark.xaml with an
identical key set; ThemeService swaps slot 0 of the app's merged dictionaries and
every colour reference became DynamicResource (a StaticResource binds once and
would not follow the swap). Default follows AppsUseLightTheme; the toggle stores
an explicit choice. WPF's stock TabItem/CheckBox/Button/ScrollBar chrome is
painted light and cannot be recoloured through properties, so those are templated.
Title bar via DwmSetWindowAttribute (OS-drawn, not WPF); tray menu coloured
directly (Windows Forms, outside WPF resources).

Simple view now sets SizeToContent=Height so the window fits the list instead of
holding 580px -- that was the dead band under the last adapter. Scoped to the
Adapters tab: the route table would measure every row and snap to screen height.

- Themes/Light.xaml, Themes/Dark.xaml   (new)
- ThemeService.cs                       (new)
- App.xaml                              palette extracted, control templates added
- MainWindow.xaml / .cs                 DynamicResource, Dark toggle, UpdateWindowSizing
- MetricDialog.xaml                     DynamicResource
- SettingsService.cs                    bool? DarkMode (null = follow Windows)

## Gate 3 detail

Security: the change removes an attack path and adds none. The one new code
path is a single `DeleteValue` on a per-user `HKCU` key, on a value this app
itself created. It takes no input, launches no process, opens no network, and
adds no dependency. Simple view is pure presentation; a tampered `SimpleView`
value in settings.json can only hide or show UI. Removing the feature also
strengthens an existing README claim — the app now registers no autostart entry
of any kind.

Known trade-off, accepted: with the status bar hidden, a failed adapter toggle
has no text to report to. The failure is still visible — the toggle snaps back,
which `ToggleAdapter_Click` already does on both the failure and exception paths.

Round 2 security: ThemeService reads one HKCU registry value and calls
DwmSetWindowAttribute on our own window handle. No input, no process launch, no
network, no new dependency. Theme choice cannot affect privileged behaviour.

Quality: no deep nesting, no function over ~15 lines, no new allocation in a hot
path. `ApplySimpleView` is O(adapters) and runs on user action only.
`AdapterViewModel` gains `INotifyPropertyChanged` for exactly one property — the
README's layering section previously claimed the view models had none, and was
corrected rather than left stale.

## Build-by-inspection (since Gate 2 cannot run)

- Caught a missing `using System.Linq` in the scheduled-task draft (`Describe`
  used `Select`/`FirstOrDefault`); that file was subsequently deleted anyway
- `AdapterViewModel.cs` had no `#nullable enable`; added before introducing
  `PropertyChangedEventHandler?`, which would otherwise warn CS8632
- All five XAML/XML files re-parsed clean after editing
- `Task.Run(LegacyStartupCleanup.RemoveRunEntry)` binds the `Action` overload;
  `System.Threading.Tasks` was already imported
- Every `x:Name` the code-behind touches (`HeaderPanel`, `StatusBar`,
  `SimpleViewCheckBox`) exists in the XAML, and every `Click=` handler still
  resolves — grep confirms no `StartWithWindows` reference survives

## Known issues NOT addressed in this release
Documented in README "Known limitations and hardening notes", not fixed here:
- Firewall parse and success detection are English-string dependent (locale bug)
- Releases are unsigned (no Authenticode step in release.yml)
