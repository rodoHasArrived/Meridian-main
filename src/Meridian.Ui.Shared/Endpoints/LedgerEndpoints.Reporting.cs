using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Identity.Auth;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    private static async Task<bool> IsAccountingPackageBuildScopeAccessibleAsync(
        HttpContext context,
        WorkstationTenantContext tenantContext,
        string fundProfileId)
        => HasAccountingPackageTenantScope(tenantContext)
           && await IsBodyFundScopeAccessibleAsync(context, tenantContext, fundProfileId).ConfigureAwait(false);

    private static PrivateCapitalActivityProjectionDto FilterPrivateCapitalActivity(
        PrivateCapitalActivityProjectionDto activity,
        string? fundEventId,
        string? capitalAccountId,
        string? investorId,
        string? paymentIntentId)
    {
        var normalizedFundEventId = NormalizeOptional(fundEventId);
        var normalizedCapitalAccountId = NormalizeOptional(capitalAccountId);
        var normalizedInvestorId = NormalizeOptional(investorId);
        var normalizedPaymentIntentId = NormalizeOptional(paymentIntentId);
        if (normalizedFundEventId is null &&
            normalizedCapitalAccountId is null &&
            normalizedInvestorId is null &&
            normalizedPaymentIntentId is null)
        {
            return activity;
        }

        var paymentIntentFundEventIds = normalizedPaymentIntentId is null
            ? null
            : activity.FundEvents
                .Where(item => MatchesPrivateCapitalFilter(item.PaymentIntentId, normalizedPaymentIntentId))
                .Select(static item => item.FundEventId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matchingFundEvents = activity.FundEvents
            .Where(item =>
                MatchesPrivateCapitalFilter(item.FundEventId, normalizedFundEventId) &&
                MatchesPrivateCapitalFilter(item.CapitalAccountId, normalizedCapitalAccountId) &&
                MatchesPrivateCapitalFilter(item.InvestorId, normalizedInvestorId) &&
                MatchesPrivateCapitalFilter(item.PaymentIntentId, normalizedPaymentIntentId))
            .ToArray();
        var matchingSubledgerEntries = activity.CapitalAccountSubledgerEntries
            .Where(item =>
                MatchesPrivateCapitalFilter(item.FundEventId, normalizedFundEventId) &&
                MatchesPrivateCapitalFilter(item.CapitalAccountId, normalizedCapitalAccountId) &&
                MatchesPrivateCapitalFilter(item.InvestorId, normalizedInvestorId) &&
                MatchesPrivateCapitalFundEventSet(item.FundEventId, paymentIntentFundEventIds))
            .ToArray();
        var matchingLedgerImpacts = activity.LedgerImpacts
            .Where(item =>
                MatchesPrivateCapitalFilter(item.FundEventId, normalizedFundEventId) &&
                MatchesPrivateCapitalFilter(item.CapitalAccountId, normalizedCapitalAccountId) &&
                MatchesPrivateCapitalFilter(item.InvestorId, normalizedInvestorId) &&
                MatchesPrivateCapitalFundEventSet(item.FundEventId, paymentIntentFundEventIds))
            .ToArray();
        var matchingReportOutputs = activity.ReportOutputs
            .Where(item =>
                MatchesPrivateCapitalFilter(item.FundEventId, normalizedFundEventId) &&
                MatchesPrivateCapitalFilter(item.CapitalAccountId, normalizedCapitalAccountId) &&
                MatchesPrivateCapitalFilter(item.InvestorId, normalizedInvestorId) &&
                MatchesPrivateCapitalFundEventSet(item.FundEventId, paymentIntentFundEventIds))
            .ToArray();
        var matchedFundEventIds = matchingFundEvents
            .Select(static item => item.FundEventId)
            .Concat(matchingSubledgerEntries.Select(static item => item.FundEventId))
            .Concat(matchingLedgerImpacts.Select(static item => item.FundEventId))
            .Concat(matchingReportOutputs.Select(static item => item.FundEventId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fundEvents = activity.FundEvents
            .Where(item => matchedFundEventIds.Contains(item.FundEventId))
            .ToArray();
        var retainedFundEventIds = fundEvents
            .Select(static item => item.FundEventId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retainedPaymentIntentIds = fundEvents
            .Select(static item => item.PaymentIntentId)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var capitalAccountSubledgerEntries = activity.CapitalAccountSubledgerEntries
            .Where(item =>
                MatchesPrivateCapitalFilter(item.FundEventId, normalizedFundEventId) &&
                MatchesPrivateCapitalFilter(item.CapitalAccountId, normalizedCapitalAccountId) &&
                MatchesPrivateCapitalFilter(item.InvestorId, normalizedInvestorId) &&
                MatchesPrivateCapitalFundEventSet(item.FundEventId, paymentIntentFundEventIds) &&
                retainedFundEventIds.Contains(item.FundEventId))
            .ToArray();
        var ledgerImpacts = activity.LedgerImpacts
            .Where(item =>
                MatchesPrivateCapitalFilter(item.FundEventId, normalizedFundEventId) &&
                MatchesPrivateCapitalFilter(item.CapitalAccountId, normalizedCapitalAccountId) &&
                MatchesPrivateCapitalFilter(item.InvestorId, normalizedInvestorId) &&
                MatchesPrivateCapitalFundEventSet(item.FundEventId, paymentIntentFundEventIds) &&
                retainedFundEventIds.Contains(item.FundEventId))
            .ToArray();
        var reportOutputs = activity.ReportOutputs
            .Where(item =>
                MatchesPrivateCapitalFilter(item.FundEventId, normalizedFundEventId) &&
                (MatchesPrivateCapitalFilter(item.CapitalAccountId, normalizedCapitalAccountId) &&
                 MatchesPrivateCapitalFilter(item.InvestorId, normalizedInvestorId) ||
                 ((normalizedCapitalAccountId is not null || normalizedInvestorId is not null) &&
                  capitalAccountSubledgerEntries.Any(entry => string.Equals(entry.FundEventId, item.FundEventId, StringComparison.OrdinalIgnoreCase)))) &&
                MatchesPrivateCapitalFundEventSet(item.FundEventId, paymentIntentFundEventIds) &&
                retainedFundEventIds.Contains(item.FundEventId))
            .ToArray();
        var paymentIntents = activity.PaymentIntents
            .Where(item =>
                MatchesPrivateCapitalFilter(item.PaymentIntentId, normalizedPaymentIntentId) &&
                (retainedPaymentIntentIds.Contains(item.PaymentIntentId) ||
                 (!string.IsNullOrWhiteSpace(item.FundEventId) && retainedFundEventIds.Contains(item.FundEventId)) ||
                 MatchesPrivateCapitalFilter(item.ExpectedCashMovement.CapitalAccountId, normalizedCapitalAccountId) &&
                 MatchesPrivateCapitalFilter(item.ExpectedCashMovement.InvestorId, normalizedInvestorId) &&
                 MatchesPrivateCapitalFilter(item.FundEventId, normalizedFundEventId)))
            .ToArray();
        var fundEventRecords = PrivateCapitalFundEventLedgerRecordBuilder.Build(
            activity.FundProfileId,
            fundEvents,
            capitalAccountSubledgerEntries,
            ledgerImpacts,
            reportOutputs);
        var capitalAccounts = BuildFilteredCapitalAccounts(capitalAccountSubledgerEntries, fundEvents);
        var retainedCapitalAccountIds = capitalAccounts
            .Select(static item => item.CapitalAccountId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retainedJournalEntryIds = fundEvents
            .Select(static item => item.JournalEntryId.ToString("D"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var validationIssues = activity.ValidationIssues
            .Where(issue => MatchesFilteredPrivateCapitalIssue(
                issue,
                retainedFundEventIds,
                retainedCapitalAccountIds,
                retainedJournalEntryIds))
            .ToArray();
        var currency = fundEvents
            .Select(static item => item.Currency)
            .Concat(capitalAccountSubledgerEntries.Select(static item => item.Currency))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? activity.Currency;
        var netCapitalActivity = capitalAccountSubledgerEntries.Length > 0
            ? capitalAccountSubledgerEntries.Sum(static item => item.NetCapitalActivity)
            : fundEvents.Sum(static item => item.NetCapitalActivity);
        var capitalAccountSubledgers = PrivateCapitalCapitalAccountSubledgerBuilder.Build(
            activity.FundProfileId,
            activity.LedgerBookId,
            activity.ProjectedAtUtc,
            capitalAccounts,
            fundEventRecords,
            capitalAccountSubledgerEntries,
            ledgerImpacts,
            reportOutputs,
            validationIssues);

        return new PrivateCapitalActivityProjectionDto(
            activity.FundProfileId,
            activity.LedgerBookId,
            activity.ProjectedAtUtc,
            fundEvents.Length,
            capitalAccounts.Count,
            fundEvents.Count(static item => item.JournalStatus is ManualJournalEntryStatusDto.Submitted or ManualJournalEntryStatusDto.Approved),
            fundEvents.Count(static item => item.JournalStatus == ManualJournalEntryStatusDto.Submitted),
            fundEvents.Count(static item => item.IsPosted),
            reportOutputs.Count(static item => item.IsPublished),
            netCapitalActivity,
            currency,
            fundEvents,
            capitalAccounts,
            capitalAccountSubledgerEntries,
            ledgerImpacts,
            reportOutputs,
            validationIssues,
            fundEventRecords,
            capitalAccountSubledgers,
            paymentIntents);
    }

    private static IReadOnlyList<PrivateCapitalCapitalAccountActivityDto> BuildFilteredCapitalAccounts(
        IReadOnlyList<PrivateCapitalCapitalAccountSubledgerEntryDto> subledgerEntries,
        IReadOnlyList<PrivateCapitalFundEventDto> fundEvents)
    {
        if (subledgerEntries.Count > 0)
        {
            return subledgerEntries
                .GroupBy(static item => new { item.CapitalAccountId, item.InvestorId, item.Currency })
                .Select(group =>
                {
                    var ordered = group
                        .OrderByDescending(static item => item.EffectiveDate)
                        .ThenByDescending(static item => item.UpdatedAtUtc)
                        .ToArray();
                    return new PrivateCapitalCapitalAccountActivityDto(
                        group.Key.CapitalAccountId,
                        group.Key.InvestorId,
                        group.Key.Currency,
                        group.Where(static item => item.EntryType == ManualJournalEntryTypeDto.CapitalCall).Sum(static item => Math.Abs(item.NetCapitalActivity)),
                        group.Where(static item => item.EntryType == ManualJournalEntryTypeDto.Distribution).Sum(static item => Math.Abs(item.NetCapitalActivity)),
                        group.Where(static item => item.EntryType == ManualJournalEntryTypeDto.Subscription).Sum(static item => Math.Abs(item.NetCapitalActivity)),
                        group.Where(static item => item.EntryType == ManualJournalEntryTypeDto.Redemption).Sum(static item => Math.Abs(item.NetCapitalActivity)),
                        group.Where(static item => item.EntryType == ManualJournalEntryTypeDto.ManagementFee).Sum(static item => Math.Abs(item.NetCapitalActivity)),
                        group.Sum(static item => item.NetCapitalActivity),
                        group.Select(static item => item.FundEventId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                        ordered.Length == 0 ? null : ordered[0].EffectiveDate,
                        ordered.Length == 0 ? null : ordered[0].FundEventType,
                        group
                            .Select(static item => item.FundEventId)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Order(StringComparer.OrdinalIgnoreCase)
                            .ToArray());
                })
                .OrderBy(static item => item.CapitalAccountId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.InvestorId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.Currency, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return fundEvents
            .GroupBy(item => new { item.CapitalAccountId, item.InvestorId, item.Currency })
            .Select(group =>
            {
                var ordered = group
                    .OrderByDescending(static item => item.EffectiveDate)
                    .ThenByDescending(static item => item.UpdatedAtUtc)
                    .ToArray();
                return new PrivateCapitalCapitalAccountActivityDto(
                    group.Key.CapitalAccountId,
                    group.Key.InvestorId,
                    group.Key.Currency,
                    group.Where(static item => item.EntryType == ManualJournalEntryTypeDto.CapitalCall).Sum(static item => Math.Abs(item.NetCapitalActivity)),
                    group.Where(static item => item.EntryType == ManualJournalEntryTypeDto.Distribution).Sum(static item => Math.Abs(item.NetCapitalActivity)),
                    group.Where(static item => item.EntryType == ManualJournalEntryTypeDto.Subscription).Sum(static item => Math.Abs(item.NetCapitalActivity)),
                    group.Where(static item => item.EntryType == ManualJournalEntryTypeDto.Redemption).Sum(static item => Math.Abs(item.NetCapitalActivity)),
                    group.Where(static item => item.EntryType == ManualJournalEntryTypeDto.ManagementFee).Sum(static item => Math.Abs(item.NetCapitalActivity)),
                    group.Sum(static item => item.NetCapitalActivity),
                    group.Count(),
                    ordered.Length == 0 ? null : ordered[0].EffectiveDate,
                    ordered.Length == 0 ? null : ordered[0].FundEventType,
                    group
                        .Select(static item => item.FundEventId)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray());
            })
            .OrderBy(static item => item.CapitalAccountId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.InvestorId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Currency, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool MatchesPrivateCapitalFilter(string? value, string? filter)
        => filter is null || string.Equals(value?.Trim(), filter, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesPrivateCapitalFundEventSet(
        string fundEventId,
        IReadOnlySet<string>? fundEventIds)
        => fundEventIds is null || fundEventIds.Contains(fundEventId);

    private static bool MatchesFilteredPrivateCapitalIssue(
        AccountingConfigurationValidationIssueDto issue,
        IReadOnlySet<string> retainedFundEventIds,
        IReadOnlySet<string> retainedCapitalAccountIds,
        IReadOnlySet<string> retainedJournalEntryIds)
    {
        if (retainedFundEventIds.Count == 0 &&
            retainedCapitalAccountIds.Count == 0 &&
            retainedJournalEntryIds.Count == 0)
        {
            return false;
        }

        var targetId = NormalizeOptional(issue.TargetId);
        return targetId is null ||
               retainedFundEventIds.Contains(targetId) ||
               retainedCapitalAccountIds.Contains(targetId) ||
               retainedJournalEntryIds.Contains(targetId);
    }

    private static async Task<LedgerPeriodSummaryLoadResult> LoadClosedPeriodSummariesAsync(
        ILedgerBookService service,
        Guid? ledgerBookId,
        string? fundProfileId,
        Guid? fundStructureNodeId,
        AccountingBasisKindDto? accountingBasis,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken)
    {
        var periods = await service
            .ListPeriodsAsync(
                new LedgerPeriodQuery(
                    ledgerBookId,
                    fundProfileId,
                    fundStructureNodeId,
                    Status: null,
                    OpenOnly: false,
                    accountingBasis),
                cancellationToken)
            .ConfigureAwait(false);

        var closedPeriods = periods
            .Where(period => period.Status != LedgerPeriodStatusDto.Open)
            .Where(period => !startDate.HasValue || period.EndDate >= startDate.Value)
            .Where(period => !endDate.HasValue || period.StartDate <= endDate.Value)
            .OrderBy(static period => period.StartDate)
            .ThenBy(static period => period.PeriodNo)
            .ToArray();

        var summaries = new List<(LedgerPeriodDto period, LedgerPeriodSummaryDto summary)>(closedPeriods.Length);
        foreach (var period in closedPeriods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ledgerBookId.HasValue && period.LedgerBookId != ledgerBookId.Value)
            {
                return LedgerPeriodSummaryLoadResult.Conflict(
                    $"Ledger period '{period.PeriodId:D}' belongs to ledger book '{period.LedgerBookId:D}', not requested ledger book '{ledgerBookId.Value:D}'.");
            }

            var summary = await service.GetPeriodSummaryAsync(period.PeriodId, cancellationToken).ConfigureAwait(false);
            if (summary is not null)
            {
                if (summary.LedgerBookId != period.LedgerBookId)
                {
                    return LedgerPeriodSummaryLoadResult.Conflict(
                        $"Closed-period summary for period '{period.PeriodId:D}' belongs to ledger book '{summary.LedgerBookId:D}', but the period belongs to ledger book '{period.LedgerBookId:D}'.");
                }

                summaries.Add((period, summary));
            }
        }

        return LedgerPeriodSummaryLoadResult.Success(summaries);
    }

    private sealed record LedgerPeriodSummaryLoadResult(
        IReadOnlyList<(LedgerPeriodDto period, LedgerPeriodSummaryDto summary)> Summaries,
        IResult? Error)
    {
        public static LedgerPeriodSummaryLoadResult Success(
            IReadOnlyList<(LedgerPeriodDto period, LedgerPeriodSummaryDto summary)> summaries)
            => new(summaries, Error: null);

        public static LedgerPeriodSummaryLoadResult Conflict(string message)
            => new([], Results.Conflict(new { error = message }));
    }

    private static LedgerCrossPeriodTrialBalanceReportDto BuildTrialBalanceReport(
        IReadOnlyList<(LedgerPeriodDto period, LedgerPeriodSummaryDto summary)> summaries,
        Guid? ledgerBookId,
        string? fundProfileId,
        Guid? fundStructureNodeId,
        AccountingBasisKindDto? accountingBasis,
        DateOnly? startDate,
        DateOnly? endDate,
        LedgerDimensionReportFilter dimensionFilter)
    {
        var filteredSummaries = summaries
            .Select(item => (item.period, summary: ApplyDimensionFilter(item.summary, dimensionFilter)))
            .ToArray();
        var lines = filteredSummaries
            .SelectMany(static item => item.summary.TrialBalance.Select(line => new LedgerCrossPeriodTrialBalanceLineDto(
                item.summary.PeriodId,
                item.summary.LedgerBookId,
                item.summary.FiscalYear,
                item.summary.PeriodNo,
                item.summary.Label,
                line.AccountName,
                line.AccountType,
                line.Symbol,
                line.FinancialAccountId,
                line.DebitTotal,
                line.CreditTotal,
                line.Balance,
                line.EntryCount,
                line.AccountingBasis,
                line.AccountingPolicyId,
                line.AccountingPolicyVersion,
                line.RuleId,
                line.RuleVersion,
                line.SourceEventId,
                line.SourceJournalEntryId,
                CanonicalizeDimensions(line.Dimensions))))
            .ToArray();

        return new LedgerCrossPeriodTrialBalanceReportDto(
            DateTimeOffset.UtcNow,
            ledgerBookId,
            NormalizeOptional(fundProfileId),
            fundStructureNodeId,
            accountingBasis,
            startDate,
            endDate,
            filteredSummaries.Select(static item => item.period).ToArray(),
            lines,
            filteredSummaries.Sum(static item => item.summary.TotalDebits),
            filteredSummaries.Sum(static item => item.summary.TotalCredits),
            filteredSummaries.Sum(static item => item.summary.NetIncome));
    }

    private static LedgerCrossPeriodPnlReportDto BuildPnlReport(
        IReadOnlyList<(LedgerPeriodDto period, LedgerPeriodSummaryDto summary)> summaries,
        Guid? ledgerBookId,
        string? fundProfileId,
        Guid? fundStructureNodeId,
        AccountingBasisKindDto? accountingBasis,
        DateOnly? startDate,
        DateOnly? endDate,
        LedgerDimensionReportFilter dimensionFilter)
    {
        var periods = summaries
            .Select(item => BuildPnlSummary(ApplyDimensionFilter(item.summary, dimensionFilter)))
            .ToArray();

        return new LedgerCrossPeriodPnlReportDto(
            DateTimeOffset.UtcNow,
            ledgerBookId,
            NormalizeOptional(fundProfileId),
            fundStructureNodeId,
            accountingBasis,
            startDate,
            endDate,
            periods,
            periods.Sum(static period => period.TotalRevenue),
            periods.Sum(static period => period.TotalExpenses),
            periods.Sum(static period => period.NetIncome),
            periods.Sum(static period => period.RealizedNetIncome),
            periods.Sum(static period => period.AccrualBasisAdjustmentNetImpact));
    }

    private sealed record LedgerDimensionReportFilter(
        string? FundId,
        string? EntityId,
        string? SleeveId,
        string? StrategyId,
        string? InvestorId,
        string? CapitalAccountId,
        string? InstrumentId,
        string? PositionId,
        string? TaxLotId,
        string? CostCenterId,
        string? CounterpartyId,
        string? OrganizationId,
        string? PortfolioId,
        string? BookId,
        string? AccountId,
        string? CustomerId,
        string? VendorId,
        string? ProjectId,
        IReadOnlyDictionary<string, string> ExternalGlDimensions)
    {
        public bool HasCriteria
            => FundId is not null
               || EntityId is not null
               || SleeveId is not null
               || StrategyId is not null
               || InvestorId is not null
               || CapitalAccountId is not null
               || InstrumentId is not null
               || PositionId is not null
               || TaxLotId is not null
               || CostCenterId is not null
               || CounterpartyId is not null
               || OrganizationId is not null
               || PortfolioId is not null
               || BookId is not null
               || AccountId is not null
               || CustomerId is not null
               || VendorId is not null
               || ProjectId is not null
               || ExternalGlDimensions.Count > 0;
    }

    private static bool ContainsAccrualMarker(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Contains("accru", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetLedgerCloseActor(HttpContext context, out string actor)
    {
        actor = string.Empty;
        if (context.Items[LoginSessionMiddleware.CurrentUserKey] is not string username ||
            string.IsNullOrWhiteSpace(username))
        {
            return false;
        }

        if (context.Items[LoginSessionMiddleware.CurrentUserRoleKey] is not UserRole role)
        {
            return false;
        }

        if (role is not UserRole.Admin and not UserRole.Accounting)
        {
            return false;
        }

        actor = username.Trim();
        return true;
    }
}
