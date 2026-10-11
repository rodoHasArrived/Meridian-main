using System.Text.Json;
using Meridian.Contracts.Catalog;
using Meridian.Core.Serialization;

namespace Meridian.Storage.Services;

public sealed partial class SymbolRegistryService
{
    /// <summary>
    /// Reconciles a bounded Security Master snapshot without replacing operator/provider authority.
    /// Builds and persists a private candidate before publishing any changed resolver state.
    /// </summary>
    public async Task<int> ReconcileSecurityMasterAsync(
        IReadOnlyCollection<Guid> securityIds,
        IEnumerable<SymbolRegistryEntry> symbols,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(securityIds);
        ArgumentNullException.ThrowIfNull(symbols);
        var scope = securityIds.ToHashSet();
        var incoming = symbols.ToArray();
        if (incoming.Any(entry => entry.SecurityId is not Guid id || !scope.Contains(id)
                || string.IsNullOrWhiteSpace(entry.Canonical))
            || incoming.Select(entry => entry.SecurityId).Distinct().Count() != incoming.Length)
        {
            throw new ArgumentException("Reconciliation requires one definition per scoped SecurityId.", nameof(symbols));
        }

        await _registryLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var json = JsonSerializer.Serialize(_registry, MarketDataJsonContext.Default.SymbolRegistry);
            var candidate = JsonSerializer.Deserialize(json, MarketDataJsonContext.Default.SymbolRegistry)
                ?? throw new InvalidDataException("Unable to clone the retained symbol registry.");
            foreach (var entry in candidate.Symbols.Values)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.SecurityId is not Guid id || !scope.Contains(id))
                    continue;

                // Source is authority, not a spelling heuristic. In particular, never infer
                // ownership of an operator/legacy/provider alias from a ticker suffix.
                entry.Aliases.RemoveAll(alias => IsSecurityMasterSource(alias.Source));
                foreach (var provider in entry.ProviderSymbols.Keys.ToArray())
                {
                    if (entry.ProviderSymbolMetadata.TryGetValue(provider, out var metadata)
                        && IsSecurityMasterSource(metadata.Source) && !metadata.IsOverride)
                    {
                        entry.ProviderSymbols.Remove(provider);
                        entry.ProviderSymbolMetadata.Remove(provider);
                    }
                }
            }

            var indexes = BuildIndexesAndCaches(candidate);
            foreach (var entry in incoming)
            {
                ct.ThrowIfCancellationRequested();
                entry.Canonical = entry.Canonical.Trim().ToUpperInvariant();
                NormalizeEntry(entry);
                RegisterEntry(candidate, indexes.SecurityIdCache, entry, merge: true, _log);
            }

            candidate.LastUpdatedAt = DateTime.UtcNow;
            indexes = BuildIndexesAndCaches(candidate);
            UpdateStatistics(candidate);
            json = JsonSerializer.Serialize(candidate, MarketDataJsonContext.Default.SymbolRegistry);
            await _atomicWriteAsync(_registryPath, json, ct).ConfigureAwait(false);

            _registry = candidate;
            _aliasCache = indexes.AliasCache;
            _symbolCache = indexes.SymbolCache;
            _securityIdCache = indexes.SecurityIdCache;
            return incoming.Length;
        }
        finally
        {
            _registryLock.Release();
        }
    }

    private static bool IsSecurityMasterSource(string? source)
        => string.Equals(source, SymbolMappingSources.SecurityMaster, StringComparison.OrdinalIgnoreCase);
}
