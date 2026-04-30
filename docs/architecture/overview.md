# Meridian - System Architecture

**Last Updated:** 2026-04-29

Meridian is a modular, event-driven trading platform that is being productized as an evidence-backed investment operations system. The architecture already supports ingestion, storage, replay, backtesting, export, portfolio, ledger, retained desktop workflows, and a browser dashboard. The current roadmap extends that baseline into a connected product where trusted data, research, paper validation, books, reconciliation, approvals, and governed reports share one evidence chain.

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
- WPF desktop application retained for compatibility and regression support
- desktop-local API host and retained workstation API surfaces
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
- future reconciliation, projection, and policy logic

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

Security Master already has meaningful anchors in contracts, application, storage, migrations, and F# domain modules. The governance plan layers new workstation-facing services and projections on top of those existing modules rather than introducing a separate subsystem.

## Planned Governance Expansion

The active governance blueprint adds first-class product workflows for:

- Security Master
- account and entity management foundations
- multi-ledger tracking
- trial balance
- cash-flow modeling
- reconciliation engine
- report generation and report packs
- investor reporting

These capabilities are intended to live inside the existing Governance workspace and shared product model, not as an unrelated side application.

## Related Documents

- [Trading Workstation Migration Blueprint](../plans/trading-workstation-migration-blueprint.md)
- [Evidence-Backed Investment Operations Plan](../plans/evidence-backed-investment-operations-plan.md)
- [Governance and Fund Operations Blueprint](../plans/governance-fund-ops-blueprint.md)
- [Project Roadmap](../status/ROADMAP.md)
- [Layer Boundaries](layer-boundaries.md)
- [Desktop Layers](desktop-layers.md)
