using Meridian.Ledger;
using static Meridian.Contracts.Ledger.LedgerCurrencyRounding;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private static decimal DisposalRecognizedGainOrLoss(JournalEntry entry, LedgerAccount assetAccount)
    {
        var financialAccountId = assetAccount.FinancialAccountId;
        var gainAccount = string.IsNullOrWhiteSpace(financialAccountId)
            ? LedgerAccounts.RealizedGain : LedgerAccounts.RealizedGainFor(financialAccountId);
        var lossAccount = string.IsNullOrWhiteSpace(financialAccountId)
            ? LedgerAccounts.RealizedLoss : LedgerAccounts.RealizedLossFor(financialAccountId);
        // Names identify candidate result lines only; full account equality establishes authority.
        // Validate both price paths before a same-named asset can pass as replacement basis.
        if (entry.Lines.Any(line =>
                (line.Account.Name == LedgerAccounts.RealizedGain.Name &&
                    (line.Account != gainAccount || line.Debit != 0m)) ||
                (line.Account.Name == LedgerAccounts.RealizedLoss.Name &&
                    (line.Account != lossAccount || line.Credit != 0m))))
            throw new LedgerValidationException("Disposal realized-result lines require the disposing account's exact gain/credit or loss/debit accounts.");

        return entry.Lines.Where(line => line.Account == gainAccount).Sum(static line => line.Credit)
            - entry.Lines.Where(line => line.Account == lossAccount).Sum(static line => line.Debit);
    }

    private static void ValidateDisposalProceedsAllocation(
        AtomicTaxLotJournalCommand command, IReadOnlyList<LedgerTaxLotMutationRecord> mutations)
    {
        var account = mutations[0].LotAfter.Account;
        var recognizedResult = DisposalRecognizedGainOrLoss(command.Journal.Entry, account);
        var basis = mutations.Sum(static mutation => mutation.CostBasis);
        var proceeds = command.DisposalSalePrice is { } salePrice
            ? ValidateExplicitDisposalPrice(command, account, salePrice)
            : basis + recognizedResult;
        var pooled = IsAverageCostRelief(command);
        var retained = new LedgerTaxLotDisposalHistory(
            command.MutationBatchId, command.Journal.Entry.JournalEntryId, account,
            command.Journal.Entry.Metadata.EffectiveDate!.Value,
            Enum.Parse<LedgerTaxLotReliefMethod>(command.ReliefMethod!, ignoreCase: true),
            mutations.Select(mutation => new LedgerTaxLotDisposalHistoryLot(
                mutation.LotId, mutation.LotBefore!.AcquiredDate, mutation.LotBefore.AcquiredDate,
                -mutation.QuantityDelta,
                pooled ? mutation.CostBasis / -mutation.QuantityDelta : mutation.UnitCost, mutation.CostBasis)).ToArray(),
            // This checks the economic allocator, not a fabricated deferral outcome. The report
            // later reconciles its actual retained deferrals to the journal's recognized result.
            proceeds - basis, [], 0m,
            LedgerTaxLotReliefProjector.CurrentProceedsAllocationVersion, command.DisposalSalePrice);
        if (LedgerTaxLotReliefHistoryProjector.Project(retained) is null)
            throw new LedgerValidationException("Disposal cannot retain a reconstructable sign-preserving proceeds allocation.");
    }

    private static decimal ValidateExplicitDisposalPrice(
        AtomicTaxLotJournalCommand command, LedgerAccount assetAccount, decimal salePrice)
    {
        var cashAccount = string.IsNullOrWhiteSpace(assetAccount.FinancialAccountId)
            ? LedgerAccounts.Cash : LedgerAccounts.CashAccount(assetAccount.FinancialAccountId);
        var currencyCashAccount = LedgerAccounts.CashInCurrency(
            ResolveAtomicFunctionalCurrency(command.Journal), assetAccount.FinancialAccountId);
        var gainAccount = string.IsNullOrWhiteSpace(assetAccount.FinancialAccountId)
            ? LedgerAccounts.RealizedGain : LedgerAccounts.RealizedGainFor(assetAccount.FinancialAccountId);
        var lossAccount = string.IsNullOrWhiteSpace(assetAccount.FinancialAccountId)
            ? LedgerAccounts.RealizedLoss : LedgerAccounts.RealizedLossFor(assetAccount.FinancialAccountId);
        bool IsCash(LedgerEntry line) => line.Account == cashAccount || line.Account == currencyCashAccount;
        var entry = command.Journal.Entry;
        var cashLines = entry.Lines.Where(IsCash).ToArray();
        var quantity = command.DisposalSelections!.Sum(static selection => selection.Quantity);
        var quotedProceeds = RoundCurrency(quantity * salePrice);

        // An explicit quote is an opt-in source assertion. Support the defined cash/asset/realized
        // result posting shape, including a sibling replacement-basis debit, without guessing that
        // an arbitrary asset movement or expense is cash proceeds. Aggregate-only callers retain
        // their prior journal convention and need no quote or new account classification.
        if ((cashLines.Length == 0 && quotedProceeds != 0m) ||
            cashLines.Any(static line => line.Credit != 0m) ||
            entry.Lines.Any(line => !IsCash(line) && line.Account != assetAccount &&
                line.Account != gainAccount &&
                line.Account != lossAccount &&
                !(line.Account.AccountType == LedgerAccountType.Asset && line.Credit == 0m && line.Debit > 0m)))
            throw new LedgerValidationException("An explicit disposal sale price requires supported cash, asset-basis, and realized-result journal lines.");

        if (quotedProceeds != cashLines.Sum(static line => line.Debit))
            throw new LedgerValidationException("Disposal sale price does not reconcile to retained cash proceeds.");
        return quotedProceeds;
    }
}
