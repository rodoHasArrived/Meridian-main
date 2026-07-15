using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster.CashFlow;

/// <summary>
/// Shared postability gate for routing a structured cash flow projection to the ledger. Both the
/// preview bridge (<see cref="StructuredCashFlowLedgerBridge"/>) and the posting bridge
/// (<c>SecurityMasterAmortizationLedgerBridge</c>) consult this single gate so a stale source or a
/// rate-shocked what-if scenario can never reach the general ledger through either path.
/// </summary>
public static class StructuredCashFlowLedgerGate
{
    /// <summary>
    /// Returns <see langword="null"/> when the projection may post to the ledger; otherwise a
    /// human-readable reason describing the gate that blocked it. Staleness is evaluated before the
    /// scenario so a stale source is reported even for a what-if projection.
    /// </summary>
    public static string? EvaluateBlockReason(StructuredCashFlowProjectionDto projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        if (projection.Staleness == StructuredCashFlowStaleness.Stale)
        {
            return "Cash flow source is stale; refresh the source before posting accruals to the ledger.";
        }

        if (projection.Scenario != StructuredCashFlowScenario.Base)
        {
            return $"Only base-scenario projections are postable; '{projection.Scenario}' is a what-if scenario.";
        }

        return null;
    }

    /// <summary>True when the projection passes every ledger-posting gate.</summary>
    public static bool IsPostable(StructuredCashFlowProjectionDto projection)
        => EvaluateBlockReason(projection) is null;
}
