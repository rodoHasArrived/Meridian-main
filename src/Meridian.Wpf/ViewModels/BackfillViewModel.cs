using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using UiBackfillService = Meridian.Ui.Services.BackfillService;
using UiBackfillProgressEventArgs = Meridian.Ui.Services.BackfillProgressEventArgs;
using UiBackfillCompletedEventArgs = Meridian.Ui.Services.BackfillCompletedEventArgs;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Backfill page.
/// All state, collections, timer management, and backfill orchestration live here;
/// the code-behind is thinned to lifecycle wiring and form-input delegation.
/// Provides contextual commands for the command palette when activated.
/// </summary>
public sealed class BackfillViewModel : BindableBase, IDisposable, ICommandContextProvider, IPageActionBarProvider
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
    private CancellationTokenSource? _backfillCts;

    // Last known symbol counts for taskbar progress updates.
    private ulong _lastCompletedSymbols;
    private ulong _lastTotalSymbols;

    // ── Public collections ──────────────────────────────────────────────────
    public ObservableCollection<SymbolProgressInfo> SymbolProgress { get; } = new();
    public ObservableCollection<ScheduledJobInfo> ScheduledJobs { get; } = new();
    public ObservableCollection<ResumableJobInfo> ResumableJobs { get; } = new();
    public ObservableCollection<GapAnalysisItem> GapItems { get; } = new();

    // ── Bindable properties ─────────────────────────────────────────────────
    private string _backfillStatusText = string.Empty;
    public string BackfillStatusText
    {
        get => _backfillStatusText;
        private set => SetProperty(ref _backfillStatusText, value);
    }

    private string _overallProgressText = string.Empty;
    public string OverallProgressText
    {
        get => _overallProgressText;
        private set => SetProperty(ref _overallProgressText, value);
    }

    private bool _isBackfillActive;
    public bool IsBackfillActive
    {
        get => _isBackfillActive;
        private set => SetProperty(ref _isBackfillActive, value);
    }

    private bool _isProgressVisible;
    public bool IsProgressVisible
    {
        get => _isProgressVisible;
        private set => SetProperty(ref _isProgressVisible, value);
    }

    private bool _hasNoScheduledJobs = true;
    public bool HasNoScheduledJobs
    {
        get => _hasNoScheduledJobs;
        private set => SetProperty(ref _hasNoScheduledJobs, value);
    }

    private bool _hasNoResumableJobs = true;
    public bool HasNoResumableJobs
    {
        get => _hasNoResumableJobs;
        private set => SetProperty(ref _hasNoResumableJobs, value);
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

    private string _gapAnalysisSummaryText = string.Empty;
    public string GapAnalysisSummaryText
    {
        get => _gapAnalysisSummaryText;
        private set => SetProperty(ref _gapAnalysisSummaryText, value);
    }

    private string _gapActionHintText = string.Empty;
    public string GapActionHintText
    {
        get => _gapActionHintText;
        private set => SetProperty(ref _gapActionHintText, value);
    }

    private bool _isGapAnalysisCardVisible;
    public bool IsGapAnalysisCardVisible
    {
        get => _isGapAnalysisCardVisible;
        private set => SetProperty(ref _isGapAnalysisCardVisible, value);
    }

    private bool _isGapListVisible;
    public bool IsGapListVisible
    {
        get => _isGapListVisible;
        private set => SetProperty(ref _isGapListVisible, value);
    }

    private bool _isGapActionPanelVisible;
    public bool IsGapActionPanelVisible
    {
        get => _isGapActionPanelVisible;
        private set => SetProperty(ref _isGapActionPanelVisible, value);
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

    private Meridian.Contracts.Api.BackfillResultDto? _lastApiStatus;
    public Meridian.Contracts.Api.BackfillResultDto? LastApiStatus
    {
        get => _lastApiStatus;
        private set => SetProperty(ref _lastApiStatus, value);
    }

    private bool _hasApiStatus;
    public bool HasApiStatus
    {
        get => _hasApiStatus;
        private set => SetProperty(ref _hasApiStatus, value);
    }

    private Visibility _lastStatusVisibility = Visibility.Collapsed;
    public Visibility LastStatusVisibility
    {
        get => _lastStatusVisibility;
        private set => SetProperty(ref _lastStatusVisibility, value);
    }

    private Visibility _emptyStatusVisibility = Visibility.Visible;
    public Visibility EmptyStatusVisibility
    {
        get => _emptyStatusVisibility;
        private set => SetProperty(ref _emptyStatusVisibility, value);
    }

    private string _lastRunStatusText = string.Empty;
    public string LastRunStatusText
    {
        get => _lastRunStatusText;
        private set => SetProperty(ref _lastRunStatusText, value);
    }

    private Brush _lastRunStatusBrush = Brushes.Transparent;
    public Brush LastRunStatusBrush
    {
        get => _lastRunStatusBrush;
        private set => SetProperty(ref _lastRunStatusBrush, value);
    }

    private string _lastRunProviderText = "Unknown";
    public string LastRunProviderText
    {
        get => _lastRunProviderText;
        private set => SetProperty(ref _lastRunProviderText, value);
    }

    private string _lastRunSymbolsText = "N/A";
    public string LastRunSymbolsText
    {
        get => _lastRunSymbolsText;
        private set => SetProperty(ref _lastRunSymbolsText, value);
    }

    private string _lastRunBarsWrittenText = "0";
    public string LastRunBarsWrittenText
    {
        get => _lastRunBarsWrittenText;
        private set => SetProperty(ref _lastRunBarsWrittenText, value);
    }

    private string _lastRunStartedText = "Unknown";
    public string LastRunStartedText
    {
        get => _lastRunStartedText;
        private set => SetProperty(ref _lastRunStartedText, value);
    }

    private string _lastRunCompletedText = "N/A";
    public string LastRunCompletedText
    {
        get => _lastRunCompletedText;
        private set => SetProperty(ref _lastRunCompletedText, value);
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
    /// Replaces an existing scheduled job by recreating it through the backend API.
    /// The current schedule API has create/delete operations but no dedicated update route.
    /// </summary>
    public async Task UpdateScheduledJobAsync(
        ScheduledJobInfo job,
        string name,
        string frequencyTag,
        string timeText,
        CancellationToken ct = default)
    {
        var cronExpression = BuildCronExpression(frequencyTag, timeText);
        if (cronExpression == null)
        {
            _notificationService.ShowNotification(
                "Update Failed",
                "Enter the schedule time in HH:mm format.",
                NotificationType.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(job.ScheduleId))
        {
            var deleted = await _backfillApiService.DeleteScheduleAsync(job.ScheduleId, ct);
            if (!deleted)
            {
                _notificationService.ShowNotification(
                    "Update Failed",
                    $"Unable to update scheduled job '{job.Name}'.",
                    NotificationType.Error);
                return;
            }
        }

        var recreated = await _backfillApiService.CreateScheduleAsync(new BackfillScheduleCreateRequest
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(job.Description)
                ? $"{name} managed from the desktop backfill page."
                : job.Description,
            Enabled = job.Enabled,
            CronExpression = cronExpression,
            TimeZoneId = TimeZoneInfo.Local.Id,
            BackfillType = ParseBackfillType(job.BackfillType),
            Symbols = job.Symbols.ToList(),
            LookbackDays = job.LookbackDays,
            Granularity = ParseGranularity(job.Granularity),
            PreferredProviders = job.PreferredProviders.ToList()
        }, ct);

        if (recreated == null)
        {
            _notificationService.ShowNotification(
                "Update Failed",
                $"Unable to recreate scheduled job '{name}'.",
                NotificationType.Error);
            return;
        }

        await LoadScheduledJobsAsync(ct);
        _notificationService.ShowNotification("Job Updated", $"Scheduled job '{name}' has been updated.", NotificationType.Success);
    }

    // ── M3: Stub-action methods — notifications belong in the ViewModel, not code-behind ──

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
    public async Task StartAsync(CancellationToken ct = default)
    {
        _backfillService.ProgressUpdated += OnBackfillProgressUpdated;
        _backfillService.BackfillCompleted += OnBackfillCompleted;

        // Populate action bar.
        Actions.Clear();
        Actions.Add(new ActionEntry("Start Backfill", new RelayCommand(() => _navigationService.NavigateTo("Backfill")), "\uE768", "Start a new backfill", IsPrimary: true));
        Actions.Add(new ActionEntry("View Status", new RelayCommand(() => _navigationService.NavigateTo("Backfill")), "\uE9D9", "View backfill status"));

        await LoadScheduledJobsAsync();
        await LoadResumableJobsAsync();
        await RefreshStatusFromApiAsync();
    }

    public void Stop()
    {
        _progressPollTimer.Stop();
        _backfillCts?.Cancel();
        _backfillService.ProgressUpdated -= OnBackfillProgressUpdated;
        _backfillService.BackfillCompleted -= OnBackfillCompleted;
    }

    // ── Data loading ────────────────────────────────────────────────────────
    public async Task LoadScheduledJobsAsync(CancellationToken ct = default)
    {
        ScheduledJobs.Clear();
        try
        {
            var schedules = await _backfillApiService.GetSchedulesAsync(ct);
            foreach (var schedule in schedules
                .OrderBy(s => s.NextExecutionAt ?? DateTimeOffset.MaxValue)
                .Take(10))
            {
                ScheduledJobs.Add(new ScheduledJobInfo
                {
                    ScheduleId = schedule.ScheduleId,
                    Name = schedule.Name,
                    NextRun = schedule.NextExecutionAt?.ToLocalTime().ToString("g") ?? "Not scheduled",
                    Description = schedule.Description,
                    CronExpression = schedule.CronExpression,
                    Enabled = schedule.Enabled,
                    FrequencyTag = InferFrequencyTag(schedule.CronExpression),
                    TimeText = ExtractTimeText(schedule.CronExpression),
                    BackfillType = schedule.BackfillType,
                    LookbackDays = schedule.LookbackDays,
                    Granularity = schedule.Granularity,
                    Symbols = schedule.Symbols.ToArray(),
                    PreferredProviders = schedule.PreferredProviders.ToArray()
                });
            }
        }
        catch
        {
            // Fallback if API unavailable
        }

        HasNoScheduledJobs = ScheduledJobs.Count == 0;
    }

    public async Task ValidateDataAsync(string[] symbols, DateTime fromDate, DateTime toDate, CancellationToken ct = default)
    {
        await ScanGapsAsync(symbols, fromDate, toDate, ct);

        var symbolsWithGaps = GetSymbolsWithGaps();
        var message = symbolsWithGaps.Length == 0
            ? "No data gaps were detected for the selected symbols and date range."
            : $"Detected gaps in {symbolsWithGaps.Length} symbol(s). Review the gap analysis panel for details.";

        _notificationService.ShowNotification("Data Validation", message, NotificationType.Info);
    }

    public async Task RepairGapsAsync(string[] symbols, DateTime fromDate, DateTime toDate, CancellationToken ct = default)
    {
        await ScanGapsAsync(symbols, fromDate, toDate, ct);
        var symbolsWithGaps = GetSymbolsWithGaps();
        if (symbolsWithGaps.Length == 0)
        {
            HandleNoGapsFound();
            return;
        }

        await StartGapFillAsync(symbolsWithGaps, ComputeLookbackDays(fromDate, toDate), ct);
    }

    public async Task FillAllGapsAsync(string[] symbols, DateTime fromDate, DateTime toDate, CancellationToken ct = default)
    {
        var effectiveSymbols = symbols.Length > 0
            ? symbols
            : (await GetSubscribedSymbolsTextAsync()).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (effectiveSymbols.Length == 0)
        {
            _notificationService.ShowNotification(
                "Fill Gaps",
                "No symbols are available to analyze. Add symbols or subscribe to symbols first.",
                NotificationType.Warning);
            return;
        }

        await RepairGapsAsync(effectiveSymbols, fromDate, toDate, ct);
    }

    public async Task AutoFillGapsAsync(CancellationToken ct = default)
    {
        var symbolsWithGaps = GetSymbolsWithGaps();
        if (symbolsWithGaps.Length == 0)
        {
            HandleNoGapsFound();
            return;
        }

        await StartGapFillAsync(symbolsWithGaps, 30, ct);
    }

    public async Task SaveScheduleAsync(
        bool enabled,
        string frequencyTag,
        string timeText,
        bool includeAllSymbols,
        string symbolsText,
        CancellationToken ct = default)
    {
        var cronExpression = BuildCronExpression(frequencyTag, timeText);
        if (cronExpression == null)
        {
            _notificationService.ShowNotification(
                "Invalid Schedule",
                "Enter the schedule time in HH:mm format.",
                NotificationType.Warning);
            return;
        }

        var scheduleSymbols = includeAllSymbols
            ? new List<string>()
            : ParseSymbols(symbolsText).ToList();

        if (!includeAllSymbols && scheduleSymbols.Count == 0)
        {
            _notificationService.ShowNotification(
                "Invalid Schedule",
                "Enter at least one symbol or choose all subscribed symbols.",
                NotificationType.Warning);
            return;
        }

        var request = new BackfillScheduleCreateRequest
        {
            Name = BuildScheduleName(frequencyTag),
            Description = $"Scheduled {frequencyTag.ToLowerInvariant()} gap-fill from the desktop backfill page.",
            Enabled = enabled,
            CronExpression = cronExpression,
            TimeZoneId = TimeZoneInfo.Local.Id,
            BackfillType = 0,
            Symbols = scheduleSymbols,
            LookbackDays = 30,
            Granularity = 6
        };

        var created = await _backfillApiService.CreateScheduleAsync(request, ct);
        if (created == null)
        {
            _notificationService.ShowNotification(
                "Schedule Failed",
                "Unable to save the backfill schedule. Ensure the backend is running.",
                NotificationType.Error);
            return;
        }

        await LoadScheduledJobsAsync(ct);
        _notificationService.ShowNotification(
            "Schedule Saved",
            $"Saved scheduled backfill '{created.Name}'.",
            NotificationType.Success);
    }

    public async Task RunScheduledJobAsync(ScheduledJobInfo job, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(job.ScheduleId))
        {
            _notificationService.ShowNotification(
                "Run Failed",
                "This schedule cannot be run because it has no identifier.",
                NotificationType.Error);
            return;
        }

        var execution = await _backfillApiService.RunScheduleNowAsync(job.ScheduleId, ct);
        if (execution == null)
        {
            _notificationService.ShowNotification(
                "Run Failed",
                $"Unable to start scheduled job '{job.Name}'.",
                NotificationType.Error);
            return;
        }

        _notificationService.ShowNotification(
            "Running Job",
            $"Started scheduled job '{job.Name}' ({execution.ExecutionId}).",
            NotificationType.Info);
    }

    public async Task DeleteScheduledJobAsync(ScheduledJobInfo job, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(job.ScheduleId))
        {
            DeleteScheduledJob(job);
            return;
        }

        var deleted = await _backfillApiService.DeleteScheduleAsync(job.ScheduleId, ct);
        if (!deleted)
        {
            _notificationService.ShowNotification(
                "Delete Failed",
                $"Unable to delete scheduled job '{job.Name}'.",
                NotificationType.Error);
            return;
        }

        DeleteScheduledJob(job);
        HandleJobDeletedNotification(job.Name);
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
    private void UpdateLastApiStatus(Meridian.Contracts.Api.BackfillResultDto status)
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
        if (e.Progress == null) return;
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

        if (progress.SymbolProgress == null) return;
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

    private async Task StartGapFillAsync(string[] symbols, int lookbackDays, CancellationToken ct)
    {
        SymbolProgress.Clear();
        foreach (var symbol in symbols)
        {
            SymbolProgress.Add(new SymbolProgressInfo
            {
                Symbol = symbol.Trim().ToUpperInvariant(),
                Progress = 0,
                BarsText = "0 bars",
                StatusText = "Pending",
                TimeText = "--",
                StatusBackground = new SolidColorBrush(Color.FromArgb(40, 139, 148, 158))
            });
        }

        BackfillStatusText = "Gap fill running...";
        OverallProgressText = $"Overall: 0 / {symbols.Length} symbols complete";
        IsBackfillActive = true;
        IsProgressVisible = true;
        _taskbarProgressService.SetIndeterminate();
        _progressPollTimer.Start();

        try
        {
            await _backfillService.StartGapFillAsync(symbols, lookbackDays, ct: ct);
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
            _notificationService.ShowNotification("Gap Fill Failed", ex.Message, NotificationType.Error);
        }
    }

    // ── UI hint helpers ─────────────────────────────────────────────────────
    public void UpdateGranularityHint(string granularity)
    {
        GranularityHintText = granularity switch
        {
            "1Min" => "1-minute data is best for short tactical windows (typically days to a few weeks).",
            "15Min" => "15-minute data balances detail and request size for multi-week to multi-month backfills.",
            "Hourly" => "Hourly data is well-suited for trend/rotation systems over months.",
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

    public static (DateTime From, DateTime To) ComputeSmartRange(string granularity, int symbolCount)
    {
        var lookbackDays = granularity switch
        {
            "1Min" => symbolCount > 20 ? 3 : symbolCount > 5 ? 7 : 14,
            "15Min" => symbolCount > 20 ? 14 : symbolCount > 5 ? 30 : 60,
            "Hourly" => symbolCount > 50 ? 30 : symbolCount > 10 ? 90 : 180,
            _ => symbolCount > 100 ? 365 : symbolCount > 30 ? 365 * 2 : 365 * 5
        };
        return (DateTime.Today.AddDays(-lookbackDays), DateTime.Today);
    }

    public static string GetGranularityDisplay(string granularity) => granularity switch
    {
        "1Min" => "1-minute",
        "15Min" => "15-minute",
        "Hourly" => "hourly",
        "Daily" => "daily",
        _ => granularity.ToLowerInvariant()
    };

    private static string InferFrequencyTag(string cronExpression)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
        {
            return "Daily";
        }

        var parts = cronExpression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
        {
            return "Daily";
        }

        if (parts[2] == "1")
        {
            return "Monthly";
        }

        return parts[4] != "*" ? "Weekly" : "Daily";
    }

    private static string ExtractTimeText(string cronExpression)
    {
        var parts = cronExpression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
        {
            return "06:00";
        }

        return $"{parts[1].PadLeft(2, '0')}:{parts[0].PadLeft(2, '0')}";
    }

    private static string? BuildCronExpression(string frequencyTag, string timeText)
    {
        if (!TimeOnly.TryParseExact(timeText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return null;
        }

        return frequencyTag switch
        {
            "Weekly" => $"{time.Minute} {time.Hour} * * 0",
            "Monthly" => $"{time.Minute} {time.Hour} 1 * *",
            _ => $"{time.Minute} {time.Hour} * * *"
        };
    }

    private static int ComputeLookbackDays(DateTime fromDate, DateTime toDate) =>
        Math.Max(1, (int)Math.Ceiling((toDate.Date - fromDate.Date).TotalDays));

    private static string[] ParseSymbols(string? symbolsText) =>
        (symbolsText ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string BuildScheduleName(string frequencyTag) =>
        frequencyTag switch
        {
            "Weekly" => "Weekly Gap Fill",
            "Monthly" => "Monthly Gap Fill",
            _ => "Daily Gap Fill"
        };

    private static int ParseBackfillType(string? backfillType) =>
        string.Equals(backfillType, "FullBackfill", StringComparison.OrdinalIgnoreCase) ? 1 :
        string.Equals(backfillType, "RollingWindow", StringComparison.OrdinalIgnoreCase) ? 2 :
        string.Equals(backfillType, "EndOfDay", StringComparison.OrdinalIgnoreCase) ? 3 :
        0;

    private static int ParseGranularity(string? granularity) =>
        granularity?.ToLowerInvariant() switch
        {
            "1min" or "minute1" => 0,
            "5min" or "minute5" => 1,
            "15min" or "minute15" => 2,
            "30min" or "minute30" => 3,
            "hourly" or "hour1" => 4,
            "hour4" => 5,
            "weekly" => 7,
            "monthly" => 8,
            _ => 6
        };

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

        var symbols = symbolsText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

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

    public void Dispose() => Stop();

    public readonly record struct BackfillStartRequest(
        string[] Symbols,
        string Provider,
        DateTime FromDate,
        DateTime ToDate,
        string Granularity);
}
