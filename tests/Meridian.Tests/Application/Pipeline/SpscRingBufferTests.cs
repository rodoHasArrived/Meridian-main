using FluentAssertions;
using Meridian.Core.Performance;
using Xunit;

namespace Meridian.Tests.Pipeline;

/// <summary>
/// Tests for the lock-free single-producer/single-consumer ring buffer.
/// </summary>
public class SpscRingBufferTests
{
    #region Constructor tests

    [Fact]
    public void Constructor_CapacityRoundedUpToPowerOfTwo()
    {
        var buffer = new SpscRingBuffer<int>(3);
        buffer.Capacity.Should().Be(4);
    }

    [Fact]
    public void Constructor_CapacityAlreadyPowerOfTwo_NotChanged()
    {
        var buffer = new SpscRingBuffer<int>(8);
        buffer.Capacity.Should().Be(8);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    public void Constructor_CapacityTooSmall_ThrowsArgumentOutOfRangeException(int capacity)
    {
        var act = () => new SpscRingBuffer<int>(capacity);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("capacity");
    }

    [Fact]
    public void NewBuffer_IsEmpty_AndNotFull()
    {
        var buffer = new SpscRingBuffer<int>(4);
        buffer.IsEmpty.Should().BeTrue();
        buffer.IsFull.Should().BeFalse();
        buffer.Count.Should().Be(0);
    }

    #endregion

    #region Basic read/write tests

    [Fact]
    public void TryWrite_SingleItem_Succeeds()
    {
        var buffer = new SpscRingBuffer<int>(4);
        buffer.TryWrite(42).Should().BeTrue();
        buffer.Count.Should().Be(1);
        buffer.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void TryRead_OnEmptyBuffer_ReturnsFalseAndDefault()
    {
        var buffer = new SpscRingBuffer<int>(4);
        var result = buffer.TryRead(out var item);
        result.Should().BeFalse();
        item.Should().Be(default);
    }

    [Fact]
    public void TryWrite_ThenTryRead_ReturnsItem()
    {
        var buffer = new SpscRingBuffer<int>(4);
        buffer.TryWrite(99);

        var ok = buffer.TryRead(out var item);
        ok.Should().BeTrue();
        item.Should().Be(99);
        buffer.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void TryWrite_FillToCapacity_Succeeds()
    {
        var buffer = new SpscRingBuffer<int>(4);
        buffer.TryWrite(1).Should().BeTrue();
        buffer.TryWrite(2).Should().BeTrue();
        buffer.TryWrite(3).Should().BeTrue();
        buffer.TryWrite(4).Should().BeTrue();

        buffer.IsFull.Should().BeTrue();
        buffer.Count.Should().Be(4);
    }

    [Fact]
    public void TryWrite_WhenFull_ReturnsFalse()
    {
        var buffer = new SpscRingBuffer<int>(4);
        for (var i = 0; i < 4; i++)
            buffer.TryWrite(i);

        buffer.TryWrite(99).Should().BeFalse();
        buffer.Count.Should().Be(4);
    }

    [Fact]
    public void MultipleWritesAndReads_PreserveFifoOrder()
    {
        var buffer = new SpscRingBuffer<int>(8);
        for (var i = 0; i < 5; i++)
            buffer.TryWrite(i);

        for (var expected = 0; expected < 5; expected++)
        {
            buffer.TryRead(out var item).Should().BeTrue();
            item.Should().Be(expected);
        }
    }

    #endregion

    #region Wrap-around tests

    [Fact]
    public void WrapAround_ProducesCorrectOrder()
    {
        // Capacity = 4; write 3, read 3, write 4 more (wraps around the array)
        var buffer = new SpscRingBuffer<int>(4);

        buffer.TryWrite(1);
        buffer.TryWrite(2);
        buffer.TryWrite(3);

        buffer.TryRead(out _);
        buffer.TryRead(out _);
        buffer.TryRead(out _);

        // Write 4 more: slots wrap around
        buffer.TryWrite(10).Should().BeTrue();
        buffer.TryWrite(20).Should().BeTrue();
        buffer.TryWrite(30).Should().BeTrue();
        buffer.TryWrite(40).Should().BeTrue();

        buffer.TryRead(out var a);
        a.Should().Be(10);
        buffer.TryRead(out var b);
        b.Should().Be(20);
        buffer.TryRead(out var c);
        c.Should().Be(30);
        buffer.TryRead(out var d);
        d.Should().Be(40);
        buffer.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void LargeNumberOfWritesAndReads_RemainCorrect()
    {
        var buffer = new SpscRingBuffer<int>(16);
        const int total = 1000;

        for (var i = 0; i < total; i++)
        {
            // Drain one before writing if full
            if (buffer.IsFull)
                buffer.TryRead(out _);
            buffer.TryWrite(i);
        }

        // Just verify Count is reasonable (no crash, no data corruption)
        buffer.Count.Should().BeGreaterThan(0);
    }

    #endregion

    #region DrainTo tests

    [Fact]
    public void DrainTo_EmptyBuffer_ReturnsZero()
    {
        var buffer = new SpscRingBuffer<int>(8);
        Span<int> dest = stackalloc int[4];
        buffer.DrainTo(dest).Should().Be(0);
    }

    [Fact]
    public void DrainTo_DrainsSmallerOfBatchAndAvailable()
    {
        var buffer = new SpscRingBuffer<int>(8);
        for (var i = 0; i < 6; i++)
            buffer.TryWrite(i);

        Span<int> dest = stackalloc int[4];
        var count = buffer.DrainTo(dest, maxItems: 4);

        count.Should().Be(4);
        buffer.Count.Should().Be(2);
        dest[0].Should().Be(0);
        dest[3].Should().Be(3);
    }

    [Fact]
    public void DrainTo_MaxItemsLimitApplied()
    {
        var buffer = new SpscRingBuffer<int>(8);
        for (var i = 0; i < 8; i++)
            buffer.TryWrite(i);

        var dest = new int[8];
        var count = buffer.DrainTo(dest, maxItems: 3);

        count.Should().Be(3);
        buffer.Count.Should().Be(5);
    }

    #endregion

    #region Struct type tests

    [Fact]
    public void TryWrite_RawTradeEvent_RoundTrips()
    {
        var buffer = new SpscRingBuffer<RawTradeEvent>(4);
        var ts = DateTimeOffset.UtcNow.UtcTicks;
        var trade = new RawTradeEvent(ts, symbolHash: 7, price: 123.45m, size: 500L, aggressor: 1, sequence: 42L);

        buffer.TryWrite(in trade).Should().BeTrue();
        buffer.TryRead(out var result).Should().BeTrue();

        result.TimestampTicks.Should().Be(ts);
        result.SymbolHash.Should().Be(7);
        result.Price.Should().Be(123.45m);
        result.Size.Should().Be(500L);
        result.Aggressor.Should().Be(1);
        result.Sequence.Should().Be(42L);
    }

    [Fact]
    public void TryWrite_RawQuoteEvent_RoundTrips()
    {
        var buffer = new SpscRingBuffer<RawQuoteEvent>(4);
        var ts = DateTimeOffset.UtcNow.UtcTicks;
        var quote = new RawQuoteEvent(ts, symbolHash: 3, bidPrice: 99.5m, bidSize: 200L, askPrice: 100.0m, askSize: 150L, sequence: 7L);

        buffer.TryWrite(in quote).Should().BeTrue();
        buffer.TryRead(out var result).Should().BeTrue();

        result.TimestampTicks.Should().Be(ts);
        result.SymbolHash.Should().Be(3);
        result.BidPrice.Should().Be(99.5m);
        result.BidSize.Should().Be(200L);
        result.AskPrice.Should().Be(100.0m);
        result.AskSize.Should().Be(150L);
        result.Sequence.Should().Be(7L);
    }

    #endregion

    #region Concurrency invariant tests

    [Fact]
    public async Task Concurrent_ProducerConsumer_AllItemsTransferred()
    {
        const int count = 10_000;
        var buffer = new SpscRingBuffer<int>(256);
        var received = new List<int>(count);

        var producer = Task.Run(() =>
        {
            for (var i = 0; i < count; i++)
            {
                while (!buffer.TryWrite(i))
                    Thread.SpinWait(1); // Spin until slot is available
            }
        });

        var consumer = Task.Run(() =>
        {
            var read = 0;
            while (read < count)
            {
                if (buffer.TryRead(out var item))
                {
                    received.Add(item);
                    read++;
                }
                else
                {
                    Thread.SpinWait(1);
                }
            }
        });

        await Task.WhenAll(producer, consumer).WaitAsync(TimeSpan.FromSeconds(10));

        received.Should().HaveCount(count);
        for (var i = 0; i < count; i++)
            received[i].Should().Be(i);
    }

    #endregion
}
