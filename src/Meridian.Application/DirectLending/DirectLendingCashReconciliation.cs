using System.Globalization;
using Meridian.Contracts.DirectLending;

namespace Meridian.Application.DirectLending;

/// <summary>Retained receipt-to-allocation evidence; this does not certify external cash matching.</summary>
internal static class DirectLendingCashReconciliation
{
    public static IReadOnlyList<ReconciliationResultDto> Build(
        Guid loanId,
        Guid runId,
        IReadOnlyList<CashTransactionDto> cashTransactions,
        IReadOnlyList<PaymentAllocationDto> allocations,
        IReadOnlyList<DailyAccrualEntryDto>? accrualEntries = null,
        IReadOnlyList<JournalEntryDto>? journalEntries = null)
    {
        var activeReceipts = cashTransactions.Where(item => !item.IsVoided && item.TransactionType == "MixedPayment").ToArray();
        var receiptIds = activeReceipts.Where(item => item.LoanId == loanId).Select(item => item.CashTransactionId).ToHashSet();
        var byReceipt = allocations.ToLookup(item => item.CashTransactionId);
        var releasesByReceipt = (accrualEntries ?? []).SelectMany(item => item.PrepaidInterestReleases ?? [])
            .ToLookup(item => item.CashTransactionId);
        var invalidReleaseReceipts = new HashSet<Guid>();
        var unattributedRelease = false;
        var results = new List<ReconciliationResultDto>();
        foreach (var entry in accrualEntries ?? [])
        {
            var releases = entry.PrepaidInterestReleases ?? [];
            if (entry.PrepaidInterestConsumed == 0m && releases.Count == 0)
                continue;

            var orphanedReleases = releases.Where(item => item.CashTransactionId == Guid.Empty || !receiptIds.Contains(item.CashTransactionId)).ToArray();
            var missingReceiptEvidence = releases.Count == 0 || orphanedReleases.Length > 0;
            if (missingReceiptEvidence)
            {
                unattributedRelease = true;
                var unsupportedAmount = releases.Count == 0 ? entry.PrepaidInterestConsumed : orphanedReleases.Sum(item => item.Amount);
                results.Add(new ReconciliationResultDto(
                    Guid.NewGuid(), runId, loanId, null, null, "CashAllocationEvidenceInvalid",
                    0m, unsupportedAmount, -unsupportedAmount, entry.AccrualDate, entry.AccrualDate,
                    "RetainedPrepaidReleaseEvidence", "{\"amountTolerance\":0}",
                    [
                        $"Accrual entry: {entry.AccrualEntryId:D}; unapportioned prepaid-interest application: {unsupportedAmount.ToString(CultureInfo.InvariantCulture)}.",
                        "Retained release evidence does not identify an active cash receipt. This is an internal release-evidence difference; it does not certify an external statement match."
                    ], DateTimeOffset.UtcNow));
            }

            if (missingReceiptEvidence || entry.PrepaidInterestConsumed < 0m || entry.PrepaidInterestConsumed > entry.InterestAmount ||
                releases.Sum(item => item.Amount) != entry.PrepaidInterestConsumed ||
                releases.Select(item => item.CashTransactionId).Distinct().Count() != releases.Count ||
                releases.Any(item => item.CashTransactionId == Guid.Empty || item.Amount <= 0m ||
                    !DirectLendingPaymentResolver.IsLedgerAmount(item.Amount)))
            {
                foreach (var release in releases)
                    invalidReleaseReceipts.Add(release.CashTransactionId);
            }
        }
        foreach (var cash in activeReceipts)
        {
            var receiptAllocations = byReceipt[cash.CashTransactionId].ToArray();
            var accountedFor = receiptAllocations.Sum(item => item.AllocatedAmount);
            var unapplied = receiptAllocations.Where(item => item.TargetType == "UnappliedCash").Sum(item => item.AllocatedAmount);
            var prepaid = receiptAllocations.Where(item => item.TargetType == "PrepaidInterest").Sum(item => item.AllocatedAmount);
            var releases = releasesByReceipt[cash.CashTransactionId].ToArray();
            var releasedPrepaid = releases.Sum(item => item.Amount);
            var variance = cash.Amount - accountedFor;
            var invalidEvidence = cash.LoanId != loanId || invalidReleaseReceipts.Contains(cash.CashTransactionId) ||
                (unattributedRelease && prepaid > 0m) || releasedPrepaid > prepaid || releases.Any(item => item.Amount <= 0m) || receiptAllocations.Any(item =>
                item.LoanId != loanId || item.SourceEventId != cash.SourceEventId || item.AllocatedAmount <= 0m ||
                item.TargetType is not ("Interest" or "InterestAccrued" or "CommitmentFee" or "CommitmentFeeAccrued" or
                    "Fees" or "FeeBalance" or "Penalty" or "PenaltyAccrued" or "Principal" or "PrincipalOutstanding" or
                    "UnappliedCash" or "PrepaidInterest"));
            var status = invalidEvidence ? "CashAllocationEvidenceInvalid"
                : variance != 0m ? "CashAllocationDifference"
                : unapplied > 0m ? "UnappliedCashHeld"
                : prepaid > releasedPrepaid ? "PrepaidInterestHeld"
                : "CashAllocationBalanced";
            results.Add(new ReconciliationResultDto(
                Guid.NewGuid(), runId, loanId, null, cash.CashTransactionId, status,
                cash.Amount, accountedFor, variance, cash.SettlementDate, cash.SettlementDate,
                "RetainedCashAllocation", "{\"amountTolerance\":0}",
                [
                    $"Source event: {cash.SourceEventId:D}; currency: {cash.Currency}.",
                    $"Unapplied cash retained: {unapplied.ToString(CultureInfo.InvariantCulture)}; original prepaid interest: {prepaid.ToString(CultureInfo.InvariantCulture)}; applied to earned cash interest: {releasedPrepaid.ToString(CultureInfo.InvariantCulture)}; remaining prepaid credit: {(prepaid - releasedPrepaid).ToString(CultureInfo.InvariantCulture)}.",
                    invalidEvidence
                        ? "Allocation identity, source, amount, or target is invalid. Numeric equality alone does not establish supported cash accounting."
                    : variance != 0m
                        ? "Retained allocations do not explain the receipt. Historical cash requires explicit remediation; no balance has been inferred."
                        : "Receipt accounting is complete. Held cash remains a liability; external statement matching is assessed separately."
                ], DateTimeOffset.UtcNow));
        }

        // Operational journals are a retained source of historical clearing evidence. They
        // are not the authoritative general ledger, and neither a bank match nor a proposed
        // reclassification follows from their residual. Keep currencies and bases separate.
        var clearingLines = (journalEntries ?? [])
            .Where(entry => entry.LoanId == loanId && entry.Status is JournalEntryStatus.Draft or JournalEntryStatus.Posted)
            .SelectMany(entry => entry.Lines
                .Where(line => string.Equals(line.AccountCode, "LoanAndAccrualsClearing", StringComparison.OrdinalIgnoreCase))
                .Select(line => (Entry: entry, Line: line)));
        foreach (var group in clearingLines.GroupBy(item => (item.Line.Currency, item.Entry.LedgerBasis)))
        {
            var debits = group.Sum(item => item.Line.DebitAmount);
            var credits = group.Sum(item => item.Line.CreditAmount);
            var residual = credits - debits;
            if (residual == 0m)
                continue;
            var entries = group.Select(item => item.Entry).DistinctBy(item => item.JournalEntryId).ToArray();
            results.Add(new ReconciliationResultDto(
                Guid.NewGuid(), runId, loanId, null, null, "HistoricalClearingResidual",
                null, residual, null, null, entries.Max(item => item.AccountingDate),
                "RetainedOperationalClearing", null,
                [
                    $"Currency: {group.Key.Currency}; ledger basis: {group.Key.LedgerBasis}; account: LoanAndAccrualsClearing; credit-positive residual: {residual.ToString(CultureInfo.InvariantCulture)}.",
                    $"Retained debits: {debits.ToString(CultureInfo.InvariantCulture)}; retained credits: {credits.ToString(CultureInfo.InvariantCulture)}; draft entries: {entries.Count(item => item.Status == JournalEntryStatus.Draft)}; posted entries: {entries.Count(item => item.Status == JournalEntryStatus.Posted)}.",
                    $"Source events: {string.Join(", ", entries.Select(item => item.SourceEventId).Distinct().OrderBy(item => item).Select(item => item.ToString("D")))}.",
                    "This residual comes from retained operational journals, including drafts. It does not certify the authoritative general-ledger balance or an external cash match. Historical postings are unchanged; any reclassification requires Accounting review."
                ], DateTimeOffset.UtcNow));
        }

        return results;
    }
}
