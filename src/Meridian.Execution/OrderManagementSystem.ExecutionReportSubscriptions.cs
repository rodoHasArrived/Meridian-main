using System.Threading.Channels;
using Meridian.Execution.Sdk;
using Meridian.Execution.OrderManagement;

namespace Meridian.Execution;

/// <summary>
/// Accounted execution-report subscriptions for consumers that participate in the authoritative
/// fill path. The legacy <see cref="OrderManagementSystem.ExecutionReports"/> reader remains a
/// best-effort compatibility observer and must not be used for strategy or accounting state.
/// </summary>
public sealed partial class OrderManagementSystem
{
    /// <summary>
    /// Handles a report that could not be admitted to, or drained by, an authoritative subscriber.
    /// Returning successfully declares that the report was durably retained or its owning outcome
    /// was explicitly failed. Throwing fails the OMS fill path instead of silently losing the fill.
    /// </summary>
    public delegate ValueTask UndeliverableExecutionReportHandler(
        ExecutionReport report,
        string reason,
        CancellationToken ct);

    /// <summary>
    /// Creates an independent, bounded execution-report subscription. Every report is either
    /// admitted to the subscriber or passed to an explicit accounting handler; a slow subscriber
    /// never blocks unrelated subscribers or the gateway report pump indefinitely.
    /// </summary>
    public ExecutionReportSubscription SubscribeLosslessExecutionReports()
        => SubscribeLosslessExecutionReports(_options.ValidatedExecutionChannelCapacity);

    /// <summary>
    /// Creates an independent accounted execution-report subscription with the specified bounded
    /// <paramref name="capacity"/>. When no custom handler is supplied, the durable execution audit
    /// trail is used; without one, an undeliverable report fails explicitly.
    /// </summary>
    public ExecutionReportSubscription SubscribeLosslessExecutionReports(int capacity)
        => SubscribeLosslessExecutionReports(
            capacity,
            subscriberName: null,
            undeliverableHandler: null);

    /// <summary>
    /// Creates an accounted subscription with the OMS-wide validated capacity and a
    /// consumer-owned durable recovery or fail-closed handler.
    /// </summary>
    public ExecutionReportSubscription SubscribeLosslessExecutionReports(
        string? subscriberName,
        UndeliverableExecutionReportHandler? undeliverableHandler)
        => SubscribeLosslessExecutionReports(
            _options.ValidatedExecutionChannelCapacity,
            subscriberName,
            undeliverableHandler);

    /// <summary>
    /// Creates an independent accounted subscription with a consumer-owned durable recovery or
    /// fail-closed handler for reports that cannot enter its bounded inbox.
    /// </summary>
    public ExecutionReportSubscription SubscribeLosslessExecutionReports(
        int capacity,
        string? subscriberName,
        UndeliverableExecutionReportHandler? undeliverableHandler)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                "Execution-report subscription capacity must be positive.");
        }

        lock (_disposeSync)
        {
            ObjectDisposedException.ThrowIf(_disposeStarted != 0, this);
            return new ExecutionReportSubscription(
                _executionReportHub.Subscribe(capacity, subscriberName, undeliverableHandler));
        }
    }

    /// <summary>Consumer-owned handle for one independent accounted execution-report stream.</summary>
    public sealed class ExecutionReportSubscription : IAsyncDisposable
    {
        private readonly ExecutionReportHub.Subscription _subscription;

        internal ExecutionReportSubscription(ExecutionReportHub.Subscription subscription)
            => _subscription = subscription;

        /// <summary>Reports delivered only to this subscription, in OMS publication order.</summary>
        public ChannelReader<ExecutionReport> Reports => _subscription.Reports;

        /// <inheritdoc />
        public ValueTask DisposeAsync() => _subscription.DisposeAsync();

        /// <summary>
        /// Fails this authoritative consumer and closes OMS order admission. Consumers call this
        /// when a report they accepted cannot be delivered or durably accounted; merely disposing
        /// would remove the only consumer and allow later fills to pass without one.
        /// </summary>
        public ValueTask FailAsync(Exception failure) => _subscription.FailAsync(failure);
    }

    /// <summary>Raised when an authoritative execution report has neither delivery nor durable accounting.</summary>
    public sealed class ExecutionReportDeliveryException : Exception
    {
        public ExecutionReportDeliveryException(ExecutionReport report, string message)
            : base($"Execution report for order '{report.ClientOrderId ?? report.OrderId}' was not delivered: {message}")
        {
            Report = report;
        }

        public ExecutionReport Report { get; }
    }
}
