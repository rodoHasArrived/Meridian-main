namespace Meridian.DataIntegration.Canonicalization;

public interface ICanonicalSecurityIdLookup
{
    Guid? TryGetSecurityId(string canonicalTicker);
}
