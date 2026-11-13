---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-STORAGE-ACCOUNTING
path: src/Meridian.Storage.Accounting
status: active
owner_lane: Accounting and Ledger
last_reviewed: 2026-11-13
---

# src/Meridian.Storage.Accounting

## Purpose

Compile business persistence adapters separately from market-data storage primitives. This project
owns the PostgreSQL adapters for ledger, reporting, Security Master, asset operations, banking,
fund accounts/structure, direct lending, money market, operations and tenant cutover.

## Layer responsibility

Compose `Meridian.Storage` durable IO with Ledger and Reporting contracts. The lower-level Storage
assembly must not reference those business owners. Namespaces and source paths remain unchanged
through linked compile items; schema versions, SQL migrations and output paths remain unchanged.

## Key folders and files

- `Meridian.Storage.Accounting.csproj` declares linked source ownership and migration content.
- `../Meridian.Storage/Ledger/` and `../Meridian.Storage/Reporting/` hold accounting adapters.
- Other linked business store folders are enumerated explicitly in the project file.

## Important workflows

Linked intercompany and entity adapters retain approved legal-agreement pins, immutable lifecycle
and account-role history, book-control activation receipts, counterpart proposal intent and
consolidation/FX authority through the financial commit. This assembly mints the internal Contracts
structure-read proof while retaining its live ownership lease. See the
[implementation sequence](../../docs/engineering/plans/intercompany-entity-controls.md) and
[operator procedure](../../docs/operators/intercompany-entity-controls.md).

Rebuild all consumers when changing assembly ownership. See
[boundary extractions](../../docs/architecture/review-boundary-extractions.md) for the compatibility
boundary and focused test ownership. Register concrete adapters at application composition roots;
do not introduce a reverse reference from Storage primitives.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-STORAGE-ACCOUNTING -->
| Roadmap item | Title |
| --- | --- |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W4-RPT-001` | Governed report pack readiness |
| `W5-ACCT-001` | Accounting records and operational evidence |
| `W9-ASSET-010` | Asset Accounting Event Spine and atomic lot posting |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-STORAGE-ACCOUNTING -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.Storage.Accounting/Meridian.Storage.Accounting.csproj
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~ReviewBoundaryTests"
```

## Change rules

Preserve database transaction boundaries, migration hashes and tenant/accounting authority.

## Related docs

- [Storage source guide](../Meridian.Storage/README.md)
- [Module map](../../docs/architecture/module-map.md)
