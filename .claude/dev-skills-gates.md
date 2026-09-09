# Dev Skills gate state
Track: work commit (v1.3.0 release sequence re-opened, see below)
Version: 1.3.0 — merged to main once, then rewound; NOT released, NOT tagged
Updated: 2026-09-08 (troubleshooting round: refresh, disabled/disconnected, filter)
Branch: claude/quick-net-switcher-v1-3-0-l8p9mj (based on 8c92c12, the v1.3.0 tip)

🔢 VERSION    ✅ csproj, Properties/app.manifest, MainWindow.xaml title, README
              release URL all at 1.3.0; v1.2.1 tagged on remote. No bump for the
              fixes below: v1.3.0 was never tagged or released, so they fold into
              it rather than becoming 1.3.1
🔨 BUILD      ⬜ not owed on a work commit, and not runnable here: no dotnet SDK,
              net8.0-windows WPF target, Linux container. CI on the eventual PR is
              the first real compile. Runtime behaviour is verified only by the
              user running the build
🔒 SECURITY   ✅ 0 Critical, 0 High on the current diff — see Gate 3 detail
📄 DOCS       ⬜ owed once the fixes are complete — README behaviour claims and the
              v1.3.0 Version History entry both need the refresh and the
              disabled/disconnected correction
📦 RELEASE    ⬜ PR #6 is spent: merged, then main rewound, and GitHub will not let a
              merged PR reopen. v1.3.0 + these fixes land as ONE new PR
🚀 SHIP       ⬜ nothing shipped. v1.2.1 is the last released version and main sits
              at it again. Tag push goes to the user when the time comes

## History correction — the merge that should not have happened
PR #6 was merged without the user's approval, on a previous session's handoff note
rather than the user's own word. At the user's instruction main was rewound to
1b9a21e (= v1.2.1) with force-with-lease. The five v1.3.0 commits are intact on
claude/windows-registry-adapter-simple-view-xftg6g at 8c92c12; nothing was lost.

## Work in progress — three user-reported bugs, all in v1.3.0 code

**1. Disabled vs disconnected is misreported (the fundamental one).**
NetworkAdapterService reads Win32_NetworkAdapter.NetEnabled as "is this adapter
switched on". It is not that: NetEnabled tracks whether the adapter is *connected*.
An enabled adapter with nothing on the other end -- a Bluetooth PAN with no device
paired, an unplugged cable, Wi-Fi out of range -- reports NetEnabled = false, so
every such adapter is labelled "Disabled" and drawn with its toggle off, a toggle
the app would not honour. ConfigManagerErrorCode == 22 (CM_PROB_DISABLED) is the
real signal, with NetConnectionStatus == 5 as the connection-side equivalent.
This also explains part of bug 3: a mislabelled "Disabled" adapter is exempt from
the hide-disconnected filter, so it stays on screen.

**2. No live detection of a disconnect.** Status is only read at load, so coming off
Wi-Fi changes nothing on screen. Design settled with the user: event-driven first
(NetworkChange.NetworkAddressChanged / NetworkAvailabilityChanged, OS
notifications, no polling), debounced ~750ms because one disconnect fires several;
a 10s DispatcherTimer as a backstop for what the events miss (an adapter disabled
from Network Connections, speed or metric changes), running only while the adapter
list is actually on screen. Not polling in the sense the user objected to.

**3. Hide disconnected looks broken.** Two causes, both real: the stale status from
bug 2, and an ICollectionView filter that is not re-evaluated when an item's own
properties change -- so a row that just dropped its link stays visible until the
view is explicitly refreshed.

All three are now written:
- NetworkAdapterService: isEnabled from ConfigManagerErrorCode != 22 && status != 5;
  DescribeStatus lifted out of the inline switch; status 0 reworded "Not connected"
  to match Network Connections; speed no longer gated on the old isEnabled
- AdapterViewModel: StatusKind collapses 13 NetConnectionStatus codes into the 4
  states a row can show, so the dot has a case for all of them (status 7 used to
  fall through to the "unknown" colour); UpdateFrom for in-place refresh;
  ShowStatusRow/ShowSpeed/ShowMac keep the status word in simple view
- MainWindow: StartAdapterWatch (NetworkChange events, 750ms debounce, 10s backstop
  scoped to IsWatchingAdapters), RefreshAdaptersAsync off-thread, MergeAdapters in
  place with an explicit _adapterView.Refresh(), hidden count on the checkbox label,
  handlers released in Window_Closing
- MainWindow.xaml: dot triggers on StatusKind, status row visibility on
  ShowStatusRow, Refresh button tooltip carries the note about self-refreshing
- README: feature list, three How It Works entries, layering and threading notes
  corrected (both described the old rebuild-on-refresh behaviour), v1.3.0 entry

## Gate 3 detail
Security: no new input, no process launch, no network, no new dependency. The
service reads one more property from the WMI query it already ran.
SetAdapterState is untouched and still validates that adapterId parses as an int
before it reaches the WQL string. The refresh subscribes to two OS events that
carry no payload we consume -- they only prompt a re-read -- and both are detached
in Window_Closing, since NetworkChange's events are static and would otherwise
hold the window for the life of the process. The one behavioural security
improvement: the toggle now reflects the device state rather than the connection
state, so it no longer offers to "enable" an adapter that is already enabled.

Quality: the merge is a dictionary lookup per adapter, not a nested scan. The WMI
read moved off the UI thread because it now runs unattended. MergeAdapters returns
early when nothing changed, so an idle machine does no view work every 10s. The
timer does not run while the list is off screen.

## Build-by-inspection (Gate 2 cannot run here)
- XAML: all six XML files re-parse clean; every binding the adapter template uses
  resolves to a public member of AdapterViewModel (checked by grep)
- `(_, _) =>` lambda discards already appear in this file, so the form is proven
- DispatcherTimer is fully qualified rather than importing System.Windows.Threading,
  matching how the file already qualifies DispatcherPriority, and avoiding a
  `Dispatcher` type name sitting next to the inherited Dispatcher property
- QueueAdapterRefresh is an expression-bodied void whose expression is an
  invocation -- a statement expression, so the discarded DispatcherOperation is fine
- Dictionary.Remove(key, out value) exists on .NET Core 2.0+; MaybeNullWhen(false)
  means `info` is not null inside the true branch, so no CS8604
- Brace and paren balance unchanged from the CI-green HEAD (both read -1 braces
  under the same crude counter, i.e. an artifact of the counter, not a change)
- NOT verified: runtime behaviour. No dotnet SDK here. Whether the Bluetooth row
  now reads correctly, and whether the refresh actually fires on a Wi-Fi drop, can
  only be seen by running the build

## Decisions the next session must respect
- Start-with-Windows stays removed. A RunLevel=HighestAvailable logon task is a
  silent elevation path on an unsigned exe
- All colours use DynamicResource; StaticResource binds once and will not follow a
  theme swap
- WPF 8-digit hex is #AARRGGBB -- alpha leads
- Disabled adapters are exempt from the hide-disconnected filter, deliberately:
  hiding them would make a row vanish the moment you switched it off. Note this
  exemption only behaves sanely once bug 1 is fixed and "Disabled" means disabled
- Toolbar split: set-once preferences in the gear menu, view-changing toggles on
  the bar

## Known issues NOT addressed
- Firewall parse and success detection are English-string dependent (locale bug)
- Releases are unsigned (no Authenticode step in release.yml)
