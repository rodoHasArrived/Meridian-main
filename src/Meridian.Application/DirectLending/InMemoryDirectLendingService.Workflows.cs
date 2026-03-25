using System.Text.Json;
using Meridian.Contracts.DirectLending;

namespace Meridian.Application.DirectLending;

public sealed partial class InMemoryDirectLendingService
{
    private readonly Dictionary<Guid, List<CashTransactionDto>> _cashTransactions = new();
    private readonly Dictionary<Guid, List<PaymentAllocationDto>> _paymentAllocations = new();
    private readonly Dictionary<Guid, List<FeeBalanceDto>> _feeBalances = new();
    private readonly Dictionary<Guid, List<ProjectionRunDto>> _projectionRuns = new();
    private readonly Dictionary<Guid, List<ProjectedCashFlowDto>> _projectedCashFlows = new();
    private readonly Dictionary<Guid, List<JournalEntryDto>> _journals = new();
    private readonly Dictionary<Guid, List<ReconciliationRunDto>> _reconciliationRuns = new();
    private readonly Dictionary<Guid, List<ReconciliationResultDto>> _reconciliationResults = new();
    private readonly List<ReconciliationExceptionDto> _reconciliationExceptions = new();
    private readonly Dictionary<Guid, ServicerReportBatchDto> _servicerBatches = new();
    private readonly Dictionary<Guid, List<ServicerPositionReportLineDto>> _servicerPositionLines = new();
    private readonly Dictionary<Guid, List<ServicerTransactionReportLineDto>> _servicerTransactionLines = new();
    private readonly List<RebuildCheckpointDto> _checkpoints = new();

    public Task<LoanServicingStateDto?> ApplyMixedPaymentAsync(Guid loanId, ApplyMixedPaymentRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Amount <= 0m)
        {
            throw new DirectLendingCommandException(new DirectLendingCommandError(DirectLendingErrorCode.Validation, "Payment amount must be positive."));
        }

        lock (_gate)
        {
            if (!_loans.TryGetValue(loanId, out var stored))
            {
                return Task.FromResult<LoanServicingStateDto?>(null);
            }

            var resolution = DirectLendingServiceSupport.ResolveMixedPayment(ToServicingState(stored), request.Amount, request.Breakdown);
            var breakdown = resolution.Breakdown;
            ApplyPaymentToLots(stored.Servicing.DrawdownLots, breakdown.ToPrincipal);
            stored.Servicing = stored.Servicing with
            {
                TotalDrawn = Math.Max(0m, stored.Servicing.TotalDrawn - breakdown.ToPrincipal),
                AvailableToDraw = Meridian.FSharp.DirectLendingInterop.DirectLendingInterop.CalculateAvailableToDraw(stored.Servicing.CurrentCommitment, Math.Max(0m, stored.Servicing.TotalDrawn - breakdown.ToPrincipal)),
                Balances = stored.Servicing.Balances with
                {
                    PrincipalOutstanding = Math.Max(0m, stored.Servicing.Balances.PrincipalOutstanding - breakdown.ToPrincipal),
                    InterestAccruedUnpaid = Math.Max(0m, stored.Servicing.Balances.InterestAccruedUnpaid - breakdown.ToInterest),
                    CommitmentFeeAccruedUnpaid = Math.Max(0m, stored.Servicing.Balances.CommitmentFeeAccruedUnpaid - breakdown.ToCommitmentFee),
                    FeesAccruedUnpaid = Math.Max(0m, stored.Servicing.Balances.FeesAccruedUnpaid - breakdown.ToFees),
                    PenaltyAccruedUnpaid = Math.Max(0m, stored.Servicing.Balances.PenaltyAccruedUnpaid - breakdown.ToPenalty)
                },
                LastPaymentDate = request.EffectiveDate
            };
            AppendRevision(stored, "InternalEvent", request.EffectiveDate, $"Mixed payment applied for {request.Amount:0.00}.");
            AppendEvent(stored, "loan.mixed-payment-applied", request.EffectiveDate, new { loanId, request.Amount, request.EffectiveDate, resolution }, metadata);

            var cashTxn = new CashTransactionDto(Guid.NewGuid(), loanId, "MixedPayment", request.EffectiveDate, request.EffectiveDate, request.EffectiveDate, request.Amount, stored.TermsVersions[^1].Terms.BaseCurrency, request.ExternalRef, stored.History[^1].EventId, DateTimeOffset.UtcNow, false);
            GetList(_cashTransactions, loanId).Add(cashTxn);
            var allocations = GetList(_paymentAllocations, loanId);
            var seq = allocations.Count + 1;
            AddAllocation(breakdown.ToInterest, "InterestAccrued");
            AddAllocation(breakdown.ToCommitmentFee, "CommitmentFeeAccrued");
            AddAllocation(breakdown.ToFees, "FeeBalance");
            AddAllocation(breakdown.ToPenalty, "PenaltyAccrued");
            AddAllocation(breakdown.ToPrincipal, "PrincipalOutstanding");
            return Task.FromResult<LoanServicingStateDto?>(ToServicingState(stored));

            void AddAllocation(decimal amount, string targetType)
            {
                if (amount <= 0m) return;
                allocations.Add(new PaymentAllocationDto(Guid.NewGuid(), loanId, cashTxn.CashTransactionId, seq++, targetType, Guid.NewGuid().ToString("D"), amount, "Waterfall", stored.History[^1].EventId, DateTimeOffset.UtcNow));
            }
        }
    }

    public Task<LoanServicingStateDto?> AssessFeeAsync(Guid loanId, AssessFeeRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Amount <= 0m)
        {
            throw new DirectLendingCommandException(new DirectLendingCommandError(DirectLendingErrorCode.Validation, "Fee amount must be positive."));
        }

        lock (_gate)
        {
            if (!_loans.TryGetValue(loanId, out var stored))
            {
                return Task.FromResult<LoanServicingStateDto?>(null);
            }

            stored.Servicing = stored.Servicing with
            {
                Balances = stored.Servicing.Balances with
                {
                    FeesAccruedUnpaid = stored.Servicing.Balances.FeesAccruedUnpaid + request.Amount
                }
            };
            AppendRevision(stored, "InternalEvent", request.EffectiveDate, $"Fee assessed for {request.Amount:0.00}.");
            AppendEvent(stored, "loan.fee-assessed", request.EffectiveDate, new { loanId, request.FeeType, request.Amount, request.EffectiveDate, request.Note }, metadata);
            GetList(_feeBalances, loanId).Add(new FeeBalanceDto(Guid.NewGuid(), loanId, request.FeeType, request.EffectiveDate, request.Amount, request.Amount, stored.History[^1].EventId, request.Note, DateTimeOffset.UtcNow));
            return Task.FromResult<LoanServicingStateDto?>(ToServicingState(stored));
        }
    }

    public Task<LoanServicingStateDto?> ApplyWriteOffAsync(Guid loanId, ApplyWriteOffRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Amount <= 0m)
        {
            throw new DirectLendingCommandException(new DirectLendingCommandError(DirectLendingErrorCode.Validation, "Write-off amount must be positive."));
        }

        lock (_gate)
        {
            if (!_loans.TryGetValue(loanId, out var stored))
            {
                return Task.FromResult<LoanServicingStateDto?>(null);
            }

            var amount = Math.Min(request.Amount, stored.Servicing.Balances.PrincipalOutstanding);
            ApplyPaymentToLots(stored.Servicing.DrawdownLots, amount);
            stored.Servicing = stored.Servicing with
            {
                TotalDrawn = Math.Max(0m, stored.Servicing.TotalDrawn - amount),
                AvailableToDraw = Meridian.FSharp.DirectLendingInterop.DirectLendingInterop.CalculateAvailableToDraw(stored.Servicing.CurrentCommitment, Math.Max(0m, stored.Servicing.TotalDrawn - amount)),
                Balances = stored.Servicing.Balances with
                {
                    PrincipalOutstanding = Math.Max(0m, stored.Servicing.Balances.PrincipalOutstanding - amount)
                }
            };
            AppendRevision(stored, "InternalEvent", request.EffectiveDate, request.Reason);
            AppendEvent(stored, "loan.write-off-applied", request.EffectiveDate, new { loanId, Amount = amount, request.EffectiveDate, request.Reason }, metadata);
            return Task.FromResult<LoanServicingStateDto?>(ToServicingState(stored));
        }
    }

    public Task<IReadOnlyList<CashTransactionDto>> GetCashTransactionsAsync(Guid loanId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CashTransactionDto>>(GetList(_cashTransactions, loanId).ToArray());
    public Task<IReadOnlyList<PaymentAllocationDto>> GetPaymentAllocationsAsync(Guid loanId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PaymentAllocationDto>>(GetList(_paymentAllocations, loanId).ToArray());
    public Task<IReadOnlyList<FeeBalanceDto>> GetFeeBalancesAsync(Guid loanId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FeeBalanceDto>>(GetList(_feeBalances, loanId).ToArray());

    public Task<ProjectionRunDto> RequestProjectionAsync(Guid loanId, DateOnly? projectionAsOf = null, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_loans.TryGetValue(loanId, out var stored))
            {
                throw new DirectLendingCommandException(new DirectLendingCommandError(DirectLendingErrorCode.NotFound, $"Loan '{loanId}' was not found."));
            }
            var run = new ProjectionRunDto(Guid.NewGuid(), loanId, stored.TermsVersions[^1].VersionNumber, stored.Servicing.ServicingRevision, projectionAsOf ?? stored.TermsVersions[^1].Terms.MaturityDate, null, stored.History.LastOrDefault()?.EventId, "manual.request", ComputeTermsHash(stored.TermsVersions[^1].Terms), "in-memory", ProjectionRunStatus.Completed, GetList(_projectionRuns, loanId).LastOrDefault()?.ProjectionRunId, DateTimeOffset.UtcNow);
            var flows = BuildFlows(stored, run);
            GetList(_projectionRuns, loanId).Add(run);
            _projectedCashFlows[run.ProjectionRunId] = flows.ToList();
            return Task.FromResult(run);
        }
    }

    public Task<IReadOnlyList<ProjectionRunDto>> GetProjectionsAsync(Guid loanId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProjectionRunDto>>(GetList(_projectionRuns, loanId).OrderByDescending(static x => x.GeneratedAt).ToArray());
    public Task<IReadOnlyList<ProjectedCashFlowDto>> GetProjectedCashFlowsAsync(Guid projectionRunId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProjectedCashFlowDto>>((_projectedCashFlows.TryGetValue(projectionRunId, out var flows) ? flows : []).ToArray());

    public Task<IReadOnlyList<JournalEntryDto>> GetJournalsAsync(Guid loanId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<JournalEntryDto>>(GetList(_journals, loanId).ToArray());

    public Task<JournalEntryDto?> PostJournalAsync(Guid journalEntryId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            foreach (var bucket in _journals.Values)
            {
                var index = bucket.FindIndex(item => item.JournalEntryId == journalEntryId);
                if (index >= 0)
                {
                    bucket[index] = bucket[index] with { PostedAt = DateTimeOffset.UtcNow, Status = JournalEntryStatus.Posted };
                    return Task.FromResult<JournalEntryDto?>(bucket[index]);
                }
            }

            return Task.FromResult<JournalEntryDto?>(null);
        }
    }

    public Task<ReconciliationRunDto?> ReconcileAsync(Guid loanId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_loans.ContainsKey(loanId))
            {
                throw new DirectLendingCommandException(new DirectLendingCommandError(DirectLendingErrorCode.NotFound, $"Loan '{loanId}' was not found."));
            }
            var latestProjection = GetList(_projectionRuns, loanId).OrderByDescending(static x => x.GeneratedAt).FirstOrDefault();
            if (latestProjection is null)
            {
                latestProjection = RequestProjectionAsync(loanId, null, ct).Result;
            }

            var runId = Guid.NewGuid();
            var run = new ReconciliationRunDto(runId, loanId, latestProjection.ProjectionRunId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Completed");
            var flows = _projectedCashFlows.TryGetValue(latestProjection.ProjectionRunId, out var projected) ? projected : [];
            var cashTransactions = GetList(_cashTransactions, loanId);
            var results = new List<ReconciliationResultDto>();

            foreach (var flow in flows)
            {
                var cash = cashTransactions.FirstOrDefault(x => x.SettlementDate == flow.DueDate && x.Amount == flow.Amount);
                var result = new ReconciliationResultDto(
                    Guid.NewGuid(),
                    runId,
                    loanId,
                    flow.ProjectedCashFlowId,
                    cash?.CashTransactionId,
                    cash is null ? "MissingCash" : "Matched",
                    flow.Amount,
                    cash?.Amount,
                    cash is null ? flow.Amount : 0m,
                    flow.DueDate,
                    cash?.SettlementDate,
                    cash is null ? "ExpectedFlowVsCash" : "ExactAmountDate",
                    "{\"amountTolerance\":0,\"dateToleranceDays\":0}",
                    cash is null ? ["Projected flow did not match cash transaction."] : ["Matched."],
                    DateTimeOffset.UtcNow);
                results.Add(result);

                if (cash is null)
                {
                    _reconciliationExceptions.Add(new ReconciliationExceptionDto(
                        Guid.NewGuid(),
                        result.ReconciliationResultId,
                        "MissingCash",
                        "High",
                        "Open",
                        null,
                        null,
                        DateTimeOffset.UtcNow,
                        null));
                }
            }

            foreach (var cash in cashTransactions.Where(cash => flows.All(flow => flow.DueDate != cash.SettlementDate || flow.Amount != cash.Amount)))
            {
                var result = new ReconciliationResultDto(
                    Guid.NewGuid(),
                    runId,
                    loanId,
                    null,
                    cash.CashTransactionId,
                    "UnexpectedCash",
                    null,
                    cash.Amount,
                    cash.Amount,
                    null,
                    cash.SettlementDate,
                    "UnexpectedCash",
                    "{\"amountTolerance\":0,\"dateToleranceDays\":0}",
                    ["Cash transaction did not match any projected flow."],
                    DateTimeOffset.UtcNow);
                results.Add(result);
                _reconciliationExceptions.Add(new ReconciliationExceptionDto(
                    Guid.NewGuid(),
                    result.ReconciliationResultId,
                    "UnexpectedCash",
                    "Medium",
                    "Open",
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    null));
            }

            _reconciliationResults[runId] = results;
            GetList(_reconciliationRuns, loanId).Add(run);
            return Task.FromResult<ReconciliationRunDto?>(run);
        }
    }

    public Task<IReadOnlyList<ReconciliationRunDto>> GetReconciliationRunsAsync(Guid loanId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReconciliationRunDto>>(GetList(_reconciliationRuns, loanId).ToArray());
    public Task<IReadOnlyList<ReconciliationResultDto>> GetReconciliationResultsAsync(Guid reconciliationRunId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReconciliationResultDto>>((_reconciliationResults.TryGetValue(reconciliationRunId, out var results) ? results : []).ToArray());
    public Task<IReadOnlyList<ReconciliationExceptionDto>> GetReconciliationExceptionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReconciliationExceptionDto>>(_reconciliationExceptions.ToArray());

    public Task<ReconciliationExceptionDto?> ResolveReconciliationExceptionAsync(Guid exceptionId, ResolveReconciliationExceptionRequest request, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var index = _reconciliationExceptions.FindIndex(item => item.ExceptionId == exceptionId);
            if (index < 0)
            {
                return Task.FromResult<ReconciliationExceptionDto?>(null);
            }

            _reconciliationExceptions[index] = _reconciliationExceptions[index] with
            {
                Status = "Resolved",
                ResolutionNote = request.ResolutionNote,
                AssignedTo = request.AssignedTo,
                ResolvedAt = DateTimeOffset.UtcNow
            };
            return Task.FromResult<ReconciliationExceptionDto?>(_reconciliationExceptions[index]);
        }
    }

    public Task<ServicerReportBatchDto> CreateServicerReportBatchAsync(CreateServicerReportBatchRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var batchId = Guid.NewGuid();
            var batch = new ServicerReportBatchDto(batchId, request.ServicerName, request.ReportType, request.SourceFormat, request.ReportAsOfDate, DateTimeOffset.UtcNow, request.FileName, request.FileHash, (request.PositionLines?.Count ?? 0) + (request.TransactionLines?.Count ?? 0), "Loaded", metadata?.SourceSystem, request.Notes);
            _servicerBatches[batchId] = batch;
            _servicerPositionLines[batchId] = (request.PositionLines ?? []).Select(line => new ServicerPositionReportLineDto(Guid.NewGuid(), batchId, line.LoanId, request.ReportAsOfDate, line.PrincipalOutstanding, line.InterestAccruedUnpaid, line.FeesAccruedUnpaid, line.PenaltyAccruedUnpaid, line.CommitmentAvailable, line.NextDueDate, line.NextDueAmount, line.DelinquencyStatus, line.RawPayloadJson)).ToList();
            _servicerTransactionLines[batchId] = (request.TransactionLines ?? []).Select(line => new ServicerTransactionReportLineDto(Guid.NewGuid(), batchId, line.LoanId, null, line.TransactionType, line.EffectiveDate, line.TransactionDate, line.SettlementDate, line.GrossAmount, line.PrincipalAmount, line.InterestAmount, line.FeeAmount, line.PenaltyAmount, line.Currency, line.ExternalRef, line.RawPayloadJson)).ToList();
            return Task.FromResult(batch);
        }
    }

    public Task<ServicerReportBatchDto?> GetServicerReportBatchAsync(Guid batchId, CancellationToken ct = default) => Task.FromResult(_servicerBatches.TryGetValue(batchId, out var batch) ? batch : null);
    public Task<IReadOnlyList<ServicerPositionReportLineDto>> GetServicerPositionLinesAsync(Guid batchId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ServicerPositionReportLineDto>>((_servicerPositionLines.TryGetValue(batchId, out var lines) ? lines : []).ToArray());
    public Task<IReadOnlyList<ServicerTransactionReportLineDto>> GetServicerTransactionLinesAsync(Guid batchId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ServicerTransactionReportLineDto>>((_servicerTransactionLines.TryGetValue(batchId, out var lines) ? lines : []).ToArray());
    public Task<IReadOnlyList<RebuildCheckpointDto>> GetRebuildCheckpointsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RebuildCheckpointDto>>(_checkpoints.ToArray());

    public Task<IReadOnlyList<LoanAggregateSnapshotDto>> RebuildAllAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            _checkpoints.Clear();
            _checkpoints.Add(new RebuildCheckpointDto("direct-lending.full-rebuild", _loans.Values.Sum(static x => x.AggregateVersion), null, DateTimeOffset.UtcNow, "Completed", $"Rebuilt {_loans.Count} loans."));
            return Task.FromResult<IReadOnlyList<LoanAggregateSnapshotDto>>(_loans.Values.Select(stored => new LoanAggregateSnapshotDto(stored.LoanId, stored.AggregateVersion, ToContractDetail(stored), ToServicingState(stored))).ToArray());
        }
    }

    private static List<T> GetList<T>(Dictionary<Guid, List<T>> source, Guid key)
    {
        if (!source.TryGetValue(key, out var list))
        {
            list = new List<T>();
            source[key] = list;
        }

        return list;
    }

    private static IReadOnlyList<ProjectedCashFlowDto> BuildFlows(StoredLoan stored, ProjectionRunDto run)
    {
        var flows = new List<ProjectedCashFlowDto>();
        var currentDate = stored.ActivationDate ?? stored.EffectiveDate;
        var seq = 1;
        var annualRate = ResolveAnnualRate(stored.TermsVersions[^1].Terms, stored.Servicing.CurrentRateReset);
        while (currentDate < stored.TermsVersions[^1].Terms.MaturityDate && currentDate < run.ProjectionAsOf)
        {
            var nextDate = currentDate.AddMonths(1);
            if (nextDate > stored.TermsVersions[^1].Terms.MaturityDate) nextDate = stored.TermsVersions[^1].Terms.MaturityDate;
            var amount = Meridian.FSharp.DirectLendingInterop.DirectLendingInterop.CalculateDailyAccrualAmount(stored.Servicing.Balances.PrincipalOutstanding, annualRate, (int)stored.TermsVersions[^1].Terms.DayCountBasis) * Math.Max(1, nextDate.DayNumber - currentDate.DayNumber);
            flows.Add(new ProjectedCashFlowDto(Guid.NewGuid(), run.ProjectionRunId, stored.LoanId, seq++, "Interest", nextDate, currentDate, nextDate, decimal.Round(amount, 2, MidpointRounding.AwayFromZero), stored.TermsVersions[^1].Terms.BaseCurrency, stored.Servicing.Balances.PrincipalOutstanding, annualRate, JsonSerializer.Serialize(new { type = "interest" }), DateTimeOffset.UtcNow));
            currentDate = nextDate;
        }

        if (stored.Servicing.Balances.PrincipalOutstanding > 0m)
        {
            flows.Add(new ProjectedCashFlowDto(Guid.NewGuid(), run.ProjectionRunId, stored.LoanId, seq, "Principal", stored.TermsVersions[^1].Terms.MaturityDate, null, null, stored.Servicing.Balances.PrincipalOutstanding, stored.TermsVersions[^1].Terms.BaseCurrency, stored.Servicing.Balances.PrincipalOutstanding, null, JsonSerializer.Serialize(new { type = "principal" }), DateTimeOffset.UtcNow));
        }

        return flows;
    }

    // -----------------------------------------------------------------------
    // Payment initiation & approval workflow
    // -----------------------------------------------------------------------

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

        lock (_gate)
        {
            if (!_loans.ContainsKey(loanId))
            {
                throw new DirectLendingCommandException(new DirectLendingCommandError(
                    DirectLendingErrorCode.NotFound, $"Loan '{loanId}' not found."));
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

            _pendingPayments[pending.PendingPaymentId] = pending;
            return Task.FromResult(pending);
        }
    }

    public Task<LoanServicingStateDto?> ApprovePaymentAsync(
        Guid pendingPaymentId,
        ApprovePaymentRequest request,
        DirectLendingCommandMetadataDto? metadata = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            if (!_pendingPayments.TryGetValue(pendingPaymentId, out var pending))
            {
                return Task.FromResult<LoanServicingStateDto?>(null);
            }

            if (pending.Status != PaymentApprovalStatus.Pending)
            {
                throw new DirectLendingCommandException(new DirectLendingCommandError(
                    DirectLendingErrorCode.Validation,
                    $"Payment '{pendingPaymentId}' is not in Pending status (current: {pending.Status})."));
            }

            if (!_loans.TryGetValue(pending.LoanId, out var stored))
            {
                return Task.FromResult<LoanServicingStateDto?>(null);
            }

            // Mark as approved
            _pendingPayments[pendingPaymentId] = pending with
            {
                Status = PaymentApprovalStatus.Approved,
                ReviewedBy = request.ReviewedBy,
                ReviewNotes = request.ReviewNotes,
                ReviewedAt = DateTimeOffset.UtcNow
            };

            // Apply the payment via the mixed-payment path
            var mixedRequest = new ApplyMixedPaymentRequest(
                pending.Amount,
                pending.EffectiveDate,
                pending.RequestedBreakdown,
                pending.ExternalRef);

            var resolution = DirectLendingServiceSupport.ResolveMixedPayment(
                ToServicingState(stored), pending.Amount, mixedRequest.Breakdown);
            var breakdown = resolution.Breakdown;

            ApplyPaymentToLots(stored.Servicing.DrawdownLots, breakdown.ToPrincipal);
            stored.Servicing = stored.Servicing with
            {
                TotalDrawn = Math.Max(0m, stored.Servicing.TotalDrawn - breakdown.ToPrincipal),
                AvailableToDraw = Meridian.FSharp.DirectLendingInterop.DirectLendingInterop.CalculateAvailableToDraw(
                    stored.Servicing.CurrentCommitment,
                    Math.Max(0m, stored.Servicing.TotalDrawn - breakdown.ToPrincipal)),
                Balances = stored.Servicing.Balances with
                {
                    PrincipalOutstanding = Math.Max(0m, stored.Servicing.Balances.PrincipalOutstanding - breakdown.ToPrincipal),
                    InterestAccruedUnpaid = Math.Max(0m, stored.Servicing.Balances.InterestAccruedUnpaid - breakdown.ToInterest),
                    CommitmentFeeAccruedUnpaid = Math.Max(0m, stored.Servicing.Balances.CommitmentFeeAccruedUnpaid - breakdown.ToCommitmentFee),
                    FeesAccruedUnpaid = Math.Max(0m, stored.Servicing.Balances.FeesAccruedUnpaid - breakdown.ToFees),
                    PenaltyAccruedUnpaid = Math.Max(0m, stored.Servicing.Balances.PenaltyAccruedUnpaid - breakdown.ToPenalty)
                },
                LastPaymentDate = pending.EffectiveDate
            };

            AppendRevision(stored, "PaymentApproval", pending.EffectiveDate,
                $"Approved payment {pending.Amount:0.00} (pending id: {pendingPaymentId}).");
            AppendEvent(stored, "loan.payment-approved", pending.EffectiveDate, new
            {
                loanId = pending.LoanId,
                pendingPaymentId,
                pending.Amount,
                pending.EffectiveDate,
                pending.ExternalRef,
                resolution
            }, metadata);

            var cashTxn = new CashTransactionDto(
                Guid.NewGuid(), stored.LoanId, "ApprovedPayment",
                pending.EffectiveDate, pending.EffectiveDate, pending.EffectiveDate,
                pending.Amount, stored.TermsVersions[^1].Terms.BaseCurrency,
                pending.ExternalRef, stored.History[^1].EventId,
                DateTimeOffset.UtcNow, false);
            GetList(_cashTransactions, stored.LoanId).Add(cashTxn);

            var allocations = GetList(_paymentAllocations, stored.LoanId);
            var seq = allocations.Count + 1;
            AddAllocation(breakdown.ToInterest, "InterestAccrued");
            AddAllocation(breakdown.ToCommitmentFee, "CommitmentFeeAccrued");
            AddAllocation(breakdown.ToFees, "FeeBalance");
            AddAllocation(breakdown.ToPenalty, "PenaltyAccrued");
            AddAllocation(breakdown.ToPrincipal, "PrincipalOutstanding");

            return Task.FromResult<LoanServicingStateDto?>(ToServicingState(stored));

            void AddAllocation(decimal amount, string targetType)
            {
                if (amount <= 0m) return;
                allocations.Add(new PaymentAllocationDto(
                    Guid.NewGuid(), stored.LoanId, cashTxn.CashTransactionId,
                    seq++, targetType, Guid.NewGuid().ToString("D"),
                    amount, "Waterfall", stored.History[^1].EventId, DateTimeOffset.UtcNow));
            }
        }
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

        lock (_gate)
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
        lock (_gate)
        {
            IEnumerable<PendingPaymentDto> all = _pendingPayments.Values
                .Where(static p => p.Status == PaymentApprovalStatus.Pending);

            if (loanId.HasValue)
            {
                all = all.Where(p => p.LoanId == loanId.Value);
            }

            IReadOnlyList<PendingPaymentDto> result = all
                .OrderByDescending(static p => p.InitiatedAt)
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
        if (request.CountPerLoan <= 0)
        {
            throw new DirectLendingCommandException(new DirectLendingCommandError(
                DirectLendingErrorCode.Validation, "CountPerLoan must be positive."));
        }

        var rng = new Random(42); // deterministic seed for reproducibility
        var seeded = 0;
        var processedLoanIds = new List<Guid>();

        lock (_gate)
        {
            var targetLoans = (request.LoanIds is { Count: > 0 }
                    ? request.LoanIds.Where(id => _loans.ContainsKey(id))
                    : _loans.Keys)
                .ToArray();

            var fromDate = request.FromDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6));
            var toDate = request.ToDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var totalDays = Math.Max(1, toDate.DayNumber - fromDate.DayNumber);

            foreach (var loanId in targetLoans)
            {
                var stored = _loans[loanId];
                var currency = stored.TermsVersions[^1].Terms.BaseCurrency;
                var list = GetList(_cashTransactions, loanId);

                for (var i = 0; i < request.CountPerLoan; i++)
                {
                    var txDate = fromDate.AddDays(rng.Next(totalDays));
                    var txType = SeedTransactionTypes[rng.Next(SeedTransactionTypes.Length)];

                    // Generate a plausible amount based on commitment size
                    var commitment = stored.TermsVersions[^1].Terms.CommitmentAmount;
                    var amount = decimal.Round(
                        (decimal)(rng.NextDouble() * (double)(commitment * 0.05m)) + 100m,
                        2,
                        MidpointRounding.AwayFromZero);

                    list.Add(new CashTransactionDto(
                        CashTransactionId: Guid.NewGuid(),
                        LoanId: loanId,
                        TransactionType: txType,
                        EffectiveDate: txDate,
                        TransactionDate: txDate,
                        SettlementDate: txDate.AddDays(2),
                        Amount: amount,
                        Currency: currency,
                        ExternalRef: $"SEED-{i + 1:D4}-{loanId.ToString("N")[..8]}",
                        SourceEventId: Guid.NewGuid(),
                        RecordedAt: DateTimeOffset.UtcNow,
                        IsVoided: false));
                    seeded++;
                }

                processedLoanIds.Add(loanId);
            }
        }

        return Task.FromResult(new BankTransactionSeedResultDto(
            LoansProcessed: processedLoanIds.Count,
            TransactionsSeeded: seeded,
            ProcessedLoanIds: processedLoanIds));
    }
}
