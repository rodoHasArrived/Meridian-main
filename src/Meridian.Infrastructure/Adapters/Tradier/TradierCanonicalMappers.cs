using System.Globalization;
using Meridian.Execution.Sdk;

namespace Meridian.Infrastructure.Adapters.Tradier;

public static class TradierCanonicalMappers
{
    public static TradierCanonicalEquity NormalizeEquityPayload(TradierEquityPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var normalizedSymbol = NormalizeEquitySymbol(payload.Symbol);
        return new TradierCanonicalEquity(
            Symbol: normalizedSymbol,
            Exchange: string.IsNullOrWhiteSpace(payload.Exchange) ? "UNKNOWN" : payload.Exchange.Trim().ToUpperInvariant(),
            LastPrice: payload.LastPrice,
            BidPrice: payload.BidPrice,
            AskPrice: payload.AskPrice,
            TradeTimestampUtc: payload.TradeTimestampUtc,
            SourceSymbol: payload.Symbol);
    }

    public static TradierCanonicalOptionContract NormalizeOptionPayload(TradierOptionPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var underlying = NormalizeEquitySymbol(payload.UnderlyingSymbol);
        var expiry = payload.ExpirationDate.ToString("yyMMdd", CultureInfo.InvariantCulture);
        var right = payload.IsCall ? "C" : "P";
        var strikeScaled = (int)Math.Round(payload.StrikePrice * 1000m, MidpointRounding.AwayFromZero);
        var strikeToken = strikeScaled.ToString("D8", CultureInfo.InvariantCulture);
        var canonicalContract = $"{underlying}{expiry}{right}{strikeToken}";

        return new TradierCanonicalOptionContract(
            UnderlyingSymbol: underlying,
            ContractSymbol: canonicalContract,
            ExpirationDate: payload.ExpirationDate,
            StrikePrice: payload.StrikePrice,
            IsCall: payload.IsCall,
            BidPrice: payload.BidPrice,
            AskPrice: payload.AskPrice,
            LastPrice: payload.LastPrice,
            OpenInterest: payload.OpenInterest,
            Volume: payload.Volume,
            SourceContractSymbol: payload.ContractSymbol);
    }

    public static TradierOrderTransition MapOrderTransition(TradierOrderStatusPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var status = (payload.Status ?? string.Empty).Trim().ToLowerInvariant();
        var filled = Math.Max(payload.FilledQuantity, 0m);
        var total = Math.Max(payload.OrderQuantity, 0m);

        return status switch
        {
            "filled" => BuildTransition(OrderStatus.Filled, ExecutionReportType.Fill),
            "partially_filled" => BuildTransition(OrderStatus.PartiallyFilled, ExecutionReportType.PartialFill),
            "partial" => BuildTransition(OrderStatus.PartiallyFilled, ExecutionReportType.PartialFill),
            "canceled" => BuildTransition(OrderStatus.Cancelled, ExecutionReportType.Cancelled),
            "cancelled" => BuildTransition(OrderStatus.Cancelled, ExecutionReportType.Cancelled),
            "rejected" => BuildTransition(OrderStatus.Rejected, ExecutionReportType.Rejected),
            "expired" => BuildTransition(OrderStatus.Expired, ExecutionReportType.Expired),
            "open" when filled > 0m && filled < total => BuildTransition(OrderStatus.PartiallyFilled, ExecutionReportType.PartialFill),
            "open" => BuildTransition(OrderStatus.Accepted, ExecutionReportType.New),
            "pending" => BuildTransition(OrderStatus.PendingNew, ExecutionReportType.New),
            "pending_cancel" => BuildTransition(OrderStatus.PendingCancel, ExecutionReportType.New),
            _ when filled > 0m && filled < total => BuildTransition(OrderStatus.PartiallyFilled, ExecutionReportType.PartialFill),
            _ => BuildTransition(OrderStatus.Accepted, ExecutionReportType.New)
        };

        static TradierOrderTransition BuildTransition(OrderStatus status, ExecutionReportType reportType) =>
            new(status, reportType);
    }

    public static TradierOperationalError MapBrokerError(TradierBrokerErrorPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var brokerCode = payload.Code?.Trim();
        var message = string.IsNullOrWhiteSpace(payload.Message)
            ? "Tradier returned an unspecified broker error."
            : payload.Message.Trim();
        var retriable = payload.HttpStatusCode is 408 or 429 || (payload.HttpStatusCode >= 500 && payload.HttpStatusCode <= 599);
        var category = payload.HttpStatusCode switch
        {
            400 => "Validation",
            401 or 403 => "Authentication",
            404 => "NotFound",
            408 => "Timeout",
            409 => "Conflict",
            429 => "RateLimit",
            >= 500 and <= 599 => "ProviderUnavailable",
            _ => "ProviderError"
        };

        return new TradierOperationalError(
            Category: category,
            Message: message,
            BrokerCode: brokerCode,
            HttpStatusCode: payload.HttpStatusCode,
            IsRetriable: retriable,
            RawPayload: payload.RawPayload ?? string.Empty);
    }

    private static string NormalizeEquitySymbol(string? symbol)
    {
        var trimmed = string.IsNullOrWhiteSpace(symbol) ? "UNKNOWN" : symbol.Trim().ToUpperInvariant();
        return trimmed.Replace("/", ".", StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
    }
}

public sealed record TradierEquityPayload(string Symbol, string? Exchange, decimal LastPrice, decimal? BidPrice, decimal? AskPrice, DateTimeOffset TradeTimestampUtc);
public sealed record TradierOptionPayload(string UnderlyingSymbol, string? ContractSymbol, DateOnly ExpirationDate, decimal StrikePrice, bool IsCall, decimal? BidPrice, decimal? AskPrice, decimal? LastPrice, long? OpenInterest, long? Volume);
public sealed record TradierOrderStatusPayload(string? Status, decimal OrderQuantity, decimal FilledQuantity);
public sealed record TradierBrokerErrorPayload(int HttpStatusCode, string? Code, string? Message, string? RawPayload = null);

public sealed record TradierCanonicalEquity(string Symbol, string Exchange, decimal LastPrice, decimal? BidPrice, decimal? AskPrice, DateTimeOffset TradeTimestampUtc, string SourceSymbol);
public sealed record TradierCanonicalOptionContract(string UnderlyingSymbol, string ContractSymbol, DateOnly ExpirationDate, decimal StrikePrice, bool IsCall, decimal? BidPrice, decimal? AskPrice, decimal? LastPrice, long? OpenInterest, long? Volume, string? SourceContractSymbol);
public sealed record TradierOrderTransition(OrderStatus Status, ExecutionReportType ReportType);
public sealed record TradierOperationalError(string Category, string Message, string? BrokerCode, int HttpStatusCode, bool IsRetriable, string RawPayload);
