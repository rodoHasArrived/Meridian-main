using System;
using System.Linq;

using FluentAssertions;

using Meridian.Ledger;

using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Coverage for the bespoke partners' capital statement layout projector: it classifies capital
/// accounts by partner role, computes each partner's ownership share of ending capital, and anchors
/// the statement to the fund's ledger-backed net asset value (the unitized NAV base) with an
/// explicit reconciliation flag. The projection is presentation only — it never alters a figure.
/// </summary>
public sealed class PartnersCapitalStatementLayoutTests
{
    private static readonly DateTimeOffset PeriodStart = new(2025, 12, 31, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AsOf = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_FromContributionPack_ClassifiesLimitedPartnerAndAnchorsToNetAssets()
    {
        var pack = LedgerReportPackTestData.BuildContributionPack();
        var statement = pack.Statements.PartnersCapital!;

        var layout = PartnersCapitalStatementLayoutBuilder.Build(pack);

        // Fund identity flows from the request.
        layout.FundId.Should().Be(LedgerReportPackTestData.FundId);
        layout.PeriodId.Should().Be(LedgerReportPackTestData.PeriodId);
        layout.BaseCurrency.Should().Be("USD");

        // The NAV anchor is the fund's ledger-backed net assets (ending equity), and the statement ties.
        layout.NetAssetValue.Should().Be(pack.Statements.EndingEquity);
        layout.Total.EndingCapital.Should().Be(statement.EndingCapital);
        layout.TiesToNetAssets.Should().BeTrue();
        layout.NetAssetVariance.Should().Be(0m);

        // The investor-capital account is presented as a limited partner keyed by investor id.
        var lp = layout.Lines.Should()
            .ContainSingle(line => line.Role == PartnersCapitalPartnerRole.LimitedPartner).Which;
        lp.PartnerLabel.Should().Be(LedgerReportPackTestData.InvestorId);
        lp.Contributions.Should().Be(LedgerReportPackTestData.ContributionAmount);
        lp.Distributions.Should().Be(LedgerReportPackTestData.DistributionAmount);

        // The total line preserves the statement aggregate exactly (presentation never changes a figure).
        layout.Total.BeginningCapital.Should().Be(statement.BeginningCapital);
        layout.Total.Contributions.Should().Be(statement.Contributions);
        layout.Total.Distributions.Should().Be(statement.Distributions);
        layout.Total.IncomeGainAllocations.Should().Be(statement.IncomeGainAllocations);
        layout.Total.ExpenseAllocations.Should().Be(statement.ExpenseAllocations);
        layout.Total.FeeAllocations.Should().Be(statement.FeeAllocations);
        layout.Total.EndingCapital.Should().Be(statement.EndingCapital);
    }

    [Fact]
    public void Build_Ownership_ExcludesNonPartnerEquityAndBoundsPartners()
    {
        var layout = PartnersCapitalStatementLayoutBuilder.Build(
            LedgerReportPackTestData.BuildContributionPack());

        // The undistributed net income line is a fund-level result, not a partner: it holds no ownership.
        // (Before the fix its negative capital produced a negative "ownership" share.)
        var undistributed = layout.Lines.Should()
            .ContainSingle(line => line.Role == PartnersCapitalPartnerRole.UndistributedResult).Which;
        undistributed.OwnershipPercent.Should().Be(0m);

        // Named partners hold 100% between them and none is reported above 100%. (Before the fix the LP
        // read ~102% because the negative undistributed line sat in the denominator.)
        var lp = layout.Lines.Should()
            .ContainSingle(line => line.Role == PartnersCapitalPartnerRole.LimitedPartner).Which;
        lp.OwnershipPercent.Should().BeApproximately(100m, 0.0001m);
        layout.Lines.Should().OnlyContain(line => line.OwnershipPercent <= 100m);
        layout.Total.OwnershipPercent.Should().BeApproximately(100m, 0.0001m);
    }

    [Fact]
    public void Build_CarriedInterestAllocation_LabelledByCaptionNotTheLimitedPartnerId()
    {
        // The distribution-waterfall factory posts the GP carried-interest line under the *LP's* investor
        // id (BuildDistributionWaterfallDraft reuses its single investorId for both legs). Reproduce that
        // by scoping the carry account to the LP id, then prove the layout never surfaces it as the GP.
        const string lpId = "lp-1";
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(new DateTimeOffset(2025, 6, 15, 0, 0, 0, TimeSpan.Zero), "opening contribution",
            [(LedgerAccounts.Cash, 5_000_000m, 0m), (LedgerAccounts.InvestorCapitalFor(lpId), 0m, 5_000_000m)]);
        ledger.PostLines(new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero), "realized gain",
            [(LedgerAccounts.Cash, 1_000_000m, 0m), (LedgerAccounts.RealizedGainFor("fund-a"), 0m, 1_000_000m)]);
        // Close part of the gain into the carried-interest capital account, scoped to the LP id exactly
        // as the waterfall factory posts it.
        ledger.PostLines(new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero), "carry allocation",
            [(LedgerAccounts.RealizedGainFor("fund-a"), 200_000m, 0m), (LedgerAccounts.CarriedInterestAllocationFor(lpId), 0m, 200_000m)]);

        var statements = LedgerFinancialStatementBuilder.BuildForPeriod(ledger, PeriodStart, AsOf);
        var layout = PartnersCapitalStatementLayoutBuilder.Build(
            statements.PartnersCapital!, "fund-a", "2026", "USD", statements.EndingEquity);

        var gp = layout.Lines.Should()
            .ContainSingle(line => line.Role == PartnersCapitalPartnerRole.GeneralPartner).Which;
        gp.EndingCapital.Should().Be(200_000m);
        // The GP row is labelled by its role caption, never the limited partner's investor id.
        gp.PartnerLabel.Should().Be("Carried Interest Allocation");
        gp.PartnerLabel.Should().NotBe(lpId);
        layout.Lines.Should().Contain(line => line.Role == PartnersCapitalPartnerRole.LimitedPartner);
        layout.TiesToNetAssets.Should().BeTrue();
    }

    [Fact]
    public void Build_WithoutPartnersCapitalStatement_Throws()
    {
        var pack = LedgerReportPackTestData.BuildContributionPack();
        var withoutPartnersCapital = pack with { Statements = pack.Statements with { PartnersCapital = null } };

        var act = () => PartnersCapitalStatementLayoutBuilder.Build(withoutPartnersCapital);

        act.Should().Throw<InvalidOperationException>();
    }
}
