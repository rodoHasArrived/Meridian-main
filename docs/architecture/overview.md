# Meridian - System Architecture

**Last Updated:** 2026-05-22

Meridian is a modular operational-finance platform with shared ingestion, storage, replay,
backtesting, portfolio, ledger, reconciliation, and reporting modules. Active browser and WPF
workstations consume common contracts and services. The [design charter](../product/meridian-design-document.md)
owns product scope; [source documentation](../source/README.md) records implementation ownership,
and [Product](../product/README.md) routes delivery and release-readiness questions.

Maintenance check 2026-10-05: this overview's module and workstation framing was aligned with
current source ownership. Earlier roadmap language below does not establish release certification.

## Current Direction

The architecture now supports two connected product tracks:

1. **Evidence-backed operator delivery**
   `Data`, `Strategy`, `Trading`, `Portfolio`, `Accounting`, and `Reporting` workflows backed by shared run, portfolio, ledger, readiness, and evidence models.
2. **Middle- and back-office delivery**
   Security Master productization, account/entity support, multi-ledger views, trial balance, cash-flow modeling, reconciliation, investor reporting, and governed report generation.

This is an expansion of the existing architecture, not a replacement of it.

## Core Principles

- **Provider-agnostic design**: one abstraction layer for multiple market-data sources
- **Archival-first storage**: durable persistence with WAL, JSONL, Parquet, and export pipelines
- **Workflow-centric productization**: shared run, portfolio, ledger, governance, and reporting concepts across the product experience
- **Type-safe domain logic**: F# for correctness-heavy financial kernels and domain transforms
- **Operational visibility**: monitoring, diagnostics, replay, and quality scoring are first-class concerns

## Layered Architecture

### Presentation layer

- browser workstation dashboard
- WPF desktop application — active desktop workstation (co-equal UI lane); current focus is web-UI parity
- desktop-local API host and workstation API surfaces
- CLI and operator tooling

This layer should present workflows, not isolated technical pages. The current migration focus is on real workspace-first shells.

### Application layer

- host startup and dependency composition
- orchestration services
- run, portfolio, ledger, and governance read models
- scheduling, backfill, diagnostics, and export flows

This layer is primarily C# and acts as the product orchestration boundary.

### Domain layer

- market-event and validation logic
- strategy and run semantics
- portfolio and ledger rules
- reconciliation, projection, and policy rules in their owning domain modules

This is where Meridian should concentrate financial correctness and deterministic transformations.

### Storage and integration layer

- JSONL, Parquet, packaging, and export infrastructure
- provider adapters and resilience
- catalog, replay, and maintenance services
- Security Master persistence and supporting storage models

### Graceful shutdown

Graceful shutdown depends on the same cancellation tokens and write-ahead-log durability discussed in the storage design doc. Collector services drain their channels, flush pending writes, and close subscriptions before the WPF shell, CLI, or API surface stops accepting new connections. Controlled shutdown is coordinated by `EventPipelinePolicy`, `WriteAheadLog`, and dedicated drain logic so retries, metrics, and telemetry complete cleanly.

### Backpressure monitoring

Backpressure is monitored through the bounded channel policies, metrics exporters, and telemetry dashboards already part of the platform. `BoundedChannelPolicy` enforces per-provider limits, `EventPipelinePolicy` captures overload signals, and monitoring pipelines report queue depth, publish rate, and retry counts so operators see when producers are saturating consumers.

## Language Split

The current plan uses a deliberate C# / F# split:

- **F#** for accounting kernels, cash-flow rules, trial-balance math, reconciliation rules, projections, and policy evaluation
- **C#** for orchestration, DI, storage adapters, API/WPF surfaces, report workflow wiring, and application services

## Current Architectural Anchors

Examples of existing areas the plan builds on:

- `src/Meridian.Application/`
- `src/Meridian.Contracts/`
- `src/Meridian.Storage/`
- `src/Meridian.Ledger/`
- `src/Meridian.FSharp/`
- `src/Meridian.Wpf/`

Security Master, accounting, reconciliation, and reporting have implemented source owners. Extend
those shared contracts and services rather than creating per-workstation financial state.

## Planned Accounting And Governance-Control Expansion

This heading is retained for older links. The original expansion topics now span implemented
modules and further planned work; they are not all future capabilities. Consult the owning source
guides and roadmap rows for each bounded workflow:

- Security Master
- account and entity management foundations
- multi-ledger tracking
- trial balance
- cash-flow modeling
- reconciliation engine
- report generation and report packs
- investor reporting

Accounting and reporting use their canonical workspaces and shared product model; governance
controls remain policy and evidence workflows rather than a separate root workspace. Follow
[Financial Operations](../../src/Meridian.FinancialOperations/README.md),
[Ledger](../../src/Meridian.Ledger/README.md),
[Reporting](../../src/Meridian.Reporting/README.md), and the
[Module Map](module-map.md) for current ownership. The roadmap registry owns acceptance status.

## Related Documents

- [Trading Workstation Migration Blueprint (Archived)](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/plans/trading-workstation-migration-blueprint.md)
- [Stakeholder Product Charter](../product/meridian-design-document.md)
- [Current Direction and Status (Archived)](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/plans/current-direction-and-status.md)
- [Governance and Fund Operations Blueprint (Archived)](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/plans/governance-fund-ops-blueprint.md)
- [Project Roadmap](../roadmap/README.md)
- [Layer Boundaries](layer-boundaries.md)
- [Desktop Layers](desktop-layers.md)
