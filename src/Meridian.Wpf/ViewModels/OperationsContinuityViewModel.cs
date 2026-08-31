using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Dedicated operations continuity page: workflow queue with selected-workflow detail (gates,
/// blockers, checklist, server-recommended next action), the unified open-item operator queue,
/// and the promoted close-calendar and approval-policy cards that previously lived on a Settings
/// tab. Read-only in this wave — approval, close, and reopen commands remain browser-first.
/// Sources that are unavailable degrade into per-panel error text rather than empty-looking rows.
/// </summary>
public sealed class OperationsContinuityViewModel : BindableBase, IDisposable
{
    private readonly IOperationsControlCenterClient? _client;
    private readonly CancellationTokenSource _cts = new();
    private bool _isDisposed;
    private bool _hasLoaded;
    private int _loadRevision;
    private bool _isRefreshing;
    private Guid? _selectedWorkflowId;
    private string _statusText = "Waiting for continuity sources.";
    private string _workflowsErrorText = string.Empty;
    private string _detailErrorText = string.Empty;
    private string _calendarErrorText = string.Empty;
    private string _policyErrorText = string.Empty;
    private string _policySummaryText = "Approval policy matrix not loaded.";
    private string _calendarSummaryText = "Close calendar not loaded.";
    private OperationsContinuityNextActionModel _nextAction =
        OperationsContinuityMapper.ResolveNextAction(null, isLoading: false, detailError: null);
    private OperationsContinuityQueueRollupModel _queueRollup =
        OperationsContinuityMapper.BuildQueueRollup(isLoading: true, []);

    public OperationsContinuityViewModel(IOperationsControlCenterClient? client = null)
    {
        _client = client;
        RefreshCommand = new AsyncRelayCommand(
            () => _isDisposed ? Task.CompletedTask : RefreshAsync(_cts.Token),
            () => !IsRefreshing);
        SelectWorkflowCommand = new AsyncRelayCommand<OperationsContinuityWorkflowRowModel>(
            row => row is null || _isDisposed ? Task.CompletedTask : SelectWorkflowAsync(row.WorkflowId, _cts.Token));
        OpenPageCommand = new RelayCommand<string>(
            OpenPage,
            static tag => !string.IsNullOrWhiteSpace(tag));
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand<OperationsContinuityWorkflowRowModel> SelectWorkflowCommand { get; }

    public IRelayCommand<string> OpenPageCommand { get; }

    public ObservableCollection<OperationsContinuityWorkflowRowModel> WorkflowRows { get; } = [];

    public ObservableCollection<OperationsContinuityPanelRowModel> GateRows { get; } = [];

    public ObservableCollection<OperationsContinuityPanelRowModel> BlockerRows { get; } = [];

    public ObservableCollection<OperationsContinuityPanelRowModel> ChecklistRows { get; } = [];

    public ObservableCollection<OperationsContinuityPanelRowModel> QueueRows { get; } = [];

    public ObservableCollection<OperationsContinuityPanelRowModel> CloseCalendarRows { get; } = [];

    public ObservableCollection<SettingsOperationsApprovalPolicyRow> ApprovalPolicyRows { get; } = [];

    public string Title => "Operations continuity";

    public string Subtitle => "Review continuity workflows, gates, close calendar, and governed approval policy.";

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

    public Guid? SelectedWorkflowId
    {
        get => _selectedWorkflowId;
        private set => SetProperty(ref _selectedWorkflowId, value);
    }

    public OperationsContinuityNextActionModel NextAction
    {
        get => _nextAction;
        private set => SetProperty(ref _nextAction, value);
    }

    public OperationsContinuityQueueRollupModel QueueRollup
    {
        get => _queueRollup;
        private set => SetProperty(ref _queueRollup, value);
    }

    public string PolicySummaryText
    {
        get => _policySummaryText;
        private set => SetProperty(ref _policySummaryText, value);
    }

    public string CalendarSummaryText
    {
        get => _calendarSummaryText;
        private set => SetProperty(ref _calendarSummaryText, value);
    }

    public string WorkflowsErrorText
    {
        get => _workflowsErrorText;
        private set
        {
            if (SetProperty(ref _workflowsErrorText, value))
            {
                OnPropertyChanged(nameof(HasWorkflowsError));
            }
        }
    }

    public bool HasWorkflowsError => !string.IsNullOrWhiteSpace(WorkflowsErrorText);

    public string DetailErrorText
    {
        get => _detailErrorText;
        private set
        {
            if (SetProperty(ref _detailErrorText, value))
            {
                OnPropertyChanged(nameof(HasDetailError));
            }
        }
    }

    public bool HasDetailError => !string.IsNullOrWhiteSpace(DetailErrorText);

    public string CalendarErrorText
    {
        get => _calendarErrorText;
        private set
        {
            if (SetProperty(ref _calendarErrorText, value))
            {
                OnPropertyChanged(nameof(HasCalendarError));
            }
        }
    }

    public bool HasCalendarError => !string.IsNullOrWhiteSpace(CalendarErrorText);

    public string PolicyErrorText
    {
        get => _policyErrorText;
        private set
        {
            if (SetProperty(ref _policyErrorText, value))
            {
                OnPropertyChanged(nameof(HasPolicyError));
            }
        }
    }

    public bool HasPolicyError => !string.IsNullOrWhiteSpace(PolicyErrorText);

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
            LoggingService.Instance.LogDebug(
                "Ignored cancel on already-disposed token source.",
                ("view", nameof(OperationsContinuityViewModel)));
        }

        _cts.Dispose();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (_isDisposed)
        {
            return;
        }

        var revision = ++_loadRevision;
        IsRefreshing = true;
        StatusText = "Loading continuity sources.";
        try
        {
            var workflowsTask = LoadWorkflowsAsync(ct);
            var calendarTask = LoadCalendarAsync(ct);
            var policyTask = LoadPolicyAsync(ct);

            var (workflows, workflowsError) = await workflowsTask.ConfigureAwait(true);
            var (calendar, calendarError) = await calendarTask.ConfigureAwait(true);
            var (policy, policyError) = await policyTask.ConfigureAwait(true);
            if (IsStale(revision))
            {
                return;
            }

            WorkflowsErrorText = workflowsError;
            CalendarErrorText = calendarError;
            PolicyErrorText = policyError;

            ApplyRows(WorkflowRows, workflows is null ? [] : OperationsContinuityMapper.BuildWorkflowRows(workflows));
            ApplyRows(CloseCalendarRows, OperationsContinuityMapper.BuildCloseCalendarRows(calendar));
            CalendarSummaryText = calendar is null
                ? "Close calendar is unavailable."
                : $"{CloseCalendarRows.Count} close item(s), {CloseCalendarRows.Count(static row => row.ReadinessTone == WorkstationReadinessTone.EvidenceLinked)} ready, {CloseCalendarRows.Count(static row => row.ReadinessTone == WorkstationReadinessTone.Blocked)} blocked.";
            ApplyPolicy(policy);

            var selectedId = WorkflowRows.FirstOrDefault(row => row.WorkflowId == SelectedWorkflowId)?.WorkflowId
                ?? WorkflowRows.FirstOrDefault()?.WorkflowId;
            OperationsContinuityWorkflowDto? detail = null;
            var detailError = string.Empty;
            if (selectedId is not null)
            {
                (detail, detailError) = await LoadDetailAsync(selectedId.Value, ct).ConfigureAwait(true);
                if (IsStale(revision))
                {
                    return;
                }
            }

            SelectedWorkflowId = selectedId;
            DetailErrorText = detailError;
            ApplyDetail(detail);

            StatusText = $"Continuity sources refreshed {OperationsContinuityMapper.FormatTimestamp(DateTimeOffset.UtcNow)}.";
            _hasLoaded = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Navigation or disposal cancelled the in-flight refresh; leave current state as-is.
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Operations continuity refresh failed.", ex);
            if (!IsStale(revision))
            {
                StatusText = "Operations continuity failed to load.";
                WorkflowsErrorText = ex.Message;
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

    public async Task SelectWorkflowAsync(Guid workflowId, CancellationToken ct = default)
    {
        if (_isDisposed)
        {
            return;
        }

        var revision = ++_loadRevision;
        SelectedWorkflowId = workflowId;
        var (detail, detailError) = await LoadDetailAsync(workflowId, ct).ConfigureAwait(true);
        if (IsStale(revision))
        {
            return;
        }

        DetailErrorText = detailError;
        ApplyDetail(detail);
    }

    private async Task<(IReadOnlyList<OperationsContinuityWorkflowSummaryDto>? Workflows, string Error)> LoadWorkflowsAsync(CancellationToken ct)
    {
        if (_client is null)
        {
            return (null, "Operations continuity client is not available in this session.");
        }

        try
        {
            var workflows = await _client.GetWorkflowsAsync(ct).ConfigureAwait(true);
            return workflows is null
                ? (null, "Continuity workflows failed to load from the shared workstation API.")
                : (workflows, string.Empty);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Continuity workflow list load failed.", ex);
            return (null, ex.Message);
        }
    }

    private async Task<(OperationsContinuityWorkflowDto? Detail, string Error)> LoadDetailAsync(Guid workflowId, CancellationToken ct)
    {
        if (_client is null)
        {
            return (null, "Operations continuity client is not available in this session.");
        }

        try
        {
            var detail = await _client.GetWorkflowAsync(workflowId, ct).ConfigureAwait(true);
            return detail is null
                ? (null, "The selected workflow detail failed to load from the shared workstation API.")
                : (detail, string.Empty);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Continuity workflow detail load failed.", ex);
            return (null, ex.Message);
        }
    }

    private async Task<(OperationsCloseCalendarDto? Calendar, string Error)> LoadCalendarAsync(CancellationToken ct)
    {
        if (_client is null)
        {
            return (null, "Operations continuity client is not available in this session.");
        }

        try
        {
            var calendar = await _client.GetCloseCalendarAsync(ct).ConfigureAwait(true);
            return calendar is null
                ? (null, "Close calendar failed to load from the shared workstation API.")
                : (calendar, string.Empty);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Close calendar load failed.", ex);
            return (null, ex.Message);
        }
    }

    private async Task<(OperationsApprovalPolicyMatrixDto? Policy, string Error)> LoadPolicyAsync(CancellationToken ct)
    {
        if (_client is null)
        {
            return (null, "Operations continuity client is not available in this session.");
        }

        try
        {
            var policy = await _client.GetApprovalPolicyMatrixAsync(ct).ConfigureAwait(true);
            return policy is null
                ? (null, "Approval policy matrix failed to load from the shared workstation API.")
                : (policy, string.Empty);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Approval policy matrix load failed.", ex);
            return (null, ex.Message);
        }
    }

    private void ApplyPolicy(OperationsApprovalPolicyMatrixDto? policy)
    {
        ApprovalPolicyRows.Clear();
        if (policy?.Rows is not null)
        {
            foreach (var row in policy.Rows
                         .OrderBy(static item => item.WorkflowArea, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(static item => item.Gate)
                         .ThenBy(static item => item.Action, StringComparer.OrdinalIgnoreCase))
            {
                ApprovalPolicyRows.Add(new SettingsOperationsApprovalPolicyRow(row));
            }
        }

        PolicySummaryText = policy is null
            ? "Approval policy matrix is unavailable."
            : $"{policy.PolicyId} {policy.Version}: {ApprovalPolicyRows.Count} governed approval rule(s).";
    }

    private void ApplyDetail(OperationsContinuityWorkflowDto? detail)
    {
        ApplyRows(GateRows, detail is null ? [] : OperationsContinuityMapper.BuildGateRows(detail));
        ApplyRows(BlockerRows, detail is null ? [] : OperationsContinuityMapper.BuildBlockerRows(detail));
        ApplyRows(ChecklistRows, detail is null ? [] : OperationsContinuityMapper.BuildChecklistRows(detail));
        ApplyRows(QueueRows, OperationsContinuityMapper.BuildQueueRows(detail, [.. CloseCalendarRows]));
        QueueRollup = OperationsContinuityMapper.BuildQueueRollup(isLoading: false, [.. QueueRows]);
        NextAction = OperationsContinuityMapper.ResolveNextAction(
            detail,
            isLoading: false,
            detailError: HasDetailError ? DetailErrorText : null);
    }

    private static void ApplyRows<T>(ObservableCollection<T> target, IReadOnlyList<T> rows)
    {
        target.Clear();
        foreach (var row in rows)
        {
            target.Add(row);
        }
    }

    private bool IsStale(int revision) => _isDisposed || revision != _loadRevision;

    private void OpenPage(string? pageTag)
    {
        if (string.IsNullOrWhiteSpace(pageTag))
        {
            return;
        }

        NavigationService.Instance.NavigateTo(pageTag);
    }
}
