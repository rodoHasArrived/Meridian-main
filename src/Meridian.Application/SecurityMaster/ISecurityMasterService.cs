using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public interface ISecurityMasterService : ISecurityMasterAmender
{
    Task<SecurityDetailDto> CreateAsync(CreateSecurityRequest request, CancellationToken ct = default);
    Task DeactivateAsync(DeactivateSecurityRequest request, CancellationToken ct = default);
    Task<SecurityAliasDto> UpsertAliasAsync(UpsertSecurityAliasRequest request, CancellationToken ct = default);
}
