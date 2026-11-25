using System.Text.Json;
using FluentAssertions;
using Meridian.Core.Config;
using Xunit;

namespace Meridian.Tests.Core.Config;

public sealed class CrossProviderTolerancePolicyTests
{
    [Fact]
    public void Defaults_ReproduceHistoricalHardCodedThresholds()
    {
        var policy = CrossProviderTolerancePolicy.Default;

        policy.PriceTolerancePercent.Should().Be(0.5);
        policy.QuoteTolerancePercent.Should().Be(0.5);
        policy.VolumeTolerancePercent.Should().Be(10.0);
        policy.VolumeMode.Should().Be(CrossProviderVolumeMode.Alert);
        policy.BackfillBarsToCompare.Should().Be(5);
        policy.BackfillCloseTolerancePercent.Should().Be(1.0);
        CrossProviderTolerancePolicy.FromConfig(new AppConfig()).Should().Be(policy);
    }

    [Theory]
    [InlineData(-0.01, 0.5, 10.0, 1.0)]
    [InlineData(5.01, 0.5, 10.0, 1.0)]
    [InlineData(0.5, 100.0, 10.0, 1.0)]
    [InlineData(0.5, 0.5, 50.5, 1.0)]
    [InlineData(0.5, 0.5, 10.0, double.NaN)]
    public void Validate_RejectsOutOfBoundsTolerances(double price, double quote, double volume, double close)
    {
        var policy = new CrossProviderTolerancePolicy(price, quote, volume, BackfillCloseTolerancePercent: close);

        policy.Invoking(p => p.Validate()).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_RejectsZeroBarsAndBadOverrides()
    {
        new CrossProviderTolerancePolicy(BackfillBarsToCompare: 0)
            .Invoking(p => p.Validate()).Should().Throw<ArgumentException>();
        new CrossProviderTolerancePolicy(AssetClassOverrides: new Dictionary<string, CrossProviderToleranceOverride>
        {
            ["preferred stock"] = new(PriceTolerancePercent: 2)
        }).Invoking(p => p.Validate()).Should().Throw<ArgumentException>("asset-class names are identifiers");
        new CrossProviderTolerancePolicy(AssetClassOverrides: new Dictionary<string, CrossProviderToleranceOverride>
        {
            ["preferred"] = new(PriceTolerancePercent: 9)
        }).Invoking(p => p.Validate()).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void StrictFiveBasisPointPolicyWithAllBars_IsValid()
    {
        var strict = new CrossProviderTolerancePolicy(
            PriceTolerancePercent: 0.05,
            BackfillBarsToCompare: null,
            BackfillCloseTolerancePercent: 0.15,
            VolumeMode: CrossProviderVolumeMode.Informational);

        strict.Invoking(p => p.Validate()).Should().NotThrow();
        strict.Loosens(CrossProviderTolerancePolicy.Default).Should().BeTrue(
            "informational volume flags fewer alerts than the default even though prices are stricter");
    }

    [Fact]
    public void Resolve_AppliesAssetClassOverrideCaseInsensitively()
    {
        var policy = new CrossProviderTolerancePolicy(AssetClassOverrides: new Dictionary<string, CrossProviderToleranceOverride>
        {
            ["Preferred"] = new(PriceTolerancePercent: 2.0, VolumeMode: CrossProviderVolumeMode.Off)
        });

        var preferred = policy.Resolve("preferred");
        preferred.PriceTolerancePercent.Should().Be(2.0);
        preferred.VolumeMode.Should().Be(CrossProviderVolumeMode.Off);
        preferred.QuoteTolerancePercent.Should().Be(0.5, "unset override fields inherit the defaults");
        policy.Resolve("equity").PriceTolerancePercent.Should().Be(0.5);
        policy.Resolve((string?)null).PriceTolerancePercent.Should().Be(0.5);
    }

    [Fact]
    public void Loosens_DetectsWiderTolerancesFewerBarsAndRemovedOverrides()
    {
        var current = new CrossProviderTolerancePolicy(BackfillBarsToCompare: null,
            AssetClassOverrides: new Dictionary<string, CrossProviderToleranceOverride>
            {
                ["preferred"] = new(PriceTolerancePercent: 0.25)
            });

        (current with { PriceTolerancePercent = 0.4 }).Loosens(current).Should().BeFalse();
        (current with { PriceTolerancePercent = 0.6 }).Loosens(current).Should().BeTrue();
        (current with { BackfillBarsToCompare = 20 }).Loosens(current).Should().BeTrue("all bars → 20 bars compares fewer");
        (current with { AssetClassOverrides = null }).Loosens(current).Should().BeTrue("preferred widens back to 0.5%");
        (current with { VolumeMode = CrossProviderVolumeMode.Off }).Loosens(current).Should().BeTrue();
    }

    [Fact]
    public void Policy_RoundTripsThroughAppConfigJson()
    {
        var config = new AppConfig
        {
            DataQuality = new DataQualityConfig(new CrossProviderTolerancePolicy(
                PriceTolerancePercent: 0.15,
                VolumeMode: CrossProviderVolumeMode.Informational,
                BackfillBarsToCompare: null,
                AssetClassOverrides: new Dictionary<string, CrossProviderToleranceOverride>
                {
                    ["preferred"] = new(PriceTolerancePercent: 1.5)
                }))
        };

        var json = JsonSerializer.Serialize(config, AppConfigJsonOptions.Write);
        var restored = JsonSerializer.Deserialize<AppConfig>(json, AppConfigJsonOptions.Read)!;

        json.Should().Contain("\"Informational\"");
        var policy = CrossProviderTolerancePolicy.FromConfig(restored);
        policy.PriceTolerancePercent.Should().Be(0.15);
        policy.VolumeMode.Should().Be(CrossProviderVolumeMode.Informational);
        policy.BackfillBarsToCompare.Should().BeNull();
        policy.Resolve("PREFERRED").PriceTolerancePercent.Should().Be(1.5);
    }
}
