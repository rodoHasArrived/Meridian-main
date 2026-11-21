using FluentAssertions;
using Meridian.Core.Config;
using Meridian.DataIntegration.Monitoring.DataQuality;
using Xunit;

namespace Meridian.Tests.DataIntegration.Monitoring.DataQuality;

/// <summary>
/// The streaming cross-provider comparison honours the user's tolerance policy: strict basis-point
/// checks, informational volume, and wider bands for illiquid asset classes such as preferreds.
/// </summary>
public sealed class CrossProviderTolerancePolicyComparisonTests
{
    private static readonly CrossProviderConfig Window = new() { ComparisonWindowSeconds = 60 };

    [Fact]
    public void StrictPolicy_FlagsFifteenBasisPointGapThatDefaultsIgnore()
    {
        using var defaults = new CrossProviderComparisonService(Window);
        using var strict = new CrossProviderComparisonService(Window,
            new CrossProviderTolerancePolicy(PriceTolerancePercent: 0.10));
        var now = DateTimeOffset.UtcNow;

        foreach (var service in new[] { defaults, strict })
        {
            service.RecordTrade("SPY", "alpaca", now, 500.00m, 100);
            service.RecordTrade("SPY", "yahoo", now, 500.75m, 100); // 0.15% apart
        }

        defaults.GetRecentDiscrepancies().Should().BeEmpty();
        strict.GetRecentDiscrepancies().Should().ContainSingle(d => d.DiscrepancyType == "PriceDifference");
    }

    [Fact]
    public void InformationalVolume_IsRecordedButExcludedFromAlertStatistics()
    {
        using var service = new CrossProviderComparisonService(Window,
            new CrossProviderTolerancePolicy(VolumeMode: CrossProviderVolumeMode.Informational));
        var now = DateTimeOffset.UtcNow;

        service.RecordTrade("AAPL", "alpaca", now, 150m, 100);
        service.RecordTrade("AAPL", "polygon", now, 150m, 500);

        var discrepancy = service.GetRecentDiscrepancies().Should().ContainSingle().Subject;
        discrepancy.IsInformational.Should().BeTrue();
        discrepancy.Severity.Should().Be(DiscrepancySeverity.Low);
        service.GetStatistics().TotalDiscrepancies.Should().Be(0);
    }

    [Fact]
    public void VolumeOff_SkipsVolumeComparison()
    {
        using var service = new CrossProviderComparisonService(Window,
            new CrossProviderTolerancePolicy(VolumeMode: CrossProviderVolumeMode.Off));
        var now = DateTimeOffset.UtcNow;

        service.RecordTrade("AAPL", "alpaca", now, 150m, 100);
        service.RecordTrade("AAPL", "polygon", now, 150m, 5000);

        service.GetRecentDiscrepancies().Should().BeEmpty();
    }

    [Fact]
    public void AssetClassOverride_WidensBandForPreferreds()
    {
        var policy = new CrossProviderTolerancePolicy(AssetClassOverrides: new Dictionary<string, CrossProviderToleranceOverride>
        {
            ["preferred"] = new(PriceTolerancePercent: 2.0)
        });
        using var service = new CrossProviderComparisonService(Window, policy,
            symbol => symbol == "BAC-PL" ? "preferred" : "equity");
        var now = DateTimeOffset.UtcNow;

        service.RecordTrade("BAC-PL", "alpaca", now, 25.00m, 10);
        service.RecordTrade("BAC-PL", "polygon", now, 25.25m, 10); // 1% apart: inside the preferred band
        service.RecordTrade("MSFT", "alpaca", now, 400.00m, 10);
        service.RecordTrade("MSFT", "polygon", now, 404.00m, 10); // 1% apart: outside the equity band

        service.GetRecentDiscrepancies().Should().ContainSingle()
            .Which.Value1.Should().Be("400.0000");
    }

    [Fact]
    public void UpdatePolicy_AppliesToNextEventAndRejectsOutOfBounds()
    {
        using var service = new CrossProviderComparisonService(Window);
        var now = DateTimeOffset.UtcNow;

        service.UpdatePolicy(new CrossProviderTolerancePolicy(QuoteTolerancePercent: 0.05));
        service.RecordQuote("QQQ", "alpaca", now, 400.00m, 400.10m, 1, 1);
        service.RecordQuote("QQQ", "polygon", now, 400.40m, 400.10m, 1, 1); // bid 0.1% apart

        service.GetRecentDiscrepancies().Should().ContainSingle(d => d.DiscrepancyType == "BidPriceDifference");
        service.Invoking(s => s.UpdatePolicy(new CrossProviderTolerancePolicy(PriceTolerancePercent: 100)))
            .Should().Throw<ArgumentException>("a 100% tolerance would silently disable alarms");
        service.Policy.QuoteTolerancePercent.Should().Be(0.05);
    }
}
