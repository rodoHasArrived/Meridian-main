using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Api;

namespace Meridian.Ui.Services.Services;

/// <summary>
/// Provides settings configuration capabilities including storage path preview generation,
/// provider catalog data, and configuration profile management.
/// </summary>
public sealed class SettingsConfigurationService
{
    private static readonly Lazy<SettingsConfigurationService> LazyInstance = new(() => new SettingsConfigurationService());
    private static string? _desktopPreferencesFilePathOverride;
    private readonly List<ConfigProfile> _profiles = new();
    private readonly Lock _desktopPreferencesGate = new();
    private DesktopShellPreferences _desktopShellPreferences = DesktopShellPreferences.Default;
    private bool _desktopPreferencesLoaded;

    /// <summary>Gets the singleton instance.</summary>
    public static SettingsConfigurationService Instance => LazyInstance.Value;

    public event EventHandler<DesktopShellPreferences>? DesktopShellPreferencesChanged;

    private SettingsConfigurationService()
    {
        // Seed built-in profiles
        _profiles.Add(new ConfigProfile("research", "Research", "Balanced for analysis workflows. Gzip compression, BySymbol naming.", IsBuiltIn: true));
        _profiles.Add(new ConfigProfile("low-latency", "Low Latency", "Minimum ingest latency. No compression, hourly partitioning.", IsBuiltIn: true));
        _profiles.Add(new ConfigProfile("archival", "Archival", "Long-term retention. ZSTD compression, monthly partitioning.", IsBuiltIn: true));
    }

    /// <summary>
    /// Generates a file tree preview showing what the storage directory structure would look like
    /// based on the given naming convention and symbols.
    /// </summary>
    public string GenerateStoragePreview(string rootPath, string namingConvention, string datePartition, string compression, IReadOnlyList<string> symbols)
    {
        var sb = new StringBuilder();
        var root = string.IsNullOrWhiteSpace(rootPath) ? "data" : rootPath;
        var ext = GetExtensionForCompression(compression);
        var sampleDate = "2026-03-03";
        var displaySymbols = symbols.Count > 0 ? symbols.Take(3).ToList() : new List<string> { "SPY", "AAPL" };

        sb.AppendLine($"{root}/");

        switch (namingConvention?.ToLowerInvariant())
        {
            case "bydate":
                GenerateByDatePreview(sb, displaySymbols, sampleDate, ext);
                break;
            case "bytype":
                GenerateByTypePreview(sb, displaySymbols, sampleDate, ext);
                break;
            case "flat":
                GenerateFlatPreview(sb, displaySymbols, sampleDate, ext);
                break;
            default: // BySymbol (default)
                GenerateBySymbolPreview(sb, displaySymbols, sampleDate, ext);
                break;
        }

        if (symbols.Count > 3)
        {
            sb.AppendLine($"  ... and {symbols.Count - 3} more symbols");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Gets metadata about all available data providers for display in a catalog.
    /// </summary>
    public IReadOnlyList<ProviderCatalogEntry> GetProviderCatalog()
    {
        return ProviderCatalog.GetAll()
            .Select(MapProviderCatalogEntry)
            .ToList();
    }

    /// <summary>
    /// Gets the credential status for each provider by checking environment variables.
    /// </summary>
    public IReadOnlyList<ProviderCredentialStatus> GetProviderCredentialStatuses()
    {
        var catalog = GetProviderCatalog();
        var result = new List<ProviderCredentialStatus>();

        foreach (var provider in catalog)
        {
            var requiredFields = provider.CredentialFields
                .Where(field => field.Required)
                .ToArray();

            if (requiredFields.Length == 0)
            {
                result.Add(new ProviderCredentialStatus(provider.Id, provider.DisplayName,
                    CredentialState.NotRequired, "No credentials required", Array.Empty<string>()));
                continue;
            }

            var missing = requiredFields
                .Where(field => !HasConfiguredEnvironmentVariable(field))
                .Select(field => field.EnvironmentVariable)
                .Where(env => !string.IsNullOrWhiteSpace(env))
                .Select(env => env!)
                .ToArray();

            if (missing.Length == 0)
            {
                result.Add(new ProviderCredentialStatus(provider.Id, provider.DisplayName,
                    CredentialState.Configured, "Configured via environment", Array.Empty<string>()));
            }
            else if (missing.Length < requiredFields.Length)
            {
                result.Add(new ProviderCredentialStatus(provider.Id, provider.DisplayName,
                    CredentialState.Partial, "Some credentials missing", missing));
            }
            else
            {
                result.Add(new ProviderCredentialStatus(provider.Id, provider.DisplayName,
                    CredentialState.Missing, "Not configured", missing));
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the persisted desktop shell preferences, migrating any legacy compact-mode payloads on first load.
    /// </summary>
    public DesktopShellPreferences GetDesktopShellPreferences()
    {
        lock (_desktopPreferencesGate)
        {
            EnsureDesktopShellPreferencesLoaded();
            return _desktopShellPreferences;
        }
    }

    /// <summary>
    /// Gets the current workstation shell density mode.
    /// </summary>
    public ShellDensityMode GetShellDensityMode() => GetDesktopShellPreferences().ShellDensityMode;

    /// <summary>
    /// Persists a new workstation shell density mode and raises a change event for live shell surfaces.
    /// </summary>
    public void SetShellDensityMode(ShellDensityMode densityMode)
    {
        DesktopShellPreferences updatedPreferences;
        var changed = false;

        lock (_desktopPreferencesGate)
        {
            EnsureDesktopShellPreferencesLoaded();
            if (_desktopShellPreferences.ShellDensityMode == densityMode)
            {
                return;
            }

            updatedPreferences = _desktopShellPreferences with { ShellDensityMode = densityMode };
            PersistDesktopShellPreferencesCore(updatedPreferences);
            _desktopShellPreferences = updatedPreferences;
            changed = true;
        }

        if (changed)
        {
            DesktopShellPreferencesChanged?.Invoke(this, updatedPreferences);
        }
    }

    private static ProviderCatalogEntry MapProviderCatalogEntry(Meridian.Contracts.Api.ProviderCatalogEntry entry)
    {
        var envCredentialFields = entry.CredentialFields
            .Where(field => field.AllEnvironmentVariables.Length > 0)
            .ToArray();

        var supportsStreaming = entry.Capabilities.SupportsStreaming;
        var supportsHistorical = entry.ProviderType is ProviderTypeKind.Backfill or ProviderTypeKind.Hybrid;
        var supportsSymbolSearch = entry.ProviderType is ProviderTypeKind.SymbolSearch;
        var supportsOptions = entry.Capabilities.SupportsOptionsChain;
        var supportsBrokerage = entry.Capabilities.SupportsBrokerage;

        return new ProviderCatalogEntry(
            entry.Id,
            entry.DisplayName,
            MapTier(entry.Id),
            supportsStreaming,
            supportsHistorical,
            supportsSymbolSearch,
            entry.Description,
            GetRequestsPerMinute(entry.RateLimit),
            envCredentialFields,
            SupportsOptions: supportsOptions,
            SupportsBrokerage: supportsBrokerage);
    }

    private static ProviderTier MapTier(string providerId) =>
        providerId.ToLowerInvariant() switch
        {
            "alpaca" or "ib" or "ibkr" => ProviderTier.FreeWithAccount,
            "robinhood" => ProviderTier.FreeWithAccount,
            "polygon" or "nasdaq" or "nasdaqdatalink" => ProviderTier.LimitedFree,
            "nyse" => ProviderTier.Premium,
            _ => ProviderTier.Free,
        };

    private static bool HasConfiguredEnvironmentVariable(CredentialFieldInfo field)
    {
        return field.AllEnvironmentVariables
            .Any(envVar => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(envVar)));
    }

    private static int GetRequestsPerMinute(RateLimitInfo? rateLimit)
    {
        if (rateLimit is null || rateLimit.MaxRequestsPerWindow <= 0 || rateLimit.WindowSeconds <= 0)
        {
            return 0;
        }

        var requestsPerMinute = rateLimit.MaxRequestsPerWindow * (60d / rateLimit.WindowSeconds);
        return (int)Math.Round(requestsPerMinute, MidpointRounding.AwayFromZero);
    }

    /// <summary>Gets all configuration profiles (built-in + custom).</summary>
    public IReadOnlyList<ConfigProfile> GetProfiles() => _profiles.AsReadOnly();

    /// <summary>Creates a new custom configuration profile.</summary>
    public ConfigProfile CreateProfile(string name, string description)
    {
        var id = name.ToLowerInvariant().Replace(' ', '-');
        var profile = new ConfigProfile(id, name, description, IsBuiltIn: false);
        _profiles.Add(profile);
        return profile;
    }

    /// <summary>Removes a custom profile (built-in profiles cannot be removed).</summary>
    public bool RemoveProfile(string profileId)
    {
        var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null || profile.IsBuiltIn)
            return false;
        return _profiles.Remove(profile);
    }

    /// <summary>
    /// Estimates daily storage size based on symbol count and data types.
    /// </summary>
    public string EstimateDailyStorageSize(int symbolCount, bool trades, bool quotes, bool depth)
    {
        // Rough estimates per symbol per day (compressed gzip)
        long bytesPerSymbol = 0;
        if (trades)
            bytesPerSymbol += 2 * 1024 * 1024;  // ~2 MB trades
        if (quotes)
            bytesPerSymbol += 5 * 1024 * 1024;  // ~5 MB quotes
        if (depth)
            bytesPerSymbol += 15 * 1024 * 1024;  // ~15 MB L2 depth

        if (bytesPerSymbol == 0)
            bytesPerSymbol = 1024 * 1024; // fallback 1 MB

        var totalBytes = bytesPerSymbol * symbolCount;
        return FormatHelpers.FormatBytes(totalBytes);
    }

    internal static void SetDesktopPreferencesFilePathOverrideForTests(string? filePath)
    {
        _desktopPreferencesFilePathOverride = filePath;
        if (LazyInstance.IsValueCreated)
        {
            LazyInstance.Value.ResetDesktopShellPreferencesForTests();
        }
    }

    private void ResetDesktopShellPreferencesForTests()
    {
        lock (_desktopPreferencesGate)
        {
            _desktopShellPreferences = DesktopShellPreferences.Default;
            _desktopPreferencesLoaded = false;
        }
    }

    private static string GetExtensionForCompression(string compression) =>
        compression?.ToLowerInvariant() switch
        {
            "none" => ".jsonl",
            "lz4" => ".jsonl.lz4",
            "zstd" => ".jsonl.zst",
            _ => ".jsonl.gz", // gzip default
        };

    private static void GenerateBySymbolPreview(StringBuilder sb, List<string> symbols, string date, string ext)
    {
        for (var i = 0; i < symbols.Count; i++)
        {
            var sym = symbols[i];
            var isLast = i == symbols.Count - 1;
            var prefix = isLast ? "  \u2514\u2500\u2500" : "  \u251C\u2500\u2500";
            var childPrefix = isLast ? "      " : "  \u2502   ";

            sb.AppendLine($"{prefix} {sym}/");
            sb.AppendLine($"{childPrefix}\u251C\u2500\u2500 trades/");
            sb.AppendLine($"{childPrefix}\u2502   \u2514\u2500\u2500 {date}{ext}");
            sb.AppendLine($"{childPrefix}\u2514\u2500\u2500 quotes/");
            sb.AppendLine($"{childPrefix}    \u2514\u2500\u2500 {date}{ext}");
        }
    }

    private static void GenerateByDatePreview(StringBuilder sb, List<string> symbols, string date, string ext)
    {
        sb.AppendLine($"  \u2514\u2500\u2500 {date}/");
        for (var i = 0; i < symbols.Count; i++)
        {
            var sym = symbols[i];
            var isLast = i == symbols.Count - 1;
            var prefix = isLast ? "      \u2514\u2500\u2500" : "      \u251C\u2500\u2500";
            var childPrefix = isLast ? "          " : "      \u2502   ";

            sb.AppendLine($"{prefix} {sym}/");
            sb.AppendLine($"{childPrefix}\u251C\u2500\u2500 trades{ext}");
            sb.AppendLine($"{childPrefix}\u2514\u2500\u2500 quotes{ext}");
        }
    }

    private static void GenerateByTypePreview(StringBuilder sb, List<string> symbols, string date, string ext)
    {
        sb.AppendLine("  \u251C\u2500\u2500 trades/");
        foreach (var sym in symbols)
        {
            sb.AppendLine($"  \u2502   \u251C\u2500\u2500 {sym}/");
            sb.AppendLine($"  \u2502   \u2502   \u2514\u2500\u2500 {date}{ext}");
        }
        sb.AppendLine("  \u2514\u2500\u2500 quotes/");
        foreach (var sym in symbols)
        {
            sb.AppendLine($"      \u251C\u2500\u2500 {sym}/");
            sb.AppendLine($"      \u2502   \u2514\u2500\u2500 {date}{ext}");
        }
    }

    private static void GenerateFlatPreview(StringBuilder sb, List<string> symbols, string date, string ext)
    {
        foreach (var sym in symbols)
        {
            sb.AppendLine($"  \u251C\u2500\u2500 {sym}_trades_{date}{ext}");
            sb.AppendLine($"  \u251C\u2500\u2500 {sym}_quotes_{date}{ext}");
        }
    }

    private void EnsureDesktopShellPreferencesLoaded()
    {
        if (_desktopPreferencesLoaded)
        {
            return;
        }

        var path = GetDesktopPreferencesFilePath();
        if (!File.Exists(path))
        {
            _desktopShellPreferences = DesktopShellPreferences.Default;
            _desktopPreferencesLoaded = true;
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var model = JsonSerializer.Deserialize<DesktopShellPreferencesStorageModel>(json, DesktopShellJsonOptions);
            _desktopShellPreferences = model is null
                ? DesktopShellPreferences.Default
                : new DesktopShellPreferences(ResolveShellDensityMode(model));
        }
        catch
        {
            _desktopShellPreferences = DesktopShellPreferences.Default;
        }
        finally
        {
            _desktopPreferencesLoaded = true;
        }
    }

    private void PersistDesktopShellPreferencesCore(DesktopShellPreferences preferences)
    {
        var path = GetDesktopPreferencesFilePath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var model = new DesktopShellPreferencesStorageModel
        {
            ShellDensityMode = preferences.ShellDensityMode.ToString()
        };
        var json = JsonSerializer.Serialize(model, DesktopShellJsonOptions);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, json, Encoding.UTF8);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static ShellDensityMode ResolveShellDensityMode(DesktopShellPreferencesStorageModel model)
    {
        if (!string.IsNullOrWhiteSpace(model.ShellDensityMode) &&
            Enum.TryParse<ShellDensityMode>(model.ShellDensityMode, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return model.IsCompactMode switch
        {
            true => ShellDensityMode.Compact,
            false => ShellDensityMode.Standard,
            null => ShellDensityMode.Standard
        };
    }

    private static string GetDesktopPreferencesFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_desktopPreferencesFilePathOverride))
        {
            return _desktopPreferencesFilePathOverride!;
        }

        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "Meridian", "desktop-shell-preferences.json");
    }

    private static JsonSerializerOptions DesktopShellJsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class DesktopShellPreferencesStorageModel
    {
        public string? ShellDensityMode { get; init; }

        public bool? IsCompactMode { get; init; }
    }
}

/// <summary>Represents a configuration profile (built-in or custom).</summary>
public sealed record ConfigProfile(string Id, string Name, string Description, bool IsBuiltIn);

/// <summary>Provider catalog entry for the provider marketplace display.</summary>
public sealed record ProviderCatalogEntry(
    string Id,
    string DisplayName,
    ProviderTier Tier,
    bool SupportsStreaming,
    bool SupportsHistorical,
    bool SupportsSymbolSearch,
    string Description,
    int RateLimitPerMinute,
    CredentialFieldInfo[] CredentialFields,
    bool SupportsOptions = false,
    bool SupportsBrokerage = false)
{
    public string[] RequiredEnvVars => CredentialFields
        .Where(field => field.Required)
        .SelectMany(field => field.AllEnvironmentVariables)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

/// <summary>Provider tier classification.</summary>
public enum ProviderTier : byte
{
    Free,
    FreeWithAccount,
    LimitedFree,
    Premium,
}

/// <summary>Credential configuration state for a provider.</summary>
public enum CredentialState : byte
{
    NotRequired,
    Configured,
    Partial,
    Missing,
}

/// <summary>Credential status for a specific provider.</summary>
public sealed record ProviderCredentialStatus(
    string ProviderId,
    string DisplayName,
    CredentialState State,
    string StatusMessage,
    string[] MissingEnvVars);
