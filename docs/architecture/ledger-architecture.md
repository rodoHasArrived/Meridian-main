# Ledger Architecture

Meridian uses a **double-entry accounting ledger** to provide an independent, auditable record of all financial movements produced by backtesting runs and live strategy execution. This document explains the layered design, key types, and the relationship between the C# engine and the F# validation/reconciliation layer.

---

## Why a ledger?

The portfolio state produced by `SimulatedPortfolio` and `PaperTradingPortfolio` answers the question *"what do we hold right now?"*. The ledger answers the complementary question *"how did we get here, and does the accounting add up?"*

Every fill, commission, interest accrual, dividend, and corporate-action adjustment is recorded as a balanced journal entry (debits = credits). This means:

- **Auditability** — every cash movement has a timestamped, immutable record.
- **Reconciliation** — the trial balance can be compared to the portfolio snapshot to detect drift.
- **Reporting** — account-level balances (asset, liability, equity, revenue, expense) can be projected as a P&L or balance sheet.

---

## Package layout

| Assembly | Role |
|----------|------|
| `Meridian.Ledger` | Core double-entry engine — `Ledger`, `ProjectLedgerBook`, `FundLedgerBook`, domain types |
| `Meridian.FSharp.Ledger` | F# validation, reconciliation, and matching-rule engine |
| `Meridian.Strategies` | `LedgerReadService` — converts a ledger to workstation read models |
| `Meridian.Backtesting.Sdk` | `BacktestLedger` / `BacktestJournalEntry` type aliases pointing to `Meridian.Ledger` |

---

## Core types (Meridian.Ledger)

### `Ledger`

The main double-entry bookkeeping object. One `Ledger` instance tracks one strategy run or one financial account. It is **not** injectable as a singleton — the backtesting engine creates one per `RunAsync` call and the result is stored in `StrategyRunEntry.Metrics.Ledger`.

Key operations:

| Method | Description |
|--------|-------------|
| `Post(JournalEntry)` | Validates balance (debits = credits) then appends the entry |
| `PostLines(...)` | Convenience overload: builds and posts a balanced entry in one call |
| `GetBalance(account)` | Net balance applying normal-balance rules (debit-normal for assets/expenses) |
| `GetBalanceAsOf(account, t)` | Point-in-time balance |
| `TrialBalance()` | All account balances as of now |
| `SnapshotAsOf(t)` | Complete account snapshot at a past timestamp |
| `GetJournalEntries(...)` | Filtered journal query (date range, symbol, fill ID, etc.) |

Validation is delegated to `LedgerInterop.ValidateJournalEntry` (F#) before posting.

### `ProjectLedgerBook`

Manages a keyed collection of `Ledger` instances within a single host process. Use it when multiple parallel runs or projects need isolated ledgers but a single lookup point.

```csharp
var book = serviceProvider.GetRequiredService<ProjectLedgerBook>();
var key  = new LedgerBookKey(projectId: "alpha-momentum", runId: runId);
var ledger = book.GetOrCreate(key);
ledger.Post(journalEntry);
```

Registered as a singleton by `LedgerFeatureRegistration`.

### `FundLedgerBook`

A thin wrapper around `ProjectLedgerBook` that provides fund-structure-aware accessors:

```csharp
var fundBook = new FundLedgerBook(book, fundId);
var sleeveLedger = fundBook.SleeveLedger(sleeveId);
```

Useful for fund-of-funds structures where assets, liabilities, and P&L must be tracked per entity/sleeve/vehicle.

### `LedgerAccounts`

A static factory that produces well-known `LedgerAccount` values:

```csharp
var cash      = LedgerAccounts.CashAccount(brokerageAccountId);
var realized  = LedgerAccounts.RealizedGain;
var dividends = LedgerAccounts.DividendIncome;
```

### `ChartOfAccounts`

`ChartOfAccounts` registers customizable colon-delimited account paths such as
`Assets:Cash:Brokerage`, creates missing parent accounts, rejects parent/child account-type
conflicts, and aggregates flat trial-balance balances up the hierarchy. This keeps the core ledger
compatible with fixed built-in accounts while supporting fund-specific chart-of-accounts structures
for reporting and close workflows.

### `LedgerFinancialStatementBuilder`

`LedgerFinancialStatementBuilder` projects a current or point-in-time trial balance into
income-statement rows, balance-sheet rows, net income, ending equity, and an accounting-equation
variance. Totals are computed from the flat trial balance to avoid double-counting parent rollups;
rows use the chart hierarchy for operator-facing statement sections.

---

## How backtesting posts to the ledger

`SimulatedPortfolio` owns the posting logic. Entries are created for:

| Event | Debit | Credit |
|-------|-------|--------|
| Buy fill | Position account | Cash |
| Sell fill | Cash | Position account + Realized Gain/Loss |
| Commission | Commission Expense | Cash |
| Dividend | Cash | Dividend Income |
| Margin interest | Margin Interest Expense | Cash |
| Corporate action (split/spinoff) | Adjusted position | Contra entry |

All entries are balanced (`Σ debits = Σ credits`) and validated by the F# layer before posting.

---

## Security Master-driven expected accounting

Security Master is also an accounting rule source for reconciliation and close-readiness checks,
not just an identity lookup. The first production slice lives in
`SecurityMasterAccountingEventService` and generates deterministic expected accounting events from
Security Master terms, accounting rules, positions, optional factor schedules, and optional actual
cash activity.

The shared DTO enum reserves the broader expected-event vocabulary needed for accrual reversal,
premium/discount, maturity, call, dividend, and FX events. The current generated event set is still
intentionally narrow:

| Security Master input | Expected event | Journal preview |
|-----------------------|----------------|-----------------|
| Fixed coupon terms plus par position | `AccrueInterestIncome` | Dr Accrued Interest Receivable / Cr Coupon Income |
| Fixed coupon pay date in period | `ReceiveCashInterest` | Dr Cash / Cr Accrued Interest Receivable |
| Factor reduction for amortizing securities | `RecognizePrincipalPaydown` | Dr Cash / Cr Securities |

Generated events include an `AccrualInputSnapshotDto` and deterministic idempotency key so the same
security, account, period, event type, and source snapshot produce the same event identity. The
service emits `ExpectedJournalPreviewDto` records only; posting remains a separate
operator-approved workflow. Previews are balanced before they are exposed to reconciliation
consumers.

Factor paydowns are calculated at par. A factor reduction from `1.00` to `0.97` on `100,000` par
generates a `3,000` principal expectation regardless of carrying or sale price. Factor-based
securities are treated as schedule-dependent when the Security Master terms explicitly require a
factor schedule, carry a current factor below `1.00`, or report current face below original face.
The supported factor-based fixed-income family now includes bonds, mortgage-backed securities,
asset-backed securities, and loan/amortizing-loan instruments, so MBS/ABS principal reductions use
the same par-based expectation path rather than falling into the unsupported-instrument posture.
The reconciliation issue set flags missing schedules, missing coupon/day-count/payment terms,
missing accounting classification, missing actual cash, amount mismatches, and principal/income
classification mismatches.

`ReconciliationRunService` integrates the accounting-event result through
`ISecurityMasterAccountingEventSourceAdapter`. The workstation service graph registers
`SecurityMasterAccountingEventSourceAdapter`, which builds accounting-event inputs from resolved
portfolio positions first and falls back to resolved ledger trial-balance lines when positions are
not available. The adapter loads the current Security Master economic definition for each resolved
security id, maps coupon/accrual/maturity/factor terms into `SecurityMasterAccountingSecurity`, and
preserves the fund-account scope as the accounting-event account id. Existing runs continue to
reconcile portfolio, ledger, bank, and statement inputs when no Security Master query service or no
resolved economic definitions are available. When the adapter supplies Security Master accounting
inputs, the run detail carries expected accounting events, accrual calculations, balanced journal
previews, and structured Security Master accounting issues alongside the existing matches, breaks,
coverage issues, and classification map.

## Operations continuity workflow

`OperationsContinuityWorkflow` is the shared application aggregate for the account-period
operations lane. It is scoped to a fund account, accounting period, optional Security Master
snapshot, and broker/custodian/bank source. The workflow models broker intake, Security Master,
ledger posting, reconciliation, and approval as explicit gates. UI clients consume the derived
status from `IOperationsStatusDerivationService`; they must not infer close readiness from local
component state.

The implemented continuity slice provides:

| Component | Responsibility |
|-----------|----------------|
| `OperationsContinuityWorkflowService` | Command-driven workflow transitions, optimistic version checks, audit writes, DTO projection |
| `IOperationsContinuityRepository` | Workflow snapshot persistence; file-backed by default, or PostgreSQL-backed through `PostgresOperationsContinuityStore` when `MERIDIAN_LEDGER_CONNECTION_STRING` is configured |
| `IOperationsWorkflowAuditStore` | Append-only audit timeline with previous/current SHA-256 hash chaining |
| `PostgresOperationsContinuityStore` | PostgreSQL implementation of `IOperationsContinuityRepository`, `IOperationsWorkflowAuditStore`, and `IOperationsContinuityTransactionalCommitStore`; successful ledger posts share the ledger journal transaction |
| `IOperationsContinuityTransactionalCommitStore` | Strict-atomicity commit seam for successful ledger posting: append journal, append workflow audit, and save workflow snapshot in one persistence boundary |
| `OperationsStatusDerivationService` | Deterministic server-side overall status derivation from gate/sub-state posture |
| `Meridian.Contracts.Workstation` operations DTOs | Shared browser/WPF read and command contracts |

Every implemented transition writes an audit record before the workflow snapshot is saved. Audit
records include actor, rationale, correlation id, evidence references, previous hash, and current
hash. Broker import is intentionally separate from normalization. Ledger drafting is intentionally
separate from posting: validation can mark a journal preview ready, but reconciliation requires the
explicit `ledger/post` command with a durable ledger batch reference.

Approval submission is also guarded by the application service and aggregate, not by workstation
clients. The `approval/submit` command now requires reviewer, rationale, and report-pack metadata,
and it refuses submission until broker intake, Security Master, and ledger posting gates have
passed and reconciliation has reached a completed or reviewable posture. Rejected approval
submissions return structured blockers and do not append audit records or mutate the workflow
snapshot.
Once a report pack is marked ready through gate posture, submit, approve, and close commands must
reference that same report-pack id; mismatches return `REPORT_PACK_ID_MISMATCH` without appending
an audit event or mutating the workflow snapshot.
Approval decisions must also come from the reviewer assigned during `approval/submit`.
The shared workstation approve/reject endpoints replace body-supplied reviewer values with the
authenticated operator before calling the application service; mismatched decision reviewers return
`APPROVAL_REVIEWER_MISMATCH` without appending an audit event or mutating the workflow snapshot.
The close command also verifies the existing audit timeline before it appends `workflow-closed`.
If the timeline is missing or any previous/current hash link fails canonical verification, close
returns `AUDIT_CHAIN_MISSING` or `AUDIT_CHAIN_INVALID` on the Approval gate without appending a new
audit event or mutating the workflow snapshot.
When gate posture reports that a report pack exists but is not ready, the aggregate projects a
`REPORT_PACK_NOT_READY` blocker onto the Approval gate with the linked evidence. That keeps
approval and close guidance server-derived for both browser and WPF desktop clients.

The reconciliation command can carry Security Master coverage issue counts, Security Master
accounting issue counts, expected-event counts, and journal-preview counts directly from
reconciliation output. `OperationsContinuityWorkflow` applies those counts to the Security Master
gate during the reconciliation transition, so unresolved Security Master coverage or
accounting-term problems block the close lane without requiring UI-side status derivation.
`OperationsContinuityReconciliationBridge` also preserves the underlying Security Master coverage
and accounting issue rows as workflow break cases, using stable issue codes such as
`SM_RECON_SECURITY_UNRESOLVED`, `ACCRUAL_AMOUNT_MISMATCH`, and
`FACTOR_PAYDOWN_AMOUNT_MISMATCH`. That gives browser and WPF desktop clients the same
server-authored blocker detail behind the aggregate counts.
Security Master override approval metadata is governed server-side as well: override approvals must
carry rationale, policy reference, and an expiration that is not already expired; stale approvals
return `SM_OVERRIDE_APPROVAL_EXPIRED` without appending an audit record or mutating the workflow.
The shared `OperationsWorkflowContractMatrix` also publishes the production blocker and issue code
vocabulary for broker intake, Security Master accounting coverage, accrual reconciliation, factor
paydowns, ledger posting, reconciliation evidence, approval, and close blockers. It also publishes
the audit-event vocabulary, including `ledger-posting-blocked`, so browser and WPF clients can map
timeline rows without local string catalogs. Security Master
accounting event generation distinguishes a missing factor schedule from a stale prior-period
factor source so the continuity gate can route factor-paydown remediation without UI-side
classification.

For successful postings, `OperationsLedgerPostRequestDto` now carries an
`OperationsLedgerJournalCandidateDto`. The application service converts that candidate into a
`LedgerJournalEntryWrite`. The candidate must carry a durable command id, an idempotency key, and
Security Master provenance (`SecurityId` plus a provenance string) before the service will append it
to the ledger journal; missing values return structured ledger-gate blockers without appending an
audit event or mutating the workflow snapshot. When an `IOperationsContinuityTransactionalCommitStore` is registered,
the service commits the journal append, workflow audit append, and workflow snapshot save through
that single commit seam. Production hosts enable the PostgreSQL path with
`MERIDIAN_LEDGER_CONNECTION_STRING`; `LedgerStartup` runs ledger migrations and registers
`PostgresOperationsContinuityStore` for workflow snapshots, audit timeline reads, and transactional
ledger-post commits. The same store commits workflow start by inserting the initial snapshot and
first audit event in one PostgreSQL transaction, which preserves the audit table's workflow foreign
key without weakening file-backed audit-first behavior. The live integration fixture
`OperationsContinuityPostgresRoundTripTests` can use either `MERIDIAN_LEDGER_CONNECTION_STRING` or
Testcontainers PostgreSQL to prove the workflow snapshot, audit hash chain, and durable journal
append round-trip through one migrated schema. When the transactional store is not registered, the workstation file-backed
mode keeps the split persistence path: await `ILedgerJournalStore.AppendAsync`, append audit, then
save the workflow snapshot. If no ledger store or transactional commit store is registered, or the
candidate is missing or invalid, the command returns a structured validation failure and does not
advance the workflow. Requests that fail posting posture checks such as closed period, duplicate
candidate, missing batch id, or missing posting kind still update the workflow to a blocked ledger
gate with a `ledger-posting-blocked` audit event and do not append a journal candidate. If no
durable journal store or transactional commit store is registered, the command follows the same
blocked-audit path instead of silently failing outside the workflow timeline.

The command path now covers start, broker import, broker normalization, Security Master resolution,
governed Security Master override approval, ledger draft, ledger validation, ledger posting,
reconciliation run, break resolution, approval submit, approval approve/reject, close, and governed
reopen. Posting is blocked without Security Master resolution or approved override, a validated
journal draft, an open period posture, a posting kind, and a non-duplicate posting candidate.
Once a workflow is closed, mutation commands are rejected before command-specific preconditions are
evaluated; only the governed reopen command can transition the workflow back into active
reconciliation.

---

## F# validation and reconciliation layer (Meridian.FSharp.Ledger)

### JournalValidation

`LedgerInterop.ValidateJournalEntry` runs before every `Post` call and enforces:

- Debit/credit balance within 0.000001 tolerance
- Journal entry ID uniqueness within the ledger
- Ledger entry ID uniqueness
- Consistent timestamps across all lines in an entry

### Reconciliation

`Reconciliation.reconcilePayment` and `Reconciliation.reconcileEventStream` compare projected cash flows to actual `CashLedgerEvent` values:

```fsharp
let results = Reconciliation.reconcileEventStream 0 projectedFlows events
```

Each `ReconciliationResult` carries a `ReconciliationStatus`:

| Status | Meaning |
|--------|---------|
| `Matched` | Amount and timing agree within tolerance |
| `UnderPaid` | Actual < expected |
| `OverPaid` | Actual > expected |
| `CurrencyMismatch` | Currencies differ |
| `TimingMismatch` | Settlement outside the tolerance window |
| `MissingActual` | Expected flow has no corresponding event |

### Matching rules (ReconciliationRules)

`ReconciliationRules.apply` evaluates a single `MatchingRule` against a `MatchCandidate`:

```fsharp
let outcome = ReconciliationRules.apply MatchingRule.``default`` candidate
// FullMatch 0.99m | PartialMatch(0.85m, "Timing drift 1 day(s)") | NoMatch(AmountBreak(...))
```

`ReconciliationRules.classifyBreaks` converts all non-matching candidates into `BreakRecord` values with severity (`Critical`, `High`, `Medium`, `Low`, `Info`).

Two predefined rules are provided:

| Rule | Amount tolerance | Timing tolerance | Partial match |
|------|-----------------|-----------------|---------------|
| `MatchingRule.default` | 1 % | 2 days | No |
| `MatchingRule.strict` | 0 % | 0 days | No |

### Portfolio ↔ ledger reconciliation

`LedgerInterop.ReconcilePortfolioLedgerChecks` compares portfolio-level aggregates (cash, equity, positions) to their ledger counterparts and produces `PortfolioLedgerCheckResult` records. Categories include `matched`, `amount_mismatch`, `missing_ledger_coverage`, `missing_portfolio_coverage`, `classification_gap`, `timing_mismatch`, and `partial_match`.

Portfolio ↔ ledger checks are evaluated directly inside the F# kernel rather than being coerced through the day-based cash-flow matching rules. This keeps `MaxAsOfDriftMinutes` minute-granular, preserves `partial_match` as an explicit status/category at the interop boundary, and ensures the severity exposed to workstation/governance consumers comes from the F# classification result instead of being recomputed in C#.

---

## REST API

Ledger data is exposed through the workstation endpoints under `/api/workstation/runs/{runId}/`:

| Route | Description |
|-------|-------------|
| `GET /api/workstation/runs/{runId}/ledger` | Full `LedgerSummary` (trial balance + journal) |
| `GET /api/workstation/runs/{runId}/continuity` | Shared run-centered continuity drill-in that bundles portfolio, ledger, cash-flow, reconciliation, and lineage context |
| `GET /api/workstation/runs/{runId}/ledger/trial-balance` | Trial balance lines, optionally filtered by `?accountType=Asset` |
| `GET /api/workstation/runs/{runId}/ledger/journal` | Journal entries, optionally filtered by `?from=…&to=…` |
| `GET /api/workstation/operations/continuity` | Fund-account/period operations workflow summaries, optionally filtered by `fundAccountId`, `periodId`, and derived `status` |
| `POST /api/workstation/operations/continuity` | Starts an operations continuity workflow and writes the first audit event |
| `GET /api/workstation/operations/continuity/{workflowId}` | Workflow detail with gates, timeline, blockers, evidence links, and next actions |
| `GET /api/workstation/operations/continuity/{workflowId}/timeline` | Append-only audit timeline for explainability |
| `POST /api/workstation/operations/continuity/{workflowId}/broker/import` | Records broker/custodian/bank import progress with expected-version enforcement |
| `POST /api/workstation/operations/continuity/{workflowId}/broker/normalize` | Records normalized external activity before Security Master resolution |
| `POST /api/workstation/operations/continuity/{workflowId}/security-master/resolve` | Updates Security Master resolution and accounting-term blockers |
| `POST /api/workstation/operations/continuity/{workflowId}/security-master/overrides/{overrideId}/approve` | Requires override approver, rationale, policy, non-expired expiration, and audit metadata |
| `POST /api/workstation/operations/continuity/{workflowId}/ledger/draft` | Creates a ledger journal preview without committing it |
| `POST /api/workstation/operations/continuity/{workflowId}/ledger/validate` | Validates balanced journal draft and period posture |
| `POST /api/workstation/operations/continuity/{workflowId}/ledger/post` | Appends a supplied journal candidate through the ledger journal store, then marks the validated ledger gate posted with a durable batch reference; with PostgreSQL Operations Continuity enabled, the journal append, audit append, and workflow snapshot save share one transaction |
| `POST /api/workstation/operations/continuity/{workflowId}/reconciliation/run` | Runs expected-vs-actual reconciliation after ledger posting |
| `POST /api/workstation/operations/continuity/{workflowId}/reconciliation/breaks/{breakId}/resolve` | Resolves or dismisses a break with rationale and evidence |
| `POST /api/workstation/operations/continuity/{workflowId}/approval/submit` | Submits a clean workflow for reviewer approval with report-pack evidence |
| `POST /api/workstation/operations/continuity/{workflowId}/approval/approve` | Records approval decision metadata and marks close readiness |
| `POST /api/workstation/operations/continuity/{workflowId}/approval/reject` | Routes rejected workflows back to ledger draft or reconciliation based on reason code |
| `POST /api/workstation/operations/continuity/{workflowId}/close` | Closes the approved workflow when all gates pass |
| `POST /api/workstation/operations/continuity/{workflowId}/reopen` | Reopens a closed workflow only with governed admin and incident metadata |

These endpoints are implemented in `WorkstationEndpoints.cs` and map to route constants in `UiApiRoutes`.

---

## Dependency injection

`LedgerFeatureRegistration` (registered unconditionally by `ServiceCompositionRoot.AddMarketDataServices`) contributes:

| Service | Lifetime | Notes |
|---------|----------|-------|
| `ProjectLedgerBook` | Singleton | Keyed ledger namespace for the host process |
| `PostgresOperationsContinuityStore` | Singleton when `MERIDIAN_LEDGER_CONNECTION_STRING` is set | PostgreSQL workflow snapshot/audit store and transactional ledger-post commit store |

`LedgerReadService` is registered separately by UI host startup (it depends on `Meridian.Strategies` types that are not available to `Meridian.Application`):

```csharp
// UiEndpoints.AddUiSharedServices
services.TryAddSingleton<LedgerReadService>();
```

---

## Extending the ledger

To post custom entries from a new strategy or service:

1. Resolve `ProjectLedgerBook` from DI (or create a local `Ledger` if isolation is preferred).
2. Define accounts using `LedgerAccounts` or construct a `LedgerAccount` directly.
3. Call `ledger.PostLines(description, timestamp, debitAccount, creditAccount, amount)`.
4. The F# validation layer runs automatically; a `LedgerValidationException` is thrown for unbalanced or duplicate entries.

See `SimulatedPortfolio.PostFillLedgerEntries` for a complete reference implementation.
