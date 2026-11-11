using Meridian.Contracts.Workstation;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>A durable accounting period: its canonical selector (the period id) and its boundaries.</summary>
public sealed record ReportingSchedulePeriodMatch(string Selector, DateOnly StartDate, DateOnly EndDate);

/// <summary>
/// Resolves the accounting period a period-close reporting schedule should report on for a
/// given date. The ledger store is the authority on period identity: fiscal periods need
/// not follow calendar months, so a selector must never be derived from the date alone.
/// </summary>
public interface IReportingSchedulePeriodResolver
{
    /// <summary>
    /// Returns the one accounting period of the parameters' ledger book that contains
    /// <paramref name="date"/>, or <see langword="null"/> when the book or a unique containing
    /// period cannot be found.
    /// </summary>
    Task<ReportingSchedulePeriodMatch?> ResolvePeriodAsync(
        ReportingRunParametersDto parameters,
        DateOnly date,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the accounting period a period-close schedule should close next, after the
    /// period ending on <paramref name="currentPeriodEnd"/>:
    /// <list type="bullet">
    /// <item>Monthly: the period that starts the following day.</item>
    /// <item>Quarterly: the quarter-end period (period number 3, 6, 9 or 12) of the fiscal
    /// quarter that the following day falls in.</item>
    /// <item>Annually: the last period (number 12) of the fiscal year that the following day
    /// falls in.</item>
    /// </list>
    /// Quarter and year ends are defined by the ledger's own period numbers, so only fiscal
    /// years with exactly twelve periods numbered 1 to 12 are supported for those cadences.
    /// Returns <see langword="null"/> whenever the ledger cannot name one period.
    /// </summary>
    Task<ReportingSchedulePeriodMatch?> ResolveNextClosePeriodAsync(
        ReportingRunParametersDto parameters,
        DateOnly currentPeriodEnd,
        ReportingSchedulePeriodFrequencyDto frequency,
        CancellationToken ct = default);
}

/// <summary>Ledger-store-backed <see cref="IReportingSchedulePeriodResolver"/>.</summary>
public sealed class LedgerReportingSchedulePeriodResolver : IReportingSchedulePeriodResolver
{
    private readonly Func<Guid, CancellationToken, Task<LedgerBookRecord?>> _getBook;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<LedgerBookRecord>>> _listFundBooks;
    private readonly Func<Guid, CancellationToken, Task<IReadOnlyList<LedgerAccountingPeriod>>> _listBookPeriods;

    public LedgerReportingSchedulePeriodResolver(ILedgerJournalStore journalStore)
        : this(
            (journalStore ?? throw new ArgumentNullException(nameof(journalStore))).GetLedgerBookAsync,
            (fundId, ct) => journalStore.ListLedgerBooksAsync(fundProfileId: fundId, ct: ct),
            (bookId, ct) => journalStore.ListPeriodsAsync(ledgerBookId: bookId, ct: ct))
    {
    }

    /// <summary>Lookup-delegate constructor; lets the selection rules be exercised without a full store.</summary>
    public LedgerReportingSchedulePeriodResolver(
        Func<Guid, CancellationToken, Task<LedgerBookRecord?>> getBook,
        Func<string, CancellationToken, Task<IReadOnlyList<LedgerBookRecord>>> listFundBooks,
        Func<Guid, CancellationToken, Task<IReadOnlyList<LedgerAccountingPeriod>>> listBookPeriods)
    {
        _getBook = getBook ?? throw new ArgumentNullException(nameof(getBook));
        _listFundBooks = listFundBooks ?? throw new ArgumentNullException(nameof(listFundBooks));
        _listBookPeriods = listBookPeriods ?? throw new ArgumentNullException(nameof(listBookPeriods));
    }

    public async Task<ReportingSchedulePeriodMatch?> ResolvePeriodAsync(
        ReportingRunParametersDto parameters,
        DateOnly date,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var book = await ResolveBookAsync(parameters, ct).ConfigureAwait(false);
        if (book is null)
        {
            return null;
        }

        var periods = await _listBookPeriods(book.LedgerBookId, ct).ConfigureAwait(false);
        var containing = periods
            .Where(period => period.LedgerBookId == book.LedgerBookId
                && period.StartDate <= date
                && date <= period.EndDate)
            .ToArray();

        // Overlapping or missing periods are a ledger-setup question for an operator; picking one
        // here would silently certify a report against the wrong period.
        return containing.Length == 1
            ? new ReportingSchedulePeriodMatch(
                containing[0].PeriodId.ToString("D"),
                containing[0].StartDate,
                containing[0].EndDate)
            : null;
    }

    public async Task<ReportingSchedulePeriodMatch?> ResolveNextClosePeriodAsync(
        ReportingRunParametersDto parameters,
        DateOnly currentPeriodEnd,
        ReportingSchedulePeriodFrequencyDto frequency,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var book = await ResolveBookAsync(parameters, ct).ConfigureAwait(false);
        if (book is null)
        {
            return null;
        }

        var periods = (await _listBookPeriods(book.LedgerBookId, ct).ConfigureAwait(false))
            .Where(period => period.LedgerBookId == book.LedgerBookId)
            .ToArray();
        var nextDay = currentPeriodEnd.AddDays(1);
        var following = periods
            .Where(period => period.StartDate <= nextDay && nextDay <= period.EndDate)
            .ToArray();
        if (following.Length != 1)
        {
            return null;
        }

        var target = following[0];
        if (frequency != ReportingSchedulePeriodFrequencyDto.Monthly)
        {
            // A fiscal quarter is three consecutive period numbers and a fiscal year is twelve.
            // Any other shape (a 13-period year, gaps, duplicates, a year not fully opened) is
            // not guessed at.
            var fiscalYear = periods
                .Where(period => period.FiscalYear == target.FiscalYear)
                .OrderBy(static period => period.PeriodNo)
                .ToArray();
            if (fiscalYear.Length != PeriodsPerFiscalYear
                || !fiscalYear.Select(static period => period.PeriodNo)
                    .SequenceEqual(Enumerable.Range(1, PeriodsPerFiscalYear)))
            {
                return null;
            }

            var closingPeriodNo = frequency == ReportingSchedulePeriodFrequencyDto.Quarterly
                ? ((target.PeriodNo + PeriodsPerFiscalQuarter - 1) / PeriodsPerFiscalQuarter) * PeriodsPerFiscalQuarter
                : PeriodsPerFiscalYear;
            target = fiscalYear[closingPeriodNo - 1];
        }

        return new ReportingSchedulePeriodMatch(target.PeriodId.ToString("D"), target.StartDate, target.EndDate);
    }

    private const int PeriodsPerFiscalYear = 12;
    private const int PeriodsPerFiscalQuarter = 3;

    // Mirrors LedgerReportingAuthoritativeSource.ResolveBookAsync: an explicit book id wins,
    // otherwise the code must match exactly one book of the fund by id or display name.
    private async Task<LedgerBookRecord?> ResolveBookAsync(
        ReportingRunParametersDto parameters,
        CancellationToken ct)
    {
        var fundId = parameters.Scope?.FundProfileId;
        LedgerBookRecord? book;
        if (parameters.LedgerBook?.LedgerBookId is { } bookId)
        {
            book = bookId == Guid.Empty ? null : await _getBook(bookId, ct).ConfigureAwait(false);
        }
        else
        {
            var code = parameters.LedgerBook?.LedgerBookCode?.Trim();
            if (string.IsNullOrEmpty(code) || string.IsNullOrWhiteSpace(fundId))
            {
                return null;
            }

            var matches = (await _listFundBooks(fundId, ct).ConfigureAwait(false))
                .Where(candidate =>
                    string.Equals(candidate.LedgerBookId.ToString("D"), code, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.DisplayName, code, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            book = matches.Length == 1 ? matches[0] : null;
        }

        return book is not null
            && (string.IsNullOrWhiteSpace(fundId)
                || string.Equals(book.FundProfileId, fundId, StringComparison.Ordinal))
            ? book
            : null;
    }
}
