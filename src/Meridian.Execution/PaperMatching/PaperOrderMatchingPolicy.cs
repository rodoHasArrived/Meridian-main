using Meridian.Execution.Sdk;

namespace Meridian.Execution.PaperMatching;

/// <summary>Outcome kind of one paper-matching evaluation.</summary>
public enum PaperMatchOutcome
{
    /// <summary>The order fills now at <see cref="PaperMatchResult.FillPrice"/>.</summary>
    Filled,

    /// <summary>The order does not fill against the current observation and rests.</summary>
    Resting,

    /// <summary>No usable market data has been observed for the symbol.</summary>
    NoMarketData
}

/// <summary>Result of one paper-matching evaluation.</summary>
/// <param name="Outcome">Whether the order filled, rests, or found no market data.</param>
/// <param name="FillPrice">
/// The observed-envelope-bounded fill price when <see cref="Outcome"/> is
/// <see cref="PaperMatchOutcome.Filled"/>; otherwise <c>null</c>.
/// </param>
/// <param name="StopTriggered">
/// True once a stop order's trigger condition has been met. A triggered stop-limit that
/// is not yet marketable rests with this flag set so it is not re-armed.
/// </param>
public readonly record struct PaperMatchResult(
    PaperMatchOutcome Outcome,
    decimal? FillPrice,
    bool StopTriggered);

/// <summary>
/// Order-type-aware paper matching over observed market data. This is the documented
/// matching policy (<see cref="MatchingModelVersion"/>) shared by both paper gateways:
///
/// <list type="bullet">
/// <item><description><b>Market</b> orders fill from observed market data only — buys at the
/// observed ask (falling back to last trade, then bar close), sells at the observed bid
/// (same fallbacks). With no observation the order does not fill; the gateway decides
/// whether to reject (default, fail closed) or apply explicitly opted-in scaffold
/// pricing.</description></item>
/// <item><description><b>Limit</b> orders fill only at or better than the limit price: a buy fills
/// when the observed aggressive price is at or below the limit, at that observed price; a
/// sell fills when the observed aggressive price is at or above the limit. Otherwise the
/// order rests and is re-evaluated as new market data arrives.</description></item>
/// <item><description><b>Stop</b> triggers are trade-preferred: a buy stop triggers when the last
/// trade (or bar close) is at or above the stop price, a sell stop when at or below.
/// When no trade or bar has been observed, the quote fallback applies: a buy stop
/// triggers on ask, a sell stop on bid. A triggered stop-market order fills with market
/// semantics; a triggered stop-limit order fills with limit semantics.</description></item>
/// <item><description>Every fill price is clamped into the observed price envelope
/// (<see cref="PaperMarketObservation.EnvelopeLow"/> ..
/// <see cref="PaperMarketObservation.EnvelopeHigh"/>) — a paper fill can never print
/// outside the observed market data in effect.</description></item>
/// <item><description>Partial fills are not modeled: an order fills in full or rests.</description></item>
/// </list>
/// </summary>
public static class PaperOrderMatchingPolicy
{
    /// <summary>
    /// Version identifier for this matching policy, recorded on paper sessions and cited
    /// by promotion evidence.
    /// </summary>
    public const string MatchingModelVersion = "paper-match/1";

    /// <summary>
    /// Evaluates one order against the current observation.
    /// </summary>
    /// <param name="side">Order side.</param>
    /// <param name="type">Order type (market, limit, stop-market, or stop-limit).</param>
    /// <param name="limitPrice">Limit price for limit-style orders.</param>
    /// <param name="stopPrice">Stop price for stop-style orders.</param>
    /// <param name="stopAlreadyTriggered">
    /// True when a previous evaluation already met the stop trigger; the trigger is not
    /// re-evaluated once armed.
    /// </param>
    /// <param name="observation">The observed market data in effect for the symbol.</param>
    public static PaperMatchResult Evaluate(
        OrderSide side,
        OrderType type,
        decimal? limitPrice,
        decimal? stopPrice,
        bool stopAlreadyTriggered,
        in PaperMarketObservation observation)
    {
        return type switch
        {
            OrderType.Market => EvaluateMarket(side, observation, stopTriggered: false),
            OrderType.Limit => EvaluateLimit(side, limitPrice, observation, stopTriggered: false),
            OrderType.StopMarket => EvaluateStop(side, stopPrice, stopAlreadyTriggered, observation, isStopLimit: false, limitPrice),
            OrderType.StopLimit => EvaluateStop(side, stopPrice, stopAlreadyTriggered, observation, isStopLimit: true, limitPrice),
            _ => new PaperMatchResult(PaperMatchOutcome.NoMarketData, null, stopAlreadyTriggered)
        };
    }

    private static PaperMatchResult EvaluateMarket(
        OrderSide side, in PaperMarketObservation observation, bool stopTriggered)
    {
        var reference = AggressiveReferencePrice(side, observation);
        if (reference is not { } price)
        {
            return new PaperMatchResult(PaperMatchOutcome.NoMarketData, null, stopTriggered);
        }

        return new PaperMatchResult(
            PaperMatchOutcome.Filled,
            ClampToEnvelope(price, observation),
            stopTriggered);
    }

    private static PaperMatchResult EvaluateLimit(
        OrderSide side, decimal? limitPrice, in PaperMarketObservation observation, bool stopTriggered)
    {
        if (limitPrice is not { } limit || limit <= 0m)
        {
            return new PaperMatchResult(PaperMatchOutcome.NoMarketData, null, stopTriggered);
        }

        var reference = AggressiveReferencePrice(side, observation);
        if (reference is not { } price)
        {
            // No usable observation: a limit order rests until market data arrives — it
            // never fills at its own limit price without an observed market to cross.
            return new PaperMatchResult(
                observation.HasAnyObservation ? PaperMatchOutcome.Resting : PaperMatchOutcome.NoMarketData,
                null,
                stopTriggered);
        }

        var marketable = IsBuy(side) ? price <= limit : price >= limit;
        if (!marketable)
        {
            return new PaperMatchResult(PaperMatchOutcome.Resting, null, stopTriggered);
        }

        // Fill at the observed price — at or better than the limit by construction.
        return new PaperMatchResult(
            PaperMatchOutcome.Filled,
            ClampToEnvelope(price, observation),
            stopTriggered);
    }

    private static PaperMatchResult EvaluateStop(
        OrderSide side,
        decimal? stopPrice,
        bool stopAlreadyTriggered,
        in PaperMarketObservation observation,
        bool isStopLimit,
        decimal? limitPrice)
    {
        var triggered = stopAlreadyTriggered || IsStopTriggered(side, stopPrice, observation);
        if (!triggered)
        {
            return new PaperMatchResult(
                observation.HasAnyObservation ? PaperMatchOutcome.Resting : PaperMatchOutcome.NoMarketData,
                null,
                false);
        }

        return isStopLimit
            ? EvaluateLimit(side, limitPrice, observation, stopTriggered: true)
            : EvaluateMarket(side, observation, stopTriggered: true);
    }

    /// <summary>
    /// Trade-preferred stop trigger policy: last trade (or bar close) crossing the stop
    /// price triggers; with no trade-side observation the quote fallback applies (buy
    /// stops trigger on ask, sell stops on bid).
    /// </summary>
    private static bool IsStopTriggered(OrderSide side, decimal? stopPrice, in PaperMarketObservation observation)
    {
        if (stopPrice is not { } stop || stop <= 0m)
        {
            return false;
        }

        // Shared with the pre-trade fat-finger guard, so the control that refuses wrong-side
        // stops and the engine that fires them can never disagree about what a stop triggers on.
        var trigger = observation.ResolveStopTriggerPrice(side);
        if (trigger is not { } price)
        {
            return false;
        }

        return IsBuy(side) ? price >= stop : price <= stop;
    }

    /// <summary>
    /// The observed price a marketable order transacts against: buys pay the ask (falling
    /// back to last trade, then bar close), sells hit the bid (same fallbacks). The
    /// opposite side of the book alone is never used — that would fill better than any
    /// real counterparty offered.
    /// </summary>
    // Shared with the pre-trade fat-finger guard, so the control that refuses limits priced through
    // the market and the engine that fills them read the same observation the same way.
    private static decimal? AggressiveReferencePrice(OrderSide side, in PaperMarketObservation observation) =>
        observation.ResolveAggressiveReferencePrice(side);

    private static decimal ClampToEnvelope(decimal price, in PaperMarketObservation observation)
    {
        if (observation.EnvelopeLow is { } low && price < low)
        {
            price = low;
        }

        if (observation.EnvelopeHigh is { } high && price > high)
        {
            price = high;
        }

        return price;
    }

    private static bool IsBuy(OrderSide side) => side == OrderSide.Buy;
}
