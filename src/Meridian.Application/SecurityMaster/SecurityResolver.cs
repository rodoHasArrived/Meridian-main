using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

#pragma warning disable CS0618 // Native resolver also supports legacy host registrations.
/// <summary>
/// Resolves security identifiers against canonical Security Master records.
/// </summary>
/// <remarks>
/// A request with a <see cref="ResolveSecurityRequest.Provider"/> keeps the exact-provider contract of
/// <see cref="ISecurityMasterStore.GetByIdentifierAsync"/>. A provider-less request resolves
/// canonically: provider is identity only for provider-scoped kinds, so a ticker mastered by the
/// Polygon or EDGAR ingest (which records its feed as provider) still resolves, and an identifier
/// claimed by several securities resolves to none of them instead of an arbitrary one.
/// </remarks>
public sealed class SecurityResolver : ISecurityResolver
#pragma warning restore CS0618
{
    private readonly ISecurityMasterStore _store;
    private readonly ISecurityMasterRedirectStore? _redirects;

    public SecurityResolver(ISecurityMasterStore store, ISecurityMasterRedirectStore? redirects = null)
    {
        _store = store;
        _redirects = redirects;
    }

    public async Task<Guid?> ResolveAsync(ResolveSecurityRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (UsesCanonicalPath(request))
        {
            var resolution = await ResolveCanonicalAsync(request, ct).ConfigureAwait(false);
            return resolution.Status == SecurityResolutionStatus.Resolved ? resolution.SecurityId : null;
        }

        var record = await _store.GetByIdentifierAsync(
            request.IdentifierKind,
            request.IdentifierValue,
            request.Provider,
            request.AsOfUtc ?? DateTimeOffset.UtcNow,
            includeInactive: !request.ActiveOnly,
            ct).ConfigureAwait(false);

        return record?.SecurityId;
    }

    public async Task<SecurityCanonicalResolution> ResolveCanonicalAsync(ResolveSecurityRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.IdentifierValue))
        {
            return SecurityCanonicalResolution.NotFound;
        }

        var claims = await _store.FindIdentityClaimsAsync(
                request.IdentifierKind,
                request.IdentifierValue,
                request.Provider,
                request.AsOfUtc ?? DateTimeOffset.UtcNow,
                ignoreValidityWindow: false,
                ct)
            .ConfigureAwait(false);
        var resolution = SecurityCanonicalResolution.FromClaims(claims, request.ActiveOnly);
        return await FollowMergeRedirectAsync(resolution, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Review C1: an identifier that lands only on a security merged into a survivor resolves to the
    /// active survivor. Lots and history stay keyed to the retired id; only resolution is redirected.
    /// </summary>
    private async Task<SecurityCanonicalResolution> FollowMergeRedirectAsync(SecurityCanonicalResolution resolution, CancellationToken ct)
    {
        if (_redirects is null
            || resolution.Status != SecurityResolutionStatus.Inactive
            || resolution.SecurityId is not { } retired)
        {
            return resolution;
        }

        var survivor = await _redirects.ResolveSurvivorAsync(retired, ct).ConfigureAwait(false);
        if (survivor == retired)
        {
            return resolution;
        }

        var survivorProjection = await _store.GetProjectionAsync(survivor, ct).ConfigureAwait(false);
        return survivorProjection?.Status == SecurityStatusDto.Active
            ? new SecurityCanonicalResolution(SecurityResolutionStatus.Resolved, survivor, [retired, survivor])
            : resolution;
    }

    private static bool UsesCanonicalPath(ResolveSecurityRequest request)
        => string.IsNullOrWhiteSpace(request.Provider);
}
