using Meridian.Contracts.Configuration;
using Meridian.Infrastructure.CppTrader.Options;
using Meridian.Infrastructure.DataSources;

namespace Meridian.Infrastructure.CppTrader.Providers;

/// <summary>
/// Placeholder market-data provider registration for future CppTrader order-log ingestion.
/// </summary>
[DataSource("cpptrader", "CppTrader", DataSourceType.Realtime, DataSourceCategory.Premium,
    Priority = 50, Description = "External CppTrader-native market data and execution host")]
public sealed class CppTraderMarketDataClient(IOptionsMonitor<CppTraderOptions> optionsMonitor) : IMarketDataClient
{
    private readonly IOptionsMonitor<CppTraderOptions> _optionsMonitor = optionsMonitor;
    private int _nextSubscriptionId = 1_000_000;

    public bool IsEnabled => _optionsMonitor.CurrentValue.Enabled && _optionsMonitor.CurrentValue.Features.ItchIngestionEnabled;

    public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public int SubscribeMarketDepth(SymbolConfig cfg) => Interlocked.Increment(ref _nextSubscriptionId);

    public void UnsubscribeMarketDepth(int subscriptionId)
    {
    }

    public int SubscribeTrades(SymbolConfig cfg) => Interlocked.Increment(ref _nextSubscriptionId);

    public void UnsubscribeTrades(int subscriptionId)
    {
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
