using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Application.Backfill;

internal static class BackfillPartitionPlanner
{
    public static IReadOnlyList<BackfillPartitionEstimate> Build(
        IHistoricalDataProvider provider,
        DataGranularity granularity,
        DateOnly fromInclusive,
        DateOnly toExclusive,
        int symbolCount)
    {
        if (toExclusive <= fromInclusive)
        {
            return Array.Empty<BackfillPartitionEstimate>();
        }

        EnsureGranularitySupported(provider, granularity);

        var tradingDays = EstimateTradingDays(fromInclusive, toExclusive);
        var maxTradingDays = GetMaxTradingDaysPerPartition(granularity, tradingDays);
        var partitions = new List<BackfillPartitionEstimate>();
        var partitionStart = fromInclusive;
        var cursor = fromInclusive;
        var partitionTradingDays = 0;

        while (cursor < toExclusive)
        {
            if (IsWeekday(cursor))
            {
                partitionTradingDays++;
            }

            var next = cursor.AddDays(1);
            var shouldCloseForTradingLimit =
                partitionTradingDays >= maxTradingDays && HasWeekdayBetween(next, toExclusive);
            if (shouldCloseForTradingLimit || next >= toExclusive)
            {
                partitions.Add(new BackfillPartitionEstimate(
                    Sequence: partitions.Count + 1,
                    From: partitionStart,
                    ToExclusive: next,
                    TradingDays: partitionTradingDays,
                    EstimatedApiCalls: symbolCount));

                partitionStart = next;
                partitionTradingDays = 0;
            }

            cursor = next;
        }

        return partitions;
    }

    public static int EstimateTradingDays(DateOnly fromInclusive, DateOnly toExclusive)
    {
        if (toExclusive <= fromInclusive)
            return 0;

        var totalDays = toExclusive.DayNumber - fromInclusive.DayNumber;
        var fullWeeks = totalDays / 7;
        var remainingDays = totalDays % 7;
        var startDow = (int)fromInclusive.DayOfWeek;

        var partialWeekdays = 0;
        for (var i = 0; i < remainingDays; i++)
        {
            var dow = (startDow + i) % 7;
            if (dow is not 0 and not 6)
                partialWeekdays++;
        }

        return fullWeeks * 5 + partialWeekdays;
    }

    public static string GetPartitionReason(IHistoricalDataProvider provider, DataGranularity granularity, int tradingDays)
    {
        if (granularity.IsIntraday())
        {
            return $"{granularity.ToDisplayName()} provider-window";
        }

        return tradingDays > 365
            ? "large-range trading-year"
            : $"{provider.Name} single-window";
    }

    public static DateOnly ToInclusiveEnd(BackfillPartitionEstimate partition)
        => partition.ToExclusive.AddDays(-1);

    private static int GetMaxTradingDaysPerPartition(DataGranularity granularity, int tradingDays)
    {
        if (!granularity.IsIntraday())
        {
            return tradingDays > 365 ? 252 : Math.Max(tradingDays, 1);
        }

        return granularity switch
        {
            DataGranularity.Minute1 => 8,
            DataGranularity.Minute5 or DataGranularity.Minute15 or DataGranularity.Minute30 => 60,
            DataGranularity.Hour1 or DataGranularity.Hour4 => 730,
            _ => 1
        };
    }

    private static void EnsureGranularitySupported(IHistoricalDataProvider provider, DataGranularity granularity)
    {
        if (!granularity.IsIntraday())
        {
            return;
        }

        if (provider is not IHistoricalAggregateBarProvider aggregateProvider ||
            !aggregateProvider.SupportedGranularities.Contains(granularity))
        {
            throw new InvalidOperationException(
                $"Provider '{provider.DisplayName}' does not support {granularity.ToDisplayName()} intraday backfill.");
        }
    }

    private static bool IsWeekday(DateOnly date)
    {
        var day = date.DayOfWeek;
        return day is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
    }

    private static bool HasWeekdayBetween(DateOnly fromInclusive, DateOnly toExclusive)
    {
        for (var cursor = fromInclusive; cursor < toExclusive; cursor = cursor.AddDays(1))
        {
            if (IsWeekday(cursor))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Operator-visible partition plan for a provider backfill estimate.
/// </summary>
public sealed record BackfillPartitionEstimate(
    int Sequence,
    DateOnly From,
    DateOnly ToExclusive,
    int TradingDays,
    long EstimatedApiCalls);
