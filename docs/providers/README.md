# Provider Documentation

**Last Reviewed:** 2026-04-05
**Owner:** Core Team
**Scope:** Engineering / Product - Provider-Facing
**Review Cadence:** When providers change APIs, tiers, runtime behavior, or validation status

## Purpose

This directory contains setup guides, inventory references, connector notes, and validation context for the providers currently supported by Meridian.

## Contents

### Setup Guides

| Document | Description |
|----------|-------------|
| [Alpaca Setup](alpaca-setup.md) | Alpaca Markets API key setup and configuration |
| [Interactive Brokers Setup](interactive-brokers-setup.md) | IB TWS and Gateway configuration |
| [IB Free Equity Reference](interactive-brokers-free-equity-reference.md) | IB free-equity data availability reference |
| [Security Master Guide](security-master-guide.md) | Security Master coverage and golden-record guidance |

### Selection, Inventory, And Validation

| Document | Description |
|----------|-------------|
| [Provider Comparison](provider-comparison.md) | Side-by-side selection guidance for streaming, historical, and symbol-search providers |
| [Data Sources Reference](data-sources.md) | Current provider inventory and implementation locations |
| [Backfill Guide](backfill-guide.md) | Historical data backfill procedures |
| [Provider Confidence Baseline](provider-confidence-baseline.md) | Confidence narrative for the main provider families |
| [Provider Validation Matrix](../status/provider-validation-matrix.md) | Evidence-backed readiness matrix used by status docs |
| [StockSharp Connectors](stocksharp-connectors.md) | Connector-specific StockSharp setup and runtime notes |

## Supported Provider Families

### Streaming And Hybrid

| Provider | Notes |
|----------|-------|
| Alpaca | Easy entry point for US streaming workflows |
| Interactive Brokers | Broker-aligned professional setup with `IBApi`-aware workflows |
| Polygon | High-quality trades, quotes, and aggregates |
| StockSharp | Connector-driven multi-exchange workflows with adapter-specific runtime behavior |
| NYSE Streaming | NYSE-focused feed path |
| Synthetic Market Data | Deterministic offline streaming and demos |

### Historical

| Provider | Notes |
|----------|-------|
| Alpaca | Recent US history with credentials |
| Yahoo Finance | Broad no-auth fallback |
| Stooq | Free daily fallback |
| Nasdaq Data Link | Alternative data and macro datasets |
| Tiingo | Adjusted data and corporate actions |
| Finnhub | Historical plus company reference data |
| Alpha Vantage | Narrow, rate-limited intraday lookups |
| Polygon | Premium-quality historical market data |
| Interactive Brokers | Broker-aligned historical workflows once the official `IBApi` path is available |
| StockSharp | Connector-dependent historical access shaped by the configured adapter and package surface |
| Twelve Data | Credentialed international OHLCV fallback |
| FRED | Economic time series mapped to daily bars |
| Synthetic Historical | Deterministic offline fixtures |
| Composite Provider | Multi-source failover orchestration |

### Symbol Search And Reference

| Provider | Notes |
|----------|-------|
| Alpaca Symbol Search | US equities lookup |
| Finnhub Symbol Search | Global security search |
| Polygon Symbol Search | Filterable symbol search |
| OpenFIGI | Identifier normalization and FIGI mapping |
| StockSharp Symbol Search | Connector-native security lookup |
| Synthetic Symbol Search | Offline stock and ETF catalog |

## Related

- [Environment Variables](../reference/environment-variables.md)
- [Provider Implementation Guide](../development/provider-implementation.md)
- [Historical Data Providers Evaluation](../evaluations/historical-data-providers-evaluation.md)
- [Production Status](../status/production-status.md)
