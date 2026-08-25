using System.Collections.Concurrent;
using System.Collections.Generic;
using Meridian.Contracts.Domain;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Domain.Telemetry;

namespace Meridian.Domain.Collectors;

/// <summary>
/// Maintains per-symbol Best-Bid/Offer (BBO) state and emits BboQuote market events.
/// </summary>
public sealed class QuoteCollector : IQuoteStateStore
{
    private readonly IMarketEventPublisher _publisher;
    private readonly IQuoteUpdateNotifier _updates;

    private readonly ConcurrentDictionary<SymbolId, BboQuotePayload> _latest = new();
    private readonly ConcurrentDictionary<SymbolId, long> _seq = new();

    public QuoteCollector(IMarketEventPublisher publisher, IQuoteUpdateNotifier? updates = null)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _updates = updates ?? NullQuoteUpdateNotifier.Instance;
    }

    /// <summary>
    /// Adapter entry point (e.g., Alpaca WebSocket quote updates).
    /// </summary>
    public void OnQuote(MarketQuoteUpdate update)
    {
        if (update is null)
            throw new ArgumentNullException(nameof(update));
        if (string.IsNullOrWhiteSpace(update.Symbol))
            return;

        using var publishActivity = MarketEventIngressTracing.StartCollectorActivity("quote-collector", "quote", update.Symbol);

        // The collector is a shared singleton serving every active adapter, so provenance
        // must arrive per event. Reject sourceless updates loudly instead of silently
        // attributing them to a default vendor.
        if (MarketDataSources.IsMissing(update.Source))
        {
            var integrity = IntegrityEvent.MissingSource(
                update.Timestamp,
                update.Symbol,
                "quote",
                update.SequenceNumber ?? 0,
                update.StreamId,
                update.Venue);

            _publisher.TryPublish(MarketEvent.Integrity(
                update.Timestamp, update.Symbol, integrity, MarketDataSources.Unknown));
            return;
        }

        var payload = Upsert(update);
        _publisher.TryPublish(MarketEvent.BboQuote(payload.Timestamp, payload.Symbol, payload, update.Source!));
    }

    public bool TryGet(string symbol, out BboQuotePayload? quote)
        => _latest.TryGetValue(new SymbolId(symbol), out quote);

    public BboQuotePayload Upsert(MarketQuoteUpdate update)
    {
        if (update is null)
            throw new ArgumentNullException(nameof(update));
        if (string.IsNullOrWhiteSpace(update.Symbol))
            throw new ArgumentException("Symbol is required", nameof(update));

        var symbolId = new SymbolId(update.Symbol);

        // Preserve the provider's own quote sequence when the feed supplies one — quote-stream
        // gap detection is only meaningful against real provider sequences. Fall back to a
        // locally assigned monotonic per-symbol counter only when the provider genuinely does
        // not sequence its quotes, and mark which regime produced the number on the payload.
        long seq;
        bool isProviderSequence;
        if (update.SequenceNumber is > 0)
        {
            seq = update.SequenceNumber.Value;
            isProviderSequence = true;
        }
        else
        {
            seq = _seq.AddOrUpdate(symbolId, _ => 1, (_, v) => v + 1);
            isProviderSequence = false;
        }

        var payload = BboQuotePayload.FromUpdate(update, seq, isProviderSequence);
        _latest[symbolId] = payload;

        // Fan-out signal for out-of-band consumers (UI quote stream). Guarded at the
        // call site so a faulty notifier can never surface on the ingestion path.
        try
        {
            _updates.NotifyQuoteChanged(payload.Symbol);
        }
        catch
        {
            // Best effort — ingestion must never fail on a UI-streaming concern.
        }

        return payload;
    }

    public bool TryRemove(string symbol, out BboQuotePayload? removed)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            removed = null;
            return false;
        }

        var key = new SymbolId(symbol.Trim());
        var removedLatest = _latest.TryRemove(key, out removed);
        _seq.TryRemove(key, out _);

        return removedLatest;
    }

    public IReadOnlyDictionary<string, BboQuotePayload> Snapshot()
        => _latest.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
}
