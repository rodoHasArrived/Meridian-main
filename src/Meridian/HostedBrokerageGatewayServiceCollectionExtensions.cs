using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.Infrastructure.Adapters.Robinhood;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

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
            var options = sp.GetService<Meridian.Application.Config.AlpacaOptions>()
                ?? new Meridian.Application.Config.AlpacaOptions();
            var logger = sp.GetRequiredService<ILogger<AlpacaBrokerageGateway>>();
            return new AlpacaBrokerageGateway(httpClientFactory, options, logger);
        });
        services.AddBrokerageGateway("alpaca", sp => sp.GetRequiredService<AlpacaBrokerageGateway>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBrokerageAccountCatalog, AlpacaBrokerageSyncAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBrokeragePortfolioSync, AlpacaBrokerageSyncAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBrokerageActivitySync, AlpacaBrokerageSyncAdapter>());

        services.TryAddSingleton<IBBrokerageGateway>(sp =>
        {
            var options = sp.GetService<Meridian.Application.Config.IBOptions>()
                ?? new Meridian.Application.Config.IBOptions();
            var logger = sp.GetRequiredService<ILogger<IBBrokerageGateway>>();
            return new IBBrokerageGateway(options, logger);
        });
        services.AddBrokerageGateway("ib", sp => sp.GetRequiredService<IBBrokerageGateway>());
        services.AddBrokerageGateway("ibkr", sp => sp.GetRequiredService<IBBrokerageGateway>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBrokerageAccountCatalog, IbBrokerageSyncAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBrokeragePortfolioSync, IbBrokerageSyncAdapter>());

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
        if (gatewayType is null || !typeof(IBrokerageGateway).IsAssignableFrom(gatewayType))
        {
            return;
        }

        services.AddBrokerageGateway("stocksharp", sp => (IBrokerageGateway)sp.GetRequiredService(gatewayType));
        services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IBrokerageAccountCatalog), sp =>
            new DelegatingBrokerageSyncAdapter((IBrokerageGateway)sp.GetRequiredService(gatewayType), "stocksharp")));
        services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IBrokeragePortfolioSync), sp =>
            new DelegatingBrokerageSyncAdapter((IBrokerageGateway)sp.GetRequiredService(gatewayType), "stocksharp")));
        services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IBrokerageActivitySync), sp =>
            new DelegatingBrokerageSyncAdapter((IBrokerageGateway)sp.GetRequiredService(gatewayType), "stocksharp")));
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
    }

    private sealed class IbBrokerageSyncAdapter(IBBrokerageGateway gateway) :
        IBrokerageAccountCatalog,
        IBrokeragePortfolioSync
    {
        public string ProviderId => "ibkr";

        public string ProviderDisplayName => "Interactive Brokers";

        public async Task<IReadOnlyList<BrokerageExternalAccountDto>> GetAccountsAsync(CancellationToken ct)
        {
            var accountId = await gateway.GetPrimaryAccountIdAsync(ct).ConfigureAwait(false);
            return [new BrokerageExternalAccountDto(ProviderId, accountId, ProviderDisplayName, IsTradable: true)];
        }

        public async Task<BrokeragePortfolioSnapshotDto> GetPortfolioSnapshotAsync(string externalAccountId, CancellationToken ct)
        {
            var positions = await gateway.GetPositionsAsync(ct).ConfigureAwait(false);
            var openOrders = await gateway.GetOpenOrdersAsync(ct).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;

            return new BrokeragePortfolioSnapshotDto(
                ProviderId,
                externalAccountId,
                now,
                null,
                positions.Select(position => new BrokeragePositionDto(
                    position.Symbol,
                    position.Quantity,
                    position.Quantity * position.AveragePrice,
                    position.MarketPrice,
                    position.MarketPrice,
                    position.Quantity * position.MarketPrice,
                    position.CostBasis,
                    position.UnrealizedPnl,
                    position.RealizedPnl,
                    "USD")).ToArray(),
                openOrders.Select(order => new BrokerageOpenOrderDto(
                    order.OrderId,
                    order.Symbol,
                    order.Side.ToString(),
                    order.Type.ToString(),
                    order.Status.ToString(),
                    order.Quantity,
                    order.FilledQuantity,
                    order.LimitPrice,
                    order.StopPrice,
                    order.CreatedAt,
                    order.UpdatedAt,
                    order.TimeInForce.ToString(),
                    order.ClientOrderId)).ToArray(),
                []);
        }
    }

    private sealed class DelegatingBrokerageSyncAdapter(IBrokerageGateway gateway, string providerId) :
        IBrokerageAccountCatalog,
        IBrokeragePortfolioSync,
        IBrokerageActivitySync
    {
        public string ProviderId { get; } = providerId;
        public string ProviderDisplayName => providerId;
        public Task<IReadOnlyList<BrokerageExternalAccountDto>> GetAccountsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<BrokerageExternalAccountDto>>([]);
        public Task<BrokeragePortfolioSnapshotDto> GetPortfolioSnapshotAsync(string externalAccountId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(string externalAccountId, DateTimeOffset? since, CancellationToken ct) => throw new NotSupportedException();
    }
}
