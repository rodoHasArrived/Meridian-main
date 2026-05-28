namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Shared provider identifier normalization for adapter registries and runtime routing.
/// </summary>
internal static class ProviderIdentity
{
    public static string NormalizeId(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return providerId.Trim().ToLowerInvariant();
    }

    public static bool EqualsId(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) &&
           !string.IsNullOrWhiteSpace(right) &&
           string.Equals(NormalizeId(left), NormalizeId(right), StringComparison.Ordinal);
}
