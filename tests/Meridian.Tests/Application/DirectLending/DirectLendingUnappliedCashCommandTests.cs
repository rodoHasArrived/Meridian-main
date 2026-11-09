using System.Text.Json;
using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.FSharp.DirectLending.Aggregates;

namespace Meridian.Tests.Application.DirectLending;

/// <summary>
/// PRD-115 increment 7: ApplyUnappliedCash and RefundUnappliedCash in the in-memory lane, checked
/// against the F# aggregate (Postgres lane), replayed by the rebuilder, and journaled by the shared
/// <see cref="DirectLendingJournalLines"/>.
/// </summary>
public sealed class DirectLendingUnappliedCashCommandTests
{
    private static readonly DateOnly Origination = new(2026, 1, 15);
    private static readonly DateOnly PaymentDate = new(2026, 4, 1);

    [Fact]
    public async Task ApplyThenRefund_MovesHeldCash_MatchesTheFSharpAggregate_AndReplays()
    {
        var terms = Terms();
        var service = new InMemoryDirectLendingService();
        var loanId = await LoanHoldingUnappliedCashAsync(service, terms, 2_000m);
        await service.PostDailyAccrualAsync(loanId, new PostDailyAccrualRequest(PaymentDate.AddDays(-1)));
        var before = (await service.GetServicingStateAsync(loanId))!;
        var owedInterest = before.Balances.InterestAccruedUnpaid;
        owedInterest.Should().BeGreaterThan(0m);

        var applied = (await service.ApplyUnappliedCashAsync(loanId, new ApplyUnappliedCashRequest(1_500m, PaymentDate, null, "apply")))!;
        var fsharp = DirectLendingAggregateInterop.ApplyUnappliedCash(before, terms, new ApplyUnappliedCashRequest(1_500m, PaymentDate, null, "apply"));

        // Interest-only period: only the accrued interest is owed, so the rest stays held.
        applied.Balances.InterestAccruedUnpaid.Should().Be(0m);
        applied.Balances.UnappliedCash.Should().Be(2_000m - owedInterest);
        applied.Balances.Should().Be(fsharp.Servicing.Balances, "both lanes apply held cash identically");

        var refunded = (await service.RefundUnappliedCashAsync(loanId, new RefundUnappliedCashRequest(applied.Balances.UnappliedCash, PaymentDate, "wire-out-1")))!;
        refunded.Balances.UnappliedCash.Should().Be(0m);
        (await service.GetCashTransactionsAsync(loanId))
            .Should().ContainSingle(item => item.TransactionType == "UnappliedCashRefund" && item.Amount == applied.Balances.UnappliedCash && item.ExternalRef == "wire-out-1");

        var rebuilt = new DirectLendingEventRebuilder().Rebuild(loanId, await service.GetHistoryAsync(loanId));
        rebuilt!.Servicing.Balances.Should().Be(refunded.Balances);
    }

    [Theory]
    [InlineData(0, "apply")]
    [InlineData(2_000.01, "apply")]
    [InlineData(2_000.01, "refund")]
    public async Task Commands_RefuseAmountsThatAreNotPositiveOrExceedTheHeldCash(decimal amount, string action)
    {
        var service = new InMemoryDirectLendingService();
        var loanId = await LoanHoldingUnappliedCashAsync(service, Terms(), 2_000m);

        Func<Task<LoanServicingStateDto?>> act = action == "apply"
            ? () => service.ApplyUnappliedCashAsync(loanId, new ApplyUnappliedCashRequest(amount, PaymentDate, null, "x"))
            : () => service.RefundUnappliedCashAsync(loanId, new RefundUnappliedCashRequest(amount, PaymentDate, "x"));

        (await act.Should().ThrowAsync<DirectLendingCommandException>())
            .Which.Error.Code.Should().Be(DirectLendingErrorCode.Validation);
        (await service.GetServicingStateAsync(loanId))!.Balances.UnappliedCash.Should().Be(2_000m);
    }

    [Fact]
    public async Task Refund_WithoutAnExternalReference_IsRefused()
    {
        var service = new InMemoryDirectLendingService();
        var loanId = await LoanHoldingUnappliedCashAsync(service, Terms(), 2_000m);

        var act = () => service.RefundUnappliedCashAsync(loanId, new RefundUnappliedCashRequest(100m, PaymentDate, " "));

        (await act.Should().ThrowAsync<DirectLendingCommandException>()).Which.Error.Message.Should().Contain("external reference");
    }

    [Fact]
    public async Task Refund_RetriedUnderTheSameExternalReference_IsRefusedAndMovesNothing()
    {
        var service = new InMemoryDirectLendingService();
        var loanId = await LoanHoldingUnappliedCashAsync(service, Terms(), 2_000m);
        await service.RefundUnappliedCashAsync(loanId, new RefundUnappliedCashRequest(500m, PaymentDate, "wire-out-1"));

        var retry = () => service.RefundUnappliedCashAsync(loanId, new RefundUnappliedCashRequest(500m, PaymentDate, " wire-out-1 "));

        (await retry.Should().ThrowAsync<DirectLendingCommandException>()).Which.Error.Code.Should().Be(DirectLendingErrorCode.Conflict);
        (await service.GetServicingStateAsync(loanId))!.Balances.UnappliedCash.Should().Be(1_500m);
        (await service.GetCashTransactionsAsync(loanId)).Should().ContainSingle(item => item.TransactionType == "UnappliedCashRefund");
    }

    [Fact]
    public void Reconciliation_TreatsRefundsAsOutgoing()
    {
        static CashTransactionDto Cash(string type)
            => new(Guid.NewGuid(), Guid.NewGuid(), type, PaymentDate, PaymentDate, PaymentDate, 100m, CurrencyCode.USD, "ref", Guid.NewGuid(), DateTimeOffset.UtcNow, false);

        DirectLendingServiceSupport.IsIncomingReceipt(Cash("MixedPayment")).Should().BeTrue();
        DirectLendingServiceSupport.IsIncomingReceipt(Cash("UnappliedCashRefund")).Should().BeFalse(
            "a refund paid out on a due date must not satisfy the receipt expected that day");
    }

    [Fact]
    public void JournalLines_ForApplyAndRefund_Balance()
    {
        using var applied = JsonDocument.Parse("""{"amount":1500,"appliedAmount":1200,"breakdown":{"toInterest":1000,"toCommitmentFee":0,"toFees":0,"toPenalty":0,"toPrincipal":200}}""");
        using var refunded = JsonDocument.Parse("""{"amount":800,"externalRef":"wire-out-1"}""");

        DirectLendingJournalLines.ForUnappliedCashApplied(applied.RootElement)
            .Select(line => (line.Account, line.Debit, line.Credit))
            .Should().Equal(
                ("UnappliedCashLiability", 1_200m, 0m),
                ("AccruedInterestReceivable", 0m, 1_000m),
                ("LoanPrincipal", 0m, 200m));
        DirectLendingJournalLines.ForUnappliedCashRefunded(refunded.RootElement)
            .Select(line => (line.Account, line.Debit, line.Credit))
            .Should().Equal(("UnappliedCashLiability", 800m, 0m), ("Cash", 0m, 800m));
    }

    [Fact]
    public void JournalLines_ForAnApplicationWhoseBreakdownDoesNotMatch_AreRejected()
    {
        using var applied = JsonDocument.Parse("""{"appliedAmount":1200,"breakdown":{"toInterest":1000,"toCommitmentFee":0,"toFees":0,"toPenalty":0,"toPrincipal":0}}""");

        var act = () => DirectLendingJournalLines.ForUnappliedCashApplied(applied.RootElement);

        act.Should().Throw<InvalidOperationException>().WithMessage("*would not balance*");
    }

    /// <summary>
    /// Active interest-only loan with 900,000 drawn and <paramref name="held"/> of unapplied cash,
    /// created by an excess payment under the default HoldUnapplied rule.
    /// </summary>
    private static async Task<Guid> LoanHoldingUnappliedCashAsync(InMemoryDirectLendingService service, DirectLendingTermsDto terms, decimal held)
    {
        var created = await service.CreateLoanAsync(new CreateLoanRequest(
            null, "Unapplied Cash Loan", new BorrowerInfoDto(Guid.NewGuid(), "Borrower", null), Origination, terms));
        await service.ActivateLoanAsync(created.LoanId, new ActivateLoanRequest(Origination));
        await service.BookDrawdownAsync(created.LoanId, new BookDrawdownRequest(900_000m, Origination, Origination, "wire"));
        await service.ApplyMixedPaymentAsync(created.LoanId, new ApplyMixedPaymentRequest(held, Origination, null, "excess"));
        (await service.GetServicingStateAsync(created.LoanId))!.Balances.UnappliedCash.Should().Be(held);
        return created.LoanId;
    }

    private static DirectLendingTermsDto Terms()
        => new(
            OriginationDate: Origination,
            MaturityDate: new DateOnly(2029, 1, 15),
            CommitmentAmount: 1_000_000m,
            BaseCurrency: CurrencyCode.USD,
            RateTypeKind: RateTypeKind.Fixed,
            FixedAnnualRate: 0.08m,
            InterestIndexName: null,
            SpreadBps: null,
            FloorRate: null,
            CapRate: null,
            DayCountBasis: DayCountBasis.Act360,
            PaymentFrequency: PaymentFrequency.Quarterly,
            AmortizationType: AmortizationType.InterestOnly,
            CommitmentFeeRate: null,
            DefaultRateSpreadBps: null,
            PrepaymentAllowed: true,
            CovenantsJson: null,
            InterestOnlyMonths: 12);
}
