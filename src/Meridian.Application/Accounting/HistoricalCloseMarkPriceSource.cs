using System.Threading;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
using Meridian.Core.Logging;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Ledger;
using Serilog;

namespace Meridian.Application.Accounting;

/// <summary>
/// Marks positions at the most recent daily close on or before the valuation date,
/// sourced only from an explicit raw accounting-close capability, never generic research bars.
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
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ct.ThrowIfCancellationRequested();
        try
        {
            var from = asOf.AddDays(-Math.Min(LookbackDays, asOf.DayNumber));
            var close = await _provider
                .GetRawAccountingCloseAsync(symbol, from, asOf, ct)
                .ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (close is null)
            {
                _log.Warning("No raw accounting close found for {Symbol} on or before {AsOf}", symbol, asOf);
                return null;
            }

            if (close.PriceBasis != AccountingClosePriceBasis.Raw || close.Close <= 0m
                || !string.Equals(close.Symbol, symbol.Trim(), StringComparison.OrdinalIgnoreCase)
                || close.SessionDate < from || close.SessionDate > asOf
                || string.IsNullOrWhiteSpace(close.Provider) || string.IsNullOrWhiteSpace(close.Feed)
                || !Sha256Digest.IsCanonical(close.SourceContentHash))
            {
                _log.Warning("Raw accounting close evidence is invalid for {Symbol} on or before {AsOf}", symbol, asOf);
                return null;
            }

            var source = close.Provider;
            var provenance = ResolveProvenance(source);
            return new MarkPriceQuote(
                close.Close,
                source,
                close.ToEvidenceReference(),
                // A quoted exchange close for the identical instrument is an ASC 820 Level 1 input.
                // A fabricated close is not an observation of anything, so it is classified at the
                // unobservable tier instead of inheriting Level 1 from the shape of the request.
                provenance.IsNonReal() ? FairValueLevel.Level3 : FairValueLevel.Level1,
                close.SessionDate,
                Provenance: provenance);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
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
    /// <see cref="IHistoricalDataProvider.IsSimulated"/> declaration is the primary gate; the close's
    /// actual serving provider is the second, and is what catches an aggregator such as
    /// <see cref="CompositeHistoricalDataProvider"/> — not simulated itself — serving a fabricated
    /// close from a constituent provider. Both use the shared structured token table, so a real
    /// vendor named "Sample Custodian" is not mistaken for a simulated origin.
    /// </summary>
    private DataProvenance ResolveProvenance(string source)
        => _provider.IsSimulated || DataProvenanceExtensions.IsSimulatedOriginToken(source)
            ? DataProvenance.Simulated
            : DataProvenance.Real;
}
