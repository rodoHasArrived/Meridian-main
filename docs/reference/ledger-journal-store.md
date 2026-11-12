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

## Recovery, reconciliation, and journal access

PostgreSQL remains the authoritative posted journal. Paper fill handoffs and financing assessments
use durable source outboxes with the existing WAL and atomic snapshots; they are independently
replayable source evidence, not additional accounting books. Financing assessments have their own
source identity, exact book/period scope and changed-content refusal. Session recovery confirms
pending assessments before exposing rebuilt cash and journals. An unresolved assessment blocks
further fills, settlements and close. Account-level charges have no inferred strategy allocation.

`POST /api/portfolio/import-runs` retains an immutable broker snapshot before subscription effects
and optionally prepares one opening-balance draft. Reusing the run ID resumes its checkpoints;
different content under that ID is refused. Opening balances require complete cash, quantity,
currency and cost evidence. The supported opening slice is unit-priced equities in one currency;
derivative multipliers and missing tax-lot history are not inferred. Review, approval and posting
remain ordinary journal-workbench actions. The atomic posting boundary rechecks the opening
cutoff and rejects overlapping originating activity. The cutoff is a calendar date: same-day
post-snapshot trading requires a future timestamp-cutover treatment.

`Ledger:DailyReconciliation` configures `UtcHour`, `RetryMinutes` and explicit `Targets` containing
tenant, company, fund and account identities. With no targets the worker is idle. Each daily run
reauthorizes retained ownership, uses a stable operation ID across restart, and compares through
the existing persistent break queue. It never resolves or signs off a break. Posted cash and
canonical lot quantities/costs are separate comparison evidence; missing or unsupported lot
history blocks a claim of reconciliation rather than becoming a zero position.

Run journal queries are available under `/api/workstation/runs/{runId}/ledger`: `entries`,
`accounts`, `trial-balance/as-of`, `running-balance` and `symbols/{symbol}`. Paper/live reads can
rebuild from authorized posted journals carrying the exact `strategy.runId`, strategy and account
provenance. An unspecified book selects only a unique primary book; parallel accounting bases
are never added together. Existing WPF run-ledger views consume the shared summary.

`POST /api/workstation/runs/{runId}/ledger/export` and
`POST /api/ledger/periods/{periodId}/journal-export` produce CSV or Parquet journal lines.
Requests select the format and may supply a complete account-name mapping. Missing mappings
refuse export. Original accounts, IDs, metadata, dimensions and currency envelopes remain in the
output; external mapping does not modify the source journal. Monetary columns use invariant
decimal text in both formats to preserve precision. JSON audit envelopes use generated contexts.
The MCP ledger tools use these authenticated, scoped read APIs.

The read-only completeness control at
`GET /api/ledger/books/{ledgerBookId}/periods/{periodId}/completeness` compares independent,
explicitly selected source populations with their required journal purposes. A fill can require
both a trade and a commission receipt. Missing, duplicate, unlinked and wrong-scope journals are
reported separately. Unsupported source versions or absent ownership bindings leave coverage
incomplete; an empty journal never proves an empty source population.

## Close snapshots and subsequent corrections

Migration `V_ledger_055__period_close_snapshots.sql` adds immutable close snapshots. Hard close
captures the ordered posted journal population and content hash in the same transaction as the
period transition. History fences, the accounting audit chain and temporary-account balance
checks remain in force. Retrieval verifies the stored hash and declared scope; reopened periods
retain their earlier close versions. `GET /api/ledger/periods/{periodId}/close-snapshot` exposes
the authorized snapshot without changing period state.

`POST /api/ledger/period-adjustments/prepare` prepares a source-linked inverse in an explicitly
selected current open period, preserving the closed source journal. Native-currency period-end
marks can opt into next-period reversal preparation; the default carrying-value-delta policy
remains available. `/api/ledger/period-adjustments/schedule-mark-reversal` retains the exact
source/target policy before its due date and uses the existing automated-journal scheduler to
prepare the draft. A prepared or scheduled draft grants no approval or posting authority.
Foreign-currency correction preparation and automatic rerouting of an unposted late fill are
outside this treatment and fail closed; they must not silently lose FX or source evidence.

Declared journal lines must share one functional currency and match the retained book currency.
A fully declared single-transaction-currency journal must also balance its original amounts.
Legitimate FX exchange journals retain their differing original currencies and recorded rates;
unlike nominal amounts never satisfy the functional balance check. Legacy currency-blind history
retains its explicit compatibility path and governed backfill workflow. Lending now supplies
explicit currency envelopes through the shared journal boundary.

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
