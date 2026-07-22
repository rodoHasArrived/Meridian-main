using System.Threading.Channels;
using FluentAssertions;
using Meridian.Contracts.Pipeline;
using Meridian.Platform.Diagnostics;
using Xunit;

namespace Meridian.Tests.Platform.Diagnostics;

public sealed class PipelineDiagnosticsProjectionTests
{
    [Fact]
    public void FromStatistics_ClassifiesDroppedEventsAsCriticalWithRecoveryGuidance()
    {
        var snapshot = PipelineDiagnosticsProjection.FromStatistics(new PipelineStatistics(
            PublishedCount: 100,
            DroppedCount: 3,
            ConsumedCount: 90,
            CurrentQueueSize: 7,
            PeakQueueSize: 10,
            QueueCapacity: 10,
            QueueUtilization: 70,
            AverageProcessingTimeUs: 125.4,
            TimeSinceLastFlush: TimeSpan.FromMilliseconds(250),
            Timestamp: DateTimeOffset.UtcNow,
            QueueFullMode: BoundedChannelFullMode.DropWrite,
            HighWaterMarkWarned: true));

        snapshot.Available.Should().BeTrue();
        snapshot.OperationName.Should().Be("pipeline.health.summary");
        snapshot.Status.Should().Be("Critical");
        snapshot.FailureReason.Should().Be("pipeline.events.dropped");
        snapshot.RecoveryAction.Should().Contain("Reduce ingestion rate");
        snapshot.ConsumerLag.Should().Be(7);
        snapshot.QueueFullMode.Should().Be("DropWrite");
        snapshot.Summary.Should().Contain("dropped events");
    }

    [Fact]
    public void FromStatistics_ClassifiesHighWaterQueueAsDegradedWithoutPayloads()
    {
        var snapshot = PipelineDiagnosticsProjection.FromStatistics(new PipelineStatistics(
            PublishedCount: 50,
            DroppedCount: 0,
            ConsumedCount: 40,
            CurrentQueueSize: 9,
            PeakQueueSize: 9,
            QueueCapacity: 10,
            QueueUtilization: 90,
            AverageProcessingTimeUs: 25,
            TimeSinceLastFlush: TimeSpan.FromSeconds(1),
            Timestamp: DateTimeOffset.UtcNow,
            QueueFullMode: BoundedChannelFullMode.Wait));

        snapshot.Status.Should().Be("Degraded");
        snapshot.FailureReason.Should().Be("pipeline.queue.high-water");
        snapshot.RecoveryAction.Should().Contain("throttle providers");
        snapshot.Summary.Should().Contain("high-water");
    }
}
