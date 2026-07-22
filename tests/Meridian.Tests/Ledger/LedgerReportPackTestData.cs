using Meridian.Ledger;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Shared, fully deterministic report-pack fixtures for the client-deliverable tests. The scenario
/// posts a capital contribution, a management-fee accrual, and a distribution so the resulting
/// partners-capital statement carries real contribution/distribution/allocation movements that
/// reconcile to the fund's net assets (NAV) and to a matching distribution waterfall.
/// </summary>
internal static class LedgerReportPackTestData
{
    internal const string FundId = "fund-alpha";
    internal const string InvestorId = "investor-1";
    internal const string PeriodId = "2026-Q1";
    internal const string ReportId = "quarterly-report";
    internal const decimal ContributionAmount = 10_000_000m;
    internal const decimal ManagementFeeAmount = 200_000m;
    internal const decimal DistributionAmount = 500_000m;

    private static readonly DateOnly PeriodStartDate = new(2026, 1, 1);
    private static readonly DateOnly PeriodEndDate = new(2026, 3, 31);
    private static readonly DateTimeOffset AsOf = new(2026, 3, 31, 23, 59, 59, TimeSpan.Zero);
    private static readonly DateTimeOffset GeneratedAtUtc = new(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);

    internal static Meridian.Ledger.Ledger BuildLedger()
    {
        var ledger = new Meridian.Ledger.Ledger();

        // Capital contribution: debit cash, credit the LP's capital account.
        ledger.PostLines(
            new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero),
            "capital contribution",
            [
                (LedgerAccounts.Cash, ContributionAmount, 0m),
                (LedgerAccounts.InvestorCapitalFor(InvestorId), 0m, ContributionAmount),
            ]);

        // Management fee accrual: debit fee expense, credit fee payable (reduces net income).
        ledger.PostLines(
            new DateTimeOffset(2026, 3, 31, 12, 0, 0, TimeSpan.Zero),
            "management fee accrual",
            [
                (LedgerAccounts.ManagementFeeExpenseFor(FundId), ManagementFeeAmount, 0m),
                (LedgerAccounts.ManagementFeePayableFor(FundId), 0m, ManagementFeeAmount),
            ]);

        // Distribution: debit the LP's capital account, credit the distribution payable.
        ledger.PostLines(
            new DateTimeOffset(2026, 3, 31, 13, 0, 0, TimeSpan.Zero),
            "distribution",
            [
                (LedgerAccounts.InvestorCapitalFor(InvestorId), DistributionAmount, 0m),
                (LedgerAccounts.FundDistributionPayableFor(InvestorId), 0m, DistributionAmount),
            ]);

        return ledger;
    }

    /// <summary>A scheduled export whose report identity matches <see cref="BuildRequest"/> exactly.</summary>
    internal static LedgerReportScheduledExport BuildScheduledExport()
    {
        var schedule = new LedgerReportSchedule(
            ReportId,
            FundId,
            "Quarterly client pack",
            LedgerReportScheduleFrequency.Monthly,
            PeriodStartDate,
            5,
            "usd",
            [LedgerReportExportFormat.Pdf, LedgerReportExportFormat.Xlsx],
            ["controller@example.com"],
            "controller",
            new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero));

        return new LedgerReportScheduledExport(
            schedule,
            ReportId,
            PeriodId,
            PeriodStartDate,
            PeriodEndDate,
            AsOf,
            new DateTimeOffset(2026, 4, 5, 12, 0, 0, TimeSpan.Zero),
            [LedgerReportExportFormat.Pdf, LedgerReportExportFormat.Xlsx],
            ["controller@example.com"]);
    }

    // The request is derived from the scheduled export so the export-package builder's
    // schedule/report validation always passes and the two stay in lockstep.
    internal static LedgerReportPackRequest BuildRequest()
        => BuildScheduledExport().ToReportPackRequest("controller", GeneratedAtUtc);

    internal static LedgerFinancialReportPack BuildContributionPack()
        => LedgerReportPackBuilder.Build(BuildLedger(), BuildRequest());
}
