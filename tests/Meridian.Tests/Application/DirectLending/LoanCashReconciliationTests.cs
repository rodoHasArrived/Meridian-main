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
        IReadOnlyList<CashTransactionDto> cash, IReadOnlyList<PaymentAllocationDto>? allocations = null, ReconcileLoanRequest? request = null) =>
        LoanCashReconciliation.Match(Guid.NewGuid(), LoanId, AsOf, ObservedAt, flows, cash, allocations ?? [], request ?? new(0m, 0));

    private static ProjectedCashFlowDto Flow(decimal amount) => new(Guid.NewGuid(), Guid.NewGuid(), LoanId,
        1, "Principal", AsOf, null, null, amount, CurrencyCode.USD, null, null, null, ObservedAt);

    private static CashTransactionDto Cash(decimal amount) => new(Guid.NewGuid(), LoanId, "PrincipalPayment",
        AsOf, AsOf, AsOf, amount, CurrencyCode.USD, null, Guid.NewGuid(), ObservedAt, false);

    private static PaymentAllocationDto Allocation(CashTransactionDto cash, decimal amount, string target, int sequence) =>
        new(Guid.NewGuid(), LoanId, cash.CashTransactionId, sequence, target, target, amount, "synthetic-test", Guid.NewGuid(), ObservedAt);
}
