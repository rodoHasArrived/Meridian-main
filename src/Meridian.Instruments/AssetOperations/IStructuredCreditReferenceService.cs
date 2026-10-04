using Meridian.Contracts.AssetOperations;

namespace Meridian.Instruments.AssetOperations;

/// <summary>
/// Reference reads over the StructuredCredit relational terms projection — the queryable
/// counterpart of the asset-specific-terms document, which stays the source of truth.
/// </summary>
public interface IStructuredCreditReferenceService
{
    Task<StructuredCreditReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default);

    Task<IReadOnlyList<StructuredCreditReferenceDto>> GetByPoolAsync(string poolId, CancellationToken ct = default);

    Task<IReadOnlyList<StructuredCreditReferenceDto>> GetByCollateralTypeAsync(string collateralType, CancellationToken ct = default);

    Task<IReadOnlyList<StructuredCreditFactorPointDto>> GetFactorScheduleAsync(Guid securityId, CancellationToken ct = default);

    /// <summary>
    /// The factor effective on <paramref name="asOfDate"/> — the latest dated point on or before it —
    /// or null when the schedule starts later or is empty.
    /// </summary>
    Task<StructuredCreditFactorPointDto?> GetFactorAsOfAsync(Guid securityId, DateOnly asOfDate, CancellationToken ct = default);
}
