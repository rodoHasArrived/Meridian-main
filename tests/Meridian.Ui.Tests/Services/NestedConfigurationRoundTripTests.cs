using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Xunit;
using DomainAppConfig = Meridian.Core.Config.AppConfig;

namespace Meridian.Ui.Tests.Services;

public sealed class NestedConfigurationRoundTripTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void CompressionOverrideRetainsHostNullFalseAndTrue(bool? configuredOverride)
    {
        var original = new DomainAppConfig(Compress: configuredOverride);
        var dto = JsonSerializer.Deserialize<AppConfigDto>(JsonSerializer.Serialize(original, AppConfigJsonOptions.Write),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        dto.Compress.Should().Be(configuredOverride ?? false);
        var restored = JsonSerializer.Deserialize<DomainAppConfig>(JsonSerializer.Serialize(dto), AppConfigJsonOptions.Read)!;
        restored.Compress.Should().Be(configuredOverride);
    }

    [Fact]
    public async Task FileServiceLoadSavePreservesProfilesAuditFailoverTimingAndMappings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-nested-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "appsettings.json");
            var original = CreateConfiguration();
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(original, AppConfigJsonOptions.Write));
            var service = new ConfigService(() => path);
            var dto = await service.LoadConfigAsync();
            dto.Should().NotBeNull();
            dto!.Compress = true;
            await service.SaveConfigAsync(dto);
            await service.UpdateFailoverSettingsAsync(enableFailover: false, failoverTimeoutSeconds: 45);
            var restored = JsonSerializer.Deserialize<DomainAppConfig>(await File.ReadAllTextAsync(path), AppConfigJsonOptions.Read)!;
            AssertProtectedConfigurationPreserved(original, restored);
            restored.Compress.Should().BeTrue();
            restored.DataSources!.EnableFailover.Should().BeFalse();
            restored.DataSources.FailoverTimeoutSeconds.Should().Be(45);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task SharedSectionMutationsPreserveUnknownNestedFieldsAndProviderSpecificSettings()
    {
        var original = CreateConfiguration();
        var service = new JsonRoundTripService(JsonSerializer.Serialize(original, AppConfigJsonOptions.Write));
        await service.SaveSymbolsAsync([new SymbolConfigDto { Symbol = "MSFT" }]);
        await service.SetBackfillProviderOptionsAsync("polygon", new BackfillProviderOptionsDto
        {
            Enabled = true,
            Priority = 3,
            TierId = "starter",
            RateLimitPerMinute = 20
        });
        await service.SetBackfillProviderOptionsAsync("nasdaq", new BackfillProviderOptionsDto
        {
            Enabled = true,
            Priority = 2
        });
        var restored = JsonSerializer.Deserialize<DomainAppConfig>(service.Json, AppConfigJsonOptions.Read)!;
        AssertProtectedConfigurationPreserved(original, restored);
        restored.Symbols.Should().ContainSingle(symbol => symbol.Symbol == "MSFT");
        restored.Backfill!.Providers!.Polygon!.TierId.Should().Be("starter");
        restored.Backfill.Providers.Polygon.RateLimitPerMinute.Should().Be(20);
        restored.Backfill.Providers.Polygon.ApiKey.Should().Be("retained-synthetic-fixture-key");
        restored.Backfill.Providers.Nasdaq!.Priority.Should().Be(2);
    }

    [Fact]
    public void LegacyNasdaqAliasReadsAndWritesOnlyTheHostCanonicalKey()
    {
        var dto = JsonSerializer.Deserialize<AppConfigDto>("""
            { "backfill": { "providers": { "nasdaqDataLink": { "enabled": true, "priority": 9 } } } }
            """)!;
        dto.Backfill!.Providers!.NasdaqDataLink!.Priority.Should().Be(9);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(dto));
        var providers = document.RootElement.GetProperty("backfill").GetProperty("providers");
        providers.GetProperty("nasdaq").GetProperty("priority").GetInt32().Should().Be(9);
        providers.TryGetProperty("nasdaqDataLink", out _).Should().BeFalse();
    }

    private static DomainAppConfig CreateConfiguration()
    {
        var before = new ProviderValidationProfile();
        var after = new ProviderValidationProfile(StaleToleranceDays: 30, MaxLatencyMs: 1500);
        var timestamp = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        return new DomainAppConfig(Alpaca: new AlpacaOptions(OptionsFeed: "opra", CryptoFeed: "us-1", NewsFeed: "news"),
            Backfill: new BackfillConfig(
            PreferAdjustedPrices: false,
            ProviderPriority: ["polygon", "stooq"],
            Providers: new BackfillProvidersConfig(
                Polygon: new PolygonConfig(ApiKey: "retained-synthetic-fixture-key"),
                Alpaca: new AlpacaBackfillConfig(Adjustment: "raw"),
                Nasdaq: new NasdaqDataLinkConfig(ApiKey: "retained-nasdaq-fixture-key", Database: "EOD", Priority: 31),
                Synthetic: new SyntheticMarketDataConfig(Enabled: false, Seed: 42)),
            ValidationProfiles: new Dictionary<string, ProviderValidationProfile> { ["polygon"] = after },
            ValidationProfileAudit: [new(Guid.Parse("c7fd1473-0dfd-49e8-a458-4ea34ef2b06d"), "polygon", timestamp,
                "synthetic-operator", "Thinly traded research universe.", true, before, after)]),
            DataSources: new DataSourcesConfig(
                Sources: [new DataSourceConfig("alpaca-primary", "Synthetic fixture", DataSourceKind.Alpaca)],
                HealthCheckIntervalSeconds: 17,
                AutoRecover: false,
                FailoverRules: [new("research", "alpaca-primary", ["ib-backup"], 4, 6, 85, 1500)],
                FailoverRuleAudit: [new("research", "synthetic-operator", "Approved fixture tolerance.", timestamp,
                    90, 85, 1000, 1500, 3, 4, 5, 6)],
                SymbolMappings: new Meridian.Core.Config.SymbolMappingsConfig(
                    Mappings: [new SymbolMappingConfig("BRK.B", IbSymbol: "BRK B")],
                    ProviderResolutionModes: new Dictionary<string, SymbolResolutionMode> { ["yahoo"] = SymbolResolutionMode.Compare })));
    }

    private static void AssertProtectedConfigurationPreserved(DomainAppConfig original, DomainAppConfig restored)
    {
        restored.Backfill!.ValidationProfiles.Should().BeEquivalentTo(original.Backfill!.ValidationProfiles);
        restored.Backfill.ValidationProfileAudit.Should().BeEquivalentTo(original.Backfill.ValidationProfileAudit);
        restored.Backfill.PreferAdjustedPrices.Should().BeFalse();
        restored.Backfill.ProviderPriority.Should().Equal(original.Backfill.ProviderPriority!);
        restored.Backfill.Providers!.Alpaca!.Adjustment.Should().Be("raw");
        restored.Backfill.Providers.Synthetic.Should().BeEquivalentTo(original.Backfill.Providers!.Synthetic);
        restored.Backfill.Providers.Nasdaq!.Database.Should().Be("EOD");
        restored.Backfill.Providers.Nasdaq.ApiKey.Should().Be("retained-nasdaq-fixture-key");
        restored.Alpaca.Should().BeEquivalentTo(original.Alpaca);
        restored.DataSources!.FailoverRules.Should().BeEquivalentTo(original.DataSources!.FailoverRules);
        restored.DataSources.FailoverRuleAudit.Should().BeEquivalentTo(original.DataSources.FailoverRuleAudit);
        restored.DataSources.HealthCheckIntervalSeconds.Should().Be(17);
        restored.DataSources.AutoRecover.Should().BeFalse();
        restored.DataSources.SymbolMappings.Should().BeEquivalentTo(original.DataSources.SymbolMappings);
    }

    private sealed class JsonRoundTripService(string json) : ConfigServiceBase
    {
        public string Json { get; private set; } = json;
        public override string ConfigPath => "/synthetic/nested-settings.json";
        protected override Task<AppConfigDto?> LoadConfigCoreAsync(CancellationToken ct = default)
            => Task.FromResult(JsonSerializer.Deserialize<AppConfigDto>(Json, SharedJsonOptions));
        protected override Task SaveConfigCoreAsync(AppConfigDto config, CancellationToken ct = default)
        {
            Json = JsonSerializer.Serialize(config, SharedJsonOptions);
            return Task.CompletedTask;
        }
        protected override void LogError(string message, Exception? exception = null) { }
    }
}
