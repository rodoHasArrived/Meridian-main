using System.Threading.Channels;

namespace Meridian.Contracts.Pipeline;

/// <summary>
/// Snapshot of pipeline performance statistics.
/// </summary>
public readonly record struct PipelineStatistics(
    long PublishedCount,
    long DroppedCount,
    long ConsumedCount,
    int CurrentQueueSize,
    long PeakQueueSize,
    int QueueCapacity,
    double QueueUtilization,
    double AverageProcessingTimeUs,
    TimeSpan TimeSinceLastFlush,
    DateTimeOffset Timestamp,
    long RecoveredCount = 0,
    long RejectedCount = 0,
    long DeduplicatedCount = 0,
    bool IsWalEnabled = false,
    bool IsValidationEnabled = false,
    bool IsDeduplicationEnabled = false,
    BoundedChannelFullMode QueueFullMode = BoundedChannelFullMode.Wait,
    bool HighWaterMarkWarned = false,
    int ConsumerCount = 1,
    int ActiveConsumers = 0,
    int FaultedConsumers = 0,
    long ConsumerIterationFailures = 0,
    DateTimeOffset? LastConsumerFaultAtUtc = null
);
