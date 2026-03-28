# QuantScript Page — Comprehensive Implementation Guide

**Owner:** Desktop / Research Engineering
**Audience:** Implementers, architects, and product contributors
**Last Updated:** 2026-03-28
**Status:** Design-ready — implementation pending
**Supersedes / extends:** `docs/plans/quant-script-environment-blueprint.md` (v1 blueprint, 2026-03-18)

---

## 1. Purpose and Scope

This document is the single authoritative implementation reference for the **QuantScript page** — Meridian's interactive C# scripting environment for quantitative research.

It covers:

- What the screen does and how users experience it
- The full interface contract: ViewModel, services, data flow, and XAML structure
- Ten actionable design enhancements that polish the v1 blueprint before code is written
- Alternative implementation strategies for the five most consequential architectural decisions
- A prioritised implementation checklist

Read the [v1 blueprint](quant-script-environment-blueprint.md) first for the foundational architecture; this guide assumes familiarity with it and focuses on refinement, alternatives, and polish.

---

## 2. Screen Overview — What the User Experiences

### 2.1 Core Purpose

QuantScript gives Meridian users a C# scripting surface that is directly connected to locally-collected market data, the backtesting engine, and the statistical analysis layer — all in a single window without leaving the desktop application.

The primary user journeys are:

| Journey | Steps | Result |
|---------|-------|--------|
| **Load and chart a symbol** | Select symbol range → `Data.Prices("SPY")` → `.DailyReturns().PlotCumulative()` | Cumulative return chart appears in Charts tab |
| **Run a technical indicator** | Load `PriceSeries` → `.Sma(20)` → `.Plot("SMA-20")` | Indicator line chart rendered |
| **Quick backtest from script** | Call `Backtest.WithSymbols(...).OnBar(...).RunAsync()` | Fill list, metrics table, and equity curve populated |
| **Parameter sweep** | Define `[ScriptParam]` variables → Adjust sidebar → Re-run | Results update with new parameter values |
| **Save and reuse** | Click Save → Script persists to `scripts/` directory → appears in browser | Script reloaded next session |

### 2.2 Layout

The page uses a **three-column split-pane** layout:

```
┌─────────────────┬──────────────────────────────────────┬────────────────────────────┐
│ Left (220 px)   │  Centre (flex)                       │  Right (380 px)            │
│                 │                                      │                            │
│  Script Browser │   AvalonEdit                         │  TabControl                │
│  ─────────────  │   (C# syntax highlighting,           │  ┌─Console────────────────┐│
│  scripts/       │    line numbers,                     │  │ timestamped output      ││
│  ├ sharpe.csx   │    folding, monospace font)           │  │ error lines in red      ││
│  ├ momentum.csx │                                      │  └────────────────────────┘│
│  └ macd.csx     │                                      │  ┌─Charts─────────────────┐│
│                 │                                      │  │ WpfPlot list            ││
│  ─────────────  │   ─────────────────────────────────  │  │ (ScottPlot 5.x)         ││
│  Parameters     │   [▶ Run] [■ Stop] [New] [Save] [⟳] │  └────────────────────────┘│
│  ─────────────  │   ProgressBar ─────────────────────  │  ┌─Metrics────────────────┐│
│  Lookback  [20] │   Status: Ready                      │  │ Label │ Value DataGrid  ││
│  Threshold [0.02│                                      │  └────────────────────────┘│
│  RiskFree  [0.04│                                      │  ┌─Trades─────────────────┐│
│                 │                                      │  │ Fill list DataGrid      ││
│                 │                                      │  └────────────────────────┘│
│                 │                                      │  ┌─Diagnostics────────────┐│
│                 │                                      │  │ timing/memory/ADR info  ││
└─────────────────┴──────────────────────────────────────┴──└────────────────────────┘┘
```

All three columns are resizable via `GridSplitter`. Column widths persist to user settings.

### 2.3 Toolbar Actions

| Button | Keyboard | Behaviour |
|--------|----------|-----------|
| ▶ Run | `F5` | Compile and execute the current script |
| ■ Stop | `Shift+F5` | Cancel running script via `CancellationToken` |
| New | `Ctrl+N` | Open blank script in editor |
| Save | `Ctrl+S` | Write current script to selected `.csx` path (or prompt for name) |
| ⟳ Refresh | — | Reload `.csx` file list from disk |

### 2.4 Tab Behaviour

- **Console tab** — visible immediately; receives `Print()`, warning, and error output during execution. Tab header shows unread count: `Console (12)`.
- **Charts tab** — activated automatically when the first plot is enqueued during a run. Shows a vertically-stacked list of `WpfPlot` controls, each with a title and an action menu.
- **Metrics tab** — populated when `PrintMetric()` or a backtest completes. Formatted as a key/value table.
- **Trades tab** — populated with `FillEvent` entries from a backtest run. Activated automatically if fills > 0.
- **Diagnostics tab** — always available; shows per-run wall-clock time, peak memory, Roslyn compile time, event count, and any ADR compliance warnings.

### 2.5 Script Browser

- Lists all `.csx` files found in the configured `QuantScript:ScriptsDirectory`.
- Clicking a script loads its content into the editor. Unsaved changes prompt a discard confirmation.
- Right-click menu: Rename, Duplicate, Delete, Open in Explorer.
- A "New Script" entry at the top of the list opens a blank editor.
- The list auto-refreshes when files change on disk (via `FileSystemWatcher`).

### 2.6 Parameter Sidebar

- Populated dynamically from `[ScriptParam]` attributes (or runtime `Param()` calls — see §4.2) found in the currently-loaded script.
- Each parameter renders as a labelled input: `NumericUpDown` for numeric types, `TextBox` for string, `CheckBox` for bool, `ComboBox` for enum.
- Parameters are sent to the script as a pre-populated globals dictionary before execution — the script does not need to re-declare them.
- Changed parameter values do not re-run the script automatically; the user presses Run.

---

## 3. Interface Contracts

### 3.1 QuantScriptViewModel

```csharp
// src/Meridian.Wpf/ViewModels/QuantScriptViewModel.cs
namespace Meridian.Wpf.ViewModels;

public sealed class QuantScriptViewModel : BindableBase, IDisposable, IPageActionBarProvider
{
    // ── DI dependencies ───────────────────────────────────────────────────────
    // IScriptRunner, IQuantScriptCompiler, PlotQueue,
    // IQuantScriptLayoutService, IOptions<QuantScriptOptions>, ILogger<>

    // ── Script source ─────────────────────────────────────────────────────────
    public string ScriptSource { get; set; }           // two-way bound to AvalonEdit
    public ObservableCollection<ScriptFileEntry> ScriptFiles { get; }
    public ScriptFileEntry? SelectedScript { get; set; }  // setter loads file into ScriptSource

    // ── Parameters ────────────────────────────────────────────────────────────
    public ObservableCollection<ParameterViewModel> Parameters { get; }

    // ── Results ───────────────────────────────────────────────────────────────
    public BoundedObservableCollection<ConsoleEntry> ConsoleOutput { get; }  // cap: 10 000
    public ObservableCollection<PlotViewModel> Charts { get; }
    public ObservableCollection<MetricEntry> Metrics { get; }
    public ObservableCollection<TradeEntry> Trades { get; }
    public ObservableCollection<DiagnosticEntry> Diagnostics { get; }

    // ── Tab headers (computed, wired to CollectionChanged) ────────────────────
    public string ConsoleTabHeader { get; }      // "Console (12)"
    public string ChartsTabHeader { get; }       // "Charts (3)"
    public string MetricsTabHeader { get; }      // "Metrics (7)"
    public string TradesTabHeader { get; }       // "Trades (42)"
    public string DiagnosticsTabHeader { get; }  // "Diagnostics"

    // ── Status ────────────────────────────────────────────────────────────────
    public bool IsRunning { get; private set; }
    public double ProgressFraction { get; private set; }
    public string StatusText { get; private set; }
    public string ElapsedText { get; private set; }
    public string MemoryText { get; private set; }
    public int ActiveResultsTab { get; set; }    // 0=Console, 1=Charts, 2=Metrics, 3=Trades, 4=Diagnostics
    public bool CanRun => !IsRunning;

    // ── Commands ──────────────────────────────────────────────────────────────
    public IAsyncRelayCommand RunScriptCommand { get; }  // AsyncRelayCommand; Stop() calls Cancel()
    public IRelayCommand StopCommand { get; }            // calls RunScriptCommand.Cancel()
    public IRelayCommand NewScriptCommand { get; }
    public IAsyncRelayCommand SaveScriptCommand { get; }
    public IRelayCommand RefreshScriptsCommand { get; }
    public IRelayCommand ClearConsoleCommand { get; }

    // ── IPageActionBarProvider ────────────────────────────────────────────────
    public string PageTitle => "QuantScript";
    public ObservableCollection<ActionEntry> Actions { get; }

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    internal void OnActivated();   // called from code-behind OnPageLoaded (UI thread)
    public void Dispose();         // called from code-behind OnPageUnloaded
}
```

**Key ViewModel rules:**

- `RunScriptCommand` is `AsyncRelayCommand` — it owns `CancellationToken` propagation. `StopCommand` calls `RunScriptCommand.Cancel()` — no manual `CancellationTokenSource` field.
- All `ObservableCollection` mutations go through `Application.Current.Dispatcher.InvokeAsync(action, DispatcherPriority.Background)` for progress-rate updates, and `DispatcherPriority.Normal` for terminal state (complete/error).
- `ConsoleOutput` uses `BoundedObservableCollection<ConsoleEntry>(10_000)` (already in `Meridian.Ui.Services.Collections`) — no manual `RemoveAt(0)` loops.
- All five tab header computed properties are wired to their collection's `CollectionChanged` event in the constructor — not scattered through the run path.

### 3.2 Supporting Model Types

```csharp
// src/Meridian.Wpf/Models/QuantScriptModels.cs
namespace Meridian.Wpf.Models;

public sealed record ScriptFileEntry(string Name, string FullPath);

public sealed record ConsoleEntry(
    DateTimeOffset Timestamp,
    string Text,
    ConsoleEntryKind Kind);

public enum ConsoleEntryKind { Output, Warning, Error, Separator }

public sealed record MetricEntry(string Label, string Value, string? Category = null);

public sealed record TradeEntry(
    DateTimeOffset FilledAt,
    string Symbol,
    decimal FilledQuantity,
    decimal FillPrice,
    decimal Commission,
    string Side);

public sealed record DiagnosticEntry(string Key, string Value);

public sealed record PlotViewModel(string Title, PlotRequest Request);

public sealed class ParameterViewModel : BindableBase
{
    public ParameterDescriptor Descriptor { get; }
    private string _rawValue;
    public string RawValue { get => _rawValue; set => SetProperty(ref _rawValue, value); }
    public bool IsValid { get; private set; }
    public string? ValidationMessage { get; private set; }
    public object? ParsedValue { get; private set; }
}
```

### 3.3 QuantScriptPage Code-Behind

```csharp
// src/Meridian.Wpf/Views/QuantScriptPage.xaml.cs
namespace Meridian.Wpf.Views;

public partial class QuantScriptPage : Page
{
    private readonly QuantScriptViewModel _vm;

    public QuantScriptPage(QuantScriptViewModel vm)   // DI-injected
    {
        _vm = vm;
        InitializeComponent();
        DataContext = _vm;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _vm.OnActivated();   // initialises timers on UI thread
        // Wire AvalonEdit.TextChanged → _vm.ScriptSource (AvalonEdit bypasses standard Binding)
        ScriptEditor.TextChanged += (_, _) => _vm.ScriptSource = ScriptEditor.Text;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        Charts.ItemContainerGenerator.ItemsChanged += OnChartsItemsChanged;
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _vm.SaveLayout(LeftColumn.Width.Value, RightColumn.Width.Value);
        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm.Dispose();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuantScriptViewModel.ScriptSource)
            && ScriptEditor.Text != _vm.ScriptSource)
            ScriptEditor.Text = _vm.ScriptSource;  // sync editor when loaded from file
    }

    // Wire each new WpfPlot control after its container is generated
    private void OnChartsItemsChanged(object sender, ItemsChangedEventArgs e) { ... }
}
```

### 3.4 IScriptRunner

```csharp
// src/Meridian.QuantScript/Compilation/IScriptRunner.cs
namespace Meridian.QuantScript.Compilation;

public interface IScriptRunner
{
    Task<ScriptRunResult> RunAsync(
        string source,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct = default);
}

public sealed record ScriptRunResult(
    bool Success,
    TimeSpan Elapsed,
    long PeakMemoryBytes,
    IReadOnlyList<ScriptDiagnostic> CompilationErrors,
    string? RuntimeError,
    IReadOnlyList<MetricEntry> Metrics,
    IReadOnlyList<TradeEntry> Trades);
```

### 3.5 IQuantScriptCompiler

```csharp
// src/Meridian.QuantScript/Compilation/IQuantScriptCompiler.cs
namespace Meridian.QuantScript.Compilation;

public interface IQuantScriptCompiler
{
    Task<ScriptCompilationResult> CompileAsync(string source, CancellationToken ct = default);
    IReadOnlyList<ParameterDescriptor> ExtractParameters(string source);
}

public sealed record ScriptCompilationResult(
    bool Success,
    TimeSpan CompilationTime,
    IReadOnlyList<ScriptDiagnostic> Diagnostics);

public sealed record ScriptDiagnostic(string Severity, string Message, int Line, int Column);

public sealed record ParameterDescriptor(
    string Name,
    string TypeName,
    string Label,
    object? DefaultValue,
    double Min,
    double Max,
    string? Description);
```

### 3.6 IQuantScriptLayoutService

```csharp
// src/Meridian.Wpf/Services/IQuantScriptLayoutService.cs (or Ui.Services)
namespace Meridian.Wpf.Services;

public interface IQuantScriptLayoutService
{
    (double LeftWidth, double RightWidth) LoadColumnWidths();
    void SaveColumnWidths(double leftWidth, double rightWidth);
    int LoadLastActiveTab();
    void SaveLastActiveTab(int tabIndex);
}
```

### 3.7 QuantScriptGlobals

```csharp
// src/Meridian.QuantScript/Compilation/QuantScriptGlobals.cs
namespace Meridian.QuantScript.Compilation;

/// <summary>
/// Roslyn script globals. All members appear as top-level identifiers in .csx scripts.
/// </summary>
public sealed class QuantScriptGlobals
{
    // Primary APIs (async-native — scripts use await)
    public IQuantDataContext Data { get; }
    public BacktestProxy Backtest { get; }

    // Portfolio
    public PortfolioResult EqualWeight(params PriceSeries[] series);
    public PortfolioResult CustomWeight(
        IReadOnlyDictionary<string, double> weights, params PriceSeries[] series);

    // Output helpers
    public void Print(object? value);
    public void PrintTable<T>(IEnumerable<T> rows);
    public void PrintMetric(string label, object value, string? category = null);

    // Statistics helpers (mirror ReturnSeries instance methods for top-level use)
    public double SharpeRatio(ReturnSeries r, double riskFreeRate = 0.04);
    public double SortinoRatio(ReturnSeries r, double riskFreeRate = 0.04);
    public double MaxDrawdown(ReturnSeries r);
    public double AnnualizedVolatility(ReturnSeries r);
    public double Beta(ReturnSeries r, ReturnSeries benchmark);
    public double Alpha(ReturnSeries r, ReturnSeries benchmark, double rfr = 0.04);
    public double Correlation(ReturnSeries a, ReturnSeries b);

    // Runtime parameter registration (alternative to [ScriptParam] attribute)
    public T Param<T>(string name, T defaultValue, double min = double.MinValue,
                      double max = double.MaxValue, string? description = null);

    // Cancellation
    public CancellationToken CancellationToken { get; }
}
```

---

## 4. Design Enhancements — 10 Targeted Improvements

The following refinements address gaps and fragile patterns in the v1 blueprint. Each is independent; they can be applied selectively.

### Enhancement 1 — Use `IAsyncRelayCommand` and Remove Manual CTS

**Problem:** The v1 blueprint declares `RunCommand` as `ICommand` and manages a `CancellationTokenSource` field manually. Every other async operation in the codebase (confirmed in `BacktestViewModel`, `SecurityMasterViewModel`) uses `CommunityToolkit.Mvvm`'s `AsyncRelayCommand`, which owns `CancellationToken` propagation internally and exposes `Cancel()`.

**Resolution:**

```csharp
// Constructor
RunScriptCommand = new AsyncRelayCommand(RunAsync, () => CanRun);
StopCommand      = new RelayCommand(() => RunScriptCommand.Cancel(), () => IsRunning);

// RunAsync signature — CT supplied by AsyncRelayCommand, not a field
private async Task RunAsync(CancellationToken ct)
{
    ClearResults();
    IsRunning = true;
    // ...
    var result = await Task.Run(() => _runner.RunAsync(_scriptSource, GetParameterDict(), ct), ct);
    // ...
}
```

No `CancellationTokenSource` field; no `_cts?.Cancel()` in Dispose.

---

### Enhancement 2 — `DispatcherPriority.Background` for High-Frequency Dispatches

**Problem:** The v1 blueprint calls `Dispatcher.InvokeAsync` without a priority. The `BacktestViewModel` (the closest analogue in the codebase) explicitly uses `DispatcherPriority.Background` for per-event progress updates so the render thread is not starved during rapid output.

**Resolution:**

```csharp
// Console drain (high-frequency, per-flush)
await Application.Current.Dispatcher.InvokeAsync(
    FlushConsoleCore, DispatcherPriority.Background);

// Per-plot arrival (during run)
await Application.Current.Dispatcher.InvokeAsync(
    () => Charts.Add(plotVm), DispatcherPriority.Background);

// Terminal state (once per run — full priority)
await Application.Current.Dispatcher.InvokeAsync(() =>
{
    IsRunning = false;
    StatusText = "Completed";
    ProgressFraction = 1.0;
}, DispatcherPriority.Normal);
```

---

### Enhancement 3 — Source Chart Colors from `ColorPalette`, Not Hex Literals

**Problem:** `PlotRenderBehavior` (the ScottPlot rendering code) hardcodes hex values like `"#42C6D6"` and `"#08111B"`. The project already has a `ColorPalette` registry in `Meridian.Ui.Services.Services.ColorPalette` which is used by `BrushRegistry` throughout the WPF layer. Hardcoded chart colors diverge silently when the theme changes.

**Resolution:**

```csharp
// src/Meridian.Wpf/Behaviors/PlotRenderBehavior.cs
using Palette = Meridian.Ui.Services.Services.ColorPalette;
using ScottColor = ScottPlot.Color;

private static ScottColor ToScottPlot(ColorPalette.ArgbColor c)
    => ScottColor.FromARGB(c.A, c.R, c.G, c.B);

// Usage:
scatter.Color = ToScottPlot(Palette.Accent);
plot.Style.Background(
    figure: ToScottPlot(Palette.BackgroundDark),
    data:   ToScottPlot(Palette.BackgroundMedium));

private static readonly ColorPalette.ArgbColor[] SeriesPalette =
[
    Palette.Accent, Palette.Success, Palette.Info, Palette.Warning, Palette.Error
];
```

---

### Enhancement 4 — `ConsoleOutput` Backed by `BoundedObservableCollection`

**Problem:** The v1 blueprint uses `ObservableCollection<ConsoleEntry>` and proposes trimming with `RemoveAt(0)` when the count exceeds a cap. `RemoveAt(0)` on `ObservableCollection` is O(n) because it shifts all backing elements. Under rapid console output this creates an O(n²) pattern.

**Resolution:** Use `BoundedObservableCollection<ConsoleEntry>` (already in `Meridian.Ui.Services.Collections`) which handles capacity enforcement internally via O(1) amortized operations:

```csharp
public BoundedObservableCollection<ConsoleEntry> ConsoleOutput { get; }
    = new BoundedObservableCollection<ConsoleEntry>(10_000);
```

The `FlushConsole` method simply calls `ConsoleOutput.Add(entry)` with no trim logic needed.

---

### Enhancement 5 — Tab Headers Wired to `CollectionChanged` in Constructor

**Problem:** The v1 blueprint scatters `RaisePropertyChanged(nameof(ConsoleTabHeader))` calls through multiple code paths. If a new path mutates a collection without calling it, the tab header silently shows a stale count. It also requires touching multiple places when adding a new collection.

**Resolution:** Wire once in the constructor — no scattered calls:

```csharp
public QuantScriptViewModel(...)
{
    ConsoleOutput.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(ConsoleTabHeader));
    Charts.CollectionChanged        += (_, _) => RaisePropertyChanged(nameof(ChartsTabHeader));
    Metrics.CollectionChanged       += (_, _) => RaisePropertyChanged(nameof(MetricsTabHeader));
    Trades.CollectionChanged        += (_, _) => RaisePropertyChanged(nameof(TradesTabHeader));
    Diagnostics.CollectionChanged   += (_, _) => RaisePropertyChanged(nameof(DiagnosticsTabHeader));
}

public string ConsoleTabHeader
    => ConsoleOutput.Count > 0 ? $"Console ({ConsoleOutput.Count})" : "Console";
// ... similar for others
```

---

### Enhancement 6 — Define `ClearResults()` Explicitly

**Problem:** `RunAsync` calls `ClearResults()` but the v1 blueprint omits the body. With five collections, two status strings, a counter, and a progress fraction to reset, omitting the body is a subtle source of state leakage between runs.

**Resolution:**

```csharp
private void ClearResults()
{
    ConsoleOutput.Clear();
    Charts.Clear();
    Metrics.Clear();
    Trades.Clear();
    Diagnostics.Clear();
    _chartCount = 0;
    ProgressFraction = 0;
    ActiveResultsTab = 0;   // return focus to Console tab
    ElapsedText = "--";
    MemoryText = "--";
}
```

---

### Enhancement 7 — `PlotQueue` in Constructor Injection, Not Implicit

**Problem:** The v1 blueprint's constructor lists `IScriptRunner`, `IQuantScriptCompiler`, `IQuantScriptLayoutService`, and `IOptions<QuantScriptOptions>` — but not `PlotQueue`. However, `DrainPlotQueueAsync` reads from `_plotQueue.ReadAllAsync(ct)`. The dependency exists but is never wired.

**Resolution:** Add `PlotQueue` explicitly to the constructor and DI registration:

```csharp
public QuantScriptViewModel(
    IScriptRunner runner,
    IQuantScriptCompiler compiler,
    PlotQueue plotQueue,                          // ← explicit
    IQuantScriptLayoutService layoutService,
    IOptions<QuantScriptOptions> options,
    ILogger<QuantScriptViewModel> logger)
```

Register `PlotQueue` as a singleton in WPF's DI setup alongside `IScriptRunner`.

---

### Enhancement 8 — Create `DispatcherTimer` Lazily in `OnActivated()`

**Problem:** Initialising `DispatcherTimer` in field initializers or the constructor may run on a non-UI thread when the DI container constructs the ViewModel. `DashboardViewModel.cs` in the codebase explicitly notes: *"DispatcherTimer must be created on the UI thread."*

**Resolution:** Create timers lazily inside `OnActivated()`, called from `OnPageLoaded` (guaranteed UI thread):

```csharp
private DispatcherTimer? _consoleDrainTimer;
private DispatcherTimer? _elapsedTimer;

internal void OnActivated()
{
    var (leftWidth, rightWidth) = _layoutService.LoadColumnWidths();
    // ... apply layout ...

    _consoleDrainTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
    _consoleDrainTimer.Tick += (_, _) => FlushConsole();

    _elapsedTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
    _elapsedTimer.Tick += (_, _) =>
    {
        if (_runStopwatch?.IsRunning == true)
            ElapsedText = $"{_runStopwatch.Elapsed.TotalSeconds:F1}s";
    };
}
```

---

### Enhancement 9 — Explicit `Dispose()` in `OnPageUnloaded`

**Problem:** The v1 blueprint declares `IDisposable` on the ViewModel but does not show where `Dispose()` is called. `BacktestPage.xaml.cs` calls `_viewModel.Dispose()` in `OnPageUnloaded` — the same pattern must be explicit here.

**Resolution:**

```csharp
// QuantScriptPage.xaml.cs
private void OnPageUnloaded(object sender, RoutedEventArgs e)
{
    _vm.SaveLayout(LeftColumn.Width.Value, RightColumn.Width.Value);
    _vm.PropertyChanged -= OnViewModelPropertyChanged;
    _vm.Dispose();
}

// QuantScriptViewModel.Dispose():
public void Dispose()
{
    _consoleDrainTimer?.Stop();
    _elapsedTimer?.Stop();
    _runStopwatch?.Stop();
    // RunScriptCommand owns its own CT; Cancel() is idempotent
    if (IsRunning) RunScriptCommand.Cancel();
    _plotQueue.Complete();
}
```

---

### Enhancement 10 — XAML `xmlns` for `ParameterTemplateSelector`

**Problem:** The v1 XAML uses `<local:ParameterTemplateSelector>` but omits the `xmlns:local` declaration. This is a compile-time XAML error.

**Resolution:** Add to the `<Page>` opening tag, matching the namespace where `ParameterTemplateSelector` is defined:

```xml
<!-- If in Meridian.Wpf.Views: -->
xmlns:local="clr-namespace:Meridian.Wpf.Views"

<!-- If in Meridian.Wpf.Behaviors: -->
xmlns:behaviors="clr-namespace:Meridian.Wpf.Behaviors"
```

Reference it consistently: `<behaviors:ParameterTemplateSelector ...>`.

---

## 5. Alternative Implementation Strategies

For five major design decisions, two or three concrete implementation paths are documented with tradeoffs and a recommendation.

### 5.1 Script Execution Model: Monolithic vs. Cell-Based

#### Option A — Monolithic (v1 blueprint)

One AvalonEdit editor. User writes a complete script, presses Run, all output appears at once.

- **Pros:** Simple ViewModel. Single `ScriptRunner.RunAsync` invocation. No inter-cell state management.
- **Cons:** No iterative exploration. Long scripts show nothing until complete. Feels like a compiler, not an interactive environment. Every quantitative scripting platform (Jupyter, QuantConnect Research, MATLAB Live Editor) has moved away from this model.
- **Effort:** Low (as designed)
- **Recommendation:** Acceptable for v1 if time is constrained.

#### Option B — Cell-Based Execution (Recommended for v2)

`ItemsControl` of `CellViewModel` objects, each containing an AvalonEdit instance. Roslyn `ScriptState<object>` is passed between cell executions via `CSharpScript.RunAsync(source, previousState: lastState)`.

```csharp
public sealed class CellViewModel : BindableBase
{
    public string Source { get; set; }
    public string OutputText { get; private set; }
    public ObservableCollection<PlotViewModel> OutputPlots { get; }
    public CellExecutionState State { get; private set; }  // Idle / Running / Done / Error / Stale
    public TimeSpan ElapsedTime { get; private set; }
}

public enum CellExecutionState { Idle, Running, Done, Error, Stale }
```

Cells after a failed cell are marked `Stale`. "Run All" iterates cells in order and re-executes each with the accumulated `ScriptState`.

- **Pros:** Dramatically better exploration UX. Results per-cell, not per-run. Errors don't require rerunning preceding cells.
- **Cons:** Complex ViewModel. Multiple AvalonEdit instances (mitigate: share syntax highlighting resources). State management overhead.
- **Effort:** +1–2 weeks over Option A

#### Option C — Notebook-Format Persistence

Extends Option B by persisting cells to a `.ipynb`-compatible or custom `.mqnb` format so that cell outputs are saved alongside code.

- **Pros:** Reproducible research artifacts; reloadable outputs without re-running.
- **Cons:** Significant serialisation complexity; ScottPlot charts must be serialised as SVG/PNG.
- **Effort:** +2–3 weeks over Option B
- **Recommendation:** Defer to v3.

---

### 5.2 Async Model: Synchronous DataProxy vs. Async-Native Globals

#### Option A — Synchronous DataProxy (v1 blueprint)

`DataProxy` wraps `IQuantDataContext` in `.GetAwaiter().GetResult()`. Scripts call `var spy = Data.Prices("SPY")` synchronously.

- **Pros:** Ergonomic — no `await` required in scripts. Familiar R/Python-style imperative workflow.
- **Cons:** `.GetAwaiter().GetResult()` from a background thread is safe but architecturally wrong. Multiple symbol loads are serial, not parallel. Deadlock risk if ever called from UI context. Cannot report per-load progress.
- **Recommendation:** Acceptable for v1. Document restriction that `DataProxy` must never be called from UI thread.

#### Option B — Async-Native Scripts (Recommended for v1.5+)

Expose `IQuantDataContext` directly as `Data` on globals. Roslyn scripts natively support top-level `await`. Multi-symbol loads can use `Task.WhenAll`.

```csharp
// Script:
var spy  = await Data.PricesAsync("SPY", from, to);
var aapl = await Data.PricesAsync("AAPL", from, to);

// Or parallel:
var (spy, aapl) = await Data.PricesAsync("SPY", "AAPL");
```

`IQuantDataContext.PricesAsync(params string[] symbols)` returns a `ValueTuple` or `IReadOnlyDictionary<string, PriceSeries>`.

- **Pros:** Cleaner architecture. True parallel loading. Proper `CancellationToken` propagation at every await point. No `.GetAwaiter().GetResult()` risk.
- **Cons:** Requires `await` keyword in every data call — slightly more verbose for simple scripts. Templates must use `await` by default to guide users.
- **Effort:** S (2–3 days) over Option A

#### Option C — Hybrid (DataProxy + async opt-in)

Keep `DataProxy` as the default surface for simple scripts. Add `Data.Async` property exposing `IQuantDataContext` for users who want parallel loads.

```csharp
var spy  = Data.Prices("SPY");         // synchronous convenience
var (s1, s2) = await Data.Async.PricesAsync("SPY", "AAPL");  // parallel
```

- **Pros:** Both audiences served. No breaking change from v1.
- **Cons:** Two API surfaces diverge; documentation burden.

---

### 5.3 Parameter Declaration: Attribute vs. Runtime Call

#### Option A — `[ScriptParam]` Compile-Time Attribute (v1 blueprint)

```csharp
[ScriptParam("Lookback", Default = 20, Min = 5, Max = 100)]
int lookback = 20;
```

Roslyn `SyntaxTree` walking extracts parameters via `LocalDeclarationStatementSyntax` node analysis.

- **Pros:** Declarative. Parameters visible without running the script.
- **Cons:** Roslyn AST walking in script context (no class body) is non-trivial. Fails on syntax errors before the user can fix them. Attribute application to top-level variable declarations has quirks in Roslyn script mode.

#### Option B — Runtime `Param()` Call (Recommended)

```csharp
var lookback  = Param("Lookback",  20,   min: 5,   max: 100);
var threshold = Param("Threshold", 0.02, min: 0.0, max: 0.5);
```

`Param<T>` is a method on `QuantScriptGlobals` that registers the descriptor at runtime and returns the value from the parameter dictionary (if previously set) or the default.

- **Pros:** Simple to implement — no AST walking. Works even with syntax errors elsewhere. Self-documenting. Can have conditional parameters (`if (useAdvanced) var window = Param(...)`).
- **Cons:** Parameters are not known until the script runs at least once (or a "dry run" of just the param declarations is executed).
- **Recommendation:** Use Option B for v1. Add Option A later for pre-run parameter detection if needed.

#### Option C — Comment Convention

```csharp
// @param Lookback:int:20:5:100
var lookback = 20;
```

Regex parsing of comment annotations; zero Roslyn dependency for extraction.

- **Pros:** Extremely simple to parse.
- **Cons:** Not type-safe. Easy to get out of sync with actual variable. Non-standard.
- **Recommendation:** Avoid.

---

### 5.4 Plot Rendering: Code-Behind vs. Attached Behavior

#### Option A — `Loaded` Event in Code-Behind

`QuantScriptPage.xaml.cs` subscribes to `Charts.ItemContainerGenerator.ItemsChanged`. For each new `PlotViewModel`, the corresponding `WpfPlot` control is located and configured imperatively:

```csharp
private void ConfigurePlot(WpfPlot wpfPlot, PlotRequest request)
{
    var plot = wpfPlot.Plot;
    plot.Style.Background(ToScottPlot(Palette.BackgroundDark),
                          ToScottPlot(Palette.BackgroundMedium));
    switch (request.Type)
    {
        case PlotType.Line:
            var scatter = plot.Add.Scatter(
                request.Series!.Select(p => (double)p.Date.DayNumber).ToArray(),
                request.Series!.Select(p => p.Value).ToArray());
            scatter.Color = ToScottPlot(Palette.Accent);
            break;
        // ... other types
    }
    wpfPlot.Refresh();
}
```

- **Pros:** Simple to implement. ScottPlot is inherently imperative; this is its natural usage pattern.
- **Cons:** Code-behind grows as plot types multiply. Harder to unit test.

#### Option B — `PlotRenderBehavior` Attached Behavior (Recommended)

An `Attached Property` on `WpfPlot` accepts a `PlotRequest` and renders it. The code-behind is reduced to a single XAML `Style`:

```xml
<DataTemplate DataType="{x:Type models:PlotViewModel}">
  <StackPanel>
    <TextBlock Text="{Binding Title}" FontWeight="SemiBold"/>
    <scottplot:WpfPlot Height="220"
      behaviors:PlotRenderBehavior.Request="{Binding Request}"/>
  </StackPanel>
</DataTemplate>
```

`PlotRenderBehavior.OnRequestChanged` handles all ScottPlot configuration.

- **Pros:** Clean MVVM separation. One place for all plot rendering logic. Easily testable by constructing a `WpfPlot` in a test and verifying series counts.
- **Cons:** Attached behavior pattern requires careful `DependencyProperty` registration.
- **Recommendation:** Option B for production quality. Option A acceptable for v1 iteration.

---

### 5.5 Script Console Drain: DispatcherTimer vs. Dedicated Task

#### Option A — `DispatcherTimer` Poll (v1 blueprint)

A `DispatcherTimer` at 50ms interval polls a `Channel<ConsoleEntry>` for new output and flushes it to `ConsoleOutput`.

- **Pros:** Always runs on UI thread — no marshaling needed. Simple to reason about.
- **Cons:** 50ms polling regardless of activity. If the script produces no output, 20 timer ticks per second fire uselessly.

#### Option B — Dedicated Drain Task (Recommended)

A `Task` started at run-begin drains the `Channel<ConsoleEntry>` via `await foreach` and marshals batches to the UI:

```csharp
private async Task DrainConsoleAsync(CancellationToken ct)
{
    await foreach (var entry in _consoleChannel.Reader.ReadAllAsync(ct))
    {
        await Application.Current.Dispatcher.InvokeAsync(
            () => ConsoleOutput.Add(entry), DispatcherPriority.Background);
    }
}
```

The channel's `Writer.Complete()` terminates the loop cleanly.

- **Pros:** No polling; CPU-efficient. Backpressure-aware. Cleaner lifecycle — the drain task ends when the run ends.
- **Cons:** Requires marshaling call per entry (or per batch if entries are collected before dispatching). Slightly more complex startup/teardown.
- **Recommendation:** Option B for production; Option A acceptable for v1.

---

## 6. Data Flows

### 6.1 Analytical Script: Data → Stats → Plot

```
User presses Run
  └─ QuantScriptViewModel.RunAsync(ct)
       ClearResults()
       IsRunning = true
       IScriptRunner.RunAsync(source, params, ct) on Task.Run
         └─ RoslynScriptCompiler.CompileAsync(source) → Script<object> (cached by SHA256)
         └─ QuantScriptGlobals constructed
         └─ script.RunAsync(globals, ct)
              Script body executes on thread pool:
                await Data.PricesAsync("SPY", from, to)
                  └─ QuantDataContext.PricesAsync
                       └─ HistoricalDataQueryService.QueryBarsAsync
                            └─ JsonlMarketDataStore → reads JSONL files → PriceSeries
                spy.DailyReturns()
                  └─ PriceSeriesExtensions.DailyReturns → ReturnSeries
                ret.SharpeRatio()
                  └─ StatisticsEngine.Sharpe(points, rfr) → double
                PrintMetric("Sharpe", 2.31)
                  └─ enqueues MetricEntry on internal channel
                ret.PlotCumulative("SPY 2023")
                  └─ PlotQueue.Enqueue(new PlotRequest(..., PlotType.CumulativeReturn))
         ScriptRunResult returned (metrics collected, trades empty)
       Back on UI thread (Dispatcher):
         ConsoleOutput ← new entries (Background priority)
         Charts ← PlotViewModel per PlotRequest (Background priority)
         Metrics ← MetricEntry list (Normal priority, once)
         IsRunning = false, ProgressFraction = 1.0, StatusText = "Completed in 1.4s"
```

### 6.2 Backtest Script: OnBar → Engine → Fills

```
Script body:
  await Backtest
    .WithSymbols("SPY")
    .From(new DateOnly(2022, 1, 1))
    .To(new DateOnly(2023, 12, 31))
    .OnBar((bar, ctx) => { ... })
    .RunAsync(ct)
  └─ BacktestProxy builds BacktestRequest and LambdaBacktestStrategy
  └─ BacktestEngine.RunAsync(request, strategy, progress, ct)
       └─ UniverseDiscovery.DiscoverAsync
       └─ MultiSymbolMergeEnumerator.MergeAsync → chronological HistoricalBar stream
       └─ For each bar: strategy.OnBar → LambdaBacktestStrategy → user lambda
       └─ BacktestMetricsEngine.Compute → BacktestMetrics
  BacktestResult returned to script
  Script calls PrintMetric() and Plot() with result
  ScriptRunResult includes Fills → mapped to TradeEntry list
Back on UI thread:
  Trades ← fill entries; Trades tab activated if fills > 0
  Metrics ← performance stats
  Charts ← equity curve (if script plots it)
```

### 6.3 Compilation Error Path

```
User has syntax error → presses Run
  └─ RoslynScriptCompiler.CompileAsync → ScriptCompilationResult(Success: false)
  └─ ScriptRunner returns ScriptRunResult(Success: false, CompilationErrors: [...])
  └─ QuantScriptViewModel maps each diagnostic to ConsoleEntry(Kind: Error)
  └─ ConsoleOutput gets red error lines with line/column
  └─ StatusText = "Compilation failed (2 errors)"
  └─ IsRunning = false immediately (no run started)
```

---

## 7. XAML Reference

### 7.1 Page Skeleton

```xml
<Page x:Class="Meridian.Wpf.Views.QuantScriptPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:avalonEdit="http://icsharpcode.net/sharpdevelop/avalonedit"
      xmlns:scottplot="clr-namespace:ScottPlot.WPF;assembly=ScottPlot.WPF"
      xmlns:local="clr-namespace:Meridian.Wpf.Views"
      xmlns:models="clr-namespace:Meridian.Wpf.Models"
      xmlns:behaviors="clr-namespace:Meridian.Wpf.Behaviors"
      xmlns:conv="clr-namespace:Meridian.Wpf.Converters"
      Loaded="OnPageLoaded"
      Unloaded="OnPageUnloaded">

  <Page.Resources>
    <conv:ConsoleEntryKindToBrushConverter x:Key="ConsoleColor"/>
    <BooleanToVisibilityConverter x:Key="BoolVis"/>
  </Page.Resources>

  <Grid>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width="220" MinWidth="140" x:Name="LeftColumn"/>
      <ColumnDefinition Width="4"/>
      <ColumnDefinition Width="*" MinWidth="300"/>
      <ColumnDefinition Width="4"/>
      <ColumnDefinition Width="380" MinWidth="240" x:Name="RightColumn"/>
    </Grid.ColumnDefinitions>

    <!-- Left: browser + params -->
    <!-- Centre: editor + toolbar -->
    <!-- Right: results TabControl -->

  </Grid>
</Page>
```

### 7.2 Left Column — Script Browser and Parameters

```xml
<DockPanel Grid.Column="0" Margin="4">

  <TextBlock DockPanel.Dock="Top" Text="Scripts" Style="{StaticResource SectionHeader}"/>

  <ListBox DockPanel.Dock="Top" Height="180"
           ItemsSource="{Binding ScriptFiles}"
           SelectedItem="{Binding SelectedScript}">
    <ListBox.ItemTemplate>
      <DataTemplate DataType="{x:Type models:ScriptFileEntry}">
        <TextBlock Text="{Binding Name}" ToolTip="{Binding FullPath}"/>
      </DataTemplate>
    </ListBox.ItemTemplate>
  </ListBox>

  <Separator DockPanel.Dock="Top"/>

  <TextBlock DockPanel.Dock="Top" Text="Parameters" Style="{StaticResource SectionHeader}"
             Visibility="{Binding Parameters.Count, Converter={StaticResource CountVis}}"/>

  <ScrollViewer>
    <ItemsControl ItemsSource="{Binding Parameters}">
      <ItemsControl.ItemTemplate>
        <DataTemplate DataType="{x:Type models:ParameterViewModel}">
          <!-- ParameterTemplateSelector renders NumericUpDown / TextBox / CheckBox by type -->
          <ContentPresenter ContentTemplateSelector="{behaviors:ParameterTemplateSelector}"
                            Content="{Binding}"/>
        </DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
  </ScrollViewer>

</DockPanel>
```

### 7.3 Centre Column — Editor and Toolbar

```xml
<DockPanel Grid.Column="2">

  <!-- Bottom toolbar -->
  <ToolBar DockPanel.Dock="Bottom">
    <Button Content="▶ Run" Command="{Binding RunScriptCommand}"
            IsEnabled="{Binding CanRun}"/>
    <Button Content="■ Stop" Command="{Binding StopCommand}"
            IsEnabled="{Binding IsRunning}"/>
    <Separator/>
    <Button Content="New"  Command="{Binding NewScriptCommand}"/>
    <Button Content="Save" Command="{Binding SaveScriptCommand}"/>
    <Button Content="⟳"   Command="{Binding RefreshScriptsCommand}" ToolTip="Refresh script list"/>
    <Separator/>
    <ProgressBar Value="{Binding ProgressFraction}" Minimum="0" Maximum="1"
                 Width="120" Height="8"
                 Visibility="{Binding IsRunning, Converter={StaticResource BoolVis}}"/>
    <TextBlock Text="{Binding StatusText}" VerticalAlignment="Center" Margin="4,0"
               Foreground="{StaticResource ForegroundSecondaryBrush}"/>
    <TextBlock Text="{Binding ElapsedText}" VerticalAlignment="Center" Margin="4,0"/>
  </ToolBar>

  <!-- AvalonEdit -->
  <avalonEdit:TextEditor x:Name="ScriptEditor"
                         SyntaxHighlighting="C#"
                         ShowLineNumbers="True"
                         WordWrap="False"
                         FontFamily="Cascadia Code, Consolas, Courier New"
                         FontSize="13"
                         Background="{StaticResource BackgroundDarkBrush}"
                         Foreground="{StaticResource ForegroundPrimaryBrush}"/>

</DockPanel>
```

### 7.4 Right Column — Results TabControl

```xml
<TabControl Grid.Column="4"
            SelectedIndex="{Binding ActiveResultsTab}">

  <TabItem Header="{Binding ConsoleTabHeader}">
    <ScrollViewer x:Name="ConsoleScroll" VerticalScrollBarVisibility="Auto">
      <ItemsControl ItemsSource="{Binding ConsoleOutput}">
        <ItemsControl.ItemTemplate>
          <DataTemplate DataType="{x:Type models:ConsoleEntry}">
            <TextBlock Text="{Binding Text}"
                       Foreground="{Binding Kind, Converter={StaticResource ConsoleColor}}"
                       FontFamily="Cascadia Code, Consolas" FontSize="11"
                       TextWrapping="Wrap"/>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
    </ScrollViewer>
  </TabItem>

  <TabItem Header="{Binding ChartsTabHeader}">
    <ScrollViewer VerticalScrollBarVisibility="Auto">
      <ItemsControl ItemsSource="{Binding Charts}">
        <ItemsControl.ItemTemplate>
          <DataTemplate DataType="{x:Type models:PlotViewModel}">
            <StackPanel Margin="0,4,0,8">
              <TextBlock Text="{Binding Title}" FontWeight="SemiBold" Margin="4,0,0,2"/>
              <scottplot:WpfPlot Height="220"
                behaviors:PlotRenderBehavior.Request="{Binding Request}"/>
            </StackPanel>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
    </ScrollViewer>
  </TabItem>

  <TabItem Header="{Binding MetricsTabHeader}">
    <DataGrid ItemsSource="{Binding Metrics}" AutoGenerateColumns="False"
              IsReadOnly="True" HeadersVisibility="Column" GridLinesVisibility="None">
      <DataGrid.Columns>
        <DataGridTextColumn Header="Metric"   Binding="{Binding Label}"    Width="160"/>
        <DataGridTextColumn Header="Value"    Binding="{Binding Value}"    Width="*"/>
        <DataGridTextColumn Header="Category" Binding="{Binding Category}" Width="80"/>
      </DataGrid.Columns>
    </DataGrid>
  </TabItem>

  <TabItem Header="{Binding TradesTabHeader}">
    <DataGrid ItemsSource="{Binding Trades}" AutoGenerateColumns="False"
              IsReadOnly="True" HeadersVisibility="Column" GridLinesVisibility="None">
      <DataGrid.Columns>
        <DataGridTextColumn Header="Time"   Binding="{Binding FilledAt, StringFormat='HH:mm:ss'}" Width="70"/>
        <DataGridTextColumn Header="Symbol" Binding="{Binding Symbol}"                            Width="65"/>
        <DataGridTextColumn Header="Side"   Binding="{Binding Side}"                              Width="40"/>
        <DataGridTextColumn Header="Qty"    Binding="{Binding FilledQuantity}"                    Width="55"/>
        <DataGridTextColumn Header="Price"  Binding="{Binding FillPrice, StringFormat='N2'}"      Width="70"/>
        <DataGridTextColumn Header="Comm."  Binding="{Binding Commission, StringFormat='N2'}"     Width="60"/>
      </DataGrid.Columns>
    </DataGrid>
  </TabItem>

  <TabItem Header="{Binding DiagnosticsTabHeader}">
    <DataGrid ItemsSource="{Binding Diagnostics}" AutoGenerateColumns="False"
              IsReadOnly="True" HeadersVisibility="Column" GridLinesVisibility="None">
      <DataGrid.Columns>
        <DataGridTextColumn Header="Key"   Binding="{Binding Key}"   Width="160"/>
        <DataGridTextColumn Header="Value" Binding="{Binding Value}" Width="*"/>
      </DataGrid.Columns>
    </DataGrid>
  </TabItem>

</TabControl>
```

---

## 8. New Project Structure

```
src/
└── Meridian.QuantScript/
    ├── Meridian.QuantScript.csproj         net9.0-windows
    ├── QuantScriptOptions.cs
    ├── Api/
    │   ├── PriceBar.cs
    │   ├── PriceSeries.cs
    │   ├── PriceSeriesExtensions.cs
    │   ├── ReturnSeries.cs
    │   ├── ReturnPoint.cs
    │   ├── ReturnKind.cs
    │   ├── StatisticsEngine.cs             internal static
    │   ├── TechnicalSeriesExtensions.cs
    │   ├── ScriptTrade.cs
    │   ├── ScriptOrderBook.cs
    │   ├── IQuantDataContext.cs
    │   ├── QuantDataContext.cs
    │   ├── DataProxy.cs                    sync facade over IQuantDataContext
    │   ├── ScriptParamAttribute.cs
    │   ├── BacktestProxy.cs
    │   ├── LambdaBacktestStrategy.cs       internal sealed
    │   ├── PortfolioBuilder.cs
    │   ├── PortfolioResult.cs
    │   └── EfficientFrontierConstraints.cs
    ├── Compilation/
    │   ├── IQuantScriptCompiler.cs
    │   ├── RoslynScriptCompiler.cs
    │   ├── IScriptRunner.cs
    │   ├── ScriptRunner.cs
    │   ├── QuantScriptGlobals.cs
    │   └── ScriptRunResult.cs
    └── Plotting/
        ├── PlotQueue.cs
        ├── PlotRequest.cs
        └── PlotType.cs

src/Meridian.Wpf/
    ├── Models/
    │   └── QuantScriptModels.cs            ScriptFileEntry, ConsoleEntry, …
    ├── ViewModels/
    │   └── QuantScriptViewModel.cs
    ├── Views/
    │   ├── QuantScriptPage.xaml
    │   └── QuantScriptPage.xaml.cs
    ├── Behaviors/
    │   ├── PlotRenderBehavior.cs           attached property for WpfPlot
    │   └── ParameterTemplateSelector.cs   DataTemplateSelector for param types
    ├── Converters/
    │   └── ConsoleEntryKindToBrushConverter.cs
    └── Services/
        └── QuantScriptLayoutService.cs

tests/
└── Meridian.QuantScript.Tests/
    ├── PriceSeriesTests.cs
    ├── StatisticsEngineTests.cs
    ├── RoslynScriptCompilerTests.cs
    ├── ScriptRunnerTests.cs
    ├── PlotQueueTests.cs
    ├── QuantScriptViewModelTests.cs
    └── Helpers/
        ├── FakeQuantDataContext.cs
        ├── FakeScriptRunner.cs
        └── TestPriceSeriesBuilder.cs
```

---

## 9. Implementation Checklist

### Phase 0 — Housekeeping

- [ ] Add `Microsoft.CodeAnalysis.CSharp.Scripting` version `5.0.0` to `Directory.Packages.props`
- [ ] Add `AvalonEdit` (ICSharpCode.AvalonEdit) version `6.3.0.90` to `Directory.Packages.props`
- [ ] Add `ScottPlot.WPF` version `5.0.55` to `Directory.Packages.props`
- [ ] Create `src/Meridian.QuantScript/Meridian.QuantScript.csproj`; add to `Meridian.sln`
- [ ] Create `tests/Meridian.QuantScript.Tests/`; add to `Meridian.sln`
- [ ] Add `QuantScript` section to `config/appsettings.json` and `config/appsettings.sample.json`

### Phase 1 — Core API Library

- [ ] `PriceBar`, `PriceSeries`, `PriceSeriesExtensions`
- [ ] `ReturnSeries`, `ReturnPoint`, `ReturnKind`
- [ ] `StatisticsEngine` (internal static) — all 14 math methods
- [ ] `TechnicalSeriesExtensions` — Sma, Ema, Rsi, Macd, BollingerBands via Skender
- [ ] `ScriptTrade`, `ScriptOrderBook`, `IQuantDataContext`, `QuantDataContext`
- [ ] `DataProxy` (sync façade, documented thread restriction)
- [ ] `ScriptParamAttribute`
- [ ] `LambdaBacktestStrategy` (internal)
- [ ] `BacktestProxy` (fluent builder)
- [ ] `PortfolioBuilder`, `PortfolioResult` (EfficientFrontier stub + `// TODO`)
- [ ] `PlotRequest`, `PlotType`, `PlotQueue`
- [ ] `QuantScriptOptions`

### Phase 2 — Compilation Pipeline

- [ ] `IQuantScriptCompiler`, `RoslynScriptCompiler` (compile + SHA256 cache + parameter extraction)
- [ ] `IScriptRunner`, `ScriptRunner` (compile + run + capture output + channel lifecycle)
- [ ] `QuantScriptGlobals` (all members, `Param<T>` runtime registration, `AsyncLocal<PlotQueue>`)
- [ ] DI registrations in WPF app startup: `IQuantDataContext`, `IQuantScriptCompiler`, `IScriptRunner`, `PlotQueue` (singleton)

### Phase 3 — WPF Integration

- [ ] `Models/QuantScriptModels.cs` — all record/enum types
- [ ] `QuantScriptViewModel` — apply all 10 enhancements from §4
- [ ] `QuantScriptLayoutService`
- [ ] `PlotRenderBehavior` attached property
- [ ] `ParameterTemplateSelector` (`DataTemplateSelector` for numeric/text/bool/enum)
- [ ] `ConsoleEntryKindToBrushConverter`
- [ ] `QuantScriptPage.xaml` — full XAML per §7; add correct `xmlns` declarations
- [ ] `QuantScriptPage.xaml.cs` — thin code-behind: DI constructor, `OnActivated`, `Dispose`, AvalonEdit text wiring
- [ ] `Views/Pages.cs` — add `QuantScript` entry
- [ ] `Services/NavigationService.cs` — register `QuantScript` → `QuantScriptPage`
- [ ] `MainPage.xaml` or navigation sidebar — add QuantScript navigation item
- [ ] Add `AvalonEdit` and `ScottPlot.WPF` `<PackageReference>` to `Meridian.Wpf.csproj` (no version)

### Phase 4 — Tests

- [ ] `PriceSeriesTests.cs` (6 tests)
- [ ] `StatisticsEngineTests.cs` (17 tests; expose via `InternalsVisibleTo`)
- [ ] `RoslynScriptCompilerTests.cs` (7 tests; real in-process Roslyn)
- [ ] `ScriptRunnerTests.cs` (6 tests)
- [ ] `PlotQueueTests.cs` (4 tests)
- [ ] `QuantScriptViewModelTests.cs` (6 tests; `FakeScriptRunner`)
- [ ] All 46 tests green

### Phase 5 — Wrap-Up

- [ ] ADR compliance: add `[ImplementsAdr("ADR-004", ...)]` to `IQuantDataContext`, `[ImplementsAdr("ADR-013", "PlotQueue uses unbounded channel — justified, documented")]` to `PlotQueue`
- [ ] XML doc comments on all public interfaces and classes
- [ ] Sample script `scripts/example-sharpe.csx` demonstrating full API
- [ ] Update `docs/generated/provider-registry.md` and `docs/status/FEATURE_INVENTORY.md` to include QuantScript
- [ ] PR checklist: no `.Result`/`.Wait()` except `DataProxy` (documented); MVVM compliance; constructor injection only; `sealed` on all non-abstract classes

---

## 10. Open Questions

| # | Question | Impact if Deferred |
|---|----------|--------------------|
| 1 | **Cell-based execution (Enhancement A, §5.1)** — ship monolithic v1 or invest in cells from the start? | Exploration UX; not a blocker for data analysis workflows |
| 2 | **`[ScriptParam]` attribute or runtime `Param()` call (§5.3)** — the recommended Option B (runtime) means parameters are unknown until first run. Is a "parse parameters without running" dry-run mode needed? | Parameter sidebar empty on first load |
| 3 | **`EfficientFrontier` implementation** — MathNet.Numerics QP solver adds ~3 MB; stub returns equal-weight. Acceptable? | Missing portfolio optimisation feature |
| 4 | **`PlotRequest` injection into `ReturnSeries.Plot()`** — which mechanism: `AsyncLocal<PlotQueue>`, `PlotQueue` constructor arg on `ReturnSeries`, or extension method with explicit `PlotQueue` parameter? | Core design decision; must resolve before Phase 1 |
| 5 | **Result persistence** — should `ScriptRunResult` be saved to storage (like `BacktestResult`)? Needed for run comparison. | Research reproducibility; deferred to v2 |
| 6 | **AvalonEdit IntelliSense** — defer Roslyn-powered completion to v2? | UX quality; not a correctness blocker |
| 7 | **File watching on `ScriptsDirectory`** — `FileSystemWatcher` auto-refresh vs. manual `⟳` button? | Convenience; manual refresh is safe default |

---

## 11. Risks

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| In-process script crashes the WPF host | Low | High | Wrap `script.RunAsync` in `try/catch`; document `Environment.Exit` restriction; `EnableUnsafeScripts = false` blocks file/process access |
| Roslyn first-compile latency >2 s on large scripts | Medium | Medium | SHA256 compilation cache eliminates repeat-run overhead; show "Compiling…" status |
| `BacktestMetricsEngine` is `internal` to `Meridian.Backtesting` | Low | High | `Meridian.QuantScript` references `Meridian.Backtesting` project directly (not only SDK) |
| ScottPlot.WPF imperative API complicates pure MVVM | Medium | Low | `PlotRenderBehavior` attached property encapsulates imperative calls; accepted WPF charting pattern |
| `BoundedObservableCollection` fires `Reset` on `PrependRange` bulk operations | Low | Low | Use `Add` (not `Prepend`) for console output; `Reset` only fires during `PrependRange`, not single `Add` |
| `DataProxy` synchronous `.GetAwaiter().GetResult()` risk if ever called from UI thread | Low | Medium | `ScriptRunner` always executes scripts via `Task.Run`; document restriction; suppress CA2012 warning with a comment explaining intent |

---

*Reference documents:*
*— [quant-script-environment-blueprint.md](quant-script-environment-blueprint.md) — v1 architectural blueprint*
*— [quantscript-l3-multiinstance-round2-roadmap.md](quantscript-l3-multiinstance-round2-roadmap.md) — round 2 feature ideas*
*— [quant-script-blueprint-brainstorm.md](../evaluations/quant-script-blueprint-brainstorm.md) — design evaluation and improvement ideas*
