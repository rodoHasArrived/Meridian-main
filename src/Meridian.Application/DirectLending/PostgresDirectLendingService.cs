using Meridian.Contracts.DirectLending;

namespace Meridian.Application.DirectLending;

public sealed partial class PostgresDirectLendingService : IDirectLendingService
{
    private readonly IDirectLendingCommandService _commandService;
    private readonly IDirectLendingQueryService _queryService;

    public PostgresDirectLendingService(
        IDirectLendingCommandService commandService,
        IDirectLendingQueryService queryService)
    {
        _commandService = commandService;
        _queryService = queryService;
    }

    public async Task<LoanContractDetailDto> CreateLoanAsync(CreateLoanRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
        => DirectLendingServiceSupport.RequireSuccess(await _commandService.CreateLoanAsync(request, metadata, ct).ConfigureAwait(false));

    public Task<LoanContractDetailDto?> GetLoanAsync(Guid loanId, CancellationToken ct = default)
        => _queryService.GetLoanAsync(loanId, ct);

    public Task<LoanContractDetailDto?> GetContractProjectionAsync(Guid loanId, CancellationToken ct = default)
        => _queryService.GetContractProjectionAsync(loanId, ct);

    public Task<LoanAggregateSnapshotDto?> RebuildStateFromHistoryAsync(Guid loanId, CancellationToken ct = default)
        => _queryService.RebuildStateFromHistoryAsync(loanId, ct);

    public Task<IReadOnlyList<LoanEventLineageDto>> GetHistoryAsync(Guid loanId, CancellationToken ct = default)
        => _queryService.GetHistoryAsync(loanId, ct);

    public Task<IReadOnlyList<LoanTermsVersionDto>> GetTermsVersionsAsync(Guid loanId, CancellationToken ct = default)
        => _queryService.GetTermsVersionsAsync(loanId, ct);

    public Task<IReadOnlyList<LoanTermsVersionDto>> GetTermsVersionProjectionsAsync(Guid loanId, CancellationToken ct = default)
        => _queryService.GetTermsVersionProjectionsAsync(loanId, ct);

    public async Task<LoanContractDetailDto?> AmendTermsAsync(Guid loanId, AmendLoanTermsRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
        => DirectLendingServiceSupport.RequireSuccess(await _commandService.AmendTermsAsync(loanId, request, metadata, ct).ConfigureAwait(false));

    public async Task<LoanContractDetailDto?> ActivateLoanAsync(Guid loanId, ActivateLoanRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
        => DirectLendingServiceSupport.RequireSuccess(await _commandService.ActivateLoanAsync(loanId, request, metadata, ct).ConfigureAwait(false));

    public Task<LoanServicingStateDto?> GetServicingStateAsync(Guid loanId, CancellationToken ct = default)
        => _queryService.GetServicingStateAsync(loanId, ct);

    public Task<LoanServicingStateDto?> GetServicingProjectionAsync(Guid loanId, CancellationToken ct = default)
        => _queryService.GetServicingProjectionAsync(loanId, ct);

    public Task<IReadOnlyList<DrawdownLotDto>> GetDrawdownLotProjectionsAsync(Guid loanId, CancellationToken ct = default)
        => _queryService.GetDrawdownLotProjectionsAsync(loanId, ct);

    public Task<IReadOnlyList<ServicingRevisionDto>> GetServicingRevisionProjectionsAsync(Guid loanId, CancellationToken ct = default)
        => _queryService.GetServicingRevisionProjectionsAsync(loanId, ct);

    public Task<IReadOnlyList<DailyAccrualEntryDto>> GetAccrualEntryProjectionsAsync(Guid loanId, CancellationToken ct = default)
        => _queryService.GetAccrualEntryProjectionsAsync(loanId, ct);

    public async Task<LoanServicingStateDto?> BookDrawdownAsync(Guid loanId, BookDrawdownRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
        => DirectLendingServiceSupport.RequireSuccess(await _commandService.BookDrawdownAsync(loanId, request, metadata, ct).ConfigureAwait(false));

    public async Task<LoanServicingStateDto?> ApplyRateResetAsync(Guid loanId, ApplyRateResetRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
        => DirectLendingServiceSupport.RequireSuccess(await _commandService.ApplyRateResetAsync(loanId, request, metadata, ct).ConfigureAwait(false));

    public async Task<LoanServicingStateDto?> ApplyPrincipalPaymentAsync(Guid loanId, ApplyPrincipalPaymentRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
        => DirectLendingServiceSupport.RequireSuccess(await _commandService.ApplyPrincipalPaymentAsync(loanId, request, metadata, ct).ConfigureAwait(false));

    public async Task<DailyAccrualEntryDto?> PostDailyAccrualAsync(Guid loanId, PostDailyAccrualRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
        => DirectLendingServiceSupport.RequireSuccess(await _commandService.PostDailyAccrualAsync(loanId, request, metadata, ct).ConfigureAwait(false));

    public async Task<LoanServicingStateDto?> ApplyMixedPaymentAsync(Guid loanId, ApplyMixedPaymentRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
        => DirectLendingServiceSupport.RequireSuccess(await _commandService.ApplyMixedPaymentAsync(loanId, request, metadata, ct).ConfigureAwait(false));

    public async Task<LoanServicingStateDto?> AssessFeeAsync(Guid loanId, AssessFeeRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
        => DirectLendingServiceSupport.RequireSuccess(await _commandService.AssessFeeAsync(loanId, request, metadata, ct).ConfigureAwait(false));

    public async Task<LoanServicingStateDto?> ApplyWriteOffAsync(Guid loanId, ApplyWriteOffRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
        => DirectLendingServiceSupport.RequireSuccess(await _commandService.ApplyWriteOffAsync(loanId, request, metadata, ct).ConfigureAwait(false));

    public Task<IReadOnlyList<CashTransactionDto>> GetCashTransactionsAsync(Guid loanId, CancellationToken ct = default)
        => _queryService.GetCashTransactionsAsync(loanId, ct);

    public Task<IReadOnlyList<PaymentAllocationDto>> GetPaymentAllocationsAsync(Guid loanId, CancellationToken ct = default)
        => _queryService.GetPaymentAllocationsAsync(loanId, ct);

    public Task<IReadOnlyList<FeeBalanceDto>> GetFeeBalancesAsync(Guid loanId, CancellationToken ct = default)
        => _queryService.GetFeeBalancesAsync(loanId, ct);

    public async Task<ProjectionRunDto> RequestProjectionAsync(Guid loanId, DateOnly? projectionAsOf = null, CancellationToken ct = default)
        => DirectLendingServiceSupport.RequireSuccess(await _commandService.RequestProjectionAsync(loanId, projectionAsOf, metadata: null, ct).ConfigureAwait(false));

    public Task<IReadOnlyList<ProjectionRunDto>> GetProjectionsAsync(Guid loanId, CancellationToken ct = default)
        => _queryService.GetProjectionsAsync(loanId, ct);

    public Task<IReadOnlyList<ProjectedCashFlowDto>> GetProjectedCashFlowsAsync(Guid projectionRunId, CancellationToken ct = default)
        => _queryService.GetProjectedCashFlowsAsync(projectionRunId, ct);

    public Task<IReadOnlyList<JournalEntryDto>> GetJournalsAsync(Guid loanId, CancellationToken ct = default)
        => _queryService.GetJournalsAsync(loanId, ct);

    public async Task<JournalEntryDto?> PostJournalAsync(Guid journalEntryId, CancellationToken ct = default)
        => DirectLendingServiceSupport.RequireSuccess(await _commandService.PostJournalAsync(journalEntryId, ct).ConfigureAwait(false));

    public async Task<ReconciliationRunDto?> ReconcileAsync(Guid loanId, CancellationToken ct = default)
        => DirectLendingServiceSupport.RequireSuccess(await _commandService.ReconcileAsync(loanId, metadata: null, ct).ConfigureAwait(false));

    public Task<IReadOnlyList<ReconciliationRunDto>> GetReconciliationRunsAsync(Guid loanId, CancellationToken ct = default)
        => _queryService.GetReconciliationRunsAsync(loanId, ct);

    public Task<IReadOnlyList<ReconciliationResultDto>> GetReconciliationResultsAsync(Guid reconciliationRunId, CancellationToken ct = default)
        => _queryService.GetReconciliationResultsAsync(reconciliationRunId, ct);

    public Task<IReadOnlyList<ReconciliationExceptionDto>> GetReconciliationExceptionsAsync(CancellationToken ct = default)
        => _queryService.GetReconciliationExceptionsAsync(ct);

    public Task<ReconciliationExceptionDto?> ResolveReconciliationExceptionAsync(Guid exceptionId, ResolveReconciliationExceptionRequest request, CancellationToken ct = default)
        => _queryService.ResolveReconciliationExceptionAsync(exceptionId, request, ct);

    public async Task<ServicerReportBatchDto> CreateServicerReportBatchAsync(CreateServicerReportBatchRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
        => DirectLendingServiceSupport.RequireSuccess(await _commandService.CreateServicerReportBatchAsync(request, metadata, ct).ConfigureAwait(false));

    public Task<ServicerReportBatchDto?> GetServicerReportBatchAsync(Guid batchId, CancellationToken ct = default)
        => _queryService.GetServicerReportBatchAsync(batchId, ct);

    public Task<IReadOnlyList<ServicerPositionReportLineDto>> GetServicerPositionLinesAsync(Guid batchId, CancellationToken ct = default)
        => _queryService.GetServicerPositionLinesAsync(batchId, ct);

    public Task<IReadOnlyList<ServicerTransactionReportLineDto>> GetServicerTransactionLinesAsync(Guid batchId, CancellationToken ct = default)
        => _queryService.GetServicerTransactionLinesAsync(batchId, ct);

    public Task<IReadOnlyList<RebuildCheckpointDto>> GetRebuildCheckpointsAsync(CancellationToken ct = default)
        => _queryService.GetRebuildCheckpointsAsync(ct);

    public async Task<IReadOnlyList<LoanAggregateSnapshotDto>> RebuildAllAsync(CancellationToken ct = default)
        => DirectLendingServiceSupport.RequireSuccess(await _commandService.RebuildAllAsync(ct).ConfigureAwait(false));

    // -----------------------------------------------------------------------
    // Payment initiation & approval workflow
    // In-process store; a future migration will persist these to Postgres.
    // -----------------------------------------------------------------------

    private readonly object _pendingGate = new();
    private readonly Dictionary<Guid, PendingPaymentDto> _pendingPayments = new();

    public Task<PendingPaymentDto> InitiatePaymentAsync(
        Guid loanId,
        InitiatePaymentRequest request,
        DirectLendingCommandMetadataDto? metadata = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Amount <= 0m)
        {
            throw new DirectLendingCommandException(new DirectLendingCommandError(
                DirectLendingErrorCode.Validation, "Payment amount must be positive."));
        }

        var pending = new PendingPaymentDto(
            PendingPaymentId: Guid.NewGuid(),
            LoanId: loanId,
            Amount: request.Amount,
            EffectiveDate: request.EffectiveDate,
            RequestedBreakdown: request.Breakdown,
            ExternalRef: request.ExternalRef,
            Notes: request.Notes,
            Status: PaymentApprovalStatus.Pending,
            ReviewedBy: null,
            ReviewNotes: null,
            InitiatedAt: DateTimeOffset.UtcNow,
            ReviewedAt: null);

        lock (_pendingGate)
        {
            _pendingPayments[pending.PendingPaymentId] = pending;
        }

        return Task.FromResult(pending);
    }

    public async Task<LoanServicingStateDto?> ApprovePaymentAsync(
        Guid pendingPaymentId,
        ApprovePaymentRequest request,
        DirectLendingCommandMetadataDto? metadata = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PendingPaymentDto? pending;
        lock (_pendingGate)
        {
            if (!_pendingPayments.TryGetValue(pendingPaymentId, out pending))
            {
                return null;
            }

            if (pending.Status != PaymentApprovalStatus.Pending)
            {
                throw new DirectLendingCommandException(new DirectLendingCommandError(
                    DirectLendingErrorCode.Validation,
                    $"Payment '{pendingPaymentId}' is not in Pending status (current: {pending.Status})."));
            }

            _pendingPayments[pendingPaymentId] = pending with
            {
                Status = PaymentApprovalStatus.Approved,
                ReviewedBy = request.ReviewedBy,
                ReviewNotes = request.ReviewNotes,
                ReviewedAt = DateTimeOffset.UtcNow
            };
        }

        var mixedRequest = new ApplyMixedPaymentRequest(
            pending.Amount,
            pending.EffectiveDate,
            pending.RequestedBreakdown,
            pending.ExternalRef);

        var result = await _commandService.ApplyMixedPaymentAsync(pending.LoanId, mixedRequest, metadata, ct).ConfigureAwait(false);
        return DirectLendingServiceSupport.RequireSuccess(result);
    }

    public Task<PendingPaymentDto?> RejectPaymentAsync(
        Guid pendingPaymentId,
        RejectPaymentRequest request,
        DirectLendingCommandMetadataDto? metadata = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new DirectLendingCommandException(new DirectLendingCommandError(
                DirectLendingErrorCode.Validation, "Rejection reason is required."));
        }

        lock (_pendingGate)
        {
            if (!_pendingPayments.TryGetValue(pendingPaymentId, out var pending))
            {
                return Task.FromResult<PendingPaymentDto?>(null);
            }

            if (pending.Status != PaymentApprovalStatus.Pending)
            {
                throw new DirectLendingCommandException(new DirectLendingCommandError(
                    DirectLendingErrorCode.Validation,
                    $"Payment '{pendingPaymentId}' is not in Pending status (current: {pending.Status})."));
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

    public Task<IReadOnlyList<PendingPaymentDto>> GetPendingPaymentsAsync(
        Guid? loanId = null,
        CancellationToken ct = default)
    {
        lock (_pendingGate)
        {
            IEnumerable<PendingPaymentDto> query = _pendingPayments.Values
                .Where(static p => p.Status == PaymentApprovalStatus.Pending);

            if (loanId.HasValue)
            {
                query = query.Where(p => p.LoanId == loanId.Value);
            }

            IReadOnlyList<PendingPaymentDto> result = query
                .OrderByDescending(static p => p.InitiatedAt)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    // -----------------------------------------------------------------------
    // Bank transaction seeding
    // Full Postgres-side seeding requires direct DB writes not yet wired up;
    // return an acknowledgement so the endpoint is functional.
    // -----------------------------------------------------------------------

    public Task<BankTransactionSeedResultDto> SeedBankTransactionsAsync(
        BankTransactionSeedRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CountPerLoan <= 0)
        {
            throw new DirectLendingCommandException(new DirectLendingCommandError(
                DirectLendingErrorCode.Validation, "CountPerLoan must be positive."));
        }

        return Task.FromResult(new BankTransactionSeedResultDto(
            LoansProcessed: 0,
            TransactionsSeeded: 0,
            ProcessedLoanIds: Array.Empty<Guid>()));
    }
}
