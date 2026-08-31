using Meridian.Contracts.Banking;
using Meridian.Contracts.Operations;

namespace Meridian.FinancialOperations.Banking;

/// <summary>
/// In-memory implementation of <see cref="IBankingService"/>.
/// Holds pending payments and bank transactions in process memory; suitable for
/// testing and non-persistent deployments.
/// </summary>
public sealed class InMemoryBankingService : IBankingService
{
    private const int MaximumRemediationReasonLength = 1_000;
    private readonly object _gate = new object();
    private readonly Dictionary<Guid, PendingPaymentDto> _pendingPayments = new();
    private readonly Dictionary<Guid, List<BankTransactionDto>> _bankTransactions = new();
    private readonly Dictionary<(Guid PendingPaymentId, string EvidenceId), BankTransactionDto> _paymentEvidence = new();

    // -----------------------------------------------------------------------
    // Payment initiation & approval workflow
    // -----------------------------------------------------------------------

    public Task<PendingPaymentDto> InitiatePaymentAsync(
        Guid entityId,
        InitiatePaymentRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        if (entityId == Guid.Empty)
        {
            throw new BankingException("Payment entity id is required.");
        }

        if (request.Amount <= 0m)
        {
            throw new BankingException("Payment amount must be positive.");
        }

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
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_pendingPayments.TryGetValue(pendingPaymentId, out var pending))
            {
                return Task.FromResult<PendingPaymentDto?>(null);
            }

            if (pending.Status != PaymentApprovalStatus.Pending)
            {
                throw new BankingConflictException(
                    $"Payment '{pendingPaymentId}' is not in Pending status (current: {pending.Status}).");
            }

            _ = PaymentBankEvidenceFactory.NormalizeRequiredCurrency(
                pending.Currency,
                "Payment intent");

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

    public Task<PendingPaymentDto?> RemediatePaymentCurrencyAsync(
        Guid pendingPaymentId,
        RemediatePaymentCurrencyRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, "remediate payment currency");
        ct.ThrowIfCancellationRequested();
        var currency = PaymentBankEvidenceFactory.NormalizeRequiredCurrency(
            request.Currency,
            "Payment remediation");
        if (string.IsNullOrWhiteSpace(request.RemediatedBy))
            throw new BankingException("Payment currency remediation requires the human operator identity.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BankingException("Payment currency remediation requires a reason.");
        var reason = request.Reason.Trim();
        if (reason.Length > MaximumRemediationReasonLength)
        {
            throw new BankingException(
                $"Payment currency remediation reason cannot exceed {MaximumRemediationReasonLength} characters.");
        }

        lock (_gate)
        {
            if (!_pendingPayments.TryGetValue(pendingPaymentId, out var pending))
                return Task.FromResult<PendingPaymentDto?>(null);
            if (pending.Status != PaymentApprovalStatus.Pending)
            {
                throw new BankingConflictException(
                    $"Payment '{pendingPaymentId}' cannot be remediated after a review decision (current: {pending.Status}).");
            }
            if (pending.Currency is not null)
            {
                throw new BankingConflictException(
                    $"Payment '{pendingPaymentId}' already has retained currency '{pending.Currency}'. Currency is immutable once set.");
            }

            var remediated = pending with
            {
                Currency = currency,
                CurrencyRemediatedBy = request.RemediatedBy.Trim(),
                CurrencyRemediationReason = reason,
                CurrencyRemediatedAt = DateTimeOffset.UtcNow,
            };
            _pendingPayments[pendingPaymentId] = remediated;
            return Task.FromResult<PendingPaymentDto?>(remediated);
        }
    }

    private static void EnsureHumanOrigin(
        Meridian.Contracts.Workstation.OperationsActionOriginDto actionOrigin,
        string action)
    {
        if (!OperationsOriginGuard.IsHumanOperator(actionOrigin))
        {
            throw new BankingException(
                OperationsOriginGuard.RefusalMessage(action),
                OperationsOriginGuard.Refusal(action));
        }
    }

    public Task<PendingPaymentDto?> RejectPaymentAsync(
        Guid pendingPaymentId,
        RejectPaymentRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, "reject payments");
        ct.ThrowIfCancellationRequested();
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
                throw new BankingConflictException(
                    $"Payment '{pendingPaymentId}' is not in Pending status (current: {pending.Status}).");
            }

            var rejected = pending with
            {
                Status = PaymentApprovalStatus.Rejected,
                ReviewedBy = request.ReviewedBy,
                ReviewNotes = request.Reason.Trim(),
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
        EnsureHumanOrigin(request.ActionOrigin, "record bank evidence");
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

            var transaction = PaymentBankEvidenceFactory.Create(pending, request);
            var evidenceKey = (pendingPaymentId, transaction.EvidenceId!);
            if (_paymentEvidence.TryGetValue(evidenceKey, out var retained))
            {
                if (string.Equals(
                        retained.CanonicalInputHash,
                        transaction.CanonicalInputHash,
                        StringComparison.Ordinal))
                {
                    return Task.FromResult<BankTransactionDto?>(retained);
                }

                throw new BankingConflictException(
                    $"EvidenceId '{transaction.EvidenceId}' is already retained for payment '{pendingPaymentId}' with different input.");
            }

            ct.ThrowIfCancellationRequested();
            GetOrCreateList(pending.EntityId).Add(transaction);
            _paymentEvidence.Add(evidenceKey, transaction);
            return Task.FromResult<BankTransactionDto?>(transaction);
        }
    }

    public Task<IReadOnlyList<PendingPaymentDto>> GetPendingPaymentsAsync(
        Guid? entityId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
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
        ct.ThrowIfCancellationRequested();
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

}
