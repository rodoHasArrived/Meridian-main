using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Lean Integration page.
/// Manages Lean Engine configuration, data synchronization, backtest execution,
/// auto-export, symbol mapping, and results ingestion.
/// All state, business logic, and timer management live here; code-behind is lifecycle wiring only.
/// </summary>
public sealed class LeanIntegrationViewModel : BindableBase, IDisposable
{
    private readonly LeanIntegrationService _leanService;
    private readonly DispatcherTimer _backtestPollTimer;
    private readonly CancellationTokenSource _cts = new();
    private bool _isDisposed;

    // Cached brushes (resolved once at construction so FindResource is never called in update paths).
    private readonly Brush _successBrush;
    private readonly Brush _errorBrush;
    private readonly Brush _infoBrush;

    private string? _currentBacktestId;

    // ── Status ───────────────────────────────────────────────────────────────────

    private Brush _statusIndicatorBrush;
    public Brush StatusIndicatorBrush { get => _statusIndicatorBrush; private set => SetProperty(ref _statusIndicatorBrush, value); }

    private string _statusText = "Not Configured";
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    private string _statusDetailsText = "Last sync: Never | Symbols synced: 0";
    public string StatusDetailsText { get => _statusDetailsText; private set => SetProperty(ref _statusDetailsText, value); }

    // ── Configuration ────────────────────────────────────────────────────────────

    private string _leanPath = string.Empty;
    public string LeanPath
    {
        get => _leanPath;
        set => SetProperty(ref _leanPath, value);
    }

    private string _dataPath = string.Empty;
    public string DataPath
    {
        get => _dataPath;
        set => SetProperty(ref _dataPath, value);
    }

    private bool _autoSync;
    public bool AutoSync
    {
        get => _autoSync;
        set => SetProperty(ref _autoSync, value);
    }

    // ── Data Sync ────────────────────────────────────────────────────────────────

    private string _syncSymbols = string.Empty;
    public string SyncSymbols
    {
        get => _syncSymbols;
        set => SetProperty(ref _syncSymbols, value);
    }

    private DateTime? _syncFromDate;
    public DateTime? SyncFromDate
    {
        get => _syncFromDate;
        set => SetProperty(ref _syncFromDate, value);
    }

    private DateTime? _syncToDate;
    public DateTime? SyncToDate
    {
        get => _syncToDate;
        set => SetProperty(ref _syncToDate, value);
    }

    private string _selectedResolution = "Minute";
    public string SelectedResolution
    {
        get => _selectedResolution;
        set => SetProperty(ref _selectedResolution, value);
    }

    private bool _overwriteSync;
    public bool OverwriteSync
    {
        get => _overwriteSync;
        set => SetProperty(ref _overwriteSync, value);
    }

    private bool _isSyncEnabled = true;
    public bool IsSyncEnabled { get => _isSyncEnabled; private set => SetProperty(ref _isSyncEnabled, value); }

    private string _syncStatusText = string.Empty;
    public string SyncStatusText { get => _syncStatusText; private set => SetProperty(ref _syncStatusText, value); }

    // ── Backtest ─────────────────────────────────────────────────────────────────

    public ObservableCollection<AlgorithmInfo> Algorithms { get; } = new();

    private AlgorithmInfo? _selectedAlgorithm;
    public AlgorithmInfo? SelectedAlgorithm
    {
        get => _selectedAlgorithm;
        set => SetProperty(ref _selectedAlgorithm, value);
    }

    private DateTime? _backtestStartDate;
    public DateTime? BacktestStartDate
    {
        get => _backtestStartDate;
        set => SetProperty(ref _backtestStartDate, value);
    }

    private DateTime? _backtestEndDate;
    public DateTime? BacktestEndDate
    {
        get => _backtestEndDate;
        set => SetProperty(ref _backtestEndDate, value);
    }

    private string _initialCapitalText = "100000";
    public string InitialCapitalText
    {
        get => _initialCapitalText;
        set => SetProperty(ref _initialCapitalText, value);
    }

    private bool _isBacktestEnabled = true;
    public bool IsBacktestEnabled { get => _isBacktestEnabled; private set => SetProperty(ref _isBacktestEnabled, value); }

    private bool _isStopButtonVisible;
    public bool IsStopButtonVisible { get => _isStopButtonVisible; private set => SetProperty(ref _isStopButtonVisible, value); }

    private bool _isBacktestProgressVisible;
    public bool IsBacktestProgressVisible { get => _isBacktestProgressVisible; private set => SetProperty(ref _isBacktestProgressVisible, value); }

    private double _backtestProgress;
    public double BacktestProgress { get => _backtestProgress; private set => SetProperty(ref _backtestProgress, value); }

    private string _backtestProgressText = "Processing...";
    public string BacktestProgressText { get => _backtestProgressText; private set => SetProperty(ref _backtestProgressText, value); }

    private string _backtestProgressPercent = "0%";
    public string BacktestProgressPercent { get => _backtestProgressPercent; private set => SetProperty(ref _backtestProgressPercent, value); }

    // ── Backtest Results ─────────────────────────────────────────────────────────

    private bool _isResultsVisible;
    public bool IsResultsVisible { get => _isResultsVisible; private set => SetProperty(ref _isResultsVisible, value); }

    private string _totalReturnText = "--";
    public string TotalReturnText { get => _totalReturnText; private set => SetProperty(ref _totalReturnText, value); }

    private Brush _totalReturnBrush;
    public Brush TotalReturnBrush { get => _totalReturnBrush; private set => SetProperty(ref _totalReturnBrush, value); }

    private string _annualizedReturnText = "--";
    public string AnnualizedReturnText { get => _annualizedReturnText; private set => SetProperty(ref _annualizedReturnText, value); }

    private string _sharpeRatioText = "--";
    public string SharpeRatioText { get => _sharpeRatioText; private set => SetProperty(ref _sharpeRatioText, value); }

    private string _maxDrawdownText = "--";
    public string MaxDrawdownText { get => _maxDrawdownText; private set => SetProperty(ref _maxDrawdownText, value); }

    private string _totalTradesText = "--";
    public string TotalTradesText { get => _totalTradesText; private set => SetProperty(ref _totalTradesText, value); }

    private string _winRateText = "--";
    public string WinRateText { get => _winRateText; private set => SetProperty(ref _winRateText, value); }

    private string _profitFactorText = "--";
    public string ProfitFactorText { get => _profitFactorText; private set => SetProperty(ref _profitFactorText, value); }

    // ── Recent Backtests ─────────────────────────────────────────────────────────

    public ObservableCollection<BacktestDisplayItem> RecentBacktests { get; } = new();

    private bool _isNoBacktestsVisible = true;
    public bool IsNoBacktestsVisible { get => _isNoBacktestsVisible; private set => SetProperty(ref _isNoBacktestsVisible, value); }

    // ── Data Sync Stats ───────────────────────────────────────────────────────────

    private string _symbolsSyncedText = "0";
    public string SymbolsSyncedText { get => _symbolsSyncedText; private set => SetProperty(ref _symbolsSyncedText, value); }

    private string _dataFilesText = "0";
    public string DataFilesText { get => _dataFilesText; private set => SetProperty(ref _dataFilesText, value); }

    private string _lastSyncText = "Never";
    public string LastSyncText { get => _lastSyncText; private set => SetProperty(ref _lastSyncText, value); }

    // ── Auto-Export ──────────────────────────────────────────────────────────────

    private bool _autoExportEnabled;
    public bool AutoExportEnabled
    {
        get => _autoExportEnabled;
        set => SetProperty(ref _autoExportEnabled, value);
    }

    private string _autoExportLeanDataPath = string.Empty;
    public string AutoExportLeanDataPath
    {
        get => _autoExportLeanDataPath;
        set => SetProperty(ref _autoExportLeanDataPath, value);
    }

    private int _autoExportIntervalSeconds = 300;
    public int AutoExportIntervalSeconds
    {
        get => _autoExportIntervalSeconds;
        set => SetProperty(ref _autoExportIntervalSeconds, value);
    }

    private string _autoExportSymbols = string.Empty;
    public string AutoExportSymbols
    {
        get => _autoExportSymbols;
        set => SetProperty(ref _autoExportSymbols, value);
    }

    private string _autoExportStatusText = string.Empty;
    public string AutoExportStatusText { get => _autoExportStatusText; private set => SetProperty(ref _autoExportStatusText, value); }

    private string _autoExportLastRunText = "Never";
    public string AutoExportLastRunText { get => _autoExportLastRunText; private set => SetProperty(ref _autoExportLastRunText, value); }

    private string _autoExportTotalFilesText = "0";
    public string AutoExportTotalFilesText { get => _autoExportTotalFilesText; private set => SetProperty(ref _autoExportTotalFilesText, value); }

    private bool _isAutoExportBusy;
    public bool IsAutoExportEnabled_Button { get => !_isAutoExportBusy; private set { /* computed */ } }

    // ── Symbol Mapping ───────────────────────────────────────────────────────────

    private string _mappingSymbols = string.Empty;
    public string MappingSymbols
    {
        get => _mappingSymbols;
        set => SetProperty(ref _mappingSymbols, value);
    }

    private string _mappingStatusText = string.Empty;
    public string MappingStatusText { get => _mappingStatusText; private set => SetProperty(ref _mappingStatusText, value); }

    public ObservableCollection<LeanSymbolMappingDisplayItem> SymbolMappings { get; } = new();

    private bool _isMappingTableVisible;
    public bool IsMappingTableVisible { get => _isMappingTableVisible; private set => SetProperty(ref _isMappingTableVisible, value); }

    // ── Results Ingestion ────────────────────────────────────────────────────────

    private string _ingestFilePath = string.Empty;
    public string IngestFilePath
    {
        get => _ingestFilePath;
        set => SetProperty(ref _ingestFilePath, value);
    }

    private string _ingestAlgorithmName = string.Empty;
    public string IngestAlgorithmName
    {
        get => _ingestAlgorithmName;
        set => SetProperty(ref _ingestAlgorithmName, value);
    }

    private string _ingestStatusText = string.Empty;
    public string IngestStatusText { get => _ingestStatusText; private set => SetProperty(ref _ingestStatusText, value); }

    private bool _isIngestBusy;
    public bool IsIngestEnabled { get => !_isIngestBusy; private set { /* computed */ } }

    // ── Dialog request event ─────────────────────────────────────────────────────

    /// <summary>
    /// Raised when the ViewModel needs the View to show a message dialog.
    /// The View (code-behind) subscribes and calls MessageBox.Show with the provided arguments.
    /// </summary>
    public event EventHandler<LeanDialogRequestArgs>? DialogRequested;

    // ── Commands ──────────────────────────────────────────────────────────────────

    public IAsyncRelayCommand InitializeCommand { get; }
    public IAsyncRelayCommand VerifyInstallationCommand { get; }
    public IAsyncRelayCommand SaveConfigurationCommand { get; }
    public IAsyncRelayCommand SyncDataCommand { get; }
    public IAsyncRelayCommand RunBacktestCommand { get; }
    public IRelayCommand StopBacktestCommand { get; }
    public IAsyncRelayCommand RefreshAlgorithmsCommand { get; }
    public IAsyncRelayCommand ConfigureAutoExportCommand { get; }
    public IAsyncRelayCommand ResolveSymbolMappingCommand { get; }
    public IAsyncRelayCommand IngestResultsCommand { get; }

    // ─────────────────────────────────────────────────────────────────────────────

    public LeanIntegrationViewModel(LeanIntegrationService leanService)
    {
        _leanService = leanService;

        // Cache brushes from application resources.
        _successBrush = (Brush)System.Windows.Application.Current.Resources["SuccessColorBrush"];
        _errorBrush = (Brush)System.Windows.Application.Current.Resources["ErrorColorBrush"];
        _infoBrush = (Brush)System.Windows.Application.Current.Resources["InfoColorBrush"];

        // Initialize brush-dependent backing fields before first use.
        _statusIndicatorBrush = _errorBrush;
        _totalReturnBrush = _successBrush;

        // Wire commands.
        InitializeCommand = new AsyncRelayCommand(InitializeAsync);
        VerifyInstallationCommand = new AsyncRelayCommand(VerifyInstallationAsync);
        SaveConfigurationCommand = new AsyncRelayCommand(SaveConfigurationAsync);
        SyncDataCommand = new AsyncRelayCommand(SyncDataAsync);
        RunBacktestCommand = new AsyncRelayCommand(RunBacktestAsync, () => IsBacktestEnabled);
        StopBacktestCommand = new RelayCommand(StopBacktest);
        RefreshAlgorithmsCommand = new AsyncRelayCommand(LoadAlgorithmsAsync);
        ConfigureAutoExportCommand = new AsyncRelayCommand(ConfigureAutoExportAsync);
        ResolveSymbolMappingCommand = new AsyncRelayCommand(ResolveSymbolMappingAsync);
        IngestResultsCommand = new AsyncRelayCommand(IngestResultsAsync);

        // Backtest progress polling timer.
        _backtestPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _backtestPollTimer.Tick += async (_, _) => await PollBacktestStatusAsync();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────────

    /// <summary>Called by the Page's Loaded handler to kick off initial data loads.</summary>
    public void Start() => _ = InitializeAsync();

    // ── Initialization ────────────────────────────────────────────────────────────

    private async Task InitializeAsync(CancellationToken ct = default)
    {
        await Task.WhenAll(
            LoadStatusAsync(),
            LoadConfigurationAsync(),
            LoadAlgorithmsAsync(),
            LoadBacktestHistoryAsync(),
            LoadAutoExportStatusAsync());
    }

    private async Task LoadStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var status = await _leanService.GetStatusAsync(_cts.Token);
            StatusIndicatorBrush = status.IsConfigured ? _successBrush : _errorBrush;
            StatusText = status.IsConfigured
                ? (status.IsInstalled ? "Lean Integration Active" : "Lean Not Found")
                : "Not Configured";
            StatusDetailsText = $"Last sync: {(status.LastSync?.ToString("g") ?? "Never")} | Symbols synced: {status.SymbolsSynced}";
            SymbolsSyncedText = status.SymbolsSynced.ToString();
            LastSyncText = status.LastSync?.ToString("g") ?? "Never";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText = "Connection Error";
            StatusDetailsText = ex.Message;
        }
    }

    private async Task LoadConfigurationAsync(CancellationToken ct = default)
    {
        try
        {
            var config = await _leanService.GetConfigurationAsync(_cts.Token);
            LeanPath = config.LeanPath ?? string.Empty;
            DataPath = config.DataPath ?? string.Empty;
            AutoSync = config.AutoSync;
        }
        catch (OperationCanceledException) { }
        catch { /* Use defaults */ }
    }

    private async Task LoadAlgorithmsAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _leanService.GetAlgorithmsAsync(_cts.Token);
            Algorithms.Clear();
            if (result.Success)
            {
                foreach (var algo in result.Algorithms)
                    Algorithms.Add(algo);
            }
        }
        catch (OperationCanceledException) { }
        catch { /* Algorithms not available */ }
    }

    private async Task LoadBacktestHistoryAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _leanService.GetBacktestHistoryAsync(ct: _cts.Token);
            RecentBacktests.Clear();
            if (result.Success && result.Backtests.Count > 0)
            {
                foreach (var b in result.Backtests)
                {
                    RecentBacktests.Add(new BacktestDisplayItem
                    {
                        AlgorithmName = b.AlgorithmName,
                        DateText = b.StartedAt.ToString("g"),
                        ReturnText = b.TotalReturn.HasValue ? $"{b.TotalReturn:+0.0%;-0.0%}" : "N/A",
                        ReturnBrush = b.TotalReturn >= 0 ? _successBrush : _errorBrush
                    });
                }
            }
            IsNoBacktestsVisible = RecentBacktests.Count == 0;
        }
        catch (OperationCanceledException) { }
        catch { IsNoBacktestsVisible = true; }
    }

    private async Task LoadAutoExportStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var status = await _leanService.GetAutoExportStatusAsync(_cts.Token);
            AutoExportEnabled = status.Enabled;
            AutoExportLeanDataPath = status.LeanDataPath ?? string.Empty;
            AutoExportIntervalSeconds = status.IntervalSeconds > 0 ? status.IntervalSeconds : 300;
            AutoExportLastRunText = status.LastExportAt?.ToString("g") ?? "Never";
            AutoExportTotalFilesText = status.TotalFilesExported.ToString();
            AutoExportStatusText = status.Enabled ? "Auto-export is enabled" : "Auto-export is disabled";
        }
        catch (OperationCanceledException) { }
        catch { AutoExportStatusText = "Status unavailable"; }
    }

    // ── Verify Installation ───────────────────────────────────────────────────────

    private async Task VerifyInstallationAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _leanService.VerifyInstallationAsync(_cts.Token);
            var message = result.Success
                ? $"Lean {result.Version} found at {result.LeanPath}"
                : string.Join("\n", result.Errors);
            var title = result.Success ? "Lean Installation Valid" : "Lean Installation Issues";
            DialogRequested?.Invoke(this, new LeanDialogRequestArgs(message, title, result.Success));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            DialogRequested?.Invoke(this, new LeanDialogRequestArgs(ex.Message, "Verification Failed", false));
        }
    }

    // ── Save Configuration ────────────────────────────────────────────────────────

    private async Task SaveConfigurationAsync(CancellationToken ct = default)
    {
        try
        {
            var config = new LeanConfigurationUpdate
            {
                LeanPath = LeanPath,
                DataPath = DataPath,
                AutoSync = AutoSync
            };
            await _leanService.UpdateConfigurationAsync(config, _cts.Token);
            await LoadStatusAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            DialogRequested?.Invoke(this, new LeanDialogRequestArgs($"Failed to save: {ex.Message}", "Configuration Error", false));
        }
    }

    // ── Data Sync ─────────────────────────────────────────────────────────────────

    private async Task SyncDataAsync(CancellationToken ct = default)
    {
        IsSyncEnabled = false;
        SyncStatusText = "Syncing...";
        try
        {
            var symbols = string.IsNullOrWhiteSpace(SyncSymbols)
                ? null
                : SyncSymbols.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            var options = new DataSyncOptions
            {
                Symbols = symbols,
                FromDate = SyncFromDate is DateTime from ? DateOnly.FromDateTime(from) : null,
                ToDate = SyncToDate is DateTime to ? DateOnly.FromDateTime(to) : null,
                Resolution = SelectedResolution,
                Overwrite = OverwriteSync
            };

            var result = await _leanService.SyncDataAsync(options, _cts.Token);
            if (result.Success)
            {
                SyncStatusText = $"Synced {result.SymbolsSynced} symbols, {result.FilesCreated} files";
                DataFilesText = result.FilesCreated.ToString();
                await LoadStatusAsync();
            }
            else
            {
                SyncStatusText = string.Join(", ", result.Errors);
            }
        }
        catch (OperationCanceledException) { SyncStatusText = "Cancelled"; }
        catch (Exception ex) { SyncStatusText = $"Error: {ex.Message}"; }
        finally { IsSyncEnabled = true; }
    }

    // ── Backtest ──────────────────────────────────────────────────────────────────

    private async Task RunBacktestAsync(CancellationToken ct = default)
    {
        if (SelectedAlgorithm == null)
        {
            DialogRequested?.Invoke(this, new LeanDialogRequestArgs("Please select an algorithm.", "Error", false));
            return;
        }

        IsBacktestEnabled = false;
        IsStopButtonVisible = true;
        IsBacktestProgressVisible = true;
        BacktestProgress = 0;
        BacktestProgressText = "Initializing...";
        BacktestProgressPercent = "0%";

        try
        {
            _ = decimal.TryParse(InitialCapitalText, out var capital);
            if (capital < 1000) capital = 100000;

            var options = new BacktestOptions
            {
                AlgorithmPath = SelectedAlgorithm.Path,
                AlgorithmName = SelectedAlgorithm.Name,
                StartDate = BacktestStartDate is DateTime start ? DateOnly.FromDateTime(start) : null,
                EndDate = BacktestEndDate is DateTime end ? DateOnly.FromDateTime(end) : null,
                InitialCapital = capital
            };

            var result = await _leanService.StartBacktestAsync(options, _cts.Token);
            if (result.Success && result.BacktestId != null)
            {
                _currentBacktestId = result.BacktestId;
                _backtestPollTimer.Start();
            }
            else
            {
                DialogRequested?.Invoke(this, new LeanDialogRequestArgs(result.Error ?? "Unknown error", "Backtest Failed", false));
                ResetBacktestState();
            }
        }
        catch (OperationCanceledException) { ResetBacktestState(); }
        catch (Exception ex)
        {
            DialogRequested?.Invoke(this, new LeanDialogRequestArgs(ex.Message, "Backtest Failed", false));
            ResetBacktestState();
        }
    }

    private void StopBacktest()
    {
        if (_currentBacktestId == null) return;
        _backtestPollTimer.Stop();
        _ = _leanService.StopBacktestAsync(_currentBacktestId, _cts.Token);
        ResetBacktestState();
    }

    private async Task PollBacktestStatusAsync(CancellationToken ct = default)
    {
        if (_currentBacktestId == null) return;
        try
        {
            var status = await _leanService.GetBacktestStatusAsync(_currentBacktestId, _cts.Token);
            BacktestProgressText = $"Processing {status.CurrentDate:d}...";
            BacktestProgressPercent = $"{status.Progress:F0}%";
            BacktestProgress = status.Progress;

            if (status.State == BacktestState.Completed)
            {
                _backtestPollTimer.Stop();
                await ShowBacktestResultsAsync(_currentBacktestId);
                ResetBacktestState();
            }
            else if (status.State == BacktestState.Failed)
            {
                _backtestPollTimer.Stop();
                DialogRequested?.Invoke(this, new LeanDialogRequestArgs(status.Error ?? "Unknown error", "Backtest Failed", false));
                ResetBacktestState();
            }
        }
        catch (OperationCanceledException) { _backtestPollTimer.Stop(); }
        catch { /* Ignore transient poll errors */ }
    }

    private async Task ShowBacktestResultsAsync(string backtestId, CancellationToken ct = default)
    {
        try
        {
            var results = await _leanService.GetBacktestResultsAsync(backtestId, _cts.Token);
            TotalReturnText = $"{results.TotalReturn:+0.0%;-0.0%}";
            TotalReturnBrush = results.TotalReturn >= 0 ? _successBrush : _errorBrush;
            AnnualizedReturnText = $"{results.AnnualizedReturn:+0.0%;-0.0%}";
            SharpeRatioText = $"{results.SharpeRatio:F2}";
            MaxDrawdownText = $"{results.MaxDrawdown:0.0%}";
            TotalTradesText = results.TotalTrades.ToString();
            WinRateText = $"{results.WinRate:0.0%}";
            ProfitFactorText = $"{results.ProfitFactor:F2}";
            IsResultsVisible = true;
            await LoadBacktestHistoryAsync();
        }
        catch (OperationCanceledException) { }
        catch { /* Results may not be available */ }
    }

    private void ResetBacktestState()
    {
        IsBacktestEnabled = true;
        IsStopButtonVisible = false;
        IsBacktestProgressVisible = false;
        _currentBacktestId = null;
    }

    // ── Auto-Export ───────────────────────────────────────────────────────────────

    private async Task ConfigureAutoExportAsync(CancellationToken ct = default)
    {
        _isAutoExportBusy = true;
        AutoExportStatusText = "Saving...";
        try
        {
            var symbols = string.IsNullOrWhiteSpace(AutoExportSymbols)
                ? null
                : AutoExportSymbols.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            var options = new LeanAutoExportConfigureOptions
            {
                Enabled = AutoExportEnabled,
                LeanDataPath = AutoExportLeanDataPath,
                IntervalSeconds = AutoExportIntervalSeconds,
                Symbols = symbols
            };

            var success = await _leanService.ConfigureAutoExportAsync(options, _cts.Token);
            AutoExportStatusText = success
                ? (AutoExportEnabled ? "Auto-export enabled" : "Auto-export disabled")
                : "Failed to save auto-export settings";
        }
        catch (OperationCanceledException) { AutoExportStatusText = "Cancelled"; }
        catch (Exception ex) { AutoExportStatusText = $"Error: {ex.Message}"; }
        finally { _isAutoExportBusy = false; }
    }

    // ── Symbol Mapping ────────────────────────────────────────────────────────────

    private async Task ResolveSymbolMappingAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(MappingSymbols))
        {
            MappingStatusText = "Enter at least one symbol.";
            return;
        }

        MappingStatusText = "Resolving...";
        IsMappingTableVisible = false;
        try
        {
            var symbols = MappingSymbols
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var result = await _leanService.GetSymbolMappingAsync(symbols, _cts.Token);
            SymbolMappings.Clear();
            foreach (var m in result.Mappings)
            {
                SymbolMappings.Add(new LeanSymbolMappingDisplayItem
                {
                    MdcSymbol = m.MdcSymbol,
                    LeanTicker = m.LeanTicker,
                    SecurityType = m.SecurityType,
                    Market = m.Market
                });
            }

            MappingStatusText = $"Resolved {result.Total} symbol(s)";
            IsMappingTableVisible = SymbolMappings.Count > 0;
        }
        catch (OperationCanceledException) { MappingStatusText = "Cancelled"; }
        catch (Exception ex) { MappingStatusText = $"Error: {ex.Message}"; }
    }

    // ── Results Ingestion ─────────────────────────────────────────────────────────

    private async Task IngestResultsAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(IngestFilePath))
        {
            IngestStatusText = "Select a results file first.";
            return;
        }

        _isIngestBusy = true;
        IngestStatusText = "Ingesting...";
        try
        {
            var result = await _leanService.IngestBacktestResultsAsync(
                IngestFilePath,
                algorithmName: string.IsNullOrWhiteSpace(IngestAlgorithmName) ? null : IngestAlgorithmName,
                ct: _cts.Token);

            if (result.Success)
            {
                var ret = result.TotalReturn.HasValue ? $"{result.TotalReturn:+0.0%;-0.0%}" : "N/A";
                IngestStatusText = $"Ingested: {result.AlgorithmName} | Return {ret} | {result.TotalTrades} trades";
                await LoadBacktestHistoryAsync();
            }
            else
            {
                IngestStatusText = result.Error ?? "Ingestion failed";
            }
        }
        catch (OperationCanceledException) { IngestStatusText = "Cancelled"; }
        catch (Exception ex) { IngestStatusText = $"Error: {ex.Message}"; }
        finally { _isIngestBusy = false; }
    }

    // ── IDisposable ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _backtestPollTimer.Stop();

        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Shutdown can reach the page after another lifetime path already disposed its CTS.
        }

        _cts.Dispose();
    }
}

/// <summary>Arguments for a dialog request raised by the ViewModel.</summary>
public sealed class LeanDialogRequestArgs : EventArgs
{
    public LeanDialogRequestArgs(string message, string title, bool isSuccess)
    {
        Message = message;
        Title = title;
        IsSuccess = isSuccess;
    }

    public string Message { get; }
    public string Title { get; }
    public bool IsSuccess { get; }
}
