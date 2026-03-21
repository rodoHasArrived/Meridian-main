using System.Collections.Concurrent;

namespace Meridian.Application.Canonicalization;

/// <summary>
/// Abstraction over canonicalization counters that enables DI injection and test isolation.
/// Register the default implementation (<see cref="DefaultCanonicalizationMetrics"/>) as a
/// singleton via <c>services.AddSingleton&lt;ICanonicalizationMetrics, DefaultCanonicalizationMetrics&gt;()</c>.
/// </summary>
public interface ICanonicalizationMetrics
{
    /// <summary>Records a successful canonicalization.</summary>
    void RecordSuccess(string provider, string eventType);

    /// <summary>Records a soft failure (partial canonicalization).</summary>
    void RecordSoftFail(string provider, string eventType);

    /// <summary>Records a hard failure (event dropped or missing required fields).</summary>
    void RecordHardFail(string provider, string eventType);

    /// <summary>Records an unresolved field (symbol, venue, or condition).</summary>
    void RecordUnresolved(string provider, string field);

    /// <summary>Records a dual-write event (both raw and enriched persisted).</summary>
    void RecordDualWrite();

    /// <summary>Sets the active canonicalization version.</summary>
    void SetActiveVersion(int version);

    /// <summary>Gets a snapshot of current metrics.</summary>
    CanonicalizationSnapshot GetSnapshot();

    /// <summary>Resets all counters. Intended for use in tests.</summary>
    void Reset();
}

/// <summary>
/// Thread-safe in-memory counters for canonicalization events.
/// Integrates with <see cref="Monitoring.PrometheusMetrics"/> for metric export.
/// </summary>
/// <remarks>
/// Register as a DI singleton via
/// <c>services.AddSingleton&lt;ICanonicalizationMetrics, DefaultCanonicalizationMetrics&gt;()</c>
/// to enable test isolation (mock injection) and avoid global static state.
/// The legacy static <see cref="CanonicalizationMetrics"/> façade still exists for
/// call sites that have not yet been migrated to DI.
/// </remarks>
public sealed class DefaultCanonicalizationMetrics : ICanonicalizationMetrics
{
    private long _successTotal;
    private long _softFailTotal;
    private long _hardFailTotal;
    private long _dualWriteTotal;
    private readonly ConcurrentDictionary<(string Provider, string Field), long> _unresolvedCounts = new();
    private readonly ConcurrentDictionary<string, ProviderParityStats> _parityStats = new();
    private int _activeVersion;

    /// <inheritdoc/>
    public void RecordSuccess(string provider, string eventType)
    {
        Interlocked.Increment(ref _successTotal);
        GetOrAddParity(provider).RecordSuccess();
    }

    /// <inheritdoc/>
    public void RecordSoftFail(string provider, string eventType)
    {
        Interlocked.Increment(ref _softFailTotal);
        GetOrAddParity(provider).RecordSoftFail();
    }

    /// <inheritdoc/>
    public void RecordHardFail(string provider, string eventType)
    {
        Interlocked.Increment(ref _hardFailTotal);
        GetOrAddParity(provider).RecordHardFail();
    }

    /// <inheritdoc/>
    public void RecordUnresolved(string provider, string field)
    {
        _unresolvedCounts.AddOrUpdate((provider, field), 1, (_, count) => count + 1);
        GetOrAddParity(provider).RecordUnresolved(field);
    }

    /// <inheritdoc/>
    public void RecordDualWrite()
    {
        Interlocked.Increment(ref _dualWriteTotal);
    }

    /// <inheritdoc/>
    public void SetActiveVersion(int version)
    {
        Interlocked.Exchange(ref _activeVersion, version);
    }

    /// <inheritdoc/>
    public CanonicalizationSnapshot GetSnapshot()
    {
        return new CanonicalizationSnapshot(
            SuccessTotal: Interlocked.Read(ref _successTotal),
            SoftFailTotal: Interlocked.Read(ref _softFailTotal),
            HardFailTotal: Interlocked.Read(ref _hardFailTotal),
            DualWriteTotal: Interlocked.Read(ref _dualWriteTotal),
            ActiveVersion: _activeVersion,
            UnresolvedCounts: new Dictionary<(string Provider, string Field), long>(_unresolvedCounts),
            ProviderParity: _parityStats.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.ToSnapshot())
        );
    }

    /// <inheritdoc/>
    public void Reset()
    {
        Interlocked.Exchange(ref _successTotal, 0);
        Interlocked.Exchange(ref _softFailTotal, 0);
        Interlocked.Exchange(ref _hardFailTotal, 0);
        Interlocked.Exchange(ref _dualWriteTotal, 0);
        _unresolvedCounts.Clear();
        _parityStats.Clear();
        Interlocked.Exchange(ref _activeVersion, 0);
    }

    private ProviderParityStats GetOrAddParity(string provider)
    {
        return _parityStats.GetOrAdd(provider, _ => new ProviderParityStats());
    }
}

/// <summary>
/// Static façade that delegates to a configurable <see cref="ICanonicalizationMetrics"/> instance.
/// Retains backward compatibility for call sites that use the static API.
/// Replace the <see cref="Current"/> instance during test setup to achieve isolation.
/// </summary>
public static class CanonicalizationMetrics
{
    private static ICanonicalizationMetrics _current = new DefaultCanonicalizationMetrics();

    /// <summary>
    /// Gets or sets the active metrics instance.
    /// Set to a fresh <see cref="DefaultCanonicalizationMetrics"/> (or a mock) before each test.
    /// </summary>
    public static ICanonicalizationMetrics Current
    {
        get => _current;
        set => _current = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Records a successful canonicalization.</summary>
    public static void RecordSuccess(string provider, string eventType)
        => _current.RecordSuccess(provider, eventType);

    /// <summary>Records a soft failure (partial canonicalization).</summary>
    public static void RecordSoftFail(string provider, string eventType)
        => _current.RecordSoftFail(provider, eventType);

    /// <summary>Records a hard failure (event dropped or missing required fields).</summary>
    public static void RecordHardFail(string provider, string eventType)
        => _current.RecordHardFail(provider, eventType);

    /// <summary>Records an unresolved field (symbol, venue, or condition).</summary>
    public static void RecordUnresolved(string provider, string field)
        => _current.RecordUnresolved(provider, field);

    /// <summary>Records a dual-write event (both raw and enriched persisted).</summary>
    public static void RecordDualWrite()
        => _current.RecordDualWrite();

    /// <summary>Sets the active canonicalization version.</summary>
    public static void SetActiveVersion(int version)
        => _current.SetActiveVersion(version);

    /// <summary>Gets a snapshot of current metrics.</summary>
    public static CanonicalizationSnapshot GetSnapshot()
        => _current.GetSnapshot();

    /// <summary>Resets all counters. Intended for use in tests.</summary>
    public static void Reset()
        => _current.Reset();
}

/// <summary>
/// Thread-safe per-provider parity counters for Phase 2 validation.
/// </summary>
internal sealed class ProviderParityStats
{
    private long _success;
    private long _softFail;
    private long _hardFail;
    private long _unresolvedSymbol;
    private long _unresolvedVenue;
    private long _unresolvedCondition;

    public void RecordSuccess() => Interlocked.Increment(ref _success);
    public void RecordSoftFail() => Interlocked.Increment(ref _softFail);
    public void RecordHardFail() => Interlocked.Increment(ref _hardFail);

    public void RecordUnresolved(string field)
    {
        switch (field)
        {
            case "symbol":
                Interlocked.Increment(ref _unresolvedSymbol);
                break;
            case "venue":
                Interlocked.Increment(ref _unresolvedVenue);
                break;
            case "condition":
                Interlocked.Increment(ref _unresolvedCondition);
                break;
        }
    }

    public ProviderParitySnapshot ToSnapshot()
    {
        var total = Interlocked.Read(ref _success) +
                    Interlocked.Read(ref _softFail) +
                    Interlocked.Read(ref _hardFail);
        var successCount = Interlocked.Read(ref _success);

        return new ProviderParitySnapshot(
            Total: total,
            Success: successCount,
            SoftFail: Interlocked.Read(ref _softFail),
            HardFail: Interlocked.Read(ref _hardFail),
            UnresolvedSymbol: Interlocked.Read(ref _unresolvedSymbol),
            UnresolvedVenue: Interlocked.Read(ref _unresolvedVenue),
            UnresolvedCondition: Interlocked.Read(ref _unresolvedCondition),
            MatchRatePercent: total > 0 ? Math.Round(100.0 * successCount / total, 2) : 0
        );
    }
}

/// <summary>
/// Immutable snapshot of canonicalization metrics at a point in time.
/// </summary>
public sealed record CanonicalizationSnapshot(
    long SuccessTotal,
    long SoftFailTotal,
    long HardFailTotal,
    long DualWriteTotal,
    int ActiveVersion,
    Dictionary<(string Provider, string Field), long> UnresolvedCounts,
    Dictionary<string, ProviderParitySnapshot> ProviderParity
);

/// <summary>
/// Per-provider parity statistics for Phase 2 validation dashboard.
/// </summary>
public sealed record ProviderParitySnapshot(
    long Total,
    long Success,
    long SoftFail,
    long HardFail,
    long UnresolvedSymbol,
    long UnresolvedVenue,
    long UnresolvedCondition,
    double MatchRatePercent
);
