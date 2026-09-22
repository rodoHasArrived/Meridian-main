using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.Contracts.Integrity;
using Meridian.Storage.Ledger;
using System.Text.Json;

namespace Meridian.Ui.Shared.Services;

public interface IOperationsReportPackAuthority
{
    Task<OperationsReportPackReadinessDto> ResolveAsync(
        OperationsContinuityWorkflowDto workflow, string? packageId,
        string tenantId, string companyId, CancellationToken ct = default);
}

/// <summary>Resolves pre-close report support from retained packages, never a caller readiness flag.</summary>
public sealed class OperationsReportPackAuthority(
    IAccountingReportPackageService? packages = null,
    ILedgerBookService? books = null,
    IFundProfileTenancyRegistry? ownership = null,
    ILedgerJournalStore? journals = null) : IOperationsReportPackAuthority
{
    private const string RevisionSource = "accounting-report-package-revision";

    public static bool MatchesRetainedRevision(OperationsReportPackReadinessDto retained,
        OperationsReportPackReadinessDto current)
    {
        var revisions = current.EvidenceLinks.Where(link => link.Source == RevisionSource).ToArray();
        return retained.IsReady && current.IsReady && retained.ReportPackId == current.ReportPackId
            && revisions.Length == 1
            && retained.EvidenceLinks.Count(link => link.Source == RevisionSource) == 1
            && retained.EvidenceLinks.Any(link => link.Source == RevisionSource
                && link.EvidenceId == revisions[0].EvidenceId);
    }

    public async Task<OperationsReportPackReadinessDto> ResolveAsync(
        OperationsContinuityWorkflowDto workflow, string? packageId,
        string tenantId, string companyId, CancellationToken ct = default)
    {
        static OperationsReportPackReadinessDto Block(string message) => new(false, null, message, []);
        ct.ThrowIfCancellationRequested();
        // Seeded and setup-only hosts can render the workstation before a durable ledger is
        // configured. Composition stays available, but none of its missing sources can attest
        // report readiness or allow the publication guard to close a period.
        if (packages is null || books is null || ownership is null || journals is null)
            return Block("The retained report-package or canonical ledger authority is unavailable.");
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId)
            || string.IsNullOrWhiteSpace(packageId) || workflow.LedgerBookId is not { } bookId)
            return Block("Select a retained report package for the exact authenticated account, book, and period.");
        var book = await books.GetBookAsync(bookId, ct).ConfigureAwait(false);
        if (book is null || book.FundStructureNodeId != workflow.FundAccountId)
            return Block("The selected ledger book does not belong to the workflow account.");
        var owner = await ownership.ResolveAsync(book.FundProfileId, ct).ConfigureAwait(false);
        if (owner is null || !owner.IsHeldBy(tenantId)
            || !string.Equals(owner.CompanyId, companyId, StringComparison.Ordinal))
            return Block("The retained report package belongs to another tenant or company.");
        var retained = await packages.ListPackagesAsync(book.FundProfileId, workflow.PeriodId,
            bookId, tenantId: tenantId, companyId: companyId, ct: ct).ConfigureAwait(false);
        var matches = retained.Where(package => package.FinancialStatements.PackageId == packageId).ToArray();
        if (matches.Length != 1)
            return Block("The selected report package is missing or ambiguous in the retained accounting authority.");
        var selected = matches[0];
        var statement = selected.FinancialStatements;
        if (selected.TenantId != tenantId || selected.CompanyId != companyId
            || statement.FundProfileId != book.FundProfileId || statement.LedgerBookId != bookId
            || statement.PeriodId != workflow.PeriodId
            || (selected.CloseWorkflowId.HasValue && selected.CloseWorkflowId != workflow.WorkflowId)
            || (statement.Dimensions.AccountId is { Length: > 0 } accountId
                && !string.Equals(accountId, workflow.FundAccountId.ToString("D"), StringComparison.OrdinalIgnoreCase)))
            return Block("The retained report package does not match the selected workflow scope.");
        // Final certification is an output of the hard close. Requiring it here would create
        // a circular prerequisite; pre-close support must already be retained and reviewable.
        if (selected.Certification.State is not (AccountingCertificationStateDto.ReadyForReview
                or AccountingCertificationStateDto.Certified)
            || statement.CertificationState != selected.Certification.State
            || selected.ValidationIssues.Any(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical)
            || selected.Certification.EvidenceLinks.Count == 0 || statement.EvidenceLinks.Count == 0)
            return Block("The retained report package has missing evidence or unresolved validation issues.");
        var prerequisiteChange = workflow.Timeline
            .Where(entry => entry.EventType is "broker-imported" or "broker-transactions-normalized"
                or "security-master-resolved" or "ledger-posted" or "reconciliation-run"
                or "reconciliation-break-resolved" or "reconciliation-break-waived"
                or "reconciliation-break-superseded" or "workflow-reopened")
            .Select(entry => entry.OccurredAtUtc).DefaultIfEmpty(DateTimeOffset.MinValue).Max();
        if (selected.Certification.RecordedAtUtc < prerequisiteChange
            || selected.Certification.RecordedAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
            return Block("Rebuild the retained report support after the latest reconciliation or ledger change.");

        // A manual or automated ledger posting need not create an Operations audit event.
        // Resolve the canonical period and include the actual scoped journal, so those changes
        // also invalidate report review. Period status is deliberately excluded: the hard-close
        // transition follows this check and must not invalidate its own pre-close evidence.
        var periods = await books.ListPeriodsAsync(new LedgerPeriodQuery(LedgerBookId: bookId), ct).ConfigureAwait(false);
        var matchesByPeriod = periods.Where(period => period.LedgerBookId == bookId
            && (period.PeriodId.ToString("D") == workflow.PeriodId || period.Label == workflow.PeriodId))
            .DistinctBy(period => period.PeriodId).ToArray();
        if (matchesByPeriod.Length != 1)
            return Block("The report support cannot resolve one canonical ledger period.");
        var periodId = matchesByPeriod[0].PeriodId;
        var records = (await journals.QueryAsync(new LedgerJournalEntryQuery(
            LedgerBookId: bookId, PeriodId: periodId), ct).ConfigureAwait(false))
            .OrderBy(record => record.GlobalSequence).ThenBy(record => record.Entry.JournalEntryId).ToArray();
        if (records.Any(record => record.PeriodId != periodId || record.AccountingBasis != book.AccountingBasis))
            return Block("The canonical journal returned evidence outside the selected period or accounting basis.");
        if (records.Any(record => record.CreatedAt > selected.Certification.RecordedAtUtc))
            return Block("Rebuild retained report support after the latest canonical ledger posting.");
        var revisionHash = Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Package = selected,
            LedgerBookId = bookId,
            PeriodId = periodId,
            Journal = records
        }));
        return new(true, statement.PackageId, null,
            [new OperationsEvidenceLinkDto(statement.PackageId,
                $"Retained report support: {selected.Certification.CertificationId}", null,
                "accounting-report-pack", selected.Certification.RecordedAtUtc),
             new OperationsEvidenceLinkDto($"accounting-report-package-revision:{revisionHash}",
                "Retained accounting report package content revision", null,
                RevisionSource, selected.Certification.RecordedAtUtc)]);
    }
}
