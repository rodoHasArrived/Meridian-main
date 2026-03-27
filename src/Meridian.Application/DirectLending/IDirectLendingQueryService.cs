using Meridian.Contracts.DirectLending;
using Meridian.Storage.DirectLending;

namespace Meridian.Application.DirectLending;

public interface IDirectLendingQueryService
{
    Task<LoanContractDetailDto?> GetLoanAsync(Guid loanId, CancellationToken ct = default);

    Task<LoanContractDetailDto?> GetContractProjectionAsync(Guid loanId, CancellationToken ct = default);

    Task<LoanAggregateSnapshotDto?> RebuildStateFromHistoryAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<LoanEventLineageDto>> GetHistoryAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<LoanTermsVersionDto>> GetTermsVersionsAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<LoanTermsVersionDto>> GetTermsVersionProjectionsAsync(Guid loanId, CancellationToken ct = default);

    Task<LoanServicingStateDto?> GetServicingStateAsync(Guid loanId, CancellationToken ct = default);

    Task<LoanServicingStateDto?> GetServicingProjectionAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<DrawdownLotDto>> GetDrawdownLotProjectionsAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<ServicingRevisionDto>> GetServicingRevisionProjectionsAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<DailyAccrualEntryDto>> GetAccrualEntryProjectionsAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<CashTransactionDto>> GetCashTransactionsAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<PaymentAllocationDto>> GetPaymentAllocationsAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<FeeBalanceDto>> GetFeeBalancesAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<ProjectionRunDto>> GetProjectionsAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<ProjectedCashFlowDto>> GetProjectedCashFlowsAsync(Guid projectionRunId, CancellationToken ct = default);

    Task<IReadOnlyList<JournalEntryDto>> GetJournalsAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationRunDto>> GetReconciliationRunsAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationResultDto>> GetReconciliationResultsAsync(Guid reconciliationRunId, CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationExceptionDto>> GetReconciliationExceptionsAsync(CancellationToken ct = default);

    Task<ReconciliationExceptionDto?> ResolveReconciliationExceptionAsync(Guid exceptionId, ResolveReconciliationExceptionRequest request, CancellationToken ct = default);

    Task<ServicerReportBatchDto?> GetServicerReportBatchAsync(Guid batchId, CancellationToken ct = default);

    Task<IReadOnlyList<ServicerPositionReportLineDto>> GetServicerPositionLinesAsync(Guid batchId, CancellationToken ct = default);

    Task<IReadOnlyList<ServicerTransactionReportLineDto>> GetServicerTransactionLinesAsync(Guid batchId, CancellationToken ct = default);

    Task<IReadOnlyList<RebuildCheckpointDto>> GetRebuildCheckpointsAsync(CancellationToken ct = default);

    Task<PersistedDirectLendingState?> LoadAggregateAsync(Guid loanId, CancellationToken ct = default);

    Task<LoanPortfolioSummaryDto> GetPortfolioSummaryAsync(CancellationToken ct = default);
}
