using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>Source-backed comparisons of posted accounting periods under retained fund ownership.</summary>
public sealed class PostedPeriodComparisonService(
    ILedgerJournalStore journals, ILedgerBookService books, IFundProfileTenancyRegistry tenancy)
{
    public async Task<PostedPeriodComparisonDto> GetAsync(string tenantId, string companyId,
        Guid bookId, Guid currentPeriodId, Guid? baselinePeriodId, DateTimeOffset asOf,
        LedgerDimensionSetDto? dimensions,
        Func<IReadOnlyList<LedgerJournalEntryRecord>, IReadOnlyList<LedgerJournalEntryDto>> project,
        CancellationToken ct = default, AccountingBasisKindDto? accountingBasis = null)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId))
            throw new UnauthorizedAccessException("An exact tenant and company are required.");
        if (asOf > DateTimeOffset.UtcNow.AddSeconds(1))
            throw new ArgumentException("The accounting comparison cutoff cannot be in the future.");
        var book = await books.GetBookAsync(bookId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The ledger book is unavailable.");
        if (book.LedgerBookId != bookId)
            throw new UnauthorizedAccessException("The returned ledger book does not match the requested source identity.");
        var selectedBasis = accountingBasis ?? book.AccountingBasis;
        if (!Enum.IsDefined(selectedBasis))
            throw new ArgumentException("The selected accounting basis is unsupported.");
        var owner = await tenancy.ResolveAsync(book.FundProfileId, ct).ConfigureAwait(false);
        if (owner is null || owner.FundProfileId != book.FundProfileId || owner.TenantId != tenantId || owner.CompanyId != companyId)
            throw new UnauthorizedAccessException("The retained ledger-book ownership does not match this operator scope.");
        var current = await journals.GetPeriodAsync(currentPeriodId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The current ledger period is unavailable.");
        if (current.PeriodId != currentPeriodId || current.LedgerBookId != bookId)
            throw new UnauthorizedAccessException("The returned period does not match the requested ledger source identity.");
        var periods = await journals.ListPeriodsAsync(ledgerBookId: bookId, ct: ct).ConfigureAwait(false);
        LedgerAccountingPeriod? baseline;
        if (baselinePeriodId is { } explicitId)
        {
            baseline = await journals.GetPeriodAsync(explicitId, ct).ConfigureAwait(false);
            if (baseline is not null && (baseline.PeriodId != explicitId || baseline.LedgerBookId != bookId))
                throw new UnauthorizedAccessException("The baseline is outside this ledger book.");
        }
        else
        {
            var candidates = periods.Where(period => period.LedgerBookId == bookId && IsCompleted(period)
                && period.EndDate < current.StartDate).OrderByDescending(period => period.EndDate).ToArray();
            if (candidates.Length > 1 && candidates[0].EndDate == candidates[1].EndDate)
                return Result("Unavailable", "The prior fiscal period is ambiguous. Select an exact completed baseline.", null);
            baseline = candidates.FirstOrDefault();
        }
        if (baseline is null)
            return Result("Unavailable", "No completed prior period is available for this ledger book.", null);
        if (!IsCompleted(baseline) || baseline.EndDate >= current.StartDate)
            return Result("Incompatible", "The baseline must be a completed prior fiscal period in the same ledger book.", null);
        var windows = SelectWindows(current, baseline, asOf);
        if (windows.Reason is not null)
            return Result("Unavailable", windows.Reason, windows.Baseline);
        var currentWindow = windows.Current;
        var baselineWindow = windows.Baseline!;
        // Bracket both populations. A concurrent append cannot yield a silently mixed comparison.
        var first = await ReadPairAsync().ConfigureAwait(false);
        var second = await ReadPairAsync().ConfigureAwait(false);
        if (Fingerprint(first.Current, first.Baseline) != Fingerprint(second.Current, second.Baseline))
        {
            first = second;
            second = await ReadPairAsync().ConfigureAwait(false);
            if (Fingerprint(first.Current, first.Baseline) != Fingerprint(second.Current, second.Baseline))
                return Result("Unavailable", "The posted journal population is changing. Refresh to compare one stable source population.", baselineWindow);
        }
        var afterCurrent = await journals.GetPeriodAsync(current.PeriodId, ct).ConfigureAwait(false);
        var afterBaseline = await journals.GetPeriodAsync(baseline.PeriodId, ct).ConfigureAwait(false);
        var afterBook = await books.GetBookAsync(bookId, ct).ConfigureAwait(false);
        if (afterCurrent?.Version != current.Version || afterBaseline?.Version != baseline.Version || afterBook != book)
            return Result("Unavailable", "The ledger book or period changed during comparison. Refresh before relying on the result.", baselineWindow);
        // Legacy periods may retain multiple independent accounting bases. Filter source records,
        // before dimensions or aggregation, so one basis can never borrow another's postings.
        var currentBasisRecords = second.Current.Where(record => record.AccountingBasis == selectedBasis).ToArray();
        var baselineBasisRecords = second.Baseline.Where(record => record.AccountingBasis == selectedBasis).ToArray();
        if (selectedBasis != book.AccountingBasis && (currentBasisRecords.Length == 0 || baselineBasisRecords.Length == 0))
            return Result("Unavailable", "Retained postings do not establish this non-book accounting basis in both comparison intervals.", baselineWindow);
        var currentProjected = project(currentBasisRecords);
        var baselineProjected = project(baselineBasisRecords);
        if (currentProjected.Concat(baselineProjected).Any(entry => entry.AccountingBasis != selectedBasis))
            throw new InvalidDataException("The projected comparison does not preserve its selected accounting basis.");
        var selectedJournalIds = currentProjected.Concat(baselineProjected).Select(entry => entry.JournalEntryId).ToHashSet();
        var selectedLineIds = currentProjected.Concat(baselineProjected).SelectMany(entry => entry.Lines).Select(line => line.EntryId).ToHashSet();
        var selectedRecords = second.Current.Concat(second.Baseline).Where(record => selectedJournalIds.Contains(record.Entry.JournalEntryId)).ToArray();
        if (selectedRecords.Any(record => record.AccountingBasis != selectedBasis
            || record.AccountingPolicyId != book.AccountingPolicyId || record.AccountingPolicyVersion != book.AccountingPolicyVersion))
            return Result("Incompatible", "The retained postings use different accounting bases or policy versions. Their balances are not combined.", baselineWindow);
        if (selectedRecords.SelectMany(record => record.Entry.Lines).Where(line => selectedLineIds.Contains(line.EntryId))
            .Any(line => line.Currency is { } currency && currency.FunctionalCurrency != book.BaseCurrency))
            return Result("Incompatible", "The retained postings do not share the ledger book's functional currency.", baselineWindow);
        var lines = CompareLines(currentProjected, baselineProjected);
        var currentIds = currentProjected.Select(entry => entry.JournalEntryId).Order().ToArray();
        var priorIds = baselineProjected.Select(entry => entry.JournalEntryId).Order().ToArray();
        var hash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new
        {
            Version = "posted-period-comparison-v1",
            Book = book,
            AsOf = asOf,
            Current = currentWindow,
            Baseline = baselineWindow,
            AccountingBasis = selectedBasis,
            Dimensions = dimensions,
            Lines = lines,
            CurrentJournalIds = currentIds,
            BaselineJournalIds = priorIds
        }));
        return new("Ready", null, DateTimeOffset.UtcNow, asOf, bookId, book.FundProfileId, book.DisplayName,
            selectedBasis, book.BaseCurrency, book.AccountingPolicyId, book.AccountingPolicyVersion,
            currentWindow, baselineWindow, lines, currentIds, priorIds, hash, dimensions);

        PostedPeriodComparisonDto Result(string status, string reason, PostedPeriodComparisonRangeDto? range)
            => new(status, reason, DateTimeOffset.UtcNow, asOf, bookId, book.FundProfileId, book.DisplayName,
                selectedBasis, book.BaseCurrency, book.AccountingPolicyId, book.AccountingPolicyVersion,
                CurrentRange(current, asOf), range, [], [], [], null, dimensions);

        async Task<(IReadOnlyList<LedgerJournalEntryRecord> Current, IReadOnlyList<LedgerJournalEntryRecord> Baseline)> ReadPairAsync()
            => (await ReadWindowAsync(currentWindow).ConfigureAwait(false), await ReadWindowAsync(baselineWindow).ConfigureAwait(false));

        async Task<IReadOnlyList<LedgerJournalEntryRecord>> ReadWindowAsync(PostedPeriodComparisonRangeDto range)
        {
            var population = await journals.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: bookId,
                PeriodId: range.PeriodId, EffectiveFrom: range.RangeStart, EffectiveTo: range.RangeEnd), ct).ConfigureAwait(false);
            if (population.Any(record => record.PeriodId != range.PeriodId)
                || population.Select(record => record.Entry.JournalEntryId).Distinct().Count() != population.Count)
                throw new InvalidDataException("The retained journal population contains foreign or duplicate source identities.");
            return population.Where(record => record.CreatedAt <= asOf)
                .Where(record => (record.Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(record.Entry.Timestamp.UtcDateTime)) >= range.RangeStart
                    && (record.Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(record.Entry.Timestamp.UtcDateTime)) <= range.RangeEnd)
                .OrderBy(record => record.GlobalSequence).ToArray();
        }
    }

    public static (PostedPeriodComparisonRangeDto Current, PostedPeriodComparisonRangeDto? Baseline, string? Reason)
        SelectWindows(LedgerAccountingPeriod current, LedgerAccountingPeriod baseline, DateTimeOffset asOf)
    {
        var range = CurrentRange(current, asOf);
        if (current.StartDate > current.EndDate || baseline.StartDate > baseline.EndDate || range.RangeEnd < range.RangeStart)
            return (range, null, "The fiscal comparison interval is unavailable for this cutoff.");
        var priorEnd = baseline.EndDate;
        if (range.IsPartial)
        {
            priorEnd = baseline.StartDate.AddDays(range.RangeEnd.DayNumber - range.RangeStart.DayNumber);
            if (priorEnd > baseline.EndDate)
                return (range, null, "The prior fiscal period cannot supply an equivalent elapsed interval.");
        }
        return (range, new(baseline.PeriodId, baseline.Label, baseline.StartDate, baseline.EndDate,
            baseline.StartDate, priorEnd, priorEnd < baseline.EndDate, baseline.Status), null);
    }

    private static PostedPeriodComparisonRangeDto CurrentRange(LedgerAccountingPeriod period, DateTimeOffset asOf)
    {
        var cutoff = DateOnly.FromDateTime(asOf.UtcDateTime);
        var end = cutoff < period.EndDate ? cutoff : period.EndDate;
        return new(period.PeriodId, period.Label, period.StartDate, period.EndDate, period.StartDate, end,
            end < period.EndDate || !IsCompleted(period), period.Status);
    }

    private static bool IsCompleted(LedgerAccountingPeriod period)
        => period.Status is "SoftClosed" or "HardClosed";
    private static string Fingerprint(IReadOnlyList<LedgerJournalEntryRecord> current, IReadOnlyList<LedgerJournalEntryRecord> baseline)
        => Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new { current, baseline }));

    public static IReadOnlyList<PostedPeriodComparisonLineDto> CompareLines(
        IReadOnlyList<LedgerJournalEntryDto> current, IReadOnlyList<LedgerJournalEntryDto> baseline)
    {
        if (current.Concat(baseline).Select(entry => entry.AccountingBasis).Distinct().Take(2).Count() > 1)
            throw new InvalidDataException("Comparison aggregation requires one explicit accounting basis.");
        var rows = current.SelectMany(entry => entry.Lines).Select(line => (Line: line, Current: true))
            .Concat(baseline.SelectMany(entry => entry.Lines).Select(line => (Line: line, Current: false)));
        return rows.GroupBy(row => JsonSerializer.Serialize(new
        {
            row.Line.AccountName,
            row.Line.AccountType,
            row.Line.Symbol,
            FinancialAccountId = row.Line.FinancialAccountId?.ToUpperInvariant(),
            Dimensions = row.Line.Dimensions is null ? null : row.Line.Dimensions with
            {
                ExternalGlDimensions = row.Line.Dimensions.ExternalGlDimensions
                        .OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value)
            }
        }), StringComparer.Ordinal).Select(group =>
        {
            var identity = group.First().Line;
            if (!Enum.TryParse<LedgerAccountType>(identity.AccountType, out var accountType))
                throw new InvalidDataException("The posted account type is unsupported.");
            var currentDebit = group.Where(row => row.Current).Sum(row => row.Line.Debit);
            var currentCredit = group.Where(row => row.Current).Sum(row => row.Line.Credit);
            var priorDebit = group.Where(row => !row.Current).Sum(row => row.Line.Debit);
            var priorCredit = group.Where(row => !row.Current).Sum(row => row.Line.Credit);
            var currentBalance = Meridian.Ledger.Ledger.CalculateNetBalance(accountType, currentDebit, currentCredit);
            var priorBalance = Meridian.Ledger.Ledger.CalculateNetBalance(accountType, priorDebit, priorCredit);
            var change = currentBalance - priorBalance;
            return new PostedPeriodComparisonLineDto(Sha256Digest.ComputeUtf8(group.Key), identity.AccountName,
                identity.AccountType, identity.Symbol, identity.FinancialAccountId, currentDebit, currentCredit,
                currentBalance, priorDebit, priorCredit, priorBalance, change,
                priorBalance == 0 ? null : change / Math.Abs(priorBalance) * 100m, identity.Dimensions);
        }).OrderBy(line => line.AccountType, StringComparer.Ordinal)
            .ThenBy(line => line.AccountName, StringComparer.Ordinal).ThenBy(line => line.AccountKey, StringComparer.Ordinal).ToArray();
    }
}
