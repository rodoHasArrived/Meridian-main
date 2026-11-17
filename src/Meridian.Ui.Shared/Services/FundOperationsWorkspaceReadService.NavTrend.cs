using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FundOperationsWorkspaceReadService
{
    /// <summary>Most month-ends a NAV trend may span.</summary>
    public const int MaxNavTrendMonths = 24;

    /// <summary>
    /// Ledger NAV at the end of each of the last <paramref name="months"/> months, ending with NAV at
    /// <paramref name="asOf"/> (default now). The ledger is built once, as of the latest date; each
    /// point is its NAV attribution as of that point, which reads only entries timestamped on or
    /// before it. Months before the ledger's first NAV component are left out.
    /// </summary>
    public async Task<FundNavTrendDto> GetNavTrendAsync(
        string fundProfileId,
        int months = 12,
        DateTimeOffset? asOf = null,
        string? currency = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fundProfileId);
        ct.ThrowIfCancellationRequested();

        var normalizedFundProfileId = fundProfileId.Trim();
        var currentAsOf = (asOf ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var monthCount = Math.Clamp(months, 1, MaxNavTrendMonths);
        var runs = await LoadRunsAsync(normalizedFundProfileId, ct).ConfigureAwait(false);
        var displayName = ResolveDisplayName(normalizedFundProfileId, runs);
        var baseCurrency = ResolveCurrency(currency, []);
        var book = await BuildLedgerBookAsync(normalizedFundProfileId, runs, currentAsOf, ct).ConfigureAwait(false);

        var instants = new List<DateTimeOffset>();
        var monthEnd = PriorMonthEnd(currentAsOf);
        for (var index = 0; index < monthCount - 1; index++)
        {
            instants.Insert(0, monthEnd);
            monthEnd = PriorMonthEnd(monthEnd);
        }

        instants.Add(currentAsOf);
        var points = new List<FundNavTrendPointDto>(instants.Count);
        var navCurrency = baseCurrency;
        foreach (var instant in instants)
        {
            var nav = await BuildNavSummaryAsync(normalizedFundProfileId, baseCurrency, book, instant, ct).ConfigureAwait(false);
            navCurrency = nav.Currency;
            points.Add(new FundNavTrendPointDto(DateOnly.FromDateTime(instant.UtcDateTime), nav.TotalNav, nav.ComponentCount));
        }

        var firstWithNav = points.FindIndex(static point => point.ComponentCount > 0 || point.TotalNav != 0m);
        var notes = new List<string>
        {
            "Each point is ledger NAV rebuilt as of that date, at the end of each month; the last point is NAV now.",
            "Cash, financing and strategy cuts that do not take a date are not included."
        };
        if (firstWithNav > 0)
        {
            points.RemoveRange(0, firstWithNav);
            notes.Add("Months before the ledger's first NAV component are left out.");
        }
        else if (firstWithNav < 0)
        {
            points.Clear();
            notes.Add("The ledger has no NAV components in this window.");
        }

        return new FundNavTrendDto(normalizedFundProfileId, displayName, navCurrency, points, notes, DateTimeOffset.UtcNow);
    }
}
