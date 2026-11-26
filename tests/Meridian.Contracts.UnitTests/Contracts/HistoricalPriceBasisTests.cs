using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Domain.Models;
using Xunit;

namespace Meridian.Contracts.UnitTests.Contracts;

public sealed class HistoricalPriceBasisTests
{
    [Fact]
    public void SplitAdjustedPrimaryCannotBeConvertedToRawEvenWithDuplicateAdjustedFields()
    {
        var bar = new AdjustedHistoricalBar(
            "SPLIT", new DateOnly(2026, 1, 2), 49m, 51m, 48m, 50m, 2_000,
            Source: "polygon", AdjustedOpen: 49m, AdjustedHigh: 51m,
            AdjustedLow: 48m, AdjustedClose: 50m,
            PriceBasis: HistoricalPriceBasis.SplitAdjusted,
            AdjustedPriceBasis: HistoricalPriceBasis.SplitAdjusted);

        Assert.Throws<InvalidOperationException>(() => bar.ToHistoricalBar(preferAdjusted: false));
        Assert.Throws<InvalidOperationException>(() => bar.ToHistoricalBar(HistoricalPriceBasis.Raw));
        var research = bar.ToHistoricalBar();
        Assert.Equal(50m, research.Close);
        Assert.Equal(HistoricalPriceBasis.SplitAdjusted, research.PriceBasis);
        Assert.True(research.IsAdjusted);
    }

    [Fact]
    public void SplitAndDividendHistorySelectsCompleteRawOrFullyAdjustedSet()
    {
        var bar = CompleteBar();
        var raw = bar.ToHistoricalBar(preferAdjusted: false);
        var adjusted = bar.ToHistoricalBar(HistoricalPriceBasis.FullyAdjusted);

        Assert.Equal((98m, 102m, 96m, 100m), (raw.Open, raw.High, raw.Low, raw.Close));
        Assert.Equal((48.02m, 49.98m, 47.04m, 49m),
            (adjusted.Open, adjusted.High, adjusted.Low, adjusted.Close));
        Assert.Equal(HistoricalPriceBasis.Raw, raw.PriceBasis);
        Assert.False(raw.IsAdjusted);
        Assert.Equal(HistoricalPriceBasis.FullyAdjusted, adjusted.PriceBasis);
        Assert.Throws<InvalidOperationException>(() => bar.ToHistoricalBar(HistoricalPriceBasis.SplitAdjusted));
        Assert.Throws<InvalidOperationException>(() => bar.ToHistoricalBar(HistoricalPriceBasis.DividendAdjusted));
    }

    [Fact]
    public void DividendOnlyPricesCannotSatisfyRawOrSplitAdjustedRequest()
    {
        var bar = new AdjustedHistoricalBar("DIV", new DateOnly(2026, 1, 2), 97m, 100m, 96m, 99m, 1_000,
            PriceBasis: HistoricalPriceBasis.DividendAdjusted);

        Assert.Equal(HistoricalPriceBasis.DividendAdjusted, bar.ToHistoricalBar().PriceBasis);
        Assert.Throws<InvalidOperationException>(() => bar.ToHistoricalBar(HistoricalPriceBasis.Raw));
        Assert.Throws<InvalidOperationException>(() => bar.ToHistoricalBar(HistoricalPriceBasis.SplitAdjusted));
    }

    public static IEnumerable<object[]> PartialSets => Enumerable.Range(1, 14).Select(mask => new object[] { mask });

    [Theory]
    [MemberData(nameof(PartialSets))]
    public void EveryPartialAdjustedSetIsRejectedWithoutBorrowingPrimaryComponents(int presentMask)
    {
        var complete = CompleteBar();
        var bar = complete with
        {
            AdjustedOpen = (presentMask & 1) != 0 ? complete.AdjustedOpen : null,
            AdjustedHigh = (presentMask & 2) != 0 ? complete.AdjustedHigh : null,
            AdjustedLow = (presentMask & 4) != 0 ? complete.AdjustedLow : null,
            AdjustedClose = (presentMask & 8) != 0 ? complete.AdjustedClose : null
        };

        Assert.Throws<InvalidOperationException>(() => bar.ToHistoricalBar());
        Assert.Throws<InvalidOperationException>(() => bar.ToHistoricalBar(HistoricalPriceBasis.FullyAdjusted));
        Assert.Equal(100m, bar.ToHistoricalBar(HistoricalPriceBasis.Raw).Close);
    }

    [Fact]
    public void UndeclaredSetsRemainUnknownRegardlessOfAdjustedValuesOrRatios()
    {
        var bar = CompleteBar() with
        {
            PriceBasis = HistoricalPriceBasis.Unknown,
            AdjustedPriceBasis = HistoricalPriceBasis.Unknown,
            SplitFactor = 0.49m
        };

        var projected = bar.ToHistoricalBar();
        Assert.Equal(HistoricalPriceBasis.Unknown, projected.PriceBasis);
        Assert.Null(projected.IsAdjusted);
        Assert.Throws<InvalidOperationException>(() => bar.ToHistoricalBar(preferAdjusted: false));
    }

    [Theory]
    [InlineData(HistoricalPriceBasis.Unknown, null)]
    [InlineData(HistoricalPriceBasis.Raw, false)]
    [InlineData(HistoricalPriceBasis.SplitAdjusted, true)]
    [InlineData(HistoricalPriceBasis.DividendAdjusted, true)]
    [InlineData(HistoricalPriceBasis.FullyAdjusted, true)]
    public void ExplicitBasisSurvivesJsonRoundTrip(HistoricalPriceBasis basis, bool? expectedAdjusted)
    {
        var bar = new HistoricalBar("SAME", new DateOnly(2026, 1, 2), 100m, 101m, 99m, 100m, 1_000,
            PriceBasis: basis);

        var json = JsonSerializer.Serialize(bar);
        var restored = JsonSerializer.Deserialize<HistoricalBar>(json)!;
        Assert.Equal(basis, restored.PriceBasis);
        Assert.Equal(expectedAdjusted, restored.IsAdjusted);
        Assert.Equal(100m, restored.Close);
        if (basis == HistoricalPriceBasis.Unknown)
            Assert.DoesNotContain("PriceBasis", json);
        else
            Assert.Contains($"\"PriceBasis\":\"{basis}\"", json);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("null", null)]
    public void LegacyAdjustmentFlagDoesNotEstablishPreciseBasis(string flag, bool? expectedFlag)
    {
        var json = "{\"Symbol\":\"LEGACY\",\"SessionDate\":\"2026-01-02\",\"Open\":100," +
            "\"High\":101,\"Low\":99,\"Close\":100,\"Volume\":1000,\"IsAdjusted\":" + flag + "}";

        var restored = JsonSerializer.Deserialize<HistoricalBar>(json)!;
        Assert.Equal(HistoricalPriceBasis.Unknown, restored.PriceBasis);
        Assert.Equal(expectedFlag, restored.IsAdjusted);
        Assert.DoesNotContain("PriceBasis", JsonSerializer.Serialize(restored));
    }

    [Theory]
    [InlineData(HistoricalPriceBasis.Raw, true)]
    [InlineData(HistoricalPriceBasis.SplitAdjusted, false)]
    [InlineData(HistoricalPriceBasis.DividendAdjusted, false)]
    [InlineData(HistoricalPriceBasis.FullyAdjusted, false)]
    public void ContradictoryLegacyFlagIsRejected(HistoricalPriceBasis basis, bool adjusted)
    {
        Assert.Throws<ArgumentException>(() => new HistoricalBar(
            "BAD", new DateOnly(2026, 1, 2), 100m, 101m, 99m, 100m, 1_000,
            IsAdjusted: adjusted, PriceBasis: basis));
    }

    [Fact]
    public void UnknownOrInvalidBasisCannotBeRequested()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CompleteBar().ToHistoricalBar(HistoricalPriceBasis.Unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() => CompleteBar().ToHistoricalBar((HistoricalPriceBasis)999));
    }

    [Theory]
    [InlineData("Unknown, Raw")]
    [InlineData("Raw, SplitAdjusted")]
    [InlineData("SplitAdjusted, DividendAdjusted")]
    [InlineData("1")]
    [InlineData("999")]
    [InlineData("")]
    public void MalformedBasisNameCannotBecomeRawOrAnotherDefinedBasis(string basis)
    {
        Assert.False(HistoricalPriceBasisValues.TryParse(basis, out _));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<HistoricalPriceBasis>(JsonSerializer.Serialize(basis)));
    }

    [Fact]
    public void GlobalEnumConverterCannotOverrideStrictBarBasisParsing()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        var raw = new HistoricalBar("AAPL", new DateOnly(2026, 1, 2), 98m, 102m, 96m, 100m, 1_000,
            PriceBasis: HistoricalPriceBasis.Raw);
        var json = JsonSerializer.Serialize(raw, options).Replace("\"PriceBasis\":\"Raw\"",
            "\"PriceBasis\":\"Unknown, Raw\"", StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<HistoricalBar>(json, options));
    }

    [Theory]
    [InlineData("PriceBasis", "Raw", "Unknown, Raw")]
    [InlineData("AdjustedPriceBasis", "FullyAdjusted", "Unknown, FullyAdjusted")]
    public void GlobalEnumConverterCannotOverrideEitherAdjustedBarSet(string field, string original, string malformed)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        var json = JsonSerializer.Serialize(CompleteBar(), options).Replace(
            $"\"{field}\":\"{original}\"", $"\"{field}\":\"{malformed}\"", StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AdjustedHistoricalBar>(json, options));
    }

    private static AdjustedHistoricalBar CompleteBar() => new(
        "SPLIT-DIV", new DateOnly(2026, 1, 2), 98m, 102m, 96m, 100m, 1_000,
        Source: "fixture", AdjustedOpen: 48.02m, AdjustedHigh: 49.98m,
        AdjustedLow: 47.04m, AdjustedClose: 49m, AdjustedVolume: 2_000,
        PriceBasis: HistoricalPriceBasis.Raw, AdjustedPriceBasis: HistoricalPriceBasis.FullyAdjusted);
}
