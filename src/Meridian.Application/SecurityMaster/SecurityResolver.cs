using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

#pragma warning disable CS0618 // Native resolver also supports legacy host registrations.
/// <summary>
/// Resolves security identifiers against canonical Security Master records.
/// </summary>
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
        var record = await _store.GetByIdentifierAsync(
            request.IdentifierKind,
            request.IdentifierValue,
            request.Provider,
            request.AsOfUtc ?? DateTimeOffset.UtcNow,
            includeInactive: !request.ActiveOnly,
            ct).ConfigureAwait(false);

        return record?.SecurityId;
    }

    /// <inheritdoc />
    public async Task<CanonicalSecurityResolution> ResolveCanonicalAsync(
        ResolveCanonicalSecurityRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var asOf = request.AsOfUtc ?? DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(request.IdentifierValue))
            return CanonicalSecurityResolution.FromCandidates(request, asOf, []);

        var candidates = await _store.FindSecurityIdsByIdentifierAsync(
            request.IdentifierKind,
            request.IdentifierValue,
            asOf,
            includeInactive: !request.ActiveOnly,
            ct).ConfigureAwait(false);
        return CanonicalSecurityResolution.FromCandidates(request, asOf, candidates);
    }
}
