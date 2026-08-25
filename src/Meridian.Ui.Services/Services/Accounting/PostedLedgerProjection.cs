using System.Globalization;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

/// <summary>
/// Presentation projection for the posted-journal ledger surface — the governed book of
/// record, scoped by ledger period.
/// <para>
/// Lives here rather than in the desktop assembly on purpose: WPF cannot execute its tests off
/// Windows (<c>Microsoft.WindowsDesktop.App</c> has no Linux build), so the decisions worth
/// testing — which period is the default subject, whether a missing summary is a notice or a
/// failure, how a period reads to an operator — are kept in a <c>net10.0</c> assembly where they
/// run on any platform. The desktop view model is a thin shell over these functions, and mirrors
/// the browser panel's behaviour so the two lanes cannot disagree about the book.
/// </para>
/// </summary>
public static class PostedLedgerProjection
{
    /// <summary>Newest period first, by fiscal year then period number, with a stable id tie-break.</summary>
    public static IReadOnlyList<LedgerPeriodDto> SortPeriodsDescending(IEnumerable<LedgerPeriodDto>? periods)
    {
        if (periods is null)
        {
            return [];
        }

        return periods
            .OrderByDescending(static period => period.FiscalYear)
            .ThenByDescending(static period => period.PeriodNo)
            .ThenBy(static period => period.PeriodId)
            .ToList();
    }

    /// <summary>
    /// The period a freshly opened surface should show: the latest closed one. Trial balance and
    /// P&amp;L publish from the closed-period summary, so defaulting to an open period would land
    /// the operator on the "not closed yet" notice instead of the book.
    /// </summary>
    public static Guid? ResolveDefaultPeriodId(IEnumerable<LedgerPeriodDto>? periods)
    {
        var sorted = SortPeriodsDescending(periods);
        if (sorted.Count == 0)
        {
            return null;
        }

        var closed = sorted.FirstOrDefault(static period => period.Status != LedgerPeriodStatusDto.Open);
        return (closed ?? sorted[0]).PeriodId;
    }

    /// <summary>Operator-facing label for a period, falling back to its fiscal coordinates.</summary>
    public static string DescribePeriod(LedgerPeriodDto period)
    {
        ArgumentNullException.ThrowIfNull(period);
        var label = period.Label?.Trim();
        return string.IsNullOrEmpty(label)
            ? string.Create(CultureInfo.InvariantCulture, $"FY{period.FiscalYear} P{period.PeriodNo}")
            : label;
    }

    public static string DescribePeriodStatus(LedgerPeriodStatusDto status) => status switch
    {
        LedgerPeriodStatusDto.HardClosed => "Hard closed",
        LedgerPeriodStatusDto.SoftClosed => "Soft closed",
        _ => "Open"
    };

    public static string DescribeSignoffStatus(LedgerPeriodSignoffStatusDto status) => status switch
    {
        LedgerPeriodSignoffStatusDto.SignedOff => "Signed off",
        LedgerPeriodSignoffStatusDto.Pending => "Sign-off pending",
        LedgerPeriodSignoffStatusDto.Rejected => "Sign-off rejected",
        _ => "Sign-off not required"
    };

    /// <summary>
    /// A 404 from the trial-balance or P&amp;L route means the period has no closed-period summary
    /// yet — an expected state for an open period. It must read as a notice, never as a failure,
    /// or every open period looks like an outage.
    /// </summary>
    public static bool IsMissingSummary(int statusCode) => statusCode == 404;

    public const string MissingSummaryNotice =
        "This period has no closed-period summary yet. Trial balance and P&L publish from the posted journal when the period closes.";

    /// <summary>Debits minus credits across the posted lines; non-zero means the book does not tie.</summary>
    public static decimal SumBalances(IEnumerable<LedgerPeriodTrialBalanceLineDto>? lines)
        => lines?.Sum(static line => line.Balance) ?? 0m;

    /// <summary>
    /// True when the posted trial balance does not tie. Uses a half-cent tolerance so decimal
    /// rounding in a currency column cannot raise a false alarm.
    /// </summary>
    public static bool IsOutOfBalance(IEnumerable<LedgerPeriodTrialBalanceLineDto>? lines)
    {
        var lineList = lines as IReadOnlyCollection<LedgerPeriodTrialBalanceLineDto> ?? lines?.ToList();
        if (lineList is null || lineList.Count == 0)
        {
            return false;
        }

        return Math.Abs(SumBalances(lineList)) > 0.005m;
    }

    /// <summary>Filters posted lines to the selected accounting basis.</summary>
    public static IReadOnlyList<LedgerPeriodTrialBalanceLineDto> FilterByBasis(
        IEnumerable<LedgerPeriodTrialBalanceLineDto>? lines,
        AccountingBasisKindDto basis)
        => lines?.Where(line => line.AccountingBasis == basis).ToList() ?? [];

    /// <summary>
    /// Matches a posted line against an operator's free-text account filter, over the same fields
    /// the browser panel searches.
    /// </summary>
    public static bool MatchesAccountFilter(LedgerPeriodTrialBalanceLineDto line, string? filter)
    {
        ArgumentNullException.ThrowIfNull(line);
        var needle = filter?.Trim();
        if (string.IsNullOrEmpty(needle))
        {
            return true;
        }

        return new[] { line.AccountName, line.AccountType, line.FinancialAccountId, line.Symbol }
            .Any(value => value is not null && value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Names the dimensional scope a posted line covers, or an empty string when it carries none.
    /// <para>
    /// The ledger service deliberately returns one row per account *per dimension set*, so an
    /// account posted in several funds, entities, sleeves or cost centres comes back as several
    /// rows with identical account name, type and symbol. Rendering only those three makes those
    /// rows indistinguishable, and an operator cannot tell which scope's balance they are signing
    /// off. Ordered widest-to-narrowest so the label reads as an address.
    /// </para>
    /// </summary>
    public static string DescribeDimensionScope(LedgerPeriodTrialBalanceLineDto line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var dimensions = line.Dimensions;
        if (dimensions is null)
        {
            return string.Empty;
        }

        var parts = new[]
        {
            dimensions.OrganizationId,
            dimensions.FundId,
            dimensions.EntityId,
            dimensions.PortfolioId,
            dimensions.SleeveId,
            dimensions.StrategyId,
            dimensions.CostCenterId,
            dimensions.CounterpartyId,
            dimensions.InvestorId,
            dimensions.CapitalAccountId,
            dimensions.TaxLotId
        }.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()).ToList();

        // External GL dimensions are operator-defined, so they are named rather than positional.
        if (dimensions.ExternalGlDimensions is { Count: > 0 })
        {
            parts.AddRange(dimensions.ExternalGlDimensions
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Key.Trim()}={pair.Value.Trim()}"));
        }

        return string.Join(" · ", parts);
    }

    /// <summary>Books newest-usable first: by display name, with a stable id tie-break.</summary>
    public static IReadOnlyList<LedgerBookDto> SortBooks(IEnumerable<LedgerBookDto>? books)
        => books?
            .OrderBy(book => book.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(book => book.LedgerBookId)
            .ToList() ?? [];

    /// <summary>
    /// The book a freshly opened surface should show. There is no notion of a "default" book in
    /// the contract, so this is simply the first in a stable order — the point is that the surface
    /// names the book it chose rather than presenting whichever book happened to own the latest
    /// closed period as though it were the fund's only one.
    /// </summary>
    public static Guid? ResolveDefaultBookId(IEnumerable<LedgerBookDto>? books)
        => SortBooks(books).Select(book => (Guid?)book.LedgerBookId).FirstOrDefault();

    /// <summary>Periods belonging to one book, newest first.</summary>
    public static IReadOnlyList<LedgerPeriodDto> FilterPeriodsByBook(
        IEnumerable<LedgerPeriodDto>? periods,
        Guid? ledgerBookId)
        => ledgerBookId is null
            ? SortPeriodsDescending(periods)
            : SortPeriodsDescending(periods?.Where(period => period.LedgerBookId == ledgerBookId.Value));

    /// <summary>
    /// Formats a ledger amount in the book's own base currency.
    /// <para>
    /// Deliberately not <c>ToString("C", CultureInfo.CurrentCulture)</c>: that takes the currency
    /// symbol from the operator's OS culture, so a USD book renders as pounds on a machine set to
    /// en-GB — the same number, relabelled as another currency, with no conversion. The ledger
    /// contract carries a currency code rather than a locale, so the code itself is shown. A book
    /// with no declared currency formats as a bare number rather than borrowing a symbol.
    /// </para>
    /// </summary>
    public static string FormatAmount(decimal value, string? currencyCode)
    {
        var amount = value.ToString("N2", CultureInfo.InvariantCulture);
        var code = currencyCode?.Trim();
        return string.IsNullOrEmpty(code) ? amount : $"{code} {amount}";
    }

    /// <summary>
    /// The accounting bases actually present in a period's posted lines, in enum order. A closed
    /// period can carry a Primary projection alongside GAAP, tax or statutory ones; they are
    /// different books of basis over the same accounts, so a surface must show one at a time
    /// rather than stacking them into duplicate rows and a meaningless total.
    /// </summary>
    public static IReadOnlyList<AccountingBasisKindDto> AvailableBases(
        IEnumerable<LedgerPeriodTrialBalanceLineDto>? lines)
        => lines?
            .Select(line => line.AccountingBasis)
            .Distinct()
            .OrderBy(basis => basis)
            .ToList() ?? [];

    /// <summary>
    /// The basis a freshly loaded period should show: Primary when present, else the first
    /// available. Defaulting to Primary unconditionally would render a GAAP-only period empty.
    /// </summary>
    public static AccountingBasisKindDto ResolveDefaultBasis(
        IEnumerable<LedgerPeriodTrialBalanceLineDto>? lines)
    {
        var available = AvailableBases(lines);
        if (available.Count == 0)
        {
            return AccountingBasisKindDto.Primary;
        }

        return available.Contains(AccountingBasisKindDto.Primary)
            ? AccountingBasisKindDto.Primary
            : available[0];
    }

    /// <summary>Human-readable basis name for a picker.</summary>
    public static string DescribeBasis(AccountingBasisKindDto basis) => basis switch
    {
        AccountingBasisKindDto.Primary => "Primary",
        AccountingBasisKindDto.Gaap => "GAAP",
        AccountingBasisKindDto.Cash => "Cash",
        AccountingBasisKindDto.Tax => "Tax",
        AccountingBasisKindDto.Statutory => "Statutory",
        _ => basis.ToString()
    };
}
