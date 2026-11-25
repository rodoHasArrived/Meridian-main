using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Domain;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Services;
using Meridian.DataIntegration.Historical;
using Meridian.Platform.Scheduling;
using Meridian.Storage;
using Meridian.Storage.Store;

namespace Meridian.Ui.Shared.Services;

/// <summary>Computes the same bounded, source-attributed transformation for preview and execution.</summary>
internal sealed partial class TimeSeriesAlignmentEngine(StorageOptions storageOptions)
{
    private const int MaxOutputRows = 60_000;
    private const int MaxSourceIdCharacters = 8 * 1024 * 1024;
    private const int MaxProviders = 8;
    private const int MaxSymbols = 12;
    private const int MaxDays = 366;
    private const string DuplicatePolicy = "CollapseEqualRejectConflicting";
    private static readonly TradingCalendar UsCalendar = new();
    private static readonly string[] AllFields = ["Open", "High", "Low", "Close", "Volume"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal async Task<TimeSeriesAlignmentExecution> AlignAsync(TimeSeriesAlignmentRequest request, CancellationToken ct)
    {
        var plan = BuildPlan(request);
        var input = await ReadInputAsync(plan, ct).ConfigureAwait(false);
        var providers = plan.Providers ?? input.Keys.Select(key => key.Provider).Distinct().Order(StringComparer.Ordinal).ToArray();
        if (providers.Length < 2)
            throw new InvalidDataException("At least two providers with matching archived observations are required.");
        if (providers.Any(provider => !input.Keys.Any(key => key.Provider == provider)))
            throw new InvalidDataException("A selected provider has no usable observations for the requested symbols, dates and policies.");
        if ((long)plan.Slots.Count * providers.Length * plan.Symbols.Length > MaxOutputRows)
            throw new ArgumentException("The aligned grid is too large. Choose a coarser interval or narrow the scope.");

        var results = new List<AlignedSeries>();
        var sourceIdCharacters = 0L;
        foreach (var symbol in plan.Symbols)
        {
            foreach (var provider in providers)
            {
                ct.ThrowIfCancellationRequested();
                input.Records.TryGetValue(new SeriesKey(provider, symbol), out var observations);
                var result = AlignSeries(plan, new SeriesKey(provider, symbol), observations ?? [], ct);
                sourceIdCharacters += result.Values.Where(value => value is not null)
                    .Sum(value => value!.SourceIds.Sum(id => (long)id.Length));
                if (sourceIdCharacters > MaxSourceIdCharacters)
                    throw new ArgumentException("The aligned lineage is too large. Narrow the archive scope or reduce gap filling.");
                results.Add(result);
            }
        }

        var rows = new List<TimeSeriesAlignmentRow>();
        for (var index = 0; index < plan.Slots.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var slot = plan.Slots[index];
            foreach (var series in results)
            {
                var value = series.Values[index];
                if (plan.Gap == GapPolicy.Skip && value is null)
                    continue;
                rows.Add(ToRow(plan, slot, series.Summary, value));
            }
        }

        var summaries = results.Select(result => result.Summary).ToArray();
        var differences = CompareProviders(results, ct);
        var summary = new TimeSeriesAlignmentSummary(
            string.Empty, plan.RegularHours ? "US_EQUITIES_REGULAR" : "CONTINUOUS",
            TimestampRule(plan),
            plan.Zone.Id, plan.InputKind.ToString(), plan.Interval, plan.Gap.ToString(), plan.MaxGap,
            "Price carry and interpolation are limited to intraday gaps within one session. Filled volume is zero.",
            plan.Adjustment.ToString(), DuplicatePolicy, plan.Fields, plan.Slots.Count,
            summaries.Sum(x => x.SourceRecords), rows.Count, summaries.Sum(x => x.Gaps),
            summaries.Sum(x => x.GapsFilled), input.OutsideCalendar, summaries, differences);
        return new(summary with { ContentHash = ComputeHash(plan, summary, rows, ct) }, rows.ToArray());
    }

    private static string TimestampRule(AlignmentPlan plan)
    {
        if (plan.InputKind == DatasetKind.DailyBars)
            return "Retained session dates select the bar. Timestamps label session open or local midnight; stored OHLCV is preserved.";
        return plan.RegularHours ? "Half-open UTC buckets anchored at exchange session open"
            : "Half-open UTC buckets anchored at local midnight";
    }

    private static AlignmentPlan BuildPlan(TimeSeriesAlignmentRequest request)
    {
        var symbols = NormalizeNames(request.Symbols, 1, MaxSymbols, "symbols", HistoricalDataQueryService.IsSafeSymbolPathComponent);
        var providers = request.Providers is null or { Length: 0 } ? null :
            NormalizeNames(request.Providers, 2, MaxProviders, "providers", IsProviderId);
        if (request.FromDate is not { } from || request.ToDate is not { } to || from > to ||
            from.Year < 1900 || to.Year > 9998 || to.DayNumber - from.DayNumber >= MaxDays)
            throw new ArgumentException($"Choose an inclusive date range of at most {MaxDays} days.");

        var seconds = request.Interval?.ToUpperInvariant() switch
        {
            "SECOND1" => 1,
            "SECOND5" => 5,
            "SECOND10" => 10,
            "SECOND30" => 30,
            "MINUTE1" => 60,
            "MINUTE5" => 300,
            "MINUTE15" => 900,
            "MINUTE30" => 1800,
            "HOUR1" => 3600,
            "HOUR4" => 14400,
            "DAILY" => 86400,
            _ => throw new ArgumentException("Choose a supported alignment interval.")
        };
        var daily = seconds == 86400;
        var gap = ParsePolicy<GapPolicy>(request.GapStrategy, "gap strategy");
        var kind = ParsePolicy<DatasetKind>(request.InputKind, "input kind");
        if (kind == DatasetKind.Auto)
            kind = daily ? DatasetKind.DailyBars : DatasetKind.AggregateBars;
        if (kind == DatasetKind.DailyBars && !daily)
            throw new ArgumentException("Stored daily bars cannot be split into intraday intervals.");
        var adjustment = ParsePolicy<AdjustmentPolicy>(request.AdjustmentPolicy, "corporate-action policy");
        if (!string.Equals(request.Aggregation, "OHLCV", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Alignment supports OHLCV aggregation.");
        if (request.MaxGapIntervals is < 0 or > 100)
            throw new ArgumentException("Maximum gap length must be between 0 and 100 intervals.");

        var requestedFields = request.Fields ?? AllFields;
        if (requestedFields.Length is < 1 or > 5 ||
            requestedFields.Any(field => !AllFields.Contains(field, StringComparer.OrdinalIgnoreCase)) ||
            requestedFields.Distinct(StringComparer.OrdinalIgnoreCase).Count() != requestedFields.Length)
            throw new ArgumentException("Select one or more distinct OHLCV fields.");
        var fields = AllFields.Where(field => requestedFields.Contains(field, StringComparer.OrdinalIgnoreCase)).ToArray();
        var zone = ResolveZone(request.Timezone);
        var slots = BuildSlots(from, to, daily, TimeSpan.FromSeconds(seconds), zone, request.MarketHoursOnly,
            MaxOutputRows / (symbols.Length * (providers?.Length ?? 2)));
        if (slots.Count == 0)
            throw new ArgumentException("The selected calendar has no sessions in the requested dates.");

        return new(symbols, providers, from, to, CanonicalIntervalNames[seconds], kind, gap, request.MaxGapIntervals,
            zone, request.MarketHoursOnly, daily, adjustment, fields, slots);
    }

    private static readonly Dictionary<int, string> CanonicalIntervalNames = new()
    {
        [1] = "Second1",
        [5] = "Second5",
        [10] = "Second10",
        [30] = "Second30",
        [60] = "Minute1",
        [300] = "Minute5",
        [900] = "Minute15",
        [1800] = "Minute30",
        [3600] = "Hour1",
        [14400] = "Hour4",
        [86400] = "Daily"
    };

    private static T ParsePolicy<T>(string? value, string description) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var result) &&
        string.Equals(result.ToString(), value, StringComparison.OrdinalIgnoreCase)
            ? result : throw new ArgumentException($"Choose a supported {description}.");

    private static string[] NormalizeNames(string[]? values, int minimum, int maximum, string name, Func<string?, bool> isValid)
    {
        if (values is null || values.Length < minimum || values.Length > maximum)
            throw new ArgumentException($"Choose between {minimum} and {maximum} {name}.");
        var normalized = values.Select(value => value?.Trim()).ToArray();
        if (normalized.Any(value => !isValid(value)))
            throw new ArgumentException($"A selected {name} value is invalid.");
        var distinct = normalized.Select(value => value!.ToUpperInvariant()).Distinct().Order(StringComparer.Ordinal).ToArray();
        if (distinct.Length < minimum)
            throw new ArgumentException($"Choose at least {minimum} distinct {name}.");
        return distinct;
    }

    private static bool IsProviderId(string? value) => value is { Length: > 0 and <= 64 } &&
        value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.') &&
        !string.Equals(value, MarketDataSources.Unknown, StringComparison.OrdinalIgnoreCase);

    private static TimeZoneInfo ResolveZone(string? timezone)
    {
        var id = timezone switch
        {
            "US/Eastern" or "America/New_York" => "America/New_York",
            "US/Pacific" or "America/Los_Angeles" => "America/Los_Angeles",
            "Europe/London" => "Europe/London",
            "UTC" => "UTC",
            _ => throw new ArgumentException("Choose UTC, US Eastern, US Pacific or London time.")
        };
        return TimeZoneInfo.FindSystemTimeZoneById(id);
    }

    private static List<Slot> BuildSlots(DateOnly from, DateOnly to, bool daily, TimeSpan interval,
        TimeZoneInfo zone, bool regularHours, int limit)
    {
        var slots = new List<Slot>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            DateTimeOffset start;
            DateTimeOffset end;
            if (regularHours)
            {
                var regular = ((IOperationalTradingCalendar)UsCalendar).GetTradingSessions(date)
                    .FirstOrDefault(session => session.SessionType == "regular");
                if (regular is null)
                    continue;
                start = regular.OpenTime.ToUniversalTime();
                end = regular.CloseTime.ToUniversalTime();
            }
            else
            {
                start = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), zone);
                end = TimeZoneInfo.ConvertTimeToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue), zone);
            }

            for (var current = start; current < end;)
            {
                if (slots.Count >= limit)
                    throw new ArgumentException("The aligned grid is too large. Choose a coarser interval or narrow the scope.");
                var next = daily || end - current < interval ? end : current + interval;
                slots.Add(new(date, current, next));
                current = next;
            }
        }
        return slots;
    }

    private async Task<AlignmentInput> ReadInputAsync(AlignmentPlan plan, CancellationToken ct)
    {
        var records = new Dictionary<SeriesKey, List<Observation>>();
        var providers = new HashSet<string>(StringComparer.Ordinal);
        var dailySlots = plan.Daily
            ? plan.Slots.Select((slot, index) => (slot.Date, index)).ToDictionary(pair => pair.Date, pair => pair.index)
            : null;
        HashSet<MarketEventType> eventTypes = plan.InputKind switch
        {
            DatasetKind.DailyBars => [MarketEventType.HistoricalBar],
            DatasetKind.AggregateBars => [MarketEventType.AggregateBar],
            DatasetKind.LiveTrades => [MarketEventType.Trade],
            DatasetKind.HistoricalTrades => [MarketEventType.HistoricalTrade],
            _ => [MarketEventType.Trade, MarketEventType.HistoricalTrade]
        };
        // The outer partition range covers local-to-UTC and date-only bar labels.
        var from = new DateTimeOffset(plan.From.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var to = new DateTimeOffset(plan.To.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var outsideCalendar = 0;
        var archive = new JsonlMarketDataStore(storageOptions.RootPath);
        await foreach (var retained in archive.ReadAlignmentEventsAsync(
            plan.Symbols.ToHashSet(StringComparer.OrdinalIgnoreCase), eventTypes, storageOptions, from, to, ct).ConfigureAwait(false))
        {
            var evt = retained.Event;
            if (!IsProviderId(evt.Source))
                continue;

            var provider = evt.Source.ToUpperInvariant();
            if (plan.Providers is not null && !plan.Providers.Contains(provider, StringComparer.Ordinal))
                continue;

            var sample = ExtractObservation(retained);
            if (!MatchesAdjustment(plan.Adjustment, sample.Basis))
                continue;

            var slot = sample.SessionDate is { } date
                ? dailySlots!.GetValueOrDefault(date, -1)
                : FindSlot(plan.Slots, sample.Timestamp);
            if (slot < 0)
            {
                var calendarZone = plan.RegularHours ? ResolveZone("America/New_York") : plan.Zone;
                var eventDate = sample.SessionDate ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(sample.Timestamp, calendarZone).DateTime);
                if (eventDate >= plan.From && eventDate <= plan.To)
                    outsideCalendar++;
                continue;
            }
            if (sample.End is { } end && (end <= sample.Timestamp || end > plan.Slots[slot].End))
                throw new InvalidDataException("A stored aggregate spans an alignment boundary. Choose a compatible interval or trade input.");

            if (providers.Add(provider) && providers.Count > MaxProviders)
                throw new ArgumentException("More than eight providers match. Select the provider IDs to align.");
            var key = new SeriesKey(provider, evt.EffectiveSymbol.ToUpperInvariant());
            if (!records.TryGetValue(key, out var observations))
                records.Add(key, observations = []);
            observations.Add(sample with { Slot = slot, EventType = evt.Type });
        }
        return new(records, outsideCalendar);
    }

    private static Observation ExtractObservation(RetainedMarketEvent retained)
    {
        var evt = retained.Event;
        var id = retained.SourceId;
        return evt.Payload switch
        {
            HistoricalBar bar when evt.Type == MarketEventType.HistoricalBar => new(
                id, $"day:{bar.SessionDate.DayNumber}", bar.ToTimestampUtc(), null, bar.SessionDate, evt.Sequence,
                bar.IsAdjusted is true ? Basis.Adjusted : bar.IsAdjusted is false ? Basis.Raw : Basis.Unknown,
                bar.Open, bar.High, bar.Low, bar.Close, bar.Volume),
            AggregateBarPayload bar when evt.Type == MarketEventType.AggregateBar && bar.StartTime != default &&
                bar.Open > 0 && bar.Close > 0 && bar.High >= Math.Max(bar.Open, bar.Close) &&
                bar.Low > 0 && bar.Low <= Math.Min(bar.Open, bar.Close) && bar.Volume >= 0 => new(
                id, $"window:{bar.StartTime.UtcTicks}:{bar.EndTime.UtcTicks}", bar.StartTime, bar.EndTime, null,
                evt.Sequence, Basis.Unknown, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume),
            HistoricalTrade trade when evt.Type == MarketEventType.HistoricalTrade && trade.Timestamp != default => new(
                id, $"trade:{trade.Timestamp.UtcDateTime.Date.Ticks}:{trade.Exchange}:{trade.TradeId}",
                trade.Timestamp, null, null, evt.Sequence, Basis.Raw, trade.Price, trade.Price, trade.Price, trade.Price, trade.Size),
            Trade trade when evt.Type == MarketEventType.Trade && trade.Timestamp != default => new(
                id, trade.SequenceNumber > 0 ? $"trade:{trade.Timestamp.UtcTicks}:{trade.StreamId}:{trade.Venue}:{trade.SequenceNumber}" : id,
                trade.Timestamp, null, null, evt.Sequence, Basis.Raw, trade.Price, trade.Price, trade.Price, trade.Price, trade.Size),
            _ => throw new InvalidDataException("A selected archive payload has invalid market data or timestamps.")
        };
    }

    private static bool MatchesAdjustment(AdjustmentPolicy policy, Basis basis) => policy switch
    {
        AdjustmentPolicy.Raw => basis == Basis.Raw,
        AdjustmentPolicy.Adjusted => basis == Basis.Adjusted,
        _ => true
    };

    private static int FindSlot(List<Slot> slots, DateTimeOffset timestamp)
    {
        var lower = 0;
        var upper = slots.Count - 1;
        while (lower <= upper)
        {
            var middle = lower + (upper - lower) / 2;
            var slot = slots[middle];
            if (timestamp < slot.Start)
                upper = middle - 1;
            else if (timestamp >= slot.End)
                lower = middle + 1;
            else
                return middle;
        }
        return -1;
    }

    private static TimeSeriesAlignmentRow ToRow(AlignmentPlan plan, Slot slot, TimeSeriesAlignmentSeries series, AlignedValue? value)
    {
        return new(series.Provider, series.Symbol, slot.Date, TimeZoneInfo.ConvertTime(slot.Start, plan.Zone),
            series.AdjustmentBasis, value?.Status ?? "Missing",
            plan.Fields.Contains("Open") ? value?.Open : null,
            plan.Fields.Contains("High") ? value?.High : null,
            plan.Fields.Contains("Low") ? value?.Low : null,
            plan.Fields.Contains("Close") ? value?.Close : null,
            plan.Fields.Contains("Volume") ? value?.Volume : null,
            value?.SourceIds ?? []);
    }

    private static string ComputeHash(AlignmentPlan plan, TimeSeriesAlignmentSummary summary,
        List<TimeSeriesAlignmentRow> rows, CancellationToken ct)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hasher.AppendData(JsonSerializer.SerializeToUtf8Bytes(new { Version = 1, plan.From, plan.To, Summary = summary }, Json));
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            hasher.AppendData(JsonSerializer.SerializeToUtf8Bytes(row, Json));
        }
        return Convert.ToHexStringLower(hasher.GetHashAndReset());
    }

    private enum GapPolicy { ForwardFill, BackwardFill, LinearInterpolate, Null, Zero, Skip }
    private enum DatasetKind { Auto, DailyBars, AggregateBars, Trades, LiveTrades, HistoricalTrades }
    private enum AdjustmentPolicy { AsStored, Raw, Adjusted }
    private enum Basis { Unknown, Raw, Adjusted }

    private sealed record AlignmentPlan(string[] Symbols, string[]? Providers, DateOnly From, DateOnly To,
        string Interval, DatasetKind InputKind, GapPolicy Gap, int MaxGap, TimeZoneInfo Zone, bool RegularHours,
        bool Daily, AdjustmentPolicy Adjustment, string[] Fields, List<Slot> Slots);
    private sealed record Slot(DateOnly Date, DateTimeOffset Start, DateTimeOffset End);
    private sealed record SeriesKey(string Provider, string Symbol);
    private sealed record AlignmentInput(Dictionary<SeriesKey, List<Observation>> Records, int OutsideCalendar)
    {
        public IEnumerable<SeriesKey> Keys => Records.Keys;
    }
    private sealed record Observation(string SourceId, string DuplicateKey, DateTimeOffset Timestamp,
        DateTimeOffset? End, DateOnly? SessionDate, long Sequence, Basis Basis, decimal Open, decimal High,
        decimal Low, decimal Close, long Volume, int Slot = -1, MarketEventType EventType = MarketEventType.Unknown);
    private sealed record AlignedValue(decimal Open, decimal High, decimal Low, decimal Close, decimal Volume,
        string Status, string[] SourceIds);
    private sealed record AlignedSeries(TimeSeriesAlignmentSeries Summary, AlignedValue?[] Values);
}
