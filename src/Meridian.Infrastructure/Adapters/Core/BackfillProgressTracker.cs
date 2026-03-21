using System.Collections.Concurrent;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Tracks per-symbol backfill progress for reporting to the API and SSE streams.
/// Thread-safe for concurrent access from multiple worker tasks.
/// </summary>
public sealed class BackfillProgressTracker
{
    private readonly ConcurrentDictionary<string, SymbolProgress> _progress = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a symbol's backfill date range for tracking.
    /// </summary>
    public void RegisterSymbol(string symbol, DateOnly fromDate, DateOnly toDate)
    {
        var totalDays = toDate.DayNumber - fromDate.DayNumber + 1;
        _progress[symbol] = new SymbolProgress(fromDate, toDate, totalDays);
    }

    /// <summary>
    /// Records completion of a date range for a symbol.
    /// </summary>
    public void RecordProgress(string symbol, int barsCompleted)
    {
        _progress.AddOrUpdate(
            symbol,
            _ => new SymbolProgress(DateOnly.MinValue, DateOnly.MinValue, 0) { CompletedDays = barsCompleted },
            (_, existing) =>
            {
                existing.CompletedDays += barsCompleted;
                return existing;
            });
    }

    /// <summary>
    /// Marks a symbol as completed.
    /// </summary>
    public void MarkCompleted(string symbol)
    {
        if (_progress.TryGetValue(symbol, out var progress))
        {
            progress.CompletedDays = progress.TotalDays;
            progress.IsCompleted = true;
        }
    }

    /// <summary>
    /// Marks a symbol as failed.
    /// </summary>
    public void MarkFailed(string symbol, string? error)
    {
        if (_progress.TryGetValue(symbol, out var progress))
        {
            progress.Error = error;
            progress.IsFailed = true;
        }
    }

    /// <summary>
    /// Gets progress snapshot for all tracked symbols.
    /// </summary>
    public BackfillProgressSnapshot GetSnapshot()
    {
        var items = new Dictionary<string, BackfillSymbolProgress>(StringComparer.OrdinalIgnoreCase);
        var totalDays = 0;
        var completedDays = 0;

        foreach (var kvp in _progress)
        {
            var p = kvp.Value;
            var pct = p.TotalDays > 0 ? (double)p.CompletedDays / p.TotalDays * 100.0 : 0.0;

            items[kvp.Key] = new BackfillSymbolProgress(
                Symbol: kvp.Key,
                FromDate: p.FromDate,
                ToDate: p.ToDate,
                TotalDays: p.TotalDays,
                CompletedDays: p.CompletedDays,
                PercentComplete: Math.Min(pct, 100.0),
                IsCompleted: p.IsCompleted,
                IsFailed: p.IsFailed,
                Error: p.Error);

            totalDays += p.TotalDays;
            completedDays += p.CompletedDays;
        }

        var overallPct = totalDays > 0 ? (double)completedDays / totalDays * 100.0 : 0.0;

        return new BackfillProgressSnapshot(
            Symbols: items,
            OverallPercentComplete: Math.Min(overallPct, 100.0),
            TotalSymbols: items.Count,
            CompletedSymbols: items.Count(x => x.Value.IsCompleted),
            FailedSymbols: items.Count(x => x.Value.IsFailed),
            Timestamp: DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Clears all progress tracking data.
    /// </summary>
    public void Clear() => _progress.Clear();

    private sealed class SymbolProgress
    {
        public DateOnly FromDate { get; }
        public DateOnly ToDate { get; }
        public int TotalDays { get; }
        public int CompletedDays { get; set; }
        public bool IsCompleted { get; set; }
        public bool IsFailed { get; set; }
        public string? Error { get; set; }

        public SymbolProgress(DateOnly fromDate, DateOnly toDate, int totalDays)
        {
            FromDate = fromDate;
            ToDate = toDate;
            TotalDays = totalDays;
        }
    }
}

/// <summary>
/// Snapshot of backfill progress across all symbols.
/// </summary>
public sealed record BackfillProgressSnapshot(
    IReadOnlyDictionary<string, BackfillSymbolProgress> Symbols,
    double OverallPercentComplete,
    int TotalSymbols,
    int CompletedSymbols,
    int FailedSymbols,
    DateTimeOffset Timestamp);

/// <summary>
/// Progress for a single symbol's backfill operation.
/// </summary>
public sealed record BackfillSymbolProgress(
    string Symbol,
    DateOnly FromDate,
    DateOnly ToDate,
    int TotalDays,
    int CompletedDays,
    double PercentComplete,
    bool IsCompleted,
    bool IsFailed,
    string? Error);
