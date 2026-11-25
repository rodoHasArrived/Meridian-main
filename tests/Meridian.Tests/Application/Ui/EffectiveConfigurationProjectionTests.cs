using System.Text.Json;
using FluentAssertions;
using Meridian.Application.UI;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;

namespace Meridian.Tests.Application.UI;

public sealed class EffectiveConfigurationProjectionTests
{
    [Fact]
    public void AllDeclaredTopLevelSectionsAreRepresentedOnce()
    {
        var config = new AppConfig();
        var declaredDefaults = JsonSerializer.SerializeToElement(config, AppConfigJsonOptions.Write);
        var entries = EffectiveConfigurationProjection.Build(config, []);

        foreach (var property in declaredDefaults.EnumerateObject())
            entries.Should().Contain(entry => entry.Key == property.Name || entry.Key.StartsWith(property.Name + ".", StringComparison.Ordinal));
        entries.Select(entry => entry.Key).Should().OnlyHaveUniqueItems();
        entries.Should().OnlyContain(entry => entry.Source == "default");
    }

    [Fact]
    public void AnnotatesTypedValuesAndSummarizesSymbolsAndLargeArrays()
    {
        var config = new AppConfig(
            Storage: new(Sinks: Enumerable.Repeat("jsonl", 26).ToList()),
            Symbols: [new("TEST1"), new("TEST2")],
            Backfill: new(Enabled: true));
        EnvironmentOverrideInfo[] environment = [
            new() { EnvironmentVariable = "MDC_BACKFILL_ENABLED", ConfigPath = "Backfill:Enabled", IsSet = true },
            new() { EnvironmentVariable = "MDC_SYMBOLS", ConfigPath = "Symbols", IsSet = true }
        ];

        var entries = EffectiveConfigurationProjection.Build(config, environment).ToDictionary(entry => entry.Key);
        entries["backfill.enabled"].Should().Be(new EffectiveConfigurationEntry(
            "backfill.enabled", "true", "env:MDC_BACKFILL_ENABLED", ConfigurationChangeImpact.RestartHost));
        entries["symbols.count"].Should().Be(new EffectiveConfigurationEntry(
            "symbols.count", "2", "env:MDC_SYMBOLS", ConfigurationChangeImpact.Live));
        entries["storage.sinks.count"].Value.Should().Be("26");
        entries.Keys.Should().NotContain("symbols.0").And.NotContain("storage.sinks.0");
    }
}
