# Getting Started

**Last Reviewed:** 2026-04-05

This guide covers the fastest verified ways to get Meridian running locally. For the broader operator reference, see [../HELP.md](../HELP.md).

## Prerequisites

- .NET 9 SDK
- Git
- One configured data provider if you want live or credentialed historical data

## Fastest Local Setup

If you have `make` available:

```bash
git clone https://github.com/rodoHasArrived/Meridian-main.git
cd Meridian-main
make setup-dev
dotnet run --project src/Meridian/Meridian.csproj -- --quickstart
```

If you prefer the plain .NET path:

```bash
git clone https://github.com/rodoHasArrived/Meridian-main.git
cd Meridian-main
dotnet restore
dotnet build Meridian.sln
dotnet run --project src/Meridian/Meridian.csproj -- --quickstart
```

`--quickstart` is the fastest verified first-run path. It delegates to the configuration pipeline and prepares the app for a `--mode desktop` launch.

## Other Setup Paths

| Goal | Command | Notes |
|------|---------|-------|
| Interactive setup | `dotnet run --project src/Meridian/Meridian.csproj -- --wizard` | Step-by-step configuration flow |
| Auto-config from env vars | `dotnet run --project src/Meridian/Meridian.csproj -- --auto-config` | Best when provider credentials are already set |
| Validate config only | `dotnet run --project src/Meridian/Meridian.csproj -- --validate-config` | No long-running host |
| Fast health check | `dotnet run --project src/Meridian/Meridian.csproj -- --quick-check` | Quick configuration/status validation |
| Connectivity test | `dotnet run --project src/Meridian/Meridian.csproj -- --test-connectivity` | Verifies configured provider connections |
| Full dry run | `dotnet run --project src/Meridian/Meridian.csproj -- --dry-run` | Validation without starting collection |

## Choose A Launch Mode

### Desktop-local API host

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --mode desktop --http-port 8080
```

You can also use:

```bash
make run
```

### Headless collector

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --mode headless
```

### Windows WPF desktop shell

```powershell
pwsh ./scripts/dev/run-desktop.ps1
```

This launcher is the recommended Windows desktop path. It starts the local Meridian host on `http://localhost:8080` when needed, waits for it to pass `/healthz`, then opens the WPF shell. If a `Meridian.Desktop.exe` from this same workspace is already running, the launcher closes it before rebuilding so the WPF binaries are not left locked, and it tears down any host process it started after the desktop shell exits.

Manual fallback:

```powershell
dotnet run --project src/Meridian.Wpf/Meridian.Wpf.csproj /p:EnableFullWpfBuild=true
```

The WPF shell is the Windows-only desktop workstation path and the primary interactive operator surface.

## Provider Setup

Choose a provider path based on what you need:

| Provider | Best For | Setup Guide |
|----------|----------|-------------|
| Alpaca | Easiest credentialed starting point for US streaming and recent history | [../providers/alpaca-setup.md](../providers/alpaca-setup.md) |
| Interactive Brokers | Broker-aligned workflows, options, and deeper market coverage | [../providers/interactive-brokers-setup.md](../providers/interactive-brokers-setup.md) |
| Polygon | Strong market-data quality and research/trading workflows | [../providers/provider-comparison.md](../providers/provider-comparison.md) |
| StockSharp | Connector-driven multi-exchange setups | [../providers/stocksharp-connectors.md](../providers/stocksharp-connectors.md) |

For the broader provider inventory and tradeoffs, see [../providers/README.md](../providers/README.md) and [../providers/data-sources.md](../providers/data-sources.md).

## Validate Your Setup

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --validate-config
dotnet run --project src/Meridian/Meridian.csproj -- --quick-check
dotnet run --project src/Meridian/Meridian.csproj -- --test-connectivity
dotnet run --project src/Meridian/Meridian.csproj -- --dry-run
```

## Common Next Steps

1. [Pilot Operator Quickstart](pilot-operator-quickstart.md)
2. [Backfill Guide](../providers/backfill-guide.md)
3. [Operator Runbook](../operations/operator-runbook.md)
4. [Environment Variables](../reference/environment-variables.md)
5. [Generated Configuration Schema](../generated/configuration-schema.md)
6. [Deployment Guide](../operations/deployment.md)

## Quick Reference

- [../HELP.md](../HELP.md)
- [../providers/provider-comparison.md](../providers/provider-comparison.md)
- [../operations/preflight-checklist.md](../operations/preflight-checklist.md)
- [../architecture/overview.md](../architecture/overview.md)
