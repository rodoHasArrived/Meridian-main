using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrations;
using Meridian.Contracts.Integrity;

namespace Meridian.Storage.Integrations;

public sealed partial class FileProviderIntegrationManifestStore
{
    /// <summary>
    /// Creates the initial current snapshot, or accepts an identical retry of the current snapshot.
    /// Updates must explicitly save a candidate version and compare-and-set the current reference.
    /// </summary>
    public async Task SaveManifestAsync(ProviderIntegrationManifestDto manifest, CancellationToken ct = default)
    {
        manifest = SnapshotManifest(manifest);
        var reference = ProviderIntegrationManifestIdentity.Create(manifest);
        await using var manifestLock = await AcquireManifestLockAsync(manifest.ManifestId, ct).ConfigureAwait(false);
        var current = await ReadCurrentManifestUnderLockAsync(manifest.ManifestId, ct).ConfigureAwait(false);
        if (current is not null)
        {
            if (!ProviderIntegrationManifestIdentity.Matches(current, reference))
            {
                throw new InvalidOperationException(
                    "The current manifest already exists. Save a new immutable version and compare-and-set the current reference.");
            }

            return;
        }

        await SaveManifestVersionUnderLockAsync(manifest, reference, ct).ConfigureAwait(false);
        await WriteCurrentManifestUnderLockAsync(reference, ct).ConfigureAwait(false);
    }

    public async Task SaveManifestVersionAsync(ProviderIntegrationManifestDto manifest, CancellationToken ct = default)
    {
        manifest = SnapshotManifest(manifest);
        var reference = ProviderIntegrationManifestIdentity.Create(manifest);
        await using var manifestLock = await AcquireManifestLockAsync(manifest.ManifestId, ct).ConfigureAwait(false);
        // Preserve the surviving legacy version before accepting a candidate with the same identity.
        await ReadCurrentManifestUnderLockAsync(manifest.ManifestId, ct).ConfigureAwait(false);
        await SaveManifestVersionUnderLockAsync(manifest, reference, ct).ConfigureAwait(false);
    }

    public async Task<ProviderIntegrationManifestDto?> GetManifestAsync(string manifestId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestId);
        await using var manifestLock = await AcquireManifestLockAsync(manifestId, ct).ConfigureAwait(false);
        var current = await ReadCurrentManifestUnderLockAsync(manifestId, ct).ConfigureAwait(false);
        return current is null ? null : await ReadReferencedManifestUnderLockAsync(current, ct).ConfigureAwait(false);
    }

    public async Task<ProviderIntegrationManifestDto?> GetManifestVersionAsync(
        string manifestId, int manifestVersion, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestId);
        ArgumentOutOfRangeException.ThrowIfLessThan(manifestVersion, 1);
        await using var manifestLock = await AcquireManifestLockAsync(manifestId, ct).ConfigureAwait(false);
        var retained = await ReadManifestVersionUnderLockAsync(manifestId, manifestVersion, ct).ConfigureAwait(false);
        if (retained is not null)
        {
            // Exact historical replay does not depend on the availability of a newer current
            // pointer or snapshot. Only this requested version and its digest are authoritative.
            return retained.Manifest;
        }

        await ReadCurrentManifestUnderLockAsync(manifestId, ct).ConfigureAwait(false);
        return (await ReadManifestVersionUnderLockAsync(manifestId, manifestVersion, ct).ConfigureAwait(false))?.Manifest;
    }

    public async Task<bool> CompareExchangeCurrentManifestAsync(
        string manifestId,
        ProviderIntegrationManifestReferenceDto? expected,
        ProviderIntegrationManifestReferenceDto next,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestId);
        ValidateManifestReference(next, manifestId);
        if (expected is not null)
        {
            ValidateManifestReference(expected, manifestId);
        }

        await using var manifestLock = await AcquireManifestLockAsync(manifestId, ct).ConfigureAwait(false);
        var current = await ReadCurrentManifestUnderLockAsync(manifestId, ct).ConfigureAwait(false);
        if (current is null ? expected is not null : !ProviderIntegrationManifestIdentity.Matches(current, expected))
        {
            return false;
        }

        // A pointer must never name a missing version or a different digest of retained content.
        await ReadReferencedManifestUnderLockAsync(next, ct).ConfigureAwait(false);
        if (ProviderIntegrationManifestIdentity.Matches(current, next))
        {
            return true;
        }

        if (current is not null && next.ManifestVersion <= current.ManifestVersion)
        {
            throw new InvalidOperationException("The current manifest can only advance to a newer immutable version.");
        }

        await WriteCurrentManifestUnderLockAsync(next with { ContentDigest = Sha256Digest.Normalize(next.ContentDigest)! }, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<ProviderIntegrationManifestDto>> ListManifestsAsync(CancellationToken ct = default)
    {
        var manifests = new List<ProviderIntegrationManifestDto>();
        foreach (var path in Directory.EnumerateFiles(GetDirectory("manifests"), "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            using var document = await ReadManifestDocumentAsync(path, ct).ConfigureAwait(false);
            var root = document.RootElement;
            var identity = root.TryGetProperty("manifestReference", out var reference) ? reference : root;
            if (identity.ValueKind != JsonValueKind.Object ||
                !identity.TryGetProperty("manifestId", out var id) || id.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(id.GetString()))
            {
                throw new InvalidDataException($"Manifest current record '{path}' has no valid identity.");
            }

            var manifestId = id.GetString()!;
            if (!StringComparer.Ordinal.Equals(Path.GetFileName(path), $"{HashSegment(manifestId)}.json"))
            {
                throw new InvalidDataException("The manifest current record is stored under a different identity.");
            }

            var manifest = await GetManifestAsync(manifestId, ct).ConfigureAwait(false);
            if (manifest is not null)
            {
                manifests.Add(manifest);
            }
        }

        return manifests.OrderBy(manifest => manifest.ManifestId, StringComparer.Ordinal).ToArray();
    }

    private async Task<ProviderIntegrationManifestReferenceDto?> ReadCurrentManifestUnderLockAsync(
        string manifestId, CancellationToken ct)
    {
        var path = GetManifestPath(manifestId);
        if (!File.Exists(path))
        {
            return null;
        }

        using var document = await ReadManifestDocumentAsync(path, ct).ConfigureAwait(false);
        var root = document.RootElement;
        if (root.TryGetProperty("formatVersion", out _) || root.TryGetProperty("manifestReference", out _))
        {
            var pointer = DeserializeManifestDocument(root, ProviderManifestStoreJsonContext.Default.ProviderManifestCurrentRecord);
            if (pointer is null || pointer.FormatVersion != 1)
            {
                throw new InvalidDataException("The manifest current record has an unsupported format.");
            }

            ValidateStoredManifestReference(pointer.ManifestReference, manifestId);
            await ReadReferencedManifestUnderLockAsync(pointer.ManifestReference, ct).ConfigureAwait(false);
            return pointer.ManifestReference;
        }

        // Only a complete old-format manifest qualifies for migration. A damaged pointer is
        // never interpreted as permission to choose the highest candidate or invent history.
        string[] legacyFields = ["manifestId", "manifestVersion", "providerId", "displayName", "integrationType",
            "environment", "auth", "capabilities", "endpoints", "fieldMappings", "sync", "validationRules",
            "activation", "state", "createdBy", "createdAt"];
        if (legacyFields.Any(field => !root.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null))
        {
            throw new InvalidDataException("The manifest current record is neither a pointer nor a complete legacy manifest.");
        }

        var legacy = DeserializeManifestDocument(root, ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationManifestDto);
        if (legacy is null || !StringComparer.Ordinal.Equals(legacy.ManifestId, manifestId) || legacy.ManifestVersion < 1)
        {
            throw new InvalidDataException("The legacy manifest has an invalid identity.");
        }

        var legacyReference = ProviderIntegrationManifestIdentity.Create(legacy);
        await SaveManifestVersionUnderLockAsync(legacy, legacyReference, ct).ConfigureAwait(false);
        // Replacement commits migration. Until this succeeds, the complete legacy file remains
        // the authority, so a restart can retry a partially completed migration safely.
        await WriteCurrentManifestUnderLockAsync(legacyReference, ct).ConfigureAwait(false);
        return legacyReference;
    }

    private async Task SaveManifestVersionUnderLockAsync(
        ProviderIntegrationManifestDto manifest, ProviderIntegrationManifestReferenceDto reference, CancellationToken ct)
    {
        var existing = await ReadManifestVersionUnderLockAsync(manifest.ManifestId, manifest.ManifestVersion, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!ProviderIntegrationManifestIdentity.Matches(existing.ManifestReference, reference))
            {
                throw new InvalidOperationException("Changed content cannot be stored under an existing manifest ID and version.");
            }

            return;
        }

        await WriteAsync(
            GetManifestVersionPath(manifest.ManifestId, manifest.ManifestVersion),
            new ProviderManifestVersionRecord(1, reference, manifest),
            ProviderManifestStoreJsonContext.Default.ProviderManifestVersionRecord,
            ct).ConfigureAwait(false);
    }

    private Task WriteCurrentManifestUnderLockAsync(ProviderIntegrationManifestReferenceDto reference, CancellationToken ct)
        => WriteAsync(GetManifestPath(reference.ManifestId), new ProviderManifestCurrentRecord(1, reference),
            ProviderManifestStoreJsonContext.Default.ProviderManifestCurrentRecord, ct);

    private async Task<ProviderIntegrationManifestDto> ReadReferencedManifestUnderLockAsync(
        ProviderIntegrationManifestReferenceDto reference, CancellationToken ct)
    {
        var retained = await ReadManifestVersionUnderLockAsync(reference.ManifestId, reference.ManifestVersion, ct).ConfigureAwait(false);
        if (retained is null || !ProviderIntegrationManifestIdentity.Matches(reference, retained.ManifestReference))
        {
            throw new InvalidDataException("The manifest reference does not match a retained immutable version.");
        }

        return retained.Manifest;
    }

    private async Task<ProviderManifestVersionRecord?> ReadManifestVersionUnderLockAsync(
        string manifestId, int manifestVersion, CancellationToken ct)
    {
        var path = GetManifestVersionPath(manifestId, manifestVersion);
        if (!File.Exists(path))
        {
            return null;
        }

        using var document = await ReadManifestDocumentAsync(path, ct).ConfigureAwait(false);
        var retained = DeserializeManifestDocument(document.RootElement, ProviderManifestStoreJsonContext.Default.ProviderManifestVersionRecord);
        if (retained is null || retained.FormatVersion != 1 || retained.Manifest is null ||
            !StringComparer.Ordinal.Equals(retained.Manifest.ManifestId, manifestId) || retained.Manifest.ManifestVersion != manifestVersion)
        {
            throw new InvalidDataException("The retained manifest version has an unsupported format or inconsistent identity.");
        }

        ValidateStoredManifestReference(retained.ManifestReference, manifestId);
        if (!ProviderIntegrationManifestIdentity.Matches(retained.ManifestReference, ProviderIntegrationManifestIdentity.Create(retained.Manifest)))
        {
            throw new InvalidDataException("The retained manifest content does not match its digest.");
        }

        return retained;
    }

    private string GetManifestVersionPath(string manifestId, int version)
        => Path.Combine(GetDirectory("manifest-versions"), HashSegment(manifestId), $"{version.ToString(CultureInfo.InvariantCulture)}.json");

    private Task<FileStream> AcquireManifestLockAsync(string manifestId, CancellationToken ct)
        => AcquireEntityLockAsync("manifest-locks", manifestId, ct);

    private async Task<FileStream> AcquireEntityLockAsync(string category, string entityId, CancellationToken ct)
    {
        var lockPath = Path.Combine(GetDirectory(category), $"{HashSegment(entityId)}.lock");
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Keep the persistent lock file: deleting it could let another writer lock a
                // different inode. OS handle release (including process death) ends ownership.
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                    bufferSize: 1, FileOptions.Asynchronous);
            }
            catch (IOException exception) when ((exception.HResult & 0xffff) is 11 or 32 or 33)
            {
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
        }
    }

    private static void ValidateManifestReference(ProviderIntegrationManifestReferenceDto reference, string manifestId)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (string.IsNullOrWhiteSpace(reference.ManifestId) || !StringComparer.Ordinal.Equals(reference.ManifestId, manifestId) || reference.ManifestVersion < 1 ||
            !Sha256Digest.IsWellFormed(reference.ContentDigest))
        {
            throw new ArgumentException("A manifest reference must include the requested ID, a positive version, and a SHA-256 digest.", nameof(reference));
        }
    }

    private static ProviderIntegrationManifestDto SnapshotManifest(ProviderIntegrationManifestDto manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        // DTO collections may be mutable. Bind the digest and durable write to one detached
        // snapshot before the first await, rather than hashing one view and saving another.
        return JsonSerializer.SerializeToElement(manifest, ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationManifestDto)
            .Deserialize(ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationManifestDto)!;
    }

    private static void ValidateStoredManifestReference(ProviderIntegrationManifestReferenceDto? reference, string manifestId)
    {
        try
        {
            ValidateManifestReference(reference!, manifestId);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The retained manifest reference is invalid.", exception);
        }
    }

    private static async Task<JsonDocument> ReadManifestDocumentAsync(string path, CancellationToken ct)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw new InvalidDataException("The manifest record must be a JSON object.");
            }

            return document;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The manifest record '{path}' is malformed.", exception);
        }
    }

    private static TValue? DeserializeManifestDocument<TValue>(
        JsonElement value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo)
    {
        try
        {
            return value.Deserialize(jsonTypeInfo);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The manifest record cannot be deserialized.", exception);
        }
    }
}

internal sealed record ProviderManifestCurrentRecord(int FormatVersion, ProviderIntegrationManifestReferenceDto ManifestReference);

internal sealed record ProviderManifestVersionRecord(
    int FormatVersion, ProviderIntegrationManifestReferenceDto ManifestReference, ProviderIntegrationManifestDto Manifest);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(ProviderManifestCurrentRecord))]
[JsonSerializable(typeof(ProviderManifestVersionRecord))]
internal sealed partial class ProviderManifestStoreJsonContext : JsonSerializerContext;
