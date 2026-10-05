# Why This Architecture (Non-Engineer Explainer)

## What this program does
This explainer covers Meridian's market-data ingestion and archival architecture. The broader
platform also owns operational-finance workflows; start with the [system overview](overview.md) and
[product charter](../product/meridian-design-document.md) for that scope. The data lane captures live
and historical events, validates them, and retains them for replay, research, and evidence-backed
operating workflows.

It collects:
- **Trades:** tick-by-tick prints with sequence checks and quality validation
- **Quotes:** best bid/offer (BBO) updates for context and spread health
- **Depth:** Level 2 order book updates with integrity checks
- **Backfill:** historical bars and supplemental data from multiple providers

Browser and WPF workstations consume shared services and APIs. Prometheus metrics and export
tooling support monitoring and downstream analysis.

---

## Why we split it into layers

### 1) Provider Adapters (the “translators”)
Each data provider speaks its own API and protocol. We isolate them so:
- providers can be swapped or added without touching the core logic
- failures or quirks in one feed don’t poison the whole system
- historical backfill can run independently of live capture

Examples include Interactive Brokers, Alpaca, and Polygon streaming adapters, historical/backfill
adapters, and OpenFIGI-based symbol resolution. Use the [Provider Capability Matrix](../reference/provider-capability-matrix.md)
for supported data surfaces and the [Provider Validation Matrix](../reference/provider-validation-matrix.md)
for evidence and promotion requirements; an adapter's presence does not establish live-provider readiness.

This approach is formally documented in [ADR-001: Provider Abstraction](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/adr/001-provider-abstraction.md).

### 2) Domain Logic (the “brains”)
This layer decides what the incoming data *means* and whether it’s valid:
- `TradeDataCollector` validates trade sequences and produces order-flow stats
- `MarketDepthCollector` maintains the order book and emits integrity events
- `QuoteCollector` tracks BBO state and quote context

Because this layer is provider-agnostic, it can be tested without a live feed. See [ADR-006: Domain Events Polymorphic Payload](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/adr/006-domain-events-polymorphic-payload.md) for the sealed-record wrapper design.

### 3) Application Services (the “conductor”)
This is the orchestration layer that wires everything together and exposes tooling:
- CLI modes for **wizard setup**, **auto-config**, and **credential validation**
- Subscription management and backfill scheduling
- Health checks, data quality checks, and alerting hooks
- HTTP status and local API server for desktop and metrics endpoints

### 4) Pipeline + Storage (the “transport and memory”)
All domain events flow through a bounded, backpressured pipeline to prevent runaway memory use:
- `EventPipeline` uses a bounded channel (default **100,000 events**) with drop policies ([ADR-013](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/adr/013-bounded-channel-policy.md))
- Storage sinks include **JSONL** and **Parquet** ([ADR-008](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/adr/008-multi-format-composite-storage.md))
- **Write-ahead logging (WAL)** for crash-safe persistence ([ADR-007](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/adr/007-write-ahead-log-durability.md))
- **Compression profiles**, **schema versioning**, **retention policies**, and **replay tooling**
- Export profiles for analytics (Python/R/Lean/SQL-friendly exports)

### 5) Presentation + Monitoring (the “eyes and control surfaces”)
The system exposes status and monitoring through:
- Desktop-local API host for status, Swagger, and workstation endpoints
- Prometheus metrics endpoint ([ADR-012](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/adr/012-monitoring-and-alerting-pipeline.md))
- Active browser workstation and native Windows WPF desktop app over shared operator services

---

## Why this is safer and more “institutional”
- **Audit-first storage:** append-only JSONL/Parquet with WAL for durability
- **Data quality enforcement:** integrity events, spread checks, timestamp checks, and tick-size validation
- **Provider isolation:** adapters can fail or be replaced without corrupting core logic
- **Backpressure protection:** bounded queues prevent memory runaway under load
- **Operational visibility:** live metrics, status endpoints, and desktop/local API surfaces

---

## Current capabilities (as implemented in this repo)

### Implemented today
- Streaming adapters and historical backfill with provider-specific capabilities, failover, and rate limiting
- Deterministic canonicalization: cross-provider symbol, condition code, and venue normalization
- Integrity event emission for trade sequences and order book consistency
- Quote-aware analytics (BBO context)
- Storage in JSONL and Parquet with retention, compression, and WAL
- Data replay and export tooling for downstream analysis
- Ingestion orchestration: unified job model, scheduled backfills, checkpoint/resume, deduplication
- Data quality monitoring with SLA enforcement, anomaly detection, and gap analysis
- Monitoring via Prometheus metrics, status JSON, and shared API, browser, and WPF surfaces
- QuantConnect Lean integration for backtesting

### Notes on provider maturity
The [Polygon streaming adapter](../../src/Meridian.Infrastructure/Adapters/Polygon/PolygonMarketDataClient.cs)
uses the WebSocket provider runtime and refuses connection without credentials; it does not substitute
synthetic events. Entitlements, credentials, and provider validation remain separate from implementation.

Maintenance check 2026-10-05: provider readiness wording and workstation scope were corrected from
current source and the owning matrices. The capability examples above are not a new acceptance run.

---

## Why monolithic over microservices?

The current decision is [ADR-017: Modular Operational Monolith](../adr/017-modular-operational-monolith.md).
The earlier microservices evaluation ([ADR-003](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/adr/003-microservices-decomposition.md)) provides historical context. Key reasons for the modular monolith:

- **Deployment simplicity**: A single process is easier to deploy, configure, and debug for research teams.
- **Latency**: In-process event routing via bounded channels avoids network serialization overhead.
- **Operational cost**: Microservices demand service mesh, distributed tracing, and container orchestration — overhead that is not justified by the current deployment model.
- **Shared state**: Collectors, pipeline, and storage share event models directly; splitting them would require contract duplication and version management.

The monolith supports browser, WPF, CLI, and API entrypoints over shared modules. Local workstation
and remote production API deployments have different binding and authentication policies; see the
[host source guide](../../src/Meridian/README.md).

---

**Version:** 1.7.0
**Last Updated:** 2026-04-09
**See Also:** [Architecture Overview](overview.md) | [Domains](domains.md) | [C4 Diagrams](c4-diagrams.md) | [ADR Index](../adr/README.md) | [Lean Integration](../integrations/lean-integration.md) | [Canonicalization Design](deterministic-canonicalization.md)
