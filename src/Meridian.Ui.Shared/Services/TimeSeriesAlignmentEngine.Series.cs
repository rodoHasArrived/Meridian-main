using Meridian.Contracts.Api;

namespace Meridian.Ui.Shared.Services;

internal sealed partial class TimeSeriesAlignmentEngine
{
    private static AlignedSeries AlignSeries(AlignmentPlan plan, SeriesKey key, List<Observation> input, CancellationToken ct)
    {
        var basis = input.Select(observation => observation.Basis).Distinct().ToArray();
        if (basis.Length > 1)
            throw new InvalidDataException("A provider series mixes price-adjustment bases. Select a single corporate-action policy.");

        if (plan.InputKind == DatasetKind.Trades &&
            input.GroupBy(observation => plan.Slots[observation.Slot].Date)
                .Any(session => session.Select(observation => observation.EventType).Distinct().Count() > 1))
            throw new InvalidDataException("A provider session has both live and historical trades. Select one archive type to avoid double counting.");

        var observations = new List<(Observation Record, string[] Ids)>();
        foreach (var group in input.GroupBy(observation => observation.DuplicateKey, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var records = group.OrderBy(record => record.Sequence).ThenBy(record => record.SourceId, StringComparer.Ordinal).ToArray();
            var first = records[0];
            if (records.Skip(1).Any(record => (record with { SourceId = first.SourceId, Sequence = first.Sequence }) != first))
                throw new InvalidDataException("Conflicting observations share a provider record identity. Review the archived duplicates.");
            observations.Add((first, records.Select(record => record.SourceId).Distinct().Order(StringComparer.Ordinal).ToArray()));
        }

        var values = new AlignedValue?[plan.Slots.Count];
        var observedIntervals = 0;
        foreach (var group in observations.GroupBy(item => item.Record.Slot))
        {
            var ordered = group.OrderBy(item => item.Record.Timestamp)
                .ThenBy(item => item.Record.Sequence).ThenBy(item => item.Record.SourceId, StringComparer.Ordinal).ToArray();
            if (plan.InputKind == DatasetKind.AggregateBars)
            {
                for (var i = 1; i < ordered.Length; i++)
                {
                    if (ordered[i - 1].Record.End > ordered[i].Record.Timestamp)
                        throw new InvalidDataException("Overlapping provider aggregate bars would double-count volume. Select a single source frequency.");
                }
            }

            values[group.Key] = new(ordered[0].Record.Open, ordered.Max(item => item.Record.High),
                ordered.Min(item => item.Record.Low), ordered[^1].Record.Close, ordered.Sum(item => (decimal)item.Record.Volume),
                "Observed", ordered.SelectMany(item => item.Ids).Order(StringComparer.Ordinal).ToArray());
            observedIntervals++;
        }

        var priceBasis = basis.FirstOrDefault();
        var gapsFilled = FillGaps(plan, values, ct);
        return new(new(key.Provider, key.Symbol, priceBasis.ToString(), input.Count, observedIntervals,
            values.Length - observedIntervals, gapsFilled, input.Count - observations.Count), values);
    }

    private static int FillGaps(AlignmentPlan plan, AlignedValue?[] values, CancellationToken ct)
    {
        if (plan.Gap is GapPolicy.Null or GapPolicy.Skip || plan.MaxGap == 0)
            return 0;
        var filled = 0;
        // A provider adjustment flag does not identify action dates or methodology.
        // Keep prices from crossing session boundaries.
        var carryPrices = !plan.Daily;
        for (var segmentStart = 0; segmentStart < values.Length;)
        {
            var segmentEnd = plan.Daily ? values.Length : segmentStart + 1;
            while (segmentEnd < values.Length && plan.Slots[segmentEnd].Date == plan.Slots[segmentStart].Date)
                segmentEnd++;
            var cursor = segmentStart;
            while (cursor < segmentEnd)
            {
                ct.ThrowIfCancellationRequested();
                if (values[cursor] is not null)
                {
                    cursor++;
                    continue;
                }

                var gapStart = cursor++;
                while (cursor < segmentEnd && values[cursor] is null)
                    cursor++;
                var gapLength = cursor - gapStart;
                if (gapLength > plan.MaxGap)
                    continue;
                var previous = gapStart == segmentStart ? null : values[gapStart - 1];
                var next = cursor == segmentEnd ? null : values[cursor];
                if (previous is null && next is null)
                    continue;

                for (var index = gapStart; index < cursor; index++)
                {
                    values[index] = plan.Gap switch
                    {
                        GapPolicy.Zero => Fill(0, "ZeroFilled", []),
                        GapPolicy.ForwardFill when carryPrices && previous is not null =>
                            Fill(previous.Close, "ForwardFilled", previous.SourceIds),
                        GapPolicy.BackwardFill when carryPrices && next is not null =>
                            Fill(next.Open, "BackwardFilled", next.SourceIds),
                        GapPolicy.LinearInterpolate when carryPrices && previous is not null && next is not null =>
                            Fill(previous.Close + (next.Open - previous.Close) * ((index - gapStart + 1m) / (gapLength + 1m)),
                                "Interpolated", previous.SourceIds.Concat(next.SourceIds).Distinct().Order(StringComparer.Ordinal).ToArray()),
                        _ => null
                    };
                    if (values[index] is not null)
                        filled++;
                }
            }
            segmentStart = segmentEnd;
        }

        return filled;
    }

    private static AlignedValue Fill(decimal price, string status, string[] ids) =>
        new(price, price, price, price, 0m, status, ids);

    private static TimeSeriesAlignmentDifference[] CompareProviders(List<AlignedSeries> series, CancellationToken ct)
    {
        var differences = new List<TimeSeriesAlignmentDifference>();
        foreach (var symbol in series.GroupBy(item => item.Summary.Symbol, StringComparer.Ordinal))
        {
            var providers = symbol.ToArray();
            for (var left = 0; left < providers.Length; left++)
            {
                for (var right = left + 1; right < providers.Length; right++)
                {
                    ct.ThrowIfCancellationRequested();
                    differences.Add(ComparePair(symbol.Key, providers[left], providers[right]));
                }
            }
        }
        return differences.ToArray();
    }

    private static TimeSeriesAlignmentDifference ComparePair(string symbol, AlignedSeries left, AlignedSeries right)
    {
        var leftProvider = left.Summary.Provider;
        var rightProvider = right.Summary.Provider;
        var compared = 0;
        var equal = 0;
        var maximumDifference = 0m;
        for (var index = 0; index < left.Values.Length; index++)
        {
            if (left.Values[index] is not { Status: "Observed" } leftValue ||
                right.Values[index] is not { Status: "Observed" } rightValue)
                continue;

            compared++;
            var gap = Math.Abs(leftValue.Close - rightValue.Close);
            if (gap == 0m)
                equal++;
            maximumDifference = Math.Max(maximumDifference, gap);
        }

        var note = compared == 0 ? "No intervals have an observed close from both providers."
            : left.Summary.AdjustmentBasis != nameof(Basis.Raw) || right.Summary.AdjustmentBasis != nameof(Basis.Raw)
                ? "Differences use as-stored closes. The providers' corporate-action treatment is unverified."
                : null;
        return new(symbol, leftProvider, rightProvider, compared, equal, compared == 0 ? null : maximumDifference, note);
    }
}
