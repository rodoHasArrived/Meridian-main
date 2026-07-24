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
    string? DeclaredGatewayId,
    string? GatewayType,
    bool GatewayIdMatchesRuntimeKey,
    bool SupportsAccountCatalog,
    bool SupportsPortfolioSync,
    bool SupportsActivitySync,
    bool SupportsOrderModification,
    bool SupportsPartialFills,
    IReadOnlyList<string> SupportedAssetClasses,
    IReadOnlyList<string> ValidationIssues,
    IReadOnlyList<string> Notes);

internal static class HostedBrokerageGatewayRuntimeSurfaceCatalog
{
    private static readonly string[] ExpectedGatewayIds = ["alpaca", "ibkr", "ib", "robinhood", "stocksharp"];

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
                DeclaredGatewayId: null,
                GatewayType: null,
                GatewayIdMatchesRuntimeKey: false,
                SupportsAccountCatalog: false,
                SupportsPortfolioSync: false,
                SupportsActivitySync: false,
                SupportsOrderModification: false,
                SupportsPartialFills: false,
                SupportedAssetClasses: [],
                ValidationIssues: [gatewayId.Equals("stocksharp", StringComparison.OrdinalIgnoreCase)
                    ? "stocksharp-runtime-type-missing"
                    : "gateway-key-not-registered"],
                Notes: [gatewayId.Equals("stocksharp", StringComparison.OrdinalIgnoreCase)
                    ? "StockSharp gateway runtime type is not present in this build."
                    : "Gateway key is not registered in the hosted service provider."]);
        }

        var canonicalProviderId = CanonicalSyncProviderId(gatewayId);
        var gatewayIdMatches = GatewayIdMatches(gateway.GatewayId, gatewayId);
        var supportsAccountCatalog = accountCatalogs.Any(catalog => IsProvider(catalog.ProviderId, canonicalProviderId));
        var supportsPortfolioSync = portfolioSyncs.Any(sync => IsProvider(sync.ProviderId, canonicalProviderId));
        var supportsActivitySync = activitySyncs.Any(sync => IsProvider(sync.ProviderId, canonicalProviderId));
        var issues = BuildValidationIssues(
            gatewayId,
            gateway.GatewayId,
            gatewayIdMatches,
            supportsAccountCatalog,
            supportsPortfolioSync,
            supportsActivitySync,
            gateway.BrokerageCapabilities);
        var notes = new List<string>();
        if (gatewayId.Equals("ibkr", StringComparison.OrdinalIgnoreCase))
        {
            notes.Add("Interactive Brokers canonical runtime key; ib remains registered as a compatibility alias.");
        }
        if (gatewayId.Equals("ib", StringComparison.OrdinalIgnoreCase))
        {
            notes.Add("Interactive Brokers compatibility alias; ibkr is the canonical runtime and declared gateway id.");
        }
        if (gatewayId.Equals("stocksharp", StringComparison.OrdinalIgnoreCase))
        {
            notes.Add("Optional StockSharp gateway is available only when the runtime adapter type is loadable.");
        }
        if (gatewayId.Equals("robinhood", StringComparison.OrdinalIgnoreCase))
        {
            notes.Add("Robinhood is an unofficial broker API surface and still requires bounded broker-session evidence for readiness claims.");
        }

        return new HostedBrokerageGatewayRuntimeSurface(
            GatewayId: gatewayId,
            DisplayName: gateway.BrokerDisplayName,
            IsRegistered: true,
            DeclaredGatewayId: gateway.GatewayId,
            GatewayType: gateway.GetType().FullName,
            GatewayIdMatchesRuntimeKey: gatewayIdMatches,
            SupportsAccountCatalog: supportsAccountCatalog,
            SupportsPortfolioSync: supportsPortfolioSync,
            SupportsActivitySync: supportsActivitySync,
            SupportsOrderModification: gateway.BrokerageCapabilities.SupportsOrderModification,
            SupportsPartialFills: gateway.BrokerageCapabilities.SupportsPartialFills,
            SupportedAssetClasses: gateway.BrokerageCapabilities.SupportedAssetClasses,
            ValidationIssues: issues,
            Notes: notes);
    }

    private static string CanonicalSyncProviderId(string gatewayId) =>
        gatewayId.Equals("ib", StringComparison.OrdinalIgnoreCase) ? "ibkr" : gatewayId;

    private static bool IsProvider(string actual, string expected) =>
        actual.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static bool GatewayIdMatches(string declaredGatewayId, string runtimeKey)
        => declaredGatewayId.Equals(runtimeKey, StringComparison.OrdinalIgnoreCase) ||
           (runtimeKey.Equals("ib", StringComparison.OrdinalIgnoreCase) &&
            declaredGatewayId.Equals("ibkr", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> BuildValidationIssues(
        string gatewayId,
        string declaredGatewayId,
        bool gatewayIdMatches,
        bool supportsAccountCatalog,
        bool supportsPortfolioSync,
        bool supportsActivitySync,
        BrokerageCapabilities capabilities)
    {
        var issues = new List<string>();
        if (!gatewayIdMatches)
        {
            issues.Add($"gateway-id-mismatch:{declaredGatewayId}");
        }

        if (!supportsAccountCatalog)
        {
            issues.Add("account-catalog-missing");
        }

        if (!supportsPortfolioSync)
        {
            issues.Add("portfolio-sync-missing");
        }

        if (RequiresActivitySync(gatewayId) && !supportsActivitySync)
        {
            issues.Add("activity-sync-missing");
        }

        if (capabilities.SupportedOrderTypes.Count == 0)
        {
            issues.Add("order-types-empty");
        }

        if (capabilities.SupportedTimeInForce.Count == 0)
        {
            issues.Add("time-in-force-empty");
        }

        if (capabilities.SupportedAssetClasses.Count == 0)
        {
            issues.Add("asset-classes-empty");
        }

        return issues;
    }

    private static bool RequiresActivitySync(string gatewayId)
        => !gatewayId.Equals("ib", StringComparison.OrdinalIgnoreCase) &&
           !gatewayId.Equals("ibkr", StringComparison.OrdinalIgnoreCase);

    private static string DisplayNameFor(string gatewayId) =>
        gatewayId.ToLowerInvariant() switch
        {
            "alpaca" => "Alpaca Markets",
            "ib" or "ibkr" => "Interactive Brokers",
            "robinhood" => "Robinhood (unofficial)",
            "stocksharp" => "StockSharp",
            _ => gatewayId
        };
}
