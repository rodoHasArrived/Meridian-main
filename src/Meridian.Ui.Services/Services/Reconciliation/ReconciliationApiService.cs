using Meridian.Infrastructure.Reconciliation;
using Meridian.Ui.Shared.Contracts.Reconciliation;

namespace Meridian.Ui.Services.Services.Reconciliation;

public interface IReconciliationApiService
{
    Task<IReadOnlyList<StatementImportSummaryDto>> ListImportsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<StatementRunSummaryDto>> ListStatementRunsAsync(CancellationToken ct = default);
    Task<StatementRunSummaryDto?> GetStatementRunAsync(string runId, CancellationToken ct = default);
    Task<IReadOnlyList<StatementRunExceptionDto>> ListOpenExceptionsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ReconciliationCaseSummaryDto>> ListOpenCasesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ReconciliationQueueAccountStatusDto>> ListQueueStatusAsync(CancellationToken ct = default);
}

public sealed class ReconciliationApiService(ICanonicalStatementStore importStore, IReconciliationCaseStore caseStore, IReconciliationBreakStore breakStore) : IReconciliationApiService
{
    public async Task<IReadOnlyList<StatementImportSummaryDto>> ListImportsAsync(CancellationToken ct = default)
        => (await importStore.ListImportsAsync(ct)).Select(i => new StatementImportSummaryDto(i.ImportId, i.Broker, i.StatementDate.ToString("yyyy-MM-dd"), i.ImportedAtUtc.ToString("O"), i.RawRowCount, i.NormalizedRowCount)).ToList();


    public async Task<IReadOnlyList<StatementRunSummaryDto>> ListStatementRunsAsync(CancellationToken ct = default)
        => (await importStore.ListImportsAsync(ct)).Select(i => new StatementRunSummaryDto(i.ImportId, i.ImportId, i.ImportedAtUtc.ToString("O"), i.ImportedAtUtc.ToString("O"), i.NormalizedRowCount, 0, 0, 0)).ToList();

    public async Task<StatementRunSummaryDto?> GetStatementRunAsync(string runId, CancellationToken ct = default)
        => (await ListStatementRunsAsync(ct)).FirstOrDefault(x => x.RunId == runId);

    public async Task<IReadOnlyList<StatementRunExceptionDto>> ListOpenExceptionsAsync(CancellationToken ct = default)
        => (await breakStore.ListOpenAsync(ct)).Select(x => new StatementRunExceptionDto(x.BreakId, x.RunId, x.ImportId, x.SourceReference, x.BreakCode, x.Category, x.Delta, x.Tolerance, x.ToleranceBreached, x.CreatedAtUtc.ToString("O"), x.Status)).ToList();

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
}
