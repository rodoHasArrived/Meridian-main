using Meridian.Contracts.FixedIncome;

namespace Meridian.Instruments.FixedIncome;

/// <summary>
/// Queries bond reference data by security identifier and supported asset-specific filters.
/// </summary>
public interface IBondReferenceService
{
    Task<BondReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default);
    Task<BondLifecycleDto?> GetLifecycleAsync(Guid securityId, CancellationToken ct = default);
    Task<BondAccrualConventionDto?> GetAccrualConventionAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<BondReferenceDto>> GetIssuerLadderAsync(string issuerName, CancellationToken ct = default);
    Task<IReadOnlyList<BondReferenceDto>> GetMaturityLadderAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<BondReferenceDto?> GetReferenceAsOfAsync(Guid securityId, DateOnly asOf, CancellationToken ct = default)
        => GetReferenceAsync(securityId, ct);

    Task<BondLifecycleDto?> GetLifecycleAsOfAsync(Guid securityId, DateOnly asOf, CancellationToken ct = default)
        => GetLifecycleAsync(securityId, ct);

    Task<BondReferencePageDto> GetIssuerLadderPageAsync(string issuerName, int pageSize = 100,
        string? cursor = null, DateOnly? asOf = null, CancellationToken ct = default)
        => throw new NotSupportedException("This bond service does not support issuer paging.");

    Task<BondReferencePageDto> GetMaturityLadderPageAsync(DateOnly from, DateOnly to, int pageSize = 100,
        string? cursor = null, DateOnly? asOf = null, CancellationToken ct = default)
        => throw new NotSupportedException("This bond service does not support maturity paging.");
}
