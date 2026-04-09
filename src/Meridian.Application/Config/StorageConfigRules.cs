using Meridian.Storage;

namespace Meridian.Application.Config;

internal static class StorageConfigRules
{
    public const string DefaultNamingConvention = "BySymbol";
    public const string DefaultDatePartition = "Daily";

    private static readonly HashSet<string> ValidatorNamingConventions = new(StringComparer.OrdinalIgnoreCase)
    {
        "flat",
        "bysymbol",
        "bydate",
        "bytype"
    };

    private static readonly HashSet<string> SupportedNamingConventions = new(StringComparer.OrdinalIgnoreCase)
    {
        "flat",
        "bysymbol",
        "bydate",
        "bytype",
        "bysource",
        "byassetclass",
        "hierarchical",
        "canonical"
    };

    private static readonly HashSet<string> SupportedDatePartitions = new(StringComparer.OrdinalIgnoreCase)
    {
        "none",
        "daily",
        "hourly",
        "monthly"
    };

    private static readonly HashSet<string> SupportedProfiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "research",
        "lowlatency",
        "archival"
    };

    public static bool IsValidValidatorNamingConvention(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && ValidatorNamingConventions.Contains(value);
    }

    public static bool IsSupportedNamingConvention(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && SupportedNamingConventions.Contains(value);
    }

    public static bool IsSupportedDatePartition(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && SupportedDatePartitions.Contains(value);
    }

    public static bool IsSupportedProfile(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && SupportedProfiles.Contains(value);
    }

    public static FileNamingConvention ParseNamingConvention(string? value)
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

    public static DatePartition ParseDatePartition(string? value)
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
