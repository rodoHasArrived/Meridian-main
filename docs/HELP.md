# Meridian Help

Meridian is a multi-surface platform: the main CLI/host lives in `src/Meridian/`, the web/API surface lives in `src/Meridian.Ui/`, the dashboard frontend lives in `src/Meridian.Ui/dashboard/`, and the Windows desktop shell lives in `src/Meridian.Wpf/`.

This guide focuses on the repo entry points and CLI flows that are currently verified in code.

## Quick Start

### Web dashboard

Use the web surface for the primary cross-platform experience:

```bash
make run-ui
```

Or run the host directly:

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --http-port 8080
```

Open `http://localhost:8080`.

### Windows WPF desktop

Use the full desktop workstation shell on Windows:

```powershell
dotnet run --project src/Meridian.Wpf/Meridian.Wpf.csproj -p:EnableFullWpfBuild=true
```

On Linux/macOS, the WPF project remains in the solution as a CI-friendly stub build rather than a full desktop runtime.

### Discover commands

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --help
make help
```

## Verified CLI Workflows

The current CLI argument surface is defined in `src/Meridian.Application/Commands/CliArguments.cs`, with command handlers in `src/Meridian.Application/Commands/`.

### Configuration and startup

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --quickstart
dotnet run --project src/Meridian/Meridian.csproj -- --validate-config
dotnet run --project src/Meridian/Meridian.csproj -- --check-config
dotnet run --project src/Meridian/Meridian.csproj -- --show-config
dotnet run --project src/Meridian/Meridian.csproj -- --watch-config
dotnet run --project src/Meridian/Meridian.csproj -- --config config/appsettings.json
```

Also supported by the current command handlers:

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --wizard
dotnet run --project src/Meridian/Meridian.csproj -- --auto-config
dotnet run --project src/Meridian/Meridian.csproj -- --detect-providers
dotnet run --project src/Meridian/Meridian.csproj -- --generate-config --template minimal
dotnet run --project src/Meridian/Meridian.csproj -- --generate-config-schema --output config/appsettings.schema.json
dotnet run --project src/Meridian/Meridian.csproj -- --list-presets
dotnet run --project src/Meridian/Meridian.csproj -- --preset researcher
```

Configuration path resolution is currently:

1. `--config <path>`
2. `MDC_CONFIG_PATH`
3. `config/appsettings.json`
4. `appsettings.json`

### Diagnostics

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --quick-check
dotnet run --project src/Meridian/Meridian.csproj -- --test-connectivity
dotnet run --project src/Meridian/Meridian.csproj -- --validate-credentials
dotnet run --project src/Meridian/Meridian.csproj -- --error-codes
```

### Provider recommendation

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --recommend-providers
```

### Symbol management

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --symbols
dotnet run --project src/Meridian/Meridian.csproj -- --symbols-monitored
dotnet run --project src/Meridian/Meridian.csproj -- --symbols-archived
dotnet run --project src/Meridian/Meridian.csproj -- --symbols-add AAPL,MSFT
dotnet run --project src/Meridian/Meridian.csproj -- --symbols-add ES --depth-levels 20
dotnet run --project src/Meridian/Meridian.csproj -- --symbols-add SPY --no-depth
dotnet run --project src/Meridian/Meridian.csproj -- --symbols-remove TSLA
dotnet run --project src/Meridian/Meridian.csproj -- --symbol-status AAPL
```

### Historical backfill

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --backfill --backfill-symbols AAPL,MSFT --backfill-from 2025-01-01 --backfill-to 2025-12-31
dotnet run --project src/Meridian/Meridian.csproj -- --backfill --backfill-provider polygon --backfill-symbols SPY
dotnet run --project src/Meridian/Meridian.csproj -- --backfill --resume --backfill-symbols QQQ
```

### Package operations

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --package --package-name market-data-archive
dotnet run --project src/Meridian/Meridian.csproj -- --list-package ./packages/data.zip
dotnet run --project src/Meridian/Meridian.csproj -- --validate-package ./packages/data.zip
dotnet run --project src/Meridian/Meridian.csproj -- --import-package ./packages/data.zip
```

### Schema, replay, and validation-adjacent flags

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --validate-schemas
dotnet run --project src/Meridian/Meridian.csproj -- --strict-schemas
dotnet run --project src/Meridian/Meridian.csproj -- --dry-run
dotnet run --project src/Meridian/Meridian.csproj -- --offline
dotnet run --project src/Meridian/Meridian.csproj -- --selftest
```

## Build And Test

### Solution build

```bash
dotnet restore Meridian.sln
dotnet build Meridian.sln
```

### Focused test runs

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj
dotnet test tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj
dotnet test tests/Meridian.McpServer.Tests/Meridian.McpServer.Tests.csproj
dotnet test tests/Meridian.QuantScript.Tests/Meridian.QuantScript.Tests.csproj
```

### Frontend bundle

```bash
npm --prefix src/Meridian.Ui/dashboard install
npm --prefix src/Meridian.Ui/dashboard run build
npm --prefix src/Meridian.Ui/dashboard run test
```

## Key Paths

- `src/Meridian/` - main host executable
- `src/Meridian.Application/` - startup orchestration, commands, and application services
- `src/Meridian.Ui/` - web/API project
- `src/Meridian.Ui/dashboard/` - dashboard frontend assets
- `src/Meridian.Wpf/` - Windows WPF workstation shell
- `src/Meridian.Mcp/` and `src/Meridian.McpServer/` - MCP integrations
- `src/Meridian.QuantScript/` - QuantScript project
- `docs/README.md` - main documentation index
- `docs/status/ROADMAP.md` - current planning source
- `docs/status/FEATURE_INVENTORY.md` - capability inventory
- `docs/plans/` - active product and technical blueprints

## Notes

- This document is intentionally grounded in the local codebase rather than aspirational feature copy.
- Some built-in `--help` topic text in the application still contains older wording. When in doubt, prefer the command handlers in `src/Meridian.Application/Commands/` and the typed flags in `src/Meridian.Application/Commands/CliArguments.cs`.
