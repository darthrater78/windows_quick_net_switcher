# Dev Skills gate state
Track: release sequence
Version: 1.2.1
Updated: 2026-09-08
Branch: claude/dev-skills-loading-m8hs6o

🔢 VERSION    ✅ csproj, app.manifest, MainWindow.xaml title, README release URL
              all at 1.2.1; v1.2.0 tagged on remote (previous release shipped);
              RepositoryUrl present; in-app release-notes link derives from the
              assembly version, so it follows automatically
🔨 BUILD      🚫 cannot run here — no dotnet SDK, net8.0-windows WPF target,
              Linux container. NOT N/A: the project has a real build system.
              build.yml (windows-latest) on the PR is the first actual compile.
              Do not mark ✅ until CI is green.
🔒 SECURITY   ✅ 0 Critical, 0 High — see below
📄 DOCS       ✅ v1.2.1 changelog entry; stale limitation bullet removed; Security
              Model updated to describe absolute-path launching; Architecture
              tree includes SystemPaths.cs
📦 RELEASE    ⏳ awaiting commit approval, then PR
🚀 SHIP       ⬜ merge + tag + CI publish. Tag push goes to the user (container
              creds are commonly denied on refs/tags/*)

## What changed
Fixes a local privilege-escalation vector. netsh and explorer.exe were launched
by bare name; CreateProcess searches the application's own directory before
System32, so a planted netsh.exe beside the exe would run elevated. All three
call sites now use absolute paths resolved once in the new SystemPaths.cs.

- QuickNetSwitcher/SystemPaths.cs        (new) path resolution
- NetworkAdapterService.cs:169           FileName = SystemPaths.Netsh
- FirewallService.cs:49                  FileName = SystemPaths.Netsh
- MainWindow.xaml.cs:395                 ProcessStartInfo(SystemPaths.Explorer, url)

## Gate 3 detail
Security: the change removes an attack path and adds none. New code takes no
user input, opens no network, launches no process; it only builds paths from
Environment.GetFolderPath. On re-review the %SystemRoot% fallback was removed
deliberately — an environment variable is inherited from the launching process,
and it should not be an input to the one path this fix depends on. Resolution
now falls back to the literal default and otherwise fails closed (netsh does not
start) rather than degrading to a bare name.

Quality: shared resolution extracted to one class instead of duplicated across
the two services; resolved once at type initialization rather than per call.
No nesting, sizing, or allocation concerns.

Build-by-inspection (since Gate 2 cannot run): caught and fixed a CS8600
nullable warning in an earlier draft where a string? was assigned to a var-typed
string under #nullable enable. Current form has no nullable assignment.

## Known issues NOT addressed in this release
Documented in README "Known limitations and hardening notes", not fixed here:
- Firewall parse and success detection are English-string dependent (locale bug)
- Releases are unsigned (no Authenticode step in release.yml)
