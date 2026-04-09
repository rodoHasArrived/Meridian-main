using System;
using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.StockSharp.Converters;
using Xunit;

namespace Meridian.Tests.Infrastructure.Adapters;

/// <summary>
/// Edge-case tests for <see cref="MessageConverter"/> and <see cref="SecurityConverter"/>.
///
/// Two complementary coverage areas:
/// <list type="bullet">
///   <item><description>
///     <b>Stub contracts (non-STOCKSHARP build):</b>
///     When the StockSharp packages are absent all conversion methods compile to stubs that
///     throw <see cref="NotSupportedException"/>.  Tests here assert that the stubs throw the
///     right exception type so that callers can catch it rather than receiving a cryptic
///     <see cref="NullReferenceException"/> or <see cref="MissingMethodException"/> at runtime.
///   </description></item>
///   <item><description>
///     <b>Domain model edge cases:</b>
///     The <see cref="MarketTradeUpdate"/>, <see cref="MarketDepthUpdate"/>, and
///     <see cref="MarketQuoteUpdate"/> domain records used by the StockSharp client are pure
///     value types with no external dependencies.  These tests lock the behaviour of extreme
///     and boundary inputs (zero/negative values, null optional fields, epoch timestamps) so
///     that changes to the domain models do not silently alter StockSharp's output.
///   </description></item>
/// </list>
/// </summary>
public sealed class StockSharpConverterEdgeCaseTests
{
    // -------------------------------------------------------------------------
    // MessageConverter stub contracts (non-STOCKSHARP build)
    // -------------------------------------------------------------------------

    [Fact]
    public void MessageConverter_ToTrade_Stub_ThrowsNotSupportedException()
    {
        var act = () => MessageConverter.ToTrade(new object(), "AAPL");
        act.Should().Throw<NotSupportedException>(
            because: "non-STOCKSHARP builds must throw NotSupportedException with a clear install instruction");
    }

    [Fact]
    public void MessageConverter_ToLOBSnapshot_Stub_ThrowsNotSupportedException()
    {
        var act = () => MessageConverter.ToLOBSnapshot(new object(), "AAPL");
        act.Should().Throw<NotSupportedException>(
            because: "non-STOCKSHARP builds must surface the missing-package root cause");
    }

    [Fact]
    public void MessageConverter_ToBboQuote_Stub_ThrowsNotSupportedException()
    {
        var act = () => MessageConverter.ToBboQuote(new object(), "AAPL");
        act.Should().Throw<NotSupportedException>(
            because: "non-STOCKSHARP builds must surface the missing-package root cause");
    }

    [Fact]
    public void MessageConverter_ToHistoricalBar_Stub_ThrowsNotSupportedException()
    {
        var act = () => MessageConverter.ToHistoricalBar(new object(), "AAPL");
        act.Should().Throw<NotSupportedException>(
            because: "non-STOCKSHARP builds must surface the missing-package root cause");
    }

    [Fact]
    public void MessageConverter_Stub_ExceptionMessage_ContainsInstallHint()
    {
        var ex = Assert.Throws<NotSupportedException>(() => MessageConverter.ToTrade(new object(), "AAPL"));
        ex.Message.Should().Contain("StockSharp",
            because: "the error message should guide the operator to the correct NuGet package");
    }

    // -------------------------------------------------------------------------
    // SecurityConverter stub contracts (non-STOCKSHARP build)
    // -------------------------------------------------------------------------

    [Fact]
    public void SecurityConverter_ToSecurity_Stub_ThrowsNotSupportedException()
    {
        var cfg = new SymbolConfig("AAPL");
        var act = () => SecurityConverter.ToSecurity(cfg);
        act.Should().Throw<NotSupportedException>(
            because: "non-STOCKSHARP builds must throw NotSupportedException");
    }

    [Fact]
    public void SecurityConverter_ToSecurityId_Stub_ThrowsNotSupportedException()
    {
        var cfg = new SymbolConfig("AAPL");
        var act = () => SecurityConverter.ToSecurityId(cfg);
        act.Should().Throw<NotSupportedException>(
            because: "non-STOCKSHARP builds must throw NotSupportedException");
    }

    // -------------------------------------------------------------------------
    // MarketTradeUpdate domain model edge cases
    // -------------------------------------------------------------------------

    [Fact]
    public void MarketTradeUpdate_ZeroSize_IsValid()
    {
        var update = new MarketTradeUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            Price: 213.45m,
            Size: 0,
            Aggressor: AggressorSide.Unknown,
            SequenceNumber: 0);

        update.Size.Should().Be(0,
            because: "zero-lot trades are legal in some market venues");
    }

    [Fact]
    public void MarketTradeUpdate_SubPennyPrice_Preserved()
    {
        const decimal subPenny = 213.4501m;
        var update = new MarketTradeUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            Price: subPenny,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: 1);

        update.Price.Should().Be(subPenny,
            because: "sub-penny prices must not be rounded or truncated by the domain record");
    }

    [Fact]
    public void MarketTradeUpdate_EpochTimestamp_Preserved()
    {
        var epoch = DateTimeOffset.UnixEpoch;
        var update = new MarketTradeUpdate(
            Timestamp: epoch,
            Symbol: "SPY",
            Price: 450m,
            Size: 1,
            Aggressor: AggressorSide.Unknown,
            SequenceNumber: 0);

        update.Timestamp.Should().Be(epoch,
            because: "epoch timestamps must not be normalised or defaulted");
    }

    [Fact]
    public void MarketTradeUpdate_NullOptionalFields_DefaultToNull()
    {
        var update = new MarketTradeUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            Price: 100m,
            Size: 1,
            Aggressor: AggressorSide.Unknown,
            SequenceNumber: 0);

        update.StreamId.Should().BeNull();
        update.Venue.Should().BeNull();
    }

    [Fact]
    public void MarketTradeUpdate_LargeSize_Preserved()
    {
        const long blockSize = 10_000_000L;
        var update = new MarketTradeUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "QQQ",
            Price: 480m,
            Size: blockSize,
            Aggressor: AggressorSide.Unknown,
            SequenceNumber: long.MaxValue);

        update.Size.Should().Be(blockSize);
        update.SequenceNumber.Should().Be(long.MaxValue);
    }

    // -------------------------------------------------------------------------
    // MarketDepthUpdate domain model edge cases
    // -------------------------------------------------------------------------

    [Fact]
    public void MarketDepthUpdate_ZeroPrice_IsValid()
    {
        var update = new MarketDepthUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            Position: 0,
            Operation: DepthOperation.Delete,
            Side: OrderBookSide.Bid,
            Price: 0m,
            Size: 0m,
            MarketMaker: null,
            SequenceNumber: 0,
            StreamId: "STOCKSHARP");

        update.Price.Should().Be(0m,
            because: "zero-price remove operations are valid when the level is being cancelled");
    }

    [Fact]
    public void MarketDepthUpdate_AllDepthOperations_Representable()
    {
        foreach (var operation in Enum.GetValues<DepthOperation>())
        {
            var update = new MarketDepthUpdate(
                Timestamp: DateTimeOffset.UtcNow,
                Symbol: "SPY",
                Position: 0,
                Operation: operation,
                Side: OrderBookSide.Ask,
                Price: 450m,
                Size: 100m,
                MarketMaker: null,
                SequenceNumber: 0,
                StreamId: null);

            update.Operation.Should().Be(operation,
                because: $"all DepthOperation values must survive a round-trip through MarketDepthUpdate; failed for {operation}");
        }
    }

    [Fact]
    public void MarketDepthUpdate_BothSides_Representable()
    {
        foreach (var side in new[] { OrderBookSide.Bid, OrderBookSide.Ask })
        {
            var update = new MarketDepthUpdate(
                Timestamp: DateTimeOffset.UtcNow,
                Symbol: "AAPL",
                Position: 0,
                Operation: DepthOperation.Update,
                Side: side,
                Price: 213.45m,
                Size: 500m,
                MarketMaker: "MM1",
                SequenceNumber: 1,
                StreamId: "STOCKSHARP");

            update.Side.Should().Be(side);
        }
    }

    // -------------------------------------------------------------------------
    // MarketQuoteUpdate domain model edge cases
    // -------------------------------------------------------------------------

    [Fact]
    public void MarketQuoteUpdate_CrossedMarket_BothSidesPreserved()
    {
        // Crossed markets can occur briefly in fast-moving feeds; the record must
        // not enforce bid <= ask — that is the responsibility of the validator.
        var update = new MarketQuoteUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            BidPrice: 213.50m,
            BidSize: 100,
            AskPrice: 213.40m,  // ask < bid: crossed market
            AskSize: 50,
            SequenceNumber: 42,
            StreamId: "STOCKSHARP",
            Venue: "NASDAQ");

        update.BidPrice.Should().Be(213.50m);
        update.AskPrice.Should().Be(213.40m);
    }

    [Fact]
    public void MarketQuoteUpdate_NullVenueAndSourceId_AcceptedWithoutThrow()
    {
        var act = () => new MarketQuoteUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            BidPrice: 100m,
            BidSize: 1,
            AskPrice: 100.01m,
            AskSize: 1,
            SequenceNumber: null,
            StreamId: null,
            Venue: null);

        act.Should().NotThrow(because: "optional SourceId and Venue fields are permitted to be null");
    }
}
