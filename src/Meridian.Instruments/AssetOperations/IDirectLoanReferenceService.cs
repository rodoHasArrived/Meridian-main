using Meridian.Contracts.AssetOperations;

namespace Meridian.Instruments.AssetOperations;

/// <summary>
/// Reference reads over the DirectLoan relational terms projection — the queryable counterpart of
/// the asset-specific-terms document, which stays the source of truth.
/// </summary>
public interface IDirectLoanReferenceService
{
    Task<DirectLoanReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default);

    Task<IReadOnlyList<DirectLoanReferenceDto>> GetByBorrowerAsync(string borrower, CancellationToken ct = default);

    Task<IReadOnlyList<DirectLoanReferenceDto>> GetByReferenceIndexAsync(string referenceIndex, CancellationToken ct = default);

    Task<IReadOnlyList<DirectLoanReferenceDto>> GetMaturityLadderAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<IReadOnlyList<DirectLoanCovenantDto>> GetCovenantsAsync(Guid securityId, CancellationToken ct = default);

    Task<IReadOnlyList<DirectLoanPrincipalPaymentDto>> GetPrincipalScheduleAsync(Guid securityId, CancellationToken ct = default);

    /// <summary>
    /// Principal instalments falling due in <paramref name="from"/>..<paramref name="to"/> inclusive
    /// across every projected loan, ordered by payment date.
    /// </summary>
    Task<IReadOnlyList<DirectLoanPrincipalPaymentDto>> GetPrincipalPaymentsDueAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
