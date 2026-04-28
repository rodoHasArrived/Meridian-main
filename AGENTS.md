# AGENTS.md

This file is a compatibility shim for agents that look for root `AGENTS.md`.
Keep it short and prefer the canonical Meridian guidance sources:

- `CLAUDE.md` for the full repository guide.
- `.codex/skills/_shared/project-context.md` for current Codex project context.
- `docs/HELP.md` for verified operator and developer CLI workflows.
- `docs/ai/navigation/README.md` for generated repo-navigation workflow guidance.
- `docs/development/build-observability.md` for build diagnostics, metrics, fingerprints, and debug bundles.
- `docs/development/desktop-workflow-automation.md` for scripted WPF workflow runs.
- `docs/development/desktop-testing-guide.md` for WPF test slices and shell-first regression bundles.
- `docs/development/wpf-implementation-notes.md` for WPF shell routing, workspace surfaces, and focused validation guidance.
- `docs/development/documentation-automation.md` for local docs automation profiles and generated-docs rules.
- `docs/operations/msix-packaging.md` for desktop MSIX packaging and install workflows.
- `docs/status/provider-validation-matrix.md` for Wave 1 provider evidence gates.
- `docs/status/dk1-pilot-parity-runbook.md` for the DK1 provider parity packet workflow.
- `docs/status/kernel-readiness-dashboard.md` for DK gate status and operator sign-off.
- `docs/status/contract-compatibility-matrix.md` for shared contract compatibility gates.
- `docs/status/FEATURE_INVENTORY.md` for current feature inventory and retained local web/API scope.
- `docs/plans/paper-trading-cockpit-reliability-sprint.md` for the Wave 2 readiness contract.
- `README.md` for top-level onboarding and planning links.

## Current Direction

- Meridian is a .NET 9 fund-management and trading-platform codebase.
- `src/Meridian.Wpf/` is the primary operator shell. Prefer desktop-first workflow guidance.
- Treat `src/Meridian.Ui/` as a retained local diagnostics/API support surface, not the primary operator shell.
- Keep top-level operator navigation to `Research`, `Trading`, `Data Operations`, and `Governance`.
- Use the narrowest validation command that covers the files changed.

## Command Discovery

```bash
make help
dotnet run --project src/Meridian/Meridian.csproj -- --help
dotnet run --project src/Meridian/Meridian.csproj -- --help diagnostics
dotnet run --project src/Meridian/Meridian.csproj -- --help security-master
python3 build/python/cli/buildctl.py --help
```

## Setup And Container Workflows

```bash
make check-deps
make setup-config
make install
make install-native
make install-docker
make docker
make docker-build
make docker-up
make docker-logs
make docker-restart
make docker-down
make docker-clean
make docker-monitoring
```

## Run And Host Workflows

```bash
make setup-dev
make quickstart
make run
make run-backfill SYMBOLS=AAPL,MSFT
make run-selftest
dotnet run --project src/Meridian/Meridian.csproj -- --quickstart
dotnet run --project src/Meridian/Meridian.csproj -- --config config/appsettings.json
dotnet run --project src/Meridian/Meridian.csproj -- --validate-config
dotnet run --project src/Meridian/Meridian.csproj -- --check-config
dotnet run --project src/Meridian/Meridian.csproj -- --show-config
dotnet run --project src/Meridian/Meridian.csproj -- --watch-config
dotnet run --project src/Meridian/Meridian.csproj -- --wizard
dotnet run --project src/Meridian/Meridian.csproj -- --auto-config
dotnet run --project src/Meridian/Meridian.csproj -- --detect-providers
dotnet run --project src/Meridian/Meridian.csproj -- --generate-config --template minimal
dotnet run --project src/Meridian/Meridian.csproj -- --generate-config-schema --output config/appsettings.schema.json
dotnet run --project src/Meridian/Meridian.csproj -- --list-presets
dotnet run --project src/Meridian/Meridian.csproj -- --preset researcher
dotnet run --project src/Meridian/Meridian.csproj -- --recommend-providers
dotnet run --project src/Meridian/Meridian.csproj -- --simulate-feed
dotnet run --project src/Meridian/Meridian.csproj -- --selftest
dotnet run --project src/Meridian/Meridian.csproj -- --dry-run --offline
dotnet run --project src/Meridian/Meridian.csproj -- --mode desktop --http-port 8080
```

## CLI Data Workflows

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --symbols
dotnet run --project src/Meridian/Meridian.csproj -- --symbols-monitored
dotnet run --project src/Meridian/Meridian.csproj -- --symbols-archived
dotnet run --project src/Meridian/Meridian.csproj -- --symbols-add AAPL,MSFT
dotnet run --project src/Meridian/Meridian.csproj -- --symbols-add ES --depth-levels 20
dotnet run --project src/Meridian/Meridian.csproj -- --symbols-add SPY --no-depth
dotnet run --project src/Meridian/Meridian.csproj -- --symbols-add QQQ --no-trades
dotnet run --project src/Meridian/Meridian.csproj -- --symbols-add AAPL --no-depth --update
dotnet run --project src/Meridian/Meridian.csproj -- --symbols-remove TSLA
dotnet run --project src/Meridian/Meridian.csproj -- --symbols-import symbols.csv
dotnet run --project src/Meridian/Meridian.csproj -- --symbols-export symbols.txt
dotnet run --project src/Meridian/Meridian.csproj -- --symbol-status AAPL
dotnet run --project src/Meridian/Meridian.csproj -- --query "last SPY"
dotnet run --project src/Meridian/Meridian.csproj -- --query "count SPY" --from 2026-01-01 --to 2026-01-31
dotnet run --project src/Meridian/Meridian.csproj -- --catalog symbols
dotnet run --project src/Meridian/Meridian.csproj -- --catalog search "AAPL trades 2025"
dotnet run --project src/Meridian/Meridian.csproj -- --catalog coverage
dotnet run --project src/Meridian/Meridian.csproj -- --catalog timeline --symbol AAPL
dotnet run --project src/Meridian/Meridian.csproj -- --backfill --backfill-symbols AAPL,MSFT --backfill-from 2025-01-01 --backfill-to 2025-12-31
dotnet run --project src/Meridian/Meridian.csproj -- --backfill --backfill-symbols AAPL --backfill-from 2025-01-01 --backfill-to 2025-01-31 --backfill-granularity 15Min
dotnet run --project src/Meridian/Meridian.csproj -- --backfill --backfill-provider polygon --backfill-symbols SPY
dotnet run --project src/Meridian/Meridian.csproj -- --backfill --resume --backfill-symbols QQQ
dotnet run --project src/Meridian/Meridian.csproj -- --package --package-name market-data-archive
dotnet run --project src/Meridian/Meridian.csproj -- --package --package-symbols AAPL,MSFT --package-from 2025-01-01
dotnet run --project src/Meridian/Meridian.csproj -- --list-package ./packages/data.zip
dotnet run --project src/Meridian/Meridian.csproj -- --validate-package ./packages/data.zip
dotnet run --project src/Meridian/Meridian.csproj -- --import-package ./packages/data.zip
dotnet run --project src/Meridian/Meridian.csproj -- --validate-schemas
dotnet run --project src/Meridian/Meridian.csproj -- --validate-schemas --strict-schemas
dotnet run --project src/Meridian/Meridian.csproj -- --check-schemas --max-files 100
dotnet run --project src/Meridian/Meridian.csproj -- --wal-repair --dry-run --output artifacts/wal-repair-report.txt
dotnet run --project src/Meridian/Meridian.csproj -- --generate-loader python --output ./loaders
```

TODO: `src/Meridian.Application/Commands/EtlCommands.cs` exposes `--etl-import`,
`--etl-export`, `--etl-roundtrip`, and `--etl-resume`, but `docs/HELP.md` does not yet document
operator examples. Verify the intended ETL workflow before adding those as standard commands.

TODO: `SecurityMasterCommands` and `ProviderCalibrationCommand` expose `--security-master-ingest`
and `--calibrate-provider-degradation`, but their prerequisites are specialized. Use
`--help security-master` for Security Master details, and verify current operator setup before
adding short-form examples here.

TODO: `docs/HELP.md` mentions `--diagnostics`, but `DiagnosticsCommands` currently handles
specific flags (`--quick-check`, `--test-connectivity`, `--show-config`, `--error-codes`, and
`--validate-credentials`) rather than a standalone `--diagnostics` flag. Use `--help diagnostics`
for the reference topic unless that flag is implemented.

TODO: `docs/HELP.md` includes `--package --package-format csv`, but `PackageCommands` currently
maps `--package-format` to zip, tar.gz/tgz, or 7z. Verify the intended CSV export workflow before
adding that as a standard package command.

TODO: `--replay` is exposed in `CliArguments` and listed in `docs/status/FEATURE_INVENTORY.md`, but
`JsonlReplayer` currently treats the value as a directory root while help text describes a JSONL
file path. Verify the intended replay path semantics before adding a standard CLI replay example.

TODO: `docs/status/FEATURE_INVENTORY.md` lists `--simulate-execution` as a planned simulation CLI
workflow, but no command handler was found. Keep it out of standard command examples until it is
implemented.

## MCP Workflows

```bash
dotnet run --project src/Meridian.Mcp/Meridian.Mcp.csproj
dotnet run --project src/Meridian.McpServer/Meridian.McpServer.csproj -- --config config/appsettings.json
```

## Build And Test

```bash
make build
make build-quick
make lint
make format
make format-check
make test
make test-unit
make test-fsharp
make test-integration
make test-all
make test-coverage
make install-hooks
make pre-pr
make pre-pr-full
make watch
make watch-build
make clean
make benchmark
make bench-quick
make bench-filter FILTER=*Collector*
make publish
make publish-linux
make publish-windows
make publish-macos
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
python3 build/python/cli/buildctl.py build --project Meridian.sln --configuration Release
python3 build/python/cli/buildctl.py build --project Meridian.sln --configuration Debug --verbosity quiet
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj --logger "console;verbosity=normal"
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~MapWorkstationEndpoints_TradingReadiness" --logger "console;verbosity=normal"
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~MapWorkstationEndpoints_OperatorInbox" --logger "console;verbosity=normal"
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~PaperSessionReplayTests" --logger "console;verbosity=normal"
dotnet test tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj /p:EnableWindowsTargeting=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.McpServer.Tests/Meridian.McpServer.Tests.csproj --logger "console;verbosity=normal"
dotnet test tests/Meridian.QuantScript.Tests/Meridian.QuantScript.Tests.csproj --logger "console;verbosity=normal"
python3 -m unittest tests/scripts/test_generate_dk1_pilot_parity_packet.py
python3 -m unittest tests/scripts/test_prepare_dk1_operator_signoff.py
```

For concurrent automation, use an isolation key so builds write under `artifacts/bin/<key>/`
and `artifacts/obj/<key>/` instead of shared project output folders:

```bash
python3 build/python/cli/buildctl.py build --project Meridian.sln --configuration Release --isolation-key automation-run
python3 build/python/cli/buildctl.py build --project src/Meridian.Wpf/Meridian.Wpf.csproj --configuration Release --full-wpf-build --isolation-key desktop-smoke
```

Use `MapWorkstationEndpoints_TradingReadiness` for changes to the trading readiness endpoint,
DTOs, execution-control evidence, acceptance gates, or operator work-item projection.
Use `MapWorkstationEndpoints_OperatorInbox` for changes to the operator inbox endpoint, navigation
hints, readiness work-item aggregation, or reconciliation routing/sign-off detail projection.

## Desktop Workflows

```powershell
pwsh ./scripts/dev/desktop-dev.ps1
pwsh ./scripts/dev/run-desktop.ps1
pwsh ./scripts/dev/run-desktop.ps1 -NoBuild
pwsh ./scripts/dev/run-desktop.ps1 -Fixture
dotnet run --project src/Meridian.Wpf/Meridian.Wpf.csproj -p:EnableFullWpfBuild=true
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~ShellNavigationCatalogTests|FullyQualifiedName~NavigationServiceTests|FullyQualifiedName~MainPageUiWorkflowTests|FullyQualifiedName~WorkspaceShellContextStripControlTests|FullyQualifiedName~WorkspaceDeepPageChromeTests|FullyQualifiedName~WelcomePageViewModelTests|FullyQualifiedName~WorkstationWorkflowSummaryServiceTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~TradingWorkspaceShellPageTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~ResearchWorkspaceShellPageTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~TradingHoursViewModelTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~OrderBookViewModelTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~MainShellViewModelTests|FullyQualifiedName~MessagingHubViewModelTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~StrategyRunBrowserViewModelTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~BatchBacktestViewModelTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~QuantScriptViewModelTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~CashFlowViewModelTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~FundLedgerViewModelTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~FundAccountsViewModelTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~SecurityMasterViewModelTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~ProviderHealthViewModelTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~SystemHealthViewModelTests|FullyQualifiedName~SystemHealthPageSmokeTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~ActivityLogViewModelTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~NotificationCenterViewModelTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~WatchlistViewModelTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~DataQualityViewModelCharacterizationTests|FullyQualifiedName~DataQualityPageSmokeTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~StorageViewModelTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~SingleInstanceServiceTests|FullyQualifiedName~DesktopWorkflowScriptTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
pwsh -File ./scripts/dev/run-desktop-workflow.ps1 -Workflow debug-startup
pwsh -File ./scripts/dev/run-desktop-workflow.ps1 -Workflow debug-startup -NoFixture -ReuseExistingApp
pwsh -File ./scripts/dev/run-desktop-workflow.ps1 -Workflow screenshot-catalog -ScreenshotDirectory docs/screenshots/desktop
pwsh -File ./scripts/dev/generate-desktop-user-manual.ps1
pwsh -File ./scripts/dev/capture-desktop-screenshots.ps1
pwsh -File ./scripts/dev/capture-desktop-screenshots.ps1 -SkipBuild -ProjectPath src/Meridian.Wpf/Meridian.Wpf.csproj -Configuration Release -Framework net9.0-windows10.0.19041.0
pwsh -File ./scripts/dev/robinhood-options-smoke.ps1
pwsh -File ./build/scripts/install/install.ps1 -Mode Desktop -SkipInstall
```

The desktop launcher builds the local host and WPF shell when needed, starts the host on
`http://localhost:8080`, waits for `/healthz`, launches the shell, and stops the owned host
when the desktop exits.

Named workflow automation is defined in `scripts/dev/desktop-workflows.json`; it defaults to
fixture mode and writes run artifacts under `artifacts/desktop-workflows/`.

Use `run-desktop-workflow.ps1 -NoFixture -ReuseExistingApp` after launching
`run-desktop.ps1` when driving an already-open shell against live local services; the runner
forwards page/deep-link args through the single-instance pipe and waits on `ShellAutomationState`.
During desktop workflow and screenshot-catalog runs, restore lets shared projects use their
declared target frameworks while the build step pins the WPF shell to
`net9.0-windows10.0.19041.0`.
`robinhood-options-smoke.ps1` validates Robinhood setup and the options workflow with seeded
fixture state and writes artifacts under `artifacts/desktop-workflows/robinhood-options-smoke/`.
Use the `capture-desktop-screenshots.ps1 -SkipBuild` form only after a Release WPF build, such as
the `.github/workflows/refresh-screenshots.yml` desktop screenshot lane.
Use the focused `ResearchWorkspaceShellPageTests` and `TradingWorkspaceShellPageTests` filters
for WPF desk-briefing hero state changes, including Trading shared-work-item attention routing,
before broadening to the full WPF test pass.
Use `ShellNavigationCatalogTests`, `NavigationServiceTests`, `MainPageUiWorkflowTests`,
`WorkspaceShellContextStripControlTests`, `WorkspaceDeepPageChromeTests`,
`WelcomePageViewModelTests`, and `WorkstationWorkflowSummaryServiceTests` when changing workspace
catalog entries, shell routing/selection, context-strip attention detail, welcome readiness,
workflow-summary handoffs, or hosted deep-page chrome.
Use `TradingHoursViewModelTests` when changing Trading Hours session briefing, market calendar
status projection, or `TradingHoursPage` binding coverage.
Use `OrderBookViewModelTests` when changing Order Book posture, selected-symbol/depth scope,
spread/cumulative-delta projection, tape-readiness handoff, or `OrderBookPage` binding coverage.
Use `MainShellViewModelTests` and `MessagingHubViewModelTests` when changing shell operator-inbox
actions, queue routing or route metadata resolution, Messaging Hub delivery posture, activity
retention, empty states, or clear activity binding.
Use `StrategyRunBrowserViewModelTests` when changing run-browser filters, filter recovery, empty
states, comparison state, or `StrategyRunsPage` binding coverage.
Use `BatchBacktestViewModelTests` when changing Batch Backtest sweep result states, empty-state
guidance, cancellation/failure summaries, or `BatchBacktestPage` binding coverage.
Use `QuantScriptViewModelTests` when changing QuantScript execution history, run-browser handoffs,
parameter context, or source-level `QuantScriptPage` binding coverage.
Use `CashFlowViewModelTests` when changing Run Cash Flow selected-run, missing-run, no-event, or
loaded states, cash-flow ladder/event empty-state guidance, or `RunCashFlowPage` binding coverage.
Use `FundLedgerViewModelTests` when changing Fund Ledger reconciliation filters, break-queue
recovery, selected break actions, account drill-ins, or `FundLedgerPage` binding coverage.
Use `FundAccountsViewModelTests` when changing Fund Accounts operator briefing, account inspectors,
provider-routing previews, shared-data readiness, balance-evidence snapshot posture, or
`FundAccountsPage` binding coverage.
Use `SecurityMasterViewModelTests` when changing Security Master runtime fallback, search recovery,
selected-security trust workbench actions, conflict operator lanes, or `SecurityMasterPage` binding
coverage.
Use `ProviderHealthViewModelTests`, `SystemHealthViewModelTests` plus `SystemHealthPageSmokeTests`,
`ActivityLogViewModelTests`, `NotificationCenterViewModelTests`, and `WatchlistViewModelTests`
for provider-posture, system-health triage, support-triage, activity-log header actions,
notification-history recovery, watchlist posture, pinned-list ordering, and pinned-badge surface
changes. Use
`DataQualityViewModelCharacterizationTests` plus `DataQualityPageSmokeTests` when changing Data
Quality symbol-filter recovery or empty-state guidance. Use `StorageViewModelTests` when changing
Storage preview scope text, preview-root normalization, `StoragePage` bindings, or preview
automation IDs. Use
`SingleInstanceServiceTests` plus `DesktopWorkflowScriptTests` when changing launch/deep-link
forwarding, shell automation markers, or isolated desktop workflow restore/build behavior.

```bash
make desktop-build
make desktop-test
make desktop-test-position-blotter-route
make desktop-test-operator-inbox-route
```

`make desktop-test-position-blotter-route` runs
`scripts/dev/validate-position-blotter-route.ps1`, which builds and tests the focused WPF
position-blotter route slice with isolated output and writes validation artifacts under
`artifacts/wpf-validation/position-blotter-route/`.
`make desktop-test-operator-inbox-route` runs
`scripts/dev/validate-operator-inbox-route.ps1`, which builds and tests the focused WPF
operator-inbox route slice with isolated output and writes validation artifacts under
`artifacts/wpf-validation/operator-inbox-route/`.

TODO: `README.md` and `.codex/skills/_shared/project-context.md` mention `make desktop-run`,
but the current `make/desktop.mk` does not define that target. Use
`pwsh ./scripts/dev/run-desktop.ps1` unless the Make target is restored.

TODO: `docs/development/desktop-workflow-automation.md` mentions `make desktop-workflow`,
`make desktop-manual`, and `make desktop-screenshots`, but the current `make/desktop.mk` does not
define those targets. Use the PowerShell scripts directly unless the Make targets are added.

TODO: `docs/development/desktop-testing-guide.md` still references `make desktop-dev-bootstrap`,
`make build-wpf`, and `make test-desktop-services`; `docs/development/policies/desktop-support-policy.md`
and `docs/development/wpf-implementation-notes.md` still reference `make build-wpf` or
`make test-desktop-services`; and `scripts/dev/desktop-dev.ps1` still prints `make build-wpf`,
`make test-desktop-services`, and `make uwp-xaml-diagnose`. The current `make/*.mk` files do not
define those targets. Prefer `pwsh ./scripts/dev/desktop-dev.ps1`, `make desktop-build`,
`make desktop-test`, and `pwsh ./scripts/dev/diagnose-uwp-xaml.ps1`.

TODO: `docs/operations/msix-packaging.md` documents `make desktop-publish`, but the current
`make/*.mk` files do not define that target. Use
`pwsh -File ./build/scripts/install/install.ps1 -Mode Desktop -SkipInstall` for desktop package
builds unless the Make target is restored.

`.github/workflows/refresh-screenshots.yml` is a WPF-only screenshot lane. It runs
`screenshot-catalog` plus the `manual-*` workflows through
`scripts/dev/run-desktop-workflow.ps1` in fixture mode; `workflow_dispatch` can choose `all`,
`catalog`, or `manuals`, override `output_root`, and skip the final commit with `commit=false`.
The workflow commits screenshots once in the follow-up `commit-screenshots` job; do not restore
the removed `--ui` web-dashboard path for screenshot refreshes.

## Paper Trading Readiness

Use these probes against a running local host when checking Wave 2 cockpit readiness or replay
evidence:

```powershell
Invoke-RestMethod http://localhost:8080/api/workstation/trading/readiness
Invoke-RestMethod "http://localhost:8080/api/workstation/trading/readiness?fundAccountId=<account-guid>"
Invoke-RestMethod http://localhost:8080/api/workstation/operator/inbox
Invoke-RestMethod "http://localhost:8080/api/workstation/operator/inbox?fundAccountId=<account-guid>"
Invoke-RestMethod http://localhost:8080/api/workstation/trading
Invoke-RestMethod http://localhost:8080/api/execution/sessions/<session-id>/replay
```

The readiness endpoint is `GET /api/workstation/trading/readiness`. The broader workstation
trading payload (`GET /api/workstation/trading`) embeds the same readiness data. The operator inbox
endpoint (`GET /api/workstation/operator/inbox`) aggregates readiness work items and open or
in-review reconciliation breaks with navigation hints and routing/sign-off detail. Use the
`fundAccountId` query parameter when checking account-scoped brokerage-sync readiness or the WPF
account operating context. The replay route,
`GET /api/execution/sessions/{sessionId}/replay`, verifies a paper session replay and writes
durable execution-audit evidence used to reconstruct replay readiness after restart.
Replay readiness is stale if the active paper session's fill, order, or ledger-entry counts diverge
from the latest verification audit; rerun replay verification before accepting cockpit readiness.
Treat additive trading readiness DTO or enum changes as shared contract changes; the current
payload includes execution-control evidence explainability, acceptance gates, and operator work
items alongside session, replay, promotion, DK1 trust-gate, and brokerage-sync posture.

## Provider Validation

```powershell
pwsh ./scripts/dev/run-wave1-provider-validation.ps1
pwsh ./scripts/dev/prepare-dk1-operator-signoff.ps1 -OutputPath artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-operator-signoff.json -PacketPath artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-pilot-parity-packet.json
pwsh ./scripts/dev/prepare-dk1-operator-signoff.ps1 -OutputPath artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-operator-signoff.json -PacketPath artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-pilot-parity-packet.json -Validate
pwsh ./scripts/dev/generate-dk1-pilot-parity-packet.ps1 -SummaryJsonPath artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.json
pwsh ./scripts/dev/generate-dk1-pilot-parity-packet.ps1 -SummaryJsonPath artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.json -OperatorSignoffPath artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-operator-signoff.json
pwsh ./scripts/dev/build-ibapi-smoke.ps1
```

This is the active Wave 1 gate for Alpaca, Robinhood, Yahoo, checkpoint reliability, and Parquet
proof. It writes summaries and DK1 parity packets under
`artifacts/provider-validation/_automation/<yyyy-mm-dd>/`.
Generated provider-validation summaries and DK1 parity packets are run-date evidence; regenerate
or attach current artifacts for DK1 reviews instead of relying on older artifact paths.
`run-wave1-provider-validation.ps1` invokes `generate-dk1-pilot-parity-packet.ps1` when present;
run the packet generator directly only when rebuilding from an existing Wave 1 summary. Generate
and validate the sign-off template with `-PacketPath` so the retained `packetReview` binds owner
approvals to the reviewed DK1 parity packet. After owner review, pass the signed file to
`generate-dk1-pilot-parity-packet.ps1 -OperatorSignoffPath` for the already reviewed packet. Do not
rerun `run-wave1-provider-validation.ps1 -OperatorSignoffPath` with the same signed file after
approval; the wrapper emits a new packet timestamp, so the old packet binding is intentionally
stale. A `ready-for-operator-review` DK1 packet still requires `operatorSignoff.status=signed`, a
valid packet binding, and no missing owners before DK1 exit.
`build-ibapi-smoke.ps1` is a compile-only Interactive Brokers adapter smoke build that enables
`EnableIbApiSmoke=true` on `src/Meridian.Infrastructure/Meridian.Infrastructure.csproj`.
The Wave 2 cockpit readiness contract reads DK1 packet posture through
`GET /api/workstation/trading/readiness`; keep packet sign-off status synchronized with the
provider matrix, DK1 runbook, and kernel readiness dashboard before claiming cockpit readiness.

## Diagnostics And Docs

```bash
make doctor
make doctor-ci
make doctor-quick
make diagnose
make verify-setup
make diagnose-build
make collect-debug
make collect-debug-minimal
pwsh ./scripts/dev/cleanup-generated.ps1
make build-profile
make build-binlog
make build-graph
make fingerprint
make env-capture NAME=local
make env-diff ENV1=local ENV2=ci
make impact FILE=src/Meridian/Meridian.csproj
make bisect GOOD=<good-ref> BAD=<bad-ref>
make metrics
make history
make analyze-errors
make validate-data
make ai-audit
make ai-audit-code
make ai-audit-docs
make ai-audit-tests
make ai-audit-ai-docs
make ai-report
make ai-arch-check
make ai-arch-check-summary
make ai-arch-check-json
make ai-verify
make ai-maintenance-light
make ai-maintenance-full
make ai-docs-freshness
make ai-docs-drift
make ai-docs-sync-report
make ai-docs-archive
make verify-adrs
make verify-contracts
make verify-tooling-metadata
make docs
make gen-context
make gen-interfaces
make gen-structure
make gen-providers
make gen-workflows
make update-claude-md
python3 scripts/check_contract_compatibility_gate.py --base origin/main --head HEAD
python3 scripts/generate_contract_review_packet.py --base origin/main --head HEAD --output artifacts/contract-review/<yyyy-mm-dd>/contract-review-packet.json --markdown-output artifacts/contract-review/<yyyy-mm-dd>/contract-review-packet.md
python3 -m unittest tests/scripts/test_check_contract_compatibility_gate.py
python3 -m unittest tests/scripts/test_generate_contract_review_packet.py
make docs-all
make skill-list
make skill-resources SKILL=meridian-code-review
make skill-scripts SKILL=meridian-code-review
make skill-chains SKILL=meridian-code-review
make skill-resource SKILL=meridian-code-review RESOURCE=project-stats
make skill-run SKILL=meridian-code-review SCRIPT=validate-skill
make skill-chain SKILL=meridian-code-review SCRIPTS="validate-skill run-eval"
make skill-run-chain SKILL=meridian-code-review CHAIN=full-check
make skill-validate
make skill-run-eval RUNS=3
make skill-benchmark WORKSPACE=<dir>
make skill-discover
make health
make status
make app-metrics
make version
dotnet run --project src/Meridian/Meridian.csproj -- --quick-check
dotnet run --project src/Meridian/Meridian.csproj -- --test-connectivity
dotnet run --project src/Meridian/Meridian.csproj -- --validate-credentials
dotnet run --project src/Meridian/Meridian.csproj -- --error-codes
python3 build/scripts/ai-repo-updater.py known-errors
python3 build/scripts/docs/run-docs-automation.py --profile quick --dry-run
python3 build/scripts/docs/run-docs-automation.py --profile core --summary-output docs/status/docs-automation-summary.md
python3 build/scripts/docs/generate-ai-navigation.py --json-output docs/ai/generated/repo-navigation.json --markdown-output docs/ai/generated/repo-navigation.md --summary
```

Run `scripts/check_contract_compatibility_gate.py` when changing scoped contracts, including
`src/Meridian.Contracts/Api/UiApiRoutes.cs`; shared route removals or value changes require
migration notes in `docs/status/contract-compatibility-matrix.md`.
Run `scripts/generate_contract_review_packet.py` before weekly shared-interop reviews when scoped
contracts change; attach the JSON/Markdown packet and record the owner decision.
`scripts/dev/cleanup-generated.ps1` previews generated build/test output cleanup by default; add
`-Execute` only after reviewing the listed untracked directories.

TODO: `make doctor-fix` exists, but current `make/diagnostics.mk` says auto-fix is not yet
implemented and only delegates to `buildctl doctor`. Do not advertise it as a fix workflow until
that changes.

TODO: `make ai-docs-archive-execute` exists, but it moves stale docs. Run `make ai-docs-archive`
and inspect the preview before using it in automated cleanup.

Do not add package versions directly to project files. Central package management lives in
`Directory.Packages.props`.
