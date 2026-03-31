using System.Threading.Channels;
using Meridian.Contracts.Pipeline;

namespace Meridian.Application.Pipeline;

/// <summary>
/// Central configuration policy for bounded-channel pipelines.
/// Provides consistent backpressure settings across the application.
/// <para>
/// Use static presets for common scenarios:
/// <list type="bullet">
///   <item><see cref="Default"/> - General-purpose event pipelines (100k capacity, DropOldest)</item>
///   <item><see cref="HighThroughput"/> - Streaming data pipelines (50k capacity, DropOldest)</item>
///   <item><see cref="MessageBuffer"/> - Internal message buffering (50k capacity, DropOldest, no metrics)</item>
///   <item><see cref="MaintenanceQueue"/> - Background tasks (100 capacity, Wait/backpressure)</item>
///   <item><see cref="Logging"/> - Log channels (1k capacity, DropOldest)</item>
///   <item><see cref="CompletionQueue"/> - Completion notifications (500 capacity, Wait/backpressure)</item>
/// </list>
/// </para>
/// </summary>
/// <remarks>
/// All channel creation in the application should use this policy to ensure consistent
/// backpressure behavior. Use <see cref="CreateChannel{T}"/> for direct channel creation
/// or <see cref="ToBoundedOptions"/> when you need to configure additional options.
/// </remarks>
public sealed record EventPipelinePolicy(
    int Capacity = PipelinePolicyConstants.DefaultCapacity,
    BoundedChannelFullMode FullMode = BoundedChannelFullMode.DropOldest,
    bool EnableMetrics = true)
{

    /// <summary>
    /// Default policy for general-purpose event pipelines.
    /// High capacity (100k), drops oldest on overflow, metrics enabled.
    /// </summary>
    public static EventPipelinePolicy Default { get; } = new(
        PipelinePolicyConstants.DefaultCapacity,
        BoundedChannelFullMode.DropOldest,
        EnableMetrics: true);

    /// <summary>
    /// Policy for high-throughput streaming data pipelines (e.g., market data clients).
    /// Moderate capacity (50k), drops oldest on overflow, metrics enabled for monitoring.
    /// </summary>
    public static EventPipelinePolicy HighThroughput { get; } = new(
        PipelinePolicyConstants.HighThroughputCapacity,
        BoundedChannelFullMode.DropOldest,
        EnableMetrics: true);

    /// <summary>
    /// Policy for internal message buffering channels (e.g., StockSharp message buffer).
    /// Moderate capacity (50k), drops oldest on overflow, metrics disabled for performance.
    /// </summary>
    public static EventPipelinePolicy MessageBuffer { get; } = new(
        PipelinePolicyConstants.MessageBufferCapacity,
        BoundedChannelFullMode.DropOldest,
        EnableMetrics: false);

    /// <summary>
    /// Policy for background task/maintenance queues where no messages should be dropped.
    /// Low capacity (100), waits when full (backpressure), metrics disabled.
    /// </summary>
    public static EventPipelinePolicy MaintenanceQueue { get; } = new(
        PipelinePolicyConstants.MaintenanceQueueCapacity,
        BoundedChannelFullMode.Wait,
        EnableMetrics: false);

    /// <summary>
    /// Policy for logging channels.
    /// Low capacity (1k), drops oldest on overflow, metrics disabled.
    /// </summary>
    public static EventPipelinePolicy Logging { get; } = new(
        PipelinePolicyConstants.LoggingCapacity,
        BoundedChannelFullMode.DropOldest,
        EnableMetrics: false);

    /// <summary>
    /// Policy for completion notification channels (e.g., backfill request completions).
    /// Moderate capacity (500), waits when full (no drops), metrics disabled.
    /// Use this for channels where completion notifications must not be lost.
    /// </summary>
    public static EventPipelinePolicy CompletionQueue { get; } = new(
        PipelinePolicyConstants.CompletionQueueCapacity,
        BoundedChannelFullMode.Wait,
        EnableMetrics: false);



    /// <summary>
    /// Creates a bounded channel with this policy's configuration.
    /// This is the preferred method for creating channels to ensure consistent backpressure behavior.
    /// </summary>
    /// <typeparam name="T">The type of items in the channel.</typeparam>
    /// <param name="singleReader">Whether there is a single consumer reading from the channel. Default is true.</param>
    /// <param name="singleWriter">Whether there is a single producer writing to the channel. Default is false.</param>
    /// <returns>A configured bounded channel.</returns>
    /// <example>
    /// <code>
    /// // Using a preset
    /// var channel = EventPipelinePolicy.Logging.CreateChannel&lt;LogEntry&gt;();
    ///
    /// // Using default policy
    /// var channel = EventPipelinePolicy.Default.CreateChannel&lt;MarketEvent&gt;();
    /// </code>
    /// </example>
    public Channel<T> CreateChannel<T>(bool singleReader = true, bool singleWriter = false)
    {
        return Channel.CreateBounded<T>(ToBoundedOptions(singleReader, singleWriter));
    }

    /// <summary>
    /// Creates a <see cref="BoundedChannelOptions"/> instance from this policy.
    /// Use this when you need to further customize channel options before creation.
    /// </summary>
    /// <param name="singleReader">Whether there is a single consumer reading from the channel.</param>
    /// <param name="singleWriter">Whether there is a single producer writing to the channel.</param>
    /// <returns>Configured <see cref="BoundedChannelOptions"/>.</returns>
    public BoundedChannelOptions ToBoundedOptions(bool singleReader, bool singleWriter)
    {
        if (Capacity <= 0)
            throw new ArgumentOutOfRangeException("capacity", Capacity, "Capacity must be positive.");

        return new BoundedChannelOptions(Capacity)
        {
            FullMode = FullMode,
            SingleReader = singleReader,
            SingleWriter = singleWriter,
            AllowSynchronousContinuations = false
        };
    }

}
