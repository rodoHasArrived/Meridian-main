using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="ProviderManagementService"/> — singleton pattern,
/// and comprehensive data model validation for provider management types.
/// </summary>
public sealed class ProviderManagementServiceTests
{
    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        var a = ProviderManagementService.Instance;
        var b = ProviderManagementService.Instance;
        a.Should().BeSameAs(b);
    }

    // ── AllProvidersStatusResult ──────────────────────────────────────

    [Fact]
    public void AllProvidersStatusResult_ShouldHaveDefaults()
    {
        var result = new AllProvidersStatusResult();
        result.Success.Should().BeFalse();
        result.Error.Should().BeNull();
        result.ActiveProvider.Should().BeNull();
        result.Providers.Should().NotBeNull().And.BeEmpty();
    }

    // ── ProviderStatusInfo ───────────────────────────────────────────

    [Fact]
    public void ProviderStatusInfo_ShouldHaveDefaults()
    {
        var info = new ProviderStatusInfo();
        info.Name.Should().BeEmpty();
        info.DisplayName.Should().BeEmpty();
        info.IsEnabled.Should().BeFalse();
        info.IsConnected.Should().BeFalse();
        info.IsActive.Should().BeFalse();
        info.Status.Should().BeEmpty();
        info.LastConnectedAt.Should().BeNull();
        info.LastErrorAt.Should().BeNull();
        info.LastError.Should().BeNull();
        info.LatencyMs.Should().Be(0);
        info.ActiveSubscriptions.Should().Be(0);
        info.EventsReceived.Should().Be(0);
    }

    [Fact]
    public void ProviderStatusInfo_ShouldAcceptValues()
    {
        var info = new ProviderStatusInfo
        {
            Name = "alpaca",
            DisplayName = "Alpaca Markets",
            IsEnabled = true,
            IsConnected = true,
            IsActive = true,
            Status = "Connected",
            LatencyMs = 45.2,
            ActiveSubscriptions = 10,
            EventsReceived = 1500000
        };

        info.Name.Should().Be("alpaca");
        info.LatencyMs.Should().Be(45.2);
        info.EventsReceived.Should().Be(1500000);
    }

    // ── ProviderDetailResult ─────────────────────────────────────────

    [Fact]
    public void ProviderDetailResult_ShouldHaveDefaults()
    {
        var result = new ProviderDetailResult();
        result.Success.Should().BeFalse();
        result.Error.Should().BeNull();
        result.Provider.Should().BeNull();
    }

    // ── ProviderDetailResponse ───────────────────────────────────────

    [Fact]
    public void ProviderDetailResponse_ShouldHaveDefaults()
    {
        var response = new ProviderDetailResponse();
        response.Name.Should().BeEmpty();
        response.DisplayName.Should().BeEmpty();
        response.Description.Should().BeEmpty();
        response.IsEnabled.Should().BeFalse();
        response.IsConnected.Should().BeFalse();
        response.Capabilities.Should().BeNull();
        response.RateLimit.Should().BeNull();
        response.Statistics.Should().BeNull();
    }

    // ── ProviderStatistics ───────────────────────────────────────────

    [Fact]
    public void ProviderStatistics_ShouldHaveDefaults()
    {
        var stats = new ProviderStatistics();
        stats.TotalEventsReceived.Should().Be(0);
        stats.TotalTradesReceived.Should().Be(0);
        stats.TotalQuotesReceived.Should().Be(0);
        stats.TotalErrors.Should().Be(0);
        stats.AverageLatencyMs.Should().Be(0);
        stats.ReconnectCount.Should().Be(0);
    }

    // ── FailoverConfigResult ─────────────────────────────────────────

    [Fact]
    public void FailoverConfigResult_ShouldHaveDefaults()
    {
        var result = new FailoverConfigResult();
        result.Success.Should().BeFalse();
        result.Enabled.Should().BeFalse();
        result.TimeoutSeconds.Should().Be(0);
        result.MaxRetries.Should().Be(0);
        result.ProviderPriority.Should().NotBeNull().And.BeEmpty();
        result.CurrentPrimary.Should().BeNull();
        result.FailoverHistory.Should().NotBeNull().And.BeEmpty();
    }

    // ── FailoverEvent ────────────────────────────────────────────────

    [Fact]
    public void FailoverEvent_ShouldHaveDefaults()
    {
        var evt = new FailoverEvent();
        evt.FromProvider.Should().BeEmpty();
        evt.ToProvider.Should().BeEmpty();
        evt.Reason.Should().BeEmpty();
        evt.Success.Should().BeFalse();
    }

    [Fact]
    public void FailoverEvent_ShouldAcceptValues()
    {
        var evt = new FailoverEvent
        {
            Timestamp = DateTime.UtcNow,
            FromProvider = "alpaca",
            ToProvider = "polygon",
            Reason = "High latency",
            Success = true
        };

        evt.FromProvider.Should().Be("alpaca");
        evt.ToProvider.Should().Be("polygon");
        evt.Success.Should().BeTrue();
    }

    // ── FailoverResult ───────────────────────────────────────────────

    [Fact]
    public void FailoverResult_ShouldHaveDefaults()
    {
        var result = new FailoverResult();
        result.Success.Should().BeFalse();
        result.Error.Should().BeNull();
        result.PreviousProvider.Should().BeNull();
        result.NewProvider.Should().BeNull();
        result.Message.Should().BeNull();
    }

    // ── ProviderRateLimit ────────────────────────────────────────────

    [Fact]
    public void ProviderRateLimit_ShouldHaveDefaults()
    {
        var rateLimit = new ProviderRateLimit();
        rateLimit.Provider.Should().BeEmpty();
        rateLimit.RequestsPerMinute.Should().Be(0);
        rateLimit.RequestsPerHour.Should().Be(0);
        rateLimit.IsThrottled.Should().BeFalse();
        rateLimit.Status.Should().BeEmpty();
    }

    [Fact]
    public void ProviderRateLimit_ShouldAcceptValues()
    {
        var rateLimit = new ProviderRateLimit
        {
            Provider = "alpaca",
            RequestsPerMinute = 200,
            RequestsPerHour = 3000,
            RequestsUsedMinute = 50,
            RequestsUsedHour = 500,
            RequestsRemainingMinute = 150,
            RequestsRemainingHour = 2500,
            UsagePercentMinute = 25.0,
            UsagePercentHour = 16.7,
            IsThrottled = false,
            Status = "OK"
        };

        rateLimit.RequestsPerMinute.Should().Be(200);
        rateLimit.UsagePercentMinute.Should().Be(25.0);
    }

    // ── RateLimitsResult ─────────────────────────────────────────────

    [Fact]
    public void RateLimitsResult_ShouldHaveDefaults()
    {
        var result = new RateLimitsResult();
        result.Success.Should().BeFalse();
        result.Error.Should().BeNull();
        result.Providers.Should().NotBeNull().And.BeEmpty();
    }

    // ── ProviderCapabilities ─────────────────────────────────────────

    [Fact]
    public void ProviderCapabilities_ShouldHaveDefaults()
    {
        var caps = new ProviderCapabilities();
        caps.Provider.Should().BeEmpty();
        caps.SupportsRealTime.Should().BeFalse();
        caps.SupportsHistorical.Should().BeFalse();
        caps.SupportsTrades.Should().BeFalse();
        caps.SupportsQuotes.Should().BeFalse();
        caps.SupportsDepth.Should().BeFalse();
        caps.SupportedExchanges.Should().NotBeNull().And.BeEmpty();
        caps.SupportedBarIntervals.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void ProviderCapabilities_ShouldAcceptValues()
    {
        var caps = new ProviderCapabilities
        {
            Provider = "alpaca",
            SupportsRealTime = true,
            SupportsHistorical = true,
            SupportsTrades = true,
            SupportsQuotes = true,
            SupportsDepth = false,
            SupportedExchanges = new List<string> { "NYSE", "NASDAQ" },
            MaxSymbolsPerSubscription = 500
        };

        caps.SupportsRealTime.Should().BeTrue();
        caps.SupportedExchanges.Should().HaveCount(2);
        caps.MaxSymbolsPerSubscription.Should().Be(500);
    }

    // ── SwitchProviderResult ─────────────────────────────────────────

    [Fact]
    public void SwitchProviderResult_ShouldHaveDefaults()
    {
        var result = new SwitchProviderResult();
        result.Success.Should().BeFalse();
        result.Error.Should().BeNull();
        result.PreviousProvider.Should().BeNull();
        result.NewProvider.Should().BeNull();
    }

    // ── ProviderManagementTestResult ─────────────────────────────────

    [Fact]
    public void ProviderManagementTestResult_ShouldHaveDefaults()
    {
        var result = new ProviderManagementTestResult();
        result.Success.Should().BeFalse();
        result.Provider.Should().BeEmpty();
        result.LatencyMs.Should().Be(0);
        result.Version.Should().BeNull();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void ProviderManagementTestResult_ShouldAcceptValues()
    {
        var result = new ProviderManagementTestResult
        {
            Success = true,
            Provider = "alpaca",
            LatencyMs = 42.5,
            Version = "3.2.1"
        };

        result.Success.Should().BeTrue();
        result.LatencyMs.Should().Be(42.5);
    }

    // ── RateLimitDataPoint ───────────────────────────────────────────

    [Fact]
    public void RateLimitDataPoint_ShouldHaveDefaults()
    {
        var point = new RateLimitDataPoint();
        point.RequestsUsed.Should().Be(0);
        point.UsagePercent.Should().Be(0);
        point.WasThrottled.Should().BeFalse();
    }

    // ── RateLimitHistoryResult ───────────────────────────────────────

    [Fact]
    public void RateLimitHistoryResult_ShouldHaveDefaults()
    {
        var result = new RateLimitHistoryResult();
        result.Success.Should().BeFalse();
        result.Error.Should().BeNull();
        result.History.Should().NotBeNull().And.BeEmpty();
    }

    // ── ProviderCapabilitiesResult ───────────────────────────────────

    [Fact]
    public void ProviderCapabilitiesResult_ShouldHaveDefaults()
    {
        var result = new ProviderCapabilitiesResult();
        result.Success.Should().BeFalse();
        result.Error.Should().BeNull();
        result.Providers.Should().NotBeNull().And.BeEmpty();
    }
}
