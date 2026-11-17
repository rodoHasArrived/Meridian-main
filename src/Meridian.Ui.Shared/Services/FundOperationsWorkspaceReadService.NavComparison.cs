using System.Globalization;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FundOperationsWorkspaceReadService
{
    /// <summary>
    /// Ledger NAV at <paramref name="asOf"/> against NAV at <paramref name="baselineAsOf"/> (default: the
    /// end of the calendar month before <paramref name="asOf"/>), in total and by asset class. Both sides
    /// are rebuilt from the posted ledger as of their own date through the same NAV attribution the
    /// workspace uses, so the prior figure is what the ledger said then, not a stored snapshot.
    /// </summary>
    public async Task<PeriodComparisonDto> GetNavComparisonAsync(
        string fundProfileId,
        DateTimeOffset? asOf,
        DateTimeOffset? baselineAsOf,
        string? currency,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fundProfileId);
        ct.ThrowIfCancellationRequested();

        var normalizedFundProfileId = fundProfileId.Trim();
        var currentAsOf = asOf ?? DateTimeOffset.UtcNow;
        var baselineAt = baselineAsOf ?? PriorMonthEnd(currentAsOf);
        var generatedAt = DateTimeOffset.UtcNow;
        var current = PointInTimeWindow(currentAsOf, isPartial: asOf is null);
        var baseline = PointInTimeWindow(baselineAt, isPartial: false);
        var runs = await LoadRunsAsync(normalizedFundProfileId, ct).ConfigureAwait(false);
        var displayName = ResolveDisplayName(normalizedFundProfileId, runs);
        var baseCurrency = ResolveCurrency(currency, []);

        if (baselineAt >= currentAsOf)
        {
            return new PeriodComparisonDto(
                PeriodComparisonStatus.Incompatible,
                "The prior date must be before the current date.",
                displayName, baseCurrency, current, baseline, [], [], generatedAt);
        }

        var currentBook = await BuildLedgerBookAsync(normalizedFundProfileId, runs, currentAsOf, ct).ConfigureAwait(false);
        var baselineBook = await BuildLedgerBookAsync(normalizedFundProfileId, runs, baselineAt, ct).ConfigureAwait(false);
        var currentNav = await BuildNavSummaryAsync(normalizedFundProfileId, baseCurrency, currentBook, currentAsOf, ct).ConfigureAwait(false);
        var baselineNav = await BuildNavSummaryAsync(normalizedFundProfileId, baseCurrency, baselineBook, baselineAt, ct).ConfigureAwait(false);

        var notes = new List<string>
        {
            "NAV is rebuilt from the posted ledger as of each date. Cash, financing and strategy cuts that do not take a date are not included.",
            "Asset classes come from the ledger's NAV attribution; a class present on only one side is compared with zero."
        };

        if (baselineNav.ComponentCount == 0 && baselineNav.TotalNav == 0m)
        {
            return new PeriodComparisonDto(
                PeriodComparisonStatus.Unavailable,
                $"The ledger has no NAV components as of {baseline.End.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}.",
                displayName, currentNav.Currency, current, baseline, [], notes, generatedAt);
        }

        var currentByClass = currentNav.AssetClassExposure.ToDictionary(static item => item.AssetClass, static item => item.NetBalance, StringComparer.OrdinalIgnoreCase);
        var baselineByClass = baselineNav.AssetClassExposure.ToDictionary(static item => item.AssetClass, static item => item.NetBalance, StringComparer.OrdinalIgnoreCase);
        var lines = new List<PeriodComparisonLineDto>
        {
            PeriodComparisonLineDto.Create("total", "Total NAV", currentNav.TotalNav, baselineNav.TotalNav, isTotal: true)
        };
        lines.AddRange(currentByClass.Keys.Union(baselineByClass.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
            .Select(key => PeriodComparisonLineDto.Create(
                $"asset-class:{key}",
                key,
                currentByClass.GetValueOrDefault(key),
                baselineByClass.GetValueOrDefault(key))));

        return new PeriodComparisonDto(
            PeriodComparisonStatus.Ready, null, displayName, currentNav.Currency, current, baseline, lines, notes, generatedAt);
    }

    /// <summary>The last instant of the calendar month before <paramref name="asOf"/>, in UTC.</summary>
    internal static DateTimeOffset PriorMonthEnd(DateTimeOffset asOf)
    {
        var utc = asOf.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(-1);
    }

    private static PeriodComparisonWindowDto PointInTimeWindow(DateTimeOffset asOf, bool isPartial)
    {
        var day = DateOnly.FromDateTime(asOf.UtcDateTime);
        return new PeriodComparisonWindowDto(
            $"As of {day.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}",
            Start: null,
            End: day,
            IsPartial: isPartial);
    }
}
