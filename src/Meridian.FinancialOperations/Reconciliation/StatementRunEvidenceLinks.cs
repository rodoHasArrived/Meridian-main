using Meridian.Domain.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

public sealed record StatementRunValidationSummary(
    int IssueCount,
    int ErrorCount,
    int WarningCount,
    string Status);

public sealed record StatementRunMatchEvidenceSummary(
    int StatementItemCount,
    int MatchedItemCount,
    int ReviewRequiredItemCount,
    int BreakCount);

public sealed record StatementRunEvidenceLink(
    string EvidenceId,
    string EvidenceRoute,
    string RunId,
    string SourceFileHash,
    string BrokerCustodian,
    string Account,
    DateOnly StatementPeriodStart,
    DateOnly StatementPeriodEnd,
    string MappingProfileId,
    int MappingProfileVersion,
    string ToleranceProfileId,
    int ToleranceProfileVersion,
    StatementRunValidationSummary ValidationSummary,
    StatementRunMatchEvidenceSummary MatchSummary,
    IReadOnlyList<string> BreakIds,
    IReadOnlyList<string> CaseIds,
    string ImportedBy,
    DateTimeOffset ImportedAtUtc,
    string ReconciledBy,
    DateTimeOffset ReconciledAtUtc);

public static class StatementRunEvidenceLinkFactory
{
    public const int DefaultMappingProfileVersion = 1;

    public static StatementRunEvidenceLink Create(
        CanonicalStatementImport import,
        IReadOnlyList<ReconciliationBreakRecord> breaks,
        IReadOnlyList<ReconciliationCase> cases,
        string? runId = null,
        string? reconciledBy = null,
        DateTimeOffset? reconciledAtUtc = null,
        int mappingProfileVersion = DefaultMappingProfileVersion,
        int? toleranceProfileVersion = null,
        StatementRunValidationSummary? validationSummary = null)
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(breaks);
        ArgumentNullException.ThrowIfNull(cases);

        var effectiveRunId = string.IsNullOrWhiteSpace(runId) ? import.ImportId : runId.Trim();
        var completedAt = reconciledAtUtc ?? DateTimeOffset.UtcNow;
        var breakIds = breaks
            .Where(item => string.Equals(item.RunId, effectiveRunId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.ImportId, import.ImportId, StringComparison.OrdinalIgnoreCase))
            .Select(static item => item.BreakId)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var caseIds = cases
            .Where(item => string.Equals(item.ImportId, import.ImportId, StringComparison.OrdinalIgnoreCase))
            .Select(static item => item.CaseId)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var matchedCount = Math.Max(0, import.NormalizedRowCount - breakIds.Length);
        var evidenceId = $"statement-run:{effectiveRunId}";

        return new StatementRunEvidenceLink(
            EvidenceId: evidenceId,
            EvidenceRoute: $"/api/workstation/evidence/statement-run/{Uri.EscapeDataString(effectiveRunId)}",
            RunId: effectiveRunId,
            SourceFileHash: FirstNonEmpty(import.SourceFileHash, import.SourceChecksum),
            BrokerCustodian: FirstNonEmpty(import.SourceInstitution, import.Broker),
            Account: FirstNonEmpty(import.ExternalAccountId, import.FundAccountId),
            StatementPeriodStart: import.StatementPeriodStart,
            StatementPeriodEnd: import.StatementPeriodEnd,
            MappingProfileId: FirstNonEmpty(import.MappingProfileId, "unknown"),
            MappingProfileVersion: mappingProfileVersion,
            ToleranceProfileId: FirstNonEmpty(import.ToleranceProfileId, StatementToleranceProfile.DefaultProfileId),
            ToleranceProfileVersion: toleranceProfileVersion ?? StatementToleranceProfile.DefaultProfileVersion,
            ValidationSummary: validationSummary ?? new StatementRunValidationSummary(0, 0, 0, "Passed"),
            MatchSummary: new StatementRunMatchEvidenceSummary(import.NormalizedRowCount, matchedCount, breakIds.Length, breakIds.Length),
            BreakIds: breakIds,
            CaseIds: caseIds,
            ImportedBy: FirstNonEmpty(import.ImportedBy, "system"),
            ImportedAtUtc: import.ImportedAtUtc,
            ReconciledBy: string.IsNullOrWhiteSpace(reconciledBy) ? FirstNonEmpty(import.ImportedBy, "system") : reconciledBy.Trim(),
            ReconciledAtUtc: completedAt);
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
