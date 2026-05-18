# Meridian Help

**Last Reviewed:** 2026-05-13

This page keeps the high-traffic local operator and developer commands in one stable target for
docs links. For roadmap status and product direction, use [`status/ROADMAP.md`](status/ROADMAP.md)
and [`status/FEATURE_INVENTORY.md`](status/FEATURE_INVENTORY.md).

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

## Troubleshooting

Use the narrowest probe that matches the failure.

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --diagnostics
dotnet run --project src/Meridian/Meridian.csproj -- --selftest
dotnet run --project src/Meridian/Meridian.csproj -- --error-codes
python build/scripts/docs/run-docs-automation.py --profile quick --dry-run
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
