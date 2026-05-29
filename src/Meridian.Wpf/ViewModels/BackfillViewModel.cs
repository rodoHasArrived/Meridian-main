using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Ui.Services;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Workstation.Models;
using UiBackfillCompletedEventArgs = Meridian.Ui.Services.BackfillCompletedEventArgs;
using UiBackfillProgressEventArgs = Meridian.Ui.Services.BackfillProgressEventArgs;
using UiBackfillService = Meridian.Ui.Services.BackfillService;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

public readonly record struct BackfillStatsPresentation(
    string TotalBarsText,
    string SymbolsProcessedText,
    string RunWindowText,
    string LastSuccessfulRunText);

/// <summary>
/// ViewModel for the Backfill page.
/// All state, collections, timer management, and backfill orchestration live here;
/// the code-behind is thinned to lifecycle wiring and form-input delegation.
/// Provides contextual commands for the command palette when activated.
/// </summary>
public sealed partial class BackfillViewModel : BindableBase, IPageActivationLifetime, IDisposable, ICommandContextProvider, IPageActionBarProvider
{
    private readonly WpfServices.NotificationService _notificationService;
    private readonly WpfServices.NavigationService _navigationService;
    private readonly WpfServices.LoggingService _loggingService;
    private readonly WpfServices.ConfigService _configService;
    private readonly BackfillApiService _backfillApiService;
    private readonly UiBackfillService _backfillService;
    private readonly BackfillCheckpointService _checkpointService;
    private readonly WpfServices.TaskbarProgressService _taskbarProgressService;
    private readonly WpfServices.ToastNotificationService _toastNotificationService;
    private readonly CommandPaletteService _commandPaletteService;

    private readonly DispatcherTimer _progressPollTimer;
    private CancellationTokenSource? _activationCts;
    private CancellationTokenSource? _backfillCts;
    private bool _isActive;
    private bool _isDisposed;

    // Last known symbol counts — used to restore taskbar progress after a resume.
    private ulong _lastCompletedSymbols;
    private ulong _lastTotalSymbols;

    // ── Public collections ──────────────────────────────────────────────────
    public ObservableCollection<SymbolProgressInfo> SymbolProgress => WorkbenchSection.SymbolProgress;
    public ObservableCollection<ScheduledJobInfo> ScheduledJobs => WorkbenchSection.ScheduledJobs;
    public ObservableCollection<ResumableJobInfo> ResumableJobs => WorkbenchSection.ResumableJobs;
    public ObservableCollection<GapAnalysisItem> GapItems => WorkbenchSection.GapItems;
    public WorkstationTableModel<SymbolProgressInfo> SymbolProgressTable => WorkbenchSection.SymbolProgressTable;
    public WorkstationTableModel<GapAnalysisItem> GapItemsTable => WorkbenchSection.GapItemsTable;

    // ── Bindable properties ─────────────────────────────────────────────────
    public string BackfillStatusText
    {
        get => WorkbenchSection.BackfillStatusText;
        private set => SetBackfillSectionProperty(WorkbenchSection.BackfillStatusText, text => WorkbenchSection.BackfillStatusText = text, value);
    }

    public string OverallProgressText
    {
        get => WorkbenchSection.OverallProgressText;
        private set => SetBackfillSectionProperty(WorkbenchSection.OverallProgressText, text => WorkbenchSection.OverallProgressText = text, value);
    }

    public string PauseButtonContent
    {
        get => WorkbenchSection.PauseButtonContent;
        private set => SetBackfillSectionProperty(WorkbenchSection.PauseButtonContent, text => WorkbenchSection.PauseButtonContent = text, value);
    }

    private bool _isBackfillActive;
    private bool _lastStartSetupCanStart;
    public bool IsBackfillActive
    {
        get => _isBackfillActive;
        private set
        {
            if (SetProperty(ref _isBackfillActive, value))
                CanStartBackfill = _lastStartSetupCanStart && !value;
        }
    }

    public bool IsProgressVisible
    {
        get => WorkbenchSection.IsProgressVisible;
        private set => SetBackfillSectionProperty(WorkbenchSection.IsProgressVisible, visible => WorkbenchSection.IsProgressVisible = visible, value);
    }

    public bool HasNoScheduledJobs
    {
        get => WorkbenchSection.HasNoScheduledJobs;
        private set => SetBackfillSectionProperty(WorkbenchSection.HasNoScheduledJobs, empty => WorkbenchSection.HasNoScheduledJobs = empty, value);
    }

    public bool HasNoResumableJobs
    {
        get => WorkbenchSection.HasNoResumableJobs;
        private set => SetBackfillSectionProperty(WorkbenchSection.HasNoResumableJobs, empty => WorkbenchSection.HasNoResumableJobs = empty, value);
    }

    private string _providerPrioritySummaryText = "Priority: No providers selected";
    public string ProviderPrioritySummaryText
    {
        get => _providerPrioritySummaryText;
        private set => SetProperty(ref _providerPrioritySummaryText, value);
    }

    private string _granularityHintText = "Daily is recommended for broad symbol lists and long history windows.";
    public string GranularityHintText
    {
        get => _granularityHintText;
        private set => SetProperty(ref _granularityHintText, value);
    }

    public string GapAnalysisSummaryText
    {
        get => WorkbenchSection.GapAnalysisSummaryText;
        private set => SetBackfillSectionProperty(WorkbenchSection.GapAnalysisSummaryText, text => WorkbenchSection.GapAnalysisSummaryText = text, value);
    }

    public string GapActionHintText
    {
        get => WorkbenchSection.GapActionHintText;
        private set => SetBackfillSectionProperty(WorkbenchSection.GapActionHintText, text => WorkbenchSection.GapActionHintText = text, value);
    }

    public bool IsGapAnalysisCardVisible
    {
        get => WorkbenchSection.IsGapAnalysisCardVisible;
        private set => SetBackfillSectionProperty(WorkbenchSection.IsGapAnalysisCardVisible, visible => WorkbenchSection.IsGapAnalysisCardVisible = visible, value);
    }

    public bool IsGapListVisible
    {
        get => WorkbenchSection.IsGapListVisible;
        private set => SetBackfillSectionProperty(WorkbenchSection.IsGapListVisible, visible => WorkbenchSection.IsGapListVisible = visible, value);
    }

    public bool IsGapActionPanelVisible
    {
        get => WorkbenchSection.IsGapActionPanelVisible;
        private set => SetBackfillSectionProperty(WorkbenchSection.IsGapActionPanelVisible, visible => WorkbenchSection.IsGapActionPanelVisible = visible, value);
    }

    private string _nasdaqKeyStatusText = "No API key stored";
    public string NasdaqKeyStatusText
    {
        get => _nasdaqKeyStatusText;
        private set => SetProperty(ref _nasdaqKeyStatusText, value);
    }

    private bool _isNasdaqKeyClearVisible;
    public bool IsNasdaqKeyClearVisible
    {
        get => _isNasdaqKeyClearVisible;
        private set => SetProperty(ref _isNasdaqKeyClearVisible, value);
    }

    private string _openFigiKeyStatusText = "No API key stored (optional)";
    public string OpenFigiKeyStatusText
    {
        get => _openFigiKeyStatusText;
        private set => SetProperty(ref _openFigiKeyStatusText, value);
    }

    private bool _isOpenFigiKeyClearVisible;
    public bool IsOpenFigiKeyClearVisible
    {
        get => _isOpenFigiKeyClearVisible;
        private set => SetProperty(ref _isOpenFigiKeyClearVisible, value);
    }

    public Meridian.Contracts.Api.BackfillResultDto? LastApiStatus
    {
        get => StatusSection.LastApiStatus;
        private set => SetBackfillSectionProperty(StatusSection.LastApiStatus, status => StatusSection.LastApiStatus = status, value);
    }

    public bool HasApiStatus
    {
        get => StatusSection.HasApiStatus;
        private set => SetBackfillSectionProperty(StatusSection.HasApiStatus, hasStatus => StatusSection.HasApiStatus = hasStatus, value);
    }

    public Visibility LastStatusVisibility
    {
        get => StatusSection.LastStatusVisibility;
        private set => SetBackfillSectionProperty(StatusSection.LastStatusVisibility, visibility => StatusSection.LastStatusVisibility = visibility, value);
    }

    public Visibility EmptyStatusVisibility
    {
        get => StatusSection.EmptyStatusVisibility;
        private set => SetBackfillSectionProperty(StatusSection.EmptyStatusVisibility, visibility => StatusSection.EmptyStatusVisibility = visibility, value);
    }

    public string LastRunStatusText
    {
        get => StatusSection.LastRunStatusText;
        private set => SetBackfillSectionProperty(StatusSection.LastRunStatusText, text => StatusSection.LastRunStatusText = text, value);
    }

    public Brush LastRunStatusBrush
    {
        get => StatusSection.LastRunStatusBrush;
        private set => SetBackfillSectionProperty(StatusSection.LastRunStatusBrush, brush => StatusSection.LastRunStatusBrush = brush, value);
    }

    public string LastRunProviderText
    {
        get => StatusSection.LastRunProviderText;
        private set => SetBackfillSectionProperty(StatusSection.LastRunProviderText, text => StatusSection.LastRunProviderText = text, value);
    }

    public string LastRunSymbolsText
    {
        get => StatusSection.LastRunSymbolsText;
        private set => SetBackfillSectionProperty(StatusSection.LastRunSymbolsText, text => StatusSection.LastRunSymbolsText = text, value);
    }

    public string LastRunBarsWrittenText
    {
        get => StatusSection.LastRunBarsWrittenText;
        private set => SetBackfillSectionProperty(StatusSection.LastRunBarsWrittenText, text => StatusSection.LastRunBarsWrittenText = text, value);
    }

    public string LastRunStartedText
    {
        get => StatusSection.LastRunStartedText;
        private set => SetBackfillSectionProperty(StatusSection.LastRunStartedText, text => StatusSection.LastRunStartedText = text, value);
    }

    public string LastRunCompletedText
    {
        get => StatusSection.LastRunCompletedText;
        private set => SetBackfillSectionProperty(StatusSection.LastRunCompletedText, text => StatusSection.LastRunCompletedText = text, value);
    }

    public string BackfillStatsTotalBarsText
    {
        get => StatusSection.BackfillStatsTotalBarsText;
        private set => SetBackfillSectionProperty(StatusSection.BackfillStatsTotalBarsText, text => StatusSection.BackfillStatsTotalBarsText = text, value);
    }

    public string BackfillStatsSymbolsProcessedText
    {
        get => StatusSection.BackfillStatsSymbolsProcessedText;
        private set => SetBackfillSectionProperty(StatusSection.BackfillStatsSymbolsProcessedText, text => StatusSection.BackfillStatsSymbolsProcessedText = text, value);
    }

    public string BackfillStatsRunWindowText
    {
        get => StatusSection.BackfillStatsRunWindowText;
        private set => SetBackfillSectionProperty(StatusSection.BackfillStatsRunWindowText, text => StatusSection.BackfillStatsRunWindowText = text, value);
    }

    public string BackfillStatsLastSuccessfulRunText
    {
        get => StatusSection.BackfillStatsLastSuccessfulRunText;
        private set => SetBackfillSectionProperty(StatusSection.BackfillStatsLastSuccessfulRunText, text => StatusSection.BackfillStatsLastSuccessfulRunText = text, value);
    }

    private string _startReadinessTitle = "Backfill setup incomplete";
    public string StartReadinessTitle
    {
        get => _startReadinessTitle;
        private set => SetProperty(ref _startReadinessTitle, value);
    }

    private string _startReadinessDetail = "Add at least one symbol and verify the date range before starting a historical backfill.";
    public string StartReadinessDetail
    {
        get => _startReadinessDetail;
        private set => SetProperty(ref _startReadinessDetail, value);
    }

    private string _startReadinessScopeText = "No request configured";
    public string StartReadinessScopeText
    {
        get => _startReadinessScopeText;
        private set => SetProperty(ref _startReadinessScopeText, value);
    }

    private bool _canStartBackfill;
    public bool CanStartBackfill
    {
        get => _canStartBackfill;
        private set => SetProperty(ref _canStartBackfill, value);
    }

    private string _symbolsValidationErrorText = string.Empty;
    public string SymbolsValidationErrorText
    {
        get => _symbolsValidationErrorText;
        private set => SetProperty(ref _symbolsValidationErrorText, value);
    }

    private bool _isSymbolsValidationErrorVisible;
    public bool IsSymbolsValidationErrorVisible
    {
        get => _isSymbolsValidationErrorVisible;
        private set => SetProperty(ref _isSymbolsValidationErrorVisible, value);
    }

    private string _fromDateValidationErrorText = string.Empty;
    public string FromDateValidationErrorText
    {
        get => _fromDateValidationErrorText;
        private set => SetProperty(ref _fromDateValidationErrorText, value);
    }

    private bool _isFromDateValidationErrorVisible;
    public bool IsFromDateValidationErrorVisible
    {
        get => _isFromDateValidationErrorVisible;
        private set => SetProperty(ref _isFromDateValidationErrorVisible, value);
    }

    private string _toDateValidationErrorText = string.Empty;
    public string ToDateValidationErrorText
    {
        get => _toDateValidationErrorText;
        private set => SetProperty(ref _toDateValidationErrorText, value);
    }

    private bool _isToDateValidationErrorVisible;
    public bool IsToDateValidationErrorVisible
    {
        get => _isToDateValidationErrorVisible;
        private set => SetProperty(ref _isToDateValidationErrorVisible, value);
    }

    // ── IPageActionBarProvider implementation ──────────────────────────────────────
    public string PageTitle => "Backfill";
    public ObservableCollection<ActionEntry> Actions { get; } = new();

    // ── M1: Bindable properties for UI state previously manipulated directly in code-behind ──

    /// <summary>Displays the number of symbols entered, e.g. "3 symbols".</summary>
    private string _symbolCountText = "0 symbols";
    public string SymbolCountText
    {
        get => _symbolCountText;
        private set => SetProperty(ref _symbolCountText, value);
    }

    /// <summary>Hint text shown after the smart-range is applied.</summary>
    private string _dateRangeHintText = "Smart range uses symbol count + granularity to keep request sizes practical.";
    public string DateRangeHintText
    {
        get => _dateRangeHintText;
        private set => SetProperty(ref _dateRangeHintText, value);
    }

    /// <summary>Opacity for the schedule settings panel (1.0 when enabled, 0.5 when disabled).</summary>
    private double _schedulePanelOpacity = 0.5;
    public double SchedulePanelOpacity
    {
        get => _schedulePanelOpacity;
        private set => SetProperty(ref _schedulePanelOpacity, value);
    }

    // ── M1 update methods (called from code-behind after reading form controls) ──

    /// <summary>Updates the symbol count label from the parsed symbols array.</summary>
    public void UpdateSymbolCount(int count) =>
        SymbolCountText = $"{count} symbol{(count == 1 ? "" : "s")}";

    /// <summary>Updates the date-range hint text after a smart-range is applied.</summary>
    public void UpdateDateRangeHint(int lookbackDays, int symbolCount, string granularityDisplay) =>
        DateRangeHintText = $"Smart range applied: last {lookbackDays} days for {Math.Max(symbolCount, 1)} symbol(s) at {granularityDisplay} granularity.";

    /// <summary>Reflects the scheduled-backfill toggle state on the settings panel opacity.</summary>
    public void SetScheduleEnabled(bool enabled) =>
        SchedulePanelOpacity = enabled ? 1.0 : 0.5;

    // ── M2: Delegate collection mutation to the ViewModel ───────────────────────

    /// <summary>
    /// Updates an existing scheduled job in the collection.
    /// Code-behind must not directly index into <see cref="ScheduledJobs"/>.
    /// </summary>
    public void UpdateScheduledJob(ScheduledJobInfo job, string name, string nextRun)
    {
        var index = ScheduledJobs.IndexOf(job);
        if (index >= 0)
            ScheduledJobs[index] = new ScheduledJobInfo { Name = name, NextRun = nextRun };

        _notificationService.ShowNotification(
            "Job Updated",
            $"Scheduled job '{name}' has been updated.",
            NotificationType.Success);
    }

    // ── M3: Stub-action methods — notifications belong in the ViewModel, not code-behind ──

    public void HandleValidateData() =>
        _notificationService.ShowNotification("Data Validation", "Starting data validation...", NotificationType.Info);

    public void HandleRepairGaps() =>
        _notificationService.ShowNotification("Gap Repair", "Checking for data gaps...", NotificationType.Info);

    public void HandleFillAllGaps() =>
        _notificationService.ShowNotification("Fill Gaps", "Analyzing all symbols for gaps...", NotificationType.Info);

    public void HandleSaveSchedule() =>
        _notificationService.ShowNotification("Schedule Saved", "Backfill schedule has been saved.", NotificationType.Success);

    public void HandleRunScheduledJob(ScheduledJobInfo job) =>
        _notificationService.ShowNotification("Running Job", $"Starting scheduled job: {job.Name}", NotificationType.Info);

    public void HandleUpdateToLatest() =>
        _notificationService.ShowNotification(
            "Update to Latest",
            "Configured to update all subscribed symbols to latest data.",
            NotificationType.Info);

    public void HandleRefreshStatusNotification() =>
        _notificationService.ShowNotification("Status Refreshed", "Backfill status has been refreshed.", NotificationType.Info);

    public void HandleNoGapsFound() =>
        _notificationService.ShowNotification("No Gaps", "No gaps detected to fill.", NotificationType.Info);

    public void HandleAutoFillGapsNotification(string[] symbolsWithGaps) =>
        _notificationService.ShowNotification(
            "Gap Fill",
            $"Configured to fill gaps for {symbolsWithGaps.Length} symbols. Press Start Backfill to begin.",
            NotificationType.Info);

    public void HandleJobDeletedNotification(string jobName) =>
        _notificationService.ShowNotification(
            "Job Deleted",
            $"Scheduled job '{jobName}' has been deleted.",
            NotificationType.Success);

    public BackfillViewModel(
        WpfServices.NotificationService notificationService,
        WpfServices.NavigationService navigationService,
        WpfServices.LoggingService loggingService,
        WpfServices.ConfigService configService,
        UiBackfillService backfillService,
        BackfillCheckpointService checkpointService,
        WpfServices.TaskbarProgressService taskbarProgressService,
        WpfServices.ToastNotificationService toastNotificationService,
        CommandPaletteService commandPaletteService)
    {
        _notificationService = notificationService;
        _navigationService = navigationService;
        _loggingService = loggingService;
        _configService = configService;

        _backfillApiService = new BackfillApiService();
        _backfillService = backfillService;
        _checkpointService = checkpointService;
        _taskbarProgressService = taskbarProgressService;
        _toastNotificationService = toastNotificationService;
        _commandPaletteService = commandPaletteService;

        _progressPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _progressPollTimer.Tick += OnProgressPollTimerTick;
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────
    public bool IsActive => _isActive;

    public CancellationToken ActivationToken => _activationCts?.Token ?? CancellationToken.None;

    public Task StartAsync(CancellationToken ct = default) => ActivateAsync(ct);

    public async Task ActivateAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isActive)
        {
            return;
        }

        _isActive = true;
        _activationCts?.Dispose();
        _activationCts = ct.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : new CancellationTokenSource();

        _backfillService.ProgressUpdated += OnBackfillProgressUpdated;
        _backfillService.BackfillCompleted += OnBackfillCompleted;

        // Populate action bar.
        Actions.Clear();
        Actions.Add(new ActionEntry("Start Backfill", new RelayCommand(() => _navigationService.NavigateTo("Backfill")), "\uE768", "Start a new backfill", IsPrimary: true));
        Actions.Add(new ActionEntry("View Status", new RelayCommand(() => _navigationService.NavigateTo("Backfill")), "\uE9D9", "View backfill status"));

        await LoadScheduledJobsAsync(ActivationToken);
        await LoadResumableJobsAsync(ActivationToken);
        await RefreshStatusFromApiAsync(ActivationToken);
    }

    public void Stop() => Deactivate();

    public void CancelActivation()
    {
        CancelAndDispose(Interlocked.Exchange(ref _backfillCts, null));
        CancelAndDispose(Interlocked.Exchange(ref _activationCts, null));
    }

    public void Deactivate()
    {
        if (!_isActive)
        {
            return;
        }

        _isActive = false;
        _progressPollTimer.Stop();
        _backfillService.ProgressUpdated -= OnBackfillProgressUpdated;
        _backfillService.BackfillCompleted -= OnBackfillCompleted;
        CancelActivation();
    }

    // ── Data loading ────────────────────────────────────────────────────────
    public async Task LoadScheduledJobsAsync(CancellationToken ct = default)
    {
        ScheduledJobs.Clear();
        try
        {
            var executions = await _backfillApiService.GetExecutionHistoryAsync(limit: 10);
            foreach (var exec in executions)
            {
                ScheduledJobs.Add(new ScheduledJobInfo
                {
                    Name = $"{exec.Status}: {exec.SymbolsProcessed} symbols",
                    NextRun = exec.CompletedAt?.ToString("g") ?? exec.StartedAt.ToString("g")
                });
            }
        }
        catch
        {
            // Fallback if API unavailable
        }

        HasNoScheduledJobs = ScheduledJobs.Count == 0;
    }

    public async Task LoadResumableJobsAsync(CancellationToken ct = default)
    {
        ResumableJobs.Clear();
        try
        {
            var resumable = await _checkpointService.GetResumableJobsAsync();
            foreach (var job in resumable)
            {
                var pendingSymbols = await _checkpointService.GetPendingSymbolsAsync(job.JobId);
                ResumableJobs.Add(new ResumableJobInfo
                {
                    JobId = job.JobId,
                    Provider = job.Provider,
                    Status = job.Status.ToString(),
                    CreatedAt = job.CreatedAt.ToLocalTime().ToString("g"),
                    SymbolsSummary = $"{job.CompletedCount}/{job.SymbolCheckpoints.Count} symbols done, {pendingSymbols.Length} remaining",
                    PendingCount = pendingSymbols.Length,
                    TotalBarsDownloaded = job.TotalBarsDownloaded,
                    DateRange = $"{job.FromDate:d} — {job.ToDate:d}"
                });
            }
        }
        catch
        {
            // Checkpoint storage unavailable
        }

        HasNoResumableJobs = ResumableJobs.Count == 0;
    }

    public async Task RefreshStatusFromApiAsync(CancellationToken ct = default)
    {
        try
        {
            var lastStatus = await _backfillApiService.GetLastStatusAsync();
            if (lastStatus != null)
            {
                UpdateLastApiStatus(lastStatus);
            }
            else
            {
                ClearLastApiStatus();
            }
        }
        catch
        {
            ClearLastApiStatus();
        }
    }

    // ── Backfill control ────────────────────────────────────────────────────
    private void UpdateLastApiStatus(BackfillResultDto status)
    {
        LastApiStatus = status;
        HasApiStatus = true;
        LastStatusVisibility = Visibility.Visible;
        EmptyStatusVisibility = Visibility.Collapsed;
        LastRunStatusText = status.Success ? "Completed" : "Failed";
        LastRunStatusBrush = status.Success
            ? new SolidColorBrush(Color.FromRgb(63, 185, 80))
            : new SolidColorBrush(Color.FromRgb(244, 67, 54));
        LastRunProviderText = status.Provider ?? "Unknown";
        LastRunSymbolsText = status.Symbols is { Length: > 0 }
            ? string.Join(", ", status.Symbols)
            : "N/A";
        LastRunBarsWrittenText = status.BarsWritten.ToString("N0");
        LastRunStartedText = status.StartedUtc?.LocalDateTime.ToString("g") ?? "Unknown";
        LastRunCompletedText = status.CompletedUtc?.LocalDateTime.ToString("g") ?? "N/A";
        ApplyBackfillStats(BuildBackfillStats(status));
    }

    private void ClearLastApiStatus()
    {
        LastApiStatus = null;
        HasApiStatus = false;
        LastStatusVisibility = Visibility.Collapsed;
        EmptyStatusVisibility = Visibility.Visible;
        LastRunStatusText = string.Empty;
        LastRunStatusBrush = Brushes.Transparent;
        LastRunProviderText = "Unknown";
        LastRunSymbolsText = "N/A";
        LastRunBarsWrittenText = "0";
        LastRunStartedText = "Unknown";
        LastRunCompletedText = "N/A";
        ApplyBackfillStats(BuildBackfillStats(null));
    }

    public static BackfillStatsPresentation BuildBackfillStats(BackfillResultDto? status)
    {
        if (status is null)
        {
            return new BackfillStatsPresentation(
                "--",
                "--",
                "No run loaded",
                "No successful run loaded");
        }

        var symbolCount = CountDistinctSymbols(status);
        var symbolsText = symbolCount == 0
            ? "No symbols reported"
            : $"{symbolCount:N0} symbol{(symbolCount == 1 ? string.Empty : "s")}";

        var runWindowText = (status.StartedUtc, status.CompletedUtc) switch
        {
            ({ } started, { } completed) => $"{started.LocalDateTime:g} to {completed.LocalDateTime:g}",
            ({ } started, null) => $"Started {started.LocalDateTime:g}",
            _ => "Window unavailable"
        };

        var lastSuccessfulRunText = status.Success
            ? status.CompletedUtc?.LocalDateTime.ToString("g") ?? "Completed; timestamp unavailable"
            : "Latest run failed";

        return new BackfillStatsPresentation(
            status.BarsWritten.ToString("N0"),
            symbolsText,
            runWindowText,
            lastSuccessfulRunText);
    }

    private void ApplyBackfillStats(BackfillStatsPresentation stats)
    {
        BackfillStatsTotalBarsText = stats.TotalBarsText;
        BackfillStatsSymbolsProcessedText = stats.SymbolsProcessedText;
        BackfillStatsRunWindowText = stats.RunWindowText;
        BackfillStatsLastSuccessfulRunText = stats.LastSuccessfulRunText;
    }

    private static int CountDistinctSymbols(BackfillResultDto status)
    {
        var symbols = status.Symbols is { Length: > 0 }
            ? status.Symbols
            : status.SymbolResults?.Select(result => result.Symbol).ToArray();

        return symbols?
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() ?? 0;
    }

    public async Task StartBackfillAsync(
        string[] symbols,
        string provider,
        DateTime fromDate,
        DateTime toDate,
        string granularity, CancellationToken ct = default)
    {
        SymbolProgress.Clear();
        foreach (var symbol in symbols)
        {
            SymbolProgress.Add(new SymbolProgressInfo
            {
                Symbol = symbol.Trim().ToUpper(),
                Progress = 0,
                BarsText = "0 bars",
                StatusText = "Pending",
                TimeText = "--",
                StatusBackground = new SolidColorBrush(Color.FromArgb(40, 139, 148, 158))
            });
        }

        BackfillStatusText = "Running...";
        OverallProgressText = $"Overall: 0 / {symbols.Length} symbols complete";
        IsBackfillActive = true;
        IsProgressVisible = true;
        PauseButtonContent = "Pause";
        _taskbarProgressService.SetIndeterminate();

        _notificationService.ShowNotification(
            "Backfill Started",
            $"Downloading data for {symbols.Length} symbols...",
            NotificationType.Info);

        _progressPollTimer.Start();
        _backfillCts = new CancellationTokenSource();

        try
        {
            await _backfillService.StartBackfillAsync(
                symbols.Select(s => s.Trim().ToUpper()).ToArray(),
                provider,
                fromDate,
                toDate,
                granularity);
        }
        catch (OperationCanceledException)
        {
            _progressPollTimer.Stop();
            BackfillStatusText = "Cancelled";
            IsBackfillActive = false;
            _taskbarProgressService.Clear();
        }
        catch (Exception ex)
        {
            _progressPollTimer.Stop();
            BackfillStatusText = "Failed";
            IsBackfillActive = false;
            _taskbarProgressService.SetError();

            _notificationService.ShowNotification(
                "Backfill Failed",
                ex.Message,
                NotificationType.Error);
        }
    }

    public void PauseOrResumeBackfill()
    {
        if (_backfillService.IsPaused)
        {
            _backfillService.Resume();
            BackfillStatusText = "Running...";
            PauseButtonContent = "Pause";
            _taskbarProgressService.SetNormal(_lastCompletedSymbols, _lastTotalSymbols);
            _notificationService.ShowNotification("Backfill Resumed", "Backfill operation has been resumed.", NotificationType.Info);
        }
        else
        {
            _backfillService.Pause();
            BackfillStatusText = "Paused";
            PauseButtonContent = "Resume";
            _taskbarProgressService.SetPaused();
            _notificationService.ShowNotification("Backfill Paused", "Backfill operation has been paused.", NotificationType.Warning);
        }
    }

    public void CancelBackfill()
    {
        _backfillService.Cancel();
        _backfillCts?.Cancel();
        _progressPollTimer.Stop();
        BackfillStatusText = "Cancelled";
        IsBackfillActive = false;
        IsProgressVisible = false;
        _taskbarProgressService.Clear();
        _notificationService.ShowNotification("Backfill Cancelled", "The backfill operation was cancelled.", NotificationType.Warning);
    }

    public async Task ResumeJobAsync(ResumableJobInfo job, CancellationToken ct = default)
    {
        if (_backfillService.IsRunning)
        {
            _notificationService.ShowNotification(
                "Cannot Resume",
                "A backfill operation is already running. Cancel or wait for it to finish.",
                NotificationType.Warning);
            return;
        }

        try
        {
            BackfillStatusText = $"Resuming ({job.PendingCount} symbols remaining)...";
            IsBackfillActive = true;
            IsProgressVisible = true;
            PauseButtonContent = "Pause";
            _taskbarProgressService.SetIndeterminate();

            _notificationService.ShowNotification(
                "Resuming Backfill",
                $"Resuming job from checkpoint: {job.PendingCount} symbols remaining.",
                NotificationType.Info);

            _progressPollTimer.Start();
            await _backfillService.ResumeBackfillAsync(job.JobId);
        }
        catch (OperationCanceledException)
        {
            _progressPollTimer.Stop();
            BackfillStatusText = "Cancelled";
            IsBackfillActive = false;
            _taskbarProgressService.Clear();
        }
        catch (Exception ex)
        {
            _progressPollTimer.Stop();
            BackfillStatusText = "Resume Failed";
            IsBackfillActive = false;
            _taskbarProgressService.SetError();

            _notificationService.ShowNotification("Resume Failed", ex.Message, NotificationType.Error);
        }
    }

    public void DismissJob(ResumableJobInfo job)
    {
        ResumableJobs.Remove(job);
        HasNoResumableJobs = ResumableJobs.Count == 0;
        _notificationService.ShowNotification(
            "Job Dismissed",
            "Resumable job has been dismissed from the list.",
            NotificationType.Info);
    }

    public void DeleteScheduledJob(ScheduledJobInfo job)
    {
        ScheduledJobs.Remove(job);
        HasNoScheduledJobs = ScheduledJobs.Count == 0;
    }

    // ── Progress event handlers ─────────────────────────────────────────────
    private void OnBackfillProgressUpdated(object? sender, UiBackfillProgressEventArgs e)
    {
        if (e.Progress == null)
            return;
        _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            UpdateProgressDisplay(e.Progress);
        });
    }

    private void UpdateProgressDisplay(Meridian.Contracts.Backfill.BackfillProgress progress)
    {
        BackfillStatusText = progress.Status;
        var completedCount = progress.CompletedSymbols;
        OverallProgressText = $"Overall: {completedCount} / {progress.TotalSymbols} symbols complete";

        // Reflect symbol-level progress on the taskbar icon.
        _lastCompletedSymbols = (ulong)Math.Max(0, completedCount);
        _lastTotalSymbols = (ulong)Math.Max(1, progress.TotalSymbols);
        _taskbarProgressService.SetNormal(_lastCompletedSymbols, _lastTotalSymbols);

        if (progress.SymbolProgress == null)
            return;
        for (var i = 0; i < progress.SymbolProgress.Length && i < SymbolProgress.Count; i++)
        {
            var sp = progress.SymbolProgress[i];
            var item = SymbolProgress[i];
            item.Progress = sp.CalculatedProgress;
            item.BarsText = $"{sp.BarsDownloaded:N0} bars";
            item.StatusText = sp.Status;
            item.TimeText = sp.Duration?.ToString(@"mm\:ss") ?? "--";
            item.StatusBackground = sp.Status switch
            {
                "Completed" => new SolidColorBrush(Color.FromArgb(40, 63, 185, 80)),
                "Failed" => new SolidColorBrush(Color.FromArgb(40, 244, 67, 54)),
                "Downloading" => new SolidColorBrush(Color.FromArgb(40, 33, 150, 243)),
                _ => new SolidColorBrush(Color.FromArgb(40, 139, 148, 158))
            };
        }
    }

    private void OnBackfillCompleted(object? sender, UiBackfillCompletedEventArgs e)
    {
        _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () =>
        {
            _progressPollTimer.Stop();
            IsBackfillActive = false;

            if (e.Success)
            {
                _taskbarProgressService.Clear();
                BackfillStatusText = "Completed";
                _notificationService.ShowNotification(
                    "Backfill Complete",
                    $"Successfully downloaded data for {e.Progress?.CompletedSymbols ?? 0} symbols.",
                    NotificationType.Success);

                var symbolCount = e.Progress?.CompletedSymbols ?? 0;
                var barsWritten = e.Progress?.DownloadedBars ?? 0L;
                var duration = e.Progress?.CompletedAt.HasValue == true
                    ? e.Progress.CompletedAt.Value - e.Progress.StartedAt
                    : TimeSpan.Zero;
                _toastNotificationService.ShowBackfillComplete(symbolCount, barsWritten, duration);
            }
            else if (e.WasCancelled)
            {
                _taskbarProgressService.Clear();
                BackfillStatusText = "Cancelled";
            }
            else
            {
                _taskbarProgressService.SetError();
                BackfillStatusText = "Failed";
                _notificationService.ShowNotification(
                    "Backfill Failed",
                    e.Error?.Message ?? "Unknown error occurred.",
                    NotificationType.Error);
            }

            await RefreshStatusFromApiAsync();
            await LoadResumableJobsAsync();
        });
    }

    // E1: async void swallows exceptions — the handler delegates to a testable async Task method
    //     that is wrapped in try/catch so poll failures are logged rather than silently lost.
    private async void OnProgressPollTimerTick(object? sender, EventArgs e)
    {
        await TickPollAsync();
    }

    /// <summary>
    /// Testable poll body extracted from the async-void timer handler.
    /// </summary>
    internal async Task TickPollAsync()
    {
        try
        {
            await _backfillService.PollBackendStatusAsync();
            await RefreshStatusFromApiAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _loggingService.LogWarning("Progress poll tick failed", ("Error", ex.Message));
        }
    }

    // ── Gap scanning ────────────────────────────────────────────────────────
    public async Task ScanGapsAsync(string[] symbols, DateTime fromDate, DateTime toDate, CancellationToken ct = default)
    {
        if (symbols.Length == 0)
        {
            IsGapAnalysisCardVisible = true;
            GapAnalysisSummaryText = "Enter symbols above before scanning for gaps.";
            return;
        }

        var totalDays = Math.Max(1, (int)(toDate - fromDate).TotalDays);

        GapItems.Clear();
        IsGapAnalysisCardVisible = true;
        IsGapListVisible = false;
        IsGapActionPanelVisible = false;
        GapAnalysisSummaryText = "Scanning for data gaps\u2026";

        var totalGapDays = 0;
        var apiReachable = false;

        foreach (var symbol in symbols)
        {
            var sym = symbol.Trim().ToUpper();
            try
            {
                var result = await _backfillApiService.GetSymbolGapAnalysisAsync(sym);
                apiReachable = true;

                int coveragePct;
                int gapDays;
                if (result != null)
                {
                    coveragePct = Math.Max(0, Math.Min(100, (int)Math.Round(result.DataAvailabilityPercent)));
                    gapDays = totalDays - (int)Math.Round(totalDays * coveragePct / 100.0);
                }
                else
                {
                    coveragePct = 0;
                    gapDays = totalDays;
                }

                totalGapDays += gapDays;

                var coverageBrush = coveragePct >= 95
                    ? new SolidColorBrush(Color.FromRgb(63, 185, 80))
                    : coveragePct >= 70
                        ? new SolidColorBrush(Color.FromRgb(227, 179, 65))
                        : new SolidColorBrush(Color.FromRgb(244, 67, 54));

                GapItems.Add(new GapAnalysisItem
                {
                    Symbol = sym,
                    CoveragePercent = coveragePct,
                    CoverageText = $"{coveragePct}%",
                    GapDays = gapDays,
                    GapDaysText = gapDays == 0 ? "Complete" : $"{gapDays}d gaps",
                    CoverageBrush = coverageBrush,
                    CoverageWidth = Math.Max(4, coveragePct * 3.5)
                });
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Gap scan failed for symbol: {sym}", ex);
            }
        }

        if (!apiReachable)
        {
            GapAnalysisSummaryText = "Service unavailable. Ensure the backend is running to scan for gaps.";
            return;
        }

        IsGapListVisible = true;
        IsGapActionPanelVisible = totalGapDays > 0;
        GapActionHintText = totalGapDays > 0
            ? $"{totalGapDays} total gap days across {symbols.Length} symbols"
            : string.Empty;
        GapAnalysisSummaryText = totalGapDays == 0
            ? "All symbols have complete coverage for the selected date range."
            : $"Found gaps in {GapItems.Count(g => g.GapDays > 0)} of {symbols.Length} symbols.";
    }

    public string[] GetSymbolsWithGaps() =>
        GapItems.Where(g => g.GapDays > 0).Select(g => g.Symbol).ToArray();

    // ── UI hint helpers ─────────────────────────────────────────────────────
    public void UpdateGranularityHint(string granularity)
    {
        GranularityHintText = granularity switch
        {
            "1Min" => "1-minute data is best for short tactical windows (typically days to a few weeks).",
            "5Min" => "5-minute data balances detail and request size for multi-week backfills and focused intraday studies.",
            "15Min" => "15-minute data balances detail and request size for multi-week to multi-month backfills.",
            "30Min" => "30-minute data works well for multi-month backfills where you still want intraday structure.",
            "Hourly" => "Hourly data is well-suited for trend/rotation systems over months.",
            "4Hour" => "4-hour data compresses hourly sessions into broader trend bars while keeping partial close buckets.",
            _ => "Daily is recommended for broad symbol lists and long history windows."
        };
    }

    public void UpdateProviderPrioritySummary(string primary, string secondary, string tertiary)
    {
        var sequence = new[] { primary, secondary, tertiary }
            .Where(v => !string.IsNullOrWhiteSpace(v) && v != "No fallback")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ProviderPrioritySummaryText = sequence.Length > 0
            ? $"Priority: {string.Join(" → ", sequence)}"
            : "Priority: No providers selected";
    }

    public void UpdateStartSetupState(
        string? symbolsText,
        DateTime? fromDate,
        DateTime? toDate,
        string? providerDisplay,
        string? provider,
        string? granularity)
    {
        var state = BuildStartSetupState(symbolsText, fromDate, toDate, providerDisplay, provider, granularity);
        ApplyStartSetupState(state);
    }

    private void ApplyStartSetupState(BackfillStartSetupState state)
    {
        _lastStartSetupCanStart = state.CanStart;
        CanStartBackfill = state.CanStart && !IsBackfillActive;
        SymbolCountText = state.SymbolCountText;
        StartReadinessTitle = state.ReadinessTitle;
        StartReadinessDetail = state.ReadinessDetail;
        StartReadinessScopeText = state.ScopeText;
        SymbolsValidationErrorText = state.SymbolsValidationError;
        IsSymbolsValidationErrorVisible = state.IsSymbolsValidationErrorVisible;
        FromDateValidationErrorText = state.FromDateValidationError;
        IsFromDateValidationErrorVisible = state.IsFromDateValidationErrorVisible;
        ToDateValidationErrorText = state.ToDateValidationError;
        IsToDateValidationErrorVisible = state.IsToDateValidationErrorVisible;
    }

    public static (DateTime From, DateTime To) ComputeSmartRange(string granularity, int symbolCount)
    {
        var lookbackDays = granularity switch
        {
            "1Min" => symbolCount > 20 ? 3 : symbolCount > 5 ? 7 : 14,
            "5Min" => symbolCount > 30 ? 14 : symbolCount > 10 ? 30 : 60,
            "15Min" => symbolCount > 20 ? 14 : symbolCount > 5 ? 30 : 60,
            "30Min" => symbolCount > 30 ? 30 : symbolCount > 10 ? 90 : 180,
            "Hourly" => symbolCount > 50 ? 30 : symbolCount > 10 ? 90 : 180,
            "4Hour" => symbolCount > 75 ? 90 : symbolCount > 20 ? 180 : 365,
            _ => symbolCount > 100 ? 365 : symbolCount > 30 ? 365 * 2 : 365 * 5
        };
        return (DateTime.Today.AddDays(-lookbackDays), DateTime.Today);
    }

    public static string GetGranularityDisplay(string granularity) => granularity switch
    {
        "1Min" => "1-minute",
        "5Min" => "5-minute",
        "15Min" => "15-minute",
        "30Min" => "30-minute",
        "Hourly" => "hourly",
        "4Hour" => "4-hour",
        "Daily" => "daily",
        _ => granularity.ToLowerInvariant()
    };

    public static BackfillStartSetupState BuildStartSetupState(
        string? symbolsText,
        DateTime? fromDate,
        DateTime? toDate,
        string? providerDisplay,
        string? provider,
        string? granularity)
    {
        var symbols = ParseSymbols(symbolsText);
        var resolvedFromDate = fromDate ?? DateTime.Today.AddDays(-30);
        var resolvedToDate = toDate ?? DateTime.Today;
        var symbolCountText = $"{symbols.Length} symbol{(symbols.Length == 1 ? "" : "s")}";

        var symbolsValidationError = symbols.Length == 0
            ? "Please enter at least one symbol"
            : string.Empty;
        var fromDateValidationError = string.Empty;
        var toDateValidationError = string.Empty;

        if (resolvedFromDate > resolvedToDate)
        {
            fromDateValidationError = "From date must be earlier than To date";
            toDateValidationError = "To date must be on or after From date";
        }

        var providerName = ResolveProviderDisplay(providerDisplay, provider);
        var granularityDisplay = GetGranularityDisplay(granularity ?? "Daily");
        var dateScope = $"{resolvedFromDate:MMM d, yyyy} to {resolvedToDate:MMM d, yyyy}";
        var canStart = string.IsNullOrWhiteSpace(symbolsValidationError)
            && string.IsNullOrWhiteSpace(fromDateValidationError)
            && string.IsNullOrWhiteSpace(toDateValidationError);

        var symbolScope = symbols.Length switch
        {
            0 => "No symbols selected",
            1 => symbols[0],
            <= 3 => string.Join(", ", symbols),
            _ => $"{symbols.Length} symbols"
        };

        var readinessTitle = canStart
            ? "Backfill ready"
            : "Backfill setup incomplete";
        var readinessDetail = symbols.Length == 0
            ? "Add at least one symbol before starting a historical backfill."
            : resolvedFromDate > resolvedToDate
                ? "Fix the date range before starting the backfill."
                : $"Ready to request {granularityDisplay} history for {symbolScope} from {providerName}.";
        var scopeText = canStart
            ? $"{symbolScope} - {dateScope} - {granularityDisplay} - {providerName}"
            : $"{symbolScope} - {dateScope}";

        return new BackfillStartSetupState(
            canStart,
            readinessTitle,
            readinessDetail,
            scopeText,
            symbolCountText,
            symbolsValidationError,
            !string.IsNullOrWhiteSpace(symbolsValidationError),
            fromDateValidationError,
            !string.IsNullOrWhiteSpace(fromDateValidationError),
            toDateValidationError,
            !string.IsNullOrWhiteSpace(toDateValidationError));
    }

    public bool TryCreateStartRequest(
        string? symbolsText,
        DateTime? fromDate,
        DateTime? toDate,
        string? provider,
        string? granularity,
        out BackfillStartRequest request,
        out string? symbolValidationError,
        out string? fromDateValidationError,
        out string? toDateValidationError)
    {
        request = default;
        symbolValidationError = null;
        fromDateValidationError = null;
        toDateValidationError = null;

        if (string.IsNullOrWhiteSpace(symbolsText))
        {
            symbolValidationError = "Please enter at least one symbol";
            return false;
        }

        var symbols = ParseSymbols(symbolsText);

        if (symbols.Length == 0)
        {
            symbolValidationError = "Please enter at least one symbol";
            return false;
        }

        var resolvedFromDate = fromDate ?? DateTime.Today.AddDays(-30);
        var resolvedToDate = toDate ?? DateTime.Today;

        if (resolvedFromDate > resolvedToDate)
        {
            fromDateValidationError = "From date must be earlier than To date";
            toDateValidationError = "To date must be on or after From date";
            return false;
        }

        request = new BackfillStartRequest(
            symbols,
            provider ?? "composite",
            resolvedFromDate,
            resolvedToDate,
            granularity ?? "Daily");

        return true;
    }

    private static string[] ParseSymbols(string? symbolsText) =>
        (symbolsText ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string ResolveProviderDisplay(string? providerDisplay, string? provider)
    {
        if (!string.IsNullOrWhiteSpace(providerDisplay))
            return providerDisplay.Trim();

        return provider switch
        {
            "composite" => "Multi-Source",
            "yahoo" => "Yahoo Finance",
            "stooq" => "Stooq",
            "nasdaq" => "Nasdaq Data Link",
            _ => string.IsNullOrWhiteSpace(provider) ? "selected provider" : provider
        };
    }

    public async Task<string> GetSubscribedSymbolsTextAsync()
    {
        try
        {
            var configSymbols = await _configService.GetConfiguredSymbolsAsync();
            if (configSymbols.Length > 0)
                return string.Join(", ", configSymbols.Select(s => s.Symbol));
        }
        catch (Exception ex)
        {
            _loggingService.LogWarning("Failed to load configured symbols for backfill page", ("Error", ex.Message));
        }

        return "SPY, QQQ, AAPL, MSFT, GOOGL, AMZN, NVDA, META, TSLA";
    }

    public string AppendSymbols(string? existingSymbolsText, params string[] symbolsToAppend)
    {
        var mergedSymbols = (existingSymbolsText ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(symbolsToAppend)
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return string.Join(", ", mergedSymbols);
    }

    public (DateTime From, DateTime To, string SymbolsText) BuildLatestUpdatePreset(string? existingSymbolsText) =>
        (DateTime.Today.AddDays(-5), DateTime.Today, AppendSymbols(existingSymbolsText));

    // ── API key management ──────────────────────────────────────────────────
    public void SetNasdaqApiKey(string apiKey)
    {
        Environment.SetEnvironmentVariable("NASDAQDATALINK__APIKEY", apiKey, EnvironmentVariableTarget.User);
        NasdaqKeyStatusText = "API key configured";
        IsNasdaqKeyClearVisible = true;
        _notificationService.ShowNotification("API Key Saved", "Nasdaq Data Link API key has been configured.", NotificationType.Success);
    }

    public void ClearNasdaqApiKey()
    {
        Environment.SetEnvironmentVariable("NASDAQDATALINK__APIKEY", null, EnvironmentVariableTarget.User);
        NasdaqKeyStatusText = "No API key stored";
        IsNasdaqKeyClearVisible = false;
    }

    public void SetOpenFigiApiKey(string apiKey)
    {
        Environment.SetEnvironmentVariable("OPENFIGI__APIKEY", apiKey, EnvironmentVariableTarget.User);
        OpenFigiKeyStatusText = "API key configured (optional)";
        IsOpenFigiKeyClearVisible = true;
        _notificationService.ShowNotification("API Key Saved", "OpenFIGI API key has been configured.", NotificationType.Success);
    }

    public void ClearOpenFigiApiKey()
    {
        Environment.SetEnvironmentVariable("OPENFIGI__APIKEY", null, EnvironmentVariableTarget.User);
        OpenFigiKeyStatusText = "No API key stored (optional)";
        IsOpenFigiKeyClearVisible = false;
    }

    // ── Navigation helper ───────────────────────────────────────────────────
    public void NavigateToWizard() => _navigationService.NavigateTo("AnalysisExportWizard");
    public void NavigateToBrowser() => _navigationService.NavigateTo("DataBrowser");

    // ── ICommandContextProvider implementation ──────────────────────────────

    public string ContextKey => "Backfill";

    public IReadOnlyList<CommandEntry> GetContextualCommands()
    {
        var commands = new List<CommandEntry>();

        // Start/resume backfill command
        var startCommand = new RelayCommand(() =>
        {
            // Open the backfill start dialog via UI interaction
            _navigationService.NavigateTo("Backfill");
        });
        commands.Add(new CommandEntry(
            "Start Backfill",
            "Begin a new backfill operation for selected symbols",
            "Backfill",
            startCommand,
            "Ctrl+B"));

        // Pause/Resume command
        var pauseResumeCommand = new RelayCommand(PauseOrResumeBackfill);
        commands.Add(new CommandEntry(
            IsBackfillActive && !(_backfillService?.IsPaused ?? false) ? "Pause Backfill" : "Resume Backfill",
            IsBackfillActive && !(_backfillService?.IsPaused ?? false)
                ? "Pause the currently running backfill operation"
                : "Resume a paused backfill operation",
            "Backfill",
            pauseResumeCommand));

        // Cancel command
        if (IsBackfillActive)
        {
            var cancelCommand = new RelayCommand(CancelBackfill);
            commands.Add(new CommandEntry(
                "Cancel Backfill",
                "Stop and cancel the current backfill operation",
                "Backfill",
                cancelCommand));
        }

        // View status command
        var viewStatusCommand = new RelayCommand(() =>
        {
            _notificationService.ShowNotification(
                "Backfill Status",
                BackfillStatusText,
                NotificationType.Info);
        });
        commands.Add(new CommandEntry(
            "View Backfill Status",
            "Display current backfill job status and progress",
            "Backfill",
            viewStatusCommand));

        // View backfill schedule command
        var scheduleCommand = new RelayCommand(() =>
            _navigationService.NavigateTo("Schedules"));
        commands.Add(new CommandEntry(
            "View Backfill Schedule",
            "Open backfill schedule settings",
            "Backfill",
            scheduleCommand));

        return commands.AsReadOnly();
    }

    public void OnActivated()
    {
        var paletteService = _commandPaletteService;
        paletteService.RegisterContextualProvider(ContextKey, GetContextualCommands);
        paletteService.SetActiveContext(ContextKey);
    }

    public void OnDeactivated()
    {
        var paletteService = _commandPaletteService;
        paletteService.ClearActiveContext();
        paletteService.UnregisterContextualProvider(ContextKey);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Deactivate();
    }

    private static void CancelAndDispose(CancellationTokenSource? cts)
    {
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        cts.Dispose();
    }

    public readonly record struct BackfillStartRequest(
        string[] Symbols,
        string Provider,
        DateTime FromDate,
        DateTime ToDate,
        string Granularity);

    public readonly record struct BackfillStartSetupState(
        bool CanStart,
        string ReadinessTitle,
        string ReadinessDetail,
        string ScopeText,
        string SymbolCountText,
        string SymbolsValidationError,
        bool IsSymbolsValidationErrorVisible,
        string FromDateValidationError,
        bool IsFromDateValidationErrorVisible,
        string ToDateValidationError,
        bool IsToDateValidationErrorVisible);
}
