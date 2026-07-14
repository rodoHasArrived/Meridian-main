namespace Meridian.Contracts.SecurityMaster;

public interface ISecurityMasterCashFlowService
{
    Task<SecurityCashFlowSourceDto?> GetCashFlowSourceAsync(Guid securityId, CancellationToken ct = default);
    Task UpsertCashFlowSourceAsync(UpsertCashFlowSourceRequest request, CancellationToken ct = default);
    Task<StructuredCashFlowProjectionDto?> GetProjectionAsync(Guid securityId, StructuredCashFlowScenario scenario, CancellationToken ct = default);

    /// <summary>
    /// Converts the base-scenario projection for <paramref name="securityId"/> into balanced,
    /// ledger-ready coupon-accrual journal postings. Enforces the staleness gate — a projection
    /// whose source is <see cref="StructuredCashFlowStaleness.Stale"/> is blocked, never posted —
    /// so accrual automation cannot run off stale terms.
    /// </summary>
    /// <param name="securityId">The security whose accruals should be projected to the ledger.</param>
    /// <param name="financialAccountId">Optional financial account scoping the ledger accounts.</param>
    /// <param name="through">
    /// Optional inclusive upper bound on coupon period dates to accrue. When null, every scheduled
    /// coupon accrual is projected; callers post only what belongs to the current accrual window.
    /// </param>
    Task<StructuredCashFlowLedgerPostingResult> BuildLedgerPostingsAsync(
        Guid securityId,
        string? financialAccountId = null,
        DateOnly? through = null,
        CancellationToken ct = default);
}
