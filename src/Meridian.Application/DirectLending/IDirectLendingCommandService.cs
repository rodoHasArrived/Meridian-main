using Meridian.Contracts.DirectLending;

namespace Meridian.Application.DirectLending;

public interface IDirectLendingCommandService
{
    Task<DirectLendingCommandResult<bool>> PublishAssetOperationsAsync(Guid loanId, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

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

    Task<DirectLendingCommandResult<LoanServicingStateDto>> ChargePrepaymentPenaltyAsync(Guid loanId, ChargePrepaymentPenaltyRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<DirectLendingCommandResult<LoanServicingStateDto>> AddCollateralAsync(Guid loanId, AddCollateralRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<DirectLendingCommandResult<LoanServicingStateDto>> RemoveCollateralAsync(Guid loanId, RemoveCollateralRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<DirectLendingCommandResult<LoanServicingStateDto>> UpdateCollateralValueAsync(Guid loanId, UpdateCollateralValueRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<DirectLendingCommandResult<LoanServicingStateDto>> TransitionLoanStatusAsync(Guid loanId, TransitionLoanStatusRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<DirectLendingCommandResult<LoanServicingStateDto>> TogglePikAsync(Guid loanId, TogglePikRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<DirectLendingCommandResult<LoanContractDetailDto>> RestructureLoanAsync(Guid loanId, RestructureLoanRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<DirectLendingCommandResult<LoanServicingStateDto>> AmortizeDiscountPremiumAsync(Guid loanId, AmortizeDiscountPremiumRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<DirectLendingCommandResult<IReadOnlyList<LoanAggregateSnapshotDto>>> RebuildAllAsync(CancellationToken ct = default);
}

