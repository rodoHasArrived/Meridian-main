# GitHub Workflows

Meridian keeps the Actions surface intentionally small. The active workflows validate the
current .NET 10 solution, the browser workstation, the retained Windows desktop shell, and
safe publish output. Older automation for AI review, public release creation, documentation
deployment, Docker publishing, stale issue handling, and broad scheduled jobs was removed
because it either duplicated CI, depended on obsolete project assumptions, or performed
automation outside the current build/test/publish scope.

## Active Workflows

## Canonical lane mapping

| Lane | Workflow alignment |
| --- | --- |
| `bootstrap` | Local-only lane (`make bootstrap`); no dedicated hosted workflow. |
| `verify-fast` | `CI` (`ci.yml`) and browser workstation job names reference this lane. |
| `verify-full` | Local-only broad lane (`make verify-full`) used before PR when needed. |
| `verify-docs` | `CI` source-doc determinism job plus local docs checks. |
| `verify-desktop` | `Windows Desktop Build` (`windows-desktop-build.yml`). |
| `verify-release` | `Publish Smoke` (`publish-smoke.yml`) and `Desktop Installer Packaging` (`desktop-installer-packaging.yml`). |

| Workflow | File | Trigger | Purpose | Artifacts |
| --- | --- | --- | --- | --- |
| CI | `ci.yml` | Pull requests, pushes to `main`, manual | Restores `Meridian.sln`, verifies formatting, validates warning-suppression inventory, builds the focused `Meridian.WebWorkstation.slnf` lane, reports build warning counts, runs non-integration .NET tests, then tests and builds `src/Meridian.Ui/dashboard`. | .NET TRX results on failure |
| Golden Path Validation | `golden-path-validation.yml` | Golden-path contract, browser W4, WPF W4, or manual changes | Blocks pilot acceptance on browser `test:w4` parity and Windows `Category=W4Acceptance` desktop coverage before running `PilotAcceptanceHarnessTests`, writing `pilot-readiness.json` plus `pilot-readiness.md`, validating the pilot readiness dashboard renderer, generating `artifacts/pilot-acceptance/latest/pilot-readiness-dashboard.md`, and uploading the evidence bundles. | `pilot-acceptance-evidence`, `wpf-w4-acceptance-evidence` |
| Windows Desktop Build | `windows-desktop-build.yml` | Pull requests, pushes to `main`, manual | Builds the real WPF app on Windows, runs WPF tests, and smoke-publishes the desktop executable. | WPF TRX results on failure |
| Publish Smoke | `publish-smoke.yml` | Manual only | Runs `build/scripts/publish/publish.ps1` for a selected Windows runtime and uploads the generated standalone output. | Publish output |
| Desktop Installer Packaging | `desktop-installer-packaging.yml` | Tag pushes (`v*`), manual | Builds signed (or temporary-cert) MSIX installer packages for `win-x64` and `win-arm64`, uploads them as workflow artifacts, and attaches package assets to GitHub releases for tag runs. | Desktop installer packages (`.msix`, `.msixbundle`, `.appinstaller`) |
| Maintenance | `maintenance.yml` | Workflow/docs/tooling changes, weekly schedule, manual | Runs repository workflow hygiene checks and validates workflow syntax with `actionlint`. | None |

## Local Equivalents

```powershell
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet format Meridian.sln --verify-no-changes --verbosity minimal --no-restore
python3 build/scripts/ci/check-warning-suppressions.py
dotnet build Meridian.WebWorkstation.slnf -c Release --no-restore /p:EnableWindowsTargeting=true /p:UseAppHost=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --no-restore --filter "Category!=Integration&Category!=Performance" /p:EnableWindowsTargeting=true
npm install --prefix src/Meridian.Ui/dashboard --include=optional
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
```

Golden-path pilot acceptance:

```powershell
npm --prefix src/Meridian.Ui/dashboard run test:w4
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj -c Release --no-restore --filter "Category=W4Acceptance" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true
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

Desktop installer release packaging:

```powershell
pwsh ./build/scripts/install/install.ps1 -Mode Desktop -Architecture x64 -SkipInstall -EnableReadyToRun -NoTrustCert -NoNotify
pwsh ./build/scripts/install/install.ps1 -Mode Desktop -Architecture ARM64 -SkipInstall -EnableReadyToRun -NoTrustCert -NoNotify
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
- Publish smoke artifacts are uploaded for inspection, and desktop installer tag runs publish packaged installer assets to GitHub Releases.
