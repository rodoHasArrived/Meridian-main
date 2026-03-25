using FluentAssertions;
using Meridian.Application.Banking;
using Meridian.Contracts.Banking;

namespace Meridian.DirectLending.Tests;

public sealed class PaymentApprovalTests
{
    private static InMemoryBankingService BuildService() => new();

    // ------------------------------------------------------------------
    // InitiatePaymentAsync tests
    // ------------------------------------------------------------------

    [Fact]
    public async Task InitiatePaymentAsync_ShouldCreatePendingPaymentRecord()
    {
        var service = BuildService();
        var entityId = Guid.NewGuid();

        var pending = await service.InitiatePaymentAsync(
            entityId,
            new InitiatePaymentRequest(5_000m, new DateOnly(2026, 2, 1), ExternalRef: "ext-001", Notes: "Q1 interest"));

        pending.Should().NotBeNull();
        pending.EntityId.Should().Be(entityId);
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
        var service = BuildService();

        var act = () => service.InitiatePaymentAsync(
            Guid.NewGuid(),
            new InitiatePaymentRequest(0m, new DateOnly(2026, 2, 1), null, null));

        await Assert.ThrowsAsync<BankingException>(act);
    }

    // ------------------------------------------------------------------
    // GetPendingPaymentsAsync tests
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetPendingPaymentsAsync_ShouldReturnAllPendingForEntity()
    {
        var service = BuildService();
        var entityId = Guid.NewGuid();

        await service.InitiatePaymentAsync(entityId, new InitiatePaymentRequest(1_000m, new DateOnly(2026, 2, 1), null, null));
        await service.InitiatePaymentAsync(entityId, new InitiatePaymentRequest(2_000m, new DateOnly(2026, 2, 2), null, null));

        var pending = await service.GetPendingPaymentsAsync(entityId);

        pending.Should().HaveCount(2);
        pending.Should().OnlyContain(p => p.Status == PaymentApprovalStatus.Pending);
    }

    [Fact]
    public async Task GetPendingPaymentsAsync_ShouldExcludeApprovedAndRejected()
    {
        var service = BuildService();
        var entityId = Guid.NewGuid();

        var p1 = await service.InitiatePaymentAsync(entityId, new InitiatePaymentRequest(1_000m, new DateOnly(2026, 2, 1), null, null));
        var p2 = await service.InitiatePaymentAsync(entityId, new InitiatePaymentRequest(500m, new DateOnly(2026, 2, 2), null, null));

        await service.RejectPaymentAsync(p2.PendingPaymentId, new RejectPaymentRequest("Duplicate", null));

        var pending = await service.GetPendingPaymentsAsync(entityId);

        pending.Should().ContainSingle();
        pending[0].PendingPaymentId.Should().Be(p1.PendingPaymentId);
    }

    // ------------------------------------------------------------------
    // ApprovePaymentAsync tests
    // ------------------------------------------------------------------

    [Fact]
    public async Task ApprovePaymentAsync_ShouldMarkApprovedAndRecordBankTransaction()
    {
        var service = BuildService();
        var entityId = Guid.NewGuid();

        var pending = await service.InitiatePaymentAsync(
            entityId,
            new InitiatePaymentRequest(10_000m, new DateOnly(2026, 1, 10), ExternalRef: "ext-approve-1", Notes: null));

        var approved = await service.ApprovePaymentAsync(
            pending.PendingPaymentId,
            new ApprovePaymentRequest(ReviewNotes: "Approved by treasurer", ReviewedBy: "treasurer@example.com"));

        approved.Should().NotBeNull();
        approved!.Status.Should().Be(PaymentApprovalStatus.Approved);
        approved.ReviewedBy.Should().Be("treasurer@example.com");
        approved.ReviewNotes.Should().Be("Approved by treasurer");
        approved.ReviewedAt.Should().NotBeNull();

        // Should no longer appear in pending list
        var stillPending = await service.GetPendingPaymentsAsync(entityId);
        stillPending.Should().BeEmpty();

        // A bank transaction should be recorded
        var txns = await service.GetBankTransactionsAsync(entityId);
        txns.Should().ContainSingle(t => t.TransactionType == "ApprovedPayment" && t.Amount == 10_000m);
    }

    [Fact]
    public async Task ApprovePaymentAsync_ShouldReturnNull_WhenIdNotFound()
    {
        var service = BuildService();
        var result = await service.ApprovePaymentAsync(Guid.NewGuid(), new ApprovePaymentRequest(null, null));
        result.Should().BeNull();
    }

    [Fact]
    public async Task ApprovePaymentAsync_ShouldThrow_WhenPaymentAlreadyRejected()
    {
        var service = BuildService();
        var entityId = Guid.NewGuid();

        var pending = await service.InitiatePaymentAsync(
            entityId, new InitiatePaymentRequest(1_000m, new DateOnly(2026, 2, 1), null, null));

        await service.RejectPaymentAsync(pending.PendingPaymentId, new RejectPaymentRequest("Wrong amount", null));

        var act = () => service.ApprovePaymentAsync(pending.PendingPaymentId, new ApprovePaymentRequest(null, null));

        var ex = await Assert.ThrowsAsync<BankingException>(act);
        ex.Message.Should().Contain("not in Pending status");
    }

    // ------------------------------------------------------------------
    // RejectPaymentAsync tests
    // ------------------------------------------------------------------

    [Fact]
    public async Task RejectPaymentAsync_ShouldMarkRejected_WithReason()
    {
        var service = BuildService();
        var entityId = Guid.NewGuid();

        var pending = await service.InitiatePaymentAsync(
            entityId, new InitiatePaymentRequest(3_000m, new DateOnly(2026, 2, 15), null, null));

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
    public async Task RejectPaymentAsync_ShouldReturnNull_WhenIdNotFound()
    {
        var service = BuildService();
        var result = await service.RejectPaymentAsync(Guid.NewGuid(), new RejectPaymentRequest("No such payment", null));
        result.Should().BeNull();
    }

    [Fact]
    public async Task RejectPaymentAsync_ShouldThrow_WhenReasonIsEmpty()
    {
        var service = BuildService();
        var entityId = Guid.NewGuid();

        var pending = await service.InitiatePaymentAsync(
            entityId, new InitiatePaymentRequest(500m, new DateOnly(2026, 2, 1), null, null));

        var act = () => service.RejectPaymentAsync(
            pending.PendingPaymentId, new RejectPaymentRequest(Reason: "   ", null));

        var ex = await Assert.ThrowsAsync<BankingException>(act);
        ex.Message.Should().Contain("Rejection reason");
    }

    [Fact]
    public async Task RejectPaymentAsync_ShouldThrow_WhenPaymentAlreadyApproved()
    {
        var service = BuildService();
        var entityId = Guid.NewGuid();

        var pending = await service.InitiatePaymentAsync(
            entityId, new InitiatePaymentRequest(1_000m, new DateOnly(2026, 1, 10), null, null));

        await service.ApprovePaymentAsync(pending.PendingPaymentId, new ApprovePaymentRequest(null, null));

        var act = () => service.RejectPaymentAsync(
            pending.PendingPaymentId, new RejectPaymentRequest("Too late", null));

        var ex = await Assert.ThrowsAsync<BankingException>(act);
        ex.Message.Should().Contain("not in Pending status");
    }
}
