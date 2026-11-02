using System.Text.Json;
using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.FSharp.DirectLending.Aggregates;

namespace Meridian.Tests.Application.DirectLending;

public sealed class DirectLendingCashAccountingTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateOnly PaymentDate = new(2026, 2, 15);

    [Fact]
    public void LegacyTerms_NullRulePreservesCapturedHashAndJsonRoundTrip()
    {
        var terms = DirectLendingTermsHashCharacterizationTests.FixedBulletTerms();
        var json = JsonSerializer.Serialize(terms, Json);
        json.Should().NotContain("excessPaymentApplication");
        var roundTrip = JsonSerializer.Deserialize<DirectLendingTermsDto>(json, Json)!;
        DirectLendingServiceSupport.ComputeTermsHash(roundTrip)
            .Should().Be(DirectLendingTermsHashCharacterizationTests.FixedBulletHash);
        DirectLendingServiceSupport.ComputeTermsHash(terms with
        {
            ExcessPaymentApplication = ExcessPaymentApplication.HoldUnapplied
        }).Should().NotBe(DirectLendingTermsHashCharacterizationTests.FixedBulletHash);
    }

    [Theory]
    [InlineData(ExcessPaymentApplication.HoldUnapplied)]
    [InlineData(ExcessPaymentApplication.PrincipalPrepayment)]
    [InlineData(ExcessPaymentApplication.PrepaidInterest)]
    public async Task ManualAllocation_CSharpAndFSharpConserveTheEntireReceipt(ExcessPaymentApplication treatment)
    {
        var (service, loanId) = await CreateAsync(PolicyTerms(treatment));
        await service.BookDrawdownAsync(loanId, new(100m, PaymentDate, PaymentDate, "synthetic-draw"));
        await service.AssessFeeAsync(loanId, new("Servicing", 5m, PaymentDate, "synthetic-fee"));
        var before = (await service.GetServicingStateAsync(loanId))!;
        var request = new ApplyMixedPaymentRequest(200m, PaymentDate, new(100m, 100m, 100m, 100m, 10m), "synthetic-receipt");
        var fsharp = DirectLendingAggregateInterop.ApplyMixedPayment(before, PolicyTerms(treatment), request);

        var actual = (await service.ApplyMixedPaymentAsync(loanId, request))!;
        actual.Balances.Should().BeEquivalentTo(fsharp.Servicing.Balances, options => options.WithStrictOrdering().Excluding(info => info.Path.EndsWith("CashTransactionId")));
        actual.Balances.InterestAccruedUnpaid.Should().Be(0m);
        actual.Balances.FeesAccruedUnpaid.Should().Be(0m);
        var allocations = await service.GetPaymentAllocationsAsync(loanId);
        allocations.Sum(item => item.AllocatedAmount).Should().Be(200m);
        allocations.Should().OnlyContain(item => item.AllocatedAmount > 0m && item.TargetReference == loanId.ToString("D") &&
            item.AllocationRule == DirectLendingPaymentResolver.CashAccountingVersion);
        var receipt = (await service.GetCashTransactionsAsync(loanId)).Single();
        allocations.Should().OnlyContain(item => item.CashTransactionId == receipt.CashTransactionId && item.SourceEventId == receipt.SourceEventId);

        var rebuilt = new DirectLendingEventRebuilder().Rebuild(loanId, await service.GetHistoryAsync(loanId));
        rebuilt!.Servicing.Balances.Should().BeEquivalentTo(actual.Balances, options => options.WithStrictOrdering());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProhibitedPrepayment_HoldsCashWithoutReducingPrincipal(bool manual)
    {
        var terms = PolicyTerms(ExcessPaymentApplication.PrincipalPrepayment) with { PrepaymentAllowed = false };
        var (service, loanId) = await CreateAsync(terms);
        await service.BookDrawdownAsync(loanId, new(100m, PaymentDate, PaymentDate, null));
        var after = await service.ApplyMixedPaymentAsync(loanId,
            new(120m, PaymentDate, manual ? new(0m, 0m, 0m, 0m, 0m) : null, null));
        after!.Balances.PrincipalOutstanding.Should().Be(100m);
        after.Balances.UnappliedCash.Should().Be(120m);
        var principalOnly = () => service.ApplyPrincipalPaymentAsync(loanId, new(1m, PaymentDate, null));
        (await principalOnly.Should().ThrowAsync<DirectLendingCommandException>()).Which.Error.Code.Should().Be(DirectLendingErrorCode.Validation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterestOnly_OnlyExplicitPrepaymentElectionSweepsResidual(bool explicitPrepayment)
    {
        var terms = DirectLendingTermsHashCharacterizationTests.FixedBulletTerms() with
        {
            InterestOnlyMonths = 12,
            ExcessPaymentApplication = explicitPrepayment ? ExcessPaymentApplication.PrincipalPrepayment : null
        };
        var (service, loanId) = await CreateAsync(terms);
        await service.BookDrawdownAsync(loanId, new(100m, PaymentDate, PaymentDate, null));
        var after = (await service.ApplyMixedPaymentAsync(loanId, new(125m, PaymentDate, null, null)))!;
        after.Balances.PrincipalOutstanding.Should().Be(explicitPrepayment ? 0m : 100m);
        after.Balances.UnappliedCash.Should().Be(explicitPrepayment ? 25m : 125m);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BasePrincipalAllocation_PreservesExistingWaterfallWhenExcessPrepaymentIsProhibited(bool manual)
    {
        var terms = PolicyTerms(ExcessPaymentApplication.PrincipalPrepayment) with
        {
            InterestOnlyMonths = 0,
            PrepaymentAllowed = false
        };
        var (service, loanId) = await CreateAsync(terms);
        await service.BookDrawdownAsync(loanId, new(100m, PaymentDate, PaymentDate, null));
        var result = (await service.ApplyMixedPaymentAsync(loanId,
            new(120m, PaymentDate, manual ? new(0m, 0m, 0m, 0m, 60m) : null, null)))!;

        result.Balances.PrincipalOutstanding.Should().Be(manual ? 40m : 0m);
        result.Balances.UnappliedCash.Should().Be(manual ? 60m : 20m);
        var receipt = (await service.GetHistoryAsync(loanId)).Last();
        using var payload = JsonDocument.Parse(receipt.PayloadJson);
        var resolution = payload.RootElement.GetProperty("resolution").Deserialize<MixedPaymentResolutionDto>(Json)!;
        resolution.ExcessToPrincipal.Should().Be(0m);
        resolution.ExcessRuleNote.Should().Contain("not allowed");
    }

    [Fact]
    public async Task ExcessPrincipalPenalty_UsesOnlyExcessBasisAndRetainsOneCashReceiptAcrossRetryAndReplay()
    {
        var terms = PolicyTerms(ExcessPaymentApplication.PrincipalPrepayment) with { PrepaymentPenaltyRate = 0.05m };
        var (service, loanId) = await CreateAsync(terms);
        await service.BookDrawdownAsync(loanId, new(100m, PaymentDate, PaymentDate, null));
        var before = (await service.GetServicingStateAsync(loanId))!;
        var request = new ApplyMixedPaymentRequest(70.10m, PaymentDate, new(0m, 0m, 0m, 0m, 20m), "excess-only-penalty");
        var metadata = new DirectLendingCommandMetadataDto(Guid.NewGuid(), null, Guid.NewGuid(), "synthetic", false);
        var fsharp = DirectLendingAggregateInterop.ApplyMixedPayment(before, terms, request);

        var result = (await service.ApplyMixedPaymentAsync(loanId, request, metadata))!;
        result.Balances.PrincipalOutstanding.Should().Be(29.90m);
        result.Balances.PenaltyAccruedUnpaid.Should().Be(2.51m);
        result.Balances.Should().BeEquivalentTo(fsharp.Servicing.Balances);
        result.ServicingRevision.Should().Be(before.ServicingRevision + 2);
        var history = await service.GetHistoryAsync(loanId);
        var receipt = history[^2];
        var penalty = history[^1];
        receipt.EventType.Should().Be("loan.mixed-payment-applied");
        receipt.CommandId.Should().Be(metadata.CommandId);
        penalty.EventType.Should().Be("loan.prepayment-penalty-charged");
        penalty.CommandId.Should().BeNull();
        penalty.CausationId.Should().Be(receipt.EventId);
        penalty.CorrelationId.Should().Be(receipt.CorrelationId);
        using var payload = JsonDocument.Parse(receipt.PayloadJson);
        payload.RootElement.GetProperty("automaticPrepaymentPenaltyAmount").GetDecimal().Should().Be(2.51m);
        payload.RootElement.GetProperty("prepaymentPenaltyEventId").GetGuid().Should().Be(penalty.EventId);
        var allocations = await service.GetPaymentAllocationsAsync(loanId);
        allocations.Sum(item => item.AllocatedAmount).Should().Be(70.10m);
        allocations.Should().OnlyContain(item => item.SourceEventId == receipt.EventId && item.TargetType == "Principal");
        var retry = (await service.ApplyMixedPaymentAsync(loanId, request, metadata))!;
        retry.Balances.Should().BeEquivalentTo(result.Balances);
        (await service.GetHistoryAsync(loanId)).Should().HaveCount(history.Count);
        (await service.GetCashTransactionsAsync(loanId)).Should().ContainSingle();
        var replay = new DirectLendingEventRebuilder().Rebuild(loanId, history)!;
        replay.Servicing.Balances.Should().BeEquivalentTo(result.Balances);
        replay.Servicing.ServicingRevision.Should().Be(result.ServicingRevision);
    }

    [Fact]
    public async Task BackdatedReceiptAcrossChangedRouting_RequiresReviewedEffectiveTermsButOriginalRetrySucceeds()
    {
        var terms = PolicyTerms(ExcessPaymentApplication.HoldUnapplied);
        var (service, loanId) = await CreateAsync(terms);
        var request = new ApplyMixedPaymentRequest(20m, PaymentDate, null, "original-receipt");
        var metadata = new DirectLendingCommandMetadataDto(Guid.NewGuid(), null, null, "synthetic", false);
        await service.ApplyMixedPaymentAsync(loanId, request, metadata);
        await service.AmendTermsAsync(loanId, new(terms with
        {
            ExcessPaymentApplication = ExcessPaymentApplication.PrepaidInterest
        }, "Prospective excess routing"));
        var count = (await service.GetHistoryAsync(loanId)).Count;
        var ambiguous = () => service.ApplyMixedPaymentAsync(loanId, new(10m, PaymentDate.AddDays(1), null, null));
        await ambiguous.Should().ThrowAsync<DirectLendingCommandException>().WithMessage("*contractual effective-date evidence*");
        var retry = (await service.ApplyMixedPaymentAsync(loanId, request, metadata))!;
        retry.Balances.UnappliedCash.Should().Be(20m);
        retry.Balances.PrepaidInterest.Should().Be(0m);
        (await service.GetHistoryAsync(loanId)).Should().HaveCount(count);
        (await service.GetCashTransactionsAsync(loanId)).Should().ContainSingle();
    }

    [Fact]
    public async Task PrepaidInterest_ReceiptRuleSurvivesTermsAmendmentAndSettlesLaterAccrual()
    {
        var terms = PolicyTerms(ExcessPaymentApplication.PrepaidInterest);
        var (service, loanId) = await CreateAsync(terms);
        await service.BookDrawdownAsync(loanId, new(1_000m, PaymentDate, PaymentDate, null));
        var receipt = (await service.ApplyMixedPaymentAsync(loanId, new(40m, PaymentDate, null, null)))!;
        receipt.Balances.PrepaidInterest.Should().Be(40m);
        receipt.Balances.InterestAccruedUnpaid.Should().Be(0m);
        await service.AmendTermsAsync(loanId, new(PolicyTerms(ExcessPaymentApplication.HoldUnapplied), "Synthetic amendment for future receipts"));
        var accrual = (await service.PostDailyAccrualAsync(loanId, new(PaymentDate.AddDays(1))))!;
        var after = (await service.GetServicingStateAsync(loanId))!;
        after.Balances.PrepaidInterest.Should().Be(40m - accrual.InterestAmount);
        after.Balances.InterestAccruedUnpaid.Should().Be(0m);
        var rebuilt = new DirectLendingEventRebuilder().Rebuild(loanId, await service.GetHistoryAsync(loanId));
        rebuilt!.Servicing.Balances.Should().BeEquivalentTo(after.Balances, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task PrepaidElectionAtMaturity_RetainsApprovedResidualWithoutRecognizingIncome()
    {
        var terms = PolicyTerms(ExcessPaymentApplication.PrepaidInterest);
        var (service, loanId) = await CreateAsync(terms);
        await service.BookDrawdownAsync(loanId, new(100m, PaymentDate, PaymentDate, null));
        var after = (await service.ApplyMixedPaymentAsync(loanId, new(125m, terms.MaturityDate, null, null)))!;
        after.Balances.PrincipalOutstanding.Should().Be(0m);
        after.Balances.UnappliedCash.Should().Be(0m);
        after.Balances.PrepaidInterest.Should().Be(25m);
    }

    [Fact]
    public async Task PrepaidInterest_ReleasesOnlySubsequentEarnedInterestAndSurvivesAmendmentAndRetry()
    {
        var terms = PolicyTerms(ExcessPaymentApplication.PrepaidInterest) with
        {
            FixedAnnualRate = 0.36m,
            CommitmentFeeRate = null
        };
        var (service, loanId) = await CreateAsync(terms);
        await service.BookDrawdownAsync(loanId, new(1_000m, PaymentDate, PaymentDate, null));
        await service.ApplyMixedPaymentAsync(loanId, new(2.50m, PaymentDate, null, null));
        var sameDay = (await service.PostDailyAccrualAsync(loanId, new(PaymentDate)))!;
        sameDay.InterestAmount.Should().Be(1m);
        sameDay.PrepaidInterestConsumed.Should().Be(0m, "a receipt cannot settle earlier or same-day accrual under this election");

        var metadata = new DirectLendingCommandMetadataDto(Guid.NewGuid(), null, null, "synthetic", false);
        var nextDay = (await service.PostDailyAccrualAsync(loanId, new(PaymentDate.AddDays(1)), metadata))!;
        nextDay.InterestAmount.Should().Be(1m);
        nextDay.PrepaidInterestConsumed.Should().Be(1m);
        nextDay.PrepaidInterestReleases!.Sum(item => item.Amount).Should().Be(1m);
        (await service.GetServicingStateAsync(loanId))!.Balances.InterestAccruedUnpaid.Should().Be(1m,
            "the prior unpaid day is not a subsequent accrual allocation");

        await service.AmendTermsAsync(loanId, new(terms with
        {
            ExcessPaymentApplication = ExcessPaymentApplication.HoldUnapplied
        }, "Prospective cash policy amendment"));
        await service.PostDailyAccrualAsync(loanId, new(PaymentDate.AddDays(2)));
        var last = (await service.PostDailyAccrualAsync(loanId, new(PaymentDate.AddDays(3))))!;
        last.PrepaidInterestConsumed.Should().Be(0.5m, "release is capped at remaining receipt credit");
        var beforeRetry = (await service.GetServicingStateAsync(loanId))!;
        beforeRetry.Balances.PrepaidInterest.Should().Be(0m);
        beforeRetry.Balances.InterestAccruedUnpaid.Should().Be(1.5m);
        beforeRetry.Balances.PrepaidInterestLots!.Single().RemainingAmount.Should().Be(0m);
        beforeRetry.Balances.PrepaidInterestLots!.Single().TermsHash.Should().Be(DirectLendingServiceSupport.ComputeTermsHash(terms));

        var repeated = await service.PostDailyAccrualAsync(loanId, new(PaymentDate.AddDays(1)), metadata);
        repeated.Should().BeEquivalentTo(nextDay);
        (await service.GetServicingStateAsync(loanId))!.Balances.Should().BeEquivalentTo(beforeRetry.Balances);
        (await service.GetCashTransactionsAsync(loanId)).Should().HaveCount(1, "accrual release is not another cash receipt");
        var replay = new DirectLendingEventRebuilder().Rebuild(loanId, await service.GetHistoryAsync(loanId));
        replay!.Servicing.Balances.Should().BeEquivalentTo(beforeRetry.Balances, options => options.WithStrictOrdering());
        var reconciliation = (await service.ReconcileAsync(loanId))!;
        (await service.GetReconciliationResultsAsync(reconciliation.ReconciliationRunId))
            .Single(item => item.MatchRule == "RetainedCashAllocation").Notes
            .Should().Contain(note => note.Contains("remaining prepaid credit: 0.00") || note.Contains("remaining prepaid credit: 0."));
    }

    [Fact]
    public async Task HoldReceipt_CannotAcquireReleasePermissionFromLaterAmendment()
    {
        var terms = PolicyTerms(ExcessPaymentApplication.HoldUnapplied) with { FixedAnnualRate = 0.36m, CommitmentFeeRate = null };
        var (service, loanId) = await CreateAsync(terms);
        await service.BookDrawdownAsync(loanId, new(1_000m, PaymentDate, PaymentDate, null));
        await service.ApplyMixedPaymentAsync(loanId, new(5m, PaymentDate, null, null));
        await service.AmendTermsAsync(loanId, new(terms with
        {
            ExcessPaymentApplication = ExcessPaymentApplication.PrepaidInterest
        }, "Future receipts may settle future cash interest"));
        var entry = (await service.PostDailyAccrualAsync(loanId, new(PaymentDate.AddDays(1))))!;
        entry.PrepaidInterestConsumed.Should().Be(0m);
        (await service.GetServicingStateAsync(loanId))!.Balances.UnappliedCash.Should().Be(5m);
        (await service.GetServicingStateAsync(loanId))!.Balances.PrepaidInterest.Should().Be(0m);
    }

    [Fact]
    public async Task RetryAfterAnotherPaymentAndAmendment_ReturnsCommittedStateWithoutNewCash()
    {
        var (service, loanId) = await CreateAsync(PolicyTerms(ExcessPaymentApplication.HoldUnapplied));
        var metadata = new DirectLendingCommandMetadataDto(Guid.NewGuid(), null, null, "synthetic", false);
        var request = new ApplyMixedPaymentRequest(20m, PaymentDate, null, "retained-command");
        await service.ApplyMixedPaymentAsync(loanId, request, metadata);
        await service.AmendTermsAsync(loanId, new(PolicyTerms(ExcessPaymentApplication.PrepaidInterest), "Synthetic amendment"));
        await service.ApplyMixedPaymentAsync(loanId, new(30m, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), null, "other-receipt"));
        var before = (await service.GetServicingStateAsync(loanId))!;
        var historyCount = (await service.GetHistoryAsync(loanId)).Count;

        var retry = await service.ApplyMixedPaymentAsync(loanId, request, metadata);
        retry!.Balances.Should().BeEquivalentTo(before.Balances, options => options.WithStrictOrdering());
        (await service.GetCashTransactionsAsync(loanId)).Should().HaveCount(2);
        (await service.GetHistoryAsync(loanId)).Should().HaveCount(historyCount);
        var changed = () => service.ApplyMixedPaymentAsync(loanId, request with { ExternalRef = "different" }, metadata);
        (await changed.Should().ThrowAsync<DirectLendingCommandException>()).Which.Error.Code.Should().Be(DirectLendingErrorCode.Conflict);
    }

    [Fact]
    public async Task PrincipalOnlyOverpayment_IsRejectedWithoutDiscardingReceiptAmount()
    {
        var (service, loanId) = await CreateAsync(PolicyTerms(ExcessPaymentApplication.HoldUnapplied));
        await service.BookDrawdownAsync(loanId, new(10m, PaymentDate, PaymentDate, null));
        var historyCount = (await service.GetHistoryAsync(loanId)).Count;
        var command = () => service.ApplyPrincipalPaymentAsync(loanId, new(11m, PaymentDate, null));
        await command.Should().ThrowAsync<DirectLendingCommandException>().WithMessage("*mixed-payment*");
        (await service.GetHistoryAsync(loanId)).Should().HaveCount(historyCount);
        (await service.GetServicingStateAsync(loanId))!.Balances.PrincipalOutstanding.Should().Be(10m);
    }

    [Fact]
    public async Task PrincipalOnlyRetry_DoesNotReducePrincipalTwice()
    {
        var (service, loanId) = await CreateAsync(PolicyTerms(ExcessPaymentApplication.HoldUnapplied));
        await service.BookDrawdownAsync(loanId, new(100m, PaymentDate, PaymentDate, null));
        var metadata = new DirectLendingCommandMetadataDto(Guid.NewGuid(), null, null, "synthetic", false);
        var request = new ApplyPrincipalPaymentRequest(25m, PaymentDate, "same-receipt");
        await service.ApplyPrincipalPaymentAsync(loanId, request, metadata);
        var retry = (await service.ApplyPrincipalPaymentAsync(loanId, request, metadata))!;
        retry.Balances.PrincipalOutstanding.Should().Be(75m);
        (await service.GetHistoryAsync(loanId)).Count(item => item.EventType == "loan.principal-payment-applied").Should().Be(1);
    }

    [Theory]
    [InlineData(ExcessPaymentApplication.HoldUnapplied)]
    [InlineData(ExcessPaymentApplication.PrepaidInterest)]
    public async Task CurrencyAmendment_CannotRelabelHeldCash(ExcessPaymentApplication treatment)
    {
        var terms = PolicyTerms(treatment);
        var (service, loanId) = await CreateAsync(terms);
        await service.ApplyMixedPaymentAsync(loanId, new(10m, PaymentDate, null, null));
        var amendment = () => service.AmendTermsAsync(loanId, new(terms with { BaseCurrency = CurrencyCode.EUR }, "Unsupported redenomination"));
        await amendment.Should().ThrowAsync<DirectLendingCommandException>().WithMessage("*currency cannot change*");
        (await service.GetLoanAsync(loanId))!.CurrentTerms.BaseCurrency.Should().Be(CurrencyCode.USD);
    }

    [Fact]
    public async Task Reports_ExposeHeldBalancesByCurrencyWithoutAddingUnlikeAmounts()
    {
        var service = new InMemoryDirectLendingService();
        var usd = await AddLoanAsync(service, PolicyTerms(ExcessPaymentApplication.HoldUnapplied));
        var eur = await AddLoanAsync(service, PolicyTerms(ExcessPaymentApplication.PrepaidInterest) with { BaseCurrency = CurrencyCode.EUR });
        await service.ApplyMixedPaymentAsync(usd, new(12m, PaymentDate, null, null));
        await service.ApplyMixedPaymentAsync(eur, new(34m, PaymentDate, null, null));
        var summary = await service.GetPortfolioSummaryAsync();
        summary.TotalUnappliedCash.Should().BeNull();
        summary.TotalPrepaidInterest.Should().BeNull();
        summary.CurrencyTotals!.Single(item => item.Currency == CurrencyCode.USD).TotalUnappliedCash.Should().Be(12m);
        summary.CurrencyTotals!.Single(item => item.Currency == CurrencyCode.EUR).TotalPrepaidInterest.Should().Be(34m);
        var run = (await service.ReconcileAsync(usd))!;
        var evidence = (await service.GetReconciliationResultsAsync(run.ReconciliationRunId))
            .Single(item => item.MatchRule == "RetainedCashAllocation");
        evidence.MatchStatus.Should().Be("UnappliedCashHeld");
        evidence.VarianceAmount.Should().Be(0m);
        evidence.Notes.Should().Contain(item => item.Contains("Source event:"));
    }

    [Fact]
    public async Task InvalidManualAmounts_FailBeforeStateMutation()
    {
        var (service, loanId) = await CreateAsync(PolicyTerms(ExcessPaymentApplication.HoldUnapplied));
        var count = (await service.GetHistoryAsync(loanId)).Count;
        var command = () => service.ApplyMixedPaymentAsync(loanId, new(10m, PaymentDate, new(-1m, 0m, 0m, 0m, 0m), null));
        await command.Should().ThrowAsync<DirectLendingCommandException>();
        (await service.GetHistoryAsync(loanId)).Should().HaveCount(count);
        (await service.GetCashTransactionsAsync(loanId)).Should().BeEmpty();
    }

    [Fact]
    public async Task ReceiptPrecisionBeyondLedgerContract_IsRejectedWithoutRounding()
    {
        var (service, loanId) = await CreateAsync(PolicyTerms(ExcessPaymentApplication.HoldUnapplied));
        var command = () => service.ApplyMixedPaymentAsync(loanId, new(0.00000000001m, PaymentDate, null, null));
        await command.Should().ThrowAsync<DirectLendingCommandException>().WithMessage("*numeric(38,10)*");
        (await service.GetCashTransactionsAsync(loanId)).Should().BeEmpty();
    }

    [Fact]
    public void Reconciliation_HistoricalUnallocatedReceiptIsDifferenceNotAnInferredLiability()
    {
        var loanId = Guid.NewGuid();
        var cash = new CashTransactionDto(Guid.NewGuid(), loanId, "MixedPayment", PaymentDate, PaymentDate,
            PaymentDate, 10m, CurrencyCode.USD, null, Guid.NewGuid(), DateTimeOffset.UtcNow, false);
        var evidence = DirectLendingCashReconciliation.Build(loanId, Guid.NewGuid(), [cash], []);
        evidence.Single().MatchStatus.Should().Be("CashAllocationDifference");
        evidence.Single().VarianceAmount.Should().Be(10m);
    }

    [Theory]
    [InlineData("ForeignSource")]
    [InlineData("ForeignLoan")]
    [InlineData("UnknownTarget")]
    public void Reconciliation_NumericEqualityCannotCertifyInvalidAllocationEvidence(string corruption)
    {
        var loanId = Guid.NewGuid();
        var cash = new CashTransactionDto(Guid.NewGuid(), loanId, "MixedPayment", PaymentDate, PaymentDate,
            PaymentDate, 10m, CurrencyCode.USD, null, Guid.NewGuid(), DateTimeOffset.UtcNow, false);
        var allocation = new PaymentAllocationDto(Guid.NewGuid(), corruption == "ForeignLoan" ? Guid.NewGuid() : loanId,
            cash.CashTransactionId, 1, corruption == "UnknownTarget" ? "Unsupported" : "UnappliedCash", loanId.ToString("D"),
            10m, DirectLendingPaymentResolver.CashAccountingVersion,
            corruption == "ForeignSource" ? Guid.NewGuid() : cash.SourceEventId, DateTimeOffset.UtcNow);
        DirectLendingCashReconciliation.Build(loanId, Guid.NewGuid(), [cash], [allocation])
            .Single().MatchStatus.Should().Be("CashAllocationEvidenceInvalid");
    }

    [Theory]
    [InlineData("EmptyReceipt")]
    [InlineData("UnknownReceipt")]
    [InlineData("MissingReleases")]
    public void Reconciliation_UnattributedReleaseRemainsAnExplicitEvidenceException(string corruption)
    {
        var loanId = Guid.NewGuid();
        var cash = new CashTransactionDto(Guid.NewGuid(), loanId, "MixedPayment", PaymentDate, PaymentDate,
            PaymentDate, 100m, CurrencyCode.USD, null, Guid.NewGuid(), DateTimeOffset.UtcNow, false);
        var allocation = new PaymentAllocationDto(Guid.NewGuid(), loanId, cash.CashTransactionId, 1,
            "PrepaidInterest", loanId.ToString("D"), 100m, DirectLendingPaymentResolver.CashAccountingVersion,
            cash.SourceEventId, DateTimeOffset.UtcNow);
        IReadOnlyList<PrepaidInterestReleaseDto> releases = corruption == "MissingReleases" ? []
            : [new(corruption == "EmptyReceipt" ? Guid.Empty : Guid.NewGuid(), 10m)];
        var accrual = new DailyAccrualEntryDto(Guid.NewGuid(), PaymentDate.AddDays(1), 10m, 0m, 0m,
            0.1m, DateTimeOffset.UtcNow, 0m, 10m, releases);

        var results = DirectLendingCashReconciliation.Build(loanId, Guid.NewGuid(), [cash], [allocation], [accrual]);

        results.Single(item => item.MatchRule == "RetainedCashAllocation").MatchStatus.Should().Be("CashAllocationEvidenceInvalid");
        var orphan = results.Single(item => item.MatchRule == "RetainedPrepaidReleaseEvidence");
        orphan.MatchStatus.Should().Be("CashAllocationEvidenceInvalid");
        orphan.CashTransactionId.Should().BeNull();
        orphan.VarianceAmount.Should().Be(-10m);
        orphan.Notes.Should().Contain(item => item.Contains(accrual.AccrualEntryId.ToString("D")));
    }

    [Fact]
    public void Reconciliation_OrphanedReleaseIsReportedEvenWithoutAnySuppliedCashReceipts()
    {
        var accrual = new DailyAccrualEntryDto(Guid.NewGuid(), PaymentDate.AddDays(1), 10m, 0m, 0m,
            0.1m, DateTimeOffset.UtcNow, 0m, 10m, [new(Guid.NewGuid(), 10m)]);
        var evidence = DirectLendingCashReconciliation.Build(Guid.NewGuid(), Guid.NewGuid(), [], [], [accrual]);
        evidence.Single().MatchStatus.Should().Be("CashAllocationEvidenceInvalid");
        evidence.Single().MatchRule.Should().Be("RetainedPrepaidReleaseEvidence");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reconciliation_MismatchedOrDuplicateKnownReceiptReleasesCannotCertifyCash(bool duplicate)
    {
        var loanId = Guid.NewGuid();
        var cash = new CashTransactionDto(Guid.NewGuid(), loanId, "MixedPayment", PaymentDate, PaymentDate,
            PaymentDate, 100m, CurrencyCode.USD, null, Guid.NewGuid(), DateTimeOffset.UtcNow, false);
        var allocation = new PaymentAllocationDto(Guid.NewGuid(), loanId, cash.CashTransactionId, 1,
            "PrepaidInterest", loanId.ToString("D"), 100m, DirectLendingPaymentResolver.CashAccountingVersion,
            cash.SourceEventId, DateTimeOffset.UtcNow);
        IReadOnlyList<PrepaidInterestReleaseDto> releases = duplicate
            ? [new(cash.CashTransactionId, 5m), new(cash.CashTransactionId, 5m)]
            : [new(cash.CashTransactionId, 9m)];
        var accrual = new DailyAccrualEntryDto(Guid.NewGuid(), PaymentDate.AddDays(1), 10m, 0m, 0m,
            0.1m, DateTimeOffset.UtcNow, 0m, 10m, releases);

        DirectLendingCashReconciliation.Build(loanId, Guid.NewGuid(), [cash], [allocation], [accrual])
            .Single().MatchStatus.Should().Be("CashAllocationEvidenceInvalid");
    }

    private static DirectLendingTermsDto PolicyTerms(ExcessPaymentApplication treatment) =>
        DirectLendingTermsHashCharacterizationTests.FixedBulletTerms() with
        {
            ExcessPaymentApplication = treatment,
            InterestOnlyMonths = 12
        };

    private static async Task<(InMemoryDirectLendingService Service, Guid LoanId)> CreateAsync(DirectLendingTermsDto terms)
    {
        var service = new InMemoryDirectLendingService();
        return (service, await AddLoanAsync(service, terms));
    }

    private static async Task<Guid> AddLoanAsync(InMemoryDirectLendingService service, DirectLendingTermsDto terms)
    {
        var loan = await service.CreateLoanAsync(new(null, "Synthetic cash facility", new(Guid.NewGuid(), "Synthetic borrower", null), terms.OriginationDate, terms));
        await service.ActivateLoanAsync(loan.LoanId, new(terms.OriginationDate));
        return loan.LoanId;
    }
}
