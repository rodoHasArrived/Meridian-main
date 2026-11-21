using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Evidence;

public sealed partial class StatementReconciliationReportWorkflowService
{
    public bool SupportsReviewedRowCases => _imports is StatementImportService && _breakQueue is not null && _intakeAuthority is not null;

    private async Task<WorkflowSnapshot> PublishReviewedRowCasesAsync(WorkflowSnapshot snapshot, CancellationToken ct)
    {
        var accepted = snapshot.Request.RowDispositions.Where(row => row.Disposition == "AcceptAsBreak").ToArray();
        if (accepted.Length == 0)
            return snapshot;
        if (!SupportsReviewedRowCases || _imports is not StatementImportService imports
            || snapshot.Request.ReviewId is not { } reviewId || snapshot.ImportResult is not { } import
            || snapshot.Request.AccountingScope is not { } accountingScope || snapshot.Workflow.CompanyId is not { } companyId)
            throw new InvalidDataException("The retained row-to-case coordinator and exact accounting authority are required.");
        var review = await imports.GetReviewAsync(reviewId, snapshot.Workflow.TenantId, companyId, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("The retained row review is unavailable in the workflow scope.");
        var queueScope = new ReconciliationBreakQueueScope(snapshot.Workflow.TenantId, companyId);
        var casesByRow = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var decision in accepted)
        {
            var row = review.Rows.SingleOrDefault(row => row.RowId == decision.RowId)
                ?? throw new InvalidDataException("The accepted row is not part of the retained review.");
            if (!row.CanAcceptAsBreak || string.IsNullOrWhiteSpace(decision.Reason))
                throw new InvalidDataException("Accept-as-break requires an eligible normalized row and an operator reason.");
            var caseId = "statement-review:" + Sha256Digest.ComputeUtf8($"{snapshot.Workflow.TenantId}\n{companyId}\n{reviewId}\n{row.RowId}");
            var item = new ReconciliationBreakQueueItem(caseId, import.RunId, "Statement row review",
                ReconciliationBreakCategory.ClassificationGap, ReconciliationBreakQueueStatus.Open, 0m,
                decision.Reason.Trim(), null, snapshot.Workflow.CreatedAtUtc, snapshot.Workflow.CreatedAtUtc,
                SourceType: "statement-import-review", SourceSystem: "operator-statement-review",
                SourceReference: $"statement-review:{reviewId}/{row.RowId}", SourceImportId: import.RunId,
                SourceBreakId: row.RowId, SourceFingerprint: Sha256Digest.ComputeUtf8($"{reviewId}\n{row.RowId}"),
                FundAccountId: snapshot.Request.FundAccountId,
                LedgerBookId: accountingScope.LedgerBookId,
                AccountingPeriodId: accountingScope.AccountingPeriodId.ToString("D"), AsOfDate: accountingScope.AsOfDate,
                BlockedOutputs: ["FinalReport", "PeriodClose", "ClientDelivery"],
                EvidenceLinks: [$"evidence:statement-import-review:{reviewId}:{row.RowId}", $"statement-run:{import.RunId}"],
                Measures: [new(ReconciliationBreakMeasureKindDto.Value, null, null, null, null, row.Record.Currency ?? "unit unavailable",
                    "Operator-requested investigation. A comparable statement and ledger pair has not been retained.")])
            {
                TenantId = snapshot.Workflow.TenantId,
                CompanyId = companyId,
                FundProfileId = accountingScope.FundProfileId
            };
            await _breakQueue!.CreateIfMissingAsync(queueScope, item, ct).ConfigureAwait(false);
            var retained = await _breakQueue.GetByIdAsync(queueScope, caseId, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("The reviewed row case was not durably retained.");
            if (retained.SourceReference != item.SourceReference || retained.LedgerBookId != accountingScope.LedgerBookId
                || retained.AccountingPeriodId != item.AccountingPeriodId || retained.FundProfileId != item.FundProfileId)
                throw new InvalidDataException("The retained row case does not match its reviewed source and accounting scope.");
            casesByRow.Add(row.RowId, caseId);
        }
        var updated = await imports.RetainReviewedCaseAdmissionsAsync(reviewId, snapshot.Workflow.TenantId, companyId, casesByRow, ct).ConfigureAwait(false);
        return snapshot with
        {
            ImportResult = updated with
            {
                EvidenceVaultIdentity = import.EvidenceVaultIdentity,
                EvidenceWorkbenchRoute = import.EvidenceWorkbenchRoute,
                OperationsWorkflowId = import.OperationsWorkflowId,
                AccountingScope = import.AccountingScope
            },
            Workflow = snapshot.Workflow with
            {
                EvidenceReferences = snapshot.Workflow.EvidenceReferences.Concat(casesByRow.Values.Select(id => $"reconciliation-case:{id}"))
                    .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray()
            }
        };
    }
}
