using Meridian.Contracts.Ledger;
using Meridian.Execution.Events;
using Meridian.Ledger;

namespace Meridian.Execution.Services;

public sealed partial class PaperTradingPortfolio
{
    /// <summary>
    /// Accrues interest on any outstanding margin loans in all margin accounts.
    /// </summary>
    /// <remarks>
    /// Call this once per interest period (e.g. daily).  The charge is paid from the
    /// account's <c>Cash</c> balance and posted as margin interest expense; the loan
    /// principal (<c>MarginBorrowed</c>) is unchanged. Equity therefore falls by exactly
    /// the interest, once — growing the loan as well would charge it a second time, both
    /// in <see cref="PortfolioValue"/> and again when the sale repays the inflated loan.
    /// This matches the backtest engine, which also settles margin interest from cash.
    /// </remarks>
    /// <param name="annualRate">Annual interest rate expressed as a decimal (e.g. 0.065 = 6.5 %).</param>
    /// <param name="days">Number of days to accrue interest for. Defaults to 1 (daily).</param>
    /// <param name="asOf">Timestamp for the ledger posting. Defaults to the current UTC time.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="annualRate"/> is negative or <paramref name="days"/> is less than 1.
    /// </exception>
    public void AccrueMarginInterest(decimal annualRate, int days = 1, DateTimeOffset? asOf = null)
    {
        if (annualRate < 0m)
            throw new ArgumentOutOfRangeException(nameof(annualRate), "Annual rate must be non-negative.");
        if (days < 1)
            throw new ArgumentOutOfRangeException(nameof(days), "Days must be at least 1.");

        if (annualRate == 0m)
            return;

        var dailyRate = annualRate / 365m;
        var periodRate = dailyRate * days;

        var ts = asOf ?? DateTimeOffset.UtcNow;

        lock (_lock)
        {
            foreach (var account in _accounts.Values)
            {
                if (account.MarginModel is null)
                    continue; // cash account

                var accountInterest = 0m;
                foreach (var pos in account.Positions.Values)
                {
                    if (pos.MarginBorrowed <= 0m)
                        continue;

                    accountInterest += pos.MarginBorrowed * periodRate;
                }

                if (accountInterest == 0m)
                    continue;

                // Paid from cash once; the loan principal stays where it is.
                account.Cash -= accountInterest;

                _ledger?.PostLines(ts, $"Margin interest — {account.AccountId}",
                [
                    (_chartOfAccounts.Resolve(LedgerAccountRole.MarginInterestExpense, LedgerScope(account)), accountInterest, 0m),
                    (_chartOfAccounts.Resolve(LedgerAccountRole.Cash, LedgerScope(account)), 0m, accountInterest),
                ]);
            }
        }
    }

    /// <summary>Applies a retained cash-paid assessment once. The durable session owner
    /// persists and posts the source before calling this rebuildable projection.</summary>
    public bool ApplyFinancingCharge(PaperFinancingCharge charge)
    {
        charge.Validate();
        lock (_lock)
        {
            if (_financingCharges.TryGetValue(charge.SourceEventId, out var retained))
                return retained == charge ? false : throw new InvalidDataException("Financing identity has conflicting content.");
            if (!_accounts.TryGetValue(charge.AccountId, out var account))
                throw new InvalidDataException($"Financing account '{charge.AccountId}' is not present in the paper portfolio.");
            var nextCash = checked(account.Cash - charge.Amount);
            var scopedId = LedgerScope(account);
            var journalId = PaperFinancingCharge.Identity("projection-journal", charge.SourceEventId.ToString("D"));
            var description = $"Cash-paid margin interest: {charge.SourceReference}";
            var expense = scopedId is null ? LedgerAccounts.MarginInterestExpense : LedgerAccounts.MarginInterestExpenseFor(scopedId);
            _ledger?.Post(new JournalEntry(journalId, charge.OccurredAt, description,
            [
                new(PaperFinancingCharge.Identity("debit", journalId.ToString("D")), journalId,
                    charge.OccurredAt, expense, charge.Amount, 0m, description),
                new(PaperFinancingCharge.Identity("credit", journalId.ToString("D")), journalId,
                    charge.OccurredAt, LedgerAccounts.CashFor(scopedId), 0m, charge.Amount, description)
            ], new JournalEntryMetadata(ActivityType: "margin-interest", FinancialAccountId: scopedId,
                EffectiveDate: charge.AccrualDate, IdempotencyKey: $"paper-financing:{charge.SourceEventId:D}")));
            account.Cash = nextCash;
            _financingCharges.Add(charge.SourceEventId, charge);
            return true;
        }
    }

}
