<<<<<<< HEAD
# QuantScript Environment Blueprint

**Owner:** Desktop / Research Engineering
**Audience:** Research, desktop, and backtesting contributors
**Last Updated:** 2026-04-08
**Status:** Delivered baseline with optional post-core follow-ons

---

## Summary

QuantScript is Meridian's interactive C# research scripting environment inside the desktop workstation. The core page, compiler/runner path, tests, and sample-script baseline are already implemented; this document now serves as the high-level product-placement and architecture-intent reference, while [`quant-script-page-implementation-guide.md`](quant-script-page-implementation-guide.md) carries the detailed screen, service, and implementation guidance.

QuantScript is not part of the default Wave 1-6 core operator-readiness path. Treat it as an optional research-product track unless a specific roadmap decision pulls it forward.

---

## Product Role

- give researchers a fast local scripting surface over Meridian-collected data
- connect scripting, charting, metrics, diagnostics, and backtest-adjacent analysis inside one desktop workflow
- reuse Meridian services and data contracts instead of creating a separate research stack
- complement the Research workspace rather than replace the shared run, promotion, or governance workflows

---

## Scope

### In scope

- interactive C# scripting in the WPF workstation
- script browser, parameter inputs, console output, charts, metrics, and diagnostics
- access to existing Meridian data, analytics, and backtesting services through explicit runner abstractions
- persisted local scripts and repeatable research workflows

### Out of scope

- treating Python or R notebooks as a required Meridian platform dependency
- replacing workstation research, promotion, or governance flows with a standalone scripting product
- elevating QuantScript ahead of the Wave 1-6 operator-readiness path by default

---

## Architecture Direction

- keep QuantScript as a workstation-integrated research tool, not a second application stack
- keep execution isolated behind compiler/runner services and explicit capability injection
- keep outputs aligned with shared backtest and run contracts where that materially helps workflow continuity
- route detailed UI, ViewModel, and interaction design to the page implementation guide instead of duplicating it here

---

## Dependencies And Placement

- QuantScript depends on the platform foundations Meridian already has in data collection, charting, diagnostics, and backtesting
- further QuantScript expansion should normally follow Waves 1-4 and sit alongside other optional research and scale tracks
- if future work deepens this surface, prefer integration with shared run, export, and observability seams over bespoke storage or orchestration paths

---

## Related Documents

- [QuantScript Page Implementation Guide](quant-script-page-implementation-guide.md)
- [L3 Inference Implementation Plan](l3-inference-implementation-plan.md)
- [QuantScript L3 Multi-instance Round 2 Roadmap](quantscript-l3-multiinstance-round2-roadmap.md)
- [ROADMAP.md](../status/ROADMAP.md)
=======
# Blueprint: QuantScriptEnvironment

**Date:** 2026-03-18
**Depth:** Full
**Branch:** `feature/quant-script-environment`

---

## Step 1: Scope

**In Scope:**
- New library project `Meridian.QuantScript` containing the scripting engine, data API, returns vocabulary, statistical functions, portfolio tools, plotting queue, and Roslyn compilation pipeline
- New WPF page `QuantScriptPage` with AvalonEdit editor, tabbed results panel (Console / Charts / Metrics / Trades), script browser, and dynamic parameter form
- `QuantScriptViewModel` wired into the existing `MainPage` navigation
- Two new packages added to `Directory.Packages.props`: `AvalonEditB` (WPF code editor) and `ScottPlot.WPF` (charting)
- New test project `Meridian.QuantScript.Tests`

**Out of Scope:**
- Live/streaming data in scripts (scripts operate on locally-stored historical data only)
- Python or F# script execution (C# `.csx` only)
- Remote script execution or multi-user collaboration
- Full Roslyn IntelliSense / completion (deferred; basic keyword list only in v1)
- `Portfolio.EfficientFrontier` full implementation (interface defined, body deferred — see Open Questions)
- Persisting script run results to storage

**Assumptions:**
- Locally collected JSONL data exists under the configured `DataRoot` path; scripts that request data for dates with no local data will receive empty series with a console warning
- `BacktestMetricsEngine` remains `internal` — `Meridian.QuantScript` references the `Meridian.Backtesting` project (not only the SDK) to access metrics
- `Skender.Stock.Indicators` v2.7.1 already in CPM — reused for SMA/EMA/RSI/MACD/Bollinger
- `Microsoft.CodeAnalysis.CSharp` v5.0.0 already in CPM — `Microsoft.CodeAnalysis.CSharp.Scripting` added at the same version
- Scripts run in-process; isolation is achieved via `CancellationToken` + timeout, not AppDomain (not available in .NET Core)

---

## Step 2: Architectural Overview

### Context Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  Meridian.Wpf                                                    │
│                                                                             │
│  ┌──────────────────────┐      commands/bindings      ┌─────────────────┐  │
│  │  QuantScriptPage.xaml│ ◄──────────────────────────►│QuantScriptVM    │  │
│  │  AvalonEdit editor   │                             │(BindableBase)   │  │
│  │  TabControl results  │                             └────────┬────────┘  │
│  │  Script browser      │                                      │ uses      │
│  └──────────────────────┘                                      │           │
└───────────────────────────────────────────────────────────────┼───────────┘
                                                                 │
                              ┌──────────────────────────────────▼───────────┐
                              │  Meridian.QuantScript              │
                              │                                               │
                              │  ┌─────────────┐   ┌──────────────────────┐ │
                              │  │ScriptRunner  │──►│QuantScriptGlobals    │ │
                              │  │(IScriptRunner│   │  .Data (DataProxy)   │ │
                              │  │)             │   │  .Portfolio          │ │
                              │  └──────┬───────┘   │  .Backtest           │ │
                              │         │            │  .Print()            │ │
                              │  ┌──────▼───────┐   └──────────────────────┘ │
                              │  │RoslynScript  │                             │
                              │  │Compiler      │   ┌──────────────────────┐ │
                              │  └──────────────┘   │  StatisticsEngine    │ │
                              │                     │  (static)            │ │
                              │  ┌───────────────┐  └──────────────────────┘ │
                              │  │  PlotQueue    │                            │
                              │  │(Channel<Plot  │  ┌──────────────────────┐ │
                              │  │ Request>)     │  │TechnicalSeries       │ │
                              │  └───────────────┘  │Extensions (static)   │ │
                              │                     └──────────────────────┘ │
                              │  ┌───────────────────────────────────────┐   │
                              │  │  QuantDataContext (IQuantDataContext)  │   │
                              │  └──────────────────┬────────────────────┘   │
                              └─────────────────────┼──────────────────────-─┘
                                                    │ reads
              ┌─────────────────────────────────────┼────────────────────────┐
              │  Meridian.Application /   │                        │
              │  Meridian.Storage         │                        │
              │                                      │                        │
              │  HistoricalDataQueryService ◄────────┘                        │
              │  JsonlMarketDataStore                                         │
              └───────────────────────────────────────────────────────────────┘
              ┌───────────────────────────────────────────────────────────────┐
              │  Meridian.Backtesting                              │
              │                                                               │
              │  BacktestEngine.RunAsync(...)                                 │
              │  BacktestMetricsEngine.Compute(...)                           │
              │  SimulatedPortfolio / IFillModel                              │
              └───────────────────────────────────────────────────────────────┘
```

### Design Decisions

**Decision:** Roslyn in-process scripting via `Microsoft.CodeAnalysis.CSharp.Scripting`
**Alternatives Considered:** Separate process with stdin/stdout; Lua/Python embedded interpreter
**Rationale:** In-process gives scripts direct access to Meridian types without serialization; `CancellationToken` handles runaway scripts; already have `Microsoft.CodeAnalysis.CSharp` in CPM
**Consequences:** A buggy script can corrupt process state; mitigated by timeout + CT + documented restrictions in `QuantScriptOptions.EnableUnsafeScripts`

**Decision:** `DataProxy` exposes a synchronous API (`.Prices(...)`) over an async `IQuantDataContext`
**Alternatives Considered:** Expose `async Task<PriceSeries>` and require `await` in scripts
**Rationale:** R/Python quant workflows are imperative and synchronous-feeling; requiring `await` at every data call makes scripts verbose; `DataProxy` calls `.GetAwaiter().GetResult()` while respecting the `CancellationToken`
**Consequences:** Scripts block the Roslyn execution thread (which is already a background `Task`); this is acceptable since scripts are single-threaded analysis workflows

**Decision:** `PlotQueue` backed by `Channel<PlotRequest>` (unbounded)
**Alternatives Considered:** `IObservable<PlotRequest>`; callback delegate
**Rationale:** Matches Meridian's bounded-channel pattern (ADR-013); unbounded because a script producing 1000 charts is a user error, not a production throughput concern
**Consequences:** Memory spike if a script enqueues thousands of plots; document 100-plot soft limit in `QuantScriptOptions`

**Decision:** `BacktestProxy` is a fluent builder that wraps `IBacktestStrategy` via an anonymous inline adapter
**Alternatives Considered:** Require scripts to implement `IBacktestStrategy` directly (forces class declaration)
**Rationale:** Scripts must remain class-free; the proxy captures lambda callbacks and adapts them to `IBacktestStrategy` callbacks internally
**Consequences:** Only one `OnBar`/`OnQuote`/`OnTrade` handler per proxy instance; strategy composition not supported in v1

**Decision:** ScottPlot.WPF for charting
**Alternatives Considered:** OxyPlot (older API), LiveCharts2 (GPL concerns on commercial use)
**Rationale:** ScottPlot 5.x is MIT, .NET 9 compatible, ships a native `WpfPlot` control, and renders efficiently for time-series data
**Consequences:** New package not yet in CPM — must add `ScottPlot.WPF` to `Directory.Packages.props`

**Decision:** AvalonEdit (ICSharpCode.AvalonEdit) for the code editor
**Alternatives Considered:** `RichTextBox`, `FastColoredTextBox`
**Rationale:** AvalonEdit is the de-facto WPF C# editor used by SharpDevelop/ILSpy; has syntax highlighting for C#, folding, and a clean API
**Consequences:** New package not yet in CPM — must add `AvalonEdit` to `Directory.Packages.props`

---

## Step 3: Interface & API Contracts

### 3.1 Core Series Types

```csharp
// File: src/Meridian.QuantScript/Api/PriceSeries.cs
namespace Meridian.QuantScript.Api;

/// <summary>
/// An ordered, immutable OHLCV price series for a single symbol.
/// Produced by <see cref="DataProxy.Prices"/> and consumed by returns/indicator extensions.
/// </summary>
public sealed class PriceSeries
{
    public string Symbol { get; }
    public IReadOnlyList<PriceBar> Bars { get; }
    public int Count => Bars.Count;
    public DateOnly From => Bars.Count > 0 ? Bars[0].Date : default;
    public DateOnly To => Bars.Count > 0 ? Bars[^1].Date : default;

    public PriceSeries(string symbol, IReadOnlyList<PriceBar> bars);
}

/// <summary>Single OHLCV bar.</summary>
public sealed record PriceBar(
    DateOnly Date,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);
```

```csharp
// File: src/Meridian.QuantScript/Api/ReturnSeries.cs
namespace Meridian.QuantScript.Api;

/// <summary>
/// An ordered series of per-period returns (arithmetic or log).
/// Produced by PriceSeries extension methods; all statistical methods are instance methods here.
/// </summary>
public sealed class ReturnSeries
{
    public string Symbol { get; }
    public ReturnKind Kind { get; }
    public IReadOnlyList<ReturnPoint> Points { get; }
    public int Count => Points.Count;

    public ReturnSeries(string symbol, ReturnKind kind, IReadOnlyList<ReturnPoint> points);

    // ── Statistics ───────────────────────────────────────────────────────────

    /// <summary>Annualised Sharpe ratio (252 trading days). Risk-free rate in decimal (e.g. 0.04).</summary>
    public double SharpeRatio(double riskFreeRate = 0.04);

    /// <summary>Annualised Sortino ratio using downside deviation.</summary>
    public double SortinoRatio(double riskFreeRate = 0.04);

    /// <summary>Annualised volatility (std dev of daily returns × √252).</summary>
    public double AnnualizedVolatility();

    /// <summary>Maximum peak-to-trough drawdown as a fraction (e.g. 0.25 = 25%).</summary>
    public double MaxDrawdown();

    /// <summary>Full drawdown series for every period.</summary>
    public IReadOnlyList<ReturnPoint> DrawdownSeries();

    /// <summary>Beta relative to a benchmark return series.</summary>
    public double Beta(ReturnSeries benchmark);

    /// <summary>Jensen's Alpha relative to a benchmark return series (annualised).</summary>
    public double Alpha(ReturnSeries benchmark, double riskFreeRate = 0.04);

    /// <summary>Pearson correlation with another return series (aligned by date).</summary>
    public double Correlation(ReturnSeries other);

    /// <summary>Sample skewness.</summary>
    public double Skewness();

    /// <summary>Excess kurtosis (normal = 0).</summary>
    public double Kurtosis();

    /// <summary>Rolling arithmetic mean over <paramref name="window"/> periods.</summary>
    public ReturnSeries RollingMean(int window);

    /// <summary>Rolling sample standard deviation over <paramref name="window"/> periods.</summary>
    public ReturnSeries RollingSd(int window);

    /// <summary>Cumulative return series (compounded).</summary>
    public ReturnSeries Cumulative();

    // ── Plot terminals ───────────────────────────────────────────────────────

    /// <summary>Enqueues a line chart of this return series to the results panel.</summary>
    public void Plot(string? title = null);

    /// <summary>Enqueues a cumulative return chart.</summary>
    public void PlotCumulative(string? title = null);

    /// <summary>Enqueues an underwater (drawdown) chart.</summary>
    public void PlotDrawdown(string? title = null);
}

public sealed record ReturnPoint(DateOnly Date, double Value);

public enum ReturnKind { Arithmetic, Log, Cumulative, Rolling, DrawdownSeries, RollingStat }
```

```csharp
// File: src/Meridian.QuantScript/Api/PriceSeriesExtensions.cs
namespace Meridian.QuantScript.Api;

public static class PriceSeriesExtensions
{
    /// <summary>Day-over-day arithmetic returns: (Close[t] - Close[t-1]) / Close[t-1].</summary>
    public static ReturnSeries DailyReturns(this PriceSeries series);

    /// <summary>Day-over-day log returns: ln(Close[t] / Close[t-1]).</summary>
    public static ReturnSeries LogReturns(this PriceSeries series);

    /// <summary>Compounded cumulative return starting from 1.0.</summary>
    public static ReturnSeries CumulativeReturns(this PriceSeries series);

    /// <summary>Non-overlapping rolling returns over <paramref name="window"/> days.</summary>
    public static ReturnSeries RollingReturns(this PriceSeries series, int window);
}
```

### 3.2 Technical Indicator Extensions

```csharp
// File: src/Meridian.QuantScript/Api/TechnicalSeriesExtensions.cs
namespace Meridian.QuantScript.Api;

/// <summary>
/// Technical indicator extension methods on PriceSeries.
/// Delegates to Skender.Stock.Indicators where available; pure math otherwise.
/// </summary>
public static class TechnicalSeriesExtensions
{
    public static IReadOnlyList<(DateOnly Date, double? Sma)> Sma(this PriceSeries series, int period);
    public static IReadOnlyList<(DateOnly Date, double? Ema)> Ema(this PriceSeries series, int period);
    public static IReadOnlyList<(DateOnly Date, double? Rsi)> Rsi(this PriceSeries series, int period = 14);
    public static IReadOnlyList<(DateOnly Date, double? Macd, double? Signal, double? Histogram)>
        Macd(this PriceSeries series, int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9);
    public static IReadOnlyList<(DateOnly Date, double? Upper, double? Mid, double? Lower)>
        BollingerBands(this PriceSeries series, int period = 20, double stdDevMultiplier = 2.0);

    /// <summary>Plots a single named indicator line to the results panel.</summary>
    public static void Plot<T>(this IReadOnlyList<(DateOnly Date, T Value)> series, string title);
}
```

### 3.3 Data Access

```csharp
// File: src/Meridian.QuantScript/Api/IQuantDataContext.cs
namespace Meridian.QuantScript.Api;

/// <summary>
/// Async data access contract; implemented by QuantDataContext which delegates to
/// HistoricalDataQueryService and JsonlMarketDataStore.
/// </summary>
public interface IQuantDataContext
{
    Task<PriceSeries> PricesAsync(
        string symbol, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<PriceSeries> PricesAsync(
        string symbol, DateOnly from, DateOnly to, string provider, CancellationToken ct = default);

    Task<IReadOnlyList<ScriptTrade>> TradesAsync(
        string symbol, DateOnly date, CancellationToken ct = default);

    Task<ScriptOrderBook?> OrderBookAsync(
        string symbol, DateTimeOffset timestamp, CancellationToken ct = default);
}

/// <summary>Lightweight trade tick for script consumption.</summary>
public sealed record ScriptTrade(DateTimeOffset Timestamp, decimal Price, long Size, string Side);

/// <summary>Lightweight order book snapshot for script consumption.</summary>
public sealed record ScriptOrderBook(
    DateTimeOffset Timestamp,
    IReadOnlyList<(decimal Price, long Size)> Bids,
    IReadOnlyList<(decimal Price, long Size)> Asks);
```

```csharp
// File: src/Meridian.QuantScript/Api/DataProxy.cs
namespace Meridian.QuantScript.Api;

/// <summary>
/// Synchronous façade over IQuantDataContext for ergonomic script use.
/// Internally calls GetAwaiter().GetResult() on the background script thread.
/// </summary>
public sealed class DataProxy(IQuantDataContext context, Func<CancellationToken> ctProvider)
{
    public PriceSeries Prices(string symbol, DateOnly from, DateOnly to)
        => context.PricesAsync(symbol, from, to, ctProvider()).GetAwaiter().GetResult();

    public PriceSeries Prices(string symbol, DateOnly from, DateOnly to, string provider)
        => context.PricesAsync(symbol, from, to, provider, ctProvider()).GetAwaiter().GetResult();

    public IReadOnlyList<ScriptTrade> Trades(string symbol, DateOnly date)
        => context.TradesAsync(symbol, date, ctProvider()).GetAwaiter().GetResult();

    public ScriptOrderBook? OrderBook(string symbol, DateTimeOffset timestamp)
        => context.OrderBookAsync(symbol, timestamp, ctProvider()).GetAwaiter().GetResult();
}
```

### 3.4 Portfolio Tools

```csharp
// File: src/Meridian.QuantScript/Api/PortfolioBuilder.cs
namespace Meridian.QuantScript.Api;

public static class PortfolioBuilder
{
    /// <summary>Equal-weight portfolio across all provided series.</summary>
    public static PortfolioResult EqualWeight(params PriceSeries[] series);

    /// <summary>Custom weight portfolio. Weights must sum to ~1.0; keys are symbols.</summary>
    public static PortfolioResult CustomWeight(
        IReadOnlyDictionary<string, double> weights, params PriceSeries[] series);

    /// <summary>
    /// Efficient frontier stub — returns equal-weight in v1.
    /// Full quadratic optimisation deferred (see Open Questions).
    /// </summary>
    public static PortfolioResult EfficientFrontier(
        EfficientFrontierConstraints constraints, params PriceSeries[] series);
}

public sealed class EfficientFrontierConstraints
{
    public double TargetReturn { get; init; }
    public double? MinWeight { get; init; } = 0.0;
    public double? MaxWeight { get; init; } = 1.0;
}

public sealed class PortfolioResult
{
    public IReadOnlyDictionary<string, double> Weights { get; }
    public IReadOnlyList<string> Symbols { get; }

    public ReturnSeries Returns();
    public double[,] CorrelationMatrix();
    public double[,] CovarianceMatrix();
    public double SharpeRatio(double riskFreeRate = 0.04);
    public IReadOnlyList<ReturnPoint> Drawdowns();

    /// <summary>Enqueues a correlation heatmap chart.</summary>
    public void PlotHeatmap(string? title = null);

    /// <summary>Enqueues a cumulative return overlay for all constituent series.</summary>
    public void PlotCumulative(string? title = null);
}
```

### 3.5 Backtesting Proxy

```csharp
// File: src/Meridian.QuantScript/Api/BacktestProxy.cs
namespace Meridian.QuantScript.Api;

/// <summary>
/// Fluent backtest builder for use inside scripts. Adapts lambda callbacks to
/// IBacktestStrategy and delegates execution to the existing BacktestEngine.
/// Usage: Backtest.WithSymbols("SPY").From(d1).To(d2).OnBar((bar, ctx) => { ... }).Run()
/// </summary>
public sealed class BacktestProxy(BacktestEngine engine, QuantScriptOptions options)
{
    public BacktestProxy WithSymbols(params string[] symbols);
    public BacktestProxy From(DateOnly from);
    public BacktestProxy To(DateOnly to);
    public BacktestProxy WithInitialCash(decimal cash);
    public BacktestProxy WithFillModel(string model);  // "midpoint" | "orderbook"
    public BacktestProxy WithDataRoot(string path);

    public BacktestProxy OnInitialize(Action<IBacktestContext> handler);
    public BacktestProxy OnBar(Action<HistoricalBar, IBacktestContext> handler);
    public BacktestProxy OnTrade(Action<Trade, IBacktestContext> handler);
    public BacktestProxy OnQuote(Action<BboQuotePayload, IBacktestContext> handler);
    public BacktestProxy OnOrderBook(Action<LOBSnapshot, IBacktestContext> handler);
    public BacktestProxy OnFill(Action<FillEvent, IBacktestContext> handler);
    public BacktestProxy OnDayEnd(Action<DateOnly, IBacktestContext> handler);
    public BacktestProxy OnFinished(Action<IBacktestContext, BacktestResult> handler);

    /// <summary>Runs the backtest synchronously on the calling (script) thread.</summary>
    public BacktestResult Run();

    /// <summary>Runs with a progress callback (forwards BacktestProgressEvent to console).</summary>
    public BacktestResult Run(Action<BacktestProgressEvent> onProgress);
}
```

### 3.6 Plotting

```csharp
// File: src/Meridian.QuantScript/Plotting/PlotQueue.cs
namespace Meridian.QuantScript.Plotting;

/// <summary>
/// Thread-safe unbounded queue of plot requests produced by scripts and
/// consumed by the WPF results panel. Backed by Channel{PlotRequest}.
/// </summary>
public sealed class PlotQueue : IDisposable
{
    public void Enqueue(PlotRequest request);
    public IAsyncEnumerable<PlotRequest> ReadAllAsync(CancellationToken ct = default);
    public void Complete();
    public void Dispose();
}

public sealed record PlotRequest(
    string Title,
    PlotType Type,
    /// <summary>Primary data series (used for Line, CumulativeReturn, Drawdown, Bar, Scatter, Histogram).</summary>
    IReadOnlyList<(DateOnly Date, double Value)>? Series = null,
    /// <summary>Multiple named series for overlay line charts.</summary>
    IReadOnlyList<(string Label, IReadOnlyList<(DateOnly Date, double Value)> Values)>? MultiSeries = null,
    /// <summary>OHLCV data for Candlestick charts.</summary>
    IReadOnlyList<PriceBar>? Candlestick = null,
    /// <summary>Row-major 2D data for Heatmap.</summary>
    double[][]? HeatmapData = null,
    string[]? HeatmapLabels = null);

public enum PlotType
{
    Line,
    MultiLine,
    CumulativeReturn,
    Drawdown,
    Heatmap,
    Candlestick,
    Bar,
    Scatter,
    Histogram
}
```

### 3.7 Compilation Pipeline

```csharp
// File: src/Meridian.QuantScript/Compilation/IQuantScriptCompiler.cs
namespace Meridian.QuantScript.Compilation;

public interface IQuantScriptCompiler
{
    /// <summary>Compiles script source and returns diagnostics. Does not execute.</summary>
    Task<ScriptCompilationResult> CompileAsync(string source, CancellationToken ct = default);

    /// <summary>Reflects [ScriptParam] attributes from top-level variable declarations.</summary>
    IReadOnlyList<ParameterDescriptor> ExtractParameters(string source);
}

public sealed record ScriptCompilationResult(
    bool Success,
    IReadOnlyList<ScriptDiagnostic> Diagnostics);

public sealed record ScriptDiagnostic(
    string Severity,   // "Error" | "Warning"
    string Message,
    int Line,
    int Column);

public sealed record ParameterDescriptor(
    string Name,
    string TypeName,
    string Label,
    object? DefaultValue,
    double Min,
    double Max,
    string? Description);
```

```csharp
// File: src/Meridian.QuantScript/Compilation/IScriptRunner.cs
namespace Meridian.QuantScript.Compilation;

public interface IScriptRunner
{
    /// <summary>
    /// Compiles and executes a script, injecting QuantScriptGlobals.
    /// Console output, plots, and metrics are forwarded via the globals' internal channels.
    /// </summary>
    Task<ScriptRunResult> RunAsync(
        string source,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct = default);
}

public sealed record ScriptRunResult(
    bool Success,
    TimeSpan Elapsed,
    IReadOnlyList<ScriptDiagnostic> CompilationErrors,
    string? RuntimeError);
```

### 3.8 Script Globals

```csharp
// File: src/Meridian.QuantScript/Compilation/QuantScriptGlobals.cs
namespace Meridian.QuantScript.Compilation;

/// <summary>
/// Injected as the Roslyn script globals object. All members are visible as top-level
/// identifiers inside .csx scripts. CancellationToken is exposed for scripts that loop.
/// </summary>
public sealed class QuantScriptGlobals
{
    // ── Primary APIs ─────────────────────────────────────────────────────────
    public DataProxy Data { get; }
    public BacktestProxy Backtest { get; }
    public PlotQueue PlotQueue { get; }   // internal plumbing; scripts use .Plot() on series

    // ── Portfolio factory ────────────────────────────────────────────────────
    // Scripts call: var p = EqualWeight(spy, qqq)  or  CustomWeight(weights, spy, qqq)
    public PortfolioResult EqualWeight(params PriceSeries[] series)
        => PortfolioBuilder.EqualWeight(series);
    public PortfolioResult CustomWeight(
        IReadOnlyDictionary<string, double> weights, params PriceSeries[] series)
        => PortfolioBuilder.CustomWeight(weights, series);

    // ── Standalone statistical helpers (mirror ReturnSeries instance methods) ─
    public double SharpeRatio(ReturnSeries r, double riskFreeRate = 0.04) => r.SharpeRatio(riskFreeRate);
    public double SortinoRatio(ReturnSeries r, double riskFreeRate = 0.04) => r.SortinoRatio(riskFreeRate);
    public double AnnualizedVolatility(ReturnSeries r) => r.AnnualizedVolatility();
    public double MaxDrawdown(ReturnSeries r) => r.MaxDrawdown();
    public double Beta(ReturnSeries r, ReturnSeries benchmark) => r.Beta(benchmark);
    public double Alpha(ReturnSeries r, ReturnSeries benchmark, double rfr = 0.04) => r.Alpha(benchmark, rfr);
    public double Correlation(ReturnSeries a, ReturnSeries b) => a.Correlation(b);

    // ── Output ───────────────────────────────────────────────────────────────
    public void Print(object? value);
    public void PrintTable<T>(IEnumerable<T> rows);
    public void PrintMetric(string label, object value);

    // ── Cancellation ─────────────────────────────────────────────────────────
    public CancellationToken CancellationToken { get; }
}
```

### 3.9 Script Parameter Attribute

```csharp
// File: src/Meridian.QuantScript/Api/ScriptParamAttribute.cs
namespace Meridian.QuantScript.Api;

/// <summary>
/// Marks a top-level variable declaration in a .csx script as a user-configurable parameter.
/// The UI reflects these to render a dynamic parameter form before execution.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public sealed class ScriptParamAttribute(string label) : Attribute
{
    public string Label { get; } = label;
    public object? Default { get; init; }
    public double Min { get; init; } = double.MinValue;
    public double Max { get; init; } = double.MaxValue;
    public string? Description { get; init; }
}
```

### 3.10 Configuration

```csharp
// File: src/Meridian.QuantScript/QuantScriptOptions.cs
namespace Meridian.QuantScript;

public sealed class QuantScriptOptions
{
    public const string SectionName = "QuantScript";

    /// <summary>Directory to scan for .csx script files.</summary>
    public string ScriptsDirectory { get; init; } = "scripts";

    /// <summary>Maximum wall-clock seconds a script may run before cancellation.</summary>
    public int RunTimeoutSeconds { get; init; } = 300;

    /// <summary>Maximum seconds allowed for Roslyn compilation.</summary>
    public int CompilationTimeoutSeconds { get; init; } = 15;

    /// <summary>
    /// When false (default), scripts are denied File/Network/Process access via
    /// Roslyn's MetadataReferenceResolver restriction list.
    /// </summary>
    public bool EnableUnsafeScripts { get; init; } = false;

    /// <summary>Soft limit on plot requests per run. Excess plots are silently dropped.</summary>
    public int MaxPlotsPerRun { get; init; } = 100;

    /// <summary>Default data root passed to BacktestProxy when not overridden in script.</summary>
    public string DefaultDataRoot { get; init; } = "./data";
}
```

```json
// appsettings.json addition:
{
  "QuantScript": {
    "ScriptsDirectory": "scripts",
    "RunTimeoutSeconds": 300,
    "CompilationTimeoutSeconds": 15,
    "EnableUnsafeScripts": false,
    "MaxPlotsPerRun": 100,
    "DefaultDataRoot": "./data"
  }
}
```

---

## Step 4: Component Design

### 4.1 QuantDataContext

**Namespace:** `Meridian.QuantScript.Api`
**Type:** `sealed class QuantDataContext : IQuantDataContext`
**Lifetime:** Singleton

**Responsibilities:**
- Bridges `IQuantDataContext` to `HistoricalDataQueryService` and `JsonlMarketDataStore`
- Translates `HistoricalBar` records to `PriceBar` for `PriceSeries`
- Translates `Trade` events to `ScriptTrade`; `LOBSnapshot` to `ScriptOrderBook`
- Logs a console warning (not exception) when no data is found for a symbol/date range

**Dependencies:**
```csharp
public QuantDataContext(
    HistoricalDataQueryService queryService,
    ILogger<QuantDataContext> logger)
```

**Key Internal State:** None — stateless pass-through.

**Error Handling:** If no bars found, returns `PriceSeries` with empty `Bars` list; does not throw. Logs `LogWarning`.

---

### 4.2 RoslynScriptCompiler

**Namespace:** `Meridian.QuantScript.Compilation`
**Type:** `sealed class RoslynScriptCompiler : IQuantScriptCompiler`
**Lifetime:** Singleton

**Responsibilities:**
- Compiles `.csx` source via `CSharpScript.Create<object>(source, options, globalsType: typeof(QuantScriptGlobals))`
- Builds `ScriptOptions` with references to all Meridian assemblies needed by scripts
- Caches compiled `Script<object>` by `SHA256(source)` to avoid recompilation on identical re-runs
- Extracts `[ScriptParam]` metadata via Roslyn `SyntaxTree` — walks `LocalDeclarationStatementSyntax` nodes looking for `ScriptParamAttribute` on trivia/comments pattern OR uses a convention: variables declared at top level with a `// [Param]` comment (simplified v1 approach — see Open Questions)
- Produces `IReadOnlyList<ScriptDiagnostic>` from `Diagnostic[]`

**Dependencies:**
```csharp
public RoslynScriptCompiler(
    IOptions<QuantScriptOptions> options,
    ILogger<RoslynScriptCompiler> logger)
```

**Key Internal State:**
```csharp
private readonly ConcurrentDictionary<string, Script<object>> _cache = new();
```

**Concurrency Model:** `ConcurrentDictionary` for cache; compilation itself is `await`ed on the caller's thread.

**Script References added to ScriptOptions:**
- `Meridian.QuantScript`
- `Meridian.Backtesting`
- `Meridian.Backtesting.Sdk`
- `Meridian.Contracts`
- `Skender.Stock.Indicators`

---

### 4.3 ScriptRunner

**Namespace:** `Meridian.QuantScript.Compilation`
**Type:** `sealed class ScriptRunner : IScriptRunner`
**Lifetime:** Scoped (one per UI session is acceptable; Singleton also works since state is per-run)

**Responsibilities:**
- Holds references to `IQuantScriptCompiler`, `IQuantDataContext`, `PlotQueue`, `BacktestEngine`
- For each `RunAsync` call: constructs `QuantScriptGlobals` with a fresh `CancellationTokenSource` linked to the caller's `ct` and the configured timeout
- Calls `compiler.CompileAsync` then `script.RunAsync(globals, ct)`
- Captures `Console.Out` via `StringWriter` redirect to collect `Print()` output
- Catches `OperationCanceledException` and `Exception`, populates `ScriptRunResult`
- Completes the `PlotQueue` after execution so the UI drain loop terminates cleanly

**Dependencies:**
```csharp
public ScriptRunner(
    IQuantScriptCompiler compiler,
    IQuantDataContext dataContext,
    PlotQueue plotQueue,
    BacktestEngine backtestEngine,
    IOptions<QuantScriptOptions> options,
    ILogger<ScriptRunner> logger)
```

**Key Internal State:** None persisted between runs.

**Concurrency Model:** `CancellationTokenSource` with `TimeSpan` from `options.RunTimeoutSeconds` is created per `RunAsync` call. Only one concurrent run per `ScriptRunner` instance is guaranteed by the `QuantScriptViewModel` setting `IsRunning = true`.

---

### 4.4 PlotQueue

**Namespace:** `Meridian.QuantScript.Plotting`
**Type:** `sealed class PlotQueue : IDisposable`
**Lifetime:** Singleton (shared between `ScriptRunner` producer and `QuantScriptViewModel` consumer)

**Key Internal State:**
```csharp
private readonly Channel<PlotRequest> _channel =
    Channel.CreateUnbounded<PlotRequest>(new UnboundedChannelOptions { SingleReader = true });
```

**Concurrency Model:** `Channel<T>` is thread-safe. `Enqueue` called from script (background thread); `ReadAllAsync` drained by `QuantScriptViewModel` on the UI thread via `await foreach`.

---

### 4.5 StatisticsEngine

**Namespace:** `Meridian.QuantScript.Api`
**Type:** `internal static class StatisticsEngine`

**Responsibilities:** Pure math. All statistical calculations used by `ReturnSeries` instance methods delegate here. No DI, no I/O.

**Key methods:**
```csharp
internal static double Sharpe(IReadOnlyList<double> dailyReturns, double annualRfr);
internal static double Sortino(IReadOnlyList<double> dailyReturns, double annualRfr);
internal static double AnnualizedVolatility(IReadOnlyList<double> dailyReturns);
internal static double MaxDrawdown(IReadOnlyList<double> dailyReturns);
internal static IReadOnlyList<double> DrawdownSeries(IReadOnlyList<double> dailyReturns);
internal static double Beta(IReadOnlyList<double> returns, IReadOnlyList<double> benchmarkReturns);
internal static double Alpha(IReadOnlyList<double> returns, IReadOnlyList<double> benchmarkReturns, double annualRfr);
internal static double Correlation(IReadOnlyList<double> a, IReadOnlyList<double> b);
internal static double Skewness(IReadOnlyList<double> values);
internal static double Kurtosis(IReadOnlyList<double> values);
internal static IReadOnlyList<double> RollingMean(IReadOnlyList<double> values, int window);
internal static IReadOnlyList<double> RollingSd(IReadOnlyList<double> values, int window);
internal static double[,] CorrelationMatrix(IReadOnlyList<IReadOnlyList<double>> returnStreams);
internal static double[,] CovarianceMatrix(IReadOnlyList<IReadOnlyList<double>> returnStreams);
// Aligns two series by DateOnly, returns matched pairs:
internal static (IReadOnlyList<double> a, IReadOnlyList<double> b)
    AlignByDate(ReturnSeries a, ReturnSeries b);
```

---

### 4.6 LambdaBacktestStrategy (internal adapter)

**Namespace:** `Meridian.QuantScript.Api`
**Type:** `internal sealed class LambdaBacktestStrategy : IBacktestStrategy`

Bridges `BacktestProxy`'s captured lambdas to `IBacktestStrategy`. Each optional callback defaults to a no-op. `Name` property returns `"ScriptStrategy"`.

---

### 4.7 QuantScriptViewModel

**Namespace:** `Meridian.Wpf.ViewModels`
**Type:** `sealed class QuantScriptViewModel : BindableBase`
**Lifetime:** Singleton (registered in WPF DI)

**Responsibilities:**
- Loads `.csx` file list from `ScriptsDirectory` on construction
- Reflects `[ScriptParam]` metadata from selected script to populate `Parameters` collection
- On `RunCommand`: collects parameter values → calls `IScriptRunner.RunAsync` on `Task.Run` → drains `PlotQueue` via `await foreach` on UI thread → populates `Charts`, `ConsoleOutput`, `Metrics`, `Trades`
- On `StopCommand`: cancels the `CancellationTokenSource`
- Marshals all `ObservableCollection` mutations via `Application.Current.Dispatcher`

**Dependencies:**
```csharp
public QuantScriptViewModel(
    IScriptRunner runner,
    IQuantScriptCompiler compiler,
    IOptions<QuantScriptOptions> options,
    ILogger<QuantScriptViewModel> logger)
```

**Key Properties:**
```csharp
public string ScriptSource { get => _scriptSource; set => SetProperty(ref _scriptSource, value); }
public ObservableCollection<ScriptFileEntry> ScriptFiles { get; } = [];
public ScriptFileEntry? SelectedScript { get => _selected; set { SetProperty(ref _selected, value); LoadScript(value); } }
public ObservableCollection<ParameterViewModel> Parameters { get; } = [];
public ObservableCollection<ConsoleEntry> ConsoleOutput { get; } = [];
public ObservableCollection<PlotViewModel> Charts { get; } = [];
public ObservableCollection<MetricEntry> Metrics { get; } = [];
public ObservableCollection<FillEvent> Trades { get; } = [];
public bool IsRunning { get => _isRunning; private set => SetProperty(ref _isRunning, value); }
public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
public ICommand RunCommand { get; }
public ICommand StopCommand { get; }
public ICommand NewScriptCommand { get; }
public ICommand SaveScriptCommand { get; }
public ICommand RefreshScriptsCommand { get; }
```

**Supporting model types:**
```csharp
public sealed record ScriptFileEntry(string Name, string FullPath);
public sealed record ConsoleEntry(DateTimeOffset Timestamp, string Text, ConsoleEntryKind Kind);
public enum ConsoleEntryKind { Output, Warning, Error }
public sealed record MetricEntry(string Label, string Value);
public sealed class ParameterViewModel : BindableBase
{
    public ParameterDescriptor Descriptor { get; }
    public string RawValue { get => _raw; set => SetProperty(ref _raw, value); }
    public object? ParsedValue { get; }
}
public sealed record PlotViewModel(string Title, PlotRequest Request);
```

---

## Step 5: Data Flow

### Path 1: Analytical Script (Data → Stats → Plot)

```
Script source:
  var spy = Data.Prices("SPY", new DateOnly(2023,1,1), new DateOnly(2024,1,1));
  var ret = spy.DailyReturns();
  PrintMetric("Sharpe", ret.SharpeRatio());
  ret.PlotCumulative("SPY 2023");
```

1. **User clicks Run** → `QuantScriptViewModel.RunCommand` executes
2. `IsRunning = true`; `CancellationTokenSource` created with 300s timeout
3. Parameter values collected from `Parameters` → `IReadOnlyDictionary<string,object?>`
4. `IScriptRunner.RunAsync(source, parameters, ct)` called on `Task.Run` (background thread)
5. `ScriptRunner` constructs `QuantScriptGlobals` — injects `DataProxy`, `PlotQueue`, `BacktestProxy`
6. Roslyn `script.RunAsync(globals, ct)` begins executing script body
7. **`Data.Prices("SPY", ...)`** → `DataProxy.Prices` → `IQuantDataContext.PricesAsync(...).GetAwaiter().GetResult()`
8. `QuantDataContext.PricesAsync` → `HistoricalDataQueryService.QueryBarsAsync("SPY", from, to)`
9. `HistoricalDataQueryService` → `JsonlMarketDataStore` → reads `data/SPY/bars/*.jsonl`
10. `IReadOnlyList<HistoricalBar>` returned → mapped to `IReadOnlyList<PriceBar>` → `new PriceSeries("SPY", bars)`
11. `spy.DailyReturns()` → `PriceSeriesExtensions.DailyReturns` → computes `(Close[t]-Close[t-1])/Close[t-1]` for each bar → `new ReturnSeries(...)`
12. `ret.SharpeRatio()` → `StatisticsEngine.Sharpe(points, 0.04)` → `double` returned
13. `globals.PrintMetric("Sharpe", 2.31)` → enqueues `MetricEntry` on internal `Channel<MetricEntry>`
14. `ret.PlotCumulative("SPY 2023")` → `PlotQueue.Enqueue(new PlotRequest("SPY 2023", PlotType.CumulativeReturn, cumulativeSeries))`
15. Script returns normally → `ScriptRunner` calls `PlotQueue.Complete()`
16. **Back on UI thread**: `QuantScriptViewModel` drains `PlotQueue` via `await foreach`
17. For each `PlotRequest` → creates `PlotViewModel` → appends to `Charts` collection
18. `QuantScriptPage` TabControl switches to Charts tab; `WpfPlot` control renders via ScottPlot
19. `IsRunning = false`; `StatusText = "Completed in 1.4s"`

### Path 2: Backtest Script (OnBar → Engine → Metrics)

```
Script source:
  Backtest
    .WithSymbols("SPY")
    .From(new DateOnly(2022,1,1))
    .To(new DateOnly(2023,12,31))
    .OnBar((bar, ctx) => {
        if (ctx.GetLastPrice("SPY") is null) return;
        ctx.PlaceMarketOrder("SPY", 10);
    })
    .Run();
```

1–6. Same as Path 1 up to Roslyn execution start
7. `Backtest.WithSymbols("SPY").From(...).To(...).OnBar(...).Run()` called
8. `BacktestProxy.Run()` → builds `BacktestRequest(From, To, ["SPY"], InitialCash=100_000, DataRoot=options.DefaultDataRoot)`
9. `BacktestProxy` instantiates `LambdaBacktestStrategy` (wraps captured `onBar` lambda)
10. `BacktestEngine.RunAsync(request, strategy, progress, ct)` called synchronously via `.GetAwaiter().GetResult()`
11. `BacktestEngine` → `UniverseDiscovery.DiscoverAsync` → scans catalog
12. `MultiSymbolMergeEnumerator.MergeAsync` replays `HistoricalBar` events chronologically
13. For each `HistoricalBar` event → `strategy.OnBar(bar, ctx)` → `LambdaBacktestStrategy` calls captured lambda
14. Lambda calls `ctx.PlaceMarketOrder("SPY", 10)` → `BacktestContext.DrainPendingOrders()` → `BarMidpointFillModel.TryFill`
15. Fills accumulated in `allFills`; day snapshots in `allSnapshots`
16. `BacktestMetricsEngine.Compute(snapshots, cashFlows, fills, request)` → `BacktestMetrics`
17. `BacktestResult` returned to `BacktestProxy.Run()`
18. Script body ends → `ScriptRunner` reads `BacktestResult` from globals → dispatches fills to `Trades` channel, metrics to `Metrics` channel
19. **UI thread**: `QuantScriptViewModel` populates `Trades` (DataGrid) and `Metrics` (key/value table)
20. TabControl shows Trades tab automatically if fills > 0

### Error Path: Compilation Failure

1. User types invalid C# → clicks Run
2. `RoslynScriptCompiler.CompileAsync` returns `ScriptCompilationResult(Success: false, Diagnostics: [...])`
3. `ScriptRunner` returns `ScriptRunResult(Success: false, CompilationErrors: [...], RuntimeError: null)`
4. `QuantScriptViewModel` maps `CompilationErrors` to `ConsoleEntry` items with `Kind = Error`
5. ConsoleOutput tab shows red error lines with line/column info
6. `IsRunning = false`; `StatusText = "Compilation failed (2 errors)"`

---

## Step 6: XAML Design

### QuantScriptPage.xaml

**Layout:** Three-column `Grid` with `GridSplitter` separators

```
┌────────────────┬──────────────────────────────────┬───────────────────────┐
│ Col 0 (220px)  │ Col 2 (*)                         │ Col 4 (380px)         │
│                │                                   │                       │
│ Script Browser │  AvalonEdit TextEditor            │ TabControl            │
│ (ListBox)      │  (syntax: C#)                     │  ┌─Console──────────┐ │
│                │                                   │  │ ConsoleOutput     │ │
│ scripts/       │                                   │  │ (ItemsControl)    │ │
│ ├ momentum.csx │                                   │  └──────────────────┘ │
│ ├ mean-rev.csx │                                   │  ┌─Charts───────────┐ │
│ └ sharpe.csx   │                                   │  │ WpfPlot items     │ │
│                │                                   │  └──────────────────┘ │
│ ──────────────  │                                   │  ┌─Metrics──────────┐ │
│ Parameters     │                                   │  │ DataGrid          │ │
│                │  ─────────────────────────────── │  └──────────────────┘ │
│ [Label] [Input]│  [▶ Run] [■ Stop] [New] [Save]   │  ┌─Trades───────────┐ │
│ [Label] [Input]│  Status: Ready                   │  │ DataGrid fills    │ │
└────────────────┴──────────────────────────────────┴───────────────────────┘
```

**Key XAML structure:**

```xml
<Page x:Class="Meridian.Wpf.Views.QuantScriptPage"
      xmlns:avalonedit="http://icsharpcode.net/sharpdevelop/avalonedit"
      xmlns:scottplot="clr-namespace:ScottPlot.WPF;assembly=ScottPlot.WPF">

  <Grid>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width="220" MinWidth="150"/>
      <ColumnDefinition Width="4"/>   <!-- GridSplitter -->
      <ColumnDefinition Width="*" MinWidth="300"/>
      <ColumnDefinition Width="4"/>   <!-- GridSplitter -->
      <ColumnDefinition Width="380" MinWidth="250"/>
    </Grid.ColumnDefinitions>

    <!-- Col 0: Script browser + parameters -->
    <DockPanel Grid.Column="0">
      <TextBlock DockPanel.Dock="Top" Text="Scripts" Style="{StaticResource SectionHeader}"/>
      <ListBox ItemsSource="{Binding ScriptFiles}"
               SelectedItem="{Binding SelectedScript}"
               DisplayMemberPath="Name"/>
      <Separator/>
      <TextBlock Text="Parameters" Style="{StaticResource SectionHeader}"/>
      <ItemsControl ItemsSource="{Binding Parameters}">
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <StackPanel Orientation="Horizontal" Margin="0,2">
              <TextBlock Text="{Binding Descriptor.Label}" Width="90" VerticalAlignment="Center"/>
              <TextBox Text="{Binding RawValue, UpdateSourceTrigger=PropertyChanged}" Width="80"/>
            </StackPanel>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
    </DockPanel>

    <GridSplitter Grid.Column="1" HorizontalAlignment="Stretch"/>

    <!-- Col 2: Editor + toolbar -->
    <DockPanel Grid.Column="2">
      <!-- Toolbar -->
      <ToolBar DockPanel.Dock="Bottom">
        <Button Content="▶ Run" Command="{Binding RunCommand}"
                IsEnabled="{Binding IsRunning, Converter={StaticResource NotBool}}"/>
        <Button Content="■ Stop" Command="{Binding StopCommand}"
                IsEnabled="{Binding IsRunning}"/>
        <Separator/>
        <Button Content="New" Command="{Binding NewScriptCommand}"/>
        <Button Content="Save" Command="{Binding SaveScriptCommand}"/>
        <Separator/>
        <TextBlock Text="{Binding StatusText}" VerticalAlignment="Center" Margin="4,0"/>
      </ToolBar>
      <!-- AvalonEdit -->
      <avalonedit:TextEditor x:Name="ScriptEditor"
                             SyntaxHighlighting="C#"
                             ShowLineNumbers="True"
                             FontFamily="Cascadia Code, Consolas, Monospace"
                             FontSize="13"
                             Document="{Binding ScriptDocument, Mode=TwoWay}"/>
    </DockPanel>

    <GridSplitter Grid.Column="3" HorizontalAlignment="Stretch"/>

    <!-- Col 4: Results TabControl -->
    <TabControl Grid.Column="4">

      <TabItem Header="Console">
        <ItemsControl ItemsSource="{Binding ConsoleOutput}">
          <ItemsControl.ItemTemplate>
            <DataTemplate>
              <TextBlock Text="{Binding Text}"
                         Foreground="{Binding Kind, Converter={StaticResource ConsoleColorConverter}}"
                         FontFamily="Cascadia Code, Consolas" FontSize="11"/>
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
      </TabItem>

      <TabItem Header="Charts">
        <ItemsControl ItemsSource="{Binding Charts}">
          <ItemsControl.ItemTemplate>
            <DataTemplate>
              <StackPanel>
                <TextBlock Text="{Binding Title}" FontWeight="SemiBold" Margin="0,4,0,2"/>
                <scottplot:WpfPlot x:Name="PlotControl" Height="220"
                                   DataContext="{Binding Request}"/>
              </StackPanel>
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
      </TabItem>

      <TabItem Header="Metrics">
        <DataGrid ItemsSource="{Binding Metrics}" AutoGenerateColumns="False"
                  IsReadOnly="True" HeadersVisibility="Column">
          <DataGrid.Columns>
            <DataGridTextColumn Header="Metric" Binding="{Binding Label}" Width="160"/>
            <DataGridTextColumn Header="Value"  Binding="{Binding Value}" Width="*"/>
          </DataGrid.Columns>
        </DataGrid>
      </TabItem>

      <TabItem Header="Trades">
        <DataGrid ItemsSource="{Binding Trades}" AutoGenerateColumns="False"
                  IsReadOnly="True" HeadersVisibility="Column">
          <DataGrid.Columns>
            <DataGridTextColumn Header="Time"   Binding="{Binding FilledAt, StringFormat='HH:mm:ss'}" Width="80"/>
            <DataGridTextColumn Header="Symbol" Binding="{Binding Symbol}"     Width="70"/>
            <DataGridTextColumn Header="Qty"    Binding="{Binding FilledQuantity}" Width="60"/>
            <DataGridTextColumn Header="Price"  Binding="{Binding FillPrice, StringFormat='N2'}" Width="80"/>
            <DataGridTextColumn Header="Comm."  Binding="{Binding Commission, StringFormat='N2'}" Width="70"/>
          </DataGrid.Columns>
        </DataGrid>
      </TabItem>

    </TabControl>
  </Grid>
</Page>
```

**QuantScriptPage.xaml.cs** — wires AvalonEdit `TextChanged` to `ViewModel.ScriptSource` (AvalonEdit doesn't support standard `Binding` on its text; use `TextChanged` event or attached property).

**Navigation registration** — add `QuantScriptPage` to `Pages.cs` enum and `NavigationService` mapping in `MainPage.xaml` sidebar.

---

## Step 7: Test Plan

**Principle:** Mock at the interface boundary. `StatisticsEngine` and series extension methods tested with known numeric fixtures. Roslyn tests use real in-process compilation against a minimal script string.

### Unit Tests — PriceSeries / ReturnSeries (`PriceSeriesTests.cs`)

| Test Name | What It Verifies |
|-----------|-----------------|
| `DailyReturns_TwoBars_ReturnsSingleReturn` | `(Close[1]-Close[0])/Close[0]` correct |
| `DailyReturns_SingleBar_ReturnsEmptySeries` | No return for 1 bar |
| `LogReturns_TwoBars_ReturnsLnRatio` | `ln(C1/C0)` correct |
| `CumulativeReturns_ThreeBars_CompoundsCorrectly` | `(1+r1)(1+r2)-1` correct |
| `RollingReturns_Window3_Returns5Bar_ProducesCorrectCount` | count = floor(n/window) |
| `DailyReturns_NegativeReturn_HandledCorrectly` | price decline produces negative return |

### Unit Tests — StatisticsEngine (`StatisticsEngineTests.cs`)

| Test Name | What It Verifies |
|-----------|-----------------|
| `Sharpe_KnownReturns_MatchesManualCalculation` | against hand-computed value |
| `Sharpe_ZeroVolatility_ReturnsZero` | guards against divide-by-zero |
| `Sortino_AllPositiveReturns_ReturnsPositiveInfinity` | no downside deviation |
| `Sortino_KnownReturns_MatchesManualCalculation` | against hand-computed value |
| `MaxDrawdown_FlatSeries_ReturnsZero` | no drawdown |
| `MaxDrawdown_KnownDip_ReturnsCorrectFraction` | peak-to-trough fraction |
| `Beta_PerfectlyCorrelated_ReturnsOne` | spy vs spy → β=1 |
| `Beta_Uncorrelated_ReturnsNearZero` | random vs market |
| `Alpha_BenchmarkEqualsAsset_ReturnsNearZero` | no excess return |
| `Correlation_PerfectPositive_ReturnsOne` | r=1 |
| `Correlation_PerfectNegative_ReturnsNegativeOne` | r=-1 |
| `Correlation_MismatchedLengths_AlignsToShorter` | date alignment |
| `Skewness_SymmetricData_ReturnsNearZero` | |
| `Kurtosis_NormalData_ReturnsNearZero` | excess kurtosis |
| `RollingMean_Window3_FirstTwoNaN` | insufficient window |
| `RollingSd_Window3_KnownSeries_MatchesManual` | |
| `AnnualizedVolatility_KnownDailyVol_Annualizes` | × √252 |

### Unit Tests — RoslynScriptCompiler (`RoslynScriptCompilerTests.cs`)

| Test Name | What It Verifies |
|-----------|-----------------|
| `CompileAsync_ValidScript_ReturnsSuccess` | no diagnostics |
| `CompileAsync_SyntaxError_ReturnsDiagnosticsWithLineInfo` | error on correct line |
| `CompileAsync_SameSourceTwice_ReturnsCachedResult` | cache hit (same object reference) |
| `CompileAsync_DifferentSource_ReturnsDifferentCompilation` | cache miss |
| `ExtractParameters_NoAttributes_ReturnsEmptyList` | |
| `ExtractParameters_OneScriptParam_ReturnsDescriptor` | label, default, min, max |
| `CompileAsync_CancellationRequested_ThrowsOperationCanceledException` | |

### Unit Tests — ScriptRunner (`ScriptRunnerTests.cs`)

| Test Name | What It Verifies |
|-----------|-----------------|
| `RunAsync_HelloWorldScript_ReturnSuccess` | basic execution |
| `RunAsync_PrintCall_OutputCapturedInConsole` | `Print("hello")` captured |
| `RunAsync_CancellationMidRun_ReturnsRuntimeError` | CT propagated |
| `RunAsync_CompilationError_ReturnsFailureWithDiagnostics` | surfaced without throw |
| `RunAsync_ScriptThrowsException_ReturnsRuntimeError` | uncaught exception in script |
| `RunAsync_ParameterInjected_ScriptUsesValue` | parameter dict → globals |

### Unit Tests — PlotQueue (`PlotQueueTests.cs`)

| Test Name | What It Verifies |
|-----------|-----------------|
| `Enqueue_SingleRequest_CanBeReadAsync` | basic producer-consumer |
| `Enqueue_MultipleRequests_ReadInOrder` | FIFO ordering |
| `Complete_AfterEnqueue_DrainTerminates` | `ReadAllAsync` completes |
| `ReadAllAsync_CancellationBeforeComplete_StopsIteration` | CT respected |

### Unit Tests — QuantScriptViewModel (`QuantScriptViewModelTests.cs`)

| Test Name | What It Verifies |
|-----------|-----------------|
| `RunCommand_WhenNotRunning_CanExecute` | `IsRunning=false` → enabled |
| `RunCommand_WhenRunning_CannotExecute` | `IsRunning=true` → disabled |
| `StopCommand_WhenRunning_CanExecute` | |
| `StopCommand_WhenNotRunning_CannotExecute` | |
| `SelectedScript_Changed_LoadsScriptSource` | file content loaded into `ScriptSource` |
| `RunAsync_CompletesSuccessfully_IsRunningSetFalse` | state reset after run |

**Test Infrastructure Needed:**
- `FakeQuantDataContext` — returns deterministic `PriceSeries` for test symbols
- `FakeScriptRunner` — returns configurable `ScriptRunResult`
- `TestPriceSeriesBuilder` — fluent helper to construct `PriceSeries` from decimal arrays

---

## Step 8: Implementation Checklist

**Estimated effort:** XL (3–4 weeks solo)
**Suggested branch:** `feature/quant-script-environment`
**Suggested PR sequence:** PR1 (QuantScript library + tests), PR2 (WPF integration)

### Phase 1: Foundation

- [ ] Create `src/Meridian.QuantScript/Meridian.QuantScript.csproj`
  - Target `net9.0-windows`; reference `Meridian.Backtesting`, `Meridian.Backtesting.Sdk`, `Meridian.Application`, `Meridian.Storage`, `Meridian.Contracts`
  - PackageReference (no version): `Microsoft.CodeAnalysis.CSharp.Scripting`, `Skender.Stock.Indicators`
- [ ] Add `Microsoft.CodeAnalysis.CSharp.Scripting` version `5.0.0` to `Directory.Packages.props` (same version as existing `Microsoft.CodeAnalysis.CSharp`)
- [ ] Add `AvalonEdit` version `6.3.0.90` to `Directory.Packages.props`
- [ ] Add `ScottPlot.WPF` version `5.0.55` to `Directory.Packages.props`
- [ ] Add `QuantScript` project to `Meridian.sln`
- [ ] Add `QuantScriptOptions` class and register `services.Configure<QuantScriptOptions>` in `ServiceCompositionRoot` or WPF DI setup
- [ ] Add `QuantScript` section to `config/appsettings.json` with defaults
- [ ] Create test project `tests/Meridian.QuantScript.Tests/`; add to solution

### Phase 2: Core API (QuantScript library)

- [ ] `Api/PriceBar.cs` — `sealed record PriceBar(...)`
- [ ] `Api/PriceSeries.cs` — `sealed class PriceSeries`
- [ ] `Api/ReturnSeries.cs` — `sealed class ReturnSeries` with all stat instance methods delegating to `StatisticsEngine`
- [ ] `Api/StatisticsEngine.cs` — `internal static class` with all math (no external deps)
- [ ] `Api/PriceSeriesExtensions.cs` — `DailyReturns`, `LogReturns`, `CumulativeReturns`, `RollingReturns`
- [ ] `Api/TechnicalSeriesExtensions.cs` — `Sma`, `Ema`, `Rsi`, `Macd`, `BollingerBands` via `Skender.Stock.Indicators`
- [ ] `Api/ScriptTrade.cs`, `Api/ScriptOrderBook.cs`
- [ ] `Api/IQuantDataContext.cs`
- [ ] `Api/QuantDataContext.cs` — implement `IQuantDataContext` wrapping `HistoricalDataQueryService`
- [ ] `Api/DataProxy.cs`
- [ ] `Api/ScriptParamAttribute.cs`
- [ ] `Api/PortfolioBuilder.cs` + `PortfolioResult.cs` (`EfficientFrontier` returns equal-weight stub + `// TODO` comment)
- [ ] `Api/EfficientFrontierConstraints.cs`
- [ ] `Api/LambdaBacktestStrategy.cs` — `internal sealed class` implementing `IBacktestStrategy`
- [ ] `Api/BacktestProxy.cs`
- [ ] `Plotting/PlotRequest.cs` + `PlotType.cs`
- [ ] `Plotting/PlotQueue.cs`
- [ ] `QuantScriptOptions.cs`

### Phase 3: Compilation Pipeline

- [ ] `Compilation/IQuantScriptCompiler.cs` + supporting records
- [ ] `Compilation/IScriptRunner.cs` + supporting records
- [ ] `Compilation/QuantScriptGlobals.cs`
- [ ] `Compilation/RoslynScriptCompiler.cs` — compile, cache, extract parameters
- [ ] `Compilation/ScriptRunner.cs` — run, capture output, handle cancellation, collect results
- [ ] Register `IQuantDataContext → QuantDataContext`, `IQuantScriptCompiler → RoslynScriptCompiler`, `IScriptRunner → ScriptRunner`, `PlotQueue` as Singleton in DI
- [ ] Wire `PlotRequest.Plot()` calls in `ReturnSeries` and `PortfolioResult` to inject `PlotQueue` via a thread-static or `AsyncLocal<PlotQueue>` set by `ScriptRunner` before execution

### Phase 4: WPF Integration

- [ ] `Models/QuantScriptModels.cs` — `ScriptFileEntry`, `ConsoleEntry`, `ConsoleEntryKind`, `MetricEntry`, `ParameterViewModel`, `PlotViewModel`
- [ ] `ViewModels/QuantScriptViewModel.cs`
- [ ] `Views/QuantScriptPage.xaml` + `QuantScriptPage.xaml.cs` — full XAML as designed above
- [ ] Add `QuantScriptPage` entry to `Views/Pages.cs` enum
- [ ] Register `QuantScriptPage` in `NavigationService` mapping
- [ ] Add navigation item to `MainWindow.xaml` or `MainPage.xaml` sidebar
- [ ] Add `AvalonEdit` and `ScottPlot.WPF` PackageReferences to `Meridian.Wpf.csproj`
- [ ] Implement `PlotViewModel → WpfPlot` rendering in `QuantScriptPage.xaml.cs` code-behind (ScottPlot WpfPlot requires imperative API calls; use `Loaded` event on each plot control)
- [ ] Implement `ConsoleColorConverter` (value converter: `ConsoleEntryKind → Brush`)

### Phase 5: Tests

- [ ] `PriceSeriesTests.cs` — 6 tests
- [ ] `StatisticsEngineTests.cs` — 17 tests
- [ ] `RoslynScriptCompilerTests.cs` — 7 tests (real in-process Roslyn compilation)
- [ ] `ScriptRunnerTests.cs` — 6 tests
- [ ] `PlotQueueTests.cs` — 4 tests
- [ ] `QuantScriptViewModelTests.cs` — 6 tests
- [ ] Add `FakeQuantDataContext`, `FakeScriptRunner`, `TestPriceSeriesBuilder` to test helpers
- [ ] All 46 tests green

### Phase 6: Wrap-up

- [ ] Verify `appsettings.json` and `appsettings.sample.json` have `QuantScript` section
- [ ] Add `Meridian.QuantScript` and its test project to `Meridian.sln`
- [ ] Check ADR compliance: `QuantDataContext` wraps async storage correctly per ADR-004; `PlotQueue` uses bounded channel pattern per ADR-013 (unbounded is justified — document exception)
- [ ] Add `[ImplementsAdr("ADR-004", "All async data access methods support CancellationToken")]` to `IQuantDataContext`
- [ ] Add XML doc comments to all public interfaces and classes
- [ ] Write a sample script `scripts/example-sharpe.csx` demonstrating the full API
- [ ] PR checklist: no `.Result`/`.Wait()` except in `DataProxy` (intentional, documented); MVVM compliance in ViewModel; constructor injection only

---

## Step 9: Open Questions & Risks

### Open Questions

| # | Question | Owner | Impact if Unresolved |
|---|---------|-------|---------------------|
| 1 | **EfficientFrontier implementation** — MathNet.Numerics has a quadratic programming solver but adds a new package dependency (~3MB). Alternatively, use a simple mean-variance gradient descent. Does the team want to ship EF in v1 or stub it permanently? | Product | Feature gap; stub returns equal-weight which is a safe default |
| 2 | **`[ScriptParam]` extraction strategy** — Roslyn syntax tree walking for `ScriptParamAttribute` on top-level declarations is complex in script context (no class body). Simpler alternative: parse a `// @param label:default:min:max` comment convention at the top of the file. Which approach? | Implementer | Affects how parameters are declared in scripts |
| 3 | **`PlotRequest` injection into `ReturnSeries.Plot()`** — `ReturnSeries` is a plain data object; injecting `PlotQueue` violates single-responsibility. Options: (a) `AsyncLocal<PlotQueue>` set by `ScriptRunner`; (b) `ReturnSeries` takes a `PlotQueue?` constructor arg; (c) extension method on `ReturnSeries` that requires caller to pass a `PlotQueue`. Recommend (a) — confirm. | Implementer | Core design decision; must resolve before Phase 2 |
| 4 | **AvalonEdit IntelliSense** — v1 ships with C# syntax highlighting only. Roslyn-powered completion (hover types, member lists) is a significant undertaking. Defer to v2? | Product | UX quality; not a blocker |
| 5 | **Script file watching** — should the script browser auto-refresh when `.csx` files are added/removed from disk? | Product | UX convenience |

### Risks

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| In-process script crashes the WPF host process | Low | High | Wrap `script.RunAsync` in `try/catch Exception`; document that scripts should not use `Environment.Exit` or `GC.Collect`. Add `EnableUnsafeScripts=false` to block file/process access |
| Roslyn compilation of large scripts is slow (>2s) | Medium | Medium | SHA256 cache eliminates recompilation on re-run; show "Compiling…" status during first compile |
| `BacktestMetricsEngine` is `internal` to `Backtesting` assembly | Low | High | `QuantScript` project references `Backtesting` project directly (not just SDK); already accounted for in csproj plan |
| ScottPlot.WPF `WpfPlot` imperative API complicates MVVM | Medium | Low | Render in `PlotViewModel` binding via a thin `PlotViewModelToScottPlot` attached behavior or `Loaded` event in code-behind; this is an accepted WPF chart pattern |
| `Channel<PlotRequest>` memory spike from runaway scripts | Low | Medium | `MaxPlotsPerRun` option (default 100); `PlotQueue.Enqueue` checks count and drops excess with a `LogWarning` |
| `DataProxy` synchronous `.GetAwaiter().GetResult()` risks deadlock if called from UI thread | Low | Medium | `ScriptRunner` always runs scripts on `Task.Run` (background thread pool); `DataProxy` is never called from UI thread. Document restriction clearly |
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe
