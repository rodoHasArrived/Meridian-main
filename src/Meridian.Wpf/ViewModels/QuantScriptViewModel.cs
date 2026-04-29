using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Meridian.Backtesting.Sdk;
using Meridian.QuantScript;
using Meridian.QuantScript.Compilation;
using Meridian.QuantScript.Documents;
using Meridian.QuantScript.Plotting;
using Meridian.Ui.Services.Collections;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Palette = Meridian.Ui.Services.Services.ColorPalette;

namespace Meridian.Wpf.ViewModels;

public sealed class QuantScriptViewModel : BindableBase, IDisposable
{
    private const int LocalDataTabIndex = 3;
    private const int BacktestOutputTabIndex = 4;
    private const int RunHistoryTabIndex = 5;

    private static readonly System.Windows.Media.Color[] LegendPalette =
    [
        ToWpfColor(Palette.ChartPrimary),
        ToWpfColor(Palette.ChartSecondary),
        ToWpfColor(Palette.ChartTertiary),
        ToWpfColor(Palette.ChartPositive),
        ToWpfColor(Palette.ChartNegative),
    ];

    private static System.Windows.Media.Color ToWpfColor(Palette.ArgbColor color)
        => System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B);

    private readonly IScriptRunner _runner;
    private readonly IQuantScriptCompiler _compiler;
    private readonly PlotQueue _plotQueue;
    private readonly IQuantScriptLayoutService _layoutService;
    private readonly IQuantScriptNotebookStore _notebookStore;
    private readonly QuantScriptTemplateCatalogService _templateCatalogService;
    private readonly QuantScriptExecutionHistoryService _executionHistoryService;
    private readonly NavigationService _navigationService;
    private readonly QuantScriptOptions _options;
    private readonly ILogger<QuantScriptViewModel> _logger;
    private readonly NotebookExecutionSession _session = new();
    private DispatcherTimer? _elapsedTimer;
    private System.Diagnostics.Stopwatch? _runStopwatch;
    private FileSystemWatcher? _fileWatcher;
    private bool _disposed;
    private bool _suppressCellSync;
    private bool _loadingDocument;
    private bool _suppressTemplateSelection;
    private string _currentDocumentPath = string.Empty;
    private QuantScriptDocumentKind _currentDocumentKind = QuantScriptDocumentKind.LegacyScript;
    private string _currentDocumentTitle = "Untitled Script";
    private string _assetSymbol = string.Empty;
    private DateTime _fromDate = DateTime.Today.AddYears(-1);
    private DateTime _toDate = DateTime.Today;
    private string _selectedInterval = "Daily (Custom)";
    private string _scriptSource = string.Empty;
    private ScriptDocumentEntry? _selectedDocument;
    private NotebookCellViewModel? _selectedCell;
    private QuantScriptTemplateDefinition? _selectedTemplate;
    private QuantScriptExecutionRecord? _selectedExecutionRecord;
    private bool _isRunning;
    private bool _isDirty;
    private double _progressFraction;
    private string _statusText = "Ready";
    private string _elapsedText = "--";
    private string _memoryText = "--";
    private int _activeResultsTab;

    public QuantScriptViewModel(
        IScriptRunner runner,
        IQuantScriptCompiler compiler,
        PlotQueue plotQueue,
        IQuantScriptLayoutService layoutService,
        IQuantScriptNotebookStore notebookStore,
        QuantScriptTemplateCatalogService templateCatalogService,
        QuantScriptExecutionHistoryService executionHistoryService,
        NavigationService navigationService,
        IOptions<QuantScriptOptions> options,
        ILogger<QuantScriptViewModel> logger)
    {
        _runner = runner;
        _compiler = compiler;
        _plotQueue = plotQueue;
        _layoutService = layoutService;
        _notebookStore = notebookStore;
        _templateCatalogService = templateCatalogService;
        _executionHistoryService = executionHistoryService;
        _navigationService = navigationService;
        _options = options?.Value ?? new QuantScriptOptions();
        _logger = logger;

        RunScriptCommand = new AsyncRelayCommand(RunCurrentCellAsync, () => CanRun);
        RunAllCommand = new AsyncRelayCommand(RunAllAsync, () => !IsRunning && NotebookCells.Count > 0);
        RunAndAdvanceCommand = new AsyncRelayCommand(RunAndAdvanceAsync, () => CanRun);
        StopCommand = new RelayCommand(StopRunning, () => IsRunning);
        NewScriptCommand = new RelayCommand(NewScript, () => !IsRunning);
        NewNotebookCommand = new RelayCommand(NewNotebook, () => !IsRunning);
        SaveScriptCommand = new AsyncRelayCommand(SaveScriptAsync, () => !IsRunning && NotebookCells.Count > 0);
        RefreshScriptsCommand = new RelayCommand(RefreshScripts, () => !IsRunning);
        ClearConsoleCommand = new RelayCommand(() => ConsoleOutput.Clear());
        AddCellCommand = new RelayCommand(AddCell, () => !IsRunning);
        DeleteCellCommand = new RelayCommand(DeleteSelectedCell, () => !IsRunning && NotebookCells.Count > 1);
        TemplatesCommand = new RelayCommand(OpenTemplates, () => !IsRunning);
        OpenRunBrowserCommand = new RelayCommand(OpenRunBrowser, () => CanOpenSelectedHistoryRun);
        OpenRunDetailCommand = new RelayCommand(OpenRunDetail, () => CanOpenSelectedHistoryRun);
        CompareInResearchCommand = new RelayCommand(CompareInResearch, () => CanCompareSelectedHistoryRun);

        ConsoleOutput.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(ConsoleTabHeader));
        Charts.CollectionChanged += (_, _) => { RaisePropertyChanged(nameof(ChartsTabHeader)); UpdatePrimaryChart(); };
        Metrics.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(MetricsTabHeader));
        Trades.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(TradesTabHeader));
        Diagnostics.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(DiagnosticsTabHeader));
        RunHistory.CollectionChanged += (_, _) =>
        {
            RaisePropertyChanged(nameof(RunHistoryTabHeader));
            RaisePropertyChanged(nameof(HasRunHistory));
            RaisePropertyChanged(nameof(HasNoRunHistory));
            RaisePropertyChanged(nameof(RunHistoryScopeText));
            NotifyHistoryCommandStateChanged();
        };
        NotebookCells.CollectionChanged += (_, _) =>
        {
            UpdateCellOrdinals();
            RefreshParameters();
            RaisePropertyChanged(nameof(HasNotebookCells));
            RaisePropertyChanged(nameof(CanDeleteCell));
            NotifyCommandStateChanged();
        };

        LoadTemplates();
        InitializeDefaultDocument();
    }

    public string AssetSymbol { get => _assetSymbol; set => SetProperty(ref _assetSymbol, value); }
    public DateTime FromDate { get => _fromDate; set => SetProperty(ref _fromDate, value); }
    public DateTime ToDate { get => _toDate; set => SetProperty(ref _toDate, value); }
    public string SelectedInterval { get => _selectedInterval; set => SetProperty(ref _selectedInterval, value); }
    public static IReadOnlyList<string> Intervals { get; } = ["Daily", "Daily (Custom)", "Weekly", "Monthly"];
    public PlotRequest? PrimaryChartRequest => Charts.Count > 0 ? Charts[0].Request : null;
    public string PrimaryChartTitle => Charts.Count > 0 ? Charts[0].Title : string.Empty;
    public bool HasChart => Charts.Count > 0;
    public bool HasNoChart => Charts.Count == 0;
    public bool HasLegend => LegendEntries.Count > 0;
    public ObservableCollection<ChartLegendEntry> LegendEntries { get; } = [];

    public string ScriptSource
    {
        get => _scriptSource;
        set
        {
            if (!SetProperty(ref _scriptSource, value))
                return;

            if (!_suppressCellSync && SelectedCell is not null && SelectedCell.Source != value)
                SelectedCell.Source = value;

            RefreshParameters();
        }
    }

    public ObservableCollection<ScriptDocumentEntry> Documents { get; } = [];
    public ObservableCollection<QuantScriptTemplateDefinition> Templates { get; } = [];
    public ObservableCollection<NotebookCellViewModel> NotebookCells { get; } = [];
    public ObservableCollection<ParameterViewModel> Parameters { get; } = [];
    public BoundedObservableCollection<ConsoleEntry> ConsoleOutput { get; } = new(10_000);
    public ObservableCollection<PlotViewModel> Charts { get; } = [];
    public ObservableCollection<MetricEntry> Metrics { get; } = [];
    public ObservableCollection<TradeEntry> Trades { get; } = [];
    public ObservableCollection<DiagnosticEntry> Diagnostics { get; } = [];
    public ObservableCollection<QuantScriptExecutionRecord> RunHistory { get; } = [];

    public ScriptDocumentEntry? SelectedDocument
    {
        get => _selectedDocument;
        set
        {
            if (!SetProperty(ref _selectedDocument, value) || value is null)
                return;

            LoadDocumentAsync(value);
        }
    }

    public QuantScriptTemplateDefinition? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (!SetProperty(ref _selectedTemplate, value) || value is null || _suppressTemplateSelection)
                return;

            _ = ApplyTemplateAsync(value);
        }
    }

    public NotebookCellViewModel? SelectedCell
    {
        get => _selectedCell;
        set
        {
            if (!SetProperty(ref _selectedCell, value))
                return;

            SyncScriptSourceFromCell();
            RaisePropertyChanged(nameof(CurrentCellTitle));
            RaisePropertyChanged(nameof(CurrentCellStatus));
            NotifyCommandStateChanged();
        }
    }

    public QuantScriptExecutionRecord? SelectedExecutionRecord
    {
        get => _selectedExecutionRecord;
        set
        {
            if (!SetProperty(ref _selectedExecutionRecord, value))
                return;

            NotifyHistoryCommandStateChanged();
        }
    }

    public bool HasNotebookCells => NotebookCells.Count > 0;
    public bool HasRunHistory => RunHistory.Count > 0;
    public bool HasNoRunHistory => RunHistory.Count == 0;
    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    public string CurrentDocumentTitle { get => _currentDocumentTitle; private set => SetProperty(ref _currentDocumentTitle, value); }
    public string CurrentDocumentKindText => ResolveSaveKind() == QuantScriptDocumentKind.Notebook ? "Notebook" : "Script";
    public string CurrentCellTitle => SelectedCell?.Title ?? "Cell";
    public string CurrentCellStatus => SelectedCell?.StatusText ?? "Idle";
    public bool CanDeleteCell => NotebookCells.Count > 1 && !IsRunning;
    public string ConsoleTabHeader => ConsoleOutput.Count > 0 ? $"Console ({ConsoleOutput.Count})" : "Console";
    public string ChartsTabHeader => Charts.Count > 0 ? $"Charts ({Charts.Count})" : "Charts";
    public string MetricsTabHeader => Metrics.Count > 0 ? $"Metrics ({Metrics.Count})" : "Metrics";
    public string TradesTabHeader => Trades.Count > 0 ? $"Trades ({Trades.Count})" : "Trades";
    public string DiagnosticsTabHeader => Diagnostics.Count > 0 ? $"Diagnostics ({Diagnostics.Count})" : "Diagnostics";
    public string RunHistoryTabHeader => RunHistory.Count > 0 ? $"Run History ({RunHistory.Count})" : "Run History";
    public string RunHistoryScopeText => RunHistory.Count switch
    {
        0 => "No execution history",
        1 => "1 execution record",
        _ => $"{RunHistory.Count} execution records"
    };

    public string HistoryEmptyStateTitle => "No QuantScript execution history yet";
    public string HistoryEmptyStateDetail => "Run a cell or notebook to capture parameters, outputs, and run-browser handoffs.";
    public string SelectedHistoryTitle => SelectedExecutionRecord?.DocumentTitle ?? "No history entry selected";
    public string SelectedHistoryDetail => SelectedExecutionRecord is null
        ? "Select a history entry to inspect the captured parameters, console excerpt, and Strategy Runs handoff."
        : $"{SelectedExecutionRecord.StatusText} {SelectedExecutionRecord.DocumentKindLabel} from {SelectedExecutionRecord.ExecutedAtText}";
    public string SelectedHistoryEvidenceText => SelectedExecutionRecord is null
        ? "No execution evidence selected"
        : string.Join(
            " | ",
            FormatCount(SelectedExecutionRecord.Metrics.Count, "metric"),
            FormatCount(SelectedExecutionRecord.PlotTitles.Count, "plot"),
            FormatCount(SelectedExecutionRecord.CapturedBacktestCount, "mirrored backtest"));
    public string SelectedHistoryRunLinkText => SelectedExecutionRecord is null
        ? "No run handoff selected"
        : SelectedExecutionRecord.HasMirroredRun
            ? $"Mirrored Strategy Runs handoff: {SelectedExecutionRecord.MirroredRunId}"
            : "Local execution only; no Strategy Runs handoff was recorded.";
    public string SelectedHistoryParameterText => SelectedExecutionRecord?.ParameterSummary ?? "No parameters selected";
    public string SelectedHistoryConsolePreview => SelectedExecutionRecord?.ConsolePreview ?? "No console output selected";
    public bool CanOpenSelectedHistoryRun => SelectedExecutionRecord?.HasMirroredRun == true;
    public bool CanCompareSelectedHistoryRun => SelectedExecutionRecord?.HasMirroredRun == true;

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!SetProperty(ref _isRunning, value))
                return;

            RaisePropertyChanged(nameof(CanRun));
            RaisePropertyChanged(nameof(CanDeleteCell));
            NotifyCommandStateChanged();
        }
    }

    public double ProgressFraction { get => _progressFraction; private set => SetProperty(ref _progressFraction, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string ElapsedText { get => _elapsedText; private set => SetProperty(ref _elapsedText, value); }
    public string MemoryText { get => _memoryText; private set => SetProperty(ref _memoryText, value); }
    public bool CanRun => !IsRunning && SelectedCell is not null;
    public int ActiveResultsTab { get => _activeResultsTab; set => SetProperty(ref _activeResultsTab, value); }

    public IAsyncRelayCommand RunScriptCommand { get; }
    public IAsyncRelayCommand RunAllCommand { get; }
    public IAsyncRelayCommand RunAndAdvanceCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IRelayCommand NewScriptCommand { get; }
    public IRelayCommand NewNotebookCommand { get; }
    public IAsyncRelayCommand SaveScriptCommand { get; }
    public IRelayCommand RefreshScriptsCommand { get; }
    public IRelayCommand ClearConsoleCommand { get; }
    public IRelayCommand AddCellCommand { get; }
    public IRelayCommand DeleteCellCommand { get; }
    public IRelayCommand TemplatesCommand { get; }
    public IRelayCommand OpenRunBrowserCommand { get; }
    public IRelayCommand OpenRunDetailCommand { get; }
    public IRelayCommand CompareInResearchCommand { get; }

    internal (double ChartHeight, double EditorHeight) OnActivated()
    {
        var (chartHeight, editorHeight) = _layoutService.LoadRowHeights();
        ActiveResultsTab = _layoutService.LoadLastActiveTab();

        if (_elapsedTimer is null)
        {
            _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _elapsedTimer.Tick += (_, _) =>
            {
                if (_runStopwatch?.IsRunning == true)
                    ElapsedText = $"{_runStopwatch.Elapsed.TotalSeconds:F1}s";
            };
        }

        SetupFileWatcher();
        RefreshScripts();
        _ = LoadRunHistoryAsync();

        if (Documents.Count > 0 && SelectedDocument is null)
            SelectedDocument = Documents[0];
        else if (NotebookCells.Count == 0)
            NewScript();

        return (chartHeight, editorHeight);
    }

    public void SaveLayout(double chartHeight, double editorHeight)
    {
        _layoutService.SaveRowHeights(chartHeight, editorHeight);
        _layoutService.SaveLastActiveTab(ActiveResultsTab);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _elapsedTimer?.Stop();
        _fileWatcher?.Dispose();
        _plotQueue.Complete();

        if (IsRunning)
            StopRunning();
    }

    private async Task RunCurrentCellAsync(CancellationToken ct)
    {
        if (!ValidateToolbarContext())
            return;

        if (SelectedCell is null)
            return;

        var targetIndex = NotebookCells.IndexOf(SelectedCell);
        if (targetIndex < 0)
            return;

        var identities = GetCellIdentities();
        await ExecuteCellsAsync(_session.GetReplayStartIndex(identities, targetIndex), targetIndex, false, ct);
    }

    private async Task RunAllAsync(CancellationToken ct)
    {
        if (!ValidateToolbarContext())
            return;

        if (NotebookCells.Count == 0)
            return;

        _session.Reset();
        foreach (var cell in NotebookCells)
        {
            cell.State = NotebookCellExecutionState.Stale;
            cell.StatusText = "Pending";
        }

        await ExecuteCellsAsync(0, NotebookCells.Count - 1, false, ct);
    }

    private async Task RunAndAdvanceAsync(CancellationToken ct)
    {
        if (!ValidateToolbarContext())
            return;

        if (SelectedCell is null)
            return;

        var targetIndex = NotebookCells.IndexOf(SelectedCell);
        if (targetIndex < 0)
            return;

        var identities = GetCellIdentities();
        await ExecuteCellsAsync(_session.GetReplayStartIndex(identities, targetIndex), targetIndex, true, ct);
    }

    private async Task<bool> ExecuteCellsAsync(int startIndex, int endIndex, bool advanceSelection, CancellationToken ct)
    {
        if (startIndex < 0 || endIndex < startIndex || endIndex >= NotebookCells.Count)
            return false;

        ClearResults();
        IsRunning = true;
        StatusText = startIndex == endIndex
            ? $"Running {NotebookCells[endIndex].Title.ToLowerInvariant()}..."
            : $"Running cells {startIndex + 1}-{endIndex + 1}...";
        _runStopwatch = System.Diagnostics.Stopwatch.StartNew();
        _elapsedTimer?.Start();

        var identities = GetCellIdentities();
        var parameters = BuildParameterDictionary();
        var checkpoint = startIndex > 0 ? _session.GetPreviousCheckpoint(identities, startIndex) : null;
        var currentIndex = startIndex;
        var succeeded = true;
        var latestRuntimeParameters = Array.Empty<ParameterDescriptor>();
        var capturedBacktests = new List<BacktestResult>();

        try
        {
            for (var index = startIndex; index <= endIndex; index++)
            {
                currentIndex = index;
                var cell = NotebookCells[index];
                cell.State = NotebookCellExecutionState.Running;
                cell.StatusText = "Running";

                if (endIndex > startIndex || startIndex > 0)
                    AppendConsole($"# {cell.Title}", ConsoleEntryKind.Separator);

                var result = checkpoint is null
                    ? await _runner.RunAsync(cell.Source, parameters, ct)
                    : await _runner.ContinueWithAsync(cell.Source, checkpoint, parameters, ct);

                ApplyResult(result);

                if (result.RuntimeParameters.Count > 0)
                {
                    latestRuntimeParameters = result.RuntimeParameters.ToArray();
                    MergeParameterDescriptors(latestRuntimeParameters, preserveUserValues: true);
                    parameters = BuildParameterDictionary();
                }

                if (result.CapturedBacktests.Count > 0)
                    capturedBacktests.AddRange(result.CapturedBacktests);

                if (result.Success && result.Checkpoint is not null)
                {
                    checkpoint = result.Checkpoint;
                    _session.RecordSuccessfulRun(identities[index], result.Checkpoint);
                    cell.State = NotebookCellExecutionState.Done;
                    cell.StatusText = $"{result.Elapsed.TotalSeconds:F1}s";
                }
                else
                {
                    succeeded = false;
                    _session.RecordFailedRun(identities, index);
                    cell.State = NotebookCellExecutionState.Error;
                    cell.StatusText = result.CompilationErrors.Count > 0 ? $"{result.CompilationErrors.Count} error(s)" : "Failed";
                    MarkCellsStaleFrom(index + 1);
                    break;
                }

                ProgressFraction = (double)(index - startIndex + 1) / (endIndex - startIndex + 1);
            }

            if (succeeded)
            {
                StatusText = endIndex > startIndex
                    ? $"Completed {endIndex - startIndex + 1} cells"
                    : $"Completed {NotebookCells[endIndex].Title.ToLowerInvariant()}";

                if (advanceSelection)
                    AdvanceSelectionAfterRun();
            }

            return succeeded;
        }
        catch (OperationCanceledException)
        {
            succeeded = false;
            StatusText = "Cancelled";
            AppendConsole("Script was cancelled.", ConsoleEntryKind.Warning);
            MarkCellsStaleFrom(currentIndex);
            return false;
        }
        catch (Exception ex)
        {
            succeeded = false;
            _logger.LogError(ex, "Unhandled error in QuantScript runner");
            StatusText = "Error";
            AppendConsole($"Error: {ex.Message}", ConsoleEntryKind.Error);
            MarkCellsStaleFrom(currentIndex);
            return false;
        }
        finally
        {
            _runStopwatch?.Stop();
            _elapsedTimer?.Stop();
            IsRunning = false;
            if (succeeded)
                ProgressFraction = 1.0;

            await PersistExecutionHistoryAsync(succeeded, latestRuntimeParameters, capturedBacktests, CancellationToken.None);
        }
    }

    private void ApplyResult(ScriptRunResult result)
    {
        if (!string.IsNullOrEmpty(result.ConsoleOutput))
        {
            foreach (var line in result.ConsoleOutput.Split(Environment.NewLine, StringSplitOptions.None))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    AppendConsole(line, ConsoleEntryKind.Output);
            }
        }

        if (result.RuntimeError is not null)
            AppendConsole($"Error: {result.RuntimeError}", ConsoleEntryKind.Error);

        foreach (var diagnostic in result.CompilationErrors)
            AppendConsole($"[{diagnostic.Line}:{diagnostic.Column}] {diagnostic.Message}", ConsoleEntryKind.Error);

        foreach (var metric in result.Metrics)
            AddOrUpdateMetric(metric.Key, metric.Value);

        foreach (var plot in result.Plots)
            Charts.Add(new PlotViewModel(plot.Title, plot));

        if (result.CapturedBacktests.Count > 0)
            ApplyCapturedBacktests(result.CapturedBacktests);

        if (Metrics.Count > 0 && Charts.Count == 0 && Trades.Count == 0)
            ActiveResultsTab = 1;
        else if (Trades.Count > 0 && Charts.Count == 0)
            ActiveResultsTab = BacktestOutputTabIndex;

        Diagnostics.Add(new DiagnosticEntry("Wall clock", $"{result.Elapsed.TotalSeconds:F2}s"));
        Diagnostics.Add(new DiagnosticEntry("Compile time", $"{result.CompileTime.TotalMilliseconds:F0}ms"));
        Diagnostics.Add(new DiagnosticEntry("Peak memory", $"{result.PeakMemoryBytes / 1024.0:F0} KB"));

        ElapsedText = $"{result.Elapsed.TotalSeconds:F1}s";
        MemoryText = $"{result.PeakMemoryBytes / 1024.0:F0} KB";
    }

    private void ApplyCapturedBacktests(IReadOnlyList<BacktestResult> backtests)
    {
        for (var index = 0; index < backtests.Count; index++)
        {
            var backtest = backtests[index];
            var category = backtests.Count == 1 ? "Backtest" : $"Backtest {index + 1}";

            AddOrUpdateMetric("Net PnL", backtest.Metrics.NetPnl.ToString("C2"), category, "Captured");
            AddOrUpdateMetric("Total Return", backtest.Metrics.TotalReturn.ToString("P2"), category, "Captured");
            AddOrUpdateMetric("Final Equity", backtest.Metrics.FinalEquity.ToString("C2"), category, "Captured");
            AddOrUpdateMetric("Sharpe Ratio", backtest.Metrics.SharpeRatio.ToString("F3"), category, "Captured");
            AddOrUpdateMetric("Max Drawdown", backtest.Metrics.MaxDrawdownPercent.ToString("P2"), category, "Captured");
            AddOrUpdateMetric("Total Trades", backtest.Metrics.TotalTrades.ToString("N0"), category, "Captured");

            foreach (var fill in backtest.Fills.OrderBy(static item => item.FilledAt))
            {
                Trades.Add(new TradeEntry(
                    fill.FilledAt,
                    fill.Symbol,
                    Math.Abs(fill.FilledQuantity),
                    fill.FillPrice,
                    fill.Commission,
                    fill.FilledQuantity >= 0 ? "Buy" : "Sell"));
            }
        }
    }

    private void StopRunning()
    {
        RunScriptCommand.Cancel();
        RunAllCommand.Cancel();
        RunAndAdvanceCommand.Cancel();
    }

    private void NewScript()
    {
        LoadInMemoryDocument(
            "Untitled Script",
            string.Empty,
            QuantScriptDocumentKind.LegacyScript,
            [new QuantScriptNotebookCellDocument(Guid.NewGuid().ToString("N"), BuildDefaultStarterSource())]);
        StatusText = "New script";
    }

    private void NewNotebook()
    {
        LoadInMemoryDocument(
            "QuantScript Notebook",
            string.Empty,
            QuantScriptDocumentKind.Notebook,
            [new QuantScriptNotebookCellDocument(Guid.NewGuid().ToString("N"), BuildDefaultStarterSource())]);
        StatusText = "New notebook";
    }

    private async Task SaveScriptAsync()
    {
        if (NotebookCells.Count == 0)
            return;

        if (ResolveSaveKind() == QuantScriptDocumentKind.Notebook)
        {
            var path = !string.IsNullOrWhiteSpace(_currentDocumentPath) && _currentDocumentKind == QuantScriptDocumentKind.Notebook
                ? _currentDocumentPath
                : _notebookStore.GetSuggestedNotebookPath(CurrentDocumentTitle);
            await _notebookStore.SaveNotebookAsync(path, BuildNotebookDocument());
            _currentDocumentPath = path;
            _currentDocumentKind = QuantScriptDocumentKind.Notebook;
            CurrentDocumentTitle = Path.GetFileNameWithoutExtension(path);
            StatusText = $"Saved {Path.GetFileName(path)}";
        }
        else
        {
            var path = !string.IsNullOrWhiteSpace(_currentDocumentPath) && _currentDocumentKind == QuantScriptDocumentKind.LegacyScript
                ? _currentDocumentPath
                : Path.Combine(_options.ScriptsDirectory, $"script-{DateTime.Now:yyyyMMddHHmmss}.csx");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? _options.ScriptsDirectory);
            await File.WriteAllTextAsync(path, NotebookCells[0].Source);
            _currentDocumentPath = path;
            _currentDocumentKind = QuantScriptDocumentKind.LegacyScript;
            CurrentDocumentTitle = Path.GetFileNameWithoutExtension(path);
            StatusText = $"Saved {Path.GetFileName(path)}";
        }

        RefreshScripts();
        MarkClean();
        RaisePropertyChanged(nameof(CurrentDocumentKindText));
    }

    private void RefreshScripts()
    {
        var preferredPath = !string.IsNullOrWhiteSpace(_currentDocumentPath) ? _currentDocumentPath : _selectedDocument?.FullPath;
        var documents = _notebookStore.ListDocuments()
            .Select(static d => new ScriptDocumentEntry(d.Name, d.FullPath, d.Kind))
            .ToList();

        Documents.Clear();
        foreach (var document in documents)
            Documents.Add(document);

        _selectedDocument = preferredPath is null
            ? null
            : Documents.FirstOrDefault(document => string.Equals(document.FullPath, preferredPath, StringComparison.OrdinalIgnoreCase));
        RaisePropertyChanged(nameof(SelectedDocument));
    }

    private void LoadTemplates()
    {
        Templates.Clear();
        foreach (var template in _templateCatalogService.ListTemplates())
            Templates.Add(template);
    }

    private async Task LoadRunHistoryAsync(CancellationToken ct = default)
    {
        try
        {
            var history = await _executionHistoryService.GetHistoryAsync(ct).ConfigureAwait(true);
            RunHistory.Clear();
            foreach (var record in history)
                RunHistory.Add(record);

            if (SelectedExecutionRecord is null ||
                !RunHistory.Any(record => string.Equals(record.ExecutionId, SelectedExecutionRecord.ExecutionId, StringComparison.Ordinal)))
            {
                SelectedExecutionRecord = RunHistory.FirstOrDefault();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load QuantScript execution history");
        }
    }

    private async Task ApplyTemplateAsync(QuantScriptTemplateDefinition template)
    {
        try
        {
            if (IsDirty)
            {
                var response = MessageBox.Show(
                    "Replace the current QuantScript document with the selected template? Unsaved edits will be lost.",
                    "Overwrite current document",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (response != MessageBoxResult.Yes)
                {
                    ResetSelectedTemplate();
                    return;
                }
            }

            var document = await _templateCatalogService.LoadTemplateAsync(template.Id).ConfigureAwait(true);
            LoadInMemoryDocument(
                document.Definition.Title,
                string.Empty,
                document.Definition.DocumentKind,
                [new QuantScriptNotebookCellDocument(Guid.NewGuid().ToString("N"), document.Source)]);

            ActiveResultsTab = 0;
            StatusText = $"Loaded template '{document.Definition.Title}'";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load QuantScript template {TemplateId}", template.Id);
            AppendConsole($"Failed to load template: {ex.Message}", ConsoleEntryKind.Error);
        }
        finally
        {
            ResetSelectedTemplate();
        }
    }

    private void ResetSelectedTemplate()
    {
        _suppressTemplateSelection = true;
        SelectedTemplate = null;
        _suppressTemplateSelection = false;
    }

    private void AddCell()
    {
        var cell = new NotebookCellViewModel(Guid.NewGuid().ToString("N"), "// New notebook cell\n");
        AttachCell(cell);

        if (SelectedCell is null)
            NotebookCells.Add(cell);
        else
            NotebookCells.Insert(Math.Max(NotebookCells.IndexOf(SelectedCell) + 1, 0), cell);

        if (_currentDocumentKind == QuantScriptDocumentKind.LegacyScript)
            _currentDocumentKind = QuantScriptDocumentKind.Notebook;

        SelectedCell = cell;
        _session.Reset();
        MarkCellsStaleFrom(0);
        MarkDirty();
        RaisePropertyChanged(nameof(CurrentDocumentKindText));
    }

    private void DeleteSelectedCell()
    {
        if (SelectedCell is null || NotebookCells.Count <= 1)
            return;

        var index = NotebookCells.IndexOf(SelectedCell);
        if (index < 0)
            return;

        DetachCell(SelectedCell);
        NotebookCells.RemoveAt(index);
        _session.Reset();
        MarkCellsStaleFrom(0);
        SelectedCell = NotebookCells[Math.Min(index, NotebookCells.Count - 1)];
        MarkDirty();
    }

    private void OpenTemplates() => ActiveResultsTab = LocalDataTabIndex;

    private void OpenRunBrowser()
    {
        if (SelectedExecutionRecord?.MirroredRunId is null)
            return;

        _navigationService.NavigateTo(
            "StrategyRuns",
            new StrategyRunsNavigationContext(
                StrategyId: QuantScriptExecutionHistoryService.BuildQuantScriptStrategyId(SelectedExecutionRecord.DocumentTitle),
                PrimaryRunId: SelectedExecutionRecord.MirroredRunId));
    }

    private void OpenRunDetail()
    {
        if (SelectedExecutionRecord?.MirroredRunId is not null)
            _navigationService.NavigateTo("RunDetail", SelectedExecutionRecord.MirroredRunId);
    }

    private void CompareInResearch()
    {
        if (SelectedExecutionRecord?.MirroredRunId is null)
            return;

        var strategyId = QuantScriptExecutionHistoryService.BuildQuantScriptStrategyId(SelectedExecutionRecord.DocumentTitle);
        var comparisonRunId = ResolveComparisonRunId(SelectedExecutionRecord);

        _navigationService.NavigateTo(
            "StrategyRuns",
            new StrategyRunsNavigationContext(
                StrategyId: strategyId,
                PrimaryRunId: SelectedExecutionRecord.MirroredRunId,
                ComparisonRunId: comparisonRunId,
                AutoCompare: !string.IsNullOrWhiteSpace(comparisonRunId)));
    }

    private string? ResolveComparisonRunId(QuantScriptExecutionRecord record)
        => RunHistory
            .FirstOrDefault(candidate =>
                !ReferenceEquals(candidate, record) &&
                candidate.HasMirroredRun &&
                string.Equals(candidate.DocumentTitle, record.DocumentTitle, StringComparison.OrdinalIgnoreCase))
            ?.MirroredRunId;

    private void UpdatePrimaryChart()
    {
        LegendEntries.Clear();
        if (Charts.Count > 0)
        {
            var request = Charts[0].Request;
            if (request.MultiSeries is { Count: > 0 } multi)
            {
                var index = 0;
                foreach (var (label, _) in multi)
                    LegendEntries.Add(new ChartLegendEntry(label, LegendPalette[index++ % LegendPalette.Length]));
            }
            else if (request.Series is { Count: > 0 })
            {
                LegendEntries.Add(new ChartLegendEntry(Charts[0].Title, LegendPalette[0]));
            }
        }

        RaisePropertyChanged(nameof(PrimaryChartRequest));
        RaisePropertyChanged(nameof(PrimaryChartTitle));
        RaisePropertyChanged(nameof(HasChart));
        RaisePropertyChanged(nameof(HasNoChart));
        RaisePropertyChanged(nameof(HasLegend));
    }

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

    private void AddOrUpdateMetric(string label, string value, string? category = null, string? source = null)
    {
        var metric = new MetricEntry(label, value, category, source);
        for (var index = 0; index < Metrics.Count; index++)
        {
            var existing = Metrics[index];
            if (!string.Equals(existing.Label, metric.Label, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(existing.Category, metric.Category, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(existing.Source, metric.Source, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Metrics[index] = metric;
            return;
        }

        Metrics.Add(metric);
    }

    private void AppendConsole(string text, ConsoleEntryKind kind) =>
        ConsoleOutput.Add(new ConsoleEntry(DateTimeOffset.Now, text, kind));

    private async void LoadDocumentAsync(ScriptDocumentEntry document)
    {
        if (_loadingDocument)
            return;

        _loadingDocument = true;
        try
        {
            var notebook = document.Kind == QuantScriptDocumentKind.Notebook
                ? await _notebookStore.LoadNotebookAsync(document.FullPath).ConfigureAwait(true)
                : await _notebookStore.ImportLegacyScriptAsync(document.FullPath).ConfigureAwait(true);

            LoadInMemoryDocument(
                Path.GetFileNameWithoutExtension(document.FullPath),
                document.FullPath,
                document.Kind,
                notebook.Cells);
            StatusText = $"Loaded {document.Name}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load QuantScript document from {Path}", document.FullPath);
            AppendConsole($"Failed to load: {ex.Message}", ConsoleEntryKind.Error);
        }
        finally
        {
            _loadingDocument = false;
        }
    }

    private void LoadInMemoryDocument(string title, string path, QuantScriptDocumentKind kind, IReadOnlyList<QuantScriptNotebookCellDocument> cells)
    {
        foreach (var cell in NotebookCells)
            DetachCell(cell);

        NotebookCells.Clear();
        ClearResults();
        _session.Reset();
        _currentDocumentPath = path;
        _currentDocumentKind = kind;
        CurrentDocumentTitle = title;
        RaisePropertyChanged(nameof(CurrentDocumentKindText));

        var effectiveCells = cells.Count > 0
            ? cells
            : [new QuantScriptNotebookCellDocument(Guid.NewGuid().ToString("N"), BuildDefaultStarterSource())];

        foreach (var cell in effectiveCells)
        {
            var viewModel = new NotebookCellViewModel(cell.Id, cell.Source, cell.Collapsed);
            AttachCell(viewModel);
            NotebookCells.Add(viewModel);
        }

        SelectedCell = NotebookCells.FirstOrDefault();
        RefreshParameters();
        MarkClean();
    }

    private void InitializeDefaultDocument()
    {
        if (NotebookCells.Count > 0)
            return;

        LoadInMemoryDocument(
            "Untitled Script",
            string.Empty,
            QuantScriptDocumentKind.LegacyScript,
            [new QuantScriptNotebookCellDocument(Guid.NewGuid().ToString("N"), BuildDefaultStarterSource())]);
    }

    private void SyncScriptSourceFromCell()
    {
        _suppressCellSync = true;
        ScriptSource = SelectedCell?.Source ?? string.Empty;
        _suppressCellSync = false;
    }

    private void RefreshParameters()
    {
        if (_loadingDocument)
            return;

        var descriptors = new List<ParameterDescriptor>();
        foreach (var cell in NotebookCells)
            descriptors.AddRange(_compiler.ExtractParameters(cell.Source));

        MergeParameterDescriptors(descriptors, preserveUserValues: true);
    }

    private void MergeParameterDescriptors(IReadOnlyList<ParameterDescriptor> descriptors, bool preserveUserValues)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orderedDescriptors = descriptors
            .Where(descriptor => !string.IsNullOrWhiteSpace(descriptor.Name) && seen.Add(descriptor.Name))
            .ToList();
        var existing = Parameters.ToDictionary(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);
        var merged = new List<ParameterViewModel>(orderedDescriptors.Count);

        foreach (var descriptor in orderedDescriptors)
        {
            var parameterType = ResolveParameterType(descriptor.TypeName);
            if (existing.TryGetValue(descriptor.Name, out var parameter))
            {
                parameter.ApplyDescriptor(descriptor, parameterType, preserveUserValues);
                merged.Add(parameter);
            }
            else
            {
                merged.Add(new ParameterViewModel(
                    descriptor.Name,
                    descriptor.DefaultValue,
                    parameterType,
                    descriptor.Label,
                    descriptor.Description,
                    descriptor.Min,
                    descriptor.Max));
            }
        }

        Parameters.Clear();
        foreach (var parameter in merged)
            Parameters.Add(parameter);
    }

    private static Type ResolveParameterType(string? typeName) => (typeName ?? string.Empty).ToLowerInvariant() switch
    {
        "int" => typeof(int),
        "double" => typeof(double),
        "decimal" => typeof(decimal),
        "bool" => typeof(bool),
        "float" => typeof(float),
        "long" => typeof(long),
        _ => typeof(string)
    };

    private void SetupFileWatcher()
    {
        if (!Directory.Exists(_options.ScriptsDirectory))
            return;

        _fileWatcher?.Dispose();
        _fileWatcher = new FileSystemWatcher(_options.ScriptsDirectory, "*.*")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };
        _fileWatcher.Created += (_, _) => System.Windows.Application.Current?.Dispatcher.InvokeAsync(RefreshScripts, DispatcherPriority.Background);
        _fileWatcher.Deleted += (_, _) => System.Windows.Application.Current?.Dispatcher.InvokeAsync(RefreshScripts, DispatcherPriority.Background);
        _fileWatcher.Renamed += (_, _) => System.Windows.Application.Current?.Dispatcher.InvokeAsync(RefreshScripts, DispatcherPriority.Background);
    }

    private bool ValidateToolbarContext()
    {
        if (FromDate.Date <= ToDate.Date)
            return true;

        StatusText = "Invalid date range: from date must be on or before to date.";
        Diagnostics.Add(new DiagnosticEntry("Validation", StatusText));
        ActiveResultsTab = 2;
        return false;
    }

    private IReadOnlyDictionary<string, object?> BuildParameterDictionary()
    {
        var parameters = Parameters.ToDictionary(
            parameter => parameter.Name,
            parameter => parameter.ParsedValue,
            StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(AssetSymbol))
            parameters["symbol"] = AssetSymbol.Trim().ToUpperInvariant();

        parameters["from"] = DateOnly.FromDateTime(FromDate);
        parameters["to"] = DateOnly.FromDateTime(ToDate);
        parameters["interval"] = NormalizeInterval(SelectedInterval);
        return parameters;
    }

    private static string NormalizeInterval(string? interval)
    {
        var trimmed = (interval ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return "daily";

        var tokenEnd = trimmed.IndexOfAny([' ', '(']);
        var token = tokenEnd > 0 ? trimmed[..tokenEnd] : trimmed;
        return token.ToLowerInvariant();
    }

    private Dictionary<string, string> BuildResolvedParameterSnapshot(IReadOnlyList<ParameterDescriptor> runtimeParameters)
    {
        var runtimeDefaults = runtimeParameters.ToDictionary(
            descriptor => descriptor.Name,
            descriptor => descriptor.DefaultValue,
            StringComparer.OrdinalIgnoreCase);
        var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in Parameters)
        {
            object? resolvedValue = parameter.IsValid
                ? parameter.ParsedValue
                : runtimeDefaults.TryGetValue(parameter.Name, out var defaultValue)
                    ? defaultValue
                    : parameter.RawValue;

            snapshot[parameter.Name] = QuantScriptExecutionHistoryService.ConvertValueToString(resolvedValue);
        }

        return snapshot;
    }

    private List<QuantScriptResolvedParameterDescriptorRecord> BuildRuntimeParameterRecords(IReadOnlyList<ParameterDescriptor> runtimeParameters)
    {
        if (runtimeParameters.Count == 0)
        {
            return Parameters
                .Select(static parameter => new QuantScriptResolvedParameterDescriptorRecord(
                    parameter.Name,
                    parameter.ParameterType.Name,
                    parameter.Label,
                    DefaultValue: null,
                    ResolvedValue: parameter.RawValue,
                    parameter.Min,
                    parameter.Max,
                    parameter.Description))
                .ToList();
        }

        var resolvedValues = BuildResolvedParameterSnapshot(runtimeParameters);
        return runtimeParameters
            .Select(descriptor => new QuantScriptResolvedParameterDescriptorRecord(
                descriptor.Name,
                descriptor.TypeName,
                descriptor.Label,
                QuantScriptExecutionHistoryService.ConvertValueToString(descriptor.DefaultValue),
                resolvedValues.TryGetValue(descriptor.Name, out var resolvedValue) ? resolvedValue : null,
                descriptor.Min,
                descriptor.Max,
                descriptor.Description))
            .ToList();
    }

    private string BuildConsoleExcerpt()
        => string.Join(
            Environment.NewLine,
            ConsoleOutput
                .TakeLast(80)
                .Select(static entry => entry.Text));

    private async Task PersistExecutionHistoryAsync(
        bool success,
        IReadOnlyList<ParameterDescriptor> runtimeParameters,
        IReadOnlyList<BacktestResult> capturedBacktests,
        CancellationToken ct)
    {
        try
        {
            var record = await _executionHistoryService.RecordExecutionAsync(
                new QuantScriptExecutionRecordRequest(
                    DocumentTitle: CurrentDocumentTitle,
                    DocumentPath: _currentDocumentPath,
                    DocumentKind: ResolveSaveKind(),
                    Success: success,
                    ParameterSnapshot: BuildResolvedParameterSnapshot(runtimeParameters),
                    RuntimeParameters: BuildRuntimeParameterRecords(runtimeParameters),
                    ConsoleExcerpt: BuildConsoleExcerpt(),
                    Metrics: Metrics.Select(static metric => new QuantScriptExecutionMetricRecord(metric.Label, metric.Value, metric.Category)).ToList(),
                    PlotTitles: Charts.Select(static plot => plot.Title).ToList(),
                    CapturedBacktests: capturedBacktests),
                ct).ConfigureAwait(true);

            RunHistory.Insert(0, record);
            SelectedExecutionRecord = record;

            if (!string.IsNullOrWhiteSpace(record.Warning))
            {
                AppendConsole(record.Warning, ConsoleEntryKind.Warning);
                ActiveResultsTab = RunHistoryTabIndex;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist QuantScript execution history");
            AppendConsole($"Failed to record run history: {ex.Message}", ConsoleEntryKind.Warning);
        }
    }

    private List<NotebookCellExecutionIdentity> GetCellIdentities() =>
        NotebookCells.Select(cell => new NotebookCellExecutionIdentity(cell.Id, cell.Revision)).ToList();

    private void UpdateCellOrdinals()
    {
        for (var index = 0; index < NotebookCells.Count; index++)
            NotebookCells[index].Ordinal = index + 1;
    }

    private void AttachCell(NotebookCellViewModel cell) => cell.PropertyChanged += OnCellPropertyChanged;
    private void DetachCell(NotebookCellViewModel cell) => cell.PropertyChanged -= OnCellPropertyChanged;

    private void OnCellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_loadingDocument || sender is not NotebookCellViewModel cell || e.PropertyName != nameof(NotebookCellViewModel.Source))
            return;

        var index = NotebookCells.IndexOf(cell);
        if (index < 0)
            return;

        _session.InvalidateFrom(GetCellIdentities(), index);
        MarkCellsStaleFrom(index);

        if (ReferenceEquals(cell, SelectedCell))
            SyncScriptSourceFromCell();

        MarkDirty();
    }

    private void MarkCellsStaleFrom(int startIndex)
    {
        if (startIndex < 0 || startIndex >= NotebookCells.Count)
            return;

        for (var index = startIndex; index < NotebookCells.Count; index++)
        {
            NotebookCells[index].State = NotebookCellExecutionState.Stale;
            NotebookCells[index].StatusText = "Stale";
        }
    }

    private void AdvanceSelectionAfterRun()
    {
        if (SelectedCell is null)
            return;

        var index = NotebookCells.IndexOf(SelectedCell);
        if (index < 0)
            return;

        if (index < NotebookCells.Count - 1)
            SelectedCell = NotebookCells[index + 1];
        else
            AddCell();
    }

    private void MarkDirty() => IsDirty = true;
    private void MarkClean() => IsDirty = false;

    private QuantScriptDocumentKind ResolveSaveKind() =>
        _currentDocumentKind == QuantScriptDocumentKind.Notebook || NotebookCells.Count > 1
            ? QuantScriptDocumentKind.Notebook
            : QuantScriptDocumentKind.LegacyScript;

    private QuantScriptNotebookDocument BuildNotebookDocument() => new()
    {
        Title = CurrentDocumentTitle,
        Cells = NotebookCells.Select(cell => new QuantScriptNotebookCellDocument(cell.Id, cell.Source, cell.Collapsed)).ToList()
    };

    private static string BuildDefaultStarterSource() =>
        """
        var symbol = Param<string>("symbol", "SPY");
        var from = new DateOnly(2024, 1, 2);
        var to = new DateOnly(2024, 3, 29);

        var prices = await Data.PricesAsync(symbol, from, to);
        if (prices.Count == 0)
        {
            Print($"No bars found for {symbol}.");
            return;
        }

        var lastBar = prices.Bars[^1];
        Print($"Loaded {prices.Count} bars for {symbol}.");
        PrintMetric("Last Close", lastBar.Close.ToString("F2"), "Price");
        """;

    private void NotifyCommandStateChanged()
    {
        RunScriptCommand.NotifyCanExecuteChanged();
        RunAllCommand.NotifyCanExecuteChanged();
        RunAndAdvanceCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        NewScriptCommand.NotifyCanExecuteChanged();
        NewNotebookCommand.NotifyCanExecuteChanged();
        SaveScriptCommand.NotifyCanExecuteChanged();
        RefreshScriptsCommand.NotifyCanExecuteChanged();
        AddCellCommand.NotifyCanExecuteChanged();
        DeleteCellCommand.NotifyCanExecuteChanged();
        TemplatesCommand.NotifyCanExecuteChanged();
    }

    private void NotifyHistoryCommandStateChanged()
    {
        RaisePropertyChanged(nameof(CanOpenSelectedHistoryRun));
        RaisePropertyChanged(nameof(CanCompareSelectedHistoryRun));
        RaisePropertyChanged(nameof(SelectedHistoryTitle));
        RaisePropertyChanged(nameof(SelectedHistoryDetail));
        RaisePropertyChanged(nameof(SelectedHistoryEvidenceText));
        RaisePropertyChanged(nameof(SelectedHistoryRunLinkText));
        RaisePropertyChanged(nameof(SelectedHistoryParameterText));
        RaisePropertyChanged(nameof(SelectedHistoryConsolePreview));
        OpenRunBrowserCommand.NotifyCanExecuteChanged();
        OpenRunDetailCommand.NotifyCanExecuteChanged();
        CompareInResearchCommand.NotifyCanExecuteChanged();
    }

    private static string FormatCount(int count, string noun) =>
        $"{count} {noun}{(count == 1 ? string.Empty : "s")}";
}
