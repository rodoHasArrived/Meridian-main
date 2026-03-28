using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
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
    private readonly QuantScriptOptions _options;
    private readonly ILogger<QuantScriptViewModel> _logger;
    private CancellationTokenSource? _runCts;

    // ── Script editor ───────────────────────────────────────────────────────

    private string _scriptSource = string.Empty;
    public string ScriptSource
    {
        get => _scriptSource;
        set { if (SetProperty(ref _scriptSource, value)) UpdateParameters(value); }
    }

    // ── Script browser ──────────────────────────────────────────────────────

    public ObservableCollection<ScriptFileEntry> ScriptFiles { get; } = [];

    private ScriptFileEntry? _selectedScript;
    public ScriptFileEntry? SelectedScript
    {
        get => _selectedScript;
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
                RunCommand.NotifyCanExecuteChanged();
                StopCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private string _statusText = "Ready";
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    // ── Commands ─────────────────────────────────────────────────────────────

    public IAsyncRelayCommand RunCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IRelayCommand NewScriptCommand { get; }
    public IAsyncRelayCommand SaveScriptCommand { get; }
    public IRelayCommand RefreshScriptsCommand { get; }

    public QuantScriptViewModel(
        IScriptRunner runner,
        IQuantScriptCompiler compiler,
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
