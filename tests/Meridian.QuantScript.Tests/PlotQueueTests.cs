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
    }
}
