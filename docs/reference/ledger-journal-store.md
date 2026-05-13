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

`LedgerStoreExtensions.AddLedgerJournalStore(string connStr)` registers
`ILedgerJournalStore`, `PostgresLedgerJournalStore`, `ILedgerBookService`,
`PostgresLedgerBookService`, and a default options instance with the provided connection string.

## Migrations

Migration scripts are copied from `src/Meridian.Storage/Ledger/Migrations/` with the Storage
assembly:

| Migration | Tables |
| --- | --- |
| `V_ledger_001__journal_entries.sql` | `journal_entries`, `journal_legs` |
| `V_ledger_002__accounting_periods.sql` | `accounting_periods`, `period_close_events` |
| `V_ledger_003__ledger_books.sql` | `ledger_books`, `accounting_periods.ledger_book_id` |

`journal_entries` and `journal_legs` both carry `aggregate_id`, `period_id`, `command_id`, and
`correlation_id` lineage columns. `journal_entries` has `UNIQUE (journal_entry_id)`.

`ledger_books` scopes accounting periods to fund-structure nodes. `accounting_periods` uses
`optimistic_version` for period updates and carries nullable `ledger_book_id` for compatibility
with periods created before book scoping. `period_close_events` records the close audit event and
the period version produced by that save.

## Store Contract

`ILedgerJournalStore` exposes:

| Method | Behavior |
| --- | --- |
| `AppendAsync` | Appends one balanced `JournalEntry` plus lineage in a serializable transaction. |
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

## Ledger Book Service

`ILedgerBookService` is the application-facing contract for the multi-ledger workflow. The
Postgres implementation is backed by `ILedgerJournalStore` and supports:

- Creating, loading, and listing books scoped to fund-structure nodes.
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
