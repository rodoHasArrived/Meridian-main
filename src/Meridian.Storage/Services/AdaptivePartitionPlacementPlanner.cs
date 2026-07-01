namespace Meridian.Storage.Services;

/// <summary>
/// Recommends a storage partition strategy from observed ingestion shape.
/// </summary>
public sealed class AdaptivePartitionPlacementPlanner
{
    public const double HighVolumeEventsPerHourThreshold = 100_000d;
    public const double LowVolumeEventsPerDayThreshold = 10_000d;

    public StorageOptions ApplyRecommendation(
        StorageOptions baseOptions,
        AdaptivePartitionPlacementRecommendation recommendation,
        string? filePrefix = null)
    {
        ArgumentNullException.ThrowIfNull(baseOptions);
        ArgumentNullException.ThrowIfNull(recommendation);

        return new StorageOptions
        {
            RootPath = baseOptions.RootPath,
            Compress = baseOptions.Compress,
            CompressionCodec = baseOptions.CompressionCodec,
            NamingConvention = SelectNamingConvention(baseOptions, recommendation),
            DatePartition = recommendation.Strategy.DateGranularity,
            IncludeProvider = baseOptions.IncludeProvider,
            FilePrefix = filePrefix ?? baseOptions.FilePrefix,
            RetentionDays = baseOptions.RetentionDays,
            MaxTotalBytes = baseOptions.MaxTotalBytes,
            Tiering = baseOptions.Tiering,
            Quotas = baseOptions.Quotas,
            Policies = baseOptions.Policies,
            GenerateManifests = baseOptions.GenerateManifests,
            EmbedChecksum = baseOptions.EmbedChecksum,
            VerifyOnRead = baseOptions.VerifyOnRead,
            EnableParquetSink = baseOptions.EnableParquetSink,
            ActiveSinks = baseOptions.ActiveSinks,
            PartitionStrategy = recommendation.Strategy
        };
    }

    public AdaptivePartitionPlacementRecommendation Recommend(AdaptivePartitionPlacementRequest request)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(request.TotalEvents);
        ArgumentOutOfRangeException.ThrowIfNegative(request.SymbolCount);
        ArgumentOutOfRangeException.ThrowIfNegative(request.SourceCount);
        ArgumentOutOfRangeException.ThrowIfNegative(request.EventTypeCount);

        var effectiveCoverage = request.Coverage > TimeSpan.Zero ? request.Coverage : TimeSpan.FromDays(1);
        var eventsPerHour = request.TotalEvents / Math.Max(1d, effectiveCoverage.TotalHours);
        var eventsPerDay = request.TotalEvents / Math.Max(1d, effectiveCoverage.TotalDays);
        var notes = new List<string>();

        if (effectiveCoverage != request.Coverage)
        {
            notes.Add("Coverage was not positive; recommendation used a one-day evidence window.");
        }

        if (request.LatencySensitive || eventsPerHour >= HighVolumeEventsPerHourThreshold)
        {
            notes.Add(request.LatencySensitive
                ? "Latency-sensitive ingest keeps hot partitions narrow."
                : $"Observed {eventsPerHour:F0} events/hour, above the {HighVolumeEventsPerHourThreshold:F0} hourly threshold.");

            return new AdaptivePartitionPlacementRecommendation(
                Strategy: new PartitionStrategy(
                    PartitionDimension.Symbol,
                    request.EventTypeCount > 1 ? PartitionDimension.EventType : PartitionDimension.Date,
                    request.SourceCount > 1 ? PartitionDimension.Source : null,
                    DateGranularity: DatePartition.Hourly),
                RecommendedProfile: StorageProfile.LowLatency,
                ReasonCode: "HighVolumeHotIngest",
                EventsPerHour: eventsPerHour,
                EventsPerDay: eventsPerDay,
                Notes: notes);
        }

        if (request.ArchivalPromotion ||
            (effectiveCoverage.TotalDays >= 90 && eventsPerDay <= LowVolumeEventsPerDayThreshold))
        {
            notes.Add(request.ArchivalPromotion
                ? "Archival promotion favors coarse date partitions and source provenance."
                : $"Observed {eventsPerDay:F0} events/day across a long window, at or below the {LowVolumeEventsPerDayThreshold:F0} daily threshold.");

            return new AdaptivePartitionPlacementRecommendation(
                Strategy: new PartitionStrategy(
                    PartitionDimension.Date,
                    request.SourceCount > 1 ? PartitionDimension.Source : PartitionDimension.Symbol,
                    request.SourceCount > 1 && request.SymbolCount > 1 ? PartitionDimension.Symbol : null,
                    DateGranularity: DatePartition.Monthly),
                RecommendedProfile: StorageProfile.Archival,
                ReasonCode: "LowVolumeArchivalWindow",
                EventsPerHour: eventsPerHour,
                EventsPerDay: eventsPerDay,
                Notes: notes);
        }

        if (request.SourceCount > 1)
        {
            notes.Add("Multiple providers were observed; provider/source remains an explicit partition dimension for reconciliation.");

            return new AdaptivePartitionPlacementRecommendation(
                Strategy: new PartitionStrategy(
                    PartitionDimension.Date,
                    PartitionDimension.Source,
                    request.SymbolCount > 1 ? PartitionDimension.Symbol : null,
                    DateGranularity: DatePartition.Daily),
                RecommendedProfile: StorageProfile.Research,
                ReasonCode: "MultiSourceDailyEvidence",
                EventsPerHour: eventsPerHour,
                EventsPerDay: eventsPerDay,
                Notes: notes);
        }

        notes.Add("Default evidence profile keeps date-first daily partitions with symbol drill-down.");

        return new AdaptivePartitionPlacementRecommendation(
            Strategy: new PartitionStrategy(
                PartitionDimension.Date,
                request.SymbolCount > 1 ? PartitionDimension.Symbol : null,
                request.EventTypeCount > 1 ? PartitionDimension.EventType : null,
                DateGranularity: DatePartition.Daily),
            RecommendedProfile: StorageProfile.Research,
            ReasonCode: "DefaultDailyEvidence",
            EventsPerHour: eventsPerHour,
            EventsPerDay: eventsPerDay,
            Notes: notes);
    }

    private static FileNamingConvention SelectNamingConvention(
        StorageOptions baseOptions,
        AdaptivePartitionPlacementRecommendation recommendation)
    {
        var strategy = recommendation.Strategy;
        var hasSourceDimension =
            strategy.Primary == PartitionDimension.Source ||
            strategy.Secondary == PartitionDimension.Source ||
            strategy.Tertiary == PartitionDimension.Source;

        return recommendation.ReasonCode switch
        {
            "HighVolumeHotIngest" => hasSourceDimension
                ? FileNamingConvention.BySource
                : FileNamingConvention.BySymbol,
            "LowVolumeArchivalWindow" => hasSourceDimension
                ? FileNamingConvention.BySource
                : FileNamingConvention.ByDate,
            "MultiSourceDailyEvidence" => FileNamingConvention.BySource,
            _ => baseOptions.NamingConvention
        };
    }
}

public sealed record AdaptivePartitionPlacementRequest(
    long TotalEvents,
    TimeSpan Coverage,
    int SymbolCount,
    int SourceCount,
    int EventTypeCount = 1,
    bool LatencySensitive = false,
    bool ArchivalPromotion = false);

public sealed record AdaptivePartitionPlacementRecommendation(
    PartitionStrategy Strategy,
    StorageProfile RecommendedProfile,
    string ReasonCode,
    double EventsPerHour,
    double EventsPerDay,
    IReadOnlyList<string> Notes);
