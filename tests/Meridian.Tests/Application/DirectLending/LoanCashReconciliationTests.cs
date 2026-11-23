using System.Text.Json;
using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;

namespace Meridian.Tests.Application.DirectLending;

public sealed class LoanCashReconciliationTests
{
    private static readonly Guid LoanId = Guid.Parse("f1000000-0000-0000-0000-000000000001");
    private static readonly DateOnly AsOf = new(2026, 11, 21);
    private static readonly DateTimeOffset ObservedAt = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Match_OnePaymentForTwoIdenticalFlows_ConsumesItOnce()
    {
        var results = Match([Flow(100m), Flow(100m)], [Cash(100m)]);

        results.Count(static result => result.MatchStatus == "Matched").Should().Be(1);
        results.Count(static result => result.MatchStatus == "MissingCash").Should().Be(1);
        results.Where(static result => result.CashTransactionId.HasValue).Select(static result => result.CashTransactionId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Match_UsesCurrencyAndExcludesVoidAndFutureEvidence()
    {
        var results = Match([Flow(100m), Flow(50m) with { DueDate = AsOf.AddDays(1) }],
            [Cash(100m) with { Currency = CurrencyCode.EUR }, Cash(100m) with { IsVoided = true },
             Cash(100m) with { SettlementDate = AsOf.AddDays(1) }]);

        results.Should().HaveCount(2);
        results.Should().ContainSingle(static result => result.MatchStatus == "MissingCash");
        results.Should().ContainSingle(static result => result.MatchStatus == "UnexpectedCash" && result.Currency == "EUR");
    }

    [Fact]
    public void Match_Prd115OutgoingRefundCannotSatisfyAProjectedReceipt()
    {
        var results = Match([Flow(100m)], [Cash(100m) with { TransactionType = "UnappliedCashRefund" }]);
        results.Should().ContainSingle().Which.MatchStatus.Should().Be("MissingCash");
    }

    [Theory]
    [InlineData("UnappliedCash")]
    [InlineData("PrepaidInterest")]
    [InlineData("PrincipalPrepayment")]
    public void Match_Prd115RetainedExcess_IsExplainedWithoutSatisfyingDueFlow(string disposition)
    {
        var cash = Cash(100m) with { TransactionType = "MixedPayment" };
        var results = Match([Flow(100m)], [cash], [Allocation(cash, 100m, disposition, 1)]);
        results.Should().ContainSingle(static row => row.MatchStatus == "MissingCash");
        results.Should().ContainSingle(static row => row.MatchStatus == "Matched" && row.MeasurementKind == "cash-disposition");
        results.Should().NotContain(static row => row.MatchStatus == "UnexpectedCash");
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(150, 50)]
    public void Match_AppliedUnappliedCash_UsesApplicationDateAndPreservesUnconsumedBalance(int receiptAmount, int residual)
    {
        var cash = Cash(receiptAmount) with { TransactionType = "MixedPayment", SettlementDate = AsOf.AddDays(-10) };
        var allocation = Allocation(cash, receiptAmount, "UnappliedCash", 1) with { SourceEventId = cash.SourceEventId };
        var application = HeldMovement(2, 100m, new(100m, 0m, 0m, 0m, 0m));
        var results = Match([Flow(100m) with { FlowType = "Interest" }, Flow(100m) with { FlowType = "Interest" }],
            [cash], [allocation], history: [ReceiptEvent(cash, 1), application, application]);

        var matched = results.Should().ContainSingle(row => row.ProjectedCashFlowId.HasValue && row.MatchStatus == "Matched").Which;
        matched.ActualAmount.Should().Be(100m);
        matched.ActualDate.Should().Be(AsOf, "held funds settle the obligation on their application date");
        matched.CashTransactionId.Should().Be(cash.CashTransactionId);
        matched.PaymentAllocationId.Should().Be(allocation.AllocationId);
        matched.Notes.Should().Contain(note => note.Contains(application.EventId.ToString("D")));
        results.Should().ContainSingle(row => row.MatchStatus == "MissingCash", "the same application cannot satisfy a second obligation");
        results.Where(row => row.MeasurementKind == "cash-disposition").Sum(row => row.ActualAmount).Should().Be(residual);
        results.Sum(row => row.ActualAmount).Should().Be(receiptAmount, "applied funds are removed from the original held slice");
        results.Should().NotContain(row => row.MatchStatus == "UnexpectedCash" || row.MatchStatus == "CashCoverageUnavailable");
    }

    [Fact]
    public void Match_SplitHeldApplications_SettleOneObligationAndConserveOriginalFunding()
    {
        var cash = Cash(150m) with { TransactionType = "MixedPayment", SettlementDate = AsOf.AddDays(-10) };
        var allocation = Allocation(cash, 150m, "UnappliedCash", 1) with { SourceEventId = cash.SourceEventId };
        var first = HeldMovement(2, 40m, new(40m, 0m, 0m, 0m, 0m));
        var second = HeldMovement(3, 60m, new(60m, 0m, 0m, 0m, 0m));
        var results = Match([Flow(100m) with { FlowType = "Interest" }], [cash], [allocation],
            history: [ReceiptEvent(cash, 1), first, second, second]);

        var matched = results.Should().ContainSingle(row => row.ProjectedCashFlowId.HasValue).Which;
        matched.MatchStatus.Should().Be("Matched");
        matched.ActualAmount.Should().Be(100m);
        matched.ActualDate.Should().Be(AsOf);
        matched.CashTransactionId.Should().Be(cash.CashTransactionId);
        matched.PaymentAllocationId.Should().Be(allocation.AllocationId);
        matched.Notes.Should().Contain(note => note.Contains(first.EventId.ToString("D")) && note.Contains("amount 40."));
        matched.Notes.Should().Contain(note => note.Contains(second.EventId.ToString("D")) && note.Contains("amount 60."));
        results.Should().ContainSingle(row => row.MeasurementKind == "cash-disposition" && row.ActualAmount == 50m);
        results.Sum(row => row.ActualAmount).Should().Be(150m);
        LoanCashReconciliation.Exceptions(results, ObservedAt).Should().BeEmpty();
    }

    [Fact]
    public void Match_ExactHeldApplicationTotal_PrecedesLooseIndividualAmountTolerance()
    {
        var cash = Cash(100m) with { TransactionType = "MixedPayment" };
        var allocation = Allocation(cash, 100m, "UnappliedCash", 1) with { SourceEventId = cash.SourceEventId };
        var results = Match([Flow(100m)], [cash], [allocation], new(60m, 0), history:
            [ReceiptEvent(cash, 1), HeldMovement(2, 40m, new(0m, 0m, 0m, 0m, 40m)), HeldMovement(3, 60m, new(0m, 0m, 0m, 0m, 60m))]);

        var matched = results.Should().ContainSingle().Which;
        matched.MatchStatus.Should().Be("Matched");
        matched.ActualAmount.Should().Be(100m);
        matched.VarianceAmount.Should().Be(0m);
        LoanCashReconciliation.Exceptions(results, ObservedAt).Should().BeEmpty();
    }

    [Fact]
    public void Match_SplitHeldApplications_DoesNotAssignFundsBetweenCompetingObligations()
    {
        var cash = Cash(100m) with { TransactionType = "MixedPayment" };
        var allocation = Allocation(cash, 100m, "UnappliedCash", 1) with { SourceEventId = cash.SourceEventId };
        var results = Match([Flow(100m), Flow(100m)], [cash], [allocation], history:
            [ReceiptEvent(cash, 1), HeldMovement(2, 40m, new(0m, 0m, 0m, 0m, 40m)), HeldMovement(3, 60m, new(0m, 0m, 0m, 0m, 60m))]);

        results.Count(row => row.MatchStatus == "MissingCash").Should().Be(2);
        results.Should().NotContain(row => row.MatchStatus == "Matched");
        results.Sum(row => row.ActualAmount).Should().Be(100m);
    }

    [Fact]
    public void Match_SplitHeldApplications_PreservesIndividualExactMatches()
    {
        var cash = Cash(100m) with { TransactionType = "MixedPayment" };
        var allocation = Allocation(cash, 100m, "UnappliedCash", 1) with { SourceEventId = cash.SourceEventId };
        var results = Match([Flow(40m), Flow(60m)], [cash], [allocation], history:
            [ReceiptEvent(cash, 1), HeldMovement(2, 40m, new(0m, 0m, 0m, 0m, 40m)), HeldMovement(3, 60m, new(0m, 0m, 0m, 0m, 60m))]);

        results.Should().HaveCount(2).And.OnlyContain(row => row.MatchStatus == "Matched");
        results.Select(row => row.ActualAmount).Should().BeEquivalentTo(new decimal?[] { 40m, 60m });
        results.Sum(row => row.ActualAmount).Should().Be(100m);
    }

    [Theory]
    [InlineData(1, "MissingCash")]
    [InlineData(2, "Matched")]
    public void Match_SplitHeldApplications_RequiresEveryApplicationWithinDateTolerance(int toleranceDays, string expected)
    {
        var cash = Cash(100m) with { TransactionType = "MixedPayment", SettlementDate = AsOf.AddDays(-10) };
        var allocation = Allocation(cash, 100m, "UnappliedCash", 1) with { SourceEventId = cash.SourceEventId };
        var results = Match([Flow(100m)], [cash], [allocation], new(0m, toleranceDays), history:
            [ReceiptEvent(cash, 1), HeldMovement(2, 40m, new(0m, 0m, 0m, 0m, 40m), AsOf.AddDays(-2)),
             HeldMovement(3, 60m, new(0m, 0m, 0m, 0m, 60m))]);

        results.Single(row => row.ProjectedCashFlowId.HasValue).MatchStatus.Should().Be(expected);
        results.Sum(row => row.ActualAmount).Should().Be(100m);
    }

    [Fact]
    public void Match_SplitHeldApplications_DoesNotChooseAnArbitrarySubset()
    {
        var cash = Cash(150m) with { TransactionType = "MixedPayment" };
        var allocation = Allocation(cash, 150m, "UnappliedCash", 1) with { SourceEventId = cash.SourceEventId };
        var results = Match([Flow(100m)], [cash], [allocation], history:
            [ReceiptEvent(cash, 1), HeldMovement(2, 40m, new(0m, 0m, 0m, 0m, 40m)),
             HeldMovement(3, 60m, new(0m, 0m, 0m, 0m, 60m)), HeldMovement(4, 50m, new(0m, 0m, 0m, 0m, 50m))]);

        results.Should().ContainSingle(row => row.MatchStatus == "MissingCash");
        results.Should().NotContain(row => row.MatchStatus == "Matched");
        results.Sum(row => row.ActualAmount).Should().Be(150m);
    }

    [Fact]
    public void Match_AppliedUnappliedCash_CombinesFundingWithoutInventingOneReceiptIdentity()
    {
        var first = Cash(60m) with { TransactionType = "MixedPayment" };
        var second = Cash(40m) with { TransactionType = "MixedPayment" };
        var allocations = new[]
        {
            Allocation(first, 60m, "UnappliedCash", 1) with { SourceEventId = first.SourceEventId },
            Allocation(second, 40m, "UnappliedCash", 1) with { SourceEventId = second.SourceEventId }
        };
        var application = HeldMovement(3, 100m, new(0m, 0m, 0m, 0m, 100m));
        var results = Match([Flow(100m)], [first, second], allocations,
            history: [ReceiptEvent(first, 1), ReceiptEvent(second, 2), application]);

        var matched = results.Should().ContainSingle().Which;
        matched.MatchStatus.Should().Be("Matched");
        matched.ActualAmount.Should().Be(100m);
        matched.CashTransactionId.Should().BeNull();
        matched.PaymentAllocationId.Should().BeNull();
        foreach (var allocation in allocations)
            matched.Notes.Should().Contain(note => note.Contains(allocation.AllocationId.ToString("D")));
    }

    [Fact]
    public void Match_RefundThenPartialApplication_CannotReuseRefundedFunding()
    {
        var cash = Cash(150m) with { TransactionType = "MixedPayment" };
        var allocation = Allocation(cash, 150m, "UnappliedCash", 1) with { SourceEventId = cash.SourceEventId };
        var refund = HeldMovement(2, 50m, null);
        var application = HeldMovement(3, 100m, new(0m, 0m, 0m, 0m, 100m));
        var results = Match([Flow(100m), Flow(50m)], [cash], [allocation],
            history: [ReceiptEvent(cash, 1), refund, application]);

        results.Should().ContainSingle(row => row.MatchStatus == "Matched" && row.ProjectedCashFlowId.HasValue && row.ActualAmount == 100m);
        results.Should().ContainSingle(row => row.MatchStatus == "MissingCash" && row.ExpectedAmount == 50m);
        results.Should().ContainSingle(row => row.MatchStatus == "Matched" && row.MeasurementKind == "cash-disposition" && row.ActualAmount == 50m);
        results.Sum(row => row.ActualAmount).Should().Be(150m);

        var overdrawn = Match([Flow(150m)], [cash], [allocation],
            history: [ReceiptEvent(cash, 1), refund, HeldMovement(3, 150m, new(0m, 0m, 0m, 0m, 150m))]);
        overdrawn.Should().NotContain(row => row.MatchStatus == "Matched");
        overdrawn.Should().ContainSingle(row => row.MatchStatus == "CashCoverageUnavailable");
    }

    [Theory]
    [InlineData("currency")]
    [InlineData("void")]
    [InlineData("later-receipt")]
    [InlineData("unlinked-allocation")]
    [InlineData("malformed-breakdown")]
    public void Match_AppliedUnappliedCash_RefusesUnsupportedFunding(string fault)
    {
        var cash = Cash(100m) with { TransactionType = "MixedPayment" };
        if (fault == "currency")
            cash = cash with { Currency = CurrencyCode.EUR };
        if (fault == "void")
            cash = cash with { IsVoided = true };
        var allocation = Allocation(cash, 100m, "UnappliedCash", 1) with
        { SourceEventId = fault == "unlinked-allocation" ? Guid.NewGuid() : cash.SourceEventId };
        var application = HeldMovement(2, 100m, new(0m, 0m, 0m, 0m, 100m));
        if (fault == "malformed-breakdown")
            application = application with { PayloadJson = "{}" };
        var results = Match([Flow(100m)], [cash], [allocation],
            history: [ReceiptEvent(cash, fault == "later-receipt" ? 3 : 1), application]);

        results.Should().ContainSingle(row => row.MatchStatus == "MissingCash");
        results.Should().ContainSingle(row => row.MatchStatus == "CashCoverageUnavailable");
        results.Should().NotContain(row => row.MatchStatus == "Matched");
    }

    [Fact]
    public void Match_FutureUnappliedApplication_DoesNotSettleAnObligationYet()
    {
        var cash = Cash(100m) with { TransactionType = "MixedPayment" };
        var allocation = Allocation(cash, 100m, "UnappliedCash", 1) with { SourceEventId = cash.SourceEventId };
        var application = HeldMovement(2, 100m, new(0m, 0m, 0m, 0m, 100m)) with { EffectiveDate = AsOf.AddDays(1) };
        var results = Match([Flow(100m)], [cash], [allocation], history: [ReceiptEvent(cash, 1), application]);

        results.Should().ContainSingle(row => row.MatchStatus == "MissingCash");
        results.Should().ContainSingle(row => row.MatchStatus == "Matched" && row.MeasurementKind == "cash-disposition");
    }

    [Theory]
    [InlineData("Interest", "Interest")]
    [InlineData("InterestAccrued", "Interest")]
    [InlineData("PrincipalOutstanding", "Principal")]
    public void Match_CurrentDurableAndSyntheticAllocationAliases_Agree(string target, string flowType)
    {
        var cash = Cash(100m) with { TransactionType = "MixedPayment" };
        var results = Match([Flow(100m) with { FlowType = flowType }], [cash], [Allocation(cash, 100m, target, 1)]);
        results.Should().ContainSingle().Which.MatchStatus.Should().Be("Matched");
    }

    [Fact]
    public void Match_MixedPayment_ConsumesSeparateRetainedAllocations()
    {
        var cash = Cash(110m) with { TransactionType = "MixedPayment" };
        var principal = Allocation(cash, 100m, "Principal", 1);
        var interest = Allocation(cash, 10m, "Interest", 2);
        var results = Match([Flow(100m), Flow(10m) with { FlowType = "Interest" }], [cash], [principal, interest]);

        results.Should().HaveCount(2).And.OnlyContain(static result => result.MatchStatus == "Matched");
        results.Select(static result => result.PaymentAllocationId).Should().BeEquivalentTo([principal.AllocationId, interest.AllocationId]);
        results.Sum(static result => result.ActualAmount).Should().Be(110m);
    }

    [Fact]
    public void Match_PrincipalPaymentCannotSatisfyAnInterestFlow()
    {
        var results = Match([Flow(100m) with { FlowType = "Interest" }], [Cash(100m)]);
        results.Should().NotContain(static result => result.MatchStatus == "Matched");
    }

    [Fact]
    public void Match_MixedPaymentResidual_RemainsAnUnmatchedObservation()
    {
        var cash = Cash(120m);
        var results = Match([Flow(100m)], [cash], [Allocation(cash, 100m, "Principal", 1)]);

        results.Should().ContainSingle(static result => result.MatchStatus == "Matched");
        results.Should().ContainSingle(static result => result.MatchStatus == "UnexpectedCash" && result.ActualAmount == 20m);
    }

    [Fact]
    public void Match_ToleranceAugmentation_DoesNotLoseAnAvailablePair()
    {
        // 100 can match 101 or 98; 102 can match 101 only. Greedy first-choice matching loses 102.
        var results = Match([Flow(100m) with { FlowSequenceNumber = 1 }, Flow(102m) with { FlowSequenceNumber = 2 }],
            [Cash(101m), Cash(98m)], request: new ReconcileLoanRequest(2m, 0));

        results.Should().HaveCount(2).And.OnlyContain(static result => result.MatchStatus == "Matched");
        results.Select(static result => result.CashTransactionId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Match_ExactMatchIsNotDisplacedByToleranceCandidates()
    {
        var exact = Cash(100m);
        var results = Match([Flow(100m), Flow(101m)], [exact], request: new ReconcileLoanRequest(1m, 0));

        results.Should().ContainSingle(result => result.MatchStatus == "Matched" && result.ExpectedAmount == 100m && result.CashTransactionId == exact.CashTransactionId);
        results.Should().ContainSingle(static result => result.MatchStatus == "MissingCash" && result.ExpectedAmount == 101m);
    }

    [Fact]
    public void Match_AppliesAbsoluteAmountAndDateTolerance()
    {
        var results = Match([Flow(100_000m)], [Cash(100_000.02m) with { SettlementDate = AsOf.AddDays(-1) }],
            request: new ReconcileLoanRequest(0.01m, 1));

        results.Should().NotContain(static result => result.MatchStatus == "Matched");
    }

    [Fact]
    public void Replay_DifferentToleranceCommand_Conflicts()
    {
        var request = new ReconcileLoanRequest(0.01m, 1);
        var retained = new ReconciliationRunDto(Guid.NewGuid(), LoanId, null, ObservedAt, ObservedAt, "Completed")
        { RequestHash = LoanCashReconciliation.RequestHash(request) };
        var same = () => LoanCashReconciliation.EnsureReplay(retained, new ReconcileLoanRequest(0.010m, 1));
        var different = () => LoanCashReconciliation.EnsureReplay(retained, new ReconcileLoanRequest(0.02m, 1));

        same.Should().NotThrow();
        different.Should().Throw<DirectLendingCommandException>().Which.Error.Code.Should().Be(DirectLendingErrorCode.ConcurrencyConflict);
    }

    [Fact]
    public void Normalize_NegativeTolerance_RejectsRequest()
    {
        var act = () => LoanCashReconciliation.Normalize(new ReconcileLoanRequest(-0.01m, 0));
        act.Should().Throw<DirectLendingCommandException>();
    }

    private static IReadOnlyList<ReconciliationResultDto> Match(IReadOnlyList<ProjectedCashFlowDto> flows,
        IReadOnlyList<CashTransactionDto> cash, IReadOnlyList<PaymentAllocationDto>? allocations = null, ReconcileLoanRequest? request = null,
        IReadOnlyList<LoanEventLineageDto>? history = null) =>
        LoanCashReconciliation.Match(Guid.NewGuid(), LoanId, AsOf, ObservedAt, flows, cash, allocations ?? [], request ?? new(0m, 0), history, CurrencyCode.USD);

    private static LoanEventLineageDto ReceiptEvent(CashTransactionDto cash, long version) =>
        new(cash.SourceEventId, version, "loan.mixed-payment-applied", 1, cash.EffectiveDate, ObservedAt, "{}", null, null, null, "synthetic-test", false);

    private static LoanEventLineageDto HeldMovement(long version, decimal amount, PaymentBreakdownDto? breakdown, DateOnly? effective = null) =>
        new(Guid.NewGuid(), version, breakdown is null ? "loan.unapplied-cash-refunded" : "loan.unapplied-cash-applied", 1,
            effective ?? AsOf, ObservedAt, JsonSerializer.Serialize(new
            {
                loanId = LoanId,
                amount,
                effectiveDate = effective ?? AsOf,
                appliedAmount = breakdown?.TotalAllocated,
                breakdown
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)), null, null, null, "synthetic-test", false);

    private static ProjectedCashFlowDto Flow(decimal amount) => new(Guid.NewGuid(), Guid.NewGuid(), LoanId,
        1, "Principal", AsOf, null, null, amount, CurrencyCode.USD, null, null, null, ObservedAt);

    private static CashTransactionDto Cash(decimal amount) => new(Guid.NewGuid(), LoanId, "PrincipalPayment",
        AsOf, AsOf, AsOf, amount, CurrencyCode.USD, null, Guid.NewGuid(), ObservedAt, false);

    private static PaymentAllocationDto Allocation(CashTransactionDto cash, decimal amount, string target, int sequence) =>
        new(Guid.NewGuid(), LoanId, cash.CashTransactionId, sequence, target, target, amount, "synthetic-test", Guid.NewGuid(), ObservedAt);
}
