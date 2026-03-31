using System.Threading;
using Meridian.Application.Config;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;

namespace Meridian.Infrastructure;

/// <summary>
/// Runtime no-op implementation used when no data provider is configured or available.
/// Keeps Program logic identical in all builds.
/// </summary>
[ImplementsAdr("ADR-001", "No-op data provider for disabled/unconfigured scenarios")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
public sealed class NoOpMarketDataClient : IMarketDataClient
{
    public bool IsEnabled => false;


    /// <inheritdoc/>
    public string ProviderId => "noop";

    /// <inheritdoc/>
    public string ProviderDisplayName => "No-Op Provider";

    /// <inheritdoc/>
    public string ProviderDescription => "Placeholder provider when no data source is configured";

    /// <inheritdoc/>
    public int ProviderPriority => int.MaxValue;

    /// <inheritdoc/>
    public ProviderCapabilities ProviderCapabilities { get; } = ProviderCapabilities.None;

    /// <inheritdoc/>
    public string[] ProviderNotes => new[]
    {
        "This provider is used when no data source is configured.",
        "Configure a real data provider in appsettings.json."
    };


    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public int SubscribeMarketDepth(SymbolConfig cfg) => -1;
    public void UnsubscribeMarketDepth(int subscriptionId) { }

    public int SubscribeTrades(SymbolConfig cfg) => -1;
    public void UnsubscribeTrades(int subscriptionId) { }
}
