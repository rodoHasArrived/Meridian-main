using System.Collections.ObjectModel;

namespace Meridian.Ui.Shared.Services;

internal static class ProviderNavigationRouteMapper
{
    private const string FallbackRoute = "/settings#provider-connection-center";

    private static readonly IReadOnlyDictionary<string, string> CanonicalProviderByNormalizedId =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["alpaca"] = "alpaca",
            ["ib"] = "ibkr",
            ["ibkr"] = "ibkr",
            ["interactivebrokers"] = "ibkr",
            ["interactive-brokers"] = "ibkr",
            ["stocksharp"] = "stocksharp",
            ["ssharp"] = "stocksharp",
            ["robinhood"] = "robinhood"
        });

    private static readonly IReadOnlyDictionary<string, string> RouteByCanonicalProvider =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["alpaca"] = "/settings#alpaca-provider-setup",
            ["ibkr"] = "/settings#ibkr-provider-setup",
            ["stocksharp"] = "/settings#stocksharp-provider-setup",
            ["robinhood"] = "/settings#robinhood-provider-setup"
        });

    public static string ResolveProviderConnectionSettingsRoute(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return FallbackRoute;
        }

        var normalizedProviderId = providerId.Trim().ToLowerInvariant();
        if (!CanonicalProviderByNormalizedId.TryGetValue(normalizedProviderId, out var canonicalProvider))
        {
            return FallbackRoute;
        }

        return RouteByCanonicalProvider.TryGetValue(canonicalProvider, out var route)
            ? route
            : FallbackRoute;
    }
}
