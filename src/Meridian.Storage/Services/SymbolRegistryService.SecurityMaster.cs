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
            var incomingBySecurityId = incoming.ToDictionary(entry => entry.SecurityId!.Value);
            foreach (var (canonical, entry) in candidate.Symbols.ToArray())
            {
                ct.ThrowIfCancellationRequested();
                if (entry.SecurityId is not Guid id || !scope.Contains(id))
                    continue;

                // Only aliases emitted from a Security Master identifier field prove that the
                // matching typed identifier came from Security Master; an ordinary alias sharing
                // the value does not, so it never clears another source's identifier.
                var ownedValues = entry.Aliases
                    .Where(alias => string.Equals(alias.Source, SymbolMappingSources.SecurityMasterIdentifier, StringComparison.OrdinalIgnoreCase))
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

                if (!incomingBySecurityId.TryGetValue(id, out var supplied))
                {
                    if (entry.SecurityMasterOwnsCanonical && !HasIndependentAuthority(entry))
                        candidate.Symbols.Remove(canonical);
                    continue;
                }

                var currentCanonical = supplied.Canonical.Trim().ToUpperInvariant();
                if (!entry.Canonical.Equals(currentCanonical, StringComparison.OrdinalIgnoreCase))
                {
                    if (candidate.Symbols.ContainsKey(currentCanonical))
                        throw new InvalidOperationException($"Canonical symbol '{currentCanonical}' is already registered to a different instrument.");

                    // Reconciliation follows durable identity to the new primary ticker. A previous
                    // independently registered canonical remains an alias with that authority; an old
                    // Security Master ticker survives only if its current alias history supplies it.
                    if (!entry.SecurityMasterOwnsCanonical)
                    {
                        entry.Aliases.Add(new SymbolAlias
                        {
                            Alias = entry.Canonical,
                            Source = SymbolMappingSources.Registry,
                            Type = "ticker"
                        });
                    }
                    candidate.Symbols.Remove(canonical);
                    entry.Canonical = currentCanonical;
                    entry.SecurityMasterOwnsCanonical = true;
                    candidate.Symbols[currentCanonical] = entry;
                }
            }

            var indexes = BuildIndexesAndCaches(candidate);
            foreach (var entry in incoming)
            {
                ct.ThrowIfCancellationRequested();
                entry.Canonical = entry.Canonical.Trim().ToUpperInvariant();
                entry.SecurityMasterOwnsCanonical = true;
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

    // Unknown legacy alias ownership is never inferred from spelling or a matching typed field.
    // The opt-in legacy cleanup capability requires a reviewed, exact evidence-bound manifest.
    private static bool HasIndependentAuthority(SymbolRegistryEntry entry)
        => entry.Aliases.Count > 0 || entry.ProviderSymbols.Count > 0
           || !string.IsNullOrWhiteSpace(entry.Identifiers.Isin)
           || !string.IsNullOrWhiteSpace(entry.Identifiers.Cusip)
           || !string.IsNullOrWhiteSpace(entry.Identifiers.Figi)
           || !string.IsNullOrWhiteSpace(entry.Identifiers.Sedol)
           || !string.IsNullOrWhiteSpace(entry.Identifiers.CompositeFigi)
           || !string.IsNullOrWhiteSpace(entry.Identifiers.ShareClassFigi)
           || !string.IsNullOrWhiteSpace(entry.Identifiers.Cik)
           || !string.IsNullOrWhiteSpace(entry.Identifiers.Lei)
           || !string.IsNullOrWhiteSpace(entry.Identifiers.BloombergId)
           || !string.IsNullOrWhiteSpace(entry.Identifiers.Ric);

    /// <summary>
    /// Clears the Security Master-governed identifiers (ISIN, CUSIP, FIGI, SEDOL) whose value the
    /// entry also carried as a Security Master identifier alias, so a removed identifier stops resolving.
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
        => string.Equals(source, SymbolMappingSources.SecurityMaster, StringComparison.OrdinalIgnoreCase)
           || string.Equals(source, SymbolMappingSources.SecurityMasterIdentifier, StringComparison.OrdinalIgnoreCase);
}
