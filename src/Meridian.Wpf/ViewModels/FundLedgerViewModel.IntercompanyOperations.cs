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

    /// <summary>Durable agreement follow-up and read-only guarantee disclosures over the shared API.</summary>
    public IntercompanyAgreementWorkQueueWorkspace? AgreementWorkQueue { get; private set; }

    private void InitializeIntercompanyOperations(IWorkstationIntercompanyAgreementApiClient? client)
    {
        if (client is null)
            return;
        ManagementFee = new IntercompanyManagementFeeWorkspace(client);
        RateFixings = new IntercompanyRateFixingWorkspace(client);
        BalanceConfirmation = new IntercompanyBalanceConfirmationWorkspace(client);
        AccrualSchedules = new IntercompanyAccrualScheduleWorkspace(client);
        AgreementWorkQueue = new IntercompanyAgreementWorkQueueWorkspace(client, async (item, asOf, token) =>
        {
            if (Agreements is null || Agreements.IsDirty || Agreements.IsBusy)
                throw new InvalidOperationException("Finish or discard the open agreement edit before opening another agreement.");
            var response = await client.GetAsync(item.AgreementId, asOf, token);
            token.ThrowIfCancellationRequested();
            if (!response.Success || response.Data is null)
                throw new InvalidOperationException(response.ErrorMessage ?? "The agreement is unavailable in this scope.");
            if (Agreements.IsDirty || Agreements.IsBusy)
                throw new InvalidOperationException("An agreement edit started while the case was loading. Finish it before continuing.");
            Agreements.AsOf = asOf.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            Agreements.LedgerBook = item.LedgerBookId.ToString("D");
            Agreements.SelectedRow = response.Data;
            await Agreements.OpenCommand.ExecuteAsync(null);
            token.ThrowIfCancellationRequested();
            // The existing Agreements tab follows the ten stable fund-operation tabs.
            SelectedTabIndex = 10;
        });
    }

    private void DisposeIntercompanyOperations()
    {
        ManagementFee?.Dispose();
        RateFixings?.Dispose();
        BalanceConfirmation?.Dispose();
        AccrualSchedules?.Dispose();
        AgreementWorkQueue?.Dispose();
    }

    private void ResetIntercompanyOperationsSession()
    {
        ManagementFee?.ResetSession();
        RateFixings?.ResetSession();
        BalanceConfirmation?.ResetSession();
        AccrualSchedules?.ResetSession();
        AgreementWorkQueue?.ResetSession();
    }
}
