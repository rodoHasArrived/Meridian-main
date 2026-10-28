namespace Meridian.DataIntegration.Canonicalization;

/// <summary>Resolves a canonical ticker to its Security Master identifier when available.</summary>
public interface ICanonicalSecurityIdLookup
{
    Guid? TryGetSecurityId(string canonicalTicker);
}
