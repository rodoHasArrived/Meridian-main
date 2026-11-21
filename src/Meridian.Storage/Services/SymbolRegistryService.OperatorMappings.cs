using System.Text.Json;
using Meridian.Contracts.Catalog;
using Meridian.Core.Serialization;

namespace Meridian.Storage.Services;

public sealed partial class SymbolRegistryService
{
    internal Task<int> SaveOperatorMappingsAsync(
        IReadOnlyList<SymbolRegistryEntry> entries,
        CancellationToken ct)
        => WriteOperatorMappingsAsync(candidate =>
        {
            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                entry.Canonical = entry.Canonical.Trim().ToUpperInvariant();
                NormalizeEntry(entry);
                if (candidate.Symbols.TryGetValue(entry.Canonical, out var existing))
                {
                    // Merge overrides without asserting authority over the canonical ticker.
                    MergeEntry(existing, entry, _log);
                }
                else
                {
                    candidate.Symbols.Add(entry.Canonical, entry);
                }
            }

            return entries.Count;
        }, ct);

    internal Task<int> RemoveOperatorMappingsAsync(string canonical, string? provider, CancellationToken ct)
        => WriteOperatorMappingsAsync(candidate =>
        {
            if (!candidate.Symbols.TryGetValue(canonical.Trim().ToUpperInvariant(), out var entry))
                return 0;

            var providerKeys = entry.ProviderSymbolMetadata
                .Where(pair => pair.Value.IsOverride
                    && (provider is null || pair.Key.Equals(provider, StringComparison.OrdinalIgnoreCase)))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var key in providerKeys)
            {
                entry.ProviderSymbols.Remove(key);
                entry.ProviderSymbolMetadata.Remove(key);
            }

            if (providerKeys.Length > 0)
                entry.LastUpdatedAt = DateTime.UtcNow;
            return providerKeys.Length;
        }, ct);

    private async Task<int> WriteOperatorMappingsAsync(Func<SymbolRegistry, int> edit, CancellationToken ct)
    {
        await _registryLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = JsonSerializer.Serialize(_registry, MarketDataJsonContext.Default.SymbolRegistry);
            var candidate = JsonSerializer.Deserialize(current, MarketDataJsonContext.Default.SymbolRegistry)
                ?? throw new InvalidDataException("Unable to clone the retained symbol registry.");
            var count = edit(candidate);
            if (count == 0)
                return 0;

            candidate.LastUpdatedAt = DateTime.UtcNow;
            var indexes = BuildIndexesAndCaches(candidate);
            UpdateStatistics(candidate);
            var contents = JsonSerializer.Serialize(candidate, MarketDataJsonContext.Default.SymbolRegistry);
            await _atomicWriteAsync(_registryPath, contents, ct).ConfigureAwait(false);
            _registry = candidate;
            _aliasCache = indexes.AliasCache;
            _symbolCache = indexes.SymbolCache;
            _securityIdCache = indexes.SecurityIdCache;
            return count;
        }
        finally
        {
            _registryLock.Release();
        }
    }
}
