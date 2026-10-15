using System.Text.Json;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Serialization;

namespace Meridian.Storage.Services;

public sealed partial class SymbolRegistryService : ISecurityMasterLegacyIdentifierCleanupWriter
{
    /// <inheritdoc />
    public async Task<string> GetLegacyIdentifierCleanupFingerprintAsync(CancellationToken ct = default)
    {
        await _registryLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return CleanupFingerprint(JsonSerializer.Serialize(_registry, MarketDataJsonContext.Default.SymbolRegistry));
        }
        finally
        {
            _registryLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<SecurityMasterLegacyIdentifierCleanupValidation> ValidateLegacyIdentifierCleanupAsync(
        SecurityMasterLegacyIdentifierCleanupManifest manifest,
        CancellationToken ct = default)
    {
        var snapshot = SnapshotCleanupManifest(manifest);
        var fingerprint = CleanupManifestFingerprint(snapshot);
        await _registryLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (FindCleanupReceipt(snapshot, fingerprint) is { } completed)
            {
                return new(snapshot, fingerprint, completed.RemovedAliases, completed.ClearedIdentifiers);
            }

            CheckCleanupRegistryFingerprint(snapshot);
            var selections = SelectCleanupAliases(_registry, snapshot, ct);
            return new(snapshot, fingerprint, selections.Count, selections.Count(selection => selection.ClearIdentifier));
        }
        finally
        {
            _registryLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<SecurityMasterLegacyIdentifierCleanupReceipt> ApplyLegacyIdentifierCleanupAsync(
        SecurityMasterLegacyIdentifierCleanupValidation validation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(validation);
        var manifest = SnapshotCleanupManifest(validation.Manifest);
        var manifestFingerprint = CleanupManifestFingerprint(manifest);
        if (!string.Equals(manifestFingerprint, validation.ManifestFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("The cleanup manifest no longer matches its dry-run validation.");

        await _registryLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (FindCleanupReceipt(manifest, manifestFingerprint) is { } completed)
            {
                CheckCleanupValidationCounts(validation, completed.RemovedAliases, completed.ClearedIdentifiers);
                return SnapshotCleanupReceipt(completed);
            }

            CheckCleanupRegistryFingerprint(manifest);
            var candidate = JsonSerializer.Deserialize(
                JsonSerializer.Serialize(_registry, MarketDataJsonContext.Default.SymbolRegistry),
                MarketDataJsonContext.Default.SymbolRegistry)
                ?? throw new InvalidDataException("Unable to clone the retained symbol registry.");
            var selections = SelectCleanupAliases(candidate, manifest, ct);
            var clearedIdentifiers = selections.Count(selection => selection.ClearIdentifier);
            CheckCleanupValidationCounts(validation, selections.Count, clearedIdentifiers);

            // Validate every selection first. Neither the live snapshot nor the disk is changed
            // if one item is missing, ambiguous, explicitly owned, or has changed since review.
            foreach (var selection in selections)
            {
                ct.ThrowIfCancellationRequested();
                selection.Entry.Aliases.Remove(selection.Alias);
                if (selection.ClearIdentifier)
                    ClearCleanupIdentifier(selection.Entry.Identifiers, selection.Item.IdentifierKind);
            }

            var receipt = new SecurityMasterLegacyIdentifierCleanupReceipt(
                manifest, manifestFingerprint, DateTimeOffset.UtcNow, selections.Count, clearedIdentifiers);
            candidate.LegacyIdentifierCleanupReceipts ??= new();
            candidate.LegacyIdentifierCleanupReceipts.Add(manifest.CleanupId, receipt);
            candidate.LastUpdatedAt = DateTime.UtcNow;
            var indexes = BuildIndexesAndCaches(candidate);
            UpdateStatistics(candidate);
            var json = JsonSerializer.Serialize(candidate, MarketDataJsonContext.Default.SymbolRegistry);
            await _atomicWriteAsync(_registryPath, json, ct).ConfigureAwait(false);

            _registry = candidate;
            _aliasCache = indexes.AliasCache;
            _symbolCache = indexes.SymbolCache;
            _securityIdCache = indexes.SecurityIdCache;
            return SnapshotCleanupReceipt(receipt);
        }
        finally
        {
            _registryLock.Release();
        }
    }

    private static SecurityMasterLegacyIdentifierCleanupManifest SnapshotCleanupManifest(
        SecurityMasterLegacyIdentifierCleanupManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.CleanupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.RegistryFingerprint);
        if (manifest.CleanupId != manifest.CleanupId.Trim())
            throw new ArgumentException("A cleanup ID cannot contain leading or trailing whitespace.", nameof(manifest));
        if (manifest.Items is null || manifest.Items.Count == 0)
            throw new ArgumentException("A cleanup manifest requires at least one evidence-bound selection.", nameof(manifest));

        var items = manifest.Items.ToArray();
        foreach (var item in items)
        {
            if (item is null || item.SecurityId == Guid.Empty
                || string.IsNullOrWhiteSpace(item.IdentifierValue)
                || item.IdentifierValue != item.IdentifierValue.Trim()
                || string.IsNullOrWhiteSpace(item.EvidenceReference)
                || (item.TypedIdentifierEvidenceReference is not null
                    && string.IsNullOrWhiteSpace(item.TypedIdentifierEvidenceReference))
                || item.IdentifierKind is not (SecurityIdentifierKind.Isin or SecurityIdentifierKind.Cusip
                    or SecurityIdentifierKind.Figi or SecurityIdentifierKind.Sedol))
            {
                throw new ArgumentException("Each cleanup selection requires a SecurityId, supported identifier kind, exact value, and evidence reference.", nameof(manifest));
            }

            if (!IsInferredCleanupSource(item.ExpectedAliasSource))
            {
                throw new ArgumentException("Cleanup cannot select aliases with explicit operator, provider, or other authority.", nameof(manifest));
            }
        }

        // A single alias cannot be selected again under a different identifier kind or evidence.
        if (items.Select(item => (item.SecurityId, item.IdentifierValue, item.ExpectedAliasSource)).Distinct().Count() != items.Length)
            throw new ArgumentException("A cleanup manifest cannot select the same alias more than once.", nameof(manifest));
        return manifest with { Items = Array.AsReadOnly(items) };
    }

    private SecurityMasterLegacyIdentifierCleanupReceipt? FindCleanupReceipt(
        SecurityMasterLegacyIdentifierCleanupManifest manifest,
        string manifestFingerprint)
    {
        if (_registry.LegacyIdentifierCleanupReceipts is null
            || !_registry.LegacyIdentifierCleanupReceipts.TryGetValue(manifest.CleanupId, out var receipt))
            return null;
        if (!string.Equals(receipt.ManifestFingerprint, manifestFingerprint, StringComparison.Ordinal)
            || !string.Equals(CleanupManifestFingerprint(receipt.Manifest), manifestFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("The cleanup ID is already bound to a different manifest.");
        return receipt;
    }

    private void CheckCleanupRegistryFingerprint(SecurityMasterLegacyIdentifierCleanupManifest manifest)
    {
        var current = CleanupFingerprint(JsonSerializer.Serialize(_registry, MarketDataJsonContext.Default.SymbolRegistry));
        if (!string.Equals(current, manifest.RegistryFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("The registry changed since cleanup review; capture a new fingerprint and repeat the dry run.");
    }

    private static List<CleanupAliasSelection> SelectCleanupAliases(
        SymbolRegistry registry,
        SecurityMasterLegacyIdentifierCleanupManifest manifest,
        CancellationToken ct)
    {
        var selections = new List<CleanupAliasSelection>(manifest.Items.Count);
        var clearedFields = new HashSet<(Guid, SecurityIdentifierKind)>();
        foreach (var item in manifest.Items)
        {
            ct.ThrowIfCancellationRequested();
            var entries = registry.Symbols.Values.Where(entry => entry.SecurityId == item.SecurityId).ToArray();
            if (entries.Length != 1)
                throw new InvalidOperationException("A cleanup SecurityId must identify exactly one retained symbol.");
            var entry = entries[0];
            var aliases = entry.Aliases.Where(alias => alias.Provider is null
                && string.Equals(alias.Alias, item.IdentifierValue, StringComparison.Ordinal)
                && string.Equals(alias.Source, item.ExpectedAliasSource, StringComparison.Ordinal)).ToArray();
            if (aliases.Length != 1)
                throw new InvalidOperationException("A cleanup selection must match exactly one providerless alias with the expected value and source.");

            // An alias's origin does not establish the typed field's origin. Clearing that
            // field requires independent evidence, an exact retained value, and no competing
            // explicit authority for that value. Alias-only cleanup never changes typed fields.
            var clearIdentifier = item.TypedIdentifierEvidenceReference is not null
                && string.Equals(GetCleanupIdentifier(entry.Identifiers, item.IdentifierKind), item.IdentifierValue, StringComparison.Ordinal);
            if (clearIdentifier && (entry.Aliases.Any(alias =>
                    string.Equals(alias.Alias, item.IdentifierValue, StringComparison.OrdinalIgnoreCase)
                    && (alias.Provider is not null || !IsInferredCleanupSource(alias.Source)))
                || entry.ProviderSymbols.Values.Any(value => string.Equals(value, item.IdentifierValue, StringComparison.OrdinalIgnoreCase))
                || registry.ProviderMappings.Values.Any(mapping => mapping.Any(pair =>
                    string.Equals(pair.Key, item.IdentifierValue, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(pair.Value, entry.Canonical, StringComparison.OrdinalIgnoreCase)))))
            {
                throw new InvalidOperationException("Typed identifier cleanup conflicts with retained explicit alias or provider authority.");
            }

            selections.Add(new(entry, aliases[0], item,
                clearIdentifier && clearedFields.Add((item.SecurityId, item.IdentifierKind))));
        }
        return selections;
    }

    private static bool IsInferredCleanupSource(string? source)
        => source is null
           || string.Equals(source, "manual", StringComparison.OrdinalIgnoreCase)
           || string.Equals(source, "isin", StringComparison.OrdinalIgnoreCase)
           || string.Equals(source, "figi", StringComparison.OrdinalIgnoreCase);

    private static string? GetCleanupIdentifier(SymbolIdentifiers identifiers, SecurityIdentifierKind kind) => kind switch
    {
        SecurityIdentifierKind.Isin => identifiers.Isin,
        SecurityIdentifierKind.Cusip => identifiers.Cusip,
        SecurityIdentifierKind.Figi => identifiers.Figi,
        SecurityIdentifierKind.Sedol => identifiers.Sedol,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static void ClearCleanupIdentifier(SymbolIdentifiers identifiers, SecurityIdentifierKind kind)
    {
        switch (kind)
        {
            case SecurityIdentifierKind.Isin: identifiers.Isin = null; break;
            case SecurityIdentifierKind.Cusip: identifiers.Cusip = null; break;
            case SecurityIdentifierKind.Figi: identifiers.Figi = null; break;
            case SecurityIdentifierKind.Sedol: identifiers.Sedol = null; break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static void CheckCleanupValidationCounts(
        SecurityMasterLegacyIdentifierCleanupValidation validation,
        int removedAliases,
        int clearedIdentifiers)
    {
        if (validation.RemovedAliases != removedAliases || validation.ClearedIdentifiers != clearedIdentifiers)
            throw new InvalidOperationException("The cleanup selection no longer matches its dry-run validation.");
    }

    private static string CleanupManifestFingerprint(SecurityMasterLegacyIdentifierCleanupManifest manifest)
        => CleanupFingerprint(JsonSerializer.Serialize(manifest, typeof(SecurityMasterLegacyIdentifierCleanupManifest), MarketDataJsonContext.Default));

    private static string CleanupFingerprint(string json) => Sha256Digest.ComputeUtf8(json);

    private static SecurityMasterLegacyIdentifierCleanupReceipt SnapshotCleanupReceipt(SecurityMasterLegacyIdentifierCleanupReceipt receipt)
        => receipt with { Manifest = SnapshotCleanupManifest(receipt.Manifest) };

    private sealed record CleanupAliasSelection(
        SymbolRegistryEntry Entry,
        SymbolAlias Alias,
        SecurityMasterLegacyIdentifierCleanupItem Item,
        bool ClearIdentifier);
}
