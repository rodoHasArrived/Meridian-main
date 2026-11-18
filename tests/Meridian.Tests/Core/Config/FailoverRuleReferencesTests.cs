using FluentAssertions;
using Meridian.Core.Config;
using Xunit;

namespace Meridian.Tests.Core.Config;

/// <summary>
/// Failover rules that name a provider ID with no data source are rejected when saved, not
/// discovered at runtime when the backup silently fails to exist.
/// </summary>
public sealed class FailoverRuleReferencesTests
{
    private static readonly DataSourcesConfig Config = new(
        Sources:
        [
            new DataSourceConfig("alpaca-live", "Alpaca", DataSourceKind.Alpaca),
            new DataSourceConfig("polygon-delayed", "Polygon", DataSourceKind.Polygon)
        ],
        FailoverRules: [new FailoverRuleConfig("equities", "alpaca-live", ["polygon-delayed"])]);

    [Fact]
    public void FindUnknownProvider_ReturnsTheMisspelledId()
    {
        var rule = new FailoverRuleConfig("equities", "alpaca-live", ["polygon"]);

        FailoverRuleReferences.FindUnknownProvider(Config, rule).Should().Be("polygon");
        FailoverRuleReferences.FindUnknownProvider(Config, Config.FailoverRules![0]).Should().BeNull();
    }

    [Fact]
    public void FindUnknownProvider_WithoutSources_AllowsBuiltInIds()
    {
        FailoverRuleReferences.FindUnknownProvider(new DataSourcesConfig(), new FailoverRuleConfig("r", "alpaca", ["ib"]))
            .Should().BeNull();
    }

    [Fact]
    public void FindUnknownInChangedRules_IgnoresUntouchedPreExistingRules()
    {
        var legacy = Config with { FailoverRules = [new FailoverRuleConfig("legacy", "alpaca-live", ["typo"])] };

        FailoverRuleReferences.FindUnknownInChangedRules(legacy, legacy with { HealthCheckIntervalSeconds = 5 })
            .Should().BeNull("an unrelated save must not fail because of an old rule");
        FailoverRuleReferences.FindUnknownInChangedRules(legacy,
                legacy with { FailoverRules = [new FailoverRuleConfig("legacy", "alpaca-live", ["typo"], FailoverThreshold: 1)] })
            .Should().BeNull("threshold-only edits keep the same provider references");
        FailoverRuleReferences.FindUnknownInChangedRules(legacy,
                legacy with { FailoverRules = [new FailoverRuleConfig("legacy", "alpaca-live", ["typo", "other"])] })
            .Should().Contain("legacy");
    }
}
