using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Instruments.AssetOperations;
using Meridian.Storage.AssetOperations;

namespace Meridian.Tests.AssetOperations;

public sealed class AccountCashForecastTests : IDisposable
{
    private static readonly DateOnly Start = new(2026, 10, 1);
    private static readonly DateOnly End = Start.AddDays(6);
    private static readonly DateTimeOffset Recorded = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
    private static readonly AccountCashForecastScopeDto Scope = new("tenant", "company", "account-a", "book-a", "USD");
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "meridian-account-cash-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Build_ReconcilesActualCashAndRecognizesReceiptInsteadOfEconomicOrSettlementDate()
    {
        var expected = Flow("coupon", "prediction", AccountCashFlowStatus.Expected, 25m);
        var actual = Flow("coupon", "statement", AccountCashFlowStatus.Actual, 24m);
        var snapshot = AccountCashForecastEngine.Build(Request([expected, actual], 124m));

        snapshot.IsCertified.Should().BeTrue();
        snapshot.ActualNetBaseFlow.Should().Be(24m);
        snapshot.ReconciledClosingBaseCash.Should().Be(124m);
        snapshot.ClosingVariance.Should().Be(0m);
        snapshot.ForecastLadder.EngineVersion.Should().Be(PortfolioCashLadderEngine.EngineVersion);
        snapshot.ForecastLadder.Contributions.Should().ContainSingle().Which.DueDate.Should().Be(Start.AddDays(4));
        snapshot.ForecastLadder.Buckets[1].CumulativeCash.Should().Be(100m);
        snapshot.ForecastLadder.Buckets[2].CumulativeCash.Should().Be(100m);
        snapshot.ForecastLadder.Buckets[4].CumulativeCash.Should().Be(124m);
        snapshot.Inputs.Flows.Single(row => row.Status == AccountCashFlowStatus.Actual).Should().Be(actual);
    }

    [Fact]
    public void Build_DoesNotCountUnsettledTradesBeforeActualCashReceipt()
    {
        var expected = Flow("trade", "unsettled", AccountCashFlowStatus.Expected, -50m) with
        {
            FlowType = "UnsettledTrade",
            CashReceiptDate = End.AddDays(2)
        };
        var snapshot = AccountCashForecastEngine.Build(Request([expected], 100m));
        snapshot.IsCertified.Should().BeTrue();
        snapshot.ActualNetBaseFlow.Should().Be(0m);
        snapshot.ForecastLadder.Contributions.Should().BeEmpty();
    }

    [Fact]
    public void Build_ExpectedCashAloneCannotCertifyClosingCash()
    {
        var snapshot = AccountCashForecastEngine.Build(Request([Flow("coupon", "predicted", AccountCashFlowStatus.Expected, 25m)], 100m));
        snapshot.IsCertified.Should().BeFalse();
        snapshot.ActualNetBaseFlow.Should().Be(0m);
        snapshot.ForecastLadder.Buckets.Last().CumulativeCash.Should().Be(125m);
        snapshot.Blockers.Should().Contain(row => row.Code == "cash-forecast.expected-unreceived");
    }

    [Fact]
    public void Build_MissingAccountScopeBlocksCertification()
    {
        var snapshot = AccountCashForecastEngine.Build(Request([], 100m) with { Scope = Scope with { AccountId = "" } });
        snapshot.IsCertified.Should().BeFalse();
        snapshot.OpeningBaseCash.Should().BeNull();
        snapshot.Blockers.Should().Contain(row => row.Code == "cash-forecast.scope-required");
    }

    [Fact]
    public void Build_RejectsForeignAccountOrBookInsteadOfMatchingItsAmount()
    {
        var foreign = Flow("coupon", "foreign", AccountCashFlowStatus.Actual, 25m) with { AccountId = "account-b" };
        var wrongBook = Flow("principal", "wrong-book", AccountCashFlowStatus.Actual, 50m) with { BookId = "book-b" };
        var snapshot = AccountCashForecastEngine.Build(Request([foreign, wrongBook], 125m));
        snapshot.IsCertified.Should().BeFalse();
        snapshot.ActualNetBaseFlow.Should().Be(0m);
        snapshot.ReconciledClosingBaseCash.Should().Be(100m);
        snapshot.ForecastLadder.Contributions.Should().BeEmpty();
        snapshot.Blockers.Should().Contain(row => row.Code == "cash-forecast.flow-scope-mismatch");
    }

    [Fact]
    public void Build_ConvertsCashWithDeclaredDirectionAndExactReceiptDate()
    {
        var actual = Flow("coupon", "eur-statement", AccountCashFlowStatus.Actual, 10m) with { Currency = "EUR" };
        var rate = Fx("EUR", "USD", Start.AddDays(4), 2.4m);
        var snapshot = AccountCashForecastEngine.Build(Request([actual], 124m) with { FxConversions = [rate] });
        snapshot.IsCertified.Should().BeTrue();
        snapshot.ActualNetBaseFlow.Should().Be(24m);
        snapshot.Inputs.Flows.Single().Currency.Should().Be("EUR");
        snapshot.Inputs.FxConversions.Single().BaseUnitsPerCurrencyUnit.Should().Be(2.4m);
        snapshot.ForecastLadder.Contributions.Single().Amount.Should().Be(24m);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-date")]
    [InlineData("inverse")]
    [InlineData("ambiguous")]
    public void Build_MissingWrongDateInverseOrAmbiguousFxCannotCertify(string variant)
    {
        var flow = Flow("coupon", "eur-cash", AccountCashFlowStatus.Actual, 10m) with { Currency = "EUR" };
        var rate = Fx("EUR", "USD", Start.AddDays(4), 1.2m);
        AccountCashFxConversionDto[] conversions = variant switch
        {
            "wrong-date" => [rate with { ConversionDate = Start.AddDays(3) }],
            "inverse" => [Fx("USD", "EUR", Start.AddDays(4), 1m / 1.2m)],
            "ambiguous" => [rate, rate with { BaseUnitsPerCurrencyUnit = 1.3m }],
            _ => []
        };
        var snapshot = AccountCashForecastEngine.Build(Request([flow], 112m) with { FxConversions = conversions });
        snapshot.IsCertified.Should().BeFalse();
        snapshot.ActualNetBaseFlow.Should().BeNull();
        snapshot.ForecastLadder.IsDecisionReady.Should().BeFalse();
        snapshot.Blockers.Should().Contain(row => row.Code == "cash-forecast.fx-required");
    }

    [Fact]
    public void Build_ForeignOpeningAndClosingRequireOwnDatedFx()
    {
        var request = Request([], 100m);
        var snapshot = AccountCashForecastEngine.Build(request with
        {
            OpeningCash = request.OpeningCash! with { Currency = "EUR" },
            ClosingCash = request.ClosingCash! with { Currency = "EUR" },
            FxConversions = [Fx("EUR", "USD", Start, 1.2m), Fx("EUR", "USD", End, 1.2m)]
        });
        snapshot.IsCertified.Should().BeTrue();
        snapshot.OpeningBaseCash.Should().Be(120m);
        snapshot.ObservedClosingBaseCash.Should().Be(120m);
    }

    [Fact]
    public void Build_RequiresReviewedIndependentObservedSourceAndAllThreeDates()
    {
        var expected = Flow("coupon", "predicted", AccountCashFlowStatus.Expected, 25m);
        var actual = Flow("coupon", "actual", AccountCashFlowStatus.Actual, 25m) with
        {
            SettlementDate = null,
            Provenance = expected.Provenance with { Role = AccountCashEvidenceRole.Observation }
        };
        var snapshot = AccountCashForecastEngine.Build(Request([expected, actual], 125m));
        snapshot.IsCertified.Should().BeFalse();
        snapshot.Blockers.Should().Contain(row => row.Code == "cash-forecast.flow-dates-required");
        snapshot.Blockers.Should().Contain(row => row.Code == "cash-forecast.actual-independent-source-required");
        var unreviewed = actual with { Provenance = actual.Provenance with { Evidence = actual.Provenance.Evidence with { ReviewStatus = "Pending" } } };
        AccountCashForecastEngine.Build(Request([unreviewed], 125m)).Blockers.Should().Contain(row => row.Code == "cash-forecast.provenance-incomplete");
    }

    [Fact]
    public void Build_DuplicateActualFlowVersionsAreBlockedWithoutThrowing()
    {
        var actual = Flow("coupon", "statement", AccountCashFlowStatus.Actual, 25m);
        var snapshot = AccountCashForecastEngine.Build(Request([actual, actual], 150m));
        snapshot.IsCertified.Should().BeFalse();
        snapshot.Blockers.Should().Contain(row => row.Code == "cash-forecast.flow-ambiguous");
        snapshot.Blockers.Should().Contain(row => row.Code == "cash-forecast.version-duplicate");
    }

    [Fact]
    public void Build_StableFlowIdCannotMatchUnrelatedSecurityOrDifferentEconomicFlowType()
    {
        var expected = Flow("cash", "prediction", AccountCashFlowStatus.Expected, 25m) with { SecurityId = Guid.NewGuid() };
        var actual = Flow("cash", "statement", AccountCashFlowStatus.Actual, 25m) with { SecurityId = Guid.NewGuid(), FlowType = "Principal" };
        var snapshot = AccountCashForecastEngine.Build(Request([expected, actual], 125m));
        snapshot.IsCertified.Should().BeFalse();
        snapshot.Blockers.Should().Contain(row => row.Code == "cash-forecast.flow-link-mismatch");
    }

    [Fact]
    public void Build_OverflowProducesVisibleBlocker()
    {
        var flow = Flow("coupon", "overflow", AccountCashFlowStatus.Actual, decimal.MaxValue) with { Currency = "EUR" };
        var snapshot = AccountCashForecastEngine.Build(Request([flow], 100m) with { FxConversions = [Fx("EUR", "USD", Start.AddDays(4), 2m)] });
        snapshot.IsCertified.Should().BeFalse();
        snapshot.Blockers.Should().Contain(row => row.Code == "cash-forecast.amount-overflow");
    }

    [Fact]
    public void Build_FutureObservationOrUnretainedReviewCannotCertify()
    {
        var actual = Flow("coupon", "future-statement", AccountCashFlowStatus.Actual, 25m) with { CashReceiptDate = End.AddDays(3) };
        var request = Request([actual], 100m) with { RecordedAtUtc = Recorded.AddDays(-3) };
        var snapshot = AccountCashForecastEngine.Build(request);
        snapshot.IsCertified.Should().BeFalse();
        snapshot.Blockers.Should().Contain(row => row.Code == "cash-forecast.actual-date-in-future");
        snapshot.Blockers.Should().Contain(row => row.Code == "cash-forecast.balance-date-in-future");
        snapshot.Blockers.Should().Contain(row => row.Code == "cash-forecast.evidence-not-yet-retained");
    }

    [Fact]
    public void Build_EarlierRetainedPredictionCannotBecomeAnActualReceiptByWaiting()
    {
        var actual = Flow("coupon", "previously-predicted", AccountCashFlowStatus.Actual, 25m);
        actual = actual with
        {
            Provenance = actual.Provenance with
            {
                Evidence = actual.Provenance.Evidence with { ReviewedAtUtc = Recorded.AddDays(-5), RetainedAtUtc = Recorded.AddDays(-4) }
            }
        };
        var snapshot = AccountCashForecastEngine.Build(Request([actual], 125m));
        snapshot.IsCertified.Should().BeFalse();
        snapshot.Blockers.Should().Contain(row => row.Code == "cash-forecast.actual-not-observed-at-retention");
    }

    [Fact]
    public void Build_PreservesFractionalProjectedEconomicsAndRoundsCashInBaseMinorUnits()
    {
        var expected = Flow("coupon", "fractional-projection", AccountCashFlowStatus.Expected, 1.2345m);
        var snapshot = AccountCashForecastEngine.Build(Request([expected], 100m) with { ClosingCash = null });
        snapshot.Inputs.Flows.Single().Amount.Should().Be(1.2345m);
        snapshot.ForecastLadder.IsDecisionReady.Should().BeTrue();
        snapshot.ForecastLadder.Contributions.Single().Amount.Should().Be(1.23m);
        snapshot.ForecastLadder.Buckets.Last().CumulativeCash.Should().Be(101.23m);
    }

    [Fact]
    public async Task Capture_RestartCorrectionPreservesHistoryAndDeterministicEvidence()
    {
        var store = new FileAccountCashForecastStore(_dataRoot);
        await store.InitializeAsync();
        var service = new AccountCashForecastService(store);
        var expected = Flow("coupon", "predicted", AccountCashFlowStatus.Expected, 25m);
        var actual = Flow("coupon", "observed-v1", AccountCashFlowStatus.Actual, 24m);
        var first = await service.CaptureAsync(Request([expected, actual], 124m));
        var restarted = new AccountCashForecastService(new FileAccountCashForecastStore(_dataRoot));
        var corrected = Flow("coupon", "observed-v2", AccountCashFlowStatus.Actual, 25m) with { SupersedesVersionId = actual.VersionId };
        var secondRequest = Request([expected, corrected], 125m) with
        {
            RunId = "run-2",
            PreviousSnapshotId = first.SnapshotId,
            CorrectionRationale = "Custodian corrected the receipt amount.",
            RecordedAtUtc = Recorded.AddDays(1)
        };
        var second = await restarted.CaptureAsync(secondRequest);
        second.IsCertified.Should().BeTrue();
        second.SnapshotId.Should().NotBe(first.SnapshotId);
        (await restarted.CaptureAsync(secondRequest)).SnapshotId.Should().Be(second.SnapshotId);
        var history = await restarted.GetHistoryAsync(Scope);
        history.Should().HaveCount(2);
        history[0].Inputs.Flows.Single(row => row.Status == AccountCashFlowStatus.Actual).Amount.Should().Be(24m);
        history[1].Inputs.Flows.Single(row => row.Status == AccountCashFlowStatus.Actual).Amount.Should().Be(25m);
        JsonSerializer.Serialize(AccountCashForecastEngine.Build(history[0].Inputs), AccountCashForecastJsonContext.Default.AccountCashForecastSnapshotDto)
            .Should().Be(JsonSerializer.Serialize(first, AccountCashForecastJsonContext.Default.AccountCashForecastSnapshotDto));
        (await restarted.GetAsync(Scope with { AccountId = "account-b" }, first.SnapshotId)).Should().BeNull();
        (await restarted.GetHistoryAsync(Scope with { TenantId = "tenant-b" })).Should().BeEmpty();
        var collision = () => restarted.CaptureAsync(secondRequest with { ClosingCash = secondRequest.ClosingCash! with { Amount = 126m } });
        await collision.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Capture_LaterObservedReceiptSuppressesPriorPredictionInsidePeriod()
    {
        var store = new FileAccountCashForecastStore(_dataRoot);
        await store.InitializeAsync();
        var service = new AccountCashForecastService(store);
        // The old expected currency needs no FX once an independent observed movement supersedes it.
        var expected = Flow("coupon", "predicted", AccountCashFlowStatus.Expected, 25m) with { Currency = "EUR" };
        var first = await service.CaptureAsync(Request([expected], 100m) with { ClosingCash = null });
        var lateSource = Provenance("late-statement", AccountCashEvidenceRole.Observation, Start.AddDays(1));
        var lateActual = Flow("coupon", "late-statement", AccountCashFlowStatus.Actual, 25m) with
        {
            CashReceiptDate = End.AddDays(1),
            Provenance = lateSource with { Evidence = lateSource.Evidence with { ReviewedAtUtc = Recorded.AddHours(1), RetainedAtUtc = Recorded.AddHours(2) } }
        };
        var corrected = await service.CaptureAsync(Request([expected, lateActual], 100m) with
        {
            RunId = "late-run",
            PreviousSnapshotId = first.SnapshotId,
            CorrectionRationale = "Receipt was delayed into the next period.",
            RecordedAtUtc = Recorded.AddHours(3)
        });
        corrected.IsCertified.Should().BeTrue();
        corrected.ForecastLadder.Contributions.Should().BeEmpty();
        corrected.ActualNetBaseFlow.Should().Be(0m);
        (await service.GetHistoryAsync(Scope))[0].Inputs.Flows.Single().CashReceiptDate.Should().Be(Start.AddDays(4));
    }

    [Fact]
    public async Task Capture_FactorRestatementRetainsOriginalPredictionAcrossRestart()
    {
        var store = new FileAccountCashForecastStore(_dataRoot);
        await store.InitializeAsync();
        var security = Guid.NewGuid();
        var position = Guid.NewGuid();
        var firstProvenance = Provenance("factor-v1", AccountCashEvidenceRole.Projection, Start.AddDays(1));
        var factor = new FactorPaydownProjectionRequest(security, position, 1, 1, 1000m, .8m, .7m, "USD",
            Start.AddDays(1), Recorded, firstProvenance.SourceDomain, firstProvenance.SourceEntityId,
            firstProvenance.SourceContentHash, [firstProvenance.Evidence.EvidenceUri]);
        var original = AccountCashForecastProjectionAdapter.FromFactorPaydown(Scope, "factor-payment", factor,
            Start.AddDays(2), Start.AddDays(4), firstProvenance);
        var first = await new AccountCashForecastService(store).CaptureAsync(Request([original], 100m) with { ClosingCash = null });
        var revisedProvenance = Provenance("factor-v2", AccountCashEvidenceRole.Projection, Start.AddDays(1));
        var revised = AccountCashForecastProjectionAdapter.FromFactorPaydown(Scope, "factor-payment", factor with
        {
            CurrentFactor = .6m,
            SourceEntityId = revisedProvenance.SourceEntityId,
            SourceContentHash = revisedProvenance.SourceContentHash,
            EvidenceLinks = [revisedProvenance.Evidence.EvidenceUri]
        }, Start.AddDays(2), Start.AddDays(4), revisedProvenance, original.VersionId);
        var restarted = new AccountCashForecastService(new FileAccountCashForecastStore(_dataRoot));
        await restarted.CaptureAsync(Request([revised], 100m) with
        {
            ClosingCash = null,
            RunId = "factor-restatement",
            PreviousSnapshotId = first.SnapshotId,
            CorrectionRationale = "Servicer restated the factor observation."
        });
        var history = await restarted.GetHistoryAsync(Scope);
        history[0].Inputs.Flows.Single().Amount.Should().Be(100m);
        history[1].Inputs.Flows.Single().Amount.Should().Be(200m);
        history[1].Inputs.Flows.Single().SupersedesVersionId.Should().Be(original.VersionId);
        history[0].Inputs.Flows.Single().ProjectionEngineVersion.Should().Be(FactorPaydownProjectionService.EngineVersion);
        var repeated = await restarted.CaptureAsync(Request([revised], 100m) with
        {
            ClosingCash = null,
            RunId = "factor-unchanged-rerun",
            PreviousSnapshotId = history[1].SnapshotId,
            CorrectionRationale = "Retain the unchanged revised factor prediction on the following run."
        });
        repeated.Inputs.Flows.Single().SupersedesVersionId.Should().Be(original.VersionId);
        (await restarted.GetHistoryAsync(Scope)).Should().HaveCount(3);
    }

    [Fact]
    public void FromObligation_PreservesExistingTermsRunAndSignedHeldQuantity()
    {
        var security = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var source = Provenance("bond-terms", AccountCashEvidenceRole.Projection, Start);
        var flow = new AssetProjectedCashFlowDto(Guid.NewGuid(), runId, security, 1, "Coupon", Start.AddDays(1), 10m, "USD", "Expected",
            SourceDomain: source.SourceDomain, SourceEntityId: source.SourceEntityId);
        var run = new AssetCashFlowProjectionRunDto(runId, security, Start, AssetObligationProjectionService.EngineVersion,
            "Completed", Recorded, source.SourceDomain, source.SourceEntityId);
        var term = new AssetTermsVersionDto(Guid.NewGuid(), security, 1, source.SourceContentHash, Start, Recorded,
            source.SourceDomain, source.SourceEntityId, "Retained bond terms");
        var expected = AccountCashForecastProjectionAdapter.FromObligation(Scope, "bond-coupon", flow, run, term,
            -2m, Start.AddDays(2), Start.AddDays(4), source);
        expected.Amount.Should().Be(-20m);
        expected.ProjectionRunId.Should().Be(runId);
        expected.TermsVersionId.Should().Be(term.TermsVersionId);
        expected.TermsHash.Should().Be(term.TermsHash);
        expected.EconomicDate.Should().Be(flow.DueDate);
        var failedRun = () => AccountCashForecastProjectionAdapter.FromObligation(Scope, "bond-coupon", flow,
            run with { Status = "Failed" }, term, -2m, Start.AddDays(2), Start.AddDays(4), source);
        failedRun.Should().Throw<ArgumentException>();
        // Existing Security Master projections use a version label, rather than SHA256, for terms identity.
        AccountCashForecastProjectionAdapter.FromObligation(Scope, "bond-coupon", flow, run,
            term with { TermsHash = "security-master:1" }, -2m, Start.AddDays(2), Start.AddDays(4), source)
            .TermsHash.Should().Be("security-master:1");
    }

    [Fact]
    public async Task Store_MissingInitializedStateCannotResetHistory()
    {
        var store = new FileAccountCashForecastStore(_dataRoot);
        var uninitialized = () => store.GetHistoryAsync(Scope);
        await uninitialized.Should().ThrowAsync<InvalidOperationException>();
        await store.InitializeAsync();
        await new AccountCashForecastService(store).CaptureAsync(Request([], 100m));
        File.Delete(Path.Combine(_dataRoot, "asset-operations", "account-cash-forecasts", "snapshots.json"));
        var reinitialize = () => new FileAccountCashForecastStore(_dataRoot).InitializeAsync();
        await reinitialize.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Store_ConcurrentIndependentInstancesRetainBothAccounts()
    {
        var store = new FileAccountCashForecastStore(_dataRoot);
        await store.InitializeAsync();
        var first = AccountCashForecastEngine.Build(Request([], 100m));
        var otherScope = Scope with { AccountId = "account-b" };
        var otherRequest = Request([], 100m) with { Scope = otherScope };
        var second = AccountCashForecastEngine.Build(otherRequest with
        {
            OpeningCash = otherRequest.OpeningCash! with { AccountId = otherScope.AccountId },
            ClosingCash = otherRequest.ClosingCash! with { AccountId = otherScope.AccountId }
        });
        await Task.WhenAll(store.AppendAsync(first), new FileAccountCashForecastStore(_dataRoot).AppendAsync(second));
        (await store.GetHistoryAsync(Scope)).Should().ContainSingle();
        (await store.GetHistoryAsync(otherScope)).Should().ContainSingle();
    }

    [Fact]
    public async Task Store_ForeignOrStaleCorrectionPredecessorIsRejected()
    {
        var store = new FileAccountCashForecastStore(_dataRoot);
        await store.InitializeAsync();
        var service = new AccountCashForecastService(store);
        var first = await service.CaptureAsync(Request([], 100m));
        await service.CaptureAsync(Request([], 100m) with { RunId = "second", PreviousSnapshotId = first.SnapshotId, CorrectionRationale = "Refresh source evidence." });
        var stale = () => service.CaptureAsync(Request([], 100m) with { RunId = "third", PreviousSnapshotId = first.SnapshotId, CorrectionRationale = "Stale client correction." });
        await stale.Should().ThrowAsync<InvalidOperationException>();
    }

    private static AccountCashForecastRequestDto Request(IReadOnlyList<AccountCashFlowDto> flows, decimal closing) =>
        new("run-1", Scope, Start, End,
            new(Scope.AccountId, Scope.BookId, Start, 100m, "USD", true, Provenance("opening", AccountCashEvidenceRole.Observation, Start)),
            new(Scope.AccountId, Scope.BookId, End, closing, "USD", true, Provenance("closing", AccountCashEvidenceRole.Observation, End)),
            flows, [], Recorded);

    private static AccountCashFlowDto Flow(string id, string version, AccountCashFlowStatus status, decimal amount) =>
        new(id, version, Scope.AccountId, Scope.BookId, status, "Coupon", amount, "USD",
            Start.AddDays(1), Start.AddDays(2), Start.AddDays(4),
            Provenance(version, status == AccountCashFlowStatus.Actual ? AccountCashEvidenceRole.Observation : AccountCashEvidenceRole.Projection, Start.AddDays(1)));

    private static AccountCashFxConversionDto Fx(string currency, string baseCurrency, DateOnly date, decimal rate) =>
        new(currency, baseCurrency, date, rate, Provenance($"fx-{currency}-{baseCurrency}-{date}", AccountCashEvidenceRole.DeclaredFx, date));

    private static AccountCashProvenanceDto Provenance(string id, AccountCashEvidenceRole role, DateOnly date)
    {
        var source = role switch { AccountCashEvidenceRole.Projection => "AssetOperations", AccountCashEvidenceRole.DeclaredFx => "TreasuryFx", _ => "Custodian" };
        var hash = Sha256Digest.ComputeUtf8(id);
        var evidence = new RetainedEvidenceIdentityDto(id, $"https://evidence.example.test/{id}", hash, source, id,
            "Accepted", "independent-reviewer", Recorded.AddHours(-2), date, 1, Recorded.AddHours(-1), "retaining-operator", "AccountCash", id);
        return new(source, id, hash, role, evidence);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }
}
