using System.Collections.ObjectModel;
using Meridian.Application.AccountingClose;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed class AccountingCloseViewModel
{
    private readonly IAccountingProjectionQueryService _queryService;

    public AccountingCloseViewModel(IAccountingProjectionQueryService queryService)
    {
        _queryService = queryService;
    }

    public ObservableCollection<TrialBalanceLine> TrialBalance { get; } = [];
    public ObservableCollection<RollForwardLine> RollForward { get; } = [];
    public ObservableCollection<SourceLinkedAuditLine> AuditTrail { get; } = [];

    public ClosePeriodState CloseState { get; private set; } = ClosePeriodState.Open;

    public void Load(string ledgerId)
    {
        TrialBalance.Clear();
        foreach (var line in _queryService.GetTrialBalance(ledgerId)) TrialBalance.Add(line);

        RollForward.Clear();
        foreach (var line in _queryService.GetRollForward(ledgerId)) RollForward.Add(line);

        AuditTrail.Clear();
        foreach (var line in _queryService.GetAuditLines(ledgerId)) AuditTrail.Add(line);
    }

    public void SetCloseState(ClosePeriodState state) => CloseState = state;
}
