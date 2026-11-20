using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.AccountingClose;

public sealed partial class AccountingReportPackageService
{
    private async Task<IReadOnlyList<AccountingConfigurationValidationIssueDto>> BuildCurrentCloseCertificationIssuesAsync(
        AccountingReportPackageBundleDto package,
        CancellationToken ct)
    {
        if (!package.CloseWorkflowId.HasValue)
        {
            return
            [
                new AccountingConfigurationValidationIssueDto(
                    "CloseWorkflowRequired",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Accounting report package '{package.FinancialStatements.PackageId}' is not linked to a retained close workflow.",
                    package.FinancialStatements.PackageId,
                    "Build the report package from a close workflow with ledger-book scope, period lock, sign-offs, reconciliation, and report evidence before certification.")
            ];
        }

        if (_closeManagementService is null)
        {
            return
            [
                new AccountingConfigurationValidationIssueDto(
                    "ClosePlanServiceMissing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Close-backed report package '{package.FinancialStatements.PackageId}' cannot refresh close-plan state because close management is not registered.",
                    package.FinancialStatements.PackageId,
                    "Register close management before certifying close-backed report packages.")
            ];
        }

        var closePlan = await _closeManagementService.GetPeriodPlanAsync(package.CloseWorkflowId.Value, ct).ConfigureAwait(false);
        if (closePlan is null)
        {
            return
            [
                new AccountingConfigurationValidationIssueDto(
                    "ClosePlanMissing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Close workflow '{package.CloseWorkflowId.Value:D}' was not found.",
                    package.CloseWorkflowId.Value.ToString("D"),
                    "Create or select a close workflow before certifying the accounting report package.")
            ];
        }

        var issues = new List<AccountingConfigurationValidationIssueDto>();
        issues.AddRange(BuildCloseLedgerBookConsistencyIssues(
            closePlan,
            package.FinancialStatements.LedgerBookId,
            $"retained report package '{package.FinancialStatements.PackageId}'"));
        issues.AddRange(BuildCloseBackedReportEvidenceScopeIssues(
            closePlan,
            package.FinancialStatements.PeriodId,
            package.FinancialStatements.LedgerBookId,
            package.FinancialStatements.EvidenceLinks));
        issues.AddRange(closePlan.ValidationIssues);
        issues.AddRange(BuildCloseCertificationIssues(closePlan));
        if (!closePlan.IsPeriodLocked)
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "PeriodNotLocked",
                AccountingConfigurationValidationSeverityDto.Critical,
                "The close period is not locked; close-backed report package certification is blocked.",
                closePlan.ClosePlanId,
                "Lock the period after close approvals before final report certification."));
        }

        if (_externalGl is not null && package.FinancialStatements.LedgerBookId is { } bookId)
        {
            try
            {
                if (Guid.TryParse(closePlan.PeriodId, out var periodId))
                    await _externalGl.RequireConfirmationAsync(bookId, periodId, ct).ConfigureAwait(false);
                else
                    await _externalGl.RequireConfirmationForRangeAsync(bookId, closePlan.PeriodStart, closePlan.PeriodEnd, ct).ConfigureAwait(false);
            }
            catch (LedgerPeriodTransitionException exception)
            {
                issues.Add(new AccountingConfigurationValidationIssueDto("ExternalGlConfirmationRequired",
                    AccountingConfigurationValidationSeverityDto.Critical, exception.Message));
            }
        }

        return issues;
    }
}
