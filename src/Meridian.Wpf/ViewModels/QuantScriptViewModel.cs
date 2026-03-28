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

    private string _scriptSource = string.Empty;
    public string ScriptSource
    {
        get => _scriptSource;
        set => SetProperty(ref _scriptSource, value);
    }

    public ObservableCollection<ScriptFileEntry> ScriptFiles { get; } = new();

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

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                RaisePropertyChanged(nameof(CanRun));
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
    }
}
