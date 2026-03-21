using System.IO;

namespace Meridian.Storage;

/// <summary>
/// Storage profile presets for simplifying configuration.
/// </summary>
public enum StorageProfile : byte
{
    Research,
    LowLatency,
    Archival
}

public sealed record StorageProfilePreset(
    string Id,
    string Label,
    string Description,
    Func<StorageOptions, StorageOptions> Apply);

public static class StorageProfilePresets
{
    /// <summary>
    /// Default storage profile when none is specified.
    /// Research profile provides balanced defaults suitable for most use cases.
    /// </summary>
    public const string DefaultProfile = "Research";

    private static readonly IReadOnlyList<StorageProfilePreset> Presets = new[]
    {
        new StorageProfilePreset(
            Id: "Research",
            Label: "Research",
            Description: "Balanced defaults for analysis workflows (manifests + compression).",
            Apply: options => new StorageOptions
            {
                RootPath = options.RootPath,
                Compress = true,
                CompressionCodec = CompressionCodec.Gzip,
                NamingConvention = options.NamingConvention,
                DatePartition = options.DatePartition,
                IncludeProvider = options.IncludeProvider,
                FilePrefix = options.FilePrefix,
                RetentionDays = options.RetentionDays,
                MaxTotalBytes = options.MaxTotalBytes,
                Tiering = options.Tiering,
                Quotas = options.Quotas,
                Policies = options.Policies,
                GenerateManifests = true,
                EmbedChecksum = options.EmbedChecksum,
                PartitionStrategy = options.PartitionStrategy ?? new PartitionStrategy(PartitionDimension.Date, PartitionDimension.Symbol, Tertiary: null, DateGranularity: DatePartition.Daily)
            }),
        new StorageProfilePreset(
            Id: "LowLatency",
            Label: "Low Latency",
            Description: "Prioritizes ingest speed with minimal processing.",
            Apply: options => new StorageOptions
            {
                RootPath = options.RootPath,
                Compress = false,
                CompressionCodec = CompressionCodec.None,
                NamingConvention = options.NamingConvention,
                DatePartition = options.DatePartition,
                IncludeProvider = options.IncludeProvider,
                FilePrefix = options.FilePrefix,
                RetentionDays = options.RetentionDays,
                MaxTotalBytes = options.MaxTotalBytes,
                Tiering = options.Tiering,
                Quotas = options.Quotas,
                Policies = options.Policies,
                GenerateManifests = false,
                EmbedChecksum = options.EmbedChecksum,
                PartitionStrategy = options.PartitionStrategy ?? new PartitionStrategy(PartitionDimension.Symbol, PartitionDimension.EventType, Tertiary: null, DateGranularity: DatePartition.Hourly)
            }),
        new StorageProfilePreset(
            Id: "Archival",
            Label: "Archival",
            Description: "Long-term retention with tiering-friendly defaults.",
            Apply: options => new StorageOptions
            {
                RootPath = options.RootPath,
                Compress = true,
                CompressionCodec = CompressionCodec.Zstd,
                NamingConvention = options.NamingConvention,
                DatePartition = options.DatePartition,
                IncludeProvider = options.IncludeProvider,
                FilePrefix = options.FilePrefix,
                RetentionDays = options.RetentionDays ?? 3650,
                MaxTotalBytes = options.MaxTotalBytes ?? 2L * 1024L * 1024L * 1024L * 1024L,
                Tiering = options.Tiering ?? new TieringOptions
                {
                    Enabled = true,
                    Tiers = new List<TierConfig>
                    {
                        new() { Name = "hot", Path = Path.Combine(options.RootPath, "hot"), MaxAgeDays = 7, Format = "jsonl", Compression = CompressionCodec.None },
                        new() { Name = "warm", Path = Path.Combine(options.RootPath, "warm"), MaxAgeDays = 30, Format = "jsonl", Compression = CompressionCodec.Gzip },
                        new() { Name = "cold", Path = Path.Combine(options.RootPath, "cold"), MaxAgeDays = 180, Format = "parquet", Compression = CompressionCodec.Zstd },
                        new() { Name = "archive", Path = Path.Combine(options.RootPath, "archive"), Format = "parquet", Compression = CompressionCodec.Zstd }
                    }
                },
                Quotas = options.Quotas,
                Policies = options.Policies,
                GenerateManifests = true,
                EmbedChecksum = true,
                PartitionStrategy = options.PartitionStrategy ?? new PartitionStrategy(PartitionDimension.Date, PartitionDimension.Source, Tertiary: null, DateGranularity: DatePartition.Monthly)
            })
    };

    /// <summary>
    /// Applies a storage profile to the given options.
    /// If no profile is specified, applies the default profile (Research).
    /// </summary>
    /// <param name="profile">Profile ID, or null/empty to use default.</param>
    /// <param name="options">Base storage options to apply profile to.</param>
    /// <param name="useDefaultIfEmpty">If true, applies default profile when profile is null/empty. Default is true.</param>
    /// <returns>Storage options with profile settings applied.</returns>
    public static StorageOptions ApplyProfile(string? profile, StorageOptions options, bool useDefaultIfEmpty = true)
    {
        var profileToApply = string.IsNullOrWhiteSpace(profile) && useDefaultIfEmpty
            ? DefaultProfile
            : profile;

        if (string.IsNullOrWhiteSpace(profileToApply))
        {
            return options;
        }

        var preset = Presets.FirstOrDefault(p => string.Equals(p.Id, profileToApply, StringComparison.OrdinalIgnoreCase));
        return preset?.Apply(options) ?? options;
    }

    /// <summary>
    /// Gets a profile preset by ID.
    /// </summary>
    public static StorageProfilePreset? GetProfile(string id)
    {
        return Presets.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Creates storage options from a profile without requiring a base options object.
    /// Uses default profile if none specified.
    /// </summary>
    public static StorageOptions CreateFromProfile(string? profile, string rootPath, bool? compress = null)
    {
        var profileToUse = string.IsNullOrWhiteSpace(profile) ? DefaultProfile : profile;
        var baseOptions = new StorageOptions
        {
            RootPath = rootPath,
            Compress = compress ?? false
        };
        return ApplyProfile(profileToUse, baseOptions, useDefaultIfEmpty: true);
    }

    public static IReadOnlyList<StorageProfilePreset> GetPresets() => Presets;
}
