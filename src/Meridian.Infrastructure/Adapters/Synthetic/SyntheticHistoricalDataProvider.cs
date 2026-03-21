using System.Globalization;
using Meridian.Application.Config;
using Meridian.Application.Subscriptions.Models;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.DataSources;

namespace Meridian.Infrastructure.Adapters.Synthetic;

public sealed class SyntheticHistoricalDataProvider : IHistoricalDataProvider, ICorporateActionSource
{
    private readonly SyntheticMarketDataConfig _config;

    public SyntheticHistoricalDataProvider(SyntheticMarketDataConfig? config = null)
    {
        _config = config ?? new SyntheticMarketDataConfig(Enabled: true);
    }

    public string Name => "synthetic";
    public string DisplayName => "Synthetic Historical Provider";
    public string Description => "Deterministic historical bars, quotes, trades, auctions, and corporate actions for offline development.";
    public int Priority => Math.Max(0, _config.Priority);
    public TimeSpan RateLimitDelay => TimeSpan.Zero;
    public HistoricalDataCapabilities Capabilities => HistoricalDataCapabilities.FullFeatured;

    public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(_config.Enabled);

    public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<HistoricalBar>>(BuildAdjustedBars(symbol, from, to)
            .Select(bar => bar.ToHistoricalBar(preferAdjusted: true))
            .ToArray());

    public Task<IReadOnlyList<AdjustedHistoricalBar>> GetAdjustedDailyBarsAsync(string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AdjustedHistoricalBar>>(BuildAdjustedBars(symbol, from, to));

    public Task<HistoricalQuotesResult> GetHistoricalQuotesAsync(string symbol, DateTimeOffset start, DateTimeOffset end, int? limit = null, CancellationToken ct = default)
        => Task.FromResult(BuildHistoricalQuotes(new[] { symbol }, start, end, limit));

    public Task<HistoricalQuotesResult> GetHistoricalQuotesAsync(IEnumerable<string> symbols, DateTimeOffset start, DateTimeOffset end, int? limit = null, CancellationToken ct = default)
        => Task.FromResult(BuildHistoricalQuotes(symbols, start, end, limit));

    public Task<HistoricalTradesResult> GetHistoricalTradesAsync(string symbol, DateTimeOffset start, DateTimeOffset end, int? limit = null, CancellationToken ct = default)
        => Task.FromResult(BuildHistoricalTrades(new[] { symbol }, start, end, limit));

    public Task<HistoricalTradesResult> GetHistoricalTradesAsync(IEnumerable<string> symbols, DateTimeOffset start, DateTimeOffset end, int? limit = null, CancellationToken ct = default)
        => Task.FromResult(BuildHistoricalTrades(symbols, start, end, limit));

    public Task<HistoricalAuctionsResult> GetHistoricalAuctionsAsync(string symbol, DateOnly start, DateOnly end, CancellationToken ct = default)
        => Task.FromResult(BuildHistoricalAuctions(new[] { symbol }, start, end));

    public Task<HistoricalAuctionsResult> GetHistoricalAuctionsAsync(IEnumerable<string> symbols, DateOnly start, DateOnly end, CancellationToken ct = default)
        => Task.FromResult(BuildHistoricalAuctions(symbols, start, end));

    public Task<IReadOnlyList<DividendInfo>> GetDividendsAsync(string symbol, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        var profile = SyntheticReferenceDataCatalog.GetProfileOrDefault(symbol);
        var dividends = profile.Dividends
            .Where(d => (!from.HasValue || d.ExDate >= from.Value) && (!to.HasValue || d.ExDate <= to.Value))
            .ToArray();
        return Task.FromResult<IReadOnlyList<DividendInfo>>(dividends);
    }

    public Task<IReadOnlyList<SplitInfo>> GetSplitsAsync(string symbol, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        var profile = SyntheticReferenceDataCatalog.GetProfileOrDefault(symbol);
        var splits = profile.Splits
            .Where(s => (!from.HasValue || s.ExDate >= from.Value) && (!to.HasValue || s.ExDate <= to.Value))
            .ToArray();
        return Task.FromResult<IReadOnlyList<SplitInfo>>(splits);
    }

    private AdjustedHistoricalBar[] BuildAdjustedBars(string symbol, DateOnly? from, DateOnly? to)
    {
        var profile = SyntheticReferenceDataCatalog.GetProfileOrDefault(symbol);
        var startDate = from ?? _config.DefaultHistoryStart ?? new DateOnly(2024, 1, 2);
        var endDate = to ?? _config.DefaultHistoryEnd ?? new DateOnly(2024, 12, 31);
        if (endDate < startDate)
            return Array.Empty<AdjustedHistoricalBar>();

        var sessionDates = EnumerateSessions(startDate, endDate).ToArray();
        if (sessionDates.Length == 0)
            return Array.Empty<AdjustedHistoricalBar>();

        var splitMap = profile.Splits.ToDictionary(s => s.ExDate, s => s.SplitRatio);
        var dividendMap = profile.Dividends.ToDictionary(d => d.ExDate, d => d.Amount);
        var bars = new List<AdjustedHistoricalBar>(sessionDates.Length);
        var cumulativeSplitFactor = 1m;

        for (var i = 0; i < sessionDates.Length; i++)
        {
            var sessionDate = sessionDates[i];
            if (splitMap.TryGetValue(sessionDate, out var ratio) && ratio > 0)
                cumulativeSplitFactor *= ratio;

            var referenceClose = ComputeReferenceClose(profile, sessionDate, i);
            var referenceOpen = Round4(referenceClose * (1m + Noise(profile.Symbol, i, 11, 0.008m)));
            var referenceCloseWithNoise = Round4(referenceClose * (1m + Noise(profile.Symbol, i, 13, 0.009m)));
            var referenceHigh = Round4(Math.Max(referenceOpen, referenceCloseWithNoise) * (1m + PositiveNoise(profile.Symbol, i, 17, 0.007m)));
            var referenceLow = Round4(Math.Min(referenceOpen, referenceCloseWithNoise) * (1m - PositiveNoise(profile.Symbol, i, 19, 0.007m)));
            var adjustedVolume = Math.Max(1L, (long)Math.Round(profile.AverageDailyVolume * (0.65m + PositiveNoise(profile.Symbol, i, 23, 0.70m)), MidpointRounding.AwayFromZero));
            dividendMap.TryGetValue(sessionDate, out var dividendAmount);

            var open = Round2(referenceOpen / cumulativeSplitFactor);
            var high = Round2(referenceHigh / cumulativeSplitFactor);
            var low = Round2(referenceLow / cumulativeSplitFactor);
            var close = Round2(referenceCloseWithNoise / cumulativeSplitFactor);
            var volume = Math.Max(1L, (long)Math.Round(adjustedVolume * cumulativeSplitFactor, MidpointRounding.AwayFromZero));

            var adjustedOpen = Round4(referenceOpen + dividendAmount);
            var adjustedHigh = Round4(referenceHigh + dividendAmount);
            var adjustedLow = Round4(referenceLow + dividendAmount);
            var adjustedClose = Round4(referenceCloseWithNoise + dividendAmount);

            bars.Add(new AdjustedHistoricalBar(
                Symbol: profile.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase) ? profile.Symbol : symbol.ToUpperInvariant(),
                SessionDate: sessionDate,
                Open: open,
                High: high,
                Low: low,
                Close: close,
                Volume: volume,
                Source: Name,
                SequenceNumber: i + 1,
                AdjustedOpen: adjustedOpen,
                AdjustedHigh: adjustedHigh,
                AdjustedLow: adjustedLow,
                AdjustedClose: adjustedClose,
                AdjustedVolume: adjustedVolume,
                SplitFactor: splitMap.TryGetValue(sessionDate, out var splitRatio) ? splitRatio : null,
                DividendAmount: dividendAmount > 0 ? dividendAmount : null));
        }

        return bars.ToArray();
    }

    private HistoricalQuotesResult BuildHistoricalQuotes(IEnumerable<string> symbols, DateTimeOffset start, DateTimeOffset end, int? limit)
    {
        var quotes = new List<HistoricalQuote>();
        foreach (var symbol in symbols.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var profile = SyntheticReferenceDataCatalog.GetProfileOrDefault(symbol);
            var intervals = Math.Max(4, _config.HistoricalQuoteDensityPerDay);
            foreach (var point in EnumerateIntradayPoints(start, end, intervals))
            {
                var anchor = ComputeIntradayAnchorPrice(profile, point);
                var spreadBps = 0.6m + PositiveNoise(profile.Symbol, point.DayOfYear, point.Minute, 0.8m);
                var spread = Round4(anchor * spreadBps / 10_000m);
                var bid = Round4(anchor - spread / 2m);
                var ask = Round4(anchor + spread / 2m);
                var bidSize = 100L * (1 + (long)(PositiveNoise(profile.Symbol, point.DayOfYear, point.Minute + 1, 25m)));
                var askSize = 100L * (1 + (long)(PositiveNoise(profile.Symbol, point.DayOfYear, point.Minute + 2, 25m)));
                quotes.Add(new HistoricalQuote(profile.Symbol, point, profile.Exchange, ask, askSize, profile.Exchange, bid, bidSize,
                    Conditions: new[] { "R" }, Tape: ResolveTape(profile.Exchange), Source: Name, SequenceNumber: quotes.Count + 1));
            }
        }

        var ordered = ApplyLimit(quotes.OrderBy(q => q.Timestamp).ThenBy(q => q.Symbol, StringComparer.OrdinalIgnoreCase), limit).ToArray();
        return new HistoricalQuotesResult(ordered, TotalCount: ordered.Length);
    }

    private HistoricalTradesResult BuildHistoricalTrades(IEnumerable<string> symbols, DateTimeOffset start, DateTimeOffset end, int? limit)
    {
        var trades = new List<HistoricalTrade>();
        foreach (var symbol in symbols.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var profile = SyntheticReferenceDataCatalog.GetProfileOrDefault(symbol);
            var intervals = Math.Max(4, _config.HistoricalTradeDensityPerDay);
            var index = 0;
            foreach (var point in EnumerateIntradayPoints(start, end, intervals))
            {
                var anchor = ComputeIntradayAnchorPrice(profile, point);
                var price = Round4(anchor * (1m + Noise(profile.Symbol, point.DayOfYear, index, 0.0009m)));
                var size = 25L * (1 + (long)(PositiveNoise(profile.Symbol, point.DayOfYear, index + 7, 80m)));
                var conditions = index % 18 == 0 ? new[] { "I", "T" } : new[] { "@" };
                trades.Add(new HistoricalTrade(
                    profile.Symbol,
                    point,
                    profile.Exchange,
                    price,
                    size,
                    TradeId: $"{profile.Symbol}-{point:yyyyMMddHHmmss}-{index.ToString(CultureInfo.InvariantCulture)}",
                    Conditions: conditions,
                    Tape: ResolveTape(profile.Exchange),
                    Source: Name,
                    SequenceNumber: trades.Count + 1));
                index++;
            }
        }

        var ordered = ApplyLimit(trades.OrderBy(t => t.Timestamp).ThenBy(t => t.Symbol, StringComparer.OrdinalIgnoreCase), limit).ToArray();
        return new HistoricalTradesResult(ordered, TotalCount: ordered.Length);
    }

    private HistoricalAuctionsResult BuildHistoricalAuctions(IEnumerable<string> symbols, DateOnly start, DateOnly end)
    {
        var auctions = new List<HistoricalAuction>();
        foreach (var symbol in symbols.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var profile = SyntheticReferenceDataCatalog.GetProfileOrDefault(symbol);
            var sessionIndex = 0;
            foreach (var sessionDate in EnumerateSessions(start, end))
            {
                var reference = ComputeReferenceClose(profile, sessionDate, sessionIndex++);
                var openTs = new DateTimeOffset(sessionDate.ToDateTime(new TimeOnly(9, 30), DateTimeKind.Utc));
                var closeTs = new DateTimeOffset(sessionDate.ToDateTime(new TimeOnly(16, 0), DateTimeKind.Utc));
                auctions.Add(new HistoricalAuction(
                    profile.Symbol,
                    sessionDate,
                    OpeningAuctions: new[]
                    {
                        new AuctionPrice(openTs, Round4(reference * (1m - 0.0015m)), Math.Max(10_000, profile.AverageDailyVolume / 140), profile.Exchange, "OPG")
                    },
                    ClosingAuctions: new[]
                    {
                        new AuctionPrice(closeTs, Round4(reference * (1m + 0.0012m)), Math.Max(10_000, profile.AverageDailyVolume / 95), profile.Exchange, "CLS")
                    },
                    Source: Name,
                    SequenceNumber: auctions.Count + 1));
            }
        }

        return new HistoricalAuctionsResult(auctions, TotalCount: auctions.Count);
    }

    private static IEnumerable<DateOnly> EnumerateSessions(DateOnly start, DateOnly end)
    {
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                yield return date;
        }
    }

    private static IEnumerable<DateTimeOffset> EnumerateIntradayPoints(DateTimeOffset start, DateTimeOffset end, int densityPerDay)
    {
        var currentDate = DateOnly.FromDateTime(start.UtcDateTime.Date);
        var endDate = DateOnly.FromDateTime(end.UtcDateTime.Date);

        while (currentDate <= endDate)
        {
            if (currentDate.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                var open = new DateTimeOffset(currentDate.ToDateTime(new TimeOnly(9, 30), DateTimeKind.Utc));
                var close = new DateTimeOffset(currentDate.ToDateTime(new TimeOnly(16, 0), DateTimeKind.Utc));
                var step = TimeSpan.FromMinutes(Math.Max(1, 390.0 / densityPerDay));
                for (var ts = open; ts <= close; ts += step)
                {
                    if (ts >= start && ts <= end)
                        yield return ts;
                }
            }

            currentDate = currentDate.AddDays(1);
        }
    }

    private static decimal ComputeReferenceClose(SyntheticSymbolProfile profile, DateOnly sessionDate, int sessionIndex)
    {
        var days = sessionDate.DayNumber - new DateOnly(2024, 1, 2).DayNumber;
        var drift = 1m + days * 0.00035m * Math.Max(0.45m, profile.Beta);
        var seasonal = 1m + (decimal)Math.Sin(days / 18d) * 0.018m + (decimal)Math.Cos(days / 47d) * 0.011m;
        var noise = 1m + Noise(profile.Symbol, sessionIndex, sessionDate.DayOfYear, 0.020m);
        return Round4(Math.Max(5m, profile.BasePrice * drift * seasonal * noise));
    }

    private static decimal ComputeIntradayAnchorPrice(SyntheticSymbolProfile profile, DateTimeOffset point)
    {
        var sessionDate = DateOnly.FromDateTime(point.UtcDateTime.Date);
        var baseClose = ComputeReferenceClose(profile, sessionDate, sessionDate.DayOfYear);
        var minutesSinceOpen = (decimal)(point.TimeOfDay - new TimeSpan(9, 30, 0)).TotalMinutes;
        var intradayWave = (decimal)Math.Sin((double)(minutesSinceOpen / 390m) * Math.PI * 2d) * 0.0025m;
        var microNoise = Noise(profile.Symbol, point.DayOfYear, point.Minute, 0.0018m);
        return Round4(baseClose * (1m + intradayWave + microNoise));
    }

    private static IEnumerable<T> ApplyLimit<T>(IEnumerable<T> values, int? limit)
        => limit is > 0 ? values.Take(limit.Value) : values;

    private static decimal Noise(string symbol, int a, int b, decimal amplitude)
    {
        var value = StableUnit(symbol, a, b);
        return ((decimal)value * 2m - 1m) * amplitude;
    }

    private static decimal PositiveNoise(string symbol, int a, int b, decimal amplitude)
        => ((decimal)StableUnit(symbol, a, b) + 0.15m) * amplitude;

    private static double StableUnit(string symbol, int a, int b)
    {
        var hash = HashCode.Combine(symbol.ToUpperInvariant(), a, b);
        var normalized = (hash & 0x7fffffff) / (double)int.MaxValue;
        return normalized;
    }

    private static string ResolveTape(string exchange)
        => exchange switch
        {
            "XNYS" => "A",
            "ARCX" => "B",
            _ => "C"
        };

    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    private static decimal Round4(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}
