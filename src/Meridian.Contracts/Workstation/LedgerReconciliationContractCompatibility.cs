namespace Meridian.Contracts.Workstation;

public static class LedgerReconciliationContractCompatibility
{
    public static void EnsureFundLedgerSummaryRequiredFields(FundLedgerSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (string.IsNullOrWhiteSpace(summary.FundProfileId)) throw new InvalidOperationException("FundProfileId is required.");
        if (string.IsNullOrWhiteSpace(summary.FundDisplayName)) throw new InvalidOperationException("FundDisplayName is required.");
        if (summary.TrialBalance is null) throw new InvalidOperationException("TrialBalance is required.");
        if (summary.Journal is null) throw new InvalidOperationException("Journal is required.");
        if (summary.EntityCount < 0 || summary.SleeveCount < 0 || summary.VehicleCount < 0)
        {
            throw new InvalidOperationException("Entity/Sleeve/Vehicle counts cannot be negative.");
        }
    }

    public static void EnsureReconciliationSummaryRequiredFields(ReconciliationRunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (string.IsNullOrWhiteSpace(summary.ReconciliationRunId)) throw new InvalidOperationException("ReconciliationRunId is required.");
        if (string.IsNullOrWhiteSpace(summary.RunId)) throw new InvalidOperationException("RunId is required.");
        if (summary.BreakCount < 0 || summary.OpenBreakCount < 0) throw new InvalidOperationException("Break counters cannot be negative.");
        if (summary.OpenBreakCount > summary.BreakCount) throw new InvalidOperationException("OpenBreakCount cannot exceed BreakCount.");
    }

    public static void EnsureContinuityRequiredFields(StrategyRunContinuityDto continuity)
    {
        ArgumentNullException.ThrowIfNull(continuity);
        if (continuity.Run is null) throw new InvalidOperationException("Run payload is required.");
        if (continuity.Lineage is null) throw new InvalidOperationException("Lineage payload is required.");
        if (continuity.ContinuityStatus is null) throw new InvalidOperationException("ContinuityStatus payload is required.");

        if (!continuity.ContinuityStatus.HasRun)
        {
            throw new InvalidOperationException("ContinuityStatus.HasRun must be true for readiness/governance flows.");
        }

        if (continuity.ContinuityStatus.Warnings is null)
        {
            throw new InvalidOperationException("ContinuityStatus.Warnings is required.");
        }
    }
}
