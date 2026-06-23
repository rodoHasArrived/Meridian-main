using System.Globalization;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;

namespace Meridian.Ui.Shared.Services;

public sealed class DirectLendingOperationsReadService
{
    private readonly IDirectLendingService? _directLendingService;
    private readonly IDirectLendingServicerStatementService? _servicerStatementService;

    public DirectLendingOperationsReadService(
        IDirectLendingService? directLendingService = null,
        IDirectLendingServicerStatementService? servicerStatementService = null)
    {
        _directLendingService = directLendingService;
        _servicerStatementService = servicerStatementService;
    }

    public async Task<DirectLendingOperationsReadModelDto> GetOperationsAsync(CancellationToken ct = default)
    {
        if (_directLendingService is null)
        {
            return new DirectLendingOperationsReadModelDto(
                DateTimeOffset.UtcNow,
                "Unavailable",
                "Direct lending service is not registered.",
                [], [], [], [], [], [], [], [
                    new DirectLendingOperationsCloseBlockerDto(
                        Guid.Empty,
                        "Direct lending",
                        "ServiceRegistration",
                        "Critical",
                        "Direct lending service is not registered.",
                        "/api/loans/portfolio")
                ],
                []);
        }

        var portfolio = await _directLendingService.GetPortfolioSummaryAsync(ct).ConfigureAwait(false);
        var collateralRows = new List<DirectLendingOperationsCollateralCoverageDto>();
        var healthRows = new List<DirectLendingOperationsLoanHealthDto>();
        var postureRows = new List<DirectLendingOperationsStatusPostureDto>();
        var calendarRows = new List<DirectLendingOperationsCalendarItemDto>();
        var evidenceRows = new List<DirectLendingOperationsEvidenceItemDto>();
        var journalRows = new List<DirectLendingOperationsJournalPostureDto>();
        var exceptionRows = new List<DirectLendingOperationsReconciliationExceptionDto>();
        var closeBlockers = new List<DirectLendingOperationsCloseBlockerDto>();
        var servicerStatementRows = await BuildServicerStatementRowsAsync(ct).ConfigureAwait(false);
        var resultLoanMap = new Dictionary<Guid, LoanSummaryDto>();

        foreach (var loan in portfolio.Loans)
        {
            ct.ThrowIfCancellationRequested();
            var servicing = await _directLendingService.GetServicingStateAsync(loan.LoanId, ct).ConfigureAwait(false);
            var contract = await _directLendingService.GetLoanAsync(loan.LoanId, ct).ConfigureAwait(false);
            var journals = await _directLendingService.GetJournalsAsync(loan.LoanId, ct).ConfigureAwait(false);
            var runs = await _directLendingService.GetReconciliationRunsAsync(loan.LoanId, ct).ConfigureAwait(false);
            var projections = await _directLendingService.GetProjectionsAsync(loan.LoanId, ct).ConfigureAwait(false);

            healthRows.Add(BuildLoanHealth(loan, contract?.CurrentTerms.SecurityMasterReference?.SecurityId));
            collateralRows.Add(BuildCollateralCoverage(loan, servicing));
            postureRows.Add(BuildStatusPosture(loan, contract, servicing));
            calendarRows.AddRange(BuildServicingCalendar(loan, contract, servicing));
            evidenceRows.AddRange(BuildEvidenceRows(loan, contract, servicing, journals, runs, projections));
            journalRows.Add(BuildJournalPosture(loan, journals));

            foreach (var run in runs)
            {
                var results = await _directLendingService.GetReconciliationResultsAsync(run.ReconciliationRunId, ct).ConfigureAwait(false);
                foreach (var result in results)
                {
                    resultLoanMap[result.ReconciliationResultId] = loan;
                }
            }

            closeBlockers.AddRange(BuildCloseBlockers(loan, contract, servicing, journals, runs));
        }

        var exceptions = await _directLendingService.GetReconciliationExceptionsAsync(ct).ConfigureAwait(false);
        foreach (var exception in exceptions.Where(static item => !IsResolved(item.Status)))
        {
            var loan = resultLoanMap.TryGetValue(exception.ReconciliationResultId, out var mapped)
                ? mapped
                : portfolio.Loans.FirstOrDefault();
            exceptionRows.Add(BuildExceptionRow(exception, loan));
        }

        closeBlockers.AddRange(exceptionRows.Select(static row => new DirectLendingOperationsCloseBlockerDto(
            row.LoanId,
            row.FacilityName,
            "ReconciliationException",
            row.Status,
            row.Detail,
            row.Route)));

        closeBlockers.AddRange(servicerStatementRows
            .Where(static row => row.UnmappedRowCount > 0 || row.ExceptionCount > 0)
            .Select(static row => new DirectLendingOperationsCloseBlockerDto(
                Guid.Empty,
                "Servicer statements",
                "ServicerStatement",
                row.ExceptionCount > 0 ? "Critical" : "Warning",
                row.Detail,
                row.EvidenceRoute)));

        var status = closeBlockers.Any(static blocker => IsCritical(blocker.Severity))
            ? "Blocked"
            : closeBlockers.Count > 0 || exceptionRows.Count > 0
                ? "ReviewRequired"
                : "Ready";

        var detail = status == "Ready"
            ? "Direct lending operations have no active close blockers."
            : $"{closeBlockers.Count.ToString(CultureInfo.InvariantCulture)} close blocker(s) require review across {portfolio.TotalLoans.ToString(CultureInfo.InvariantCulture)} loan(s).";

        return new DirectLendingOperationsReadModelDto(
            DateTimeOffset.UtcNow,
            status,
            detail,
            healthRows,
            collateralRows.OrderBy(static row => row.CoverageRatio).ThenBy(static row => row.FacilityName, StringComparer.OrdinalIgnoreCase).ToArray(),
            postureRows,
            calendarRows.OrderBy(static row => row.DueDate).ThenBy(static row => row.FacilityName, StringComparer.OrdinalIgnoreCase).ToArray(),
            evidenceRows,
            journalRows,
            exceptionRows,
            closeBlockers,
            servicerStatementRows);
    }

    private async Task<IReadOnlyList<DirectLendingOperationsServicerStatementDto>> BuildServicerStatementRowsAsync(CancellationToken ct)
    {
        if (_servicerStatementService is null)
        {
            return [];
        }

        var batches = await _servicerStatementService.ListBatchesAsync(ct).ConfigureAwait(false);
        return batches.Select(static batch =>
        {
            var exceptionCount = batch.Issues.Count(static issue =>
                issue.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase) ||
                issue.Severity.Equals("Critical", StringComparison.OrdinalIgnoreCase));
            var detail = exceptionCount > 0
                ? $"{exceptionCount.ToString(CultureInfo.InvariantCulture)} hard issue(s), {batch.UnmappedRowCount.ToString(CultureInfo.InvariantCulture)} unmapped row(s), {batch.ReadyToApplyRowCount.ToString(CultureInfo.InvariantCulture)} ready to apply."
                : $"{batch.RowCount.ToString(CultureInfo.InvariantCulture)} row(s), {batch.ReadyToApplyRowCount.ToString(CultureInfo.InvariantCulture)} ready to apply, {batch.AppliedRowCount.ToString(CultureInfo.InvariantCulture)} applied.";
            return new DirectLendingOperationsServicerStatementDto(
                batch.BatchId ?? Guid.Empty,
                batch.Kind,
                batch.ServicerName,
                batch.StatementDate,
                batch.Status,
                batch.RowCount,
                batch.UnmappedRowCount,
                exceptionCount,
                batch.ReadyToApplyRowCount,
                batch.AppliedRowCount,
                batch.EvidenceRoute ?? "/api/loans/servicer-statements",
                detail);
        }).ToArray();
    }

    private static DirectLendingOperationsLoanHealthDto BuildLoanHealth(LoanSummaryDto loan, Guid? securityId)
    {
        var isAtRisk = loan.Status is LoanStatus.Defaulted or LoanStatus.NonPerforming or LoanStatus.Workout;
        var hasStaleAccrual = loan.LastAccrualDate is null && loan.PrincipalOutstanding > 0m;
        var status = isAtRisk ? "Blocked" : hasStaleAccrual ? "ReviewRequired" : "Ready";
        var detail = isAtRisk
            ? $"Loan is {loan.Status}; review workout, covenant, and collateral posture."
            : hasStaleAccrual
                ? "No retained accrual date is available for an outstanding loan."
                : "Loan servicing posture has retained accrual and payment context.";
        return new DirectLendingOperationsLoanHealthDto(
            loan.LoanId,
            loan.FacilityName,
            loan.BorrowerName,
            loan.Status,
            loan.BaseCurrency,
            loan.CommitmentAmount,
            loan.PrincipalOutstanding,
            loan.InterestAccruedUnpaid,
            loan.PenaltyAccruedUnpaid,
            loan.MaturityDate,
            loan.LastAccrualDate,
            loan.LastPaymentDate,
            status,
            detail,
            LoanRoute(loan.LoanId),
            securityId);
    }

    private static DirectLendingOperationsCollateralCoverageDto BuildCollateralCoverage(
        LoanSummaryDto loan,
        LoanServicingStateDto? servicing)
    {
        var collateralValue = servicing?.Collateral?.Sum(static item => item.EstimatedValue) ?? 0m;
        var principal = loan.PrincipalOutstanding;
        var ratio = principal == 0m ? (collateralValue > 0m ? 999m : 0m) : collateralValue / principal;
        var status = principal == 0m
            ? "Ready"
            : collateralValue <= 0m
                ? "Blocked"
                : ratio < 1m
                    ? "ReviewRequired"
                    : "Ready";
        var detail = collateralValue <= 0m
            ? "No retained collateral value covers the outstanding principal."
            : $"Collateral coverage is {ratio:P1} against outstanding principal.";
        return new DirectLendingOperationsCollateralCoverageDto(
            loan.LoanId,
            loan.FacilityName,
            principal,
            collateralValue,
            ratio,
            status,
            detail,
            $"{LoanRoute(loan.LoanId)}/collateral");
    }

    private static DirectLendingOperationsStatusPostureDto BuildStatusPosture(
        LoanSummaryDto loan,
        LoanContractDetailDto? contract,
        LoanServicingStateDto? servicing)
    {
        var covenants = contract?.CurrentTerms.CovenantsJson;
        var covenantStatus = string.IsNullOrWhiteSpace(covenants) ? "No retained covenants" : "Covenants retained";
        var restructurePosture = loan.Status is LoanStatus.Workout or LoanStatus.Defaulted or LoanStatus.NonPerforming
            ? "Workout review required"
            : "No restructure blocker";
        return new DirectLendingOperationsStatusPostureDto(
            loan.LoanId,
            loan.FacilityName,
            loan.Status,
            covenantStatus,
            servicing?.IsPikToggled ?? false,
            contract?.CurrentTerms.AmortizationType ?? AmortizationType.Bullet,
            restructurePosture,
            $"{loan.Status} loan with {covenantStatus.ToLowerInvariant()} and {restructurePosture.ToLowerInvariant()}.",
            LoanRoute(loan.LoanId),
            contract?.CurrentTerms.SecurityMasterReference?.SecurityId);
    }

    private static IEnumerable<DirectLendingOperationsCalendarItemDto> BuildServicingCalendar(
        LoanSummaryDto loan,
        LoanContractDetailDto? contract,
        LoanServicingStateDto? servicing)
    {
        yield return new DirectLendingOperationsCalendarItemDto(
            loan.LoanId,
            loan.FacilityName,
            "Maturity",
            loan.MaturityDate,
            loan.MaturityDate < DateOnly.FromDateTime(DateTime.UtcNow) ? "Blocked" : "Scheduled",
            "Contractual maturity date.",
            LoanRoute(loan.LoanId));

        if (servicing?.LastAccrualDate is DateOnly accrualDate)
        {
            yield return new DirectLendingOperationsCalendarItemDto(
                loan.LoanId,
                loan.FacilityName,
                "Last accrual",
                accrualDate,
                "Complete",
                "Most recent retained accrual date.",
                $"{LoanRoute(loan.LoanId)}/projections/accruals");
        }
        else if (loan.PrincipalOutstanding > 0m)
        {
            yield return new DirectLendingOperationsCalendarItemDto(
                loan.LoanId,
                loan.FacilityName,
                "Accrual",
                contract?.EffectiveDate ?? loan.OriginationDate,
                "ReviewRequired",
                "Outstanding loan has no retained accrual date.",
                $"{LoanRoute(loan.LoanId)}/projections/accruals");
        }
    }

    private static IEnumerable<DirectLendingOperationsEvidenceItemDto> BuildEvidenceRows(
        LoanSummaryDto loan,
        LoanContractDetailDto? contract,
        LoanServicingStateDto? servicing,
        IReadOnlyList<JournalEntryDto> journals,
        IReadOnlyList<ReconciliationRunDto> runs,
        IReadOnlyList<ProjectionRunDto> projections)
    {
        yield return new DirectLendingOperationsEvidenceItemDto(
            loan.LoanId,
            loan.FacilityName,
            "Contract",
            contract is null ? "Contract unavailable" : $"Terms v{contract.CurrentTermsVersion.ToString(CultureInfo.InvariantCulture)}",
            contract is null ? "Blocked" : "Ready",
            LoanRoute(loan.LoanId));
        yield return new DirectLendingOperationsEvidenceItemDto(
            loan.LoanId,
            loan.FacilityName,
            "Collateral",
            $"{(servicing?.Collateral?.Count ?? 0).ToString(CultureInfo.InvariantCulture)} collateral item(s)",
            servicing?.Collateral is { Count: > 0 } ? "Ready" : "ReviewRequired",
            $"{LoanRoute(loan.LoanId)}/collateral");
        yield return new DirectLendingOperationsEvidenceItemDto(
            loan.LoanId,
            loan.FacilityName,
            "Journals",
            $"{journals.Count.ToString(CultureInfo.InvariantCulture)} journal(s)",
            journals.Count > 0 ? "Ready" : "ReviewRequired",
            $"{LoanRoute(loan.LoanId)}/journals");
        yield return new DirectLendingOperationsEvidenceItemDto(
            loan.LoanId,
            loan.FacilityName,
            "Reconciliation",
            $"{runs.Count.ToString(CultureInfo.InvariantCulture)} run(s)",
            runs.Count > 0 ? "Ready" : "ReviewRequired",
            $"{LoanRoute(loan.LoanId)}/reconciliation-runs");
        yield return new DirectLendingOperationsEvidenceItemDto(
            loan.LoanId,
            loan.FacilityName,
            "Projection",
            $"{projections.Count.ToString(CultureInfo.InvariantCulture)} projection run(s)",
            projections.Count > 0 ? "Ready" : "ReviewRequired",
            $"{LoanRoute(loan.LoanId)}/projections");
    }

    private static DirectLendingOperationsJournalPostureDto BuildJournalPosture(
        LoanSummaryDto loan,
        IReadOnlyList<JournalEntryDto> journals)
    {
        var draftCount = journals.Count(static journal => journal.Status == JournalEntryStatus.Draft);
        var postedCount = journals.Count(static journal => journal.Status == JournalEntryStatus.Posted);
        var status = draftCount > 0 ? "ReviewRequired" : postedCount > 0 ? "Ready" : "ReviewRequired";
        var detail = journals.Count == 0
            ? "No retained direct-lending journals exist for this loan."
            : $"{draftCount.ToString(CultureInfo.InvariantCulture)} draft and {postedCount.ToString(CultureInfo.InvariantCulture)} posted journal(s).";
        return new DirectLendingOperationsJournalPostureDto(
            loan.LoanId,
            loan.FacilityName,
            draftCount,
            postedCount,
            status,
            detail,
            $"{LoanRoute(loan.LoanId)}/journals");
    }

    private static IEnumerable<DirectLendingOperationsCloseBlockerDto> BuildCloseBlockers(
        LoanSummaryDto loan,
        LoanContractDetailDto? contract,
        LoanServicingStateDto? servicing,
        IReadOnlyList<JournalEntryDto> journals,
        IReadOnlyList<ReconciliationRunDto> runs)
    {
        if (contract is null)
        {
            yield return new(loan.LoanId, loan.FacilityName, "Contract", "Critical", "Loan contract detail is unavailable.", LoanRoute(loan.LoanId));
        }

        if (loan.PrincipalOutstanding > 0m && (servicing?.Collateral?.Count ?? 0) == 0)
        {
            yield return new(loan.LoanId, loan.FacilityName, "Collateral", "Warning", "Outstanding principal has no retained collateral evidence.", $"{LoanRoute(loan.LoanId)}/collateral");
        }

        if (journals.Count == 0)
        {
            yield return new(loan.LoanId, loan.FacilityName, "JournalEvidence", "Warning", "No retained direct-lending journal evidence is available.", $"{LoanRoute(loan.LoanId)}/journals");
        }

        if (runs.Count == 0)
        {
            yield return new(loan.LoanId, loan.FacilityName, "Reconciliation", "Warning", "No retained direct-lending reconciliation run is available.", $"{LoanRoute(loan.LoanId)}/reconciliation-runs");
        }
    }

    private static DirectLendingOperationsReconciliationExceptionDto BuildExceptionRow(
        ReconciliationExceptionDto exception,
        LoanSummaryDto? loan)
    {
        var loanId = loan?.LoanId ?? Guid.Empty;
        var facility = loan?.FacilityName ?? "Unmapped loan";
        return new DirectLendingOperationsReconciliationExceptionDto(
            exception.ExceptionId,
            loanId,
            facility,
            exception.ExceptionType,
            0m,
            exception.Status,
            exception.AssignedTo ?? "Unassigned",
            $"{exception.Severity} reconciliation exception created {exception.CreatedAt:u}.",
            $"/api/reconciliation/exceptions/{exception.ExceptionId:D}");
    }

    private static bool IsResolved(string status)
        => status.Contains("resolved", StringComparison.OrdinalIgnoreCase) ||
           status.Contains("dismissed", StringComparison.OrdinalIgnoreCase);

    private static bool IsCritical(string severity)
        => severity.Contains("critical", StringComparison.OrdinalIgnoreCase) ||
           severity.Contains("block", StringComparison.OrdinalIgnoreCase);

    private static int RankLoanStatus(LoanStatus status) => status switch
    {
        LoanStatus.Defaulted => 5,
        LoanStatus.NonPerforming => 4,
        LoanStatus.Workout => 3,
        LoanStatus.Suspended => 2,
        LoanStatus.Active => 1,
        _ => 0
    };

    private static string LoanRoute(Guid loanId) => $"/api/loans/{loanId:D}";
}
