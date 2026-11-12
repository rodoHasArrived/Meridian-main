using FluentAssertions;
using Meridian.Execution.Margin;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ledger;

namespace Meridian.Tests.Execution;

public sealed class PaperTradingPortfolioAccountingEvidenceTests
{
    private static readonly DateTimeOffset FilledAt = new(2026, 11, 12, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NamedBrokerageAccounts_KeepCashPositionsFeesAndShortLiabilitiesSeparate()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(
        [
            new AccountDefinition("broker-a", "Synthetic A", AccountKind.Brokerage, 10_000m),
            new AccountDefinition("broker-b", "Synthetic B", AccountKind.Brokerage, 10_000m)
        ], ledger);
        portfolio.ApplyFill("broker-a", Fill(OrderSide.Buy, 10m, 100m) with
        {
            Commission = 1m, Fees = 2m, SlippageCost = 3m
        });
        portfolio.ApplyFill("broker-b", Fill(OrderSide.Sell, 10m, 100m) with { Commission = 1m });
        portfolio.ApplyFill("broker-b", Fill(OrderSide.Buy, 4m, 90m) with { Commission = 1m });

        var chart = StandardChartOfAccounts.Instance;
        ledger.GetBalance(chart.Resolve(LedgerAccountRole.Cash, "broker-a")).Should().Be(8_994m);
        ledger.GetBalance(chart.Resolve(LedgerAccountRole.Cash, "broker-b")).Should().Be(10_638m);
        ledger.GetBalance(chart.Resolve(LedgerAccountRole.Securities, "broker-a", "AAPL")).Should().Be(1_000m);
        ledger.GetBalance(chart.Resolve(LedgerAccountRole.ShortSecuritiesPayable, "broker-b", "AAPL")).Should().Be(600m);
        ledger.GetBalance(chart.Resolve(LedgerAccountRole.RealizedGain, "broker-b")).Should().Be(40m);
        ledger.GetBalance(chart.Resolve(LedgerAccountRole.CommissionExpense, "broker-a")).Should().Be(6m);
        ledger.GetBalance(chart.Resolve(LedgerAccountRole.CommissionExpense, "broker-b")).Should().Be(2m);
        ledger.GetBalance(LedgerAccounts.Cash).Should().Be(0m);
        ledger.Journal.Should().OnlyContain(entry => entry.IsBalanced);
        ledger.Accounts.Should().OnlyContain(account => account.FinancialAccountId == "broker-a"
            || account.FinancialAccountId == "broker-b");
        portfolio.Cash.Should().Be(19_632m);
    }

    [Theory]
    [InlineData(true, SecurityTradePostingKind.CoverShort, SecurityTradePostingKind.OpenLong, 6, 84)]
    [InlineData(false, SecurityTradePostingKind.CloseLong, SecurityTradePostingKind.OpenShort, -6, 104)]
    public void CapturedFill_CrossingZeroRetainsClosingBasisOpeningCashAndEveryCost(
        bool shortFirst,
        SecurityTradePostingKind closingKind,
        SecurityTradePostingKind openingKind,
        int realizedPnl,
        int cash)
    {
        var portfolio = new PaperTradingPortfolio(100m);
        portfolio.ApplyFillWithAccountingEvidence(Fill(shortFirst ? OrderSide.Sell : OrderSide.Buy, 3m, 10m), "fund-a");

        var result = portfolio.ApplyFillWithAccountingEvidence(
            Fill(shortFirst ? OrderSide.Buy : OrderSide.Sell, 5m, 8m) with
            {
                Commission = 1m, Fees = 2m, SlippageCost = 3m
            }, "fund-a");

        result.Evidence.Should().NotBeNull();
        var evidence = result.Evidence!;
        evidence.Closing.Should().Be(new SecurityTradePosting(closingKind, 3m, 24m, 30m));
        evidence.Opening.Should().Be(new SecurityTradePosting(openingKind, 2m, 16m));
        evidence.Fees.Should().Be(2m);
        evidence.SlippageCost.Should().Be(3m);
        evidence.FinancialAccountId.Should().Be("fund-a");
        result.RealizedPnl.Should().Be(realizedPnl);
        result.NewCash.Should().Be(cash);
        result.NewCash.Should().Be(portfolio.Cash);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedClosingBasis_ExhaustsTheExactRetainedAmount(bool shortPosition)
    {
        const decimal originalBasis = 1.0000000000000000000000000001m;
        var portfolio = new PaperTradingPortfolio(10m);
        portfolio.ApplyFillWithAccountingEvidence(
            Fill(shortPosition ? OrderSide.Sell : OrderSide.Buy, 1m, originalBasis), "fund-a");

        var first = portfolio.ApplyFillWithAccountingEvidence(
            Fill(shortPosition ? OrderSide.Buy : OrderSide.Sell, 0.3m, 1m), "fund-a");
        var last = portfolio.ApplyFillWithAccountingEvidence(
            Fill(shortPosition ? OrderSide.Buy : OrderSide.Sell, 0.7m, 1m), "fund-a");

        (first.Evidence!.Closing!.CarryingValue + last.Evidence!.Closing!.CarryingValue)
            .Should().Be(originalBasis);
        (first.RealizedPnl + last.RealizedPnl).Should().Be(shortPosition ? originalBasis - 1m : 1m - originalBasis);
        portfolio.Positions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("fund-b")]
    [InlineData(null)]
    public void CapturedFill_OwnerWithoutLotAuthorityCannotCertifyClosingBasis(string? closingOwner)
    {
        var portfolio = new PaperTradingPortfolio(100m);
        portfolio.ApplyFillWithAccountingEvidence(Fill(OrderSide.Buy, 3m, 10m), "fund-a");

        var result = portfolio.ApplyFillWithAccountingEvidence(Fill(OrderSide.Sell, 1m, 12m), closingOwner);

        result.Evidence.Should().BeNull("the shared position does not retain per-owner lot authority");
        result.RealizedPnl.Should().Be(2m);
        result.NewCash.Should().Be(82m);
        portfolio.Positions["AAPL"].ExactQuantity.Should().Be(2m,
            "an accepted execution still updates the actual paper book when accounting authority is unavailable");
    }

    [Fact]
    public void CapturedFill_MixedOwnersCannotCertifyAClosingLegFromThePooledLotBook()
    {
        var portfolio = new PaperTradingPortfolio(100m);
        portfolio.ApplyFillWithAccountingEvidence(Fill(OrderSide.Buy, 3m, 10m), "fund-a");
        portfolio.ApplyFillWithAccountingEvidence(Fill(OrderSide.Buy, 1m, 11m), "fund-b");

        var result = portfolio.ApplyFillWithAccountingEvidence(Fill(OrderSide.Sell, 1m, 12m), "fund-a");

        result.Evidence.Should().BeNull();
        result.RealizedPnl.Should().Be(2m);
        result.NewCash.Should().Be(71m);
    }

    [Fact]
    public void WithheldAcquisition_RemainsUnverifiedAfterOtherOwnersLeaveThePool()
    {
        var portfolio = new PaperTradingPortfolio(1_000m);
        var first = portfolio.ApplyFillWithAccountingEvidence(Fill(OrderSide.Buy, 10m, 10m), "fund-a");
        var withheldOpening = portfolio.ApplyFillWithAccountingEvidence(Fill(OrderSide.Buy, 10m, 20m), "fund-b");
        var firstClose = portfolio.ApplyFillWithAccountingEvidence(Fill(OrderSide.Sell, 10m, 30m), "fund-a");

        // B is now the only attributed owner, but its acquisition was never certified.
        var remainingOwnerClose = portfolio.ApplyFillWithAccountingEvidence(Fill(OrderSide.Sell, 10m, 40m), "fund-b");
        var fresh = portfolio.ApplyFillWithAccountingEvidence(Fill(OrderSide.Buy, 1m, 10m), "fund-b");
        var freshClose = portfolio.ApplyFillWithAccountingEvidence(Fill(OrderSide.Sell, 1m, 11m), "fund-b");

        first.Evidence.Should().NotBeNull();
        withheldOpening.Evidence.Should().BeNull();
        firstClose.Evidence.Should().BeNull();
        remainingOwnerClose.Evidence.Should().BeNull("a sole owner cannot certify an acquisition whose posting was withheld");
        fresh.Evidence.Should().NotBeNull("the intervening fill fully flattened the unverified position");
        freshClose.Evidence.Should().NotBeNull();
        portfolio.Cash.Should().Be(1_401m);
    }

    [Fact]
    public void CapturedFill_NewOwnerCannotCertifyPreviouslyAnonymousBasis()
    {
        var portfolio = new PaperTradingPortfolio(100m);
        portfolio.ApplyFill(Fill(OrderSide.Buy, 3m, 10m));

        var result = portfolio.ApplyFillWithAccountingEvidence(Fill(OrderSide.Sell, 1m, 12m), "fund-a");

        result.Evidence.Should().BeNull();
        result.RealizedPnl.Should().Be(2m);
    }

    [Fact]
    public void CapturedOptionFlip_RetainsMultipliedMoneyRatherThanPerContractQuotes()
    {
        var portfolio = new PaperTradingPortfolio(2_000m);
        var open = Fill(OrderSide.Buy, 2m, 2.5m) with { AssetClass = "Option", LotMultiplier = 100m };
        portfolio.ApplyFillWithAccountingEvidence(open, "fund-a");

        var result = portfolio.ApplyFillWithAccountingEvidence(
            open with { Side = OrderSide.Sell, OrderQuantity = 3m, FilledQuantity = 3m, FillPrice = 3.5m }, "fund-a");

        result.Evidence!.Closing.Should().Be(new SecurityTradePosting(SecurityTradePostingKind.CloseLong, 2m, 700m, 500m));
        result.Evidence.Opening.Should().Be(new SecurityTradePosting(SecurityTradePostingKind.OpenShort, 1m, 350m));
        result.RealizedPnl.Should().Be(200m);
        result.NewCash.Should().Be(2_550m);
    }

    [Fact]
    public void CapturedFutureClose_RetainsOnlyChangeSinceTheLastSettlement()
    {
        var portfolio = new PaperTradingPortfolio(10_000m);
        var open = Fill(OrderSide.Buy, 1m, 6_000m) with
        {
            AssetClass = "Future", LotMultiplier = 50m,
            BookedMoneyMultiplier = 50m, BookedVariationMargin = true
        };
        portfolio.ApplyFillWithAccountingEvidence(open, "fund-a");
        portfolio.ApplyVariationMarginSettlement(PaperTradingPortfolio.DefaultAccountId, "AAPL", 6_010m, FilledAt);

        var result = portfolio.ApplyFillWithAccountingEvidence(open with { Side = OrderSide.Sell, FillPrice = 6_004m }, "fund-a");

        result.Evidence!.SettlesVariationMargin.Should().BeTrue();
        result.Evidence.Closing.Should().BeNull();
        result.Evidence.Opening.Should().BeNull();
        result.Evidence.VariationMarginPnl.Should().Be(-300m);
        result.RealizedPnl.Should().Be(-300m);
        result.NewCash.Should().Be(10_200m);
    }

    [Theory]
    [InlineData(OrderSide.Buy, OrderSide.Sell, null)]
    [InlineData(OrderSide.Sell, OrderSide.Buy, null)]
    [InlineData(OrderSide.Buy, OrderSide.Sell, "fund-a")]
    [InlineData(OrderSide.Sell, OrderSide.Buy, "fund-a")]
    public void ReplayVersionZeroPosition_WithholdsNewClosingEvidenceUntilTheWholePositionIsFlat(
        OrderSide openingSide, OrderSide closingSide, string? ownerAccountId)
    {
        var portfolio = new PaperTradingPortfolio(1_000m);
        portfolio.ApplyRecordedFill(Fill(openingSide, 10m, 10m), accountingEvidenceVersion: 0, ownerAccountId);

        var partial = portfolio.ApplyFillWithAccountingEvidence(Fill(closingSide, 4m, 9m), ownerAccountId);
        var final = portfolio.ApplyFillWithAccountingEvidence(Fill(closingSide, 6m, 9m), ownerAccountId);
        var fresh = portfolio.ApplyFillWithAccountingEvidence(Fill(openingSide, 2m, 10m), ownerAccountId);
        var freshClose = portfolio.ApplyFillWithAccountingEvidence(Fill(closingSide, 2m, 9m), ownerAccountId);

        partial.Evidence.Should().BeNull("unverified basis cannot authorize a typed closing journal");
        final.Evidence.Should().BeNull();
        fresh.Evidence.Should().NotBeNull("a genuinely flat book can start a newly captured position");
        freshClose.Evidence.Should().NotBeNull();
        freshClose.Evidence!.Closing!.CarryingValue.Should().Be(20m);
    }

    [Theory]
    [InlineData(OrderSide.Buy, OrderSide.Sell)]
    [InlineData(OrderSide.Sell, OrderSide.Buy)]
    public void ReplayCapturedVersionOnePosition_PreservesOwnerAuthorityForNewClosingEvidence(
        OrderSide openingSide, OrderSide closingSide)
    {
        var portfolio = new PaperTradingPortfolio(1_000m);
        portfolio.ApplyRecordedFill(Fill(openingSide, 3m, 10m), accountingEvidenceVersion: 1, ownerAccountId: "fund-a");

        var close = portfolio.ApplyFillWithAccountingEvidence(Fill(closingSide, 1m, 9m), "fund-a");

        close.Evidence.Should().NotBeNull();
        close.Evidence!.FinancialAccountId.Should().Be("fund-a");
        close.Evidence.Closing!.CarryingValue.Should().Be(10m);
    }

    [Fact]
    public void ReplayLegacyShort_FlipRetainsTheRestrictionOnTheUnpostedResidualLong()
    {
        var portfolio = new PaperTradingPortfolio(100m);
        // The one-argument API deliberately preserves the historical version-zero treatment.
        portfolio.ApplyRecordedFill(Fill(OrderSide.Sell, 3m, 10m));

        var flip = portfolio.ApplyFillWithAccountingEvidence(Fill(OrderSide.Buy, 5m, 8m), ownerAccountId: null);
        var partial = portfolio.ApplyFillWithAccountingEvidence(Fill(OrderSide.Sell, 1m, 9m), ownerAccountId: null);
        var flat = portfolio.ApplyFillWithAccountingEvidence(Fill(OrderSide.Sell, 1m, 9m), ownerAccountId: null);
        var fresh = portfolio.ApplyFillWithAccountingEvidence(Fill(OrderSide.Buy, 1m, 10m), ownerAccountId: null);
        var close = portfolio.ApplyFillWithAccountingEvidence(Fill(OrderSide.Sell, 1m, 11m), ownerAccountId: null);

        flip.Evidence.Should().BeNull();
        partial.Evidence.Should().BeNull("the residual long opening was withheld with the legacy short cover");
        flat.Evidence.Should().BeNull();
        fresh.Evidence.Should().NotBeNull();
        close.Evidence.Should().NotBeNull();
        portfolio.Cash.Should().Be(109m);
    }

    [Fact]
    public void ReplayUnsupportedAccountingVersion_RejectsBeforeApplyingTheFill()
    {
        var portfolio = new PaperTradingPortfolio(100m);

        var replay = () => portfolio.ApplyRecordedFill(Fill(OrderSide.Sell, 1m, 10m), accountingEvidenceVersion: 2);

        replay.Should().Throw<ArgumentOutOfRangeException>();
        portfolio.Cash.Should().Be(100m);
        portfolio.Positions.Should().BeEmpty();
    }

    [Fact]
    public void ReplayUnverifiedPartialClose_MarksTheSurvivingPreviouslyVerifiedBasis()
    {
        var portfolio = new PaperTradingPortfolio(100m);
        portfolio.ApplyRecordedFill(Fill(OrderSide.Buy, 3m, 10m), accountingEvidenceVersion: 1, ownerAccountId: "fund-a");
        portfolio.ApplyRecordedFill(Fill(OrderSide.Sell, 1m, 11m), accountingEvidenceVersion: 0, ownerAccountId: "fund-a");

        var close = portfolio.ApplyFillWithAccountingEvidence(Fill(OrderSide.Sell, 1m, 12m), "fund-a");

        close.Evidence.Should().BeNull("the surviving basis was changed by a fill without a certified posting receipt");
        close.RealizedPnl.Should().Be(2m);
        portfolio.Positions["AAPL"].ExactQuantity.Should().Be(1m);
    }

    [Fact]
    public void RegTSevereLoss_RepaysDebtWithAdditionalCashAndKeepsJournalBalanced()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(
        [
            new AccountDefinition("margin-a", "Synthetic Margin", AccountKind.Brokerage, 100_000m,
                MarginType: MarginAccountType.RegT)
        ], ledger);
        portfolio.ApplyFill("margin-a", Fill(OrderSide.Buy, 100m, 200m));

        portfolio.ApplyFill("margin-a", Fill(OrderSide.Sell, 100m, 50m));

        var chart = StandardChartOfAccounts.Instance;
        portfolio.Cash.Should().Be(85_000m);
        portfolio.MarginBalance.Should().Be(0m);
        ledger.GetBalance(chart.Resolve(LedgerAccountRole.Cash, "margin-a")).Should().Be(85_000m);
        ledger.GetBalance(chart.Resolve(LedgerAccountRole.RealizedLoss, "margin-a")).Should().Be(15_000m);
        ledger.GetBalance(chart.Resolve(LedgerAccountRole.MarginLoanPayable, "margin-a")).Should().Be(0m);
        ledger.GetBalance(chart.Resolve(LedgerAccountRole.Securities, "margin-a", "AAPL")).Should().Be(0m);
        ledger.Journal.Should().OnlyContain(entry => entry.IsBalanced);
        ledger.Journal.SelectMany(entry => entry.Lines).Should().OnlyContain(line => line.Debit >= 0m && line.Credit >= 0m);
    }

    [Theory]
    [InlineData("price")]
    [InlineData("commission")]
    [InlineData("fees")]
    [InlineData("slippage")]
    public void MalformedFill_IsRejectedBeforeAnyPortfolioMutation(string invalidMember)
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(100m, ledger);
        var report = Fill(OrderSide.Buy, 1m, 10m) with { Commission = 2m };
        report = invalidMember switch
        {
            "price" => report with { FillPrice = 0m },
            "commission" => report with { Commission = -1m, Fees = 2m },
            "fees" => report with { Fees = -1m },
            "slippage" => report with { SlippageCost = -1m },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidMember))
        };

        var apply = () => portfolio.ApplyFill(report);

        apply.Should().Throw<ArgumentOutOfRangeException>();
        portfolio.Cash.Should().Be(100m);
        portfolio.Positions.Should().BeEmpty();
        ledger.Journal.Should().ContainSingle();
    }

    private static ExecutionReport Fill(OrderSide side, decimal quantity, decimal price) => new()
    {
        OrderId = Guid.NewGuid().ToString("N"),
        Symbol = "AAPL",
        Side = side,
        ReportType = ExecutionReportType.Fill,
        OrderStatus = Meridian.Execution.Sdk.OrderStatus.Filled,
        OrderQuantity = quantity,
        FilledQuantity = quantity,
        FillPrice = price,
        Timestamp = FilledAt
    };
}
