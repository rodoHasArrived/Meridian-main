# Why This Architecture (Non-Engineer Explainer)

## What this program does
Meridian captures **live** and **historical** market microstructure data, validates it for quality, and stores it in audit-friendly formats so it can be replayed, analyzed, or fed into research tools.

It collects:
- **Trades:** tick-by-tick prints with sequence checks and quality validation
- **Quotes:** best bid/offer (BBO) updates for context and spread health
- **Depth:** Level 2 order book updates with integrity checks
- **Backfill:** historical bars and supplemental data from multiple providers

It also provides **monitoring dashboards**, **Prometheus metrics**, and **export tooling** for downstream analysis.

---

## Why we split it into layers

### 1) Provider Adapters (the “translators”)
Each data provider speaks its own API and protocol. We isolate them so:
- providers can be swapped or added without touching the core logic
- failures or quirks in one feed don’t poison the whole system
- historical backfill can run independently of live capture

Examples of current adapters:
- **Live providers:** Interactive Brokers, Alpaca, NYSE Direct, StockSharp (Polygon streaming is stub-only; historical data is fully functional)
- **Historical/backfill providers:** Alpaca, Yahoo Finance, Stooq, Tiingo, Finnhub, Alpha Vantage, Nasdaq Data Link, Polygon, Interactive Brokers
- **Symbol resolution:** OpenFIGI-based resolution for cross-provider symbol mapping, plus Alpaca, Finnhub, and Polygon search providers

This approach is formally documented in [ADR-001: Provider Abstraction](../adr/001-provider-abstraction.md).

### 2) Domain Logic (the “brains”)
This layer decides what the incoming data *means* and whether it’s valid:
- `TradeDataCollector` validates trade sequences and produces order-flow stats
- `MarketDepthCollector` maintains the order book and emits integrity events
- `QuoteCollector` tracks BBO state and quote context

Because this layer is provider-agnostic, it can be tested without a live feed. See [ADR-006: Domain Events Polymorphic Payload](../adr/006-domain-events-polymorphic-payload.md) for the sealed-record wrapper design.

### 3) Application Services (the “conductor”)
This is the orchestration layer that wires everything together and exposes tooling:
- CLI modes for **wizard setup**, **auto-config**, and **credential validation**
- Subscription management and backfill scheduling
- Health checks, data quality checks, and alerting hooks
- HTTP status server for dashboard and metrics endpoints

### 4) Pipeline + Storage (the “transport and memory”)
All domain events flow through a bounded, backpressured pipeline to prevent runaway memory use:
- `EventPipeline` uses a bounded channel (default **100,000 events**) with drop policies ([ADR-013](../adr/013-bounded-channel-policy.md))
- Storage sinks include **JSONL** and **Parquet** ([ADR-008](../adr/008-multi-format-composite-storage.md))
- **Write-ahead logging (WAL)** for crash-safe persistence ([ADR-007](../adr/007-write-ahead-log-durability.md))
- **Compression profiles**, **schema versioning**, **retention policies**, and **replay tooling**
- Export profiles for analytics (Python/R/Lean/SQL-friendly exports)

### 5) Presentation + Monitoring (the “eyes and dashboard”)
The system exposes status and monitoring through:
- Web dashboard (HTTP status server + UI)
- Prometheus metrics endpoint ([ADR-012](../adr/012-monitoring-and-alerting-pipeline.md))
- Native Windows WPF desktop app for monitoring and configuration

---

## Why this is safer and more “institutional”
- **Audit-first storage:** append-only JSONL/Parquet with WAL for durability
- **Data quality enforcement:** integrity events, spread checks, timestamp checks, and tick-size validation
- **Provider isolation:** adapters can fail or be replaced without corrupting core logic
- **Backpressure protection:** bounded queues prevent memory runaway under load
- **Operational visibility:** live metrics, status endpoints, and UI dashboards

---

## Current capabilities (as implemented in this repo)

### Implemented today
- Live capture from IB, Alpaca, NYSE Direct, StockSharp (Polygon streaming adapter is stub-only; Polygon historical data is fully functional)
- Historical backfill from 10 providers with automatic failover chain and rate limiting
- Deterministic canonicalization: cross-provider symbol, condition code, and venue normalization
- Integrity event emission for trade sequences and order book consistency
- Quote-aware analytics (BBO context)
- Storage in JSONL and Parquet with retention, compression, and WAL
- Data replay and export tooling for downstream analysis
- Ingestion orchestration: unified job model, scheduled backfills, checkpoint/resume, deduplication
- Data quality monitoring with SLA enforcement, anomaly detection, and gap analysis
- Monitoring via Prometheus metrics, status JSON, and web/WPF dashboards
- QuantConnect Lean integration for backtesting

### Notes on provider maturity
- Polygon streaming is currently **stub-only** (synthetic events). The Polygon historical data provider is fully functional.

---

## Why monolithic over microservices?

We evaluated a microservices decomposition ([ADR-003](../adr/003-microservices-decomposition.md)) and rejected it. Key reasons:

- **Deployment simplicity**: A single process is easier to deploy, configure, and debug for research teams.
- **Latency**: In-process event routing via bounded channels avoids network serialization overhead.
- **Operational cost**: Microservices demand service mesh, distributed tracing, and container orchestration — overhead that doesn't justify the scale of a meridian.
- **Shared state**: Collectors, pipeline, and storage share event models directly; splitting them would require contract duplication and version management.

The monolith supports optional UI projects (web dashboard, WPF desktop) as separate entry points that compose the same library assemblies.

---

**Version:** 1.7.0
**Last Updated:** 2026-03-18
**See Also:** [Architecture Overview](overview.md) | [Domains](domains.md) | [C4 Diagrams](c4-diagrams.md) | [ADR Index](../adr/README.md) | [Lean Integration](../integrations/lean-integration.md) | [Canonicalization Design](deterministic-canonicalization.md)
