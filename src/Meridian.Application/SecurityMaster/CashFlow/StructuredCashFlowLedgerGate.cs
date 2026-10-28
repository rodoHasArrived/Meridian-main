using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster.CashFlow;

/// <summary>
/// Shared postability gate for routing a structured cash flow projection to the ledger. Both the
/// preview bridge (<see cref="StructuredCashFlowLedgerBridge"/>) and the posting bridge
/// (<c>SecurityMasterAmortizationLedgerBridge</c>) consult this single gate so a source without
/// verified freshness, a stale source, a rate-shocked what-if scenario, or a schedule generated from
/// declared schedule conventions (one carrying <see cref="StructuredCashFlowProjectionDto.ScheduleEvidence"/>)
/// can never reach the general ledger through either path.
/// </summary>
public static class StructuredCashFlowLedgerGate
{
    /// <summary>
    /// Returns <see langword="null"/> when the projection may post to the ledger; otherwise a
    /// human-readable reason describing the gate that blocked it. Freshness is evaluated before the
    /// scenario so a source without verified freshness or a stale source is reported even for a
    /// what-if projection.
    /// </summary>
    public static string? EvaluateBlockReason(StructuredCashFlowProjectionDto projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        if (projection.BlockedReason is { Length: > 0 } blockedReason)
            return blockedReason;

        // A declared schedule (ADR-025) stays off the ledger until journal identity keys on schedule
        // period identity and a retention decision covers its calendar evidence.
        if (projection.ScheduleEvidence is not null)
            return "Declared-schedule projections are not postable until journal identity keys on schedule period identity and calendar evidence retention is decided.";

        if (projection.IsNormalizedPer100)
            return "A normalized per-100 analytical schedule cannot be posted without actual principal or notional.";

        if (projection.Staleness == StructuredCashFlowStaleness.Unknown)
        {
            return "Cash flow source freshness is unknown; refresh or confirm the source before posting accruals to the ledger.";
        }

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
