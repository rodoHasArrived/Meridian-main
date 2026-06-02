# GitHub Actions Workflows - Summary

**Status:** Active
**Reviewed:** 2026-05-18

The Meridian Actions inventory is generated from `.github/workflows/*.yml` files instead of hand-maintained counts or tables. Use [`docs/generated/workflows-overview.md`](../generated/workflows-overview.md) as the workflow list source of truth.

Related generated command and validation artifacts:

- `docs/status/workflow-manifest.json` (canonical command manifest)
- `docs/generated/workflow-command-reference.md` (generated workflow commands)
- `docs/status/workflow-validation-summary.json` (machine-readable command validation summary)

## Local Command Map

The automatic CI lane mirrors:

```powershell
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet format Meridian.sln --verify-no-changes --verbosity minimal --no-restore
dotnet build Meridian.WebWorkstation.slnf -c Release --no-restore /p:EnableWindowsTargeting=true /p:UseAppHost=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --no-restore --filter "Category!=Integration&Category!=Performance" /p:EnableWindowsTargeting=true
npm ci --prefix src/Meridian.Ui/dashboard
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
```

The Windows desktop lane mirrors:

```powershell
dotnet restore tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true
dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Release --no-restore /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true /p:WindowsPackageType=None
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj -c Release --no-restore --filter "Category!=Integration&FullyQualifiedName!~Integration" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true
```

The manual publish smoke lane mirrors:

```powershell
pwsh ./build/scripts/publish/publish.ps1 -Platform win-x64 -Project collector -Version 1.0.0-smoke -Configuration Release -OutputDir artifacts/publish/publish-smoke -OutputRetentionDays 0 -OutputRetainLatest 0
pwsh ./build/scripts/publish/publish.ps1 -Platform win-x64 -Project desktop -Version 1.0.0-smoke -Configuration Release -OutputDir artifacts/publish/publish-smoke -OutputRetentionDays 0 -OutputRetainLatest 0
pwsh ./build/scripts/publish/publish.ps1 -Platform win-x64 -Project web-workstation -Version 1.0.0-smoke -Configuration Release -OutputDir artifacts/publish/publish-smoke -OutputRetentionDays 0 -OutputRetainLatest 0
```

## Removed Automation

The previous workflow set included overlapping PR, test-matrix, quality, nightly,
release, documentation, Docker, benchmark, issue-management, AI, and generated-artifact
workflows. Useful build/test/publish logic was merged into the current generated
workflow inventory in `docs/generated/workflows-overview.md`.
Externally deploying or mutating jobs were removed from the active workflow surface.
