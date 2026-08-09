using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Channels;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;

namespace Meridian.Execution;

/// <summary>
/// Accounted execution-report subscriptions for consumers that participate in the authoritative
/// fill path. The legacy <see cref="OrderManagementSystem.ExecutionReports"/> reader remains a
/// best-effort compatibility observer and must not be used for strategy or accounting state.
/// </summary>
public sealed partial class OrderManagementSystem
{
    private readonly ConcurrentDictionary<long, LosslessExecutionReportSubscriber>
        _losslessExecutionReportSubscribers = new();
    private long _nextLosslessExecutionReportSubscriberId;

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

            var subscriberId = Interlocked.Increment(ref _nextLosslessExecutionReportSubscriberId);
            var normalizedName = string.IsNullOrWhiteSpace(subscriberName)
                ? $"subscriber-{subscriberId.ToString(CultureInfo.InvariantCulture)}"
                : subscriberName.Trim();
            var channel = Channel.CreateBounded<ExecutionReport>(new BoundedChannelOptions(capacity)
            {
                // Normal delivery has one consumer, but shutdown recovery may take ownership
                // after the drain deadline while that consumer is still unwinding. Do not make
                // the single-reader optimization promise across that recovery race.
                SingleReader = false,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
            var subscriber = new LosslessExecutionReportSubscriber(
                subscriberId,
                normalizedName,
                channel,
                undeliverableHandler ?? RetainUndeliverableSubscriptionReportAsync);
            if (!_losslessExecutionReportSubscribers.TryAdd(subscriberId, subscriber))
            {
                subscriber.CompleteWriter();
                throw new InvalidOperationException(
                    $"Could not register execution-report subscriber {subscriberId}.");
            }

            return new ExecutionReportSubscription(this, subscriber);
        }
    }

    private async ValueTask PublishToLosslessExecutionReportSubscribersAsync(
        FillProcessingProgress progress)
    {
        if (Volatile.Read(ref _terminalReportPumpFailure) is { } terminalFailure)
        {
            throw new ExecutionReportDeliveryException(
                progress.FillIncrement,
                $"The authoritative execution-report consumer previously failed: {terminalFailure.Message}");
        }

        var targets = progress.LosslessSubscriberTargets ??=
            _losslessExecutionReportSubscribers.Values
                .Where(static subscriber => !subscriber.IsClosed)
                .OrderBy(static subscriber => subscriber.Id)
                .ToArray();

        foreach (var subscriber in targets)
        {
            var subscriberId = subscriber.Id;
            if (progress.DeliveredLosslessSubscriberIds.Contains(subscriberId))
            {
                continue;
            }

            if (!subscriber.TryPublish(progress.FillIncrement))
            {
                await subscriber.AccountUndeliverableAsync(
                        progress.FillIncrement,
                        subscriber.IsClosed
                            ? $"Subscriber '{subscriber.Name}' closed before accepting the report."
                            : $"Subscriber '{subscriber.Name}' reached its bounded capacity.",
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            progress.DeliveredLosslessSubscriberIds.Add(subscriberId);
        }

        progress.LosslessSubscribersPublished = true;
        progress.ReleaseLosslessSubscriberProgress();
    }

    private async ValueTask RetainUndeliverableSubscriptionReportAsync(
        ExecutionReport report,
        string reason,
        CancellationToken ct)
    {
        if (_auditTrail is null)
        {
            throw new ExecutionReportDeliveryException(
                report,
                $"{reason} No durable execution audit trail is configured.");
        }

        var orderId = report.ClientOrderId ?? report.OrderId;
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["reportType"] = report.ReportType.ToString(),
            ["orderStatus"] = report.OrderStatus.ToString(),
            ["side"] = report.Side.ToString(),
            ["orderQuantity"] = report.OrderQuantity.ToString(CultureInfo.InvariantCulture),
            ["filledQuantity"] = report.FilledQuantity.ToString(CultureInfo.InvariantCulture),
            ["fillPrice"] = report.FillPrice?.ToString(CultureInfo.InvariantCulture) ?? "missing",
            ["commission"] = report.Commission?.ToString(CultureInfo.InvariantCulture) ?? "missing",
            ["reportTimestampUtc"] = report.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["clientOrderId"] = report.ClientOrderId ?? string.Empty,
            ["gatewayOrderId"] = report.GatewayOrderId ?? string.Empty
        };
        await _auditTrail.RecordAsync(new ExecutionAuditEntry(
                AuditId: $"audit-{Guid.NewGuid():N}",
                Category: "Execution",
                Action: "ExecutionReportDeliveryRetained",
                Outcome: "AttentionRequired",
                OccurredAt: DateTimeOffset.UtcNow,
                Actor: "order-management-system",
                BrokerName: _gateway.GatewayId,
                OrderId: orderId,
                Symbol: report.Symbol,
                CorrelationId: orderId,
                Message: reason,
                Reason: "authoritative-subscriber-undeliverable",
                Scope: $"subscriber-delivery/order:{orderId}",
                Metadata: metadata), ct)
            .ConfigureAwait(false);

        _logger.LogError(
            "Execution report for order {OrderId} could not reach an authoritative subscriber; durable recovery evidence was retained: {Reason}",
            orderId,
            reason);
    }

    private async ValueTask UnsubscribeLosslessExecutionReportsAsync(
        LosslessExecutionReportSubscriber subscriber)
    {
        try
        {
            await subscriber.AbandonAsync(
                    $"Subscriber '{subscriber.Name}' was disposed before its accepted reports were drained.",
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            // Publication snapshots hold the subscriber object itself, so removing the registry
            // entry cannot make a concurrent fill disappear. A snapshot racing this disposal
            // observes the closed object and runs its explicit accounting handler.
            _losslessExecutionReportSubscribers.TryRemove(subscriber.Id, out _);
        }
    }

    private async ValueTask FailLosslessExecutionReportSubscriptionAsync(
        LosslessExecutionReportSubscriber subscriber,
        Exception failure)
    {
        MarkTerminalReportPumpFailure(
            failure,
            $"Authoritative execution-report subscriber '{subscriber.Name}' failed.");
        await UnsubscribeLosslessExecutionReportsAsync(subscriber).ConfigureAwait(false);
    }

    private async Task CloseLosslessExecutionReportSubscriptionsAsync()
    {
        var subscribers = _losslessExecutionReportSubscribers.Values.ToArray();
        try
        {
            await Task.WhenAll(subscribers.Select(subscriber =>
                    subscriber.CompleteAndFinalizeAsync(_options.ValidatedExecutionSubscriberDrainTimeout)))
                .ConfigureAwait(false);
        }
        finally
        {
            _losslessExecutionReportSubscribers.Clear();
        }
    }

    internal sealed class LosslessExecutionReportSubscriber(
        long id,
        string name,
        Channel<ExecutionReport> channel,
        UndeliverableExecutionReportHandler undeliverableHandler)
    {
        private int _closed;

        public long Id { get; } = id;

        public string Name { get; } = name;

        public ChannelReader<ExecutionReport> Reader { get; } = channel.Reader;

        private ChannelWriter<ExecutionReport> Writer { get; } = channel.Writer;

        public bool IsClosed => Volatile.Read(ref _closed) != 0;

        public bool TryPublish(ExecutionReport report)
            => !IsClosed && Writer.TryWrite(report);

        public void CompleteWriter()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 0)
            {
                Writer.TryComplete();
            }
        }

        public ValueTask AccountUndeliverableAsync(
            ExecutionReport report,
            string reason,
            CancellationToken ct)
            => undeliverableHandler(report, reason, ct);

        public async ValueTask AbandonAsync(string reason, CancellationToken ct)
        {
            CompleteWriter();
            await AccountBufferedReportsAsync(reason, ct).ConfigureAwait(false);
        }

        public async Task CompleteAndFinalizeAsync(TimeSpan drainTimeout)
        {
            CompleteWriter();
            try
            {
                await Reader.Completion.WaitAsync(drainTimeout).ConfigureAwait(false);
                return;
            }
            catch (TimeoutException)
            {
                // The reader did not consume accepted reports within the bounded shutdown window.
                // Take ownership of what remains and route each report through its explicit
                // recovery/failure handler instead of declaring the abandoned delivery settled.
            }

            await AccountBufferedReportsAsync(
                    $"Subscriber '{Name}' did not drain before the {drainTimeout} shutdown deadline.",
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        private async Task AccountBufferedReportsAsync(string reason, CancellationToken ct)
        {
            List<Exception>? failures = null;
            while (Reader.TryRead(out var report))
            {
                try
                {
                    await undeliverableHandler(report, reason, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    (failures ??= []).Add(ex);
                }
            }

            if (failures is { Count: 1 })
            {
                throw failures[0];
            }

            if (failures is { Count: > 1 })
            {
                throw new AggregateException(
                    $"{failures.Count} execution reports could not be accounted for subscriber '{Name}'.",
                    failures);
            }
        }
    }

    /// <summary>
    /// Consumer-owned handle for one independent accounted execution-report stream.
    /// </summary>
    public sealed class ExecutionReportSubscription : IAsyncDisposable
    {
        private readonly ChannelReader<ExecutionReport> _reports;
        private readonly object _disposeSync = new();
        private OrderManagementSystem? _owner;
        private LosslessExecutionReportSubscriber? _subscriber;
        private Task? _disposeTask;

        internal ExecutionReportSubscription(
            OrderManagementSystem owner,
            LosslessExecutionReportSubscriber subscriber)
        {
            _owner = owner;
            _subscriber = subscriber;
            _reports = subscriber.Reader;
        }

        /// <summary>Reports delivered only to this subscription, in OMS publication order.</summary>
        public ChannelReader<ExecutionReport> Reports => _reports;

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            lock (_disposeSync)
            {
                _disposeTask ??= DisposeCoreAsync();
                return new ValueTask(_disposeTask);
            }
        }

        /// <summary>
        /// Fails this authoritative consumer and closes OMS order admission. Consumers call this
        /// when a report they accepted cannot be delivered or durably accounted; merely disposing
        /// would remove the only consumer and allow later fills to pass without one.
        /// </summary>
        public ValueTask FailAsync(Exception failure)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Volatile.Read(ref _owner)?.MarkTerminalReportPumpFailure(
                failure,
                "An authoritative execution-report consumer failed after accepting a report.");

            lock (_disposeSync)
            {
                _disposeTask ??= FailCoreAsync(failure);
                return new ValueTask(_disposeTask);
            }
        }

        private async Task DisposeCoreAsync()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            var subscriber = Interlocked.Exchange(ref _subscriber, null);
            if (owner is not null && subscriber is not null)
            {
                await owner.UnsubscribeLosslessExecutionReportsAsync(subscriber).ConfigureAwait(false);
            }
        }

        private async Task FailCoreAsync(Exception failure)
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            var subscriber = Interlocked.Exchange(ref _subscriber, null);
            if (owner is not null && subscriber is not null)
            {
                await owner
                    .FailLosslessExecutionReportSubscriptionAsync(subscriber, failure)
                    .ConfigureAwait(false);
            }
        }
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
