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

        var query = new Meridian.Contracts.Store.MarketDataQuery
        {
            Symbol = symbol,
            From = from,
            To = to,
            DataType = "bars"
        };

        var result = await _queryService.QueryAsync(query, ct).ConfigureAwait(false);

        var bars = result.Bars
            .Select(b => new PriceBar(
                b.Timestamp,
                (double)b.Open,
                (double)b.High,
                (double)b.Low,
                (double)b.Close,
                (long)b.Volume))
            .ToList();

        _logger.LogInformation(
            "Loaded {Count} bars for {Symbol}", bars.Count, symbol);

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
