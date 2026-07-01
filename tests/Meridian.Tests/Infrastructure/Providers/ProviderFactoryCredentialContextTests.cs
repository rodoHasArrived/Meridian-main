using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Meridian.Application.Services;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.AlphaVantage;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Finnhub;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Infrastructure.Adapters.NasdaqDataLink;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Infrastructure.Adapters.Stooq;
using Meridian.Infrastructure.Adapters.Tiingo;
using Meridian.Infrastructure.Adapters.TwelveData;
using Meridian.Infrastructure.Adapters.YahooFinance;
using Meridian.Infrastructure.Contracts;
using Meridian.Tests.Ui;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

[Collection(AlpacaCredentialEnvironmentCollection.Name)]
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
    public async Task StoredProviderCredentialResolver_CreateContext_UsesEncryptedStoreBeforeConfigFallback()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "provider-factory-store", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new FileProviderCredentialStore(root);
            await store.SaveAsync(new ProviderCredentialSaveRequest(
                "polygon",
                new Dictionary<string, string?>
                {
                    ["ApiKey"] = "stored-polygon-key"
                },
                Actor: "test"));
            var resolver = new StoredProviderCredentialResolver(
                store,
                new EnvironmentCredentialResolver());

            var context = resolver.CreateContext(
                typeof(PolygonHistoricalDataProvider),
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["POLYGON_API_KEY"] = "config-polygon-key"
                });

            context.Get("POLYGON_API_KEY").Should().Be("stored-polygon-key");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
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
                        Fred: new FredConfig(Enabled: true, ApiKey: "cfg-fred-key")))),
            resolver);

        var providers = factory.CreateBackfillProviders();

        providers.Should().ContainSingle(p => p is AlpacaHistoricalDataProvider);
        providers.Should().ContainSingle(p => p is PolygonHistoricalDataProvider);
        providers.Should().ContainSingle(p => p is TiingoHistoricalDataProvider);
        providers.Should().ContainSingle(p => p is FinnhubHistoricalDataProvider);
        providers.Should().ContainSingle(p => p is AlphaVantageHistoricalDataProvider);
        providers.Should().ContainSingle(p => p is FredHistoricalDataProvider);
        providers.Should().ContainSingle(p => p is NasdaqDataLinkHistoricalDataProvider);
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
    public void CreateBackfillProviders_WithConfiguredFreeProviderSet_IsDeterministicAndCapabilityAligned()
    {
        var resolver = new TrackingCredentialResolver();
        var factory = new ProviderFactory(
            new AppConfig(
                Backfill: new BackfillConfig(
                    Providers: new BackfillProvidersConfig(
                        Yahoo: new YahooFinanceConfig(Enabled: true),
                        Nasdaq: new NasdaqDataLinkConfig(Enabled: true, ApiKey: "cfg-nasdaq-key"),
                        Stooq: new StooqConfig(Enabled: true),
                        Tiingo: new TiingoConfig(Enabled: true, ApiToken: "cfg-tiingo-token"),
                        AlphaVantage: new AlphaVantageConfig(Enabled: true, ApiKey: "cfg-alpha-key"),
                        Finnhub: new FinnhubConfig(Enabled: true, ApiKey: "cfg-finnhub-key"),
                        Fred: new FredConfig(Enabled: true, ApiKey: "cfg-fred-key")))),
            resolver);

        var first = factory.CreateBackfillProviders();
        var second = factory.CreateBackfillProviders();

        first.Select(p => p.Name).Should().Equal(second.Select(p => p.Name),
            "backfill provider ordering should remain deterministic for parity/backfill planning");
        first.Should().ContainSingle(p => p is YahooFinanceHistoricalDataProvider);
        first.Should().ContainSingle(p => p is NasdaqDataLinkHistoricalDataProvider);
        first.Should().ContainSingle(p => p is StooqHistoricalDataProvider);
        first.Should().ContainSingle(p => p is TiingoHistoricalDataProvider);
        first.Should().ContainSingle(p => p is AlphaVantageHistoricalDataProvider);
        first.Should().ContainSingle(p => p is FinnhubHistoricalDataProvider);
        first.Should().ContainSingle(p => p is FredHistoricalDataProvider);
    }

    [Fact]
    public void CreateBackfillProviders_UsesTopLevelAlpacaConfigWhenBackfillConfigIsEmpty()
    {
        var resolver = new TrackingCredentialResolver();
        var factory = new ProviderFactory(
            new AppConfig(
                Alpaca: new AlpacaOptions(
                    KeyId: "top-level-alpaca-key",
                    SecretKey: "top-level-alpaca-secret",
                    Feed: "delayed_sip"),
                Backfill: new BackfillConfig(
                    Providers: new BackfillProvidersConfig(
                        Alpaca: new AlpacaBackfillConfig()))),
            resolver);

        var providers = factory.CreateBackfillProviders();

        providers.Should().ContainSingle(p => p is AlpacaHistoricalDataProvider);
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(AlpacaHistoricalDataProvider), ["ALPACA_KEY_ID", "ALPACA_SECRET_KEY"]));
    }

    [Fact]
    public void CreateBackfillProviders_UsesAlpacaApcaEnvironmentAliases()
    {
        using var env = new AlpacaEnvironmentScope();
        env.SetProcessAndUser("ALPACA_KEY_ID", null);
        env.SetProcessAndUser("ALPACA_SECRET_KEY", null);
        env.SetProcessAndUser("APCA_API_KEY_ID", "apca-alpaca-key");
        env.SetProcessAndUser("APCA_API_SECRET_KEY", "apca-alpaca-secret");

        var resolver = new TrackingCredentialResolver();
        var factory = new ProviderFactory(
            new AppConfig(
                Backfill: new BackfillConfig(
                    Providers: new BackfillProvidersConfig(
                        Alpaca: new AlpacaBackfillConfig()))),
            resolver);

        var providers = factory.CreateBackfillProviders();

        providers.Should().ContainSingle(p => p is AlpacaHistoricalDataProvider);
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
                        Tiingo: new TiingoConfig(ApiToken: null),
                        Finnhub: new FinnhubConfig(ApiKey: null),
                        Fred: new FredConfig(ApiKey: null),
                        Nasdaq: new NasdaqDataLinkConfig(ApiKey: null),
                        AlphaVantage: new AlphaVantageConfig(Enabled: true, ApiKey: null)))),
            resolver);

        var providers = factory.CreateSymbolSearchProviders();

        providers.Should().BeEmpty();
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(AlpacaHistoricalDataProvider), ["ALPACA_KEY_ID", "ALPACA_SECRET_KEY"]));
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(FinnhubHistoricalDataProvider), ["FINNHUB_API_KEY"]));
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(TiingoHistoricalDataProvider), ["TIINGO_API_TOKEN"]));
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(FredHistoricalDataProvider), ["FRED_API_KEY"]));
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(NasdaqDataLinkHistoricalDataProvider), ["NASDAQ_DATA_LINK_API_KEY"]));
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(AlphaVantageHistoricalDataProvider), ["ALPHA_VANTAGE_API_KEY"]));
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(TwelveDataHistoricalDataProvider), ["TWELVEDATA_API_KEY"]));
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(PolygonHistoricalDataProvider), ["POLYGON_API_KEY"]));
    }

    [Fact]
    public void CreateSymbolSearchProviders_WithAlphaVantageConfigKey_AddsAlphaVantageSearchProvider()
    {
        var resolver = new TrackingCredentialResolver();
        var factory = new ProviderFactory(
            new AppConfig(
                Backfill: new BackfillConfig(
                    Providers: new BackfillProvidersConfig(
                        AlphaVantage: new AlphaVantageConfig(Enabled: true, ApiKey: "cfg-alpha-key")))),
            resolver);

        var providers = factory.CreateSymbolSearchProviders();

        providers.Should().ContainSingle(p => p is AlphaVantageSymbolSearchProvider);
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(AlphaVantageHistoricalDataProvider), ["ALPHA_VANTAGE_API_KEY"]));
    }

    [Fact]
    public void CreateSymbolSearchProviders_WithNasdaqConfigKey_AddsNasdaqDataLinkSearchProvider()
    {
        var resolver = new TrackingCredentialResolver();
        var factory = new ProviderFactory(
            new AppConfig(
                Backfill: new BackfillConfig(
                    Providers: new BackfillProvidersConfig(
                        Nasdaq: new NasdaqDataLinkConfig(ApiKey: "cfg-nasdaq-key")))),
            resolver);

        var providers = factory.CreateSymbolSearchProviders();

        providers.Should().ContainSingle(p => p is NasdaqDataLinkSymbolSearchProvider);
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(NasdaqDataLinkHistoricalDataProvider), ["NASDAQ_DATA_LINK_API_KEY"]));
    }

    [Fact]
    public void CreateSymbolSearchProviders_WithTwelveDataCredential_AddsTwelveDataSearchProvider()
    {
        var resolver = new TrackingCredentialResolver(new Dictionary<string, string?>
        {
            ["TWELVEDATA_API_KEY"] = "cfg-twelve-key"
        });
        var factory = new ProviderFactory(new AppConfig(), resolver);

        var providers = factory.CreateSymbolSearchProviders();

        providers.Should().ContainSingle(p => p is TwelveDataSymbolSearchProvider);
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(TwelveDataHistoricalDataProvider), ["TWELVEDATA_API_KEY"]));
    }

    [Fact]
    public void CreateSymbolSearchProviders_WithTiingoConfigToken_AddsTiingoSearchProvider()
    {
        var resolver = new TrackingCredentialResolver();
        var factory = new ProviderFactory(
            new AppConfig(
                Backfill: new BackfillConfig(
                    Providers: new BackfillProvidersConfig(
                        Tiingo: new TiingoConfig(ApiToken: "cfg-tiingo-token")))),
            resolver);

        var providers = factory.CreateSymbolSearchProviders();

        providers.Should().ContainSingle(p => p is TiingoSymbolSearchProvider);
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(TiingoHistoricalDataProvider), ["TIINGO_API_TOKEN"]));
    }

    [Fact]
    public void CreateSymbolSearchProviders_WithFredConfigKey_AddsFredSearchProvider()
    {
        var resolver = new TrackingCredentialResolver();
        var factory = new ProviderFactory(
            new AppConfig(
                Backfill: new BackfillConfig(
                    Providers: new BackfillProvidersConfig(
                        Fred: new FredConfig(ApiKey: "cfg-fred-key")))),
            resolver);

        var providers = factory.CreateSymbolSearchProviders();

        providers.Should().ContainSingle(p => p is FredSymbolSearchProvider);
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(FredHistoricalDataProvider), ["FRED_API_KEY"]));
    }

    private sealed class TrackingCredentialResolver : IProviderCredentialResolver
    {
        private readonly IReadOnlyDictionary<string, string?> _resolvedValues;

        public TrackingCredentialResolver(IReadOnlyDictionary<string, string?>? resolvedValues = null)
        {
            _resolvedValues = resolvedValues ?? new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        public List<ContextRequest> ContextRequests { get; } = new();

        public ICredentialContext CreateContext(Type providerType, IReadOnlyDictionary<string, string?>? configuredValues = null)
        {
            var credentialNames = configuredValues?.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray()
                ?? Array.Empty<string>();
            ContextRequests.Add(new ContextRequest(providerType, credentialNames));

            return new TestCredentialContext(configuredValues, _resolvedValues);
        }
    }

    private sealed class TestCredentialContext : ICredentialContext
    {
        private readonly IReadOnlyDictionary<string, string?> _configuredValues;
        private readonly IReadOnlyDictionary<string, string?> _resolvedValues;

        public TestCredentialContext(
            IReadOnlyDictionary<string, string?>? configuredValues,
            IReadOnlyDictionary<string, string?>? resolvedValues)
        {
            _configuredValues = configuredValues ?? new Dictionary<string, string?>(StringComparer.Ordinal);
            _resolvedValues = resolvedValues ?? new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        public string? Get(string name)
            => _configuredValues.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : _resolvedValues.TryGetValue(name, out var resolvedValue)
                    ? resolvedValue
                    : null;

        public bool IsConfigured(string name)
            => !string.IsNullOrWhiteSpace(Get(name));
    }

    private sealed record ContextRequest(Type ProviderType, IReadOnlyList<string> CredentialNames);

    private sealed class AlpacaEnvironmentScope : IDisposable
    {
        private static readonly string[] Names =
        [
            "ALPACA_KEY_ID",
            "ALPACA_SECRET_KEY",
            "APCA_API_KEY_ID",
            "APCA_API_SECRET_KEY",
            "ALPACA__KEYID",
            "ALPACA__SECRETKEY"
        ];

        private readonly Dictionary<string, string?> _processValues = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string?> _userValues = new(StringComparer.Ordinal);

        public AlpacaEnvironmentScope()
        {
            foreach (var name in Names)
            {
                _processValues[name] = Environment.GetEnvironmentVariable(name);
                _userValues[name] = ReadUser(name);
            }
        }

        public void SetProcessAndUser(string name, string? value)
        {
            Environment.SetEnvironmentVariable(name, value);
            TrySetUser(name, value);
        }

        public void Dispose()
        {
            foreach (var name in Names)
            {
                Environment.SetEnvironmentVariable(name, _processValues[name]);
                TrySetUser(name, _userValues[name]);
            }
        }

        private static string? ReadUser(string name)
        {
            try
            {
                return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
            }
            catch (PlatformNotSupportedException)
            {
                return null;
            }
        }

        private static void TrySetUser(string name, string? value)
        {
            try
            {
                Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
            }
            catch (PlatformNotSupportedException)
            {
            }
        }
    }
}
