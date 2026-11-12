using System.Globalization;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Npgsql;
using static Meridian.Storage.Ledger.LedgerRetainedValueComparison;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private static readonly IReadOnlyDictionary<string, string> EmptyPriorPeriodDimensions =
        new Dictionary<string, string>();

    private static bool IsMixedPriorPeriodAdjustment(LedgerJournalEntryWrite write)
        => write.Entry.Metadata.Tags?.GetValueOrDefault("securityMaster.lineageMode") == "per-line"
           && write.Entry.Metadata.IdempotencyKey?.StartsWith("prior-period|", StringComparison.Ordinal) == true;

    /// <summary>
    /// Establishes mixed-security correction authority from immutable books, never a caller's
    /// prefix alone. The root period stays locked until append commits, before the audit fence.
    /// A rebook contains a complete inverse and a separately balanced replacement.
    /// </summary>
    private async Task<bool> VerifyMixedPriorPeriodAdjustmentAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, LedgerJournalEntryWrite write,
        LedgerAccountingPeriod target, CancellationToken ct)
    {
        var metadata = write.Entry.Metadata;
        if (!IsMixedPriorPeriodAdjustment(write))
            return false;

        var tags = metadata.Tags!;
        var command = write.PostingCommand;
        if (target.LedgerBookId is not { } bookId || target.Status != "Open"
            || write.LedgerBookId != bookId || command is null || command.LedgerBookId != bookId
            || write.PostingKind != LedgerPostingKindDto.Adjustment
            || command.ApprovalState != AccountingPostingApprovalStateDto.Approved
            || write.AdjustmentApproval?.Status != LedgerAdjustmentApprovalStatusDto.Approved
            || command.Intent is not (AccountingPostingIntentDto.Reversal or AccountingPostingIntentDto.Rebook)
            || !Guid.TryParse(tags.GetValueOrDefault("prior-period.sourceJournalEntryId"), out var rootId) || rootId == Guid.Empty
            || !Guid.TryParse(tags.GetValueOrDefault("prior-period.sourcePeriodId"), out var rootPeriodId) || rootPeriodId == Guid.Empty
            || !DateOnly.TryParseExact(tags.GetValueOrDefault("prior-period.originalEffectiveDate"), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var originalDate)
            || string.IsNullOrWhiteSpace(tags.GetValueOrDefault("prior-period.intentId"))
            || string.IsNullOrWhiteSpace(tags.GetValueOrDefault("prior-period.reason")))
            throw new LedgerValidationException("Mixed prior-period posting requires approved typed source, book, date and correction authority.");

        // Append and hard-close both lock their target first. A shared root fence excludes
        // reopen while remaining compatible with a later hard-close's ordered history fences;
        // taking an exclusive earlier lock here would introduce a cross-period deadlock.
        await using var rootFence = connection.CreateCommand();
        rootFence.Transaction = transaction;
        rootFence.CommandText = $"select period_id from {Qualified("accounting_periods")} where period_id = @source and ledger_book_id = @book for share;";
        rootFence.Parameters.AddWithValue("source", rootPeriodId);
        rootFence.Parameters.AddWithValue("book", bookId);
        _ = await rootFence.ExecuteScalarAsync(ct).ConfigureAwait(false);
        var rootPeriod = await LoadPeriodAsync(connection, transaction, rootPeriodId, forUpdate: false,
            callerTenantId: _tenantScope.IsFailClosed ? ResolveCallerTenant() : null, ct: ct).ConfigureAwait(false);
        if (rootPeriod is null || rootPeriod.LedgerBookId != bookId || rootPeriod.Status != "HardClosed"
            || target.StartDate <= rootPeriod.EndDate)
            throw new LedgerValidationException("Mixed prior-period posting requires its retained hard-closed source and a later open period in the same book.");
        var roots = await QueryAsync(connection, transaction,
            new LedgerJournalEntryQuery(LedgerBookId: bookId, JournalEntryId: rootId), null, ct).ConfigureAwait(false);
        if (roots.Count != 1 || roots[0].PeriodId != rootPeriodId || roots[0].AccountingBasis != write.AccountingBasis
            || (roots[0].Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(roots[0].Entry.Timestamp.UtcDateTime)) != originalDate)
            throw new LedgerValidationException("Mixed prior-period posting root does not match the retained book, basis, period and original date.");

        var intent = tags["prior-period.intentId"];
        var canonicalKey = $"prior-period|book={bookId:N}|source={rootId:N}|intent={Uri.EscapeDataString(intent)}";
        var predecessor = roots[0];
        var correctionIdText = tags.GetValueOrDefault("prior-period.correctionJournalEntryId");
        if (correctionIdText is not null)
        {
            if (!Guid.TryParse(correctionIdText, out var correctionId) || correctionId == Guid.Empty
                || tags.GetValueOrDefault("prior-period.correctionAction") !=
                    (command.Intent == AccountingPostingIntentDto.Reversal ? "Reverse" : "Rebook")
                || string.IsNullOrWhiteSpace(tags.GetValueOrDefault("prior-period.correctionReason")))
                throw new LedgerValidationException("Mixed prior-period correction requires the exact retained predecessor and correction action.");
            var predecessors = await QueryAsync(connection, transaction,
                new LedgerJournalEntryQuery(LedgerBookId: bookId, JournalEntryId: correctionId), null, ct).ConfigureAwait(false);
            if (predecessors.Count != 1 || predecessors[0].PeriodId != target.PeriodId
                || predecessors[0].AccountingBasis != write.AccountingBasis
                || predecessors[0].PostingKind != LedgerPostingKindDto.Adjustment
                || new[] { "prior-period.sourceJournalEntryId", "prior-period.sourcePeriodId",
                    "prior-period.originalEffectiveDate", "prior-period.intentId" }.Any(key =>
                    predecessors[0].Entry.Metadata.Tags?.GetValueOrDefault(key) != tags[key]))
                throw new LedgerValidationException("Mixed prior-period correction predecessor does not retain the same closed root, basis and target period.");
            predecessor = predecessors[0];
            canonicalKey += $"|correction={write.Entry.JournalEntryId:N}";
        }
        else if (command.Intent != AccountingPostingIntentDto.Reversal)
            throw new LedgerValidationException("An initial mixed prior-period posting must reverse its retained closed source.");

        if (metadata.IdempotencyKey != canonicalKey || command.IdempotencyKey != canonicalKey
            || write.SourceJournalEntryId != predecessor.Entry.JournalEntryId
            || command.SourceJournalEntryId != predecessor.Entry.JournalEntryId)
            throw new LedgerValidationException("Mixed prior-period posting intent and typed source must bind the exact retained predecessor.");

        var book = await LoadLedgerBookAsync(connection, transaction, bookId, ct).ConfigureAwait(false)
            ?? throw new LedgerValidationException("The authoritative mixed prior-period ledger book is unavailable.");
        if (book.AccountingBasis != write.AccountingBasis || !predecessor.Entry.IsBalanced
            || predecessor.Entry.Lines.Concat(write.Entry.Lines).Any(line => line.Currency is not { } currency
                || !currency.IsFunctionalCurrency || currency.FunctionalCurrency != book.BaseCurrency
                || !IsIdentityTranslation(currency, line.Debit, line.Credit)))
            throw new LedgerValidationException("Mixed prior-period correction requires balanced native book-currency predecessor and posting lines in the same accounting basis.");

        var remaining = write.Entry.Lines.ToList();
        foreach (var source in predecessor.Entry.Lines)
        {
            var index = remaining.FindIndex(candidate => IsExactPriorPeriodInverse(source, candidate));
            if (index < 0)
                throw new LedgerValidationException("Mixed prior-period posting must retain every immutable predecessor inverse leg, currency and dimension.");
            remaining.RemoveAt(index);
        }
        if ((command.Intent == AccountingPostingIntentDto.Reversal && remaining.Count != 0)
            || remaining.Sum(line => line.Debit) != remaining.Sum(line => line.Credit))
            throw new LedgerValidationException("Mixed prior-period reversal must be exact; a rebook replacement must balance independently.");
        return true;
    }

    private static bool IsExactPriorPeriodInverse(LedgerEntry source, LedgerEntry candidate)
        => source.Account == candidate.Account
           && AmountsMatch(source.Debit, candidate.Credit) && AmountsMatch(source.Credit, candidate.Debit)
           && source.Currency is { } sourceCurrency && candidate.Currency is { } currency
           && CurrencyDetailsMatch(new LedgerEntryCurrency(sourceCurrency.TransactionCurrency,
               sourceCurrency.FunctionalCurrency, sourceCurrency.TransactionCredit,
               sourceCurrency.TransactionDebit, sourceCurrency.FxRateToFunctional), currency)
           && PriorPeriodDimensionsMatch(source.Dimensions, candidate.Dimensions);

    private static bool PriorPeriodDimensionsMatch(LedgerLineDimensionSet? source, LedgerLineDimensionSet? candidate)
    {
        if (source is null || candidate is null)
            return source is null && candidate is null;
        return (source with { ExternalGlDimensions = EmptyPriorPeriodDimensions })
                == (candidate with { ExternalGlDimensions = EmptyPriorPeriodDimensions })
            && ExternalDimensionsMatch(source.ExternalGlDimensions, candidate.ExternalGlDimensions);
    }
}
