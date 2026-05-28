using Meridian.Contracts.Workstation;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Ui.Shared.Contracts.Reconciliation;

namespace Meridian.Ui.Services.Services.Reconciliation;

public sealed class ReconciliationApiService(ICanonicalStatementStore importStore, IReconciliationCaseStore caseStore, IReconciliationBreakStore breakStore) : IReconciliationApiService
{
    public async Task<IReadOnlyList<StatementImportSummaryDto>> ListImportsAsync(CancellationToken ct = default)
        => (await importStore.ListImportsAsync(ct)).Select(i => new StatementImportSummaryDto(i.ImportId, i.Broker, i.StatementDate.ToString("yyyy-MM-dd"), i.ImportedAtUtc.ToString("O"), i.RawRowCount, i.NormalizedRowCount)).ToList();


    public async Task<IReadOnlyList<StatementRunSummaryDto>> ListStatementRunsAsync(CancellationToken ct = default)
    {
        var imports = await importStore.ListImportsAsync(ct).ConfigureAwait(false);
        var breaks = await breakStore.ListOpenAsync(ct).ConfigureAwait(false);
        var cases = await caseStore.ListAsync(ct).ConfigureAwait(false);
        return imports.Select(i => ToStatementRunSummary(i, breaks, cases)).ToList();
    }

    public async Task<StatementRunSummaryDto?> GetStatementRunAsync(string runId, CancellationToken ct = default)
        => (await ListStatementRunsAsync(ct).ConfigureAwait(false)).FirstOrDefault(x => string.Equals(x.RunId, runId, StringComparison.OrdinalIgnoreCase));

    public async Task<IReadOnlyList<StatementRunExceptionDto>> ListOpenExceptionsAsync(CancellationToken ct = default)
        => (await breakStore.ListOpenAsync(ct)).Select(x => new StatementRunExceptionDto(x.BreakId, x.RunId, x.ImportId, x.SourceReference, x.BreakCode, x.Category, x.Delta, x.Tolerance, x.ToleranceBreached, x.CreatedAtUtc.ToString("O"), x.Status)).ToList();

    public async Task<IReadOnlyList<StatementBreakDto>> ListOpenStatementBreaksAsync(CancellationToken ct = default)
        => (await breakStore.ListOpenAsync(ct)).Select(x => new StatementBreakDto(
            BreakId: x.BreakId,
            BreakType: MapStatementBreakType(x.Category, x.BreakCode),
            Severity: x.ToleranceBreached ? StatementValidationSeverity.Error : StatementValidationSeverity.Warning,
            MatchTier: StatementMatchTier.Manual,
            StatementReference: x.SourceReference,
            Description: $"{x.Category} break {x.BreakCode} requires statement reconciliation review.",
            StatementAmount: x.Delta,
            BookAmount: null,
            Delta: x.Delta,
            Tolerance: x.Tolerance,
            Currency: null,
            CreatedAtUtc: x.CreatedAtUtc,
            Status: x.Status,
            InternalReference: x.RunId,
            Owner: null,
            LastObservedAtUtc: x.CreatedAtUtc,
            RecommendedAction: "ReviewAndResolve",
            EvidenceLink: $"/api/workstation/reconciliation/exceptions/{Uri.EscapeDataString(x.BreakId)}")).ToList();

    public async Task<IReadOnlyList<ReconciliationCaseSummaryDto>> ListOpenCasesAsync(CancellationToken ct = default)
        => (await caseStore.ListAsync(ct)).Where(c => c.Status == "Open").Select(c => new ReconciliationCaseSummaryDto(c.CaseId, c.ImportId, c.Status, c.Reason, c.Confidence, c.Rationale, c.CreatedAtUtc.ToString("O"))).ToList();

    public async Task<IReadOnlyList<ReconciliationQueueAccountStatusDto>> ListQueueStatusAsync(CancellationToken ct = default)
    {
        var openCases = (await caseStore.ListAsync(ct).ConfigureAwait(false)).Where(c => c.Status == "Open").ToList();
        return openCases
            .GroupBy(c => c.ImportId)
            .Select(group => new ReconciliationQueueAccountStatusDto(
                AccountId: Guid.Empty,
                AccountCode: group.Key,
                QueueState: group.Any(c => c.Confidence < 0.5m) ? "Blocked" : "Review",
                UnresolvedBreakCount: group.Count(),
                SignOffReady: false,
                NextBestAction: "Resolve open reconciliation breaks before operator sign-off.",
                BlockerReason: "Unresolved breaks remain in the reconciliation queue.",
                EvidenceLinks: group.Select(c => $"/api/workstation/reconciliation/cases/{Uri.EscapeDataString(c.CaseId)}").ToList()))
            .ToList();
    }

    private static StatementRunSummaryDto ToStatementRunSummary(
        Meridian.Domain.Reconciliation.CanonicalStatementImport import,
        IReadOnlyList<Meridian.Domain.Reconciliation.ReconciliationBreakRecord> breaks,
        IReadOnlyList<Meridian.Domain.Reconciliation.ReconciliationCase> cases)
    {
        var relatedBreaks = breaks
            .Where(item => string.Equals(item.ImportId, import.ImportId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.RunId, import.ImportId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var relatedCases = cases
            .Where(item => string.Equals(item.ImportId, import.ImportId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var openExceptionCount = relatedBreaks.Count(item => string.Equals(item.Status, "Open", StringComparison.OrdinalIgnoreCase));
        var matchedCount = Math.Max(0, import.NormalizedRowCount - relatedBreaks.Length);
        var evidenceLink = BuildEvidenceLink(import, relatedBreaks, relatedCases, matchedCount);

        return new StatementRunSummaryDto(
            import.ImportId,
            import.ImportId,
            import.ImportedAtUtc.ToString("O"),
            import.ImportedAtUtc.ToString("O"),
            matchedCount,
            0,
            0,
            openExceptionCount,
            [evidenceLink]);
    }

    private static StatementRunEvidenceLinkDto BuildEvidenceLink(
        Meridian.Domain.Reconciliation.CanonicalStatementImport import,
        IReadOnlyList<Meridian.Domain.Reconciliation.ReconciliationBreakRecord> breaks,
        IReadOnlyList<Meridian.Domain.Reconciliation.ReconciliationCase> cases,
        int matchedCount)
    {
        var runId = import.ImportId;
        var breakIds = breaks
            .Select(static item => item.BreakId)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var caseIds = cases
            .Select(static item => item.CaseId)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sourceFileHash = FirstNonEmpty(import.SourceFileHash, import.SourceChecksum);
        var brokerCustodian = FirstNonEmpty(import.SourceInstitution, import.Broker);
        var account = FirstNonEmpty(import.ExternalAccountId, import.FundAccountId);
        var validationSummary = "Passed: 0 issue(s), 0 error(s), 0 warning(s).";
        var matchSummary = $"{matchedCount}/{import.NormalizedRowCount} item(s) matched; {breakIds.Length} break(s); {caseIds.Length} case(s).";

        return new StatementRunEvidenceLinkDto(
            EvidenceId: $"statement-run:{runId}",
            EvidenceRoute: $"/api/workstation/evidence/statement-run/{Uri.EscapeDataString(runId)}",
            RunId: runId,
            SourceFileHash: sourceFileHash,
            BrokerCustodian: brokerCustodian,
            Account: account,
            StatementPeriodStart: import.StatementPeriodStart.ToString("yyyy-MM-dd"),
            StatementPeriodEnd: import.StatementPeriodEnd.ToString("yyyy-MM-dd"),
            MappingProfileId: FirstNonEmpty(import.MappingProfileId, "unknown"),
            MappingProfileVersion: 1,
            ToleranceProfileId: FirstNonEmpty(import.ToleranceProfileId, "statement-default"),
            ToleranceProfileVersion: 1,
            ValidationSummary: validationSummary,
            MatchSummary: matchSummary,
            BreakIds: breakIds,
            CaseIds: caseIds,
            ImportedBy: FirstNonEmpty(import.ImportedBy, "system"),
            ImportedAtUtc: import.ImportedAtUtc.ToString("O"),
            ReconciledBy: FirstNonEmpty(import.ImportedBy, "system"),
            ReconciledAtUtc: import.ImportedAtUtc.ToString("O"));
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static StatementBreakType MapStatementBreakType(string category, string breakCode)
    {
        if (category.Equals("position", StringComparison.OrdinalIgnoreCase))
        {
            return breakCode.Contains("missing", StringComparison.OrdinalIgnoreCase)
                ? StatementBreakType.MissingBookPosition
                : StatementBreakType.PositionQuantityMismatch;
        }

        if (category.Equals("cash", StringComparison.OrdinalIgnoreCase))
        {
            return StatementBreakType.CashBalanceMismatch;
        }

        if (category.Equals("transaction", StringComparison.OrdinalIgnoreCase))
        {
            return breakCode.Contains("missing", StringComparison.OrdinalIgnoreCase)
                ? StatementBreakType.MissingBookTransaction
                : StatementBreakType.TransactionAmountMismatch;
        }

        return StatementBreakType.ValidationFailure;
    }
}
