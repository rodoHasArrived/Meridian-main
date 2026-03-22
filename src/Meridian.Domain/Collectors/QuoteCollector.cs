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

    private readonly ConcurrentDictionary<SymbolId, BboQuotePayload> _latest = new();
    private readonly ConcurrentDictionary<SymbolId, long> _seq = new();

    public QuoteCollector(IMarketEventPublisher publisher)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
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
        var payload = Upsert(update);
        _publisher.TryPublish(MarketEvent.BboQuote(payload.Timestamp, payload.Symbol, payload));
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

        // We keep our own monotonically increasing per-symbol sequence for quotes.
        var nextSeq = _seq.AddOrUpdate(symbolId, _ => 1, (_, v) => v + 1);

        var payload = BboQuotePayload.FromUpdate(update, nextSeq);
        _latest[symbolId] = payload;

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
