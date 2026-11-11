using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using static Meridian.Contracts.Text.TextPrimitives;

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
        string? financialAccountId,
        RunningBalanceDimensionScope? dimensionScope = null,
        string? bookFundProfileId = null)
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
            .Where(item => dimensionScope is null
                || string.Equals(
                    TrialBalanceDimensionSignature(ResolveLineDimensions(item.Record, item.Line), bookFundProfileId),
                    TrialBalanceDimensionSignature(dimensionScope.Dimensions, bookFundProfileId),
                    StringComparison.Ordinal))
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

    /// <summary>
    /// The dimension set of one trial-balance row, as sent back by a client. A row with no
    /// dimensions is a scope too (<see cref="Dimensions"/> is null), distinct from sending no
    /// scope at all, which selects every dimension set of the account.
    /// </summary>
    internal sealed record RunningBalanceDimensionScope(LedgerDimensionSetDto? Dimensions);

    /// <summary>
    /// Reads the optional <c>dimensions</c> query value: absent means no dimension scope, otherwise
    /// it is the JSON of the selected trial-balance row's dimension set (<c>null</c> or empty for a
    /// row without one). Returns false when the value is present but not a dimension set.
    /// </summary>
    internal static bool TryParseRunningBalanceDimensionScope(
        string? dimensions,
        JsonSerializerOptions jsonOptions,
        out RunningBalanceDimensionScope? scope)
    {
        scope = null;
        if (dimensions is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(dimensions))
        {
            scope = new RunningBalanceDimensionScope(null);
            return true;
        }

        try
        {
            scope = new RunningBalanceDimensionScope(
                JsonSerializer.Deserialize<LedgerDimensionSetDto>(dimensions, jsonOptions));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Same precedence the journal-entry DTOs and the period trial balance use for a line:
    // line dimensions, then line-scoped metadata tags, then the entry's own.
    private static LedgerDimensionSetDto? ResolveLineDimensions(LedgerJournalEntryRecord record, LedgerEntry line)
        => BuildDimensions(line.Dimensions)
           ?? BuildDimensions(record.Entry.Metadata, line.EntryId)
           ?? BuildDimensions(record.Entry.Metadata);

    // The period trial balance stamps the book's fund onto every row that names none, so a row and
    // the lines behind it are compared after the same defaulting.
    private static string TrialBalanceDimensionSignature(LedgerDimensionSetDto? dimensions, string? bookFundProfileId)
    {
        var canonical = CanonicalizeDimensions(dimensions);
        var fundId = NormalizeOptional(canonical?.FundId) ?? NormalizeOptional(bookFundProfileId);
        return BuildDimensionSignature(
            canonical is null ? new LedgerDimensionSetDto(FundId: fundId) : canonical with { FundId = fundId });
    }

    private static bool MatchesOptionalAccountScope(string? actual, string? filter)
        => filter is null
           || (filter.Length == 0
               ? string.IsNullOrWhiteSpace(actual)
               : string.Equals(actual?.Trim(), filter, StringComparison.OrdinalIgnoreCase));
}
