# GitHub Workflows

Meridian keeps the Actions surface intentionally small. The active workflows validate the
current .NET 10 solution, the browser workstation, the retained Windows desktop shell, and
safe publish output. Older automation for AI review, public release creation, documentation
deployment, Docker publishing, stale issue handling, and broad scheduled jobs was removed
because it either duplicated CI, depended on obsolete project assumptions, or performed
automation outside the current build/test/publish scope.

## Active Workflows

| Workflow | File | Trigger | Purpose | Artifacts |
| --- | --- | --- | --- | --- |
| CI | `ci.yml` | Pull requests, pushes to `main`, manual | Restores `Meridian.sln`, verifies formatting, builds the focused `Meridian.WebWorkstation.slnf` lane, runs non-integration .NET tests, then tests and builds `src/Meridian.Ui/dashboard`. | .NET TRX results on failure |
| Golden Path Validation | `golden-path-validation.yml` | Golden-path contract changes, manual | Runs `PilotAcceptanceHarnessTests`, writes `pilot-readiness.json` plus `pilot-readiness.md`, validates the pilot readiness dashboard renderer, generates `artifacts/pilot-acceptance/latest/pilot-readiness-dashboard.md`, and uploads the acceptance evidence bundle. | `pilot-acceptance-evidence` |
| Windows Desktop Build | `windows-desktop-build.yml` | Pull requests, pushes to `main`, manual | Builds the real WPF app on Windows, runs WPF tests, and smoke-publishes the desktop executable. | WPF TRX results on failure |
| Publish Smoke | `publish-smoke.yml` | Manual only | Runs `build/scripts/publish/publish.ps1` for a selected Windows runtime and uploads the generated standalone output. | Publish output |
| Maintenance | `maintenance.yml` | Workflow/docs/tooling changes, weekly schedule, manual | Runs repository workflow hygiene checks and validates workflow syntax with `actionlint`. | None |

## Local Equivalents

```powershell
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet format Meridian.sln --verify-no-changes --verbosity minimal --no-restore
dotnet build Meridian.WebWorkstation.slnf -c Release --no-restore /p:EnableWindowsTargeting=true /p:UseAppHost=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --no-restore --filter "Category!=Integration&Category!=Performance" /p:EnableWindowsTargeting=true
npm ci --prefix src/Meridian.Ui/dashboard
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
```

Golden-path pilot acceptance:

```powershell
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~PilotAcceptanceHarnessTests" --logger "console;verbosity=normal"
python build/scripts/docs/generate-pilot-readiness-dashboard.py --output artifacts/pilot-acceptance/latest/pilot-readiness-dashboard.md --json-output artifacts/pilot-acceptance/latest/pilot-readiness-dashboard.json
python -m unittest build/scripts/docs/tests/test_pilot_readiness_dashboard.py
```

Windows desktop validation:

```powershell
dotnet restore tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true
dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Release --no-restore /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true /p:WindowsPackageType=None
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj -c Release --no-restore --filter "Category!=Integration&FullyQualifiedName!~Integration" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true
```

Publish smoke:

```powershell
pwsh ./build/scripts/publish/publish.ps1 -Platform win-x64 -Project collector -Version 1.0.0-smoke -Configuration Release -OutputDir artifacts/publish/publish-smoke -OutputRetentionDays 0 -OutputRetainLatest 0
pwsh ./build/scripts/publish/publish.ps1 -Platform win-x64 -Project desktop -Version 1.0.0-smoke -Configuration Release -OutputDir artifacts/publish/publish-smoke -OutputRetentionDays 0 -OutputRetainLatest 0
pwsh ./build/scripts/publish/publish.ps1 -Platform win-x64 -Project web-workstation -Version 1.0.0-smoke -Configuration Release -OutputDir artifacts/publish/publish-smoke -OutputRetentionDays 0 -OutputRetainLatest 0
```

Workflow hygiene:

```powershell
python build/scripts/ci/check-workflow-hygiene.py
```

## Standards

- All workflows use repository-relative paths.
- Default token permissions are read-only.
- PR and branch workflows cancel superseded runs.
- Build/test workflows use explicit restore, build, and test phases.
- Generated outputs stay under ignored `artifacts/`, `bin/`, `obj/`, `publish/`, `dist/`, or `TestResults/` paths.
- Publish smoke artifacts are uploaded for inspection only; no workflow creates a public release or deploys externally.
