using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="SymbolManagementService"/> and its associated DTO models.
/// </summary>
public sealed class SymbolManagementServiceTests
{
    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnNonNull()
    {
        var instance = SymbolManagementService.Instance;
        instance.Should().NotBeNull();
    }

    [Fact]
    public void Instance_ShouldReturnSameSingleton()
    {
        var a = SymbolManagementService.Instance;
        var b = SymbolManagementService.Instance;
        a.Should().BeSameAs(b);
    }

    // ── SymbolListResult model ──────────────────────────────────────

    [Fact]
    public void SymbolListResult_DefaultValues_ShouldBeCorrect()
    {
        var result = new SymbolListResult();

        result.Success.Should().BeFalse();
        result.Error.Should().BeNull();
        result.Symbols.Should().NotBeNull().And.BeEmpty();
        result.TotalCount.Should().Be(0);
        result.FromLocalConfig.Should().BeFalse();
    }

    // ── SymbolInfo model ────────────────────────────────────────────

    [Fact]
    public void SymbolInfo_DefaultValues_ShouldBeCorrect()
    {
        var info = new SymbolInfo();

        info.Symbol.Should().BeEmpty();
        info.SubscribeTrades.Should().BeFalse();
        info.SubscribeDepth.Should().BeFalse();
        info.DepthLevels.Should().Be(0);
        info.Exchange.Should().Be("SMART");
        info.SecurityType.Should().Be("STK");
        info.Currency.Should().Be("USD");
        info.LocalSymbol.Should().BeNull();
        info.PrimaryExchange.Should().BeNull();
        info.IsMonitored.Should().BeFalse();
        info.HasArchivedData.Should().BeFalse();
        info.LastTradeTime.Should().BeNull();
        info.TotalTrades.Should().Be(0);
        info.TotalQuotes.Should().Be(0);
    }

    // ── SymbolDetailedStatus model ──────────────────────────────────

    [Fact]
    public void SymbolDetailedStatus_DefaultValues_ShouldBeCorrect()
    {
        var status = new SymbolDetailedStatus();

        status.Symbol.Should().BeEmpty();
        status.Error.Should().BeNull();
        status.IsConfigured.Should().BeFalse();
        status.IsMonitored.Should().BeFalse();
        status.TradesSubscribed.Should().BeFalse();
        status.DepthSubscribed.Should().BeFalse();
        status.DepthLevels.Should().Be(0);
        status.FirstSeenTime.Should().BeNull();
        status.LastTradeTime.Should().BeNull();
        status.LastQuoteTime.Should().BeNull();
        status.TotalTradesReceived.Should().Be(0);
        status.TotalQuotesReceived.Should().Be(0);
        status.TotalDepthUpdates.Should().Be(0);
        status.HasArchivedData.Should().BeFalse();
        status.DataQualityScore.Should().Be(0);
        status.SequenceGaps.Should().Be(0);
        status.IntegrityIssues.Should().Be(0);
        status.ActiveProvider.Should().BeNull();
    }

    // ── SymbolOperationResult model ─────────────────────────────────

    [Fact]
    public void SymbolOperationResult_DefaultValues_ShouldBeCorrect()
    {
        var result = new SymbolOperationResult();

        result.Success.Should().BeFalse();
        result.Symbol.Should().BeEmpty();
        result.Message.Should().BeNull();
        result.Error.Should().BeNull();
    }

    // ── BulkSymbolOperationResult model ─────────────────────────────

    [Fact]
    public void BulkSymbolOperationResult_DefaultValues_ShouldBeCorrect()
    {
        var result = new BulkSymbolOperationResult();

        result.Success.Should().BeFalse();
        result.Error.Should().BeNull();
        result.Message.Should().BeNull();
        result.SuccessfulSymbols.Should().NotBeNull().And.BeEmpty();
        result.FailedSymbols.Should().NotBeNull().And.BeEmpty();
    }

    // ── SymbolStatistics model ──────────────────────────────────────

    [Fact]
    public void SymbolStatistics_DefaultValues_ShouldBeCorrect()
    {
        var stats = new SymbolStatistics();

        stats.TotalConfigured.Should().Be(0);
        stats.TotalMonitored.Should().Be(0);
        stats.TotalWithArchivedData.Should().Be(0);
        stats.TradesEnabled.Should().Be(0);
        stats.DepthEnabled.Should().Be(0);
        stats.TotalTradesCollected.Should().Be(0);
        stats.TotalQuotesCollected.Should().Be(0);
        stats.AverageTradesPerSymbol.Should().Be(0);
        stats.LastUpdateTime.Should().BeNull();
    }

    // ── SymbolValidationResult model ────────────────────────────────

    [Fact]
    public void SymbolValidationResult_DefaultValues_ShouldBeCorrect()
    {
        var result = new SymbolValidationResult();

        result.Symbol.Should().BeEmpty();
        result.IsValid.Should().BeFalse();
        result.Error.Should().BeNull();
        result.NormalizedSymbol.Should().BeNull();
        result.SuggestedExchange.Should().BeNull();
        result.SecurityType.Should().BeNull();
        result.IsAvailableForTrading.Should().BeFalse();
    }

    // ── SymbolSearchResultItem model ────────────────────────────────

    [Fact]
    public void SymbolSearchResultItem_DisplayText_WithName_ShowsSymbolAndName()
    {
        var item = new SymbolSearchResultItem
        {
            Symbol = "AAPL",
            Name = "Apple Inc."
        };

        item.DisplayText.Should().Be("AAPL - Apple Inc.");
    }

    [Fact]
    public void SymbolSearchResultItem_DisplayText_WithEmptyName_ShowsSymbolOnly()
    {
        var item = new SymbolSearchResultItem
        {
            Symbol = "AAPL",
            Name = string.Empty
        };

        item.DisplayText.Should().Be("AAPL");
    }

    [Fact]
    public void SymbolSearchResultItem_DisplayText_WhenNameEqualsSymbol_ShowsSymbolOnly()
    {
        var item = new SymbolSearchResultItem
        {
            Symbol = "SPY",
            Name = "SPY"
        };

        item.DisplayText.Should().Be("SPY");
    }

    // ── SymbolArchiveInfo model ─────────────────────────────────────

    [Fact]
    public void SymbolArchiveInfo_DefaultValues_ShouldBeCorrect()
    {
        var info = new SymbolArchiveInfo();

        info.Symbol.Should().BeEmpty();
        info.HasData.Should().BeFalse();
        info.FirstDate.Should().BeNull();
        info.LastDate.Should().BeNull();
        info.TotalDays.Should().Be(0);
        info.TotalFiles.Should().Be(0);
        info.TotalSizeBytes.Should().Be(0);
        info.Files.Should().NotBeNull().And.BeEmpty();
    }
}
