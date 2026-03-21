using FluentAssertions;
using Meridian.Application.Canonicalization;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Application.Services;

/// <summary>
/// Tests for <see cref="EventCanonicalizer"/>.
/// Covers symbol resolution, venue normalization, condition code mapping,
/// idempotency, determinism, and preservation properties.
/// </summary>
public sealed class EventCanonicalizerTests
{
    private readonly ICanonicalSymbolRegistry _mockRegistry;
    private readonly ConditionCodeMapper _conditionMapper;
    private readonly VenueMicMapper _venueMapper;
    private readonly EventCanonicalizer _canonicalizer;

    public EventCanonicalizerTests()
    {
        _mockRegistry = Substitute.For<ICanonicalSymbolRegistry>();
        _mockRegistry.ResolveToCanonical("AAPL").Returns("AAPL");
        _mockRegistry.ResolveToCanonical("AAPL.US").Returns("AAPL");
        _mockRegistry.ResolveToCanonical("MSFT").Returns("MSFT");
        _mockRegistry.ResolveToCanonical("UNKNOWN_SYM").Returns((string?)null);

        _conditionMapper = ConditionCodeMapper.LoadFromJson("""
        {
            "version": 1,
            "mappings": {
                "ALPACA": { "@": "Regular", "T": "FormT_ExtendedHours", "I": "Intermarket_Sweep" },
                "POLYGON": { "0": "Regular", "12": "FormT_ExtendedHours", "37": "OddLot" }
            }
        }
        """);

        _venueMapper = VenueMicMapper.LoadFromJson("""
        {
            "version": 1,
            "mappings": {
                "ALPACA": { "V": "XNAS", "P": "ARCX", "N": "XNYS" },
                "POLYGON": { "4": "XNAS", "1": "XNYS", "3": "ARCX" },
                "IB": { "ISLAND": "XNAS", "NYSE": "XNYS", "SMART": null }
            }
        }
        """);

        _canonicalizer = new EventCanonicalizer(_mockRegistry, _conditionMapper, _venueMapper);
    }

    private static MarketEvent CreateTradeEvent(
        string symbol,
        string source,
        string? venue = null,
        MarketEventTier tier = MarketEventTier.Raw,
        byte canonVersion = 0)
    {
        var trade = new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Price: 150.0m,
            Size: 100,
            Aggressor: AggressorSide.Unknown,
            SequenceNumber: 1,
            StreamId: source,
            Venue: venue);

        return new MarketEvent(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Type: MarketEventType.Trade,
            Payload: trade,
            Sequence: 1,
            Source: source,
            CanonicalizationVersion: canonVersion,
            Tier: tier);
    }

    // --- Symbol Resolution ---

    [Fact]
    public void Canonicalize_ResolvesKnownSymbol()
    {
        var raw = CreateTradeEvent("AAPL", "ALPACA");

        var result = _canonicalizer.Canonicalize(raw);

        result.CanonicalSymbol.Should().Be("AAPL");
    }

    [Fact]
    public void Canonicalize_UnresolvedSymbol_SetsCanonicalSymbolToNull()
    {
        var raw = CreateTradeEvent("UNKNOWN_SYM", "ALPACA");

        var result = _canonicalizer.Canonicalize(raw);

        result.CanonicalSymbol.Should().BeNull();
    }

    [Fact]
    public void Canonicalize_PreservesRawSymbol()
    {
        var raw = CreateTradeEvent("AAPL.US", "ALPACA");
        _mockRegistry.ResolveToCanonical("AAPL.US").Returns("AAPL");

        var result = _canonicalizer.Canonicalize(raw);

        result.Symbol.Should().Be("AAPL.US", "raw symbol must never be mutated");
        result.CanonicalSymbol.Should().Be("AAPL");
    }

    // --- Venue Normalization ---

    [Fact]
    public void Canonicalize_MapsAlpacaVenueToMic()
    {
        var raw = CreateTradeEvent("AAPL", "ALPACA", venue: "V");

        var result = _canonicalizer.Canonicalize(raw);

        result.CanonicalVenue.Should().Be("XNAS");
    }

    [Fact]
    public void Canonicalize_MapsPolygonNumericVenueToMic()
    {
        var raw = CreateTradeEvent("AAPL", "POLYGON", venue: "4");

        var result = _canonicalizer.Canonicalize(raw);

        result.CanonicalVenue.Should().Be("XNAS");
    }

    [Fact]
    public void Canonicalize_MapsIBVenueToMic()
    {
        var raw = CreateTradeEvent("AAPL", "IB", venue: "ISLAND");

        var result = _canonicalizer.Canonicalize(raw);

        result.CanonicalVenue.Should().Be("XNAS");
    }

    [Fact]
    public void Canonicalize_IbSmartVenue_ReturnsNull()
    {
        var raw = CreateTradeEvent("AAPL", "IB", venue: "SMART");

        var result = _canonicalizer.Canonicalize(raw);

        result.CanonicalVenue.Should().BeNull("SMART is IB-specific routing, not a real exchange");
    }

    [Fact]
    public void Canonicalize_UnknownVenue_ReturnsNull()
    {
        var raw = CreateTradeEvent("AAPL", "ALPACA", venue: "UNKNOWN_EXCHANGE");

        var result = _canonicalizer.Canonicalize(raw);

        result.CanonicalVenue.Should().BeNull();
    }

    [Fact]
    public void Canonicalize_NullVenue_ReturnsNull()
    {
        var raw = CreateTradeEvent("AAPL", "ALPACA", venue: null);

        var result = _canonicalizer.Canonicalize(raw);

        result.CanonicalVenue.Should().BeNull();
    }

    // --- Tier Progression ---

    [Fact]
    public void Canonicalize_SetsTierToEnriched()
    {
        var raw = CreateTradeEvent("AAPL", "ALPACA");

        var result = _canonicalizer.Canonicalize(raw);

        result.Tier.Should().Be(MarketEventTier.Enriched);
    }

    [Fact]
    public void Canonicalize_DoesNotDowngradeTier()
    {
        var raw = CreateTradeEvent("AAPL", "ALPACA", tier: MarketEventTier.Processed);

        var result = _canonicalizer.Canonicalize(raw);

        result.Tier.Should().Be(MarketEventTier.Processed, "tier should never decrease");
    }

    // --- Canonicalization Version ---

    [Fact]
    public void Canonicalize_SetsCanonicalizationVersion()
    {
        var raw = CreateTradeEvent("AAPL", "ALPACA");

        var result = _canonicalizer.Canonicalize(raw);

        result.CanonicalizationVersion.Should().Be(1);
    }

    // --- Idempotency ---

    [Fact]
    public void Canonicalize_IsIdempotent()
    {
        var raw = CreateTradeEvent("AAPL", "ALPACA", venue: "V");

        var first = _canonicalizer.Canonicalize(raw);
        var second = _canonicalizer.Canonicalize(first);

        second.Should().Be(first, "applying canonicalization twice should produce the same result");
    }

    // --- Skip Conditions ---

    [Fact]
    public void Canonicalize_SkipsHeartbeats()
    {
        var heartbeat = MarketEvent.Heartbeat(DateTimeOffset.UtcNow, "ALPACA");

        var result = _canonicalizer.Canonicalize(heartbeat);

        result.Should().BeSameAs(heartbeat);
        result.CanonicalizationVersion.Should().Be(0);
    }

    [Fact]
    public void Canonicalize_SkipsAlreadyCanonicalized()
    {
        var alreadyCanonicalized = CreateTradeEvent("AAPL", "ALPACA", canonVersion: 1);

        var result = _canonicalizer.Canonicalize(alreadyCanonicalized);

        result.Should().BeSameAs(alreadyCanonicalized);
    }

    // --- Determinism ---

    [Fact]
    public void Canonicalize_IsDeterministic()
    {
        var raw = CreateTradeEvent("AAPL", "ALPACA", venue: "V");

        var result1 = _canonicalizer.Canonicalize(raw);
        var result2 = _canonicalizer.Canonicalize(raw);

        result1.CanonicalSymbol.Should().Be(result2.CanonicalSymbol);
        result1.CanonicalVenue.Should().Be(result2.CanonicalVenue);
        result1.CanonicalizationVersion.Should().Be(result2.CanonicalizationVersion);
        result1.Tier.Should().Be(result2.Tier);
    }

    // --- Null Safety ---

    [Fact]
    public void Canonicalize_ThrowsOnNull()
    {
        var act = () => _canonicalizer.Canonicalize(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ThrowsOnNullSymbolRegistry()
    {
        var act = () => new EventCanonicalizer(null!, _conditionMapper, _venueMapper);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ThrowsOnNullConditionMapper()
    {
        var act = () => new EventCanonicalizer(_mockRegistry, null!, _venueMapper);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ThrowsOnNullVenueMapper()
    {
        var act = () => new EventCanonicalizer(_mockRegistry, _conditionMapper, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // --- BBO Quote payload venue extraction ---

    [Fact]
    public void Canonicalize_ExtractsVenueFromBboQuote()
    {
        var quote = new BboQuotePayload(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            BidPrice: 149.0m,
            BidSize: 100,
            AskPrice: 151.0m,
            AskSize: 200,
            MidPrice: 150.0m,
            Spread: 2.0m,
            SequenceNumber: 1,
            Venue: "N");

        var raw = new MarketEvent(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            Type: MarketEventType.BboQuote,
            Payload: quote,
            Sequence: 1,
            Source: "ALPACA");

        var result = _canonicalizer.Canonicalize(raw);

        result.CanonicalVenue.Should().Be("XNYS");
    }

    // --- Cross-provider symbol convergence ---

    [Fact]
    public void Canonicalize_DifferentProviders_SameCanonicalSymbol()
    {
        var alpacaEvent = CreateTradeEvent("AAPL", "ALPACA", venue: "V");
        var polygonEvent = CreateTradeEvent("AAPL", "POLYGON", venue: "4");

        var canonAlpaca = _canonicalizer.Canonicalize(alpacaEvent);
        var canonPolygon = _canonicalizer.Canonicalize(polygonEvent);

        canonAlpaca.CanonicalSymbol.Should().Be(canonPolygon.CanonicalSymbol,
            "same instrument from different providers should resolve to same canonical symbol");
        canonAlpaca.CanonicalVenue.Should().Be(canonPolygon.CanonicalVenue,
            "same exchange from different providers should resolve to same MIC");
    }
}
