# Meridian Help

**Last Reviewed:** 2026-05-18

This page keeps the high-traffic local operator and developer commands in one stable target for
docs links. For roadmap status and product direction, start with
`docs/product/meridian-design-document.md`, then use
[`docs/roadmap/README.md`](roadmap/README.md) and the registry-backed generated views.

## Command-line usage

Run these from the repository root unless a command says otherwise.

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --help
dotnet run --project src/Meridian/Meridian.csproj -- --setup
dotnet run --project src/Meridian/Meridian.csproj -- --mode workstation --http-port 8080
dotnet run --project src/Meridian/Meridian.csproj -- --selftest
dotnet run --project src/Meridian/Meridian.csproj -- --diagnostics
dotnet run --project src/Meridian/Meridian.csproj -- --validate-config
```

The browser workstation is the active operator UI lane. During local development:

```bash
cd src/Meridian.Ui/dashboard
npm install
npm run dev
npm run test
npm run build
```

`npm run dev` serves the workstation under `/workstation/` and proxies `/api` to
`MERIDIAN_API_BASE_URL` when set, or `http://localhost:8080` by default.

To install the browser workstation as a local Windows app with Desktop and Start Menu shortcuts:

```powershell
.\build\scripts\install\install-web-workstation.ps1
.\build\scripts\install\install.ps1 -Mode WebWorkstation
```

The installed shortcut starts the local host and opens `http://localhost:8080/workstation/`.
It uses `--mode workstation`, which keeps provider connections and collector subscriptions
deferred until an operator action needs them.
For an end-to-end installed-copy smoke, run:

```powershell
.\build\scripts\install\smoke-web-workstation-install.ps1
```

## Configuration

Configuration path resolution uses this order:

1. `--config <path>`
2. `MDC_CONFIG_PATH`
3. `config/appsettings.json`

Useful probes:

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --setup
dotnet run --project src/Meridian/Meridian.csproj -- --show-config
dotnet run --project src/Meridian/Meridian.csproj -- --check-config
dotnet run --project src/Meridian/Meridian.csproj -- --validate-config
dotnet run --project src/Meridian/Meridian.csproj -- --detect-providers
dotnet run --project src/Meridian/Meridian.csproj -- --recommend-providers
```

`--setup` and `--first-run` are friendly aliases for the existing `--quickstart` path. They
auto-detect provider credentials from the environment, generate a practical starter config, validate
credentials when keys are present, back up any existing `config/appsettings.json`, and save the new
config to `config/appsettings.json`.

Provider setup should stay paper-first by default. Use Settings in the browser workstation for
Alpaca paper-key verification and only test live endpoints after an explicit operator
acknowledgement.

## Analysis-ready exports

Use package and export commands when producing local analysis artifacts:

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --package --package-name market-data-archive
dotnet run --project src/Meridian/Meridian.csproj -- --package --package-symbols AAPL,MSFT --package-from 2025-01-01
dotnet run --project src/Meridian/Meridian.csproj -- --list-package ./packages/data.zip
dotnet run --project src/Meridian/Meridian.csproj -- --validate-package ./packages/data.zip
```

For ETL local-file workflows, use the `--etl-source-kind` and `--etl-source-path` arguments exposed
by `src/Meridian.Application/Commands/EtlCommands.cs`.


## Production-safe DI defaults

By default, production startup paths must use persistence-backed domain services.
In-memory governance/domain services are for local fixture/dev scenarios only and now require explicit opt-in:

```bash
DOTNET_ENVIRONMENT=Development
ASPNETCORE_ENVIRONMENT=Development
MERIDIAN_USE_INMEMORY_GOVERNANCE=true
```

When `DOTNET_ENVIRONMENT=Production` (or `ASPNETCORE_ENVIRONMENT=Production`), Meridian fails fast
if an in-memory governance profile is requested.

For the WPF desktop launcher, use the explicit launch modes:

```powershell
pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Development
pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Production -BuildOnly
pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Production
```

`-LaunchMode Production` builds Release host and desktop artifacts and requires
`MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING` plus `MERIDIAN_FUND_STRUCTURE_CONNECTION_STRING` before
the desktop-local host starts. `-BuildOnly` verifies the Release build without starting the host.
The WPF startup screen uses the same environment-backed operator credentials as the browser
workstation: prefer `MDC_USERS` with `passwordHash` values, or use `MDC_USERNAME` /
`MDC_PASSWORD_HASH` for a single local admin bootstrap. Production, packaged, and customer-build auth
fails closed when no user profile is configured.

## Troubleshooting

Use the narrowest probe that matches the failure.

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --diagnostics
dotnet run --project src/Meridian/Meridian.csproj -- --selftest
dotnet run --project src/Meridian/Meridian.csproj -- --error-codes
python3 build/scripts/docs/run-docs-automation.py --profile quick --dry-run
```

For browser-workstation issues, first verify the local host and route:

```powershell
Invoke-RestMethod http://localhost:8080/healthz
Invoke-RestMethod http://localhost:8080/api/workstation/trading/readiness
Invoke-RestMethod http://localhost:8080/api/workstation/operator/inbox
```

Known local-environment pitfalls:

- stale Vite preview or Node processes can lock built workstation assets during `npm run build`
- missing Playwright-managed browsers may require installed Chrome or Edge for smoke checks
- low free space on `C:` can break restore/build/test lanes before product code is at fault


## Workstation governance workflow references

For workstation governance lifecycle, approval/rejection/reopen guidance, and API route catalog, use the operator and reference lanes:

- [Operator preflight and cutover procedures](operators/preflight-checklist.md)
- [Fund-ops persistence and approval continuity controls](operators/fund-ops-persistence-cutover.md)
- [Workstation API governance route catalog](reference/api-reference.md)

<!-- BEGIN AUTO-GENERATED: WORKFLOW-MANIFEST-HELP -->
### Canonical Workflow Manifest (Generated)

The commands below are generated from `docs/status/workflow-manifest.json`.

#### `docs-automation-core`

- Owners: @platform-docs, @developer-experience
- Commands:
  - `python3 build/scripts/docs/run-docs-automation.py --profile core --summary-output docs/status/docs-automation-summary.md --json-output docs/status/docs-automation-summary.json`
  - `python3 build/scripts/docs/generate-workflow-manifest.py`

#### `desktop-screenshot-catalog`

- Owners: @desktop-shell, @operator-experience
- Commands:
  - `pwsh -File ./scripts/dev/run-desktop-workflow.ps1 -Workflow screenshot-catalog -ScreenshotDirectory docs/screenshots/desktop`
  - `pwsh -File ./scripts/dev/capture-desktop-screenshots.ps1 -SkipBuild -ProjectPath src/Meridian.Wpf/Meridian.Wpf.csproj -Configuration Release -Framework net10.0-windows10.0.19041.0`

#### `provider-validation-wave1`

- Owners: @provider-infra, @ops-readiness
- Commands:
  - `pwsh ./scripts/dev/run-wave1-provider-validation.ps1`
  - `pwsh ./scripts/dev/generate-dk1-pilot-parity-packet.ps1 -SummaryJsonPath artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.json`

#### `operator-inbox-route-validation`

- Owners: @desktop-shell, @api-workstation
- Commands:
  - `make desktop-test-operator-inbox-route`
  - `pwsh -File ./scripts/dev/validate-operator-inbox-route.ps1`

#### `provider-validation-evidence-bundle`

- Owners: @provider-infra, @ops-readiness
- Commands:
  - `pwsh ./scripts/dev/run-provider-validation-evidence-bundle.ps1`

#### `ibapi-smoke-build`

- Owners: @provider-infra, @desktop-shell
- Commands:
  - `pwsh ./scripts/dev/build-ibapi-smoke.ps1 -Configuration Release`

#### `wpf-route-validation-position-blotter`

- Owners: @desktop-shell, @api-workstation
- Commands:
  - `pwsh -File ./scripts/dev/validate-position-blotter-route.ps1`

#### `wpf-dev-loop-validation`

- Owners: @desktop-shell, @developer-experience
- Commands:
  - `pwsh ./scripts/dev/validate-wpf-dev.ps1 -Restore`

#### `targeted-test`

- Owners: @developer-experience, @ci-platform
- Commands:
  - `python build/scripts/ci/dispatch-targeted-test.py --ref <branch> --mode dotnet-filtered --dotnet-project tests/Meridian.Tests/Meridian.Tests.csproj --dotnet-filter="FullyQualifiedName~<TestClassOrMethod>" --wait`
  - `gh workflow run targeted-test.yml --ref <branch> -f mode=dotnet-filtered -f dotnet_project=tests/Meridian.Tests/Meridian.Tests.csproj -f dotnet_filter="FullyQualifiedName~<TestClassOrMethod>"`
  - `gh workflow run targeted-test.yml --ref <branch> -f mode=wpf-dev-loop -f runner=windows-latest -f dotnet_filter="FullyQualifiedName~DesktopWorkflowScriptTests"`

#### `robinhood-options-smoke`

- Owners: @desktop-shell, @provider-infra
- Commands:
  - `pwsh ./scripts/dev/robinhood-options-smoke.ps1 -Configuration Release`

#### `web-screenshot-capture`

- Owners: @operator-experience, @developer-experience
- Commands:
  - `node scripts/dev/capture-web-screenshots.mjs --output-dir docs/screenshots/web --config scripts/dev/web-screenshot-routes.json`

_Generated by `python3 build/scripts/docs/generate-workflow-manifest.py`._
<!-- END AUTO-GENERATED: WORKFLOW-MANIFEST-HELP -->


### Execution simulation

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --simulate-execution --symbols AAPL,MSFT --sim-from 2026-01-01 --sim-to 2026-01-31 --sim-window-start 09:30 --sim-window-end 16:00 --sim-output-dir ./artifacts/simulation/jan-2026
dotnet run --project src/Meridian/Meridian.csproj -- --simulate-execution --dry-run --symbols AAPL --sim-from 2026-01-01 --sim-to 2026-01-07
```

The simulation command writes `fill-tape.jsonl`, `order-lifecycle.jsonl`, `summary.json`, and `queue-diagnostics.jsonl` into the selected output directory. Simulation artifacts are labeled `isInferred: true`; `summary.json` includes confidence grade, fill rate, average slippage bps, and warnings, while `queue-diagnostics.jsonl` records displayed size, trade quantity, estimated queue-ahead, and inference reason per event.
