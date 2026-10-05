# Meridian Help

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05

This page keeps the high-traffic local operator and developer commands in one stable target for
docs links. For roadmap status and product direction, start with
`docs/product/meridian-design-document.md`, then use
[`docs/roadmap/README.md`](roadmap/README.md) and the registry-backed generated views.

## Command-line usage

Run these independent commands from the repository root in Bash or PowerShell with the SDK
from [global.json](../global.json). The normal workstation launch requires persistence and
operator authentication; complete [preflight](operators/preflight-checklist.md) first. For a
configured demo instead, use `--seed-demo`, then `--demo` to reopen it.

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --help
dotnet run --project src/Meridian/Meridian.csproj -- --setup
dotnet run --project src/Meridian/Meridian.csproj -- --mode workstation --http-port 8080
dotnet run --project src/Meridian/Meridian.csproj -- --selftest
dotnet run --project src/Meridian/Meridian.csproj -- --diagnostics
dotnet run --project src/Meridian/Meridian.csproj -- --validate-config
```

Both browser and Windows WPF workstations are active operator UI lanes. For browser development,
install dependencies from the lockfile and start Vite in its own terminal (Node.js 24):

```bash
npm --prefix src/Meridian.Ui/dashboard ci
npm --prefix src/Meridian.Ui/dashboard run dev
```

The dev server stays in the foreground. It serves `/workstation/` on port 5173 and proxies `/api`
to `MERIDIAN_API_BASE_URL`, or `http://localhost:8080` by default. Start the configured API host
in a separate terminal. Run tests or build assets from another terminal:

```bash
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
```

For Windows installation, use the [installer guide](operators/browser-workstation-installer.md)
for prerequisites, commands, expected installed paths, verification, and recovery. For WPF
source builds, use [desktop testing](development/desktop-testing-guide.md). A non-Windows stub
build does not validate the Windows UI.

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

Normal startup requires configured fund-account and fund-structure persistence. The
`MERIDIAN_USE_INMEMORY_GOVERNANCE` name is historical: its explicit non-production opt-in selects
file-backed governance stores. Other money-path stores still need PostgreSQL for durability.
It is refused in Production, the default when no environment is named. Use the
[environment reference](reference/environment-variables.md) for exact settings and precedence,
and [preflight](operators/preflight-checklist.md) to verify the resulting host.

For the WPF desktop launcher, use the explicit launch modes:

```powershell
pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Development
pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Production -BuildOnly
pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Production
```

`-LaunchMode Production` builds Release host and desktop artifacts and requires
`MERIDIAN_DATABASE_URL` (or `MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING` plus
`MERIDIAN_FUND_STRUCTURE_CONNECTION_STRING`) before the desktop-local host starts. `-BuildOnly` verifies the Release build without starting the host.
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

For browser-workstation issues, verify the running host in a second PowerShell terminal:

```powershell
Invoke-RestMethod http://localhost:8080/healthz
```

Expect an HTTP success response from the live host. Then follow the authenticated readiness
and operator-inbox requests in [preflight](operators/preflight-checklist.md); those routes need
operator scope. A successful health probe alone does not establish readiness. If connection
fails, inspect the host terminal and configured port. For 401/403, repair login and scope using
preflight before retrying.

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

#### `postgresql-schema-control`

- Owners: @storage-platform, @developer-experience
- Commands:
  - `python3 build/scripts/schema-control.py inventory --base-ref <baseline-sha>`
  - `python3 build/scripts/schema-control.py verify --database-url "$DATABASE_URL" --base-ref <baseline-sha>`
  - `gh workflow run schema-control.yml --ref <branch> -f mode=snapshot -f baseline_ref=<baseline-sha>`

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
