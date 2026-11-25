using Meridian.Contracts.Backfill;
using Meridian.Contracts.Domain;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Integrity;
using Meridian.Domain.Events;
using Meridian.Ledger;
using Meridian.Platform.Scheduling;
using Meridian.Storage.Backfill;

namespace Meridian.Backtesting.Engine;

/// <summary>Records replayed session dates and bar sources and verifies optional ingestion receipts.</summary>
internal sealed class BacktestInputEvidence(string dataRoot, HistoricalSessionScheduleResolver scheduleResolver)
{
    private static readonly TimeZoneInfo UsEquitiesTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private readonly BackfillBatchStore _batches = new BackfillStatusStore(dataRoot).Batches;
    private readonly DateTimeOffset _capturedAt = DateTimeOffset.UtcNow;
    private readonly Dictionary<string, VerifiedBatch?> _batchCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<DateOnly>> _observedDates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Symbol, string Source), long> _sourceCounts = new();
    private readonly HashSet<string> _unverified = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BackfillBatchReceipt> _consumedRevisions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (DateTimeOffset At, JournalEvidenceReference Reference)?> _lastPriceEvidence = new(StringComparer.OrdinalIgnoreCase);
    private MarketEvent? _currentEvent;
    private JournalEvidenceReference? _currentEvidence;

    public async IAsyncEnumerable<MarketEvent> VerifyAsync(
        IAsyncEnumerable<MarketEvent> events, [EnumeratorCancellation] CancellationToken ct)
    {
        DateTimeOffset? verificationTime = null;
        var verifiedRows = new HashSet<(string Source, string Feed, string Instrument, string Hash)>();
        await foreach (var item in events.WithCancellation(ct).ConfigureAwait(false))
        {
            if (item.Timestamp != verificationTime)
            {
                verificationTime = item.Timestamp;
                verifiedRows.Clear();
            }

            if (item.BackfillRevisionId is not { } id)
            {
                yield return item;
                continue;
            }

            var retained = await ReadBatchAsync(id, ct).ConfigureAwait(false);
            if (retained is not null &&
                string.Equals(retained.Receipt.Instrument, item.Symbol, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(retained.Receipt.ServingProvider, item.Source, StringComparison.OrdinalIgnoreCase) &&
                item.Payload is HistoricalBar or AggregateBarPayload)
            {
                var hash = BackfillBatchRevision.HashRow(item);
                if (retained.RowHashes.Contains(hash))
                {
                    // A crash may leave the same stamped row in a partition twice before its
                    // receipt is sealed. Range refreshes can also overlap without revising it.
                    if (verifiedRows.Add((retained.Receipt.ServingProvider!, retained.Receipt.Feed,
                            retained.Receipt.Instrument, hash)))
                        yield return item;
                    continue;
                }
            }

            _unverified.Add(item.EffectiveSymbol);
            yield return item with { BackfillRevisionId = null };
        }
    }

    public void Observe(MarketEvent item)
    {
        _currentEvent = item;
        _currentEvidence = null;
        var symbol = item.EffectiveSymbol;
        DateOnly? date = item.Payload switch
        {
            HistoricalBar bar => bar.SessionDate,
            AggregateBarPayload bar => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(bar.StartTime, UsEquitiesTimeZone).DateTime),
            _ => null
        };
        if (date is { } session)
        {
            if (!_observedDates.TryGetValue(symbol, out var dates))
                _observedDates.Add(symbol, dates = []);
            dates.Add(session);

            var source = MarketDataSources.IsMissing(item.Source)
                ? MarketDataSources.Unknown
                : item.Source.Trim().ToUpperInvariant();
            var key = (symbol.Trim().ToUpperInvariant(), source);
            _sourceCounts[key] = _sourceCounts.GetValueOrDefault(key) + 1;
        }

        if (item.BackfillRevisionId is { } id && _batchCache.GetValueOrDefault(id) is { } batch)
        {
            var receipt = batch.Receipt;
            _consumedRevisions.TryAdd(id, receipt);
            _currentEvidence = new JournalEvidenceReference(
                id, $"backfill://batches/{id}", "Backfill batch", receipt.ServingProvider!,
                _capturedAt, "Meridian backtest", SubjectId: symbol, ContentHash: receipt.ContentHash,
                SourceReference: id, EffectiveDate: date, SubjectType: "Instrument");
        }

        if (item.Payload is HistoricalBar or Trade or BboQuotePayload)
        {
            // A later unstamped mark cannot inherit evidence from a previous backfilled price.
            _lastPriceEvidence[symbol] = _currentEvidence is { } evidence ? (item.Timestamp, evidence) : null;
        }
    }

    public IReadOnlyList<JournalEvidenceReference> PriceEvidence(string symbol, DateTimeOffset priceTimestamp)
    {
        if (_currentEvent is { } observed && observed.Timestamp == priceTimestamp &&
            string.Equals(observed.EffectiveSymbol, symbol, StringComparison.OrdinalIgnoreCase))
            return _currentEvidence is { } evidence ? [evidence] : [];

        return _lastPriceEvidence.GetValueOrDefault(symbol) is { } item && item.At <= priceTimestamp
            ? [item.Reference] : [];
    }

    public async Task<BacktestInputCoverage> CompleteAsync(
        IReadOnlyCollection<string> requiredInstruments, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var instruments = new List<BacktestInstrumentCoverage>();
        foreach (var instrument in requiredInstruments.Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
                     .Select(static symbol => symbol.Trim().ToUpperInvariant()).Distinct().Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var schedule = await scheduleResolver.ResolveAsync(instrument, from, to, ct).ConfigureAwait(false);
            var dates = _observedDates.GetValueOrDefault(instrument) ?? [];
            var missing = schedule?.Sessions.Where(date => !dates.Contains(date)).ToArray() ?? [];
            var status = missing.Length > 0 ? "Incomplete"
                : schedule is null || _unverified.Contains(instrument) ? "Unknown"
                : "Complete";
            instruments.Add(new BacktestInstrumentCoverage(instrument, status, schedule?.Calendar,
                schedule is null ? dates.Count : schedule.Sessions.Count - missing.Length,
                schedule?.Sessions.Count, missing));
        }

        return new BacktestInputCoverage(1m, instruments,
            _consumedRevisions.Values.OrderBy(static receipt => receipt.RevisionId, StringComparer.Ordinal)
                .Select(static receipt => new BacktestDataRevision(receipt.RevisionId, receipt.ContentHash,
                    receipt.ServingProvider!, receipt.Instrument, receipt.Granularity)).ToArray())
        {
            SourceMix = _sourceCounts.Count > 0
                ? _sourceCounts.OrderBy(static item => item.Key.Symbol, StringComparer.Ordinal)
                    .ThenBy(static item => item.Key.Source, StringComparer.Ordinal)
                    .Select(static item => new BacktestSourceCount(item.Key.Symbol, item.Key.Source, item.Value)).ToArray()
                : null
        };
    }

    private async Task<VerifiedBatch?> ReadBatchAsync(string id, CancellationToken ct)
    {
        if (_batchCache.TryGetValue(id, out var retained))
            return retained;

        VerifiedBatch? verified = null;
        try
        {
            if (Sha256Digest.IsCanonical(id) && await _batches.ReadAsync(id, ct).ConfigureAwait(false) is { } batch)
                verified = new VerifiedBatch(batch.Receipt, batch.RowHashes.ToHashSet(StringComparer.Ordinal));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // Availability of a receipt is separate from availability of the bar itself.
        }

        _batchCache[id] = verified;
        return verified;
    }

    private sealed record VerifiedBatch(BackfillBatchReceipt Receipt, HashSet<string> RowHashes);
}
