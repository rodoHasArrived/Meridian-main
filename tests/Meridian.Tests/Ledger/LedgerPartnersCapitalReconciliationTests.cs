using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Proves the partners-capital statement produced from ledger-backed data reconciles: its
/// per-account roll-forward closes, its aggregate ending capital ties to the fund's net assets (the
/// unitized NAV base), and its distribution movement ties to a matching distribution waterfall.
/// </summary>
public sealed class LedgerPartnersCapitalReconciliationTests
{
    [Fact]
    public void PartnersCapitalStatement_RollForwardReconciles()
    {
        var pack = LedgerReportPackTestData.BuildContributionPack();
        var statement = pack.Statements.PartnersCapital;

        statement.Should().NotBeNull();
        statement!.IsReconciled.Should().BeTrue();
        statement.ReconciliationVariance.Should().Be(0m);
        statement.Accounts.Should().OnlyContain(account => account.ReconciliationVariance == 0m);

        // The movements match what was posted: one contribution, one distribution.
        statement.Contributions.Should().Be(LedgerReportPackTestData.ContributionAmount);
        statement.Distributions.Should().Be(LedgerReportPackTestData.DistributionAmount);
    }

    [Fact]
    public void PartnersCapitalEndingCapital_TiesToNetAssetsAndUnitizedNav()
    {
        var pack = LedgerReportPackTestData.BuildContributionPack();
        var statements = pack.Statements;
        var statement = statements.PartnersCapital!;

        // Net assets (the NAV base) = total assets less total liabilities.
        var netAssets = statements.TotalAssets - statements.TotalLiabilities;

        // The statement is internally consistent with the balance sheet.
        statements.AccountingEquationVariance.Should().Be(0m);
        statement.EndingCapital.Should().Be(statements.EndingEquity);
        statement.EndingCapital.Should().Be(netAssets);

        // Ending partners' capital drives the unitized NAV per unit: 10,000 units were issued for the
        // 10,000,000 contribution at a 1,000 inception price, so NAV per unit = net assets / units.
        var navPerUnit = NavPerUnitCalculator.ComputeNavPerUnit(netAssets, unitsOutstanding: 10_000m, inceptionNavPerUnit: 1_000m);
        navPerUnit.Should().Be(930m);
    }

    [Fact]
    public void PartnersCapitalDistribution_TiesToDistributionWaterfall()
    {
        var pack = LedgerReportPackTestData.BuildContributionPack();
        var statement = pack.Statements.PartnersCapital!;

        // The posted distribution is a pure return of capital (below contributed capital, no profit),
        // so a whole-fund waterfall over the same amount pays the LP the full amount and the GP nothing.
        var waterfall = EuropeanDistributionWaterfall.Distribute(new EuropeanWaterfallInput(
            contributedCapital: LedgerReportPackTestData.ContributionAmount,
            preferredReturnAccrued: 0m,
            amountToDistribute: LedgerReportPackTestData.DistributionAmount,
            carryRate: 0.20m));

        waterfall.GpTotal.Should().Be(0m);
        waterfall.LpTotal.Should().Be(LedgerReportPackTestData.DistributionAmount);
        statement.Distributions.Should().Be(waterfall.LpTotal);
    }
}
