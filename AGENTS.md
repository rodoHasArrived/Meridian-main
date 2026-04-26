# AGENTS.md

This file is a compatibility shim for agents that look for root `AGENTS.md`.
Keep it short and prefer the canonical Meridian guidance sources:

- `CLAUDE.md` for the full repository guide.
- `.codex/skills/_shared/project-context.md` for current Codex project context.
- `docs/HELP.md` for verified operator and developer CLI workflows.
- `docs/ai/navigation/README.md` for generated repo-navigation workflow guidance.
- `docs/development/desktop-workflow-automation.md` for scripted WPF workflow runs.
- `docs/status/provider-validation-matrix.md` for Wave 1 provider evidence gates.
- `docs/status/dk1-pilot-parity-runbook.md` for the DK1 provider parity packet workflow.
- `docs/status/kernel-readiness-dashboard.md` for DK gate status and operator sign-off.
- `docs/plans/paper-trading-cockpit-reliability-sprint.md` for the Wave 2 readiness contract.
- `README.md` for top-level onboarding and planning links.

## Current Direction

- Meridian is a .NET 9 fund-management and trading-platform codebase.
- `src/Meridian.Wpf/` is the primary operator shell. Prefer desktop-first workflow guidance.
- Keep top-level operator navigation to `Research`, `Trading`, `Data Operations`, and `Governance`.
- Use the narrowest validation command that covers the files changed.

## Command Discovery

```bash
make help
dotnet run --project src/Meridian/Meridian.csproj -- --help
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
dotnet run --project src/Meridian/Meridian.csproj -- --symbols-remove TSLA
dotnet run --project src/Meridian/Meridian.csproj -- --symbols-import symbols.csv
dotnet run --project src/Meridian/Meridian.csproj -- --symbols-export symbols.txt
dotnet run --project src/Meridian/Meridian.csproj -- --symbol-status AAPL
dotnet run --project src/Meridian/Meridian.csproj -- --query "last SPY"
dotnet run --project src/Meridian/Meridian.csproj -- --query "count SPY" --from 2026-01-01 --to 2026-01-31
dotnet run --project src/Meridian/Meridian.csproj -- --catalog symbols
dotnet run --project src/Meridian/Meridian.csproj -- --catalog search "AAPL trades 2025"
dotnet run --project src/Meridian/Meridian.csproj -- --catalog timeline --symbol AAPL
dotnet run --project src/Meridian/Meridian.csproj -- --backfill --backfill-symbols AAPL,MSFT --backfill-from 2025-01-01 --backfill-to 2025-12-31
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
and `--calibrate-provider-degradation`, but their prerequisites are specialized. Verify current
operator setup before adding short-form examples here.

TODO: `docs/HELP.md` includes `--package --package-format csv`, but `PackageCommands` currently
maps `--package-format` to zip, tar.gz/tgz, or 7z. Verify the intended CSV export workflow before
adding that as a standard package command.

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
make publish-windows
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
python3 build/python/cli/buildctl.py build --project Meridian.sln --configuration Release
python3 build/python/cli/buildctl.py build --project Meridian.sln --configuration Debug --verbosity quiet
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj --logger "console;verbosity=normal"
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~MapWorkstationEndpoints_TradingReadiness" --logger "console;verbosity=normal"
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~PaperSessionReplayTests" --logger "console;verbosity=normal"
dotnet test tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj /p:EnableWindowsTargeting=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.McpServer.Tests/Meridian.McpServer.Tests.csproj --logger "console;verbosity=normal"
dotnet test tests/Meridian.QuantScript.Tests/Meridian.QuantScript.Tests.csproj --logger "console;verbosity=normal"
```

For concurrent automation, use an isolation key so builds write under `artifacts/bin/<key>/`
and `artifacts/obj/<key>/` instead of shared project output folders:

```bash
python3 build/python/cli/buildctl.py build --project Meridian.sln --configuration Release --isolation-key automation-run
python3 build/python/cli/buildctl.py build --project src/Meridian.Wpf/Meridian.Wpf.csproj --configuration Release --full-wpf-build --isolation-key desktop-smoke
```

## Desktop Workflows

```powershell
pwsh ./scripts/dev/desktop-dev.ps1
pwsh ./scripts/dev/run-desktop.ps1
pwsh ./scripts/dev/run-desktop.ps1 -NoBuild
pwsh ./scripts/dev/run-desktop.ps1 -Fixture
dotnet run --project src/Meridian.Wpf/Meridian.Wpf.csproj -p:EnableFullWpfBuild=true
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~TradingWorkspaceShellPageTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~ResearchWorkspaceShellPageTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
pwsh -File ./scripts/dev/run-desktop-workflow.ps1 -Workflow debug-startup
pwsh -File ./scripts/dev/run-desktop-workflow.ps1 -Workflow debug-startup -NoFixture -ReuseExistingApp
pwsh -File ./scripts/dev/generate-desktop-user-manual.ps1
pwsh -File ./scripts/dev/capture-desktop-screenshots.ps1
pwsh -File ./scripts/dev/robinhood-options-smoke.ps1
```

The desktop launcher builds the local host and WPF shell when needed, starts the host on
`http://localhost:8080`, waits for `/healthz`, launches the shell, and stops the owned host
when the desktop exits.

Named workflow automation is defined in `scripts/dev/desktop-workflows.json`; it defaults to
fixture mode and writes run artifacts under `artifacts/desktop-workflows/`.

Use `run-desktop-workflow.ps1 -NoFixture -ReuseExistingApp` after launching
`run-desktop.ps1` when driving an already-open shell against live local services.
`robinhood-options-smoke.ps1` validates Robinhood setup and the options workflow with seeded
fixture state and writes artifacts under `artifacts/desktop-workflows/robinhood-options-smoke/`.
Use the focused `ResearchWorkspaceShellPageTests` and `TradingWorkspaceShellPageTests` filters
for WPF desk-briefing hero state changes before broadening to the full WPF test pass.

```bash
make desktop-build
make desktop-test
make desktop-test-position-blotter-route
```

`make desktop-test-position-blotter-route` runs
`scripts/dev/validate-position-blotter-route.ps1`, which builds and tests the focused WPF
position-blotter route slice with isolated output and writes validation artifacts under
`artifacts/wpf-validation/position-blotter-route/`.

TODO: `README.md` and `.codex/skills/_shared/project-context.md` mention `make desktop-run`,
but the current `make/desktop.mk` does not define that target. Use
`pwsh ./scripts/dev/run-desktop.ps1` unless the Make target is restored.

TODO: `docs/development/desktop-workflow-automation.md` mentions `make desktop-workflow`,
`make desktop-manual`, and `make desktop-screenshots`, but the current `make/desktop.mk` does not
define those targets. Use the PowerShell scripts directly unless the Make targets are added.

TODO: `scripts/dev/desktop-dev.ps1` still prints `make build-wpf`,
`make test-desktop-services`, and `make uwp-xaml-diagnose`, but the current `make/*.mk` files do
not define those targets. Prefer `make desktop-build`, `make desktop-test`, and
`pwsh ./scripts/dev/diagnose-uwp-xaml.ps1`.

## Paper Trading Readiness

Use these probes against a running local host when checking Wave 2 cockpit readiness or replay
evidence:

```powershell
Invoke-RestMethod http://localhost:8080/api/workstation/trading/readiness
Invoke-RestMethod http://localhost:8080/api/workstation/trading
Invoke-RestMethod http://localhost:8080/api/execution/sessions/<session-id>/replay
```

The readiness endpoint is `GET /api/workstation/trading/readiness`. The broader workstation
trading payload (`GET /api/workstation/trading`) embeds the same readiness data. The replay route,
`GET /api/execution/sessions/{sessionId}/replay`, verifies a paper session replay and writes
durable execution-audit evidence used to reconstruct replay readiness after restart.

## Provider Validation

```powershell
pwsh ./scripts/dev/run-wave1-provider-validation.ps1
pwsh ./scripts/dev/run-wave1-provider-validation.ps1 -OperatorSignoffPath artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-operator-signoff.json
pwsh ./scripts/dev/generate-dk1-pilot-parity-packet.ps1 -SummaryJsonPath artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.json
pwsh ./scripts/dev/generate-dk1-pilot-parity-packet.ps1 -SummaryJsonPath artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.json -OperatorSignoffPath artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-operator-signoff.json
pwsh ./scripts/dev/build-ibapi-smoke.ps1
```

This is the active Wave 1 gate for Alpaca, Robinhood, Yahoo, checkpoint reliability, and Parquet
proof. It writes summaries and DK1 parity packets under
`artifacts/provider-validation/_automation/<yyyy-mm-dd>/`.
`run-wave1-provider-validation.ps1` invokes `generate-dk1-pilot-parity-packet.ps1` when present;
run the packet generator directly only when rebuilding from an existing Wave 1 summary. Pass
`-OperatorSignoffPath` after owner review so the packet records machine-readable sign-off status. A
`ready-for-operator-review` DK1 packet still requires signed owner evidence before DK1 exit.
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
make verify-setup
make diagnose-build
make collect-debug
make collect-debug-minimal
make build-binlog
make validate-data
make ai-arch-check
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
make docs-all
make skill-list
make skill-resources SKILL=meridian-code-review
make skill-run SKILL=meridian-code-review SCRIPT=validate-skill
make skill-discover
make health
make status
make app-metrics
make version
dotnet run --project src/Meridian/Meridian.csproj -- --quick-check
dotnet run --project src/Meridian/Meridian.csproj -- --test-connectivity
dotnet run --project src/Meridian/Meridian.csproj -- --validate-credentials
dotnet run --project src/Meridian/Meridian.csproj -- --error-codes
python3 build/scripts/docs/generate-ai-navigation.py --json-output docs/ai/generated/repo-navigation.json --markdown-output docs/ai/generated/repo-navigation.md --summary
```

TODO: `make doctor-fix` exists, but current `make/diagnostics.mk` says auto-fix is not yet
implemented and only delegates to `buildctl doctor`. Do not advertise it as a fix workflow until
that changes.

Do not add package versions directly to project files. Central package management lives in
`Directory.Packages.props`.
