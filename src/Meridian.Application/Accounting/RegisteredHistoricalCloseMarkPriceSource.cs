using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Application.Accounting;

/// <summary>
/// Resolves closing marks from the live provider registry in priority order. The
/// registry remains the routing authority; an individual provider failure or empty
/// history falls through to the next enabled historical provider.
/// </summary>
public sealed class RegisteredHistoricalCloseMarkPriceSource : IMarkPriceSource
{
    private readonly ProviderRegistry _registry;

    public RegisteredHistoricalCloseMarkPriceSource(ProviderRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async Task<MarkPriceQuote?> GetMarkPriceAsync(
        string symbol,
        DateOnly asOf,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        foreach (var provider in _registry.GetBackfillProviders())
        {
            ct.ThrowIfCancellationRequested();
            var quote = await new HistoricalCloseMarkPriceSource(provider)
                .GetMarkPriceAsync(symbol, asOf, ct)
                .ConfigureAwait(false);
            if (quote is not null)
            {
                return quote;
            }
        }

        return null;
    }
}
