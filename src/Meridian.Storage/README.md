---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-STORAGE
path: src/Meridian.Storage
status: active
owner_lane: Governance and Ledger
last_reviewed: 2026-05-20
---

# src/Meridian.Storage

## Purpose

Storage owns WAL-backed persistence, archival packaging, storage catalogs, replay storage, ledger storage, and export support.

## Layer responsibility

This layer persists durable state and evidence. It should expose storage seams without owning application orchestration or UI presentation.

## Key folders and files

- `Archival/` - write-ahead log and archive package support.
- `Interfaces/` and `Sinks/` - storage sink contracts and implementations.
- `Replay/`, `Ledger/`, and `SecurityMaster/` - durable domain-specific stores.
- `FundAccounts/` - account definitions, balance snapshots, statement batches, reconciliation
  results, and account readiness persistence. Balance snapshots include optional realized and
  unrealized P&L columns for provider-ledger shadow-book comparison evidence.
- `FundStructure/` - PostgreSQL fund-structure storage contracts and store implementation, including the setup-batch seam used when entity setup commits must be persisted as one transaction.
- `Packaging/`, `Export/`, and `Maintenance/` - operational storage workflows.

## Important workflows

Use this module for persistence, replay evidence, storage maintenance, package import/export, and durable operator evidence.

Ledger journal appends are fail-closed for instrument-bearing postings. Any journal line that
targets securities, dividends, accrued interest, corporate actions, options, futures MTM, short
securities, or a symbol-scoped ledger account must carry Security Master provenance plus approved
lineage that includes active Security Master status and a ledger mapping reference tied to the
resolved line symbol or Security Master id before `ILedgerJournalStore` accepts it. Instrument
account lines must also carry an explicit line symbol, so entry-level Security Master metadata
cannot mask an unattributed securities, receivable, option, futures, or short-position posting.
Metadata-level instrument symbols are checked against the same lineage and mapping evidence as
line-level symbols, so an entry cannot declare one symbol at the journal level while posting another
instrument line.
Until durable journal writes carry line-level Security Master ids, a single journal entry may not
combine multiple instrument symbols behind one entry-level Security Master id.

Ledger tax-lot state is persisted as account-scoped policy records plus open-lot records in the
ledger schema. `ILedgerJournalStore` owns durable FIFO/LIFO/HIFO/SpecificId policy lookup inputs and
open-lot balances for a ledger book/account, while relief projection, approval workflow, and
tax-reporting exports remain outside the storage layer.

Direct-lending state persistence can include projected ledger journals in the same database
transaction as the loan event append. `PostgresDirectLendingStateStore.SaveAsync` accepts
`LedgerJournalEntryWrite` records and appends them through `ITransactionalLedgerJournalStore` with
the active `NpgsqlConnection` and serializable transaction, so a failed ledger append rolls back the
loan state/event/projection/outbox write as one unit.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-STORAGE -->
| Roadmap item | Title |
| --- | --- |
| `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W4-RPT-001` | Governed report pack readiness |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-STORAGE -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
```

## Change rules

Route durable writes through WAL or atomic file helpers. Avoid direct unguarded file writes for evidence-bearing data.

## Related docs

- `docs/architecture/module-map.md`
- `docs/development/build-observability.md`
- `docs/source/generated/source-roadmap-traceability.md`
