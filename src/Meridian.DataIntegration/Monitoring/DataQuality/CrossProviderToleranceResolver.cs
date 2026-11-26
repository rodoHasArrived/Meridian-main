using Meridian.Core.Config;
using Meridian.Core.Logging;
using Meridian.Storage.Interfaces;
using Serilog;

namespace Meridian.DataIntegration.Monitoring.DataQuality;

/// <summary>The authoritative asset class and the effective thresholds selected for it.</summary>
public readonly record struct CrossProviderToleranceResolution(
    string? AssetClass,
    CrossProviderTolerances Tolerances);

/// <summary>Selects per-symbol thresholds for both live comparisons and historical previews.</summary>
public static class CrossProviderToleranceResolver
{
    private static readonly ILogger Log = LoggingSetup.ForContext(typeof(CrossProviderToleranceResolver));

    /// <summary>Uses the registry's canonical symbol, alias and identifier lookups.</summary>
    public static Func<string, string?>? FromSymbolRegistry(ISymbolRegistryService? registry)
        => registry is null ? null : symbol => registry.LookupSymbol(symbol).Entry?.AssetClass;

    /// <summary>
    /// Resolves the asset class from the configured lookup. An unknown class or a failed lookup
    /// uses the base thresholds.
    /// </summary>
    public static CrossProviderToleranceResolution Resolve(
        string symbol,
        CrossProviderTolerancePolicy policy,
        Func<string, string?>? assetClassResolver)
    {
        string? assetClass = null;
        try
        {
            var resolved = assetClassResolver?.Invoke(symbol);
            assetClass = string.IsNullOrWhiteSpace(resolved) ? null : resolved.Trim();
        }
        catch (Exception exception)
        {
            Log.Debug(exception, "Asset-class lookup failed for {Symbol}; using base tolerances", symbol);
        }

        return new CrossProviderToleranceResolution(assetClass, policy.Resolve(assetClass));
    }
}
