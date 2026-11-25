using Meridian.Wpf.ViewModels.Comparisons;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// NAV against a prior date and the NAV and exposure trend (UX Phase E parity). Both read the shared
/// <see cref="Meridian.Ui.Shared.Services.FundOperationsWorkspaceReadService"/> in process, the same
/// seam the browser's <c>/portfolio/nav</c> page reads over HTTP.
/// </summary>
public sealed partial class FundLedgerViewModel
{
    private const string NavComparisonIdleMessage = "Choose a fund profile to compare its NAV with a prior date.";
    private PeriodComparisonSectionViewModel? _navComparison;
    private NavTrendSectionViewModel? _navTrend;
    private string? _navFundProfileId;
    private string? _navCurrency;
    private DateTime? _navBaselineDate;

    /// <summary>NAV now against NAV at <see cref="NavBaselineDate"/>, in total and by asset class.</summary>
    public PeriodComparisonSectionViewModel NavComparison => _navComparison ??= new(
        "NAV vs prior period",
        "Net asset value now against a prior date, in total and by asset class, rebuilt from the posted ledger as of each date.",
        NavComparisonIdleMessage);

    /// <summary>Ledger NAV and securities exposure at the last 12 month-ends.</summary>
    public NavTrendSectionViewModel NavTrend => _navTrend ??= new();

    /// <summary>The prior date; empty means the end of the previous month. Changing it reloads the comparison.</summary>
    public DateTime? NavBaselineDate
    {
        get => _navBaselineDate;
        set
        {
            if (SetProperty(ref _navBaselineDate, value?.Date) && _navFundProfileId is not null)
            {
                _ = LoadNavComparisonAsync();
            }
        }
    }

    /// <summary>Starts both NAV reads for the active fund, or clears them when there is none.</summary>
    private void StartNavComparison(string? fundProfileId, string? currency)
    {
        _navFundProfileId = string.IsNullOrWhiteSpace(fundProfileId) ? null : fundProfileId.Trim();
        _navCurrency = currency;
        if (_navFundProfileId is null)
        {
            NavComparison.Reset(NavComparisonIdleMessage);
            NavTrend.Reset("Choose a fund profile to see its NAV trend.");
            return;
        }

        var trendFundProfileId = _navFundProfileId;
        var trendCurrency = _navCurrency;
        _ = NavTrend.LoadAsync(ct => _fundOperationsWorkspaceReadService.GetNavTrendAsync(trendFundProfileId, 12, asOf: null, currency: trendCurrency, ct: ct));
        _ = LoadNavComparisonAsync();
    }

    private Task LoadNavComparisonAsync()
    {
        var fundProfileId = _navFundProfileId!;
        var currency = _navCurrency;
        // The prior date is the end of that day (UTC), as in the browser.
        DateTimeOffset? baselineAsOf = _navBaselineDate is { } day
            ? new DateTimeOffset(DateTime.SpecifyKind(day.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc))
            : null;
        return NavComparison.LoadAsync(ct => _fundOperationsWorkspaceReadService.GetNavComparisonAsync(
            fundProfileId, asOf: null, baselineAsOf: baselineAsOf, currency: currency, ct: ct));
    }
}
