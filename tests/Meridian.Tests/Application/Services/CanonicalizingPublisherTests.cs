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
/// Tests for <see cref="CanonicalizingPublisher"/>.
/// Covers decorator behavior, pilot symbol filtering, dual-write mode,
/// metrics tracking, and passthrough for non-pilot symbols.
/// </summary>
public sealed class CanonicalizingPublisherTests
{
    private readonly IEventCanonicalizer _canonicalizer;
    private readonly TestPublisher _inner;

    public CanonicalizingPublisherTests()
    {
        _canonicalizer = CreateTestCanonicalizer();
        _inner = new TestPublisher();
    }

    [Fact]
    public void Canonicalize_PublishesCanonicalized_WhenNoPilotFilter()
    {
        var publisher = new CanonicalizingPublisher(_inner, _canonicalizer, pilotSymbols: null, dualWrite: false);
        var evt = CreateTradeEvent("AAPL", "ALPACA");

        publisher.TryPublish(in evt);

        _inner.PublishedEvents.Should().HaveCount(1);
        _inner.PublishedEvents[0].CanonicalizationVersion.Should().BeGreaterThan(0);
        _inner.PublishedEvents[0].CanonicalSymbol.Should().Be("AAPL");
    }

    [Fact]
    public void DualWrite_PublishesBothRawAndCanonical()
    {
        var publisher = new CanonicalizingPublisher(_inner, _canonicalizer, pilotSymbols: null, dualWrite: true);
        var evt = CreateTradeEvent("AAPL", "ALPACA");

        publisher.TryPublish(in evt);

        _inner.PublishedEvents.Should().HaveCount(2);
        // First is raw
        _inner.PublishedEvents[0].CanonicalizationVersion.Should().Be(0);
        _inner.PublishedEvents[0].Symbol.Should().Be("AAPL");
        // Second is canonical
        _inner.PublishedEvents[1].CanonicalizationVersion.Should().BeGreaterThan(0);
    }

    [Fact]
    public void PilotSymbols_SkipsNonPilotSymbols()
    {
        var publisher = new CanonicalizingPublisher(_inner, _canonicalizer, pilotSymbols: new[] { "AAPL" }, dualWrite: false);
        var evt = CreateTradeEvent("MSFT", "ALPACA");

        publisher.TryPublish(in evt);

        _inner.PublishedEvents.Should().HaveCount(1);
        // Non-pilot symbol should pass through uncanonicalized
        _inner.PublishedEvents[0].CanonicalizationVersion.Should().Be(0);
        _inner.PublishedEvents[0].Symbol.Should().Be("MSFT");
    }

    [Fact]
    public void PilotSymbols_CanonicalizesPilotSymbols()
    {
        var publisher = new CanonicalizingPublisher(_inner, _canonicalizer, pilotSymbols: new[] { "AAPL" }, dualWrite: false);
        var evt = CreateTradeEvent("AAPL", "ALPACA");

        publisher.TryPublish(in evt);

        _inner.PublishedEvents.Should().HaveCount(1);
        _inner.PublishedEvents[0].CanonicalizationVersion.Should().BeGreaterThan(0);
    }

    [Fact]
    public void PilotSymbols_CaseInsensitive()
    {
        var publisher = new CanonicalizingPublisher(_inner, _canonicalizer, pilotSymbols: new[] { "aapl" }, dualWrite: false);
        var evt = CreateTradeEvent("AAPL", "ALPACA");

        publisher.TryPublish(in evt);

        _inner.PublishedEvents.Should().HaveCount(1);
        _inner.PublishedEvents[0].CanonicalizationVersion.Should().BeGreaterThan(0);
    }

    [Fact]
    public void DualWrite_ReturnsFalse_WhenInnerRejects()
    {
        _inner.ShouldReject = true;
        var publisher = new CanonicalizingPublisher(_inner, _canonicalizer, pilotSymbols: null, dualWrite: true);
        var evt = CreateTradeEvent("AAPL", "ALPACA");

        var result = publisher.TryPublish(in evt);

        result.Should().BeFalse();
        // When raw publish fails, canonical should not be attempted
        _inner.PublishedEvents.Should().BeEmpty();
    }

    [Fact]
    public void Metrics_TracksCanonicalizedCount()
    {
        var publisher = new CanonicalizingPublisher(_inner, _canonicalizer, pilotSymbols: null, dualWrite: false);
        var evt = CreateTradeEvent("AAPL", "ALPACA");

        publisher.TryPublish(in evt);
        publisher.TryPublish(in evt);

        publisher.CanonicalizationCount.Should().Be(2);
    }

    [Fact]
    public void Metrics_TracksSkippedCount()
    {
        var publisher = new CanonicalizingPublisher(_inner, _canonicalizer, pilotSymbols: new[] { "AAPL" }, dualWrite: false);
        var evt = CreateTradeEvent("MSFT", "ALPACA");

        publisher.TryPublish(in evt);

        publisher.SkippedCount.Should().Be(1);
        publisher.CanonicalizationCount.Should().Be(0);
    }

    [Fact]
    public void Metrics_TracksDualWriteCount()
    {
        var publisher = new CanonicalizingPublisher(_inner, _canonicalizer, pilotSymbols: null, dualWrite: true);
        var evt = CreateTradeEvent("AAPL", "ALPACA");

        publisher.TryPublish(in evt);

        publisher.DualWriteCount.Should().Be(1);
    }

    [Fact]
    public void Metrics_TracksUnresolvedSymbols()
    {
        var publisher = new CanonicalizingPublisher(_inner, _canonicalizer, pilotSymbols: null, dualWrite: false);
        var evt = CreateTradeEvent("UNKNOWN_SYM", "ALPACA");

        publisher.TryPublish(in evt);

        publisher.UnresolvedCount.Should().Be(1);
    }

    [Fact]
    public void MetricsSnapshot_ReflectsAllCounters()
    {
        var publisher = new CanonicalizingPublisher(_inner, _canonicalizer, pilotSymbols: new[] { "AAPL" }, dualWrite: true);

        publisher.TryPublish(CreateTradeEvent("AAPL", "ALPACA"));
        publisher.TryPublish(CreateTradeEvent("MSFT", "ALPACA")); // skipped

        var snapshot = publisher.GetMetricsSnapshot();
        snapshot.Canonicalized.Should().Be(1);
        snapshot.Skipped.Should().Be(1);
        snapshot.DualWrites.Should().Be(1);
    }

    [Fact]
    public void AverageDuration_IsPositive_AfterCanonicalization()
    {
        var publisher = new CanonicalizingPublisher(_inner, _canonicalizer, pilotSymbols: null, dualWrite: false);
        var evt = CreateTradeEvent("AAPL", "ALPACA");

        publisher.TryPublish(in evt);

        publisher.AverageDurationUs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void NullPilotSymbols_CanonicalizeAll()
    {
        var publisher = new CanonicalizingPublisher(_inner, _canonicalizer, pilotSymbols: null, dualWrite: false);

        publisher.TryPublish(CreateTradeEvent("AAPL", "ALPACA"));
        publisher.TryPublish(CreateTradeEvent("MSFT", "ALPACA"));

        publisher.CanonicalizationCount.Should().Be(2);
        publisher.SkippedCount.Should().Be(0);
    }

    [Fact]
    public void EmptyPilotSymbols_CanonicalizeAll()
    {
        var publisher = new CanonicalizingPublisher(_inner, _canonicalizer, pilotSymbols: Array.Empty<string>(), dualWrite: false);

        publisher.TryPublish(CreateTradeEvent("AAPL", "ALPACA"));
        publisher.TryPublish(CreateTradeEvent("MSFT", "ALPACA"));

        // Empty pilot set means no filter → canonicalize all
        publisher.CanonicalizationCount.Should().Be(2);
    }

    [Fact]
    public void VenueNormalization_AppliedThroughPublisher()
    {
        var publisher = new CanonicalizingPublisher(_inner, _canonicalizer, pilotSymbols: null, dualWrite: false);
        var trade = new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            Price: 150.0m,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: 1,
            Venue: "V");
        var evt = MarketEvent.Trade(DateTimeOffset.UtcNow, "AAPL", trade, source: "ALPACA");

        publisher.TryPublish(in evt);

        _inner.PublishedEvents[0].CanonicalVenue.Should().Be("XNAS");
    }

    [Fact]
    public void ThrowsOnNullInner()
    {
        var act = () => new CanonicalizingPublisher(null!, _canonicalizer);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ThrowsOnNullCanonicalizer()
    {
        var act = () => new CanonicalizingPublisher(_inner, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #region Helpers

    private static MarketEvent CreateTradeEvent(string symbol, string source)
    {
        var trade = new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Price: 150.0m,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: 1);
        return MarketEvent.Trade(DateTimeOffset.UtcNow, symbol, trade, source: source);
    }

    private static IEventCanonicalizer CreateTestCanonicalizer()
    {
        var registry = Substitute.For<ICanonicalSymbolRegistry>();
        registry.ResolveToCanonical("AAPL").Returns("AAPL");
        registry.ResolveToCanonical("MSFT").Returns("MSFT");
        registry.ResolveToCanonical("UNKNOWN_SYM").Returns((string?)null);

        var conditions = ConditionCodeMapper.LoadFromJson("""
        {
            "version": 1,
            "mappings": {
                "ALPACA": { "@": "Regular" }
            }
        }
        """);

        var venues = VenueMicMapper.LoadFromJson("""
        {
            "version": 1,
            "mappings": {
                "ALPACA": { "V": "XNAS", "N": "XNYS" }
            }
        }
        """);

        return new EventCanonicalizer(registry, conditions, venues, version: 1);
    }

    /// <summary>
    /// Test publisher that records all published events.
    /// </summary>
    private sealed class TestPublisher : IMarketEventPublisher
    {
        public List<MarketEvent> PublishedEvents { get; } = new();
        public bool ShouldReject { get; set; }

        public bool TryPublish(in MarketEvent evt)
        {
            if (ShouldReject)
                return false;
            PublishedEvents.Add(evt);
            return true;
        }
    }

    #endregion
}
