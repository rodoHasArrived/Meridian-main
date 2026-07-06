using FluentAssertions;
using Meridian.Ledger;
using Meridian.Reporting;
using NSubstitute;
using Xunit;
using SecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Reporting;

public sealed class NavAttributionServiceTests
{
    private static readonly DateTimeOffset PostedAt = new(2026, 6, 30, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AsOf = new(2026, 6, 30, 23, 59, 0, TimeSpan.Zero);

    private static NavAttributionService CreateService()
        => new(Substitute.For<SecurityMasterQueryService>());

    private static FundLedgerBook BuildFundWithMixedAccountTypes()
    {
        var fund = new FundLedgerBook("fund-nav");
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var securities = new LedgerAccount("Securities", LedgerAccountType.Asset, Symbol: "AAPL");
        var feePayable = new LedgerAccount("Management Fee Payable", LedgerAccountType.Liability);
        var feeExpense = new LedgerAccount("Management Fee Expense", LedgerAccountType.Expense);
        var investorCapital = new LedgerAccount("Investor Capital", LedgerAccountType.Equity);
        var dividendIncome = new LedgerAccount("Dividend Income", LedgerAccountType.Revenue);

        fund.FundLedger.PostLines(PostedAt, "capital-subscription", new[]
        {
            (cash, 1000m, 0m),
            (investorCapital, 0m, 1000m),
        });
        fund.FundLedger.PostLines(PostedAt, "security-purchase", new[]
        {
            (securities, 500m, 0m),
            (cash, 0m, 500m),
        });
        fund.FundLedger.PostLines(PostedAt, "dividend-received", new[]
        {
            (cash, 300m, 0m),
            (dividendIncome, 0m, 300m),
        });
        fund.FundLedger.PostLines(PostedAt, "fee-accrual", new[]
        {
            (feeExpense, 200m, 0m),
            (feePayable, 0m, 200m),
        });

        // Assets: cash 800 + securities 500 = 1300; liabilities: fee payable 200.
        return fund;
    }

    [Fact]
    public async Task AttributeAsync_TotalNav_IsAssetsMinusLiabilities()
    {
        var service = CreateService();
        var fund = BuildFundWithMixedAccountTypes();

        var result = await service.AttributeAsync(
            new NavAttributionRequest("fund-nav", AsOf, fund));

        // NAV must exclude equity, revenue, and expense balances: summing every account's
        // normal balance (1300 + 200 + 1000 + 300 + 200 = 3000) double-counts the fund.
        result.Consolidated.TotalNav.Should().Be(1100m);
    }

    [Fact]
    public async Task AttributeAsync_ByAssetClass_DecomposesTotalNav()
    {
        var service = CreateService();
        var fund = BuildFundWithMixedAccountTypes();

        var result = await service.AttributeAsync(
            new NavAttributionRequest("fund-nav", AsOf, fund));

        result.Consolidated.ByAssetClass.Values.Sum().Should().Be(result.Consolidated.TotalNav);
    }

    [Fact]
    public async Task AttributeAsync_Components_StillReportEveryAccount()
    {
        var service = CreateService();
        var fund = BuildFundWithMixedAccountTypes();

        var result = await service.AttributeAsync(
            new NavAttributionRequest("fund-nav", AsOf, fund));

        result.Consolidated.Components.Should().HaveCount(6);
        result.Consolidated.Components
            .Single(component => component.AccountType == nameof(LedgerAccountType.Equity))
            .Balance.Should().Be(1000m);
    }

    [Fact]
    public async Task AttributeAsync_LiabilityOnlyBook_ReportsNegativeNav()
    {
        var service = CreateService();
        var fund = new FundLedgerBook("fund-underwater");
        var expense = new LedgerAccount("Accrued Expense", LedgerAccountType.Expense);
        var payable = new LedgerAccount("Accrued Payable", LedgerAccountType.Liability);
        fund.FundLedger.PostLines(PostedAt, "accrual", new[]
        {
            (expense, 250m, 0m),
            (payable, 0m, 250m),
        });

        var result = await service.AttributeAsync(
            new NavAttributionRequest("fund-underwater", AsOf, fund));

        result.Consolidated.TotalNav.Should().Be(-250m);
    }
}
