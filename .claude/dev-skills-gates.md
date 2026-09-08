# Dev Skills gate state
Track: work commit
Version: 1.2.0 (unchanged — docs-only change, no bump)
Updated: 2026-09-08
Branch: claude/dev-skills-loading-m8hs6o (from main @ 89e320e, post-v1.2.0 ship)

🔢 VERSION    ⬜ not owed — work commit, no version bump
🔨 BUILD      ⬜ not owed — work commit; no source code changed (README only)
🔒 SECURITY   ✅ 0 Critical, 0 High — see scope below
📄 DOCS       ⬜ not owed — work commit (this change IS docs; no changelog entry
              since no version bump)
📦 RELEASE    ⬜ not owed — work commit
🚀 SHIP       ⬜ not owed — work commit

## Gate 3 scope
Changed content is Markdown only; no executable code was modified. Reviewed for
disclosure risk: no secrets, credentials, internal hostnames, or paths beyond
the documented per-user %LOCALAPPDATA% locations.

A full read of the C# source was performed to write the Security Model section.
Findings were documented in README "Known limitations and hardening notes"
rather than silently omitted:
- Helper executables (`netsh`, `explorer.exe`) launched by bare name; Windows
  searches the app directory before System32, so a planted `netsh.exe` beside
  the exe would run elevated. NOT fixed in this change — offered to the user as
  a follow-up code change (pin to %SystemRoot%\System32\netsh.exe).
- Firewall parse/success detection is English-string dependent (locale bug).
- Releases are unsigned (no Authenticode) — release.yml has no signing step.
Confirmed clean: no network I/O of any kind; WQL interpolation guarded by
int.TryParse; all Process.Start use UseShellExecute=false; JSON deserialized to
concrete types only; startup entry is HKCU (not HKLM) with a quoted path.

## Prior session note
v1.2.0 is fully shipped (merged, tagged, released). The stale mid-release state
previously in this file described that completed release, not current work.
