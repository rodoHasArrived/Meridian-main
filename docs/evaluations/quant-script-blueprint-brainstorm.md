# QuantScriptEnvironment Blueprint — Brainstorm & Critical Evaluation

**Date:** 2026-03-18 (updated 2026-04-16) | **Mode:** Problem-Focused + UX/Information Design
**Input:** `docs/plans/quant-script-environment-blueprint.md` (v1 blueprint)
**Focus:** (1) Design improvements, (2) Usefulness audit, (3) Interface maximisation

> **Previous sessions covered:** L3 inference, Python SDK, DuckDB, symbol health, VWAP/TWAP algorithms, provider SLA. **This session:** First deep evaluation of the QuantScript blueprint — covering gaps in the design, honest usefulness scoring, and interface-level rethinking to maximise versatility and ease of use across all three personas.

---

## 2026-04-16 Update — Blueprint Decisions for Implementation Readiness

This update converts the brainstorm into explicit go/no-go decisions so implementation can begin without re-litigating scope during execution.

### Locked for MVP (ship in first implementation wave)

1. **Async-native script surface (`await` first).**
   - Keep synchronous helpers only as thin convenience wrappers.
   - All templates should demonstrate `await` by default.
2. **Fluent `BacktestBuilder` path.**
   - Keep `IBacktestStrategy` as an advanced escape hatch.
   - MVP must support `.OnBar(...).From(...).To(...).WithCash(...).RunAsync()`.
3. **Flattened series-first DSL.**
   - `PriceSeries`, `NumericSeries`, and `ReturnSeries` methods should handle the most common analysis flow directly.
   - `Stats` remains for multi-series and niche calculations.
4. **Template gallery with beginner-first scripts.**
   - “Hello SPY” and one backtest template are mandatory acceptance items.
5. **Run history persistence with side-by-side comparison of key metrics.**
   - MVP stores metrics + equity curve and supports at least two-run comparisons.

### Deferred (explicitly out of MVP)

- **Cell-based execution model** (high value, but treated as Phase 2 UI re-architecture).
- **Full `AlignedSeries` outer-join + advanced alignment semantics** (inner-join-first only).
- **2D optimization heatmap UX polish** (basic parameter sweep table is acceptable in MVP).

### Acceptance checks added for blueprint handoff

- A first-time user can load and plot SPY in under **2 minutes** using a template.
- A user can run and compare two SMA backtests without writing an `IBacktestStrategy` class.
- At least one long-running script shows progressive log output before completion.
- Persisted run records survive app restart and can be re-opened in the comparison view.

### Why this update

The original brainstorm intentionally explored a wide option space. This addendum narrows scope to a deliverable baseline while preserving the highest-leverage interaction improvements (async flow, low-boilerplate backtests, and reusable run analysis).

---

## Ideas at a Glance

| # | Idea | Category | Effort | Audience | Impact |
|---|------|----------|--------|----------|--------|
| 1 | Cell-Based Execution Model (steal from Jupyter) | Design | M | H, Q | Very High |
| 2 | Async-Native Scripts with `await` | Design | S | H, Q, I | High |
| 3 | Fluent Backtest Builder (kill the boilerplate) | Interface | M | H, Q | Very High |
| 4 | DataFrame-Like `Series` with Join/Align/Merge | Interface | L | Q, I | High |
| 5 | "First 5 Minutes" Template Gallery | UX | S | H | Very High |
| 6 | Result Persistence & Run Comparison | Design | M | Q, I | High |
| 7 | Flatten the DSL — Method Chains over Nested Calls | Interface | S | H, Q, I | High |
| 8 | Real Parameter Sweep with `Optimize()` API | Design | M | Q, I | Medium-High |
| 9 | Live Script Output Streaming (not batch) | UX | M | H, Q | Medium-High |
| 10 | Script Import & Composition (`#load`) | Design | S | Q, I | Medium |

**Effort:** S = days, M = 1–2 weeks, L = 1+ month | **Audience:** H = Hobbyist, Q = Academic, I = Institutional

---

## Part 1: Usefulness Audit — What Actually Matters

Before proposing improvements, let's be honest about what in the blueprint will get daily use vs. what's furniture.

### Tier 1: Core Loop (will be used every session)

| Feature | Verdict | Why |
|---|---|---|
| `Data.Prices("SPY")` | **Essential** | This is the reason someone opens the page. If loading data isn't instant and obvious, nothing else matters. |
| `Plot.Line(...)` | **Essential** | Visual feedback is the entire point of an interactive environment. Every session ends with a chart. |
| `.SMA()`, `.EMA()`, `.RSI()` | **Essential** | Technical indicators are the bread-and-butter of the target audience. Wrapping Skender via extensions is exactly right. |
| AvalonEdit with syntax highlighting | **Essential** | The editor IS the feature. Without a good editor, this is just a worse version of a `.csx` file in VS Code. |
| Save/Load scripts | **Essential** | Users build a personal library over weeks. Losing scripts is unforgivable. |

### Tier 2: High Value (used weekly, drives retention)

| Feature | Verdict | Notes |
|---|---|---|
| `Test.Run(request, strategy)` — backtest bridge | **High value** | This is Meridian's differentiator vs. Jupyter — data collection + backtesting in one app. But the current interface is too verbose (see Idea #3). |
| `Stats.Compute(returns)` | **High value, but redundant** | `BacktestResult.Metrics` already computes Sharpe, Sortino, CAGR, MaxDD, Calmar. The standalone `StatisticsEngine` duplicates this for non-backtest use cases (pure data analysis). Keep it, but make the backtest path use the existing engine and only expose `StatisticsEngine` for ad-hoc series analysis. |
| Parameter sidebar | **High value if auto-detected** | The `[ScriptParam]` attribute + auto-generated UI is genuinely good design. But it's only useful if parameter extraction works reliably — Roslyn syntax tree walking for attributes on globals is fragile. Consider a simpler convention: `var lookback = Param("Lookback", 20, min: 5, max: 100);` as a runtime call that self-registers. |

### Tier 3: Occasional Use (monthly, nice-to-have)

| Feature | Verdict | Notes |
|---|---|---|
| `CorrelationMatrix` | **Niche** | Useful for portfolio construction but most users won't touch it. Keep it in the API but don't build UI for it. |
| `RollingBeta` | **Niche** | Same — available but not featured. |
| Histogram plot type | **Occasional** | Return distribution plots are common in research papers, rare in daily work. |
| Candlestick plot type | **Occasional** | Contradicts the "analysis" framing — candlestick charts are for trading screens, not script output. Keep it but don't prioritise. |

### Tier 4: Likely Unused (cut or defer)

| Feature | Verdict | Why |
|---|---|---|
| `PlotType.Area` | **Cut** | Almost never used in quant analysis. Line covers the same need. |
| `ScriptParamAttribute` (compile-time attribute) | **Replace** | Fragile — requires Roslyn tree walking, breaks on syntax errors, doesn't work for runtime-discovered params. Replace with runtime `Param()` call (see Idea #7). |
| Skewness / Kurtosis in `PortfolioStatistics` | **Defer** | Higher moments are academic. They can be added as extension methods later without core API changes. |

### What's Painfully Missing

| Gap | Impact | Who Feels It |
|---|---|---|
| **No cell-based execution** | Very High | Everyone. Monolithic scripts are a regression from Jupyter. |
| **No `await` support** | High | Anyone loading multiple symbols. `DataProxy` uses `.GetAwaiter().GetResult()` — synchronous blocking disguised as convenience. |
| **No result persistence** | High | Academics who need to compare runs across days. |
| **No backtest comparison** | High | Anyone iterating on strategy parameters. |
| **No guided first experience** | Very High | Hobbyists who open a blank editor and freeze. |
| **No universe filtering** | Medium | QuantConnect's `AddUniverse` equivalent — screen symbols before analysis. |
| **No progress feedback during long scripts** | Medium | Anyone loading 10+ years of data for multiple symbols. |

---

## Part 2: Design Improvements — 10 Ideas

### Idea 1: Cell-Based Execution Model

**The problem.** The blueprint treats scripts as monolithic files: write code, press Run, see all results at once. This is how compiled programs work. It is _not_ how exploration works. Every successful quant scripting environment — Jupyter, QuantConnect's Research, Matlab's Live Editor, R Markdown — uses cells. The user writes a few lines, executes them, sees the result, then writes the next few lines. The feedback loop is measured in seconds, not minutes.

**The user moment.** A hobbyist opens QuantScript. They see a vertical stack of cells, the first pre-filled with `var spy = Data.Prices("SPY");`. They press Shift+Enter. Below the cell, a compact summary appears: "SPY: 2,517 bars (2016-01-04 to 2026-03-17)". They add a new cell: `Plot.Line("SPY", spy.Close);`. Shift+Enter. A chart renders inline below the cell. They keep going. Each cell builds on the previous state. No full-page "Run" required.

**The implementation shape.** Replace the monolithic AvalonEdit editor with a vertical `ItemsControl` of cell blocks. Each cell is an AvalonEdit instance (lightweight — they share syntax highlighting resources). Execution state (`ScriptState<object>`) is preserved between cells via Roslyn's `CSharpScript.RunAsync(... previousState)`. The `ScriptState` carries all variables and declarations forward.

Key changes to the blueprint:
- `IScriptRunner.RunAsync` gains a `ScriptState<object>? previousState` parameter.
- `ScriptExecutionResult` gains a `ScriptState<object> State` property for chaining.
- The ViewModel manages an `ObservableCollection<CellViewModel>` instead of a single `ScriptText`.
- Each `CellViewModel` has: `SourceCode`, `OutputText`, `OutputPlots`, `ExecutionState`, `ElapsedTime`.
- A "Run All" button still exists for full-script execution (iterates cells in order).

**The tradeoff.** More complex ViewModel. AvalonEdit instances consume memory (mitigate: virtualise off-screen cells). State management between cells requires careful error handling (what if cell 3 fails — do cells 4+ still hold stale state?). The standard answer: mark all cells after a failed cell as "stale" with a visual indicator.

**Competitive signal.** QuantConnect Research, Databento's Python notebooks, and every serious quant platform use cell-based execution. Not offering it positions Meridian as a toy. This is the single highest-impact design change.

**Effort:** M (1–2 weeks) | **Audience:** H, Q | **Impact:** Very High

---

### Idea 2: Async-Native Scripts with `await`

**The problem.** The blueprint's `DataProxy` wraps `IQuantDataContext` (async) in synchronous `.GetAwaiter().GetResult()` calls. The justification — "acceptable because scripts run on a background thread" — is technically correct but architecturally wrong. Roslyn's `CSharpScript` natively supports `await` in script context. Using it means scripts can load multiple symbols concurrently, show progress, and remain cancellable at every await point.

**The user moment.** Instead of:
```csharp
var spy = Data.Prices("SPY");   // blocks for 200ms
var aapl = Data.Prices("AAPL"); // blocks for 200ms — total 400ms serial
```
The user writes:
```csharp
var spy = await Data.PricesAsync("SPY");   // 200ms
var aapl = await Data.PricesAsync("AAPL"); // concurrent if using Task.WhenAll
// or even:
var (spy, aapl) = await Data.PricesAsync("SPY", "AAPL"); // parallel multi-load
```

**The implementation shape.** Remove `DataProxy` entirely. Expose `IQuantDataContext` directly on globals as `Data`. Since Roslyn scripts support top-level `await`, the script body is implicitly async. Change `IScriptRunner.RunAsync` to use `CSharpScript.Create<object>(...).RunAsync(globals)` which already handles async script bodies. Add a convenience `Data.PricesAsync(params string[] symbols)` that returns a tuple or dictionary for parallel loading.

The `CancellationToken` from globals is passed through naturally — every `await` is a cancellation checkpoint. No more `.GetAwaiter().GetResult()` deadlock risk.

**The tradeoff.** Users must write `await` — slightly more syntax. Mitigate with helper methods that hide async: `Data.Prices("SPY")` could still exist as a synchronous convenience that internally calls the async version, but the _recommended_ path is async. Templates should use `await` by default.

**Effort:** S (2–3 days) | **Audience:** H, Q, I | **Impact:** High

---

### Idea 3: Fluent Backtest Builder — Kill the Boilerplate

**The problem.** The blueprint requires users to implement `IBacktestStrategy` (8 callback methods!) and construct a `BacktestRequest` record manually. This is fine for a compiled project with full IDE support. It's terrible for a scripting environment where the user wants to test an idea in 10 lines. Compare QuantConnect Research, where you can backtest an idea in ~15 lines, or Backtrader where `cerebro.addstrategy(MyStrategy)` is the entire setup.

The current blueprint requires:
```csharp
class MyStrategy : IBacktestStrategy
{
    public string Name => "My Strategy";
    public void Initialize(IBacktestContext ctx) { }
    public void OnTrade(Trade trade, IBacktestContext ctx) { }
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
    public void OnBar(HistoricalBar bar, IBacktestContext ctx) {
        // actual logic here
    }
    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
    public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
    public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
    public void OnFinished(IBacktestContext ctx) { }
}
var result = Test.Run(new BacktestRequest(
    From: new DateOnly(2020, 1, 1),
    To: new DateOnly(2024, 12, 31),
    Symbols: ["SPY", "AAPL"],
    InitialCash: 100_000m
), new MyStrategy());
```

That's ~20 lines of ceremony for a simple moving-average crossover. The actual logic is 5 lines buried inside `OnBar`.

**The user moment.** With a fluent builder:
```csharp
var result = await Test.OnBar(["SPY"], (bar, ctx) =>
{
    var sma20 = ctx.Indicator(bar.Symbol, "SMA", 20);
    var sma50 = ctx.Indicator(bar.Symbol, "SMA", 50);
    if (sma20 > sma50) ctx.SetTarget(bar.Symbol, 1.0m);
    else ctx.SetTarget(bar.Symbol, 0.0m);
})
.From(2020, 1, 1)
.To(2024, 12, 31)
.WithCash(100_000m)
.RunAsync();

Plot.Line("Equity", result.EquityCurve);
Log($"Sharpe: {result.Metrics.SharpeRatio:F2}");
```

Ten lines. Zero boilerplate. The user focuses entirely on their idea.

**The implementation shape.** Add a `BacktestBuilder` class to `Meridian.QuantScript`:

```csharp
public sealed class BacktestBuilder
{
    // Fluent configuration
    public BacktestBuilder From(int year, int month, int day);
    public BacktestBuilder To(int year, int month, int day);
    public BacktestBuilder WithCash(decimal amount);
    public BacktestBuilder WithSymbols(params string[] symbols);

    // Lambda-based strategy (no class required)
    public BacktestBuilder OnBar(string[] symbols, Action<HistoricalBar, IBacktestContext> handler);
    public BacktestBuilder OnTrade(Action<Trade, IBacktestContext> handler);
    public BacktestBuilder OnDayEnd(Action<DateOnly, IBacktestContext> handler);

    // Execution
    public Task<BacktestResult> RunAsync(CancellationToken ct = default);
}
```

Internally, `BacktestBuilder` creates a `LambdaBacktestStrategy : IBacktestStrategy` that dispatches to the registered lambdas. The `Test` global becomes a `BacktestBuilder` factory: `Test.OnBar(...)` returns a new builder.

The full `IBacktestStrategy` interface remains available for power users who want class-based strategies with state. The builder is the "easy mode" on-ramp.

**The tradeoff.** The lambda approach can't easily share state between callbacks (e.g., `OnBar` and `OnDayEnd`). Solution: `BacktestBuilder` accepts an optional `state` object or closure-captured variables work naturally in C# lambdas.

**Effort:** M (1 week) | **Audience:** H, Q | **Impact:** Very High

---

### Idea 4: DataFrame-Like Series with Join/Align/Merge

**The problem.** `PriceSeries` is a good start but it's a single-symbol, single-type container. Real quant workflows constantly need to align multiple series by date, join price data with indicator data, merge across symbols, and filter by conditions. The blueprint forces users to manually iterate and index-match.

**The user moment.** A researcher wants to compute the spread between SPY and IWM normalised by their rolling volatility:
```csharp
var spy = await Data.PricesAsync("SPY");
var iwm = await Data.PricesAsync("IWM");

// Current blueprint: manual alignment by date — painful
// Proposed: DataFrame-like operations
var aligned = spy.AlignWith(iwm);  // inner join on dates
var spread = aligned["SPY"].Close - aligned["IWM"].Close;
var normSpread = spread / spread.RollingStd(20);

Plot.Line("Normalised SPY-IWM Spread", normSpread);
```

**The implementation shape.** Add an `AlignedSeries` type that holds multiple `PriceSeries` aligned to a common date index:

```csharp
public sealed class AlignedSeries
{
    public IReadOnlyList<DateOnly> Dates { get; }
    public PriceSeries this[string symbol] { get; }

    // Alignment modes
    public static AlignedSeries InnerJoin(params PriceSeries[] series);
    public static AlignedSeries OuterJoin(params PriceSeries[] series); // NaN-fill gaps
}
```

Add a `NumericSeries` type for derived calculations:
```csharp
public sealed class NumericSeries
{
    public IReadOnlyList<DateOnly> Dates { get; }
    public IReadOnlyList<decimal> Values { get; }

    // Operators
    public static NumericSeries operator +(NumericSeries a, NumericSeries b);
    public static NumericSeries operator -(NumericSeries a, NumericSeries b);
    public static NumericSeries operator *(NumericSeries a, decimal scalar);
    public static NumericSeries operator /(NumericSeries a, NumericSeries b);

    // Rolling stats
    public NumericSeries RollingMean(int window);
    public NumericSeries RollingStd(int window);
    public NumericSeries Cumulative(); // cumulative sum

    // Filtering
    public NumericSeries Where(Func<decimal, bool> predicate);
}
```

`PriceSeries.Close`, `.Open`, etc. return `NumericSeries` instead of `IReadOnlyList<decimal>`, enabling operator chaining. This is the pandas `Series` pattern adapted to C#.

**The tradeoff.** Significant API surface. Operator overloading in C# can produce confusing error messages. Date alignment logic is surprisingly tricky (holidays, half-days, different exchange calendars). Start with inner-join only; add outer-join in v2.

**`decimal` vs `double` note.** `NumericSeries` uses `decimal` to preserve the precision of price data sourced from `PriceSeries` (which is already `decimal`). Libraries such as `Skender.Stock.Indicators` accept `double` inputs; callers that bridge to such libraries must explicitly cast (`(double)value`) at the adapter boundary rather than silently losing precision inside the core series type.

**Effort:** L (2–3 weeks) | **Audience:** Q, I | **Impact:** High

---

### Idea 5: "First 5 Minutes" Template Gallery

**The problem.** The blueprint mentions a "default script template (momentum crossover example)" in Phase 4. One template is not enough. A blank editor with a single example is the number one reason people close a tool and never come back. The first 5 minutes determine adoption.

**The user moment.** The user opens QuantScript for the first time. Instead of a blank editor, they see a gallery sidebar (or modal) with 8–10 categorised templates:

**Quick Start (< 10 lines each):**
1. **"Hello SPY"** — Load SPY, plot the close, print basic stats. The minimal viable script.
2. **"Moving Average Crossover"** — SMA(20) vs SMA(50) on any symbol, plot with crossover markers.
3. **"Volatility Snapshot"** — Load 5 symbols, compute 30-day rolling vol, plot on one chart.

**Analysis (15–30 lines):**
4. **"Correlation Heatmap"** — Load tech stocks, compute pairwise correlation, display matrix.
5. **"Bollinger Band Mean Reversion"** — Bollinger Bands with z-score entry signals marked.
6. **"Sector Rotation"** — Compare returns across sector ETFs (XLF, XLK, XLE, XLV), rank by momentum.

**Backtesting (20–40 lines):**
7. **"Simple Momentum Backtest"** — Long top-3 by 12-month return, rebalance monthly.
8. **"Pairs Trading"** — Cointegration check + z-score-based entry/exit on two correlated stocks.

**Advanced:**
9. **"Custom Indicator"** — Show how to compute a bespoke indicator and plot it.
10. **"Parameter Sweep"** — Demonstrate the `Optimize()` API across SMA lookback periods.

Each template has a one-sentence description, an estimated run time, and a "Use This" button that populates the editor (or first cell). The gallery is accessible anytime via a toolbar button.

**The implementation shape.** Templates stored as `.csx` files in `data/_scripts/_templates/` (shipped with the app, not user-editable). A `TemplateGalleryService` reads them. The gallery UI is a simple `ListBox` with `DataTemplate` showing name, description, category, and a preview thumbnail (static screenshot of expected output).

**The tradeoff.** Templates go stale. They must be maintained — if a Skender API changes, templates break. Mitigate: include template scripts in the test suite (compile-only test for each template).

**Effort:** S (3–4 days for templates + gallery UI) | **Audience:** H (primary), Q | **Impact:** Very High

---

### Idea 6: Result Persistence & Run Comparison

**The problem.** The blueprint has no concept of run history. Execute a script, close the page, results are gone. Worse: there's no way to compare "run with SMA(20)" vs. "run with SMA(50)" side-by-side. Every serious research tool (QuantConnect, Zipline, even Excel) lets you compare results across parameter changes.

**The user moment.** After running a backtest, the results panel shows a "Save Run" button. The user names it "SMA-20-50 crossover v1". Next, they change the lookback to 30/60, re-run, and save as "SMA-30-60 crossover v2". They then click "Compare" and see a side-by-side view:

| Metric | v1 (20/50) | v2 (30/60) | Delta |
|---|---|---|---|
| Sharpe | 1.42 | 1.18 | -0.24 |
| MaxDD | -12.3% | -9.8% | +2.5% |
| CAGR | 15.2% | 11.8% | -3.4% |

Below: overlaid equity curves on the same chart, colour-coded.

**The implementation shape.** A `ScriptRunRecord` persisted as JSON in `data/_scripts/_runs/`:
```csharp
sealed record ScriptRunRecord(
    Guid Id,
    string ScriptName,
    DateTime RunTimestamp,
    TimeSpan Elapsed,
    string SourceCodeHash,
    Dictionary<string, object> Parameters,
    ScriptRunMetrics? Metrics,
    IReadOnlyList<PlotRequest> Plots);
```

`PortfolioStatistics` and the existing SDK `BacktestMetrics` overlap on Sharpe, Sortino, Calmar,
MaxDrawdown, WinRate, ProfitFactor, and TotalTrades — carrying both would create duplicate fields
with different nullability rules and confuse the comparison view.  Instead, a single unified record
merges every field from both sources; backtest-only fields are nullable so a standalone series
analysis can leave them unpopulated:

```csharp
/// <summary>
/// Unified performance record for a QuantScript run.
/// Populated from a backtest via <see cref="FromBacktest"/>,
/// or from a standalone return-series analysis via <see cref="FromReturnSeries"/>.
/// Backtest-specific fields are nullable and absent in series-only runs.
/// </summary>
sealed record ScriptRunMetrics
{
    // ── Core metrics — always populated ──────────────────────────────────
    public required double SharpeRatio          { get; init; }
    public required double SortinoRatio         { get; init; }
    public required double CalmarRatio          { get; init; }
    public required double AnnualisedReturn     { get; init; }
    public          double? AnnualisedVolatility { get; init; }  // null when sourced from BacktestMetrics (not computed by BacktestMetricsEngine)
    public required double MaxDrawdown          { get; init; }  // positive fraction, e.g. 0.123 = 12.3% peak-to-trough
    public required double Cagr                 { get; init; }
    public required double WinRate              { get; init; }
    public required double ProfitFactor         { get; init; }
    public required int    TotalTrades          { get; init; }

    // ── Backtest-only fields — null for series-analysis runs ─────────────
    public decimal? InitialCapital          { get; init; }
    public decimal? FinalEquity             { get; init; }
    public decimal? GrossPnl               { get; init; }
    public decimal? NetPnl                 { get; init; }
    public decimal? TotalReturn            { get; init; }
    public decimal? MaxDrawdownAbsolute    { get; init; }  // dollar amount; MaxDrawdown holds the percentage form
    public int?     MaxDrawdownRecoveryDays { get; init; }
    public int?     WinningTrades          { get; init; }
    public int?     LosingTrades           { get; init; }
    public decimal? TotalCommissions       { get; init; }
    public decimal? TotalMarginInterest    { get; init; }
    public decimal? TotalShortRebates      { get; init; }
    public double?  Xirr                   { get; init; }
    public IReadOnlyDictionary<string, SymbolAttribution>? SymbolAttribution { get; init; }

    // ── Factory methods ──────────────────────────────────────────────────

    /// <summary>Build from an existing <see cref="BacktestMetrics"/> (all fields populated).</summary>
    public static ScriptRunMetrics FromBacktest(BacktestMetrics m) => new()
    {
        SharpeRatio           = m.SharpeRatio,
        SortinoRatio          = m.SortinoRatio,
        CalmarRatio           = m.CalmarRatio,
        AnnualisedReturn      = (double)m.AnnualizedReturn,
        AnnualisedVolatility  = null,             // BacktestMetricsEngine does not compute return-series volatility
        // BacktestMetrics.AnnualizedReturn is already CAGR: computed as (1 + totalReturn)^(1/years) − 1
        MaxDrawdown           = (double)m.MaxDrawdownPercent,  // use the fractional field, not the absolute dollar amount
        Cagr                  = (double)m.AnnualizedReturn,
        WinRate               = m.WinRate,
        ProfitFactor          = m.ProfitFactor,
        TotalTrades           = m.TotalTrades,
        InitialCapital        = m.InitialCapital,
        FinalEquity           = m.FinalEquity,
        GrossPnl              = m.GrossPnl,
        NetPnl                = m.NetPnl,
        TotalReturn           = m.TotalReturn,
        MaxDrawdownAbsolute   = m.MaxDrawdown,
        MaxDrawdownRecoveryDays = m.MaxDrawdownRecoveryDays,
        WinningTrades         = m.WinningTrades,
        LosingTrades          = m.LosingTrades,
        TotalCommissions      = m.TotalCommissions,
        TotalMarginInterest   = m.TotalMarginInterest,
        TotalShortRebates     = m.TotalShortRebates,
        Xirr                  = m.Xirr,
        SymbolAttribution     = m.SymbolAttribution,
    };

    /// <summary>Build from a <see cref="ReturnSeries"/> (backtest-only fields remain null).</summary>
    public static ScriptRunMetrics FromReturnSeries(PortfolioStatistics s) => new()
    {
        SharpeRatio           = s.SharpeRatio,
        SortinoRatio          = s.SortinoRatio,
        CalmarRatio           = s.CalmarRatio,
        AnnualisedReturn      = s.AnnualisedReturn,
        AnnualisedVolatility  = s.AnnualisedVolatility,
        MaxDrawdown           = s.MaxDrawdown,    // PortfolioStatistics.MaxDrawdown is the fractional form
        Cagr                  = s.Cagr,
        WinRate               = s.WinRate,
        ProfitFactor          = s.ProfitFactor,
        TotalTrades           = s.TotalTrades,
    };
}
```

> **`PortfolioStatistics` is still used** — `StatisticsEngine.Compute` returns it, and `FromReturnSeries`
> maps it into `ScriptRunMetrics`.  It remains the internal computation type; `ScriptRunMetrics` is
> purely the persistence / comparison type.

A `RunHistoryService` manages CRUD. The comparison view is a new tab in the results pane that accepts 2–4 run records and renders delta tables + overlaid plots.

**The tradeoff.** Storing full plot data (X/Y arrays) for every run can grow large. Mitigate: cap stored runs at 50, auto-prune oldest. Or store only metrics + equity curve, not all plot data.

**Effort:** M (1–2 weeks) | **Audience:** Q, I | **Impact:** High

---

### Idea 7: Flatten the DSL — Method Chains over Nested Calls

**The problem.** The blueprint's DSL has inconsistent depth. Some operations are fluent (`spy.Returns().SharpeRatio()`), others require reaching into separate globals (`Stats.Compute(spy.Returns())`). The `Stats` global duplicates what `ReturnSeries` methods already provide. The plotting DSL uses positional arrays (`Plot.Line("title", xValues, yValues)`) when it could accept series directly.

**The user moment.** Current blueprint:
```csharp
var spy = Data.Prices("SPY");
var returns = spy.Returns();
var stats = Stats.Compute(returns);
Plot.Line("SPY Close", spy.Dates.Select(d => d.DayNumber).ToList(),
          spy.Close.Select(c => (double)c).ToList());
Log($"Sharpe: {stats.SharpeRatio:F2}");
```

Proposed flattened DSL:
```csharp
var spy = await Data.PricesAsync("SPY");
spy.Plot("SPY Close");                    // PriceSeries knows how to plot itself
spy.SMA(20).Plot("SMA 20", color: "blue");
Log($"Sharpe: {spy.Returns().Sharpe():F2}");
```

**The implementation shape.** Add `.Plot()` extension methods directly on `PriceSeries`, `ReturnSeries`, and `NumericSeries`:
```csharp
public static void Plot(this PriceSeries series, string? title = null, string? color = null)
{
    // Access PlotQueue from ambient context (AsyncLocal or captured in series constructor)
    PlotQueue.Current.Line(title ?? series.Symbol, series.DatesAsDouble, series.CloseAsDouble, color: color);
}
```

Move `.Sharpe()`, `.Sortino()`, `.MaxDrawdown()` directly onto `ReturnSeries` as instance methods (they already exist in the blueprint — just promote them and deprecate the `Stats.Compute` wrapper).

Keep `StatisticsEngine` available for multi-series operations (`Correlation`, `RollingBeta`) but remove it as a required global. Move it to `Stats` as a convenience namespace: `Stats.Correlation(spyReturns, aaplReturns)`.

**The tradeoff.** Ambient `PlotQueue.Current` via `AsyncLocal<PlotQueue>` is implicit state — harder to test, surprising behaviour if used outside a script context. Mitigate: throw a clear exception if `PlotQueue.Current` is null ("Plot() can only be called inside a QuantScript execution context").

**Effort:** S (2–3 days) | **Audience:** H, Q, I | **Impact:** High

---

### Idea 8: Real Parameter Sweep with `Optimize()` API

**The problem.** The blueprint mentions "optional grid-search over param ranges" and `[ScriptParam]` attributes but provides no actual optimisation API. Parameter sweep is one of the top-3 reasons quants use scripting environments. Without it, users manually change values and re-run — exactly the tedium the tool should eliminate.

**The user moment.** The user defines a parameterised strategy and runs a sweep:
```csharp
var results = await Test.OnBar(["SPY"], (bar, ctx) =>
{
    var fast = ctx.Indicator(bar.Symbol, "SMA", Param<int>("Fast"));
    var slow = ctx.Indicator(bar.Symbol, "SMA", Param<int>("Slow"));
    if (fast > slow) ctx.SetTarget(bar.Symbol, 1.0m);
    else ctx.SetTarget(bar.Symbol, 0.0m);
})
.From(2020, 1, 1).To(2024, 12, 31)
.Optimize("Fast", 10, 50, step: 5)
.Optimize("Slow", 20, 100, step: 10)
.RunAllAsync();

// results is IReadOnlyList<(Dictionary<string,object> Params, BacktestResult Result)>
results.BestBy(r => r.Metrics.SharpeRatio).Plot("Best Equity Curve");
results.Heatmap("Fast", "Slow", r => r.Metrics.SharpeRatio);
```

The results pane shows a 2D heatmap of Sharpe ratios across the parameter grid, with the best combination highlighted.

**The implementation shape.** `BacktestBuilder` gains `.Optimize(string paramName, double min, double max, double step)` methods. `.RunAllAsync()` generates the Cartesian product of all optimised params, runs each backtest (potentially parallelised with `Parallel.ForEachAsync`), and returns a `SweepResult` collection. The `PlotQueue` gains a `.Heatmap()` method for 2D visualisation.

**The tradeoff.** Combinatorial explosion: 9 values for `Fast` x 9 values for `Slow` = 81 backtests. At ~2 seconds each, that's 2.7 minutes. Mitigate: run with `Parallel.ForEachAsync(maxDegreeOfParallelism: Environment.ProcessorCount)`. Show a progress bar. Warn the user if the grid exceeds 200 combinations.

**Effort:** M (1–2 weeks) | **Audience:** Q, I | **Impact:** Medium-High

---

### Idea 9: Live Script Output Streaming

**The problem.** The blueprint's execution model is batch: run script → wait → see all results. For scripts that load large datasets or run long backtests, the user stares at a spinner for 30+ seconds with no feedback. This is the opposite of interactive.

**The user moment.** As the script runs, the Log tab streams messages in real-time. When a `Plot.Line(...)` call executes, the chart renders immediately in the Plot tab — the user watches it build up. Progress messages from `BacktestEngine` appear as they happen: "Processing 2021... 2022... 2023...".

**The implementation shape.** Replace `Action<string> Log` in globals with a `ChannelWriter<string>` that the ViewModel reads via `ChannelReader<string>` on the UI thread. Similarly, `PlotQueue` becomes a `Channel<PlotRequest>` that the ViewModel subscribes to and renders incrementally.

The key change: the ViewModel doesn't wait for `RunAsync` to complete before rendering. It starts reading from channels immediately and updates the UI as items arrive. `RunAsync` completion simply signals "done — no more updates coming."

**The tradeoff.** Incremental ScottPlot rendering requires reploting the entire chart on each new series addition (ScottPlot doesn't support incremental rendering natively). For many series, this causes flicker. Mitigate: batch updates with a 100ms debounce — collect all plot requests that arrive within a 100ms window, then render once.

**Effort:** M (1 week) | **Audience:** H, Q | **Impact:** Medium-High

---

### Idea 10: Script Import & Composition

**The problem.** The blueprint treats each script as isolated. Real workflows build on shared utilities: a custom indicator, a standard data-loading preamble, a shared plotting style. Without `#load` or import semantics, users copy-paste between scripts.

**The user moment.** A user creates `_lib/indicators.csx` with custom indicator functions. In any new script:
```csharp
#load "_lib/indicators.csx"

var spy = await Data.PricesAsync("SPY");
var myIndicator = CustomRSI(spy, 21); // defined in indicators.csx
myIndicator.Plot("Custom RSI");
```

**The implementation shape.** Roslyn `CSharpScript` supports `#load` directives natively via `ScriptOptions.WithSourceResolver(...)`. Configure a `SourceFileResolver` that resolves paths relative to `ScriptsDirectory`. The compiler already handles `#load` — the only work is configuring the resolver correctly and documenting the convention.

**The tradeoff.** `#load` executes the loaded file's top-level code on every inclusion (no header-guard equivalent). Mitigate: document that shared scripts should only contain type/method declarations, not executable statements.

**Effort:** S (1–2 days) | **Audience:** Q, I | **Impact:** Medium

---

## Part 3: Interface Design — Making the DSL Shine

### Design Principle: Three Levels of Depth

The interface should serve three usage levels without forcing the user to learn concepts they don't need:

| Level | User | API Surface | Example |
|---|---|---|---|
| **Explore** | 5-minute user, hobbyist | `Data.Prices → .Plot() → .SMA()` | `(await Data.PricesAsync("SPY")).Plot();` |
| **Analyse** | Weekly user, academic | `+ .Returns() → .Sharpe() → .AlignWith() → Stats.Correlation` | Multi-symbol spread analysis, rolling stats |
| **Backtest** | Power user, pro | `Test.OnBar(...).Optimize(...).RunAllAsync()` | Full parameter-swept backtest with strategy composition |

Each level naturally introduces the next. A user can stay at "Explore" forever and still get value. The API never forces them into "Backtest" complexity to plot a moving average.

### Recommended Final DSL Shape

Based on all 10 ideas, here's the recommended public API surface:

```
Globals:
  Data          → DataContext (async, direct — no proxy wrapper)
  Test          → BacktestBuilder factory
  Plot          → PlotQueue (kept for explicit control)
  Stats         → StatisticsEngine (for multi-series operations only)
  Log(string)   → Action<string> (streamed to UI)
  Param<T>(name, default, min?, max?)  → runtime parameter registration
  CancellationToken

Data:
  .PricesAsync(symbol, from?, to?)          → Task<PriceSeries>
  .PricesAsync(params symbols)              → Task<AlignedSeries>
  .ReturnsAsync(symbol, from?, to?)         → Task<ReturnSeries>
  .SymbolsAsync()                           → Task<IReadOnlyList<string>>

PriceSeries:
  .Close, .Open, .High, .Low, .Volume      → NumericSeries
  .Dates                                    → IReadOnlyList<DateOnly>
  .Slice(from, to)                          → PriceSeries
  .Returns(), .LogReturns()                 → ReturnSeries
  .ToQuotes()                               → IReadOnlyList<IQuote>
  .SMA(n), .EMA(n), .RSI(n), .MACD(...)    → NumericSeries
  .BollingerBands(n, stdDev)               → (Upper, Middle, Lower) NumericSeries tuple
  .ATR(n)                                   → NumericSeries
  .AlignWith(other)                         → AlignedSeries
  .Plot(title?, color?)                     → void (renders close price)

NumericSeries:
  +, -, *, / operators                      → NumericSeries
  .RollingMean(n), .RollingStd(n)          → NumericSeries
  .Cumulative()                             → NumericSeries
  .Where(predicate)                         → NumericSeries
  .Plot(title?, color?)                     → void
  .ToList()                                 → IReadOnlyList<double>

ReturnSeries:
  .Sharpe(rf?), .Sortino(rf?)              → double
  .MaxDrawdown()                            → double
  .AnnualisedReturn(), .AnnualisedVol()    → double
  .Plot(title?)                             → void (plots cumulative return)

AlignedSeries:
  [symbol]                                  → PriceSeries (date-aligned)
  .Dates                                    → IReadOnlyList<DateOnly>

Test (BacktestBuilder factory):
  .OnBar(symbols, handler)                  → BacktestBuilder
  .OnTrade(handler)                         → BacktestBuilder (chains)
  .OnDayEnd(handler)                        → BacktestBuilder (chains)
  .From(y, m, d), .To(y, m, d)            → BacktestBuilder (chains)
  .WithCash(amount)                         → BacktestBuilder (chains)
  .Optimize(param, min, max, step)          → BacktestBuilder (chains)
  .RunAsync()                               → Task<BacktestResult>
  .RunAllAsync()                            → Task<SweepResult>
  .Run(request, IBacktestStrategy)          → BacktestResult (power-user escape hatch)

Stats:
  .Correlation(series...)                   → CorrelationMatrix
  .RollingBeta(asset, benchmark, window)   → NumericSeries

Plot (explicit, for advanced use):
  .Line(...), .Scatter(...), .Bar(...)     → void
  .Histogram(values, bins?)                 → void
  .Heatmap(xParam, yParam, metric)         → void (for sweep results)
```

### What This Gets Right

1. **Discoverability.** A user types `spy.` and sees methods they understand: `.Close`, `.SMA(20)`, `.Plot()`, `.Returns()`. No need to consult docs.

2. **Composability.** Everything chains: `spy.SMA(20).Plot("SMA")`. No intermediate variables required unless the user wants them.

3. **Progressive disclosure.** `Data.Prices → Plot` is 2 calls. Backtesting is 10+ lines. Statistics, correlation, and sweeps are available but never forced.

4. **Escape hatches.** Power users can still implement `IBacktestStrategy` directly and call `Test.Run(request, strategy)`. The fluent builder doesn't replace the full API — it layers on top.

5. **Async-native.** Every data operation is async. Scripts use `await`. No hidden `.GetAwaiter().GetResult()`.

---

## Synthesis

### Highest-Leverage Idea

**Idea #3 (Fluent Backtest Builder)** has the best impact/effort ratio. The current blueprint requires 20+ lines of boilerplate for a simple backtest. The builder reduces this to 8–10 lines. This single change transforms QuantScript from "a C# editor bolted onto a backtest engine" into "the fastest path from idea to equity curve." It's also a prerequisite for Idea #8 (Parameter Sweep) — you can't chain `.Optimize()` onto a class-based strategy.

### Platform Bet

**Idea #1 (Cell-Based Execution)** is the platform bet. It's more effort than any single idea, but it unlocks a fundamentally different usage pattern — exploration vs. execution. Without cells, QuantScript competes with VS Code (and loses). With cells, it competes with Jupyter (and wins, because it has live data + backtesting built in). Every subsequent feature (templates, run comparison, streaming output) becomes more valuable inside a cell model.

### Cross-Cutting Theme

Ideas #2, #4, and #7 all converge on the same insight: **the type system is the interface**. If `PriceSeries.Close` returns `NumericSeries` with operator overloading and `.Plot()`, if `ReturnSeries` has `.Sharpe()` directly, if `await` works natively — then the user never consults documentation. They type a dot and the IDE (or even AvalonEdit's basic completion) tells them what's available. The DSL becomes self-documenting through types.

### Competitive Signals

QuantConnect Research (their Jupyter-like environment) is the closest comparable. It uses Python, has cell-based execution, and provides `qb.AddEquity("SPY")` + `qb.History(...)` as the data layer. Their weakness: the data layer is cloud-only and requires a QuantConnect account. Meridian's advantage: local data, offline execution, no vendor lock-in. Databento's approach (pure API, no IDE) leaves the scripting experience entirely to Jupyter — Meridian can capture that "all-in-one" positioning by having first-class scripting in-app.

### Recommended Sequencing

```
Phase 0 (before current blueprint Phase 1):
  → Adopt async-native scripts (#2) — foundational, changes all signatures
  → Adopt flattened DSL (#7) — changes PriceSeries/ReturnSeries API shape

Phase 1 (blueprint Phase 1 + 2, modified):
  → PriceSeries with NumericSeries (#4, partial — operators + .Plot())
  → Template gallery (#5) — ship with the editor, not as afterthought
  → Fluent BacktestBuilder (#3) — alongside the existing IBacktestStrategy path

Phase 2 (blueprint Phase 3 + 4):
  → Cell-based execution (#1) — replace monolithic editor
  → Live output streaming (#9) — natural fit with cells
  → Script import (#10) — `#load` for shared code

Phase 3 (post-blueprint):
  → Result persistence & comparison (#6)
  → Parameter sweep (#8) — requires BacktestBuilder from Phase 1
  → AlignedSeries full implementation (#4, complete)
```

This sequencing front-loads the API design changes (which affect everything downstream) and defers the most complex UI work (cells, persistence) to Phase 2 when the core DSL is stable.

---

*Generated by meridian-brainstorm. This evaluation should be read alongside `docs/plans/quant-script-environment-blueprint.md` and used to revise the blueprint before implementation begins.*
