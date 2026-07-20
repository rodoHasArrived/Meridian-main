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
| `quality-gate` | `Meridian CI` (`meridian-ci.yml`) fans out parallel evidence lanes and finishes with one stable `quality-gate` aggregator that should be the required status check for `main`. |
| `verify-dotnet` | `Meridian CI` .NET lane plus local `bash scripts/ci.sh --lane verify-dotnet`. |
| `verify-browser` | `Meridian CI` browser lane plus local `bash scripts/ci.sh --lane verify-browser`. |
| `verify-docs` | `Meridian CI` docs/source/AI lane, `CI` source-doc determinism job, and local docs checks. |
| `verify-workflows` | `Meridian CI` workflow hygiene lane plus lane manifest validation. |
| `verify-fast` | Local aggregate of `verify-dotnet` and `verify-browser`. |
| `targeted-test` | `Targeted Test` (`targeted-test.yml`) for manually dispatched GitHub-hosted curated lanes. |
| `schema-control` | `PostgreSQL Schema Control` (`schema-control.yml`) for migration execution, catalog/policy validation, and generated schema/data-object drift checks. |
| `verify-full` | Local-only broad lane (`make verify-full`) used before PR when needed. |
| `verify-desktop` | `Windows Desktop Build` (`windows-desktop-build.yml`). |
| `verify-release` | `Publish Smoke` (`publish-smoke.yml`) and `Desktop Installer Packaging` (`desktop-installer-packaging.yml`). |
| `production-certification` | `Production Certification` (`production-certification.yml`) for PostgreSQL integrations, zero-skip coverage, dependency scans, encrypted recovery drill, and same-commit docs evidence. |

| Workflow | File | Trigger | Purpose | Artifacts |
| --- | --- | --- | --- | --- |
| Meridian CI | `meridian-ci.yml` | Pull requests to `main`, pushes to `main`, merge queue groups, manual | Runs `.NET`, browser workstation, docs/source/AI, and workflow-hygiene lanes in parallel, then reports one stable `quality-gate` aggregator result. `Meridian CI / quality-gate` is the required status check for protected `main` merges after repository rulesets are enabled. | Lane summaries, build logs, TRX summaries, docs outputs, and workflow-hygiene evidence |
| CI | `ci.yml` | Pull requests, pushes to `main`, nightly, manual | Keeps PR secret scanning separate from the required `Meridian CI / quality-gate`, while legacy dotnet/browser/docs evidence jobs are gated away from normal PR runs. Nightly/manual `main` runs keep verify-full coverage evidence current. | Secret scan SARIF/evidence, .NET build logs, TRX summaries, browser bundle, and coverage artifacts |
| CodeQL | `codeql.yml` | Pull requests, pushes to `main`, weekly schedule, manual | Runs GitHub CodeQL static analysis for C# and JavaScript/TypeScript. C# uses an explicit .NET 10 restore/build (`build-mode: manual`); JavaScript/TypeScript uses `build-mode: none`. | CodeQL security alerts surfaced in the repository Security tab |
| Targeted Test | `targeted-test.yml` | Manual only | Runs a whitelisted hosted validation mode when local machine capacity, locks, or long-running suites make local validation impractical. Modes include filtered .NET, browser workstation, docs/source, WPF dev loop, WPF route, and desktop smoke. | Targeted TRX, browser bundle, docs/source, WPF validation, or desktop smoke artifacts |
| Golden Path Validation | `golden-path-validation.yml` | Golden-path contract, browser W4, WPF W4, or manual changes | Blocks pilot acceptance on browser `test:w4` parity and Windows `Category=W4Acceptance` desktop coverage before running `PilotAcceptanceHarnessTests`, validating the pilot readiness dashboard renderer, and uploading evidence bundles. | `pilot-acceptance-evidence`, `wpf-w4-acceptance-evidence` |
| Windows Desktop Build | `windows-desktop-build.yml` | WPF or WPF dependency changes, pushes to `main`, manual | Runs the isolated WPF validation script on Windows, including build-once/test-without-rebuild behavior. Desktop smoke publish runs on `main`, manual request, or PR changes to the WPF/publish graph. | WPF validation bundle and optional desktop smoke publish output |
| WPF Dev Loop Validation | `wpf-dev-validation.yml` | WPF, WPF dependency, desktop workflow script, or manual changes | Runs `scripts/dev/validate-wpf-dev.ps1` with the desktop workflow script-test default or a manual filter override. | WPF dev-loop evidence |
| WPF Route Validation | `wpf-route-validation.yml` | WPF, shared route dependency, route script, or manual changes | Runs position-blotter and operator-inbox route validation scripts on Windows. | Route validation evidence |
| Documentation Automation | `documentation.yml` | Documentation, Codex memory, docs-script, workflow, WPF navigation, diagram changes, or manual | Runs docs automation checks, validates AI inventory, Codex memory summary/receipt commands and focused memory-checker tests, regenerates tracked documentation outputs, refreshes Mermaid/UI diagrams using root lockfile-backed `npm ci`, renders UML artifacts through the PlantUML container, excludes version-specific UML render binaries from the final freshness diff, and gates severe dashboard regressions when a previous baseline exists. | Docs dashboard delta summary on failure |
| PostgreSQL Schema Control | `schema-control.yml` | PostgreSQL migrations, public contracts, schema-control registry/policies/tooling, generated database docs, or manual | Applies all registered SQL migrations to disposable PostgreSQL 16, extracts `pg_catalog`, inventories public C# data objects, evaluates policy and migration safety rules, and rejects stale deterministic manifests or diagrams. Manual `snapshot` mode uploads refresh candidates without writing to the repository. | Candidate manifests, generated docs/diagrams, policy report, schema diff, and run summary |
| Roadmap Source Docs | `roadmap-source-docs.yml` | Roadmap/source/status/architecture/source-README changes or manual | Enforces PR phase scope for roadmap/source-doc changes, validates roadmap and source registries, renders generated outputs, and checks for drift. | None |
| Roadmap Tools (Manual) | `roadmap-tools-manual.yml` | Manual only | Runs individual roadmap tooling operations on demand — fixture-enum validation, roadmap evidence-file validation, normalized render, or phase-scope enforcement — selected through the `script` dispatch input. | Normalized roadmap render output when `render`/`all` is selected |
| Maintenance | `maintenance.yml` | Workflow/docs/tooling changes, weekly schedule, manual | Runs repository workflow hygiene checks, validates tooling metadata, validates workflow syntax with `actionlint`, and checks AI contract/navigation drift. | None |
| Provider Validation | `provider-validation.yml` | Weekly or manual | Runs Wave 1 validation and prepares the DK1 review packet plus pending operator sign-off template. Final DK1 exit evidence remains fail-closed until the required owners sign and validate the template. | Provider validation review evidence |
| Publish Smoke | `publish-smoke.yml` | Manual only | Runs `build/scripts/publish/publish.ps1` for a selected Windows runtime. The `web-workstation`/`win-x64` lane starts the exact published artifact through an isolated production-authenticated install, fetches readiness, health, the workstation shell, and a referenced asset, then generates `release-evidence.json`. | Publish output, installed-startup receipt/probes, and release evidence manifest |
| Desktop Standalone Publish | `desktop-standalone-publish.yml` | Manual only | Publishes a desktop standalone `win-x64` executable and uploads the output. | Desktop standalone output |
| Desktop Installer Packaging | `desktop-installer-packaging.yml` | Tag pushes (`v*`), manual | Runs a WPF release preflight, builds x64/ARM64 MSIX and consumer setup artifacts, signs tag releases, generates checksums/SPDX SBOM/GitHub attestations, and blocks release publication on native x64 and ARM64 N-1 install/launch/update/repair/rollback/uninstall receipts. The ARM64 certification job requires a self-hosted Windows ARM64 runner. | Signed installers, checksum/SBOM/provenance, release manifests, and clean-machine lifecycle receipts |
| Production Certification | `production-certification.yml` | Weekly, release tags, manual | Runs deterministic API/auth/PostgreSQL integrations with coverage and zero tolerated skips, fails on current NuGet/npm vulnerabilities, executes an encrypted database plus data-root backup/clean-restore drill with RPO/RTO receipt, and rejects same-commit documentation drift. Live provider tests remain a separate explicit lane. | TRX/Cobertura/skip evidence, dependency reports, database schema inventory, encrypted recovery drill, and docs automation evidence |
| Windows Desktop Build Support | `desktop-workflow-runner.yml`, `desktop-screenshot-capture.yml`, `desktop-user-manual.yml` | Manual only | Runs selected desktop workflows, captures desktop screenshots, or generates the desktop user manual. These workflows always upload artifacts; `desktop-screenshot-capture.yml` can additionally open a `peter-evans/create-pull-request` PR with the refreshed catalog when dispatched with `commit: true`. They never push commits directly. | Desktop workflow, screenshot, or manual artifacts; optional screenshot refresh PR |
| Web Screenshot Capture | `web-screenshot-capture.yml` | Manual only | Captures browser workstation screenshots from the configured route list with `npm install --include=optional`, cached Playwright Chromium setup. Each route is captured independently, so a screen that fails to render is reported and skipped without blocking the rest; artifacts (including partial catalogs) always upload, and the `peter-evans/create-pull-request` refresh PR (`automation/web-screenshot-capture`) opens only when every route rendered. Duplicate dispatches share one workflow concurrency lane so stale queued runs are canceled before they can reopen the same refresh PR. It never pushes commits directly. | Web screenshot artifacts; screenshot refresh PR |
| Provider Smoke Checks | `ibapi-smoke.yml`, `robinhood-options-smoke.yml` | Path-filtered or manual | Runs provider smoke checks that are too specialized for the normal PR fast path. | Smoke evidence artifacts |
| Copilot Setup Steps | `copilot-setup-steps.yml` | Copilot setup, relevant pushes/PRs, manual | Validates the GitHub Copilot hosted setup path for repository dependencies. | None |

## Local Equivalents

When local CPU, memory, disk, package restore, or MSBuild lock contention makes testing unreliable,
push the branch and use `Targeted Test` from the GitHub Actions tab or dispatch it with `gh`.
Use the GitHub-hosted targeted lane as the preferred remote proof tool before retrying broad local
scripts. The .NET lane requires a repo-relative test project under `tests/` and a non-empty
`dotnet_filter` with a positive class, method, trait, or fully qualified name selector when
`mode=dotnet-filtered`; leave solution-level, negative-only, and broad CI filters on the normal CI
workflow. Non-.NET modes are selected with the `mode` input and run only whitelisted commands.
After timed-out generation, build, or test attempts, run
`python build/python/cli/buildctl.py validation-status --summary`, then `dotnet build-server
shutdown`; stop only abandoned repo-owned `dotnet`, `MSBuild`, `testhost`, `csc`, or
`VBCSCompiler` PIDs whose command lines clearly point at this checkout before retrying local
validation.

```powershell
gh workflow run targeted-test.yml --ref <branch> `
  -f mode=dotnet-filtered `
  -f runner=ubuntu-latest `
  -f dotnet_project=tests/Meridian.Tests/Meridian.Tests.csproj `
  -f dotnet_filter="FullyQualifiedName~ReportPackWorkflowServiceTests"

gh workflow run targeted-test.yml --ref <branch> `
  -f mode=wpf-dev-loop `
  -f runner=windows-latest `
  -f dotnet_filter="FullyQualifiedName~DesktopWorkflowScriptTests"

gh workflow run targeted-test.yml --ref <branch> `
  -f mode=browser-workstation `
  -f runner=ubuntu-latest

gh workflow run targeted-test.yml --ref <branch> `
  -f mode=wpf-route `
  -f runner=windows-latest `
  -f wpf_route=operator-inbox

python build/scripts/ci/dispatch-targeted-test.py `
  --ref <branch> `
  --mode dotnet-filtered `
  --dotnet-project tests/Meridian.Tests/Meridian.Tests.csproj `
  --dotnet-filter "FullyQualifiedName~ReportPackWorkflowServiceTests" `
  --wait
```

```powershell
bash scripts/ci.sh
bash scripts/ci.sh --lane verify-dotnet
bash scripts/ci.sh --lane verify-browser
bash scripts/ci.sh --lane verify-docs
bash scripts/ci.sh --lane verify-workflows
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet format whitespace Meridian.sln --verify-no-changes --verbosity minimal --no-restore
python3 build/scripts/ci/check-warning-suppressions.py
dotnet build Meridian.WebWorkstation.slnf -c Release --no-restore /p:EnableWindowsTargeting=true /p:UseAppHost=false
python3 build/scripts/ci/run-dotnet-ci-tests.py --configuration Release --filter "Category!=Integration&Category!=Performance" --results-dir artifacts/test-results/dotnet
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
pwsh ./scripts/dev/validate-wpf-dev.ps1 -Restore -Filter "Category!=Integration&FullyQualifiedName!~Integration"
```

Publish smoke:

```powershell
pwsh ./build/scripts/publish/publish.ps1 -Platform win-x64 -Project collector -Version 1.0.0-smoke -Configuration Release -OutputDir artifacts/publish/publish-smoke -OutputRetentionDays 0 -OutputRetainLatest 0
pwsh ./build/scripts/publish/publish.ps1 -Platform win-x64 -Project desktop -Version 1.0.0-smoke -Configuration Release -OutputDir artifacts/publish/publish-smoke -OutputRetentionDays 0 -OutputRetainLatest 0
pwsh ./build/scripts/publish/publish.ps1 -Platform win-x64 -Project web-workstation -Version 1.0.0-smoke -Configuration Release -OutputDir artifacts/publish/publish-smoke -OutputRetentionDays 0 -OutputRetainLatest 0
pwsh ./build/scripts/publish/generate-sbom.ps1 -BuildDropPath artifacts/publish/publish-smoke/win-x64/web-workstation -PackageName Meridian -PackageVersion 1.0.0-smoke
python build/scripts/ci/validate-test-results.py --results-dir artifacts/test-results/production-certification --output artifacts/test-results/production-certification/skip-evidence.json
```

Desktop installer release packaging:

```powershell
pwsh ./build/scripts/install/install.ps1 -Mode Desktop -Architecture x64 -SkipInstall -EnableReadyToRun -NoTrustCert -NoNotify
pwsh ./build/scripts/install/install.ps1 -Mode Desktop -Architecture ARM64 -SkipInstall -EnableReadyToRun -NoTrustCert -NoNotify
pwsh ./build/scripts/recovery/invoke-production-recovery.ps1 -Mode Drill -ConnectionString $env:MERIDIAN_LEDGER_CONNECTION_STRING -DataRoot $env:MDC_DATA_ROOT -BackupRoot artifacts/recovery/backups -RestoreConnectionString $env:MERIDIAN_RECOVERY_CONNECTION_STRING -RestoreDataRoot artifacts/recovery/restore-data -AllowDatabaseOverwrite
```

Workflow hygiene:

```powershell
python build/scripts/ci/check-workflow-hygiene.py
python build/scripts/ci/check-lane-manifest.py --summary
```

PostgreSQL schema control:

```powershell
python build/scripts/schema-control.py inventory --base-ref origin/main
gh workflow run schema-control.yml --ref <branch> -f mode=snapshot
```

Documentation automation:

```powershell
python build/scripts/docs/check-codex-memory.py --summary
python build/scripts/docs/check-codex-memory.py --task .codex/memory/tasks/example.yml --receipt --summary
python build/scripts/docs/check-codex-memory.py --goal .codex/memory/goals/example.yml --receipt --summary
python -m unittest build.scripts.docs.tests.test_check_codex_memory
python3 build/scripts/docs/run-docs-automation.py --profile core --summary-output docs/status/docs-automation-summary.md --json-output docs/status/docs-automation-summary.json
python3 build/scripts/docs/render-roadmap-diagrams.py --summary
python3 build/scripts/docs/render-source-diagrams.py --summary
npm ci --no-fund --no-audit
npm run generate-diagrams
python3 build/scripts/docs/generate-structure-docs.py --workflows-only --output docs/generated/workflows-overview.md
python3 build/scripts/docs/generate-workflow-manifest.py
```

## Standards

- All workflows use repository-relative paths.
- Default token permissions are read-only.
- Manual screenshot-capture workflows never push commits directly; any repository write is proposed
  through a reviewed `peter-evans/create-pull-request` PR (always for the web capture, and for the
  desktop capture only when dispatched with `commit: true`).
- PR and branch workflows cancel superseded runs.
- Build/test workflows use explicit restore, build, and test phases; the CI .NET lane aggregates per-project test results before failing so each run reports all broken configured test slices.
- Test lanes that enable hang diagnostics upload uniquely named evidence artifacts for reruns so passing and failing runs both leave inspectable logs.
- Generated outputs stay under ignored `artifacts/`, `bin/`, `obj/`, `publish/`, `dist/`, or `TestResults/` paths.
- Publish smoke artifacts are uploaded for inspection, and desktop installer tag runs publish packaged installer assets to GitHub Releases.
