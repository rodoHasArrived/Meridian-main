using Meridian.Contracts.Accounting.Lots;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>Only actual canonical carrying-basis journals can share coupon income without clearing coupon balances.</summary>
internal static class BondCouponRecognitionNoncashHistory
{
    internal static async Task<IReadOnlyList<LedgerJournalEntryRecord>> ResolveAsync(ILedgerJournalStore ledger,
        AccountCashForecastScopeDto scope, LedgerTaxLotRecord physical, BondCouponBookAccountsDto paths,
        IReadOnlyList<BondCouponRecognitionAccountDto> accounts, CancellationToken ct = default)
    {
        if (physical.LedgerBookId.ToString("D") != scope.BookId || physical.Account.FinancialAccountId != scope.AccountId
            || physical.SecurityId == Guid.Empty || physical.BookPositionId == Guid.Empty)
            throw new InvalidOperationException("Noncash coupon history requires exact physical lot and financial-account scope.");
        var result = new List<LedgerJournalEntryRecord>();
        var visited = new HashSet<Guid>();
        var cursor = physical;
        while (cursor.LastMutationBatchId is { } batchId)
        {
            ct.ThrowIfCancellationRequested();
            if (!visited.Add(batchId) || visited.Count > 10000)
                throw new InvalidOperationException("Noncash coupon history has a cyclic or excessive immutable lot lineage.");
            var receipt = await ledger.GetAtomicTaxLotPostingAsync(batchId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Noncash coupon history requires its complete immutable lot lineage.");
            var mutation = receipt.Mutations.SingleOrDefault(row => row.TaxLotRecordId == physical.TaxLotRecordId);
            if (receipt.MutationBatchId != batchId || mutation is null || mutation.MutationBatchId != batchId
                || mutation.JournalEntryId != receipt.Journal.Entry.JournalEntryId
                || !SameRecord(mutation.LotAfter, cursor))
                throw new InvalidOperationException("Noncash coupon history does not reproduce the actual current lot lineage.");
            if (receipt.MutationKind == AtomicTaxLotMutationKind.Amortization && mutation.LotBefore is { } before)
            {
                var permitted = Canonical(receipt, mutation, scope, paths, accounts);
                if (!permitted && receipt.CorrectsMutationBatchId is { } correctedId)
                {
                    var corrected = await ledger.GetAtomicTaxLotPostingAsync(correctedId, ct).ConfigureAwait(false);
                    var original = corrected?.Mutations.SingleOrDefault(row => row.TaxLotRecordId == physical.TaxLotRecordId);
                    permitted = corrected is not null && original?.LotBefore is { } restored
                        && Canonical(corrected, original, scope, paths, accounts)
                        && SameRecord(before, original.LotAfter)
                        && mutation.LotAfter.Version == before.Version + 1
                        && mutation.LotAfter.OpenQuantity == before.OpenQuantity
                        && mutation.LotAfter.OriginalQuantity == before.OriginalQuantity
                        && receipt.Journal.AccountingBasis == AccountingBasisKindDto.Gaap
                        && receipt.Journal.AggregateId == physical.LedgerBookId
                        && receipt.Journal.SourceEventId == mutation.SourceEventId
                        && receipt.Journal.SourceJournalEntryId == corrected.Journal.Entry.JournalEntryId
                        && receipt.Journal.PostingKind == LedgerPostingKindDto.Adjustment
                        && mutation.LotAfter.ToOpenLot().OpenTransactionCostBasis == restored.ToOpenLot().OpenTransactionCostBasis
                        && mutation.LotAfter.ToOpenLot().OpenFunctionalCostBasis == restored.ToOpenLot().OpenFunctionalCostBasis
                        && Inverse(receipt.Journal.Entry, corrected.Journal.Entry);
                }
                if (permitted) result.Add(receipt.Journal);
            }
            if (mutation.LotBefore is not { } predecessor) break;
            if (predecessor.TaxLotRecordId != physical.TaxLotRecordId || predecessor.LedgerBookId != physical.LedgerBookId
                || predecessor.Version >= cursor.Version || predecessor.SecurityId != physical.SecurityId
                || predecessor.BookPositionId != physical.BookPositionId || predecessor.Account != physical.Account)
                throw new InvalidOperationException("Noncash coupon history crosses an unrelated lot or ownership boundary.");
            cursor = predecessor;
        }
        return result;
    }

    private static bool Canonical(AtomicTaxLotJournalResult receipt, LedgerTaxLotMutationRecord mutation,
        AccountCashForecastScopeDto scope, BondCouponBookAccountsDto paths, IReadOnlyList<BondCouponRecognitionAccountDto> accounts)
    {
        if (receipt.MutationKind != AtomicTaxLotMutationKind.Amortization || mutation.MutationKind != AtomicTaxLotMutationKind.Amortization
            || mutation.LotBefore is not { } before || mutation.LotAfter.BasisAdjustment is not
                { Reason: OpenLotBasisAdjustmentReasons.Amortization, Amortization: { } inputs } adjustment
            || adjustment.MutationBatchId != receipt.MutationBatchId || inputs.Reversal is not null
            || !OpenLotAmortization.SameLot(inputs.ExpectedLot, before.ToOpenLot())
            || mutation.LotAfter.Version != before.Version + 1 || mutation.LotAfter.OpenQuantity != before.OpenQuantity
            || mutation.LotAfter.OriginalQuantity != before.OriginalQuantity || mutation.LotAfter.Account != before.Account
            || receipt.Journal.AggregateId != before.LedgerBookId || receipt.Journal.AccountingBasis != AccountingBasisKindDto.Gaap
            || receipt.Journal.Entry.Metadata.EffectiveDate != inputs.AsOfDate || !receipt.Journal.Entry.IsBalanced
            || receipt.Journal.SourceEventId != mutation.SourceEventId || receipt.Journal.Entry.Lines.Count != 2)
            return false;
        OpenLotAmortizationProjectionDto projected;
        try { projected = OpenLotAmortization.Project(inputs); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or ArithmeticException) { return false; }
        var after = mutation.LotAfter.ToOpenLot();
        if (projected.FunctionalMovement == 0m || projected.TransactionCostBasis != after.OpenTransactionCostBasis
            || projected.FunctionalCostBasis != after.OpenFunctionalCostBasis)
            return false;
        var income = accounts.Single(row => row.Path == paths.Income);
        var protectedAccounts = accounts.Where(row => row.Path == paths.Receivable || row.Path == paths.Payable).ToArray();
        var lines = receipt.Journal.Entry.Lines;
        if (lines.Any(line => protectedAccounts.Any(account => account.LedgerName == line.Account.Name
                && account.AccountType == line.Account.AccountType.ToString())
            || line.Account.FinancialAccountId != scope.AccountId
            || line.Dimensions?.AccountId != scope.AccountId || line.Dimensions.BookId != scope.BookId
            || line.Dimensions.InstrumentId != before.SecurityId || line.Dimensions.PositionId != before.BookPositionId
            || line.Dimensions.TaxLotId != before.LotId
            || line.Currency is not { } currency || currency.TransactionCurrency != before.Acquisition!.AcquisitionCurrency
            || currency.FunctionalCurrency != scope.BaseCurrency
            || currency.FxRateToFunctional != before.Acquisition.AcquisitionFxRateToFunctional))
            return false;
        var asset = lines.SingleOrDefault(line => line.Account == before.Account);
        var counter = lines.SingleOrDefault(line => line.Account.Name == income.LedgerName
            && line.Account.AccountType.ToString() == income.AccountType && line.Account.Symbol is null);
        return asset is not null && counter is not null && !ReferenceEquals(asset, counter)
            && asset.Debit - asset.Credit == projected.FunctionalMovement
            && asset.Currency!.TransactionDebit - asset.Currency.TransactionCredit == projected.TransactionMovement
            && counter.Debit - counter.Credit == -projected.FunctionalMovement
            && counter.Currency!.TransactionDebit - counter.Currency.TransactionCredit == -projected.TransactionMovement;
    }

    private static bool SameRecord(LedgerTaxLotRecord left, LedgerTaxLotRecord right)
        => left.Account == right.Account && left.Version == right.Version && left.LastMutationBatchId == right.LastMutationBatchId
            && left.OriginatingMutationBatchId == right.OriginatingMutationBatchId && left.SourceJournalEntryId == right.SourceJournalEntryId
            && OpenLotAmortization.SameLot(left.ToOpenLot(), right.ToOpenLot());

    private static bool Inverse(JournalEntry inverse, JournalEntry source)
        => inverse.IsBalanced && inverse.Lines.Count == source.Lines.Count
            && source.Lines.All(original => inverse.Lines.Count(line => line.Account == original.Account
                && line.Debit == original.Credit && line.Credit == original.Debit
                && JsonElement.DeepEquals(JsonSerializer.SerializeToElement(line.Dimensions), JsonSerializer.SerializeToElement(original.Dimensions))
                && line.Currency is { } currency && original.Currency is { } prior
                && currency.TransactionCurrency == prior.TransactionCurrency && currency.FunctionalCurrency == prior.FunctionalCurrency
                && currency.FxRateToFunctional == prior.FxRateToFunctional
                && currency.TransactionDebit == prior.TransactionCredit && currency.TransactionCredit == prior.TransactionDebit) == 1);
}
