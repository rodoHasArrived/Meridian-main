---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-DOMAIN
path: src/Meridian.Domain
status: active
owner_lane: Data Confidence and Validation
last_reviewed: 2026-07-21
---

# src/Meridian.Domain

## Purpose

Domain contains market-data collectors, domain events, models, reconciliation concepts, and telemetry primitives used by higher-level workflows.

## Layer responsibility

This layer owns domain concepts without depending on application orchestration, infrastructure adapters, or UI surfaces.

## Key folders and files

- `Collectors/` - market data and ingestion collectors.
- `Events/` - domain event shapes and event-flow contracts, including the quarantine sink port
  used by canonicalization and pipeline dead-letter retention.
- `Models/` - core domain models.
- `Reconciliation/` and `Telemetry/` - domain-level reconciliation and observability types,
  including durable reconciliation cases with owner, SLA, comments, attachments, disposition,
  aging, and break-explanation metadata.

## Important workflows

Use this module for domain behavior that should remain stable across providers, storage, execution, and UI projections.

## API contract notes

- Statement reconciliation domain models include typed normalized positions, cash balances, transactions, security references, and source-row references. Each normalized entity carries `StatementRunId`, `SourceRowNumber`, `SourceRowHash`, and `RawSnapshot` traceability fields so downstream reconciliation evidence can be tied back to the raw statement line.
- Statement break and paired-case models carry optimistic `Version` values plus terminal disposition,
  authenticated actor, rationale, evidence, transaction, successor, and timestamp metadata. Case
  audit events retain transaction/version context and previous/current hashes so higher layers can
  expose a verifiable decision history without moving authentication or persistence policy into the
  Domain layer.
- `IEventQuarantineSink` is the domain event-flow port for retaining market events that require
  quarantine or dead-letter review. Application supplies the concrete dead-letter implementation;
  Data Integration can depend on the port while keeping canonicalization free of Application
  storage dependencies.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-DOMAIN -->
| Roadmap item | Title |
| --- | --- |
| `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-DOMAIN -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
```

## Change rules

Domain code must not reference Application or Infrastructure. Shared DTOs that cross project boundaries belong in `src/Meridian.Contracts`.

## Related docs

- `docs/architecture/module-map.md`
- `docs/source/generated/source-roadmap-traceability.md`
