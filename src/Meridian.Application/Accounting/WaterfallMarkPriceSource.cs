using System.Threading;
using Meridian.Contracts.Operations;
using Meridian.Core.Logging;
using Meridian.Ledger;
using Serilog;

namespace Meridian.Application.Accounting;

/// <summary>
/// One priority tier in a price-source waterfall: a mark-price source, the ASC 820 fair-value
/// level that prices from it should carry, and a human-readable label for evidence and logging.
/// </summary>
/// <param name="Source">The underlying mark-price source (e.g. exchange close, vendor composite, matrix model, manual).</param>
/// <param name="Level">Fair-value level stamped on quotes from this tier when the tier's source did not classify the price itself.</param>
/// <param name="Label">Descriptive tier label (e.g. "exchange-close", "vendor-composite", "matrix-model", "manual").</param>
public sealed record PriceSourceTier(IMarkPriceSource Source, FairValueLevel Level, string Label);

/// <summary>
/// Resolves a mark price by walking an ordered list of <see cref="PriceSourceTier"/> and returning
/// the first tier that yields a price — the price-source selection waterfall (exchange close →
/// vendor composite → matrix/model → manual). The resolving tier's <see cref="PriceSourceTier.Level"/>
/// classifies the quote unless the tier's own source already assigned a fair-value level. Returns
/// null when no tier can price the symbol, so the caller surfaces the gap rather than marking at cost.
/// </summary>
public sealed class WaterfallMarkPriceSource : IMarkPriceSource
{
    private readonly IReadOnlyList<PriceSourceTier> _tiers;
    private readonly ILogger _log;

    public WaterfallMarkPriceSource(IReadOnlyList<PriceSourceTier> tiers, ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(tiers);
        if (tiers.Count == 0)
            throw new ArgumentException("At least one price-source tier is required.", nameof(tiers));
        if (tiers.Any(static tier => tier is null || tier.Source is null))
            throw new ArgumentException("Every price-source tier and its source must be non-null.", nameof(tiers));

        _tiers = tiers;
        _log = log ?? LoggingSetup.ForContext<WaterfallMarkPriceSource>();
    }

    public async Task<MarkPriceQuote?> GetMarkPriceAsync(string symbol, DateOnly asOf, CancellationToken ct = default)
    {
        foreach (var tier in _tiers)
        {
            ct.ThrowIfCancellationRequested();

            var quote = await tier.Source.GetMarkPriceAsync(symbol, asOf, ct).ConfigureAwait(false);
            if (quote is null)
                continue;

            // The tier's level classifies an unclassified quote, but never raises a fabricated one:
            // a tier labelled "exchange-close" must not lend Level 1 standing to a simulated price.
            var level = FairValueLevelPolicy.Resolve(quote.Level, tier.Level, quote.Provenance);
            _log.Debug(
                "Priced {Symbol} as of {AsOf} from tier {Tier} at fair-value {Level} (origin {Provenance})",
                symbol, asOf, tier.Label, level, quote.Provenance.Token());
            return quote with { Level = level };
        }

        _log.Warning("No price-source tier produced a mark for {Symbol} as of {AsOf}", symbol, asOf);
        return null;
    }
}
