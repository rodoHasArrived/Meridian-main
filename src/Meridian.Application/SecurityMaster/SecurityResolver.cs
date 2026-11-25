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

    public SecurityResolver(ISecurityMasterStore store)
    {
        _store = store;
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
        return SecurityCanonicalResolution.FromClaims(claims, request.ActiveOnly);
    }

    private static bool UsesCanonicalPath(ResolveSecurityRequest request)
        => string.IsNullOrWhiteSpace(request.Provider);
}
