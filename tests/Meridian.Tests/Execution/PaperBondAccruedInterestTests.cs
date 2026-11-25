using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Execution;

/// <summary>
/// Dirty-price settlement for paper bond fills (W10-BOND-001): a face-value fill carries the
/// accrued interest measured to its T+1 settlement date, the buyer pays it on top of the clean
/// consideration into Accrued Interest Receivable, and the seller receives it back, relieving
/// the purchased receivable and booking the remainder as coupon income earned while held.
/// </summary>
public sealed partial class PaperBondAccruedInterestTests
{
    private const string Bond = "ACME5.25-2031";

    [Fact]
    public void Buy_WithAccruedInterest_PaysDirtyCashAndBooksReceivableOutsideCostBasis()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(200_000m, ledger);

        var result = portfolio.ApplyFillWithAccountingEvidence(
            BondFill(OrderSide.Buy, face: 100_000m, cleanPer100: 101.25m, accruedPer100: 1.50m),
            ownerAccountId: null);

        portfolio.Cash.Should().Be(200_000m - 101_250m - 1_500m,
            "settlement cash is clean consideration plus purchased accrued interest");
        portfolio.Positions[Bond].AverageCostBasis.Should().Be(1.0125m,
            "purchased accrued interest is a receivable, never capitalized into cost basis");
        ledger.GetBalance(LedgerAccounts.AccruedInterestReceivable(Bond)).Should().Be(1_500m);
        result.Evidence!.AccruedInterest.Should().Be(1_500m);
        result.Evidence.AccruedInterestRelieved.Should().Be(0m);
    }

    [Fact]
    public void Sell_AfterHolding_RelievesPurchasedReceivableAndBooksEarnedCouponIncome()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(200_000m, ledger);
        portfolio.ApplyFill(BondFill(OrderSide.Buy, 100_000m, 101.25m, accruedPer100: 1.50m));

        var result = portfolio.ApplyFillWithAccountingEvidence(
            BondFill(OrderSide.Sell, 100_000m, 101.25m, accruedPer100: 2.00m), ownerAccountId: null);

        portfolio.Cash.Should().Be(200_000m + 500m,
            "the seller receives $2,000 accrued against $1,500 paid: $500 earned while held");
        ledger.GetBalance(LedgerAccounts.AccruedInterestReceivable(Bond)).Should().Be(0m);
        Math.Abs(ledger.GetBalance(LedgerAccounts.CouponIncome)).Should().Be(500m);
        result.Evidence!.AccruedInterest.Should().Be(2_000m);
        result.Evidence.AccruedInterestRelieved.Should().Be(1_500m);
    }

    [Fact]
    public void PartialSell_RelievesReceivableProRataToFaceSold()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(200_000m, ledger);
        portfolio.ApplyFill(BondFill(OrderSide.Buy, 100_000m, 101.25m, accruedPer100: 1.50m));

        portfolio.ApplyFill(BondFill(OrderSide.Sell, 40_000m, 101.25m, accruedPer100: 1.50m));

        ledger.GetBalance(LedgerAccounts.AccruedInterestReceivable(Bond)).Should().Be(900m);
        var account = (AccountState)portfolio.GetAccount(PaperTradingPortfolio.DefaultAccountId)!;
        account.Positions[Bond].PurchasedAccruedInterest.Should().Be(900m);
    }

    [Fact]
    public void FillWithoutAccruedInterest_SettlesCleanAsBefore()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(200_000m, ledger);

        portfolio.ApplyFill(BondFill(OrderSide.Buy, 100_000m, 101.25m, accruedPer100: null));

        portfolio.Cash.Should().Be(98_750m);
        ledger.GetBalance(LedgerAccounts.AccruedInterestReceivable(Bond)).Should().Be(0m);
    }

    [Fact]
    public void DurableReplay_ReproducesDirtySettlementFromTheStampedReport()
    {
        var fill = BondFill(OrderSide.Buy, 100_000m, 101.25m, accruedPer100: 1.50m)
            with
        { SettlementDate = new DateOnly(2026, 11, 19) };
        var replayed = JsonSerializer.Deserialize<ExecutionReport>(JsonSerializer.Serialize(fill))!;

        replayed.AccruedInterestPer100.Should().Be(1.50m);
        replayed.SettlementDate.Should().Be(new DateOnly(2026, 11, 19));
        var portfolio = new PaperTradingPortfolio(200_000m);
        portfolio.ApplyFill(replayed);
        portfolio.Cash.Should().Be(97_250m);
    }

    [Fact]
    public void UnstampedReport_SerializesWithoutAccruedFields()
    {
        var json = JsonSerializer.Serialize(BondFill(OrderSide.Buy, 1_000m, 100m, accruedPer100: null));

        json.Should().NotContain(nameof(ExecutionReport.AccruedInterestPer100))
            .And.NotContain(nameof(ExecutionReport.SettlementDate));
    }

    [Theory]
    [InlineData("2026-11-18", "2026-11-19")] // Wednesday -> Thursday
    [InlineData("2026-11-20", "2026-11-23")] // Friday -> Monday
    [InlineData("2026-11-21", "2026-11-23")] // Saturday -> Monday
    public void SettlementDate_IsNextWeekday(string trade, string expected)
        => PaperBondSettlement.SettlementDate(DateOnly.Parse(trade)).Should().Be(DateOnly.Parse(expected));

    [Fact]
    public async Task Gateway_FaceValueFill_StampsAccruedInterestFromBondAnalytics()
    {
        var securityId = Guid.NewGuid();
        var analytics = Substitute.For<IBondAnalyticsService>();
        analytics.CalculateAsync(Arg.Any<BondAnalyticsRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Analytics(call.Arg<BondAnalyticsRequest>(), accruedPer100: 1.3125m));
        await using var gateway = BondGateway(securityId, analytics);

        var report = await gateway.SubmitOrderAsync(BondOrder());

        report.UsesFaceValuePercentageOfPar.Should().BeTrue();
        report.AccruedInterestPer100.Should().Be(1.3125m);
        report.SettlementDate.Should().Be(
            PaperBondSettlement.SettlementDate(DateOnly.FromDateTime(report.Timestamp.UtcDateTime)));
        await analytics.Received(1).CalculateAsync(
            Arg.Is<BondAnalyticsRequest>(r => r.SecurityId == securityId && r.CleanPrice == report.FillPrice
                && r.Context.SettlementDate == report.SettlementDate),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Gateway_AnalyticsRefusal_LeavesFillCleanRatherThanGuessing()
    {
        var analytics = Substitute.For<IBondAnalyticsService>();
        analytics.CalculateAsync(Arg.Any<BondAnalyticsRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Analytics(call.Arg<BondAnalyticsRequest>(), accruedPer100: null,
                blocker: new BondAnalyticsBlocker("DeclaredScheduleRequired", "No declared schedule.")));
        await using var gateway = BondGateway(Guid.NewGuid(), analytics);

        var report = await gateway.SubmitOrderAsync(BondOrder());

        report.UsesFaceValuePercentageOfPar.Should().BeTrue();
        report.AccruedInterestPer100.Should().BeNull();
        report.SettlementDate.Should().BeNull();
    }

    private static ExecutionReport BondFill(OrderSide side, decimal face, decimal cleanPer100, decimal? accruedPer100) => new()
    {
        OrderId = $"{Bond}-{side}-{face}",
        ReportType = ExecutionReportType.Fill,
        Symbol = Bond,
        Side = side,
        OrderStatus = OrderStatus.Filled,
        OrderQuantity = face,
        FilledQuantity = face,
        FillPrice = cleanPer100,
        UsesFaceValuePercentageOfPar = true,
        AccruedInterestPer100 = accruedPer100,
        Timestamp = DateTimeOffset.UtcNow
    };

    private static OrderRequest BondOrder() => new()
    {
        Symbol = Bond,
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 100_000m,
        LimitPrice = 101.25m
    };

    private static Meridian.Execution.PaperTradingGateway BondGateway(Guid securityId, IBondAnalyticsService? analytics,
        Meridian.Execution.Adapters.PaperTradingGatewayOptions? options = null,
        Meridian.Execution.Adapters.LiveMarketDataCache? feed = null,
        Meridian.Execution.PaperMatching.PaperTradingCostOptions? costOptions = null)
    {
        var identity = Substitute.For<ISecurityIdentityQueryService>();
        var detail = new SecurityDetailDto(securityId, "Bond", SecurityStatusDto.Active,
            "Acme 5.25% 2031", "USD", JsonSerializer.SerializeToElement(new { }),
            JsonSerializer.SerializeToElement(new { couponType = "Fixed" }), [], [], 1, DateTimeOffset.UtcNow.AddYears(-1), null);
        identity.GetByIdentifierAsync(SecurityIdentifierKind.Ticker, Bond, null,
            Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>()).Returns(detail);
        feed ??= new Meridian.Execution.Adapters.LiveMarketDataCache();
        feed.RecordTrade(Bond, new Meridian.Contracts.Domain.Models.Trade(DateTimeOffset.UtcNow, Bond, 101.25m, 100,
            Meridian.Contracts.Domain.Enums.AggressorSide.Buy, 1));
        return new Meridian.Execution.PaperTradingGateway(
            NullLogger<Meridian.Execution.PaperTradingGateway>.Instance,
            securityMaster: (ISecurityTradingParametersQueryService?)null, liveFeed: feed,
            securityIdentity: identity, bondAnalytics: analytics, options: options, costOptions: costOptions);
    }

    private static BondAnalyticsResult Analytics(BondAnalyticsRequest request, decimal? accruedPer100,
        BondAnalyticsBlocker? blocker = null)
        => new(request.SecurityId, "USD", "bond-analytics-v2", request.Context, BondYieldConvention.NominalPeriodic,
            request.CleanPrice, request.CleanPrice + accruedPer100, accruedPer100,
            null, null, null, null, null, null, null, [], [], [], blocker is null ? [] : [blocker], [], []);
}
