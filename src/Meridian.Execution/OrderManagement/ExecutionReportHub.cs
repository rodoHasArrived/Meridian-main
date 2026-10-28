using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Channels;
using Meridian.Core.Pipeline;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging;
using ExecutionReportDeliveryException = Meridian.Execution.OrderManagementSystem.ExecutionReportDeliveryException;

namespace Meridian.Execution.OrderManagement;

/// <summary>
/// Owns execution-report observers and authoritative subscriber delivery. The lifecycle owner
/// closes registration before draining producers, then completes subscriber and observer writers.
/// </summary>
internal sealed class ExecutionReportHub
{
    private readonly ConcurrentDictionary<long, Subscriber> _subscribers = new();
    private readonly object _registrationSync = new();
    private readonly Channel<ExecutionReport> _observer;
    private readonly OrderManagementSystemOptions _options;
    private readonly Func<string> _gatewayId;
    private readonly ExecutionAuditTrailService? _auditTrail;
    private readonly ILogger _logger;
    private readonly ExecutionFailureState _failures;
    private long _nextSubscriberId;
    private long _droppedReports;
    private bool _closing;

    public ExecutionReportHub(
        OrderManagementSystemOptions options,
        Func<string> gatewayId,
        ExecutionAuditTrailService? auditTrail,
        ILogger logger,
        ExecutionFailureState failures)
    {
        _options = options;
        _gatewayId = gatewayId;
        _auditTrail = auditTrail;
        _logger = logger;
        _failures = failures;
        // This compatibility observer never supplies authoritative state. Slow or absent
        // observers drop oldest reports while the fill's durable effects remain lossless.
        var policy = new EventPipelinePolicy(
            Capacity: options.ValidatedExecutionChannelCapacity,
            FullMode: BoundedChannelFullMode.DropOldest,
            EnableMetrics: false);
        _observer = Channel.CreateBounded<ExecutionReport>(
            policy.ToBoundedOptions(singleReader: true, singleWriter: false),
            dropped =>
            {
                var totalDropped = Interlocked.Increment(ref _droppedReports);
                _logger.LogWarning(
                    "Execution-report observer channel is full; dropped oldest report for order {OrderId} ({ReportType}). Total dropped: {DroppedTotal}",
                    dropped.OrderId, dropped.ReportType, totalDropped);
            });
    }

    public ChannelReader<ExecutionReport> Observer => _observer.Reader;

    public long DroppedReports => Interlocked.Read(ref _droppedReports);

    public Subscription Subscribe(
        int capacity,
        string? subscriberName,
        OrderManagementSystem.UndeliverableExecutionReportHandler? undeliverableHandler)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity,
                "Execution-report subscription capacity must be positive.");

        lock (_registrationSync)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            var subscriberId = Interlocked.Increment(ref _nextSubscriberId);
            var normalizedName = string.IsNullOrWhiteSpace(subscriberName)
                ? $"subscriber-{subscriberId.ToString(CultureInfo.InvariantCulture)}"
                : subscriberName.Trim();
            var channel = Channel.CreateBounded<ExecutionReport>(new BoundedChannelOptions(capacity)
            {
                // Shutdown recovery may race the ordinary consumer after the drain deadline.
                SingleReader = false,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
            var subscriber = new Subscriber(subscriberId, normalizedName, channel,
                undeliverableHandler ?? RetainUndeliverableSubscriptionReportAsync);
            if (!_subscribers.TryAdd(subscriberId, subscriber))
            {
                subscriber.CompleteWriter();
                throw new InvalidOperationException(
                    $"Could not register execution-report subscriber {subscriberId}.");
            }
            return new Subscription(this, subscriber);
        }
    }

    /// <summary>Closes registration only; already-admitted producers may still publish.</summary>
    public void BeginClose()
    {
        lock (_registrationSync)
            _closing = true;
    }

    /// <summary>
    /// Retained per-fill cursor. Its caller serializes attempts with the existing fill gate;
    /// targets captured by the first attempt and successful deliveries survive a failed attempt.
    /// </summary>
    internal sealed class DeliveryCheckpoint
    {
        internal Subscriber[]? Targets;
        internal HashSet<long> DeliveredSubscriberIds { get; } = [];
        internal bool SubscribersPublished;
        public bool IsComplete { get; internal set; }
    }

    public async ValueTask PublishAsync(
        ExecutionReport report,
        DeliveryCheckpoint checkpoint,
        Guid? fillId = null)
    {
        if (checkpoint.IsComplete)
            return;

        if (!checkpoint.SubscribersPublished)
        {
            if (_failures.Failure is { } terminalFailure)
                throw new ExecutionReportDeliveryException(report,
                    $"The authoritative execution-report consumer previously failed: {terminalFailure.Message}");

            var targets = checkpoint.Targets ??= _subscribers.Values
                .Where(static subscriber => !subscriber.IsClosed)
                .OrderBy(static subscriber => subscriber.Id)
                .ToArray();
            foreach (var subscriber in targets)
            {
                if (checkpoint.DeliveredSubscriberIds.Contains(subscriber.Id))
                    continue;
                if (!subscriber.TryPublish(report))
                {
                    await subscriber.AccountUndeliverableAsync(report,
                            subscriber.IsClosed
                                ? $"Subscriber '{subscriber.Name}' closed before accepting the report."
                                : $"Subscriber '{subscriber.Name}' reached its bounded capacity.",
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                checkpoint.DeliveredSubscriberIds.Add(subscriber.Id);
            }
            checkpoint.SubscribersPublished = true;
            checkpoint.Targets = null;
            checkpoint.DeliveredSubscriberIds.Clear();
        }

        // Completion of this best-effort writer is not an authoritative delivery failure.
        if (!_observer.Writer.TryWrite(report))
            _logger.LogDebug(
                "Best-effort execution-report observer was closed before fill {FillId} could be published",
                fillId);
        checkpoint.IsComplete = true;
    }

    public async Task CompleteSubscribersAsync()
    {
        BeginClose();
        var subscribers = _subscribers.Values.ToArray();
        try
        {
            await Task.WhenAll(subscribers.Select(subscriber =>
                    subscriber.CompleteAndFinalizeAsync(_options.ValidatedExecutionSubscriberDrainTimeout)))
                .ConfigureAwait(false);
        }
        finally
        {
            _subscribers.Clear();
        }
    }

    public void CompleteObserver() => _observer.Writer.TryComplete();

    private void MarkFailure(Exception failure, string message) => _failures.Mark(failure, message);

    private async ValueTask UnsubscribeAsync(Subscriber subscriber)
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
            // A captured publication retains this object and accounts through its handler even
            // after removal from the registry, so disposal cannot erase an in-flight target.
            _subscribers.TryRemove(subscriber.Id, out _);
        }
    }

    private async ValueTask FailSubscriptionAsync(Subscriber subscriber, Exception failure)
    {
        MarkFailure(failure, $"Authoritative execution-report subscriber '{subscriber.Name}' failed.");
        await UnsubscribeAsync(subscriber).ConfigureAwait(false);
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
                BrokerName: _gatewayId(),
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

    internal sealed class Subscriber(
        long id,
        string name,
        Channel<ExecutionReport> channel,
        OrderManagementSystem.UndeliverableExecutionReportHandler undeliverableHandler)
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

    internal sealed class Subscription : IAsyncDisposable
    {
        private readonly ChannelReader<ExecutionReport> _reports;
        private readonly object _disposeSync = new();
        private ExecutionReportHub? _owner;
        private Subscriber? _subscriber;
        private Task? _disposeTask;

        internal Subscription(
            ExecutionReportHub owner,
            Subscriber subscriber)
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
            Volatile.Read(ref _owner)?.MarkFailure(
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
                await owner.UnsubscribeAsync(subscriber).ConfigureAwait(false);
            }
        }

        private async Task FailCoreAsync(Exception failure)
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            var subscriber = Interlocked.Exchange(ref _subscriber, null);
            if (owner is not null && subscriber is not null)
            {
                await owner
                    .FailSubscriptionAsync(subscriber, failure)
                    .ConfigureAwait(false);
            }
        }
    }

}
