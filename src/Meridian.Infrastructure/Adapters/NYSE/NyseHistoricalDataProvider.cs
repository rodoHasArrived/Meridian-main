using System.Text.Json;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Utilities;
using Serilog;

namespace Meridian.Infrastructure.Adapters.NYSE;

/// <summary>
/// Historical and corporate-action data access for the NYSE Direct adapter (daily/adjusted bars,
/// intraday bars, dividends, splits). Extracted from <see cref="NYSEDataSource"/> so historical
/// fetch/parse logic is separated from the streaming lifecycle; it shares the same
/// <see cref="NyseAccessTokenProvider"/> for authentication. The owning <see cref="NYSEDataSource"/>
/// wraps each call in its resilience policies.
/// </summary>
internal sealed class NyseHistoricalDataProvider
{
    private readonly NyseAccessTokenProvider _auth;
    private readonly string _sourceId;
    private readonly ILogger _log;

    public NyseHistoricalDataProvider(NyseAccessTokenProvider auth, string sourceId, ILogger log)
    {
        _auth = auth;
        _sourceId = sourceId;
        _log = log;
    }

    public async Task<IReadOnlyList<AdjustedHistoricalBar>> FetchAdjustedDailyBarsAsync(
        string symbol,
        DateOnly? from,
        DateOnly? to,
        CancellationToken token)
    {
        await _auth.EnsureAuthenticatedAsync(token).ConfigureAwait(false);

        var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1));
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var url = $"/historical/bars/{symbol}?from={fromDate:yyyy-MM-dd}&to={toDate:yyyy-MM-dd}&adjusted=true";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        _auth.AddAuthHeader(request);

        using var httpClient = _auth.CreateHttpClient();
        using var response = await httpClient.SendAsync(request, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        var data = JsonSerializer.Deserialize<NYSEHistoricalBarsResponse>(json);

        if (data?.Bars == null || data.Bars.Count == 0)
        {
            _log.Information("No historical bars returned from NYSE for {Symbol}", symbol);
            return Array.Empty<AdjustedHistoricalBar>();
        }

        var bars = new List<AdjustedHistoricalBar>();

        foreach (var bar in data.Bars)
        {
            if (!ProviderDateParsing.TryParseProviderDate(bar.Date, out var sessionDate))
            {
                _log.Warning("Skipping NYSE bar for {Symbol} with unparseable date {Date}", symbol, bar.Date);
                continue;
            }

            try
            {
                bars.Add(new AdjustedHistoricalBar(
                    Symbol: symbol.ToUpperInvariant(),
                    SessionDate: sessionDate,
                    Open: bar.Open,
                    High: bar.High,
                    Low: bar.Low,
                    Close: bar.Close,
                    Volume: bar.Volume,
                    Source: _sourceId,
                    SequenceNumber: bar.SequenceNumber ?? 0,
                    AdjustedOpen: bar.AdjustedOpen,
                    AdjustedHigh: bar.AdjustedHigh,
                    AdjustedLow: bar.AdjustedLow,
                    AdjustedClose: bar.AdjustedClose,
                    AdjustedVolume: bar.AdjustedVolume,
                    SplitFactor: bar.SplitFactor,
                    DividendAmount: bar.DividendAmount
                ));
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to parse NYSE bar for {Symbol} on {Date}", symbol, bar.Date);
            }
        }

        _log.Information("Fetched {Count} historical bars from NYSE for {Symbol}", bars.Count, symbol);
        return bars.OrderBy(b => b.SessionDate).ToArray();
    }

    public async Task<IReadOnlyList<IntradayBar>> FetchIntradayBarsAsync(
        string symbol,
        string interval,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken token)
    {
        await _auth.EnsureAuthenticatedAsync(token).ConfigureAwait(false);

        var fromTime = from ?? DateTimeOffset.UtcNow.AddDays(-5);
        var toTime = to ?? DateTimeOffset.UtcNow;

        var url = $"/historical/intraday/{symbol}?from={fromTime:O}&to={toTime:O}&interval={interval}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        _auth.AddAuthHeader(request);

        using var httpClient = _auth.CreateHttpClient();
        using var response = await httpClient.SendAsync(request, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        var data = JsonSerializer.Deserialize<NYSEIntradayBarsResponse>(json);

        if (data?.Bars == null || data.Bars.Count == 0)
        {
            return Array.Empty<IntradayBar>();
        }

        return data.Bars.Select(bar => new IntradayBar(
            Symbol: symbol.ToUpperInvariant(),
            Timestamp: DateTimeOffset.Parse(bar.Timestamp),
            Interval: interval,
            Open: bar.Open,
            High: bar.High,
            Low: bar.Low,
            Close: bar.Close,
            Volume: bar.Volume,
            Source: _sourceId,
            TradeCount: bar.TradeCount,
            VWAP: bar.Vwap
        )).OrderBy(b => b.Timestamp).ToArray();
    }

    public async Task<IReadOnlyList<DividendInfo>> FetchDividendsAsync(
        string symbol,
        DateOnly? from,
        DateOnly? to,
        CancellationToken token)
    {
        await _auth.EnsureAuthenticatedAsync(token).ConfigureAwait(false);

        var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-5));
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var url = $"/corporate-actions/dividends/{symbol}?from={fromDate:yyyy-MM-dd}&to={toDate:yyyy-MM-dd}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        _auth.AddAuthHeader(request);

        using var httpClient = _auth.CreateHttpClient();
        using var response = await httpClient.SendAsync(request, token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _log.Warning("NYSE returned {Status} for dividends request for {Symbol}", response.StatusCode, symbol);
            return Array.Empty<DividendInfo>();
        }

        var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        var data = JsonSerializer.Deserialize<NYSEDividendsResponse>(json);

        if (data?.Dividends == null || data.Dividends.Count == 0)
        {
            return Array.Empty<DividendInfo>();
        }

        var dividends = new List<DividendInfo>();
        foreach (var div in data.Dividends)
        {
            if (!ProviderDateParsing.TryParseProviderDate(div.ExDate, out var exDate))
            {
                _log.Warning("Skipping NYSE dividend for {Symbol} with unparseable ex-date {ExDate}", symbol, div.ExDate);
                continue;
            }

            dividends.Add(new DividendInfo(
                Symbol: symbol.ToUpperInvariant(),
                ExDate: exDate,
                PaymentDate: ProviderDateParsing.ParseProviderDateOrNull(div.PaymentDate),
                RecordDate: ProviderDateParsing.ParseProviderDateOrNull(div.RecordDate),
                Amount: div.Amount,
                Currency: div.Currency ?? "USD",
                Type: ParseDividendType(div.Type),
                Source: _sourceId
            ));
        }

        return dividends.OrderBy(d => d.ExDate).ToArray();
    }

    public async Task<IReadOnlyList<SplitInfo>> FetchSplitsAsync(
        string symbol,
        DateOnly? from,
        DateOnly? to,
        CancellationToken token)
    {
        await _auth.EnsureAuthenticatedAsync(token).ConfigureAwait(false);

        var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-10));
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var url = $"/corporate-actions/splits/{symbol}?from={fromDate:yyyy-MM-dd}&to={toDate:yyyy-MM-dd}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        _auth.AddAuthHeader(request);

        using var httpClient = _auth.CreateHttpClient();
        using var response = await httpClient.SendAsync(request, token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _log.Warning("NYSE returned {Status} for splits request for {Symbol}", response.StatusCode, symbol);
            return Array.Empty<SplitInfo>();
        }

        var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        var data = JsonSerializer.Deserialize<NYSESplitsResponse>(json);

        if (data?.Splits == null || data.Splits.Count == 0)
        {
            return Array.Empty<SplitInfo>();
        }

        var splits = new List<SplitInfo>();
        foreach (var split in data.Splits)
        {
            if (!ProviderDateParsing.TryParseProviderDate(split.ExDate, out var exDate))
            {
                _log.Warning("Skipping NYSE split for {Symbol} with unparseable ex-date {ExDate}", symbol, split.ExDate);
                continue;
            }

            splits.Add(new SplitInfo(
                Symbol: symbol.ToUpperInvariant(),
                ExDate: exDate,
                SplitFrom: split.SplitFrom,
                SplitTo: split.SplitTo,
                Source: _sourceId
            ));
        }

        return splits.OrderBy(s => s.ExDate).ToArray();
    }

    private static DividendType ParseDividendType(string? type) => type?.ToLowerInvariant() switch
    {
        "special" => DividendType.Special,
        "return" => DividendType.Return,
        "liquidation" => DividendType.Liquidation,
        _ => DividendType.Regular
    };
}
