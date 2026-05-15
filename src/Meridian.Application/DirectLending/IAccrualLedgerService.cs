using Meridian.Contracts.DirectLending;
using Meridian.Storage.DirectLending;
using Meridian.Storage.Ledger;

namespace Meridian.Application.DirectLending;

public interface IAccrualLedgerService
{
    Task<IReadOnlyList<LedgerJournalEntryWrite>> AccrueAsync(
        Guid loanId,
        LoanContractDetailDto contract,
        PostDailyAccrualRequest request,
        DailyAccrualEntryDto accrual,
        Guid sourceEventId,
        DirectLendingEventWriteMetadata metadata,
        CancellationToken ct = default);

    Task<IReadOnlyList<LedgerJournalEntryWrite>> ReverseAccrualAsync(
        Guid loanId,
        LoanContractDetailDto contract,
        DailyAccrualEntryDto accrual,
        Guid sourceEventId,
        DirectLendingEventWriteMetadata metadata,
        CancellationToken ct = default);
}
