# Getting Started

Quick start guide for the Meridian. For comprehensive documentation, see [HELP.md](../HELP.md).

## Prerequisites

- .NET 9.0 SDK ([download](https://dotnet.microsoft.com/download/dotnet/9.0))
- At least one data provider account (see [Provider Setup](#provider-setup) below)

## Fastest Setup

```bash
# Clone and build
git clone <repository-url>
cd Meridian
dotnet build

# Run the interactive wizard
dotnet run --project src/Meridian/Meridian.csproj -- --wizard
```

The wizard guides you through provider selection, symbol configuration, and storage setup.

## Provider Setup

You need at least one data provider. Choose based on your needs:

| Provider | Free Tier | Setup Guide | Best For |
|----------|-----------|-------------|----------|
| **Alpaca** | Yes (with account) | [Alpaca Setup](../providers/alpaca-setup.md) | Easiest to start, real-time US equities |
| **Interactive Brokers** | Yes (with account) | [IB Setup](../providers/interactive-brokers-setup.md) | Full L2 depth, options, broad coverage |
| **Polygon** | Limited | [Provider Comparison](../providers/provider-comparison.md) | High-quality aggregated data |
| **StockSharp** | Yes (with account) | [Data Sources](../providers/data-sources.md) | 90+ exchange connectors |

Set credentials via environment variables (never in config files):

```bash
# Example: Alpaca credentials
export ALPACA__KEYID=your-key-id
export ALPACA__SECRETKEY=your-secret-key
```

See [Environment Variables](../reference/environment-variables.md) for the full list.

## Alternative Setup Methods

| Method | Command | Best For |
|--------|---------|----------|
| **Configuration Wizard** | `--wizard` | New users, interactive setup |
| **Auto-Configuration** | `--auto-config` | Users with env vars already set |
| **Web Dashboard** | `--mode web` | Visual configuration via browser |
| **Manual Config** | Edit `config/appsettings.json` | Power users |
| **Docker** | `docker compose up` | Containerized deployment |
| **Dry Run** | `--dry-run` | Validate config without starting |

## Validate Your Setup

Before starting data collection, validate that everything is configured correctly:

```bash
# Quick configuration health check
dotnet run --project src/Meridian/Meridian.csproj -- --quick-check

# Test connectivity to all configured providers
dotnet run --project src/Meridian/Meridian.csproj -- --test-connectivity

# Full validation without starting collection
dotnet run --project src/Meridian/Meridian.csproj -- --dry-run
```

## Start Collecting Data

```bash
# Web dashboard mode (recommended — opens at http://localhost:8080)
dotnet run --project src/Meridian/Meridian.csproj -- --mode web

# Headless mode (no UI, for servers)
dotnet run --project src/Meridian/Meridian.csproj -- --mode headless
```

## Where Data Is Stored

By default, collected data goes to the `data/` directory:

```
data/
├── live/           # Real-time streaming data (hot tier)
├── historical/     # Backfill data from historical providers
├── _wal/           # Write-ahead log for crash safety
└── _archive/       # Compressed archives (cold tier)
```

See [Storage Design](../architecture/storage-design.md) for details on tiered storage and file organization.

## Next Steps

1. **Backfill historical data**: [Backfill Guide](../providers/backfill-guide.md)
2. **Monitor data quality**: Check the Data Quality page in the web dashboard
3. **Export data**: [Portable Data Packager](../operations/portable-data-packager.md)
4. **Run backtests**: [Lean Integration](../integrations/lean-integration.md)
5. **Deploy to production**: [Deployment Guide](../operations/deployment.md)

## Quick Reference

- **[User Guide](../HELP.md)** — Complete reference for all features
- **[Configuration](../HELP.md#configuration)** — All configuration options
- **[Provider Comparison](../providers/provider-comparison.md)** — Feature comparison across providers
- **[Troubleshooting](../HELP.md#troubleshooting)** — Common issues and solutions
- **[FAQ](../HELP.md#faq)** — Frequently asked questions
- **[Architecture Overview](../architecture/overview.md)** — System design and data flow
