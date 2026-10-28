namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Manages security cash flow sources and retrieves current or historical scenario projections.
/// </summary>
public interface ISecurityMasterCashFlowService
{
    Task<SecurityCashFlowSourceDto?> GetCashFlowSourceAsync(Guid securityId, CancellationToken ct = default);
    Task UpsertCashFlowSourceAsync(UpsertCashFlowSourceRequest request, CancellationToken ct = default);
    Task<StructuredCashFlowProjectionDto?> GetProjectionAsync(Guid securityId, StructuredCashFlowScenario scenario, CancellationToken ct = default);

    /// <summary>
    /// The projection as of <paramref name="asOf"/> rather than the wall clock: periods paying on or
    /// after its UTC date are projected, and floating legs read the forward curve in force on that
    /// date. <see cref="GetProjectionAsync"/> is this method at <see cref="DateTimeOffset.UtcNow"/>.
    /// </summary>
    Task<StructuredCashFlowProjectionDto?> GetProjectionAsOfAsync(Guid securityId, StructuredCashFlowScenario scenario, DateTimeOffset asOf, CancellationToken ct = default);

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
