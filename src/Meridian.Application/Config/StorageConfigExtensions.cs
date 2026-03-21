using Meridian.Storage;

namespace Meridian.Application.Config;

/// <summary>
/// Extension methods for <see cref="StorageConfig"/> that require Storage layer types.
/// These are separated from the core StorageConfig record to keep it free of Storage dependencies.
/// </summary>
public static class StorageConfigExtensions
{
    /// <summary>
    /// Converts to StorageOptions for use by storage components.
    /// </summary>
    public static StorageOptions ToStorageOptions(this StorageConfig config, string rootPath, bool compress)
    {
        var options = new StorageOptions
        {
            RootPath = rootPath,
            Compress = compress,
            NamingConvention = ParseNamingConvention(config.NamingConvention),
            DatePartition = ParseDatePartition(config.DatePartition),
            IncludeProvider = config.IncludeProvider,
            FilePrefix = config.FilePrefix,
            RetentionDays = config.RetentionDays,
            MaxTotalBytes = config.MaxTotalMegabytes is null ? null : config.MaxTotalMegabytes * 1024L * 1024L,
            EnableParquetSink = config.EnableParquetSink,
            ActiveSinks = config.Sinks
        };

        return StorageProfilePresets.ApplyProfile(config.Profile, options);
    }

    private static FileNamingConvention ParseNamingConvention(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return FileNamingConvention.BySymbol;

        return value.ToLowerInvariant() switch
        {
            "flat" => FileNamingConvention.Flat,
            "bysymbol" => FileNamingConvention.BySymbol,
            "bydate" => FileNamingConvention.ByDate,
            "bytype" => FileNamingConvention.ByType,
            "bysource" => FileNamingConvention.BySource,
            "byassetclass" => FileNamingConvention.ByAssetClass,
            "hierarchical" => FileNamingConvention.Hierarchical,
            "canonical" => FileNamingConvention.Canonical,
            _ => FileNamingConvention.BySymbol
        };
    }

    private static DatePartition ParseDatePartition(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DatePartition.Daily;

        return value.ToLowerInvariant() switch
        {
            "none" => DatePartition.None,
            "daily" => DatePartition.Daily,
            "hourly" => DatePartition.Hourly,
            "monthly" => DatePartition.Monthly,
            _ => DatePartition.Daily
        };
    }
}
