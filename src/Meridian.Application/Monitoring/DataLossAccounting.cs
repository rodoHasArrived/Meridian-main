using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Meridian.Application.Monitoring;

/// <summary>
/// End-to-end data loss accounting across the entire event pipeline.
/// Tracks events at each stage: ingestion → validation → pipeline → storage,
/// enabling reconciliation to detect unexplained data loss.
/// </summary>
public static class DataLossAccounting
{
    // Stage 1: Provider ingestion (events received from external sources)
    private static long _received;
    private static long _receivedDuplicates;

    // Stage 2: Validation (events checked by validators)
    private static long _validated;
    private static long _rejected;

    // Stage 3: Pipeline (events entering the bounded channel)
    private static long _pipelineAccepted;
    private static long _pipelineDropped;

    // Stage 4: Storage (events persisted to disk)
    private static long _stored;
    private static long _storeFailed;

    // Per-provider breakdown
    private static readonly ConcurrentDictionary<string, ProviderCounters> _providerCounters
        = new(StringComparer.OrdinalIgnoreCase);

    #region Stage 1: Ingestion

    /// <summary>Total events received from all providers.</summary>
    public static long Received => Interlocked.Read(ref _received);

    /// <summary>Events identified as duplicates at ingestion.</summary>
    public static long ReceivedDuplicates => Interlocked.Read(ref _receivedDuplicates);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncReceived(string? provider = null)
    {
        Interlocked.Increment(ref _received);
        if (provider != null)
            GetProvider(provider).IncReceived();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncReceivedDuplicate(string? provider = null)
    {
        Interlocked.Increment(ref _receivedDuplicates);
        if (provider != null)
            GetProvider(provider).IncDuplicate();
    }

    #endregion

    #region Stage 2: Validation

    /// <summary>Events that passed validation.</summary>
    public static long Validated => Interlocked.Read(ref _validated);

    /// <summary>Events rejected by validators.</summary>
    public static long Rejected => Interlocked.Read(ref _rejected);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncValidated() => Interlocked.Increment(ref _validated);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncRejected() => Interlocked.Increment(ref _rejected);

    #endregion

    #region Stage 3: Pipeline

    /// <summary>Events accepted into the pipeline channel.</summary>
    public static long PipelineAccepted => Interlocked.Read(ref _pipelineAccepted);

    /// <summary>Events dropped by pipeline backpressure.</summary>
    public static long PipelineDropped => Interlocked.Read(ref _pipelineDropped);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncPipelineAccepted() => Interlocked.Increment(ref _pipelineAccepted);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncPipelineDropped() => Interlocked.Increment(ref _pipelineDropped);

    #endregion

    #region Stage 4: Storage

    /// <summary>Events successfully persisted to storage.</summary>
    public static long Stored => Interlocked.Read(ref _stored);

    /// <summary>Events that failed to persist.</summary>
    public static long StoreFailed => Interlocked.Read(ref _storeFailed);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncStored() => Interlocked.Increment(ref _stored);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncStoreFailed() => Interlocked.Increment(ref _storeFailed);

    #endregion

    #region Reconciliation

    /// <summary>
    /// Generates a full reconciliation report across all pipeline stages.
    /// The formula: Received = Duplicates + Rejected + PipelineDropped + StoreFailed + Stored + Unaccounted
    /// </summary>
    public static ReconciliationReport GetReconciliationReport()
    {
        var received = Received;
        var duplicates = ReceivedDuplicates;
        var rejected = Rejected;
        var pipelineDropped = PipelineDropped;
        var storeFailed = StoreFailed;
        var stored = Stored;

        var accountedFor = duplicates + rejected + pipelineDropped + storeFailed + stored;
        var unaccounted = received - accountedFor;
        var reconciliationRate = received > 0
            ? 1.0 - (double)Math.Abs(unaccounted) / received
            : 1.0;

        var providerBreakdown = new Dictionary<string, ProviderReconciliation>();
        foreach (var (name, counters) in _providerCounters)
        {
            providerBreakdown[name] = new ProviderReconciliation(
                Received: counters.Received,
                Duplicates: counters.Duplicates);
        }

        return new ReconciliationReport(
            Timestamp: DateTimeOffset.UtcNow,
            Received: received,
            Duplicates: duplicates,
            Validated: Validated,
            Rejected: rejected,
            PipelineAccepted: PipelineAccepted,
            PipelineDropped: pipelineDropped,
            Stored: stored,
            StoreFailed: storeFailed,
            Unaccounted: unaccounted,
            ReconciliationRate: reconciliationRate,
            ProviderBreakdown: providerBreakdown);
    }

    /// <summary>
    /// Resets all accounting counters. Useful for testing.
    /// </summary>
    public static void Reset()
    {
        Interlocked.Exchange(ref _received, 0);
        Interlocked.Exchange(ref _receivedDuplicates, 0);
        Interlocked.Exchange(ref _validated, 0);
        Interlocked.Exchange(ref _rejected, 0);
        Interlocked.Exchange(ref _pipelineAccepted, 0);
        Interlocked.Exchange(ref _pipelineDropped, 0);
        Interlocked.Exchange(ref _stored, 0);
        Interlocked.Exchange(ref _storeFailed, 0);
        _providerCounters.Clear();
    }

    #endregion

    private static ProviderCounters GetProvider(string provider)
        => _providerCounters.GetOrAdd(provider, _ => new ProviderCounters());

    private sealed class ProviderCounters
    {
        private long _received;
        private long _duplicates;

        public long Received => Interlocked.Read(ref _received);
        public long Duplicates => Interlocked.Read(ref _duplicates);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void IncReceived() => Interlocked.Increment(ref _received);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void IncDuplicate() => Interlocked.Increment(ref _duplicates);
    }
}

/// <summary>
/// Full pipeline reconciliation report.
/// Invariant: Received == Duplicates + Rejected + PipelineDropped + StoreFailed + Stored + Unaccounted
/// </summary>
public readonly record struct ReconciliationReport(
    DateTimeOffset Timestamp,
    long Received,
    long Duplicates,
    long Validated,
    long Rejected,
    long PipelineAccepted,
    long PipelineDropped,
    long Stored,
    long StoreFailed,
    long Unaccounted,
    double ReconciliationRate,
    Dictionary<string, ProviderReconciliation> ProviderBreakdown)
{
    /// <summary>
    /// Whether the pipeline is fully reconciled (no unexplained loss).
    /// </summary>
    public bool IsFullyReconciled => Unaccounted == 0;

    /// <summary>
    /// Overall loss rate as a percentage.
    /// </summary>
    public double LossRatePercent => Received > 0
        ? (double)(Duplicates + Rejected + PipelineDropped + StoreFailed) / Received * 100
        : 0;
}

/// <summary>
/// Per-provider reconciliation data.
/// </summary>
public readonly record struct ProviderReconciliation(
    long Received,
    long Duplicates);
