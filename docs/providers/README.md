# Provider Documentation

**Owner:** Core Team  
**Scope:** Engineering / Product — Provider-Facing  
**Review Cadence:** When providers change APIs, tiers, or rate limits

---

## Purpose

This directory contains setup guides, comparison tables, and usage instructions for the data providers supported by Meridian.

---

## What Belongs Here

- Provider-specific setup and credential configuration guides
- Provider feature comparison and selection guidance
- Historical data backfill procedures for specific providers
- Data source coverage and availability notes (e.g., IB free equity reference)

## What Does NOT Belong Here

- Generic architecture of the provider abstraction layer → use `architecture/`
- Provider implementation code patterns → use `development/provider-implementation.md`
- Evaluation and benchmarks comparing providers → use `evaluations/`

---

## Contents

### Setup Guides

| Document | Description |
|----------|-------------|
| [Alpaca Setup](alpaca-setup.md) | Alpaca Markets API key setup and configuration |
| [Interactive Brokers Setup](interactive-brokers-setup.md) | IB TWS / Gateway configuration |
| [IB Free Equity Reference](interactive-brokers-free-equity-reference.md) | IB free equity data availability reference |

### Backfill

| Document | Description |
|----------|-------------|
| [Backfill Guide](backfill-guide.md) | Historical data backfill procedures |

### Comparison & Selection

| Document | Description |
|----------|-------------|
| [Provider Comparison](provider-comparison.md) | Feature, cost, and data quality comparison |
| [Data Sources Overview](data-sources.md) | All supported data sources |

---

## Supported Providers

### Streaming (Real-Time)

| Provider | Type | Docs |
|----------|------|------|
| Interactive Brokers | TWS/Gateway WebSocket | [Setup](interactive-brokers-setup.md) |
| Alpaca | REST + WebSocket | [Setup](alpaca-setup.md) |
| Polygon | WebSocket | See [environment variables](../reference/environment-variables.md) |
| StockSharp | 90+ sources | See [configuration reference](../reference/environment-variables.md) |
| NYSE Direct | Hybrid | See [data sources](data-sources.md) |

### Historical (Backfill)

| Provider | Free Tier | Notes |
|----------|-----------|-------|
| Stooq | Yes | Equity daily bars |
| Yahoo Finance | Yes | Unofficial API |
| Tiingo | Yes | 500 req/hr |
| Alpaca | With account | Intraday + daily |
| Finnhub | Yes | 60 req/min |
| Alpha Vantage | Yes | 5 req/min free |
| Polygon | Limited | Full tier required for history |
| Interactive Brokers | With account | IB pacing rules apply |
| Nasdaq Data Link | Limited | QUANDL datasets |
| StockSharp | With account | Various datasets |

See [Backfill Guide](backfill-guide.md) for full backfill procedures.

---

## Related

- [Environment Variables Reference](../reference/environment-variables.md) — API key configuration
- [Provider Implementation Guide](../development/provider-implementation.md) — Adding new providers
- [Historical Data Providers Evaluation](../evaluations/historical-data-providers-evaluation.md) — Provider comparison analysis

---

*Provider documentation is updated whenever API capabilities or setup procedures change.*
