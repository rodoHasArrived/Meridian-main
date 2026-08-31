namespace Meridian.Execution.Interfaces;

/// <summary>
/// Implemented by paper gateways that hold resting limit/stop orders. The host's market
/// event tap pokes this after recording new market data for a symbol so resting orders are
/// re-evaluated against the fresh observation without polling. Implementations must be
/// non-blocking: evaluation work is scheduled, never run inline on the market data path.
/// </summary>
public interface IPaperFillEvaluationTrigger
{
    /// <summary>Schedules re-evaluation of resting paper orders for <paramref name="symbol"/>.</summary>
    void EvaluateSymbol(string symbol);
}
