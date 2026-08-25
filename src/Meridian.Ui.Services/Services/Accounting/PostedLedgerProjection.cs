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
}
