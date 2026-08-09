using Meridian.Contracts.Banking;
using Meridian.Storage.Banking;

namespace Meridian.FinancialOperations.Banking;

/// <summary>
/// PostgreSQL-backed implementation of <see cref="IBankingService"/>.
/// All mutations are persisted immediately via <see cref="IBankingStore"/>.
/// </summary>
public sealed class PostgresBankingService : IBankingService
{
    private const int MaximumRemediationReasonLength = 1_000;
    private readonly IBankingStore _store;

    public PostgresBankingService(IBankingStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    // ── Payment initiation & approval workflow ───────────────────────────────

    public async Task<PendingPaymentDto> InitiatePaymentAsync(
        Guid entityId,
        InitiatePaymentRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (entityId == Guid.Empty)
            throw new BankingException("Payment entity id is required.");

        if (request.Amount <= 0m)
            throw new BankingException("Payment amount must be positive.");

        var currency = PaymentBankEvidenceFactory.NormalizeRequiredCurrency(request.Currency, "Payment");

        var pending = new PendingPaymentDto(
            PendingPaymentId: Guid.NewGuid(),
            EntityId: entityId,
            Amount: request.Amount,
            EffectiveDate: request.EffectiveDate,
            ExternalRef: request.ExternalRef,
            Notes: request.Notes,
            Status: PaymentApprovalStatus.Pending,
            ReviewedBy: null,
            ReviewNotes: null,
            InitiatedAt: DateTimeOffset.UtcNow,
            ReviewedAt: null,
            Currency: currency);

        await _store.UpsertPendingPaymentAsync(pending, ct);
        return pending;
    }

    public async Task<PendingPaymentDto?> ApprovePaymentAsync(
        Guid pendingPaymentId,
        ApprovePaymentRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, "approve payment requests");

        var transitioned = await _store.TryTransitionPendingPaymentAsync(
            pendingPaymentId,
            PaymentApprovalStatus.Approved,
            request.ReviewedBy,
            request.ReviewNotes,
            DateTimeOffset.UtcNow,
            ct).ConfigureAwait(false);
        if (transitioned is not null)
            return transitioned;

        return await ResolveFailedTransitionAsync(
            pendingPaymentId,
            PaymentApprovalStatus.Approved,
            ct).ConfigureAwait(false);
    }

    public async Task<PendingPaymentDto?> RemediatePaymentCurrencyAsync(
        Guid pendingPaymentId,
        RemediatePaymentCurrencyRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, "remediate payment currency");
        var currency = PaymentBankEvidenceFactory.NormalizeRequiredCurrency(
            request.Currency,
            "Payment remediation");
        var actor = NormalizeRemediationActor(request.RemediatedBy);
        var reason = NormalizeRemediationReason(request.Reason);
        var remediated = await _store.TryRemediatePendingPaymentCurrencyAsync(
            pendingPaymentId,
            currency,
            actor,
            reason,
            DateTimeOffset.UtcNow,
            ct).ConfigureAwait(false);
        if (remediated is not null)
            return remediated;

        var current = await _store.GetPendingPaymentAsync(pendingPaymentId, ct).ConfigureAwait(false);
        if (current is null)
            return null;
        if (current.Status != PaymentApprovalStatus.Pending)
        {
            throw new BankingConflictException(
                $"Payment '{pendingPaymentId}' cannot be remediated after a review decision (current: {current.Status}).");
        }

        throw new BankingConflictException(
            $"Payment '{pendingPaymentId}' already has retained currency '{current.Currency}'. Currency is immutable once set.");
    }

    private static void EnsureHumanOrigin(
        Meridian.Contracts.Workstation.OperationsActionOriginDto actionOrigin,
        string action)
    {
        if (actionOrigin != Meridian.Contracts.Workstation.OperationsActionOriginDto.HumanOperator)
        {
            throw new BankingException(
                $"Reviewed automation cannot {action}; a human operator approval is required.");
        }
    }

    private static string NormalizeRemediationActor(string? actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new BankingException("Payment currency remediation requires the human operator identity.");
        return actor.Trim();
    }

    private static string NormalizeRemediationReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new BankingException("Payment currency remediation requires a reason.");

        var normalized = reason.Trim();
        if (normalized.Length > MaximumRemediationReasonLength)
        {
            throw new BankingException(
                $"Payment currency remediation reason cannot exceed {MaximumRemediationReasonLength} characters.");
        }

        return normalized;
    }

    public async Task<PendingPaymentDto?> RejectPaymentAsync(
        Guid pendingPaymentId,
        RejectPaymentRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, "reject payments");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BankingException("Rejection reason is required.");

        var transitioned = await _store.TryTransitionPendingPaymentAsync(
            pendingPaymentId,
            PaymentApprovalStatus.Rejected,
            request.ReviewedBy,
            request.Reason.Trim(),
            DateTimeOffset.UtcNow,
            ct).ConfigureAwait(false);
        if (transitioned is not null)
            return transitioned;

        return await ResolveFailedTransitionAsync(
            pendingPaymentId,
            PaymentApprovalStatus.Rejected,
            ct).ConfigureAwait(false);
    }

    public Task<PendingPaymentDto?> GetPaymentAsync(Guid pendingPaymentId, CancellationToken ct = default)
        => _store.GetPendingPaymentAsync(pendingPaymentId, ct);

    public async Task<BankTransactionDto?> RecordPaymentBankEvidenceAsync(
        Guid pendingPaymentId,
        RecordPaymentBankEvidenceRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, "record bank evidence");
        var pending = await _store.GetPendingPaymentAsync(pendingPaymentId, ct).ConfigureAwait(false);
        if (pending is null)
        {
            return null;
        }

        var bankTx = PaymentBankEvidenceFactory.Create(pending, request);
        var write = await _store.RecordPaymentBankEvidenceAsync(bankTx, ct).ConfigureAwait(false);
        return write.Status switch
        {
            PaymentBankEvidenceWriteStatus.Inserted or PaymentBankEvidenceWriteStatus.Replay
                => write.Transaction,
            PaymentBankEvidenceWriteStatus.PaymentNotFound => null,
            PaymentBankEvidenceWriteStatus.PaymentNotApproved => throw new BankingException(
                $"Payment '{pendingPaymentId}' must be approved before bank confirmation, return, or reversal evidence is recorded."),
            PaymentBankEvidenceWriteStatus.PaymentCurrencyUnresolved => throw new BankingException(
                $"Payment '{pendingPaymentId}' has no retained currency. Remediate this legacy intent before recording bank evidence."),
            PaymentBankEvidenceWriteStatus.PaymentBindingConflict => throw new BankingConflictException(
                $"Payment '{pendingPaymentId}' changed while bank evidence was being recorded; retry against the retained payment intent."),
            PaymentBankEvidenceWriteStatus.IdempotencyConflict => throw new BankingConflictException(
                $"EvidenceId '{bankTx.EvidenceId}' is already retained for payment '{pendingPaymentId}' with different input."),
            _ => throw new InvalidOperationException($"Unsupported payment evidence write status '{write.Status}'.")
        };
    }

    public async Task<IReadOnlyList<PendingPaymentDto>> GetPendingPaymentsAsync(
        Guid? entityId = null,
        CancellationToken ct = default)
    {
        var all = await _store.GetAllPendingPaymentsAsync(ct);

        IEnumerable<PendingPaymentDto> query = all
            .Where(static p => p.Status == PaymentApprovalStatus.Pending);

        if (entityId.HasValue)
            query = query.Where(p => p.EntityId == entityId.Value);

        return query.OrderByDescending(static p => p.InitiatedAt).ToArray();
    }

    // ── Bank transaction records ─────────────────────────────────────────────

    public Task<IReadOnlyList<BankTransactionDto>> GetBankTransactionsAsync(
        Guid? entityId = null,
        CancellationToken ct = default)
        => _store.GetBankTransactionsAsync(entityId, ct);

    // ── Bank transaction seeding ─────────────────────────────────────────────

    private static readonly string[] SeedTransactionTypes =
        ["InterestPayment", "PrincipalPayment", "FeePayment", "MixedPayment", "Drawdown"];

    public async Task<BankTransactionSeedResultDto> SeedBankTransactionsAsync(
        BankTransactionSeedRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CountPerEntity <= 0)
            throw new BankingException("CountPerEntity must be positive.");

        var rng = new Random(42);
        var seeded = 0;
        var processedIds = new List<Guid>();

        var fromDate = request.FromDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6));
        var toDate = request.ToDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var totalDays = Math.Max(1, toDate.DayNumber - fromDate.DayNumber);

        // When no entity IDs are provided use existing entity IDs from the transactions table
        IReadOnlyList<Guid> targetIds;
        if (request.EntityIds is { Count: > 0 })
        {
            targetIds = request.EntityIds;
        }
        else
        {
            var existing = await _store.GetBankTransactionsAsync(null, ct);
            targetIds = existing.Select(static t => t.EntityId).Distinct().ToArray();
        }

        foreach (var entityId in targetIds)
        {
            for (var i = 0; i < request.CountPerEntity; i++)
            {
                var txDate = fromDate.AddDays(rng.Next(totalDays));
                var txType = SeedTransactionTypes[rng.Next(SeedTransactionTypes.Length)];
                var amount = decimal.Round(
                    (decimal)(rng.NextDouble() * 4900d) + 100m,
                    2, MidpointRounding.AwayFromZero);

                var bankTx = new BankTransactionDto(
                    BankTransactionId: Guid.NewGuid(),
                    EntityId: entityId,
                    TransactionType: txType,
                    EffectiveDate: txDate,
                    TransactionDate: txDate,
                    SettlementDate: txDate.AddDays(2),
                    Amount: amount,
                    Currency: "USD",
                    ExternalRef: $"SEED-{i + 1:D4}-{entityId.ToString("N")[..8]}",
                    RecordedAt: DateTimeOffset.UtcNow,
                    IsVoided: false);

                await _store.InsertBankTransactionAsync(bankTx, ct);
                seeded++;
            }

            processedIds.Add(entityId);
        }

        return new BankTransactionSeedResultDto(
            EntitiesProcessed: processedIds.Count,
            TransactionsSeeded: seeded,
            ProcessedEntityIds: processedIds);
    }

    private async Task<PendingPaymentDto?> ResolveFailedTransitionAsync(
        Guid pendingPaymentId,
        PaymentApprovalStatus targetStatus,
        CancellationToken ct)
    {
        var current = await _store.GetPendingPaymentAsync(pendingPaymentId, ct).ConfigureAwait(false);
        if (current is null)
            return null;

        if (targetStatus == PaymentApprovalStatus.Approved
            && current.Status == PaymentApprovalStatus.Pending
            && string.IsNullOrWhiteSpace(current.Currency))
        {
            throw new BankingException(
                $"Payment '{pendingPaymentId}' has no retained currency. "
                + "Remediate this legacy intent before approval.");
        }

        if (targetStatus == PaymentApprovalStatus.Approved
            && current.Status == PaymentApprovalStatus.Pending)
        {
            _ = PaymentBankEvidenceFactory.NormalizeRequiredCurrency(
                current.Currency,
                "Payment intent");
        }

        throw new BankingConflictException(
            $"Payment '{pendingPaymentId}' is not in Pending status (current: {current.Status}).");
    }
}
