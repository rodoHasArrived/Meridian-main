namespace Meridian.Contracts.SecurityMaster;

/// <summary>Provider-less request to resolve one identifier to exactly one Security Master identity.</summary>
/// <param name="IdentifierKind">Identifier kind, usually <see cref="SecurityIdentifierKind.Ticker"/>.</param>
/// <param name="IdentifierValue">Identifier value; normalized by the resolver.</param>
/// <param name="AsOfUtc">Validity instant for identifier history; <see langword="null"/> means now.</param>
/// <param name="ActiveOnly">When <see langword="true"/>, inactive securities are not candidates.</param>
public sealed record ResolveCanonicalSecurityRequest(
    SecurityIdentifierKind IdentifierKind,
    string IdentifierValue,
    DateTimeOffset? AsOfUtc = null,
    bool ActiveOnly = false);

/// <summary>Outcome of a canonical (provider-less) identifier resolution.</summary>
public enum CanonicalSecurityResolutionStatus
{
    /// <summary>Exactly one security claims the identifier at the requested instant.</summary>
    Resolved,

    /// <summary>More than one security claims the identifier; the caller must not pick one.</summary>
    Ambiguous,

    /// <summary>No security claims the identifier at the requested instant.</summary>
    NotFound
}

/// <summary>Typed result of <see cref="ISecurityResolver.ResolveCanonicalAsync"/>.</summary>
public sealed record CanonicalSecurityResolution(
    CanonicalSecurityResolutionStatus Status,
    Guid? SecurityId,
    IReadOnlyList<Guid> Candidates,
    SecurityIdentifierKind IdentifierKind,
    string IdentifierValue,
    DateTimeOffset AsOfUtc)
{
    /// <summary><see langword="true"/> when exactly one security was resolved.</summary>
    public bool IsResolved => Status == CanonicalSecurityResolutionStatus.Resolved && SecurityId.HasValue;

    /// <summary>Builds a result from the distinct candidates that claim the identifier.</summary>
    public static CanonicalSecurityResolution FromCandidates(
        ResolveCanonicalSecurityRequest request,
        DateTimeOffset asOfUtc,
        IEnumerable<Guid> candidates)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(candidates);
        var distinct = candidates.Where(static id => id != Guid.Empty).Distinct().OrderBy(static id => id).ToArray();
        var status = distinct.Length switch
        {
            0 => CanonicalSecurityResolutionStatus.NotFound,
            1 => CanonicalSecurityResolutionStatus.Resolved,
            _ => CanonicalSecurityResolutionStatus.Ambiguous
        };
        return new(
            status,
            status == CanonicalSecurityResolutionStatus.Resolved ? distinct[0] : null,
            distinct,
            request.IdentifierKind,
            request.IdentifierValue,
            asOfUtc);
    }
}

/// <summary>Thrown when a workflow requires one canonical security but resolution was not unique.</summary>
public sealed class CanonicalSecurityResolutionException : InvalidOperationException
{
    public CanonicalSecurityResolutionException(CanonicalSecurityResolution resolution)
        : base(BuildMessage(resolution))
    {
        Resolution = resolution;
    }

    /// <summary>The typed resolution that blocked the workflow.</summary>
    public CanonicalSecurityResolution Resolution { get; }

    private static string BuildMessage(CanonicalSecurityResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        return resolution.Status switch
        {
            CanonicalSecurityResolutionStatus.Ambiguous =>
                $"{resolution.IdentifierKind} '{resolution.IdentifierValue}' is claimed by {resolution.Candidates.Count} securities as of {resolution.AsOfUtc:O}; refusing to pick one.",
            CanonicalSecurityResolutionStatus.NotFound =>
                $"{resolution.IdentifierKind} '{resolution.IdentifierValue}' is not registered in the Security Master as of {resolution.AsOfUtc:O}.",
            _ => $"{resolution.IdentifierKind} '{resolution.IdentifierValue}' resolved to {resolution.SecurityId:D}."
        };
    }
}
