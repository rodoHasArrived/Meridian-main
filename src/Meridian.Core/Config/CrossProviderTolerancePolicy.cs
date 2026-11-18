using System.Text.Json.Serialization;

namespace Meridian.Core.Config;

/// <summary>How cross-provider volume differences are reported.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CrossProviderVolumeMode>))]
public enum CrossProviderVolumeMode
{
    /// <summary>Volume differences above the tolerance raise discrepancies like price differences.</summary>
    Alert,

    /// <summary>Volume differences are recorded but flagged informational and excluded from alert counts.</summary>
    Informational,

    /// <summary>Volume is not compared.</summary>
    Off
}

/// <summary>
/// Optional per-asset-class override. Unset fields inherit from the policy defaults.
/// </summary>
public sealed record CrossProviderToleranceOverride(
    double? PriceTolerancePercent = null,
    double? QuoteTolerancePercent = null,
    double? VolumeTolerancePercent = null,
    CrossProviderVolumeMode? VolumeMode = null,
    double? BackfillCloseTolerancePercent = null);

/// <summary>Effective thresholds after applying an asset-class override.</summary>
public readonly record struct CrossProviderTolerances(
    double PriceTolerancePercent,
    double QuoteTolerancePercent,
    double VolumeTolerancePercent,
    CrossProviderVolumeMode VolumeMode,
    double BackfillCloseTolerancePercent);

/// <summary>
/// User-editable cross-provider data-quality tolerances shared by the streaming comparison service
/// and the backfill cross-provider validator. Defaults reproduce the historical hard-coded values
/// (price/quote 0.5%, volume 10%, backfill first 5 bars with a 1% close gap).
/// </summary>
/// <remarks>
/// Bounds are deliberately narrow: a 100% tolerance would quietly turn off data-integrity alarms.
/// </remarks>
public sealed record CrossProviderTolerancePolicy(
    double PriceTolerancePercent = CrossProviderTolerancePolicy.DefaultPriceTolerancePercent,
    double QuoteTolerancePercent = CrossProviderTolerancePolicy.DefaultQuoteTolerancePercent,
    double VolumeTolerancePercent = CrossProviderTolerancePolicy.DefaultVolumeTolerancePercent,
    CrossProviderVolumeMode VolumeMode = CrossProviderVolumeMode.Alert,
    // Always written: null means "compare every bar", which must not round-trip back to the default of 5.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    int? BackfillBarsToCompare = CrossProviderTolerancePolicy.DefaultBackfillBarsToCompare,
    double BackfillCloseTolerancePercent = CrossProviderTolerancePolicy.DefaultBackfillCloseTolerancePercent,
    IReadOnlyDictionary<string, CrossProviderToleranceOverride>? AssetClassOverrides = null)
{
    public const double DefaultPriceTolerancePercent = 0.5;
    public const double DefaultQuoteTolerancePercent = 0.5;
    public const double DefaultVolumeTolerancePercent = 10.0;
    public const int DefaultBackfillBarsToCompare = 5;
    public const double DefaultBackfillCloseTolerancePercent = 1.0;

    /// <summary>Upper bound for price, quote and backfill close tolerances (percent).</summary>
    public const double MaxPriceTolerancePercent = 5.0;

    /// <summary>Upper bound for the volume tolerance (percent).</summary>
    public const double MaxVolumeTolerancePercent = 50.0;

    /// <summary>Upper bound for the number of backfill bars compared per request.</summary>
    public const int MaxBackfillBarsToCompare = 10_000;

    /// <summary>Upper bound for the number of asset-class overrides.</summary>
    public const int MaxAssetClassOverrides = 32;

    public static CrossProviderTolerancePolicy Default { get; } = new();

    /// <summary>Throws <see cref="ArgumentException"/> when any tolerance is outside its supported bounds.</summary>
    public void Validate()
    {
        ValidatePrice(PriceTolerancePercent, nameof(PriceTolerancePercent));
        ValidatePrice(QuoteTolerancePercent, nameof(QuoteTolerancePercent));
        ValidatePrice(BackfillCloseTolerancePercent, nameof(BackfillCloseTolerancePercent));
        ValidateVolume(VolumeTolerancePercent, nameof(VolumeTolerancePercent));
        ValidateMode(VolumeMode, nameof(VolumeMode));
        if (BackfillBarsToCompare is < 1 or > MaxBackfillBarsToCompare)
            throw new ArgumentException(
                $"{nameof(BackfillBarsToCompare)} must be between 1 and {MaxBackfillBarsToCompare}, or empty to compare every bar.");

        if (AssetClassOverrides is null)
            return;
        if (AssetClassOverrides.Count > MaxAssetClassOverrides)
            throw new ArgumentException($"At most {MaxAssetClassOverrides} asset-class overrides are supported.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (assetClass, value) in AssetClassOverrides)
        {
            if (string.IsNullOrWhiteSpace(assetClass) || assetClass.Length > 40 ||
                assetClass.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
                throw new ArgumentException($"Asset class '{assetClass}' must be 1-40 letters, digits, '-' or '_'.");
            if (!seen.Add(assetClass))
                throw new ArgumentException($"Asset class '{assetClass}' is overridden more than once.");
            if (value is null)
                throw new ArgumentException($"Asset class '{assetClass}' override is empty.");
            if (value.PriceTolerancePercent is { } price)
                ValidatePrice(price, $"{assetClass}.{nameof(PriceTolerancePercent)}");
            if (value.QuoteTolerancePercent is { } quote)
                ValidatePrice(quote, $"{assetClass}.{nameof(QuoteTolerancePercent)}");
            if (value.BackfillCloseTolerancePercent is { } close)
                ValidatePrice(close, $"{assetClass}.{nameof(BackfillCloseTolerancePercent)}");
            if (value.VolumeTolerancePercent is { } volume)
                ValidateVolume(volume, $"{assetClass}.{nameof(VolumeTolerancePercent)}");
            if (value.VolumeMode is { } mode)
                ValidateMode(mode, $"{assetClass}.{nameof(VolumeMode)}");
        }
    }

    /// <summary>Resolves the effective tolerances for an asset class (case-insensitive; null uses the defaults).</summary>
    public CrossProviderTolerances Resolve(string? assetClass)
    {
        var o = FindOverride(assetClass);
        return new CrossProviderTolerances(
            o?.PriceTolerancePercent ?? PriceTolerancePercent,
            o?.QuoteTolerancePercent ?? QuoteTolerancePercent,
            o?.VolumeTolerancePercent ?? VolumeTolerancePercent,
            o?.VolumeMode ?? VolumeMode,
            o?.BackfillCloseTolerancePercent ?? BackfillCloseTolerancePercent);
    }

    /// <summary>
    /// True when this policy flags fewer differences than <paramref name="previous"/> anywhere:
    /// a wider tolerance, a weaker volume mode, fewer compared bars, or a removed/widened override.
    /// </summary>
    public bool Loosens(CrossProviderTolerancePolicy previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        if (Looser(Resolve((string?)null), previous.Resolve((string?)null)))
            return true;
        if (Fewer(BackfillBarsToCompare, previous.BackfillBarsToCompare))
            return true;

        var classes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in AssetClassOverrides?.Keys ?? []) classes.Add(key);
        foreach (var key in previous.AssetClassOverrides?.Keys ?? []) classes.Add(key);
        return classes.Any(assetClass => Looser(Resolve(assetClass), previous.Resolve(assetClass)));
    }

    /// <summary>Resolves the configured policy, falling back to <see cref="Default"/>.</summary>
    public static CrossProviderTolerancePolicy FromConfig(AppConfig? config)
        => config?.DataQuality?.CrossProvider ?? Default;

    private CrossProviderToleranceOverride? FindOverride(string? assetClass)
    {
        if (string.IsNullOrWhiteSpace(assetClass) || AssetClassOverrides is null)
            return null;
        if (AssetClassOverrides.TryGetValue(assetClass, out var exact))
            return exact;
        foreach (var (key, value) in AssetClassOverrides)
        {
            if (string.Equals(key, assetClass, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }

    private static bool Looser(CrossProviderTolerances next, CrossProviderTolerances previous) =>
        next.PriceTolerancePercent > previous.PriceTolerancePercent ||
        next.QuoteTolerancePercent > previous.QuoteTolerancePercent ||
        next.BackfillCloseTolerancePercent > previous.BackfillCloseTolerancePercent ||
        next.VolumeMode > previous.VolumeMode ||
        (next.VolumeMode == previous.VolumeMode && next.VolumeTolerancePercent > previous.VolumeTolerancePercent);

    // null compares every bar, so any limit after "all" is fewer.
    private static bool Fewer(int? next, int? previous) =>
        next.HasValue && (!previous.HasValue || next.Value < previous.Value);

    private static void ValidatePrice(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > MaxPriceTolerancePercent)
            throw new ArgumentException($"{name} must be between 0% and {MaxPriceTolerancePercent}%.");
    }

    private static void ValidateVolume(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > MaxVolumeTolerancePercent)
            throw new ArgumentException($"{name} must be between 0% and {MaxVolumeTolerancePercent}%.");
    }

    private static void ValidateMode(CrossProviderVolumeMode mode, string name)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentException($"{name} is not a supported volume mode.");
    }
}

/// <summary>Before/after evidence saved atomically with each cross-provider tolerance change.</summary>
public sealed record CrossProviderTolerancePolicyAudit(
    Guid Id,
    DateTimeOffset ChangedAt,
    string Actor,
    string? Reason,
    bool Loosened,
    CrossProviderTolerancePolicy Before,
    CrossProviderTolerancePolicy After);

/// <summary>User-editable data-quality configuration. Absent sections keep built-in defaults.</summary>
public sealed record DataQualityConfig(
    CrossProviderTolerancePolicy? CrossProvider = null,
    CrossProviderTolerancePolicyAudit[]? CrossProviderAudit = null);
