using System.Text.Json;
using Meridian.Application.Services;

namespace Meridian.QuantScript.API;

/// <summary>
/// Live implementation of <see cref="IQuantDataContext"/> backed by the local storage layer.
/// </summary>
public sealed class QuantDataContext : IQuantDataContext
{
    private readonly HistoricalDataQueryService _queryService;
    private readonly ILogger<QuantDataContext> _logger;

    public QuantDataContext(
        HistoricalDataQueryService queryService,
        ILogger<QuantDataContext> logger)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<PriceSeries> GetPricesAsync(
        string symbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        _logger.LogInformation(
            "Loading prices for {Symbol} from {From:d} to {To:d}", symbol, from, to);

        var query = new HistoricalDataQuery(
            Symbol: symbol,
            From: DateOnly.FromDateTime(from),
            To: DateOnly.FromDateTime(to),
            DataType: "bars");

        var result = await _queryService.QueryAsync(query, ct).ConfigureAwait(false);

        var bars = new List<PriceBar>(result.Records.Count);
        foreach (var record in result.Records)
        {
            if (string.IsNullOrEmpty(record.RawJson))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(record.RawJson);
                var root = doc.RootElement;

                var timestamp = record.Timestamp.UtcDateTime;
                var open   = root.TryGetProperty("open",   out var o) ? o.GetDouble() : 0d;
                var high   = root.TryGetProperty("high",   out var h) ? h.GetDouble() : 0d;
                var low    = root.TryGetProperty("low",    out var l) ? l.GetDouble() : 0d;
                var close  = root.TryGetProperty("close",  out var c) ? c.GetDouble() : 0d;
                var volume = root.TryGetProperty("volume", out var v) ? v.GetInt64()  : 0L;

                if (close > 0)
                    bars.Add(new PriceBar(timestamp, open, high, low, close, volume));
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Skipping malformed record in {File} line {Line}",
                    record.SourceFile, record.LineNumber);
            }
        }

        _logger.LogInformation("Loaded {Count} bars for {Symbol}", bars.Count, symbol);
        return new PriceSeries(symbol, "1d", bars);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScriptOrderBook>> GetOrderBookAsync(
        string symbol,
        CancellationToken ct = default)
    {
        // Order book snapshots are not yet persisted in the local storage layer.
        IReadOnlyList<ScriptOrderBook> empty = Array.Empty<ScriptOrderBook>();
        return Task.FromResult(empty);
    }
}

