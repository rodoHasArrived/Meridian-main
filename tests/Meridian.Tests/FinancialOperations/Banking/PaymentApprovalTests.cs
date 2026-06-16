using FluentAssertions;
using Meridian.Contracts.Banking;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Banking;

namespace Meridian.Tests.FinancialOperations.Banking;

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
    public async Task ApprovePaymentAsync_ShouldMarkApprovedWithoutRecordingBankTransaction()
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

        var txns = await service.GetBankTransactionsAsync(entityId);
        txns.Should().BeEmpty("approval records Meridian intent and reviewer state; bank evidence is retained separately");

        var reloaded = await service.GetPaymentAsync(pending.PendingPaymentId);
        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be(PaymentApprovalStatus.Approved);
    }

    [Fact]
    public async Task RecordPaymentBankEvidenceAsync_ShouldRetainBankConfirmationAndReturnAfterApproval()
    {
        var service = BuildService();
        var entityId = Guid.NewGuid();

        var pending = await service.InitiatePaymentAsync(
            entityId,
            new InitiatePaymentRequest(10_000m, new DateOnly(2026, 1, 10), ExternalRef: "payment-intent-1", Notes: null));
        await service.ApprovePaymentAsync(
            pending.PendingPaymentId,
            new ApprovePaymentRequest(ReviewNotes: "Approved by treasurer", ReviewedBy: "treasurer@example.com"));

        var confirmation = await service.RecordPaymentBankEvidenceAsync(
            pending.PendingPaymentId,
            new RecordPaymentBankEvidenceRequest(
                "BankConfirmation",
                TransactionDate: new DateOnly(2026, 1, 11),
                SettlementDate: new DateOnly(2026, 1, 12),
                Amount: 10_000m,
                Currency: "usd",
                ExternalRef: "bank-confirmation-1",
                RecordedBy: "cash-ops@example.com"));
        var returned = await service.RecordPaymentBankEvidenceAsync(
            pending.PendingPaymentId,
            new RecordPaymentBankEvidenceRequest(
                "BankReturn",
                TransactionDate: new DateOnly(2026, 1, 13),
                SettlementDate: new DateOnly(2026, 1, 13),
                ExternalRef: "bank-return-1",
                RecordedBy: "treasury-reviewer@example.com"));

        confirmation.Should().NotBeNull();
        confirmation!.TransactionType.Should().Be("BankConfirmation");
        confirmation.Currency.Should().Be("USD");
        confirmation.ExternalRef.Should().Be("bank-confirmation-1");
        confirmation.RecordedBy.Should().Be("cash-ops@example.com");
        confirmation.IsVoided.Should().BeFalse();
        returned.Should().NotBeNull();
        returned!.TransactionType.Should().Be("BankReturn");
        returned.RecordedBy.Should().Be("treasury-reviewer@example.com");
        returned.IsVoided.Should().BeTrue();

        var txns = await service.GetBankTransactionsAsync(entityId);
        txns.Should().HaveCount(2);
        txns.Should().Contain(t =>
            t.TransactionType == "BankConfirmation" &&
            t.Amount == 10_000m &&
            t.RecordedBy == "cash-ops@example.com");
        txns.Should().Contain(t =>
            t.TransactionType == "BankReturn" &&
            t.ExternalRef == "bank-return-1" &&
            t.RecordedBy == "treasury-reviewer@example.com");
    }

    [Fact]
    public async Task RecordPaymentBankEvidenceAsync_ShouldIgnoreBlankRecordedBy()
    {
        var service = BuildService();
        var entityId = Guid.NewGuid();

        var pending = await service.InitiatePaymentAsync(
            entityId,
            new InitiatePaymentRequest(10_000m, new DateOnly(2026, 1, 10), ExternalRef: "payment-intent-1", Notes: null));
        await service.ApprovePaymentAsync(
            pending.PendingPaymentId,
            new ApprovePaymentRequest(ReviewNotes: "Approved by treasurer", ReviewedBy: "treasurer@example.com"));

        var confirmation = await service.RecordPaymentBankEvidenceAsync(
            pending.PendingPaymentId,
            new RecordPaymentBankEvidenceRequest(
                "BankConfirmation",
                RecordedBy: "   "));

        confirmation.Should().NotBeNull();
        confirmation!.RecordedBy.Should().BeNull();
    }

    [Fact]
    public async Task RecordPaymentBankEvidenceAsync_ShouldRequireApprovedPayment()
    {
        var service = BuildService();
        var entityId = Guid.NewGuid();

        var pending = await service.InitiatePaymentAsync(
            entityId,
            new InitiatePaymentRequest(10_000m, new DateOnly(2026, 1, 10), ExternalRef: "payment-intent-1", Notes: null));

        var act = () => service.RecordPaymentBankEvidenceAsync(
            pending.PendingPaymentId,
            new RecordPaymentBankEvidenceRequest("BankConfirmation"));

        var ex = await Assert.ThrowsAsync<BankingException>(act);
        ex.Message.Should().Contain("must be approved before bank confirmation");
    }

    [Fact]
    public async Task RecordPaymentBankEvidenceAsync_ShouldRejectReviewedAutomationOriginBeforeCashEvidenceMutation()
    {
        var service = BuildService();
        var entityId = Guid.NewGuid();

        var pending = await service.InitiatePaymentAsync(
            entityId,
            new InitiatePaymentRequest(10_000m, new DateOnly(2026, 1, 10), ExternalRef: "payment-intent-automation", Notes: null));
        await service.ApprovePaymentAsync(
            pending.PendingPaymentId,
            new ApprovePaymentRequest(ReviewNotes: "Approved by treasurer", ReviewedBy: "treasurer@example.com"));

        var act = () => service.RecordPaymentBankEvidenceAsync(
            pending.PendingPaymentId,
            new RecordPaymentBankEvidenceRequest(
                "BankConfirmation",
                TransactionDate: new DateOnly(2026, 1, 11),
                SettlementDate: new DateOnly(2026, 1, 12),
                Amount: 10_000m,
                Currency: "usd",
                ExternalRef: "assistant-bank-confirmation-1",
                RecordedBy: "reviewed-automation",
                ActionOrigin: OperationsActionOriginDto.AssistantDraft));

        var ex = await Assert.ThrowsAsync<BankingException>(act);
        ex.Message.Should().Contain("Reviewed automation cannot record bank evidence");

        var approved = await service.GetPaymentAsync(pending.PendingPaymentId);
        approved.Should().NotBeNull();
        approved!.Status.Should().Be(PaymentApprovalStatus.Approved);

        var txns = await service.GetBankTransactionsAsync(entityId);
        txns.Should().BeEmpty();
    }

    [Fact]
    public async Task ApprovePaymentAsync_ShouldRejectReviewedAutomationOriginBeforeRelease()
    {
        var service = BuildService();
        var entityId = Guid.NewGuid();

        var pending = await service.InitiatePaymentAsync(
            entityId,
            new InitiatePaymentRequest(10_000m, new DateOnly(2026, 1, 10), ExternalRef: "ext-assistant-1", Notes: null));

        var act = () => service.ApprovePaymentAsync(
            pending.PendingPaymentId,
            new ApprovePaymentRequest(
                ReviewNotes: "Assistant draft approval",
                ReviewedBy: "reviewed-automation",
                ActionOrigin: OperationsActionOriginDto.AssistantDraft));

        var ex = await Assert.ThrowsAsync<BankingException>(act);
        ex.Message.Should().Contain("Reviewed automation cannot approve payment requests");

        var stillPending = await service.GetPendingPaymentsAsync(entityId);
        stillPending.Should().ContainSingle(item =>
            item.PendingPaymentId == pending.PendingPaymentId &&
            item.Status == PaymentApprovalStatus.Pending);

        var txns = await service.GetBankTransactionsAsync(entityId);
        txns.Should().BeEmpty();
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
    public async Task RejectPaymentAsync_ShouldRejectReviewedAutomationOriginBeforeMutation()
    {
        var service = BuildService();
        var entityId = Guid.NewGuid();

        var pending = await service.InitiatePaymentAsync(
            entityId,
            new InitiatePaymentRequest(3_000m, new DateOnly(2026, 2, 15), ExternalRef: "ext-reject-automation", Notes: null));

        var act = () => service.RejectPaymentAsync(
            pending.PendingPaymentId,
            new RejectPaymentRequest(
                Reason: "Assistant draft rejection",
                ReviewedBy: "reviewed-automation",
                ActionOrigin: OperationsActionOriginDto.AssistantDraft));

        var ex = await Assert.ThrowsAsync<BankingException>(act);
        ex.Message.Should().Contain("Reviewed automation cannot reject payments");

        var stillPending = await service.GetPendingPaymentsAsync(entityId);
        stillPending.Should().ContainSingle(item =>
            item.PendingPaymentId == pending.PendingPaymentId &&
            item.Status == PaymentApprovalStatus.Pending &&
            item.ReviewedAt == null &&
            item.ReviewedBy == null);

        var txns = await service.GetBankTransactionsAsync(entityId);
        txns.Should().BeEmpty();
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
