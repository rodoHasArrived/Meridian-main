using System.Collections.ObjectModel;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.Ui.Services.Services.Accounting;
using System.Globalization;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed class AccountingCloseViewModel : Meridian.Wpf.ViewModels.BindableBase
{
    private readonly IAccountingProjectionQueryService _queryService;
    private readonly IAccountingCloseManagementService? _closeManagementService;
    private ClosePeriodPlanDto? _closePlan;
    private Guid _closeWorkflowId;
    private long _closeWorkflowVersion;
    private ClosePeriodState _closeState = ClosePeriodState.Open;
    private string _closeStateText = "Open";
    private string _trialBalanceStatusText = "Trial balance has not loaded.";
    private string _closePlanSetupStatusText = "Load a close plan before retaining governed close setup.";
    private string _closePeriodLockStatusText = "Load a close plan before locking the accounting period.";
    private decimal _closeSetupAmountThreshold;
    private decimal _closeSetupPercentThreshold;
    private string _closeSetupCurrency = "USD";
    private string _closeSetupReviewRole = "controller";
    private bool _closeSetupRequiresLateAdjustmentApproval = true;
    private string _closeSetupTaskId = string.Empty;
    private string _closeSetupTaskDisplayName = string.Empty;
    private string _closeSetupTaskOwner = string.Empty;
    private string _closeSetupTaskDueDateText = string.Empty;
    private int _closeSetupTaskRequiredApprovalCount = 1;
    private string _closeSetupTaskRequiredApprovalRole = "Controller";
    private string _closeSetupTaskRequiredEvidence = "Retained close checklist evidence";
    private string _closeSetupTaskDependsOnTaskIdsText = string.Empty;
    private string _selectedAuditDetailText = "Select a journal audit row to inspect source-event and approval linkage.";
    private SourceLinkedAuditLine? _selectedAuditLine;

    public AccountingCloseViewModel(
        IAccountingProjectionQueryService queryService,
        IAccountingCloseManagementService? closeManagementService = null)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _closeManagementService = closeManagementService;
        ConfigureClosePlanCommand = new AsyncRelayCommand(ConfigureClosePlanAsync, CanConfigureClosePlan);
        LockClosePeriodCommand = new AsyncRelayCommand(LockClosePeriodAsync, CanLockClosePeriod);
    }

    public ObservableCollection<TrialBalanceLine> TrialBalance { get; } = [];
    public ObservableCollection<RollForwardLine> RollForward { get; } = [];
    public ObservableCollection<SourceLinkedAuditLine> AuditTrail { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ClosePeriodLockIssueRows { get; } = [];

    public IAsyncRelayCommand ConfigureClosePlanCommand { get; }
    public IAsyncRelayCommand LockClosePeriodCommand { get; }

    public decimal CloseSetupAmountThreshold
    {
        get => _closeSetupAmountThreshold;
        set => SetProperty(ref _closeSetupAmountThreshold, value);
    }

    public decimal CloseSetupPercentThreshold
    {
        get => _closeSetupPercentThreshold;
        set => SetProperty(ref _closeSetupPercentThreshold, value);
    }

    public string CloseSetupCurrency
    {
        get => _closeSetupCurrency;
        set => SetProperty(ref _closeSetupCurrency, value ?? string.Empty);
    }

    public string CloseSetupReviewRole
    {
        get => _closeSetupReviewRole;
        set => SetProperty(ref _closeSetupReviewRole, value ?? string.Empty);
    }

    public bool CloseSetupRequiresLateAdjustmentApproval
    {
        get => _closeSetupRequiresLateAdjustmentApproval;
        set => SetProperty(ref _closeSetupRequiresLateAdjustmentApproval, value);
    }

    public string CloseSetupTaskId
    {
        get => _closeSetupTaskId;
        set => SetProperty(ref _closeSetupTaskId, value ?? string.Empty);
    }

    public string CloseSetupTaskDisplayName
    {
        get => _closeSetupTaskDisplayName;
        set => SetProperty(ref _closeSetupTaskDisplayName, value ?? string.Empty);
    }

    public string CloseSetupTaskOwner
    {
        get => _closeSetupTaskOwner;
        set => SetProperty(ref _closeSetupTaskOwner, value ?? string.Empty);
    }

    public string CloseSetupTaskDueDateText
    {
        get => _closeSetupTaskDueDateText;
        set => SetProperty(ref _closeSetupTaskDueDateText, value ?? string.Empty);
    }

    public int CloseSetupTaskRequiredApprovalCount
    {
        get => _closeSetupTaskRequiredApprovalCount;
        set => SetProperty(ref _closeSetupTaskRequiredApprovalCount, Math.Max(1, value));
    }

    public string CloseSetupTaskRequiredApprovalRole
    {
        get => _closeSetupTaskRequiredApprovalRole;
        set => SetProperty(ref _closeSetupTaskRequiredApprovalRole, value ?? string.Empty);
    }

    public string CloseSetupTaskRequiredEvidence
    {
        get => _closeSetupTaskRequiredEvidence;
        set => SetProperty(ref _closeSetupTaskRequiredEvidence, value ?? string.Empty);
    }

    public string CloseSetupTaskDependsOnTaskIdsText
    {
        get => _closeSetupTaskDependsOnTaskIdsText;
        set => SetProperty(ref _closeSetupTaskDependsOnTaskIdsText, value ?? string.Empty);
    }

    public ClosePeriodState CloseState
    {
        get => _closeState;
        private set
        {
            if (_closeState == value)
            {
                return;
            }

            _closeState = value;
            RaisePropertyChanged();
        }
    }

    public string CloseStateText
    {
        get => _closeStateText;
        private set
        {
            if (string.Equals(_closeStateText, value, StringComparison.Ordinal))
            {
                return;
            }

            _closeStateText = value;
            RaisePropertyChanged();
        }
    }

    public string TrialBalanceStatusText
    {
        get => _trialBalanceStatusText;
        private set
        {
            if (string.Equals(_trialBalanceStatusText, value, StringComparison.Ordinal))
            {
                return;
            }

            _trialBalanceStatusText = value;
            RaisePropertyChanged();
        }
    }

    public string ClosePlanSetupStatusText
    {
        get => _closePlanSetupStatusText;
        private set
        {
            if (string.Equals(_closePlanSetupStatusText, value, StringComparison.Ordinal))
            {
                return;
            }

            _closePlanSetupStatusText = value;
            RaisePropertyChanged();
        }
    }

    public string ClosePeriodLockStatusText
    {
        get => _closePeriodLockStatusText;
        private set
        {
            if (string.Equals(_closePeriodLockStatusText, value, StringComparison.Ordinal))
            {
                return;
            }

            _closePeriodLockStatusText = value;
            RaisePropertyChanged();
        }
    }

    public SourceLinkedAuditLine? SelectedAuditLine
    {
        get => _selectedAuditLine;
        set
        {
            if (Equals(_selectedAuditLine, value))
            {
                return;
            }

            _selectedAuditLine = value;
            SelectedAuditDetailText = value is null
                ? "Select a journal audit row to inspect source-event and approval linkage."
                : $"Journal {value.JournalEntryId:D} links source {value.SourceEventId} to approval {value.ApprovalId}.";
            RaisePropertyChanged();
        }
    }

    public string SelectedAuditDetailText
    {
        get => _selectedAuditDetailText;
        private set
        {
            if (string.Equals(_selectedAuditDetailText, value, StringComparison.Ordinal))
            {
                return;
            }

            _selectedAuditDetailText = value;
            RaisePropertyChanged();
        }
    }

    public void Load(string ledgerId)
    {
        TrialBalance.Clear();
        foreach (var line in _queryService.GetTrialBalance(ledgerId))
        {
            TrialBalance.Add(line);
        }

        RollForward.Clear();
        foreach (var line in _queryService.GetRollForward(ledgerId))
        {
            RollForward.Add(line);
        }

        AuditTrail.Clear();
        foreach (var line in _queryService.GetAuditLines(ledgerId))
        {
            AuditTrail.Add(line);
        }

        TrialBalanceStatusText = TrialBalance.Count == 0
            ? "No trial-balance lines are available for the selected ledger."
            : $"{TrialBalance.Count} trial-balance line{(TrialBalance.Count == 1 ? string.Empty : "s")} loaded with source-event drill-through.";
    }

    public void ApplyCloseProjection(AccountingCloseProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        SetCloseState(projection.ClosePeriod.State);
        TrialBalanceStatusText = projection.TrialBalanceBalanced
            ? "Trial balance is balanced and eligible for close evidence review."
            : "Trial balance is out of balance; close workflow remains blocked until corrected.";
    }

    public void ApplyClosePlan(ClosePeriodPlanDto closePlan)
    {
        ApplyClosePlan(closePlan.Configuration?.WorkflowId ?? Guid.Empty, closePlan);
    }

    public void ApplyClosePlan(Guid workflowId, ClosePeriodPlanDto closePlan)
        => ApplyClosePlan(workflowId, _closeWorkflowVersion, closePlan);

    public void ApplyClosePlan(Guid workflowId, long workflowVersion, ClosePeriodPlanDto closePlan)
    {
        ArgumentNullException.ThrowIfNull(closePlan);
        _closeWorkflowId = workflowId;
        _closeWorkflowVersion = Math.Max(0, workflowVersion);
        _closePlan = closePlan;
        ApplyCloseSetupDraft(closePlan);
        ClosePlanSetupStatusText = workflowId == Guid.Empty
            ? $"Close plan {closePlan.PeriodId} loaded without workflow context; setup retention is disabled."
            : closePlan.IsPeriodLocked
            ? $"Close plan {closePlan.PeriodId} is locked; setup changes require a governed reopen workflow."
            : $"Close plan {closePlan.PeriodId} loaded for governed setup retention.";
        ClosePeriodLockStatusText = workflowId == Guid.Empty
            ? $"Close plan {closePlan.PeriodId} loaded without workflow context; period lock is disabled."
            : closePlan.IsPeriodLocked
            ? $"Close plan {closePlan.PeriodId} is already locked."
            : $"Close plan {closePlan.PeriodId} is ready for governed period-lock review.";
        ApplyClosePeriodLockIssues(closePlan.ValidationIssues);
        ConfigureClosePlanCommand.NotifyCanExecuteChanged();
        LockClosePeriodCommand.NotifyCanExecuteChanged();
    }

    public void SetCloseState(ClosePeriodState state)
    {
        CloseState = state;
        CloseStateText = state.ToString();
    }

    private bool CanConfigureClosePlan()
        => _closeManagementService is not null &&
           _closeWorkflowId != Guid.Empty &&
           _closePlan is { IsPeriodLocked: false };

    private bool CanLockClosePeriod()
        => _closeManagementService is not null &&
           _closeWorkflowId != Guid.Empty &&
           _closePlan is { IsPeriodLocked: false };

    private async Task ConfigureClosePlanAsync()
    {
        if (_closeManagementService is null)
        {
            ClosePlanSetupStatusText = "Close management service is not registered for this desktop session.";
            return;
        }

        if (_closePlan is null)
        {
            ClosePlanSetupStatusText = "Load a close plan before retaining governed close setup.";
            return;
        }

        if (_closePlan.IsPeriodLocked)
        {
            ClosePlanSetupStatusText = $"Close plan {_closePlan.PeriodId} is locked; setup changes require a governed reopen workflow.";
            return;
        }

        try
        {
            var request = BuildClosePlanConfigurationRequest(_closeWorkflowId, _closePlan);
            var updated = await _closeManagementService
                .ConfigurePeriodPlanAsync(request, "wpf-accounting-controller")
                .ConfigureAwait(true);

            if (updated is null)
            {
                ClosePlanSetupStatusText = $"Close workflow {_closeWorkflowId:D} was not found.";
                return;
            }

            ApplyClosePlan(_closeWorkflowId, _closeWorkflowVersion, updated);
            ClosePlanSetupStatusText = $"Retained close-plan setup for {updated.PeriodId}.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ClosePlanSetupStatusText = $"Close-plan setup could not be retained: {ex.Message}";
        }
    }

    private async Task LockClosePeriodAsync()
    {
        if (_closeManagementService is null)
        {
            ClosePeriodLockStatusText = "Close management service is not registered for this desktop session.";
            return;
        }

        if (_closePlan is null)
        {
            ClosePeriodLockStatusText = "Load a close plan before locking the accounting period.";
            return;
        }

        if (_closeWorkflowId == Guid.Empty)
        {
            ClosePeriodLockStatusText = $"Close plan {_closePlan.PeriodId} loaded without workflow context; period lock is disabled.";
            return;
        }

        if (_closePlan.IsPeriodLocked)
        {
            ClosePeriodLockStatusText = $"Close plan {_closePlan.PeriodId} is already locked.";
            return;
        }

        try
        {
            var request = BuildClosePeriodLockRequest(_closeWorkflowId, _closeWorkflowVersion, _closePlan);
            var result = await _closeManagementService
                .LockClosePeriodAsync(request, "wpf-accounting-controller")
                .ConfigureAwait(true);

            if (result is null)
            {
                ClosePeriodLockStatusText = $"Close workflow {_closeWorkflowId:D} was not found.";
                return;
            }

            if (result.Plan is not null)
            {
                ApplyClosePlan(_closeWorkflowId, result.Transition?.NewVersion ?? _closeWorkflowVersion, result.Plan);
            }

            ApplyClosePeriodLockIssues(result.Issues);
            ClosePeriodLockStatusText = result.IsLocked
                ? $"Locked close period {result.Plan?.PeriodId ?? _closePlan.PeriodId} with retained close-package evidence."
                : $"Close period lock is blocked by {result.Issues.Count} issue(s).";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ClosePeriodLockStatusText = $"Close-period lock could not be retained: {ex.Message}";
        }
    }

    private UpsertClosePeriodPlanConfigurationRequestDto BuildClosePlanConfigurationRequest(
        Guid workflowId,
        ClosePeriodPlanDto closePlan)
    {
        var materialityPolicy = new MaterialityPolicyDto(
            closePlan.MaterialityPolicy.PolicyId,
            CloseSetupAmountThreshold,
            CloseSetupPercentThreshold,
            NormalizeRequired(CloseSetupCurrency, closePlan.MaterialityPolicy.Currency),
            NormalizeRequired(CloseSetupReviewRole, closePlan.MaterialityPolicy.ReviewRole),
            CloseSetupRequiresLateAdjustmentApproval);
        var editableTaskId = NormalizeOptional(CloseSetupTaskId)
                             ?? closePlan.Tasks.FirstOrDefault()?.TaskId
                             ?? "close-task";
        var editableTaskDueDate = ParseCloseSetupDueDate(CloseSetupTaskDueDateText);
        var editableTaskDependencies = ParseCloseSetupDependencies(CloseSetupTaskDependsOnTaskIdsText);
        var taskConfigurations = closePlan.Tasks
            .Select(task =>
            {
                var requiredApprovalCount = Math.Max(
                    1,
                    task.SignOffRequirements.Count == 0
                        ? 1
                        : task.SignOffRequirements.Max(static requirement => requirement.RequiredApprovalCount));
                var requiredEvidence = string.Join(
                    "; ",
                    task.SignOffRequirements
                        .Select(static requirement => requirement.EvidenceRequirement.Trim())
                        .Where(static value => value.Length > 0));

                if (!string.Equals(task.TaskId, editableTaskId, StringComparison.OrdinalIgnoreCase))
                {
                    return new CloseTaskConfigurationDto(
                        task.TaskId,
                        task.DisplayName,
                        task.Owner,
                        task.DueDate,
                        requiredApprovalCount,
                        task.SignOffRequirements.FirstOrDefault()?.Role,
                        string.IsNullOrWhiteSpace(requiredEvidence) ? "Retained close checklist evidence" : requiredEvidence,
                        task.Dependencies.Select(static dependency => dependency.DependsOnTaskId).ToArray());
                }

                return new CloseTaskConfigurationDto(
                    task.TaskId,
                    NormalizeOptional(CloseSetupTaskDisplayName) ?? task.DisplayName,
                    NormalizeOptional(CloseSetupTaskOwner) ?? task.Owner,
                    editableTaskDueDate ?? task.DueDate,
                    Math.Max(1, CloseSetupTaskRequiredApprovalCount),
                    NormalizeOptional(CloseSetupTaskRequiredApprovalRole)
                        ?? task.SignOffRequirements.FirstOrDefault()?.Role
                        ?? task.Owner,
                    NormalizeOptional(CloseSetupTaskRequiredEvidence)
                        ?? (string.IsNullOrWhiteSpace(requiredEvidence) ? "Retained close checklist evidence" : requiredEvidence),
                    editableTaskDependencies);
            })
            .ToArray();

        return new UpsertClosePeriodPlanConfigurationRequestDto(
            workflowId,
            materialityPolicy,
            taskConfigurations,
            Actor: "wpf-accounting-controller",
            EvidenceLinks: BuildClosePlanConfigurationEvidence(workflowId, closePlan),
            CorrelationId: $"wpf-close-plan-configuration-{workflowId:D}",
            ActionOrigin: OperationsActionOriginDto.HumanOperator);
    }

    private void ApplyCloseSetupDraft(ClosePeriodPlanDto closePlan)
    {
        var materiality = closePlan.MaterialityPolicy;
        CloseSetupAmountThreshold = materiality.AmountThreshold;
        CloseSetupPercentThreshold = materiality.PercentThreshold;
        CloseSetupCurrency = materiality.Currency;
        CloseSetupReviewRole = materiality.ReviewRole;
        CloseSetupRequiresLateAdjustmentApproval = materiality.RequiresLateAdjustmentApproval;

        var task = closePlan.Tasks.FirstOrDefault();
        if (task is null)
        {
            CloseSetupTaskId = string.Empty;
            CloseSetupTaskDisplayName = string.Empty;
            CloseSetupTaskOwner = string.Empty;
            CloseSetupTaskDueDateText = string.Empty;
            CloseSetupTaskRequiredApprovalCount = 1;
            CloseSetupTaskRequiredApprovalRole = "Controller";
            CloseSetupTaskRequiredEvidence = "Retained close checklist evidence";
            CloseSetupTaskDependsOnTaskIdsText = string.Empty;
            return;
        }

        var requiredApprovalCount = Math.Max(
            1,
            task.SignOffRequirements.Count == 0
                ? 1
                : task.SignOffRequirements.Max(static requirement => requirement.RequiredApprovalCount));
        var requiredEvidence = string.Join(
            "; ",
            task.SignOffRequirements
                .Select(static requirement => requirement.EvidenceRequirement.Trim())
                .Where(static value => value.Length > 0));

        CloseSetupTaskId = task.TaskId;
        CloseSetupTaskDisplayName = task.DisplayName;
        CloseSetupTaskOwner = task.Owner;
        CloseSetupTaskDueDateText = task.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        CloseSetupTaskRequiredApprovalCount = requiredApprovalCount;
        CloseSetupTaskRequiredApprovalRole = task.SignOffRequirements.FirstOrDefault()?.Role ?? task.Owner;
        CloseSetupTaskRequiredEvidence = string.IsNullOrWhiteSpace(requiredEvidence)
            ? "Retained close checklist evidence"
            : requiredEvidence;
        CloseSetupTaskDependsOnTaskIdsText = string.Join(", ", task.Dependencies.Select(static dependency => dependency.DependsOnTaskId));
    }

    private static string NormalizeRequired(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateOnly? ParseCloseSetupDueDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dueDate))
        {
            return dueDate;
        }

        throw new ArgumentException("Close task due date must use yyyy-MM-dd format.", nameof(CloseSetupTaskDueDateText));
    }

    private static IReadOnlyList<string> ParseCloseSetupDependencies(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static dependency => dependency.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private static IReadOnlyList<string> BuildClosePlanConfigurationEvidence(
        Guid workflowId,
        ClosePeriodPlanDto closePlan)
    {
        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"wpf://accounting/close/setup/{workflowId:D}",
            $"evidence://close-plan-configuration/fund/{closePlan.FundProfileId}/period/{closePlan.PeriodId}"
        };

        if (closePlan.LedgerBookId is { } ledgerBookId)
        {
            links.Add($"evidence://close-plan-configuration/ledger-book/{ledgerBookId}");
        }

        foreach (var task in closePlan.Tasks)
        {
            foreach (var evidence in task.EvidenceLinks)
            {
                if (!string.IsNullOrWhiteSpace(evidence))
                {
                    links.Add(evidence.Trim());
                }
            }
        }

        foreach (var adjustment in closePlan.LateAdjustments)
        {
            foreach (var evidence in adjustment.EvidenceLinks)
            {
                if (!string.IsNullOrWhiteSpace(evidence))
                {
                    links.Add(evidence.Trim());
                }
            }
        }

        return links.ToArray();
    }

    private static LockClosePeriodRequestDto BuildClosePeriodLockRequest(
        Guid workflowId,
        long workflowVersion,
        ClosePeriodPlanDto closePlan)
    {
        var reportPackId = BuildCloseReportPackId(closePlan);
        var closePackageId = $"close-package-{closePlan.FundProfileId}-{closePlan.PeriodId}";
        var manifestId = $"manifest-{closePlan.FundProfileId}-{closePlan.PeriodId}";
        return new LockClosePeriodRequestDto(
            workflowId,
            ExpectedWorkflowVersion: workflowVersion,
            Actor: "wpf-accounting-controller",
            Rationale: "Lock close period from WPF Accounting Close after checklist, sign-off, reconciliation, and report certification review.",
            ReportPackId: reportPackId,
            EvidenceLinks: BuildClosePeriodLockEvidence(workflowId, closePlan, reportPackId, closePackageId, manifestId),
            ChecklistControlApprovals: BuildClosePeriodLockApprovals(closePlan),
            CorrelationId: $"wpf-close-period-lock-{workflowId:D}",
            ClosePackageId: closePackageId,
            ClosePackageManifestId: manifestId,
            ClosePackageRetainedManifestRoute: $"/workstation/reporting/packages/{manifestId}",
            ActionOrigin: OperationsActionOriginDto.HumanOperator);
    }

    private static IReadOnlyList<OperationsChecklistControlApprovalDto> BuildClosePeriodLockApprovals(
        ClosePeriodPlanDto closePlan)
        => closePlan.Tasks
            .SelectMany(static task => task.SignOffs
                .Where(static signOff => signOff.ApprovalState == ManualJournalEntryStatusDto.Approved)
                .Select(signOff => new OperationsChecklistControlApprovalDto(
                    task.TaskId,
                    string.IsNullOrWhiteSpace(signOff.Actor) ? "wpf-accounting-controller" : signOff.Actor.Trim(),
                    signOff.SignedAtUtc ?? DateTimeOffset.UtcNow)))
            .ToArray();

    private static IReadOnlyList<string> BuildClosePeriodLockEvidence(
        Guid workflowId,
        ClosePeriodPlanDto closePlan,
        string reportPackId,
        string closePackageId,
        string manifestId)
    {
        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"wpf://accounting/close/period-lock/{workflowId:D}",
            $"evidence://close-package/{closePackageId}/workflow/{workflowId:D}/period/{closePlan.PeriodId}/report-pack/{reportPackId}/manifest/{manifestId}/period-lock"
        };

        if (closePlan.LedgerBookId is { } ledgerBookId)
        {
            links.Add($"evidence://close-package/{closePackageId}/book/{ledgerBookId:D}/period/{closePlan.PeriodId}/report-pack/{reportPackId}/period-lock");
        }

        foreach (var task in closePlan.Tasks)
        {
            foreach (var evidence in task.EvidenceLinks)
            {
                if (!string.IsNullOrWhiteSpace(evidence))
                {
                    links.Add(evidence.Trim());
                }
            }

            foreach (var signOff in task.SignOffs)
            {
                foreach (var evidence in signOff.EvidenceLinks)
                {
                    if (!string.IsNullOrWhiteSpace(evidence))
                    {
                        links.Add(evidence.Trim());
                    }
                }
            }
        }

        foreach (var adjustment in closePlan.LateAdjustments)
        {
            foreach (var evidence in adjustment.EvidenceLinks)
            {
                if (!string.IsNullOrWhiteSpace(evidence))
                {
                    links.Add(evidence.Trim());
                }
            }
        }

        return links.ToArray();
    }

    private static string BuildCloseReportPackId(ClosePeriodPlanDto closePlan)
        => $"report-pack-{closePlan.FundProfileId}-{closePlan.PeriodId}";

    private void ApplyClosePeriodLockIssues(IReadOnlyList<AccountingConfigurationValidationIssueDto> issues)
    {
        ClosePeriodLockIssueRows.Clear();
        foreach (var issue in issues)
        {
            ClosePeriodLockIssueRows.Add(new AccountingWorkbenchRow(
                issue.Code,
                issue.Severity.ToString(),
                issue.Message,
                issue.SuggestedAction ?? "Resolve the close blocker before locking the period.",
                issue.TargetId ?? string.Empty));
        }
    }
}
