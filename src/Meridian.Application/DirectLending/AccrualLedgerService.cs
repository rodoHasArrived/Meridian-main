using Meridian.Contracts.DirectLending;
using Meridian.Contracts.Ledger;
using Meridian.Storage.DirectLending;
using Meridian.Storage.Ledger;

namespace Meridian.Application.DirectLending;

public sealed class AccrualLedgerService : IAccrualLedgerService
{
    private readonly LoanAccountingProjector _projector;

    public AccrualLedgerService(LoanAccountingProjector projector)
    {
        _projector = projector;
    }

    public Task<IReadOnlyList<LedgerJournalEntryWrite>> AccrueAsync(
        Guid loanId,
        LoanContractDetailDto contract,
        PostDailyAccrualRequest request,
        DailyAccrualEntryDto accrual,
        Guid sourceEventId,
        DirectLendingEventWriteMetadata metadata,
        CancellationToken ct = default)
    {
        using var payload = DirectLendingServiceSupport.CreatePayloadDocument(new
        {
            loanId,
            accrual.AccrualEntryId,
            request.AccrualDate,
            accrual.InterestAmount,
            accrual.CommitmentFeeAmount,
            accrual.PenaltyAmount,
            accrual.AnnualRateApplied
        });

        return _projector.ProjectAsync(
            loanId,
            contract,
            "loan.daily-accrual-posted",
            request.AccrualDate,
            payload,
            sourceEventId,
            metadata,
            ct);
    }

    public Task<IReadOnlyList<LedgerJournalEntryWrite>> ReverseAccrualAsync(
        Guid loanId,
        LoanContractDetailDto contract,
        DailyAccrualEntryDto accrual,
        Guid sourceEventId,
        DirectLendingEventWriteMetadata metadata,
        CancellationToken ct = default)
    {
        using var payload = DirectLendingServiceSupport.CreatePayloadDocument(new
        {
            loanId,
            accrual.AccrualEntryId,
            AccrualDate = accrual.AccrualDate,
            accrual.InterestAmount,
            accrual.CommitmentFeeAmount,
            accrual.PenaltyAmount,
            accrual.AnnualRateApplied
        });

        return _projector.ProjectAsync(
            loanId,
            contract,
            "loan.daily-accrual-reversed",
            accrual.AccrualDate,
            payload,
            sourceEventId,
            metadata,
            ct,
            LedgerPostingKindDto.Adjustment);
    }
}
