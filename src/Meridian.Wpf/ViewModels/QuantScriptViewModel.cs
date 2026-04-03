using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Meridian.QuantScript;
using Meridian.QuantScript.Compilation;
using Meridian.QuantScript.Documents;
using Meridian.QuantScript.Plotting;
using Meridian.Ui.Services.Collections;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for Meridian's notebook-style QuantScript workspace.
/// </summary>
public sealed class QuantScriptViewModel : BindableBase, IDisposable
{
    private static readonly System.Windows.Media.Color[] LegendPalette =
    [
        System.Windows.Media.Color.FromRgb(66, 153, 225),
        System.Windows.Media.Color.FromRgb(159, 122, 234),
        System.Windows.Media.Color.FromRgb(56, 178, 172),
        System.Windows.Media.Color.FromRgb(72, 187, 120),
        System.Windows.Media.Color.FromRgb(245, 101, 101)
    ];

    private readonly IScriptRunner _runner;
    private readonly IQuantScriptCompiler _compiler;
    private readonly NotebookExecutionSession _executionSession;
    private readonly IQuantScriptNotebookStore _notebookStore;
    private readonly IQuantScriptLayoutService _layoutService;
    private readonly QuantScriptOptions _options;
    private readonly ILogger<QuantScriptViewModel> _logger;

    private DispatcherTimer? _elapsedTimer;
    private System.Diagnostics.Stopwatch? _runStopwatch;
    private FileSystemWatcher? _fileWatcher;
    private string? _currentDocumentPath;
    private bool _isLegacyImport;
    private bool _disposed;
    private bool _isLoadingDocument;

    private QuantScriptDocumentEntry? _selectedDocument;
    private QuantScriptCellViewModel? _selectedCell;
    private string? _selectedCellId;
    private bool _isRunning;
    private double _progressFraction;
    private string _statusText = "Ready";
    private string _elapsedText = "--";
    private string _memoryText = "--";
    private int _activeResultsTab;
    private string _notebookTitle = "QuantScript Notebook";
    private string? _documentWarningText;

    public QuantScriptViewModel(
        IScriptRunner runner,
        IQuantScriptCompiler compiler,
        NotebookExecutionSession executionSession,
        IQuantScriptNotebookStore notebookStore,
        IQuantScriptLayoutService layoutService,
        IOptions<QuantScriptOptions> options,
        ILogger<QuantScriptViewModel> logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _executionSession = executionSession ?? throw new ArgumentNullException(nameof(executionSession));
        _notebookStore = notebookStore ?? throw new ArgumentNullException(nameof(notebookStore));
        _layoutService = layoutService ?? throw new ArgumentNullException(nameof(layoutService));
        _options = options?.Value ?? new QuantScriptOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Documents = [];
        Cells = [];
        Parameters = [];
        ConsoleOutput = new BoundedObservableCollection<ConsoleEntry>(10_000);
        Charts = [];
        Metrics = [];
        Trades = [];
        Diagnostics = [];
        LegendEntries = [];

        RunAllCommand = new AsyncRelayCommand(RunAllAsync, () => CanRun);
        RunCellCommand = new AsyncRelayCommand<QuantScriptCellViewModel?>(
            (cell, ct) => RunCellAsync(cell, false, ct),
            cell => CanRun && cell is not null);
        RunCellAndAdvanceCommand = new AsyncRelayCommand<QuantScriptCellViewModel?>(
            (cell, ct) => RunCellAsync(cell, true, ct),
            cell => CanRun && cell is not null);
        StopCommand = new RelayCommand(StopExecution, () => IsRunning);
        NewNotebookCommand = new RelayCommand(CreateNewNotebook);
        SaveNotebookCommand = new AsyncRelayCommand(SaveNotebookAsync);
        RefreshDocumentsCommand = new RelayCommand(RefreshDocuments);
        ClearConsoleCommand = new RelayCommand(() => ConsoleOutput.Clear());
        AddCellBelowCommand = new RelayCommand<QuantScriptCellViewModel?>(AddCellBelow);
        DeleteCellCommand = new RelayCommand<QuantScriptCellViewModel?>(DeleteCell, cell => cell is not null && Cells.Count > 1);
        SelectCellCommand = new RelayCommand<QuantScriptCellViewModel?>(SelectCell);

        ConsoleOutput.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(ConsoleTabHeader));
        Charts.CollectionChanged += (_, _) =>
        {
            RaisePropertyChanged(nameof(ChartsTabHeader));
            UpdatePrimaryChart();
        };
        Metrics.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(MetricsTabHeader));
        Trades.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(TradesTabHeader));
        Diagnostics.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(DiagnosticsTabHeader));
        Cells.CollectionChanged += (_, _) =>
        {
            UpdateDocumentWarning();
            DeleteCellCommand.NotifyCanExecuteChanged();
        };
    }

    public ObservableCollection<QuantScriptDocumentEntry> Documents { get; }

    public ObservableCollection<QuantScriptCellViewModel> Cells { get; }

    public ObservableCollection<ParameterViewModel> Parameters { get; }

    public BoundedObservableCollection<ConsoleEntry> ConsoleOutput { get; }

    public ObservableCollection<PlotViewModel> Charts { get; }

    public ObservableCollection<MetricEntry> Metrics { get; }

    public ObservableCollection<TradeEntry> Trades { get; }

    public ObservableCollection<DiagnosticEntry> Diagnostics { get; }

    public ObservableCollection<ChartLegendEntry> LegendEntries { get; }

    public QuantScriptDocumentEntry? SelectedDocument
    {
        get => _selectedDocument;
        set
        {
            if (!SetProperty(ref _selectedDocument, value) || _isLoadingDocument || value is null)
                return;

            _ = OpenDocumentAsync(value);
        }
    }

    public QuantScriptCellViewModel? SelectedCell
    {
        get => _selectedCell;
        private set
        {
            if (!SetProperty(ref _selectedCell, value))
                return;

            SelectedCellId = value?.CellId;
        }
    }

    public string? SelectedCellId
    {
        get => _selectedCellId;
        private set => SetProperty(ref _selectedCellId, value);
    }

    public string NotebookTitle
    {
        get => _notebookTitle;
        set => SetProperty(ref _notebookTitle, value);
    }

    public string? DocumentWarningText
    {
        get => _documentWarningText;
        private set => SetProperty(ref _documentWarningText, value);
    }

    public PlotRequest? PrimaryChartRequest => Charts.Count > 0 ? Charts[0].Request : null;

    public string PrimaryChartTitle => Charts.Count > 0 ? Charts[0].Title : string.Empty;

    public bool HasChart => Charts.Count > 0;

    public bool HasNoChart => Charts.Count == 0;

    public bool HasLegend => LegendEntries.Count > 0;

    public string ConsoleTabHeader => ConsoleOutput.Count > 0 ? $"Console ({ConsoleOutput.Count})" : "Console";

    public string ChartsTabHeader => Charts.Count > 0 ? $"Charts ({Charts.Count})" : "Charts";

    public string MetricsTabHeader => Metrics.Count > 0 ? $"Metrics ({Metrics.Count})" : "Metrics";

    public string TradesTabHeader => Trades.Count > 0 ? $"Trades ({Trades.Count})" : "Trades";

    public string DiagnosticsTabHeader => Diagnostics.Count > 0 ? $"Diagnostics ({Diagnostics.Count})" : "Diagnostics";

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!SetProperty(ref _isRunning, value))
                return;

            RaisePropertyChanged(nameof(CanRun));
            RunAllCommand.NotifyCanExecuteChanged();
            RunCellCommand.NotifyCanExecuteChanged();
            RunCellAndAdvanceCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
        }
    }

    public double ProgressFraction
    {
        get => _progressFraction;
        private set => SetProperty(ref _progressFraction, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ElapsedText
    {
        get => _elapsedText;
        private set => SetProperty(ref _elapsedText, value);
    }

    public string MemoryText
    {
        get => _memoryText;
        private set => SetProperty(ref _memoryText, value);
    }

    public int ActiveResultsTab
    {
        get => _activeResultsTab;
        set => SetProperty(ref _activeResultsTab, value);
    }

    public bool CanRun => !IsRunning;

    public IAsyncRelayCommand RunAllCommand { get; }

    public IAsyncRelayCommand<QuantScriptCellViewModel?> RunCellCommand { get; }

    public IAsyncRelayCommand<QuantScriptCellViewModel?> RunCellAndAdvanceCommand { get; }

    public IRelayCommand StopCommand { get; }

    public IRelayCommand NewNotebookCommand { get; }

    public IAsyncRelayCommand SaveNotebookCommand { get; }

    public IRelayCommand RefreshDocumentsCommand { get; }

    public IRelayCommand ClearConsoleCommand { get; }

    public IRelayCommand<QuantScriptCellViewModel?> AddCellBelowCommand { get; }

    public IRelayCommand<QuantScriptCellViewModel?> DeleteCellCommand { get; }

    public IRelayCommand<QuantScriptCellViewModel?> SelectCellCommand { get; }

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
        RefreshDocuments();

        if (Cells.Count == 0)
            CreateNewNotebook();

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
        StopExecution();
    }

    private async Task RunAllAsync(CancellationToken ct)
    {
        if (Cells.Count == 0)
            return;

        _executionSession.Reset();
        foreach (var cell in Cells)
        {
            if (cell.Status != QuantScriptCellStatus.Running)
                cell.Status = QuantScriptCellStatus.Stale;
        }

        await ExecuteCellsAsync(0, Cells.Count - 1, advanceSelection: false, ct).ConfigureAwait(false);
    }

    private Task RunCellAsync(QuantScriptCellViewModel? cell, bool advanceSelection, CancellationToken ct)
    {
        if (cell is null)
            return Task.CompletedTask;

        var index = Cells.IndexOf(cell);
        if (index < 0)
            return Task.CompletedTask;

        return ExecuteCellsAsync(
            _executionSession.GetReplayStartIndex(BuildExecutionIdentities(), index),
            index,
            advanceSelection,
            ct);
    }

    private async Task ExecuteCellsAsync(
        int startIndex,
        int targetIndex,
        bool advanceSelection,
        CancellationToken externalCt)
    {
        if (startIndex < 0 || targetIndex < 0 || startIndex >= Cells.Count || targetIndex >= Cells.Count)
            return;

        IsRunning = true;
        StatusText = startIndex == 0 && targetIndex == Cells.Count - 1 ? "Running notebook…" : "Running cell…";
        ProgressFraction = 0;
        _runStopwatch = System.Diagnostics.Stopwatch.StartNew();
        _elapsedTimer?.Start();

        try
        {
            var parameters = BuildParameterDictionary();
            var identities = BuildExecutionIdentities();
            var total = Math.Max(1, targetIndex - startIndex + 1);

            for (var index = startIndex; index <= targetIndex; index++)
            {
                externalCt.ThrowIfCancellationRequested();

                var cell = Cells[index];
                SelectCell(cell);
                cell.Status = QuantScriptCellStatus.Running;
                cell.RuntimeError = null;

                var previousCheckpoint = _executionSession.GetPreviousCheckpoint(identities, index);
                var result = await _runner
                    .RunAsync(cell.SourceCode, parameters, previousCheckpoint, externalCt)
                    .ConfigureAwait(false);

                ApplyCellResult(cell, result);

                if (result.Success && result.Checkpoint is not null)
                {
                    _executionSession.RecordSuccessfulRun(identities[index], result.Checkpoint);
                }
                else
                {
                    _executionSession.RecordFailedRun(identities, index);
                    MarkCellsStale(index + 1);
                    break;
                }

                ProgressFraction = (index - startIndex + 1d) / total;
            }

            RebuildAggregateResults();
            if (advanceSelection && targetIndex < Cells.Count)
                AdvanceSelectionFrom(targetIndex);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled";
            AppendConsole("Notebook execution was cancelled.", ConsoleEntryKind.Warning);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error during QuantScript notebook execution");
            StatusText = "Error";
            AppendConsole($"Error: {ex.Message}", ConsoleEntryKind.Error);
        }
        finally
        {
            _runStopwatch?.Stop();
            _elapsedTimer?.Stop();
            IsRunning = false;
            ProgressFraction = 1;
        }
    }

    private void ApplyCellResult(QuantScriptCellViewModel cell, ScriptRunResult result)
    {
        cell.OutputPlots.Clear();
        cell.OutputMetrics.Clear();

        foreach (var metric in result.Metrics)
            cell.OutputMetrics.Add(new MetricEntry(metric.Key, metric.Value));

        foreach (var plot in result.Plots)
            cell.OutputPlots.Add(new CellPlotViewModel(plot.Title, plot));

        cell.OutputText = result.ConsoleOutput;
        cell.ElapsedTime = result.Elapsed;

        if (result.CompilationErrors.Count > 0)
        {
            cell.RuntimeError = string.Join(
                Environment.NewLine,
                result.CompilationErrors.Select(diagnostic => $"[{diagnostic.Line}:{diagnostic.Column}] {diagnostic.Message}"));
            cell.Status = QuantScriptCellStatus.Error;
        }
        else if (!string.IsNullOrWhiteSpace(result.RuntimeError))
        {
            cell.RuntimeError = result.RuntimeError;
            cell.Status = QuantScriptCellStatus.Error;
        }
        else
        {
            cell.RuntimeError = null;
            cell.Status = QuantScriptCellStatus.Success;
        }
    }

    private void UpdatePrimaryChart()
    {
        LegendEntries.Clear();

        if (Charts.Count > 0)
        {
            var request = Charts[0].Request;
            if (request.MultiSeries is { Count: > 0 } multiSeries)
            {
                var index = 0;
                foreach (var (label, _) in multiSeries)
                {
                    LegendEntries.Add(new ChartLegendEntry(label, LegendPalette[index % LegendPalette.Length]));
                    index++;
                }
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

    private void RebuildAggregateResults()
    {
        ConsoleOutput.Clear();
        Charts.Clear();
        Metrics.Clear();
        Trades.Clear();
        Diagnostics.Clear();

        var frontier = _executionSession.GetValidFrontierIndex(BuildExecutionIdentities());

        for (var index = 0; index <= frontier; index++)
        {
            var cell = Cells[index];

            if (!string.IsNullOrWhiteSpace(cell.OutputText))
            {
                foreach (var line in cell.OutputText.Split(Environment.NewLine))
                    AppendConsole($"[{index + 1}] {line}", ConsoleEntryKind.Output);
            }

            foreach (var plot in cell.OutputPlots)
                Charts.Add(new PlotViewModel(plot.Title, plot.Request));

            foreach (var metric in cell.OutputMetrics)
                Metrics.Add(metric);

            Diagnostics.Add(new DiagnosticEntry(
                $"Cell {index + 1}",
                $"{cell.StatusText} ({cell.ElapsedTime.TotalMilliseconds:F0} ms)"));
        }

        Diagnostics.Add(new DiagnosticEntry("Valid frontier", frontier >= 0 ? $"Cell {frontier + 1}" : "None"));
        Diagnostics.Add(new DiagnosticEntry("Notebook title", NotebookTitle));
        ElapsedText = _runStopwatch is { } stopwatch ? $"{stopwatch.Elapsed.TotalSeconds:F1}s" : "--";
        MemoryText = $"{GC.GetTotalMemory(false) / 1024.0:F0} KB";

        StatusText = frontier == Cells.Count - 1
            ? $"Notebook ready ({Cells.Count} cells)"
            : frontier >= 0
                ? $"Ready through cell {frontier + 1}"
                : "Ready";

        if (Charts.Count > 0)
            ActiveResultsTab = 1;
        else if (Metrics.Count > 0)
            ActiveResultsTab = 2;
        else
            ActiveResultsTab = 0;
    }

    private void CreateNewNotebook()
    {
        _currentDocumentPath = null;
        _isLegacyImport = false;
        NotebookTitle = "QuantScript Notebook";
        LoadNotebookDocument(new QuantScriptNotebookDocument());
        StatusText = "New notebook";
    }

    private async Task SaveNotebookAsync(CancellationToken ct)
    {
        var document = new QuantScriptNotebookDocument
        {
            Title = NotebookTitle,
            Cells = Cells.Select(cell => new QuantScriptNotebookCellDocument(cell.CellId, cell.SourceCode)).ToList()
        };

        var targetPath = _currentDocumentPath;
        if (string.IsNullOrWhiteSpace(targetPath) || _isLegacyImport)
            targetPath = _notebookStore.GetSuggestedNotebookPath(NotebookTitle);

        await _notebookStore.SaveNotebookAsync(targetPath, document, ct).ConfigureAwait(false);

        _currentDocumentPath = targetPath;
        _isLegacyImport = false;
        StatusText = $"Saved {Path.GetFileName(targetPath)}";

        await RunOnUiThreadAsync(() =>
        {
            RefreshDocuments();
            SelectedDocument = Documents.FirstOrDefault(doc =>
                string.Equals(doc.FullPath, targetPath, StringComparison.OrdinalIgnoreCase));
        }).ConfigureAwait(false);
    }

    private void RefreshDocuments()
    {
        Documents.Clear();
        foreach (var document in _notebookStore.ListDocuments())
            Documents.Add(new QuantScriptDocumentEntry(document.Name, document.FullPath, document.Kind));
    }

    private async Task OpenDocumentAsync(QuantScriptDocumentEntry document)
    {
        try
        {
            _isLoadingDocument = true;

            QuantScriptNotebookDocument notebook;
            if (document.Kind == QuantScriptDocumentKind.LegacyScript)
            {
                notebook = await _notebookStore.ImportLegacyScriptAsync(document.FullPath).ConfigureAwait(false);
                _currentDocumentPath = null;
                _isLegacyImport = true;
            }
            else
            {
                notebook = await _notebookStore.LoadNotebookAsync(document.FullPath).ConfigureAwait(false);
                _currentDocumentPath = document.FullPath;
                _isLegacyImport = false;
            }

            await RunOnUiThreadAsync(() =>
            {
                NotebookTitle = notebook.Title;
                LoadNotebookDocument(notebook);
                StatusText = document.Kind == QuantScriptDocumentKind.LegacyScript
                    ? $"Imported {document.Name}"
                    : $"Loaded {document.Name}";
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open QuantScript document {Path}", document.FullPath);
            await RunOnUiThreadAsync(() =>
            {
                StatusText = "Open failed";
                AppendConsole($"Failed to open {document.Name}: {ex.Message}", ConsoleEntryKind.Error);
            }).ConfigureAwait(false);
        }
        finally
        {
            _isLoadingDocument = false;
        }
    }

    private void LoadNotebookDocument(QuantScriptNotebookDocument notebook)
    {
        Cells.Clear();
        _executionSession.Reset();
        ConsoleOutput.Clear();
        Charts.Clear();
        Metrics.Clear();
        Trades.Clear();
        Diagnostics.Clear();

        foreach (var cell in notebook.Cells)
            Cells.Add(new QuantScriptCellViewModel(cell.Id, cell.Source, HandleCellEdited));

        if (Cells.Count == 0)
            Cells.Add(CreateEmptyCell());

        UpdateParameters();
        UpdateDocumentWarning();
        SelectCell(Cells[0]);
        RebuildAggregateResults();
    }

    private void HandleCellEdited(QuantScriptCellViewModel editedCell)
    {
        if (_isLoadingDocument)
            return;

        var index = Cells.IndexOf(editedCell);
        if (index < 0)
            return;

        _executionSession.InvalidateFrom(BuildExecutionIdentities(), index);
        MarkCellsStale(index);
        UpdateParameters();
        RebuildAggregateResults();
    }

    private void MarkCellsStale(int startIndex)
    {
        if (startIndex < 0)
            startIndex = 0;

        for (var index = startIndex; index < Cells.Count; index++)
        {
            if (Cells[index].Status != QuantScriptCellStatus.Running)
                Cells[index].Status = QuantScriptCellStatus.Stale;
        }
    }

    private IReadOnlyDictionary<string, object?> BuildParameterDictionary()
        => Parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.ParsedValue, StringComparer.OrdinalIgnoreCase);

    private void UpdateParameters()
    {
        Parameters.Clear();
        var source = string.Join(Environment.NewLine + Environment.NewLine, Cells.Select(cell => cell.SourceCode));
        foreach (var descriptor in _compiler.ExtractParameters(source))
        {
            var parameterType = ResolveParameterType(descriptor.TypeName);
            Parameters.Add(new ParameterViewModel(descriptor.Name, descriptor.DefaultValue, parameterType));
        }
    }

    private static Type ResolveParameterType(string typeName)
    {
        var normalized = typeName.Trim().ToLowerInvariant();
        return normalized switch
        {
            "int" => typeof(int),
            "long" => typeof(long),
            "double" => typeof(double),
            "float" => typeof(float),
            "decimal" => typeof(decimal),
            "bool" => typeof(bool),
            _ => typeof(string)
        };
    }

    private List<NotebookCellExecutionIdentity> BuildExecutionIdentities()
        => Cells.Select(cell => new NotebookCellExecutionIdentity(cell.CellId, cell.Revision)).ToList();

    private void AddCellBelow(QuantScriptCellViewModel? currentCell)
    {
        var insertIndex = currentCell is null ? Cells.Count : Cells.IndexOf(currentCell) + 1;
        if (insertIndex < 0)
            insertIndex = Cells.Count;

        var cell = CreateEmptyCell();
        Cells.Insert(insertIndex, cell);
        _executionSession.InvalidateFrom(BuildExecutionIdentities(), insertIndex);
        MarkCellsStale(insertIndex + 1);
        UpdateParameters();
        SelectCell(cell);
        RebuildAggregateResults();
    }

    private void DeleteCell(QuantScriptCellViewModel? currentCell)
    {
        if (currentCell is null || Cells.Count <= 1)
            return;

        var index = Cells.IndexOf(currentCell);
        if (index < 0)
            return;

        Cells.RemoveAt(index);
        _executionSession.InvalidateFrom(BuildExecutionIdentities(), index);
        MarkCellsStale(index);
        UpdateParameters();

        var nextIndex = Math.Min(index, Cells.Count - 1);
        if (nextIndex >= 0)
            SelectCell(Cells[nextIndex]);

        RebuildAggregateResults();
    }

    private void SelectCell(QuantScriptCellViewModel? cell)
    {
        foreach (var existing in Cells)
            existing.IsSelected = ReferenceEquals(existing, cell);

        SelectedCell = cell;
    }

    private void AdvanceSelectionFrom(int index)
    {
        if (index < 0)
            return;

        if (index == Cells.Count - 1)
        {
            var newCell = CreateEmptyCell();
            Cells.Add(newCell);
            SelectCell(newCell);
            return;
        }

        SelectCell(Cells[index + 1]);
    }

    private QuantScriptCellViewModel CreateEmptyCell()
        => new(Guid.NewGuid().ToString("N"), string.Empty, HandleCellEdited);

    private void StopExecution()
    {
        RunAllCommand.Cancel();
        RunCellCommand.Cancel();
        RunCellAndAdvanceCommand.Cancel();
    }

    private void AppendConsole(string text, ConsoleEntryKind kind)
        => ConsoleOutput.Add(new ConsoleEntry(DateTimeOffset.Now, text, kind));

    private void UpdateDocumentWarning()
    {
        DocumentWarningText = Cells.Count > _options.NotebookCellWarningThreshold
            ? $"Notebook has {Cells.Count} cells; performance may degrade without virtualization."
            : null;
    }

    private void SetupFileWatcher()
    {
        Directory.CreateDirectory(_notebookStore.ScriptsDirectory);

        _fileWatcher?.Dispose();
        _fileWatcher = new FileSystemWatcher(_notebookStore.ScriptsDirectory)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            Filter = "*.*",
            EnableRaisingEvents = true
        };

        FileSystemEventHandler refreshHandler = (_, _) =>
        {
            if (System.Windows.Application.Current?.Dispatcher is { } dispatcher)
                dispatcher.InvokeAsync(RefreshDocuments, DispatcherPriority.Background);
            else
                RefreshDocuments();
        };

        RenamedEventHandler renameHandler = (_, _) =>
        {
            if (System.Windows.Application.Current?.Dispatcher is { } dispatcher)
                dispatcher.InvokeAsync(RefreshDocuments, DispatcherPriority.Background);
            else
                RefreshDocuments();
        };

        _fileWatcher.Created += refreshHandler;
        _fileWatcher.Deleted += refreshHandler;
        _fileWatcher.Changed += refreshHandler;
        _fileWatcher.Renamed += renameHandler;
    }

    private static Task RunOnUiThreadAsync(Action action)
    {
        if (System.Windows.Application.Current?.Dispatcher is not { } dispatcher || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action, DispatcherPriority.Normal).Task;
    }
}
