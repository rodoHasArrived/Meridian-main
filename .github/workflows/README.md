# GitHub Workflows

Meridian keeps the Actions surface scoped to the current .NET 10 solution, browser workstation,
retained Windows desktop shell, provider smoke checks, safe publish output, release packaging, and
documentation/diagram refreshes. Older automation for AI review, Docker publishing, stale issue
handling, and broad scheduled jobs was removed because it either duplicated CI, depended on
obsolete project assumptions, or performed automation outside the current build/test/publish/docs
scope.

## Active Workflows

## Canonical lane mapping

| Lane | Workflow alignment |
| --- | --- |
| `bootstrap` | Local-only lane (`make bootstrap`); no dedicated hosted workflow. |
| `verify-fast` | `CI` (`ci.yml`) and browser workstation job names reference this lane. |
| `targeted-test` | `Targeted Test` (`targeted-test.yml`) for manually dispatched GitHub-hosted .NET project/filter slices. |
| `verify-full` | Local-only broad lane (`make verify-full`) used before PR when needed. |
| `verify-docs` | `CI` source-doc determinism job plus local docs checks. |
| `verify-desktop` | `Windows Desktop Build` (`windows-desktop-build.yml`). |
| `verify-release` | `Publish Smoke` (`publish-smoke.yml`) and `Desktop Installer Packaging` (`desktop-installer-packaging.yml`). |

| Workflow | File | Trigger | Purpose | Artifacts |
| --- | --- | --- | --- | --- |
| CI | `ci.yml` | Pull requests, pushes to `main`, nightly, manual | Restores `Meridian.sln`, verifies formatting and warning-suppression inventory, builds the focused `Meridian.WebWorkstation.slnf` lane, runs non-integration .NET tests, tests and builds the dashboard with lockfile-strict `npm ci`, scans secrets, validates source-doc determinism, and runs nightly/manual verify-full coverage on `main`. | .NET TRX and coverage artifacts |
| Targeted Test | `targeted-test.yml` | Manual only | Runs one selected .NET test project plus a required `dotnet test --filter` on a GitHub-hosted runner when local machine capacity, locks, or long-running suites make local validation impractical. Inputs are validated and limited to repo-relative `tests/` project paths and normal filter expressions for the failing slice. | Targeted TRX results |
| Golden Path Validation | `golden-path-validation.yml` | Golden-path contract, browser W4, WPF W4, or manual changes | Blocks pilot acceptance on browser `test:w4` parity and Windows `Category=W4Acceptance` desktop coverage before running `PilotAcceptanceHarnessTests`, validating the pilot readiness dashboard renderer, and uploading evidence bundles. | `pilot-acceptance-evidence`, `wpf-w4-acceptance-evidence` |
| Windows Desktop Build | `windows-desktop-build.yml` | WPF or WPF dependency changes, pushes to `main`, manual | Builds the real WPF app on Windows, runs WPF tests, and smoke-publishes the desktop executable. Path filters include the WPF project-reference closure so shared service, reporting, storage, workflow, and related dependency changes trigger the lane. | WPF TRX results and desktop smoke publish output |
| WPF Dev Loop Validation | `wpf-dev-validation.yml` | WPF, WPF dependency, desktop workflow script, or manual changes | Runs `scripts/dev/validate-wpf-dev.ps1` with the desktop workflow script-test default or a manual filter override. | WPF dev-loop evidence |
| WPF Route Validation | `wpf-route-validation.yml` | WPF, shared route dependency, route script, or manual changes | Runs position-blotter and operator-inbox route validation scripts on Windows. | Route validation evidence |
| Documentation Automation | `documentation.yml` | Documentation, docs-script, workflow, WPF navigation, diagram changes, or manual | Runs docs automation checks, regenerates tracked documentation outputs, refreshes Mermaid/UML/UI diagrams using `npm ci`, and gates severe dashboard regressions when a previous baseline exists. | Docs dashboard delta summary on failure |
| Roadmap Source Docs | `roadmap-source-docs.yml` | Roadmap/source/status/architecture/source-README changes or manual | Enforces PR phase scope for roadmap/source-doc changes, validates roadmap and source registries, renders generated outputs, and checks for drift. | None |
| Maintenance | `maintenance.yml` | Workflow/docs/tooling changes, weekly schedule, manual | Runs repository workflow hygiene checks, validates tooling metadata, validates workflow syntax with `actionlint`, and checks AI contract/navigation drift. | None |
| Provider Validation | `provider-validation.yml` | Weekly or manual | Runs the Wave 1 provider validation evidence bundle and DK1 sign-off packet generation. | Provider validation evidence |
| Publish Smoke | `publish-smoke.yml` | Manual only | Runs `build/scripts/publish/publish.ps1` for a selected Windows runtime and uploads the generated standalone output. | Publish output |
| Desktop Standalone Publish | `desktop-standalone-publish.yml` | Manual only | Publishes a desktop standalone `win-x64` executable and uploads the output. | Desktop standalone output |
| Desktop Installer Packaging | `desktop-installer-packaging.yml` | Tag pushes (`v*`), manual | Runs a WPF release preflight build/test gate, builds signed MSIX installer packages for `win-x64` and `win-arm64`, uploads workflow artifacts, and attaches package assets to GitHub releases for tag runs. | Desktop installer packages (`.msix`, `.msixbundle`, `.appinstaller`) |
| Windows Desktop Build Support | `desktop-workflow-runner.yml`, `desktop-screenshot-capture.yml`, `desktop-user-manual.yml` | Manual only | Runs selected desktop workflows, captures desktop screenshots, or generates the desktop user manual. Screenshot/manual workflows are read-only and upload artifacts; they do not commit back to the repository. | Desktop workflow, screenshot, or manual artifacts |
| Web Screenshot Capture | `web-screenshot-capture.yml` | Manual only | Captures browser workstation screenshots from the configured route list using lockfile-strict `npm ci`. The workflow is read-only and uploads artifacts; it does not open PRs or push commits. | Web screenshot artifacts |
| Provider Smoke Checks | `ibapi-smoke.yml`, `robinhood-options-smoke.yml` | Path-filtered or manual | Runs provider smoke checks that are too specialized for the normal PR fast path. | Smoke evidence artifacts |
| Copilot Setup Steps | `copilot-setup-steps.yml` | Copilot setup, relevant pushes/PRs, manual | Validates the GitHub Copilot hosted setup path for repository dependencies. | None |

## Local Equivalents

When local CPU, memory, disk, package restore, or MSBuild lock contention makes testing unreliable,
push the branch and use `Targeted Test` from the GitHub Actions tab or dispatch it with `gh`.
Use the GitHub-hosted targeted lane as the preferred remote proof tool before retrying broad local
scripts. The .NET lane requires a repo-relative test project under `tests/` and a non-empty
`dotnet_filter` with a positive class, method, trait, or fully qualified name selector; leave
solution-level, negative-only, and broad CI filters on the normal CI workflow.

```powershell
gh workflow run targeted-test.yml --ref <branch> `
  -f runner=ubuntu-latest `
  -f dotnet_project=tests/Meridian.Tests/Meridian.Tests.csproj `
  -f dotnet_filter="FullyQualifiedName~ReportPackWorkflowServiceTests"

gh workflow run targeted-test.yml --ref <branch> `
  -f runner=windows-latest `
  -f dotnet_project=tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj `
  -f dotnet_filter="FullyQualifiedName~DesktopWorkflowScriptTests" `
  -f enable_full_wpf_build=true
```

```powershell
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet format Meridian.sln --verify-no-changes --verbosity minimal --no-restore
python3 build/scripts/ci/check-warning-suppressions.py
dotnet build Meridian.WebWorkstation.slnf -c Release --no-restore /p:EnableWindowsTargeting=true /p:UseAppHost=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --no-restore --filter "Category!=Integration&Category!=Performance" /p:EnableWindowsTargeting=true
npm ci --prefix src/Meridian.Ui/dashboard --include=optional
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
pwsh ./scripts/dev/run-local-quality.ps1 -IncludePlaywrightSmoke
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
pwsh ./build/scripts/publish/generate-sbom.ps1 -BuildDropPath artifacts/publish/publish-smoke/win-x64/web-workstation -PackageName Meridian -PackageVersion 1.0.0-smoke
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

Documentation automation:

```powershell
python3 build/scripts/docs/run-docs-automation.py --profile core --summary-output docs/status/docs-automation-summary.md --json-output docs/status/docs-automation-summary.json
python3 build/scripts/docs/render-roadmap-diagrams.py --summary
python3 build/scripts/docs/render-source-diagrams.py --summary
npm ci
npm run generate-diagrams
python3 build/scripts/docs/generate-structure-docs.py --workflows-only --output docs/generated/workflows-overview.md
python3 build/scripts/docs/generate-workflow-manifest.py
```

## Standards

- All workflows use repository-relative paths.
- Default token permissions are read-only.
- Manual screenshot and manual-generation workflows are artifact-only; repository writes should be
  made from a reviewed local or PR workflow, not from those capture jobs.
- PR and branch workflows cancel superseded runs.
- Build/test workflows use explicit restore, build, and test phases.
- Generated outputs stay under ignored `artifacts/`, `bin/`, `obj/`, `publish/`, `dist/`, or `TestResults/` paths.
- Publish smoke artifacts are uploaded for inspection, and desktop installer tag runs publish packaged installer assets to GitHub Releases.
