using Meridian.Contracts.FixedIncome;

namespace Meridian.Application.FixedIncome;

public interface IBondReferenceService
{
    Task<BondReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default);
    Task<BondLifecycleDto?> GetLifecycleAsync(Guid securityId, CancellationToken ct = default);
    Task<BondAccrualConventionDto?> GetAccrualConventionAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<BondReferenceDto>> GetIssuerLadderAsync(string issuerName, CancellationToken ct = default);
    Task<IReadOnlyList<BondReferenceDto>> GetMaturityLadderAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
