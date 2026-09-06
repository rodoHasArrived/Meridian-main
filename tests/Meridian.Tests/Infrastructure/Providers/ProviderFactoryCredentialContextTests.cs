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
using Meridian.Infrastructure.DataSources;
using Meridian.Tests.Ui;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

[Collection(AlpacaCredentialEnvironmentCollection.Name)]
public sealed class ProviderFactoryCredentialContextTests
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedCanonicalProviderIds =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["alphavantage"] = "alphavantage",
            ["alphavantage-corp-actions"] = "alphavantage",
            ["alphavantage-symbols"] = "alphavantage",
            ["alpaca"] = "alpaca",
            ["alpaca-options"] = "alpaca",
            ["finnhub"] = "finnhub",
            ["finnhub-corp-actions"] = "finnhub",
            ["fred"] = "fred",
            ["fred-symbols"] = "fred",
            ["nasdaq"] = "nasdaqdatalink",
            ["nasdaq-corp-actions"] = "nasdaqdatalink",
            ["nasdaq-symbols"] = "nasdaqdatalink",
            ["polygon"] = "polygon",
            ["polygon-options"] = "polygon",
            ["robinhood"] = "robinhood",
            ["tiingo"] = "tiingo",
            ["tiingo-corp-actions"] = "tiingo",
            ["tiingo-symbols"] = "tiingo",
            ["twelvedata"] = "twelvedata",
            ["twelvedata-corp-actions"] = "twelvedata",
            ["twelvedata-symbols"] = "twelvedata"
        };

    public static IEnumerable<object[]> CredentialBearingProviderTypes()
        => typeof(ProviderFactory).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && AttributeCredentialResolver.GetAttributes(type).Count > 0)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => new object[] { type });

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
    public async Task StoredResolver_PartialOrRemovedSecretNeverMixesAnotherAccountsFallback()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "credential-source-isolation", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileProviderCredentialStore(root);
            var fallback = new TrackingCredentialResolver(new Dictionary<string, string?>
            {
                ["ALPACA_KEY_ID"] = "other-account-key",
                ["ALPACA_SECRET_KEY"] = "other-account-secret"
            });
            var resolver = new StoredProviderCredentialResolver(store, fallback);
            await store.SaveAsync(new ProviderCredentialSaveRequest("alpaca", new Dictionary<string, string?> { ["KeyId"] = "retained-account-key" }));
            var partial = resolver.CreateContext(typeof(AlpacaHistoricalDataProvider));
            partial.Get("ALPACA_KEY_ID").Should().Be("retained-account-key");
            partial.Get("ALPACA_SECRET_KEY").Should().BeNull();
            partial.IsConfigured("ALPACA_SECRET_KEY").Should().BeFalse();

            await store.SaveAsync(new ProviderCredentialSaveRequest("alpaca", new Dictionary<string, string?> { ["SecretKey"] = "retained-account-secret" }));
            resolver.CreateContext(typeof(AlpacaHistoricalDataProvider)).Get("ALPACA_SECRET_KEY").Should().Be("retained-account-secret");
            await store.SaveAsync(new ProviderCredentialSaveRequest("alpaca", new Dictionary<string, string?> { ["SecretKey"] = "" }));
            resolver.CreateContext(typeof(AlpacaHistoricalDataProvider), new Dictionary<string, string?>
            { ["ALPACA_SECRET_KEY"] = "configured-other-account-secret" }).Get("ALPACA_SECRET_KEY").Should().BeNull();
            fallback.ContextRequests.Should().BeEmpty();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void StoredResolver_MissingManagedRecordDoesNotBypassStoreThroughConfiguration()
    {
        using var environment = new AlpacaEnvironmentScope();
        foreach (var name in new[] { "ALPACA_KEY_ID", "ALPACA_SECRET_KEY", "APCA_API_KEY_ID", "APCA_API_SECRET_KEY", "ALPACA__KEYID", "ALPACA__SECRETKEY" })
            environment.SetProcessAndUser(name, null);
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "credential-source-isolation", Guid.NewGuid().ToString("N"));
        try
        {
            var fallback = new TrackingCredentialResolver();
            var resolver = new StoredProviderCredentialResolver(new FileProviderCredentialStore(root), fallback);
            var context = resolver.CreateContext(typeof(AlpacaHistoricalDataProvider), new Dictionary<string, string?>
            {
                ["ALPACA_KEY_ID"] = "unretained-key",
                ["ALPACA_SECRET_KEY"] = "unretained-secret"
            });
            context.IsConfigured("ALPACA_KEY_ID").Should().BeFalse();
            context.IsConfigured("ALPACA_SECRET_KEY").Should().BeFalse();
            fallback.ContextRequests.Should().BeEmpty();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task StoredResolver_CorruptVaultDoesNotSilentlySwitchToAnotherCredentialSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "credential-source-isolation", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileProviderCredentialStore(root);
            await store.SaveAsync(new ProviderCredentialSaveRequest("alpaca", new Dictionary<string, string?> { ["KeyId"] = "retained-key" }));
            await File.WriteAllTextAsync(store.VaultPath, "{invalid-json");
            var fallback = new TrackingCredentialResolver();
            var resolver = new StoredProviderCredentialResolver(store, fallback);
            var resolve = () => resolver.CreateContext(typeof(AlpacaHistoricalDataProvider));
            resolve.Should().Throw<System.Text.Json.JsonException>();
            fallback.ContextRequests.Should().BeEmpty();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("production")]
    [InlineData("packaged")]
    [InlineData("customer")]
    public void StoredResolver_ProductionPolicyCannotBeBypassedThroughLegacyFallback(string mode)
    {
        using var environment = new AlpacaEnvironmentScope();
        var names = new[] { "DOTNET_ENVIRONMENT", "ASPNETCORE_ENVIRONMENT", "MDC_PACKAGED_BUILD", "MERIDIAN_CUSTOMER_BUILD", "MDC_PROVIDER_ALLOW_ENV_FALLBACK" };
        var prior = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "credential-source-policy", Guid.NewGuid().ToString("N"));
        try
        {
            environment.SetProcessAndUser("ALPACA_KEY_ID", "ambient-other-account-key");
            environment.SetProcessAndUser("ALPACA_SECRET_KEY", "ambient-other-account-secret");
            Environment.SetEnvironmentVariable("MDC_PROVIDER_ALLOW_ENV_FALLBACK", null);
            Environment.SetEnvironmentVariable("MDC_PACKAGED_BUILD", mode == "packaged" ? "true" : null);
            Environment.SetEnvironmentVariable("MERIDIAN_CUSTOMER_BUILD", mode == "customer" ? "true" : null);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", mode == "production" ? "Production" : "Development");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", mode == "production" ? "Production" : "Development");
            var fallback = new TrackingCredentialResolver(new Dictionary<string, string?>
            { ["ALPACA_KEY_ID"] = "fallback-key", ["ALPACA_SECRET_KEY"] = "fallback-secret" });
            var resolver = new StoredProviderCredentialResolver(new FileProviderCredentialStore(root), fallback);
            var context = resolver.CreateContext(typeof(AlpacaHistoricalDataProvider), new Dictionary<string, string?>
            { ["ALPACA_KEY_ID"] = "config-key", ["ALPACA_SECRET_KEY"] = "config-secret" });
            context.IsConfigured("ALPACA_KEY_ID").Should().BeFalse();
            context.IsConfigured("ALPACA_SECRET_KEY").Should().BeFalse();
            fallback.ContextRequests.Should().BeEmpty();
        }
        finally
        {
            foreach (var pair in prior)
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StoredResolver_UnmanagedTypeRetainsItsExplicitLegacyResolver()
    {
        var fallback = new TrackingCredentialResolver();
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "credential-source-isolation", Guid.NewGuid().ToString("N"));
        var resolver = new StoredProviderCredentialResolver(new FileProviderCredentialStore(root), fallback);
        var context = resolver.CreateContext(typeof(ProviderFactoryCredentialContextTests),
            new Dictionary<string, string?> { ["custom-credential"] = "unmanaged-value" });
        context.Get("custom-credential").Should().Be("unmanaged-value");
        fallback.ContextRequests.Should().ContainSingle();
        Directory.Exists(root).Should().BeFalse();
    }

    [Theory]
    [InlineData(typeof(TwelveDataHistoricalDataProvider))]
    [InlineData(typeof(TwelveDataSymbolSearchProvider))]
    [InlineData(typeof(CatalogIdentityCredentialProvider))]
    public async Task StoredProviderCredentialResolver_CreateContext_UsesCanonicalDataSourceIdentityForTwelveData(
        Type providerType)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "provider-factory-twelve-data-store",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new FileProviderCredentialStore(root);
            await store.SaveAsync(new ProviderCredentialSaveRequest(
                "twelvedata",
                new Dictionary<string, string?>
                {
                    ["ApiKey"] = "stored-twelve-data-key"
                },
                Actor: "test"));
            var resolver = new StoredProviderCredentialResolver(
                store,
                new EnvironmentCredentialResolver());

            var context = resolver.CreateContext(
                providerType,
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["TWELVEDATA_API_KEY"] = "config-twelve-data-key"
                });

            context.Get("TWELVEDATA_API_KEY").Should().Be("stored-twelve-data-key");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(typeof(AlpacaOptionsMarketDataClient))]
    [InlineData(typeof(AlpacaCryptoMarketDataClient))]
    [InlineData(typeof(AlpacaNewsMarketDataClient))]
    public async Task StoredProviderCredentialResolver_CreateContext_UsesNearestDeclaredIdentityForDerivedAlpacaStreams(
        Type providerType)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "provider-factory-alpaca-stream-store",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new FileProviderCredentialStore(root);
            await store.SaveAsync(new ProviderCredentialSaveRequest(
                "alpaca",
                new Dictionary<string, string?>
                {
                    ["KeyId"] = "stored-alpaca-key",
                    ["SecretKey"] = "stored-alpaca-secret"
                },
                Actor: "test"));
            var resolver = new StoredProviderCredentialResolver(
                store,
                new EnvironmentCredentialResolver());

            var context = resolver.CreateContext(providerType);

            context.Get("ALPACA_KEY_ID").Should().Be("stored-alpaca-key");
            context.Get("ALPACA_SECRET_KEY").Should().Be("stored-alpaca-secret");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [MemberData(nameof(CredentialBearingProviderTypes))]
    public void ProviderCredentialCatalog_CredentialBearingImplementation_HasCanonicalIdentity(
        Type providerType)
    {
        var dataSource = GetCredentialSourceAttribute(providerType);
        dataSource.Should().NotBeNull($"{providerType.FullName} declares credentials and must resolve a data-source identity from its type hierarchy");
        ExpectedCanonicalProviderIds.Should().ContainKey(
            dataSource!.Id,
            $"credential-bearing provider identity '{dataSource.Id}' must be mapped explicitly");

        var expectedProviderId = ExpectedCanonicalProviderIds[dataSource.Id];
        var descriptor = ProviderCredentialCatalog.Find(dataSource.Id);
        descriptor.Should().NotBeNull($"credential-bearing provider identity '{dataSource.Id}' must resolve through the catalog");
        descriptor!.ProviderId.Should().Be(expectedProviderId);

        foreach (var credential in AttributeCredentialResolver.GetAttributes(providerType))
        {
            descriptor.RequiredFields.Any(field =>
                    string.Equals(field.Name, credential.Name, StringComparison.OrdinalIgnoreCase) ||
                    field.EnvironmentNames.Any(environmentName =>
                        string.Equals(environmentName, credential.Name, StringComparison.OrdinalIgnoreCase)))
                .Should().BeTrue(
                    $"credential '{credential.Name}' on {providerType.FullName} must map to the canonical '{expectedProviderId}' descriptor");
        }
    }

    private static DataSourceAttribute? GetCredentialSourceAttribute(Type providerType)
    {
        for (var candidate = providerType; candidate is not null; candidate = candidate.BaseType)
        {
            var dataSource = candidate.GetDataSourceAttribute();
            if (dataSource is not null)
            {
                return dataSource;
            }
        }

        return null;
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

    [DataSource(
        "twelve-data",
        "Catalog identity credential provider",
        Meridian.Infrastructure.DataSources.DataSourceType.Historical,
        DataSourceCategory.Premium)]
    [RequiresCredential("TWELVEDATA_API_KEY")]
    private sealed class CatalogIdentityCredentialProvider
    {
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
