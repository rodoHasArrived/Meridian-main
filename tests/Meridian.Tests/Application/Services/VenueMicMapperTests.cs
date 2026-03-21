using FluentAssertions;
using Meridian.Application.Canonicalization;
using Xunit;

namespace Meridian.Tests.Application.Services;

/// <summary>
/// Tests for <see cref="VenueMicMapper"/>.
/// </summary>
public sealed class VenueMicMapperTests
{
    private static readonly string TestJson = """
    {
        "version": 1,
        "mappings": {
            "ALPACA": {
                "V": "XNAS",
                "P": "ARCX",
                "N": "XNYS",
                "NASDAQ": "XNAS",
                "NYSE_ARCA": "ARCX"
            },
            "POLYGON": {
                "1": "XNYS",
                "3": "ARCX",
                "4": "XNAS",
                "8": "BATS",
                "9": "IEXG"
            },
            "IB": {
                "ISLAND": "XNAS",
                "NYSE": "XNYS",
                "ARCA": "ARCX",
                "SMART": null
            }
        }
    }
    """;

    private readonly VenueMicMapper _mapper = VenueMicMapper.LoadFromJson(TestJson);

    [Fact]
    public void LoadFromJson_SetsVersion()
    {
        _mapper.Version.Should().Be(1);
    }

    [Theory]
    [InlineData("ALPACA", "V", "XNAS")]
    [InlineData("ALPACA", "P", "ARCX")]
    [InlineData("ALPACA", "N", "XNYS")]
    [InlineData("ALPACA", "NASDAQ", "XNAS")]
    [InlineData("POLYGON", "1", "XNYS")]
    [InlineData("POLYGON", "4", "XNAS")]
    [InlineData("POLYGON", "8", "BATS")]
    [InlineData("POLYGON", "9", "IEXG")]
    [InlineData("IB", "ISLAND", "XNAS")]
    [InlineData("IB", "NYSE", "XNYS")]
    [InlineData("IB", "ARCA", "ARCX")]
    public void TryMapVenue_KnownVenues_ReturnsCanonicalMic(string provider, string raw, string expected)
    {
        _mapper.TryMapVenue(raw, provider).Should().Be(expected);
    }

    [Fact]
    public void TryMapVenue_IbSmart_ReturnsNull()
    {
        _mapper.TryMapVenue("SMART", "IB").Should().BeNull();
    }

    [Fact]
    public void TryMapVenue_UnknownVenue_ReturnsNull()
    {
        _mapper.TryMapVenue("UNKNOWN", "ALPACA").Should().BeNull();
    }

    [Fact]
    public void TryMapVenue_UnknownProvider_ReturnsNull()
    {
        _mapper.TryMapVenue("V", "BINANCE").Should().BeNull();
    }

    [Fact]
    public void TryMapVenue_NullVenue_ReturnsNull()
    {
        _mapper.TryMapVenue(null, "ALPACA").Should().BeNull();
    }

    [Fact]
    public void TryMapVenue_EmptyVenue_ReturnsNull()
    {
        _mapper.TryMapVenue("", "ALPACA").Should().BeNull();
    }

    [Fact]
    public void TryMapVenue_ProviderIsCaseInsensitive()
    {
        _mapper.TryMapVenue("V", "alpaca").Should().Be("XNAS");
        _mapper.TryMapVenue("V", "Alpaca").Should().Be("XNAS");
    }

    [Fact]
    public void TryMapVenue_CaseInsensitiveVenueFallback()
    {
        // "island" should match "ISLAND" via uppercase fallback
        _mapper.TryMapVenue("island", "IB").Should().Be("XNAS");
    }

    [Fact]
    public void LoadFromJson_EmptyMappings_ProducesEmptyMapper()
    {
        var mapper = VenueMicMapper.LoadFromJson("""{ "version": 0, "mappings": {} }""");

        mapper.MappingCount.Should().Be(0);
        mapper.TryMapVenue("V", "ALPACA").Should().BeNull();
    }

    [Fact]
    public void CrossProvider_SameExchange_SameCanonicalMic()
    {
        // NASDAQ from all three providers should map to XNAS
        _mapper.TryMapVenue("V", "ALPACA").Should().Be("XNAS");
        _mapper.TryMapVenue("4", "POLYGON").Should().Be("XNAS");
        _mapper.TryMapVenue("ISLAND", "IB").Should().Be("XNAS");
    }

    [Fact]
    public void CrossProvider_NYSE_SameCanonicalMic()
    {
        _mapper.TryMapVenue("N", "ALPACA").Should().Be("XNYS");
        _mapper.TryMapVenue("1", "POLYGON").Should().Be("XNYS");
        _mapper.TryMapVenue("NYSE", "IB").Should().Be("XNYS");
    }
}
