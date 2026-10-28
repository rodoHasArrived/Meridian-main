namespace Meridian.Wpf.ViewModels.Accounting;

public sealed partial class AccountingConfigureViewModel
{
    public AuditChainIntegrityViewModel AuditChainIntegrity { get; }
    public FactorPaydownViewModel FactorPaydown { get; }

    private async Task ResetGovernedQueuesAsync(CancellationToken ct = default)
    {
        // Invalidate financial commands immediately, before a potentially delayed queue read.
        FactorPaydown.SetScope(null, null);
        await RecurringJournalQueue.LoadAsync(null, null, null, ct).ConfigureAwait(false);
    }

    private async Task LoadGovernedQueuesAsync(string? entityId, CancellationToken ct)
    {
        FactorPaydown.SetScope(_activeFundProfile?.FundProfileId, _configuration?.LedgerBookId);
        await RecurringJournalQueue.LoadAsync(_activeFundProfile?.FundProfileId, _configuration?.LedgerBookId, entityId, ct).ConfigureAwait(false);
    }
}
