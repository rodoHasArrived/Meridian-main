using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FundOperationsWorkspaceReadService
{
    /// <summary>Most month-ends a NAV trend may span.</summary>
    public const int MaxNavTrendMonths = 24;

    private static readonly string LongSecuritiesAccountName = LedgerAccounts.Securities("X").Name;
    private static readonly string ShortSecuritiesAccountName = LedgerAccounts.ShortSecuritiesPayable("X").Name;

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
            var (nav, components) = await AttributeNavAsync(normalizedFundProfileId, baseCurrency, book, instant, ct).ConfigureAwait(false);
            navCurrency = nav.Currency;
            var (longExposure, shortExposure) = SecuritiesExposure(components);
            points.Add(new FundNavTrendPointDto(
                DateOnly.FromDateTime(instant.UtcDateTime),
                nav.TotalNav,
                nav.ComponentCount,
                LongExposure: longExposure,
                ShortExposure: shortExposure,
                GrossExposure: longExposure + shortExposure,
                NetExposure: longExposure - shortExposure));
        }

        var firstWithNav = points.FindIndex(static point => point.ComponentCount > 0 || point.TotalNav != 0m);
        var notes = new List<string>
        {
            "Each point is ledger NAV rebuilt as of that date, at the end of each month; the last point is NAV now.",
            "Cash, financing and strategy cuts that do not take a date are not included.",
            "Exposure is the ledger's securities held long and the short securities payable, at the last posted mark on or before each date."
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

    /// <summary>
    /// Long exposure is the balance of the ledger's securities accounts; short exposure is the balance
    /// owed on short securities payable, as a positive amount.
    /// </summary>
    private static (decimal Long, decimal Short) SecuritiesExposure(IReadOnlyList<NavComponent> components)
    {
        var longExposure = 0m;
        var shortExposure = 0m;
        foreach (var component in components)
        {
            if (string.Equals(component.AccountName, LongSecuritiesAccountName, StringComparison.Ordinal))
            {
                longExposure += component.Balance;
            }
            else if (string.Equals(component.AccountName, ShortSecuritiesAccountName, StringComparison.Ordinal))
            {
                shortExposure += component.Balance;
            }
        }

        return (longExposure, shortExposure);
    }
}
