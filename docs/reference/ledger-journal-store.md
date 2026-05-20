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
workflow audit timeline with `previous_hash` and `current_hash` uniqueness guards. When
`PostgresOperationsContinuityStore` handles `ledger/post`, it opens one serializable transaction,
appends the ledger journal candidate through `ITransactionalLedgerJournalStore`, appends the
workflow audit event, touches the workflow version, and saves the workflow snapshot before commit.

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
