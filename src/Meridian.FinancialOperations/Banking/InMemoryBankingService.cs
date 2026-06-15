using Meridian.Contracts.Banking;

namespace Meridian.FinancialOperations.Banking;

/// <summary>
/// In-memory implementation of <see cref="IBankingService"/>.
/// Holds pending payments and bank transactions in process memory; suitable for
/// testing and non-persistent deployments.
/// </summary>
public sealed class InMemoryBankingService : IBankingService
{
    private readonly object _gate = new object();
    private readonly Dictionary<Guid, PendingPaymentDto> _pendingPayments = new();
    private readonly Dictionary<Guid, List<BankTransactionDto>> _bankTransactions = new();

    // -----------------------------------------------------------------------
    // Payment initiation & approval workflow
    // -----------------------------------------------------------------------

    public Task<PendingPaymentDto> InitiatePaymentAsync(
        Guid entityId,
        InitiatePaymentRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Amount <= 0m)
        {
            throw new BankingException("Payment amount must be positive.");
        }

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

        lock (_gate)
        {
            _pendingPayments[pending.PendingPaymentId] = pending;
        }

        return Task.FromResult(pending);
    }

    public Task<PendingPaymentDto?> ApprovePaymentAsync(
        Guid pendingPaymentId,
        ApprovePaymentRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, "approve payment requests");

        lock (_gate)
        {
            if (!_pendingPayments.TryGetValue(pendingPaymentId, out var pending))
            {
                return Task.FromResult<PendingPaymentDto?>(null);
            }

            if (pending.Status != PaymentApprovalStatus.Pending)
            {
                throw new BankingException(
                    $"Payment '{pendingPaymentId}' is not in Pending status (current: {pending.Status}).");
            }

            var approved = pending with
            {
                Status = PaymentApprovalStatus.Approved,
                ReviewedBy = request.ReviewedBy,
                ReviewNotes = request.ReviewNotes,
                ReviewedAt = DateTimeOffset.UtcNow
            };
            _pendingPayments[pendingPaymentId] = approved;

            return Task.FromResult<PendingPaymentDto?>(approved);
        }
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

    public Task<PendingPaymentDto?> RejectPaymentAsync(
        Guid pendingPaymentId,
        RejectPaymentRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, "reject payments");
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new BankingException("Rejection reason is required.");
        }

        lock (_gate)
        {
            if (!_pendingPayments.TryGetValue(pendingPaymentId, out var pending))
            {
                return Task.FromResult<PendingPaymentDto?>(null);
            }

            if (pending.Status != PaymentApprovalStatus.Pending)
            {
                throw new BankingException(
                    $"Payment '{pendingPaymentId}' is not in Pending status (current: {pending.Status}).");
            }

            var rejected = pending with
            {
                Status = PaymentApprovalStatus.Rejected,
                ReviewedBy = request.ReviewedBy,
                ReviewNotes = request.Reason,
                ReviewedAt = DateTimeOffset.UtcNow
            };
            _pendingPayments[pendingPaymentId] = rejected;
            return Task.FromResult<PendingPaymentDto?>(rejected);
        }
    }

    public Task<PendingPaymentDto?> GetPaymentAsync(Guid pendingPaymentId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _pendingPayments.TryGetValue(pendingPaymentId, out var pending);
            return Task.FromResult(pending);
        }
    }

    public Task<BankTransactionDto?> RecordPaymentBankEvidenceAsync(
        Guid pendingPaymentId,
        RecordPaymentBankEvidenceRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_pendingPayments.TryGetValue(pendingPaymentId, out var pending))
            {
                return Task.FromResult<BankTransactionDto?>(null);
            }

            if (pending.Status != PaymentApprovalStatus.Approved)
            {
                throw new BankingException(
                    $"Payment '{pendingPaymentId}' must be approved before bank confirmation, return, or reversal evidence is recorded.");
            }

            var transaction = BuildPaymentBankEvidenceTransaction(pending, request);
            GetOrCreateList(pending.EntityId).Add(transaction);
            return Task.FromResult<BankTransactionDto?>(transaction);
        }
    }

    public Task<IReadOnlyList<PendingPaymentDto>> GetPendingPaymentsAsync(
        Guid? entityId = null,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            IEnumerable<PendingPaymentDto> query = _pendingPayments.Values
                .Where(static p => p.Status == PaymentApprovalStatus.Pending);

            if (entityId.HasValue)
            {
                query = query.Where(p => p.EntityId == entityId.Value);
            }

            IReadOnlyList<PendingPaymentDto> result = query
                .OrderByDescending(static p => p.InitiatedAt)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    // -----------------------------------------------------------------------
    // Bank transaction records
    // -----------------------------------------------------------------------

    public Task<IReadOnlyList<BankTransactionDto>> GetBankTransactionsAsync(
        Guid? entityId = null,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            IEnumerable<BankTransactionDto> query = entityId.HasValue
                ? (GetOrCreateList(entityId.Value) as IEnumerable<BankTransactionDto>)
                : _bankTransactions.Values.SelectMany(static l => l);

            IReadOnlyList<BankTransactionDto> result = query
                .OrderByDescending(static t => t.EffectiveDate)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    // -----------------------------------------------------------------------
    // Bank transaction seeding
    // -----------------------------------------------------------------------

    private static readonly string[] SeedTransactionTypes =
        ["InterestPayment", "PrincipalPayment", "FeePayment", "MixedPayment", "Drawdown"];

    public Task<BankTransactionSeedResultDto> SeedBankTransactionsAsync(
        BankTransactionSeedRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CountPerEntity <= 0)
        {
            throw new BankingException("CountPerEntity must be positive.");
        }

        var rng = new Random(42); // deterministic seed for reproducibility
        var seeded = 0;
        var processedIds = new List<Guid>();

        var fromDate = request.FromDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6));
        var toDate = request.ToDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var totalDays = Math.Max(1, toDate.DayNumber - fromDate.DayNumber);

        // When no entity IDs are provided, use already-known entities from the bank transactions store
        IReadOnlyList<Guid> targetIds;
        lock (_gate)
        {
            targetIds = request.EntityIds is { Count: > 0 }
                ? request.EntityIds
                : _bankTransactions.Keys.ToArray();
        }

        lock (_gate)
        {
            foreach (var entityId in targetIds)
            {
                var list = GetOrCreateList(entityId);

                for (var i = 0; i < request.CountPerEntity; i++)
                {
                    var txDate = fromDate.AddDays(rng.Next(totalDays));
                    var txType = SeedTransactionTypes[rng.Next(SeedTransactionTypes.Length)];
                    var amount = decimal.Round(
                        (decimal)(rng.NextDouble() * 4900d) + 100m,
                        2, MidpointRounding.AwayFromZero);

                    list.Add(new BankTransactionDto(
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
                        IsVoided: false));
                    seeded++;
                }

                processedIds.Add(entityId);
            }
        }

        return Task.FromResult(new BankTransactionSeedResultDto(
            EntitiesProcessed: processedIds.Count,
            TransactionsSeeded: seeded,
            ProcessedEntityIds: processedIds));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private List<BankTransactionDto> GetOrCreateList(Guid entityId)
    {
        if (!_bankTransactions.TryGetValue(entityId, out var list))
        {
            list = new List<BankTransactionDto>();
            _bankTransactions[entityId] = list;
        }

        return list;
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
