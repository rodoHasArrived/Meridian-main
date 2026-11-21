using MarketState = Meridian.Platform.Scheduling.MarketState;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Sdk;
using Meridian.Platform.Scheduling;

namespace Meridian.Ui.Shared.Services;

/// <summary>Session admission for known US equities. Other instruments keep their existing policies.</summary>
public sealed class EquityOrderSessionPolicy(
    ISecurityIdentityQueryService? securities = null, TimeProvider? clock = null) : IOrderSessionPolicy, IOrderInstrumentCoverageSource
{
    private readonly TradingCalendar _calendar = new();
    public async Task<bool> IsUsdEquityAsync(OrderRequest request, CancellationToken ct = default)
    {
        if (securities is null || request.Legs is { Count: > 0 } || request.OptionContract is not null)
            return false;
        var security = await securities.GetByIdentifierAsync(SecurityIdentifierKind.Ticker, request.Symbol, null, ct).ConfigureAwait(false);
        return security is not null && security.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase)
            && SecurityAssetClassCatalog.TryGet(security.AssetClass, out var descriptor) && descriptor.SupportsEquityClassification;
    }

    public async Task<OrderCheck> EvaluateAsync(OrderRequest request, CancellationToken ct = default)
    {
        if (request.Legs is { Count: > 0 } || request.OptionContract is not null)
            return new("SESSION_NOT_APPLICABLE", "passed", "Existing instrument session policy applies.");
        var security = securities is null ? null : await securities.GetByIdentifierAsync(
            SecurityIdentifierKind.Ticker, request.Symbol, null, ct).ConfigureAwait(false);
        if (security is not null && (!security.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase)
            || !SecurityAssetClassCatalog.TryGet(security.AssetClass, out var descriptor) || !descriptor.SupportsEquityClassification))
            return new("SESSION_NOT_APPLICABLE", "passed", "Existing instrument session policy applies.");
        if (security is null)
            return new("SESSION_IDENTITY_UNKNOWN", "blocked", "Resolve instrument identity before assessing its market session.");
        var intent = request.SessionIntent ?? OrderSessionIntent.RegularOnly;
        if (!Enum.IsDefined(intent))
            return new("SESSION_INTENT_INVALID", "blocked", "Session intent is invalid.");
        var status = _calendar.GetStatusAt((clock ?? TimeProvider.System).GetUtcNow());
        if (intent == OrderSessionIntent.ExtendedHours)
        {
            if (request.Type != OrderType.Limit || request.TimeInForce is not (TimeInForce.Day or TimeInForce.GoodTilCancelled))
                return new("EXTENDED_HOURS_INELIGIBLE", "blocked", "Extended-hours execution requires a Day or GTC limit order.");
            if (status.State is not (MarketState.Open or MarketState.PreMarket or MarketState.AfterHours))
                return new("MARKET_CLOSED", "blocked", "No eligible extended-hours session is currently open.");
        }
        else if (intent == OrderSessionIntent.QueueNextSession)
        {
            if (request.Type is not (OrderType.Market or OrderType.Limit) || request.TimeInForce is not (TimeInForce.Day or TimeInForce.GoodTilCancelled))
                return new("QUEUED_ORDER_INELIGIBLE", "blocked", "Next-session queuing supports Day or GTC market and limit orders.");
            if (status.State == MarketState.Open)
                return new("QUEUED_SESSION_OPEN", "blocked", "Next-session queuing is available only outside regular trading hours.");
            return new("QUEUED_FOR_SESSION", "warning", "The broker may queue this order for the next eligible regular session. All other checks still apply.");
        }
        else if (status.State != MarketState.Open)
            return new("MARKET_CLOSED", "blocked", "Regular trading hours are closed. Select explicit queuing or eligible extended-hours intent.");
        return new("SESSION_CHECKED", "passed", "Order session intent is eligible.");
    }
}

public sealed class FeedOrderMarketReferenceSource(ILiveFeedAdapter? feed = null) : IOrderMarketReferenceSource
{
    public OrderMarketReference? GetReference(string symbol, OrderSide side)
    {
        var quote = feed?.GetSnapshot(symbol).Quote;
        if (quote is null || quote.BidPrice <= 0m || quote.AskPrice < quote.BidPrice)
            return null;
        return new(side == OrderSide.Buy ? quote.AskPrice : quote.BidPrice, quote.Timestamp, quote.Venue ?? "market-feed");
    }
}
