using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Instruments.AssetOperations;
using Meridian.Instruments.FixedIncome;
using Meridian.Storage.FixedIncome;

namespace Meridian.Tests.FixedIncome;

public sealed class BondMonitoringAndNativeCashTests
{
    private static readonly DateOnly Start = new(2026, 11, 12);
    private static readonly DateTimeOffset Recorded = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Security = Guid.Parse("c2dd2652-6547-447a-a62b-ded515988d35");
    private static readonly AccountCashForecastScopeDto Scope = new("tenant", "company", "account", "book", "USD");

    [Fact]
    public void NativeCurrenciesRemainUsableWhenBaseFxIsMissing()
    {
        var result = AccountCashForecastEngine.Build(Request([]) with
        {
            NativeOpeningBalances = [Balance("USD", 100m), Balance("EUR", 200m)]
        });
        result.CurrencyLadders.Should().HaveCount(2).And.OnlyContain(row => row.IsDecisionReady);
        result.CurrencyLadders!.Single(row => row.BaseCurrency == "EUR").OpeningCash.Should().Be(200m);
        result.ForecastLadder.IsDecisionReady.Should().BeFalse();
        result.OpeningBaseCash.Should().BeNull();
        result.Blockers.Should().Contain(row => row.Code == "cash-forecast.fx-required");
    }

    [Fact]
    public void PartialReceiptPreservesGrossResidualAndExplainsWithholding()
    {
        var expected = Flow("coupon", "expected", AccountCashFlowStatus.Expected, 100m, Start.AddDays(1));
        var actual = Flow("receipt-one", "cash-one", AccountCashFlowStatus.Actual, 40m, Start);
        var result = AccountCashForecastEngine.Build(Request([expected, actual]) with
        {
            PaymentAllocations = [new("allocation", expected.FlowId, actual.VersionId, 40m, 45m, 5m, DeductionEvidence: Evidence("tax"))]
        });
        result.Residuals.Should().ContainSingle().Which.RemainingAmount.Should().Be(55m);
        result.CurrencyLadders!.Single().Contributions.Sum(row => row.Amount).Should().Be(95m);
        result.Inputs.Flows.Single(row => row.Status == AccountCashFlowStatus.Expected).Amount.Should().Be(100m);
        result.IsCertified.Should().BeFalse();
    }

    [Fact]
    public void OverdueResidualIsVisibleButDoesNotBecomeSpendableFutureCash()
    {
        var expected = Flow("coupon", "expected", AccountCashFlowStatus.Expected, 100m, Start.AddDays(-1));
        var actual = Flow("receipt", "cash", AccountCashFlowStatus.Actual, 40m, Start);
        var request = Request([expected, actual]) with { PaymentAllocations = [new("partial", "coupon", "cash", 40m, 40m)] };
        var result = AccountCashForecastEngine.Build(request);
        result.Residuals.Should().ContainSingle().Which.Status.Should().Be("Overdue");
        result.Residuals!.Single().IncludedInForecast.Should().BeFalse();
        result.CurrencyLadders!.Single().Contributions.Sum(row => row.Amount).Should().Be(40m);

        var revised = AccountCashForecastEngine.Build(request with
        {
            ReceiptRevisions = [new("coupon", Start.AddDays(2), Evidence("revised-receipt"))]
        });
        revised.Residuals!.Single().RemainingAmount.Should().Be(60m);
        revised.CurrencyLadders!.Single().Contributions.Sum(row => row.Amount).Should().Be(100m);
    }

    [Fact]
    public void SignedOutgoingCouponsRemainCashOutflowsAfterPartialPayment()
    {
        var expected = Flow("coupon", "expected", AccountCashFlowStatus.Expected, -100m, Start.AddDays(1));
        var actual = Flow("receipt", "cash", AccountCashFlowStatus.Actual, -40m, Start);
        var result = AccountCashForecastEngine.Build(Request([expected, actual]) with
        { PaymentAllocations = [new("partial", "coupon", "cash", -40m, -40m)] });
        result.Residuals!.Single().RemainingAmount.Should().Be(-60m);
        result.CurrencyLadders!.Single().Contributions.Sum(row => row.Amount).Should().Be(-100m);
    }

    [Fact]
    public void FeeOnOutgoingCouponIncreasesObservedCashOutflow()
    {
        var payment = Flow("payment", "cash-out", AccountCashFlowStatus.Actual, -105m, Start);
        var allocation = new AccountCashPaymentAllocationDto("outgoing", "coupon", "cash-out", -105m, -100m,
            Fees: 5m, DeductionEvidence: Evidence("bank-fee"));
        var monitoring = BondMonitoringEngine.Build(Monitoring([Obligation("coupon", -100m)], [payment], [allocation]));
        monitoring.Issues.Should().BeEmpty();
        monitoring.Collections.Single().RemainingAmount.Should().Be(0m);
        var forecast = AccountCashForecastEngine.Build(Request([
            Flow("coupon", "expected-out", AccountCashFlowStatus.Expected, -100m, Start.AddDays(1)), payment])
            with
        { PaymentAllocations = [allocation] });
        forecast.CurrencyLadders!.Single().Contributions.Sum(row => row.Amount).Should().Be(-105m);
    }

    [Fact]
    public void ReceiptCashCannotBeReusedAcrossObligations()
    {
        var request = Monitoring([
            Obligation("coupon-one", 100m), Obligation("coupon-two", 100m)
        ], [Flow("receipt", "cash", AccountCashFlowStatus.Actual, 40m, Start)],
        [new("one", "coupon-one", "cash", 40m, 40m), new("two", "coupon-two", "cash", 40m, 40m)]);
        BondMonitoringEngine.Build(request).Issues.Should().Contain(row => row.Code == "bond-monitoring.payment-overallocated");
    }

    [Fact]
    public void UnallocatedRelatedCashDoesNotDoubleCountFullExpectedCoupon()
    {
        var receipt = Flow("unmatched", "bank-cash", AccountCashFlowStatus.Actual, 40m, Start);
        var monitoring = BondMonitoringEngine.Build(Monitoring([Obligation("coupon", 100m)], [receipt], []));
        monitoring.Issues.Should().BeEmpty();
        monitoring.Collections.Single().Status.Should().Be("Unexplained");
        monitoring.Collections.Single().IncludedInSpendableForecast.Should().BeFalse();
        monitoring.Watch.Should().Contain(row => row.Kind == "UnallocatedCash");
        var forecast = AccountCashForecastEngine.Build(Request([
            Flow("coupon", "expected", AccountCashFlowStatus.Expected, 100m, Start.AddDays(1)), receipt]));
        forecast.ForecastLadder.IsDecisionReady.Should().BeFalse();
        forecast.Residuals!.Single().IncludedInForecast.Should().BeFalse();
    }

    [Fact]
    public void MultipleIndependentReceiptsSettleOneObligationAndRetainNetTaxBridge()
    {
        var request = Monitoring([Obligation("coupon", 100m)],
            [Flow("receipt-one", "cash-one", AccountCashFlowStatus.Actual, 40m, Start), Flow("receipt-two", "cash-two", AccountCashFlowStatus.Actual, 50m, Start)],
            [new("one", "coupon", "cash-one", 40m, 50m, 10m, DeductionEvidence: Evidence("tax")), new("two", "coupon", "cash-two", 50m, 50m)]);
        var snapshot = BondMonitoringEngine.Build(request);
        snapshot.Issues.Should().BeEmpty();
        var status = snapshot.Collections.Should().ContainSingle().Which;
        status.Status.Should().Be("Collected");
        status.NetCollected.Should().Be(90m);
        status.GrossCollected.Should().Be(100m);
        status.WithholdingTax.Should().Be(10m);
        status.PaymentVersionIds.Should().HaveCount(2);
    }

    [Fact]
    public void UnexplainedTaxAndUnscopedReceiptsCannotEstablishCollection()
    {
        var request = Monitoring([Obligation("coupon", 100m)],
            [Flow("receipt", "cash", AccountCashFlowStatus.Actual, 90m, Start) with { BookId = "other-book" }],
            [new("one", "coupon", "cash", 90m, 100m, 10m)]);
        var snapshot = BondMonitoringEngine.Build(request);
        snapshot.Issues.Should().Contain(row => row.Code == "bond-monitoring.payment-invalid");
        snapshot.Issues.Should().Contain(row => row.Code == "bond-monitoring.source-evidence-required");
    }

    [Fact]
    public void RatingAgenciesAndNoticeSupersessionStayDistinct()
    {
        var request = Monitoring([Obligation("coupon", 100m)], [], []) with
        {
            Observations = [new("sp", Security, "Rating", Start, "AA", "S&P", Evidence("sp")),
                new("moodys", Security, "Rating", Start, "Aa2", "Moody's", Evidence("moodys")),
                new("call", Security, "IssuerCallNotice", Start.AddDays(4), "Called at 101", null, Evidence("call")),
                new("cancel", Security, "NoticeCancellation", Start.AddDays(4), "Notice cancelled", null, Evidence("cancel"), "call")]
        };
        var snapshot = BondMonitoringEngine.Build(request);
        snapshot.Issues.Should().BeEmpty();
        snapshot.Watch.Where(row => row.Kind == "Rating").Should().HaveCount(2);
        snapshot.Watch.Should().NotContain(row => row.WatchId == "call");
        snapshot.Watch.Should().Contain(row => row.WatchId == "cancel");
    }

    [Fact]
    public void CorrectedReceiptRetainsOldEvidenceButCountsOnlyCurrentVersion()
    {
        var old = Flow("bank-movement", "old", AccountCashFlowStatus.Actual, 40m, Start);
        var revised = old with { VersionId = "new", Amount = 45m, Provenance = Evidence("new"), SupersedesVersionId = "old" };
        var request = Monitoring([Obligation("coupon", 100m)], [old, revised], [new("allocation-new", "coupon", "new", 45m, 45m)]);
        var snapshot = BondMonitoringEngine.Build(request);
        snapshot.Issues.Should().BeEmpty();
        snapshot.Collections.Single().RemainingAmount.Should().Be(55m);
        BondMonitoringEngine.SelectActivePayments(snapshot.Inputs.Payments).Should().ContainSingle().Which.Amount.Should().Be(45m);
        var stale = BondMonitoringEngine.Build(request with { Allocations = [new("old-allocation", "coupon", "old", 40m, 40m)] });
        stale.Issues.Should().Contain(row => row.Code == "bond-monitoring.allocation-source-missing");
    }

    [Fact]
    public void CyclicNoticeSupersessionCannotHideBothNotices()
    {
        var request = Monitoring([], [], []) with
        {
            Observations = [new("a", Security, "IssuerCallNotice", Start, "Call A", null, Evidence("a"), "b"),
                new("b", Security, "NoticeCancellation", Start, "Cancelled B", null, Evidence("b"), "a")]
        };
        BondMonitoringEngine.Build(request).Issues.Should().Contain(row => row.Code == "bond-monitoring.observation-revision-cycle");
    }

    [Fact]
    public async Task RetainedMonitoringSurvivesRestartAndRejectsChangedRunOrLostHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-bond-monitoring", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new BondMonitoringService(new BondMonitoringFileStore(root));
            var request = Monitoring([Obligation("coupon", 100m)], [], []);
            var first = await service.CaptureAsync(request);
            var restarted = new BondMonitoringService(new BondMonitoringFileStore(root));
            (await restarted.GetHistoryAsync(Scope)).Should().ContainSingle().Which.SnapshotId.Should().Be(first.SnapshotId);
            var changed = () => restarted.CaptureAsync(request with { Obligations = [Obligation("coupon", 99m)] });
            await changed.Should().ThrowAsync<InvalidOperationException>();
            File.Delete(Path.Combine(root, "fixed-income", "bond-monitoring", "history.json"));
            var lost = () => restarted.GetHistoryAsync(Scope);
            await lost.Should().ThrowAsync<InvalidDataException>();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static AccountCashForecastRequestDto Request(IReadOnlyList<AccountCashFlowDto> flows) =>
        new("forecast", Scope, Start, Start.AddDays(365), null, null, flows, [], Recorded)
        { CalculationVersion = AccountCashForecastEngine.NativeEngineVersion, NativeOpeningBalances = [Balance("USD", 100m)] };
    private static AccountSettledCashObservationDto Balance(string currency, decimal amount) =>
        new(Scope.AccountId, Scope.BookId, Start, amount, currency, true, Evidence($"opening-{currency}"));
    private static AccountCashFlowDto Flow(string flow, string version, AccountCashFlowStatus status, decimal amount, DateOnly receipt) =>
        new(flow, version, Scope.AccountId, Scope.BookId, status, "Coupon", amount, "USD", Start, Start, receipt,
            Evidence(version, status == AccountCashFlowStatus.Actual ? AccountCashEvidenceRole.Observation : AccountCashEvidenceRole.Projection), Security);
    private static AccountCashProvenanceDto Evidence(string id, AccountCashEvidenceRole role = AccountCashEvidenceRole.Observation)
    {
        var hash = Sha256Digest.ComputeUtf8(id);
        return new("SyntheticCustodian", id, hash, role, new(id, $"https://evidence.example.test/{id}", hash,
            "SyntheticCustodian", id, "Accepted", "reviewer", Recorded.AddHours(-2), Start, 1,
            Recorded.AddHours(-1), "retainer", "AccountCash", Scope.AccountId));
    }
    private static BondMonitoringObligationDto Obligation(string id, decimal amount) =>
        new(id, Security, "Coupon", Start.AddDays(1), amount, "USD", "synthetic-verified",
            [new("Terms", "terms-1", ContentSha256: Sha256Digest.ComputeUtf8("terms"), RecordedAt: Recorded.AddHours(-2))]);
    private static BondMonitoringCaptureRequest Monitoring(IReadOnlyList<BondMonitoringObligationDto> obligations,
        IReadOnlyList<AccountCashFlowDto> payments, IReadOnlyList<AccountCashPaymentAllocationDto> allocations) =>
        new("monitoring", Scope, Recorded, obligations, payments, allocations, [], []);
}
