using Meridian.Contracts.DirectLending;
using Meridian.Contracts.Ledger;
using Meridian.Storage.DirectLending;
using Meridian.Storage.Ledger;

namespace Meridian.Application.DirectLending;

/// <summary>
/// Defines journal projection for daily loan accruals and accrual reversals.
/// </summary>
public interface IAccrualLedgerService
{
    Task<IReadOnlyList<LedgerJournalEntryWrite>> AccrueAsync(
        Guid loanId,
        LoanContractDetailDto contract,
        PostDailyAccrualRequest request,
        DailyAccrualEntryDto accrual,
        Guid sourceEventId,
        DirectLendingEventWriteMetadata metadata,
        CancellationToken ct = default,
        decimal prepaidInterestConsumed = 0m);

    Task<IReadOnlyList<LedgerJournalEntryWrite>> ReverseAccrualAsync(
        Guid loanId,
        LoanContractDetailDto contract,
        DailyAccrualEntryDto accrual,
        Guid sourceEventId,
        DirectLendingEventWriteMetadata metadata,
        CancellationToken ct = default,
        LedgerAdjustmentApprovalMetadataDto? adjustmentApproval = null);

    /// <summary>Projects a PIK capitalization: Dr LoanPrincipal / Cr PikInterestReceivable.</summary>
    Task<IReadOnlyList<LedgerJournalEntryWrite>> CapitalizePikAsync(
        Guid loanId,
        LoanContractDetailDto contract,
        PikCapitalizationDto capitalization,
        Guid sourceEventId,
        DirectLendingEventWriteMetadata metadata,
        CancellationToken ct = default);
}
