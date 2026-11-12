using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>
/// Proves posted income closes from their complete ordered period prefix and the canonical close
/// projector. Labels alone grant no exemption. Legacy closes without retained sequence, period,
/// dimensions or canonical projection identity remain unproved and the coupon guard rejects them.
/// </summary>
internal static class BondCouponRecognitionClosingHistory
{
    internal static async Task<IReadOnlyList<LedgerJournalEntryRecord>> ResolveAsync(ILedgerJournalStore ledger,
        AccountCashForecastScopeDto scope, LedgerTaxLotRecord physical, BondCouponBookAccountsDto paths,
        IReadOnlyList<BondCouponRecognitionAccountDto> accounts, IReadOnlyList<LedgerJournalEntryRecord> history,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(scope.BookId, out var bookId) || physical.LedgerBookId != bookId
            || physical.Account.FinancialAccountId != scope.AccountId)
            throw new InvalidOperationException("Coupon closing history requires exact lot, account and book authority.");
        var candidates = history.Where(row => row.PostingKind == LedgerPostingKindDto.ClosingEntry).ToArray();
        if (candidates.Length == 0) return [];
        var book = await ledger.GetLedgerBookAsync(bookId, ct).ConfigureAwait(false);
        if (book is null || book.LedgerBookId != bookId || book.BaseCurrency != scope.BaseCurrency) return [];
        var protectedAccounts = accounts.Where(row => row.Path == paths.Receivable || row.Path == paths.Payable || row.Path == paths.Cash).ToArray();
        var verified = new Dictionary<Guid, LedgerJournalEntryRecord>();
        foreach (var row in candidates.OrderBy(row => row.GlobalSequence))
        {
            ct.ThrowIfCancellationRequested();
            var period = await ledger.GetPeriodAsync(row.PeriodId, ct).ConfigureAwait(false);
            if (period is null || period.LedgerBookId != bookId
                || row.AccountingBasis != book.AccountingBasis
                || row.AggregateId != bookId || row.GlobalSequence <= 0 || row.SourceJournalEntryId is not null
                || row.Entry.Metadata.EffectiveDate != period.EndDate || !SafeLines(row.Entry, scope, protectedAccounts)) continue;
            var samePeriod = history.Where(prior => prior.PeriodId == row.PeriodId).ToArray();
            if (samePeriod.Any(prior => prior.GlobalSequence <= 0)
                || samePeriod.Select(prior => prior.GlobalSequence).Distinct().Count() != samePeriod.Length) continue;
            var prefix = samePeriod.Where(prior => prior.GlobalSequence < row.GlobalSequence).ToArray();
            // The shared period summary enriches its explicit line dimensions with the book's fund.
            // Metadata-only legacy dimensions cannot establish the exact original input here.
            if (prefix.SelectMany(prior => prior.Entry.Lines).Any(line =>
                line.Account.AccountType is LedgerAccountType.Revenue or LedgerAccountType.Expense && line.Dimensions is null)) continue;
            var balances = prefix.SelectMany(prior => prior.Entry.Lines)
                .Where(line => line.Account.AccountType is LedgerAccountType.Revenue or LedgerAccountType.Expense)
                .GroupBy(line => JsonSerializer.Serialize(new { line.Account, Dimensions = Dimensions(line.Dimensions, book.FundProfileId) }))
                .Select(group => new PeriodCloseAccountBalance(group.First().Account,
                    Meridian.Ledger.Ledger.CalculateNetBalance(group.First().Account, group.Sum(line => line.Debit), group.Sum(line => line.Credit)),
                    Dimensions(group.First().Dimensions, book.FundProfileId))).ToArray();
            var at = new DateTimeOffset(period.EndDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            var projection = PeriodCloseProjector.Project(new PeriodCloseInput(period.PeriodId.ToString("D"), at, balances, "retained-close-proof"));
            var draft = PeriodCloseDraftBuilder.BuildDraft(projection);
            if (draft is null || row.Entry.Metadata.IdempotencyKey != draft.Metadata.IdempotencyKey
                || !SameLines(row.Entry.Lines.Select(line => (line.Account, line.Debit, line.Credit, line.Dimensions)), projection.JournalLines)) continue;
            verified.Add(row.Entry.JournalEntryId, row);
        }
        // A governed reopen does not clear coupon balances either. Only the exact approved inverse
        // of a close independently proved above can share its exemption.
        foreach (var row in history.Where(row => row.PostingKind == LedgerPostingKindDto.Adjustment))
        {
            if (row.SourceJournalEntryId is not { } sourceId || !verified.TryGetValue(sourceId, out var source)
                || source.PostingKind != LedgerPostingKindDto.ClosingEntry || row.PeriodId != source.PeriodId
                || row.AggregateId != bookId || row.GlobalSequence <= source.GlobalSequence
                || row.AccountingBasis != source.AccountingBasis
                || history.Count(other => other.PostingKind == LedgerPostingKindDto.Adjustment && other.SourceJournalEntryId == sourceId) != 1
                || row.AdjustmentApproval is not { Status: LedgerAdjustmentApprovalStatusDto.Approved } approval
                || string.IsNullOrWhiteSpace(approval.ApprovalId) || string.IsNullOrWhiteSpace(approval.ApprovedBy)
                || approval.ApprovedAt == default || approval.ApprovedAt.Offset != TimeSpan.Zero
                || approval.ApprovedAt > row.CreatedAt || string.IsNullOrWhiteSpace(approval.ReasonCode)
                || !SafeLines(row.Entry, scope, protectedAccounts)
                || !InverseCurrencies(row.Entry, source.Entry)
                || !SameLines(row.Entry.Lines.Select(line => (line.Account, line.Credit, line.Debit, line.Dimensions)),
                    source.Entry.Lines.Select(line => (line.Account, line.Debit, line.Credit, line.Dimensions)))) continue;
            verified.Add(row.Entry.JournalEntryId, row);
        }
        return verified.Values.ToArray();
    }

    private static bool SafeLines(JournalEntry entry, AccountCashForecastScopeDto scope,
        IReadOnlyList<BondCouponRecognitionAccountDto> protectedAccounts)
        => entry.IsBalanced && entry.Lines.Count > 0 && entry.Lines.All(line =>
            line.Account.AccountType is LedgerAccountType.Revenue or LedgerAccountType.Expense
                || line.Account == (string.IsNullOrWhiteSpace(line.Account.FinancialAccountId)
                    ? LedgerAccounts.RetainedEarnings : LedgerAccounts.RetainedEarningsFor(line.Account.FinancialAccountId)))
            && entry.Lines.All(line => !protectedAccounts.Any(account => account.LedgerName == line.Account.Name
                && account.AccountType == line.Account.AccountType.ToString())
                && (line.Currency is null || line.Currency.TransactionCurrency == scope.BaseCurrency
                    && line.Currency.FunctionalCurrency == scope.BaseCurrency && line.Currency.FxRateToFunctional == 1m
                    && line.Currency.TransactionDebit == line.Debit && line.Currency.TransactionCredit == line.Credit));

    private static bool InverseCurrencies(JournalEntry inverse, JournalEntry source)
        => inverse.Lines.All(line => source.Lines.Count(prior => prior.Account == line.Account
            && prior.Credit == line.Debit && prior.Debit == line.Credit
            && JsonElement.DeepEquals(JsonSerializer.SerializeToElement(prior.Dimensions), JsonSerializer.SerializeToElement(line.Dimensions))
            && (line.Currency is null && prior.Currency is null || line.Currency is { } currency && prior.Currency is { } original
                && currency.TransactionCurrency == original.TransactionCurrency && currency.FunctionalCurrency == original.FunctionalCurrency
                && currency.FxRateToFunctional == original.FxRateToFunctional
                && currency.TransactionDebit == original.TransactionCredit && currency.TransactionCredit == original.TransactionDebit)) == 1);

    private static LedgerLineDimensionSet? Dimensions(LedgerLineDimensionSet? value, string fund)
        => value is null ? new(FundId: fund) : value with
        {
            FundId = string.IsNullOrWhiteSpace(value.FundId) ? fund : value.FundId,
            ExternalGlDimensions = value.ExternalGlDimensions.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        };

    private static bool SameLines(IEnumerable<(LedgerAccount Account, decimal Debit, decimal Credit, LedgerLineDimensionSet? Dimensions)> left,
        IEnumerable<(LedgerAccount Account, decimal Debit, decimal Credit, LedgerLineDimensionSet? Dimensions)> right)
    {
        static string Key((LedgerAccount Account, decimal Debit, decimal Credit, LedgerLineDimensionSet? Dimensions) line)
            => JsonSerializer.Serialize(new { line.Account, line.Debit, line.Credit,
                Dimensions = line.Dimensions is null ? null : line.Dimensions with
                { ExternalGlDimensions = line.Dimensions.ExternalGlDimensions.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) } });
        return left.Select(Key).Order(StringComparer.Ordinal).SequenceEqual(right.Select(Key).Order(StringComparer.Ordinal));
    }
}
