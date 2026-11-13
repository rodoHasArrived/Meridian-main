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

The linked native intercompany registry retains optional structured legal declarations in its
existing PostgreSQL JSONB state, approved revisions, audit snapshots and command receipts. New
companions omit null/default legacy values, preserving approved immutable prefixes and legal
hashes. Adding terms requires a new reviewed revision; no replacement registry or schema migration
rewrites earlier approvals. See
[the native legal contract](../../docs/domain/intercompany-agreements.md#structured-declarations-and-date-previews).

Legacy attribution revocation appends an explicit reviewed successor with unchanged source,
legal/profile, account and transaction pins plus a rationale. Active resolution omits the revoked
assertion while retaining history; no posted source, native legal hash or SQL schema is rewritten.

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

## Accounting exports

The `ExportLedgerAsync` extension exports an immutable journal generation, as-of trial balance and
balance sheet, period income statement, financial summary and reconciled cash-flow statement as
CSV, XLSX, Parquet or Arrow. Period income uses activity in `(PeriodStart, AsOf]`, excludes pure
retained-earnings closing transfers and retains ordinary reversals. The financial summary keeps
that period result separate from accumulated unclosed income used in the as-of equity equation.
Statement account rows retain chart paths with direct and aggregate balances; summing hierarchical
aggregate balances would double-count child accounts. Every amount and quantity uses
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

## Durable balance checkpoints

Governed book hard-close commits a cumulative native balance checkpoint, its close-event reference
and the existing ledger audit fact in the same PostgreSQL transaction. Migration 067 leaves earlier
close events unchanged. Checkpoints retain every account and exact line-dimension group, including
known accounts whose net balance is zero. Populated accounting periods cannot change ledger books.
Unscoped compatibility periods still close normally without a book checkpoint.

`ILedgerBalanceSnapshotSource.ReadTrialBalanceAsOfAsync` reads an authorized book in one repeatable-read
transaction. It combines the latest eligible checkpoint with journal entries posted after its
sequence watermark or dated after its cutoff, bounded by the requested timestamp and captured
current watermark. This includes later backdated entries and previously posted future entries
exactly once, including after a governed reopen. Account and dimension filters use the same
case-insensitive matching rules as the in-memory ledger. Missing accounts remain distinct from
known zero balances. The journal-store extension retains full hydration for compatibility stores.

Checkpoint content is bound to its retained close-event audit digest. Reads verify the audit suffix
through the current durable head and compare selected tail journals and legs with retained audit
snapshots before using their totals. A checkpoint also pins the earliest preposted future journal's
audit sequence, so that suffix may extend before the close when future entries already exist.
Legacy genesis inventory retains its existing identity-only protection boundary. Missing or altered
checkpoint evidence fails closed. This bounded read authenticates its checkpoint and relevant journal
tail; it does not replace `VerifyLedgerEventAuditAsync`, which verifies whole-ledger source coverage.
Financial reports that need journal evidence still capture their
complete reporting generation; the mark-to-market carrying-value reader uses the balance API.

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
