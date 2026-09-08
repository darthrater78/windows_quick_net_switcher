# Dev Skills gate state
Track: release sequence
Version: 1.2.0
Updated: 2026-09-08

🔢 VERSION    ✅ csproj, app.manifest, MainWindow.xaml title, README all at 1.2.0; v1.1.0 tagged on remote
🔨 BUILD      🚫 cannot run here — no dotnet SDK, Windows-only WPF target. Delegated to CI
              (build.yml: windows-latest, Release build + single-file publish, runs on PR)
🔒 SECURITY   ✅ 0 Critical, 0 High (1 High found and fixed on re-review: elevated browser launch)
📄 DOCS       ✅ v1.2.0 changelog entry, feature listed, How It Works updated, release URL at v1.2.0
📦 RELEASE    ⏳ PR being opened
🚀 SHIP       ⬜ merge + tag + publish. Tag push goes to the user (container creds 403 on tag refs)

## Notes
- Gate 2 is NOT ✅ and NOT N/A: the project has a real build system, it simply
  cannot run in this Linux container. The PR's CI build check is the first
  actual compile. Do not mark ✅ until CI is green.
- Security re-review: `OpenUrl` originally shell-executed the URL from a
  `requireAdministrator` process, which would launch the browser elevated.
  Now routed through `explorer.exe` to the user-level shell.
