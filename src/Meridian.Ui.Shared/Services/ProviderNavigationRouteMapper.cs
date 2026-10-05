using System.Collections.ObjectModel;
using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Ui.Shared.Services;

internal static class ProviderNavigationRouteMapper
{
    private const string FallbackRoute = "/settings#provider-connection-center";

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

        var canonicalProvider = ProviderIdentity.NormalizeId(providerId);
        return RouteByCanonicalProvider.TryGetValue(canonicalProvider, out var route)
            ? route
            : FallbackRoute;
    }
}
