namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Resolves an identifier to a stable Security Master identity without exposing its implementation.
/// </summary>
public interface ISecurityResolver
{
    Task<Guid?> ResolveAsync(ResolveSecurityRequest request, CancellationToken ct = default);

    /// <summary>
    /// Resolves an identifier across all providers to exactly one Security Master identity, returning
    /// a typed <see cref="CanonicalSecurityResolutionStatus.Ambiguous"/> or
    /// <see cref="CanonicalSecurityResolutionStatus.NotFound"/> result instead of picking a candidate.
    /// </summary>
    /// <remarks>
    /// The default implementation exists for resolvers that predate this member: it delegates to
    /// <see cref="ResolveAsync"/> with no provider and therefore cannot detect ambiguity. The
    /// Security Master resolver overrides it with a candidate query.
    /// </remarks>
    async Task<CanonicalSecurityResolution> ResolveCanonicalAsync(
        ResolveCanonicalSecurityRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var asOf = request.AsOfUtc ?? DateTimeOffset.UtcNow;
        var resolved = await ResolveAsync(
            new ResolveSecurityRequest(request.IdentifierKind, request.IdentifierValue, Provider: null, asOf, request.ActiveOnly),
            ct).ConfigureAwait(false);
        return CanonicalSecurityResolution.FromCandidates(request, asOf, resolved is { } id ? [id] : []);
    }
}
