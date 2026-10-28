namespace Meridian.Contracts.Workstation;

/// <summary>
/// Validates the canonical identity fields required on a strategy run summary.
/// </summary>
public static class StrategyRunContractCompatibility
{
    public static void EnsureCanonicalIdentity(StrategyRunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var identity = summary.Identity ?? throw new InvalidOperationException("Run projection missing canonical identity.");
        if (string.IsNullOrWhiteSpace(identity.CanonicalRunKey))
            throw new InvalidOperationException("CanonicalRunKey is required.");
        if (identity.Mode != summary.Mode)
            throw new InvalidOperationException("Identity mode must match summary mode.");
    }
}
