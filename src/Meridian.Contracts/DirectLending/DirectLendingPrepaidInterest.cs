namespace Meridian.Contracts.DirectLending;

/// <summary>Applies an explicitly retained receipt instruction to subsequently earned cash interest.</summary>
public static class DirectLendingPrepaidInterest
{
    public static IReadOnlyList<PrepaidInterestLotDto> CreateReceiptLots(
        LoanServicingStateDto servicing, DirectLendingTermsDto terms, Guid cashTransactionId,
        DateOnly receiptDate, decimal prepaidAmount)
    {
        var lots = ValidateLots(servicing).ToList();
        if (prepaidAmount == 0m)
            return lots;
        if (prepaidAmount < 0m || !DirectLendingPaymentResolver.IsLedgerAmount(prepaidAmount) ||
            cashTransactionId == Guid.Empty || lots.Any(item => item.CashTransactionId == cashTransactionId))
            throw new ArgumentException("Prepaid receipt identity or amount is invalid.");
        if (DirectLendingPaymentResolver.ValidatePolicy(terms) is { } policyError)
            throw new ArgumentException(policyError);
        if (terms.ExcessPaymentPolicy is not { Treatment: ExcessPaymentTreatment.PrepaidInterest } policy ||
            receiptDate >= terms.MaturityDate)
            throw new ArgumentException("Prepaid interest requires a retained contractual election before maturity.");

        lots.Add(new PrepaidInterestLotDto(cashTransactionId, receiptDate, prepaidAmount, prepaidAmount,
            policy.Application, policy.ContractualReference));
        return lots.OrderBy(item => item.ReceiptDate).ThenBy(item => item.CashTransactionId).ToArray();
    }

    public static PrepaidInterestApplicationResultDto ApplyAccrual(
        LoanServicingStateDto servicing, decimal cashInterest, DateOnly accrualDate)
    {
        var lots = ValidateLots(servicing);
        if (cashInterest < 0m)
            throw new ArgumentException("Earned cash interest cannot be negative.", nameof(cashInterest));
        var remainingInterest = cashInterest;
        var releases = new List<PrepaidInterestReleaseDto>();
        foreach (var lot in lots.OrderBy(item => item.ReceiptDate).ThenBy(item => item.CashTransactionId))
        {
            if (remainingInterest <= 0m)
                break;
            if (lot.Application != PrepaidInterestApplication.FutureCashInterest ||
                accrualDate <= lot.ReceiptDate || lot.RemainingAmount == 0m)
                continue;
            var amount = Math.Min(remainingInterest, lot.RemainingAmount);
            releases.Add(new PrepaidInterestReleaseDto(lot.CashTransactionId, amount));
            remainingInterest -= amount;
        }

        return ApplyRecordedRelease(servicing, cashInterest, accrualDate, cashInterest - remainingInterest, releases);
    }

    /// <summary>Replays retained deltas, never current terms or a newly calculated allocation.</summary>
    public static PrepaidInterestApplicationResultDto ApplyRecordedRelease(
        LoanServicingStateDto servicing, decimal cashInterest, DateOnly accrualDate,
        decimal appliedAmount, IReadOnlyList<PrepaidInterestReleaseDto>? releases)
    {
        var lots = ValidateLots(servicing);
        var retainedReleases = releases ?? [];
        if (appliedAmount < 0m || appliedAmount > cashInterest || appliedAmount > servicing.Balances.PrepaidInterest ||
            !DirectLendingPaymentResolver.IsLedgerAmount(appliedAmount) || retainedReleases.Sum(item => item.Amount) != appliedAmount ||
            retainedReleases.Select(item => item.CashTransactionId).Distinct().Count() != retainedReleases.Count)
            throw new InvalidOperationException("Prepaid release must reconcile to retained credits and earned cash interest.");

        var byId = lots.Select((lot, index) => (lot.CashTransactionId, index)).ToDictionary(item => item.CashTransactionId, item => item.index);
        foreach (var release in retainedReleases)
        {
            if (release.Amount <= 0m || !DirectLendingPaymentResolver.IsLedgerAmount(release.Amount) ||
                !byId.TryGetValue(release.CashTransactionId, out var index))
                throw new InvalidOperationException("Prepaid release has an unsupported receipt identity or amount.");
            var lot = lots[index];
            if (lot.Application != PrepaidInterestApplication.FutureCashInterest ||
                accrualDate <= lot.ReceiptDate || release.Amount > lot.RemainingAmount)
                throw new InvalidOperationException("Prepaid release exceeds the retained receipt instruction or available credit.");
            lots[index] = lot with { RemainingAmount = lot.RemainingAmount - release.Amount };
        }

        return new PrepaidInterestApplicationResultDto(appliedAmount, lots, retainedReleases.ToArray());
    }

    private static PrepaidInterestLotDto[] ValidateLots(LoanServicingStateDto servicing)
    {
        var lots = servicing.Balances.PrepaidInterestLots?.ToArray() ?? [];
        if (lots.Any(item => item.CashTransactionId == Guid.Empty || item.OriginalAmount <= 0m ||
                item.RemainingAmount < 0m || item.RemainingAmount > item.OriginalAmount ||
                !DirectLendingPaymentResolver.IsLedgerAmount(item.OriginalAmount) ||
                !DirectLendingPaymentResolver.IsLedgerAmount(item.RemainingAmount) ||
                !Enum.IsDefined(item.Application) || string.IsNullOrWhiteSpace(item.ContractualReference)) ||
            lots.Select(item => item.CashTransactionId).Distinct().Count() != lots.Length ||
            lots.Sum(item => item.RemainingAmount) > servicing.Balances.PrepaidInterest)
            throw new InvalidOperationException("Retained prepaid-interest credits are inconsistent with their liability balance.");
        // A legacy balance without receipt-level instructions stays held; it is never granted a release rule.
        return lots;
    }
}
