using System.Text.Json.Serialization;

namespace Meridian.Storage.Backfill;

/// <summary>
/// One contiguous run of session dates whose bars were durably written for a symbol.
/// </summary>
/// <param name="From">
/// First covered date, inclusive. <see cref="OpenStart"/> when the fetch asked for all history up
/// to <paramref name="To"/>, so nothing earlier is outstanding.
/// </param>
/// <param name="To">Last covered date, inclusive: the newest bar that was written.</param>
/// <param name="Bars">Bars written for this range (summed when ranges merge).</param>
public sealed record BackfillCoverageRange(DateOnly From, DateOnly To, long Bars)
{
    /// <summary>Marks a range fetched without a lower bound.</summary>
    public static readonly DateOnly OpenStart = DateOnly.MinValue;
}

/// <summary>Coverage to record for one symbol after its bars reached storage.</summary>
/// <param name="Symbol">Symbol the bars were stored under.</param>
/// <param name="Ranges">Date ranges written in this run.</param>
/// <param name="Sources">Vendors that served the bars (differs from the provider on a composite route).</param>
public sealed record BackfillCoverageUpdate(
    string Symbol,
    IReadOnlyList<BackfillCoverageRange> Ranges,
    IReadOnlyCollection<string> Sources);

/// <summary>Interval arithmetic for verified backfill coverage.</summary>
public static class BackfillCoverageMath
{
    /// <summary>
    /// A fetch that starts on a non-session day (weekend, holiday, a short market closure) returns
    /// its first bar a few days later. A gap of at most this many calendar days between the
    /// requested start and the first written bar is recorded as covered, so resuming the same
    /// request does not ask for those empty days again. A larger gap is not assumed: coverage
    /// then starts at the first written bar.
    /// </summary>
    public const int LeadingNonSessionToleranceDays = 7;

    /// <summary>
    /// Coverage start for bars fetched from <paramref name="requestedFrom"/> whose earliest written
    /// bar is <paramref name="firstBar"/>.
    /// </summary>
    public static DateOnly CoverageStart(DateOnly? requestedFrom, DateOnly firstBar)
    {
        if (requestedFrom is not { } from)
            return BackfillCoverageRange.OpenStart;
        if (from >= firstBar)
            return firstBar;
        return firstBar.DayNumber - from.DayNumber <= LeadingNonSessionToleranceDays ? from : firstBar;
    }

    /// <summary>
    /// The coverage to record for one successfully fetched range, or <c>null</c> when the fetch
    /// proves nothing.
    /// </summary>
    /// <param name="requestedFrom">Inclusive start that was requested; null for all earlier history.</param>
    /// <param name="requestedTo">Inclusive end that was requested; null for open-ended.</param>
    /// <param name="firstBar">Earliest bar written for the range, if any.</param>
    /// <param name="lastBar">Newest bar written for the range, if any.</param>
    /// <param name="bars">Bars written for the range.</param>
    /// <param name="followedByCoverage">
    /// True when the day after <paramref name="requestedTo"/> is already covered, so the range
    /// fills in before existing coverage rather than extending the newest end.
    /// </param>
    /// <remarks>
    /// Coverage normally ends at the newest written bar, never at the requested end: a provider
    /// can publish the last sessions later, and those must still be fetched. Only when later dates
    /// are already covered do trailing empty days (and a short, entirely empty range) count, within
    /// <see cref="LeadingNonSessionToleranceDays"/>, so a weekend or holiday between two covered
    /// ranges is not requested again on every resume.
    /// </remarks>
    public static BackfillCoverageRange? WrittenRange(
        DateOnly? requestedFrom,
        DateOnly? requestedTo,
        DateOnly? firstBar,
        DateOnly? lastBar,
        long bars,
        bool followedByCoverage)
    {
        if (bars > 0 && firstBar is { } first && lastBar is { } last)
        {
            var end = followedByCoverage && requestedTo is { } to && to > last &&
                      to.DayNumber - last.DayNumber <= LeadingNonSessionToleranceDays
                ? to
                : last;
            return new BackfillCoverageRange(CoverageStart(requestedFrom, first), end, bars);
        }

        return followedByCoverage && requestedFrom is { } emptyFrom && requestedTo is { } emptyTo &&
               emptyTo >= emptyFrom && emptyTo.DayNumber - emptyFrom.DayNumber < LeadingNonSessionToleranceDays
            ? new BackfillCoverageRange(emptyFrom, emptyTo, 0)
            : null;
    }

    /// <summary>True when <paramref name="date"/> lies inside one of <paramref name="covered"/>.</summary>
    public static bool Covers(IEnumerable<BackfillCoverageRange> covered, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(covered);
        return covered.Any(range => range.From <= date && range.To >= date);
    }

    /// <summary>Sorts ranges and merges those that overlap or touch.</summary>
    public static IReadOnlyList<BackfillCoverageRange> Merge(IEnumerable<BackfillCoverageRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        var merged = new List<BackfillCoverageRange>();
        foreach (var range in ranges.Where(static r => r.To >= r.From).OrderBy(static r => r.From).ThenBy(static r => r.To))
        {
            if (merged.Count > 0 && range.From.DayNumber <= merged[^1].To.DayNumber + 1)
            {
                var last = merged[^1];
                merged[^1] = new BackfillCoverageRange(last.From, range.To > last.To ? range.To : last.To, last.Bars + range.Bars);
            }
            else
            {
                merged.Add(range);
            }
        }

        return merged;
    }

    /// <summary>
    /// Returns the parts of the requested range that <paramref name="covered"/> does not contain,
    /// in date order. A null bound is open: a null <paramref name="from"/> asks for all earlier
    /// history, a null <paramref name="to"/> for everything after the newest covered date.
    /// </summary>
    public static IReadOnlyList<(DateOnly? From, DateOnly? To)> Missing(
        DateOnly? from,
        DateOnly? to,
        IEnumerable<BackfillCoverageRange> covered)
    {
        ArgumentNullException.ThrowIfNull(covered);
        var ranges = Merge(covered);
        var missing = new List<(DateOnly? From, DateOnly? To)>();
        if (from.HasValue && to.HasValue && to.Value < from.Value)
            return missing;

        // Null means "no lower bound yet": only an open-start range can satisfy it.
        DateOnly? cursor = from;
        foreach (var range in ranges)
        {
            if (to.HasValue && range.From > to.Value)
                break;

            if (cursor is null)
            {
                if (range.From != BackfillCoverageRange.OpenStart)
                    missing.Add((null, range.From.AddDays(-1)));
                cursor = range.To.AddDays(1);
                continue;
            }

            if (range.To < cursor.Value)
                continue;
            if (range.From > cursor.Value)
                missing.Add((cursor, range.From.AddDays(-1)));
            cursor = range.To.AddDays(1);
        }

        if (cursor is null || to is null || cursor.Value <= to.Value)
            missing.Add((cursor, to));

        return missing;
    }
}

/// <summary>Persisted coverage sidecar. Unknown versions are ignored, which only causes a refetch.</summary>
internal sealed class BackfillCoverageDocument
{
    public const int CurrentVersion = 1;

    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName("entries")]
    public List<BackfillCoverageEntry> Entries { get; set; } = [];
}

/// <summary>Coverage of one symbol in one granularity lane, from one provider dataset.</summary>
internal sealed class BackfillCoverageEntry
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("granularity")]
    public string Granularity { get; set; } = string.Empty;

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    [JsonPropertyName("feed")]
    public string Feed { get; set; } = string.Empty;

    [JsonPropertyName("ranges")]
    public List<BackfillCoverageRange> Ranges { get; set; } = [];

    [JsonPropertyName("sources")]
    public List<string> Sources { get; set; } = [];

    [JsonPropertyName("recordedAt")]
    public DateTimeOffset RecordedAt { get; set; }
}
