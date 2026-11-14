using Meridian.Execution.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Meridian.Execution.Services;

/// <summary>Composes read-only broker accounting checks over the shared portfolio registry.</summary>
public static class PortfolioAccountingReconciliationRegistration
{
    public static IServiceCollection AddPortfolioAccountingReconciliation(this IServiceCollection services)
    {
        services.AddOptions<PositionSyncOptions>();
        services.TryAddSingleton(sp => new PositionReconciliationService(
            sp.GetServices<IBrokeragePortfolioSync>().Select(provider => (
                provider.ProviderId,
                (IBrokeragePositionSync)new PortfolioPositionSyncAdapter(provider,
                    sp.GetServices<IBrokerageActivitySync>().FirstOrDefault(activity =>
                        string.Equals(activity.ProviderId, provider.ProviderId, StringComparison.OrdinalIgnoreCase))))),
            sp.GetRequiredService<PortfolioRegistry>(),
            sp.GetRequiredService<IOptions<PositionSyncOptions>>(),
            sp.GetRequiredService<ILogger<PositionReconciliationService>>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PortfolioAccountingReconciliationHostedService>());
        return services;
    }

    /// <summary>Adapts current brokerage read contracts to the retained reconciliation surface.</summary>
    private sealed class PortfolioPositionSyncAdapter(
        IBrokeragePortfolioSync portfolio,
        IBrokerageActivitySync? activity) : IBrokeragePositionSync, IBrokeragePortfolioSync, IBrokerageActivitySync
    {
        public string ProviderId => portfolio.ProviderId;
        public Task<BrokeragePortfolioSnapshotDto> GetPortfolioSnapshotAsync(string externalAccountId, CancellationToken ct = default)
            => portfolio.GetPortfolioSnapshotAsync(externalAccountId, ct);
        public async Task<IReadOnlyList<BrokeragePositionDto>> GetCurrentPositionsAsync(string accountId, CancellationToken ct = default)
            => (await portfolio.GetPortfolioSnapshotAsync(accountId, ct).ConfigureAwait(false)).Positions
                .Select(p => new BrokeragePositionDto(p.Symbol, p.Quantity, p.AverageEntryPrice, p.UnrealizedPnl)).ToArray();
        public async Task<BrokerageAccountSummaryDto> GetAccountSummaryAsync(string accountId, CancellationToken ct = default)
        {
            var snapshot = await portfolio.GetPortfolioSnapshotAsync(accountId, ct).ConfigureAwait(false);
            return new(snapshot.Account.AccountId, snapshot.Account.DisplayName, snapshot.Balance.Cash, snapshot.Balance.MarginBalance);
        }
        public Task<IReadOnlyList<string>> GetAllAccountsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(string externalAccountId, DateTimeOffset? since = null, CancellationToken ct = default)
            => activity?.GetActivitySnapshotAsync(externalAccountId, since, ct)
               ?? Task.FromResult(new BrokerageActivitySnapshotDto(ProviderId, externalAccountId,
                   DateTimeOffset.UtcNow, [], [], [], Cursor: new(null, null, 0, 0, false)));
    }
}

internal sealed class PortfolioAccountingReconciliationHostedService(
    PositionReconciliationService reconciliation,
    IOptions<PositionSyncOptions> options,
    ILogger<PortfolioAccountingReconciliationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.SyncInterval;
        if (interval <= TimeSpan.Zero || interval > TimeSpan.FromDays(1))
            throw new InvalidOperationException("Accounting reconciliation must run at least daily with a positive interval.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await reconciliation.ReconcileAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled broker accounting reconciliation failed");
            }
            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
