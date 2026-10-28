using Meridian.Contracts.Deposits;

namespace Meridian.Instruments.Deposits;

/// <summary>
/// Queries deposit reference data by security identifier and supported asset-specific filters.
/// </summary>
public interface IDepositReferenceService
{
    Task<DepositReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<DepositReferenceDto>> GetByInstitutionAsync(string institutionName, CancellationToken ct = default);
    Task<IReadOnlyList<DepositReferenceDto>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default);
}
