using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Storage.Archival;
using Meridian.Storage.Interfaces;

namespace Meridian.Storage.Services;

/// <summary>
/// In-memory implementation of source and symbol registry with JSON persistence.
/// </summary>
public sealed class SourceRegistry : ISourceRegistry
{
    private readonly ConcurrentDictionary<string, SourceInfo> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SymbolInfo> _symbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly string? _persistencePath;

    public SourceRegistry(string? persistencePath = null)
    {
        _persistencePath = persistencePath;

        if (!string.IsNullOrEmpty(_persistencePath) && File.Exists(_persistencePath))
        {
            Load();
        }
        else
        {
            InitializeDefaults();
        }
    }

    public SourceInfo? GetSourceInfo(string sourceId)
    {
        return _sources.TryGetValue(sourceId, out var info) ? info : null;
    }

    public SymbolInfo? GetSymbolInfo(string symbol)
    {
        // First check canonical names
        if (_symbols.TryGetValue(symbol, out var info))
            return info;

        // Then check aliases
        if (_aliases.TryGetValue(symbol, out var canonical))
            return _symbols.TryGetValue(canonical, out info) ? info : null;

        return null;
    }

    public IReadOnlyList<SourceInfo> GetAllSources()
    {
        return _sources.Values.OrderBy(s => s.Priority).ToList();
    }

    public IReadOnlyList<SymbolInfo> GetAllSymbols()
    {
        return _symbols.Values.OrderBy(s => s.Symbol).ToList();
    }

    public void RegisterSource(SourceInfo source)
    {
        _sources[source.Id] = source;
        SaveAsync();
    }

    public void RegisterSymbol(SymbolInfo symbol)
    {
        _symbols[symbol.Symbol] = symbol;

        // Register aliases
        if (symbol.Aliases != null)
        {
            foreach (var alias in symbol.Aliases)
            {
                _aliases[alias] = symbol.Canonical;
            }
        }

        SaveAsync();
    }

    public string ResolveSymbolAlias(string alias)
    {
        if (_aliases.TryGetValue(alias, out var canonical))
            return canonical;

        return alias;
    }

    public string[] GetSourcePriorityOrder()
    {
        return _sources.Values
            .Where(s => s.Enabled)
            .OrderBy(s => s.Priority)
            .Select(s => s.Id)
            .ToArray();
    }

    private void InitializeDefaults()
    {
        // Register default data sources
        RegisterSource(new SourceInfo(
            Id: "alpaca",
            Name: "Alpaca Markets",
            Type: SourceType.Live,
            Priority: 1,
            AssetClasses: new[] { "equity" },
            DataTypes: new[] { "Trade", "BboQuote", "L2Snapshot" },
            LatencyMs: 10,
            Reliability: 0.999,
            Enabled: true
        ));

        RegisterSource(new SourceInfo(
            Id: "ib",
            Name: "Interactive Brokers",
            Type: SourceType.Live,
            Priority: 2,
            AssetClasses: new[] { "equity", "options", "futures", "forex" },
            DataTypes: new[] { "Trade", "BboQuote", "L2Snapshot", "OrderFlow" },
            LatencyMs: 5,
            Reliability: 0.9999,
            Enabled: true
        ));

        RegisterSource(new SourceInfo(
            Id: "polygon",
            Name: "Polygon.io",
            Type: SourceType.Live,
            Priority: 3,
            AssetClasses: new[] { "equity", "crypto" },
            DataTypes: new[] { "Trade", "BboQuote" },
            Enabled: false
        ));

        RegisterSource(new SourceInfo(
            Id: "stooq",
            Name: "Stooq Historical",
            Type: SourceType.Historical,
            Priority: 1,
            AssetClasses: new[] { "equity" },
            DataTypes: new[] { "HistoricalBar" },
            Enabled: true
        ));

        RegisterSource(new SourceInfo(
            Id: "yahoo",
            Name: "Yahoo Finance",
            Type: SourceType.Historical,
            Priority: 2,
            AssetClasses: new[] { "equity" },
            DataTypes: new[] { "HistoricalBar" },
            Enabled: true
        ));
    }

    private void Load()
    {
        try
        {
            if (string.IsNullOrEmpty(_persistencePath))
                return;

            var json = File.ReadAllText(_persistencePath);
            var data = JsonSerializer.Deserialize(json, SourceRegistryJsonContext.Default.RegistryData);

            if (data?.Sources != null)
            {
                foreach (var source in data.Sources)
                    _sources[source.Id] = source;
            }

            if (data?.Symbols != null)
            {
                foreach (var symbol in data.Symbols)
                {
                    _symbols[symbol.Symbol] = symbol;
                    if (symbol.Aliases != null)
                    {
                        foreach (var alias in symbol.Aliases)
                            _aliases[alias] = symbol.Canonical;
                    }
                }
            }
        }
        catch (IOException)
        {
            // If loading fails, use defaults
            InitializeDefaults();
        }
        catch (UnauthorizedAccessException)
        {
            // If loading fails, use defaults
            InitializeDefaults();
        }
        catch (JsonException)
        {
            // If loading fails, use defaults
            InitializeDefaults();
        }
        catch (NotSupportedException)
        {
            // If loading fails, use defaults
            InitializeDefaults();
        }
    }

    private void SaveAsync()
    {
        if (string.IsNullOrEmpty(_persistencePath))
            return;

        _ = SaveToDiskAsync();
    }

    private async Task SaveToDiskAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_persistencePath))
            return;

        await _saveGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();

            var data = new RegistryData
            {
                Sources = _sources.Values.ToList(),
                Symbols = _symbols.Values.ToList()
            };

            var dir = Path.GetDirectoryName(_persistencePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(data, SourceRegistryJsonContext.Default.RegistryData);
            await AtomicFileWriter.WriteAsync(_persistencePath, json, ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Silently fail on save errors
        }
        catch (UnauthorizedAccessException)
        {
            // Silently fail on save errors
        }
        catch (JsonException)
        {
            // Silently fail on save errors
        }
        catch (NotSupportedException)
        {
            // Silently fail on save errors
        }
        finally
        {
            _saveGate.Release();
        }
    }

    internal sealed class RegistryData
    {
        public List<SourceInfo>? Sources { get; set; }
        public List<SymbolInfo>? Symbols { get; set; }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SourceRegistry.RegistryData))]
internal sealed partial class SourceRegistryJsonContext : JsonSerializerContext
{
}
