using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.Infrastructure.Adapters.Robinhood;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Meridian.ProviderSdk;

namespace Meridian;

/// <summary>
/// Registers the live brokerage gateways that the hosted app can route to at runtime.
/// </summary>
internal static class HostedBrokerageGatewayServiceCollectionExtensions
{
    internal static IServiceCollection AddHostedBrokerageGateways(this IServiceCollection services)
    {
        services.TryAddSingleton<AlpacaBrokerageGateway>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var options = sp.GetService<Meridian.Core.Config.AlpacaOptions>()
                ?? new Meridian.Core.Config.AlpacaOptions();
            var logger = sp.GetRequiredService<ILogger<AlpacaBrokerageGateway>>();
            return new AlpacaBrokerageGateway(httpClientFactory, options, logger);
        });
        services.AddBrokerageGateway("alpaca", sp => sp.GetRequiredService<AlpacaBrokerageGateway>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBrokerageAccountCatalog, AlpacaBrokerageSyncAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBrokeragePortfolioSync, AlpacaBrokerageSyncAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBrokerageActivitySync, AlpacaBrokerageSyncAdapter>());

        services.TryAddSingleton<IBBrokerageGateway>(sp =>
        {
            var options = sp.GetService<Meridian.Core.Config.IBOptions>()
                ?? new Meridian.Core.Config.IBOptions();
            var logger = sp.GetRequiredService<ILogger<IBBrokerageGateway>>();
            return new IBBrokerageGateway(options, logger);
        });
#if IBAPI
        // Only an official-vendor build wires a transport; non-vendor builds remain fail-closed.
        services.TryAddSingleton<EnhancedIBConnectionManager>(sp =>
        {
            var options = sp.GetService<Meridian.Core.Config.IBOptions>() ?? new Meridian.Core.Config.IBOptions();
            return new EnhancedIBConnectionManager(new IBCallbackRouter(), options.Host, options.Port, options.ClientId);
        });
        services.TryAddSingleton<IBDataServices>(sp => new IBDataServices(
            sp.GetRequiredService<EnhancedIBConnectionManager>(),
            new IBDurableResultProjector(sp.GetRequiredService<IBDurableResultStore>())));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProviderDataReadService>(sp => sp.GetRequiredService<IBDataServices>()));
#endif
        services.AddBrokerageGateway("ib", sp => sp.GetRequiredService<IBBrokerageGateway>());
        services.AddBrokerageGateway("ibkr", sp => sp.GetRequiredService<IBBrokerageGateway>());
        services.TryAddSingleton<IBBrokerageSyncAdapter>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBrokerageAccountCatalog, IBBrokerageSyncAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBrokeragePortfolioSync, IBBrokerageSyncAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBrokerageActivitySync, IBBrokerageSyncAdapter>());

        RegisterOptionalStockSharpGateway(services);

        services.TryAddSingleton<RobinhoodBrokerageGateway>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<RobinhoodBrokerageGateway>>();
            return new RobinhoodBrokerageGateway(httpClientFactory, logger);
        });
        services.AddBrokerageGateway("robinhood", sp => sp.GetRequiredService<RobinhoodBrokerageGateway>());
        services.TryAddSingleton(_ => RobinhoodReadOnlyBrokerageOptions.FromEnvironment());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBrokerageAccountCatalog, RobinhoodReadOnlyBrokerageSyncAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBrokeragePortfolioSync, RobinhoodReadOnlyBrokerageSyncAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBrokerageActivitySync, RobinhoodReadOnlyBrokerageSyncAdapter>());

        return services;
    }

    private static void RegisterOptionalStockSharpGateway(IServiceCollection services)
    {
        const string stockSharpGatewayTypeName = "Meridian.Infrastructure.Adapters.StockSharp.StockSharpBrokerageGateway, Meridian.Infrastructure";
        var gatewayType = Type.GetType(stockSharpGatewayTypeName, throwOnError: false);
        RegisterOptionalStockSharpGateway(services, gatewayType);
    }

    internal static void RegisterOptionalStockSharpGateway(IServiceCollection services, Type? gatewayType)
    {
        if (gatewayType is null || !typeof(IBrokerageGateway).IsAssignableFrom(gatewayType))
        {
            return;
        }

        services.TryAddSingleton(gatewayType);
        services.AddBrokerageGateway("stocksharp", sp => (IBrokerageGateway)sp.GetRequiredService(gatewayType));
        services.TryAddSingleton(sp => new StockSharpBrokerageGatewayAccessor(sp, gatewayType));
        services.TryAddSingleton(sp =>
            new StockSharpBrokerageSyncAdapter(sp.GetRequiredService<StockSharpBrokerageGatewayAccessor>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBrokerageAccountCatalog, StockSharpBrokerageSyncAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBrokeragePortfolioSync, StockSharpBrokerageSyncAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBrokerageActivitySync, StockSharpBrokerageSyncAdapter>());
    }

    private sealed class AlpacaBrokerageSyncAdapter(AlpacaBrokerageGateway gateway) :
        IBrokerageAccountCatalog,
        IBrokeragePortfolioSync,
        IBrokerageActivitySync
    {
        private readonly IBrokerageAccountCatalog _accountCatalog = gateway;
        private readonly IBrokeragePortfolioSync _portfolioSync = gateway;
        private readonly IBrokerageActivitySync _activitySync = gateway;

        public string ProviderId => _accountCatalog.ProviderId;

        public string ProviderDisplayName => _accountCatalog.ProviderDisplayName;

        Task<IReadOnlyList<BrokerageExternalAccountDto>> IBrokerageAccountCatalog.GetAccountsAsync(
            CancellationToken ct)
            => _accountCatalog.GetAccountsAsync(ct);

        Task<BrokeragePortfolioSnapshotDto> IBrokeragePortfolioSync.GetPortfolioSnapshotAsync(
            string externalAccountId,
            CancellationToken ct)
            => _portfolioSync.GetPortfolioSnapshotAsync(externalAccountId, ct);

        Task<BrokerageActivitySnapshotDto> IBrokerageActivitySync.GetActivitySnapshotAsync(
            string externalAccountId,
            DateTimeOffset? since,
            CancellationToken ct)
            => _activitySync.GetActivitySnapshotAsync(externalAccountId, since, ct);

        Task<BrokerageActivitySnapshotDto> IBrokerageActivitySync.GetActivitySnapshotAsync(
            string externalAccountId,
            DateTimeOffset? since,
            DateTimeOffset? untilExclusive,
            CancellationToken ct)
            => _activitySync.GetActivitySnapshotAsync(externalAccountId, since, untilExclusive, ct);
    }

    private sealed class IBBrokerageSyncAdapter(IBBrokerageGateway gateway) :
        IBrokerageAccountCatalog,
        IBrokeragePortfolioSync,
        IBrokerageActivitySync
    {
        private readonly IBrokerageAccountCatalog _accountCatalog = gateway;
        private readonly IBrokeragePortfolioSync _portfolioSync = gateway;
        private readonly IBrokerageActivitySync _activitySync = gateway;

        public string ProviderId => _accountCatalog.ProviderId;

        public string ProviderDisplayName => _accountCatalog.ProviderDisplayName;

        Task<IReadOnlyList<BrokerageExternalAccountDto>> IBrokerageAccountCatalog.GetAccountsAsync(
            CancellationToken ct)
            => _accountCatalog.GetAccountsAsync(ct);

        Task<BrokeragePortfolioSnapshotDto> IBrokeragePortfolioSync.GetPortfolioSnapshotAsync(
            string externalAccountId,
            CancellationToken ct)
            => _portfolioSync.GetPortfolioSnapshotAsync(externalAccountId, ct);

        Task<BrokerageActivitySnapshotDto> IBrokerageActivitySync.GetActivitySnapshotAsync(
            string externalAccountId,
            DateTimeOffset? since,
            CancellationToken ct)
            => _activitySync.GetActivitySnapshotAsync(externalAccountId, since, ct);
    }

    private sealed class StockSharpBrokerageGatewayAccessor(IServiceProvider services, Type gatewayType)
    {
        public IBrokerageGateway GetGateway() => (IBrokerageGateway)services.GetRequiredService(gatewayType);
    }

    private sealed class StockSharpBrokerageSyncAdapter(StockSharpBrokerageGatewayAccessor gatewayAccessor) :
        IBrokerageAccountCatalog,
        IBrokeragePortfolioSync,
        IBrokerageActivitySync
    {
        public string ProviderId => "stocksharp";
        public string ProviderDisplayName => gatewayAccessor.GetGateway().BrokerDisplayName;
        public Task<IReadOnlyList<BrokerageExternalAccountDto>> GetAccountsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<BrokerageExternalAccountDto>>([]);
        public Task<BrokeragePortfolioSnapshotDto> GetPortfolioSnapshotAsync(string externalAccountId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(string externalAccountId, DateTimeOffset? since, CancellationToken ct) => throw new NotSupportedException();
    }
}
