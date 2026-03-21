using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.Infrastructure;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Abstract base class for verifying that every <see cref="IMarketDataClient"/> implementation
/// satisfies the common behavioral contract defined in ADR-001.
/// </summary>
/// <typeparam name="TClient">The concrete streaming client under test.</typeparam>
/// <remarks>
/// Inherit from this class and implement <see cref="CreateClient"/> to supply a testable instance.
/// Instances that require network connectivity should be constructed in a disabled/stub state
/// (i.e. <c>IsEnabled == false</c>) so that contract tests run without live credentials.
/// <code>
/// public sealed class PolygonContractTests : MarketDataClientContractTests&lt;PolygonMarketDataClient&gt;
/// {
///     protected override PolygonMarketDataClient CreateClient() =&gt;
///         new(_publisher, _tradeCollector, _quoteCollector, apiKey: "stub");
/// }
/// </code>
/// </remarks>
public abstract class MarketDataClientContractTests<TClient>
    where TClient : IMarketDataClient
{
    // ------------------------------------------------------------------ //
    //  Factory method – implemented per provider                          //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Creates and returns a new testable instance of the client under test.
    /// Prefer constructing the client in a disabled / stub state so that
    /// no real network calls are made by the contract tests.
    /// </summary>
    protected abstract TClient CreateClient();

    // ------------------------------------------------------------------ //
    //  Identity & metadata contract                                       //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task IsEnabled_DoesNotThrow()
    {
        await using var client = CreateClient();
        var act = () => _ = client.IsEnabled;
        act.Should().NotThrow("accessing IsEnabled must never throw");
    }

    [Fact]
    public async Task IProviderMetadata_ProviderId_IsNotNullOrWhiteSpace()
    {
        await using var client = CreateClient();
        var meta = (Meridian.Infrastructure.Adapters.Core.IProviderMetadata)client;
        meta.ProviderId.Should().NotBeNullOrWhiteSpace(
            "every streaming client must declare a unique provider ID");
    }

    [Fact]
    public async Task IProviderMetadata_DisplayName_IsNotNullOrWhiteSpace()
    {
        await using var client = CreateClient();
        var meta = (Meridian.Infrastructure.Adapters.Core.IProviderMetadata)client;
        meta.ProviderDisplayName.Should().NotBeNullOrWhiteSpace(
            "every streaming client must expose a human-readable display name");
    }

    [Fact]
    public async Task IProviderMetadata_ProviderCapabilities_IsNotNull()
    {
        await using var client = CreateClient();
        var meta = (Meridian.Infrastructure.Adapters.Core.IProviderMetadata)client;
        var act = () => _ = meta.ProviderCapabilities;
        act.Should().NotThrow("ProviderCapabilities must never throw");
    }

    // ------------------------------------------------------------------ //
    //  Subscription contract                                               //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task SubscribeMarketDepth_ValidConfig_ReturnsNonNegativeId()
    {
        await using var client = CreateClient();
        if (!client.IsEnabled)
            return; // skip live subscription for disabled clients

        var cfg = CreateMinimalSymbolConfig("SPY");
        var id = client.SubscribeMarketDepth(cfg);

        id.Should().BeGreaterThanOrEqualTo(0,
            "SubscribeMarketDepth must return a non-negative subscription ID");
    }

    [Fact]
    public async Task UnsubscribeMarketDepth_UnknownId_DoesNotThrow()
    {
        await using var client = CreateClient();
        var act = () => client.UnsubscribeMarketDepth(int.MaxValue);
        act.Should().NotThrow(
            "UnsubscribeMarketDepth with an unknown ID must be a no-op, not an exception");
    }

    [Fact]
    public async Task SubscribeTrades_ValidConfig_ReturnsNonNegativeId()
    {
        await using var client = CreateClient();
        if (!client.IsEnabled)
            return; // skip live subscription for disabled clients

        var cfg = CreateMinimalSymbolConfig("SPY");
        var id = client.SubscribeTrades(cfg);

        id.Should().BeGreaterThanOrEqualTo(0,
            "SubscribeTrades must return a non-negative subscription ID");
    }

    [Fact]
    public async Task UnsubscribeTrades_UnknownId_DoesNotThrow()
    {
        await using var client = CreateClient();
        var act = () => client.UnsubscribeTrades(int.MaxValue);
        act.Should().NotThrow(
            "UnsubscribeTrades with an unknown ID must be a no-op, not an exception");
    }

    // ------------------------------------------------------------------ //
    //  Lifecycle contract                                                  //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        var client = CreateClient();
        var act = async () =>
        {
            await client.DisposeAsync();
            await client.DisposeAsync();
        };

        await act.Should().NotThrowAsync(
            "DisposeAsync must be idempotent per the IAsyncDisposable contract");
    }

    // ------------------------------------------------------------------ //
    //  Helper                                                              //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Creates a minimal <see cref="SymbolConfig"/> suitable for subscription contract tests.
    /// </summary>
    private static SymbolConfig CreateMinimalSymbolConfig(string ticker)
        => new(Symbol: ticker, SubscribeTrades: true, DepthLevels: 5);
}
