namespace Meridian.Contracts.SecurityMaster;

public interface ISecurityMasterService
{
    Task<SecurityDetailDto> CreateAsync(CreateSecurityRequest request, CancellationToken ct = default);
    Task<SecurityDetailDto> AmendTermsAsync(AmendSecurityTermsRequest request, CancellationToken ct = default);
    Task DeactivateAsync(DeactivateSecurityRequest request, CancellationToken ct = default);
    Task<SecurityAliasDto> UpsertAliasAsync(UpsertSecurityAliasRequest request, CancellationToken ct = default);
    Task<SecurityDetailDto> AmendPreferredEquityTermsAsync(Guid securityId, AmendPreferredEquityTermsRequest request, CancellationToken ct = default);
}
