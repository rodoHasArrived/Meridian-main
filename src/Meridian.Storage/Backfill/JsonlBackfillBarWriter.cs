using System.Text.Json;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Serialization;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage.Archival;
using Meridian.Storage.Policies;

namespace Meridian.Storage.Backfill;

/// <summary>
/// Writes backfill batches through the shared JSONL naming policy and atomic append primitive.
/// </summary>
public sealed class JsonlBackfillBarWriter(string dataRoot) : IBackfillBarWriter
{
    public async Task<string> WriteAsync(
        DataGranularity granularity,
        IReadOnlyList<HistoricalBar> bars,
        string fallbackSource,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bars);
        if (bars.Count == 0)
            throw new ArgumentException("A historical bar batch must not be empty.", nameof(bars));

        ct.ThrowIfCancellationRequested();
        var first = bars[0];
        var source = string.IsNullOrWhiteSpace(first.Source) ||
                     string.Equals(first.Source, "composite", StringComparison.OrdinalIgnoreCase)
            ? fallbackSource
            : first.Source;
        var exemplar = MarketEvent.HistoricalBar(
            first.ToTimestampUtc(), first.Symbol, first, source, first.SequenceNumber);
        var granularityName = granularity switch
        {
            DataGranularity.Daily => "daily",
            DataGranularity.Hour1 => "hourly",
            DataGranularity.Minute1 => "1min",
            DataGranularity.Minute5 => "5min",
            DataGranularity.Minute15 => "15min",
            DataGranularity.Minute30 => "30min",
            _ => "daily"
        };
        var policy = new JsonlStoragePolicy(new StorageOptions
        {
            RootPath = dataRoot,
            NamingConvention = FileNamingConvention.BySymbol,
            DatePartition = DatePartition.Daily,
            FilePrefix = $"bar_{granularityName}"
        });
        var path = policy.GetPath(exemplar);
        var lines = bars.Select(bar => JsonSerializer.Serialize(bar, MarketDataJsonContext.Default.HistoricalBar));
        await AtomicFileWriter.AppendLinesAsync(path, lines, ct).ConfigureAwait(false);
        return path;
    }
}
