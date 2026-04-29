using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Alpaca;
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
                ?? new Meridian.Application.Config.AlpacaOptions(
                    KeyId: Environment.GetEnvironmentVariable("ALPACA_KEY_ID")
                        ?? Environment.GetEnvironmentVariable("ALPACA__KEYID") ?? string.Empty,
                    SecretKey: Environment.GetEnvironmentVariable("ALPACA_SECRET_KEY")
                        ?? Environment.GetEnvironmentVariable("ALPACA__SECRETKEY") ?? string.Empty);
            var logger = sp.GetRequiredService<ILogger<AlpacaBrokerageGateway>>();
            return new AlpacaBrokerageGateway(httpClientFactory, options, logger);
        });
        services.AddBrokerageGateway("alpaca", sp => sp.GetRequiredService<AlpacaBrokerageGateway>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBrokerageAccountCatalog, AlpacaBrokerageSyncAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBrokeragePortfolioSync, AlpacaBrokerageSyncAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBrokerageActivitySync, AlpacaBrokerageSyncAdapter>());

        services.TryAddSingleton<RobinhoodBrokerageGateway>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<RobinhoodBrokerageGateway>>();
            return new RobinhoodBrokerageGateway(httpClientFactory, logger);
        });
        services.AddBrokerageGateway("robinhood", sp => sp.GetRequiredService<RobinhoodBrokerageGateway>());

        return services;
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
}
