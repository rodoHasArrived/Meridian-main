# Provider Documentation

**Last Updated:** 2026-03-21  
**Owner:** Core Team  
**Scope:** Engineering / Product - Provider-Facing  
**Review Cadence:** When providers change APIs, tiers, or rate limits

---

## Purpose

This directory contains setup guides, comparison tables, and usage instructions for the providers currently supported by Meridian.

---

## Contents

### Setup Guides

| Document | Description |
|----------|-------------|
| [Alpaca Setup](alpaca-setup.md) | Alpaca Markets API key setup and configuration |
| [Interactive Brokers Setup](interactive-brokers-setup.md) | IB TWS / Gateway configuration |
| [IB Free Equity Reference](interactive-brokers-free-equity-reference.md) | IB free equity data availability reference |
| [Security Master Guide](security-master-guide.md) | Event-sourced golden record for securities across 14 asset classes |

### Selection And Inventory

| Document | Description |
|----------|-------------|
| [Provider Comparison](provider-comparison.md) | Side-by-side selection guidance for streaming, historical, and symbol-search providers |
| [Data Sources Reference](data-sources.md) | Current provider inventory and implementation locations |
| [Backfill Guide](backfill-guide.md) | Historical data backfill procedures |

---

## Supported Provider Families

### Streaming And Hybrid

| Provider | Notes |
|----------|-------|
| Alpaca | Easy entry point for US streaming workflows |
| Interactive Brokers | Broker-aligned professional setup with `IBAPI`; real TWS / Gateway access requires the official `IBApi` path |
| Polygon | Real-time trades, quotes, and aggregates |
| StockSharp | Connector-driven multi-exchange workflows with package- and adapter-specific runtime behavior |
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
| Interactive Brokers | Broker-aligned historical workflows once the official `IBApi` surface is enabled |
| StockSharp | Connector-dependent historical access shaped by the configured adapter and available package surfaces |
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

---

## Related

- [Environment Variables Reference](../reference/environment-variables.md) - Credential and configuration reference
- [Provider Implementation Guide](../development/provider-implementation.md) - Adding new providers
- [Historical Data Providers Evaluation](../evaluations/historical-data-providers-evaluation.md) - Evaluation and tradeoff notes
