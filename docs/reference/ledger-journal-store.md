# Ledger Journal Store

The Postgres ledger journal store persists double-entry journal entries and accounting-period state
for fund-ops workflows that need durable lineage back to commands, correlations, periods, and
domain aggregates.

## Configuration

Use `Meridian.Storage.Ledger.LedgerJournalStoreOptions`:

| Option | Default | Purpose |
| --- | --- | --- |
| `ConnectionString` | empty | Postgres connection string. Required before store methods open a connection. |
| `SchemaName` | `ledger` | Schema that owns journal and period tables. |
| `EnablePeriodLocking` | `true` | Adds `FOR UPDATE` when period records are saved inside serializable transactions. |

Production composition also recognizes these environment variables:

| Variable | Purpose |
| --- | --- |
| `MERIDIAN_LEDGER_CONNECTION_STRING` | Enables PostgreSQL ledger journal persistence and PostgreSQL-backed Operations Continuity workflow/audit storage. |
| `MERIDIAN_LEDGER_SCHEMA` | Overrides the schema used by ledger and Operations Continuity tables; defaults to `ledger`. |
| `MERIDIAN_LEDGER_ENABLE_PERIOD_LOCKING` | Enables or disables explicit period row locking; defaults to `true`. |

`LedgerStoreExtensions.AddLedgerJournalStore(string connStr)` registers
`ILedgerJournalStore`, `ITransactionalLedgerJournalStore`, `PostgresLedgerJournalStore`,
`LedgerMigrationRunner`, `ILedgerBookService`, `PostgresLedgerBookService`, and a default options
instance with the provided connection string.

## Migrations

Migration scripts are copied from `src/Meridian.Storage/Ledger/Migrations/` with the Storage
assembly:

| Migration | Tables |
| --- | --- |
| `V_ledger_001__journal_entries.sql` | `journal_entries`, `journal_legs` |
| `V_ledger_002__accounting_periods.sql` | `accounting_periods`, `period_close_events` |
| `V_ledger_003__ledger_books.sql` | `ledger_books`, `accounting_periods.ledger_book_id` |
| `V_ledger_004__accounting_basis_policies.sql` | `accounting_policies`, basis and policy columns on `ledger_books` |
| `V_ledger_005__journal_basis_lineage.sql` | basis, policy, rule, source-event, and source-journal lineage on `journal_entries` and `journal_legs` |
| `V_ledger_006__journal_posting_kind.sql` | posting kind lineage and indexes on journal entries and legs |
| `V_ledger_007__journal_adjustment_approval_metadata.sql` | governed adjustment approval metadata on journal entries and legs |
| `V_ledger_008__operations_continuity.sql` | Operations Continuity workflow snapshots and hash-chained audit events |

`journal_entries` and `journal_legs` both carry `aggregate_id`, `period_id`, `command_id`,
`correlation_id`, `accounting_basis`, `accounting_policy_id`, `accounting_policy_version`,
`rule_id`, `rule_version`, `source_event_id`, and `source_journal_entry_id` lineage columns.
`journal_entries` has `UNIQUE (journal_entry_id)`.

`ledger_books` scopes accounting periods to fund-structure nodes. `accounting_periods` uses
`optimistic_version` for period updates and carries nullable `ledger_book_id` for compatibility
with periods created before book scoping. `period_close_events` records the close audit event and
the period version produced by that save.

`operations_continuity_workflows` stores one JSON snapshot per workflow plus query columns for fund
account, period, derived status, and version. `operations_continuity_audit` stores the append-only
workflow audit timeline with `previous_hash` and `current_hash` uniqueness guards.
`PostgresOperationsContinuityStore` also takes a transaction-scoped PostgreSQL advisory lock keyed
to the workflow id before reading the tail hash, so concurrent audit appends serialize the hash
chain even when competing commands target the same workflow. When the store handles `ledger/post`,
it opens one serializable transaction, appends the ledger journal candidate through
`ITransactionalLedgerJournalStore`, appends the workflow audit event, touches the workflow version,
and saves the workflow snapshot before commit.

Basis-aware books use `Primary`, `Gaap`, `Cash`, `Tax`, or `Statutory` as the configured accounting
basis. Existing books migrate as `Primary` with the `legacy-v1` policy. New books are unique by
fund profile, fund-structure node, and basis, so a fund node can carry parallel books for different
accounting policies without reclassifying historical primary evidence.

## Store Contract

`ILedgerJournalStore` exposes:

| Method | Behavior |
| --- | --- |
| `AppendAsync` | Appends one balanced `JournalEntry` plus basis/policy/rule/source lineage in a serializable transaction; rejects entries whose basis does not match the period's ledger book. |
| `GetByPeriodAsync` | Reads journal entries ordered by occurrence and sequence for one period. |
| `GetByAggregateAsync` | Reads journal entries ordered by occurrence and sequence for one aggregate. |
| `GetPeriodAsync` | Loads one accounting period by `period_id`. |
| `ListPeriodsAsync` | Lists periods, optionally filtered by ledger book, status, fund profile, or fund-structure node. |
| `SavePeriodAsync` | Inserts or updates one period using an expected-version guard and optionally writes a close audit event. |
| `GetLedgerBookAsync` | Loads one ledger book by `ledger_book_id`. |
| `ListLedgerBooksAsync` | Lists ledger books, optionally filtered by fund profile, fund-structure node, or node kind. |
| `SaveLedgerBookAsync` | Inserts or updates one ledger book record. |

The write path rejects unbalanced journal entries before opening a connection. Period saves use
bounded single-row reads and updates; the store does not buffer beyond the requested result set for
period or aggregate journal reads.

Basis-aware postings are still policy-engine evidence, not certified GAAP, tax, cash, or statutory
reporting. Operator-facing reports should describe balances as "basis per configured policy" until
reviewed by a qualified accountant.

## Acquisition and Disposal Reference Authority

New `AtomicTaxLotJournalCommand` acquisitions and disposals require positive
`AtomicLotReferenceVersions.SecurityMasterVersion` and `BookPositionVersion` assertions.
`AccountingPostingCandidatePostService` derives them from the retained approved event spine,
not from caller defaults or descriptive journal tags. The immutable atomic fingerprint binds
both values; the approved spine retains their source evidence.

After resolving exact replay, the serializable posting transaction holds the ledger book and
period, then locks the Security Master and book-position rows before selecting or changing lots.
Both reference providers must be PostgreSQL stores in the same database as the ledger. A governed
journal effective date is required for the position check. The versions, Active/effective reference
state, Long or Asset position and complete ledger-book context must match. Position status uses
ordinal case-insensitive equality with `Active`, matching candidate validation; inactive and other
statuses remain refused. The acquisition currency
must match Security Master; a disposal may retain
the existing supported transaction/functional currency distinction. These checks and locks
remain in force until the journal, lot mutations and audit record commit together. Rejected or
cancelled postings leave no new batch and do not reserve the retry identity.

The nullable field is omitted from JSON and the fingerprint when absent. Existing receipts
therefore keep their exact identity, including after restart or reference changes. The posting
service can reconstruct that historical shape only when its complete fingerprint matches the
retained receipt. A new receipt cannot remove or change its versions under the same identity.
When a separately composed journal store does not support the optional atomic receipt read, the
service forwards the complete versioned command to the atomic writer. An unavailable receipt never
authorizes omission of the reference versions: historical replay requires a readable exact receipt,
and the atomic writer still rejects changed fingerprints or stale authority. Other read failures and
cancellation propagate normally.
Other mutation kinds continue to use their existing typed reference instructions and reject an
extraneous acquisition/disposal version assertion on a new posting.

No schema migration or browser/WPF request change is required. Deploy the producer and storage
guard together and drain older writers: newly submitted commands without the retained versions
are refused. Short opening/cover authority and consumer cutover remain separate LOT work.

## Canonical Lot Direction

`OpenLotDirection` separates position direction (`Long` or `Short`) from non-negative
lot quantities and carrying basis. `OpenLotAcquisitionDto.Direction` defaults to `Long`;
the default is omitted from JSON so historical long-lot payloads and atomic-command
fingerprints retain their existing shape. A non-default direction participates in the
immutable command fingerprint. Relief calculations require a single direction per pool.

This contract does not enable short accounting. New canonical postings and direct lot
saves currently admit only `Long`; acquisitions also require an asset ledger account.
The event spine and legacy-lot backfill reject short or liability positions before they
can be interpreted as long acquisitions or disposals. Exact committed long-lot replay
continues to resolve before new-posting admission checks.

Short lots are refused by the current long-lot disposal, amortization, capital-return,
successor and realized-tax paths. Opening journals, cover economics, position authority,
tax evidence and certified Execution/Backtesting consumer parity remain prerequisites
for enabling short posting.

Legacy-lot backfill accepts reviewed source facts only while the ledger book, Security Master,
book position and lot remain locked in the same PostgreSQL transaction. Both accepting evidence
and applying it require PostgreSQL reference stores in the ledger database. A missing provider,
different database or stale version blocks the new operation without enriching the lot or
reserving its receipt identity. Previously committed exact receipts replay before current
reference checks; later reference changes do not rewrite historical acquisition evidence.
Canonical posting uses the same book-before-reference lock order even when tenant enforcement
is disabled for compatibility. Exact committed posting replay remains ahead of that new lock.

## Ledger Book Service

`ILedgerBookService` is the application-facing contract for the multi-ledger workflow. The
Postgres implementation is backed by `ILedgerJournalStore` and supports:

- Creating, loading, and listing books scoped to fund-structure nodes.
- Creating parallel books for `Primary`, `Gaap`, `Cash`, `Tax`, and `Statutory` basis policies.
- Creating and listing accounting periods, including open-period enumeration.
- Soft-close and hard-close period transitions with optimistic-version persistence.
- Completed-period summaries containing trial balance rows, debit/credit totals, net income,
  period-on-period variance, open reconciliation break count, and sign-off status.
- Period-close operator work items routed to Accounting with the `FundReconciliation` page tag,
  machine-readable required sign-off role, tolerance profile reference, and sign-off status.

The UI host maps:

- `GET /api/ledger/books`
- `GET /api/ledger/books/{ledgerBookId}`
- `POST /api/ledger/books`
- `GET /api/ledger/periods`
- `POST /api/ledger/periods`
- `POST /api/ledger/periods/{periodId}/close`
