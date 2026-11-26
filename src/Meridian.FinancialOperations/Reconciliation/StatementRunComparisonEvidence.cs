using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Reconciliation;
using Meridian.Domain.Reconciliation;
using Meridian.DataIntegration.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>Retains the executed policy and population availability, never the later current policy.</summary>
internal static class StatementRunComparisonEvidence
{
    // Changes to matching/normalization semantics must advance this token before comparisons can
    // be used to establish clearing across runs produced by the new implementation.
    private const string CurrentMatcherRevision = "statement-run-matcher-v4";
    private const string PreviousMatcherRevision = "statement-run-matcher-v3";

    internal static string PolicyFingerprint(StatementToleranceProfile tolerance, string? mapping, bool legacy = false)
    {
        var revision = legacy ? PreviousMatcherRevision : CurrentMatcherRevision;
        return mapping is null
            ? Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new { MatcherRevision = revision, Tolerance = tolerance }))
            : Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new
            { MatcherRevision = revision, Tolerance = tolerance, ExecutedMappingFingerprint = mapping }));
    }

    public static StatementRunMatchArtifact Retain(StatementRunMatchArtifact artifact,
        IReadOnlyList<CanonicalStatementRow> rows, InternalReconciliationPopulations populations,
        StatementToleranceProfile tolerance, string? executedMappingFingerprint = null, DateOnly? effectiveDate = null)
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
        var policyFingerprint = PolicyFingerprint(tolerance, executedMappingFingerprint);
        return artifact with
        {
            SourceComparisonComplete = complete,
            SourceComparisonMappingFingerprint = executedMappingFingerprint,
            SourceComparisonPopulationKinds = kinds,
            SourceComparisonPolicyFingerprint = policyFingerprint,
            ExecutedTolerancePolicy = effectiveDate is { } date
                ? new ExecutedStatementTolerancePolicy(tolerance.ProfileId, tolerance.Version, date, policyFingerprint)
                : null
        };
    }
}
