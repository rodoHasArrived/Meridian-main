# Backtesting Engine — Code Review

**Date:** 2026-03-25
**Scope:** `src/Meridian.Backtesting/` — full subsystem
**Files reviewed:**
- `Engine/BacktestEngine.cs` (~479 lines)
- `Engine/MultiSymbolMergeEnumerator.cs`
- `Engine/ContingentOrderManager.cs`
- `FillModels/OrderBookFillModel.cs`
- `FillModels/BarMidpointFillModel.cs`
- `Portfolio/SimulatedPortfolio.cs` (~872 lines)
- `Metrics/BacktestMetricsEngine.cs` (~273 lines)
- `Metrics/XirrCalculator.cs`

**Reviewer:** AI audit (Claude Sonnet 4.6)
**Prior review context:** [CODE_REVIEW_2026-03-16.md](../../archive/docs/assessments/CODE_REVIEW_2026-03-16.md) covers the broader codebase; this review goes deep on the backtesting subsystem only.

---

## Summary

The backtesting engine is structurally sound: it uses `IAsyncEnumerable`, a correct priority-queue merge, proper `CancellationToken` propagation, and a double-entry ledger for trade accounting. `ContingentOrderManager`, `MultiSymbolMergeEnumerator`, and `XirrCalculator` are clean and correct.

There are however four issues that can produce **wrong results or crashes** in production backtests — these need to be fixed before the engine is used for consequential strategy decisions. There are also several performance and correctness gaps in the fill models and metrics engine that should be addressed as part of the Backtest Studio unification work.

---

## Critical (P0) — Can produce wrong results or crash a backtest run

### 1. `FilterBySymbolAndDate` uses `LocalDateTime` instead of UTC date — **timezone-dependent results**

**File:** `BacktestEngine.cs`
**Impact:** On any machine not running in UTC, date-boundary filtering is shifted. A backtest that requests data for `2024-01-15` on a UTC-5 machine will include events from `2024-01-15 00:00 UTC` through the `2024-01-15 19:00 UTC` boundary (local midnight), effectively cutting the trading day short and silently dropping after-hours data. Results are non-reproducible across machines in different timezones.

**Root cause:** `evt.Timestamp.LocalDateTime.Date` converts the `DateTimeOffset` to local machine time before taking the date. Market data timestamps are stored as UTC-based `DateTimeOffset` values.

**Fix:** Use `evt.Timestamp.UtcDateTime.Date` or `evt.Timestamp.Date` (which returns UTC date from a `DateTimeOffset`). Ensure all date-range comparisons in the engine use the same UTC convention.

---

### 2. `Task.Yield()` in `ProcessDayEndAsync` carries a misleading and incorrect comment

**File:** `BacktestEngine.cs`
**Impact:** The comment says "allow UI thread to breathe during long replays." The backtesting engine is server-side async code with no UI thread. `Task.Yield()` on the server yields to the `ThreadPool` scheduler — it does not help a UI thread. In a long backtest, this inserts an unnecessary task scheduler round-trip at every day boundary, adding latency for no correctness or responsiveness benefit.

This pattern is cargo-culted from WPF or similar UI contexts where `Task.Yield()` genuinely yields to the dispatcher loop. It's harmless here, but misleading enough to confuse future maintainers into thinking the engine has a UI dependency.

**Fix:** Remove `await Task.Yield()` from `ProcessDayEndAsync`. If cooperative cancellation at day boundaries is the intent, a `ct.ThrowIfCancellationRequested()` call serves that purpose explicitly.

---

### 3. `ProcessFill` throws `InvalidOperationException` for domain violations with no catch in the engine loop

**File:** `BacktestEngine.cs` + `SimulatedPortfolio.cs`
**Impact:** If a strategy attempts to short-sell a symbol that is not in `_shortableSymbols`, or exceeds a margin constraint, `ProcessFill` throws `InvalidOperationException`. The engine's main replay loop (`await foreach` in `RunAsync`) does not catch this exception. The entire backtest run crashes rather than rejecting the order and continuing.

This turns a domain-level constraint violation (strategy tried an illegal trade) into an unhandled exception that bubbles up to the caller as a backtest failure, indistinguishable from infrastructure errors.

**Fix:** Catch domain-level exceptions from `SimulatedPortfolio.ProcessFill` within the engine's fill dispatch, log the rejected fill with reason, and continue the replay loop. Alternatively, introduce a `ProcessFillResult` return type that signals rejection without throwing.

---

### 4. Recovery day calculation in `ComputeMaxDrawdown` is convoluted and semantically fragile

**File:** `BacktestMetricsEngine.cs`
**Impact:** The recovery threshold is expressed as `troughEquity / (1m - maxDdPct)`. Algebraically this equals the peak equity value (since `maxDdPct = (peak - trough) / peak`), so the formula is mathematically correct for the *global* max drawdown. However, it is correct only when `maxDdPct` is the drawdown from the peak that produced the specific trough being measured. If the code is ever modified to compute per-period drawdowns or rolling drawdowns, this formula will silently produce wrong recovery day counts.

It is also non-obvious to the reader that the expression equals "peak equity." A reviewer must derive the algebra to verify correctness.

**Fix:** Track the equity peak value explicitly during the drawdown scan (which the code already visits sequentially), and use `troughEquity's corresponding peak` as the recovery threshold directly. This makes the intent clear and eliminates the algebraic dependency.

---

## High (P1) — Correctness and design issues that degrade backtest quality

### 5. Win/loss trade statistics use sign-based heuristic instead of round-trip pairing

**File:** `BacktestMetricsEngine.cs` — `ComputeTradeStats`
**Impact:** Each `FillEvent` is counted as an independent "trade." A strategy that enters a position in three partial fills and exits in two fills is counted as five trades, not one round-trip. Win/loss classification is based on the sign of the realised PnL of each individual fill, which is meaningless for partial fills of the same order. A large entry fill on a down day could be classified as a "loss" even if the complete round-trip is profitable.

This inflates trade count, distorts win rate, and makes the `AverageTrade` metric unreliable. The comment in the code acknowledges this: *"For simplicity: each fill represents a 'trade'."*

**Fix:** Pair fills into round-trips using the same FIFO lot logic already implemented in `SimulatedPortfolio.RealiseFifo`. A round-trip is complete when a lot opened by a buy fill is closed by a sell fill (or vice versa for short positions). Win/loss is determined at round-trip level, not fill level.

---

### 6. Two independent FIFO lot implementations — divergence risk

**File:** `SimulatedPortfolio.cs` (`RealiseFifo`/`RealiseShortFifo`) and `BacktestMetricsEngine.cs` (`ComputeRealisedPnl`)
**Impact:** Both implementations independently walk FIFO lot queues to compute realised PnL. If the portfolio's lot pairing logic is ever corrected for a bug or edge case (e.g., fractional shares, lot splitting on corporate actions), the metrics engine's attribution calculation will not automatically benefit. They can produce different realised PnL figures for the same trade sequence if either implementation has a subtle difference.

**Fix:** Expose lot-pairing as a shared utility (e.g., move to `SimulatedPortfolio` as a query method or extract to a shared `FifoPairer` class), and have `BacktestMetricsEngine.ComputeRealisedPnl` call it rather than re-implementing.

---

### 7. FIFO lot splitting creates O(n) queue allocations per partial fill

**File:** `SimulatedPortfolio.cs` — `RealiseFifo` and `RealiseShortFifo`
**Impact:** When a partial fill partially consumes a lot, the code reconstructs the lot queue using `.Skip(1).Prepend(splitLot)`. This creates a new LINQ iterator chain and, when materialised into the `Queue<T>` constructor, allocates a new queue for every partial lot split. For a backtest with high-frequency trading or many partial fills, this generates continuous GC pressure.

**Fix:** Replace the `Queue<(long, decimal)>` with a `List<(long, decimal)>` and track a `startIndex` offset. Partial lot consumption updates the entry at `startIndex` in-place without allocation. Alternatively, use `ArrayDeque<T>` from a pooled collection for FIFO access with O(1) dequeue and in-place head mutation.

---

### 8. Hardcoded 4% risk-free rate in metrics engine

**File:** `BacktestMetricsEngine.cs` — `private const decimal RiskFreeRate = 0.04m;`
**Impact:** Sharpe and Sortino ratios are computed against a fixed 4% annual risk-free rate. For backtests spanning different interest rate regimes (e.g., 2010–2015 near-zero rates vs. 2023 5%+ rates), this produces systematically wrong risk-adjusted return figures. The comment says "configurable in future" — this is the future.

**Fix:** Accept `RiskFreeRate` as a parameter in `BacktestRequest` (it already has an `Options` bag) or add it to `BacktestMetricsEngine`'s constructor. Default to 0.04 for backwards compatibility.

---

### 9. `BuildAggregatePositions` double-calls `Math.Abs(position.Quantity)` redundantly

**File:** `SimulatedPortfolio.cs` — `BuildAggregatePositions` around lines 724–727
**Impact:** Minor redundancy, not a performance concern at this scale, but signals a copy-paste issue. Two consecutive `Sum(Math.Abs(position.Quantity))` calls on the same sequence compute the same value twice. One should be used directly.

**Fix:** Compute the sum once and store in a local variable.

---

## Medium (P2) — Performance and fill model fidelity

### 10. `OrderBookFillModel.FilterExecutableLevels` allocates on every fill attempt

**File:** `OrderBookFillModel.cs`
**Impact:** The LINQ `.Where().ToList()` call in `FilterExecutableLevels` allocates a new `List<T>` on every call to `TryFill`. In a tick-level backtest with millions of events, this method is called for every pending order against every LOBSnapshot, generating sustained allocation pressure.

**Fix:** Return an `IEnumerable<T>` from a `Where` without materialising, or pass a `Span<T>` / pre-filtered buffer. If the fill model is called in hot loops, consider pre-sorting levels at LOBSnapshot construction time and using binary search to find the executable range without allocation.

---

### 11. `OrderBookFillModel.IsTriggered` allocates via `.OrderBy().FirstOrDefault()`

**File:** `OrderBookFillModel.cs`
**Impact:** For stop order trigger evaluation, the code calls `.OrderBy(l => l.Price).FirstOrDefault()` on the LOB levels. This is O(n log n) and allocates a sorted array on every call. Finding the best bid/ask price requires only a `Min`/`Max` scan, not a full sort.

**Fix:** Replace with `.MinBy(l => l.Price)` or `.MaxBy(l => l.Price)` (O(n), no allocation on spans in .NET 9).

---

### 12. `BarMidpointFillModel` uses open/close midpoint rather than range midpoint for market fills

**File:** `BarMidpointFillModel.cs`
**Impact:** Market order fill price is computed as `(bar.Open + bar.Close) / 2m`. This is the average of the opening and closing prices, which represents the bar's directional trend midpoint, not its intraday price range midpoint. For an up-bar where `Open = 100, Close = 110, Low = 99, High = 112`, the fill price is `105` — but the intraday range midpoint is `(99 + 112) / 2 = 105.5`, and VWAP would likely be in that neighbourhood too. The two are often close but diverge on strongly trending or gap bars.

This is an undocumented modelling choice. It is not wrong per se, but should be documented in the summary comment so strategy developers understand their fill assumption.

**Fix:** Add a note to the XML doc comment explaining the open/close midpoint convention and when to use `BarMidpointFillModel` vs. `OrderBookFillModel`. Consider offering `(bar.High + bar.Low) / 2m` as an alternative mode.

---

### 13. Spread-aware slippage multiplier (50×) is undocumented and uncalibrated

**File:** `BarMidpointFillModel.cs` — line 95
**Impact:** The expression `slippageBasisPoints * (1m + volatilityFactor * 50m)` applies a 50× amplifier to the bar's intraday volatility ratio. For a 2% bar range, this adds 100 basis points (1%) of additional slippage on top of the base. This is a significant model assumption with no calibration basis documented. A strategy developer enabling `spreadAware = true` could be penalised with unrealistically large slippage on volatile days.

**Fix:** Document the 50× factor's origin (empirical calibration? literature reference? rule of thumb?). Alternatively, expose the multiplier as a constructor parameter so it can be tuned per strategy.

---

### 14. `MultiSymbolMergeEnumerator` has non-deterministic ordering for simultaneous timestamps

**File:** `MultiSymbolMergeEnumerator.cs`
**Impact:** When two symbols emit events with identical millisecond timestamps, the priority queue resolves ties using the internal heap ordering, which is non-deterministic for equal-priority elements in .NET's `PriorityQueue<T, T>`. Backtest results may not be exactly reproducible when re-run with the same data if tie-breaking order changes between runs.

**Impact is minor** in practice since millisecond-identical cross-symbol events are rare in OHLCV bar data, but could matter for tick-level replay of correlated instruments.

**Fix:** Use a secondary sort key (e.g., symbol index `i`) as a tiebreaker in the priority queue. Change the priority type from `long` to `(long timestamp, int symbolIndex)` with a custom comparer.

---

## Low (P3) — Style, documentation, and minor gaps

### 15. Missing `ConfigureAwait(false)` on library async calls

**Files:** `BacktestEngine.cs`, `MultiSymbolMergeEnumerator.cs`
**Context:** The backtesting engine is called from both the web API (ASP.NET Core, where `ConfigureAwait(false)` is standard) and potentially from test harnesses. Missing `ConfigureAwait(false)` is not a deadlock risk in ASP.NET Core (which has no synchronisation context), but is a best-practice gap for library code.

**Fix:** Add `ConfigureAwait(false)` to all `await` calls in the backtesting subsystem.

---

### 16. `XirrCalculator` bisection upper bound may be too restrictive for short-duration cash flows

**File:** `XirrCalculator.cs` — `Bisect(Npv, -0.999, 10.0)`
**Impact:** The upper bound of `10.0` (1,000% annualised return) is sufficient for most backtests. For very short time windows (days) or extreme strategies, the XIRR could theoretically exceed this range, causing the calculator to return `NaN` silently. The only signal is the `NaN` return value — no logging or diagnostic.

**Fix:** Log a warning when the Newton-Raphson fallback reaches bisection, and log the cash flow summary when bisection also fails, so diagnostics are available for extreme edge cases.

---

### 17. `ContingentOrderManager.ReconcileOcoSiblings` proportional reduction is correct but worth documenting

**File:** `ContingentOrderManager.cs`
**Context:** When one OCO leg partially fills, the sibling's quantity is reduced by the same absolute amount. This is correct for standard bracket orders on the same symbol. However, the comment does not explain the design decision — a reader might expect full cancellation of the sibling on any fill.

**Fix:** Add an inline comment explaining that partial OCO fills result in proportional sibling reduction, not full cancellation, to match the parent order's partial fill semantics.

---

## Test Coverage Gaps

The existing test suite in `Meridian.Backtesting.Tests/` covers the happy path well. The following scenarios lack explicit coverage:

- **Timezone edge cases:** A test that runs a backtest from a machine-local-date perspective and verifies that date boundaries match UTC dates, not local dates (issues P0/1)
- **Domain exception handling:** A test that exercises a short-sell attempt on a non-shortable symbol and verifies the engine continues rather than crashing (P0/3)
- **Recovery day correctness:** A test with a known equity curve (pre-computed drawdown and recovery days) that verifies `ComputeMaxDrawdown` returns the expected recovery day count
- **Round-trip trade statistics:** A test that verifies `ComputeTradeStats` counts a 3-fill entry + 2-fill exit as 1 round-trip trade, not 5 separate trades (P1/5)
- **FIFO divergence:** A test that runs the same cash flows through both `SimulatedPortfolio.RealiseFifo` and `BacktestMetricsEngine.ComputeRealisedPnl` and asserts they produce identical results (P1/6)

---

## What Works Well

These elements are clean, correct, and should be preserved as-is:

- **`MultiSymbolMergeEnumerator`** — priority queue with proper async disposal is the correct approach; O(log n) merge is efficient
- **`XirrCalculator`** — Newton-Raphson with bisection fallback is industry-standard; convergence tolerance and iteration limits are well-chosen
- **`ContingentOrderManager`** — clean static class, correct OCO group logic, proper parentage guard against recursive contingent creation
- **`SimulatedPortfolio` double-entry ledger posting** — posting every trade, commission, corporate action, and margin charge through the ledger is architecturally correct and enables proper reconciliation
- **`CancellationToken` propagation** — the engine correctly threads the token through `RunAsync`, `BuildSymbolStreams`, `MultiSymbolMergeEnumerator.MergeAsync`, and fill model dispatch
- **`OrderBookFillModel` partial fill and FOK/IOC semantics** — correctly walks LOB levels, respects time-in-force constraints, cancels FOK on insufficient depth, partial-fills IOC and cancels the remainder
- **`BarMidpointFillModel` stop trigger logic** — `bar.High >= stopPrice` for buy stops and `bar.Low <= stopPrice` for sell stops is the standard conservative approach for bar-level stop simulation

---

## Priority Summary

| # | Finding | Severity | Effort |
|---|---------|----------|--------|
| 1 | `LocalDateTime` in date filter — timezone-dependent results | P0 | Low |
| 2 | `Task.Yield()` "UI thread" comment — misleading server-side pattern | P0 | Low |
| 3 | `InvalidOperationException` from `ProcessFill` — crashes backtest run | P0 | Medium |
| 4 | Recovery day formula — algebraically fragile and non-obvious | P0 | Low |
| 5 | Fill-level win/loss — inflated trade count, wrong win rate | P1 | High |
| 6 | Duplicate FIFO implementations — divergence risk | P1 | Medium |
| 7 | Queue allocation per partial lot split — GC pressure | P1 | Medium |
| 8 | Hardcoded 4% risk-free rate | P1 | Low |
| 9 | Redundant `Math.Abs` double-sum | P1 | Low |
| 10 | `FilterExecutableLevels` LINQ allocation per fill | P2 | Low |
| 11 | `IsTriggered` O(n log n) sort for stop evaluation | P2 | Low |
| 12 | Open/close midpoint convention undocumented | P2 | Low |
| 13 | Spread-aware 50× multiplier undocumented | P2 | Low |
| 14 | Non-deterministic tie-breaking in merge | P2 | Low |
| 15 | Missing `ConfigureAwait(false)` | P3 | Low |
| 16 | XIRR bisection failure is silent | P3 | Low |
| 17 | OCO proportional reduction undocumented | P3 | Low |

**Recommended immediate fixes (P0):** Issues 1, 2, 3, 4 — all low-to-medium effort, directly affect result correctness or crash surface.

**Recommended before Backtest Studio unification:** Issues 5, 6, 8 — these affect the metrics model that the unified studio will surface. Fixing them before the UI is built avoids surfacing wrong metrics to operators.
