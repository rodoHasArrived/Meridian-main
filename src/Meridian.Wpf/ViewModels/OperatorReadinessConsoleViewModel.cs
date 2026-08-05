using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Cross-lane operator readiness console for the desktop workstation. It aggregates the shared
/// server-owned readiness contracts — trading operator readiness, the operator inbox, the
/// reconciliation break queue, and strategy run summaries — into one Launchpad surface and routes
/// each work item to its owning workspace. All gate and severity truth stays server-side; sources
/// that are unavailable degrade into per-panel error text rather than empty-looking rows.
/// </summary>
public sealed class OperatorReadinessConsoleViewModel : BindableBase, IDisposable
{
    private readonly ITradingOperatorReadinessProvider? _readinessProvider;
    private readonly IWorkstationOperatorInboxApiClient? _inboxClient;
    private readonly IWorkstationReconciliationApiClient? _reconciliationClient;
    private readonly StrategyRunWorkspaceService? _runWorkspaceService;
    private readonly Func<string, bool> _isRegisteredPageTag;
    private readonly CancellationTokenSource _cts = new();
    private bool _isDisposed;
    private bool _hasLoaded;
    private int _loadRevision;
    private bool _isRefreshing;
    private string _statusText = "Waiting for readiness sources.";
    private string _overallStatusText = "Not loaded";
    private WorkstationReadinessTone _overallTone = WorkstationReadinessTone.Neutral;
    private string _overallToneText = WorkspaceTone.Neutral;
    private string _asOfText = "Not loaded";
    private string _inboxSummaryText = "Operator inbox not loaded.";
    private string _readinessErrorText = string.Empty;
    private string _inboxErrorText = string.Empty;
    private string _breaksErrorText = string.Empty;
    private string _runsErrorText = string.Empty;

    public OperatorReadinessConsoleViewModel(
        ITradingOperatorReadinessProvider? readinessProvider = null,
        IWorkstationOperatorInboxApiClient? inboxClient = null,
        IWorkstationReconciliationApiClient? reconciliationClient = null,
        StrategyRunWorkspaceService? runWorkspaceService = null,
        Func<string, bool>? isRegisteredPageTag = null)
    {
        _readinessProvider = readinessProvider;
        _inboxClient = inboxClient;
        _reconciliationClient = reconciliationClient;
        _runWorkspaceService = runWorkspaceService;
        _isRegisteredPageTag = isRegisteredPageTag ?? (static tag => ShellNavigationCatalog.GetPage(tag) is not null);
        RefreshCommand = new AsyncRelayCommand(
            () => _isDisposed ? Task.CompletedTask : RefreshAsync(_cts.Token),
            () => !IsRefreshing);
        OpenWorkItemCommand = new RelayCommand<OperatorReadinessWorkItemRowModel>(
            OpenWorkItem,
            static row => row is not null && !string.IsNullOrWhiteSpace(row.TargetPageTag));
        OpenPageCommand = new RelayCommand<string>(
            OpenPage,
            static tag => !string.IsNullOrWhiteSpace(tag));
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand<OperatorReadinessWorkItemRowModel> OpenWorkItemCommand { get; }

    public IRelayCommand<string> OpenPageCommand { get; }

    public ObservableCollection<OperatorReadinessFactModel> SummaryFacts { get; } = [];

    public ObservableCollection<OperatorReadinessGateRowModel> GateRows { get; } = [];

    public ObservableCollection<OperatorReadinessPanelRowModel> SessionRows { get; } = [];

    public ObservableCollection<OperatorReadinessPanelRowModel> TrustRows { get; } = [];

    public ObservableCollection<OperatorReadinessPanelRowModel> PromotionRows { get; } = [];

    public ObservableCollection<OperatorReadinessPanelRowModel> RunRows { get; } = [];

    public ObservableCollection<OperatorReadinessPanelRowModel> BreakRows { get; } = [];

    public ObservableCollection<OperatorReadinessWorkItemRowModel> WorkItemRows { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    public string Title => "Operator readiness";

    public string Subtitle => "Review cross-workspace readiness gates and operator work items.";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string OverallStatusText
    {
        get => _overallStatusText;
        private set => SetProperty(ref _overallStatusText, value);
    }

    public WorkstationReadinessTone OverallTone
    {
        get => _overallTone;
        private set => SetProperty(ref _overallTone, value);
    }

    public string OverallToneText
    {
        get => _overallToneText;
        private set => SetProperty(ref _overallToneText, value);
    }

    public string AsOfText
    {
        get => _asOfText;
        private set => SetProperty(ref _asOfText, value);
    }

    public string InboxSummaryText
    {
        get => _inboxSummaryText;
        private set => SetProperty(ref _inboxSummaryText, value);
    }

    public string ReadinessErrorText
    {
        get => _readinessErrorText;
        private set
        {
            if (SetProperty(ref _readinessErrorText, value))
            {
                OnPropertyChanged(nameof(HasReadinessError));
            }
        }
    }

    public bool HasReadinessError => !string.IsNullOrWhiteSpace(ReadinessErrorText);

    public string InboxErrorText
    {
        get => _inboxErrorText;
        private set
        {
            if (SetProperty(ref _inboxErrorText, value))
            {
                OnPropertyChanged(nameof(HasInboxError));
            }
        }
    }

    public bool HasInboxError => !string.IsNullOrWhiteSpace(InboxErrorText);

    public string BreaksErrorText
    {
        get => _breaksErrorText;
        private set
        {
            if (SetProperty(ref _breaksErrorText, value))
            {
                OnPropertyChanged(nameof(HasBreaksError));
            }
        }
    }

    public bool HasBreaksError => !string.IsNullOrWhiteSpace(BreaksErrorText);

    public string RunsErrorText
    {
        get => _runsErrorText;
        private set
        {
            if (SetProperty(ref _runsErrorText, value))
            {
                OnPropertyChanged(nameof(HasRunsError));
            }
        }
    }

    public bool HasRunsError => !string.IsNullOrWhiteSpace(RunsErrorText);

    public void Activate()
    {
        if (!_hasLoaded && !IsRefreshing && !_isDisposed)
        {
            _ = RefreshAsync(_cts.Token);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The token source was already disposed; nothing to cancel.
            LoggingService.Instance.LogDebug(
                "Ignored cancel on already-disposed token source.",
                ("view", nameof(OperatorReadinessConsoleViewModel)));
        }

        _cts.Dispose();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (_isDisposed)
        {
            return;
        }

        // A newer refresh supersedes any in-flight one; superseded continuations stop before
        // applying state (same pattern as the Evidence Workbench view model).
        var revision = ++_loadRevision;
        IsRefreshing = true;
        StatusText = "Loading readiness sources.";
        try
        {
            var readinessTask = LoadReadinessAsync(ct);
            var inboxTask = LoadInboxAsync(ct);
            var breaksTask = LoadBreaksAsync(ct);
            var runsTask = LoadRunSummaryAsync(ct);

            var (readiness, readinessError) = await readinessTask.ConfigureAwait(true);
            var (inbox, inboxError) = await inboxTask.ConfigureAwait(true);
            var (breaks, breaksError) = await breaksTask.ConfigureAwait(true);
            var (runSummary, runsError) = await runsTask.ConfigureAwait(true);
            if (IsStale(revision))
            {
                return;
            }

            ReadinessErrorText = readinessError;
            InboxErrorText = inboxError;
            BreaksErrorText = breaksError;
            RunsErrorText = runsError;

            ApplyReadiness(readiness);
            ApplyInbox(inbox);
            ApplyOverallStatus(readiness, inbox, breaks);
            ApplyRows(BreakRows, breaks is null ? [] : OperatorReadinessConsoleMapper.BuildBreakRows(breaks));
            ApplyRows(RunRows, runSummary is null ? [] : OperatorReadinessConsoleMapper.BuildRunRows(runSummary));
            ApplyWorkItems(readiness, inbox);
            ApplySummaryFacts(readiness, inbox, breaks, runSummary);

            StatusText = $"Readiness sources refreshed {OperatorReadinessConsoleMapper.FormatTimestamp(DateTimeOffset.UtcNow)}.";
            _hasLoaded = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Navigation or disposal cancelled the in-flight refresh; leave current state as-is.
            // A foreign cancellation from a source (ct not requested) falls through to the
            // generic handler below so it surfaces instead of being silently swallowed.
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Operator readiness console refresh failed.", ex);
            if (!IsStale(revision))
            {
                StatusText = "Operator readiness console failed to load.";
                ReadinessErrorText = ex.Message;
            }
        }
        finally
        {
            if (revision == _loadRevision)
            {
                IsRefreshing = false;
            }
        }
    }

    private async Task<(TradingOperatorReadinessDto? Readiness, string Error)> LoadReadinessAsync(CancellationToken ct)
    {
        if (_readinessProvider is null)
        {
            return (null, "Trading readiness service is not available in this session.");
        }

        try
        {
            return (await _readinessProvider.GetAsync(fundAccountId: null, ct).ConfigureAwait(true), string.Empty);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Trading readiness load failed.", ex);
            return (null, ex.Message);
        }
    }

    private async Task<(OperatorInboxDto? Inbox, string Error)> LoadInboxAsync(CancellationToken ct)
    {
        if (_inboxClient is null)
        {
            return (null, "Operator inbox client is not available in this session.");
        }

        try
        {
            var inbox = await _inboxClient.GetInboxAsync(fundAccountId: null, ct).ConfigureAwait(true);
            return inbox is null
                ? (null, "Operator inbox failed to load from the shared workstation API.")
                : (inbox, string.Empty);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Operator inbox load failed.", ex);
            return (null, ex.Message);
        }
    }

    private async Task<(IReadOnlyList<ReconciliationBreakQueueItem>? Breaks, string Error)> LoadBreaksAsync(CancellationToken ct)
    {
        if (_reconciliationClient is null)
        {
            return (null, "Reconciliation client is not available in this session.");
        }

        try
        {
            var breaks = await _reconciliationClient.GetBreakQueueAsync(ct).ConfigureAwait(true);
            return breaks is null
                ? (null, "Reconciliation break queue failed to load from the shared workstation API.")
                : (breaks, string.Empty);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Reconciliation break queue load failed.", ex);
            return (null, ex.Message);
        }
    }

    private async Task<(StrategyWorkspaceSummary? Summary, string Error)> LoadRunSummaryAsync(CancellationToken ct)
    {
        if (_runWorkspaceService is null)
        {
            return (null, "Strategy run summaries are not available in this session.");
        }

        try
        {
            return (await _runWorkspaceService.GetStrategySummaryAsync(ct).ConfigureAwait(true), string.Empty);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Strategy run summary load failed.", ex);
            return (null, ex.Message);
        }
    }

    private void ApplyReadiness(TradingOperatorReadinessDto? readiness)
    {
        if (readiness is null)
        {
            AsOfText = "Not loaded";
            ApplyRows(GateRows, []);
            ApplyRows(SessionRows, []);
            ApplyRows(TrustRows, []);
            ApplyRows(PromotionRows, []);
            Warnings.Clear();
            return;
        }

        AsOfText = OperatorReadinessConsoleMapper.FormatTimestamp(readiness.AsOf);
        ApplyRows(GateRows, OperatorReadinessConsoleMapper.BuildGateRows(readiness));
        ApplyRows(SessionRows, OperatorReadinessConsoleMapper.BuildSessionRows(readiness));
        ApplyRows(TrustRows, OperatorReadinessConsoleMapper.BuildTrustRows(readiness));
        ApplyRows(PromotionRows, OperatorReadinessConsoleMapper.BuildPromotionRows(readiness));

        Warnings.Clear();
        foreach (var warning in readiness.Warnings)
        {
            Warnings.Add(warning);
        }
    }

    private void ApplyInbox(OperatorInboxDto? inbox)
        => InboxSummaryText = inbox is null
            ? "Operator inbox not loaded."
            : $"{inbox.CriticalCount} critical · {inbox.WarningCount} warning · {inbox.ReviewCount} review — {inbox.Summary}";

    /// <summary>
    /// Derives the headline status from the readiness payload escalated by the operator inbox and
    /// the reconciliation break queue, mirroring the browser console: critical inbox items, a
    /// blocked acceptance gate, or an open reconciliation break force "Blocked" even when the
    /// server overall status is Ready (the browser folds break rows into its reconciliation-clear
    /// checkpoint), and a Ready status is demoted to "Review pending" while the inbox is
    /// unavailable or breaks are still in review. The server's raw overall status stays visible
    /// in the readiness-gates panel.
    /// </summary>
    private void ApplyOverallStatus(
        TradingOperatorReadinessDto? readiness,
        OperatorInboxDto? inbox,
        IReadOnlyList<ReconciliationBreakQueueItem>? breaks)
    {
        var criticalCount = inbox?.CriticalCount ?? 0;
        var hasOpenBreak = breaks?.Any(static item => item.Status == ReconciliationBreakQueueStatus.Open) == true;
        var hasBreakInReview = breaks?.Any(static item => item.Status == ReconciliationBreakQueueStatus.InReview) == true;
        if (readiness is null)
        {
            // A missing readiness payload is a review state, not a neutral one — the operator
            // still has to act on the outage (the browser console makes the same distinction).
            SetOverallStatus(
                criticalCount > 0 ? "Blocked" : "Unavailable",
                criticalCount > 0 ? WorkstationReadinessTone.Blocked : WorkstationReadinessTone.SignoffRequired);
            return;
        }

        var serverLabel = OperatorReadinessConsoleMapper.FormatStatus(readiness.OverallStatus);
        var anyGateBlocked = readiness.AcceptanceGates.Any(static gate => gate.Status == TradingAcceptanceGateStatusDto.Blocked);
        if (readiness.OverallStatus == TradingAcceptanceGateStatusDto.Blocked || criticalCount > 0 || anyGateBlocked || hasOpenBreak)
        {
            SetOverallStatus("Blocked", WorkstationReadinessTone.Blocked);
            return;
        }

        var allGatesReady = readiness.AcceptanceGates.All(static gate => gate.Status == TradingAcceptanceGateStatusDto.Ready);
        if (readiness.OverallStatus == TradingAcceptanceGateStatusDto.Ready
            && inbox is not null
            && inbox.WarningCount == 0
            && allGatesReady
            && breaks is not null
            && !hasBreakInReview)
        {
            SetOverallStatus(serverLabel, WorkstationReadinessTone.EvidenceLinked);
            return;
        }

        SetOverallStatus(
            readiness.OverallStatus == TradingAcceptanceGateStatusDto.Ready ? "Review pending" : serverLabel,
            WorkstationReadinessTone.SignoffRequired);
    }

    private void SetOverallStatus(string statusText, WorkstationReadinessTone tone)
    {
        OverallStatusText = statusText;
        OverallTone = tone;
        OverallToneText = tone switch
        {
            WorkstationReadinessTone.Blocked => WorkspaceTone.Danger,
            WorkstationReadinessTone.SignoffRequired => WorkspaceTone.Warning,
            WorkstationReadinessTone.EvidenceLinked => WorkspaceTone.Success,
            _ => WorkspaceTone.Neutral
        };
    }

    private void ApplyWorkItems(TradingOperatorReadinessDto? readiness, OperatorInboxDto? inbox)
        => ApplyRows(
            WorkItemRows,
            OperatorReadinessConsoleMapper.BuildWorkItemRows(
                inbox?.Items ?? [],
                readiness?.WorkItems ?? [],
                _isRegisteredPageTag));

    private void ApplySummaryFacts(
        TradingOperatorReadinessDto? readiness,
        OperatorInboxDto? inbox,
        IReadOnlyList<ReconciliationBreakQueueItem>? breaks,
        StrategyWorkspaceSummary? runSummary)
        => ApplyRows(
            SummaryFacts,
            OperatorReadinessConsoleMapper.BuildSummaryFacts(readiness, inbox, breaks, runSummary));

    private static void ApplyRows<T>(ObservableCollection<T> target, IReadOnlyList<T> rows)
    {
        target.Clear();
        foreach (var row in rows)
        {
            target.Add(row);
        }
    }

    private bool IsStale(int revision) => _isDisposed || revision != _loadRevision;

    private void OpenWorkItem(OperatorReadinessWorkItemRowModel? row)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.TargetPageTag))
        {
            return;
        }

        NavigationService.Instance.NavigateTo(row.TargetPageTag);
    }

    private void OpenPage(string? pageTag)
    {
        if (string.IsNullOrWhiteSpace(pageTag))
        {
            return;
        }

        NavigationService.Instance.NavigateTo(pageTag);
    }
}
