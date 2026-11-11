using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Endpoints;

// Running-balance projection for the ledger period account drill-down. Kept in its own partial
// file because LedgerEndpoints.cs is at its file-size cap.
public static partial class LedgerEndpoints
{
    /// <summary>
    /// Posting history for one account in store order, with a running balance in the account's
    /// normal-balance sign. The store's account filter is re-applied here so the result is correct
    /// for any <see cref="ILedgerJournalStore"/> implementation.
    /// </summary>
    internal static LedgerAccountRunningBalanceDto BuildAccountRunningBalance(
        IReadOnlyList<LedgerJournalEntryRecord> entries,
        Guid periodId,
        Guid ledgerBookId,
        string accountName,
        string? symbol,
        string? financialAccountId)
    {
        // An omitted filter (null) matches any value; a filter sent empty selects the lines that
        // have no symbol or no financial account. Without that distinction, picking the unscoped
        // row of an account would also return its account-scoped postings.
        var symbolFilter = symbol?.Trim();
        var financialAccountFilter = financialAccountId?.Trim();
        var lines = entries
            .OrderBy(static entry => entry.Entry.Timestamp)
            .ThenBy(static entry => entry.GlobalSequence)
            .SelectMany(static entry => entry.Entry.Lines.Select(line => (Record: entry, Line: line)))
            .Where(item => string.Equals(item.Line.Account.Name, accountName, StringComparison.OrdinalIgnoreCase))
            .Where(item => MatchesOptionalAccountScope(item.Line.Account.Symbol, symbolFilter))
            .Where(item => MatchesOptionalAccountScope(item.Line.Account.FinancialAccountId, financialAccountFilter))
            .ToArray();

        LedgerAccountType? accountType = lines.Length > 0 ? lines[0].Line.Account.AccountType : null;
        var debitNormal = accountType is LedgerAccountType.Asset or LedgerAccountType.Expense;
        var balance = 0m;
        var points = new List<LedgerAccountRunningBalancePointDto>(lines.Length);
        foreach (var (record, line) in lines)
        {
            balance += debitNormal ? line.Debit - line.Credit : line.Credit - line.Debit;
            points.Add(new LedgerAccountRunningBalancePointDto(
                record.Entry.JournalEntryId,
                line.EntryId,
                record.GlobalSequence,
                record.Entry.Timestamp,
                string.IsNullOrWhiteSpace(line.Description) ? record.Entry.Description : line.Description,
                line.Debit,
                line.Credit,
                balance));
        }

        return new LedgerAccountRunningBalanceDto(
            periodId,
            ledgerBookId,
            accountName,
            accountType?.ToString(),
            string.IsNullOrEmpty(symbolFilter) ? null : symbolFilter,
            string.IsNullOrEmpty(financialAccountFilter) ? null : financialAccountFilter,
            lines.Sum(static item => item.Line.Debit),
            lines.Sum(static item => item.Line.Credit),
            balance,
            points);
    }

    private static bool MatchesOptionalAccountScope(string? actual, string? filter)
        => filter is null
           || (filter.Length == 0
               ? string.IsNullOrWhiteSpace(actual)
               : string.Equals(actual?.Trim(), filter, StringComparison.OrdinalIgnoreCase));
}
