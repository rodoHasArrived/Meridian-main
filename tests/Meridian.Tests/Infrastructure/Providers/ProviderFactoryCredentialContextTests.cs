using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.AlphaVantage;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Finnhub;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Infrastructure.Adapters.NasdaqDataLink;
using Meridian.Infrastructure.Adapters.Polygon;
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
                        Finnhub: new FinnhubConfig(ApiKey: null)))),
            resolver);

        var providers = factory.CreateSymbolSearchProviders();

        providers.Should().BeEmpty();
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(AlpacaHistoricalDataProvider), ["ALPACA_KEY_ID", "ALPACA_SECRET_KEY"]));
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(FinnhubHistoricalDataProvider), ["FINNHUB_API_KEY"]));
        resolver.ContextRequests.Should().ContainEquivalentOf(
            new ContextRequest(typeof(PolygonHistoricalDataProvider), ["POLYGON_API_KEY"]));
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
