using FluentAssertions;
using Meridian.Application.Canonicalization;
using Meridian.Contracts.Domain.Enums;
using Xunit;

namespace Meridian.Tests.Application.Services;

/// <summary>
/// Tests for <see cref="ConditionCodeMapper"/>.
/// </summary>
public sealed class ConditionCodeMapperTests
{
    private static readonly string TestJson = """
    {
        "version": 1,
        "mappings": {
            "ALPACA": {
                "@": "Regular",
                "T": "FormT_ExtendedHours",
                "I": "Intermarket_Sweep",
                "U": "OddLot"
            },
            "POLYGON": {
                "0": "Regular",
                "12": "FormT_ExtendedHours",
                "37": "OddLot",
                "29": "SellerInitiated"
            },
            "IB": {
                "RegularTrade": "Regular",
                "OddLot": "OddLot"
            }
        }
    }
    """;

    private readonly ConditionCodeMapper _mapper = ConditionCodeMapper.LoadFromJson(TestJson);

    [Fact]
    public void LoadFromJson_SetsVersion()
    {
        _mapper.Version.Should().Be(1);
    }

    [Fact]
    public void LoadFromJson_CountsMappings()
    {
        _mapper.MappingCount.Should().Be(10);
    }

    [Theory]
    [InlineData("ALPACA", "@", CanonicalTradeCondition.Regular)]
    [InlineData("ALPACA", "T", CanonicalTradeCondition.FormT_ExtendedHours)]
    [InlineData("ALPACA", "I", CanonicalTradeCondition.Intermarket_Sweep)]
    [InlineData("POLYGON", "0", CanonicalTradeCondition.Regular)]
    [InlineData("POLYGON", "12", CanonicalTradeCondition.FormT_ExtendedHours)]
    [InlineData("POLYGON", "37", CanonicalTradeCondition.OddLot)]
    [InlineData("POLYGON", "29", CanonicalTradeCondition.SellerInitiated)]
    [InlineData("IB", "RegularTrade", CanonicalTradeCondition.Regular)]
    [InlineData("IB", "OddLot", CanonicalTradeCondition.OddLot)]
    public void MapSingle_KnownCodes_ReturnsCanonical(string provider, string raw, CanonicalTradeCondition expected)
    {
        _mapper.MapSingle(provider, raw).Should().Be(expected);
    }

    [Fact]
    public void MapSingle_UnknownCode_ReturnsUnknown()
    {
        _mapper.MapSingle("ALPACA", "ZZZ").Should().Be(CanonicalTradeCondition.Unknown);
    }

    [Fact]
    public void MapSingle_UnknownProvider_ReturnsUnknown()
    {
        _mapper.MapSingle("BINANCE", "@").Should().Be(CanonicalTradeCondition.Unknown);
    }

    [Fact]
    public void MapSingle_ProviderIsCaseInsensitive()
    {
        _mapper.MapSingle("alpaca", "@").Should().Be(CanonicalTradeCondition.Regular);
        _mapper.MapSingle("Alpaca", "@").Should().Be(CanonicalTradeCondition.Regular);
    }

    [Fact]
    public void MapConditions_NullInput_ReturnsEmpty()
    {
        var (canonical, raw) = _mapper.MapConditions("ALPACA", null);
        canonical.Should().BeEmpty();
        raw.Should().BeEmpty();
    }

    [Fact]
    public void MapConditions_EmptyInput_ReturnsEmpty()
    {
        var (canonical, raw) = _mapper.MapConditions("ALPACA", []);
        canonical.Should().BeEmpty();
        raw.Should().BeEmpty();
    }

    [Fact]
    public void MapConditions_MixedKnownAndUnknown()
    {
        var (canonical, raw) = _mapper.MapConditions("ALPACA", ["@", "T", "ZZZ"]);

        canonical.Should().HaveCount(3);
        canonical[0].Should().Be(CanonicalTradeCondition.Regular);
        canonical[1].Should().Be(CanonicalTradeCondition.FormT_ExtendedHours);
        canonical[2].Should().Be(CanonicalTradeCondition.Unknown);
        raw.Should().Equal("@", "T", "ZZZ");
    }

    [Fact]
    public void MapConditions_PreservesRawCodes()
    {
        var input = new[] { "0", "12" };
        var (_, raw) = _mapper.MapConditions("POLYGON", input);

        raw.Should().BeSameAs(input, "raw array should be the same reference for zero-allocation");
    }

    [Fact]
    public void LoadFromJson_EmptyMappings_ProducesEmptyMapper()
    {
        var mapper = ConditionCodeMapper.LoadFromJson("""{ "version": 0, "mappings": {} }""");

        mapper.MappingCount.Should().Be(0);
        mapper.MapSingle("ALPACA", "@").Should().Be(CanonicalTradeCondition.Unknown);
    }

    [Fact]
    public void LoadFromJson_InvalidEnumValue_SkipsEntry()
    {
        var mapper = ConditionCodeMapper.LoadFromJson("""
        {
            "version": 1,
            "mappings": {
                "TEST": { "A": "NotARealCondition", "B": "Regular" }
            }
        }
        """);

        mapper.MappingCount.Should().Be(1);
        mapper.MapSingle("TEST", "A").Should().Be(CanonicalTradeCondition.Unknown);
        mapper.MapSingle("TEST", "B").Should().Be(CanonicalTradeCondition.Regular);
    }
}
