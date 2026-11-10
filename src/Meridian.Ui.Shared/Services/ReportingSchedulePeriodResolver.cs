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
