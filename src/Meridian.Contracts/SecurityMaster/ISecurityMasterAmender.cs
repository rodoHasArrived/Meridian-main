namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Narrow write interface for amending security master terms.
/// Used by infrastructure-layer services that need to persist enriched trading parameters
/// without taking a dependency on the application-layer <c>ISecurityMasterService</c>.
/// </summary>
public interface ISecurityMasterAmender
{
    Task<SecurityDetailDto> AmendTermsAsync(AmendSecurityTermsRequest request, CancellationToken ct = default);
}
