namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Resolves an identifier to a stable Security Master identity without exposing its implementation.
/// </summary>
public interface ISecurityResolver
{
    /// <summary>
    /// Resolves an identifier to one security id, or null. When <see cref="ResolveSecurityRequest.Provider"/>
    /// is null, implementations resolve canonically: provider is identity only for provider-scoped kinds
    /// (<see cref="SecurityIdentifierNormalizer.IsProviderScoped"/>), and an identifier claimed by more
    /// than one security resolves to null rather than to an arbitrary one of them.
    /// </summary>
    Task<Guid?> ResolveAsync(ResolveSecurityRequest request, CancellationToken ct = default);

    /// <summary>
    /// Resolves an identifier and reports why it did or did not resolve. Callers that must refuse
    /// with a reason (order admission, backtest corporate-action adjustment) use this instead of
    /// <see cref="ResolveAsync"/>. The default implementation wraps <see cref="ResolveAsync"/> and can
    /// therefore only report <see cref="SecurityResolutionStatus.Resolved"/> or
    /// <see cref="SecurityResolutionStatus.NotFound"/>.
    /// </summary>
    async Task<SecurityCanonicalResolution> ResolveCanonicalAsync(ResolveSecurityRequest request, CancellationToken ct = default)
    {
        var securityId = await ResolveAsync(request, ct).ConfigureAwait(false);
        return securityId is { } id
            ? SecurityCanonicalResolution.Resolve(id)
            : SecurityCanonicalResolution.NotFound;
    }
}

/// <summary>How a canonical identifier lookup ended.</summary>
public enum SecurityResolutionStatus
{
    /// <summary>Exactly one eligible security claims the identifier.</summary>
    Resolved,

    /// <summary>More than one eligible security claims the identifier; none is picked.</summary>
    Ambiguous,

    /// <summary>No security claims the identifier at the requested as-of.</summary>
    NotFound,

    /// <summary>Only inactive securities claim the identifier, and the caller asked for active ones.</summary>
    Inactive
}

/// <summary>
/// The typed outcome of a canonical identifier lookup. <see cref="SecurityId"/> is set only when
/// <see cref="Status"/> is <see cref="SecurityResolutionStatus.Resolved"/>, or when a single inactive
/// security is the only claimant. <see cref="CandidateSecurityIds"/> lists every claimant considered.
/// </summary>
public sealed record SecurityCanonicalResolution(
    SecurityResolutionStatus Status,
    Guid? SecurityId,
    IReadOnlyList<Guid> CandidateSecurityIds)
{
    public static SecurityCanonicalResolution NotFound { get; } = new(SecurityResolutionStatus.NotFound, null, []);

    public static SecurityCanonicalResolution Resolve(Guid securityId) => new(SecurityResolutionStatus.Resolved, securityId, [securityId]);

    /// <summary>
    /// Classifies claimants. With <paramref name="activeOnly"/>, a single active claimant resolves even
    /// when inactive claimants also exist (a recycled ticker whose former holder is retired).
    /// </summary>
    public static SecurityCanonicalResolution FromClaims(IReadOnlyList<SecurityIdentityClaim> claims, bool activeOnly)
    {
        ArgumentNullException.ThrowIfNull(claims);
        var distinct = claims
            .GroupBy(static claim => claim.SecurityId)
            .Select(static group => new SecurityIdentityClaim(group.Key, group.Any(static claim => claim.IsActive)))
            .OrderBy(static claim => claim.SecurityId)
            .ToArray();
        if (distinct.Length == 0)
        {
            return NotFound;
        }

        var allIds = distinct.Select(static claim => claim.SecurityId).ToArray();
        var eligible = activeOnly ? distinct.Where(static claim => claim.IsActive).ToArray() : distinct;
        if (eligible.Length == 1)
        {
            return new(SecurityResolutionStatus.Resolved, eligible[0].SecurityId, allIds);
        }

        if (eligible.Length > 1)
        {
            return new(SecurityResolutionStatus.Ambiguous, null, eligible.Select(static claim => claim.SecurityId).ToArray());
        }

        return new(SecurityResolutionStatus.Inactive, allIds.Length == 1 ? allIds[0] : null, allIds);
    }
}

/// <summary>One security's claim on an identifier, with whether that security is active.</summary>
public sealed record SecurityIdentityClaim(Guid SecurityId, bool IsActive);
