using System.Globalization;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private const string PortfolioOpeningKeyPrefix = "portfolio-opening:";

    /// <summary>
    /// Rechecks the opening cutoff after acquiring the audit-head write lock, shared by every
    /// journal append. READ COMMITTED sees the preceding writer; SERIALIZABLE aborts a stale
    /// snapshot after a competing head update. The source reservation alone is not authority
    /// to post an opening over account activity accepted while its draft awaited approval.
    /// </summary>
    private async Task ValidateOpeningCutoverAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LedgerJournalEntryWrite write,
        LedgerAccountingPeriod period,
        CancellationToken ct)
    {
        if (period.LedgerBookId is not { } bookId)
            return;

        var key = write.Entry.Metadata.IdempotencyKey;
        var isOpening = key?.StartsWith(PortfolioOpeningKeyPrefix, StringComparison.OrdinalIgnoreCase) == true;
        if (isOpening && (write.PostingKind != LedgerPostingKindDto.Originating || write.SourceJournalEntryId is not null
            || write.PostingCommand is { Intent: not AccountingPostingIntentDto.Originating }))
            throw new LedgerValidationException("An imported opening must be an originating journal without correction source linkage.");
        if (write.PostingKind != LedgerPostingKindDto.Originating)
            return;
        var effectiveDate = write.Entry.Metadata.EffectiveDate
            ?? DateOnly.FromDateTime(write.Entry.Timestamp.UtcDateTime);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("book", bookId);
        command.Parameters.AddWithValue("journal", write.Entry.JournalEntryId);
        command.Parameters.AddWithValue("effective_date", effectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        if (isOpening)
        {
            var parts = key!.Split(':');
            if (parts.Length != 3 || !Guid.TryParseExact(parts[1], "D", out var claimedBook) || claimedBook != bookId
                || !Guid.TryParseExact(parts[2], "D", out var claimedAccount) || claimedAccount == Guid.Empty)
                throw new LedgerValidationException("Opening balances require their exact retained book and financial-account identity.");
            var account = claimedAccount.ToString("D");
            if (write.Entry.Lines.Any(line => !string.Equals(line.Account.FinancialAccountId, account, StringComparison.OrdinalIgnoreCase)))
                throw new LedgerValidationException("Opening-balance lines must all belong to the account named by their source identity.");

            command.Parameters.AddWithValue("account", account);
            command.Parameters.AddWithValue("opening_key", key);
            command.CommandText = $"""
                select exists (
                    select 1 from {Qualified("journal_entries")} je
                    join {Qualified("accounting_periods")} p on p.period_id = je.period_id
                    where p.ledger_book_id = @book and je.journal_entry_id <> @journal
                      and (
                        lower(je.metadata ->> 'idempotencyKey') = lower(@opening_key)
                        or (
                          coalesce(nullif(btrim(je.metadata ->> 'effectiveDate'), ''),
                              to_char(je.occurred_at at time zone 'UTC', 'YYYY-MM-DD')) <= @effective_date
                          and exists (
                            select 1 from {Qualified("journal_legs")} leg
                            where leg.journal_entry_id = je.journal_entry_id
                              and (leg.financial_account_id is null or lower(btrim(leg.financial_account_id)) = lower(@account))))));
                """;
            if ((bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!)
                throw new LedgerValidationException("Opening balances overlap retained account activity at or before the source cutoff; prepare a governed adjustment instead.");
            return;
        }

        var accounts = write.Entry.Lines.Select(line => line.Account.FinancialAccountId)
            .Where(account => !string.IsNullOrWhiteSpace(account))
            .Select(account => account!.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray();
        var hasUnscopedLines = write.Entry.Lines.Any(line => string.IsNullOrWhiteSpace(line.Account.FinancialAccountId));
        command.Parameters.AddWithValue("accounts", accounts);
        command.Parameters.AddWithValue("unscoped", hasUnscopedLines);
        command.Parameters.AddWithValue("prefix", PortfolioOpeningKeyPrefix + "%");
        command.CommandText = $"""
            select exists (
                select 1 from {Qualified("journal_entries")} je
                join {Qualified("accounting_periods")} p on p.period_id = je.period_id
                where p.ledger_book_id = @book and je.journal_entry_id <> @journal
                  and lower(je.metadata ->> 'idempotencyKey') like @prefix
                  and (@unscoped or lower(split_part(je.metadata ->> 'idempotencyKey', ':', 3)) = any(@accounts))
                  and coalesce(nullif(btrim(je.metadata ->> 'effectiveDate'), ''),
                      to_char(je.occurred_at at time zone 'UTC', 'YYYY-MM-DD')) >= @effective_date);
            """;
        if ((bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!)
            throw new LedgerValidationException("Originating activity at or before the retained opening cutoff requires a governed adjustment rather than a backdated posting.");
    }
}
