using Meridian.Contracts.SecurityMaster;

namespace Meridian.QuantScript.Tests.Helpers;

/// <summary>
/// In-memory <see cref="IQuantDataContext"/> that returns synthetic price bars.
/// </summary>
public sealed class FakeQuantDataContext : IQuantDataContext
{
    private readonly Func<string, DateOnly, DateOnly, PriceSeries>? _factory;

    public FakeQuantDataContext(Func<string, DateOnly, DateOnly, PriceSeries>? factory = null)
    {
        _factory = factory;
    }

    public Task<PriceSeries> PricesAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var series = _factory is not null
            ? _factory(symbol, from, to)
            : TestPriceSeriesBuilder.Build(symbol, barCount: 30, startDate: from);

        return Task.FromResult(series);
    }

    public Task<PriceSeries> PricesAsync(string symbol, DateOnly from, DateOnly to, string? provider, CancellationToken ct = default)
        => PricesAsync(symbol, from, to, ct);

    public Task<IReadOnlyList<ScriptTrade>> TradesAsync(string symbol, DateOnly date, CancellationToken ct = default)
    {
        IReadOnlyList<ScriptTrade> empty = Array.Empty<ScriptTrade>();
        return Task.FromResult(empty);
    }

    public Task<ScriptOrderBook?> OrderBookAsync(string symbol, DateTimeOffset timestamp, CancellationToken ct = default)
        => Task.FromResult<ScriptOrderBook?>(null);

    public Task<SecurityDetailDto?> SecMasterAsync(string symbol, CancellationToken ct = default)
        => Task.FromResult<SecurityDetailDto?>(null);

    public Task<IReadOnlyList<CorporateActionDto>> CorporateActionsAsync(string symbol, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CorporateActionDto>>([]);
}
