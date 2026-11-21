namespace Meridian.Storage.Ledger;

/// <summary>
/// Complete, unpaged canonical lot population for one security in a tenant-owned ledger book.
/// The reader must inspect the whole book for unidentified legacy lots and reconstruct held
/// quantities from retained journal-effective mutation history, including lots disposed later.
/// Missing ownership, identity or historical quantity facts must throw; a case/account subset
/// must never be represented as this inventory.
/// </summary>
public interface ILedgerSecurityEntitlementInventory
{
    Task<LedgerSecurityEntitlementInventorySnapshot> ReadSecurityEntitlementInventoryAsync(
        string tenantId,
        string companyId,
        Guid ledgerBookId,
        Guid securityId,
        DateOnly asOfDate,
        CancellationToken ct = default);
}

public sealed record LedgerSecurityEntitlementInventorySnapshot(
    string TenantId,
    string CompanyId,
    LedgerBookRecord Book,
    Guid SecurityId,
    DateOnly AsOfDate,
    int InspectedBookLotCount,
    IReadOnlyList<LedgerTaxLotRecord> HeldLots,
    string SnapshotJson,
    string SnapshotHash);
