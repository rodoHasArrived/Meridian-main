using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services.Collections;
using Meridian.QuantScript.Compilation;
using Meridian.QuantScript.Plotting;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Microsoft.Extensions.Options;
using Meridian.QuantScript;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the QuantScript interactive C# scripting environment.
/// Drives a three-column layout: script browser / editor / results tabs.
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Meridian.Backtesting.Sdk;
using Meridian.QuantScript;
using Meridian.QuantScript.Compilation;
using Meridian.QuantScript.Plotting;

namespace Meridian.Wpf.ViewModels;

// ── Supporting model types ───────────────────────────────────────────────────

public sealed record ScriptFileEntry(string Name, string FullPath);

public enum ConsoleEntryKind { Output, Warning, Error }
public sealed record ConsoleEntry(DateTimeOffset Timestamp, string Text, ConsoleEntryKind Kind);
public sealed record MetricEntry(string Label, string Value);
public sealed class ParameterViewModel : BindableBase
{
    private string _raw = string.Empty;
    public required ParameterDescriptor Descriptor { get; init; }
    public string RawValue { get => _raw; set => SetProperty(ref _raw, value); }
}
public sealed record PlotViewModel(string Title, PlotRequest Request);

// ── ViewModel ────────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel for QuantScriptPage. Drives the three-panel script execution UI:
/// script browser, editor, and results tabs (Console / Charts / Metrics / Trades).
/// </summary>
public sealed class QuantScriptViewModel : BindableBase, IDisposable
{
    private readonly IScriptRunner _runner;
    private readonly IQuantScriptCompiler _compiler;
    private readonly PlotQueue _plotQueue;
    private readonly IQuantScriptLayoutService _layoutService;
    private readonly QuantScriptOptions _options;
    private readonly ILogger<QuantScriptViewModel> _logger;

    private DispatcherTimer? _consoleDrainTimer;
    private DispatcherTimer? _elapsedTimer;
    private System.Diagnostics.Stopwatch? _runStopwatch;
    private FileSystemWatcher? _fileWatcher;
    private bool _disposed;

    // ── Script source ─────────────────────────────────────────────────────────
    private readonly QuantScriptOptions _options;
    private readonly ILogger<QuantScriptViewModel> _logger;
    private CancellationTokenSource? _runCts;

    // ── Script editor ───────────────────────────────────────────────────────

    private string _scriptSource = string.Empty;
    public string ScriptSource
    {
        get => _scriptSource;
        set => SetProperty(ref _scriptSource, value);
    }

    public ObservableCollection<ScriptFileEntry> ScriptFiles { get; } = new();
        set { if (SetProperty(ref _scriptSource, value)) UpdateParameters(value); }
    }

    // ── Script browser ──────────────────────────────────────────────────────

    public ObservableCollection<ScriptFileEntry> ScriptFiles { get; } = [];

    private ScriptFileEntry? _selectedScript;
    public ScriptFileEntry? SelectedScript
    {
        get => _selectedScript;
        set
        {
            if (SetProperty(ref _selectedScript, value) && value != null)
                LoadScriptFile(value.FullPath);
        }
    }

    // ── Parameters ────────────────────────────────────────────────────────────

    public ObservableCollection<ParameterViewModel> Parameters { get; } = new();

    // ── Results ───────────────────────────────────────────────────────────────

    public BoundedObservableCollection<ConsoleEntry> ConsoleOutput { get; } =
        new BoundedObservableCollection<ConsoleEntry>(10_000);

    public ObservableCollection<PlotViewModel> Charts { get; } = new();
    public ObservableCollection<MetricEntry> Metrics { get; } = new();
    public ObservableCollection<TradeEntry> Trades { get; } = new();
    public ObservableCollection<DiagnosticEntry> Diagnostics { get; } = new();

    // ── Tab headers ───────────────────────────────────────────────────────────

    public string ConsoleTabHeader
        => ConsoleOutput.Count > 0 ? $"Console ({ConsoleOutput.Count})" : "Console";

    public string ChartsTabHeader
        => Charts.Count > 0 ? $"Charts ({Charts.Count})" : "Charts";

    public string MetricsTabHeader
        => Metrics.Count > 0 ? $"Metrics ({Metrics.Count})" : "Metrics";

    public string TradesTabHeader
        => Trades.Count > 0 ? $"Trades ({Trades.Count})" : "Trades";

    public string DiagnosticsTabHeader
        => Diagnostics.Count > 0 ? $"Diagnostics ({Diagnostics.Count})" : "Diagnostics";

    // ── Status ────────────────────────────────────────────────────────────────
        set { if (SetProperty(ref _selectedScript, value)) LoadScript(value); }
    }

    // ── Parameters ──────────────────────────────────────────────────────────

    public ObservableCollection<ParameterViewModel> Parameters { get; } = [];

    // ── Results ─────────────────────────────────────────────────────────────

    public ObservableCollection<ConsoleEntry> ConsoleOutput { get; } = [];
    public ObservableCollection<PlotViewModel> Charts { get; } = [];
    public ObservableCollection<MetricEntry> Metrics { get; } = [];
    public ObservableCollection<FillEvent> Trades { get; } = [];

    // ── Status ───────────────────────────────────────────────────────────────

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                RaisePropertyChanged(nameof(CanRun));
                RunCommand.NotifyCanExecuteChanged();
                StopCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private double _progressFraction;
    public double ProgressFraction { get => _progressFraction; private set => SetProperty(ref _progressFraction, value); }

    private string _statusText = "Ready";
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    private string _elapsedText = "--";
    public string ElapsedText { get => _elapsedText; private set => SetProperty(ref _elapsedText, value); }

    private string _memoryText = "--";
    public string MemoryText { get => _memoryText; private set => SetProperty(ref _memoryText, value); }

    public bool CanRun => !_isRunning;

    private int _activeResultsTab;
    public int ActiveResultsTab { get => _activeResultsTab; set => SetProperty(ref _activeResultsTab, value); }

    // ── Commands ──────────────────────────────────────────────────────────────

    public IAsyncRelayCommand RunScriptCommand { get; }
    private string _statusText = "Ready";
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    // ── Commands ─────────────────────────────────────────────────────────────

    public IAsyncRelayCommand RunCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IRelayCommand NewScriptCommand { get; }
    public IAsyncRelayCommand SaveScriptCommand { get; }
    public IRelayCommand RefreshScriptsCommand { get; }
    public IRelayCommand ClearConsoleCommand { get; }

    public QuantScriptViewModel(
        IScriptRunner runner,
        IQuantScriptCompiler compiler,
        PlotQueue plotQueue,
        IQuantScriptLayoutService layoutService,
        IOptions<QuantScriptOptions> options,
        ILogger<QuantScriptViewModel> logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _plotQueue = plotQueue ?? throw new ArgumentNullException(nameof(plotQueue));
        _layoutService = layoutService ?? throw new ArgumentNullException(nameof(layoutService));
        _options = options?.Value ?? new QuantScriptOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        RunScriptCommand = new AsyncRelayCommand(RunAsync, () => CanRun);
        StopCommand = new RelayCommand(() => RunScriptCommand.Cancel(), () => IsRunning);
        NewScriptCommand = new RelayCommand(NewScript);
        SaveScriptCommand = new AsyncRelayCommand(SaveScriptAsync);
        RefreshScriptsCommand = new RelayCommand(RefreshScripts);
        ClearConsoleCommand = new RelayCommand(ClearConsole);

        // Wire all tab headers to their collections' CollectionChanged events
        ConsoleOutput.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(ConsoleTabHeader));
        Charts.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(ChartsTabHeader));
        Metrics.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(MetricsTabHeader));
        Trades.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(TradesTabHeader));
        Diagnostics.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(DiagnosticsTabHeader));
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>Called from code-behind on page load (UI thread).</summary>
    internal void OnActivated()
    {
        var (leftWidth, rightWidth) = _layoutService.LoadColumnWidths();
        ActiveResultsTab = _layoutService.LoadLastActiveTab();

        // Timers must be created on the UI thread
        _consoleDrainTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _consoleDrainTimer.Tick += (_, _) => FlushConsole();

        _elapsedTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _elapsedTimer.Tick += (_, _) =>
        {
            if (_runStopwatch?.IsRunning == true)
                ElapsedText = $"{_runStopwatch.Elapsed.TotalSeconds:F1}s";
        };

        SetupFileWatcher();
        RefreshScripts();
    }

    public void SaveLayout(double leftWidth, double rightWidth)
    {
        _layoutService.SaveColumnWidths(leftWidth, rightWidth);
        _layoutService.SaveLastActiveTab(ActiveResultsTab);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _consoleDrainTimer?.Stop();
        _elapsedTimer?.Stop();
        _runStopwatch?.Stop();
        _fileWatcher?.Dispose();
        _plotQueue.Complete();

        if (IsRunning) RunScriptCommand.Cancel();
    }

    // ── Script execution ──────────────────────────────────────────────────────

    private async Task RunAsync(CancellationToken ct)
    {
        ClearResults();
        IsRunning = true;
        StatusText = "Compiling…";
        ProgressFraction = 0.0;
        _runStopwatch = System.Diagnostics.Stopwatch.StartNew();
        _consoleDrainTimer?.Start();
        _elapsedTimer?.Start();

        try
        {
            var paramDict = Parameters.ToDictionary(
                p => p.Name,
                p => p.ParsedValue,
                StringComparer.OrdinalIgnoreCase);

            StatusText = "Running…";

            var result = await Task.Run(
                () => _runner.RunAsync(_scriptSource, paramDict, ct), ct)
                .ConfigureAwait(false);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ApplyResult(result);
            }, DispatcherPriority.Normal);
        }
        catch (OperationCanceledException)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                StatusText = "Cancelled";
                AppendConsole("Script was cancelled.", ConsoleEntryKind.Warning);
            }, DispatcherPriority.Normal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in QuantScript runner");
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                StatusText = "Error";
                AppendConsole($"Error: {ex.Message}", ConsoleEntryKind.Error);
            }, DispatcherPriority.Normal);
        }
        finally
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _runStopwatch?.Stop();
                _elapsedTimer?.Stop();
                _consoleDrainTimer?.Stop();
                IsRunning = false;
                ProgressFraction = 1.0;
            }, DispatcherPriority.Normal);
        }
    }

    private void ApplyResult(ScriptRunResult result)
    {
        // Console output
        if (!string.IsNullOrEmpty(result.ConsoleOutput))
        {
            foreach (var line in result.ConsoleOutput.Split(Environment.NewLine))
                AppendConsole(line, ConsoleEntryKind.Output);
        }

        if (result.RuntimeError != null)
            AppendConsole($"Error: {result.RuntimeError}", ConsoleEntryKind.Error);

        foreach (var diag in result.CompilationErrors)
            AppendConsole($"[{diag.Line}:{diag.Column}] {diag.Message}", ConsoleEntryKind.Error);

        // Metrics
        foreach (var kv in result.Metrics)
            Metrics.Add(new MetricEntry(kv.Key, kv.Value));

        // Plots
        foreach (var plot in result.Plots)
        {
            Charts.Add(new PlotViewModel(plot.Title, plot));
        }

        // Auto-switch tab if we have charts and none were showing
        if (result.Plots.Count > 0 && Charts.Count == result.Plots.Count)
            ActiveResultsTab = 1;

        if (result.Metrics.Count > 0 && Metrics.Count == result.Metrics.Count && result.Plots.Count == 0)
            ActiveResultsTab = 2;

        // Diagnostics
        Diagnostics.Add(new DiagnosticEntry("Wall clock", $"{result.Elapsed.TotalSeconds:F2}s"));
        Diagnostics.Add(new DiagnosticEntry("Compile time", $"{result.CompileTime.TotalMilliseconds:F0}ms"));
        Diagnostics.Add(new DiagnosticEntry("Peak memory", $"{result.PeakMemoryBytes / 1024.0:F0} KB"));

        StatusText = result.Success
            ? $"Completed in {result.Elapsed.TotalSeconds:F1}s"
            : result.CompilationErrors.Count > 0
                ? $"Compilation failed ({result.CompilationErrors.Count} error(s))"
                : "Failed";

        ElapsedText = $"{result.Elapsed.TotalSeconds:F1}s";
        MemoryText = $"{result.PeakMemoryBytes / 1024.0:F0} KB";
    }

    // ── Other commands ────────────────────────────────────────────────────────

    private void NewScript()
    {
        _selectedScript = null;
        ScriptSource = "// New QuantScript\n";
        RaisePropertyChanged(nameof(SelectedScript));
        IOptions<QuantScriptOptions> options,
        ILogger<QuantScriptViewModel>? logger = null)
    {
        _runner = runner;
        _compiler = compiler;
        _options = options.Value;
        _logger = logger ?? NullLogger<QuantScriptViewModel>.Instance;

        RunCommand = new AsyncRelayCommand(RunScriptAsync, () => !IsRunning);
        StopCommand = new RelayCommand(StopScript, () => IsRunning);
        NewScriptCommand = new RelayCommand(NewScript);
        SaveScriptCommand = new AsyncRelayCommand(SaveScriptAsync);
        RefreshScriptsCommand = new RelayCommand(LoadScriptFiles);

        LoadScriptFiles();
    }

    private void LoadScriptFiles()
    {
        ScriptFiles.Clear();
        var dir = _options.ScriptsDirectory;
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.GetFiles(dir, "*.csx", SearchOption.TopDirectoryOnly)
                                   .OrderBy(f => f))
        {
            ScriptFiles.Add(new ScriptFileEntry(Path.GetFileName(f), f));
        }
    }

    private void LoadScript(ScriptFileEntry? entry)
    {
        if (entry is null) return;
        try
        {
            var source = File.ReadAllText(entry.FullPath);
            _scriptSource = source;   // Set backing field to avoid re-triggering UpdateParameters
            RaisePropertyChanged(nameof(ScriptSource));
            UpdateParameters(source);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load script {File}", entry.FullPath);
            AddConsole($"Failed to load script: {ex.Message}", ConsoleEntryKind.Error);
        }
    }

    private void UpdateParameters(string source)
    {
        Parameters.Clear();
        var descriptors = _compiler.ExtractParameters(source);
        foreach (var d in descriptors)
        {
            Parameters.Add(new ParameterViewModel
            {
                Descriptor = d,
                RawValue = d.DefaultValue?.ToString() ?? string.Empty
            });
        }
    }

    private async Task RunScriptAsync()
    {
        if (IsRunning) return;
        IsRunning = true;
        StatusText = "Compiling…";
        ConsoleOutput.Clear();
        Charts.Clear();
        Metrics.Clear();
        Trades.Clear();

        _runCts = new CancellationTokenSource();
        var ct = _runCts.Token;

        var parameters = Parameters.ToDictionary<ParameterViewModel, string, object?>(
            p => p.Descriptor.Name,
            p => p.Descriptor.DefaultValue);

        try
        {
            StatusText = "Running…";
            var result = await Task.Run(() => _runner.RunAsync(_scriptSource, parameters, ct), ct);

            var elapsed = $"{result.Elapsed.TotalSeconds:F1}s";
            if (result.Success)
            {
                StatusText = $"Completed in {elapsed}";
                AddConsole($"Script completed in {elapsed}", ConsoleEntryKind.Output);
            }
            else if (result.CompilationErrors.Count > 0)
            {
                StatusText = $"Compilation failed ({result.CompilationErrors.Count} error(s))";
                foreach (var err in result.CompilationErrors)
                    AddConsole($"[{err.Severity}] Line {err.Line}: {err.Message}", ConsoleEntryKind.Error);
            }
            else
            {
                StatusText = $"Failed: {result.RuntimeError}";
                AddConsole(result.RuntimeError ?? "Unknown error", ConsoleEntryKind.Error);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled";
            AddConsole("Script cancelled.", ConsoleEntryKind.Warning);
        }
        catch (Exception ex)
        {
            StatusText = "Error";
            AddConsole($"Unexpected error: {ex.Message}", ConsoleEntryKind.Error);
            _logger.LogError(ex, "Unexpected error running script");
        }
        finally
        {
            IsRunning = false;
            _runCts?.Dispose();
            _runCts = null;
        }
    }

    private void StopScript()
    {
        _runCts?.Cancel();
        StatusText = "Cancelling…";
    }

    private void NewScript()
    {
        SelectedScript = null;
        ScriptSource = "// New script\nvar spy = Data.Prices(\"SPY\", new DateOnly(2023,1,1), new DateOnly(2024,1,1));\nvar ret = spy.DailyReturns();\nPrintMetric(\"Sharpe\", SharpeRatio(ret));\nret.PlotCumulative(\"SPY 2023\");\n";
        StatusText = "New script";
    }

    private async Task SaveScriptAsync()
    {
        if (SelectedScript is not null)
        {
            await File.WriteAllTextAsync(SelectedScript.FullPath, _scriptSource).ConfigureAwait(false);
            _logger.LogInformation("Saved script to {Path}", SelectedScript.FullPath);
        }
        else
        {
            // Prompt handled by future dialog — for now save with a timestamp name
            var dir = _options.ScriptsDirectory;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"script-{DateTime.Now:yyyyMMddHHmmss}.csx");
            await File.WriteAllTextAsync(path, _scriptSource).ConfigureAwait(false);
            RefreshScripts();
        }
    }

    private void RefreshScripts()
    {
        ScriptFiles.Clear();
        var dir = _options.ScriptsDirectory;
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.GetFiles(dir, "*.csx").OrderBy(f => f))
        {
            var name = Path.GetFileName(file);
            ScriptFiles.Add(new ScriptFileEntry(name, file));
        }
    }

    private void ClearConsole() => ConsoleOutput.Clear();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ClearResults()
    {
        ConsoleOutput.Clear();
        Charts.Clear();
        Metrics.Clear();
        Trades.Clear();
        Diagnostics.Clear();
        ProgressFraction = 0;
        ActiveResultsTab = 0;
        ElapsedText = "--";
        MemoryText = "--";
    }

    private void FlushConsole()
    {
        // Drain any pending console entries during run (future channel-based approach)
    }

    private void AppendConsole(string text, ConsoleEntryKind kind)
    {
        ConsoleOutput.Add(new ConsoleEntry(DateTimeOffset.Now, text, kind));
    }

    private void LoadScriptFile(string path)
    {
        try
        {
            ScriptSource = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load script from {Path}", path);
            AppendConsole($"Failed to load: {ex.Message}", ConsoleEntryKind.Error);
        }
    }

    private void SetupFileWatcher()
    {
        var dir = _options.ScriptsDirectory;
        if (!Directory.Exists(dir)) return;

        _fileWatcher?.Dispose();
        _fileWatcher = new FileSystemWatcher(dir, "*.csx")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };

        _fileWatcher.Created += (_, _) => Application.Current?.Dispatcher.InvokeAsync(RefreshScripts, DispatcherPriority.Background);
        _fileWatcher.Deleted += (_, _) => Application.Current?.Dispatcher.InvokeAsync(RefreshScripts, DispatcherPriority.Background);
        _fileWatcher.Renamed += (_, _) => Application.Current?.Dispatcher.InvokeAsync(RefreshScripts, DispatcherPriority.Background);
        if (SelectedScript is null)
        {
            AddConsole("No file selected — use Save As (not yet implemented).", ConsoleEntryKind.Warning);
            return;
        }
        try
        {
            await File.WriteAllTextAsync(SelectedScript.FullPath, ScriptSource);
            StatusText = $"Saved {SelectedScript.Name}";
        }
        catch (Exception ex)
        {
            AddConsole($"Save failed: {ex.Message}", ConsoleEntryKind.Error);
        }
    }

    private void AddConsole(string text, ConsoleEntryKind kind)
    {
        if (Application.Current.Dispatcher.CheckAccess())
            ConsoleOutput.Add(new ConsoleEntry(DateTimeOffset.UtcNow, text, kind));
        else
            Application.Current.Dispatcher.InvokeAsync(() =>
                ConsoleOutput.Add(new ConsoleEntry(DateTimeOffset.UtcNow, text, kind)));
    }

    public void Dispose()
    {
        _runCts?.Cancel();
        _runCts?.Dispose();
    }
}
