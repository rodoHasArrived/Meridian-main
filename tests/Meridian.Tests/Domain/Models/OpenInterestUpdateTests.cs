using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// Unit tests for the OpenInterestUpdate domain model.
/// Tests constructor validation, OI change, and volume-to-OI ratio.
/// </summary>
public sealed class OpenInterestUpdateTests
{
    private static readonly OptionContractSpec CallContract = new("AAPL", 150m, new DateOnly(2026, 3, 21), OptionRight.Call);

    [Fact]
    public void Constructor_WithValidData_CreatesSuccessfully()
    {
        var oi = CreateUpdate();

        oi.Symbol.Should().Be("AAPL260321C00150000");
        oi.OpenInterest.Should().Be(5000);
        oi.Volume.Should().Be(1200);
        oi.Contract.Should().Be(CallContract);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithInvalidSymbol_ThrowsArgumentException(string? symbol)
    {
        var act = () => new OpenInterestUpdate(DateTimeOffset.UtcNow, symbol!, CallContract, 5000, 1200);

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("Symbol");
    }

    [Fact]
    public void Constructor_WithNullContract_ThrowsArgumentNullException()
    {
        var act = () => new OpenInterestUpdate(DateTimeOffset.UtcNow, "SYM", null!, 5000, 1200);

        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("Contract");
    }

    [Fact]
    public void Constructor_WithNegativeOpenInterest_ThrowsArgumentOutOfRangeException()
    {
        var act = () => CreateUpdate(openInterest: -1);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("OpenInterest");
    }

    [Fact]
    public void Constructor_WithZeroOpenInterest_CreatesSuccessfully()
    {
        // Zero OI is valid (all positions closed)
        var oi = CreateUpdate(openInterest: 0);

        oi.OpenInterest.Should().Be(0);
    }

    [Fact]
    public void Constructor_WithNegativeVolume_ThrowsArgumentOutOfRangeException()
    {
        var act = () => CreateUpdate(volume: -1);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("Volume");
    }

    [Fact]
    public void Constructor_WithZeroVolume_CreatesSuccessfully()
    {
        var oi = CreateUpdate(volume: 0);

        oi.Volume.Should().Be(0);
    }

    [Fact]
    public void OpenInterestChange_WithPreviousOI_CalculatesPositiveChange()
    {
        var oi = CreateUpdate(openInterest: 5500, previousOI: 5000);

        oi.OpenInterestChange.Should().Be(500);
    }

    [Fact]
    public void OpenInterestChange_WithPreviousOI_CalculatesNegativeChange()
    {
        var oi = CreateUpdate(openInterest: 4500, previousOI: 5000);

        oi.OpenInterestChange.Should().Be(-500);
    }

    [Fact]
    public void OpenInterestChange_WithNoPreviousOI_ReturnsNull()
    {
        var oi = CreateUpdate(previousOI: null);

        oi.OpenInterestChange.Should().BeNull();
    }

    [Fact]
    public void OpenInterestChange_WithSamePreviousOI_ReturnsZero()
    {
        var oi = CreateUpdate(openInterest: 5000, previousOI: 5000);

        oi.OpenInterestChange.Should().Be(0);
    }

    [Fact]
    public void VolumeToOpenInterestRatio_CalculatesCorrectly()
    {
        // Volume 1200 / OI 5000 = 0.24
        var oi = CreateUpdate(openInterest: 5000, volume: 1200);

        oi.VolumeToOpenInterestRatio.Should().Be(0.24m);
    }

    [Fact]
    public void VolumeToOpenInterestRatio_WithHighVolume_IndicatesUnusualActivity()
    {
        // Volume exceeds OI - unusual activity indicator
        var oi = CreateUpdate(openInterest: 1000, volume: 5000);

        oi.VolumeToOpenInterestRatio.Should().Be(5.0m);
    }

    [Fact]
    public void VolumeToOpenInterestRatio_WithZeroOI_ReturnsZero()
    {
        var oi = CreateUpdate(openInterest: 0, volume: 100);

        oi.VolumeToOpenInterestRatio.Should().Be(0m);
    }

    [Fact]
    public void VolumeToOpenInterestRatio_WithZeroVolume_ReturnsZero()
    {
        var oi = CreateUpdate(openInterest: 5000, volume: 0);

        oi.VolumeToOpenInterestRatio.Should().Be(0m);
    }

    [Fact]
    public void Constructor_WithOptionalFields_PreservesThem()
    {
        var oi = new OpenInterestUpdate(DateTimeOffset.UtcNow, "SYM", CallContract,
            5000, 1200, PreviousOpenInterest: 4800, SequenceNumber: 42, Source: "POLYGON");

        oi.PreviousOpenInterest.Should().Be(4800);
        oi.SequenceNumber.Should().Be(42);
        oi.Source.Should().Be("POLYGON");
    }

    [Fact]
    public void Equality_WithSameValues_AreEqual()
    {
        var ts = DateTimeOffset.UtcNow;
        var oi1 = new OpenInterestUpdate(ts, "SYM", CallContract, 5000, 1200, SequenceNumber: 1);
        var oi2 = new OpenInterestUpdate(ts, "SYM", CallContract, 5000, 1200, SequenceNumber: 1);

        oi1.Should().Be(oi2);
    }

    private static OpenInterestUpdate CreateUpdate(
        long openInterest = 5000,
        long volume = 1200,
        long? previousOI = null)
        => new(DateTimeOffset.UtcNow, "AAPL260321C00150000", CallContract,
            openInterest, volume, previousOI);
}
