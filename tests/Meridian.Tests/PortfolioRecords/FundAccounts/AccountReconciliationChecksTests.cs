using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.PortfolioRecords.FundAccounts;
using Xunit;

namespace Meridian.Tests.PortfolioRecords.FundAccounts;

/// <summary>
/// The run summary's <c>BreakAmountTotal</c> is a monetary figure; it must not absorb the
/// position-count check, whose variance is a number of statement lines.
/// </summary>
public sealed class AccountReconciliationChecksTests
{
    private static AccountReconciliationResultDto Break(string category, decimal variance) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        CheckLabel: category + "Check",
        IsMatch: false,
        Category: category,
        Status: "Break",
        ExpectedAmount: 0m,
        ActualAmount: variance,
        Variance: variance,
        Reason: "test break");

    [Fact]
    public void BuildRunSummary_PositionCountBreak_IsExcludedFromMonetaryBreakTotal()
    {
        var results = new[]
        {
            Break("Cash", -250m),
            Break("Continuity", 40m),
            Break("Positions", 3m), // three missing statement lines, not three dollars
        };

        var run = AccountReconciliationChecks.BuildRunSummary(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 11, 6),
            "tester",
            new DateTimeOffset(2026, 11, 7, 0, 0, 0, TimeSpan.Zero),
            results);

        run.TotalBreaks.Should().Be(3, "the position-count break still counts as a break");
        run.BreakAmountTotal.Should().Be(290m, "only cash-denominated variances sum into the money total");
    }
}
