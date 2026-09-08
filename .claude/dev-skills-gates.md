# Dev Skills gate state
Track: work commit
Version: 1.1.0 (unchanged — no release in this session)
Updated: 2026-09-08

🔢 VERSION    ⬜ not owed — work commit, no version bump
🔨 BUILD      ⬜ not owed — cannot run: no dotnet SDK, Windows-only WPF target (CI compiles on push)
🔒 SECURITY   ✅ re-review pass: 1 High found and fixed (elevated browser launch), 0 outstanding
📄 DOCS       ⬜ not owed — work commit, no release
📦 RELEASE    ⬜ not owed — work commit, no PR requested
🚀 SHIP       ⬜ not owed — work commit, no release

## Security re-review notes (2026-09-08)
- ⚠️ High (FIXED): `OpenUrl` used `ShellExecute` from a `requireAdministrator`
  process, launching the default browser elevated. Now routed through
  `explorer.exe`, which delegates to the user-level shell so the browser opens
  unelevated.
- First pass missed this — it checked `Process.Start` for argument injection
  (none: URLs are compile-time constants) and did not consider the privilege
  boundary created by the app manifest.
