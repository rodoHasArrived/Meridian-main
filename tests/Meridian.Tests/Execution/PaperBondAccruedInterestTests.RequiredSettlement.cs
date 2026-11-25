using Meridian.Ledger;
using FluentAssertions;
using Meridian.Contracts.FixedIncome;
using Meridian.Execution.Adapters;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed partial class PaperBondAccruedInterestTests
{
    private static PaperTradingGatewayOptions RequiredSettlement() => new() { RequireBondAccruedInterest = true };

    [Theory]
    [InlineData(0)]
    [InlineData(1.50)]
    public async Task RequiredSettlement_VerifiedAccrual_PreservesDirtyCashAndCleanBasis(decimal accrued)
    {
        var analytics = Substitute.For<IBondAnalyticsService>();
        analytics.CalculateAsync(Arg.Any<BondAnalyticsRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Analytics(call.Arg<BondAnalyticsRequest>(), accrued));
        await using var gateway = BondGateway(Guid.NewGuid(), analytics, RequiredSettlement(),
            costOptions: new Meridian.Execution.PaperMatching.PaperTradingCostOptions
            { CommissionRate = 0m, CommissionMinimum = 0m });

        var report = await gateway.SubmitOrderAsync(BondOrder());
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new Meridian.Execution.Services.PaperTradingPortfolio(200_000m, ledger);
        portfolio.ApplyFillWithAccountingEvidence(report, ownerAccountId: null);

        report.ReportType.Should().Be(ExecutionReportType.Fill);
        report.AccruedInterestPer100.Should().Be(accrued);
        portfolio.Cash.Should().Be(98_750m - accrued * 1_000m);
        (portfolio.Positions[Bond].AverageCostBasis * portfolio.Positions[Bond].Quantity).Should().Be(101_250m);
        ledger.GetBalance(Meridian.Ledger.LedgerAccounts.AccruedInterestReceivable(LedgerSecurityKey.Unresolved(Bond))).Should().Be(accrued * 1_000m);
        ledger.GetBalance(Meridian.Ledger.LedgerAccounts.CouponIncome).Should().Be(0m);
        portfolio.CheckLedgerIntegrity().Should().BeEmpty();
    }

    [Theory]
    [InlineData("blocked")]
    [InlineData("missing")]
    [InlineData("negative")]
    [InlineData("security")]
    [InlineData("clean")]
    [InlineData("dirty")]
    [InlineData("settlement")]
    [InlineData("as-of")]
    public async Task RequiredSettlement_IncompleteOrInconsistentAnalytics_RejectsWithoutEconomicFill(string fault)
    {
        var analytics = Substitute.For<IBondAnalyticsService>();
        analytics.CalculateAsync(Arg.Any<BondAnalyticsRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<BondAnalyticsRequest>();
            var result = Analytics(request, 1.50m);
            return fault switch
            {
                "blocked" => Analytics(request, null, new("DeclaredScheduleRequired", "Review the coupon schedule.")),
                "missing" => result with { AccruedInterest = null },
                "negative" => result with { AccruedInterest = -1m },
                "security" => result with { SecurityId = Guid.NewGuid() },
                "clean" => result with { CleanPrice = 100m },
                "dirty" => result with { DirtyPrice = 500m },
                "settlement" => result with { Context = request.Context with { SettlementDate = request.Context.SettlementDate!.Value.AddDays(1) } },
                "as-of" => result with { Context = request.Context with { AsOf = request.Context.AsOf.AddDays(-1) } },
                _ => throw new InvalidOperationException(fault)
            };
        });
        await using var gateway = BondGateway(Guid.NewGuid(), analytics, RequiredSettlement());

        var report = await gateway.SubmitOrderAsync(BondOrder());

        AssertNoEconomicFill(report);
    }

    [Fact]
    public async Task RequiredSettlement_MissingAnalyticsService_Rejects()
    {
        await using var gateway = BondGateway(Guid.NewGuid(), null, RequiredSettlement());

        AssertNoEconomicFill(await gateway.SubmitOrderAsync(BondOrder()));
    }

    [Fact]
    public async Task RequiredSettlement_MetadataDeclaresFaceValueButIdentityUnavailable_Rejects()
    {
        await using var gateway = new Meridian.Execution.PaperTradingGateway(
            NullLogger<Meridian.Execution.PaperTradingGateway>.Instance,
            options: new PaperTradingGatewayOptions { RequireBondAccruedInterest = true, AllowScaffoldMarketFills = true });
        var order = BondOrder() with
        {
            Type = OrderType.Market,
            Metadata = new Dictionary<string, string> { ["asset_class"] = "corporate_bond" }
        };

        AssertNoEconomicFill(await gateway.SubmitOrderAsync(order));
    }

    [Fact]
    public async Task RequiredSettlement_AnalyticsFailure_RejectsWithoutLeakingException()
    {
        var analytics = Substitute.For<IBondAnalyticsService>();
        analytics.CalculateAsync(Arg.Any<BondAnalyticsRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<BondAnalyticsResult?>(new InvalidOperationException("private provider detail")));
        await using var gateway = BondGateway(Guid.NewGuid(), analytics, RequiredSettlement());

        var report = await gateway.SubmitOrderAsync(BondOrder());

        AssertNoEconomicFill(report);
        report.RejectReason.Should().NotContain("private provider detail");
    }

    [Fact]
    public async Task RequiredSettlement_UnknownSecurityResult_Rejects()
    {
        var analytics = Substitute.For<IBondAnalyticsService>();
        analytics.CalculateAsync(Arg.Any<BondAnalyticsRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BondAnalyticsResult?>(null));
        await using var gateway = BondGateway(Guid.NewGuid(), analytics, RequiredSettlement());

        AssertNoEconomicFill(await gateway.SubmitOrderAsync(BondOrder()));
    }

    [Fact]
    public async Task RequiredSettlement_Cancellation_DoesNotBecomeCleanFill()
    {
        var analytics = Substitute.For<IBondAnalyticsService>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        analytics.CalculateAsync(Arg.Any<BondAnalyticsRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled<BondAnalyticsResult?>(cancellation.Token));
        await using var gateway = BondGateway(Guid.NewGuid(), analytics, RequiredSettlement());

        var submit = () => gateway.SubmitOrderAsync(BondOrder(), cancellation.Token);

        await submit.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RequiredSettlement_RestingOrder_EmitsTerminalRejectionWithOriginalIdentity()
    {
        var feed = new LiveMarketDataCache();
        await using var gateway = BondGateway(Guid.NewGuid(), null, RequiredSettlement(), feed);
        var accepted = await gateway.SubmitOrderAsync(BondOrder() with { LimitPrice = 100m });
        accepted.OrderStatus.Should().Be(OrderStatus.Accepted);
        feed.RecordTrade(Bond, new Meridian.Contracts.Domain.Models.Trade(DateTimeOffset.UtcNow, Bond, 99m, 100,
            Meridian.Contracts.Domain.Enums.AggressorSide.Buy, 2));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var reports = gateway.StreamExecutionReportsAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);

        gateway.EvaluateSymbol(Bond);
        (await reports.MoveNextAsync()).Should().BeTrue();
        var rejected = reports.Current;

        AssertNoEconomicFill(rejected);
        rejected.OrderId.Should().Be(accepted.OrderId);
        rejected.GatewayOrderId.Should().Be(accepted.GatewayOrderId);
        // Re-evaluation cannot turn the terminal rejection into a later fill.
        gateway.EvaluateSymbol(Bond);
        var cancel = await gateway.CancelOrderAsync(accepted.OrderId);
        cancel.OrderStatus.Should().Be(OrderStatus.Rejected);
    }

    private static void AssertNoEconomicFill(ExecutionReport report)
    {
        report.ReportType.Should().Be(ExecutionReportType.Rejected);
        report.OrderStatus.Should().Be(OrderStatus.Rejected);
        report.FilledQuantity.Should().Be(0m);
        report.FillPrice.Should().BeNull();
        report.Commission.Should().BeNull();
        report.Fees.Should().BeNull();
        report.AccruedInterestPer100.Should().BeNull();
        report.RejectReason.Should().Contain("Bond settlement unavailable").And.Contain("RequireBondAccruedInterest");
    }
}
