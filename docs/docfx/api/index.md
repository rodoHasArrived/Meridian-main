# API Reference

This section is automatically generated from the XML documentation comments in the Meridian source code by [DocFX](https://dotnet.github.io/docfx/).

## Namespaces

The Meridian platform is organized into focused assemblies. Browse the namespace tree on the left to explore the full public API.

| Assembly | Purpose |
|----------|---------|
| **Meridian.Contracts** | Shared DTOs, domain events, and interface contracts used across the platform |
| **Meridian.Core** | Core abstractions: configuration, exceptions, logging, and serialization |
| **Meridian.Domain** | Domain model: collectors, market event payloads, and publishers |
| **Meridian.ProviderSdk** | Provider SDK — implement `IMarketDataClient`, `IHistoricalDataProvider`, or `ISymbolSearchProvider` |
| **Meridian.Application** | Application services: pipeline, backfill orchestration, monitoring, and configuration |
| **Meridian.Infrastructure** | Concrete provider adapters (Alpaca, Polygon, IB, NYSE, StockSharp, …) |
| **Meridian.Infrastructure.CppTrader** | CppTrader native matching-engine host and replay |
| **Meridian.Storage** | Storage sinks (JSONL, Parquet), WAL, archival, export, and packaging |
| **Meridian.Execution** | Order management system, paper trading gateway, and brokerage adapter framework |
| **Meridian.Execution.Sdk** | Brokerage gateway interfaces and SDK models |
| **Meridian.Backtesting** | Tick-level strategy replay engine and fill models |
| **Meridian.Backtesting.Sdk** | Strategy SDK — implement `IBacktestStrategy` |
| **Meridian.Strategies** | Strategy lifecycle management and portfolio tracking services |
| **Meridian.Risk** | Risk validation rules (position limits, drawdown circuit breakers, order-rate throttle) |
| **Meridian.Ledger** | Double-entry ledger: accounts, journal entries, and snapshots |
| **Meridian.Ui.Services** | Shared UI service abstractions and base classes |
| **Meridian.Ui.Shared** | HTTP endpoint registration helpers and shared UI services |
| **Meridian.Mcp** | MCP server tools, resources, and prompts for AI tooling integration |
| **Meridian.McpServer** | Standalone MCP server for market data, backfill, and symbol tools |

## Key Extension Points

### Adding a streaming provider

Implement `IMarketDataClient` from `Meridian.ProviderSdk` and decorate your class with `[DataSource]`. See `AlpacaMarketDataClient` for a reference implementation.

### Adding a historical provider

Implement `IHistoricalDataProvider` (or extend `BaseHistoricalDataProvider`) from `Meridian.ProviderSdk`. See `PolygonHistoricalDataProvider` for a reference implementation.

### Writing a backtest strategy

Implement `IBacktestStrategy` from `Meridian.Backtesting.Sdk`. The engine calls `OnBarAsync` for each tick during replay.

### Risk rules

Implement `IRiskRule` from `Meridian.Risk` and register it in DI. The `CompositeRiskValidator` chains all registered rules.

## Rebuilding These Docs

```bash
# Install DocFX (once)
dotnet tool update -g docfx

# Full build — outputs to docs/_site/
docfx docs/docfx/docfx.json

# Serve locally with live preview
docfx docs/docfx/docfx.json --serve
```

> **Note:** The source code must build successfully before DocFX can extract XML documentation.
> Run `dotnet build Meridian.sln /p:EnableWindowsTargeting=true` first.
