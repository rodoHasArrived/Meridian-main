using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Intercompany operations tabs that each wrap one shared API workspace: management fees, rate fixings, balance
/// confirmation and accrual schedules. Each is null when no intercompany client is available.
/// </summary>
public sealed partial class FundLedgerViewModel
{
    /// <summary>Governed fixed management-fee accrual over the shared intercompany API.</summary>
    public IntercompanyManagementFeeWorkspace? ManagementFee { get; private set; }

    /// <summary>Reviewed index fixings that floating-rate intercompany accruals read.</summary>
    public IntercompanyRateFixingWorkspace? RateFixings { get; private set; }

    /// <summary>Advisory reciprocal balance confirmation over the shared confirmation API.</summary>
    public IntercompanyBalanceConfirmationWorkspace? BalanceConfirmation { get; private set; }

    /// <summary>Opt-in, reviewed accrual schedules over the same shared schedule API as the browser workstation.</summary>
    public IntercompanyAccrualScheduleWorkspace? AccrualSchedules { get; private set; }

    private void InitializeIntercompanyOperations(IWorkstationIntercompanyAgreementApiClient? client)
    {
        if (client is null)
            return;
        ManagementFee = new IntercompanyManagementFeeWorkspace(client);
        RateFixings = new IntercompanyRateFixingWorkspace(client);
        BalanceConfirmation = new IntercompanyBalanceConfirmationWorkspace(client);
        AccrualSchedules = new IntercompanyAccrualScheduleWorkspace(client);
    }

    private void DisposeIntercompanyOperations()
    {
        ManagementFee?.Dispose();
        RateFixings?.Dispose();
        BalanceConfirmation?.Dispose();
        AccrualSchedules?.Dispose();
    }

    private void ResetIntercompanyOperationsSession()
    {
        ManagementFee?.ResetSession();
        RateFixings?.ResetSession();
        BalanceConfirmation?.ResetSession();
        AccrualSchedules?.ResetSession();
    }
}
