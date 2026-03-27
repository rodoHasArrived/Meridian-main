using Meridian.Contracts.DirectLending;
using Meridian.Storage.DirectLending;

namespace Meridian.Application.DirectLending;

public sealed partial class PostgresDirectLendingQueryService : IDirectLendingQueryService
{
    private readonly IDirectLendingStateStore _stateStore;
    private readonly IDirectLendingOperationsStore _operationsStore;
    private readonly DirectLendingEventRebuilder _rebuilder;

    public PostgresDirectLendingQueryService(
        IDirectLendingStateStore stateStore,
        IDirectLendingOperationsStore operationsStore,
        DirectLendingEventRebuilder rebuilder)
    {
        _stateStore = stateStore;
        _operationsStore = operationsStore;
        _rebuilder = rebuilder;
    }

    public async Task<LoanContractDetailDto?> GetLoanAsync(Guid loanId, CancellationToken ct = default)
    {
        var projected = await _stateStore.LoadContractProjectionAsync(loanId, ct).ConfigureAwait(false);
        if (projected is not null)
        {
            return projected;
        }

        var stored = await LoadAggregateAsync(loanId, ct).ConfigureAwait(false);
        return stored?.Contract;
    }

    public async Task<LoanContractDetailDto?> GetContractProjectionAsync(Guid loanId, CancellationToken ct = default)
    {
        var projected = await _stateStore.LoadContractProjectionAsync(loanId, ct).ConfigureAwait(false);
        if (projected is not null)
        {
            return projected;
        }

        var rebuilt = await LoadAggregateAsync(loanId, ct).ConfigureAwait(false);
        return rebuilt?.Contract;
    }

    public async Task<LoanAggregateSnapshotDto?> RebuildStateFromHistoryAsync(Guid loanId, CancellationToken ct = default)
    {
        var history = await _stateStore.GetHistoryAsync(loanId, ct).ConfigureAwait(false);
        var rebuilt = _rebuilder.Rebuild(loanId, history);
        if (rebuilt is null)
        {
            return null;
        }

        await _stateStore.SaveStateAsync(loanId, rebuilt.AggregateVersion, rebuilt.Contract, rebuilt.Servicing, ct).ConfigureAwait(false);
        return new LoanAggregateSnapshotDto(rebuilt.LoanId, rebuilt.AggregateVersion, rebuilt.Contract, rebuilt.Servicing);
    }

    public Task<IReadOnlyList<LoanEventLineageDto>> GetHistoryAsync(Guid loanId, CancellationToken ct = default)
        => _stateStore.GetHistoryAsync(loanId, ct);

    public async Task<IReadOnlyList<LoanTermsVersionDto>> GetTermsVersionsAsync(Guid loanId, CancellationToken ct = default)
    {
        var projected = await _stateStore.LoadTermsVersionProjectionsAsync(loanId, ct).ConfigureAwait(false);
        if (projected.Count > 0)
        {
            return projected;
        }

        var stored = await LoadAggregateAsync(loanId, ct).ConfigureAwait(false);
        return stored is null
            ? []
            : stored.Contract.TermsVersions.OrderByDescending(static item => item.VersionNumber).ToArray();
    }

    public async Task<IReadOnlyList<LoanTermsVersionDto>> GetTermsVersionProjectionsAsync(Guid loanId, CancellationToken ct = default)
    {
        var projected = await _stateStore.LoadTermsVersionProjectionsAsync(loanId, ct).ConfigureAwait(false);
        if (projected.Count > 0)
        {
            return projected;
        }

        var rebuilt = await LoadAggregateAsync(loanId, ct).ConfigureAwait(false);
        return rebuilt is null
            ? []
            : rebuilt.Contract.TermsVersions.OrderByDescending(static item => item.VersionNumber).ToArray();
    }

    public async Task<LoanServicingStateDto?> GetServicingStateAsync(Guid loanId, CancellationToken ct = default)
    {
        var projected = await _stateStore.LoadServicingProjectionAsync(loanId, ct).ConfigureAwait(false);
        if (projected is not null)
        {
            return projected;
        }

        var stored = await LoadAggregateAsync(loanId, ct).ConfigureAwait(false);
        return stored?.Servicing;
    }

    public async Task<LoanServicingStateDto?> GetServicingProjectionAsync(Guid loanId, CancellationToken ct = default)
    {
        var projected = await _stateStore.LoadServicingProjectionAsync(loanId, ct).ConfigureAwait(false);
        if (projected is not null)
        {
            return projected;
        }

        var rebuilt = await LoadAggregateAsync(loanId, ct).ConfigureAwait(false);
        return rebuilt?.Servicing;
    }

    public async Task<IReadOnlyList<DrawdownLotDto>> GetDrawdownLotProjectionsAsync(Guid loanId, CancellationToken ct = default)
    {
        var projected = await _stateStore.LoadDrawdownLotProjectionsAsync(loanId, ct).ConfigureAwait(false);
        if (projected.Count > 0)
        {
            return projected;
        }

        var rebuilt = await LoadAggregateAsync(loanId, ct).ConfigureAwait(false);
        return rebuilt is null ? [] : rebuilt.Servicing.DrawdownLots.OrderBy(static item => item.DrawdownDate).ToArray();
    }

    public async Task<IReadOnlyList<ServicingRevisionDto>> GetServicingRevisionProjectionsAsync(Guid loanId, CancellationToken ct = default)
    {
        var projected = await _stateStore.LoadServicingRevisionProjectionsAsync(loanId, ct).ConfigureAwait(false);
        if (projected.Count > 0)
        {
            return projected;
        }

        var rebuilt = await LoadAggregateAsync(loanId, ct).ConfigureAwait(false);
        return rebuilt is null ? [] : rebuilt.Servicing.RevisionHistory.OrderByDescending(static item => item.RevisionNumber).ToArray();
    }

    public async Task<IReadOnlyList<DailyAccrualEntryDto>> GetAccrualEntryProjectionsAsync(Guid loanId, CancellationToken ct = default)
    {
        var projected = await _stateStore.LoadAccrualEntryProjectionsAsync(loanId, ct).ConfigureAwait(false);
        if (projected.Count > 0)
        {
            return projected;
        }

        var rebuilt = await LoadAggregateAsync(loanId, ct).ConfigureAwait(false);
        return rebuilt is null ? [] : rebuilt.Servicing.AccrualEntries.OrderByDescending(static item => item.AccrualDate).ToArray();
    }

    public Task<IReadOnlyList<CashTransactionDto>> GetCashTransactionsAsync(Guid loanId, CancellationToken ct = default)
        => _operationsStore.GetCashTransactionsAsync(loanId, ct);

    public Task<IReadOnlyList<PaymentAllocationDto>> GetPaymentAllocationsAsync(Guid loanId, CancellationToken ct = default)
        => _operationsStore.GetPaymentAllocationsAsync(loanId, ct);

    public Task<IReadOnlyList<FeeBalanceDto>> GetFeeBalancesAsync(Guid loanId, CancellationToken ct = default)
        => _operationsStore.GetFeeBalancesAsync(loanId, ct);

    public Task<IReadOnlyList<ProjectionRunDto>> GetProjectionsAsync(Guid loanId, CancellationToken ct = default)
        => _operationsStore.GetProjectionRunsAsync(loanId, ct);

    public Task<IReadOnlyList<ProjectedCashFlowDto>> GetProjectedCashFlowsAsync(Guid projectionRunId, CancellationToken ct = default)
        => _operationsStore.GetProjectedCashFlowsAsync(projectionRunId, ct);

    public Task<IReadOnlyList<JournalEntryDto>> GetJournalsAsync(Guid loanId, CancellationToken ct = default)
        => _operationsStore.GetJournalEntriesAsync(loanId, ct);

    public Task<IReadOnlyList<ReconciliationRunDto>> GetReconciliationRunsAsync(Guid loanId, CancellationToken ct = default)
        => _operationsStore.GetReconciliationRunsAsync(loanId, ct);

    public Task<IReadOnlyList<ReconciliationResultDto>> GetReconciliationResultsAsync(Guid reconciliationRunId, CancellationToken ct = default)
        => _operationsStore.GetReconciliationResultsAsync(reconciliationRunId, ct);

    public Task<IReadOnlyList<ReconciliationExceptionDto>> GetReconciliationExceptionsAsync(CancellationToken ct = default)
        => _operationsStore.GetReconciliationExceptionsAsync(ct);

    public Task<ReconciliationExceptionDto?> ResolveReconciliationExceptionAsync(Guid exceptionId, ResolveReconciliationExceptionRequest request, CancellationToken ct = default)
        => _operationsStore.ResolveReconciliationExceptionAsync(exceptionId, request.ResolutionNote, request.AssignedTo, ct);

    public Task<ServicerReportBatchDto?> GetServicerReportBatchAsync(Guid batchId, CancellationToken ct = default)
        => _operationsStore.GetServicerBatchAsync(batchId, ct);

    public Task<IReadOnlyList<ServicerPositionReportLineDto>> GetServicerPositionLinesAsync(Guid batchId, CancellationToken ct = default)
        => _operationsStore.GetServicerPositionLinesAsync(batchId, ct);

    public Task<IReadOnlyList<ServicerTransactionReportLineDto>> GetServicerTransactionLinesAsync(Guid batchId, CancellationToken ct = default)
        => _operationsStore.GetServicerTransactionLinesAsync(batchId, ct);

    public Task<IReadOnlyList<RebuildCheckpointDto>> GetRebuildCheckpointsAsync(CancellationToken ct = default)
        => _operationsStore.GetCheckpointsAsync(ct);

    public async Task<PersistedDirectLendingState?> LoadAggregateAsync(Guid loanId, CancellationToken ct = default)
    {
        var stored = await _stateStore.LoadAsync(loanId, ct).ConfigureAwait(false);
        if (stored is not null)
        {
            return stored;
        }

        var history = await _stateStore.GetHistoryAsync(loanId, ct).ConfigureAwait(false);
        var rebuilt = _rebuilder.Rebuild(loanId, history);
        if (rebuilt is null)
        {
            return null;
        }

        await _stateStore.SaveStateAsync(loanId, rebuilt.AggregateVersion, rebuilt.Contract, rebuilt.Servicing, ct).ConfigureAwait(false);
        return rebuilt;
    }

    public async Task<LoanPortfolioSummaryDto> GetPortfolioSummaryAsync(CancellationToken ct = default)
    {
        var loanIds = await _operationsStore.GetLoanIdsAsync(ct).ConfigureAwait(false);

        var summaries = new List<LoanSummaryDto>(loanIds.Count);
        foreach (var loanId in loanIds)
        {
            var contract = await _stateStore.LoadContractProjectionAsync(loanId, ct).ConfigureAwait(false);
            var servicing = await _stateStore.LoadServicingProjectionAsync(loanId, ct).ConfigureAwait(false);
            if (contract is null || servicing is null)
            {
                continue;
            }

            summaries.Add(new LoanSummaryDto(
                loanId,
                contract.FacilityName,
                contract.Borrower.BorrowerId,
                contract.Borrower.BorrowerName,
                contract.Status,
                contract.CurrentTerms.BaseCurrency,
                contract.CurrentTerms.CommitmentAmount,
                servicing.Balances.PrincipalOutstanding,
                servicing.Balances.InterestAccruedUnpaid,
                servicing.Balances.PenaltyAccruedUnpaid,
                servicing.AvailableToDraw,
                contract.CurrentTerms.OriginationDate,
                contract.CurrentTerms.MaturityDate,
                servicing.LastAccrualDate,
                servicing.LastPaymentDate));
        }

        var active = summaries.Count(s => s.Status == LoanStatus.Active);
        var defaulted = summaries.Count(s => s.Status == LoanStatus.Defaulted);

        return new LoanPortfolioSummaryDto(
            summaries.Count,
            active,
            defaulted,
            summaries.Sum(s => s.CommitmentAmount),
            summaries.Sum(s => s.PrincipalOutstanding),
            summaries.Sum(s => s.InterestAccruedUnpaid),
            summaries.Sum(s => s.PenaltyAccruedUnpaid),
            summaries.Sum(s => s.AvailableToDraw),
            summaries);
    }
}
