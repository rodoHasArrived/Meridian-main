namespace Meridian.Execution;

/// <summary>
/// Runtime options for <see cref="OrderManagementSystem"/>.
/// </summary>
public sealed class OrderManagementSystemOptions
{
    public const string SectionKey = "Execution:OrderManagement";

    public const int DefaultMaxRetainedOrders = 5_000;
    public const int DefaultExecutionChannelCapacity = 1_000;
    public const int DefaultCancelAllMaxConcurrency = 8;

    /// <summary>
    /// Maximum number of order states retained in memory before completed terminal orders are trimmed.
    /// </summary>
    public int MaxRetainedOrders { get; init; } = DefaultMaxRetainedOrders;

    /// <summary>
    /// Bounded execution-report channel capacity used by portfolio and audit subscribers.
    /// </summary>
    public int ExecutionChannelCapacity { get; init; } = DefaultExecutionChannelCapacity;

    /// <summary>
    /// Maximum number of broker cancel requests issued concurrently by <see cref="IOrderManager.CancelAllAsync"/>.
    /// </summary>
    public int CancelAllMaxConcurrency { get; init; } = DefaultCancelAllMaxConcurrency;

    /// <summary>
    /// Requires risk, portfolio-state, and operator-control dependencies during construction.
    /// Supported production composition enables this so order routing cannot silently degrade.
    /// </summary>
    public bool RequireProductionSafetyDependencies { get; init; }

    public int ValidatedMaxRetainedOrders => MaxRetainedOrders > 0
        ? MaxRetainedOrders
        : DefaultMaxRetainedOrders;

    public int ValidatedExecutionChannelCapacity => ExecutionChannelCapacity > 0
        ? ExecutionChannelCapacity
        : DefaultExecutionChannelCapacity;

    public int ValidatedCancelAllMaxConcurrency => CancelAllMaxConcurrency > 0
        ? CancelAllMaxConcurrency
        : DefaultCancelAllMaxConcurrency;
}
