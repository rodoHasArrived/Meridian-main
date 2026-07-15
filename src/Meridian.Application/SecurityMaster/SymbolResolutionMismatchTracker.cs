using Meridian.Contracts.Catalog;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Process-local, bounded comparison evidence for the Legacy-to-Canonical migration.
/// The durable registry remains the symbol source of truth; this tracker exposes only
/// recent operational disagreements for rollout inspection.
/// </summary>
public sealed class SymbolResolutionMismatchTracker : ISymbolResolutionMismatchReader
{
    private const int DefaultCapacity = 200;
    private readonly object _gate = new();
    private readonly Queue<SymbolResolutionMismatch> _recent = new();
    private readonly int _capacity;
    private long _totalCount;

    public SymbolResolutionMismatchTracker(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    public long TotalCount => Interlocked.Read(ref _totalCount);

    public void Report(SymbolResolutionMismatch mismatch)
    {
        ArgumentNullException.ThrowIfNull(mismatch);
        Interlocked.Increment(ref _totalCount);

        lock (_gate)
        {
            _recent.Enqueue(mismatch);
            while (_recent.Count > _capacity)
                _recent.Dequeue();
        }
    }

    public IReadOnlyList<SymbolResolutionMismatch> GetRecent()
    {
        lock (_gate)
            return _recent.Reverse().ToArray();
    }
}
