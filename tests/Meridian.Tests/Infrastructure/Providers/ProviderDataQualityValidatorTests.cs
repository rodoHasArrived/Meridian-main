using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Resilience;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class ProviderDataQualityValidatorTests
{
    [Fact]
    public void ValidateTrade_ValidTrade_ReturnsNoIssues()
    {
        var now = DateTimeOffset.UtcNow;
        var update = new MarketTradeUpdate(
            Timestamp: now,
            Symbol: "SPY",
            Price: 450.25m,
            Size: 100,
            Aggressor: AggressorSide.Unknown,
            SequenceNumber: 1,
            StreamId: "TEST",
            Venue: "TEST");

        var issues = ProviderDataQualityValidator.ValidateTrade("test", update, now);

        issues.Should().BeEmpty();
    }

    [Fact]
    public void ValidateTrade_NegativePriceAndFutureTimestamp_ReturnsActionableIssues()
    {
        var now = DateTimeOffset.UtcNow;
        var update = new MarketTradeUpdate(
            Timestamp: now.AddMinutes(10),
            Symbol: "SPY",
            Price: -1m,
            Size: 100,
            Aggressor: AggressorSide.Unknown,
            SequenceNumber: 1);

        var issues = ProviderDataQualityValidator.ValidateTrade("alpaca", update, now);

        issues.Should().Contain(issue => issue.FieldPath == "price" && issue.Severity == ProviderDataQualitySeverity.Error);
        issues.Should().Contain(issue => issue.FieldPath == "timestamp" && issue.Message.Contains("future", StringComparison.OrdinalIgnoreCase));
        issues.Should().OnlyContain(issue => issue.ProviderName == "alpaca");
        issues.Should().OnlyContain(issue => !string.IsNullOrWhiteSpace(issue.SuggestedRecovery));
    }

    [Fact]
    public void ValidateQuote_CrossedMarket_ReturnsAskBelowBidIssue()
    {
        var now = DateTimeOffset.UtcNow;
        var update = new MarketQuoteUpdate(
            Timestamp: now,
            Symbol: "AAPL",
            BidPrice: 185.55m,
            BidSize: 500,
            AskPrice: 185.50m,
            AskSize: 300,
            StreamId: "TEST",
            Venue: "TEST");

        var issues = ProviderDataQualityValidator.ValidateQuote("alpaca", update, now);

        issues.Should().ContainSingle(issue => issue.FieldPath == "askPrice");
    }

    [Fact]
    public async Task WebSocketConnectionManager_DisconnectWithoutConnect_ReportsSafeDiagnostics()
    {
        await using var manager = new WebSocketConnectionManager("stub");

        manager.LifecycleState.Should().Be(ProviderConnectionLifecycleState.Configured);
        manager.GetDiagnosticsSnapshot().LastError.Should().BeNull();

        await manager.DisconnectAsync();

        var snapshot = manager.GetDiagnosticsSnapshot();
        snapshot.ProviderName.Should().Be("stub");
        snapshot.LifecycleState.Should().Be(ProviderConnectionLifecycleState.Disconnected);
        snapshot.IsConnected.Should().BeFalse();
        snapshot.IsReconnecting.Should().BeFalse();
        snapshot.LastDisconnectedAt.Should().NotBeNull();
        snapshot.LastError.Should().BeNull();
    }
}
