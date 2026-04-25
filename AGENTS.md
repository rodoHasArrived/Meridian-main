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

## Run And Host Workflows

```bash
make setup-dev
make quickstart
make run
make run-backfill SYMBOLS=AAPL,MSFT
make run-selftest
dotnet run --project src/Meridian/Meridian.csproj -- --quickstart
dotnet run --project src/Meridian/Meridian.csproj -- --validate-config
dotnet run --project src/Meridian/Meridian.csproj -- --check-config
dotnet run --project src/Meridian/Meridian.csproj -- --show-config
dotnet run --project src/Meridian/Meridian.csproj -- --watch-config
dotnet run --project src/Meridian/Meridian.csproj -- --recommend-providers
dotnet run --project src/Meridian/Meridian.csproj -- --mode desktop --http-port 8080
```

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
make format-check
make test
make test-unit
make test-fsharp
make test-integration
make test-all
make pre-pr
make pre-pr-full
make watch
make watch-build
make benchmark
make bench-quick
make bench-filter FILTER=*Collector*
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
python3 build/python/cli/buildctl.py build --project Meridian.sln --configuration Release
python3 build/python/cli/buildctl.py build --project Meridian.sln --configuration Debug --verbosity quiet
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj --logger "console;verbosity=normal"
```

For concurrent automation, use an isolation key so builds write under `artifacts/bin/<key>/`
and `artifacts/obj/<key>/` instead of shared project output folders:

```bash
python3 build/python/cli/buildctl.py build --project Meridian.sln --configuration Release --isolation-key automation-run
```

## Desktop Workflows

```powershell
pwsh ./scripts/dev/desktop-dev.ps1
pwsh ./scripts/dev/run-desktop.ps1
pwsh ./scripts/dev/run-desktop.ps1 -NoBuild
pwsh ./scripts/dev/run-desktop.ps1 -Fixture
dotnet run --project src/Meridian.Wpf/Meridian.Wpf.csproj -p:EnableFullWpfBuild=true
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

## Provider Validation

```powershell
pwsh ./scripts/dev/run-wave1-provider-validation.ps1
pwsh ./scripts/dev/generate-dk1-pilot-parity-packet.ps1 -SummaryJsonPath artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.json
pwsh ./scripts/dev/build-ibapi-smoke.ps1
```

This is the active Wave 1 gate for Alpaca, Robinhood, Yahoo, checkpoint reliability, and Parquet
proof. It writes summaries and DK1 parity packets under
`artifacts/provider-validation/_automation/<yyyy-mm-dd>/`.
`run-wave1-provider-validation.ps1` invokes `generate-dk1-pilot-parity-packet.ps1` when present;
run the packet generator directly only when rebuilding from an existing Wave 1 summary. A
`ready-for-operator-review` DK1 packet still requires operator sign-off before DK1 exit.
`build-ibapi-smoke.ps1` is a compile-only Interactive Brokers adapter smoke build that enables
`EnableIbApiSmoke=true` on `src/Meridian.Infrastructure/Meridian.Infrastructure.csproj`.

## Diagnostics And Docs

```bash
make doctor
make doctor-quick
make verify-setup
make collect-debug
make validate-data
make ai-arch-check
make ai-docs-freshness
make ai-docs-drift
make ai-docs-sync-report
make verify-adrs
make verify-tooling-metadata
make docs-all
make skill-list
make skill-discover
dotnet run --project src/Meridian/Meridian.csproj -- --quick-check
dotnet run --project src/Meridian/Meridian.csproj -- --test-connectivity
dotnet run --project src/Meridian/Meridian.csproj -- --validate-credentials
dotnet run --project src/Meridian/Meridian.csproj -- --error-codes
python3 build/scripts/docs/generate-ai-navigation.py --json-output docs/ai/generated/repo-navigation.json --markdown-output docs/ai/generated/repo-navigation.md --summary
```

Do not add package versions directly to project files. Central package management lives in
`Directory.Packages.props`.
