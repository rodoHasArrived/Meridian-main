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
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly string? _persistencePath;
    private readonly Action<string, string> _writeFile;
    private RegistrySnapshot _state = RegistrySnapshot.CreateEmpty();

    public SourceRegistry(string? persistencePath = null)
        : this(
            persistencePath,
            static (path, content) => AtomicFileWriter.Write(path, content))
    {
    }

    internal SourceRegistry(string? persistencePath, Action<string, string> writeFile)
    {
        _persistencePath = persistencePath;
        _writeFile = writeFile ?? throw new ArgumentNullException(nameof(writeFile));

        RegistrySnapshot initialState;

        if (!string.IsNullOrEmpty(_persistencePath) && File.Exists(_persistencePath))
        {
            initialState = Load();
        }
        else
        {
            initialState = CreateDefaultState();

            if (!string.IsNullOrEmpty(_persistencePath))
            {
                try
                {
                    SaveToDisk(initialState);
                }
                catch (IOException)
                {
                    // Keep defaults in memory; the next explicit mutation can retry persistence.
                }
                catch (UnauthorizedAccessException)
                {
                    // Keep defaults in memory; the next explicit mutation can retry persistence.
                }
            }
        }

        Volatile.Write(ref _state, initialState);
    }

    public SourceInfo? GetSourceInfo(string sourceId)
    {
        var state = Volatile.Read(ref _state);
        return state.Sources.TryGetValue(sourceId, out var info) ? CloneSource(info) : null;
    }

    public SymbolInfo? GetSymbolInfo(string symbol)
    {
        var state = Volatile.Read(ref _state);

        // First check canonical names
        if (state.Symbols.TryGetValue(symbol, out var info))
            return CloneSymbol(info);

        // Then check aliases
        if (state.Aliases.TryGetValue(symbol, out var canonical))
            return state.Symbols.TryGetValue(canonical, out info) ? CloneSymbol(info) : null;

        return null;
    }

    public IReadOnlyList<SourceInfo> GetAllSources()
    {
        var state = Volatile.Read(ref _state);
        return state.Sources.Values
            .OrderBy(source => source.Priority)
            .Select(CloneSource)
            .ToList();
    }

    public IReadOnlyList<SymbolInfo> GetAllSymbols()
    {
        var state = Volatile.Read(ref _state);
        return state.Symbols.Values
            .OrderBy(symbol => symbol.Symbol)
            .Select(CloneSymbol)
            .ToList();
    }

    public void RegisterSource(SourceInfo source)
    {
        var ownedSource = CloneSource(source);
        PersistChange(candidate => candidate.Sources[ownedSource.Id] = ownedSource);
    }

    public void RegisterSymbol(SymbolInfo symbol)
    {
        var ownedSymbol = CloneSymbol(symbol);
        PersistChange(candidate => AddOrUpdateSymbol(candidate, ownedSymbol));
    }

    public string ResolveSymbolAlias(string alias)
    {
        var state = Volatile.Read(ref _state);
        if (state.Aliases.TryGetValue(alias, out var canonical))
            return canonical;

        return alias;
    }

    public string[] GetSourcePriorityOrder()
    {
        var state = Volatile.Read(ref _state);
        return state.Sources.Values
            .Where(s => s.Enabled)
            .OrderBy(s => s.Priority)
            .Select(s => s.Id)
            .ToArray();
    }

    private static RegistrySnapshot CreateDefaultState()
    {
        var state = RegistrySnapshot.CreateEmpty();

        // Register default data sources
        AddOrUpdateSource(state, new SourceInfo(
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

        AddOrUpdateSource(state, new SourceInfo(
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

        AddOrUpdateSource(state, new SourceInfo(
            Id: "polygon",
            Name: "Polygon.io",
            Type: SourceType.Live,
            Priority: 3,
            AssetClasses: new[] { "equity", "crypto" },
            DataTypes: new[] { "Trade", "BboQuote" },
            Enabled: false
        ));

        AddOrUpdateSource(state, new SourceInfo(
            Id: "stooq",
            Name: "Stooq Historical",
            Type: SourceType.Historical,
            Priority: 1,
            AssetClasses: new[] { "equity" },
            DataTypes: new[] { "HistoricalBar" },
            Enabled: true
        ));

        AddOrUpdateSource(state, new SourceInfo(
            Id: "yahoo",
            Name: "Yahoo Finance",
            Type: SourceType.Historical,
            Priority: 2,
            AssetClasses: new[] { "equity" },
            DataTypes: new[] { "HistoricalBar" },
            Enabled: true
        ));

        return state;
    }

    private static void AddOrUpdateSource(RegistrySnapshot state, SourceInfo source)
    {
        state.Sources[source.Id] = CloneSource(source);
    }

    private static void AddOrUpdateSymbol(RegistrySnapshot state, SymbolInfo symbol)
    {
        foreach (var existingAlias in state.Aliases
                     .Where(entry => string.Equals(entry.Value, symbol.Canonical, StringComparison.OrdinalIgnoreCase))
                     .Select(entry => entry.Key)
                     .ToArray())
        {
            state.Aliases.Remove(existingAlias);
        }

        state.Symbols[symbol.Symbol] = symbol;

        if (symbol.Aliases == null)
        {
            return;
        }

        foreach (var alias in symbol.Aliases)
        {
            state.Aliases[alias] = symbol.Canonical;
        }
    }

    private RegistrySnapshot Load()
    {
        try
        {
            if (string.IsNullOrEmpty(_persistencePath))
                return RegistrySnapshot.CreateEmpty();

            var json = File.ReadAllText(_persistencePath);
            var data = JsonSerializer.Deserialize(json, SourceRegistryJsonContext.Default.RegistryData);
            var state = RegistrySnapshot.CreateEmpty();

            if (data?.Sources != null)
            {
                foreach (var source in data.Sources)
                    state.Sources[source.Id] = CloneSource(source);
            }

            if (data?.Symbols != null)
            {
                foreach (var symbol in data.Symbols)
                {
                    var ownedSymbol = CloneSymbol(symbol);
                    state.Symbols[ownedSymbol.Symbol] = ownedSymbol;
                    if (ownedSymbol.Aliases != null)
                    {
                        foreach (var alias in ownedSymbol.Aliases)
                            state.Aliases[alias] = ownedSymbol.Canonical;
                    }
                }
            }

            return state;
        }
        catch (IOException)
        {
            // If loading fails, use defaults
            return CreateDefaultState();
        }
        catch (UnauthorizedAccessException)
        {
            // If loading fails, use defaults
            return CreateDefaultState();
        }
        catch (JsonException)
        {
            // If loading fails, use defaults
            return CreateDefaultState();
        }
        catch (NotSupportedException)
        {
            // If loading fails, use defaults
            return CreateDefaultState();
        }
    }

    private void PersistChange(Action<RegistrySnapshot> applyChange)
    {
        // A registry mutation is not reported as successful until the resulting snapshot has
        // been durably retained. Contention therefore returns an explicit failure instead of
        // silently relying on a future, unrelated mutation to persist this one.
        if (!_saveGate.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("Source registry persistence lock timed out before the mutation could be committed.");

        try
        {
            var candidate = CloneState(Volatile.Read(ref _state));
            applyChange(candidate);

            if (!string.IsNullOrEmpty(_persistencePath))
            {
                SaveToDisk(candidate);
            }

            Volatile.Write(ref _state, candidate);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private void SaveToDisk(RegistrySnapshot state)
    {
        if (string.IsNullOrEmpty(_persistencePath))
            return;

        var data = new RegistryData
        {
            Sources = state.Sources.Values.ToList(),
            Symbols = state.Symbols.Values.ToList()
        };

        var dir = Path.GetDirectoryName(_persistencePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(data, SourceRegistryJsonContext.Default.RegistryData);
        _writeFile(_persistencePath, json);
    }

    private static RegistrySnapshot CloneState(RegistrySnapshot state)
    {
        var clone = RegistrySnapshot.CreateEmpty();

        foreach (var source in state.Sources)
        {
            clone.Sources[source.Key] = CloneSource(source.Value);
        }

        foreach (var symbol in state.Symbols)
        {
            clone.Symbols[symbol.Key] = CloneSymbol(symbol.Value);
        }

        foreach (var alias in state.Aliases)
        {
            clone.Aliases[alias.Key] = alias.Value;
        }

        return clone;
    }

    private static SourceInfo CloneSource(SourceInfo source)
    {
        return source with
        {
            AssetClasses = source.AssetClasses?.ToArray(),
            DataTypes = source.DataTypes?.ToArray()
        };
    }

    private static SymbolInfo CloneSymbol(SymbolInfo symbol)
    {
        return symbol with
        {
            Aliases = symbol.Aliases?.ToArray(),
            Metadata = symbol.Metadata == null
                ? null
                : new Dictionary<string, string>(symbol.Metadata, symbol.Metadata.Comparer)
        };
    }

    private sealed class RegistrySnapshot
    {
        private RegistrySnapshot()
        {
        }

        public Dictionary<string, SourceInfo> Sources { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, SymbolInfo> Symbols { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);

        public static RegistrySnapshot CreateEmpty() => new();
    }

    internal sealed class RegistryData
    {
        public List<SourceInfo>? Sources { get; set; }
        public List<SymbolInfo>? Symbols { get; set; }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SourceRegistry.RegistryData))]
internal sealed partial class SourceRegistryJsonContext : JsonSerializerContext
{
}
