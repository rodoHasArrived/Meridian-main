using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Npgsql;
using static Meridian.Storage.Ledger.LedgerRetainedValueComparison;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private static bool IsPortfolioOpeningCorrection(LedgerJournalEntryWrite write)
        => write.Entry.Metadata.IdempotencyKey?.StartsWith("portfolio-opening-correction:", StringComparison.Ordinal) == true;

    /// <summary>
    /// Verifies the immutable opening ancestry and complete native predecessor inverse inside
    /// append. All journals belong to the already locked target period, so close cannot cross
    /// this proof and commit. A correction prefix alone confers no source authority.
    /// </summary>
    private async Task ValidatePortfolioOpeningCorrectionAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, LedgerJournalEntryWrite write, LedgerAccountingPeriod period, CancellationToken ct)
    {
        if (!IsPortfolioOpeningCorrection(write))
        {
            if (write.SourceJournalEntryId is { } sourceId && period.LedgerBookId is { } sourceBook)
            {
                var sources = await QueryAsync(connection, transaction,
                    new LedgerJournalEntryQuery(LedgerBookId: sourceBook, JournalEntryId: sourceId), null, ct).ConfigureAwait(false);
                if (sources.Count == 1 && (sources[0].Entry.Metadata.IdempotencyKey?.StartsWith(PortfolioOpeningKeyPrefix, StringComparison.Ordinal) == true
                    || sources[0].Entry.Metadata.IdempotencyKey?.StartsWith("portfolio-opening-correction:", StringComparison.Ordinal) == true)
                    && !(sources[0].PeriodId != period.PeriodId && sources[0].AggregateId == write.AggregateId
                        && IsPriorPeriodAdjustment(write)))
                    throw new LedgerValidationException("A claimed imported opening source requires its canonical opening correction or verified later-period adjustment lane.");
            }
            return;
        }
        var command = write.PostingCommand;
        if (period.LedgerBookId is not { } bookId || period.Status != "Open"
            || write.LedgerBookId != bookId || command is null || command.LedgerBookId != bookId
            || write.PostingKind != LedgerPostingKindDto.Adjustment
            || command.Intent is not (AccountingPostingIntentDto.Reversal or AccountingPostingIntentDto.Rebook)
            || command.ApprovalState != AccountingPostingApprovalStateDto.Approved
            || write.AdjustmentApproval?.Status != LedgerAdjustmentApprovalStatusDto.Approved
            || write.SourceJournalEntryId is not { } predecessorId || predecessorId == Guid.Empty
            || command.SourceJournalEntryId != predecessorId
            || command.IdempotencyKey != $"portfolio-opening-correction:{predecessorId:D}:{write.Entry.JournalEntryId:D}"
            || write.Entry.Metadata.IdempotencyKey != command.IdempotencyKey)
            throw new LedgerValidationException("Opening correction requires approved typed source, open book period and unique correction identity.");
        var book = await LoadLedgerBookAsync(connection, transaction, bookId, ct).ConfigureAwait(false)
            ?? throw new LedgerValidationException("Opening correction ledger book is unavailable.");
        var effective = write.Entry.Metadata.EffectiveDate;
        if (book.AccountingBasis != write.AccountingBasis || effective is null || effective != command.EffectiveDate
            || effective != DateOnly.FromDateTime(write.Entry.Timestamp.UtcDateTime))
            throw new LedgerValidationException("Opening correction requires the original effective date and authoritative accounting basis.");
        var visited = new HashSet<Guid> { write.Entry.JournalEntryId };
        LedgerJournalEntryRecord? predecessor = null;
        var currentId = predecessorId;
        while (true)
        {
            if (!visited.Add(currentId))
                throw new LedgerValidationException("Opening correction source lineage contains a cycle.");
            var rows = await QueryAsync(connection, transaction,
                new LedgerJournalEntryQuery(LedgerBookId: bookId, JournalEntryId: currentId), null, ct).ConfigureAwait(false);
            if (rows.Count != 1 || rows[0].PeriodId != period.PeriodId || rows[0].AccountingBasis != write.AccountingBasis
                || rows[0].AggregateId != write.AggregateId
                || (rows[0].Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(rows[0].Entry.Timestamp.UtcDateTime)) != effective
                || rows[0].Entry.Lines.Any(line => !IsNativeOpeningCorrectionLine(line, book.BaseCurrency)))
                throw new LedgerValidationException("Opening correction requires retained native source ancestry in the same book, period, date and basis.");
            var retained = rows[0];
            predecessor ??= retained;
            var key = retained.Entry.Metadata.IdempotencyKey;
            if (key?.StartsWith(PortfolioOpeningKeyPrefix, StringComparison.Ordinal) == true)
            {
                var parts = key.Split(':');
                if (retained.PostingKind != LedgerPostingKindDto.Originating || retained.SourceJournalEntryId is not null
                    || parts.Length != 3 || !Guid.TryParseExact(parts[2], "D", out var accountId) || accountId == Guid.Empty
                    || key != $"portfolio-opening:{bookId:D}:{accountId:D}"
                    || retained.Entry.Lines.Any(line => line.Account.FinancialAccountId != accountId.ToString("D")))
                    throw new LedgerValidationException("Opening correction ancestry must end at the exact originating imported book/account source.");
                if (write.Entry.Lines.Any(line => line.Account.FinancialAccountId != accountId.ToString("D")))
                    throw new LedgerValidationException("Opening correction lines must retain the imported financial-account scope.");
                break;
            }
            if (retained.PostingKind != LedgerPostingKindDto.Adjustment || retained.SourceJournalEntryId is not { } parentId
                || key != $"portfolio-opening-correction:{parentId:D}:{retained.Entry.JournalEntryId:D}")
                throw new LedgerValidationException("Opening correction predecessor must retain its exact rooted source identity.");
            currentId = parentId;
        }
        if (write.Entry.Lines.Any(line => !IsNativeOpeningCorrectionLine(line, book.BaseCurrency)))
            throw new LedgerValidationException("Opening correction posting lines must preserve native book-currency evidence.");
        var remaining = write.Entry.Lines.ToList();
        foreach (var source in predecessor!.Entry.Lines)
        {
            var index = remaining.FindIndex(candidate => IsExactPriorPeriodInverse(source, candidate));
            if (index < 0)
                throw new LedgerValidationException("Opening correction must retain every immutable predecessor inverse leg, account, currency and dimension.");
            remaining.RemoveAt(index);
        }
        if ((command.Intent == AccountingPostingIntentDto.Reversal && remaining.Count != 0)
            || remaining.Sum(line => line.Debit) != remaining.Sum(line => line.Credit))
            throw new LedgerValidationException("Opening reversal must be exact; an opening rebook replacement must balance independently.");
    }

    private static bool IsNativeOpeningCorrectionLine(LedgerEntry line, string baseCurrency)
        => line.Currency is { } currency && currency.IsFunctionalCurrency
           && currency.FunctionalCurrency == baseCurrency && IsIdentityTranslation(currency, line.Debit, line.Credit);
}
