using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Streaming;

/// <summary>
/// Event-driven report-run fan-out. A report-run-typed configuration of
/// <see cref="StreamBroadcaster{TPayload}"/>: implements <see cref="IReportingRunNotifier"/> so run
/// persistence wakes it, rebuilds each watched run's status/audit payload on the shared coalescing
/// loop, and pushes it to every subscriber of that run. Because report-run topics are keyed by an
/// unbounded set of run ids, empty topics are evicted on last unsubscribe so they cannot leak.
/// </summary>
public sealed class ReportRunStreamBroadcaster : IReportingRunNotifier, IAsyncDisposable
{
    private readonly StreamBroadcaster<ReportingRunAuditTrailDto> _engine;

    public ReportRunStreamBroadcaster(IServiceProvider services, StreamConnectionRegistry registry, QuoteStreamOptions options)
        : this(services, registry, options, FundStructureEndpoints.TryBuildReportRunAuditTrail)
    {
    }

    /// <summary>Test seam: inject the payload builder so tests don't need the full DI graph.</summary>
    internal ReportRunStreamBroadcaster(
        IServiceProvider services,
        StreamConnectionRegistry registry,
        QuoteStreamOptions options,
        Func<IServiceProvider, string, ReportingRunAuditTrailDto?> payloadFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(payloadFactory);

        _engine = new StreamBroadcaster<ReportingRunAuditTrailDto>(
            registry,
            topic => payloadFactory(services, topic.Argument),
            ReportingRunAuditTrailComparer.Instance,
            options.CoalesceIntervalMs,
            options.SubscriberChannelCapacity,
            // Run-id topics are an unbounded universe — evict empty topics so they cannot leak.
            evictEmptyTopics: true,
            logger: services.GetService<ILoggerFactory>()?.CreateLogger<ReportRunStreamBroadcaster>());
    }

    /// <inheritdoc />
    public void NotifyRunChanged(string runId) => _engine.Wake(StreamTopic.ReportRun(runId));

    public void NotifyRunChanged(
        string tenantId,
        string? companyId,
        string runId)
    {
        if (string.IsNullOrWhiteSpace(companyId))
        {
            NotifyRunChanged(runId);
            return;
        }

        _engine.Wake(StreamTopic.ReportRun(tenantId, companyId, runId));
    }

    /// <summary>Test hook: live topic count — locks in that this stream evicts empty run-id topics.</summary>
    internal int ActiveTopicCount => _engine.ActiveTopicCount;

    /// <summary>
    /// Subscribe to a report-run topic's coalesced status feed. Returns null when the session's
    /// concurrent-stream cap is exceeded. Dispose to unsubscribe and release the session's slot.
    /// </summary>
    public StreamSubscription<ReportingRunAuditTrailDto>? TrySubscribe(StreamTopic topic, string sessionId)
        => _engine.TrySubscribe(topic, sessionId);

    /// <summary>Test hook: drive one coalesced fan-out round without waiting for the loop timer.</summary>
    internal void PublishPending() => _engine.PublishPending();

    public ValueTask DisposeAsync() => _engine.DisposeAsync();

    /// <summary>
    /// Coalesce comparison for run payloads. The record's <c>Entries</c> list defeats default record
    /// equality (which reference-compares the list), so compare structurally by status, attempt
    /// count, and audit entries — any change (notably an approval transition, which appends an entry)
    /// forces a push.
    /// </summary>
    private sealed class ReportingRunAuditTrailComparer : IEqualityComparer<ReportingRunAuditTrailDto>
    {
        public static readonly ReportingRunAuditTrailComparer Instance = new();

        public bool Equals(ReportingRunAuditTrailDto? x, ReportingRunAuditTrailDto? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            if (!string.Equals(x.Status, y.Status, StringComparison.Ordinal)
                || x.AttemptCount != y.AttemptCount
                || x.Entries.Count != y.Entries.Count)
            {
                return false;
            }

            for (var i = 0; i < x.Entries.Count; i++)
            {
                // ReportingRunAuditEntryDto is a record — Default uses its structural equality.
                if (!EqualityComparer<ReportingRunAuditEntryDto>.Default.Equals(x.Entries[i], y.Entries[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(ReportingRunAuditTrailDto obj)
            => HashCode.Combine(obj.Status, obj.AttemptCount, obj.Entries.Count);
    }
}
