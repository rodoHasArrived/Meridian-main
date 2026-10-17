namespace Meridian.Contracts.SecurityMaster;

public interface ISecurityMasterService
{
    Task<SecurityDetailDto> CreateAsync(CreateSecurityRequest request, CancellationToken ct = default);
    Task<SecurityDetailDto> AmendTermsAsync(AmendSecurityTermsRequest request, CancellationToken ct = default);
    Task<SecurityDetailDto> AmendPreferredEquityTermsAsync(Guid securityId, AmendPreferredEquityTermsRequest request, CancellationToken ct = default);
    Task<SecurityDetailDto> AmendConvertibleEquityTermsAsync(Guid securityId, AmendConvertibleEquityTermsRequest request, CancellationToken ct = default);

    /// <summary>
    /// Replaces one nested term block declared by the security's asset class (see
    /// <see cref="SecurityTermBlocks"/>), carrying every other term unchanged. Implementations that
    /// predate the generic block route refuse it rather than fail to compile.
    /// </summary>
    Task<SecurityDetailDto> AmendTermBlockAsync(Guid securityId, string blockKey, AmendSecurityTermBlockRequest request, CancellationToken ct = default)
        => Task.FromException<SecurityDetailDto>(new NotSupportedException(
            $"{GetType().Name} does not support term-block amendments."));
    Task DeactivateAsync(DeactivateSecurityRequest request, CancellationToken ct = default);
    Task<SecurityAliasDto> UpsertAliasAsync(UpsertSecurityAliasRequest request, CancellationToken ct = default);
}
