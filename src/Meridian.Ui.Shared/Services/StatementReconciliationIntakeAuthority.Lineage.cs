using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

public sealed partial class StatementReconciliationIntakeAuthority
{
    private async Task ObservePublishedRunAsync(StatementImportCommitResultDto commit,
        CanonicalStatementImport import, StatementAccountingScope accounting,
        ReconciliationBreakQueueScope access, string institution,
        IReadOnlyList<ReconciliationBreakQueueItem> retained, CancellationToken ct)
    {
        // Legacy/custom queue implementations retain their existing behavior. Production's durable
        // queue and canonical statement store implement this source-backed comparison seam.
        if (_canonicalStatements is null || _matchArtifacts is null || _breakQueue is not IReconciliationBreakObservationRepository observations)
            return;
        if (commit.Status is not ("Imported" or "Duplicate"))
            return;
        var canonical = await _canonicalStatements.GetImportAsync(import.ImportId, ct).ConfigureAwait(false);
        if (canonical is null || canonical.Rows.Count != import.NormalizedRowCount
            || !string.Equals(canonical.Import.SourceChecksum, import.SourceChecksum, StringComparison.Ordinal))
            throw Failure("STATEMENT_LINEAGE_SOURCE_UNAVAILABLE", "The complete retained statement population is required for source comparison.");

        var artifact = await _matchArtifacts.GetAsync(import.ImportId, ct).ConfigureAwait(false);
        if (artifact is null || artifact.ImportId != import.ImportId)
            throw Failure("STATEMENT_LINEAGE_MATCH_UNAVAILABLE", "The immutable completed match population is required for source comparison.");

        if (!CanCompareSourceRun(artifact))
            return;

        var rowSubjects = canonical.Rows.ToDictionary(row => $"{import.ImportId}:{row.SourceRowNumber}", SourceSubject);
        var duplicateSubjects = rowSubjects.Values.Where(value => value is not null)
            .GroupBy(value => value, StringComparer.Ordinal).Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        var inputs = new List<ReconciliationBreakObservation>();
        foreach (var sourceBreak in artifact.Breaks)
        {
            var item = retained.SingleOrDefault(item => item.SourceBreakId == sourceBreak.BreakId);
            if (item is null || sourceBreak.SourceReference is null
                || !rowSubjects.TryGetValue(sourceBreak.SourceReference, out var subject)
                || subject is null || duplicateSubjects.Contains(subject))
            {
                // A row ordinal or mutable amount/date is not a business identity. Keep ambiguous,
                // internal-only, and externally unreferenced transactions explicitly untracked;
                // never use a partial population to clear a previous observation.
                return;
            }
            inputs.Add(new ReconciliationBreakObservation(item.BreakId,
                JsonSerializer.Serialize(new[] { subject, sourceBreak.BreakCode })));
        }
        await observations.ObserveCompletedRunAsync(new ReconciliationCompletedRunObservation(
            import.ImportId,
            new ReconciliationRunObservationScope(access.TenantId, access.CompanyId,
                accounting.FundProfileId, import.FundAccountId, accounting.LedgerBookId,
                accounting.AccountingPeriodId.ToString("D"),
                ComparisonSourceIdentity(import, artifact, institution),
                import.ExternalAccountId),
            import.ImportedAtUtc, true, true, inputs), ct).ConfigureAwait(false);
    }

    internal static string ComparisonSourceIdentity(CanonicalStatementImport import,
        Meridian.Infrastructure.Reconciliation.StatementRunMatchArtifact artifact, string institution)
        => JsonSerializer.Serialize(new { institution, import.MappingProfileId, import.ToleranceProfileId,
            artifact.SourceComparisonPolicyFingerprint, artifact.SourceComparisonPopulationKinds });

    internal static bool CanCompareSourceRun(Meridian.Infrastructure.Reconciliation.StatementRunMatchArtifact artifact)
        => artifact.SourceComparisonComplete == true
           && Meridian.Contracts.Integrity.Sha256Digest.IsWellFormed(artifact.SourceComparisonPolicyFingerprint)
           && artifact.SourceComparisonPopulationKinds is { Count: > 0 }
           && !artifact.Breaks.Any(item => string.Equals(item.Classification,
               ReconciliationBreakClassifications.InternalTransactionPopulationUnavailable, StringComparison.OrdinalIgnoreCase));

    internal static string? SourceSubject(CanonicalStatementRow row)
    {
        var activity = row.ActivityType.Trim().ToLowerInvariant();
        if (activity == "position")
            return string.IsNullOrWhiteSpace(row.Symbol) ? null
                : JsonSerializer.Serialize(new[] { "position", row.Symbol, row.Currency });
        if (activity is "cash" or "cashbalance")
            return JsonSerializer.Serialize(new[] { "cash", row.Currency });
        return string.IsNullOrWhiteSpace(row.ExternalTransactionId) ? null
            : JsonSerializer.Serialize(new[] { "transaction", row.ExternalTransactionId, activity, row.Symbol, row.Currency });
    }
}
