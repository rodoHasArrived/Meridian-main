using System.Linq;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage.Backfill;

namespace Meridian.Application.Backfill;

/// <summary>
/// Incoming request describing a historical backfill.
/// </summary>
/// <param name="Provider">The name of the historical data provider to use.</param>
/// <param name="Symbols">Symbols to backfill.</param>
/// <param name="From">Optional inclusive start date.</param>
/// <param name="To">Optional inclusive end date.</param>
/// <param name="Granularity">Requested backfill granularity. Defaults to daily bars.</param>
/// <param name="MaxConcurrentSymbols">
/// Maximum number of symbols to process concurrently. Overrides the value from
/// <see cref="BackfillJobsConfig.MaxConcurrentRequests"/> when set.
/// </param>
/// <param name="SymbolPriorities">
/// Optional per-symbol priority map; lower value means processed first.
/// Keys are matched case-insensitively. Symbols not listed are treated as priority 0.
/// </param>
/// <param name="ResumeFromCheckpoint">
/// When <c>true</c> and a <see cref="BackfillStatusStore"/> is provided to
/// <see cref="HistoricalBackfillService"/>, dates already recorded as durably written by the same
/// provider and feed are not fetched again. Only the uncovered parts of
/// <see cref="From"/>..<see cref="To"/> are requested (before, inside and after the recorded
/// coverage), and a symbol is skipped only when the whole range is covered. Date-only checkpoints
/// from earlier versions are not treated as coverage.
/// </param>
public sealed record BackfillRequest(
    string Provider,
    IReadOnlyList<string> Symbols,
    DateOnly? From = null,
    DateOnly? To = null,
    DataGranularity Granularity = DataGranularity.Daily,
    int? MaxConcurrentSymbols = null,
    IReadOnlyDictionary<string, int>? SymbolPriorities = null,
    bool ResumeFromCheckpoint = false
)
{
    public static BackfillRequest FromConfig(AppConfig cfg)
    {
        var defaults = cfg.Backfill;
        var symbols = (defaults?.Symbols?.Length > 0
            ? defaults.Symbols
            : cfg.Symbols?.Select(s => s.Symbol).ToArray()) ?? Array.Empty<string>();

        return new BackfillRequest(
            defaults?.Provider ?? "stooq",
            symbols,
            defaults?.From,
            defaults?.To,
            DataGranularityExtensions.ParseValueOrDefault(defaults?.Granularity),
            MaxConcurrentSymbols: defaults?.Jobs?.MaxConcurrentRequests);
    }
}
