using System.Threading;
using Meridian.Core.Logging;
using Meridian.Infrastructure.Adapters.Core;
using Serilog;

namespace Meridian.Application.Accounting;

/// <summary>
/// Marks positions at the most recent daily close on or before the valuation date,
/// sourced from the provider chain (typically the composite failover provider).
/// Weekends and holidays fall back to the latest prior session within the lookback window.
/// </summary>
public sealed class HistoricalCloseMarkPriceSource : IMarkPriceSource
{
    private const int LookbackDays = 7;

    private readonly IHistoricalDataProvider _provider;
    private readonly ILogger _log;

    public HistoricalCloseMarkPriceSource(IHistoricalDataProvider provider, ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _log = log ?? LoggingSetup.ForContext<HistoricalCloseMarkPriceSource>();
    }

    public async Task<MarkPriceQuote?> GetMarkPriceAsync(string symbol, DateOnly asOf, CancellationToken ct = default)
    {
        try
        {
            var bars = await _provider
                .GetDailyBarsAsync(symbol, asOf.AddDays(-LookbackDays), asOf, ct)
                .ConfigureAwait(false);

            var bar = bars
                .Where(b => b.SessionDate <= asOf)
                .OrderByDescending(b => b.SessionDate)
                .FirstOrDefault();
            if (bar is null)
            {
                _log.Warning("No daily close found for {Symbol} on or before {AsOf}", symbol, asOf);
                return null;
            }

            var source = string.IsNullOrWhiteSpace(bar.Source) ? _provider.Name : bar.Source;
            return new MarkPriceQuote(
                bar.Close,
                source,
                FormattableString.Invariant($"daily-close:{symbol}:{bar.SessionDate:yyyy-MM-dd}:{source}"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Mark price lookup failed for {Symbol} as of {AsOf}", symbol, asOf);
            return null;
        }
    }
}
