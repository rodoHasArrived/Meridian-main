using System.Collections.ObjectModel;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.Identity.Auth;
using Meridian.Ui.Services.Services.Accounting;
using Meridian.Wpf.Services;
using System.Globalization;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed partial class AccountingCloseViewModel : Meridian.Wpf.ViewModels.BindableBase
{
    private readonly IAccountingProjectionQueryService _queryService;
    private readonly IAccountingCloseManagementService? _closeManagementService;
    private readonly DesktopAuthenticationSession? _authenticationSession;
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
        IAccountingCloseManagementService? closeManagementService = null,
        DesktopAuthenticationSession? authenticationSession = null)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _closeManagementService = closeManagementService;
        _authenticationSession = authenticationSession;
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
           HasLedgerMutationPermission() &&
           ValidateCloseSetupDraft(closePlan) is null;

    private bool HasLedgerMutationPermission()
        => TryGetLedgerMutationActor(out _);

    private bool TryGetLedgerMutationActor(out string actor)
    {
        actor = _authenticationSession?.CurrentActor.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(actor) &&
               (_authenticationSession!.HasPermission(UserPermission.AdminMaintenance) ||
                _authenticationSession.HasPermission(UserPermission.ManageDirectLending));
    }

    private bool CanLoadClosePlan()
        => _closeManagementService is not null &&
           Guid.TryParse(CloseWorkflowIdText, out var workflowId) &&
           workflowId != Guid.Empty;

    private bool CanSignOffCloseTask()
        => _closeManagementService is not null &&
           _closeWorkflowId != Guid.Empty &&
           _closePlan is { IsPeriodLocked: false } closePlan &&
           HasLedgerMutationPermission() &&
           ValidateCloseTaskSignOffDraft(closePlan) is null;

    private bool CanReviewLateAdjustment()
        => _closeManagementService is not null &&
           _closeWorkflowId != Guid.Empty &&
           _closePlan is { IsPeriodLocked: false } closePlan &&
           HasLedgerMutationPermission() &&
           ResolveLateAdjustmentReviewDraft(closePlan) is not null;

    private bool CanReviewCloseEvidence()
        => _closeManagementService is not null &&
           _closeWorkflowId != Guid.Empty &&
           _closePlan is { IsPeriodLocked: false } closePlan &&
           HasLedgerMutationPermission() &&
           ResolveCloseEvidenceReviewIssue(closePlan) is not null;

    private bool CanRequestLateAdjustment()
        => _closeManagementService is not null &&
           _closeWorkflowId != Guid.Empty &&
           _closePlan is { IsPeriodLocked: false } closePlan &&
           HasLedgerMutationPermission() &&
           ValidateLateAdjustmentDraft(closePlan) is null;

    private bool CanQueueClosingEntries()
        => _closeManagementService is not null &&
           _closeWorkflowId != Guid.Empty &&
           _closePlan is { IsPeriodLocked: false } &&
           HasLedgerMutationPermission() &&
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
           HasLedgerMutationPermission() &&
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

        if (!TryGetLedgerMutationActor(out var actor))
        {
            ClosePlanSetupStatusText = "Your desktop session does not have permission to retain close-plan setup.";
            return;
        }

        try
        {
            var request = BuildClosePlanConfigurationRequest(_closeWorkflowId, _closePlan, actor);
            var updated = await _closeManagementService
                .ConfigurePeriodPlanAsync(request, actor)
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

        if (!TryGetLedgerMutationActor(out var actor))
        {
            CloseTaskSignOffStatusText = "Your desktop session does not have permission to retain close task sign-off evidence.";
            return;
        }

        try
        {
            var request = BuildCloseTaskSignOffRequest(
                _closeWorkflowId,
                _closePlan,
                task,
                CloseTaskSignOffRole,
                CloseTaskSignOffDecision,
                CloseTaskSignOffNotes,
                actor);
            var updated = await _closeManagementService
                .SignOffCloseTaskAsync(request, actor)
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

        if (!TryGetLedgerMutationActor(out var actor))
        {
            LateAdjustmentRequestStatusText = "Your desktop session does not have permission to request late adjustments.";
            return;
        }

        try
        {
            var request = BuildCreateLateAdjustmentRequest(_closeWorkflowId, _closePlan, actor);
            var updated = await _closeManagementService
                .RequestLateAdjustmentAsync(request, actor)
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

        if (!TryGetLedgerMutationActor(out var actor))
        {
            LateAdjustmentReviewStatusText = "Your desktop session does not have permission to review late adjustments.";
            return;
        }

        try
        {
            var request = BuildReviewLateAdjustmentRequest(_closeWorkflowId, _closePlan, adjustment, actor);
            var updated = await _closeManagementService
                .ReviewLateAdjustmentAsync(request, actor)
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

        if (!TryGetLedgerMutationActor(out var actor))
        {
            CloseEvidenceReviewStatusText = "Your desktop session does not have permission to retain close evidence review.";
            return;
        }

        try
        {
            var request = BuildReviewCloseEvidenceRequest(_closeWorkflowId, _closePlan, issue, actor);
            var updated = await _closeManagementService
                .ReviewCloseEvidenceAsync(request, actor)
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

        if (!TryGetLedgerMutationActor(out var actor))
        {
            ClosePeriodLockStatusText = "Your desktop session does not have permission to queue closing entries.";
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
                actor,
                prepareClosingEntriesOnly: true);
            var result = await _closeManagementService
                .LockClosePeriodAsync(request, actor)
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

        if (!TryGetLedgerMutationActor(out var actor))
        {
            ClosePeriodLockStatusText = "Your desktop session does not have permission to lock the close period.";
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
                actor,
                prepareClosingEntriesOnly: false);
            var result = await _closeManagementService
                .LockClosePeriodAsync(request, actor)
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

}
