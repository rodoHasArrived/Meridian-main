using Meridian.Execution.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian;

/// <summary>
/// Describes the brokerage gateway surface that is actually available in the hosted runtime.
/// This is metadata-only validation; it does not connect to a live broker or place orders.
/// </summary>
internal sealed record HostedBrokerageGatewayRuntimeSurface(
    string GatewayId,
    string DisplayName,
    bool IsRegistered,
    string? GatewayType,
    bool SupportsAccountCatalog,
    bool SupportsPortfolioSync,
    bool SupportsActivitySync,
    bool SupportsOrderModification,
    bool SupportsPartialFills,
    IReadOnlyList<string> SupportedAssetClasses,
    IReadOnlyList<string> Notes);

internal static class HostedBrokerageGatewayRuntimeSurfaceCatalog
{
    private static readonly string[] ExpectedGatewayIds = ["alpaca", "ib", "ibkr", "stocksharp"];

    internal static IReadOnlyList<HostedBrokerageGatewayRuntimeSurface> Build(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var accountCatalogs = services.GetServices<IBrokerageAccountCatalog>().ToArray();
        var portfolioSyncs = services.GetServices<IBrokeragePortfolioSync>().ToArray();
        var activitySyncs = services.GetServices<IBrokerageActivitySync>().ToArray();

        return ExpectedGatewayIds
            .Select(gatewayId => BuildSurface(services, gatewayId, accountCatalogs, portfolioSyncs, activitySyncs))
            .ToArray();
    }

    private static HostedBrokerageGatewayRuntimeSurface BuildSurface(
        IServiceProvider services,
        string gatewayId,
        IReadOnlyCollection<IBrokerageAccountCatalog> accountCatalogs,
        IReadOnlyCollection<IBrokeragePortfolioSync> portfolioSyncs,
        IReadOnlyCollection<IBrokerageActivitySync> activitySyncs)
    {
        var gateway = services.GetKeyedService<IBrokerageGateway>(gatewayId);
        if (gateway is null)
        {
            return new HostedBrokerageGatewayRuntimeSurface(
                GatewayId: gatewayId,
                DisplayName: DisplayNameFor(gatewayId),
                IsRegistered: false,
                GatewayType: null,
                SupportsAccountCatalog: false,
                SupportsPortfolioSync: false,
                SupportsActivitySync: false,
                SupportsOrderModification: false,
                SupportsPartialFills: false,
                SupportedAssetClasses: [],
                Notes: [gatewayId.Equals("stocksharp", StringComparison.OrdinalIgnoreCase)
                    ? "StockSharp gateway runtime type is not present in this build."
                    : "Gateway key is not registered in the hosted service provider."]);
        }

        var canonicalProviderId = CanonicalSyncProviderId(gatewayId);
        var notes = new List<string>();
        if (gatewayId.Equals("ib", StringComparison.OrdinalIgnoreCase))
        {
            notes.Add("Interactive Brokers primary runtime key; ibkr is registered as an alias to the same gateway instance.");
        }
        if (gatewayId.Equals("stocksharp", StringComparison.OrdinalIgnoreCase))
        {
            notes.Add("Optional StockSharp gateway is available only when the runtime adapter type is loadable.");
        }

        return new HostedBrokerageGatewayRuntimeSurface(
            GatewayId: gatewayId,
            DisplayName: gateway.BrokerDisplayName,
            IsRegistered: true,
            GatewayType: gateway.GetType().FullName,
            SupportsAccountCatalog: accountCatalogs.Any(catalog => IsProvider(catalog.ProviderId, canonicalProviderId)),
            SupportsPortfolioSync: portfolioSyncs.Any(sync => IsProvider(sync.ProviderId, canonicalProviderId)),
            SupportsActivitySync: activitySyncs.Any(sync => IsProvider(sync.ProviderId, canonicalProviderId)),
            SupportsOrderModification: gateway.BrokerageCapabilities.SupportsOrderModification,
            SupportsPartialFills: gateway.BrokerageCapabilities.SupportsPartialFills,
            SupportedAssetClasses: gateway.BrokerageCapabilities.SupportedAssetClasses,
            Notes: notes);
    }

    private static string CanonicalSyncProviderId(string gatewayId) =>
        gatewayId.Equals("ib", StringComparison.OrdinalIgnoreCase) ? "ibkr" : gatewayId;

    private static bool IsProvider(string actual, string expected) =>
        actual.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static string DisplayNameFor(string gatewayId) =>
        gatewayId.ToLowerInvariant() switch
        {
            "alpaca" => "Alpaca Markets",
            "ib" or "ibkr" => "Interactive Brokers",
            "stocksharp" => "StockSharp",
            _ => gatewayId
        };
}
