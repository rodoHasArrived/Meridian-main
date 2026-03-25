using Meridian.Contracts.DirectLending;

namespace Meridian.Application.DirectLending;

public interface IDirectLendingService
{
    Task<LoanContractDetailDto> CreateLoanAsync(CreateLoanRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<LoanContractDetailDto?> GetLoanAsync(Guid loanId, CancellationToken ct = default);

    Task<LoanContractDetailDto?> GetContractProjectionAsync(Guid loanId, CancellationToken ct = default);

    Task<LoanAggregateSnapshotDto?> RebuildStateFromHistoryAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<LoanEventLineageDto>> GetHistoryAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<LoanTermsVersionDto>> GetTermsVersionsAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<LoanTermsVersionDto>> GetTermsVersionProjectionsAsync(Guid loanId, CancellationToken ct = default);

    Task<LoanContractDetailDto?> AmendTermsAsync(Guid loanId, AmendLoanTermsRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<LoanContractDetailDto?> ActivateLoanAsync(Guid loanId, ActivateLoanRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<LoanServicingStateDto?> GetServicingStateAsync(Guid loanId, CancellationToken ct = default);

    Task<LoanServicingStateDto?> GetServicingProjectionAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<DrawdownLotDto>> GetDrawdownLotProjectionsAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<ServicingRevisionDto>> GetServicingRevisionProjectionsAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<DailyAccrualEntryDto>> GetAccrualEntryProjectionsAsync(Guid loanId, CancellationToken ct = default);

    Task<LoanServicingStateDto?> BookDrawdownAsync(Guid loanId, BookDrawdownRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<LoanServicingStateDto?> ApplyRateResetAsync(Guid loanId, ApplyRateResetRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<LoanServicingStateDto?> ApplyPrincipalPaymentAsync(Guid loanId, ApplyPrincipalPaymentRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<LoanServicingStateDto?> ApplyMixedPaymentAsync(Guid loanId, ApplyMixedPaymentRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<LoanServicingStateDto?> AssessFeeAsync(Guid loanId, AssessFeeRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<LoanServicingStateDto?> ApplyWriteOffAsync(Guid loanId, ApplyWriteOffRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<DailyAccrualEntryDto?> PostDailyAccrualAsync(Guid loanId, PostDailyAccrualRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<IReadOnlyList<CashTransactionDto>> GetCashTransactionsAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<PaymentAllocationDto>> GetPaymentAllocationsAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<FeeBalanceDto>> GetFeeBalancesAsync(Guid loanId, CancellationToken ct = default);

    Task<ProjectionRunDto> RequestProjectionAsync(Guid loanId, DateOnly? projectionAsOf = null, CancellationToken ct = default);

    Task<IReadOnlyList<ProjectionRunDto>> GetProjectionsAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<ProjectedCashFlowDto>> GetProjectedCashFlowsAsync(Guid projectionRunId, CancellationToken ct = default);

    Task<IReadOnlyList<JournalEntryDto>> GetJournalsAsync(Guid loanId, CancellationToken ct = default);

    Task<JournalEntryDto?> PostJournalAsync(Guid journalEntryId, CancellationToken ct = default);

    Task<ReconciliationRunDto?> ReconcileAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationRunDto>> GetReconciliationRunsAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationResultDto>> GetReconciliationResultsAsync(Guid reconciliationRunId, CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationExceptionDto>> GetReconciliationExceptionsAsync(CancellationToken ct = default);

    Task<ReconciliationExceptionDto?> ResolveReconciliationExceptionAsync(Guid exceptionId, ResolveReconciliationExceptionRequest request, CancellationToken ct = default);

    Task<ServicerReportBatchDto> CreateServicerReportBatchAsync(CreateServicerReportBatchRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<ServicerReportBatchDto?> GetServicerReportBatchAsync(Guid batchId, CancellationToken ct = default);

    Task<IReadOnlyList<ServicerPositionReportLineDto>> GetServicerPositionLinesAsync(Guid batchId, CancellationToken ct = default);

    Task<IReadOnlyList<ServicerTransactionReportLineDto>> GetServicerTransactionLinesAsync(Guid batchId, CancellationToken ct = default);

    Task<IReadOnlyList<RebuildCheckpointDto>> GetRebuildCheckpointsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<LoanAggregateSnapshotDto>> RebuildAllAsync(CancellationToken ct = default);
}
