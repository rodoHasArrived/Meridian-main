using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Banking;
using Meridian.Contracts.Banking;
using Meridian.Storage.Banking;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.Banking;

public sealed class PaymentApprovalTests
{
    private static InMemoryBankingService BuildService() => new();

    private static InitiatePaymentRequest PaymentRequest(
        decimal Amount,
        DateOnly EffectiveDate,
        string? ExternalRef,
        string? Notes,
        string Currency = "USD")
        => new(Amount, EffectiveDate, ExternalRef, Notes, Currency);

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
            PaymentRequest(5_000m, new DateOnly(2026, 2, 1), ExternalRef: "ext-001", Notes: "Q1 interest"));

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
            PaymentRequest(0m, new DateOnly(2026, 2, 1), null, null));

        await Assert.ThrowsAsync<BankingException>(act);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("US1")]
    [InlineData("EURO")]
    [InlineData("ZZZ")]
    [InlineData("ANG")]
    [InlineData("BGN")]
    [InlineData("BYR")]
    [InlineData("CUC")]
    [InlineData("HRK")]
    [InlineData("SLL")]
    [InlineData("ZWL")]
    public async Task InitiatePaymentAsync_ShouldRejectMissingOrInvalidCurrency(string? currency)
    {
        var service = BuildService();

        var act = () => service.InitiatePaymentAsync(
            Guid.NewGuid(),
            new InitiatePaymentRequest(
                1_000m,
                new DateOnly(2026, 2, 1),
                ExternalRef: null,
                Notes: null,
                Currency: currency));

        var exception = await Assert.ThrowsAsync<BankingException>(act);
        exception.Message.Should().Contain("three-letter");
    }

    [Fact]
    public async Task InitiatePaymentAsync_ShouldNormalizeLowercaseCurrency()
    {
        var service = BuildService();

        var pending = await service.InitiatePaymentAsync(
            Guid.NewGuid(),
            PaymentRequest(1_000m, new DateOnly(2026, 2, 1), null, null, Currency: " usd "));

        pending.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task InitiatePaymentAsync_ShouldAcceptOperationalCnhCurrency()
    {
        var service = BuildService();

        var pending = await service.InitiatePaymentAsync(
            Guid.NewGuid(),
            PaymentRequest(1_000m, new DateOnly(2026, 2, 1), null, null, Currency: " cnh "));

        pending.Currency.Should().Be("CNH");
    }

    [Fact]
    public async Task RemediatePaymentCurrencyAsync_PostgresService_RetainsNormalizedHumanEvidence()
    {
        var paymentId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var legacy = new PendingPaymentDto(
            paymentId,
            entityId,
            500m,
            new DateOnly(2026, 2, 1),
            null,
            null,
            PaymentApprovalStatus.Pending,
            null,
            null,
            DateTimeOffset.UtcNow,
            null);
        var store = Substitute.For<IBankingStore>();
        store.TryRemediatePendingPaymentCurrencyAsync(
                paymentId,
                "EUR",
                "currency-operator",
                "signed source instruction",
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(call => legacy with
            {
                Currency = call.ArgAt<string>(1),
                CurrencyRemediatedBy = call.ArgAt<string>(2),
                CurrencyRemediationReason = call.ArgAt<string>(3),
                CurrencyRemediatedAt = call.ArgAt<DateTimeOffset>(4),
            });
        var service = new PostgresBankingService(store);

        var remediated = await service.RemediatePaymentCurrencyAsync(
            paymentId,
            new RemediatePaymentCurrencyRequest(
                " eur ",
                " signed source instruction ",
                " currency-operator "));

        remediated!.Currency.Should().Be("EUR");
        remediated.CurrencyRemediatedBy.Should().Be("currency-operator");
        remediated.CurrencyRemediationReason.Should().Be("signed source instruction");
        remediated.CurrencyRemediatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RemediatePaymentCurrencyAsync_CannotReplaceExistingCurrency()
    {
        var service = BuildService();
        var pending = await service.InitiatePaymentAsync(
            Guid.NewGuid(),
            PaymentRequest(500m, new DateOnly(2026, 2, 1), null, null, "USD"));

        var act = () => service.RemediatePaymentCurrencyAsync(
            pending.PendingPaymentId,
            new RemediatePaymentCurrencyRequest("EUR", "replacement", "operator"));

        await Assert.ThrowsAsync<BankingConflictException>(act);
        (await service.GetPaymentAsync(pending.PendingPaymentId))!.Currency.Should().Be("USD");
    }

    // ------------------------------------------------------------------
    // GetPendingPaymentsAsync tests
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetPendingPaymentsAsync_ShouldReturnAllPendingForEntity()
    {
        var service = BuildService();
        var entityId = Guid.NewGuid();

        await service.InitiatePaymentAsync(entityId, PaymentRequest(1_000m, new DateOnly(2026, 2, 1), null, null));
        await service.InitiatePaymentAsync(entityId, PaymentRequest(2_000m, new DateOnly(2026, 2, 2), null, null));

        var pending = await service.GetPendingPaymentsAsync(entityId);

        pending.Should().HaveCount(2);
        pending.Should().OnlyContain(p => p.Status == PaymentApprovalStatus.Pending);
    }

    [Fact]
    public async Task GetPendingPaymentsAsync_ShouldExcludeApprovedAndRejected()
    {
        var service = BuildService();
        var entityId = Guid.NewGuid();

        var p1 = await service.InitiatePaymentAsync(entityId, PaymentRequest(1_000m, new DateOnly(2026, 2, 1), null, null));
        var p2 = await service.InitiatePaymentAsync(entityId, PaymentRequest(500m, new DateOnly(2026, 2, 2), null, null));

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
            PaymentRequest(10_000m, new DateOnly(2026, 1, 10), ExternalRef: "ext-approve-1", Notes: null));

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
            PaymentRequest(10_000m, new DateOnly(2026, 1, 10), ExternalRef: "payment-intent-1", Notes: null));
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
                RecordedBy: "cash-ops@example.com",
                EvidenceId: "confirmation-1"));
        var returned = await service.RecordPaymentBankEvidenceAsync(
            pending.PendingPaymentId,
            new RecordPaymentBankEvidenceRequest(
                "BankReturn",
                TransactionDate: new DateOnly(2026, 1, 13),
                SettlementDate: new DateOnly(2026, 1, 13),
                ExternalRef: "bank-return-1",
                RecordedBy: "treasury-reviewer@example.com",
                EvidenceId: "return-1"));

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
    public async Task RecordPaymentBankEvidenceAsync_ShouldBindEntityAmountAndCurrencyToApprovedIntent()
    {
        var service = BuildService();
        var entityId = Guid.NewGuid();
        var pending = await service.InitiatePaymentAsync(
            entityId,
            PaymentRequest(1_250m, new DateOnly(2026, 2, 10), null, null, Currency: "eur"));
        await service.ApprovePaymentAsync(
            pending.PendingPaymentId,
            new ApprovePaymentRequest(null, "reviewer@example.com"));

        var retained = await service.RecordPaymentBankEvidenceAsync(
            pending.PendingPaymentId,
            new RecordPaymentBankEvidenceRequest(
                "BankConfirmation",
                Amount: 1_250m,
                Currency: " eur ",
                EvidenceId: "eur-confirmation-1"));

        retained.Should().NotBeNull();
        retained!.EntityId.Should().Be(entityId);
        retained.Amount.Should().Be(1_250m);
        retained.Currency.Should().Be("EUR");
        retained.PendingPaymentId.Should().Be(pending.PendingPaymentId);

        var wrongAmount = () => service.RecordPaymentBankEvidenceAsync(
            pending.PendingPaymentId,
            new RecordPaymentBankEvidenceRequest(
                "BankConfirmation",
                Amount: 1_251m,
                Currency: "EUR",
                EvidenceId: "wrong-amount"));
        var wrongCurrency = () => service.RecordPaymentBankEvidenceAsync(
            pending.PendingPaymentId,
            new RecordPaymentBankEvidenceRequest(
                "BankConfirmation",
                Amount: 1_250m,
                Currency: "USD",
                EvidenceId: "wrong-currency"));

        (await Assert.ThrowsAsync<BankingException>(wrongAmount)).Message.Should().Contain("must match payment amount");
        (await Assert.ThrowsAsync<BankingException>(wrongCurrency)).Message.Should().Contain("must match payment currency");
    }

    [Fact]
    public async Task RecordPaymentBankEvidenceAsync_ShouldRetainHistoricalCurrencyForApprovedLegacyIntent()
    {
        var paymentId = Guid.NewGuid();
        var approvedLegacyPayment = new PendingPaymentDto(
            paymentId,
            Guid.NewGuid(),
            1_250m,
            new DateOnly(2026, 2, 10),
            "legacy-instruction",
            null,
            PaymentApprovalStatus.Approved,
            "legacy-reviewer",
            null,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            Currency: "HRK");
        var store = Substitute.For<IBankingStore>();
        store.GetPendingPaymentAsync(paymentId, Arg.Any<CancellationToken>())
            .Returns(approvedLegacyPayment);
        store.RecordPaymentBankEvidenceAsync(Arg.Any<BankTransactionDto>(), Arg.Any<CancellationToken>())
            .Returns(call => new PaymentBankEvidenceWriteResult(
                PaymentBankEvidenceWriteStatus.Inserted,
                call.ArgAt<BankTransactionDto>(0)));
        var service = new PostgresBankingService(store);

        var evidence = await service.RecordPaymentBankEvidenceAsync(
            paymentId,
            new RecordPaymentBankEvidenceRequest(
                "BankConfirmation",
                Currency: " hrk ",
                EvidenceId: "legacy-hrk-confirmation"));

        evidence.Should().NotBeNull();
        evidence!.Currency.Should().Be("HRK");
        evidence.PendingPaymentId.Should().Be(paymentId);
        evidence.CanonicalInputHash.Should().MatchRegex("^[0-9A-F]{64}$");
    }

    [Fact]
    public async Task RecordPaymentBankEvidenceAsync_CanonicalHashShouldBindRetainedProvenance()
    {
        var paymentId = Guid.NewGuid();
        var approvedPayment = new PendingPaymentDto(
            paymentId,
            Guid.NewGuid(),
            1_250m,
            new DateOnly(2026, 2, 10),
            "payment-reference",
            null,
            PaymentApprovalStatus.Approved,
            "reviewer",
            null,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            Currency: "USD");
        var store = Substitute.For<IBankingStore>();
        store.GetPendingPaymentAsync(paymentId, Arg.Any<CancellationToken>())
            .Returns(approvedPayment);
        store.RecordPaymentBankEvidenceAsync(Arg.Any<BankTransactionDto>(), Arg.Any<CancellationToken>())
            .Returns(call => new PaymentBankEvidenceWriteResult(
                PaymentBankEvidenceWriteStatus.Inserted,
                call.ArgAt<BankTransactionDto>(0)));
        var service = new PostgresBankingService(store);
        var baseline = new RecordPaymentBankEvidenceRequest(
            "BankConfirmation",
            TransactionDate: new DateOnly(2026, 2, 11),
            SettlementDate: new DateOnly(2026, 2, 12),
            Amount: 1_250m,
            Currency: "USD",
            ExternalRef: "bank-reference",
            RecordedBy: "cash-operator",
            EvidenceId: "evidence-1");
        var variants = new[]
        {
            baseline,
            baseline with { EvidenceType = "BankReturn" },
            baseline with { TransactionDate = new DateOnly(2026, 2, 12) },
            baseline with { SettlementDate = new DateOnly(2026, 2, 13) },
            baseline with { ExternalRef = "other-bank-reference" },
            baseline with { RecordedBy = "other-cash-operator" },
            baseline with { EvidenceId = "evidence-2" }
        };

        var retained = new List<BankTransactionDto>();
        foreach (var variant in variants)
        {
            retained.Add((await service.RecordPaymentBankEvidenceAsync(paymentId, variant))!);
        }

        retained.Should().OnlyContain(transaction =>
            transaction.PendingPaymentId == paymentId
            && transaction.CanonicalInputHash is { Length: 64 });
        retained.Select(transaction => transaction.CanonicalInputHash)
            .Should().OnlyHaveUniqueItems("every retained provenance change must alter replay identity");
    }

    [Fact]
    public async Task RecordPaymentBankEvidenceAsync_ShouldRequireStableEvidenceId()
    {
        var service = BuildService();
        var pending = await service.InitiatePaymentAsync(
            Guid.NewGuid(),
            PaymentRequest(500m, new DateOnly(2026, 2, 10), null, null));
        await service.ApprovePaymentAsync(pending.PendingPaymentId, new ApprovePaymentRequest(null, "reviewer"));

        var act = () => service.RecordPaymentBankEvidenceAsync(
            pending.PendingPaymentId,
            new RecordPaymentBankEvidenceRequest("BankConfirmation"));

        (await Assert.ThrowsAsync<BankingException>(act)).Message.Should().Contain("EvidenceId is required");
    }

    [Fact]
    public async Task RecordPaymentBankEvidenceAsync_SequentialAndConcurrentIdenticalReplay_ShouldReturnRetainedRecordOnce()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var service = BuildService();
        var entityId = Guid.NewGuid();
        var pending = await service.InitiatePaymentAsync(
            entityId,
            PaymentRequest(900m, new DateOnly(2026, 2, 10), "payment-900", null),
            cts.Token);
        await service.ApprovePaymentAsync(
            pending.PendingPaymentId,
            new ApprovePaymentRequest(null, "reviewer"),
            cts.Token);
        var request = new RecordPaymentBankEvidenceRequest(
            "BankConfirmation",
            Amount: 900m,
            Currency: "USD",
            ExternalRef: "bank-900",
            RecordedBy: "cash-ops",
            EvidenceId: "stable-bank-event-900");

        var first = await service.RecordPaymentBankEvidenceAsync(pending.PendingPaymentId, request, cts.Token);
        var sequentialReplay = await service.RecordPaymentBankEvidenceAsync(pending.PendingPaymentId, request, cts.Token);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var concurrentReplays = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(async () =>
            {
                await start.Task.WaitAsync(cts.Token);
                return await service.RecordPaymentBankEvidenceAsync(pending.PendingPaymentId, request, cts.Token);
            }, cts.Token))
            .ToArray();
        start.SetResult();
        var replayed = await Task.WhenAll(concurrentReplays);

        first.Should().NotBeNull();
        sequentialReplay!.BankTransactionId.Should().Be(first!.BankTransactionId);
        replayed.Should().OnlyContain(item => item!.BankTransactionId == first.BankTransactionId);
        (await service.GetBankTransactionsAsync(entityId, cts.Token)).Should().ContainSingle();
    }

    [Fact]
    public async Task RecordPaymentBankEvidenceAsync_SameEvidenceIdWithDifferentInput_ShouldConflictWithoutMutation()
    {
        var service = BuildService();
        var entityId = Guid.NewGuid();
        var pending = await service.InitiatePaymentAsync(
            entityId,
            PaymentRequest(900m, new DateOnly(2026, 2, 10), null, null));
        await service.ApprovePaymentAsync(pending.PendingPaymentId, new ApprovePaymentRequest(null, "reviewer"));
        var original = new RecordPaymentBankEvidenceRequest(
            "BankConfirmation",
            ExternalRef: "bank-original",
            EvidenceId: "stable-bank-event-901");
        await service.RecordPaymentBankEvidenceAsync(pending.PendingPaymentId, original);

        var act = () => service.RecordPaymentBankEvidenceAsync(
            pending.PendingPaymentId,
            original with { ExternalRef = "bank-conflict" });

        await Assert.ThrowsAsync<BankingConflictException>(act);
        (await service.GetBankTransactionsAsync(entityId)).Should().ContainSingle()
            .Which.ExternalRef.Should().Be("bank-original");
    }

    [Fact]
    public async Task RecordPaymentBankEvidenceAsync_CancelledBeforeMutation_ShouldLeaveNoEvidence()
    {
        var service = BuildService();
        var entityId = Guid.NewGuid();
        var pending = await service.InitiatePaymentAsync(
            entityId,
            PaymentRequest(900m, new DateOnly(2026, 2, 10), null, null));
        await service.ApprovePaymentAsync(pending.PendingPaymentId, new ApprovePaymentRequest(null, "reviewer"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.RecordPaymentBankEvidenceAsync(
            pending.PendingPaymentId,
            new RecordPaymentBankEvidenceRequest("BankConfirmation", EvidenceId: "cancelled-evidence"),
            cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
        (await service.GetBankTransactionsAsync(entityId)).Should().BeEmpty();
    }

    [Fact]
    public async Task RecordPaymentBankEvidenceAsync_ReversedBankTransfer_ShouldNormalizeVoidAndRetainAttribution()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var service = BuildService();
        var entityId = Guid.NewGuid();

        var pending = await service.InitiatePaymentAsync(
            entityId,
            PaymentRequest(
                7_500m,
                new DateOnly(2026, 1, 15),
                ExternalRef: "payment-intent-reversed-1",
                Notes: "Vendor settlement"),
            cts.Token);
        await service.ApprovePaymentAsync(
            pending.PendingPaymentId,
            new ApprovePaymentRequest(
                ReviewNotes: "Released by treasury",
                ReviewedBy: "treasurer@example.com"),
            cts.Token);

        var reversal = await service.RecordPaymentBankEvidenceAsync(
            pending.PendingPaymentId,
            new RecordPaymentBankEvidenceRequest(
                EvidenceType: " reversed ",
                TransactionDate: new DateOnly(2026, 1, 16),
                SettlementDate: new DateOnly(2026, 1, 17),
                Amount: 7_500m,
                Currency: "usd",
                ExternalRef: "bank-reversal-advice-1",
                RecordedBy: " treasury-operations@example.com ",
                EvidenceId: "reversal-1"),
            cts.Token);

        reversal.Should().NotBeNull();
        reversal!.TransactionType.Should().Be("BankReversal");
        reversal.IsVoided.Should().BeTrue();
        reversal.RecordedBy.Should().Be("treasury-operations@example.com");
        reversal.ExternalRef.Should().Be("bank-reversal-advice-1");

        var retainedTransactions = await service.GetBankTransactionsAsync(entityId, cts.Token);
        retainedTransactions.Should().ContainSingle();
        var retainedReversal = retainedTransactions.Single();
        retainedReversal.BankTransactionId.Should().Be(reversal.BankTransactionId);
        retainedReversal.TransactionType.Should().Be("BankReversal");
        retainedReversal.IsVoided.Should().BeTrue();
        retainedReversal.RecordedBy.Should().Be("treasury-operations@example.com");
        retainedReversal.ExternalRef.Should().Be("bank-reversal-advice-1");
    }

    [Fact]
    public async Task RecordPaymentBankEvidenceAsync_ShouldIgnoreBlankRecordedBy()
    {
        var service = BuildService();
        var entityId = Guid.NewGuid();

        var pending = await service.InitiatePaymentAsync(
            entityId,
            PaymentRequest(10_000m, new DateOnly(2026, 1, 10), ExternalRef: "payment-intent-1", Notes: null));
        await service.ApprovePaymentAsync(
            pending.PendingPaymentId,
            new ApprovePaymentRequest(ReviewNotes: "Approved by treasurer", ReviewedBy: "treasurer@example.com"));

        var confirmation = await service.RecordPaymentBankEvidenceAsync(
            pending.PendingPaymentId,
            new RecordPaymentBankEvidenceRequest(
                "BankConfirmation",
                RecordedBy: "   ",
                EvidenceId: "blank-actor-confirmation"));

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
            PaymentRequest(10_000m, new DateOnly(2026, 1, 10), ExternalRef: "payment-intent-1", Notes: null));

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
            PaymentRequest(10_000m, new DateOnly(2026, 1, 10), ExternalRef: "payment-intent-automation", Notes: null));
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
                ActionOrigin: OperationsActionOriginDto.AssistantDraft,
                EvidenceId: "assistant-confirmation-1"));

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
            PaymentRequest(10_000m, new DateOnly(2026, 1, 10), ExternalRef: "ext-assistant-1", Notes: null));

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
            entityId, PaymentRequest(1_000m, new DateOnly(2026, 2, 1), null, null));

        await service.RejectPaymentAsync(pending.PendingPaymentId, new RejectPaymentRequest("Wrong amount", null));

        var act = () => service.ApprovePaymentAsync(pending.PendingPaymentId, new ApprovePaymentRequest(null, null));

        var ex = await Assert.ThrowsAsync<BankingException>(act);
        ex.Message.Should().Contain("not in Pending status");
    }

    [Fact]
    public async Task ApproveAndRejectConcurrentBarrier_ShouldRetainExactlyOneTerminalDecision()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var service = BuildService();
        var pending = await service.InitiatePaymentAsync(
            Guid.NewGuid(),
            PaymentRequest(1_000m, new DateOnly(2026, 2, 1), null, null),
            cts.Token);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<(PendingPaymentDto? Payment, Exception? Error)> CaptureAsync(
            Func<Task<PendingPaymentDto?>> transition)
        {
            await start.Task.WaitAsync(cts.Token);
            try
            {
                return (await transition(), null);
            }
            catch (Exception exception)
            {
                return (null, exception);
            }
        }

        var approval = Task.Run(
            () => CaptureAsync(() => service.ApprovePaymentAsync(
                pending.PendingPaymentId,
                new ApprovePaymentRequest("approve", "approver"),
                cts.Token)),
            cts.Token);
        var rejection = Task.Run(
            () => CaptureAsync(() => service.RejectPaymentAsync(
                pending.PendingPaymentId,
                new RejectPaymentRequest("reject", "rejector"),
                cts.Token)),
            cts.Token);
        start.SetResult();

        var outcomes = await Task.WhenAll(approval, rejection);

        outcomes.Should().ContainSingle(outcome => outcome.Payment is not null);
        outcomes.Should().ContainSingle(outcome => outcome.Error is BankingConflictException);
        var retained = await service.GetPaymentAsync(pending.PendingPaymentId, cts.Token);
        retained!.Status.Should().BeOneOf(PaymentApprovalStatus.Approved, PaymentApprovalStatus.Rejected);
        retained.Status.Should().Be(outcomes.Single(outcome => outcome.Payment is not null).Payment!.Status);
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
            entityId, PaymentRequest(3_000m, new DateOnly(2026, 2, 15), null, null));

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
            PaymentRequest(3_000m, new DateOnly(2026, 2, 15), ExternalRef: "ext-reject-automation", Notes: null));

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
            entityId, PaymentRequest(500m, new DateOnly(2026, 2, 1), null, null));

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
            entityId, PaymentRequest(1_000m, new DateOnly(2026, 1, 10), null, null));

        await service.ApprovePaymentAsync(pending.PendingPaymentId, new ApprovePaymentRequest(null, null));

        var act = () => service.RejectPaymentAsync(
            pending.PendingPaymentId, new RejectPaymentRequest("Too late", null));

        var ex = await Assert.ThrowsAsync<BankingException>(act);
        ex.Message.Should().Contain("not in Pending status");
    }
}
