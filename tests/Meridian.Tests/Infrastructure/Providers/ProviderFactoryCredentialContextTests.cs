using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.AlphaVantage;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Edgar;
using Meridian.Infrastructure.Adapters.Finnhub;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Infrastructure.Adapters.NasdaqDataLink;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Infrastructure.Adapters.Robinhood;
using Meridian.Infrastructure.Adapters.Tiingo;
using Meridian.Infrastructure.Contracts;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class ProviderFactoryCredentialContextTests
{
    [Fact]
    public void EnvironmentCredentialResolver_CreateContext_UsesConfiguredValuesAsFallback()
    {
        var resolver = new EnvironmentCredentialResolver();

        var context = resolver.CreateContext(
            typeof(PolygonHistoricalDataProvider),
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["POLYGON_API_KEY"] = "config-polygon-key"
            });

        context.Get("POLYGON_API_KEY").Should().Be("config-polygon-key");
    }

    [Fact]
    public void CreateBackfillProviders_UsesGenericCredentialContextWithoutLegacyMethods()
    {
        var resolver = new TrackingCredentialResolver();
        var factory = new ProviderFactory(
            new AppConfig(
                Backfill: new BackfillConfig(
                    Providers: new BackfillProvidersConfig(
                        Alpaca: new AlpacaBackfillConfig(KeyId: "cfg-alpaca-key", SecretKey: "cfg-alpaca-secret"),
                        Nasdaq: new NasdaqDataLinkConfig(ApiKey: "cfg-nasdaq-key"),
                        Tiingo: new TiingoConfig(ApiToken: "cfg-tiingo-token"),
                        Polygon: new PolygonConfig(ApiKey: "cfg-polygon-key"),
                        AlphaVantage: new AlphaVantageConfig(Enabled: true, ApiKey: "cfg-alpha-key"),
                        Finnhub: new FinnhubConfig(ApiKey: "cfg-finnhub-key"),
                        Fred: new FredConfig(Enabled: true, ApiKey: "cfg-fred-key"),
                        Robinhood: new RobinhoodConfig(Enabled: true, Priority: 35, RateLimitPerHour: 120)))),
            resolver);

        var providers = factory.CreateBackfillProviders();

        providers.Should().ContainSingle(p => p is AlpacaHistoricalDataProvider);
        providers.Should().ContainSingle(p => p is PolygonHistoricalDataProvider);
        providers.Should().ContainSingle(p => p is TiingoHistoricalDataProvider);
        providers.Should().ContainSingle(p => p is FinnhubHistoricalDataProvider);
        providers.Should().ContainSingle(p => p is AlphaVantageHistoricalDataProvider);
        providers.Should().ContainSingle(p => p is FredHistoricalDataProvider);
        providers.Should().ContainSingle(p => p is NasdaqDataLinkHistoricalDataProvider);
        providers.Should().ContainSingle(p => p is RobinhoodHistoricalDataProvider);
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(AlpacaHistoricalDataProvider), ["ALPACA_KEY_ID", "ALPACA_SECRET_KEY"]));
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(PolygonHistoricalDataProvider), ["POLYGON_API_KEY"]));
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(TiingoHistoricalDataProvider), ["TIINGO_API_TOKEN"]));
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(FinnhubHistoricalDataProvider), ["FINNHUB_API_KEY"]));
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(AlphaVantageHistoricalDataProvider), ["ALPHA_VANTAGE_API_KEY"]));
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(FredHistoricalDataProvider), ["FRED_API_KEY"]));
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(NasdaqDataLinkHistoricalDataProvider), ["NASDAQ_DATA_LINK_API_KEY"]));
    }

    [Fact]
    public void CreateSymbolSearchProviders_SkipsCredentialGatedProvidersWithoutLegacyMethods()
    {
        var resolver = new TrackingCredentialResolver();
        var factory = new ProviderFactory(
            new AppConfig(
                Backfill: new BackfillConfig(
                    Providers: new BackfillProvidersConfig(
                        Alpaca: new AlpacaBackfillConfig(KeyId: null, SecretKey: null),
                        Polygon: new PolygonConfig(ApiKey: null),
                        Finnhub: new FinnhubConfig(ApiKey: null)))),
            resolver);

        var providers = factory.CreateSymbolSearchProviders();

        // EDGAR (SEC public API) requires no credentials and is always included.
        // Credential-gated providers (Alpaca, Polygon, Finnhub) are skipped when no
        // credentials are configured.
        providers.Should().ContainSingle(p => p is EdgarSymbolSearchProvider,
            because: "EDGAR is a free public data source that does not require credentials");
        providers.Should().NotContain(p => p is AlpacaSymbolSearchProviderRefactored,
            because: "Alpaca symbol search requires credentials that were not supplied");
        providers.Should().NotContain(p => p is FinnhubSymbolSearchProviderRefactored,
            because: "Finnhub symbol search requires credentials that were not supplied");
        providers.Should().NotContain(p => p is PolygonSymbolSearchProvider,
            because: "Polygon symbol search requires credentials that were not supplied");

        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(AlpacaHistoricalDataProvider), ["ALPACA_KEY_ID", "ALPACA_SECRET_KEY"]));
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(FinnhubHistoricalDataProvider), ["FINNHUB_API_KEY"]));
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(PolygonHistoricalDataProvider), ["POLYGON_API_KEY"]));
    }

    [Fact]
    public void CreateSymbolSearchProviders_IncludesRobinhoodWhenProviderFamilyIsEnabled()
    {
        var resolver = new TrackingCredentialResolver();
        var factory = new ProviderFactory(
            new AppConfig(
                Backfill: new BackfillConfig(
                    Providers: new BackfillProvidersConfig(
                        Robinhood: new RobinhoodConfig(Enabled: true)))),
            resolver);

        var providers = factory.CreateSymbolSearchProviders();

        providers.Should().ContainSingle(p => p is RobinhoodSymbolSearchProvider);
        providers.Should().ContainSingle(p => p is EdgarSymbolSearchProvider);
    }

    private sealed class TrackingCredentialResolver : IProviderCredentialResolver
    {
        public List<ContextRequest> ContextRequests { get; } = new();

        public ICredentialContext CreateContext(Type providerType, IReadOnlyDictionary<string, string?>? configuredValues = null)
        {
            var credentialNames = configuredValues?.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray()
                ?? Array.Empty<string>();
            ContextRequests.Add(new ContextRequest(providerType, credentialNames));

            return new TestCredentialContext(configuredValues);
        }
    }

    private sealed class TestCredentialContext : ICredentialContext
    {
        private readonly IReadOnlyDictionary<string, string?> _configuredValues;

        public TestCredentialContext(IReadOnlyDictionary<string, string?>? configuredValues)
        {
            _configuredValues = configuredValues ?? new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        public string? Get(string name)
            => _configuredValues.TryGetValue(name, out var value) ? value : null;

        public bool IsConfigured(string name)
            => !string.IsNullOrWhiteSpace(Get(name));
    }

    private sealed record ContextRequest(Type ProviderType, IReadOnlyList<string> CredentialNames);
}
