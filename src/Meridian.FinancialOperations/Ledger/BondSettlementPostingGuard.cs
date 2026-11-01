using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>
/// Proves purchased interest is a separate receivable and observed consideration is the cash credit. For a
/// foreign-currency settlement every component line carries the bond's currency as transaction currency,
/// the book's functional currency, the retained settlement-date FX and the once-translated functional
/// amount; no FX result arises at settlement, and later remeasurement of the foreign cash balance belongs
/// to the translation lane, not to this receipt.
/// </summary>
internal static class BondSettlementPostingGuard
{
    internal static async Task RequireVerifiedAsync(AssetAcquisitionLotDto acquisition,
        IBondSettlementConsiderationService? service, CancellationToken ct)
    {
        if (acquisition.SettlementConsideration is not { } receipt)
            return;
        if (service is null)
            throw new InvalidOperationException("Bond settlement verification is not configured; a caller-authored analytics snapshot cannot authorize acquisition.");
        var blockers = await service.VerifyAsync(receipt, ct).ConfigureAwait(false);
        if (blockers.Count > 0)
            throw new InvalidOperationException($"Bond settlement analytics verification failed: {string.Join(" ", blockers.Select(blocker => blocker.Reason))}");
    }

    internal static void RequireScope(AssetAcquisitionLotDto acquisition, Guid securityId, long securityVersion, string currency)
    {
        if (acquisition.SettlementConsideration is not { } receipt)
            return;
        if (receipt.SecurityId != securityId || receipt.Currency != currency
            || !receipt.Inputs.Any(input => input.Kind == "SecurityTerms" && input.Version == securityVersion
                && input.Key == securityId.ToString("D")))
            throw new ArgumentException("Bond settlement security, terms version and currency must match the retained acquisition authority.");
    }

    internal static void RequireJournal(AssetAcquisitionLotDto acquisition,
        PostingRuleJournalCandidateResultDto retainedCandidate, JournalEntry entry)
    {
        if (acquisition.SettlementConsideration is not { } receipt)
            return;
        if (BondSettlementConsideration.ValidateAcquisition(acquisition).Count > 0)
            throw new InvalidOperationException("Bond settlement receipt no longer matches the retained acquisition.");
        if (new[] { receipt.CashAccountId, receipt.AccruedInterestAccountId, receipt.FeesAccountId }
            .Any(account => account == acquisition.AccountId))
            throw new InvalidOperationException("Bond clean lot cost, cash, purchased interest and fees require separate accounts.");
        RequireLine(receipt.CashAccountId, receipt.DirtyConsideration, AccountingTemplateLineSideDto.Credit, LedgerAccountType.Asset);
        RequireLine(receipt.AccruedInterestAccountId, receipt.PurchasedAccruedInterest, AccountingTemplateLineSideDto.Debit, LedgerAccountType.Asset);
        if (receipt.Fees != 0m)
            RequireLine(receipt.FeesAccountId!, receipt.Fees, AccountingTemplateLineSideDto.Debit, LedgerAccountType.Expense);

        void RequireLine(string accountId, decimal amount, AccountingTemplateLineSideDto side, LedgerAccountType accountType)
        {
            var matches = retainedCandidate.GeneratedPostingLines.Select((line, index) => (line, index))
                .Where(item => item.line.AccountPath == accountId).ToArray();
            if (amount == 0m && matches.Length == 0)
                return;
            if (matches.Length != 1 || matches[0].index >= entry.Lines.Count)
                throw new InvalidOperationException($"Bond settlement account '{accountId}' must identify exactly one retained journal line.");
            var expected = matches[0];
            var actual = entry.Lines[expected.index];
            // Each component is translated once at the retained settlement-date rate and rounded to the
            // functional currency's minor units; a same-currency settlement is the FX = 1 special case.
            var functional = receipt.ToFunctional(amount);
            if (expected.line.Side != side || expected.line.Amount != amount || expected.line.Currency != receipt.Currency
                || actual.Account.AccountType != accountType
                || actual.Currency is not { } transaction || transaction.TransactionCurrency != receipt.Currency
                || transaction.FunctionalCurrency != receipt.EffectiveFunctionalCurrency
                || transaction.FxRateToFunctional != receipt.EffectiveFxRateToFunctional
                || transaction.TransactionDebit != (side == AccountingTemplateLineSideDto.Debit ? amount : 0m)
                || transaction.TransactionCredit != (side == AccountingTemplateLineSideDto.Credit ? amount : 0m)
                || actual.Debit != (side == AccountingTemplateLineSideDto.Debit ? functional : 0m)
                || actual.Credit != (side == AccountingTemplateLineSideDto.Credit ? functional : 0m)
                || entry.Lines.Count(line => line.Account == actual.Account) != 1)
                throw new InvalidOperationException($"Bond settlement account '{accountId}' does not match its reviewed consideration component.");
        }
    }
}
