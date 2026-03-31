using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Temporary observability counters for the provider-registry migration.
/// Tracks factory hit counts, reconnect attempts, and resubscription outcomes
/// so that behavioral parity can be verified during migration phases.
///
/// Part of Phase 0 — Baseline &amp; Safety Rails (refactor-map Step 0.2).
///
/// These counters are intentionally additive: they do not alter runtime behavior
/// and can be removed once the migration is complete (Phase 7).
///
/// NOTE: This class lives in the Core project (not Application) so that
/// Infrastructure can reference it without creating a circular dependency.
/// The namespace is kept as Meridian.Application.Monitoring for
/// consistency with other monitoring abstractions in this namespace.
/// </summary>
public static class MigrationDiagnostics
{

    /// <summary>Total number of streaming client factory invocations.</summary>
    private static long _streamingFactoryHits;

    /// <summary>Total number of backfill provider creation calls.</summary>
    private static long _backfillFactoryHits;

    /// <summary>Total number of symbol-search provider creation calls.</summary>
    private static long _symbolSearchFactoryHits;

    /// <summary>Per-DataSourceKind factory hit counts for streaming clients.</summary>
    private static readonly ConcurrentDictionary<string, long> _streamingFactoryHitsByKind = new();

    public static long StreamingFactoryHits => Interlocked.Read(ref _streamingFactoryHits);
    public static long BackfillFactoryHits => Interlocked.Read(ref _backfillFactoryHits);
    public static long SymbolSearchFactoryHits => Interlocked.Read(ref _symbolSearchFactoryHits);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncStreamingFactoryHit(string dataSourceKind)
    {
        Interlocked.Increment(ref _streamingFactoryHits);
        _streamingFactoryHitsByKind.AddOrUpdate(dataSourceKind, 1, (_, count) => Interlocked.Increment(ref count));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncBackfillFactoryHit() => Interlocked.Increment(ref _backfillFactoryHits);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncSymbolSearchFactoryHit() => Interlocked.Increment(ref _symbolSearchFactoryHits);

    /// <summary>
    /// Gets the streaming factory hit count for a specific data source kind.
    /// </summary>
    public static long GetStreamingFactoryHitsByKind(string dataSourceKind)
    {
        return _streamingFactoryHitsByKind.TryGetValue(dataSourceKind, out var count) ? count : 0;
    }



    /// <summary>Total reconnect attempts across all providers.</summary>
    private static long _reconnectAttempts;

    /// <summary>Total successful reconnections across all providers.</summary>
    private static long _reconnectSuccesses;

    /// <summary>Total failed reconnections across all providers.</summary>
    private static long _reconnectFailures;

    /// <summary>Per-provider reconnect attempt counts.</summary>
    private static readonly ConcurrentDictionary<string, long> _reconnectAttemptsByProvider = new();

    /// <summary>Per-provider reconnect success counts.</summary>
    private static readonly ConcurrentDictionary<string, long> _reconnectSuccessesByProvider = new();

    public static long ReconnectAttempts => Interlocked.Read(ref _reconnectAttempts);
    public static long ReconnectSuccesses => Interlocked.Read(ref _reconnectSuccesses);
    public static long ReconnectFailures => Interlocked.Read(ref _reconnectFailures);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncReconnectAttempt(string provider)
    {
        Interlocked.Increment(ref _reconnectAttempts);
        _reconnectAttemptsByProvider.AddOrUpdate(provider, 1, (_, count) => Interlocked.Increment(ref count));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncReconnectSuccess(string provider)
    {
        Interlocked.Increment(ref _reconnectSuccesses);
        _reconnectSuccessesByProvider.AddOrUpdate(provider, 1, (_, count) => Interlocked.Increment(ref count));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncReconnectFailure(string provider)
    {
        Interlocked.Increment(ref _reconnectFailures);
    }

    /// <summary>
    /// Gets the reconnect attempt count for a specific provider.
    /// </summary>
    public static long GetReconnectAttemptsByProvider(string provider)
    {
        return _reconnectAttemptsByProvider.TryGetValue(provider, out var count) ? count : 0;
    }

    /// <summary>
    /// Gets the reconnect success count for a specific provider.
    /// </summary>
    public static long GetReconnectSuccessesByProvider(string provider)
    {
        return _reconnectSuccessesByProvider.TryGetValue(provider, out var count) ? count : 0;
    }



    /// <summary>Total resubscribe attempts following a reconnect.</summary>
    private static long _resubscribeAttempts;

    /// <summary>Total successful resubscriptions following a reconnect.</summary>
    private static long _resubscribeSuccesses;

    /// <summary>Total failed resubscriptions following a reconnect.</summary>
    private static long _resubscribeFailures;

    public static long ResubscribeAttempts => Interlocked.Read(ref _resubscribeAttempts);
    public static long ResubscribeSuccesses => Interlocked.Read(ref _resubscribeSuccesses);
    public static long ResubscribeFailures => Interlocked.Read(ref _resubscribeFailures);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncResubscribeAttempt() => Interlocked.Increment(ref _resubscribeAttempts);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncResubscribeSuccess() => Interlocked.Increment(ref _resubscribeSuccesses);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncResubscribeFailure() => Interlocked.Increment(ref _resubscribeFailures);



    /// <summary>Total number of providers registered in the registry.</summary>
    private static long _providersRegistered;

    /// <summary>Total number of streaming factories registered.</summary>
    private static long _streamingFactoriesRegistered;

    public static long ProvidersRegistered => Interlocked.Read(ref _providersRegistered);
    public static long StreamingFactoriesRegistered => Interlocked.Read(ref _streamingFactoriesRegistered);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncProviderRegistered() => Interlocked.Increment(ref _providersRegistered);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncStreamingFactoryRegistered() => Interlocked.Increment(ref _streamingFactoriesRegistered);



    /// <summary>
    /// Gets a point-in-time snapshot of all migration diagnostics counters.
    /// </summary>
    public static MigrationDiagnosticsSnapshot GetSnapshot()
    {
        return new MigrationDiagnosticsSnapshot(
            StreamingFactoryHits: StreamingFactoryHits,
            BackfillFactoryHits: BackfillFactoryHits,
            SymbolSearchFactoryHits: SymbolSearchFactoryHits,
            StreamingFactoryHitsByKind: new Dictionary<string, long>(_streamingFactoryHitsByKind),
            ReconnectAttempts: ReconnectAttempts,
            ReconnectSuccesses: ReconnectSuccesses,
            ReconnectFailures: ReconnectFailures,
            ReconnectAttemptsByProvider: new Dictionary<string, long>(_reconnectAttemptsByProvider),
            ReconnectSuccessesByProvider: new Dictionary<string, long>(_reconnectSuccessesByProvider),
            ResubscribeAttempts: ResubscribeAttempts,
            ResubscribeSuccesses: ResubscribeSuccesses,
            ResubscribeFailures: ResubscribeFailures,
            ProvidersRegistered: ProvidersRegistered,
            StreamingFactoriesRegistered: StreamingFactoriesRegistered,
            Timestamp: DateTimeOffset.UtcNow
        );
    }

    /// <summary>
    /// Resets all counters to zero. Intended for test isolation only.
    /// </summary>
    public static void Reset()
    {
        Interlocked.Exchange(ref _streamingFactoryHits, 0);
        Interlocked.Exchange(ref _backfillFactoryHits, 0);
        Interlocked.Exchange(ref _symbolSearchFactoryHits, 0);
        _streamingFactoryHitsByKind.Clear();

        Interlocked.Exchange(ref _reconnectAttempts, 0);
        Interlocked.Exchange(ref _reconnectSuccesses, 0);
        Interlocked.Exchange(ref _reconnectFailures, 0);
        _reconnectAttemptsByProvider.Clear();
        _reconnectSuccessesByProvider.Clear();

        Interlocked.Exchange(ref _resubscribeAttempts, 0);
        Interlocked.Exchange(ref _resubscribeSuccesses, 0);
        Interlocked.Exchange(ref _resubscribeFailures, 0);

        Interlocked.Exchange(ref _providersRegistered, 0);
        Interlocked.Exchange(ref _streamingFactoriesRegistered, 0);
    }

}

/// <summary>
/// Immutable snapshot of migration diagnostics counters at a point in time.
/// </summary>
public readonly record struct MigrationDiagnosticsSnapshot(
    long StreamingFactoryHits,
    long BackfillFactoryHits,
    long SymbolSearchFactoryHits,
    IReadOnlyDictionary<string, long> StreamingFactoryHitsByKind,
    long ReconnectAttempts,
    long ReconnectSuccesses,
    long ReconnectFailures,
    IReadOnlyDictionary<string, long> ReconnectAttemptsByProvider,
    IReadOnlyDictionary<string, long> ReconnectSuccessesByProvider,
    long ResubscribeAttempts,
    long ResubscribeSuccesses,
    long ResubscribeFailures,
    long ProvidersRegistered,
    long StreamingFactoriesRegistered,
    DateTimeOffset Timestamp
);
