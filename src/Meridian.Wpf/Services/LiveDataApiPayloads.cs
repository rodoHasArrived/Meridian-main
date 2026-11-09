using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Api;

namespace Meridian.Wpf.Services;

/// <summary>
/// Route builders and payload readers for the live-data endpoints mapped by
/// <c>LiveDataEndpoints</c> (<see cref="UiApiRoutes.DataQuotes"/>, <see cref="UiApiRoutes.DataTrades"/>,
/// <see cref="UiApiRoutes.DataOrderbook"/>). The view models that poll these routes parse raw
/// <see cref="JsonElement"/> payloads, so the envelope shapes (<see cref="QuotesResponse"/>,
/// <see cref="TradesResponse"/>, <see cref="OrderBookResponse"/>) are unwrapped here in one place.
/// </summary>
internal static class LiveDataApiPayloads
{
    /// <summary>Relative route for the latest quote of <paramref name="symbol"/>.</summary>
    public static string QuoteRoute(string symbol)
        => UiApiRoutes.WithParam(UiApiRoutes.DataQuotes, "symbol", symbol);

    /// <summary>Relative route for the most recent trades of <paramref name="symbol"/>.</summary>
    public static string TradesRoute(string symbol, int limit)
        => UiApiRoutes.WithQuery(
            UiApiRoutes.WithParam(UiApiRoutes.DataTrades, "symbol", symbol),
            "limit=" + limit.ToString(CultureInfo.InvariantCulture));

    /// <summary>Relative route for the order book snapshot of <paramref name="symbol"/>.</summary>
    public static string OrderBookRoute(string symbol, int levels)
        => UiApiRoutes.WithQuery(
            UiApiRoutes.WithParam(UiApiRoutes.DataOrderbook, "symbol", symbol),
            "levels=" + levels.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Reads the quote carried by a <see cref="QuotesResponse"/> payload. Returns <c>false</c>
    /// when the server has no quote for the symbol yet (<c>quote</c> is null or absent).
    /// </summary>
    public static bool TryReadQuote(JsonElement root, out LiveQuoteSnapshot quote)
    {
        quote = default;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("quote", out var q) ||
            q.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        decimal? last = null;
        if (q.TryGetProperty("session", out var session) &&
            session.ValueKind == JsonValueKind.Object &&
            session.TryGetProperty("last", out var lastElement) &&
            lastElement.ValueKind == JsonValueKind.Number)
        {
            last = lastElement.GetDecimal();
        }

        quote = new LiveQuoteSnapshot(
            ReadDecimal(q, "bidPrice"),
            ReadDecimal(q, "askPrice"),
            ReadSize(q, "bidSize"),
            ReadSize(q, "askSize"),
            last);
        return true;
    }

    /// <summary>
    /// Enumerates the trade rows of a <see cref="TradesResponse"/> payload (the <c>trades</c>
    /// array). Yields nothing when the payload carries no trades.
    /// </summary>
    public static IEnumerable<JsonElement> EnumerateTrades(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("trades", out var trades) &&
            trades.ValueKind == JsonValueKind.Array)
        {
            foreach (var trade in trades.EnumerateArray())
            {
                yield return trade;
            }
        }
    }

    /// <summary>
    /// Returns true when a <see cref="TradeDataResponse"/> row was buyer-initiated
    /// (<c>aggressor</c> is the serialized <c>AggressorSide.Buy</c> name).
    /// </summary>
    public static bool IsBuyAggressor(JsonElement trade)
        => trade.ValueKind == JsonValueKind.Object &&
           trade.TryGetProperty("aggressor", out var aggressor) &&
           aggressor.ValueKind == JsonValueKind.String &&
           string.Equals(aggressor.GetString(), "Buy", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads a numeric size property as a display integer. Server sizes are <c>long</c> (quotes,
    /// trades) or <c>decimal</c> (order book levels); the result is truncated and clamped to
    /// the <see cref="int"/> range the display models use.
    /// </summary>
    public static int ReadSize(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Number)
        {
            return 0;
        }

        var size = decimal.Truncate(value.GetDecimal());
        if (size >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return size <= 0m ? 0 : (int)size;
    }

    private static decimal ReadDecimal(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDecimal()
            : 0m;
}

/// <summary>
/// Quote values read from a <see cref="QuotesResponse"/> payload. <see cref="Last"/> is the
/// session's last trade price and is null until the server has observed a trade for the symbol.
/// </summary>
internal readonly record struct LiveQuoteSnapshot(decimal Bid, decimal Ask, int BidSize, int AskSize, decimal? Last);
