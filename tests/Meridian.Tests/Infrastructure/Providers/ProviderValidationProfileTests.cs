using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Moq;
using System.Net;
using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Adapters.Core;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class ProviderValidationProfileTests
{
    [Fact]
    public void Loosens_RequiresReasonWhenDisablingExistingCheck()
    {
        var strict = new ProviderValidationProfile(StaleThresholdSeconds: 10, MaxLatencyMs: 100);
        (strict with { StaleThresholdSeconds = null }).Loosens(strict).Should().BeTrue();
        (strict with { MaxLatencyMs = 0 }).Loosens(strict).Should().BeTrue();
        (strict with { MaxLatencyMs = 50 }).Loosens(strict).Should().BeFalse();
        (strict with { FutureSkewSeconds = 301 }).Loosens(strict).Should().BeTrue();
    }

    [Fact]
    public void Validate_RejectsNonFiniteAndUnboundedTolerance()
    {
        var invalid = new ProviderValidationProfile(MaxLatencyMs: double.NaN);
        invalid.Invoking(profile => profile.Validate()).Should().Throw<ArgumentException>();
        (invalid with { MaxLatencyMs = 1, StaleToleranceDays = -1 }).Invoking(profile => profile.Validate())
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ProviderBoundary_UsesProfileFutureSkewAndLatency()
    {
        var now = DateTimeOffset.UtcNow;
        var update = new MarketTradeUpdate(now.AddSeconds(30), "AAPL", 100m, 1, AggressorSide.Unknown, 1);
        ProviderDataQualityValidator.ValidateTrade("test", update, now).Should().BeEmpty();
        ProviderDataQualityValidator.ValidateTrade("test", update, now,
            profile: new ProviderValidationProfile(FutureSkewSeconds: 10))
            .Should().ContainSingle(issue => issue.Message.Contains("future"));
        var delayed = update with { Timestamp = now.AddSeconds(-1) };
        ProviderDataQualityValidator.ValidateTrade("test", delayed, now,
            profile: new ProviderValidationProfile(MaxLatencyMs: 500))
            .Should().ContainSingle(issue => issue.Message.Contains("latency"));
    }

    [Fact]
    public void BackfillRecency_UsesThinlyTradedProfileTolerance()
    {
        var today = new DateOnly(2026, 11, 13);
        HistoricalBar[] bars = [new("AAPL", today.AddDays(-20), 100m, 101m, 99m, 100m, 100)];
        BackfillBarValidation.EvaluateDailyRecency(bars, today, today).Should().NotBeNull();
        BackfillBarValidation.EvaluateDailyRecency(bars, today, today,
            new ProviderValidationProfile(StaleToleranceDays: 30).StaleToleranceDays).Should().BeNull();
    }

    [Fact]
    public void Resolve_IsProviderSpecificAndCaseInsensitive()
    {
        var config = new BackfillConfig(ValidationProfiles: new Dictionary<string, ProviderValidationProfile>
        {
            ["Polygon"] = new(StaleToleranceDays: 30)
        });
        ProviderValidationProfile.Resolve(config, "polygon").StaleToleranceDays.Should().Be(30);
        ProviderValidationProfile.Resolve(config, "tiingo").StaleToleranceDays.Should().Be(10);
    }
    [Fact]
    public async Task Composite_AppliesProfileBeforeChoosingFirstProvider()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var thin = MockProvider("thin", 1, today.AddDays(-20));
        var fresh = MockProvider("fresh", 2, today);
        using var composite = new CompositeHistoricalDataProvider([thin.Object, fresh.Object],
            validationProfiles: new Dictionary<string, ProviderValidationProfile> { ["thin"] = new(StaleToleranceDays: 30) });
        var bars = await composite.GetDailyBarsAsync("AAPL", null, today);
        bars.Should().ContainSingle(bar => bar.Source == "thin");
        fresh.Verify(provider => provider.GetDailyBarsAsync(It.IsAny<string>(), It.IsAny<DateOnly?>(),
            It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Composite_RetainsActualThrottleAndNextAttempt()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var throttled = MockProvider("polygon", 1, today);
        throttled.Setup(provider => provider.GetDailyBarsAsync(It.IsAny<string>(), It.IsAny<DateOnly?>(),
            It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("throttled", null, HttpStatusCode.TooManyRequests));
        var fallback = MockProvider("stooq", 2, today);
        var history = new ProviderRateLimitHistory();
        using var composite = new CompositeHistoricalDataProvider([throttled.Object, fallback.Object], rateLimitHistory: history);
        await composite.GetDailyBarsAsync("AAPL", null, today);
        var observations = history.Read("polygon", 24);
        observations.Should().HaveCount(2);
        observations.Should().Contain(entry => entry.EventType == "rate-limited" && entry.HttpStatus == 429);
        observations.Should().Contain(entry => entry.EventType == "rotated" && entry.NextProvider == "stooq");
    }


    [Fact]
    public async Task AdjustedHistory_UnavailableNeverFallsBackToRawOrSynthetic()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var raw = MockProvider("real-raw", 1, today);
        var synthetic = MockProvider("synthetic", 2, today);
        synthetic.Setup(provider => provider.IsSimulated).Returns(true);
        synthetic.Setup(provider => provider.Capabilities).Returns(new HistoricalDataCapabilities { AdjustedPrices = true });
        synthetic.Setup(provider => provider.GetAdjustedDailyBarsAsync(It.IsAny<string>(), It.IsAny<DateOnly?>(),
            It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>())).ReturnsAsync([
                new AdjustedHistoricalBar("AAPL", today, 100m, 101m, 99m, 100m, 100, "synthetic")]);
        using var composite = new CompositeHistoricalDataProvider([raw.Object, synthetic.Object]);
        var fetch = () => composite.GetAdjustedDailyBarsAsync("AAPL", null, today);
        await fetch.Should().ThrowAsync<DataProviderException>().WithMessage("*Raw bars were not substituted*");
        raw.Verify(provider => provider.GetDailyBarsAsync(It.IsAny<string>(), It.IsAny<DateOnly?>(),
            It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Never);
        synthetic.Verify(provider => provider.GetAdjustedDailyBarsAsync(It.IsAny<string>(), It.IsAny<DateOnly?>(),
            It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdjustedHistory_UsesPerProviderRecencyWithoutChangingPriceRegime()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var thin = MockProvider("thin", 1, today.AddDays(-20));
        var fresh = MockProvider("fresh", 2, today);
        foreach (var provider in new[] { thin, fresh })
        {
            provider.Setup(item => item.Capabilities).Returns(new HistoricalDataCapabilities { AdjustedPrices = true });
        }
        thin.Setup(provider => provider.GetAdjustedDailyBarsAsync(It.IsAny<string>(), It.IsAny<DateOnly?>(),
            It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>())).ReturnsAsync([
                new AdjustedHistoricalBar("AAPL", today.AddDays(-20), 100m, 101m, 99m, 100m, 100, "thin", AdjustedClose: 50m)]);
        fresh.Setup(provider => provider.GetAdjustedDailyBarsAsync(It.IsAny<string>(), It.IsAny<DateOnly?>(),
            It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>())).ReturnsAsync([
                new AdjustedHistoricalBar("AAPL", today, 100m, 101m, 99m, 100m, 100, "fresh", AdjustedClose: 50m)]);
        using var ordinary = new CompositeHistoricalDataProvider([thin.Object, fresh.Object]);
        (await ordinary.GetAdjustedDailyBarsAsync("AAPL", null, today)).Should().ContainSingle(bar => bar.Source == "fresh");
        using var customized = new CompositeHistoricalDataProvider([thin.Object, fresh.Object],
            validationProfiles: new Dictionary<string, ProviderValidationProfile> { ["thin"] = new(StaleToleranceDays: 30) });
        var accepted = await customized.GetAdjustedDailyBarsAsync("AAPL", null, today);
        accepted.Should().ContainSingle(bar => bar.Source == "thin" && bar.AdjustedClose == 50m && bar.Close == 100m);
    }

    private static Mock<IHistoricalDataProvider> MockProvider(string name, int priority, DateOnly latest)
    {
        var mock = new Mock<IHistoricalDataProvider>();
        mock.Setup(provider => provider.Name).Returns(name);
        mock.Setup(provider => provider.DisplayName).Returns(name);
        mock.Setup(provider => provider.Priority).Returns(priority);
        mock.Setup(provider => provider.MaxRequestsPerWindow).Returns(100);
        mock.Setup(provider => provider.RateLimitWindow).Returns(TimeSpan.FromMinutes(1));
        mock.Setup(provider => provider.Capabilities).Returns(HistoricalDataCapabilities.None);
        mock.Setup(provider => provider.GetDailyBarsAsync(It.IsAny<string>(), It.IsAny<DateOnly?>(),
            It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new HistoricalBar("AAPL", latest, 100m, 101m, 99m, 100m, 100, Source: name)]);
        return mock;
    }

}
