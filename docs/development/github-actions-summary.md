# GitHub Actions Workflows - Summary

**Status:** Active
**Reviewed:** 2026-05-18

The Meridian Actions surface is now four workflows aligned to the current .NET 10
solution, browser workstation, retained WPF desktop shell, and publish smoke needs.
The detailed workflow reference lives in `.github/workflows/README.md`.

| Workflow | File | Trigger | Purpose |
| --- | --- | --- | --- |
| CI | `.github/workflows/ci.yml` | Pull requests, pushes to `main`, manual | Restore solution, format check, warning-suppression inventory validation, focused web-workstation Release build with warning-count reporting, non-integration .NET tests, dashboard tests, dashboard build |
| Windows Desktop Build | `.github/workflows/windows-desktop-build.yml` | Pull requests, pushes to `main`, manual | Full Windows WPF build, WPF tests, desktop publish smoke |
| Publish Smoke | `.github/workflows/publish-smoke.yml` | Manual | Standalone Windows publish artifact for `collector`, `desktop`, or `web-workstation` |
| Maintenance | `.github/workflows/maintenance.yml` | Workflow/docs/tooling changes, weekly, manual | Workflow hygiene checks and Action YAML linting |

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
workflows. Useful build/test/publish logic was merged into the four active lanes above.
Externally deploying or mutating jobs were removed from the active workflow surface.
