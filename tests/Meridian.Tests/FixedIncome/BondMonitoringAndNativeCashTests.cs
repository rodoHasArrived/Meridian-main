using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Instruments.AssetOperations;
using Meridian.Instruments.FixedIncome;
using Meridian.Storage.FixedIncome;
using Meridian.Storage.AssetOperations;

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

    [Theory]
    [InlineData(1, 40, 2)]
    [InlineData(-1, 40, 2)]
    [InlineData(1, 40, 3)]
    [InlineData(-1, 40, 3)]
    [InlineData(1, 0, 5)]
    [InlineData(-1, 0, 5)]
    [InlineData(1, 40, 5)]
    [InlineData(-1, 40, 5)]
    [InlineData(1, 100, 5)]
    [InlineData(-1, 100, 5)]
    public void RetainedReceiptRevisionSurvivesRefresh_AndOnlyUnpaidResidualAgesFromItsExplicitDate(
        int sign, int paidAbsolute, int elapsedDays)
    {
        var amount = sign * 100m;
        var paid = sign * paidAbsolute;
        var obligation = Obligation("coupon", amount);
        var revision = new AccountCashReceiptRevisionDto("coupon", Start.AddDays(3), Evidence("revised-receipt"));
        var first = BondMonitoringEngine.Build(Monitoring([obligation], [], []) with { ReceiptRevisions = [revision] });
        first.Issues.Should().BeEmpty();
        first.Collections.Single().AgeDays.Should().Be(0);

        var payment = Flow("receipt", "cash", AccountCashFlowStatus.Actual, paid, Start.AddDays(Math.Min(elapsedDays, 3)));
        payment = payment with
        {
            Provenance = payment.Provenance with
            {
                Evidence = payment.Provenance.Evidence with
                {
                    ReviewedAtUtc = Recorded.AddDays(Math.Min(elapsedDays, 3)).AddHours(-2),
                    RetainedAtUtc = Recorded.AddDays(Math.Min(elapsedDays, 3)).AddHours(-1)
                }
            }
        };
        AccountCashFlowDto[] payments = paid == 0m ? [] : [payment];
        AccountCashPaymentAllocationDto[] allocations = paid == 0m ? [] : [new("partial", "coupon", "cash", paid, paid)];
        var refreshed = BondMonitoringEngine.Build(first.Inputs with
        {
            RunId = "refreshed",
            RecordedAtUtc = Recorded.AddDays(elapsedDays),
            Payments = payments,
            Allocations = allocations,
            PreviousSnapshotId = first.SnapshotId,
            CorrectionRationale = "Refresh retained receipt evidence as time advances."
        });
        refreshed.Issues.Should().BeEmpty();
        refreshed.Inputs.ReceiptRevisions.Should().BeEquivalentTo(first.Inputs.ReceiptRevisions);
        var collection = refreshed.Collections.Single();
        var residual = amount - paid;
        var overdue = residual != 0m && elapsedDays > 3;
        var status = residual == 0m ? "Collected" : overdue ? "Overdue" : paid != 0m ? "Partial" : "Expected";
        collection.RemainingAmount.Should().Be(residual);
        collection.Status.Should().Be(status);
        collection.RevisedReceiptDate.Should().Be(revision.ReceiptDate);
        collection.AgeDays.Should().Be(residual == 0m ? 0 : Math.Max(0, elapsedDays - 3));
        collection.IncludedInSpendableForecast.Should().Be(residual != 0m && !overdue);

        var forecast = AccountCashForecastEngine.Build(Request([Flow("coupon", "expected", AccountCashFlowStatus.Expected,
            amount, obligation.DueDate), .. payments]) with
        {
            RecordedAtUtc = Recorded.AddDays(elapsedDays),
            ReceiptRevisions = [revision],
            PaymentAllocations = allocations
        });
        forecast.Blockers.Should().NotContain(row => row.Code != "cash-forecast.closing-required");
        forecast.Inputs.ReceiptRevisions.Should().BeEquivalentTo(first.Inputs.ReceiptRevisions);
        forecast.Residuals.Should().ContainSingle().Which.Status.Should().Be(status);
        forecast.Residuals!.Single().IncludedInForecast.Should().Be(residual != 0m && !overdue);
        forecast.CurrencyLadders!.Single().Contributions.Sum(row => row.Amount).Should().Be(overdue ? paid : amount);
    }

    [Fact]
    public void ExplicitAcceleratedReceiptDateRemainsEligible_ThenAgesWithoutChangingContractualDueDate()
    {
        var obligation = Obligation("coupon", 100m) with { DueDate = Start.AddDays(10) };
        var revision = new AccountCashReceiptRevisionDto("coupon", Start.AddDays(2), Evidence("accelerated-receipt"));
        var initial = Monitoring([obligation], [], []) with { ReceiptRevisions = [revision] };
        var first = BondMonitoringEngine.Build(initial);
        first.Issues.Should().BeEmpty();
        first.Collections.Single().IncludedInSpendableForecast.Should().BeTrue();
        var after = BondMonitoringEngine.Build(initial with { RecordedAtUtc = Recorded.AddDays(4) });
        after.Issues.Should().BeEmpty();
        after.Collections.Single().Status.Should().Be("Overdue");
        after.Collections.Single().AgeDays.Should().Be(2);
        after.Collections.Single().DueDate.Should().Be(obligation.DueDate);
        after.Collections.Single().IncludedInSpendableForecast.Should().BeFalse();
        var forecast = AccountCashForecastEngine.Build(Request([Flow("coupon", "expected", AccountCashFlowStatus.Expected,
            100m, obligation.DueDate)]) with
        { RecordedAtUtc = Recorded.AddDays(4), ReceiptRevisions = [revision] });
        forecast.Blockers.Should().NotContain(row => row.Code != "cash-forecast.closing-required");
        forecast.Residuals!.Single().Status.Should().Be("Overdue");
        forecast.Residuals.Single().IncludedInForecast.Should().BeFalse();
        forecast.CurrencyLadders!.Single().Contributions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("missing-date")]
    [InlineData("unknown-obligation")]
    [InlineData("unsupported-evidence")]
    [InlineData("duplicate-revision")]
    public void MalformedOrUnverifiedReceiptRevisionsStillBlockMonitoringAndForecast(string defect)
    {
        var revision = new AccountCashReceiptRevisionDto(defect == "unknown-obligation" ? "unknown" : "coupon",
            defect == "missing-date" ? default : Start.AddDays(2),
            Evidence("revision", defect == "unsupported-evidence" ? AccountCashEvidenceRole.Projection : AccountCashEvidenceRole.Observation));
        var revisions = defect == "duplicate-revision" ? new[] { revision, revision } : [revision];
        var monitoring = BondMonitoringEngine.Build(Monitoring([Obligation("coupon", 100m)], [], []) with { ReceiptRevisions = revisions });
        monitoring.Issues.Should().Contain(row => row.Code == "bond-monitoring.receipt-revision-invalid"
            || row.Code == "bond-monitoring.source-evidence-required" || row.Code == "bond-monitoring.receipt-revision-identity-invalid");
        var forecast = AccountCashForecastEngine.Build(Request([Flow("coupon", "expected", AccountCashFlowStatus.Expected,
            100m, Start.AddDays(1))]) with
        { ReceiptRevisions = revisions });
        forecast.Blockers.Should().Contain(row => row.Code == "cash-forecast.receipt-revision-invalid"
            || row.Code == "cash-forecast.provenance-incomplete" || row.Code == "cash-forecast.revision-ambiguous");
    }

    [Fact]
    public async Task HistoricalMonitoringV1AndNativeForecastV2ReplayExactly_WhileNewRunsUseRevisedDateAging()
    {
        var root = Path.Combine(Path.GetTempPath(), "bond-receipt-date-history-" + Guid.NewGuid().ToString("N"));
        try
        {
            var revision = new AccountCashReceiptRevisionDto("coupon", Start.AddDays(2), Evidence("retained-revision"));
            var original = Monitoring([Obligation("coupon", 100m) with { DueDate = Start.AddDays(-3) }], [], [])
                with
            { ReceiptRevisions = [revision] };
            var legacy = BondMonitoringEngine.Build(original, BondMonitoringEngine.LegacyEngineVersion);
            legacy.Issues.Should().BeEmpty();
            legacy.Collections.Single().AgeDays.Should().Be(3, "legacy v1 aged from the contractual date even with a future revision");
            BondMonitoringEngine.Build(original).Collections.Single().AgeDays.Should().Be(0);
            var retainedJson = JsonSerializer.Serialize(legacy, BondMonitoringJsonContext.Default.BondMonitoringSnapshotDto);
            await new BondMonitoringFileStore(root).AppendAsync(legacy);
            var monitor = new BondMonitoringService(new BondMonitoringFileStore(root));
            var replay = await monitor.CaptureAsync(original);
            JsonSerializer.Serialize(replay, BondMonitoringJsonContext.Default.BondMonitoringSnapshotDto).Should().Be(retainedJson);
            var changed = () => monitor.CaptureAsync(original with { ReceiptRevisions = [revision with { ReceiptDate = Start.AddDays(4) }] });
            await changed.Should().ThrowAsync<InvalidOperationException>();
            var current = await monitor.CaptureAsync(original with
            {
                RunId = "new-monitoring-run",
                RecordedAtUtc = Recorded.AddDays(5),
                PreviousSnapshotId = legacy.SnapshotId,
                CorrectionRationale = "Refresh collection status while retaining the exact revised-date source."
            });
            current.EngineVersion.Should().Be(BondMonitoringEngine.EngineVersion);
            current.Collections.Single().Status.Should().Be("Overdue");
            current.Collections.Single().AgeDays.Should().Be(3);
            current.Collections.Single().IncludedInSpendableForecast.Should().BeFalse();
            var history = await new BondMonitoringService(new BondMonitoringFileStore(root)).GetHistoryAsync(Scope);
            history.Should().HaveCount(2);
            JsonSerializer.Serialize(history[0], BondMonitoringJsonContext.Default.BondMonitoringSnapshotDto).Should().Be(retainedJson);

            var cashStore = new FileAccountCashForecastStore(root);
            await cashStore.InitializeAsync();
            var oldInputs = Request([Flow("coupon", "expected", AccountCashFlowStatus.Expected, 100m, Start.AddDays(1))]) with
            { CalculationVersion = AccountCashForecastEngine.LegacyNativeEngineVersion, ReceiptRevisions = [revision] };
            var oldCash = AccountCashForecastEngine.Build(oldInputs);
            var oldCashJson = JsonSerializer.Serialize(oldCash, AccountCashForecastJsonContext.Default.AccountCashForecastSnapshotDto);
            await cashStore.AppendAsync(oldCash);
            var cash = new AccountCashForecastService(new FileAccountCashForecastStore(root));
            JsonSerializer.Serialize(await cash.CaptureAsync(oldInputs), AccountCashForecastJsonContext.Default.AccountCashForecastSnapshotDto)
                .Should().Be(oldCashJson);
            var newCash = await cash.CaptureAsync(oldInputs with
            {
                RunId = "new-native-run",
                CalculationVersion = AccountCashForecastEngine.NativeEngineVersion,
                RecordedAtUtc = Recorded.AddDays(5),
                PreviousSnapshotId = oldCash.SnapshotId,
                CorrectionRationale = "Refresh native liquidity from the unchanged retained revision."
            });
            newCash.EngineVersion.Should().Be(AccountCashForecastEngine.NativeEngineVersion);
            newCash.Residuals!.Single().Status.Should().Be("Overdue");
            newCash.Residuals.Single().IncludedInForecast.Should().BeFalse();
            newCash.CurrencyLadders!.Single().Contributions.Should().BeEmpty();
            var cashHistory = await new AccountCashForecastService(new FileAccountCashForecastStore(root)).GetHistoryAsync(Scope);
            cashHistory.Should().HaveCount(2);
            JsonSerializer.Serialize(cashHistory[0], AccountCashForecastJsonContext.Default.AccountCashForecastSnapshotDto).Should().Be(oldCashJson);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
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
