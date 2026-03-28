namespace Meridian.QuantScript.Tests.Helpers;

/// <summary>
/// In-memory <see cref="IQuantDataContext"/> that returns synthetic price bars.
/// </summary>
public sealed class FakeQuantDataContext : IQuantDataContext
{
    private readonly Func<string, DateTime, DateTime, PriceSeries>? _factory;

    public FakeQuantDataContext(Func<string, DateTime, DateTime, PriceSeries>? factory = null)
    {
        _factory = factory;
    }

    public Task<PriceSeries> GetPricesAsync(string symbol, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var series = _factory is not null
            ? _factory(symbol, from, to)
            : TestPriceSeriesBuilder.Build(symbol, barCount: 30, startDate: from);

        return Task.FromResult(series);
    }

    public Task<IReadOnlyList<ScriptOrderBook>> GetOrderBookAsync(string symbol, CancellationToken ct = default)
    {
        IReadOnlyList<ScriptOrderBook> empty = Array.Empty<ScriptOrderBook>();
        return Task.FromResult(empty);
    }
}
