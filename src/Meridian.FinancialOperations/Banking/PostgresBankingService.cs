using Meridian.Contracts.Banking;
using Meridian.Storage.Banking;

namespace Meridian.FinancialOperations.Banking;

/// <summary>
/// PostgreSQL-backed implementation of <see cref="IBankingService"/>.
/// All mutations are persisted immediately via <see cref="IBankingStore"/>.
/// </summary>
public sealed class PostgresBankingService : IBankingService
{
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
        if (request.Amount <= 0m)
            throw new BankingException("Payment amount must be positive.");

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
            ReviewedAt: null);

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

        var pending = await _store.GetPendingPaymentAsync(pendingPaymentId, ct);
        if (pending is null)
            return null;

        if (pending.Status != PaymentApprovalStatus.Pending)
            throw new BankingException(
                $"Payment '{pendingPaymentId}' is not in Pending status (current: {pending.Status}).");

        var approved = pending with
        {
            Status = PaymentApprovalStatus.Approved,
            ReviewedBy = request.ReviewedBy,
            ReviewNotes = request.ReviewNotes,
            ReviewedAt = DateTimeOffset.UtcNow
        };

        await _store.UpsertPendingPaymentAsync(approved, ct);
        return approved;
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

    public async Task<PendingPaymentDto?> RejectPaymentAsync(
        Guid pendingPaymentId,
        RejectPaymentRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, "reject payments");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BankingException("Rejection reason is required.");

        var pending = await _store.GetPendingPaymentAsync(pendingPaymentId, ct);
        if (pending is null)
            return null;

        if (pending.Status != PaymentApprovalStatus.Pending)
            throw new BankingException(
                $"Payment '{pendingPaymentId}' is not in Pending status (current: {pending.Status}).");

        var rejected = pending with
        {
            Status = PaymentApprovalStatus.Rejected,
            ReviewedBy = request.ReviewedBy,
            ReviewNotes = request.Reason,
            ReviewedAt = DateTimeOffset.UtcNow
        };

        await _store.UpsertPendingPaymentAsync(rejected, ct);
        return rejected;
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

        if (pending.Status != PaymentApprovalStatus.Approved)
        {
            throw new BankingException(
                $"Payment '{pendingPaymentId}' must be approved before bank confirmation, return, or reversal evidence is recorded.");
        }

        var bankTx = BuildPaymentBankEvidenceTransaction(pending, request);
        await _store.InsertBankTransactionAsync(bankTx, ct).ConfigureAwait(false);
        return bankTx;
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

    private static BankTransactionDto BuildPaymentBankEvidenceTransaction(
        PendingPaymentDto pending,
        RecordPaymentBankEvidenceRequest request)
    {
        var evidenceType = NormalizeEvidenceType(request.EvidenceType);
        var amount = request.Amount ?? pending.Amount;
        if (amount <= 0m)
        {
            throw new BankingException("Bank evidence amount must be positive.");
        }

        var currency = string.IsNullOrWhiteSpace(request.Currency)
            ? "USD"
            : request.Currency.Trim().ToUpperInvariant();
        if (currency.Length != 3)
        {
            throw new BankingException("Bank evidence currency must be a three-letter ISO currency code.");
        }

        var transactionDate = request.TransactionDate ?? pending.EffectiveDate;
        var settlementDate = request.SettlementDate ?? transactionDate;
        if (settlementDate < transactionDate)
        {
            throw new BankingException("Bank evidence settlement date cannot be before transaction date.");
        }

        return new BankTransactionDto(
            BankTransactionId: Guid.NewGuid(),
            EntityId: pending.EntityId,
            TransactionType: evidenceType,
            EffectiveDate: pending.EffectiveDate,
            TransactionDate: transactionDate,
            SettlementDate: settlementDate,
            Amount: amount,
            Currency: currency,
            ExternalRef: FirstNonBlank(request.ExternalRef, pending.ExternalRef, pending.PendingPaymentId.ToString("D")),
            RecordedAt: DateTimeOffset.UtcNow,
            IsVoided: IsReturnOrReversalEvidence(evidenceType),
            RecordedBy: FirstNonBlank(request.RecordedBy));
    }

    private static string NormalizeEvidenceType(string? evidenceType)
        => (evidenceType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" or "confirmation" or "confirmed" or "bankconfirmation" or "bank-confirmation" => "BankConfirmation",
            "return" or "returned" or "bankreturn" or "bank-return" => "BankReturn",
            "reversal" or "reversed" or "bankreversal" or "bank-reversal" => "BankReversal",
            "failure" or "failed" or "reject" or "rejected" or "bankfailure" or "bank-failure" => "BankFailure",
            _ => throw new BankingException("Bank evidence type must be BankConfirmation, BankReturn, BankReversal, or BankFailure.")
        };

    private static bool IsReturnOrReversalEvidence(string evidenceType)
        => evidenceType is "BankReturn" or "BankReversal" or "BankFailure";

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
