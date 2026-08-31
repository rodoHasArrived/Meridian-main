using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;

namespace Meridian.Execution;

/// <summary>
/// Deterministic identity for an outbound fill event. The fill id is derived from the
/// execution report's own content rather than generated, so a replayed report produces the
/// same id and the accounting handoff stays idempotent across retries and restarts.
/// </summary>
public sealed partial class OrderManagementSystem
{
    private static TradeExecutedEvent CreateTradeExecutedEvent(
        ExecutionReport fillIncrement,
        decimal cumulativeFilledQuantity,
        decimal realizedPnl,
        decimal newCash,
        string? financialAccountId,
        bool usesFaceValuePercentageOfPar)
    {
        if (fillIncrement.FillPrice is not { } fillPrice)
        {
            throw new InvalidOperationException(
                $"Fill report '{fillIncrement.OrderId}' for '{fillIncrement.Symbol}' has no execution price.");
        }

        var fillId = CreateDeterministicFillId(
            fillIncrement,
            cumulativeFilledQuantity,
            financialAccountId);

        return new TradeExecutedEvent(
            fillId,
            fillIncrement.ClientOrderId ?? fillIncrement.OrderId,
            fillIncrement.Symbol,
            fillIncrement.Side,
            fillIncrement.FilledQuantity,
            fillPrice,
            fillIncrement.Commission ?? 0m,
            realizedPnl,
            newCash,
            fillIncrement.Timestamp,
            financialAccountId,
            usesFaceValuePercentageOfPar);
    }

    /// <summary>
    /// Derives the canonical OMS fill identity without requiring an accounting publisher.
    /// Paper-session persistence uses this same identity so retries and restart replay share
    /// the accounting handoff's idempotency key.
    /// </summary>
    internal static Guid CreateDeterministicFillId(
        ExecutionReport fillIncrement,
        decimal cumulativeFilledQuantity,
        string? financialAccountId)
    {
        // PaperSessionFillRecord is the one canonical identity authority. Cumulative portfolio
        // state and account scope are deliberately compatibility-only inputs here: including
        // either would give the same broker fill different ids across producer paths or retries.
        _ = cumulativeFilledQuantity;
        _ = financialAccountId;
        return PaperSessionFillRecord.ComputeCanonicalFillId(fillIncrement);
    }
}
