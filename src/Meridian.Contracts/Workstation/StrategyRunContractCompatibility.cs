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

    /// <summary>Discloses observed source differences and missing evidence among compared backtests.</summary>
    public static IReadOnlyList<string> DescribeSourceMixDifferences(IReadOnlyList<string?> backtestSourceMixHashes)
    {
        if (backtestSourceMixHashes.Count < 2)
            return [];

        var warnings = new List<string>(2);
        if (backtestSourceMixHashes.Where(static hash => hash is not null)
            .Distinct(StringComparer.Ordinal).Skip(1).Any())
        {
            warnings.Add("These backtests replayed different provider mixes.");
        }

        if (backtestSourceMixHashes.Any(static hash => hash is null))
            warnings.Add("Replay provider mix is unavailable for at least one backtest.");

        return warnings;
    }
}
