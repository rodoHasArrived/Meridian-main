using FluentAssertions;
using Meridian.Contracts.DirectLending;
using Meridian.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class DirectLendingSubledgerTieOutTests
{
    private static readonly Guid LoanId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void BuildSubledgerTieOutBlockers_ReturnsNothing_WhenControlAccountsTie()
    {
        var lines = Journal(
            ("LoanPrincipal", LedgerAccountType.Asset, 100_000m, 0m),
            ("Cash", LedgerAccountType.Asset, 0m, 100_000m),
            ("AccruedInterestReceivable", LedgerAccountType.Asset, 25m, 0m),
            ("InterestIncome", LedgerAccountType.Revenue, 0m, 25m));

        var blockers = DirectLendingOperationsReadService.BuildSubledgerTieOutBlockers(
            LoanId, "Facility A", Servicing(principal: 100_000m, interest: 25m), lines);

        blockers.Should().BeEmpty();
    }

    [Fact]
    public void BuildSubledgerTieOutBlockers_ReportsEachControlAccountThatDoesNotTie()
    {
        // The servicing state shows a 10,000 repayment and 5.00 more accrued interest than the
        // general ledger holds.
        var lines = Journal(
            ("LoanPrincipal", LedgerAccountType.Asset, 100_000m, 0m),
            ("Cash", LedgerAccountType.Asset, 0m, 100_000m),
            ("AccruedInterestReceivable", LedgerAccountType.Asset, 25m, 0m),
            ("InterestIncome", LedgerAccountType.Revenue, 0m, 25m));

        var blockers = DirectLendingOperationsReadService.BuildSubledgerTieOutBlockers(
            LoanId, "Facility A", Servicing(principal: 90_000m, interest: 30m), lines);

        blockers.Should().HaveCount(2);
        blockers.Should().OnlyContain(blocker =>
            blocker.LoanId == LoanId && blocker.BlockerType == "SubledgerTieOut" && blocker.Severity == "Warning");
        blockers[0].Detail.Should().Be(
            "Servicing principal outstanding 90000.00 does not tie to general-ledger LoanPrincipal 100000.00 (difference -10000.00).");
        blockers[1].Detail.Should().Contain("AccruedInterestReceivable 25.00 (difference 5.00)");
    }

    [Fact]
    public void BuildSubledgerTieOutBlockers_IgnoresRoundingAndLoansWithoutGeneralLedgerLines()
    {
        var lines = Journal(
            ("LoanPrincipal", LedgerAccountType.Asset, 100_000m, 0m),
            ("Cash", LedgerAccountType.Asset, 0m, 100_000m));

        DirectLendingOperationsReadService.BuildSubledgerTieOutBlockers(
            LoanId, "Facility A", Servicing(principal: 100_000.01m, interest: 0m), lines).Should().BeEmpty();
        DirectLendingOperationsReadService.BuildSubledgerTieOutBlockers(
            LoanId, "Facility A", Servicing(principal: 100_000m, interest: 25m), []).Should().BeEmpty();
    }

    private static LoanServicingStateDto Servicing(decimal principal, decimal interest) =>
        new(
            LoanId,
            LoanStatus.Active,
            1_000_000m,
            principal,
            1_000_000m - principal,
            new OutstandingBalancesDto(principal, interest, 0m, 0m, 0m),
            [],
            null,
            new DateOnly(2026, 3, 24),
            null,
            1,
            [],
            [],
            []);

    private static IReadOnlyList<LedgerEntry> Journal(
        params (string Account, LedgerAccountType Type, decimal Debit, decimal Credit)[] lines)
    {
        var journalEntryId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 3, 24, 0, 0, 0, TimeSpan.Zero);
        return lines
            .Select(line => new LedgerEntry(
                Guid.NewGuid(),
                journalEntryId,
                timestamp,
                new LedgerAccount(line.Account, line.Type, FinancialAccountId: LoanId.ToString("D")),
                line.Debit,
                line.Credit,
                "test"))
            .ToArray();
    }
}
