using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Maintains per-provider EWMA estimates of clock skew between exchange/provider
/// timestamps and local receive time. This enables gap detection and dedup logic
/// to be less brittle in the face of clock drift and NTP jumps.
/// </summary>
public sealed class ClockSkewEstimator
{
    private readonly ConcurrentDictionary<string, ProviderSkewState> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly double _alpha;

    /// <summary>
    /// Creates a new ClockSkewEstimator with configurable EWMA smoothing factor.
    /// </summary>
    /// <param name="alpha">EWMA smoothing factor (0..1). Higher = more reactive. Default 0.05.</param>
    public ClockSkewEstimator(double alpha = 0.05)
    {
        if (alpha is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(alpha), alpha, "Alpha must be in (0, 1]");
        _alpha = alpha;
    }

    /// <summary>
    /// Records a timestamp observation for a provider.
    /// </summary>
    /// <param name="provider">Provider/source identifier.</param>
    /// <param name="exchangeTimestamp">Timestamp reported by exchange/provider.</param>
    /// <param name="receivedAtUtc">Local wall-clock receive time.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordObservation(string provider, DateTimeOffset exchangeTimestamp, DateTimeOffset receivedAtUtc)
    {
        var skewMs = (receivedAtUtc - exchangeTimestamp).TotalMilliseconds;

        var state = _providers.GetOrAdd(provider, _ => new ProviderSkewState());
        state.Update(skewMs, _alpha);
    }

    /// <summary>
    /// Gets the estimated clock skew in milliseconds for a provider.
    /// Positive means the provider clock is behind local time.
    /// </summary>
    public double GetEstimatedSkewMs(string provider)
    {
        return _providers.TryGetValue(provider, out var state) ? state.EwmaSkewMs : 0;
    }

    /// <summary>
    /// Gets a snapshot of all provider skew estimates.
    /// </summary>
    public IReadOnlyDictionary<string, ClockSkewSnapshot> GetAllSnapshots()
    {
        var result = new Dictionary<string, ClockSkewSnapshot>(_providers.Count);
        foreach (var (provider, state) in _providers)
        {
            result[provider] = new ClockSkewSnapshot(
                EstimatedSkewMs: state.EwmaSkewMs,
                SampleCount: state.SampleCount,
                MinSkewMs: state.MinSkewMs,
                MaxSkewMs: state.MaxSkewMs);
        }
        return result;
    }

    private sealed class ProviderSkewState
    {
        private readonly object _lock = new();
        private double _ewma;
        private bool _initialized;

        public double EwmaSkewMs
        {
            get { lock (_lock) return _ewma; }
        }

        public long SampleCount;
        public double MinSkewMs = double.MaxValue;
        public double MaxSkewMs = double.MinValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(double skewMs, double alpha)
        {
            lock (_lock)
            {
                if (!_initialized)
                {
                    _ewma = skewMs;
                    _initialized = true;
                }
                else
                {
                    _ewma = alpha * skewMs + (1 - alpha) * _ewma;
                }

                Interlocked.Increment(ref SampleCount);
                if (skewMs < MinSkewMs)
                    MinSkewMs = skewMs;
                if (skewMs > MaxSkewMs)
                    MaxSkewMs = skewMs;
            }
        }
    }
}

/// <summary>
/// Snapshot of clock skew estimates for a single provider.
/// </summary>
public readonly record struct ClockSkewSnapshot(
    double EstimatedSkewMs,
    long SampleCount,
    double MinSkewMs,
    double MaxSkewMs);
