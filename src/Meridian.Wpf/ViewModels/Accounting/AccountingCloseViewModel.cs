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
    private ClosePostingGateDto? _closingEntriesGate;
    private Guid _closeWorkflowId;
    private long _closeWorkflowVersion;
    private ClosePeriodState _closeState = ClosePeriodState.Open;
    private string _closeStateText = "Open";
    private string _trialBalanceStatusText = "Trial balance has not loaded.";
    private string _evidencePackageStatusText = "Close evidence package has not loaded.";
    private string _closePlanSetupStatusText = "Load a close plan before retaining governed close setup.";
    private string _closePeriodLockStatusText = "Load a close plan before locking the accounting period.";
    private string _closeTaskSignOffStatusText = "Load a close plan before retaining close task sign-off evidence.";
    private string _lateAdjustmentRequestStatusText = "Load a close plan before requesting late adjustments.";
    private string _lateAdjustmentReviewStatusText = "Load a close plan before reviewing late adjustments.";
    private string _closeEvidenceReviewStatusText = "Load a close plan before retaining blocker/evidence review.";
    private string _closeWorkflowIdText = string.Empty;
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
    private string _closeSetupTaskSignOffRequirementsText = string.Empty;
    private string _closeSetupTaskDependsOnTaskIdsText = string.Empty;
    private string _closeSetupTaskDependencyReason = "Configured close-plan dependency.";
    private string _selectedCloseSetupTaskId = string.Empty;
    private string _closeTaskSignOffTaskId = string.Empty;
    private string _closeTaskSignOffRole = string.Empty;
    private string _closeTaskSignOffDecision = ManualJournalEntryStatusDto.Approved.ToString();
    private string _closeTaskSignOffNotes = string.Empty;
    private string _lateAdjustmentJournalEntryIdText = string.Empty;
    private string _lateAdjustmentAmountText = string.Empty;
    private string _lateAdjustmentCurrency = "USD";
    private string _lateAdjustmentReason = string.Empty;
    private string _lateAdjustmentReviewRequestId = string.Empty;
    private string _lateAdjustmentReviewDecision = ManualJournalEntryStatusDto.Approved.ToString();
    private string _lateAdjustmentReviewNotes = string.Empty;
    private string _closeEvidenceReviewIssueCode = string.Empty;
    private string _closeEvidenceReviewTargetId = string.Empty;
    private string _closeEvidenceReviewNotes = string.Empty;
    private string _selectedAuditDetailText = "Select a journal audit row to inspect source-event and approval linkage.";
    private SourceLinkedAuditLine? _selectedAuditLine;

    public AccountingCloseViewModel(
        IAccountingProjectionQueryService queryService,
        IAccountingCloseManagementService? closeManagementService = null)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _closeManagementService = closeManagementService;
        LoadClosePlanCommand = new AsyncRelayCommand(LoadClosePlanAsync, CanLoadClosePlan);
        ConfigureClosePlanCommand = new AsyncRelayCommand(ConfigureClosePlanAsync, CanConfigureClosePlan);
        SignOffCloseTaskCommand = new AsyncRelayCommand(SignOffCloseTaskAsync, CanSignOffCloseTask);
        RequestLateAdjustmentCommand = new AsyncRelayCommand(RequestLateAdjustmentAsync, CanRequestLateAdjustment);
        ReviewLateAdjustmentCommand = new AsyncRelayCommand(ReviewLateAdjustmentAsync, CanReviewLateAdjustment);
        ReviewCloseEvidenceCommand = new AsyncRelayCommand(ReviewCloseEvidenceAsync, CanReviewCloseEvidence);
        QueueClosingEntriesCommand = new AsyncRelayCommand(QueueClosingEntriesAsync, CanQueueClosingEntries);
        LockClosePeriodCommand = new AsyncRelayCommand(LockClosePeriodAsync, CanLockClosePeriod);
    }

    public ObservableCollection<TrialBalanceLine> TrialBalance { get; } = [];
    public ObservableCollection<RollForwardLine> RollForward { get; } = [];
    public ObservableCollection<SourceLinkedAuditLine> AuditTrail { get; } = [];
    public ObservableCollection<CloseApprovalHistoryEntry> ApprovalHistory { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> CloseMaterialityRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> CloseTaskRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> CloseDependencyRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> CloseSignOffMatrixRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> CloseLateAdjustmentRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> CloseEvidenceReviewRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ClosePeriodLockIssueRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> CloseOperatingCoverageRows { get; } = [];
    public ObservableCollection<AccountingClosePostingBalanceRow> ClosingEntryBalanceRows { get; } = [];
    public ObservableCollection<CloseWorkflowStep> CloseWorkflowSteps { get; } = [];
    public ObservableCollection<CloseSetupTaskOption> CloseSetupTaskOptions { get; } = [];
    public IReadOnlyList<string> CloseTaskSignOffDecisionOptions { get; } =
    [
        ManualJournalEntryStatusDto.Approved.ToString(),
        ManualJournalEntryStatusDto.Rejected.ToString()
    ];

    public IReadOnlyList<string> CloseReviewDecisionOptions { get; } =
    [
        ManualJournalEntryStatusDto.Approved.ToString(),
        ManualJournalEntryStatusDto.Rejected.ToString()
    ];

    public IAsyncRelayCommand LoadClosePlanCommand { get; }
    public IAsyncRelayCommand ConfigureClosePlanCommand { get; }
    public IAsyncRelayCommand SignOffCloseTaskCommand { get; }
    public IAsyncRelayCommand RequestLateAdjustmentCommand { get; }
    public IAsyncRelayCommand ReviewLateAdjustmentCommand { get; }
    public IAsyncRelayCommand ReviewCloseEvidenceCommand { get; }
    public IAsyncRelayCommand QueueClosingEntriesCommand { get; }
    public IAsyncRelayCommand LockClosePeriodCommand { get; }

    public string CloseWorkflowIdText
    {
        get => _closeWorkflowIdText;
        set
        {
            if (SetProperty(ref _closeWorkflowIdText, value ?? string.Empty))
            {
                LoadClosePlanCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public decimal CloseSetupAmountThreshold
    {
        get => _closeSetupAmountThreshold;
        set
        {
            if (SetProperty(ref _closeSetupAmountThreshold, value))
            {
                OnCloseSetupDraftChanged();
            }
        }
    }

    public decimal CloseSetupPercentThreshold
    {
        get => _closeSetupPercentThreshold;
        set
        {
            if (SetProperty(ref _closeSetupPercentThreshold, value))
            {
                OnCloseSetupDraftChanged();
            }
        }
    }

    public string CloseSetupCurrency
    {
        get => _closeSetupCurrency;
        set
        {
            if (SetProperty(ref _closeSetupCurrency, value ?? string.Empty))
            {
                OnCloseSetupDraftChanged();
            }
        }
    }

    public string CloseSetupReviewRole
    {
        get => _closeSetupReviewRole;
        set
        {
            if (SetProperty(ref _closeSetupReviewRole, value ?? string.Empty))
            {
                OnCloseSetupDraftChanged();
            }
        }
    }

    public bool CloseSetupRequiresLateAdjustmentApproval
    {
        get => _closeSetupRequiresLateAdjustmentApproval;
        set
        {
            if (SetProperty(ref _closeSetupRequiresLateAdjustmentApproval, value))
            {
                OnCloseSetupDraftChanged();
            }
        }
    }

    public string CloseSetupTaskId
    {
        get => _closeSetupTaskId;
        set
        {
            if (SetProperty(ref _closeSetupTaskId, value ?? string.Empty))
            {
                OnCloseSetupDraftChanged();
            }
        }
    }

    public string CloseSetupTaskDisplayName
    {
        get => _closeSetupTaskDisplayName;
        set
        {
            if (SetProperty(ref _closeSetupTaskDisplayName, value ?? string.Empty))
            {
                OnCloseSetupDraftChanged();
            }
        }
    }

    public string CloseSetupTaskOwner
    {
        get => _closeSetupTaskOwner;
        set
        {
            if (SetProperty(ref _closeSetupTaskOwner, value ?? string.Empty))
            {
                OnCloseSetupDraftChanged();
            }
        }
    }

    public string CloseSetupTaskDueDateText
    {
        get => _closeSetupTaskDueDateText;
        set
        {
            if (SetProperty(ref _closeSetupTaskDueDateText, value ?? string.Empty))
            {
                OnCloseSetupDraftChanged();
            }
        }
    }

    public int CloseSetupTaskRequiredApprovalCount
    {
        get => _closeSetupTaskRequiredApprovalCount;
        set
        {
            if (SetProperty(ref _closeSetupTaskRequiredApprovalCount, value))
            {
                OnCloseSetupDraftChanged();
            }
        }
    }

    public string CloseSetupTaskRequiredApprovalRole
    {
        get => _closeSetupTaskRequiredApprovalRole;
        set
        {
            if (SetProperty(ref _closeSetupTaskRequiredApprovalRole, value ?? string.Empty))
            {
                OnCloseSetupDraftChanged();
            }
        }
    }

    public string CloseSetupTaskRequiredEvidence
    {
        get => _closeSetupTaskRequiredEvidence;
        set
        {
            if (SetProperty(ref _closeSetupTaskRequiredEvidence, value ?? string.Empty))
            {
                OnCloseSetupDraftChanged();
            }
        }
    }

    public string CloseSetupTaskSignOffRequirementsText
    {
        get => _closeSetupTaskSignOffRequirementsText;
        set
        {
            if (SetProperty(ref _closeSetupTaskSignOffRequirementsText, value ?? string.Empty))
            {
                OnCloseSetupDraftChanged();
            }
        }
    }

    public string CloseSetupTaskDependsOnTaskIdsText
    {
        get => _closeSetupTaskDependsOnTaskIdsText;
        set
        {
            if (SetProperty(ref _closeSetupTaskDependsOnTaskIdsText, value ?? string.Empty))
            {
                OnCloseSetupDraftChanged();
            }
        }
    }

    public string CloseSetupTaskDependencyReason
    {
        get => _closeSetupTaskDependencyReason;
        set
        {
            if (SetProperty(ref _closeSetupTaskDependencyReason, value ?? string.Empty))
            {
                OnCloseSetupDraftChanged();
            }
        }
    }

    public string SelectedCloseSetupTaskId
    {
        get => _selectedCloseSetupTaskId;
        set
        {
            if (!SetProperty(ref _selectedCloseSetupTaskId, value ?? string.Empty))
            {
                return;
            }

            var selectedTask = _closePlan?.Tasks.FirstOrDefault(task =>
                string.Equals(task.TaskId, _selectedCloseSetupTaskId, StringComparison.OrdinalIgnoreCase));
            ApplyCloseSetupTaskDraft(selectedTask);
        }
    }

    public string CloseTaskSignOffTaskId
    {
        get => _closeTaskSignOffTaskId;
        set
        {
            if (SetProperty(ref _closeTaskSignOffTaskId, value ?? string.Empty))
            {
                OnCloseTaskSignOffDraftChanged();
            }
        }
    }

    public string CloseTaskSignOffRole
    {
        get => _closeTaskSignOffRole;
        set
        {
            if (SetProperty(ref _closeTaskSignOffRole, value ?? string.Empty))
            {
                OnCloseTaskSignOffDraftChanged();
            }
        }
    }

    public string CloseTaskSignOffDecision
    {
        get => _closeTaskSignOffDecision;
        set
        {
            if (SetProperty(ref _closeTaskSignOffDecision, value ?? string.Empty))
            {
                OnCloseTaskSignOffDraftChanged();
            }
        }
    }

    public string CloseTaskSignOffNotes
    {
        get => _closeTaskSignOffNotes;
        set
        {
            if (SetProperty(ref _closeTaskSignOffNotes, value ?? string.Empty))
            {
                OnCloseTaskSignOffDraftChanged();
            }
        }
    }

    public string LateAdjustmentJournalEntryIdText
    {
        get => _lateAdjustmentJournalEntryIdText;
        set
        {
            if (SetProperty(ref _lateAdjustmentJournalEntryIdText, value ?? string.Empty))
            {
                OnLateAdjustmentDraftChanged();
            }
        }
    }

    public string LateAdjustmentAmountText
    {
        get => _lateAdjustmentAmountText;
        set
        {
            if (SetProperty(ref _lateAdjustmentAmountText, value ?? string.Empty))
            {
                OnLateAdjustmentDraftChanged();
            }
        }
    }

    public string LateAdjustmentCurrency
    {
        get => _lateAdjustmentCurrency;
        set
        {
            if (SetProperty(ref _lateAdjustmentCurrency, value ?? string.Empty))
            {
                OnLateAdjustmentDraftChanged();
            }
        }
    }

    public string LateAdjustmentReason
    {
        get => _lateAdjustmentReason;
        set
        {
            if (SetProperty(ref _lateAdjustmentReason, value ?? string.Empty))
            {
                OnLateAdjustmentDraftChanged();
            }
        }
    }

    public string LateAdjustmentReviewRequestId
    {
        get => _lateAdjustmentReviewRequestId;
        set
        {
            if (SetProperty(ref _lateAdjustmentReviewRequestId, value ?? string.Empty))
            {
                OnLateAdjustmentReviewDraftChanged();
            }
        }
    }

    public string LateAdjustmentReviewDecision
    {
        get => _lateAdjustmentReviewDecision;
        set
        {
            if (SetProperty(ref _lateAdjustmentReviewDecision, value ?? string.Empty))
            {
                OnLateAdjustmentReviewDraftChanged();
            }
        }
    }

    public string LateAdjustmentReviewNotes
    {
        get => _lateAdjustmentReviewNotes;
        set
        {
            if (SetProperty(ref _lateAdjustmentReviewNotes, value ?? string.Empty))
            {
                OnLateAdjustmentReviewDraftChanged();
            }
        }
    }

    public string CloseEvidenceReviewIssueCode
    {
        get => _closeEvidenceReviewIssueCode;
        set
        {
            if (SetProperty(ref _closeEvidenceReviewIssueCode, value ?? string.Empty))
            {
                OnCloseEvidenceReviewDraftChanged();
            }
        }
    }

    public string CloseEvidenceReviewTargetId
    {
        get => _closeEvidenceReviewTargetId;
        set
        {
            if (SetProperty(ref _closeEvidenceReviewTargetId, value ?? string.Empty))
            {
                OnCloseEvidenceReviewDraftChanged();
            }
        }
    }

    public string CloseEvidenceReviewNotes
    {
        get => _closeEvidenceReviewNotes;
        set
        {
            if (SetProperty(ref _closeEvidenceReviewNotes, value ?? string.Empty))
            {
                OnCloseEvidenceReviewDraftChanged();
            }
        }
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

    public string EvidencePackageStatusText
    {
        get => _evidencePackageStatusText;
        private set
        {
            if (string.Equals(_evidencePackageStatusText, value, StringComparison.Ordinal))
            {
                return;
            }

            _evidencePackageStatusText = value;
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

    public string CloseTaskSignOffStatusText
    {
        get => _closeTaskSignOffStatusText;
        private set
        {
            if (string.Equals(_closeTaskSignOffStatusText, value, StringComparison.Ordinal))
            {
                return;
            }

            _closeTaskSignOffStatusText = value;
            RaisePropertyChanged();
        }
    }

    public string LateAdjustmentRequestStatusText
    {
        get => _lateAdjustmentRequestStatusText;
        private set
        {
            if (string.Equals(_lateAdjustmentRequestStatusText, value, StringComparison.Ordinal))
            {
                return;
            }

            _lateAdjustmentRequestStatusText = value;
            RaisePropertyChanged();
        }
    }

    public string LateAdjustmentReviewStatusText
    {
        get => _lateAdjustmentReviewStatusText;
        private set
        {
            if (string.Equals(_lateAdjustmentReviewStatusText, value, StringComparison.Ordinal))
            {
                return;
            }

            _lateAdjustmentReviewStatusText = value;
            RaisePropertyChanged();
        }
    }

    public string CloseEvidenceReviewStatusText
    {
        get => _closeEvidenceReviewStatusText;
        private set
        {
            if (string.Equals(_closeEvidenceReviewStatusText, value, StringComparison.Ordinal))
            {
                return;
            }

            _closeEvidenceReviewStatusText = value;
            RaisePropertyChanged();
        }
    }

    public ClosePostingGateDto? ClosingEntriesGate
    {
        get => _closingEntriesGate;
        private set
        {
            if (!SetProperty(ref _closingEntriesGate, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(ClosingEntriesGateStatusText));
            RaisePropertyChanged(nameof(ClosingEntriesNetIncomeRollText));
            RaisePropertyChanged(nameof(ClosingEntriesBalanceCountText));
            RaisePropertyChanged(nameof(ClosingEntriesLockPostureText));
            RaisePropertyChanged(nameof(ClosingEntriesDetailText));
            RaisePropertyChanged(nameof(ClosingEntriesJournalEvidenceText));
            QueueClosingEntriesCommand.NotifyCanExecuteChanged();
            LockClosePeriodCommand.NotifyCanExecuteChanged();
        }
    }

    public string ClosingEntriesGateStatusText
        => ClosingEntriesGate is null
            ? "Not supplied"
            : FormatClosePostingGateState(ClosingEntriesGate.State);

    public string ClosingEntriesNetIncomeRollText
        => ClosingEntriesGate is null
            ? "Net-income roll unavailable"
            : string.Format(
                CultureInfo.InvariantCulture,
                "{0:+#,##0.00;-#,##0.00;0.00} {1}",
                ClosingEntriesGate.NetIncomeRoll,
                _closePlan?.MaterialityPolicy.Currency ?? string.Empty).TrimEnd();

    public string ClosingEntriesBalanceCountText
        => ClosingEntriesGate is null
            ? "Scoped balances unavailable"
            : $"{ClosingEntriesGate.TemporaryAccountBalanceCount:N0} {Pluralize(ClosingEntriesGate.TemporaryAccountBalanceCount, "temporary-account balance", "temporary-account balances")}";

    public string ClosingEntriesLockPostureText
        => ClosingEntriesGate is null
            ? "Lock posture unavailable"
            : ClosingEntriesGate.IsReadyForLock
                ? "Ready for lock"
                : "Posting required before lock";

    public string ClosingEntriesDetailText
        => ClosingEntriesGate?.Detail
            ?? "The shared close plan did not return the typed closing-entry posting gate.";

    public string ClosingEntriesJournalEvidenceText
    {
        get
        {
            if (ClosingEntriesGate is not { } gate)
            {
                return "No closing-entry draft, batch, reversal, or evidence identifiers were returned.";
            }

            var draft = gate.DraftJournalEntryId is { } draftId
                ? $"Draft {draftId:D}{(gate.DraftStatus is { } status ? $" ({status})" : string.Empty)}"
                : "No draft queued";
            var closingBatches = gate.ClosingBatchJournalEntryIds.Count == 0
                ? "no closing batches"
                : $"closing batches {string.Join(", ", gate.ClosingBatchJournalEntryIds.Select(static id => id.ToString("D")))}";
            var reversals = gate.ReversalDraftJournalEntryIds.Count == 0
                ? "no reversal drafts"
                : $"reversal drafts {string.Join(", ", gate.ReversalDraftJournalEntryIds.Select(static id => id.ToString("D")))}";
            return $"{draft}; {closingBatches}; {reversals}; {gate.EvidenceLinks.Count:N0} {Pluralize(gate.EvidenceLinks.Count, "evidence link", "evidence links")}.";
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
        ApprovalHistory.Clear();
        foreach (var approval in projection.ApprovalHistory)
        {
            ApprovalHistory.Add(approval);
        }

        TrialBalanceStatusText = projection.TrialBalanceBalanced
            ? "Trial balance is balanced and eligible for close evidence review."
            : "Trial balance is out of balance; close workflow remains blocked until corrected.";
        var sourceEventCount = projection.EvidencePackage.SourceEventIds.Length;
        var approvalCount = projection.EvidencePackage.ApprovalIds.Length;
        EvidencePackageStatusText =
            $"{projection.EvidencePackage.PackageId} retains {sourceEventCount} source event{(sourceEventCount == 1 ? string.Empty : "s")} and {approvalCount} approval{(approvalCount == 1 ? string.Empty : "s")}.";
    }

    public void ApplyClosePlan(ClosePeriodPlanDto closePlan)
    {
        ApplyClosePlan(
            closePlan.Configuration?.WorkflowId ?? Guid.Empty,
            closePlan.WorkflowVersion,
            closePlan);
    }

    public void ApplyClosePlan(Guid workflowId, ClosePeriodPlanDto closePlan)
        => ApplyClosePlan(workflowId, closePlan.WorkflowVersion, closePlan);

    public void ApplyClosePlan(Guid workflowId, long workflowVersion, ClosePeriodPlanDto closePlan)
    {
        ArgumentNullException.ThrowIfNull(closePlan);
        _closeWorkflowId = workflowId;
        _closeWorkflowVersion = closePlan.WorkflowVersion > 0
            ? closePlan.WorkflowVersion
            : Math.Max(0, workflowVersion);
        _closePlan = closePlan;
        ApplyClosingEntriesGate(closePlan);
        ApplyCloseSetupDraft(closePlan);
        ApplyCloseReviewRows(closePlan);
        ClosePlanSetupStatusText = workflowId == Guid.Empty
            ? $"Close plan {closePlan.PeriodId} loaded without workflow context; setup retention is disabled."
            : closePlan.IsPeriodLocked
            ? $"Close plan {closePlan.PeriodId} is locked; setup changes require a governed reopen workflow."
            : $"Close plan {closePlan.PeriodId} loaded for governed setup retention.";
        ClosePeriodLockStatusText = workflowId == Guid.Empty
            ? $"Close plan {closePlan.PeriodId} loaded without workflow context; period lock is disabled."
            : closePlan.IsPeriodLocked
            ? $"Close plan {closePlan.PeriodId} is already locked."
            : ResolveClosePeriodLockStatus(closePlan);
        CloseTaskSignOffStatusText = workflowId == Guid.Empty
            ? $"Close plan {closePlan.PeriodId} loaded without workflow context; task sign-off is disabled."
            : closePlan.IsPeriodLocked
            ? $"Close plan {closePlan.PeriodId} is locked; task sign-off requires a governed reopen workflow."
            : ApplyCloseTaskSignOffDraft(closePlan) is { } signOffTask
                ? $"Close task {signOffTask.TaskId} is ready for WPF sign-off evidence retention."
                : $"Close plan {closePlan.PeriodId} has no open task sign-off requirement.";
        LateAdjustmentCurrency = closePlan.MaterialityPolicy.Currency;
        LateAdjustmentRequestStatusText = workflowId == Guid.Empty
            ? $"Close plan {closePlan.PeriodId} loaded without workflow context; late-adjustment requests are disabled."
            : closePlan.IsPeriodLocked
            ? $"Close plan {closePlan.PeriodId} is locked; late-adjustment requests require a governed reopen workflow."
            : ValidateLateAdjustmentDraft(closePlan) ?? $"Close plan {closePlan.PeriodId} is ready for retained late-adjustment requests.";
        LateAdjustmentReviewStatusText = workflowId == Guid.Empty
            ? $"Close plan {closePlan.PeriodId} loaded without workflow context; late-adjustment review is disabled."
            : closePlan.IsPeriodLocked
            ? $"Close plan {closePlan.PeriodId} is locked; late-adjustment review requires a governed reopen workflow."
            : ApplyLateAdjustmentReviewDraft(closePlan) is { } adjustment
                ? $"Late adjustment {adjustment.RequestId} is ready for WPF review."
                : $"Close plan {closePlan.PeriodId} has no submitted late adjustment to review.";
        CloseEvidenceReviewStatusText = workflowId == Guid.Empty
            ? $"Close plan {closePlan.PeriodId} loaded without workflow context; blocker/evidence review is disabled."
            : closePlan.IsPeriodLocked
            ? $"Close plan {closePlan.PeriodId} is locked; blocker/evidence review requires a governed reopen workflow."
            : ApplyCloseEvidenceReviewDraft(closePlan) is { } issue
                ? $"Close blocker {issue.Code} is ready for WPF evidence review."
                : $"Close plan {closePlan.PeriodId} has no unreviewed active blockers.";
        ApplyClosePeriodLockIssues(closePlan.ValidationIssues);
        ConfigureClosePlanCommand.NotifyCanExecuteChanged();
        SignOffCloseTaskCommand.NotifyCanExecuteChanged();
        RequestLateAdjustmentCommand.NotifyCanExecuteChanged();
        ReviewLateAdjustmentCommand.NotifyCanExecuteChanged();
        ReviewCloseEvidenceCommand.NotifyCanExecuteChanged();
        QueueClosingEntriesCommand.NotifyCanExecuteChanged();
        LockClosePeriodCommand.NotifyCanExecuteChanged();
        RefreshCloseWorkflowSteps();
    }

    public void SetCloseState(ClosePeriodState state)
    {
        CloseState = state;
        CloseStateText = state.ToString();
    }

    private void OnCloseSetupDraftChanged()
    {
        ConfigureClosePlanCommand.NotifyCanExecuteChanged();
        if (_closePlan is null || _closeWorkflowId == Guid.Empty || _closePlan.IsPeriodLocked)
        {
            return;
        }

        ClosePlanSetupStatusText = ValidateCloseSetupDraft(_closePlan)
            ?? $"Close plan {_closePlan.PeriodId} loaded for governed setup retention.";
        RefreshCloseWorkflowSteps();
    }

    private void OnCloseTaskSignOffDraftChanged()
    {
        SignOffCloseTaskCommand.NotifyCanExecuteChanged();
        if (_closePlan is null || _closeWorkflowId == Guid.Empty || _closePlan.IsPeriodLocked)
        {
            return;
        }

        CloseTaskSignOffStatusText = ValidateCloseTaskSignOffDraft(_closePlan)
            ?? $"Close task {CloseTaskSignOffTaskId.Trim()} is ready for WPF sign-off evidence retention.";
        RefreshCloseWorkflowSteps();
    }

    private void OnLateAdjustmentDraftChanged()
    {
        RequestLateAdjustmentCommand.NotifyCanExecuteChanged();
        if (_closePlan is null || _closeWorkflowId == Guid.Empty || _closePlan.IsPeriodLocked)
        {
            return;
        }

        LateAdjustmentRequestStatusText = ValidateLateAdjustmentDraft(_closePlan)
            ?? $"Close plan {_closePlan.PeriodId} is ready for retained late-adjustment requests.";
        RefreshCloseWorkflowSteps();
    }

    private void OnLateAdjustmentReviewDraftChanged()
    {
        ReviewLateAdjustmentCommand.NotifyCanExecuteChanged();
        if (_closePlan is null || _closeWorkflowId == Guid.Empty || _closePlan.IsPeriodLocked)
        {
            return;
        }

        LateAdjustmentReviewStatusText = ResolveLateAdjustmentReviewDraft(_closePlan) is null
            ? "Select a submitted late adjustment and Approved or Rejected decision before retaining review."
            : $"Late adjustment {LateAdjustmentReviewRequestId} is ready for WPF review.";
        RefreshCloseWorkflowSteps();
    }

    private void OnCloseEvidenceReviewDraftChanged()
    {
        ReviewCloseEvidenceCommand.NotifyCanExecuteChanged();
        if (_closePlan is null || _closeWorkflowId == Guid.Empty || _closePlan.IsPeriodLocked)
        {
            return;
        }

        CloseEvidenceReviewStatusText = ResolveCloseEvidenceReviewIssue(_closePlan) is null
            ? "Select an active close blocker before retaining evidence review."
            : $"Close blocker {CloseEvidenceReviewIssueCode} is ready for WPF evidence review.";
        RefreshCloseWorkflowSteps();
    }

    private void RefreshCloseWorkflowSteps()
    {
        CloseWorkflowSteps.Clear();
        foreach (var step in BuildCloseWorkflowSteps())
        {
            CloseWorkflowSteps.Add(step);
        }
    }

    private IReadOnlyList<CloseWorkflowStep> BuildCloseWorkflowSteps()
    {
        if (_closePlan is not { } closePlan)
        {
            return
            [
                new("close-setup", "Close setup", "Pending", "Load a governed close workflow before setup retention can be reviewed.", "No close plan", ClosePlanSetupStatusText, ConfigureClosePlanCommand),
                new("checklist-signoff", "Checklist sign-off", "Pending", "Load checklist tasks before retaining sign-off matrix evidence.", "No checklist rows", CloseTaskSignOffStatusText, SignOffCloseTaskCommand),
                new("late-adjustment-request", "Late adjustment request", "Pending", "Load a close plan before requesting retained late adjustments.", "No late-adjustment draft", LateAdjustmentRequestStatusText, RequestLateAdjustmentCommand),
                new("late-adjustment-review", "Late adjustment review", "Pending", "Load submitted late adjustments before controller review.", "No late-adjustment rows", LateAdjustmentReviewStatusText, ReviewLateAdjustmentCommand),
                new("blocker-review", "Blocker review", "Pending", "Load validation blockers before retaining evidence review.", "No blockers loaded", CloseEvidenceReviewStatusText, ReviewCloseEvidenceCommand),
                new("period-lock", "Period lock", "Pending", "Load a close plan before governed period-lock review.", "No ledger-book scope", ClosePeriodLockStatusText, LockClosePeriodCommand)
            ];
        }

        var setupEvidenceCount = closePlan.Configuration?.EvidenceLinks.Count
            ?? closePlan.Tasks.Sum(static task => task.EvidenceLinks.Count)
            + closePlan.LateAdjustments.Sum(static adjustment => adjustment.EvidenceLinks.Count);
        var setupRetained = closePlan.Configuration is not null;
        var openSignOffCount = closePlan.Tasks.Count(static task =>
            task.Status is not CloseTaskStatusDto.SignedOff &&
            task.SignOffRequirements.Any(static requirement => !requirement.IsSatisfied));
        var satisfiedSignOffCount = closePlan.Tasks.Sum(static task =>
            task.SignOffRequirements.Count(static requirement => requirement.IsSatisfied));
        var requiredSignOffCount = closePlan.Tasks.Sum(static task => task.SignOffRequirements.Count);
        var submittedLateAdjustments = closePlan.LateAdjustments.Count(static adjustment =>
            adjustment.ApprovalState == ManualJournalEntryStatusDto.Submitted);
        var reviewedLateAdjustments = closePlan.LateAdjustments.Count(static adjustment =>
            adjustment.ApprovalState is ManualJournalEntryStatusDto.Approved or ManualJournalEntryStatusDto.Rejected);
        var activeUnreviewedBlockers = closePlan.ValidationIssues.Count(issue =>
            FindCloseEvidenceReview(closePlan, issue) is null);
        var retainedBlockerReviews = closePlan.EvidenceReviews.Count;
        var ledgerBookLabel = closePlan.LedgerBookId is { } ledgerBookId
            ? $"Ledger book {ledgerBookId:D}"
            : "No ledger-book scope";

        return
        [
            new(
                "close-setup",
                "Close setup",
                setupRetained ? "Retained" : "Draft loaded",
                setupRetained
                    ? "Materiality, task setup, dependencies, and required sign-off evidence are retained on the shared close plan."
                    : "Review materiality, task setup, dependency reasons, and sign-off evidence before retaining setup.",
                setupEvidenceCount == 1 ? "1 evidence link" : $"{setupEvidenceCount} evidence links",
                CanConfigureClosePlan() ? null : ClosePlanSetupStatusText,
                ConfigureClosePlanCommand),
            new(
                "checklist-signoff",
                "Checklist sign-off",
                closePlan.Tasks.Count == 0 ? "Pending" : openSignOffCount == 0 ? "Signed off" : $"{openSignOffCount} open",
                openSignOffCount == 0
                    ? "Loaded checklist tasks have retained required sign-off posture."
                    : "Retain the selected checklist task decision through the shared close-management service.",
                requiredSignOffCount == 0
                    ? "No sign-off requirements"
                    : $"{satisfiedSignOffCount}/{requiredSignOffCount} requirement rows satisfied",
                CanSignOffCloseTask() ? null : CloseTaskSignOffStatusText,
                SignOffCloseTaskCommand),
            new(
                "late-adjustment-request",
                "Late adjustment request",
                closePlan.LateAdjustments.Count == 0 ? "Ready" : $"{closePlan.LateAdjustments.Count} retained",
                "Request material late adjustments with workflow, period, ledger-book, journal-entry, and human-origin evidence.",
                closePlan.LateAdjustments.Count == 0
                    ? "No retained adjustments"
                    : $"{closePlan.LateAdjustments.Count} late adjustment rows",
                CanRequestLateAdjustment() ? null : LateAdjustmentRequestStatusText,
                RequestLateAdjustmentCommand),
            new(
                "late-adjustment-review",
                "Late adjustment review",
                closePlan.LateAdjustments.Count == 0 ? "None" : submittedLateAdjustments == 0 ? "Reviewed" : $"{submittedLateAdjustments} submitted",
                submittedLateAdjustments == 0
                    ? "Loaded late adjustments have retained review posture or no review is required."
                    : "Review the next submitted late adjustment through the shared close-management service.",
                closePlan.LateAdjustments.Count == 0
                    ? "No late-adjustment evidence"
                    : $"{reviewedLateAdjustments}/{closePlan.LateAdjustments.Count} reviewed",
                CanReviewLateAdjustment() ? null : LateAdjustmentReviewStatusText,
                ReviewLateAdjustmentCommand),
            new(
                "blocker-review",
                "Blocker review",
                activeUnreviewedBlockers > 0 ? $"{activeUnreviewedBlockers} unreviewed" : retainedBlockerReviews > 0 ? "Reviewed" : closePlan.ValidationIssues.Count > 0 ? "No action" : "Clear",
                activeUnreviewedBlockers > 0
                    ? "Retain operator evidence review for active blockers without clearing service-owned validation state."
                    : "No active close blocker is waiting for WPF evidence review.",
                retainedBlockerReviews > 0
                    ? $"{retainedBlockerReviews} retained review rows"
                    : closePlan.ValidationIssues.Count == 0 ? "No blockers" : $"{closePlan.ValidationIssues.Count} validation issues",
                CanReviewCloseEvidence() ? null : CloseEvidenceReviewStatusText,
                ReviewCloseEvidenceCommand),
            new(
                "period-lock",
                "Period lock",
                closePlan.IsPeriodLocked ? "Locked" : "Open",
                closePlan.IsPeriodLocked
                    ? "The close period is locked; new close mutations require a governed reopen workflow."
                    : "Submit workflow version, checklist approvals, report-pack id, close-package ids, and retained ledger-book evidence.",
                ledgerBookLabel,
                CanLockClosePeriod() ? null : ClosePeriodLockStatusText,
                LockClosePeriodCommand)
        ];
    }

    private bool CanConfigureClosePlan()
        => _closeManagementService is not null &&
           _closeWorkflowId != Guid.Empty &&
           _closePlan is { IsPeriodLocked: false } closePlan &&
           ValidateCloseSetupDraft(closePlan) is null;

    private bool CanLoadClosePlan()
        => _closeManagementService is not null &&
           Guid.TryParse(CloseWorkflowIdText, out var workflowId) &&
           workflowId != Guid.Empty;

    private bool CanSignOffCloseTask()
        => _closeManagementService is not null &&
           _closeWorkflowId != Guid.Empty &&
           _closePlan is { IsPeriodLocked: false } closePlan &&
           ValidateCloseTaskSignOffDraft(closePlan) is null;

    private bool CanReviewLateAdjustment()
        => _closeManagementService is not null &&
           _closeWorkflowId != Guid.Empty &&
           _closePlan is { IsPeriodLocked: false } closePlan &&
           ResolveLateAdjustmentReviewDraft(closePlan) is not null;

    private bool CanReviewCloseEvidence()
        => _closeManagementService is not null &&
           _closeWorkflowId != Guid.Empty &&
           _closePlan is { IsPeriodLocked: false } closePlan &&
           ResolveCloseEvidenceReviewIssue(closePlan) is not null;

    private bool CanRequestLateAdjustment()
        => _closeManagementService is not null &&
           _closeWorkflowId != Guid.Empty &&
           _closePlan is { IsPeriodLocked: false } closePlan &&
           ValidateLateAdjustmentDraft(closePlan) is null;

    private bool CanQueueClosingEntries()
        => _closeManagementService is not null &&
           _closeWorkflowId != Guid.Empty &&
           _closePlan is { IsPeriodLocked: false } &&
           ClosingEntriesGate?.State == ClosePostingGateStateDto.Required;

    private static string ResolveClosePeriodLockStatus(ClosePeriodPlanDto closePlan)
        => closePlan.ClosingEntriesGate switch
        {
            null => "The shared close plan did not return a closing-entry gate; period lock is disabled.",
            { State: ClosePostingGateStateDto.Required } =>
                $"Close plan {closePlan.PeriodId} requires closing entries to be queued before period lock.",
            { State: ClosePostingGateStateDto.DraftQueued or ClosePostingGateStateDto.Submitted or ClosePostingGateStateDto.Approved } gate =>
                $"Close plan {closePlan.PeriodId} cannot lock until closing entries advance from {FormatClosePostingGateState(gate.State)} to Posted.",
            { IsReadyForLock: true, State: ClosePostingGateStateDto.Posted or ClosePostingGateStateDto.NotRequired } =>
                $"Close plan {closePlan.PeriodId} is ready for governed period-lock review.",
            { } gate =>
                $"Close plan {closePlan.PeriodId} cannot lock while closing-entry gate state is {FormatClosePostingGateState(gate.State)}."
        };

    private bool CanLockClosePeriod()
        => _closeManagementService is not null &&
           _closeWorkflowId != Guid.Empty &&
           _closePlan is { IsPeriodLocked: false } &&
           ClosingEntriesGate is
           {
               IsReadyForLock: true,
               State: ClosePostingGateStateDto.Posted or ClosePostingGateStateDto.NotRequired
           };

    private async Task LoadClosePlanAsync()
    {
        if (_closeManagementService is null)
        {
            ClosePlanSetupStatusText = "Close management service is not registered for this desktop session.";
            return;
        }

        if (!Guid.TryParse(CloseWorkflowIdText, out var workflowId) || workflowId == Guid.Empty)
        {
            ClosePlanSetupStatusText = "Enter a close workflow id before loading governed close setup.";
            return;
        }

        var closePlan = await _closeManagementService.GetPeriodPlanAsync(workflowId).ConfigureAwait(true);
        if (closePlan is null)
        {
            ClosePlanSetupStatusText = $"Close workflow {workflowId:D} was not found.";
            return;
        }

        ApplyClosePlan(workflowId, closePlan);
        CloseWorkflowIdText = workflowId.ToString("D");
        ClosePlanSetupStatusText = $"Loaded close plan {closePlan.PeriodId} for governed setup retention.";
    }

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

        var validationMessage = ValidateCloseSetupDraft(_closePlan);
        if (validationMessage is not null)
        {
            ClosePlanSetupStatusText = validationMessage;
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

            ApplyClosePlan(_closeWorkflowId, updated);
            ClosePlanSetupStatusText = $"Retained close-plan setup for {updated.PeriodId}.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ClosePlanSetupStatusText = $"Close-plan setup could not be retained: {ex.Message}";
        }
    }

    private async Task SignOffCloseTaskAsync()
    {
        if (_closeManagementService is null)
        {
            CloseTaskSignOffStatusText = "Close management service is not registered for this desktop session.";
            return;
        }

        if (_closePlan is null)
        {
            CloseTaskSignOffStatusText = "Load a close plan before retaining close task sign-off evidence.";
            return;
        }

        if (_closeWorkflowId == Guid.Empty)
        {
            CloseTaskSignOffStatusText = $"Close plan {_closePlan.PeriodId} loaded without workflow context; task sign-off is disabled.";
            return;
        }

        if (_closePlan.IsPeriodLocked)
        {
            CloseTaskSignOffStatusText = $"Close plan {_closePlan.PeriodId} is locked; task sign-off requires a governed reopen workflow.";
            return;
        }

        var task = ResolveCloseTaskSignOffTask(_closePlan);
        if (task is null)
        {
            CloseTaskSignOffStatusText = "Select a retained close checklist task before retaining sign-off evidence.";
            return;
        }

        var validationMessage = ValidateCloseTaskSignOffDraft(_closePlan);
        if (validationMessage is not null)
        {
            CloseTaskSignOffStatusText = validationMessage;
            return;
        }

        try
        {
            var request = BuildCloseTaskSignOffRequest(_closeWorkflowId, _closePlan, task, CloseTaskSignOffRole, CloseTaskSignOffDecision, CloseTaskSignOffNotes);
            var updated = await _closeManagementService
                .SignOffCloseTaskAsync(request, "wpf-accounting-controller")
                .ConfigureAwait(true);

            if (updated is null)
            {
                CloseTaskSignOffStatusText = $"Close workflow {_closeWorkflowId:D} was not found.";
                return;
            }

            ApplyClosePlan(_closeWorkflowId, updated);
            CloseTaskSignOffStatusText = request.Decision == ManualJournalEntryStatusDto.Approved
                ? $"Retained {request.Role} sign-off evidence for close task {request.TaskId}."
                : $"Retained {request.Role} rejection evidence for close task {request.TaskId}.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            CloseTaskSignOffStatusText = $"Close task sign-off could not be retained: {ex.Message}";
        }
    }

    private async Task RequestLateAdjustmentAsync()
    {
        if (_closeManagementService is null)
        {
            LateAdjustmentRequestStatusText = "Close management service is not registered for this desktop session.";
            return;
        }

        if (_closePlan is null)
        {
            LateAdjustmentRequestStatusText = "Load a close plan before requesting late adjustments.";
            return;
        }

        if (_closeWorkflowId == Guid.Empty)
        {
            LateAdjustmentRequestStatusText = $"Close plan {_closePlan.PeriodId} loaded without workflow context; late-adjustment requests are disabled.";
            return;
        }

        if (_closePlan.IsPeriodLocked)
        {
            LateAdjustmentRequestStatusText = $"Close plan {_closePlan.PeriodId} is locked; late-adjustment requests require a governed reopen workflow.";
            return;
        }

        var validationMessage = ValidateLateAdjustmentDraft(_closePlan);
        if (validationMessage is not null)
        {
            LateAdjustmentRequestStatusText = validationMessage;
            return;
        }

        try
        {
            var request = BuildCreateLateAdjustmentRequest(_closeWorkflowId, _closePlan);
            var updated = await _closeManagementService
                .RequestLateAdjustmentAsync(request, "wpf-accounting-controller")
                .ConfigureAwait(true);

            if (updated is null)
            {
                LateAdjustmentRequestStatusText = $"Close workflow {_closeWorkflowId:D} was not found.";
                return;
            }

            ApplyClosePlan(_closeWorkflowId, updated);
            LateAdjustmentRequestStatusText = $"Requested retained late adjustment for journal {request.JournalEntryId:D}.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            LateAdjustmentRequestStatusText = $"Late adjustment request could not be retained: {ex.Message}";
        }
    }

    private async Task ReviewLateAdjustmentAsync()
    {
        if (_closeManagementService is null)
        {
            LateAdjustmentReviewStatusText = "Close management service is not registered for this desktop session.";
            return;
        }

        if (_closePlan is null)
        {
            LateAdjustmentReviewStatusText = "Load a close plan before reviewing late adjustments.";
            return;
        }

        if (_closeWorkflowId == Guid.Empty)
        {
            LateAdjustmentReviewStatusText = $"Close plan {_closePlan.PeriodId} loaded without workflow context; late-adjustment review is disabled.";
            return;
        }

        if (_closePlan.IsPeriodLocked)
        {
            LateAdjustmentReviewStatusText = $"Close plan {_closePlan.PeriodId} is locked; late-adjustment review requires a governed reopen workflow.";
            return;
        }

        var adjustment = ResolveLateAdjustmentReviewDraft(_closePlan);
        if (adjustment is null)
        {
            LateAdjustmentReviewStatusText = "Select a submitted late adjustment and Approved or Rejected decision before retaining review.";
            return;
        }

        try
        {
            var request = BuildReviewLateAdjustmentRequest(_closeWorkflowId, _closePlan, adjustment);
            var updated = await _closeManagementService
                .ReviewLateAdjustmentAsync(request, "wpf-accounting-controller")
                .ConfigureAwait(true);

            if (updated is null)
            {
                LateAdjustmentReviewStatusText = $"Close workflow {_closeWorkflowId:D} was not found.";
                return;
            }

            ApplyClosePlan(_closeWorkflowId, updated);
            LateAdjustmentReviewStatusText = $"{request.Decision} late adjustment {request.RequestId} with retained WPF review evidence.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            LateAdjustmentReviewStatusText = $"Late adjustment review could not be retained: {ex.Message}";
        }
    }

    private async Task ReviewCloseEvidenceAsync()
    {
        if (_closeManagementService is null)
        {
            CloseEvidenceReviewStatusText = "Close management service is not registered for this desktop session.";
            return;
        }

        if (_closePlan is null)
        {
            CloseEvidenceReviewStatusText = "Load a close plan before retaining blocker/evidence review.";
            return;
        }

        if (_closeWorkflowId == Guid.Empty)
        {
            CloseEvidenceReviewStatusText = $"Close plan {_closePlan.PeriodId} loaded without workflow context; blocker/evidence review is disabled.";
            return;
        }

        if (_closePlan.IsPeriodLocked)
        {
            CloseEvidenceReviewStatusText = $"Close plan {_closePlan.PeriodId} is locked; blocker/evidence review requires a governed reopen workflow.";
            return;
        }

        var issue = ResolveCloseEvidenceReviewIssue(_closePlan);
        if (issue is null)
        {
            CloseEvidenceReviewStatusText = "Select an active close blocker before retaining evidence review.";
            return;
        }

        try
        {
            var request = BuildReviewCloseEvidenceRequest(_closeWorkflowId, _closePlan, issue);
            var updated = await _closeManagementService
                .ReviewCloseEvidenceAsync(request, "wpf-accounting-controller")
                .ConfigureAwait(true);

            if (updated is null)
            {
                CloseEvidenceReviewStatusText = $"Close workflow {_closeWorkflowId:D} was not found.";
                return;
            }

            ApplyClosePlan(_closeWorkflowId, updated);
            CloseEvidenceReviewStatusText = $"Retained WPF evidence review for blocker {request.IssueCode}.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            CloseEvidenceReviewStatusText = $"Close evidence review could not be retained: {ex.Message}";
        }
    }

    private async Task QueueClosingEntriesAsync()
    {
        if (_closeManagementService is null)
        {
            ClosePeriodLockStatusText = "Close management service is not registered for this desktop session.";
            return;
        }

        if (_closePlan is null || _closeWorkflowId == Guid.Empty)
        {
            ClosePeriodLockStatusText = "Load a workflow-scoped close plan before queuing closing entries.";
            return;
        }

        if (_closePlan.IsPeriodLocked)
        {
            ClosePeriodLockStatusText = $"Close plan {_closePlan.PeriodId} is already locked.";
            return;
        }

        if (!CanQueueClosingEntries())
        {
            ClosePeriodLockStatusText = ClosingEntriesGate is null
                ? "The shared close plan did not return a closing-entry gate."
                : $"Closing entries can only be queued while the gate is Required; current state is {ClosingEntriesGateStatusText}.";
            return;
        }

        try
        {
            var request = BuildClosePeriodLockRequest(
                _closeWorkflowId,
                _closeWorkflowVersion,
                _closePlan,
                prepareClosingEntriesOnly: true);
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
                ApplyClosePlan(_closeWorkflowId, result.Plan);
            }

            ApplyClosePeriodLockIssues(result.Issues);
            ClosePeriodLockStatusText = result.Plan is
                {
                    ClosingEntriesGate.State: ClosePostingGateStateDto.DraftQueued or
                        ClosePostingGateStateDto.Submitted or
                        ClosePostingGateStateDto.Approved or
                        ClosePostingGateStateDto.Posted
                } preparedPlan
                    ? $"Prepared closing-entry workflow for close period {preparedPlan.PeriodId}; human approval and posting remain governed."
                    : $"Closing-entry preparation is blocked by {result.Issues.Count} issue(s).";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ClosePeriodLockStatusText = $"Closing entries could not be queued: {ex.Message}";
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

        if (!CanLockClosePeriod())
        {
            ClosePeriodLockStatusText = ClosingEntriesGate is null
                ? "The accounting period cannot lock without a shared closing-entry gate."
                : $"The accounting period cannot lock while closing-entry gate state is {ClosingEntriesGateStatusText}. Post closing entries or resolve the gate first.";
            return;
        }

        try
        {
            var request = BuildClosePeriodLockRequest(
                _closeWorkflowId,
                _closeWorkflowVersion,
                _closePlan,
                prepareClosingEntriesOnly: false);
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
                ApplyClosePlan(
                    _closeWorkflowId,
                    result.Transition?.NewVersion ?? _closeWorkflowVersion,
                    result.Plan);
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

    private string? ValidateCloseSetupDraft(ClosePeriodPlanDto closePlan)
    {
        if (CloseSetupAmountThreshold < 0)
        {
            return "Enter a non-negative materiality amount threshold before retaining close setup.";
        }

        if (CloseSetupPercentThreshold < 0)
        {
            return "Enter a non-negative materiality percent threshold before retaining close setup.";
        }

        var currency = NormalizeOptional(CloseSetupCurrency);
        if (currency is null || currency.Length != 3 || currency.Any(static character => !char.IsLetter(character)))
        {
            return "Enter a three-letter materiality currency before retaining close setup.";
        }

        if (NormalizeOptional(CloseSetupReviewRole) is null)
        {
            return "Enter a materiality review role before retaining close setup.";
        }

        var taskId = NormalizeOptional(CloseSetupTaskId);
        if (taskId is null)
        {
            return "Select a retained close checklist task before retaining close setup.";
        }

        if (!closePlan.Tasks.Any(task => string.Equals(task.TaskId, taskId, StringComparison.OrdinalIgnoreCase)))
        {
            return $"Close checklist task {taskId} is not loaded in this close plan.";
        }

        if (!string.IsNullOrWhiteSpace(CloseSetupTaskDueDateText) &&
            !DateOnly.TryParseExact(CloseSetupTaskDueDateText.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return "Close task due date must use yyyy-MM-dd format before retaining close setup.";
        }

        if (CloseSetupTaskRequiredApprovalCount <= 0)
        {
            return "Enter a positive required approval count before retaining close setup.";
        }

        if (NormalizeOptional(CloseSetupTaskRequiredApprovalRole) is null)
        {
            return "Enter an approval role before retaining close setup.";
        }

        if (NormalizeOptional(CloseSetupTaskRequiredEvidence) is null)
        {
            return "Enter required sign-off evidence before retaining close setup.";
        }

        foreach (var entry in SplitCloseSetupSignOffRequirements(CloseSetupTaskSignOffRequirementsText))
        {
            var requirement = ParseCloseSetupSignOffRequirement(entry);
            if (NormalizeOptional(requirement.Role) is null)
            {
                return "Enter a role for every sign-off matrix row before retaining close setup.";
            }

            if (requirement.RequiredApprovalCount <= 0)
            {
                return $"Enter a positive approval count for {requirement.Role} before retaining close setup.";
            }
        }

        return null;
    }

    private string? ValidateCloseTaskSignOffDraft(ClosePeriodPlanDto closePlan)
    {
        var taskId = NormalizeOptional(CloseTaskSignOffTaskId);
        if (taskId is null)
        {
            return "Select a retained close checklist task before retaining sign-off evidence.";
        }

        var task = closePlan.Tasks.FirstOrDefault(task =>
            string.Equals(task.TaskId, taskId, StringComparison.OrdinalIgnoreCase));
        if (task is null)
        {
            return $"Close checklist task {taskId} is not loaded in this close plan.";
        }

        if (task.Status is CloseTaskStatusDto.SignedOff)
        {
            return $"Close checklist task {task.TaskId} is already signed off.";
        }

        if (task.Status is CloseTaskStatusDto.Blocked or CloseTaskStatusDto.WaitingOnDependency)
        {
            return $"Close checklist task {task.TaskId} is {task.Status} and cannot be signed off yet.";
        }

        var role = NormalizeOptional(CloseTaskSignOffRole);
        if (role is null)
        {
            return "Enter a sign-off role before retaining close task sign-off evidence.";
        }

        if (task.SignOffRequirements.Count > 0 &&
            !task.SignOffRequirements.Any(requirement => string.Equals(requirement.Role, role, StringComparison.OrdinalIgnoreCase)))
        {
            return $"Close checklist task {task.TaskId} does not allow sign-off role {role}.";
        }

        if (!TryParseCloseTaskSignOffDecision(out _))
        {
            return "Select Approved or Rejected before retaining close task sign-off evidence.";
        }

        return null;
    }

    private string? ValidateLateAdjustmentDraft(ClosePeriodPlanDto closePlan)
    {
        if (!Guid.TryParse(LateAdjustmentJournalEntryIdText, out var journalEntryId) || journalEntryId == Guid.Empty)
        {
            return "Enter a journal entry id before requesting a late adjustment.";
        }

        if (!TryParseLateAdjustmentAmount(out var amount) || amount == 0m)
        {
            return "Enter a non-zero late adjustment amount before retaining the request.";
        }

        var currency = NormalizeOptional(LateAdjustmentCurrency);
        if (currency is null || currency.Length != 3 || currency.Any(static character => !char.IsLetter(character)))
        {
            return "Enter a three-letter late adjustment currency before retaining the request.";
        }

        if (NormalizeOptional(LateAdjustmentReason) is null)
        {
            return "Enter a late adjustment reason before retaining the request.";
        }

        if (closePlan.LateAdjustments.Any(adjustment =>
                adjustment.JournalEntryId == journalEntryId &&
                adjustment.ApprovalState is not ManualJournalEntryStatusDto.Rejected))
        {
            return $"Journal entry {journalEntryId:D} already has a retained late adjustment request.";
        }

        return null;
    }

    private CreateLateAdjustmentRequestDto BuildCreateLateAdjustmentRequest(
        Guid workflowId,
        ClosePeriodPlanDto closePlan)
    {
        var journalEntryId = Guid.Parse(LateAdjustmentJournalEntryIdText.Trim());
        var amount = ParseLateAdjustmentAmount();
        var currency = NormalizeRequired(LateAdjustmentCurrency, closePlan.MaterialityPolicy.Currency).ToUpperInvariant();
        var reason = NormalizeRequired(LateAdjustmentReason, "WPF late adjustment request.");

        return new CreateLateAdjustmentRequestDto(
            workflowId,
            journalEntryId,
            amount,
            currency,
            reason,
            "wpf-accounting-controller",
            BuildLateAdjustmentRequestEvidence(workflowId, closePlan, journalEntryId),
            $"wpf-late-adjustment-request-{workflowId:D}-{journalEntryId:D}",
            OperationsActionOriginDto.HumanOperator);
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
        var editableTaskDependencyIdReasons = ParseCloseSetupDependencyReasonOverrides(CloseSetupTaskDependsOnTaskIdsText);
        var editableTaskDependencyReasonOverrides = ParseCloseSetupDependencyReasonOverrides(CloseSetupTaskDependencyReason);
        var editableTaskDependencyReason = editableTaskDependencyReasonOverrides.Count == 0
            ? NormalizeOptional(CloseSetupTaskDependencyReason)
            : null;
        var editableTaskSignOffRequirements = ParseCloseSetupSignOffRequirements(CloseSetupTaskSignOffRequirementsText);
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
                var fallbackSignOffRequirements = BuildCloseSetupSignOffRequirementConfigurations(task.SignOffRequirements);
                if (fallbackSignOffRequirements.Count == 0)
                {
                    fallbackSignOffRequirements =
                    [
                        new CloseTaskSignOffRequirementConfigurationDto(
                            task.SignOffRequirements.FirstOrDefault()?.Role ?? task.Owner,
                            requiredApprovalCount,
                            string.IsNullOrWhiteSpace(requiredEvidence) ? "Retained close checklist evidence" : requiredEvidence)
                    ];
                }

                if (!string.Equals(task.TaskId, editableTaskId, StringComparison.OrdinalIgnoreCase))
                {
                    var primaryRequirement = fallbackSignOffRequirements[0];
                    return new CloseTaskConfigurationDto(
                        task.TaskId,
                        task.DisplayName,
                        task.Owner,
                        task.DueDate,
                        primaryRequirement.RequiredApprovalCount,
                        primaryRequirement.Role,
                        primaryRequirement.EvidenceRequirement,
                        task.Dependencies.Select(static dependency => dependency.DependsOnTaskId).ToArray(),
                        task.Dependencies.Select(static dependency => new CloseTaskDependencyConfigurationDto(
                            dependency.DependsOnTaskId,
                            dependency.Reason)).ToArray(),
                        fallbackSignOffRequirements);
                }

                var editableFallbackRequirement = new CloseTaskSignOffRequirementConfigurationDto(
                    NormalizeOptional(CloseSetupTaskRequiredApprovalRole)
                        ?? task.SignOffRequirements.FirstOrDefault()?.Role
                        ?? task.Owner,
                    Math.Max(1, CloseSetupTaskRequiredApprovalCount),
                    NormalizeOptional(CloseSetupTaskRequiredEvidence)
                        ?? (string.IsNullOrWhiteSpace(requiredEvidence) ? "Retained close checklist evidence" : requiredEvidence));
                var editableSignOffRequirements = editableTaskSignOffRequirements.Count == 0
                    ? [editableFallbackRequirement]
                    : editableTaskSignOffRequirements;
                var editablePrimaryRequirement = editableSignOffRequirements[0];
                return new CloseTaskConfigurationDto(
                    task.TaskId,
                    NormalizeOptional(CloseSetupTaskDisplayName) ?? task.DisplayName,
                    NormalizeOptional(CloseSetupTaskOwner) ?? task.Owner,
                    editableTaskDueDate ?? task.DueDate,
                    editablePrimaryRequirement.RequiredApprovalCount,
                    editablePrimaryRequirement.Role,
                    editablePrimaryRequirement.EvidenceRequirement,
                    editableTaskDependencies,
                    BuildCloseSetupDependencyConfigurations(
                        editableTaskDependencies,
                        editableTaskDependencyIdReasons,
                        editableTaskDependencyReasonOverrides,
                        editableTaskDependencyReason,
                        task.Dependencies),
                    editableSignOffRequirements);
            })
            .ToArray();

        return new UpsertClosePeriodPlanConfigurationRequestDto(
            workflowId,
            materialityPolicy,
            taskConfigurations,
            Actor: "wpf-accounting-controller",
            EvidenceLinks: BuildClosePlanConfigurationEvidence(workflowId, closePlan),
            CorrelationId: $"wpf-close-plan-configuration-{workflowId:D}",
            ActionOrigin: OperationsActionOriginDto.HumanOperator,
            ExpectedConfiguredAtUtc: closePlan.Configuration?.ConfiguredAtUtc);
    }

    private void ApplyCloseSetupDraft(ClosePeriodPlanDto closePlan)
    {
        var materiality = closePlan.MaterialityPolicy;
        CloseSetupAmountThreshold = materiality.AmountThreshold;
        CloseSetupPercentThreshold = materiality.PercentThreshold;
        CloseSetupCurrency = materiality.Currency;
        CloseSetupReviewRole = materiality.ReviewRole;
        CloseSetupRequiresLateAdjustmentApproval = materiality.RequiresLateAdjustmentApproval;

        CloseSetupTaskOptions.Clear();
        foreach (var task in closePlan.Tasks)
        {
            CloseSetupTaskOptions.Add(BuildCloseSetupTaskOption(task));
        }

        var firstTask = closePlan.Tasks.FirstOrDefault();
        var firstTaskId = firstTask?.TaskId ?? string.Empty;
        if (string.Equals(SelectedCloseSetupTaskId, firstTaskId, StringComparison.Ordinal))
        {
            ApplyCloseSetupTaskDraft(firstTask);
        }
        else
        {
            SelectedCloseSetupTaskId = firstTaskId;
        }
    }

    private void ApplyCloseSetupTaskDraft(CloseTaskDto? task)
    {
        if (task is null)
        {
            CloseSetupTaskId = string.Empty;
            CloseSetupTaskDisplayName = string.Empty;
            CloseSetupTaskOwner = string.Empty;
            CloseSetupTaskDueDateText = string.Empty;
            CloseSetupTaskRequiredApprovalCount = 1;
            CloseSetupTaskRequiredApprovalRole = "Controller";
            CloseSetupTaskRequiredEvidence = "Retained close checklist evidence";
            CloseSetupTaskSignOffRequirementsText = string.Empty;
            CloseSetupTaskDependsOnTaskIdsText = string.Empty;
            CloseSetupTaskDependencyReason = "Configured close-plan dependency.";
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
        CloseSetupTaskSignOffRequirementsText = BuildCloseSetupSignOffRequirementText(task.SignOffRequirements);
        CloseSetupTaskDependsOnTaskIdsText = string.Join(", ", task.Dependencies.Select(static dependency => dependency.DependsOnTaskId));
        CloseSetupTaskDependencyReason = BuildCloseSetupDependencyReason(task.Dependencies);
    }

    private static CloseSetupTaskOption BuildCloseSetupTaskOption(CloseTaskDto task)
    {
        var signOffSummary = task.SignOffRequirements.Count == 0
            ? "No sign-off requirement"
            : string.Join(
                "; ",
                task.SignOffRequirements.Select(static requirement =>
                    $"{requirement.Role}: {requirement.ApprovedCount}/{requirement.RequiredApprovalCount}"));

        return new CloseSetupTaskOption(
            task.TaskId,
            string.IsNullOrWhiteSpace(task.DisplayName) ? task.TaskId : task.DisplayName,
            task.Status.ToString(),
            task.Owner,
            task.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            signOffSummary);
    }

    private CloseTaskDto? ApplyCloseTaskSignOffDraft(ClosePeriodPlanDto closePlan)
    {
        var task = ResolveNextSignOffTask(closePlan);
        if (task is null)
        {
            CloseTaskSignOffTaskId = string.Empty;
            CloseTaskSignOffRole = string.Empty;
            CloseTaskSignOffDecision = ManualJournalEntryStatusDto.Approved.ToString();
            CloseTaskSignOffNotes = string.Empty;
            return null;
        }

        var requirement = task.SignOffRequirements.FirstOrDefault(static row => !row.IsSatisfied)
                          ?? task.SignOffRequirements.FirstOrDefault();
        CloseTaskSignOffTaskId = task.TaskId;
        CloseTaskSignOffRole = string.IsNullOrWhiteSpace(requirement?.Role) ? task.Owner : requirement!.Role.Trim();
        CloseTaskSignOffDecision = ManualJournalEntryStatusDto.Approved.ToString();
        CloseTaskSignOffNotes = $"WPF Accounting Close retained {CloseTaskSignOffRole} sign-off evidence for {task.DisplayName}.";
        return task;
    }

    private LateAdjustmentRequestDto? ApplyLateAdjustmentReviewDraft(ClosePeriodPlanDto closePlan)
    {
        var adjustment = ResolveNextLateAdjustment(closePlan);
        if (adjustment is null)
        {
            LateAdjustmentReviewRequestId = string.Empty;
            LateAdjustmentReviewDecision = ManualJournalEntryStatusDto.Approved.ToString();
            LateAdjustmentReviewNotes = string.Empty;
            return null;
        }

        LateAdjustmentReviewRequestId = adjustment.RequestId;
        LateAdjustmentReviewDecision = ManualJournalEntryStatusDto.Approved.ToString();
        LateAdjustmentReviewNotes = $"WPF Accounting Close approved late adjustment {adjustment.RequestId}.";
        return adjustment;
    }

    private AccountingConfigurationValidationIssueDto? ApplyCloseEvidenceReviewDraft(ClosePeriodPlanDto closePlan)
    {
        var issue = ResolveNextCloseEvidenceReviewIssue(closePlan);
        if (issue is null)
        {
            CloseEvidenceReviewIssueCode = string.Empty;
            CloseEvidenceReviewTargetId = string.Empty;
            CloseEvidenceReviewNotes = string.Empty;
            return null;
        }

        CloseEvidenceReviewIssueCode = issue.Code;
        CloseEvidenceReviewTargetId = issue.TargetId ?? string.Empty;
        CloseEvidenceReviewNotes = $"WPF Accounting Close reviewed blocker {issue.Code} for {NormalizeOptional(issue.TargetId) ?? closePlan.ClosePlanId}. {issue.Message}";
        return issue;
    }

    private void ApplyCloseReviewRows(ClosePeriodPlanDto closePlan)
    {
        CloseMaterialityRows.Clear();
        CloseTaskRows.Clear();
        CloseDependencyRows.Clear();
        CloseSignOffMatrixRows.Clear();
        CloseLateAdjustmentRows.Clear();
        CloseEvidenceReviewRows.Clear();
        CloseOperatingCoverageRows.Clear();

        CloseMaterialityRows.Add(BuildMaterialityPolicyRow(closePlan));
        CloseMaterialityRows.Add(new AccountingWorkbenchRow(
            "Period lock",
            closePlan.IsPeriodLocked ? "Locked" : "Open",
            closePlan.IsPeriodLocked
                ? $"Close period {closePlan.PeriodId} is locked against additional close-plan, sign-off, or late-adjustment mutations."
                : $"Close period {closePlan.PeriodId} remains open for governed setup, sign-off, late-adjustment, and lock review.",
            closePlan.LedgerBookId is { } ledgerBookId
                ? $"Ledger book {ledgerBookId:D}; close due {closePlan.CloseDueDate:yyyy-MM-dd}"
                : $"No selected ledger-book scope; close due {closePlan.CloseDueDate:yyyy-MM-dd}",
            closePlan.PeriodId));

        foreach (var task in closePlan.Tasks)
        {
            CloseTaskRows.Add(BuildCloseTaskRow(task));
            foreach (var dependency in task.Dependencies)
            {
                CloseDependencyRows.Add(new AccountingWorkbenchRow(
                    task.TaskId,
                    dependency.DependsOnTaskId,
                    dependency.Reason,
                    task.Status == CloseTaskStatusDto.WaitingOnDependency
                        ? "Dependency is still blocking this close task."
                        : "Dependency is retained on the close-plan graph.",
                    dependency.DependencyId));
            }

            foreach (var requirement in task.SignOffRequirements)
            {
                CloseSignOffMatrixRows.Add(new AccountingWorkbenchRow(
                    $"{task.TaskId}:{requirement.Role}",
                    requirement.IsSatisfied ? "Satisfied" : "Open",
                    $"{requirement.ApprovedCount}/{requirement.RequiredApprovalCount} approval(s) retained for {task.DisplayName}.",
                    requirement.EvidenceRequirement,
                    requirement.RequirementId));
            }

            foreach (var signOff in task.SignOffs)
            {
                CloseSignOffMatrixRows.Add(new AccountingWorkbenchRow(
                    signOff.SignOffId,
                    signOff.ApprovalState.ToString(),
                    $"{signOff.Role} decision by {NormalizeOptional(signOff.Actor) ?? "unknown actor"}{FormatSignedAt(signOff.SignedAtUtc)}.",
                    JoinEvidence(signOff.EvidenceLinks, signOff.Notes ?? "No retained sign-off note."),
                    task.TaskId));
            }

            AddEvidenceReviewRows(CloseEvidenceReviewRows, $"task:{task.TaskId}", task.EvidenceLinks, task.DisplayName);
        }

        if (CloseDependencyRows.Count == 0)
        {
            CloseDependencyRows.Add(new AccountingWorkbenchRow(
                "No dependencies",
                "Clear",
                "The close plan has no retained task dependencies.",
                "Add dependency configuration when task order matters.",
                closePlan.ClosePlanId));
        }

        if (CloseSignOffMatrixRows.Count == 0)
        {
            CloseSignOffMatrixRows.Add(new AccountingWorkbenchRow(
                "No sign-off requirements",
                "Open",
                "The close plan has no retained sign-off matrix requirements.",
                "Add required approval roles before production close.",
                closePlan.ClosePlanId));
        }

        foreach (var adjustment in closePlan.LateAdjustments)
        {
            CloseLateAdjustmentRows.Add(BuildLateAdjustmentRow(adjustment, closePlan.IsPeriodLocked));
            AddEvidenceReviewRows(CloseEvidenceReviewRows, $"late-adjustment:{adjustment.RequestId}", adjustment.EvidenceLinks, adjustment.Reason);
        }

        if (CloseLateAdjustmentRows.Count == 0)
        {
            CloseLateAdjustmentRows.Add(new AccountingWorkbenchRow(
                "No late adjustments",
                "Clear",
                "No late adjustments are retained on this close plan.",
                "Submitted material late adjustments will appear here for controller review.",
                closePlan.ClosePlanId));
        }

        if (closePlan.Configuration is { } configuration)
        {
            AddEvidenceReviewRows(
                CloseEvidenceReviewRows,
                $"configuration:{configuration.WorkflowId:D}",
                configuration.EvidenceLinks,
                $"Configured by {NormalizeOptional(configuration.ConfiguredBy) ?? "unknown actor"}");
        }

        foreach (var issue in closePlan.ValidationIssues)
        {
            var review = FindCloseEvidenceReview(closePlan, issue);
            CloseEvidenceReviewRows.Add(new AccountingWorkbenchRow(
                issue.Code,
                review is null ? "Review required" : "Review retained",
                review is null
                    ? issue.Message
                    : $"{review.ReviewedBy} reviewed {issue.Code} at {review.ReviewedAtUtc:yyyy-MM-dd HH:mm} UTC. {review.Notes}",
                review is null
                    ? issue.SuggestedAction ?? "Retain blocker review evidence before close certification or period lock."
                    : JoinEvidence(review.EvidenceLinks, "Retained close evidence review has no evidence links."),
                issue.TargetId ?? closePlan.ClosePlanId));
        }

        if (CloseEvidenceReviewRows.Count == 0)
        {
            CloseEvidenceReviewRows.Add(new AccountingWorkbenchRow(
                "No retained evidence",
                "Missing",
                "The close plan does not expose retained setup, task, sign-off, or late-adjustment evidence.",
                "Retain close setup evidence before production certification.",
                closePlan.ClosePlanId));
        }

        foreach (var row in BuildCloseOperatingCoverageRows(closePlan))
        {
            CloseOperatingCoverageRows.Add(row);
        }

        if (CloseOperatingCoverageRows.Count == 0)
        {
            CloseOperatingCoverageRows.Add(new AccountingWorkbenchRow(
                "Operating coverage",
                "Missing",
                "The shared close plan did not return service-owned operating coverage rows.",
                "Refresh from a close-management service version that publishes operating coverage.",
                closePlan.ClosePlanId));
        }
    }

    private static IEnumerable<AccountingWorkbenchRow> BuildCloseOperatingCoverageRows(ClosePeriodPlanDto closePlan)
    {
        foreach (var item in closePlan.OperatingCoverage)
        {
            var blockerSummary = item.BlockingIssues.Count == 0
                ? "No blocking issues"
                : string.Join("; ", item.BlockingIssues.Select(static issue =>
                    $"{issue.Severity}: {issue.Code}{(string.IsNullOrWhiteSpace(issue.TargetId) ? string.Empty : $" ({issue.TargetId})")}"));
            yield return new AccountingWorkbenchRow(
                item.Label,
                FormatAccountingReadinessState(item.State),
                item.RequiredAction,
                $"{item.EvidenceCount:N0} {Pluralize(item.EvidenceCount, "evidence link", "evidence links")}; {item.BlockingIssueCount:N0} {Pluralize(item.BlockingIssueCount, "blocking issue", "blocking issues")}. {blockerSummary}",
                item.ControlId);
        }
    }

    private void ApplyClosingEntriesGate(ClosePeriodPlanDto closePlan)
    {
        ClosingEntryBalanceRows.Clear();
        ClosingEntriesGate = closePlan.ClosingEntriesGate;
        foreach (var balance in closePlan.ClosingEntriesGate?.Balances ?? [])
        {
            ClosingEntryBalanceRows.Add(new AccountingClosePostingBalanceRow(
                string.IsNullOrWhiteSpace(balance.Symbol)
                    ? balance.AccountName
                    : $"{balance.AccountName} ({balance.Symbol.Trim()})",
                balance.AccountType,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:+#,##0.00;-#,##0.00;0.00} {1}",
                    balance.Balance,
                    closePlan.MaterialityPolicy.Currency).TrimEnd(),
                FormatClosePostingBalanceScope(balance.Dimensions),
                NormalizeOptional(balance.FinancialAccountId) ?? "No financial-account id"));
        }
    }

    private static string FormatClosePostingGateState(ClosePostingGateStateDto state)
        => state switch
        {
            ClosePostingGateStateDto.NotRequired => "Not required",
            ClosePostingGateStateDto.DraftQueued => "Draft queued",
            ClosePostingGateStateDto.ReversalQueued => "Reversal queued",
            _ => state.ToString()
        };

    private static string FormatClosePostingBalanceScope(LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
        {
            return "No scoped dimensions returned";
        }

        var labels = new List<string>();
        AddScopeLabel(labels, "Fund", dimensions.FundId);
        AddScopeLabel(labels, "Entity", dimensions.EntityId);
        AddScopeLabel(labels, "Sleeve", dimensions.SleeveId);
        AddScopeLabel(labels, "Strategy", dimensions.StrategyId);
        AddScopeLabel(labels, "Investor", dimensions.InvestorId);
        AddScopeLabel(labels, "Capital account", dimensions.CapitalAccountId);
        AddScopeLabel(labels, "Instrument", dimensions.InstrumentId?.ToString("D"));
        AddScopeLabel(labels, "Position", dimensions.PositionId?.ToString("D"));
        AddScopeLabel(labels, "Tax lot", dimensions.TaxLotId);
        AddScopeLabel(labels, "Cost center", dimensions.CostCenterId);
        AddScopeLabel(labels, "Counterparty", dimensions.CounterpartyId);
        AddScopeLabel(labels, "Organization", dimensions.OrganizationId);
        AddScopeLabel(labels, "Portfolio", dimensions.PortfolioId);
        AddScopeLabel(labels, "Book", dimensions.BookId);
        AddScopeLabel(labels, "Account", dimensions.AccountId);
        foreach (var (key, value) in dimensions.ExternalGlDimensions.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            AddScopeLabel(labels, $"External {key}", value);
        }

        return labels.Count == 0
            ? "No scoped dimensions returned"
            : string.Join(" | ", labels);
    }

    private static void AddScopeLabel(ICollection<string> labels, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            labels.Add($"{label}: {value.Trim()}");
        }
    }

    private static AccountingWorkbenchRow BuildMaterialityPolicyRow(ClosePeriodPlanDto closePlan)
    {
        var materiality = closePlan.MaterialityPolicy;
        var threshold = string.Format(
            CultureInfo.InvariantCulture,
            "{0:N2} {1} / {2:N2}%",
            materiality.AmountThreshold,
            materiality.Currency,
            materiality.PercentThreshold);
        return new AccountingWorkbenchRow(
            materiality.PolicyId,
            materiality.RequiresLateAdjustmentApproval ? "Approval required" : "Advisory",
            $"Review role {materiality.ReviewRole}; threshold {threshold}.",
            closePlan.LedgerBookId is { } ledgerBookId
                ? $"Applies to ledger book {ledgerBookId:D} for period {closePlan.PeriodId}."
                : $"Applies to period {closePlan.PeriodId} without selected ledger-book scope.",
            closePlan.ClosePlanId);
    }

    private static AccountingWorkbenchRow BuildCloseTaskRow(CloseTaskDto task)
    {
        var requiredCount = task.SignOffRequirements.Sum(static requirement => requirement.RequiredApprovalCount);
        var approvedCount = task.SignOffRequirements.Sum(static requirement => requirement.ApprovedCount);
        var dependencyText = task.Dependencies.Count == 0
            ? "No dependencies"
            : $"{task.Dependencies.Count} {Pluralize(task.Dependencies.Count, "dependency", "dependencies")}";
        return new AccountingWorkbenchRow(
            task.TaskId,
            task.Status.ToString(),
            $"{task.DisplayName}; owner {task.Owner}; due {task.DueDate:yyyy-MM-dd}; {dependencyText}; sign-offs {approvedCount}/{requiredCount}.",
            string.IsNullOrWhiteSpace(task.BlockerReason)
                ? JoinEvidence(task.EvidenceLinks, "No task blocker retained.")
                : task.BlockerReason,
            task.TaskId);
    }

    private static AccountingWorkbenchRow BuildLateAdjustmentRow(
        LateAdjustmentRequestDto adjustment,
        bool periodLocked)
    {
        var decisionText = adjustment.DecidedAtUtc is { } decidedAt
            ? $"Decision by {NormalizeOptional(adjustment.DecidedBy) ?? "unknown actor"} at {decidedAt:yyyy-MM-dd HH:mm} UTC."
            : "No retained review decision.";
        return new AccountingWorkbenchRow(
            adjustment.RequestId,
            adjustment.ApprovalState.ToString(),
            $"{adjustment.Amount:N2} {adjustment.Currency} journal {adjustment.JournalEntryId:D}; requested by {adjustment.RequestedBy} at {adjustment.RequestedAtUtc:yyyy-MM-dd HH:mm} UTC.",
            $"{decisionText} {(periodLocked ? "Period is locked." : adjustment.DecisionNotes ?? adjustment.Reason)}",
            adjustment.RequestId);
    }

    private static void AddEvidenceReviewRows(
        ObservableCollection<AccountingWorkbenchRow> rows,
        string source,
        IReadOnlyList<string> evidenceLinks,
        string detail)
    {
        foreach (var evidence in evidenceLinks.Where(static link => !string.IsNullOrWhiteSpace(link)))
        {
            rows.Add(new AccountingWorkbenchRow(
                source,
                "Retained",
                detail,
                evidence.Trim(),
                evidence.Trim()));
        }
    }

    private static string JoinEvidence(IReadOnlyList<string> evidenceLinks, string fallback)
    {
        var retained = evidenceLinks
            .Where(static evidence => !string.IsNullOrWhiteSpace(evidence))
            .Select(static evidence => evidence.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return retained.Length == 0 ? fallback : string.Join("; ", retained);
    }

    private static string FormatSignedAt(DateTimeOffset? signedAt)
        => signedAt is { } value ? $" at {value:yyyy-MM-dd HH:mm} UTC" : string.Empty;

    private static string FormatAccountingReadinessState(AccountingReadinessStateDto state)
        => state switch
        {
            AccountingReadinessStateDto.NotStarted => "Not started",
            AccountingReadinessStateDto.NeedsAttention => "Needs attention",
            AccountingReadinessStateDto.ReadyForReview => "Ready for review",
            _ => state.ToString()
        };

    private static string Pluralize(int count, string singular, string plural)
        => count == 1 ? singular : plural;

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

    private bool TryParseLateAdjustmentAmount(out decimal amount)
    {
        amount = 0m;
        return decimal.TryParse(
            LateAdjustmentAmountText,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out amount);
    }

    private decimal ParseLateAdjustmentAmount()
        => decimal.Parse(LateAdjustmentAmountText.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture);

    private bool TryParseCloseTaskSignOffDecision(out ManualJournalEntryStatusDto decision)
    {
        if (Enum.TryParse(CloseTaskSignOffDecision, ignoreCase: true, out decision) &&
            decision is ManualJournalEntryStatusDto.Approved or ManualJournalEntryStatusDto.Rejected)
        {
            return true;
        }

        decision = default;
        return false;
    }

    private static ManualJournalEntryStatusDto ParseCloseTaskSignOffDecision(string value)
        => Enum.TryParse<ManualJournalEntryStatusDto>(value, ignoreCase: true, out var decision) &&
           decision is ManualJournalEntryStatusDto.Approved or ManualJournalEntryStatusDto.Rejected
            ? decision
            : throw new ArgumentException("Close task sign-off decision must be Approved or Rejected.", nameof(CloseTaskSignOffDecision));

    private static ManualJournalEntryStatusDto ParseCloseReviewDecision(string value)
        => Enum.TryParse<ManualJournalEntryStatusDto>(value, ignoreCase: true, out var decision) &&
           decision is ManualJournalEntryStatusDto.Approved or ManualJournalEntryStatusDto.Rejected
            ? decision
            : throw new ArgumentException("Close review decision must be Approved or Rejected.", nameof(LateAdjustmentReviewDecision));

    private static bool TryParseCloseReviewDecision(string value, out ManualJournalEntryStatusDto decision)
    {
        if (Enum.TryParse(value, ignoreCase: true, out decision) &&
            decision is ManualJournalEntryStatusDto.Approved or ManualJournalEntryStatusDto.Rejected)
        {
            return true;
        }

        decision = default;
        return false;
    }

    private static IReadOnlyList<string> ParseCloseSetupDependencies(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static dependency => ParseCloseSetupDependencyEntry(dependency).DependencyId)
                .Where(static dependency => dependency.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private static IReadOnlyList<CloseTaskSignOffRequirementConfigurationDto> ParseCloseSetupSignOffRequirements(string? value)
    {
        var requirements = new Dictionary<string, CloseTaskSignOffRequirementConfigurationDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in SplitCloseSetupSignOffRequirements(value)
                     .Select(static item => ParseCloseSetupSignOffRequirement(item)))
        {
            if (!string.IsNullOrWhiteSpace(entry.Role) && entry.RequiredApprovalCount > 0)
            {
                requirements[entry.Role] = entry;
            }
        }

        return requirements.Values.ToArray();
    }

    private static IEnumerable<string> SplitCloseSetupSignOffRequirements(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static item => item.Length > 0);

    private static CloseTaskSignOffRequirementConfigurationDto ParseCloseSetupSignOffRequirement(string value)
    {
        var parts = value.Contains('|', StringComparison.Ordinal)
            ? value.Split('|', StringSplitOptions.TrimEntries)
            : value.Split(':', StringSplitOptions.TrimEntries);
        var role = parts.Length > 0 ? parts[0] : string.Empty;
        var requiredCountText = parts.Length > 1 ? parts[1] : "1";
        _ = int.TryParse(requiredCountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var requiredCount);
        var evidence = parts.Length > 2
            ? string.Join(":", parts.Skip(2)).Trim()
            : string.Empty;
        return new CloseTaskSignOffRequirementConfigurationDto(role, requiredCount, NormalizeOptional(evidence));
    }

    private static IReadOnlyList<CloseTaskSignOffRequirementConfigurationDto> BuildCloseSetupSignOffRequirementConfigurations(
        IReadOnlyList<CloseSignOffRequirementDto> requirements)
        => requirements
            .Select(static requirement => new CloseTaskSignOffRequirementConfigurationDto(
                requirement.Role,
                Math.Max(1, requirement.RequiredApprovalCount),
                string.IsNullOrWhiteSpace(requirement.EvidenceRequirement)
                    ? "Retained close checklist evidence"
                    : requirement.EvidenceRequirement.Trim()))
            .ToArray();

    private static string BuildCloseSetupSignOffRequirementText(IReadOnlyList<CloseSignOffRequirementDto> requirements)
        => string.Join(
            Environment.NewLine,
            requirements.Select(static requirement =>
                $"{requirement.Role} | {Math.Max(1, requirement.RequiredApprovalCount).ToString(CultureInfo.InvariantCulture)} | {(string.IsNullOrWhiteSpace(requirement.EvidenceRequirement) ? "Retained close checklist evidence" : requirement.EvidenceRequirement.Trim())}"));

    private static IReadOnlyDictionary<string, string> ParseCloseSetupDependencyReasonOverrides(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in value.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Select(static item => ParseCloseSetupDependencyEntry(item)))
        {
            if (!string.IsNullOrWhiteSpace(entry.DependencyId) &&
                !string.IsNullOrWhiteSpace(entry.Reason) &&
                !overrides.ContainsKey(entry.DependencyId))
            {
                overrides[entry.DependencyId] = entry.Reason;
            }
        }

        return overrides;
    }

    private static (string DependencyId, string? Reason) ParseCloseSetupDependencyEntry(string value)
    {
        var item = value.Trim();
        if (item.Length == 0)
        {
            return (string.Empty, null);
        }

        var colonIndex = item.IndexOf(':', StringComparison.Ordinal);
        var equalsIndex = item.IndexOf('=', StringComparison.Ordinal);
        var separatorIndex = new[] { colonIndex, equalsIndex }
            .Where(static index => index > 0)
            .DefaultIfEmpty(-1)
            .Min();
        if (separatorIndex < 0)
        {
            return (item, null);
        }

        var dependencyId = item[..separatorIndex].Trim();
        var reason = item[(separatorIndex + 1)..].Trim();
        return (dependencyId, reason.Length == 0 ? null : reason);
    }

    private static IReadOnlyList<CloseTaskDependencyConfigurationDto> BuildCloseSetupDependencyConfigurations(
        IReadOnlyList<string> dependencyIds,
        IReadOnlyDictionary<string, string> dependencyIdReasons,
        IReadOnlyDictionary<string, string> dependencyReasonOverrides,
        string? fallbackReason,
        IReadOnlyList<CloseDependencyDto> existingDependencies)
        => dependencyIds
            .Select(dependencyId => new CloseTaskDependencyConfigurationDto(
                dependencyId,
                ResolveCloseSetupDependencyReason(
                    dependencyId,
                    dependencyIdReasons,
                    dependencyReasonOverrides,
                    fallbackReason,
                    existingDependencies)))
            .ToArray();

    private static string ResolveCloseSetupDependencyReason(
        string dependencyId,
        IReadOnlyDictionary<string, string> dependencyIdReasons,
        IReadOnlyDictionary<string, string> dependencyReasonOverrides,
        string? fallbackReason,
        IReadOnlyList<CloseDependencyDto> existingDependencies)
        => dependencyIdReasons.TryGetValue(dependencyId, out var reasonFromDependencyIds) && !string.IsNullOrWhiteSpace(reasonFromDependencyIds)
            ? reasonFromDependencyIds
            : dependencyReasonOverrides.TryGetValue(dependencyId, out var reasonFromOverrides) && !string.IsNullOrWhiteSpace(reasonFromOverrides)
                ? reasonFromOverrides
                : fallbackReason
                  ?? existingDependencies.FirstOrDefault(dependency =>
                      string.Equals(dependency.DependsOnTaskId, dependencyId, StringComparison.OrdinalIgnoreCase))?.Reason
                  ?? "Configured close-plan dependency.";

    private static string BuildCloseSetupDependencyReason(IReadOnlyList<CloseDependencyDto> dependencies)
    {
        var reasons = dependencies
            .Select(static dependency => dependency.Reason.Trim())
            .Where(static reason => reason.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return reasons.Length == 1 ? reasons[0] : "Configured close-plan dependency.";
    }

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

    private static IReadOnlyList<string> BuildLateAdjustmentRequestEvidence(
        Guid workflowId,
        ClosePeriodPlanDto closePlan,
        Guid journalEntryId)
    {
        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"wpf://accounting/close/late-adjustment/{workflowId:D}/{journalEntryId:D}",
            $"evidence://late-adjustment-request/workflow/{workflowId:D}/journal/{journalEntryId:D}/period/{closePlan.PeriodId}"
        };

        if (closePlan.LedgerBookId is { } ledgerBookId)
        {
            links.Add($"evidence://late-adjustment-request/book/{ledgerBookId:D}/journal/{journalEntryId:D}/period/{closePlan.PeriodId}");
        }

        return links.ToArray();
    }

    private static LockClosePeriodRequestDto BuildClosePeriodLockRequest(
        Guid workflowId,
        long workflowVersion,
        ClosePeriodPlanDto closePlan,
        bool prepareClosingEntriesOnly)
    {
        var reportPackId = BuildCloseReportPackId(closePlan);
        var closePackageId = $"close-package-{closePlan.FundProfileId}-{closePlan.PeriodId}";
        var manifestId = $"manifest-{closePlan.FundProfileId}-{closePlan.PeriodId}";
        return new LockClosePeriodRequestDto(
            workflowId,
            ExpectedWorkflowVersion: workflowVersion,
            Actor: "wpf-accounting-controller",
            Rationale: prepareClosingEntriesOnly
                ? "Queue closing entries from WPF Accounting Close without hard-locking the accounting period."
                : "Lock close period from WPF Accounting Close after checklist, sign-off, reconciliation, report certification, and closing-entry posting review.",
            ReportPackId: reportPackId,
            EvidenceLinks: BuildClosePeriodLockEvidence(workflowId, closePlan, reportPackId, closePackageId, manifestId),
            ChecklistControlApprovals: BuildClosePeriodLockApprovals(closePlan),
            CorrelationId: prepareClosingEntriesOnly
                ? $"wpf-close-period-prepare-closing-entries-{workflowId:D}"
                : $"wpf-close-period-lock-{workflowId:D}",
            ClosePackageId: closePackageId,
            ClosePackageManifestId: manifestId,
            ClosePackageRetainedManifestRoute: $"/workstation/reporting/packages/{manifestId}",
            ActionOrigin: OperationsActionOriginDto.HumanOperator,
            PrepareClosingEntriesOnly: prepareClosingEntriesOnly);
    }

    private SignOffCloseTaskRequestDto BuildCloseTaskSignOffRequest(
        Guid workflowId,
        ClosePeriodPlanDto closePlan,
        CloseTaskDto task,
        string roleText,
        string decisionText,
        string notesText)
    {
        var role = NormalizeRequired(roleText, task.SignOffRequirements.FirstOrDefault()?.Role ?? task.Owner);
        var decision = ParseCloseTaskSignOffDecision(decisionText);
        var notes = NormalizeRequired(
            notesText,
            decision == ManualJournalEntryStatusDto.Approved
                ? $"WPF Accounting Close retained {role} sign-off evidence for {task.DisplayName}."
                : $"WPF Accounting Close retained {role} rejection evidence for {task.DisplayName}.");
        return new SignOffCloseTaskRequestDto(
            workflowId,
            task.TaskId,
            role,
            decision,
            Actor: "wpf-accounting-controller",
            Notes: notes,
            EvidenceLinks: BuildCloseTaskSignOffEvidence(workflowId, closePlan, task, role),
            CorrelationId: $"wpf-close-task-signoff-{workflowId:D}-{SanitizeForCorrelation(task.TaskId)}-{SanitizeForCorrelation(role)}",
            ActionOrigin: OperationsActionOriginDto.HumanOperator);
    }

    private ReviewLateAdjustmentRequestDto BuildReviewLateAdjustmentRequest(
        Guid workflowId,
        ClosePeriodPlanDto closePlan,
        LateAdjustmentRequestDto adjustment)
    {
        var decision = ParseCloseReviewDecision(LateAdjustmentReviewDecision);
        var decisionText = decision.ToString();
        var notes = NormalizeRequired(
            LateAdjustmentReviewNotes,
            $"WPF Accounting Close {decisionText.ToLowerInvariant()} late adjustment {adjustment.RequestId}.");
        return new ReviewLateAdjustmentRequestDto(
            workflowId,
            adjustment.RequestId,
            decision,
            Actor: "wpf-accounting-controller",
            Notes: notes,
            EvidenceLinks: BuildLateAdjustmentReviewEvidence(workflowId, closePlan, adjustment, decision),
            CorrelationId: $"wpf-late-adjustment-review-{workflowId:D}-{SanitizeForCorrelation(adjustment.RequestId)}-{SanitizeForCorrelation(decisionText)}",
            ActionOrigin: OperationsActionOriginDto.HumanOperator);
    }

    private static IReadOnlyList<string> BuildLateAdjustmentReviewEvidence(
        Guid workflowId,
        ClosePeriodPlanDto closePlan,
        LateAdjustmentRequestDto adjustment,
        ManualJournalEntryStatusDto decision)
    {
        var decisionToken = decision == ManualJournalEntryStatusDto.Rejected ? "rejection" : "approval";
        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"wpf://accounting/close/late-adjustment-review/{workflowId:D}/{adjustment.RequestId}/{decisionToken}",
            $"evidence://late-adjustment-review/request/{adjustment.RequestId}/workflow/{workflowId:D}/period/{closePlan.PeriodId}/{decisionToken}"
        };

        if (closePlan.LedgerBookId is { } ledgerBookId)
        {
            links.Add($"evidence://late-adjustment-review/request/{adjustment.RequestId}/book/{ledgerBookId:D}/period/{closePlan.PeriodId}/{decisionToken}");
        }

        foreach (var evidence in adjustment.EvidenceLinks)
        {
            if (!string.IsNullOrWhiteSpace(evidence))
            {
                links.Add(evidence.Trim());
            }
        }

        return links.ToArray();
    }

    private ReviewCloseEvidenceRequestDto BuildReviewCloseEvidenceRequest(
        Guid workflowId,
        ClosePeriodPlanDto closePlan,
        AccountingConfigurationValidationIssueDto issue)
    {
        var targetId = NormalizeOptional(issue.TargetId) ?? closePlan.ClosePlanId;
        return new ReviewCloseEvidenceRequestDto(
            workflowId,
            issue.Code,
            issue.TargetId,
            Actor: "wpf-accounting-controller",
            Notes: NormalizeRequired(
                CloseEvidenceReviewNotes,
                $"WPF Accounting Close reviewed blocker {issue.Code} for {targetId}. {issue.Message}"),
            EvidenceLinks: BuildCloseEvidenceReviewEvidence(workflowId, closePlan, issue),
            CorrelationId: $"wpf-close-evidence-review-{workflowId:D}-{SanitizeForCorrelation(issue.Code)}-{SanitizeForCorrelation(targetId)}",
            ActionOrigin: OperationsActionOriginDto.HumanOperator);
    }

    private static IReadOnlyList<string> BuildCloseEvidenceReviewEvidence(
        Guid workflowId,
        ClosePeriodPlanDto closePlan,
        AccountingConfigurationValidationIssueDto issue)
    {
        var targetId = NormalizeOptional(issue.TargetId) ?? closePlan.ClosePlanId;
        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"wpf://accounting/close/evidence-review/{workflowId:D}/{issue.Code}/{targetId}",
            $"evidence://close-review/workflow/{workflowId:D}/period/{closePlan.PeriodId}/issue/{issue.Code}/target/{targetId}"
        };

        if (closePlan.LedgerBookId is { } ledgerBookId)
        {
            links.Add($"evidence://close-review/workflow/{workflowId:D}/period/{closePlan.PeriodId}/book/{ledgerBookId:D}/issue/{issue.Code}/target/{targetId}");
        }

        return links.ToArray();
    }

    private static IReadOnlyList<string> BuildCloseTaskSignOffEvidence(
        Guid workflowId,
        ClosePeriodPlanDto closePlan,
        CloseTaskDto task,
        string role)
    {
        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"wpf://accounting/close/task-signoff/{workflowId:D}/{task.TaskId}/{role}",
            $"evidence://close-task-signoff/workflow/{workflowId:D}/task/{task.TaskId}/role/{role}/period/{closePlan.PeriodId}"
        };

        if (closePlan.LedgerBookId is { } ledgerBookId)
        {
            links.Add($"evidence://close-task-signoff/book/{ledgerBookId:D}/task/{task.TaskId}/role/{role}/period/{closePlan.PeriodId}");
        }

        foreach (var evidence in task.EvidenceLinks)
        {
            if (!string.IsNullOrWhiteSpace(evidence))
            {
                links.Add(evidence.Trim());
            }
        }

        return links.ToArray();
    }

    private static CloseTaskDto? ResolveNextSignOffTask(ClosePeriodPlanDto closePlan)
        => closePlan.Tasks.FirstOrDefault(static task =>
            task.Status is not CloseTaskStatusDto.SignedOff and not CloseTaskStatusDto.Blocked and not CloseTaskStatusDto.WaitingOnDependency &&
            task.SignOffRequirements.Any(static requirement => !requirement.IsSatisfied));

    private CloseTaskDto? ResolveCloseTaskSignOffTask(ClosePeriodPlanDto closePlan)
    {
        var taskId = NormalizeOptional(CloseTaskSignOffTaskId);
        return taskId is null
            ? null
            : closePlan.Tasks.FirstOrDefault(task => string.Equals(task.TaskId, taskId, StringComparison.OrdinalIgnoreCase));
    }

    private static LateAdjustmentRequestDto? ResolveNextLateAdjustment(ClosePeriodPlanDto closePlan)
        => closePlan.LateAdjustments.FirstOrDefault(static adjustment =>
            adjustment.ApprovalState == ManualJournalEntryStatusDto.Submitted);

    private LateAdjustmentRequestDto? ResolveLateAdjustmentReviewDraft(ClosePeriodPlanDto closePlan)
    {
        if (!TryParseCloseReviewDecision(LateAdjustmentReviewDecision, out _))
        {
            return null;
        }

        var requestId = NormalizeOptional(LateAdjustmentReviewRequestId);
        return requestId is null
            ? null
            : closePlan.LateAdjustments.FirstOrDefault(adjustment =>
                string.Equals(adjustment.RequestId, requestId, StringComparison.OrdinalIgnoreCase) &&
                adjustment.ApprovalState == ManualJournalEntryStatusDto.Submitted);
    }

    private static AccountingConfigurationValidationIssueDto? ResolveNextCloseEvidenceReviewIssue(ClosePeriodPlanDto closePlan)
        => closePlan.ValidationIssues.FirstOrDefault(issue => FindCloseEvidenceReview(closePlan, issue) is null);

    private AccountingConfigurationValidationIssueDto? ResolveCloseEvidenceReviewIssue(ClosePeriodPlanDto closePlan)
    {
        var issueCode = NormalizeOptional(CloseEvidenceReviewIssueCode);
        if (issueCode is null)
        {
            return null;
        }

        var targetId = NormalizeOptional(CloseEvidenceReviewTargetId);
        return closePlan.ValidationIssues.FirstOrDefault(issue =>
            string.Equals(issue.Code, issueCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(issue.TargetId ?? string.Empty, targetId ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
            FindCloseEvidenceReview(closePlan, issue) is null);
    }

    private static CloseEvidenceReviewDto? FindCloseEvidenceReview(
        ClosePeriodPlanDto closePlan,
        AccountingConfigurationValidationIssueDto issue)
        => closePlan.EvidenceReviews
            .OrderByDescending(static review => review.ReviewedAtUtc)
            .FirstOrDefault(review =>
                string.Equals(review.IssueCode, issue.Code, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(review.TargetId ?? string.Empty, issue.TargetId ?? string.Empty, StringComparison.OrdinalIgnoreCase));

    private static string SanitizeForCorrelation(string value)
        => string.Concat((value ?? string.Empty)
            .Trim()
            .Select(static ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-'))
            .Trim('-');

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

public sealed record CloseSetupTaskOption(
    string TaskId,
    string DisplayName,
    string Status,
    string Owner,
    string DueDate,
    string SignOffSummary);

public sealed record AccountingClosePostingBalanceRow(
    string AccountName,
    string AccountType,
    string Balance,
    string Scope,
    string FinancialAccountId);

public sealed record CloseWorkflowStep(
    string StepId,
    string Label,
    string Status,
    string Detail,
    string Evidence,
    string? DisabledReason,
    IAsyncRelayCommand Command)
{
    public string ActionLabel => Label;
    public string DisabledReasonText => string.IsNullOrWhiteSpace(DisabledReason)
        ? "Ready"
        : DisabledReason;
}
