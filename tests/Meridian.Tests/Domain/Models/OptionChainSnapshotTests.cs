using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// Unit tests for the OptionChainSnapshot domain model.
/// Tests constructor validation, ATM strike, and put/call ratios.
/// </summary>
public sealed class OptionChainSnapshotTests
{
    [Fact]
    public void Constructor_WithValidData_CreatesSuccessfully()
    {
        var chain = CreateChain();

        chain.UnderlyingSymbol.Should().Be("AAPL");
        chain.UnderlyingPrice.Should().Be(155m);
        chain.Expiration.Should().Be(new DateOnly(2026, 3, 21));
        chain.InstrumentType.Should().Be(InstrumentType.EquityOption);
        chain.Strikes.Should().HaveCount(3);
        chain.Calls.Should().HaveCount(3);
        chain.Puts.Should().HaveCount(3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithInvalidSymbol_ThrowsArgumentException(string? symbol)
    {
        var act = () => new OptionChainSnapshot(DateTimeOffset.UtcNow, symbol!, 155m,
            new DateOnly(2026, 3, 21), new[] { 150m }, CreateCalls(1), CreatePuts(1));

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("UnderlyingSymbol");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithInvalidPrice_ThrowsArgumentOutOfRangeException(decimal price)
    {
        var act = () => new OptionChainSnapshot(DateTimeOffset.UtcNow, "AAPL", price,
            new DateOnly(2026, 3, 21), new[] { 150m }, CreateCalls(1), CreatePuts(1));

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("UnderlyingPrice");
    }

    [Fact]
    public void Constructor_WithNullStrikes_ThrowsArgumentNullException()
    {
        var act = () => new OptionChainSnapshot(DateTimeOffset.UtcNow, "AAPL", 155m,
            new DateOnly(2026, 3, 21), null!, CreateCalls(1), CreatePuts(1));

        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("Strikes");
    }

    [Fact]
    public void Constructor_WithNullCalls_ThrowsArgumentNullException()
    {
        var act = () => new OptionChainSnapshot(DateTimeOffset.UtcNow, "AAPL", 155m,
            new DateOnly(2026, 3, 21), new[] { 150m }, null!, CreatePuts(1));

        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("Calls");
    }

    [Fact]
    public void Constructor_WithNullPuts_ThrowsArgumentNullException()
    {
        var act = () => new OptionChainSnapshot(DateTimeOffset.UtcNow, "AAPL", 155m,
            new DateOnly(2026, 3, 21), new[] { 150m }, CreateCalls(1), null!);

        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("Puts");
    }

    [Theory]
    [InlineData(InstrumentType.Equity)]
    [InlineData(InstrumentType.Future)]
    public void Constructor_WithInvalidInstrumentType_ThrowsArgumentException(InstrumentType type)
    {
        var act = () => new OptionChainSnapshot(DateTimeOffset.UtcNow, "AAPL", 155m,
            new DateOnly(2026, 3, 21), new[] { 150m }, CreateCalls(1), CreatePuts(1),
            InstrumentType: type);

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("InstrumentType");
    }

    [Fact]
    public void Constructor_WithIndexOption_CreatesSuccessfully()
    {
        var chain = new OptionChainSnapshot(DateTimeOffset.UtcNow, "SPX", 5800m,
            new DateOnly(2026, 3, 21), new[] { 5800m },
            CreateCalls(1, "SPX", 5800m, 5800m),
            CreatePuts(1, "SPX", 5800m, 5800m),
            InstrumentType: InstrumentType.IndexOption);

        chain.InstrumentType.Should().Be(InstrumentType.IndexOption);
    }

    [Fact]
    public void DaysToExpiration_CalculatesCorrectly()
    {
        var ts = new DateTimeOffset(2026, 3, 1, 14, 0, 0, TimeSpan.Zero);
        var chain = new OptionChainSnapshot(ts, "AAPL", 155m,
            new DateOnly(2026, 3, 21), new[] { 150m }, CreateCalls(1), CreatePuts(1));

        chain.DaysToExpiration.Should().Be(20);
    }

    [Fact]
    public void TotalContracts_SumOfCallsAndPuts()
    {
        var chain = CreateChain();

        chain.TotalContracts.Should().Be(6); // 3 calls + 3 puts
    }

    [Fact]
    public void AtTheMoneyStrike_ReturnsClosestStrike()
    {
        // Strikes: 145, 150, 155. Underlying: 155 => ATM strike is 155
        var chain = CreateChain(underlyingPrice: 155m);

        chain.AtTheMoneyStrike.Should().Be(155m);
    }

    [Fact]
    public void AtTheMoneyStrike_WithUnderlyingBetweenStrikes_ReturnsClosest()
    {
        // Strikes: 145, 150, 155. Underlying: 152 => closest is 150
        var chain = CreateChain(underlyingPrice: 152m);

        chain.AtTheMoneyStrike.Should().Be(150m);
    }

    [Fact]
    public void AtTheMoneyStrike_WithEmptyStrikes_ReturnsNull()
    {
        var chain = new OptionChainSnapshot(DateTimeOffset.UtcNow, "AAPL", 155m,
            new DateOnly(2026, 3, 21), Array.Empty<decimal>(),
            Array.Empty<OptionQuote>(), Array.Empty<OptionQuote>());

        chain.AtTheMoneyStrike.Should().BeNull();
    }

    [Fact]
    public void PutCallVolumeRatio_WithVolume_CalculatesCorrectly()
    {
        // Calls with volume 100 each (3 calls = 300), Puts with volume 200 each (3 puts = 600)
        var calls = CreateCalls(3, volume: 100);
        var puts = CreatePuts(3, volume: 200);

        var chain = new OptionChainSnapshot(DateTimeOffset.UtcNow, "AAPL", 155m,
            new DateOnly(2026, 3, 21), new[] { 145m, 150m, 155m }, calls, puts);

        // 600 / 300 = 2.0
        chain.PutCallVolumeRatio.Should().Be(2.0m);
    }

    [Fact]
    public void PutCallVolumeRatio_WithZeroCallVolume_ReturnsNull()
    {
        var calls = CreateCalls(3, volume: 0);
        var puts = CreatePuts(3, volume: 200);

        var chain = new OptionChainSnapshot(DateTimeOffset.UtcNow, "AAPL", 155m,
            new DateOnly(2026, 3, 21), new[] { 145m, 150m, 155m }, calls, puts);

        chain.PutCallVolumeRatio.Should().BeNull();
    }

    [Fact]
    public void PutCallOpenInterestRatio_WithOI_CalculatesCorrectly()
    {
        var calls = CreateCalls(3, oi: 1000);
        var puts = CreatePuts(3, oi: 500);

        var chain = new OptionChainSnapshot(DateTimeOffset.UtcNow, "AAPL", 155m,
            new DateOnly(2026, 3, 21), new[] { 145m, 150m, 155m }, calls, puts);

        // 1500 / 3000 = 0.5
        chain.PutCallOpenInterestRatio.Should().Be(0.5m);
    }

    [Fact]
    public void PutCallOpenInterestRatio_WithZeroCallOI_ReturnsNull()
    {
        var calls = CreateCalls(3, oi: 0);
        var puts = CreatePuts(3, oi: 500);

        var chain = new OptionChainSnapshot(DateTimeOffset.UtcNow, "AAPL", 155m,
            new DateOnly(2026, 3, 21), new[] { 145m, 150m, 155m }, calls, puts);

        chain.PutCallOpenInterestRatio.Should().BeNull();
    }

    private static OptionChainSnapshot CreateChain(decimal underlyingPrice = 155m)
    {
        var strikes = new[] { 145m, 150m, 155m };
        return new OptionChainSnapshot(
            DateTimeOffset.UtcNow, "AAPL", underlyingPrice,
            new DateOnly(2026, 3, 21), strikes,
            CreateCalls(3), CreatePuts(3));
    }

    private static IReadOnlyList<OptionQuote> CreateCalls(int count,
        string underlying = "AAPL", decimal underlyingPrice = 155m, decimal baseStrike = 145m,
        long? volume = null, long? oi = null)
    {
        var list = new List<OptionQuote>();
        for (int i = 0; i < count; i++)
        {
            var strike = baseStrike + (i * 5m);
            var contract = new OptionContractSpec(underlying, strike, new DateOnly(2026, 3, 21), OptionRight.Call);
            list.Add(new OptionQuote(DateTimeOffset.UtcNow, $"{underlying}C{strike}",
                contract, 5m, 10, 5.20m, 15, underlyingPrice,
                Volume: volume, OpenInterest: oi));
        }
        return list;
    }

    private static IReadOnlyList<OptionQuote> CreatePuts(int count,
        string underlying = "AAPL", decimal underlyingPrice = 155m, decimal baseStrike = 145m,
        long? volume = null, long? oi = null)
    {
        var list = new List<OptionQuote>();
        for (int i = 0; i < count; i++)
        {
            var strike = baseStrike + (i * 5m);
            var contract = new OptionContractSpec(underlying, strike, new DateOnly(2026, 3, 21), OptionRight.Put);
            list.Add(new OptionQuote(DateTimeOffset.UtcNow, $"{underlying}P{strike}",
                contract, 3m, 10, 3.20m, 15, underlyingPrice,
                Volume: volume, OpenInterest: oi));
        }
        return list;
    }
}
