# Ledger Architecture

Meridian uses a **double-entry accounting ledger** to provide the source of all ledger truth and an independent, auditable record of financial movements produced by backtesting runs, fund-operations workflows, and live strategy execution. This document explains the layered design, key types, and the relationship between the C# engine and the F# validation/reconciliation layer.

---

## Why a ledger?

The portfolio state produced by `SimulatedPortfolio` and `PaperTradingPortfolio` answers the question *"what do we hold right now?"*. The Meridian ledger answers the complementary question *"how did we get here, and does the accounting add up?"* External GL imports are reconciliation evidence against this ledger truth, not the authority that overwrites it.

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

Closed-period trial-balance endpoints also expose `LedgerTrialBalanceReportDto`, which wraps the
same server-derived period rows with locked-period status, debit/credit/net-income totals,
accounting-policy lineage, and a SHA-256 checksum signature. The line-list route remains available
for simple grids, while the report route gives export and audit surfaces a single signed payload.
Closed-period P&L endpoints expose realized revenue/expense net income separately from
accrual-basis adjustment impact, using retained ledger line labels and lineage markers to identify
accrual adjustments while preserving the existing total revenue, expense, and net-income values.

### `LedgerReportPackBuilder`

`LedgerReportPackBuilder` packages point-in-time financial statements into export-ready ledger
artifacts. Given a `LedgerReportPackRequest`, it builds trial-balance, income-statement,
balance-sheet, financial-statements JSON, tax-lot realized-gains CSV, line-provenance, and
manifest artifacts, computes SHA-256 checksums for each artifact, and signs the report-pack
payload with a deterministic integrity checksum plus the generator identity and timestamp. The
request can carry a matching `LockedAccountingPeriod` so report manifests preserve period-lock
evidence without making the ledger domain responsible for persistence, workflow approval, or
endpoint routing. When account-level tax-lot relief projections are supplied, the tax export
records sale date, account scope, relief method, relieved lots, proceeds, cost basis, and
per-lot realized gain/loss; otherwise the artifact remains header-only for stable pack shape.

### `LedgerReportSchedulePlanner`

`LedgerReportSchedulePlanner` projects governed report schedules into concrete export occurrences.
A `LedgerReportSchedule` captures the fund, report name, monthly/quarterly/annual cadence, first
period start, due-day offset, base currency, requested formats, recipients, and creator evidence.
`LedgerScheduledReportExportPackageBuilder` then binds a signed report pack to one scheduled
occurrence and emits delivery artifacts: a recipient/format manifest for all scheduled exports and
a regulator-facing XML summary when `RegulatoryXml` is requested. The XML artifact carries period,
fund, due-date, signature, and statement-total evidence for downstream reporting tools; full
XBRL/iXBRL taxonomy production remains outside the core ledger kernel.
The planner emits period identifiers, report IDs, as-of timestamps, due timestamps, recipients, and
formats, and each occurrence can create a `LedgerReportPackRequest`. Delivery, permission checks,
artifact storage, and notification remain outside the ledger domain.

### `MultiCurrencyLedgerTranslator`

`MultiCurrencyLedgerTranslator` converts local-currency account balances into a reporting/base
currency using explicit FX rates. Currency comes from account-level configuration or ISO-style
account symbols such as the `EUR` symbol on `LedgerAccounts.CashInCurrency("EUR")`. When carrying
base balances are supplied, it can produce balanced unrealized FX revaluation lines against the
monetary asset or liability account and the scoped unrealized FX gain/loss accounts.

### `MultiCurrencyJournalProjector`

`MultiCurrencyJournalProjector` handles the posting side of multi-currency accounting. It accepts
local-currency debit/credit lines, local currency codes, and FX rates to the reporting currency,
rounds converted amounts to currency precision, verifies that the converted base-currency debits
and credits balance, and exposes both the posting lines and local-currency evidence. The output can
be passed to `Ledger.PostLines`, while local amount, currency, and FX-rate evidence remain attached
to the projection for audit and close review.

### `DailyPortfolioPricingProjector`

`DailyPortfolioPricingProjector` applies a fund-specific `DailyPortfolioPricingPolicy` to daily
position marks. Each `DailyPortfolioPriceMark` carries quantity, cost price, mark price, source,
evidence reference, optional account scope, and instrument type so listed close prices, broker
quotes, and OTC model marks keep their audit trail. The projector returns per-position valuation
rows plus balanced fair-value adjustment lines: gains debit the security carrying account and
credit scoped unrealized gain; losses debit scoped unrealized loss and credit the security carrying
account. It does not fetch market data or approve valuation policy; those remain provider,
governance, and workflow responsibilities.

### `FixedIncomeAmortizationProjector`

`FixedIncomeAmortizationProjector` produces balanced journal-line drafts for fixed-income coupon
accrual, discount accretion, and premium amortization. Coupon accrual debits accrued interest
receivable and credits coupon income. Discount accretion increases the carrying-value asset and
credits coupon income. Premium amortization debits coupon income and credits the carrying-value
asset, reducing interest income over the amortization period.

Structured cash flow projections reach this projector through
`StructuredCashFlowLedgerBridge` (see
[Structured cash flow → ledger bridge](#structured-cash-flow--ledger-bridge)), which maps a fresh,
base-scenario projection's per-period interest into coupon-accrual inputs. The bridge is the
production caller for the coupon-accrual path; discount/premium amortization inputs remain available
for callers that carry cost-basis evidence.

### `LedgerAccountTaxLotPolicyBook`

`LedgerAccountTaxLotPolicyBook` resolves tax-lot relief policy at the `LedgerAccount` boundary.
It keeps ledger accounting aware of whether a security account should use FIFO, LIFO, HIFO, or
specific-lot identification without adding a dependency from `Meridian.Ledger` back to execution
lot selectors. Execution and front-office lot engines can map the ledger enum to their selector
implementations while report and close workflows retain account-level policy evidence.

### `LedgerTaxLotReliefProjector`

`LedgerTaxLotReliefProjector` applies the resolved account-level relief method to open ledger tax
lots and returns the accounting projection for a sale. FIFO, LIFO, HIFO, and SpecificId all share
the same validation path: the sale quantity must be positive, sale price cannot be negative, and
open lots must cover the requested relief quantity. SpecificId requires explicit lot identifiers
and rejects unknown or duplicate selections.

The projector permits partial-lot relief, rounds per-lot cost basis to currency precision, debits
scoped cash for proceeds, credits the security carrying account for cost basis, and posts the
realized gain or loss to the scoped gain/loss account. It is intentionally deterministic and
in-memory; persistence and approval remain responsibilities of the storage and workflow layers,
while report packs can export supplied projections for tax-reporting evidence.

The PostgreSQL ledger store now owns the durable inputs for this projection. `ILedgerJournalStore`
persists account-scoped tax-lot policies and open tax lots by ledger book/account, including
FIFO/LIFO/HIFO/SpecificId method, effective-date policy evidence, original/open quantity, unit cost,
currency, optional source journal entry, and evidence reference. The store does not perform relief
selection or approve tax outcomes; application/workflow code must load the policy and open lots,
run the ledger projector, and route the resulting journal through the governed posting path.

### `AutomatedJournalDraftProjector`

`AutomatedJournalDraftProjector` turns normalized recurring/lifecycle events into balanced journal
drafts before approval or durable posting. The first supported event set covers dividend
declarations, dividend receipts, cash-interest credits, corporate-action income, and
corporate-action expenses. It also accrues recurring obligations for management fees, performance
fees, commissions, and withholding taxes by debiting the expense account and crediting a scoped
payable account instead of assuming immediate cash settlement. Drafts include normalized metadata
and optional source-event tags so approval, reconciliation, and reporting flows can trace the
automated journal back to the upstream event.

`AutomatedJournalApproval` is the governed handoff from a projected draft into the ledger. It
records submit, approve, reject, and post transitions with actor, reason, timestamp, and evidence
links. Approval and posting require evidence, rejected drafts cannot be posted, and approved drafts
convert to a `JournalEntry` with stable journal/line identifiers plus audit tags for source event,
approval ID, approval status, and approver. The aggregate posts to the in-memory `Ledger`; durable
storage and operator workflow queues remain application/storage responsibilities.

Direct-lending accrual reversals use the same adjustment discipline. `LoanAccountingProjector`
keeps originating accrual projections limited to open accounting periods, but resolves reversal
projections with `LedgerPostingKindDto.Adjustment` so approved reversals can target soft-closed
periods and then pass through the central ledger posting guard for final approval and period-state
validation.

Direct-lending command persistence is transactionally coupled to ledger journal persistence.
`PostgresDirectLendingCommandService` projects ledger-impacting drawdown, accrual, receipt,
discount/premium amortization, restructuring, and write-off events before calling
`IDirectLendingStateStore.SaveAsync`; the generated loan event id is carried as the ledger
`SourceEventId`. `PostgresDirectLendingStateStore` then appends those `LedgerJournalEntryWrite`
records through `ITransactionalLedgerJournalStore` using the same `NpgsqlConnection` and
serializable `NpgsqlTransaction` as the loan state, event, projection, outbox, and snapshot writes.
If the ledger append fails, the loan event append is not committed.

`DailyAccrualWorker` applies the same period-state discipline before it posts recurring daily
accruals. When the worker can resolve the loan's ledger-book period scope, it calls
`LedgerInterop.CheckPostingDate` for the accrual date as an originating posting. A blocked period
does not fall through to `PostDailyAccrualAsync`; the worker logs the block and upserts a
`LedgerPeriodClose` operator-inbox item in the Accounting workspace with
`/accounting/reconciliation` and `FundReconciliation` navigation so controllers can resolve the
period issue from the normal reconciliation workbench.

### `LockedAccountingPeriodBook`

`LockedAccountingPeriodBook` is the in-memory, book-key-scoped period-lock companion for
`ProjectLedgerBook`. It records immutable `LockedAccountingPeriod` audit facts with period range,
lock owner, lock timestamp, and reason, then guards `Post` and `PostLines` calls before the target
ledger mutates. Locks are scoped by `LedgerBookKey`, so a published actuals period can reject late
May journals while a separate shadow-NAV or scenario ledger remains available for independent
validation. Overlapping locks for the same book are rejected to keep close evidence unambiguous.

### `ShadowNavValidator`

`ShadowNavValidator` compares a published/actual ledger book to an independent shadow-NAV ledger
book at a selected close timestamp. It builds point-in-time financial statements for both books,
uses net assets (`assets - liabilities`) as NAV, and emits account-level variance findings from the
two trial balances. `ShadowNavValidationPolicy` keeps NAV and account tolerances plus reviewer
metadata with the report. When a variance exceeds tolerance, the report can produce a
`ShadowNavOverrideDraft` that carries the policy, reviewer group, variance evidence, and source
ledger keys into the later approval workflow without mutating either book.

### `PartnershipInvestorAccountingProjector`

`PartnershipInvestorAccountingProjector` prepares partnership accounting journal drafts for
management fees, performance fees, high-water marks, and investor capital allocations. Inputs carry
the fund, period, beginning NAV, ending NAV before fees, high-water mark, fee rates, and investor or
feeder/SPV allocation weights. The projector calculates the management fee from beginning NAV,
performance fee from gains above the high-water mark after management fees, allocable profit/loss
after fees, and the updated high-water mark. Output lines are balanced: fee expenses offset fee
payables, profits debit retained earnings and credit investor capital, and losses debit investor
capital and credit retained earnings. This is a deterministic projection for review and posting; it
does not persist the journal or replace accountant-approved waterfall policy configuration.

### `PartnershipWaterfallProjector`

`PartnershipWaterfallProjector` handles tiered partnership distributions after period profit is
known. A `PartnershipWaterfallAllocationInput` carries the fund, period, distributable profit,
investors, and ordered waterfall tiers. Each tier can cap the amount it consumes and split that
tier across one or more investors, enabling preferred-return, catch-up, carried-interest, and
residual distribution structures for master-feeder, SPV, and series accounting. The projection
preserves tier-level allocation evidence, rolls allocations up to investor capital totals, and
emits balanced retained-earnings-to-investor-capital journal lines.

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

## Structured cash flow → ledger bridge

Structured cash flow projections (`StructuredCashFlowProjectionDto`) were historically display-only:
the projection math probed instrument terms by fuzzy JSON key aliases, treated the factor schedule
as a free-text field, logged staleness without enforcing it, and served only the workstation UI. The
cash flow projection path now closes those gaps:

- **Typed term resolution.** `StructuredCashFlowTermsResolver` resolves a security's raw term JSON
  into a strongly typed `StructuredCashFlowTerms` once, up front. All vendor key aliases (for
  example `par` / `originalFace` / `notional`) live in one documented, unit-tested place instead of
  being re-probed inline in the amortization math.
- **Typed factor schedule.** The resolver parses a `factorSchedule` array into typed
  `StructuredFactorScheduleEntry` points and seeds outstanding balance from the factor in effect on
  the projection date (`StructuredCashFlowTerms.FactorAsOf`), falling back to the scalar current
  factor. The typed schedule is surfaced on the projection DTO rather than remaining free text.
- **Staleness as a gate.** Each projection carries a typed `StructuredCashFlowStaleness` status
  computed from the source's last-updated timestamp. The status is advisory for UI display (stale
  projections are still returned, but flagged) and a hard gate for posting.
- **Ledger wiring.** `StructuredCashFlowLedgerBridge` converts a fresh, base-scenario projection's
  per-period interest into `FixedIncomeAmortizationProjector` coupon-accrual inputs and returns
  balanced `StructuredCashFlowLedgerPostingResult` journal postings. It refuses to post when the
  source is stale or the scenario is a rate-shocked what-if, mirroring the operator-approved,
  never-auto-post posture used elsewhere in the ledger. `ISecurityMasterCashFlowService.BuildLedgerPostingsAsync`
  is the reachable entry point; `StructuredCashFlowLedgerBridge` is registered in the Security Master
  service graph and is the production caller for the coupon-accrual projector path.

## Operations continuity workflow

`OperationsContinuityWorkflow` is the Financial Operations aggregate for the account-period
operations lane. It is scoped to a fund account, accounting period, optional Security Master
snapshot, and broker/custodian/bank source. The workflow models broker intake, Security Master,
ledger posting, reconciliation, and approval as explicit gates. UI clients consume the derived
status from `IOperationsStatusDerivationService`; they must not infer close readiness from local
component state.

The implemented continuity slice provides:

| Component | Responsibility |
|-----------|----------------|
| `OperationsContinuityWorkflowService` | Financial Operations command-driven workflow transitions, optimistic version checks, audit writes, DTO projection |
| `IOperationsContinuityRepository` | Financial Operations workflow snapshot persistence; file-backed by default, or PostgreSQL-backed through `PostgresOperationsContinuityStore` when `MERIDIAN_LEDGER_CONNECTION_STRING` is configured |
| `IOperationsWorkflowAuditStore` | Append-only audit timeline with previous/current SHA-256 hash chaining |
| `PostgresOperationsContinuityStore` | Financial Operations PostgreSQL implementation of `IOperationsContinuityRepository`, `IOperationsWorkflowAuditStore`, and `IOperationsContinuityTransactionalCommitStore`; successful ledger posts share the ledger journal transaction |
| `IOperationsContinuityTransactionalCommitStore` | Strict-atomicity commit seam for successful ledger posting: append journal, append workflow audit, and save workflow snapshot in one persistence boundary |
| `OperationsStatusDerivationService` | Financial Operations deterministic server-side overall status derivation from gate/sub-state posture |
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

### AccrualTypes

`LedgerInterop.ValidateAccrualEntry` and `BuildAccrualSummary` keep direct-lending accrual inputs
deterministic before they are projected into journals. The kernel validates required loan/source
event lineage, currency, period-slice dates, non-negative interest/fee/penalty amounts, and
aggregate version, then summarizes valid entries by loan, reporting period, and normalized
currency.

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

Ledger book, period, and closed-period reporting endpoints are exposed under `/api/ledger/`:

| Route | Description |
|-------|-------------|
| `GET /api/ledger/books` | Lists ledger books with optional fund, node, and accounting-basis filters |
| `POST /api/ledger/books` | Creates or returns the ledger book for a fund-structure node and accounting basis |
| `GET /api/ledger/periods` | Lists accounting periods with optional book, fund, node, status, open-only, and accounting-basis filters |
| `POST /api/ledger/periods` | Creates a period scoped to a ledger book |
| `POST /api/ledger/periods/{periodId}/close` | Performs soft-close or hard-close, computes close summary, and contributes a FundReconciliation operator-inbox work item |
| `GET /api/ledger/periods/{periodId}/trial-balance` | Returns closed-period trial-balance rows with accounting-basis and policy metadata |
| `GET /api/ledger/periods/{periodId}/pnl-summary` | Returns closed-period revenue, expense, net-income, prior-period variance, open-break count, and signoff posture |
| `GET /api/ledger/reports/trial-balance` | Returns cross-period closed-period trial-balance rows filtered by book, fund, node, accounting basis, and date range |
| `GET /api/ledger/reports/pnl-summary` | Returns cross-period closed-period revenue, expense, net-income totals, and per-period P&L summaries |

These endpoints are implemented in `WorkstationEndpoints.cs` and `LedgerEndpoints.cs`, then map to route constants in `UiApiRoutes`.

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
