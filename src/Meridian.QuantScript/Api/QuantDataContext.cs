using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Store;
using Meridian.Domain.Events;
using Meridian.Storage.Store;

namespace Meridian.QuantScript.Api;

/// <summary>
/// Implements <see cref="IQuantDataContext"/> by delegating to <see cref="JsonlMarketDataStore"/>.
/// Returns empty series (no exception) when no data is found for a symbol/date range.
/// </summary>
public sealed class QuantDataContext(
    JsonlMarketDataStore store,
    ILogger<QuantDataContext> logger,
    Meridian.Application.SecurityMaster.ISecurityMasterQueryService? securityMasterQuery = null) : IQuantDataContext
{
    public async Task<PriceSeries> PricesAsync(
        string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
        => await PricesAsync(symbol, from, to, null!, ct);

    public async Task<PriceSeries> PricesAsync(
        string symbol, DateOnly from, DateOnly to, string? provider, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var fromUtc = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toUtc = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var query = new MarketDataQuery(
            Symbol: new Contracts.Domain.SymbolId(symbol),
            From: fromUtc,
            To: toUtc,
            EventType: MarketEventType.HistoricalBar,
            Source: string.IsNullOrWhiteSpace(provider) ? null : provider);

        var bars = new List<PriceBar>();
        await foreach (var evt in store.QueryAsync(query, ct))
        {
            if (evt.Payload is HistoricalBar bar &&
                bar.SessionDate >= from && bar.SessionDate <= to)
            {
                bars.Add(new PriceBar(bar.SessionDate, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume));
            }
        }

        if (bars.Count == 0)
        {
            logger.LogWarning("No price data found for {Symbol} from {From} to {To}", symbol, from, to);
        }

        bars.Sort((a, b) => a.Date.CompareTo(b.Date));
        return new PriceSeries(symbol, bars);
    }

    public async Task<IReadOnlyList<ScriptTrade>> TradesAsync(
        string symbol, DateOnly date, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var fromUtc = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toUtc = fromUtc.AddDays(1);

        var query = new MarketDataQuery(
            Symbol: new Contracts.Domain.SymbolId(symbol),
            From: fromUtc,
            To: toUtc,
            EventType: MarketEventType.Trade);

        var trades = new List<ScriptTrade>();
        await foreach (var evt in store.QueryAsync(query, ct))
        {
            if (evt.Payload is Contracts.Domain.Models.Trade t)
            {
                trades.Add(new ScriptTrade(
                    evt.Timestamp,
                    t.Price,
                    t.Size,
                    t.Aggressor.ToString()));
            }
        }
        return trades;
    }

    public async Task<ScriptOrderBook?> OrderBookAsync(
        string symbol, DateTimeOffset timestamp, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var window = TimeSpan.FromSeconds(1);
        var query = new MarketDataQuery(
            Symbol: new Contracts.Domain.SymbolId(symbol),
            From: timestamp - window,
            To: timestamp + window,
            EventType: MarketEventType.L2Snapshot);

        MarketEvent? closest = null;
        TimeSpan minDiff = TimeSpan.MaxValue;
        await foreach (var evt in store.QueryAsync(query, ct))
        {
            var diff = (evt.Timestamp - timestamp).Duration();
            if (diff < minDiff)
            { minDiff = diff; closest = evt; }
        }

        if (closest?.Payload is Contracts.Domain.Models.LOBSnapshot snap)
        {
            var bids = snap.Bids?.Select(l => (l.Price, (long)l.Size)).ToList()
                ?? new List<(decimal, long)>();
            var asks = snap.Asks?.Select(l => (l.Price, (long)l.Size)).ToList()
                ?? new List<(decimal, long)>();
            return new ScriptOrderBook(closest.Timestamp, bids, asks);
        }
        return null;
    }

    /// <inheritdoc />
    public async Task<SecurityDetailDto?> SecMasterAsync(string symbol, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        if (securityMasterQuery is null)
            return null;

        try
        {
            return await securityMasterQuery.GetByIdentifierAsync(
                SecurityIdentifierKind.Ticker, symbol, provider: null, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Security Master lookup failed for symbol {Symbol}", symbol);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CorporateActionDto>> CorporateActionsAsync(
        string symbol, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        if (securityMasterQuery is null)
            return [];

        try
        {
            var detail = await securityMasterQuery.GetByIdentifierAsync(
                SecurityIdentifierKind.Ticker, symbol, provider: null, ct).ConfigureAwait(false);

            if (detail is null)
                return [];

            return await securityMasterQuery.GetCorporateActionsAsync(detail.SecurityId, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Corporate actions lookup failed for symbol {Symbol}", symbol);
            return [];
        }
    }
}
