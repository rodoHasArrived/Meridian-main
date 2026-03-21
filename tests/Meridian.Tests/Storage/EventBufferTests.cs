using FluentAssertions;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Storage.Services;
using Xunit;
using AggressorSide = Meridian.Contracts.Domain.Enums.AggressorSide;

namespace Meridian.Tests.Storage;

public sealed class EventBufferTests
{
    // ---------------------------------------------------------------------------
    // Add / Count / IsEmpty
    // ---------------------------------------------------------------------------

    [Fact]
    public void Add_SingleEvent_CountIsOne()
    {
        var buffer = new EventBuffer<MarketEvent>();
        buffer.Add(CreateEvent("AAPL", 1));

        buffer.Count.Should().Be(1);
        buffer.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Add_ThrowsOnNull()
    {
        var buffer = new EventBuffer<MarketEvent>();
        var act = () => buffer.Add(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddRange_MultipleEvents_CountMatchesAdded()
    {
        var buffer = new EventBuffer<MarketEvent>();
        buffer.AddRange(Enumerable.Range(1, 5).Select(i => CreateEvent("AAPL", i)));

        buffer.Count.Should().Be(5);
    }

    // ---------------------------------------------------------------------------
    // ShouldFlush
    // ---------------------------------------------------------------------------

    [Fact]
    public void ShouldFlush_BelowThreshold_ReturnsFalse()
    {
        var buffer = new EventBuffer<MarketEvent>();
        buffer.Add(CreateEvent("AAPL", 1));
        buffer.ShouldFlush(10).Should().BeFalse();
    }

    [Fact]
    public void ShouldFlush_AtThreshold_ReturnsTrue()
    {
        var buffer = new EventBuffer<MarketEvent>();
        for (var i = 0; i < 5; i++)
            buffer.Add(CreateEvent("AAPL", i));
        buffer.ShouldFlush(5).Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // DrainAll — swap-buffer behaviour
    // ---------------------------------------------------------------------------

    [Fact]
    public void DrainAll_Empty_ReturnsEmptyArray()
    {
        var buffer = new EventBuffer<MarketEvent>();
        var result = buffer.DrainAll();
        result.Should().BeEmpty();
    }

    [Fact]
    public void DrainAll_ReturnsAllEvents_AndClearsBuffer()
    {
        var buffer = new EventBuffer<MarketEvent>();
        buffer.AddRange(Enumerable.Range(1, 10).Select(i => CreateEvent("AAPL", i)));

        var result = buffer.DrainAll();

        result.Should().HaveCount(10);
        buffer.Count.Should().Be(0);
        buffer.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void DrainAll_SubsequentDrain_ReturnsOnlyNewEvents()
    {
        // Snapshot with ToArray() to copy data out of the swap-buffer before the next
        // drain cycle replaces the backing list. Callers that need to retain drained
        // events beyond the next drain cycle must copy them themselves.
        var buffer = new EventBuffer<MarketEvent>();
        buffer.Add(CreateEvent("AAPL", 1));
        var first = buffer.DrainAll().ToArray();

        buffer.Add(CreateEvent("MSFT", 2));
        var second = buffer.DrainAll().ToArray();

        first.Should().HaveCount(1).And.Contain(e => e.Symbol == "AAPL");
        second.Should().HaveCount(1).And.Contain(e => e.Symbol == "MSFT");
    }

    [Fact]
    public void DrainAll_ReturnedListIsReusedOnNextCycle_ContractTest()
    {
        // Demonstrates the swap-buffer contract: the list returned by DrainAll is
        // cleared and reused as the active buffer on the next drain cycle.
        // Callers must not retain the reference past the next DrainAll call.
        var buffer = new EventBuffer<MarketEvent>();
        buffer.Add(CreateEvent("AAPL", 1));
        var firstDrained = buffer.DrainAll(); // holds reference to internal list

        // Add new events — they go into the ex-standby list (now active)
        buffer.Add(CreateEvent("MSFT", 2));

        // Second DrainAll swaps again: ex-active (firstDrained) becomes active and is cleared
        buffer.DrainAll();

        // firstDrained now points to the cleared (empty) internal list
        firstDrained.Should().BeEmpty(because: "the swap-buffer reuses and clears the returned list on the next drain cycle");
    }

    [Fact]
    public void DrainAll_CountResetToZero()
    {
        var buffer = new EventBuffer<MarketEvent>();
        for (var i = 0; i < 7; i++)
            buffer.Add(CreateEvent("AAPL", i));
        buffer.DrainAll();
        buffer.Count.Should().Be(0);
    }

    // ---------------------------------------------------------------------------
    // Drain(maxCount) — partial drain
    // ---------------------------------------------------------------------------

    [Fact]
    public void Drain_Empty_ReturnsEmptyArray()
    {
        var buffer = new EventBuffer<MarketEvent>();
        buffer.Drain(5).Should().BeEmpty();
    }

    [Fact]
    public void Drain_LessThanAvailable_ReturnsMaxCountAndLeavesRest()
    {
        var buffer = new EventBuffer<MarketEvent>();
        for (var i = 1; i <= 10; i++)
            buffer.Add(CreateEvent("AAPL", i));

        var result = buffer.Drain(3);

        result.Should().HaveCount(3);
        buffer.Count.Should().Be(7);
    }

    [Fact]
    public void Drain_MoreThanAvailable_DrainsAll()
    {
        var buffer = new EventBuffer<MarketEvent>();
        buffer.Add(CreateEvent("AAPL", 1));

        var result = buffer.Drain(100);

        result.Should().HaveCount(1);
        buffer.Count.Should().Be(0);
    }

    [Fact]
    public void Drain_ZeroMaxCount_Throws()
    {
        var buffer = new EventBuffer<MarketEvent>();
        var act = () => buffer.Drain(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---------------------------------------------------------------------------
    // Capacity enforcement (maxCapacity)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Add_WhenAtMaxCapacity_DropsOldest()
    {
        var buffer = new EventBuffer<MarketEvent>(initialCapacity: 3, maxCapacity: 3);
        for (var i = 1; i <= 3; i++)
            buffer.Add(CreateEvent("AAPL", i));

        // Adding a 4th event should drop the first
        buffer.Add(CreateEvent("AAPL", 4));

        buffer.Count.Should().Be(3);
        var all = buffer.DrainAll();
        all.Select(e => e.Sequence).Should().BeEquivalentTo(new[] { 2L, 3L, 4L },
            because: "oldest event (seq=1) should have been dropped");
    }

    // ---------------------------------------------------------------------------
    // Clear
    // ---------------------------------------------------------------------------

    [Fact]
    public void Clear_EmptiesBuffer()
    {
        var buffer = new EventBuffer<MarketEvent>();
        for (var i = 0; i < 5; i++)
            buffer.Add(CreateEvent("AAPL", i));

        buffer.Clear();

        buffer.Count.Should().Be(0);
        buffer.IsEmpty.Should().BeTrue();
        buffer.DrainAll().Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------
    // PeekAll
    // ---------------------------------------------------------------------------

    [Fact]
    public void PeekAll_DoesNotRemoveEvents()
    {
        var buffer = new EventBuffer<MarketEvent>();
        buffer.Add(CreateEvent("AAPL", 1));
        buffer.Add(CreateEvent("AAPL", 2));

        var peeked = buffer.PeekAll();

        peeked.Should().HaveCount(2);
        buffer.Count.Should().Be(2, because: "PeekAll must not drain the buffer");
    }

    // ---------------------------------------------------------------------------
    // Dispose
    // ---------------------------------------------------------------------------

    [Fact]
    public void Dispose_ThrowsOnSubsequentAdd()
    {
        var buffer = new EventBuffer<MarketEvent>();
        buffer.Dispose();

        var act = () => buffer.Add(CreateEvent("AAPL", 1));
        act.Should().Throw<ObjectDisposedException>();
    }

    // ---------------------------------------------------------------------------
    // MarketEventBuffer.DrainBySymbol — single-lock correctness
    // ---------------------------------------------------------------------------

    [Fact]
    public void DrainBySymbol_ReturnsOnlyMatchingEvents()
    {
        var buffer = new MarketEventBuffer();
        buffer.Add(CreateEvent("AAPL", 1));
        buffer.Add(CreateEvent("MSFT", 2));
        buffer.Add(CreateEvent("AAPL", 3));
        buffer.Add(CreateEvent("TSLA", 4));

        var result = buffer.DrainBySymbol("AAPL");

        result.Should().HaveCount(2);
        result.Should().OnlyContain(e => e.Symbol == "AAPL");
    }

    [Fact]
    public void DrainBySymbol_LeavesNonMatchingInBuffer()
    {
        var buffer = new MarketEventBuffer();
        buffer.Add(CreateEvent("AAPL", 1));
        buffer.Add(CreateEvent("MSFT", 2));
        buffer.Add(CreateEvent("TSLA", 3));

        buffer.DrainBySymbol("AAPL");

        buffer.Count.Should().Be(2);
        var remaining = buffer.DrainAll();
        remaining.Should().OnlyContain(e => e.Symbol != "AAPL");
    }

    [Fact]
    public void DrainBySymbol_PreservesOrderOfRemainingEvents()
    {
        var buffer = new MarketEventBuffer();
        buffer.Add(CreateEvent("MSFT", 1));
        buffer.Add(CreateEvent("AAPL", 2));
        buffer.Add(CreateEvent("MSFT", 3));
        buffer.Add(CreateEvent("AAPL", 4));

        buffer.DrainBySymbol("AAPL");

        var remaining = buffer.DrainAll();
        remaining.Select(e => e.Sequence).Should().ContainInOrder(1L, 3L);
    }

    [Fact]
    public void DrainBySymbol_IsCaseInsensitive()
    {
        var buffer = new MarketEventBuffer();
        buffer.Add(CreateEvent("aapl", 1));

        var result = buffer.DrainBySymbol("AAPL");

        result.Should().HaveCount(1);
    }

    [Fact]
    public void DrainBySymbol_WhenNoMatch_ReturnsEmptyAndBufferUnchanged()
    {
        var buffer = new MarketEventBuffer();
        buffer.Add(CreateEvent("MSFT", 1));

        var result = buffer.DrainBySymbol("AAPL");

        result.Should().BeEmpty();
        buffer.Count.Should().Be(1);
    }

    [Fact]
    public void DrainBySymbol_NullOrEmptySymbol_Throws()
    {
        var buffer = new MarketEventBuffer();
        var actNull = () => buffer.DrainBySymbol(null!);
        var actEmpty = () => buffer.DrainBySymbol(string.Empty);
        actNull.Should().Throw<ArgumentException>();
        actEmpty.Should().Throw<ArgumentException>();
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static MarketEvent CreateEvent(string symbol, int sequence)
    {
        var trade = new Trade(
            DateTimeOffset.UtcNow,
            symbol,
            100m + sequence,
            100,
            AggressorSide.Buy,
            sequence);

        return MarketEvent.Trade(DateTimeOffset.UtcNow, symbol, trade, sequence, "TEST");
    }
}
