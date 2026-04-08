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

The desktop shell is update-safe by default:

- Config lives at `%LocalAppData%\Meridian\appsettings.json`
- Relative `DataRoot` values such as `data` resolve relative to that config location, not the executable folder
- Desktop catalog and health metadata also live alongside the external config instead of under the install directory
- Retained desktop state now follows the external roots as well: activity logs and collection session history write under the resolved `DataRoot`, symbol mapping persistence follows its configured path or a `DataRoot` fallback, and generated schema artifacts live under `%LocalAppData%\Meridian\_catalog\schemas`

On Linux/macOS, the WPF project remains in the solution as a CI-friendly stub build rather than a full desktop runtime.

### Discover commands

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --help
make help
```

## Verified CLI Workflows

<a id="command-line-usage"></a>

The current CLI argument surface is defined in `src/Meridian.Application/Commands/CliArguments.cs`, with command handlers in `src/Meridian.Application/Commands/`.

### Configuration and startup

<a id="configuration"></a>

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

For the WPF desktop host, startup now pins both the UI shell and the launched backend process to `%LocalAppData%\Meridian\appsettings.json`, and relative storage paths resolve from that external config root.

### Diagnostics

<a id="troubleshooting"></a>

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

<a id="analysis-ready-exports"></a>

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

## Configuration

Configuration lives in `config/` at the repository root. The primary config file is `config/appsettings.json`. Provider credentials and secrets use the secrets management pattern documented in [ADR-011](adr/011-centralized-configuration-and-credentials.md).

Desktop note:

- The installed WPF application does not use the repo-local `config/` directory at runtime
- It stores config under `%LocalAppData%\Meridian\appsettings.json`
- If `DataRoot` is omitted, Meridian uses `data` under the active config root
- Legacy desktop configs that still contain `Storage.BaseDirectory` are migrated to `DataRoot` on load
- Legacy desktop installs with app-folder `sessions.json`, `data/_logs/activity_log.json`, `data/_config/symbol-mappings.json`, or `_catalog/schemas/data_dictionary.json` are migrated into the external desktop locations on first use

```bash
# View current configuration
dotnet run --project src/Meridian/Meridian.csproj -- --show-config

# Validate provider credentials
dotnet run --project src/Meridian/Meridian.csproj -- --validate-credentials
```

See [Getting Started](getting-started/README.md) for initial setup steps and provider configuration.

## Troubleshooting

Common issues and resolutions:

- **Build failures:** Run `dotnet restore Meridian.sln` before building. Ensure .NET 9 SDK is installed.
- **Provider connectivity:** Run `dotnet run --project src/Meridian/Meridian.csproj -- --selftest` to validate connectivity.
- **Missing configuration:** Confirm `config/appsettings.json` exists and contains valid provider entries.
- **WPF test failures on Linux/macOS:** WPF tests require Windows. Use `/p:EnableWindowsTargeting=true` or skip with `--filter "Category!=WPF"`.
- **Test isolation failures:** Each test must own its data; see [Desktop Testing Guide](development/desktop-testing-guide.md).

For deeper diagnostics run `dotnet run ... -- --diagnostics`.

## FAQ

**Q: Which provider should I use for equities data?**
See [Provider Comparison](providers/provider-comparison.md) for a feature matrix.

**Q: How do I add a new data provider?**
Follow the [Provider Builder Guide](ai/skills/README.md) or use the `meridian-provider-builder` skill.

**Q: Where do I find the current roadmap?**
See [ROADMAP.md](status/ROADMAP.md) for current delivery waves and priorities.

**Q: How do I run only tests for a single project?**
Use `dotnet test tests/<ProjectName>.Tests/<ProjectName>.Tests.csproj`.

## Command-Line Usage

Full CLI reference:

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --help
```

Common flags:

| Flag | Purpose |
|------|---------|
| `--dry-run` | Simulate operations without writing data |
| `--offline` | Run without live provider connections |
| `--selftest` | Run connectivity and config validation |
| `--diagnostics` | Extended diagnostic output |
| `--backfill` | Trigger historical data backfill |
| `--package` | Package data for export |
| `--check-config` | Validate configuration file |
| `--show-config` | Print the effective configuration |
| `--validate-credentials` | Test configured credentials |

## Analysis-Ready Exports

Meridian can export data in analysis-ready formats using the package command:

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --package --package-name my-export
dotnet run --project src/Meridian/Meridian.csproj -- --package --package-format csv --package-symbols AAPL,MSFT
```

See [Portable Data Packager](operations/portable-data-packager.md) for full export options including CSV, Parquet, and ZIP bundle formats.

## Notes

- This document is intentionally grounded in the local codebase rather than aspirational feature copy.
- Some built-in `--help` topic text in the application still contains older wording. When in doubt, prefer the command handlers in `src/Meridian.Application/Commands/` and the typed flags in `src/Meridian.Application/Commands/CliArguments.cs`.
