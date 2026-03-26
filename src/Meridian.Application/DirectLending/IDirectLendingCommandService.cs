using Meridian.Contracts.DirectLending;

namespace Meridian.Application.DirectLending;

public interface IDirectLendingCommandService
{
    Task<DirectLendingCommandResult<LoanContractDetailDto>> CreateLoanAsync(CreateLoanRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<DirectLendingCommandResult<LoanContractDetailDto>> AmendTermsAsync(Guid loanId, AmendLoanTermsRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<DirectLendingCommandResult<LoanContractDetailDto>> ActivateLoanAsync(Guid loanId, ActivateLoanRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<DirectLendingCommandResult<LoanServicingStateDto>> BookDrawdownAsync(Guid loanId, BookDrawdownRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<DirectLendingCommandResult<LoanServicingStateDto>> ApplyRateResetAsync(Guid loanId, ApplyRateResetRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<DirectLendingCommandResult<LoanServicingStateDto>> ApplyPrincipalPaymentAsync(Guid loanId, ApplyPrincipalPaymentRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<DirectLendingCommandResult<LoanServicingStateDto>> ApplyMixedPaymentAsync(Guid loanId, ApplyMixedPaymentRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<DirectLendingCommandResult<LoanServicingStateDto>> AssessFeeAsync(Guid loanId, AssessFeeRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<DirectLendingCommandResult<LoanServicingStateDto>> ApplyWriteOffAsync(Guid loanId, ApplyWriteOffRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<DirectLendingCommandResult<DailyAccrualEntryDto>> PostDailyAccrualAsync(Guid loanId, PostDailyAccrualRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<DirectLendingCommandResult<ProjectionRunDto>> RequestProjectionAsync(Guid loanId, DateOnly? projectionAsOf = null, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<DirectLendingCommandResult<JournalEntryDto>> PostJournalAsync(Guid journalEntryId, CancellationToken ct = default);

    Task<DirectLendingCommandResult<ReconciliationRunDto>> ReconcileAsync(Guid loanId, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<DirectLendingCommandResult<ServicerReportBatchDto>> CreateServicerReportBatchAsync(CreateServicerReportBatchRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<DirectLendingCommandResult<IReadOnlyList<LoanAggregateSnapshotDto>>> RebuildAllAsync(CancellationToken ct = default);
}

