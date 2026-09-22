using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>Retains the executed policy and population availability, never the later current policy.</summary>
internal static class StatementRunComparisonEvidence
{
    // Changes to matching/normalization semantics must advance this token before comparisons can
    // be used to establish clearing across runs produced by the new implementation.
    private const string MatcherRevision = "statement-run-matcher-v1";

    public static StatementRunMatchArtifact Retain(StatementRunMatchArtifact artifact,
        IReadOnlyList<CanonicalStatementRow> rows, InternalReconciliationPopulations populations,
        StatementToleranceProfile tolerance)
    {
        var kinds = rows.Select(row => row.ActivityType.Trim().ToLowerInvariant() switch
        {
            "position" => "position",
            "cash" or "cashbalance" => "cash",
            _ => "transaction"
        }).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        // Existing providers degrade missing/failed reads to empty populations. Empty cannot prove
        // authoritative absence. Require evidence in every represented lane; an empty statement or
        // unavailable lane remains non-comparable rather than certifying a false clearing.
        var complete = kinds.Length > 0 && kinds.All(kind => kind switch
        {
            "position" => populations.Positions.Count > 0,
            "cash" => populations.CashBalances.Count > 0,
            _ => populations.LedgerTransactions.Count > 0
        });
        return artifact with
        {
            SourceComparisonComplete = complete,
            SourceComparisonPopulationKinds = kinds,
            SourceComparisonPolicyFingerprint = Sha256Digest.ComputeUtf8(
                JsonSerializer.Serialize(new { MatcherRevision, Tolerance = tolerance }))
        };
    }
}
