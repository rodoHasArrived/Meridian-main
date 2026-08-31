using System.Threading;
using Meridian.Contracts.Operations;
using Meridian.Core.Logging;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Ledger;
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
            var provenance = ResolveProvenance(source);
            return new MarkPriceQuote(
                bar.Close,
                source,
                FormattableString.Invariant($"daily-close:{symbol}:{bar.SessionDate:yyyy-MM-dd}:{source}"),
                // A quoted exchange close for the identical instrument is an ASC 820 Level 1 input.
                // A fabricated close is not an observation of anything, so it is classified at the
                // unobservable tier instead of inheriting Level 1 from the shape of the request.
                provenance.IsNonReal() ? FairValueLevel.Level3 : FairValueLevel.Level1,
                bar.SessionDate,
                Provenance: provenance);
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

    /// <summary>
    /// Classifies where a close actually came from. The provider's own
    /// <see cref="IHistoricalDataProvider.IsSimulated"/> declaration is the primary gate; the bar's
    /// <c>Source</c> tag is the second, and is what catches an aggregator such as
    /// <see cref="CompositeHistoricalDataProvider"/> — not simulated itself — serving a fabricated
    /// bar from a constituent provider. Both use the shared structured token table, so a real
    /// vendor named "Sample Custodian" is not mistaken for a simulated origin.
    /// </summary>
    private DataProvenance ResolveProvenance(string source)
        => _provider.IsSimulated || DataProvenanceExtensions.IsSimulatedOriginToken(source)
            ? DataProvenance.Simulated
            : DataProvenance.Real;
}
