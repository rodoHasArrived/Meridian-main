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
            NamingConvention = StorageConfigRules.ParseNamingConvention(config.NamingConvention),
            DatePartition = StorageConfigRules.ParseDatePartition(config.DatePartition),
            IncludeProvider = config.IncludeProvider,
            FilePrefix = config.FilePrefix,
            RetentionDays = config.RetentionDays,
            MaxTotalBytes = config.MaxTotalMegabytes is null ? null : config.MaxTotalMegabytes * 1024L * 1024L,
            EnableParquetSink = config.EnableParquetSink,
            ActiveSinks = config.Sinks
        };

        return StorageProfilePresets.ApplyProfile(config.Profile, options);
    }
}
