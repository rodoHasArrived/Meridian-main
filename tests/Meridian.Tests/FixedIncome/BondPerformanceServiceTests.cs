using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.FixedIncome;
using NSubstitute;

namespace Meridian.Tests.FixedIncome;

[Trait("Category", "Unit")]
public sealed class BondPerformanceServiceTests
{
    private static readonly Guid Account = Guid.Parse("09f59b67-e8ce-46ad-b5af-a90495720d31");
    private static readonly Guid Security = Guid.Parse("a578240a-f966-41ca-a4ce-d72db10b7f5a");
    private static readonly DateOnly Start = new(2026, 1, 1), End = new(2026, 7, 1), Maturity = new(2027, 1, 1);
    private static readonly DateTimeOffset Known = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    private static AccountCashForecastScopeDto Scope => new("tenant-a", "company-a", Account.ToString("D"),
        "654f5a09-acd5-4881-896f-600649b2a16c", "USD");

    [Fact]
    public async Task MissingTransactionCoverage_ShowsObservedChangeAndPreservesItsUnexplainedResidual()
    {
        var fixture = Build(withCurves: false, completeTransactions: false);
        var result = await fixture.Service.ExplainAsync(new(Scope, Start, End, Known));
        var row = result.Securities.Should().ContainSingle().Subject;
        row.ObservedMarkAndCashChange.Should().Be(10m);
        row.EconomicReturn.Should().BeNull();
        row.Attribution.Residual.Should().Be(10m);
        row.Attribution.RateEffect.Should().BeNull();
        result.CompleteCoverage.Should().BeFalse();
        result.CoverageGaps.Should().Contain(gap => gap.Contains("transaction", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ZeroCouponCarryAndCurveMovement_ReconcileToObservedValueChangeWithoutAccountingDoubleCount()
    {
        var fixture = Build();
        var result = await fixture.Service.ExplainAsync(new(Scope, Start, End, Known));
        var row = result.Securities.Should().ContainSingle().Subject;
        row.EconomicReturn.Should().Be(10m);
        row.Attribution.CarryAndRoll.Should().BeApproximately(9.8989898989898989898989899m, 0.000000001m);
        row.Attribution.RateEffect.Should().BeApproximately(0.1010101010101010101010101m, 0.000000001m);
        Sum(row.Attribution).Should().BeApproximately(10m, 0.000000001m);
        row.Attribution.Residual.Should().BeApproximately(0m, 0.000000001m);
        row.BaseObservedMarkAndCashChange.Should().Be(10m);
        result.AccountingIncome.Should().BeEmpty("market return does not invent an amortization journal or add income twice");
    }

    [Fact]
    public async Task RetainedCouponAllocation_ExplainsGrossNetTaxAndCollectionEffectExactlyOnce()
    {
        var fixture = Build(couponReceipt: true);
        var result = await fixture.Service.ExplainAsync(new(Scope, Start, End, Known));
        var row = result.Securities.Should().ContainSingle().Subject;
        row.ActualNetCash.Should().Be(18m);
        row.CashGrossCoupons.Should().Be(20m);
        row.WithholdingTax.Should().Be(2m);
        row.PrincipalReturned.Should().Be(0m);
        row.ObservedMarkAndCashChange.Should().Be(28m);
        row.Attribution.CollectionAndCosts.Should().Be(-2m);
        Sum(row.Attribution).Should().BeApproximately(28m, 0.000000001m);
        row.Attribution.Residual.Should().BeApproximately(0m, 0.000000001m);
    }

    [Fact]
    public async Task ForeignMarks_RequireDatedFx_ThenSeparateFxFromNativeReturn()
    {
        var missing = Build(currency: "EUR");
        var incomplete = await missing.Service.ExplainAsync(new(Scope, Start, End, Known));
        incomplete.Securities[0].ObservedMarkAndCashChange.Should().Be(10m);
        incomplete.BaseObservedMarkAndCashChange.Should().BeNull();
        incomplete.Securities[0].CoverageGaps.Should().Contain(gap => gap.Contains("FX evidence", StringComparison.Ordinal));

        var fixture = Build(currency: "EUR", withFx: true);
        var result = await fixture.Service.ExplainAsync(new(Scope, Start, End, Known));
        var row = result.Securities[0];
        row.BaseObservedMarkAndCashChange.Should().Be(110m);
        row.BaseCurrencyAttribution!.FxEffect.Should().Be(99m);
        Sum(row.BaseCurrencyAttribution).Should().BeApproximately(110m, 0.000000001m);
    }

    [Fact]
    public async Task RoundTripTrades_AreIncludedEvenWhenBothAcceptedStatementsAreFlat()
    {
        var fixture = Build(roundTrip: true);
        var result = await fixture.Service.ExplainAsync(new(Scope, Start, End, Known));
        var row = result.Securities.Should().ContainSingle().Subject;
        row.OpeningDirtyValue.Should().Be(0m);
        row.ClosingDirtyValue.Should().Be(0m);
        row.ObservedMarkAndCashChange.Should().Be(20m);
        row.TradeSettlementCash.Should().Be(20m);
        row.EconomicReturn.Should().Be(20m);
        row.Attribution.Residual.Should().Be(20m, "the observed trade profit is retained when constant-position attribution is unavailable");
        row.BaseObservedMarkAndCashChange.Should().Be(20m);
    }

    [Fact]
    public async Task UnexplainedEnteringPosition_IsNotCountedAsInvestmentProfit()
    {
        var fixture = Build(unexplainedEntry: true);
        var result = await fixture.Service.ExplainAsync(new(Scope, Start, End, Known));
        result.Securities[0].ObservedMarkAndCashChange.Should().Be(990m);
        result.Securities[0].EconomicReturn.Should().BeNull();
        result.Securities[0].CoverageGaps.Should().Contain(gap => gap.Contains("in-kind", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModelMustReproduceAcceptedAccruedAndIndexedInvoiceBasis_BeforeAttribution(bool indexed)
    {
        var fixture = Build(holdingAccrued: indexed ? 0m : 2m, modelAccrued: indexed ? 0m : 1m,
            holdingRatio: indexed ? 1.2m : 1m, modelRatio: indexed ? 1.3m : 1m, indexed: indexed);
        var result = await fixture.Service.ExplainAsync(new(Scope, Start, End, Known));
        var row = result.Securities.Should().ContainSingle().Which;
        row.ObservedMarkAndCashChange.Should().Be(indexed ? 12m : 10m);
        row.EconomicReturn.Should().Be(row.ObservedMarkAndCashChange, "accepted marks and complete cash/trade populations still establish native P&L");
        row.Attribution.CarryAndRoll.Should().BeNull();
        row.Attribution.RateEffect.Should().BeNull();
        row.Attribution.Residual.Should().Be(row.ObservedMarkAndCashChange);
        row.CoverageGaps.Should().Contain(gap => gap.Contains("accepted same-date dirty mark", StringComparison.Ordinal));
        result.CompleteCoverage.Should().BeFalse();
    }

    private static decimal Sum(BondPerformanceAttributionDto row) => row.CarryAndRoll!.Value + row.RateEffect!.Value
        + row.SpreadEffect!.Value + row.InflationEffect!.Value + row.CollectionAndCosts!.Value + row.FxEffect!.Value + row.Residual!.Value;

    private static (BondPerformanceService Service, IAccountBondHoldingsSource Holdings) Build(string currency = "USD",
        bool withCurves = true, bool completeTransactions = true, bool couponReceipt = false, bool withFx = false,
        bool roundTrip = false, bool unexplainedEntry = false, decimal holdingAccrued = 0m,
        decimal modelAccrued = 0m, decimal holdingRatio = 1m, decimal modelRatio = 1m, bool indexed = false)
    {
        var holdings = Substitute.For<IAccountBondHoldingsSource>();
        var openingSnapshot = Snapshot(Start, 98m, currency, holdingAccrued, holdingRatio, indexed);
        var closingSnapshot = Snapshot(End, 99m, currency, holdingAccrued, holdingRatio, indexed);
        holdings.GetHoldingsAsync(Account, Start, Known, Arg.Any<CancellationToken>()).Returns(roundTrip || unexplainedEntry ? openingSnapshot with { Positions = [] } : openingSnapshot);
        holdings.GetHoldingsAsync(Account, End, Known, Arg.Any<CancellationToken>()).Returns(roundTrip ? closingSnapshot with { Positions = [] } : closingSnapshot);
        var workflow = Substitute.For<IBondWorkflowService>();
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetRecordedByIdAsOfAsync(Security, Known, Arg.Any<CancellationToken>()).Returns(new SecurityDetailDto(Security,
            "Bond", SecurityStatusDto.Active, "Synthetic zero bond", currency, JsonSerializer.SerializeToElement(new { }),
            JsonSerializer.SerializeToElement(new { couponType = "ZeroCoupon" }), [], [], 1, Instant(Start), null));
        var curveStore = Substitute.For<ISecurityCurveStore>();
        var curveId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var lastId = Guid.NewGuid();
        var definition = new SecurityCurveDefinitionDto(curveId, "SYNTHETIC-DISCOUNT", SecurityCurveKind.Discount, currency,
            null, null, "Actual365", SecurityCurveInterpolation.Linear, "synthetic", null, "operator", Instant(Start));
        var firstCurve = new SecurityCurveVersionDto(firstId, curveId, definition.CurveKey, 1, Start, Instant(Start), Instant(Start),
            "synthetic", "operator", [new(Start, 1m), new(End, 0.99m), new(Maturity, 0.98m)], new string('a', 64));
        var lastCurve = new SecurityCurveVersionDto(lastId, curveId, definition.CurveKey, 2, End, Instant(End), Instant(End),
            "synthetic", "operator", [new(End, 1m), new(Maturity, 0.99m)], new string('b', 64));
        curveStore.ListDefinitionsAsync(SecurityCurveKind.Discount, currency, null, Arg.Any<CancellationToken>()).Returns([definition]);
        curveStore.GetVersionByIdAsync(curveId, firstId, Arg.Any<CancellationToken>()).Returns(firstCurve);
        curveStore.GetVersionByIdAsync(curveId, lastId, Arg.Any<CancellationToken>()).Returns(lastCurve);
        workflow.CalculateAsync(Arg.Any<BondWorkflowRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<BondWorkflowRequest>();
            var opening = request.Context.SettlementDate == Start;
            var curve = opening ? firstCurve : lastCurve;
            var clean = request.CleanPricePer100!.Value * 10m * (indexed ? modelRatio : 1m);
            var dirty = clean + modelAccrued;
            var flows = new List<BondWorkflowCashFlow> { new(Maturity, Start, Maturity, 0m, 1000m, 0m, 1m, false, []) };
            if (opening && couponReceipt)
                flows.Add(new(new(2026, 4, 1), Start, new(2026, 4, 1), 20m, 0m, 1000m, 1m, false, []));
            return Task.FromResult<BondWorkflowResult?>(new(Security, currency, "synthetic", request.Context with { SecurityVersion = 1 },
                1000m, 1000m, modelAccrued, clean, dirty, 0m, dirty,
                dirty, null, 0m, null, null, null, flows,
                withCurves ? [new("Curve:Discount", definition.CurveKey, curve.Version, curve.ContentSha256, curve.CurveVersionId)] : [], [], [])
            { SettlementIndexRatio = modelRatio });
        });
        var monitoring = Substitute.For<IBondMonitoringService>();
        var provenance = Provenance("payment", new(2026, 4, 1));
        var obligations = couponReceipt ? new[] { new BondMonitoringObligationDto("coupon", Security, "Coupon", new(2026, 4, 1), 20m, currency, "synthetic", []) } : [];
        var payments = couponReceipt ? new[] { new AccountCashFlowDto("receipt", "payment-v1", Scope.AccountId, Scope.BookId,
            AccountCashFlowStatus.Actual, "Coupon", 18m, currency, new(2026, 4, 1), new(2026, 4, 1), new(2026, 4, 1), provenance, Security) } : [];
        var allocations = couponReceipt ? new[] { new AccountCashPaymentAllocationDto("allocation", "coupon", "payment-v1", 18m, 20m, 2m) } : [];
        var monitorRequest = new BondMonitoringCaptureRequest("monitor", Scope, Known, obligations, payments, allocations, [], []);
        monitoring.GetHistoryAsync(Scope, Arg.Any<CancellationToken>()).Returns([new("monitor", "synthetic", monitorRequest, [], [], [])]);
        var transactionSource = Substitute.For<IBondPerformanceTransactionSource>();
        BondPerformanceTradeCashDto[] trades = roundTrip
            ? [new(Security, new(2026, 2, 1), -1000m, currency, new("Trade", "buy")) { OriginalFaceDelta = 1000m },
               new(Security, new(2026, 3, 1), 1020m, currency, new("Trade", "sell")) { OriginalFaceDelta = -1000m }] : [];
        transactionSource.GetAsync(Scope, Start, End, Known, Arg.Any<CancellationToken>())
            .Returns(new BondPerformanceTransactionEvidenceDto(completeTransactions, trades, [], []));
        var forecasts = Substitute.For<IAccountCashForecastService>();
        var fx = withFx ? new[] { new AccountCashFxConversionDto(currency, "USD", Start, 1.1m, Provenance("fx-start", Start)),
            new AccountCashFxConversionDto(currency, "USD", End, 1.2m, Provenance("fx-end", End)) } : [];
        var forecastRequest = new AccountCashForecastRequestDto("fx", Scope, Start, End, null, null, [], fx, Known);
        forecasts.GetHistoryAsync(Scope, Arg.Any<CancellationToken>()).Returns([new AccountCashForecastSnapshotDto("fx", "hash", "synthetic",
            forecastRequest, null, null, null, null, null, false, null!, [])]);
        var verifier = Substitute.For<IBondPerformanceSourceVerifier>();
        return (new(holdings, workflow, monitoring, query, withCurves ? curveStore : null, forecasts,
            transactions: transactionSource, clock: new Clock(), inputVerifier: verifier), holdings);
    }

    private static AccountBondHoldingsSnapshotDto Snapshot(DateOnly date, decimal quote, string currency,
        decimal accrued = 0m, decimal ratio = 1m, bool indexed = false)
    {
        var batchId = Guid.NewGuid();
        return new(Account, date, batchId, new string('a', 64), "synthetic:holdings",
            [new(Guid.NewGuid(), batchId, Account, date, "SYNTHETIC", "CUSIP", 1000m, quote * 10m * (indexed ? ratio : 1m) + accrued, currency, "Synthetic bond", "Bond", false,
                SecurityId: Security, Bond: new(1000m, 1000m, 1m, ratio, BondHoldingQuoteBasis.PercentOfCurrentFace, quote,
                    false, accrued, InvoiceBasis: indexed ? BondHoldingInvoiceBasis.IndexedPrincipal : BondHoldingInvoiceBasis.Nominal))]);
    }

    private static DateTimeOffset Instant(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    private static AccountCashProvenanceDto Provenance(string id, DateOnly date) => new("Synthetic", id, new string('a', 64),
        AccountCashEvidenceRole.Observation, new(id, "synthetic:" + id, new string('a', 64), "Synthetic", id,
            "Accepted", "operator", Known.AddHours(-1), date, 1, Known.AddMinutes(-30), "operator", "AccountCash", id));
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Known; }
}
