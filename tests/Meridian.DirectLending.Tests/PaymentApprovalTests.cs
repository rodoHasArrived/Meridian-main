using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;

namespace Meridian.DirectLending.Tests;

public sealed class PaymentApprovalTests
{
    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static CreateLoanRequest BuildCreateRequest() =>
        new(
            LoanId: Guid.NewGuid(),
            FacilityName: "Apex Senior Term Loan",
            Borrower: new BorrowerInfoDto(Guid.NewGuid(), "Apex Capital", Guid.NewGuid()),
            EffectiveDate: new DateOnly(2026, 1, 1),
            Terms: new DirectLendingTermsDto(
                OriginationDate: new DateOnly(2026, 1, 1),
                MaturityDate: new DateOnly(2029, 1, 1),
                CommitmentAmount: 500_000m,
                BaseCurrency: CurrencyCode.USD,
                RateTypeKind: RateTypeKind.Fixed,
                FixedAnnualRate: 0.07m,
                InterestIndexName: null,
                SpreadBps: null,
                FloorRate: null,
                CapRate: null,
                DayCountBasis: DayCountBasis.Act365F,
                PaymentFrequency: PaymentFrequency.Monthly,
                AmortizationType: AmortizationType.InterestOnly,
                CommitmentFeeRate: 0.02m,
                DefaultRateSpreadBps: 200m,
                PrepaymentAllowed: true,
                CovenantsJson: null));

    private static async Task<InMemoryDirectLendingService> BuildActiveLoanService(decimal drawdownAmount = 100_000m)
    {
        var service = new InMemoryDirectLendingService();
        var loan = await service.CreateLoanAsync(BuildCreateRequest());
        await service.ActivateLoanAsync(loan.LoanId, new ActivateLoanRequest(new DateOnly(2026, 1, 2)));
        await service.BookDrawdownAsync(loan.LoanId, new BookDrawdownRequest(drawdownAmount, new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 2), "wire-100"));
        await service.PostDailyAccrualAsync(loan.LoanId, new PostDailyAccrualRequest(new DateOnly(2026, 1, 3)));
        return service;
    }

    // ------------------------------------------------------------------
    // InitiatePaymentAsync tests
    // ------------------------------------------------------------------

    [Fact]
    public async Task InitiatePaymentAsync_ShouldCreatePendingPaymentRecord()
    {
        var service = new InMemoryDirectLendingService();
        var loan = await service.CreateLoanAsync(BuildCreateRequest());

        var pending = await service.InitiatePaymentAsync(
            loan.LoanId,
            new InitiatePaymentRequest(5_000m, new DateOnly(2026, 2, 1), Breakdown: null, ExternalRef: "ext-001", Notes: "Q1 interest"));

        pending.Should().NotBeNull();
        pending.LoanId.Should().Be(loan.LoanId);
        pending.Amount.Should().Be(5_000m);
        pending.Status.Should().Be(PaymentApprovalStatus.Pending);
        pending.ReviewedAt.Should().BeNull();
        pending.ReviewedBy.Should().BeNull();
        pending.Notes.Should().Be("Q1 interest");
        pending.ExternalRef.Should().Be("ext-001");
    }

    [Fact]
    public async Task InitiatePaymentAsync_ShouldThrow_WhenAmountIsZero()
    {
        var service = new InMemoryDirectLendingService();
        var loan = await service.CreateLoanAsync(BuildCreateRequest());

        var act = () => service.InitiatePaymentAsync(
            loan.LoanId,
            new InitiatePaymentRequest(0m, new DateOnly(2026, 2, 1), null, null, null));

        var ex = await Assert.ThrowsAsync<DirectLendingCommandException>(act);
        ex.Error.Code.Should().Be(DirectLendingErrorCode.Validation);
        ex.Error.Message.Should().Contain("positive");
    }

    [Fact]
    public async Task InitiatePaymentAsync_ShouldThrow_WhenLoanDoesNotExist()
    {
        var service = new InMemoryDirectLendingService();

        var act = () => service.InitiatePaymentAsync(
            Guid.NewGuid(),
            new InitiatePaymentRequest(1_000m, new DateOnly(2026, 2, 1), null, null, null));

        var ex = await Assert.ThrowsAsync<DirectLendingCommandException>(act);
        ex.Error.Code.Should().Be(DirectLendingErrorCode.NotFound);
    }

    // ------------------------------------------------------------------
    // GetPendingPaymentsAsync tests
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetPendingPaymentsAsync_ShouldReturnAllPendingForLoan()
    {
        var service = new InMemoryDirectLendingService();
        var loan = await service.CreateLoanAsync(BuildCreateRequest());

        await service.InitiatePaymentAsync(loan.LoanId, new InitiatePaymentRequest(1_000m, new DateOnly(2026, 2, 1), null, null, null));
        await service.InitiatePaymentAsync(loan.LoanId, new InitiatePaymentRequest(2_000m, new DateOnly(2026, 2, 2), null, null, null));

        var pending = await service.GetPendingPaymentsAsync(loan.LoanId);

        pending.Should().HaveCount(2);
        pending.Should().OnlyContain(p => p.Status == PaymentApprovalStatus.Pending);
    }

    [Fact]
    public async Task GetPendingPaymentsAsync_ShouldExcludeApprovedAndRejected()
    {
        var service = new InMemoryDirectLendingService();
        var newLoan = await service.CreateLoanAsync(BuildCreateRequest());

        var p1 = await service.InitiatePaymentAsync(newLoan.LoanId, new InitiatePaymentRequest(1_000m, new DateOnly(2026, 2, 1), null, null, null));
        var p2 = await service.InitiatePaymentAsync(newLoan.LoanId, new InitiatePaymentRequest(500m, new DateOnly(2026, 2, 2), null, null, null));

        await service.RejectPaymentAsync(p2.PendingPaymentId, new RejectPaymentRequest("Duplicate", null));

        var pending = await service.GetPendingPaymentsAsync(newLoan.LoanId);

        pending.Should().ContainSingle();
        pending[0].PendingPaymentId.Should().Be(p1.PendingPaymentId);
    }

    // ------------------------------------------------------------------
    // ApprovePaymentAsync tests
    // ------------------------------------------------------------------

    [Fact]
    public async Task ApprovePaymentAsync_ShouldApplyPaymentAndReturnUpdatedServicingState()
    {
        var service = await BuildActiveLoanService(drawdownAmount: 100_000m);

        // Retrieve the known loan id by looking at servicing
        // We know there is exactly one loan in the service; get its id from cash transactions
        // Simpler: create another loan explicitly
        var loan = await service.CreateLoanAsync(BuildCreateRequest());
        await service.ActivateLoanAsync(loan.LoanId, new ActivateLoanRequest(new DateOnly(2026, 1, 2)));
        await service.BookDrawdownAsync(loan.LoanId, new BookDrawdownRequest(50_000m, new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 2), "wire-200"));
        await service.PostDailyAccrualAsync(loan.LoanId, new PostDailyAccrualRequest(new DateOnly(2026, 1, 3)));

        var pending = await service.InitiatePaymentAsync(
            loan.LoanId,
            new InitiatePaymentRequest(10_000m, new DateOnly(2026, 1, 10), null, "ext-approve-1", null));

        var servicingBefore = await service.GetServicingStateAsync(loan.LoanId);
        var principalBefore = servicingBefore!.Balances.PrincipalOutstanding;

        var servicingAfter = await service.ApprovePaymentAsync(
            pending.PendingPaymentId,
            new ApprovePaymentRequest(ReviewNotes: "Approved by treasurer", ReviewedBy: "treasurer@example.com"));

        servicingAfter.Should().NotBeNull();
        servicingAfter!.LastPaymentDate.Should().Be(new DateOnly(2026, 1, 10));

        // The approved payment should no longer appear in pending list
        var stillPending = await service.GetPendingPaymentsAsync(loan.LoanId);
        stillPending.Should().BeEmpty();

        // A cash transaction should have been recorded
        var cash = await service.GetCashTransactionsAsync(loan.LoanId);
        cash.Should().Contain(t => t.TransactionType == "ApprovedPayment" && t.Amount == 10_000m);

        // Principal should have decreased
        servicingAfter.Balances.PrincipalOutstanding.Should().BeLessThan(principalBefore);
    }

    [Fact]
    public async Task ApprovePaymentAsync_ShouldReturnNull_WhenPendingPaymentIdNotFound()
    {
        var service = new InMemoryDirectLendingService();

        var result = await service.ApprovePaymentAsync(
            Guid.NewGuid(),
            new ApprovePaymentRequest(null, null));

        result.Should().BeNull();
    }

    [Fact]
    public async Task ApprovePaymentAsync_ShouldThrow_WhenPaymentAlreadyRejected()
    {
        var service = new InMemoryDirectLendingService();
        var loan = await service.CreateLoanAsync(BuildCreateRequest());

        var pending = await service.InitiatePaymentAsync(
            loan.LoanId,
            new InitiatePaymentRequest(1_000m, new DateOnly(2026, 2, 1), null, null, null));

        await service.RejectPaymentAsync(pending.PendingPaymentId, new RejectPaymentRequest("Wrong amount", null));

        var act = () => service.ApprovePaymentAsync(
            pending.PendingPaymentId,
            new ApprovePaymentRequest(null, null));

        var ex = await Assert.ThrowsAsync<DirectLendingCommandException>(act);
        ex.Error.Code.Should().Be(DirectLendingErrorCode.Validation);
        ex.Error.Message.Should().Contain("not in Pending status");
    }

    // ------------------------------------------------------------------
    // RejectPaymentAsync tests
    // ------------------------------------------------------------------

    [Fact]
    public async Task RejectPaymentAsync_ShouldMarkPaymentRejected_WithReason()
    {
        var service = new InMemoryDirectLendingService();
        var loan = await service.CreateLoanAsync(BuildCreateRequest());

        var pending = await service.InitiatePaymentAsync(
            loan.LoanId,
            new InitiatePaymentRequest(3_000m, new DateOnly(2026, 2, 15), null, null, null));

        var rejected = await service.RejectPaymentAsync(
            pending.PendingPaymentId,
            new RejectPaymentRequest(Reason: "Insufficient funds", ReviewedBy: "ops@example.com"));

        rejected.Should().NotBeNull();
        rejected!.Status.Should().Be(PaymentApprovalStatus.Rejected);
        rejected.ReviewNotes.Should().Be("Insufficient funds");
        rejected.ReviewedBy.Should().Be("ops@example.com");
        rejected.ReviewedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RejectPaymentAsync_ShouldReturnNull_WhenPaymentIdNotFound()
    {
        var service = new InMemoryDirectLendingService();

        var result = await service.RejectPaymentAsync(
            Guid.NewGuid(),
            new RejectPaymentRequest("No such payment", null));

        result.Should().BeNull();
    }

    [Fact]
    public async Task RejectPaymentAsync_ShouldThrow_WhenReasonIsEmpty()
    {
        var service = new InMemoryDirectLendingService();
        var loan = await service.CreateLoanAsync(BuildCreateRequest());

        var pending = await service.InitiatePaymentAsync(
            loan.LoanId,
            new InitiatePaymentRequest(500m, new DateOnly(2026, 2, 1), null, null, null));

        var act = () => service.RejectPaymentAsync(
            pending.PendingPaymentId,
            new RejectPaymentRequest(Reason: "   ", null));

        var ex = await Assert.ThrowsAsync<DirectLendingCommandException>(act);
        ex.Error.Code.Should().Be(DirectLendingErrorCode.Validation);
        ex.Error.Message.Should().Contain("Rejection reason");
    }

    [Fact]
    public async Task RejectPaymentAsync_ShouldThrow_WhenPaymentAlreadyApproved()
    {
        var service = await BuildActiveLoanService(100_000m);
        var loan = await service.CreateLoanAsync(BuildCreateRequest());
        await service.ActivateLoanAsync(loan.LoanId, new ActivateLoanRequest(new DateOnly(2026, 1, 2)));
        await service.BookDrawdownAsync(loan.LoanId, new BookDrawdownRequest(50_000m, new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 2), "wire-300"));

        var pending = await service.InitiatePaymentAsync(
            loan.LoanId,
            new InitiatePaymentRequest(1_000m, new DateOnly(2026, 1, 10), null, null, null));

        await service.ApprovePaymentAsync(pending.PendingPaymentId, new ApprovePaymentRequest(null, null));

        var act = () => service.RejectPaymentAsync(
            pending.PendingPaymentId,
            new RejectPaymentRequest("Trying to reject after approval", null));

        var ex = await Assert.ThrowsAsync<DirectLendingCommandException>(act);
        ex.Error.Code.Should().Be(DirectLendingErrorCode.Validation);
    }
}
