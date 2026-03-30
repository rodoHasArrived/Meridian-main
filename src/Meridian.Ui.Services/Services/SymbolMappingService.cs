using System.Collections.ObjectModel;
using System.Text.Json;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for managing provider-specific symbol mappings.
/// Handles symbol translation between canonical format and provider-specific formats.
/// </summary>
public sealed class SymbolMappingService
{
    private static readonly Lazy<SymbolMappingService> _instance = new(() => new SymbolMappingService());
    public static SymbolMappingService Instance => _instance.Value;

    private readonly string _mappingsFilePath;
    private SymbolMappingsConfig _config = new();

    // Use centralized JSON options to avoid duplication across services
    private static JsonSerializerOptions JsonOptions => DesktopJsonOptions.PrettyPrint;

    /// <summary>
    /// Known data providers with their display names and default transformations.
    /// </summary>
    public static readonly IReadOnlyList<MappingProviderInfo> KnownProviders = new List<MappingProviderInfo>
    {
        new("IB", "Interactive Brokers", "Dots to spaces (BRK.B → BRK B)", SymbolTransform.DotsToSpaces),
        new("Alpaca", "Alpaca Markets", "Uppercase (identity)", SymbolTransform.Uppercase),
        new("Polygon", "Polygon.io", "Uppercase (identity)", SymbolTransform.Uppercase),
        new("Yahoo", "Yahoo Finance", "Dots to dashes (BRK.B → BRK-B)", SymbolTransform.DotsToDashes),
        new("Stooq", "Stooq", "Lowercase + .us suffix (AAPL → aapl.us)", SymbolTransform.StooqFormat),
        new("Tiingo", "Tiingo", "Dots to dashes (BRK.B → BRK-B)", SymbolTransform.DotsToDashes),
        new("Finnhub", "Finnhub", "Uppercase (identity)", SymbolTransform.Uppercase),
        new("AlphaVantage", "Alpha Vantage", "Uppercase (identity)", SymbolTransform.Uppercase),
        new("StockSharp", "StockSharp", "Symbol@Exchange format", SymbolTransform.StockSharpFormat),
        new("NYSE", "NYSE", "Uppercase (identity)", SymbolTransform.Uppercase),
    };

    public event EventHandler? MappingsChanged;

    private SymbolMappingService()
    {
        var baseDir = AppContext.BaseDirectory;
        _mappingsFilePath = Path.Combine(baseDir, "data", "_config", "symbol-mappings.json");
    }

    /// <summary>
    /// Loads symbol mappings from disk.
    /// </summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(_mappingsFilePath))
            {
                var json = await File.ReadAllTextAsync(_mappingsFilePath);
                _config = JsonSerializer.Deserialize<SymbolMappingsConfig>(json, JsonOptions)
                          ?? new SymbolMappingsConfig();
            }
            else
            {
                _config = new SymbolMappingsConfig();
            }
        }
        catch (Exception ex)
        {
            // Log the error but gracefully degrade to empty config
            LoggingService.Instance.LogWarning("Failed to load symbol mappings, using defaults", ex);
            _config = new SymbolMappingsConfig();
        }
    }

    /// <summary>
    /// Saves symbol mappings to disk.
    /// </summary>
    public async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(_mappingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_config, JsonOptions);
            await File.WriteAllTextAsync(_mappingsFilePath, json);
            MappingsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // Log the error but don't crash - mappings are recoverable
            LoggingService.Instance.LogWarning("Failed to save symbol mappings", ex);
        }
    }

    /// <summary>
    /// Gets all symbol mappings.
    /// </summary>
    public IReadOnlyList<SymbolMapping> GetMappings()
    {
        return _config.Mappings?.ToList() ?? new List<SymbolMapping>();
    }

    /// <summary>
    /// Gets a specific symbol mapping by canonical symbol.
    /// </summary>
    public SymbolMapping? GetMapping(string canonicalSymbol)
    {
        return _config.Mappings?.FirstOrDefault(m =>
            string.Equals(m.CanonicalSymbol, canonicalSymbol, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds or updates a symbol mapping.
    /// </summary>
    public async Task AddOrUpdateMappingAsync(SymbolMapping mapping, CancellationToken ct = default)
    {
        var mappings = _config.Mappings?.ToList() ?? new List<SymbolMapping>();

        var existingIndex = mappings.FindIndex(m =>
            string.Equals(m.CanonicalSymbol, mapping.CanonicalSymbol, StringComparison.OrdinalIgnoreCase));

        mapping.UpdatedAt = DateTime.UtcNow;

        if (existingIndex >= 0)
        {
            mappings[existingIndex] = mapping;
        }
        else
        {
            mapping.CreatedAt = DateTime.UtcNow;
            mappings.Add(mapping);
        }

        _config.Mappings = mappings.ToArray();
        await SaveAsync();
    }

    /// <summary>
    /// Removes a symbol mapping.
    /// </summary>
    public async Task RemoveMappingAsync(string canonicalSymbol, CancellationToken ct = default)
    {
        var mappings = _config.Mappings?.ToList() ?? new List<SymbolMapping>();
        mappings.RemoveAll(m =>
            string.Equals(m.CanonicalSymbol, canonicalSymbol, StringComparison.OrdinalIgnoreCase));
        _config.Mappings = mappings.ToArray();
        await SaveAsync();
    }

    /// <summary>
    /// Maps a canonical symbol to a provider-specific format.
    /// </summary>
    public string MapToProvider(string canonicalSymbol, string providerId)
    {
        // First check for explicit mapping
        var mapping = GetMapping(canonicalSymbol);
        if (mapping?.ProviderSymbols != null &&
            mapping.ProviderSymbols.TryGetValue(providerId, out var providerSymbol) &&
            !string.IsNullOrWhiteSpace(providerSymbol))
        {
            return providerSymbol;
        }

        // Fall back to default transformation
        return ApplyDefaultTransform(canonicalSymbol, providerId);
    }

    /// <summary>
    /// Maps a provider-specific symbol back to canonical format.
    /// </summary>
    public string MapToCanonical(string providerSymbol, string providerId)
    {
        // Check for explicit reverse mapping
        var mapping = _config.Mappings?.FirstOrDefault(m =>
            m.ProviderSymbols != null &&
            m.ProviderSymbols.TryGetValue(providerId, out var ps) &&
            string.Equals(ps, providerSymbol, StringComparison.OrdinalIgnoreCase));

        if (mapping != null)
        {
            return mapping.CanonicalSymbol;
        }

        // Fall back to reverse transformation
        return ApplyReverseTransform(providerSymbol, providerId);
    }

    /// <summary>
    /// Applies the default transformation for a provider.
    /// </summary>
    public static string ApplyDefaultTransform(string symbol, string providerId)
    {
        var provider = KnownProviders.FirstOrDefault(p =>
            string.Equals(p.Id, providerId, StringComparison.OrdinalIgnoreCase));

        if (provider == null)
        {
            return symbol.ToUpperInvariant();
        }

        return provider.DefaultTransform switch
        {
            SymbolTransform.Uppercase => symbol.ToUpperInvariant(),
            SymbolTransform.Lowercase => symbol.ToLowerInvariant(),
            SymbolTransform.DotsToSpaces => symbol.Replace(".", " ").ToUpperInvariant(),
            SymbolTransform.DotsToDashes => symbol.Replace(".", "-").ToUpperInvariant(),
            SymbolTransform.StooqFormat => $"{symbol.Replace(".", "-").ToLowerInvariant()}.us",
            SymbolTransform.StockSharpFormat => symbol.ToUpperInvariant(), // Requires exchange, handled separately
            _ => symbol.ToUpperInvariant()
        };
    }

    /// <summary>
    /// Applies the reverse transformation to get canonical symbol.
    /// </summary>
    public static string ApplyReverseTransform(string providerSymbol, string providerId)
    {
        var provider = KnownProviders.FirstOrDefault(p =>
            string.Equals(p.Id, providerId, StringComparison.OrdinalIgnoreCase));

        if (provider == null)
        {
            return providerSymbol.ToUpperInvariant();
        }

        return provider.DefaultTransform switch
        {
            SymbolTransform.Uppercase => providerSymbol.ToUpperInvariant(),
            SymbolTransform.Lowercase => providerSymbol.ToUpperInvariant(),
            SymbolTransform.DotsToSpaces => providerSymbol.Replace(" ", ".").ToUpperInvariant(),
            SymbolTransform.DotsToDashes => providerSymbol.Replace("-", ".").ToUpperInvariant(),
            SymbolTransform.StooqFormat => providerSymbol.Replace(".us", "").Replace("-", ".").ToUpperInvariant(),
            SymbolTransform.StockSharpFormat => providerSymbol.Split('@')[0].ToUpperInvariant(),
            _ => providerSymbol.ToUpperInvariant()
        };
    }

    /// <summary>
    /// Tests symbol mapping across all providers.
    /// </summary>
    public Dictionary<string, string> TestMapping(string canonicalSymbol)
    {
        var results = new Dictionary<string, string>();
        foreach (var provider in KnownProviders)
        {
            results[provider.Id] = MapToProvider(canonicalSymbol, provider.Id);
        }
        return results;
    }

    /// <summary>
    /// Imports mappings from CSV.
    /// </summary>
    public async Task<int> ImportFromCsvAsync(string csvContent, CancellationToken ct = default)
    {
        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
            return 0;

        var headers = lines[0].Split(',').Select(h => h.Trim().Trim('"')).ToArray();
        var canonicalIndex = Array.FindIndex(headers, h =>
            h.Equals("Canonical", StringComparison.OrdinalIgnoreCase) ||
            h.Equals("Symbol", StringComparison.OrdinalIgnoreCase));

        if (canonicalIndex < 0)
            return 0;

        var providerIndexes = new Dictionary<string, int>();
        foreach (var provider in KnownProviders)
        {
            var idx = Array.FindIndex(headers, h =>
                h.Equals(provider.Id, StringComparison.OrdinalIgnoreCase) ||
                h.Equals(provider.DisplayName, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                providerIndexes[provider.Id] = idx;
            }
        }

        var imported = 0;
        for (int i = 1; i < lines.Length; i++)
        {
            var values = ParseCsvLine(lines[i]);
            if (values.Length <= canonicalIndex)
                continue;

            var canonical = values[canonicalIndex].Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(canonical))
                continue;

            var mapping = GetMapping(canonical) ?? new SymbolMapping { CanonicalSymbol = canonical };
            mapping.ProviderSymbols ??= new Dictionary<string, string>();

            foreach (var (providerId, colIndex) in providerIndexes)
            {
                if (colIndex < values.Length)
                {
                    var value = values[colIndex].Trim().Trim('"');
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        mapping.ProviderSymbols[providerId] = value;
                    }
                }
            }

            await AddOrUpdateMappingAsync(mapping);
            imported++;
        }

        return imported;
    }

    /// <summary>
    /// Exports mappings to CSV format.
    /// </summary>
    public string ExportToCsv()
    {
        var sb = new System.Text.StringBuilder();

        // Header
        sb.Append("Canonical");
        foreach (var provider in KnownProviders)
        {
            sb.Append($",{provider.Id}");
        }
        sb.AppendLine();

        // Data rows
        foreach (var mapping in GetMappings())
        {
            sb.Append($"\"{mapping.CanonicalSymbol}\"");
            foreach (var provider in KnownProviders)
            {
                var value = mapping.ProviderSymbols?.GetValueOrDefault(provider.Id) ?? "";
                sb.Append($",\"{value}\"");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string[] ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        values.Add(current.ToString());

        return values.ToArray();
    }

    /// <summary>
    /// Gets or creates provider symbol entries for a mapping.
    /// </summary>
    public SymbolMapping EnsureProviderEntries(SymbolMapping mapping)
    {
        mapping.ProviderSymbols ??= new Dictionary<string, string>();

        foreach (var provider in KnownProviders)
        {
            if (!mapping.ProviderSymbols.ContainsKey(provider.Id))
            {
                mapping.ProviderSymbols[provider.Id] = "";
            }
        }

        return mapping;
    }
}

/// <summary>
/// Symbol mapping configuration stored on disk.
/// </summary>
public sealed class SymbolMappingsConfig
{
    public string Version { get; set; } = "1.0";
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public SymbolMapping[]? Mappings { get; set; }
}

/// <summary>
/// A symbol mapping entry with provider-specific symbols.
/// </summary>
public sealed class SymbolMapping
{
    /// <summary>
    /// The canonical (internal) symbol representation.
    /// </summary>
    public string CanonicalSymbol { get; set; } = string.Empty;

    /// <summary>
    /// Display name for the symbol.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Security type (STK, OPT, FUT, etc.).
    /// </summary>
    public string SecurityType { get; set; } = "STK";

    /// <summary>
    /// Primary exchange.
    /// </summary>
    public string? PrimaryExchange { get; set; }

    /// <summary>
    /// Currency.
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// FIGI identifier for cross-provider resolution.
    /// </summary>
    public string? Figi { get; set; }

    /// <summary>
    /// ISIN identifier.
    /// </summary>
    public string? Isin { get; set; }

    /// <summary>
    /// CUSIP identifier.
    /// </summary>
    public string? Cusip { get; set; }

    /// <summary>
    /// Provider-specific symbol representations.
    /// Key: Provider ID (e.g., "IB", "Alpaca")
    /// Value: Provider-specific symbol
    /// </summary>
    public Dictionary<string, string>? ProviderSymbols { get; set; }

    /// <summary>
    /// Notes about this symbol mapping.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Whether this is a custom/override mapping vs auto-generated.
    /// </summary>
    public bool IsCustomMapping { get; set; }

    /// <summary>
    /// When this mapping was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this mapping was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Information about a data provider for symbol mapping.
/// </summary>
public record MappingProviderInfo(
    string Id,
    string DisplayName,
    string TransformDescription,
    SymbolTransform DefaultTransform
);

/// <summary>
/// Symbol transformation types.
/// </summary>
public enum SymbolTransform : byte
{
    /// <summary>No transformation, identity.</summary>
    None,
    /// <summary>Convert to uppercase.</summary>
    Uppercase,
    /// <summary>Convert to lowercase.</summary>
    Lowercase,
    /// <summary>Replace dots with spaces (IB format).</summary>
    DotsToSpaces,
    /// <summary>Replace dots with dashes (Yahoo format).</summary>
    DotsToDashes,
    /// <summary>Stooq format: lowercase with .us suffix.</summary>
    StooqFormat,
    /// <summary>StockSharp format: SYMBOL@EXCHANGE.</summary>
    StockSharpFormat
}
