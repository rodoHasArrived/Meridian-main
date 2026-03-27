using System.Text.Json;
using Meridian.Contracts.DirectLending;
using Meridian.FSharp.DirectLending.Aggregates;
using Meridian.FSharp.DirectLendingInterop;
using Meridian.Storage.DirectLending;

namespace Meridian.Application.DirectLending;

public sealed partial class PostgresDirectLendingCommandService : IDirectLendingCommandService
{
    private readonly IDirectLendingStateStore _stateStore;
    private readonly IDirectLendingOperationsStore _operationsStore;
    private readonly IDirectLendingQueryService _queryService;
    private readonly DirectLendingOptions _options;

    public PostgresDirectLendingCommandService(
        IDirectLendingStateStore stateStore,
        IDirectLendingOperationsStore operationsStore,
        IDirectLendingQueryService queryService,
        DirectLendingOptions options)
    {
        _stateStore = stateStore;
        _operationsStore = operationsStore;
        _queryService = queryService;
        _options = options;
    }

    public async Task<DirectLendingCommandResult<LoanContractDetailDto>> CreateLoanAsync(CreateLoanRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationError = DirectLendingServiceSupport.ValidateCreateRequest(request);
        if (validationError is not null)
        {
            return new DirectLendingCommandResult<LoanContractDetailDto>(null, validationError);
        }

        var loanId = request.LoanId.GetValueOrDefault(Guid.NewGuid());
        var existing = await _stateStore.LoadAsync(loanId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return DirectLendingCommandResult<LoanContractDetailDto>.Failure(DirectLendingErrorCode.Validation, $"Loan '{loanId}' already exists.");
        }

        var decision = DirectLendingAggregateInterop.CreateLoan(loanId, request, DateTimeOffset.UtcNow);
        var contract = decision.Contract;
        var servicing = decision.Servicing;

        using var payload = DirectLendingServiceSupport.CreatePayloadDocument(new
        {
            loanId,
            request.FacilityName,
            request.Borrower,
            request.EffectiveDate,
            request.Terms
        });

        await SaveAsync(
            loanId,
            expectedVersion: 0,
            nextVersion: 1,
            contract,
            servicing,
            eventType: "loan.created",
            effectiveDate: request.EffectiveDate,
            payload,
            metadata,
            ct: ct,
            persistenceBatch: null).ConfigureAwait(false);

        return DirectLendingCommandResult<LoanContractDetailDto>.Success(contract);
    }

    public async Task<DirectLendingCommandResult<LoanContractDetailDto>> AmendTermsAsync(Guid loanId, AmendLoanTermsRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationError = DirectLendingServiceSupport.ValidateTerms(request.Terms);
        if (validationError is not null)
        {
            return new DirectLendingCommandResult<LoanContractDetailDto>(null, validationError);
        }

        var stored = await _queryService.LoadAggregateAsync(loanId, ct).ConfigureAwait(false);
        if (stored is null)
        {
            return DirectLendingCommandResult<LoanContractDetailDto>.Failure(DirectLendingErrorCode.NotFound, $"Loan '{loanId}' was not found.");
        }

        var decision = DirectLendingAggregateInterop.AmendTerms(stored.Contract, stored.Servicing, request, DateTimeOffset.UtcNow);
        var contract = decision.Contract;
        var servicing = decision.Servicing;

        using var payload = DirectLendingServiceSupport.CreatePayloadDocument(new
        {
            loanId,
            request.AmendmentReason,
            request.Terms,
            versionNumber = contract.CurrentTermsVersion
        });

        await SaveAsync(
            loanId,
            stored.AggregateVersion,
            stored.AggregateVersion + 1,
            contract,
            servicing,
            eventType: "loan.terms-amended",
            effectiveDate: request.Terms.OriginationDate,
            payload,
            metadata,
            ct: ct,
            persistenceBatch: null).ConfigureAwait(false);

        return DirectLendingCommandResult<LoanContractDetailDto>.Success(contract);
    }

    public async Task<DirectLendingCommandResult<LoanContractDetailDto>> ActivateLoanAsync(Guid loanId, ActivateLoanRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stored = await _queryService.LoadAggregateAsync(loanId, ct).ConfigureAwait(false);
        if (stored is null)
        {
            return DirectLendingCommandResult<LoanContractDetailDto>.Failure(DirectLendingErrorCode.NotFound, $"Loan '{loanId}' was not found.");
        }

        var decision = DirectLendingAggregateInterop.ActivateLoan(stored.Contract, stored.Servicing, request);
        var contract = decision.Contract;
        var servicing = decision.Servicing;

        using var payload = DirectLendingServiceSupport.CreatePayloadDocument(new
        {
            loanId,
            request.ActivationDate
        });

        await SaveAsync(
            loanId,
            stored.AggregateVersion,
            stored.AggregateVersion + 1,
            contract,
            servicing,
            eventType: "loan.activated",
            effectiveDate: request.ActivationDate,
            payload,
            metadata,
            ct: ct,
            persistenceBatch: null).ConfigureAwait(false);

        return DirectLendingCommandResult<LoanContractDetailDto>.Success(contract);
    }

    public async Task<DirectLendingCommandResult<LoanServicingStateDto>> BookDrawdownAsync(Guid loanId, BookDrawdownRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Amount <= 0m)
        {
            return DirectLendingCommandResult<LoanServicingStateDto>.Failure(DirectLendingErrorCode.Validation, "Drawdown amount must be positive.");
        }

        var stored = await _queryService.LoadAggregateAsync(loanId, ct).ConfigureAwait(false);
        if (stored is null)
        {
            return DirectLendingCommandResult<LoanServicingStateDto>.Failure(DirectLendingErrorCode.NotFound, $"Loan '{loanId}' was not found.");
        }

        var activeError = DirectLendingServiceSupport.EnsureActive(stored.Contract);
        if (activeError is not null)
        {
            return new DirectLendingCommandResult<LoanServicingStateDto>(null, activeError);
        }

        if (request.Amount > stored.Servicing.AvailableToDraw)
        {
            return DirectLendingCommandResult<LoanServicingStateDto>.Failure(DirectLendingErrorCode.Validation, "Drawdown exceeds available commitment.");
        }

        var decision = DirectLendingAggregateInterop.BookDrawdown(stored.Servicing, request);
        var lotId = decision.LotId;
        var servicing = decision.Servicing;

        using var payload = DirectLendingServiceSupport.CreatePayloadDocument(new
        {
            loanId,
            lotId,
            request.Amount,
            request.TradeDate,
            request.SettleDate,
            request.ExternalRef
        });

        await SaveAsync(
            loanId,
            stored.AggregateVersion,
            stored.AggregateVersion + 1,
            stored.Contract,
            servicing,
            eventType: "loan.drawdown-booked",
            effectiveDate: request.SettleDate,
            payload,
            metadata,
            ct: ct,
            persistenceBatch: null).ConfigureAwait(false);

        return DirectLendingCommandResult<LoanServicingStateDto>.Success(servicing);
    }

    public async Task<DirectLendingCommandResult<LoanServicingStateDto>> ApplyRateResetAsync(Guid loanId, ApplyRateResetRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stored = await _queryService.LoadAggregateAsync(loanId, ct).ConfigureAwait(false);
        if (stored is null)
        {
            return DirectLendingCommandResult<LoanServicingStateDto>.Failure(DirectLendingErrorCode.NotFound, $"Loan '{loanId}' was not found.");
        }

        var currentTerms = stored.Contract.CurrentTerms;
        if (currentTerms.RateTypeKind != RateTypeKind.Floating)
        {
            return DirectLendingCommandResult<LoanServicingStateDto>.Failure(DirectLendingErrorCode.Validation, "Rate resets can only be applied to floating-rate loans.");
        }

        var decision = DirectLendingAggregateInterop.ApplyRateReset(stored.Servicing, currentTerms, request);
        var spreadBps = request.SpreadBps ?? currentTerms.SpreadBps ?? 0m;
        var allInRate = decision.AllInRate;
        var servicing = decision.Servicing;

        using var payload = DirectLendingServiceSupport.CreatePayloadDocument(new
        {
            loanId,
            request.EffectiveDate,
            request.ObservedRate,
            SpreadBps = spreadBps,
            AllInRate = allInRate,
            request.SourceRef
        });

        await SaveAsync(
            loanId,
            stored.AggregateVersion,
            stored.AggregateVersion + 1,
            stored.Contract,
            servicing,
            eventType: "loan.rate-reset-applied",
            effectiveDate: request.EffectiveDate,
            payload,
            metadata,
            ct: ct,
            persistenceBatch: null).ConfigureAwait(false);

        return DirectLendingCommandResult<LoanServicingStateDto>.Success(servicing);
    }

    public async Task<DirectLendingCommandResult<LoanServicingStateDto>> ApplyPrincipalPaymentAsync(Guid loanId, ApplyPrincipalPaymentRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Amount <= 0m)
        {
            return DirectLendingCommandResult<LoanServicingStateDto>.Failure(DirectLendingErrorCode.Validation, "Principal payment amount must be positive.");
        }

        var stored = await _queryService.LoadAggregateAsync(loanId, ct).ConfigureAwait(false);
        if (stored is null)
        {
            return DirectLendingCommandResult<LoanServicingStateDto>.Failure(DirectLendingErrorCode.NotFound, $"Loan '{loanId}' was not found.");
        }

        var principalBefore = stored.Servicing.Balances.PrincipalOutstanding;
        if (principalBefore <= 0m)
        {
            return DirectLendingCommandResult<LoanServicingStateDto>.Failure(DirectLendingErrorCode.Validation, "No principal is outstanding.");
        }

        var decision = DirectLendingAggregateInterop.ApplyPrincipalPayment(stored.Servicing, request);
        var appliedAmount = decision.AppliedAmount;
        var servicing = decision.Servicing;

        using var payload = DirectLendingServiceSupport.CreatePayloadDocument(new
        {
            loanId,
            RequestedAmount = request.Amount,
            AppliedAmount = appliedAmount,
            request.EffectiveDate,
            request.ExternalRef
        });

        await SaveAsync(
            loanId,
            stored.AggregateVersion,
            stored.AggregateVersion + 1,
            stored.Contract,
            servicing,
            eventType: "loan.principal-payment-applied",
            effectiveDate: request.EffectiveDate,
            payload,
            metadata,
            ct: ct,
            persistenceBatch: null).ConfigureAwait(false);

        return DirectLendingCommandResult<LoanServicingStateDto>.Success(servicing);
    }

    public async Task<DirectLendingCommandResult<LoanServicingStateDto>> ApplyMixedPaymentAsync(Guid loanId, ApplyMixedPaymentRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Amount <= 0m)
        {
            return DirectLendingCommandResult<LoanServicingStateDto>.Failure(DirectLendingErrorCode.Validation, "Payment amount must be positive.");
        }

        var stored = await _queryService.LoadAggregateAsync(loanId, ct).ConfigureAwait(false);
        if (stored is null)
        {
            return DirectLendingCommandResult<LoanServicingStateDto>.Failure(DirectLendingErrorCode.NotFound, $"Loan '{loanId}' was not found.");
        }

        var decision = DirectLendingAggregateInterop.ApplyMixedPayment(stored.Servicing, request);
        var servicing = decision.Servicing;
        var resolution = decision.Resolution;
        var cashTransactionId = decision.CashTransactionId;
        var allocations = decision.Allocations
            .Select(allocation => new DirectLendingPaymentAllocationWrite(
                cashTransactionId,
                allocation.SequenceNumber,
                allocation.TargetType,
                allocation.TargetReference,
                allocation.Amount,
                allocation.AllocationRule))
            .ToArray();

        using var payload = DirectLendingServiceSupport.CreatePayloadDocument(new
        {
            loanId,
            request.Amount,
            request.EffectiveDate,
            request.ExternalRef,
            Resolution = resolution
        });

        await SaveAsync(
            loanId,
            stored.AggregateVersion,
            stored.AggregateVersion + 1,
            stored.Contract,
            servicing,
            eventType: "loan.mixed-payment-applied",
            effectiveDate: request.EffectiveDate,
            payload,
            metadata,
            persistenceBatch: new DirectLendingPersistenceBatch(
                [
                    new DirectLendingCashTransactionWrite(
                        cashTransactionId,
                        "MixedPayment",
                        request.EffectiveDate,
                        request.EffectiveDate,
                        request.EffectiveDate,
                        request.Amount,
                        stored.Contract.CurrentTerms.BaseCurrency.ToString(),
                        Counterparty: null,
                        ExternalRef: request.ExternalRef)
                ],
                allocations,
                [],
                []),
            ct: ct).ConfigureAwait(false);

        return DirectLendingCommandResult<LoanServicingStateDto>.Success(servicing);
    }

    public async Task<DirectLendingCommandResult<LoanServicingStateDto>> AssessFeeAsync(Guid loanId, AssessFeeRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Amount <= 0m)
        {
            return DirectLendingCommandResult<LoanServicingStateDto>.Failure(DirectLendingErrorCode.Validation, "Fee amount must be positive.");
        }

        var stored = await _queryService.LoadAggregateAsync(loanId, ct).ConfigureAwait(false);
        if (stored is null)
        {
            return DirectLendingCommandResult<LoanServicingStateDto>.Failure(DirectLendingErrorCode.NotFound, $"Loan '{loanId}' was not found.");
        }

        var decision = DirectLendingAggregateInterop.AssessFee(stored.Servicing, request);
        var servicing = decision.Servicing;

        var feeBalanceId = Guid.NewGuid();
        using var payload = DirectLendingServiceSupport.CreatePayloadDocument(new
        {
            loanId,
            request.FeeType,
            request.Amount,
            request.EffectiveDate,
            request.Note
        });

        await SaveAsync(
            loanId,
            stored.AggregateVersion,
            stored.AggregateVersion + 1,
            stored.Contract,
            servicing,
            eventType: "loan.fee-assessed",
            effectiveDate: request.EffectiveDate,
            payload,
            metadata,
            persistenceBatch: new DirectLendingPersistenceBatch(
                [],
                [],
                [
                    new DirectLendingFeeBalanceWrite(
                        feeBalanceId,
                        request.FeeType.ToString(),
                        request.EffectiveDate,
                        request.Amount,
                        request.Amount,
                        request.Note)
                ],
                []),
            ct: ct).ConfigureAwait(false);

        return DirectLendingCommandResult<LoanServicingStateDto>.Success(servicing);
    }

    public async Task<DirectLendingCommandResult<LoanServicingStateDto>> ApplyWriteOffAsync(Guid loanId, ApplyWriteOffRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Amount <= 0m)
        {
            return DirectLendingCommandResult<LoanServicingStateDto>.Failure(DirectLendingErrorCode.Validation, "Write-off amount must be positive.");
        }

        var stored = await _queryService.LoadAggregateAsync(loanId, ct).ConfigureAwait(false);
        if (stored is null)
        {
            return DirectLendingCommandResult<LoanServicingStateDto>.Failure(DirectLendingErrorCode.NotFound, $"Loan '{loanId}' was not found.");
        }

        var decision = DirectLendingAggregateInterop.ApplyWriteOff(stored.Servicing, request);
        var writeOffAmount = decision.AppliedAmount;
        var servicing = decision.Servicing;

        using var payload = DirectLendingServiceSupport.CreatePayloadDocument(new
        {
            loanId,
            RequestedAmount = request.Amount,
            AppliedAmount = writeOffAmount,
            request.EffectiveDate,
            request.Reason
        });

        await SaveAsync(
            loanId,
            stored.AggregateVersion,
            stored.AggregateVersion + 1,
            stored.Contract,
            servicing,
            eventType: "loan.write-off-applied",
            effectiveDate: request.EffectiveDate,
            payload,
            metadata,
            ct: ct).ConfigureAwait(false);

        return DirectLendingCommandResult<LoanServicingStateDto>.Success(servicing);
    }

    public async Task<DirectLendingCommandResult<DailyAccrualEntryDto>> PostDailyAccrualAsync(Guid loanId, PostDailyAccrualRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stored = await _queryService.LoadAggregateAsync(loanId, ct).ConfigureAwait(false);
        if (stored is null)
        {
            return DirectLendingCommandResult<DailyAccrualEntryDto>.Failure(DirectLendingErrorCode.NotFound, $"Loan '{loanId}' was not found.");
        }

        var activeError = DirectLendingServiceSupport.EnsureActive(stored.Contract);
        if (activeError is not null)
        {
            return new DirectLendingCommandResult<DailyAccrualEntryDto>(null, activeError);
        }

        if (stored.Servicing.LastAccrualDate is not null && request.AccrualDate <= stored.Servicing.LastAccrualDate.Value)
        {
            return DirectLendingCommandResult<DailyAccrualEntryDto>.Failure(DirectLendingErrorCode.Validation, "Accrual date must be greater than the last accrual date.");
        }

        var annualRateResult = DirectLendingServiceSupport.ResolveAnnualRate(stored.Contract.CurrentTerms, stored.Servicing.CurrentRateReset);
        if (!annualRateResult.IsSuccess)
        {
            return new DirectLendingCommandResult<DailyAccrualEntryDto>(null, annualRateResult.Error);
        }

        var decision = DirectLendingAggregateInterop.PostDailyAccrual(stored.Servicing, stored.Contract.CurrentTerms, request);
        var entry = decision.Entry;
        var servicing = decision.Servicing;

        using var payload = DirectLendingServiceSupport.CreatePayloadDocument(new
        {
            loanId,
            entry.AccrualEntryId,
            request.AccrualDate,
            entry.InterestAmount,
            entry.CommitmentFeeAmount,
            entry.PenaltyAmount,
            entry.AnnualRateApplied
        });

        await SaveAsync(
            loanId,
            stored.AggregateVersion,
            stored.AggregateVersion + 1,
            stored.Contract,
            servicing,
            eventType: "loan.daily-accrual-posted",
            effectiveDate: request.AccrualDate,
            payload,
            metadata,
            ct: ct,
            persistenceBatch: null).ConfigureAwait(false);

        return DirectLendingCommandResult<DailyAccrualEntryDto>.Success(entry);
    }

    public async Task<DirectLendingCommandResult<ProjectionRunDto>> RequestProjectionAsync(Guid loanId, DateOnly? projectionAsOf = null, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        var stored = await _queryService.LoadAggregateAsync(loanId, ct).ConfigureAwait(false);
        if (stored is null)
        {
            return DirectLendingCommandResult<ProjectionRunDto>.Failure(DirectLendingErrorCode.NotFound, $"Loan '{loanId}' was not found.");
        }

        var history = await _queryService.GetHistoryAsync(loanId, ct).ConfigureAwait(false);
        var triggerEvent = history.LastOrDefault();
        if (triggerEvent is null)
        {
            return DirectLendingCommandResult<ProjectionRunDto>.Failure(DirectLendingErrorCode.Validation, "Projection requires at least one source event.");
        }

        var existingRuns = await _operationsStore.GetProjectionRunsAsync(loanId, ct).ConfigureAwait(false);
        var latest = existingRuns.OrderByDescending(static x => x.GeneratedAt).FirstOrDefault();
        var asOf = projectionAsOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var runId = Guid.NewGuid();
        var run = new ProjectionRunDto(
            runId,
            loanId,
            stored.Contract.CurrentTermsVersion,
            stored.Servicing.ServicingRevision,
            asOf,
            null,
            triggerEvent.EventId,
            "ManualRequest",
            DirectLendingServiceSupport.ComputeTermsHash(stored.Contract.CurrentTerms),
            _options.ProjectionEngineVersion,
            ProjectionRunStatus.Completed,
            latest?.ProjectionRunId,
            DateTimeOffset.UtcNow);

        var flows = BuildProjectedFlows(stored.Contract, stored.Servicing, runId, asOf);
        await _operationsStore.SaveProjectionRunAsync(run, flows, ct).ConfigureAwait(false);
        return DirectLendingCommandResult<ProjectionRunDto>.Success(run);
    }

    public async Task<DirectLendingCommandResult<JournalEntryDto>> PostJournalAsync(Guid journalEntryId, CancellationToken ct = default)
    {
        var entry = await _operationsStore.MarkJournalPostedAsync(journalEntryId, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        return entry is null
            ? DirectLendingCommandResult<JournalEntryDto>.Failure(DirectLendingErrorCode.NotFound, $"Journal '{journalEntryId}' was not found.")
            : DirectLendingCommandResult<JournalEntryDto>.Success(entry);
    }

    public async Task<DirectLendingCommandResult<ReconciliationRunDto>> ReconcileAsync(Guid loanId, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        var projections = await _operationsStore.GetProjectionRunsAsync(loanId, ct).ConfigureAwait(false);
        var latestProjection = projections.OrderByDescending(static x => x.GeneratedAt).FirstOrDefault();
        if (latestProjection is null)
        {
            var requested = await RequestProjectionAsync(loanId, projectionAsOf: null, metadata, ct).ConfigureAwait(false);
            if (!requested.IsSuccess)
            {
                return new DirectLendingCommandResult<ReconciliationRunDto>(null, requested.Error);
            }

            latestProjection = requested.Value!;
        }

        var flows = await _operationsStore.GetProjectedCashFlowsAsync(latestProjection.ProjectionRunId, ct).ConfigureAwait(false);
        var cashTransactions = await _operationsStore.GetCashTransactionsAsync(loanId, ct).ConfigureAwait(false);
        var runId = Guid.NewGuid();
        var results = new List<ReconciliationResultDto>();
        var exceptions = new List<ReconciliationExceptionDto>();

        foreach (var flow in flows)
        {
            var cash = cashTransactions.FirstOrDefault(x => x.SettlementDate == flow.DueDate && x.Amount == flow.Amount);
            var matchStatus = cash is null ? "MissingCash" : "Matched";
            var variance = cash is null ? flow.Amount : 0m;
            var result = new ReconciliationResultDto(
                Guid.NewGuid(),
                runId,
                loanId,
                flow.ProjectedCashFlowId,
                cash?.CashTransactionId,
                matchStatus,
                flow.Amount,
                cash?.Amount,
                variance,
                flow.DueDate,
                cash?.SettlementDate,
                cash is null ? "ExpectedFlowVsCash" : "ExactAmountDate",
                "{\"amountTolerance\":0,\"dateToleranceDays\":0}",
                cash is null ? ["Projected flow did not match cash transaction."] : ["Matched."],
                DateTimeOffset.UtcNow);
            results.Add(result);

            if (cash is null)
            {
                exceptions.Add(new ReconciliationExceptionDto(
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
            exceptions.Add(new ReconciliationExceptionDto(
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

        var run = new ReconciliationRunDto(runId, loanId, latestProjection.ProjectionRunId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Completed");
        await _operationsStore.SaveReconciliationRunAsync(run, results, exceptions, ct).ConfigureAwait(false);
        return DirectLendingCommandResult<ReconciliationRunDto>.Success(run);
    }

    public async Task<DirectLendingCommandResult<ServicerReportBatchDto>> CreateServicerReportBatchAsync(CreateServicerReportBatchRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var batchId = Guid.NewGuid();
        var positionLines = (request.PositionLines ?? [])
            .Select(line => new ServicerPositionReportLineDto(
                Guid.NewGuid(),
                batchId,
                line.LoanId,
                request.ReportAsOfDate,
                line.PrincipalOutstanding,
                line.InterestAccruedUnpaid,
                line.FeesAccruedUnpaid,
                line.PenaltyAccruedUnpaid,
                line.CommitmentAvailable,
                line.NextDueDate,
                line.NextDueAmount,
                line.DelinquencyStatus,
                line.RawPayloadJson))
            .ToArray();
        var transactionLines = (request.TransactionLines ?? [])
            .Select(line => new ServicerTransactionReportLineDto(
                Guid.NewGuid(),
                batchId,
                line.LoanId,
                null,
                line.TransactionType,
                line.EffectiveDate,
                line.TransactionDate,
                line.SettlementDate,
                line.GrossAmount,
                line.PrincipalAmount,
                line.InterestAmount,
                line.FeeAmount,
                line.PenaltyAmount,
                line.Currency,
                line.ExternalRef,
                line.RawPayloadJson))
            .ToArray();
        var batch = new ServicerReportBatchDto(
            batchId,
            request.ServicerName,
            request.ReportType,
            request.SourceFormat,
            request.ReportAsOfDate,
            DateTimeOffset.UtcNow,
            request.FileName,
            request.FileHash,
            positionLines.Length + transactionLines.Length,
            "Validated",
            null,
            request.Notes);

        await _operationsStore.SaveServicerBatchAsync(batch, positionLines, transactionLines, ct).ConfigureAwait(false);

        if (positionLines.Length > 0)
        {
            foreach (var line in positionLines)
            {
                var stored = await _queryService.LoadAggregateAsync(line.LoanId, ct).ConfigureAwait(false);
                if (stored is null)
                {
                    continue;
                }

                var nextRevision = stored.Servicing.ServicingRevision + 1;
                var balances = stored.Servicing.Balances with
                {
                    PrincipalOutstanding = line.PrincipalOutstanding ?? stored.Servicing.Balances.PrincipalOutstanding,
                    InterestAccruedUnpaid = line.InterestAccruedUnpaid ?? stored.Servicing.Balances.InterestAccruedUnpaid,
                    FeesAccruedUnpaid = line.FeesAccruedUnpaid ?? stored.Servicing.Balances.FeesAccruedUnpaid,
                    PenaltyAccruedUnpaid = line.PenaltyAccruedUnpaid ?? stored.Servicing.Balances.PenaltyAccruedUnpaid
                };
                var servicing = stored.Servicing with
                {
                    AvailableToDraw = line.CommitmentAvailable ?? stored.Servicing.AvailableToDraw,
                    Balances = balances,
                    ServicingRevision = nextRevision,
                    RevisionHistory = DirectLendingServiceSupport.PrependRevision(
                        stored.Servicing.RevisionHistory,
                        nextRevision,
                        "ServicerReport",
                        request.ReportAsOfDate,
                        $"Servicer position revision applied from batch {batchId:D}.")
                };

                using var payload = DirectLendingServiceSupport.CreatePayloadDocument(new
                {
                    batchId,
                    line.ServicerReportLineId,
                    line.ReportAsOfDate,
                    line.PrincipalOutstanding,
                    line.InterestAccruedUnpaid,
                    line.FeesAccruedUnpaid,
                    line.PenaltyAccruedUnpaid,
                    line.CommitmentAvailable
                });

                await SaveAsync(
                    line.LoanId,
                    stored.AggregateVersion,
                    stored.AggregateVersion + 1,
                    stored.Contract,
                    servicing,
                    eventType: "loan.servicer-position-imported",
                    effectiveDate: line.ReportAsOfDate,
                    payload,
                    metadata,
                    ct).ConfigureAwait(false);
            }
        }

        return DirectLendingCommandResult<ServicerReportBatchDto>.Success(batch);
    }

    public async Task<DirectLendingCommandResult<LoanServicingStateDto>> ChargePrepaymentPenaltyAsync(Guid loanId, ChargePrepaymentPenaltyRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.OutstandingPrincipal <= 0m)
        {
            return DirectLendingCommandResult<LoanServicingStateDto>.Failure(DirectLendingErrorCode.Validation, "Outstanding principal must be positive.");
        }

        var stored = await _queryService.LoadAggregateAsync(loanId, ct).ConfigureAwait(false);
        if (stored is null)
        {
            return DirectLendingCommandResult<LoanServicingStateDto>.Failure(DirectLendingErrorCode.NotFound, $"Loan '{loanId}' was not found.");
        }

        var activeError = DirectLendingServiceSupport.EnsureActive(stored.Contract);
        if (activeError is not null)
        {
            return new DirectLendingCommandResult<LoanServicingStateDto>(null, activeError);
        }

        if (!stored.Contract.CurrentTerms.PrepaymentAllowed)
        {
            return DirectLendingCommandResult<LoanServicingStateDto>.Failure(DirectLendingErrorCode.Validation, "Prepayment is not permitted under the current loan terms.");
        }

        var decision = DirectLendingAggregateInterop.ChargePrepaymentPenalty(stored.Servicing, stored.Contract.CurrentTerms, request);
        var servicing = decision.Servicing;

        using var payload = DirectLendingServiceSupport.CreatePayloadDocument(new
        {
            loanId,
            request.OutstandingPrincipal,
            PenaltyAmount = decision.PenaltyAmount,
            request.EffectiveDate,
            request.ExternalRef
        });

        await SaveAsync(
            loanId,
            stored.AggregateVersion,
            stored.AggregateVersion + 1,
            stored.Contract,
            servicing,
            eventType: "loan.prepayment-penalty-charged",
            effectiveDate: request.EffectiveDate,
            payload,
            metadata,
            ct: ct).ConfigureAwait(false);

        return DirectLendingCommandResult<LoanServicingStateDto>.Success(servicing);
    }

    public async Task<DirectLendingCommandResult<IReadOnlyList<LoanAggregateSnapshotDto>>> RebuildAllAsync(CancellationToken ct = default)
    {
        var loanIds = await _operationsStore.GetLoanIdsAsync(ct).ConfigureAwait(false);
        var rebuilt = new List<LoanAggregateSnapshotDto>(loanIds.Count);
        foreach (var loanId in loanIds)
        {
            var snapshot = await _queryService.RebuildStateFromHistoryAsync(loanId, ct).ConfigureAwait(false);
            if (snapshot is not null)
            {
                rebuilt.Add(snapshot);
            }
        }

        var latestPosition = await _operationsStore.GetLatestEventPositionAsync(ct).ConfigureAwait(false);
        await _operationsStore.UpsertCheckpointAsync(
            new RebuildCheckpointDto(
                "direct_lending_full_rebuild",
                latestPosition,
                null,
                DateTimeOffset.UtcNow,
                "Completed",
                $"Rebuilt {rebuilt.Count} loan aggregates."),
            ct).ConfigureAwait(false);

        return DirectLendingCommandResult<IReadOnlyList<LoanAggregateSnapshotDto>>.Success(rebuilt);
    }

    private static IReadOnlyList<DirectLendingPaymentAllocationWrite> BuildPaymentAllocations(Guid loanId, Guid cashTransactionId, MixedPaymentResolutionDto resolution)
    {
        var results = new List<DirectLendingPaymentAllocationWrite>();
        var seq = 1;
        Add("Interest", resolution.Breakdown.ToInterest);
        Add("CommitmentFee", resolution.Breakdown.ToCommitmentFee);
        Add("Fees", resolution.Breakdown.ToFees);
        Add("Penalty", resolution.Breakdown.ToPenalty);
        Add("Principal", resolution.Breakdown.ToPrincipal);
        return results;

        void Add(string targetType, decimal amount)
        {
            if (amount <= 0m)
            {
                return;
            }

            results.Add(new DirectLendingPaymentAllocationWrite(
                cashTransactionId,
                seq++,
                targetType,
                Guid.NewGuid(),
                amount,
                resolution.ResolutionBasis));
        }
    }

    private IReadOnlyList<ProjectedCashFlowDto> BuildProjectedFlows(LoanContractDetailDto contract, LoanServicingStateDto servicing, Guid projectionRunId, DateOnly asOf)
    {
        var results = new List<ProjectedCashFlowDto>();
        var annualRate = contract.CurrentTerms.FixedAnnualRate
            ?? servicing.CurrentRateReset?.AllInRate
            ?? 0m;
        var dueDate = asOf;
        var seq = 1;
        var intervalMonths = contract.CurrentTerms.PaymentFrequency switch
        {
            PaymentFrequency.Monthly => 1,
            PaymentFrequency.Quarterly => 3,
            PaymentFrequency.SemiAnnual => 6,
            PaymentFrequency.Annual => 12,
            _ => 12
        };

        while (dueDate < contract.CurrentTerms.MaturityDate)
        {
            var nextDue = dueDate.AddMonths(intervalMonths);
            if (nextDue > contract.CurrentTerms.MaturityDate)
            {
                nextDue = contract.CurrentTerms.MaturityDate;
            }

            var days = nextDue.DayNumber - dueDate.DayNumber;
            var denominator = contract.CurrentTerms.DayCountBasis == DayCountBasis.Act365F ? 365m : 360m;
            var interestAmount = Math.Round(servicing.Balances.PrincipalOutstanding * annualRate * days / denominator, 2, MidpointRounding.AwayFromZero);
            results.Add(new ProjectedCashFlowDto(
                Guid.NewGuid(),
                projectionRunId,
                contract.LoanId,
                seq++,
                "Interest",
                nextDue,
                dueDate,
                nextDue,
                interestAmount,
                contract.CurrentTerms.BaseCurrency,
                servicing.Balances.PrincipalOutstanding,
                annualRate,
                $"{{\"days\":{days},\"annualRate\":{annualRate}}}",
                DateTimeOffset.UtcNow));

            dueDate = nextDue;
        }

        if (servicing.Balances.PrincipalOutstanding > 0m)
        {
            results.Add(new ProjectedCashFlowDto(
                Guid.NewGuid(),
                projectionRunId,
                contract.LoanId,
                seq,
                "Principal",
                contract.CurrentTerms.MaturityDate,
                null,
                null,
                servicing.Balances.PrincipalOutstanding,
                contract.CurrentTerms.BaseCurrency,
                servicing.Balances.PrincipalOutstanding,
                annualRate,
                "{\"type\":\"bullet-principal\"}",
                DateTimeOffset.UtcNow));
        }

        return results;
    }

    private Task SaveAsync(
        Guid loanId,
        long expectedVersion,
        long nextVersion,
        LoanContractDetailDto contract,
        LoanServicingStateDto servicing,
        string eventType,
        DateOnly? effectiveDate,
        JsonDocument payload,
        DirectLendingCommandMetadataDto? metadata,
        CancellationToken ct = default,
        DirectLendingPersistenceBatch? persistenceBatch = null)
    {
        var eventMetadata = DirectLendingServiceSupport.CreateEventMetadata(metadata, "meridian.direct-lending");
        var eventId = Guid.NewGuid();
        return _stateStore.SaveAsync(
            loanId,
            expectedVersion,
            nextVersion,
            contract,
            servicing,
            eventType,
            _options.CurrentEventSchemaVersion,
            effectiveDate,
            payload,
            eventMetadata,
            DirectLendingServiceSupport.WithOutbox(
                loanId,
                eventId,
                eventType,
                effectiveDate,
                servicing.ServicingRevision,
                eventMetadata,
                persistenceBatch),
            eventId,
            ct);
    }
}
