using System.Collections.ObjectModel;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed class AccountingCloseViewModel : Meridian.Wpf.ViewModels.BindableBase
{
    private readonly IAccountingProjectionQueryService _queryService;
    private ClosePeriodState _closeState = ClosePeriodState.Open;
    private string _closeStateText = "Open";
    private string _trialBalanceStatusText = "Trial balance has not loaded.";
    private string _selectedAuditDetailText = "Select a journal audit row to inspect source-event and approval linkage.";
    private SourceLinkedAuditLine? _selectedAuditLine;

    public AccountingCloseViewModel(IAccountingProjectionQueryService queryService)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
    }

    public ObservableCollection<TrialBalanceLine> TrialBalance { get; } = [];
    public ObservableCollection<RollForwardLine> RollForward { get; } = [];
    public ObservableCollection<SourceLinkedAuditLine> AuditTrail { get; } = [];

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

    public void SetCloseState(ClosePeriodState state)
    {
        CloseState = state;
        CloseStateText = state.ToString();
    }
}
