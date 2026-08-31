namespace Meridian.Application.Composition;

/// <summary>
/// Process-local evidence that the PostgreSQL source schemas required by client reporting
/// completed their startup migrations successfully.
/// </summary>
public sealed class DatabaseMigrationReadinessReceipt
{
    private int _ledgerReady;
    private int _fundAccountsReady;
    private int _fundStructureReady;

    public bool LedgerReady => Volatile.Read(ref _ledgerReady) == 1;

    public bool FundAccountsReady => Volatile.Read(ref _fundAccountsReady) == 1;

    public bool FundStructureReady => Volatile.Read(ref _fundStructureReady) == 1;

    internal void MarkLedgerReady() => Volatile.Write(ref _ledgerReady, 1);

    internal void MarkFundAccountsReady() => Volatile.Write(ref _fundAccountsReady, 1);

    internal void MarkFundStructureReady() => Volatile.Write(ref _fundStructureReady, 1);
}
