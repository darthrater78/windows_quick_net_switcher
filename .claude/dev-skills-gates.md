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
🔒 SECURITY   ✅ 0 Critical, 0 High on the current diff
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

Done so far (this commit): AdapterViewModel.UpdateFrom for in-place refresh, and
ShowStatusRow/ShowSpeed/ShowMac so the status word survives simple view on any row
that is not connected.

Still to write: the NetworkAdapterService fix, StatusKind for the dot colour (13
NetConnectionStatus codes collapse to 4 display states; today status 7 falls
through to the "unknown" colour), the refresh loop and merge in MainWindow, the
hidden-count feedback on the checkbox label (the status bar carrying it is hidden
in simple view), and the docs.

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
