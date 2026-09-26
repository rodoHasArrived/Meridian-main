namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Read access to the StructuredCredit relational terms projection. The asset-specific-terms JSONB
/// blob stays the source of truth; this store answers the pool- and date-shaped questions the blob
/// cannot — which tranches sit on a pool or collateral type, and what pool factor was effective on a
/// date — without parsing every security's document.
/// </summary>
public interface IStructuredCreditReferenceProjectionStore
{
    Task<StructuredCreditProjectionRow?> GetStructuredCreditAsync(Guid securityId, CancellationToken ct = default);

    Task<IReadOnlyList<StructuredCreditProjectionRow>> GetByPoolAsync(string poolId, CancellationToken ct = default);

    Task<IReadOnlyList<StructuredCreditProjectionRow>> GetByCollateralTypeAsync(string collateralType, CancellationToken ct = default);

    /// <summary>The tranche's dated factor points in the order the terms document declares them.</summary>
    Task<IReadOnlyList<StructuredCreditFactorScheduleRow>> GetFactorScheduleAsync(Guid securityId, CancellationToken ct = default);

    /// <summary>
    /// The latest factor point effective on or before <paramref name="asOfDate"/>, or null when the
    /// schedule starts after that date. The relational counterpart of the resolver's in-memory
    /// FactorAsOf walk, resolved in one indexed read.
    /// </summary>
    Task<StructuredCreditFactorScheduleRow?> GetFactorAsOfAsync(Guid securityId, DateOnly asOfDate, CancellationToken ct = default);
}

/// <summary>
/// One projected structured-credit tranche. <c>FactorScheduleReference</c> is the free-text
/// trustee-report pointer, never factor data — dated factors live in the factor schedule.
/// </summary>
public sealed record StructuredCreditProjectionRow(
    Guid SecurityId,
    string DisplayName,
    string Currency,
    string Tranche,
    string? PoolId,
    string CollateralType,
    decimal OriginalFace,
    decimal? CurrentFactor,
    string CouponOrIndex,
    string? FactorScheduleReference,
    DateOnly? MaturityDate,
    string PrimaryIdentifierValue,
    long Version);

public sealed record StructuredCreditFactorScheduleRow(
    Guid SecurityId,
    int Ordinal,
    DateOnly AsOfDate,
    decimal Factor);
