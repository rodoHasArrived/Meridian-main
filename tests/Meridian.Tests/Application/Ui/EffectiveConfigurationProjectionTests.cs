using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.UI;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;

namespace Meridian.Tests.Application.UI;

public sealed class EffectiveConfigurationProjectionTests
{
    private static readonly IReadOnlyList<EnvironmentOverrideInfo> NoEnvironment = [];

    [Fact]
    public void EveryTopLevelSectionOfTheDocumentIsRepresented()
    {
        var config = new AppConfig();
        var document = (JsonObject)JsonSerializer.SerializeToNode(config, AppConfigJsonOptions.Write)!;

        var entries = EffectiveConfigurationProjection.Build(config, NoEnvironment);

        foreach (var (section, _) in document)
        {
            entries.Should().Contain(
                entry => entry.Key == section || entry.Key.StartsWith(section + ".", StringComparison.Ordinal),
                "section {0} must not be missing from the effective view", section);
        }
        entries.Select(entry => entry.Key).Should().OnlyHaveUniqueItems();
        entries.Should().OnlyContain(entry => entry.Source == "default");
    }

    [Fact]
    public void ChangedFieldIsAttributedToConfigAndKeepsItsImpact()
    {
        var config = new AppConfig(Backfill: new BackfillConfig(Enabled: true));

        var entries = EffectiveConfigurationProjection.Build(config, NoEnvironment);

        var enabled = entries.Single(entry => entry.Key == "backfill.enabled");
        enabled.Value.Should().Be("true");
        enabled.Source.Should().Be("config");
        enabled.Impact.Should().Be(ConfigurationChangeImpact.RestartHost);
    }

    [Fact]
    public void SetEnvironmentVariableIsReportedAsTheSourceOfItsField()
    {
        var config = new AppConfig(Storage: new StorageConfig(NamingConvention: "ByDate"));
        IReadOnlyList<EnvironmentOverrideInfo> environment =
        [
            new() { EnvironmentVariable = "MDC_STORAGE_NAMING", ConfigPath = "Storage:NamingConvention", IsSet = true },
            new() { EnvironmentVariable = "MDC_STORAGE_PARTITION", ConfigPath = "Storage:DatePartition", IsSet = false }
        ];

        var entries = EffectiveConfigurationProjection.Build(config, environment);

        entries.Single(entry => entry.Key == "storage.namingConvention").Source.Should().Be("env:MDC_STORAGE_NAMING");
        entries.Single(entry => entry.Key == "storage.datePartition").Source.Should().NotStartWith("env:");
    }

    [Fact]
    public void CredentialValuesNeverAppearInTheProjection()
    {
        var config = new AppConfig(
            Alpaca: new AlpacaOptions("sensitive-key-123", "sensitive-secret-456"),
            Backfill: new BackfillConfig(Providers: new BackfillProvidersConfig(
                Polygon: new PolygonConfig(ApiKey: "sensitive-polygon-789"))));

        var entries = EffectiveConfigurationProjection.Build(config, NoEnvironment);

        var json = JsonSerializer.Serialize(entries);
        json.Should().NotContain("sensitive-key-123")
            .And.NotContain("sensitive-secret-456")
            .And.NotContain("sensitive-polygon-789");
        entries.Single(entry => entry.Key == "alpaca.secretKey").Source.Should().Be("config");
    }

    [Fact]
    public void SymbolListIsSummarizedAsACount()
    {
        var config = new AppConfig(Symbols: [new SymbolConfig("AAPL"), new SymbolConfig("MSFT")]);

        var entries = EffectiveConfigurationProjection.Build(config, NoEnvironment);

        entries.Single(entry => entry.Key == "symbols.count").Value.Should().Be("2");
        entries.Should().NotContain(entry => entry.Key.StartsWith("symbols.0", StringComparison.Ordinal));
    }
}
