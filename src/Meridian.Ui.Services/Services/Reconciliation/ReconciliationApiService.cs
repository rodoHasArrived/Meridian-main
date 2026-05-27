using Meridian.Infrastructure.Reconciliation;
using Meridian.Ui.Shared.Contracts.Reconciliation;

namespace Meridian.Ui.Services.Services.Reconciliation;

public interface IReconciliationApiService
{
    Task<IReadOnlyList<StatementImportSummaryDto>> ListImportsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ReconciliationCaseSummaryDto>> ListOpenCasesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ReconciliationQueueAccountStatusDto>> ListQueueStatusAsync(CancellationToken ct = default);
}

public sealed class ReconciliationApiService(ICanonicalStatementStore importStore, IReconciliationCaseStore caseStore) : IReconciliationApiService
{
    public async Task<IReadOnlyList<StatementImportSummaryDto>> ListImportsAsync(CancellationToken ct = default)
        => (await importStore.ListImportsAsync(ct)).Select(i => new StatementImportSummaryDto(i.ImportId, i.Broker, i.StatementDate.ToString("yyyy-MM-dd"), i.ImportedAtUtc.ToString("O"), i.RawRowCount, i.NormalizedRowCount)).ToList();

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
