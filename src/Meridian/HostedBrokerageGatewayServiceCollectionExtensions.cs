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
    }

    private sealed class IbBrokerageSyncAdapter(IBBrokerageGateway gateway) :
        IBrokerageAccountCatalog,
        IBrokeragePortfolioSync
    {
        public string ProviderId => "ibkr";

        public string ProviderDisplayName => "Interactive Brokers";

        public async Task<IReadOnlyList<BrokerageExternalAccountDto>> GetAccountsAsync(CancellationToken ct)
        {
            var account = await gateway.GetAccountInfoAsync(ct).ConfigureAwait(false);
            return [new BrokerageExternalAccountDto(
                ProviderId: ProviderId,
                AccountId: account.AccountId,
                DisplayName: string.IsNullOrWhiteSpace(account.AccountId) ? ProviderDisplayName : $"{ProviderDisplayName} {account.AccountId}",
                Status: account.Status,
                Currency: account.Currency,
                RetrievedAt: account.RetrievedAt)];
        }

        public async Task<BrokeragePortfolioSnapshotDto> GetPortfolioSnapshotAsync(string externalAccountId, CancellationToken ct)
        {
            var account = await gateway.GetAccountInfoAsync(ct).ConfigureAwait(false);
            var positions = await gateway.GetPositionsAsync(ct).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var accountDto = new BrokerageExternalAccountDto(
                ProviderId: ProviderId,
                AccountId: string.IsNullOrWhiteSpace(externalAccountId) ? account.AccountId : externalAccountId,
                DisplayName: string.IsNullOrWhiteSpace(account.AccountId) ? ProviderDisplayName : $"{ProviderDisplayName} {account.AccountId}",
                Status: account.Status,
                Currency: account.Currency,
                RetrievedAt: account.RetrievedAt);

            return new BrokeragePortfolioSnapshotDto(
                Account: accountDto,
                Balance: new BrokerageBalanceSnapshotDto(
                    Cash: account.Cash,
                    Equity: account.Equity,
                    BuyingPower: account.BuyingPower,
                    Currency: account.Currency),
                Positions: positions.Select(position => new BrokeragePositionSnapshotDto(
                    Symbol: position.Symbol,
                    Quantity: position.Quantity,
                    AverageEntryPrice: position.AverageEntryPrice,
                    MarketPrice: position.MarketPrice,
                    MarketValue: position.MarketValue,
                    UnrealizedPnl: position.UnrealizedPnl,
                    AssetClass: position.AssetClass,
                    Description: position.Description,
                    PositionId: position.PositionId,
                    Currency: account.Currency,
                    Metadata: position.Metadata)).ToArray(),
                RetrievedAt: now);
        }
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
