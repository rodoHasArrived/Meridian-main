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

                if (!incomingBySecurityId.ContainsKey(id))
                {
                    if (entry.SecurityMasterOwnsCanonical)
                    {
                        if (!HasIndependentAuthority(entry))
                            candidate.Symbols.Remove(canonical);
                        else
                            entry.SecurityMasterCanonicalWithdrawn = true;
                    }
                    continue;
                }

                entry.SecurityMasterCanonicalWithdrawn = false;
            }

            // Finish every scoped withdrawal before evaluating destinations. Then reserve and
            // move the complete active set together, so a released name or a chain/swap of
            // Security Master-owned names never depends on retained dictionary iteration order.
            ApplySecurityMasterCanonicalMoves(candidate, incomingBySecurityId, ct);
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

    private static void ApplySecurityMasterCanonicalMoves(
        SymbolRegistry candidate,
        IReadOnlyDictionary<Guid, SymbolRegistryEntry> incoming,
        CancellationToken ct)
    {
        var requestedNames = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, supplied) in incoming)
        {
            ct.ThrowIfCancellationRequested();
            var canonical = supplied.Canonical.Trim().ToUpperInvariant();
            if (!requestedNames.TryAdd(canonical, id))
                throw new InvalidOperationException($"Canonical symbol '{canonical}' is requested by multiple instruments.");
        }

        var moves = new Dictionary<string, (string Canonical, SymbolRegistryEntry Entry)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (canonical, entry) in candidate.Symbols)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.SecurityId is Guid id && incoming.TryGetValue(id, out var supplied))
            {
                var destination = supplied.Canonical.Trim().ToUpperInvariant();
                if (!canonical.Equals(destination, StringComparison.OrdinalIgnoreCase))
                    moves.Add(canonical, (destination, entry));
            }
        }

        var retainedIds = candidate.Symbols.Values.Where(entry => entry.SecurityId.HasValue)
            .Select(entry => entry.SecurityId!.Value).ToHashSet();
        foreach (var (canonical, id) in requestedNames)
        {
            ct.ThrowIfCancellationRequested();
            if (!candidate.Symbols.TryGetValue(canonical, out var occupant) || occupant.SecurityId == id)
                continue;

            // Preserve the existing non-move registration contract: a genuinely new identity
            // can bind an unidentified same-name entry while retaining that independent name.
            // A known identity moving into an unrelated unidentified entry remains a conflict.
            if (occupant.SecurityId is null && !retainedIds.Contains(id))
                continue;

            if (!moves.ContainsKey(canonical) || HasIndependentCanonicalSpelling(occupant))
            {
                throw new InvalidOperationException($"Canonical symbol '{canonical}' is already registered to a different instrument.");
            }
        }

        foreach (var canonical in moves.Keys)
        {
            ct.ThrowIfCancellationRequested();
            candidate.Symbols.Remove(canonical);
        }
        foreach (var (previousCanonical, move) in moves)
        {
            ct.ThrowIfCancellationRequested();
            // A previous independently registered name remains an alias of its instrument.
            // Destination validation above prevents another instrument from shadowing it.
            if (!move.Entry.SecurityMasterOwnsCanonical)
            {
                move.Entry.Aliases.Add(new SymbolAlias
                {
                    Alias = previousCanonical,
                    Source = SymbolMappingSources.Registry,
                    Type = "ticker"
                });
            }
            move.Entry.Canonical = move.Canonical;
            move.Entry.SecurityMasterOwnsCanonical = true;
            candidate.Symbols.Add(move.Canonical, move.Entry);
        }
    }

    private static bool HasIndependentCanonicalSpelling(SymbolRegistryEntry entry)
        => !entry.SecurityMasterOwnsCanonical
           || entry.Aliases.Any(alias => alias.IsActive && string.IsNullOrWhiteSpace(alias.Provider)
               && !IsSecurityMasterSource(alias.Source)
               && alias.Alias.Equals(entry.Canonical, StringComparison.OrdinalIgnoreCase))
           || new[]
           {
               entry.Identifiers.Isin, entry.Identifiers.Cusip, entry.Identifiers.Figi, entry.Identifiers.Sedol,
               entry.Identifiers.CompositeFigi, entry.Identifiers.ShareClassFigi, entry.Identifiers.Cik,
               entry.Identifiers.Lei, entry.Identifiers.BloombergId, entry.Identifiers.Ric
           }.Any(value => string.Equals(value, entry.Canonical, StringComparison.OrdinalIgnoreCase));

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
