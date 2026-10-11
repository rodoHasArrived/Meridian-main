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
                ClaimLegacySecurityMasterIdentifierAliases(entry);
                var ownedValues = entry.Aliases
                    .Where(alias => IsSecurityMasterSource(alias.Source))
                    .Select(alias => alias.Alias)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                entry.Aliases.RemoveAll(alias => IsSecurityMasterSource(alias.Source));
                ClearSecurityMasterIdentifiers(entry.Identifiers, ownedValues);
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
                var registered = RegisterEntry(candidate, indexes.SecurityIdCache, entry, merge: true, _log);
                // The merge only fills empty identifiers; an identifier Security Master supplies
                // replaces whatever the entry held, so a corrected value takes effect.
                if (!ReferenceEquals(registered, entry))
                {
                    ApplySecurityMasterIdentifiers(registered.Identifiers, entry.Identifiers);
                }
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

    // Sources the pre-reconciliation seed inferred from an identifier alias's spelling.
    private static readonly HashSet<string> LegacyIdentifierAliasSources =
        new(["isin", "figi", "manual"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// One-way upgrade of registries seeded before identifier aliases carried Security Master
    /// provenance: that seed wrote each identifier both as the typed identifier and as a plain alias
    /// whose source was inferred from its spelling. An alias with such an inferred source whose
    /// value equals the entry's typed ISIN, CUSIP, FIGI or SEDOL is that seed's copy, so it is
    /// claimed as Security Master-owned and reconciled like the aliases the current seed writes.
    /// Aliases with explicit provenance (operator, provider, registry) are never claimed.
    /// </summary>
    private static void ClaimLegacySecurityMasterIdentifierAliases(SymbolRegistryEntry entry)
    {
        var identifiers = entry.Identifiers;
        string?[] identifierValues = [identifiers.Isin, identifiers.Cusip, identifiers.Figi, identifiers.Sedol];
        foreach (var alias in entry.Aliases)
        {
            if (alias.Source is { } source
                && LegacyIdentifierAliasSources.Contains(source)
                && string.IsNullOrWhiteSpace(alias.Provider)
                && identifierValues.Any(value => string.Equals(value, alias.Alias, StringComparison.OrdinalIgnoreCase)))
            {
                alias.Source = SymbolMappingSources.SecurityMaster;
            }
        }
    }

    /// <summary>
    /// Clears the Security Master-governed identifiers (ISIN, CUSIP, FIGI, SEDOL) whose value the
    /// entry also carried as a Security Master-owned alias, so a removed identifier stops resolving.
    /// Identifiers another source supplied are kept.
    /// </summary>
    private static void ClearSecurityMasterIdentifiers(SymbolIdentifiers identifiers, HashSet<string> ownedValues)
    {
        if (identifiers.Isin is { } isin && ownedValues.Contains(isin))
            identifiers.Isin = null;
        if (identifiers.Cusip is { } cusip && ownedValues.Contains(cusip))
            identifiers.Cusip = null;
        if (identifiers.Figi is { } figi && ownedValues.Contains(figi))
            identifiers.Figi = null;
        if (identifiers.Sedol is { } sedol && ownedValues.Contains(sedol))
            identifiers.Sedol = null;
    }

    private static void ApplySecurityMasterIdentifiers(SymbolIdentifiers target, SymbolIdentifiers incoming)
    {
        if (!string.IsNullOrWhiteSpace(incoming.Isin))
            target.Isin = incoming.Isin;
        if (!string.IsNullOrWhiteSpace(incoming.Cusip))
            target.Cusip = incoming.Cusip;
        if (!string.IsNullOrWhiteSpace(incoming.Figi))
            target.Figi = incoming.Figi;
        if (!string.IsNullOrWhiteSpace(incoming.Sedol))
            target.Sedol = incoming.Sedol;
    }

    private static bool IsSecurityMasterSource(string? source)
        => string.Equals(source, SymbolMappingSources.SecurityMaster, StringComparison.OrdinalIgnoreCase);
}
