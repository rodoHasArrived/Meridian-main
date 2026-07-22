using Meridian.Contracts.Pipeline;

namespace Meridian.Platform.Diagnostics;

public sealed record PipelineHealthDiagnosticSnapshot(
    bool Available,
    string OperationName,
    string ComponentName,
    string QueueName,
    string Status,
    string Summary,
    string FailureReason,
    string RecoveryAction,
    long Published,
    long Consumed,
    long Dropped,
    long Rejected,
    long Deduplicated,
    long Recovered,
    int CurrentQueueSize,
    long PeakQueueSize,
    int QueueCapacity,
    double QueueUtilization,
    long ConsumerLag,
    double AverageProcessingTimeUs,
    double TimeSinceLastFlushMs,
    string QueueFullMode,
    bool HighWaterMarkWarned,
    bool IsWalEnabled,
    bool IsValidationEnabled,
    bool IsDeduplicationEnabled,
    DateTimeOffset? Timestamp);

public static class PipelineDiagnosticsProjection
{
    public static PipelineHealthDiagnosticSnapshot Unavailable(string queueName = "market-data") => new(
        Available: false,
        OperationName: "pipeline.health.summary",
        ComponentName: "EventPipeline",
        QueueName: queueName,
        Status: "Unavailable",
        Summary: "Pipeline statistics are not registered in this host.",
        FailureReason: "pipeline.statistics.unavailable",
        RecoveryAction: "Register EventPipeline diagnostics before collecting runtime support evidence.",
        Published: 0,
        Consumed: 0,
        Dropped: 0,
        Rejected: 0,
        Deduplicated: 0,
        Recovered: 0,
        CurrentQueueSize: 0,
        PeakQueueSize: 0,
        QueueCapacity: 0,
        QueueUtilization: 0,
        ConsumerLag: 0,
        AverageProcessingTimeUs: 0,
        TimeSinceLastFlushMs: 0,
        QueueFullMode: "Unknown",
        HighWaterMarkWarned: false,
        IsWalEnabled: false,
        IsValidationEnabled: false,
        IsDeduplicationEnabled: false,
        Timestamp: null);

    public static PipelineHealthDiagnosticSnapshot FromStatistics(
        PipelineStatistics statistics,
        string queueName = "market-data")
    {
        var consumerLag = Math.Max(
            0,
            statistics.PublishedCount
                - statistics.ConsumedCount
                - statistics.DroppedCount
                - statistics.RejectedCount
                - statistics.DeduplicatedCount);
        var utilization = Math.Round(statistics.QueueUtilization, 2);
        var status = DetermineStatus(statistics);
        var failureReason = DetermineFailureReason(status, statistics);

        return new PipelineHealthDiagnosticSnapshot(
            Available: true,
            OperationName: "pipeline.health.summary",
            ComponentName: "EventPipeline",
            QueueName: queueName,
            Status: status,
            Summary: CreateSummary(status, statistics),
            FailureReason: failureReason,
            RecoveryAction: DetermineRecoveryAction(failureReason),
            Published: statistics.PublishedCount,
            Consumed: statistics.ConsumedCount,
            Dropped: statistics.DroppedCount,
            Rejected: statistics.RejectedCount,
            Deduplicated: statistics.DeduplicatedCount,
            Recovered: statistics.RecoveredCount,
            CurrentQueueSize: statistics.CurrentQueueSize,
            PeakQueueSize: statistics.PeakQueueSize,
            QueueCapacity: statistics.QueueCapacity,
            QueueUtilization: utilization,
            ConsumerLag: consumerLag,
            AverageProcessingTimeUs: Math.Round(statistics.AverageProcessingTimeUs, 2),
            TimeSinceLastFlushMs: Math.Round(statistics.TimeSinceLastFlush.TotalMilliseconds, 2),
            QueueFullMode: statistics.QueueFullMode.ToString(),
            HighWaterMarkWarned: statistics.HighWaterMarkWarned,
            IsWalEnabled: statistics.IsWalEnabled,
            IsValidationEnabled: statistics.IsValidationEnabled,
            IsDeduplicationEnabled: statistics.IsDeduplicationEnabled,
            Timestamp: statistics.Timestamp);
    }

    private static string DetermineStatus(PipelineStatistics statistics)
    {
        if (statistics.QueueCapacity <= 0)
            return "Unknown";

        if (statistics.DroppedCount > 0 || statistics.QueueUtilization >= 95)
            return "Critical";

        if (statistics.QueueUtilization >= 80 || statistics.HighWaterMarkWarned)
            return "Degraded";

        if (statistics.RejectedCount > 0)
            return "Warning";

        return "Healthy";
    }

    private static string DetermineFailureReason(string status, PipelineStatistics statistics)
    {
        if (status == "Unknown")
            return "pipeline.capacity.unknown";
        if (statistics.DroppedCount > 0)
            return "pipeline.events.dropped";
        if (statistics.QueueUtilization >= 95)
            return "pipeline.queue.saturated";
        if (statistics.QueueUtilization >= 80 || statistics.HighWaterMarkWarned)
            return "pipeline.queue.high-water";
        if (statistics.RejectedCount > 0)
            return "pipeline.validation.rejections";

        return "pipeline.nominal";
    }

    private static string DetermineRecoveryAction(string failureReason) => failureReason switch
    {
        "pipeline.events.dropped" => "Reduce ingestion rate, inspect provider burst behavior, and consider increasing pipeline capacity before replaying missing data.",
        "pipeline.queue.saturated" => "Pause or slow producers and inspect storage latency before continuing high-volume ingestion.",
        "pipeline.queue.high-water" => "Watch queue depth and storage flush latency; throttle providers if utilization continues to rise.",
        "pipeline.validation.rejections" => "Review dead-letter or validation diagnostics for rejected event categories.",
        "pipeline.capacity.unknown" => "Verify pipeline registration and queue capacity configuration.",
        _ => "No operator action required."
    };

    private static string CreateSummary(string status, PipelineStatistics statistics)
    {
        if (status == "Healthy")
            return "Pipeline queue is within normal operating range.";
        if (status == "Warning")
            return "Pipeline is processing events but validation rejections are present.";
        if (status == "Degraded")
            return "Pipeline queue is above the high-water mark and may need throttling.";
        if (status == "Critical")
            return statistics.DroppedCount > 0
                ? "Pipeline has dropped events under backpressure."
                : "Pipeline queue is saturated and close to dropping events.";

        return "Pipeline health could not be classified.";
    }
}
