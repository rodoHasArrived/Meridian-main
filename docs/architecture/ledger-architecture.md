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

All three endpoints are implemented in `WorkstationEndpoints.cs` and map to route constants in `UiApiRoutes`.

---

## Dependency injection

`LedgerFeatureRegistration` (registered unconditionally by `ServiceCompositionRoot.AddMarketDataServices`) contributes:

| Service | Lifetime | Notes |
|---------|----------|-------|
| `ProjectLedgerBook` | Singleton | Keyed ledger namespace for the host process |

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
