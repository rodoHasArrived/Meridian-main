using System.Collections.ObjectModel;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed class AccountingCloseViewModel : Meridian.Wpf.ViewModels.BindableBase
{
    private readonly IAccountingProjectionQueryService _queryService;
    private readonly IAccountingCloseManagementService? _closeManagementService;
    private ClosePeriodPlanDto? _closePlan;
    private Guid _closeWorkflowId;
    private ClosePeriodState _closeState = ClosePeriodState.Open;
    private string _closeStateText = "Open";
    private string _trialBalanceStatusText = "Trial balance has not loaded.";
    private string _closePlanSetupStatusText = "Load a close plan before retaining governed close setup.";
    private string _selectedAuditDetailText = "Select a journal audit row to inspect source-event and approval linkage.";
    private SourceLinkedAuditLine? _selectedAuditLine;

    public AccountingCloseViewModel(
        IAccountingProjectionQueryService queryService,
        IAccountingCloseManagementService? closeManagementService = null)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _closeManagementService = closeManagementService;
        ConfigureClosePlanCommand = new AsyncRelayCommand(ConfigureClosePlanAsync, CanConfigureClosePlan);
    }

    public ObservableCollection<TrialBalanceLine> TrialBalance { get; } = [];
    public ObservableCollection<RollForwardLine> RollForward { get; } = [];
    public ObservableCollection<SourceLinkedAuditLine> AuditTrail { get; } = [];

    public IAsyncRelayCommand ConfigureClosePlanCommand { get; }

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
    {
        ArgumentNullException.ThrowIfNull(closePlan);
        _closeWorkflowId = workflowId;
        _closePlan = closePlan;
        ClosePlanSetupStatusText = workflowId == Guid.Empty
            ? $"Close plan {closePlan.PeriodId} loaded without workflow context; setup retention is disabled."
            : closePlan.IsPeriodLocked
            ? $"Close plan {closePlan.PeriodId} is locked; setup changes require a governed reopen workflow."
            : $"Close plan {closePlan.PeriodId} loaded for governed setup retention.";
        ConfigureClosePlanCommand.NotifyCanExecuteChanged();
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

            ApplyClosePlan(_closeWorkflowId, updated);
            ClosePlanSetupStatusText = $"Retained close-plan setup for {updated.PeriodId}.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ClosePlanSetupStatusText = $"Close-plan setup could not be retained: {ex.Message}";
        }
    }

    private static UpsertClosePeriodPlanConfigurationRequestDto BuildClosePlanConfigurationRequest(
        Guid workflowId,
        ClosePeriodPlanDto closePlan)
    {
        var taskConfigurations = closePlan.Tasks
            .Select(static task =>
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

                return new CloseTaskConfigurationDto(
                    task.TaskId,
                    task.DisplayName,
                    task.Owner,
                    task.DueDate,
                    requiredApprovalCount,
                    string.IsNullOrWhiteSpace(requiredEvidence) ? "Retained close checklist evidence" : requiredEvidence,
                    task.Dependencies.Select(static dependency => dependency.DependsOnTaskId).ToArray());
            })
            .ToArray();

        return new UpsertClosePeriodPlanConfigurationRequestDto(
            workflowId,
            closePlan.MaterialityPolicy,
            taskConfigurations,
            Actor: "wpf-accounting-controller",
            EvidenceLinks: BuildClosePlanConfigurationEvidence(workflowId, closePlan),
            CorrelationId: $"wpf-close-plan-configuration-{workflowId:D}",
            ActionOrigin: OperationsActionOriginDto.HumanOperator);
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
}
