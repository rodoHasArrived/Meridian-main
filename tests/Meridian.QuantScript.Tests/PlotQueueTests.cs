namespace Meridian.QuantScript.Tests;

public sealed class PlotQueueTests
{
    [Fact]
    public void Enqueue_And_DrainRemaining_RoundTrips()
    {
        var queue = new PlotQueue();
        var req = new PlotRequest("Title", PlotType.Line,
            Series: [new(new DateOnly(2024, 1, 1), 1.0)]);
        queue.Enqueue(req);
        queue.Complete();
        var items = queue.DrainRemaining();
        items.Should().ContainSingle();
        items[0].Title.Should().Be("Title");
    }

    [Fact]
    public void DrainRemaining_ReturnsEmpty_WhenNothingEnqueued()
    {
        var queue = new PlotQueue();
        queue.Complete();
        queue.DrainRemaining().Should().BeEmpty();
    }

    [Fact]
    public void DrainRemaining_Returns_All_Enqueued()
    {
        var queue = new PlotQueue();
        for (var i = 0; i < 5; i++)
            queue.Enqueue(new PlotRequest($"Plot {i}", PlotType.Line));
        queue.Complete();
        var drained = queue.DrainRemaining();
        drained.Should().HaveCount(5);
    }

    [Fact]
    public void Current_ReturnsNull_WhenNoRunActive()
    {
        PlotQueue.Current = null;
        PlotQueue.Current.Should().BeNull();
    }

    [Fact]
    public void Current_ReflectsSetValue()
    {
        var queue = new PlotQueue();
        PlotQueue.Current = queue;
        try
        {
            PlotQueue.Current.Should().BeSameAs(queue);
        }
        finally
        {
            PlotQueue.Current = null;
        }
    }
}
