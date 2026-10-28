namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Reads security identity and status without requiring the full Security Master query surface.
/// </summary>
public interface ISecurityIdentityQueryService
{
    /// <summary>
    /// Resolves identifiers and aliases using current knowledge of their valid-time windows.
    /// An explicit <paramref name="asOfUtc"/> selects terms by stored event-timestamp eligibility, but
    /// identity resolution can use later corrections and the legacy stable-identity fallback.
    /// This compatibility API does not establish what identifier the system knew at that time.
    /// </summary>
    Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind identifierKind, string identifierValue, string? provider, CancellationToken ct = default, DateTimeOffset? asOfUtc = null);
}
