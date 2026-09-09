# Dev Skills gate state
Track: work commit (release-notes fix). The v1.3.0 release sequence is COMPLETE.
Version: 1.3.0 — shipped 2026-09-09
Updated: 2026-09-09
Branch: claude/quick-net-switcher-v1-3-0-l8p9mj (restarted from origin/main 75dd75d)

## v1.3.0 — all six gates closed, verified from evidence not memory

🔢 VERSION    ✅ csproj 1.3.0, Properties/app.manifest 1.3.0.0, window title v1.3.0,
              README release link /tag/v1.3.0 — all checked on merged main
🔨 BUILD      ✅ build.yml/windows-latest green on the PR tip 723f8e5 (run
              34294662405) and on merged main 75dd75d (run 34294883279).
              `git diff 723f8e5 origin/main` was empty, so the merge introduced
              nothing CI had not already compiled. RUNTIME also verified: the user
              ran the build and confirmed the behaviour before tagging
🔒 SECURITY   ✅ 0 Critical, 0 High across v1.2.1→v1.3.0. Net reduction in attack
              surface: an autostart mechanism removed, a DLL-hijack vector closed,
              nothing added
📄 DOCS       ✅ v1.3.0 Version History entry, "Why there is no start with Windows",
              the DLL search-order paragraph in Security Model, and the layering and
              threading notes (both had described the old rebuild-on-refresh shape)
📦 RELEASE    ✅ PR #7, opened and merged by the user
🚀 SHIP       ✅ tag v1.3.0 → 75dd75d (same commit as main), release published
              2026-09-09 00:30:03, QuickNetSwitcher-v1.3.0.exe attached by
              release.yml, 162MB, sha256 9d197c10ce8ead39629f98fd08ca75b6db8a19087
              dd9d854b862677bd57f8040. Tag pushed by the user; confirmed here with
              `git ls-remote --tags origin v1.3.0` rather than assumed

## Current work commit — release notes came out empty

The release page showed one line, the merge commit title, because release.yml set
generate_release_notes: true with no body: that option only summarises merged pull
requests. The README's changelog never reached the release.

release.yml now lifts the "### vX.Y.Z" block out of the README's Version History
into body_path, keeps generate_release_notes on so GitHub still appends What's
Changed and the compare link, rewrites README-relative anchors to absolute links at
the tag (they resolve against the release URL otherwise, and would be dead), appends
a Download note about the unsigned single exe, and FAILS the release when the tag has
no changelog entry — DOCS enforced mechanically rather than by habit.

🔒 SECURITY   ✅ CI workflow only. No new action, no new permission (contents: write
              was already there), body content comes from in-repo README
🔨 BUILD      ➖ N/A — build.yml is unchanged. The extraction was tested locally
              (bash/awk): v1.3.0 yields 17 bullets with no bleed into v1.2.1, v1.2.1
              yields 4, v1.3.01 and v9.9.9 yield nothing so the job fails, the anchor
              rewrite lands, YAML parses at 8 steps. NOT tested on a real
              windows-latest runner; that waits for the next tag
📄 DOCS       ➖ N/A — changes how docs reach the release page, not the docs

## Dropped, on the user's call: Wi-Fi SSID and encryption
A WlanService reading the associated network's SSID and cipher through the Native
Wifi API (wlanapi.dll P/Invoke — no process launch, no netsh text parsing) was
written and then dropped at the user's request; not because of a defect. If it is
ever revisited, the findings worth keeping: WlanQueryInterface's reported data size
must be checked before PtrToStructure reads the managed struct width out of a buffer
Windows owns; an SSID is attacker-controlled display text and needs control and
Unicode-format characters stripped; WlanGetProfile must never be called, since it
hands a saved passphrase in plaintext to an elevated caller.

The DLL search-path hardening that came out of that work was kept and shipped in
v1.3.0 on its own.

## History note
PR #6 was merged without the user's approval, on a previous session's handoff note
rather than the user's own word. At the user's instruction main was rewound to
1b9a21e with force-with-lease; nothing was lost, and v1.3.0 landed later through
PR #7. PR #6 remains "Merged" on GitHub with its commits no longer on main.

## Decisions that stand
- Start-with-Windows stays removed. A RunLevel=HighestAvailable logon task is a
  silent elevation path on an unsigned exe
- Disabled adapters are exempt from the hide-disconnected filter, deliberately.
  This only behaves sanely because "Disabled" now means administratively disabled
  (ConfigManagerErrorCode 22) rather than "nothing connected"
- The adapter list is a card list, not a table: few rows, ~11 fields of uneven
  width, and a DataGrid would fight the toggle and the drag-reorder. The route table
  is the opposite shape and stays a table
- Live status is event-driven (NetworkChange) with a 10s backstop scoped to when the
  list is on screen — not a poll
- All colours use DynamicResource; StaticResource binds once and will not follow a
  theme swap
- WPF 8-digit hex is #AARRGGBB — alpha leads
- Every DllImport outside Windows' KnownDLLs list is pinned to System32
- Toolbar split: set-once preferences in the gear menu, view-changing toggles on the
  bar

## Known issues NOT addressed
- Firewall parse and success detection are English-string dependent (locale bug)
- Releases are unsigned (no Authenticode step in release.yml)
