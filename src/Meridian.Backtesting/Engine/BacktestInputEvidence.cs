using Meridian.Contracts.Backfill;
using Meridian.Contracts.Domain;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
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
    private readonly Dictionary<string, (DateTimeOffset At, IReadOnlyList<MarketDataInputEvidence> Inputs)> _lastPriceInputs = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<MarketDataInputEvidence> _currentInputs = [];
    private MarketEvent? _currentEvent;
    private JournalEvidenceReference? _currentEvidence;

    public async IAsyncEnumerable<MarketEvent> VerifyAsync(
        IAsyncEnumerable<MarketEvent> events, [EnumeratorCancellation] CancellationToken ct)
    {
        // All equal-time candidates are known before choosing a retained revision. Otherwise
        // file enumeration order decides which quality assessment accompanies an identical bar.
        var window = new List<MarketEvent>();
        DateTimeOffset? timestamp = null;
        await foreach (var item in events.WithCancellation(ct).ConfigureAwait(false))
        {
            if (timestamp.HasValue && timestamp != item.Timestamp)
            {
                await foreach (var verified in VerifyWindowAsync(window, ct).ConfigureAwait(false))
                    yield return verified;
                window.Clear();
            }
            timestamp = item.Timestamp;
            window.Add(item);
        }
        if (window.Count > 0)
            await foreach (var verified in VerifyWindowAsync(window, ct).ConfigureAwait(false))
                yield return verified;
    }

    private async IAsyncEnumerable<MarketEvent> VerifyWindowAsync(IReadOnlyList<MarketEvent> window,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var selected = new Dictionary<(string Source, string Feed, string Instrument, string Hash), MarketEvent>();
        var order = new List<(MarketEvent Item, (string Source, string Feed, string Instrument, string Hash)? Key)>();
        foreach (var item in window)
        {
            if (item.BackfillRevisionId is not { } id)
            {
                order.Add((item, null));
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
                    var key = (retained.Receipt.ServingProvider!, retained.Receipt.Feed, retained.Receipt.Instrument, hash);
                    if (!selected.TryGetValue(key, out var previous))
                    {
                        selected.Add(key, item);
                        order.Add((item, key));
                    }
                    else if (StringComparer.Ordinal.Compare(item.BackfillRevisionId, previous.BackfillRevisionId) < 0)
                        selected[key] = item;
                    continue;
                }
            }
            _unverified.Add(item.EffectiveSymbol);
            order.Add((item with { BackfillRevisionId = null }, null));
        }
        foreach (var item in order)
            yield return item.Key is { } key ? selected[key] : item.Item;
    }

    public void Observe(MarketEvent item)
    {
        _currentEvent = item;
        _currentEvidence = null;
        var symbol = item.EffectiveSymbol;
        var input = new MarketDataInputEvidence(ProviderId: item.Source,
            EventReference: FormattableString.Invariant($"{item.Source}:{symbol}:{item.Type}:{item.Sequence}:{item.Timestamp:O}"),
            ObservedAt: item.Timestamp);
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
            input = (receipt.InputEvidence ?? input) with
            {
                ProviderId = receipt.ServingProvider,
                EventReference = input.EventReference,
                ObservedAt = item.Timestamp,
                BackfillRevisionId = receipt.RevisionId,
                ContentHash = receipt.ContentHash,
                DatasetId = receipt.Feed
            };
            _consumedRevisions.TryAdd(id, receipt);
            _currentEvidence = new JournalEvidenceReference(
                id, $"backfill://batches/{id}", "Backfill batch", receipt.ServingProvider!,
                _capturedAt, "Meridian backtest", SubjectId: symbol, ContentHash: receipt.ContentHash,
                SourceReference: id, EffectiveDate: date, SubjectType: "Instrument");
        }

        input = MarketDataInputEvidenceTags.ForObservation(input, date ?? DateOnly.FromDateTime(item.Timestamp.UtcDateTime));
        _currentInputs = MarketDataInputEvidenceTags.Freeze([input])!;
        if (item.Payload is HistoricalBar or Trade or BboQuotePayload)
        {
            _lastPriceInputs[symbol] = (item.Timestamp, _currentInputs);
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

    public IReadOnlyList<MarketDataInputEvidence> PriceInputEvidence(string symbol, DateTimeOffset priceTimestamp)
    {
        if (_currentEvent is { } observed && observed.Timestamp == priceTimestamp &&
            string.Equals(observed.EffectiveSymbol, symbol, StringComparison.OrdinalIgnoreCase))
            return _currentInputs;
        return _lastPriceInputs.TryGetValue(symbol, out var mark) && mark.At <= priceTimestamp ? mark.Inputs : [];
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
