using System.Globalization;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public sealed partial class PortfolioCashLadderReadService
{
    /// <summary>
    /// Today's ladder against the ladder rebuilt as of <paramref name="baselineAsOf"/> (default: the
    /// last day of the previous calendar month): opening cash, flows, the lowest cumulative cash, the
    /// number of buckets below the minimum, and each bucket's net flow by position. The prior ladder
    /// opens from the ledger's cash as of that day; instrument projections use their current terms.
    /// </summary>
    public async Task<PeriodComparisonDto> GetCashLadderComparisonAsync(
        PortfolioCashLadderQuery query,
        DateOnly? baselineAsOf,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        var now = _clock.GetUtcNow();
        var asOfDate = DateOnly.FromDateTime(now.UtcDateTime.Date);
        var baselineDate = baselineAsOf ?? new DateOnly(asOfDate.Year, asOfDate.Month, 1).AddDays(-1);
        var current = PointInTime(asOfDate);
        var baseline = PointInTime(baselineDate);
        var notes = new List<string>
        {
            "The prior ladder is rebuilt as of the prior date: it opens from the ledger's cash at the end of that day and projects the same horizon forward from it.",
            "Instrument cash flows use each instrument's current terms; terms that changed since the prior date are not replayed.",
            "Buckets are compared by position (first bucket with first bucket), because each ladder's bucket dates start from its own as-of day."
        };

        if (baselineDate >= asOfDate)
        {
            return new PeriodComparisonDto(PeriodComparisonStatus.Incompatible, "The prior date must be before today.",
                "Portfolio cash ladder", null, current, baseline, [], notes, now);
        }

        if (_cashBalanceProvider is not IPortfolioCashBalanceHistoryProvider history)
        {
            return new PeriodComparisonDto(PeriodComparisonStatus.Unavailable,
                "The registered cash-balance provider cannot read cash as of an earlier date, so the prior ladder cannot be rebuilt.",
                "Portfolio cash ladder", null, current, baseline, [], notes, now);
        }

        var currentLadder = await GetCashLadderAsync(query, ct).ConfigureAwait(false);
        var baselineLadder = await BuildLadderAsync(
            query, now, baselineDate, token => history.GetCashBalancesAsOfAsync(baselineDate, token), ct).ConfigureAwait(false);

        if (!string.Equals(currentLadder.BaseCurrency, baselineLadder.BaseCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return new PeriodComparisonDto(PeriodComparisonStatus.Incompatible,
                $"The ladders are in different base currencies ({currentLadder.BaseCurrency} now, {baselineLadder.BaseCurrency} then) and no FX conversion source is registered.",
                "Portfolio cash ladder", currentLadder.BaseCurrency, current, baseline, [], notes, now);
        }

        var lines = new List<PeriodComparisonLineDto>
        {
            PeriodComparisonLineDto.Create("opening-cash", "Opening cash", currentLadder.OpeningCash, baselineLadder.OpeningCash),
            PeriodComparisonLineDto.Create("inflows", "Inflows over the horizon", Sum(currentLadder, static b => b.Inflows), Sum(baselineLadder, static b => b.Inflows)),
            PeriodComparisonLineDto.Create("outflows", "Outflows over the horizon", Sum(currentLadder, static b => b.Outflows), Sum(baselineLadder, static b => b.Outflows)),
            PeriodComparisonLineDto.Create("net-flow", "Net flow over the horizon", Sum(currentLadder, static b => b.NetFlow), Sum(baselineLadder, static b => b.NetFlow)),
            PeriodComparisonLineDto.Create("ending-cash", "Ending cash", EndingCash(currentLadder), EndingCash(baselineLadder), isTotal: true),
            PeriodComparisonLineDto.Create("lowest-cash", "Lowest cumulative cash", LowestCash(currentLadder), LowestCash(baselineLadder)),
            PeriodComparisonLineDto.Create("breaches", "Buckets below the minimum", currentLadder.Buckets.Count(static b => b.BreachesMinimumBalance), baselineLadder.Buckets.Count(static b => b.BreachesMinimumBalance))
        };
        var bucketCount = Math.Min(currentLadder.Buckets.Count, baselineLadder.Buckets.Count);
        for (var index = 0; index < bucketCount; index++)
        {
            lines.Add(PeriodComparisonLineDto.Create(
                $"bucket:{index + 1}",
                $"Bucket {index + 1} net flow",
                currentLadder.Buckets[index].NetFlow,
                baselineLadder.Buckets[index].NetFlow));
        }

        if (!baselineLadder.IsDecisionReady)
        {
            notes.Add($"The prior ladder is not decision-ready: {string.Join(" ", baselineLadder.BlockingReasons)}");
        }

        return new PeriodComparisonDto(PeriodComparisonStatus.Ready, null, "Portfolio cash ladder", currentLadder.BaseCurrency,
            current, baseline, lines, notes, now);
    }

    private static decimal Sum(PortfolioCashLadderDto ladder, Func<PortfolioCashLadderBucketDto, decimal> selector) =>
        ladder.Buckets.Sum(selector);

    private static decimal EndingCash(PortfolioCashLadderDto ladder) =>
        ladder.Buckets.Count == 0 ? ladder.OpeningCash : ladder.Buckets[^1].CumulativeCash;

    private static decimal LowestCash(PortfolioCashLadderDto ladder) =>
        ladder.Buckets.Count == 0 ? ladder.OpeningCash : Math.Min(ladder.OpeningCash, ladder.Buckets.Min(static b => b.CumulativeCash));

    private static PeriodComparisonWindowDto PointInTime(DateOnly day) =>
        new($"As of {day.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}", null, day, false);
}
