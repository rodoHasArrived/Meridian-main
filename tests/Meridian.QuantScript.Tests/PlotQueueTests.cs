namespace Meridian.QuantScript.Tests;

public sealed class PlotQueueTests
{
    [Fact]
    public void Enqueue_And_TryRead_RoundTrips_Request()
    {
        var queue = new PlotQueue();
        var req = new PlotRequest("Title", PlotType.Line, Array.Empty<(DateTime, double)>());
        queue.Enqueue(req);
        queue.TryRead(out var read).Should().BeTrue();
        read.Should().BeSameAs(req);
    }

    [Fact]
    public void TryRead_Returns_False_When_Empty()
    {
        var queue = new PlotQueue();
        queue.TryRead(out var read).Should().BeFalse();
        read.Should().BeNull();
    }

    [Fact]
    public void DrainRemaining_Returns_All_Enqueued()
    {
        var queue = new PlotQueue();
        for (var i = 0; i < 5; i++)
            queue.Enqueue(new PlotRequest($"Plot {i}", PlotType.Scatter, Array.Empty<(DateTime, double)>()));
        var drained = queue.DrainRemaining();
        drained.Should().HaveCount(5);
    public void Enqueue_AddsItem_ReadAllAsyncDequeues()
    {
        var queue = new PlotQueue { MaxPlotsPerRun = 10 };
        var request = new PlotRequest("Test", PlotType.Line,
            Series: [new(new DateOnly(2024, 1, 1), 1.0)]);

        queue.Enqueue(request);
        queue.Complete();

        var items = queue.ReadAllAsync().ToBlockingEnumerable().ToList();
        items.Should().ContainSingle();
        items[0].Title.Should().Be("Test");
    }

    [Fact]
    public void Enqueue_ExceedingMaxPlotsPerRun_DropsExcess()
    {
        var queue = new PlotQueue { MaxPlotsPerRun = 3 };
        for (var i = 0; i < 10; i++)
            queue.Enqueue(new PlotRequest($"Plot {i}", PlotType.Line));

        queue.Complete();
        var items = queue.ReadAllAsync().ToBlockingEnumerable().ToList();
        items.Should().HaveCount(3);
    }

    [Fact]
    public void Complete_AllowsDrain_NoFurtherItemsArriveAfter()
    {
        var queue = new PlotQueue { MaxPlotsPerRun = 10 };
        queue.Enqueue(new PlotRequest("A", PlotType.Line));
        queue.Complete();
        // Second enqueue after complete should be silently dropped
        queue.Enqueue(new PlotRequest("B", PlotType.Line));

        var items = queue.ReadAllAsync().ToBlockingEnumerable().ToList();
        items.Should().HaveCount(1);
    }
}
