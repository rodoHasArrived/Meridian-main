---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-STORAGE-ACCOUNTING
path: src/Meridian.Storage.Accounting
status: active
owner_lane: Accounting and Ledger
last_reviewed: 2026-11-12
---

# src/Meridian.Storage.Accounting

## Purpose

Compile business persistence and accounting adapters separately from market-data storage primitives.
This project owns the PostgreSQL adapters for ledger, reporting, Security Master, asset operations,
banking, fund accounts/structure, direct lending, money market, operations and tenant cutover, plus
ledger-backed export projections and portable accounting-payload validation.

## Layer responsibility

Compose `Meridian.Storage` durable IO with Ledger and Reporting contracts. The lower-level Storage
assembly must not reference those business owners. Namespaces and source paths remain unchanged
through linked compile items; schema versions, SQL migrations and output paths remain unchanged.

## Key folders and files

- `Meridian.Storage.Accounting.csproj` declares linked source ownership and migration content.
- `../Meridian.Storage/Ledger/` and `../Meridian.Storage/Reporting/` hold accounting adapters.
- `Export/` owns ledger-specific requests and the `ExportLedgerAsync` extension over the generic
  `AnalysisExportService` text-table writer. Consumers keep the `Meridian.Storage.Export` namespace;
  only the accounting adapter assembly references Ledger and interprets journals or lot evidence.
- Other linked business store folders are enumerated explicitly in the project file.

## Important workflows

Rebuild all consumers when changing assembly ownership. See
[boundary extractions](../../docs/architecture/review-boundary-extractions.md) for the compatibility
boundary and focused test ownership. Register concrete adapters at application composition roots;
do not introduce a reverse reference from Storage primitives.

## Accounting exports

The `ExportLedgerAsync` extension exports an immutable journal generation, as-of trial balance and
reconciled cash-flow statement as CSV, XLSX, Parquet or Arrow. Every amount and quantity uses
invariant decimal text to preserve booked precision; consumers parse it with a decimal type.
Empty reports retain their accounting columns. Output lives in a unique directory and failed
exports remove their partial artifacts.

Authorized callers may supply exact open-lot valuation and closed-parcel evidence. The browser run
export maps retained account snapshots through `BacktestLedgerExportMapper`; missing historical
snapshots produce explicit availability rows and warnings rather than fabricated zero balances.
Fill disposals include exact basis, fees and direction; corporate-action cash-in-lieu and return of
capital are separately exported from their authoritative journals. Fractional corporate-action
acquisition parcel dates are not retained by the simulator, so that aggregate schedule explicitly
reports the evidence gap and is not represented as a complete tax filing.

## Portable ledger evidence

`PortableLedgerSnapshot.CapturePayload` captures typed journals into the model-neutral
`PortableLedgerPayload`. `PortableLedgerPackaging.CreatePackager` installs the accounting
validator that replays journals and verifies the balance digest before publication or import.
Its `ReadLedgerAsync` extension retains typed read access without moving Ledger references back
into Storage. The workstation packaging endpoints and package CLI use this configured factory.
An unconfigured transport refuses accounting evidence even when ordinary file checksums are
disabled. Reserved ledger and manifest archive paths cannot be duplicated or aliased.

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
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~LedgerAnalysisExportTests"
```

## Change rules

Preserve database transaction boundaries, migration hashes and tenant/accounting authority.

## Related docs

- [Storage source guide](../Meridian.Storage/README.md)
- [Module map](../../docs/architecture/module-map.md)
