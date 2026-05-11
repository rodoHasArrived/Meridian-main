using Meridian.Contracts.Deposits;

namespace Meridian.Application.Deposits;

public interface IDepositReferenceService
{
    Task<DepositReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<DepositReferenceDto>> GetByInstitutionAsync(string institutionName, CancellationToken ct = default);
    Task<IReadOnlyList<DepositReferenceDto>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default);
}
