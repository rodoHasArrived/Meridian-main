using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.Integrity;
using Meridian.Core.Serialization;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Services;

public sealed partial class StorageCatalogService
{
    private const string SnapshotDigestProperty = "snapshotContentDigest";
    private const string SnapshotFormatProperty = "snapshotFormatVersion";
    private const string SnapshotDirectoriesProperty = "directorySnapshots";

    public async Task SaveCatalogAsync(CancellationToken ct = default)
    {
        await _catalogLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var candidate = CloneCatalogValue(_catalog);
            await SaveCatalogSnapshotAsync(candidate, ct).ConfigureAwait(false);
            PublishCatalogSnapshot(candidate, _directoryIndexCache);
        }
        finally
        {
            _catalogLock.Release();
        }
    }

    private async Task MutateCatalogAsync(Func<Dictionary<string, DirectoryIndex>, IReadOnlyCollection<string>> mutate, CancellationToken ct)
    {
        await _catalogLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var candidate = CloneCatalogValue(_catalog);
            var directories = _directoryIndexCache.ToDictionary(pair => pair.Key, pair => CloneCatalogValue(pair.Value));
            var affectedDirectories = mutate(directories);
            var files = BuildSnapshotFileIndex(directories);
            BuildCatalogFromFileIndex(candidate, files, directories.Keys);
            ValidateCatalogSnapshot(candidate, directories);
            // Loose indexes remain compatibility exports. They never establish current authority.
            foreach (var path in affectedDirectories)
                await SaveDirectoryIndexAsync(directories[path], Path.Combine(_rootPath, path, DirectoryIndexFileName), ct).ConfigureAwait(false);
            await SaveCatalogSnapshotAsync(candidate, ct, directories).ConfigureAwait(false);
            PublishCatalogSnapshot(candidate, directories);
        }
        finally
        {
            _catalogLock.Release();
        }
    }

    private async Task PersistCatalogIntegrityAsync(CatalogIntegrity integrity, CatalogReadState observedGeneration, CancellationToken ct)
    {
        await _catalogLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(Volatile.Read(ref _readState), observedGeneration))
                throw new InvalidOperationException("The catalog changed during integrity verification. Verify the current generation again.");
            var candidate = CloneCatalogValue(observedGeneration.Catalog);
            candidate.Integrity = integrity;
            await SaveCatalogSnapshotAsync(candidate, ct).ConfigureAwait(false);
            PublishCatalogSnapshot(candidate, _directoryIndexCache);
        }
        finally
        {
            _catalogLock.Release();
        }
    }

    private async Task SaveCatalogSnapshotAsync(StorageCatalog catalog, CancellationToken ct,
        IReadOnlyDictionary<string, DirectoryIndex>? directoryIndexes = null)
    {
        Directory.CreateDirectory(_catalogPath);
        var manifestPath = Path.Combine(_catalogPath, ManifestFileName);
        await using var lease = await ArchiveAppendLock.AcquireAsync(manifestPath, TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        var retained = await ReadRetainedCatalogSnapshotAsync(ct).ConfigureAwait(false);
        if (retained is null && _retainedCatalogDigest is null && HasRetainedCatalogRevisions())
            throw new InvalidDataException("Catalog history exists without a current reference. Restore it before publishing another generation.");
        if (retained?.Digest != _retainedCatalogDigest)
            throw new InvalidOperationException("The retained catalog changed. Reload it before publishing another generation.");

        var frozen = CloneCatalogValue(catalog);
        var directories = (directoryIndexes ?? _directoryIndexCache)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => CloneCatalogValue(pair.Value), StringComparer.Ordinal);
        ValidateCatalogSnapshot(frozen, directories);
        foreach (var index in directories.Values)
            index.Files = index.Files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToList();
        frozen.Integrity.CatalogChecksum = null;
        var root = JsonSerializer.SerializeToNode(frozen, MarketDataJsonContext.PrettyPrintOptions)!.AsObject();
        root[SnapshotFormatProperty] = 1;
        root[SnapshotDirectoriesProperty] = JsonSerializer.SerializeToNode(directories, MarketDataJsonContext.PrettyPrintOptions);
        var checksum = Sha256Digest.ComputeUtf8(CanonicalCatalogJson(root));
        root["integrity"]!["catalogChecksum"] = checksum;
        var snapshot = CanonicalCatalogJson(root);
        var digest = Sha256Digest.ComputeUtf8(snapshot);
        var revisionPath = GetCatalogRevisionPath(digest);
        if (File.Exists(revisionPath))
        {
            var existing = await ReadCatalogFileAsync(revisionPath, ct).ConfigureAwait(false);
            if (!Sha256Digest.FixedEquals(existing.Digest, digest) || existing.Json != snapshot)
                throw new InvalidDataException("A retained catalog revision is corrupt; its content cannot be replaced.");
        }
        else
        {
            await _writeCatalogFileAsync(revisionPath, snapshot, ct).ConfigureAwait(false);
        }

        root[SnapshotDigestProperty] = digest;
        var pointer = CanonicalCatalogJson(root);
        // Only this replacement makes a complete generation current. Orphan candidates are safe.
        await _writeCatalogFileAsync(manifestPath, pointer, ct).ConfigureAwait(false);
        _retainedCatalogDigest = Sha256Digest.ComputeUtf8(pointer);
        catalog.Integrity.CatalogChecksum = checksum;
    }

    private async Task<(StorageCatalog Catalog, Dictionary<string, DirectoryIndex> Directories, string Digest, bool Legacy)?>
        ReadRetainedCatalogSnapshotAsync(CancellationToken ct)
    {
        var path = Path.Combine(_catalogPath, ManifestFileName);
        (string Json, string Digest) retainedFile;
        try
        {
            retainedFile = await ReadCatalogFileAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        var json = retainedFile.Json;
        try
        {
            var root = JsonNode.Parse(json) as JsonObject
                ?? throw new InvalidDataException("The retained catalog must be a JSON object.");
            string[] required = ["catalogVersion", "catalogId", "statistics", "symbols", "directoryIndexes", "integrity"];
            if (required.Any(name => root[name] is null))
                throw new InvalidDataException("The retained catalog has an incomplete format.");
            var catalog = root.Deserialize<StorageCatalog>()
                ?? throw new InvalidDataException("The retained catalog is missing.");
            var directories = new Dictionary<string, DirectoryIndex>(StringComparer.Ordinal);
            var legacy = !root.ContainsKey(SnapshotDigestProperty);
            if (!legacy)
            {
                var digest = root[SnapshotDigestProperty]!.GetValue<string>();
                if (!Sha256Digest.IsWellFormed(digest))
                    throw new InvalidDataException("The catalog revision reference is invalid.");
                digest = Sha256Digest.Normalize(digest)!;
                var revision = await ReadCatalogFileAsync(GetCatalogRevisionPath(digest), ct).ConfigureAwait(false);
                var snapshot = revision.Json;
                if (!Sha256Digest.FixedEquals(revision.Digest, digest))
                    throw new InvalidDataException("The retained catalog revision does not match its digest.");
                root.Remove(SnapshotDigestProperty);
                if (!JsonNode.DeepEquals(root, JsonNode.Parse(snapshot)) || root[SnapshotFormatProperty]?.GetValue<int>() != 1)
                    throw new InvalidDataException("The current catalog does not match its retained immutable revision.");
                directories = root[SnapshotDirectoriesProperty]?.Deserialize<Dictionary<string, DirectoryIndex>>()
                    ?? throw new InvalidDataException("The catalog revision has no directory snapshots.");
            }
            else
            {
                if (root.ContainsKey(SnapshotFormatProperty) || root.ContainsKey(SnapshotDirectoriesProperty))
                    throw new InvalidDataException("The catalog revision reference is missing.");
                foreach (var directory in catalog.DirectoryIndexes)
                {
                    ValidateCatalogRelativePath(directory, allowRoot: true);
                    var indexJson = await File.ReadAllTextAsync(Path.Combine(_rootPath, directory, DirectoryIndexFileName), ct).ConfigureAwait(false);
                    directories.Add(directory, JsonSerializer.Deserialize<DirectoryIndex>(indexJson)
                        ?? throw new InvalidDataException("A legacy directory index is missing."));
                }
            }
            ValidateCatalogSnapshot(catalog, directories);
            return (catalog, directories, retainedFile.Digest, legacy);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        {
            throw new InvalidDataException("The retained catalog is malformed; restore its current reference and immutable revision.", exception);
        }
    }

    private void ValidateCatalogSnapshot(StorageCatalog catalog, IReadOnlyDictionary<string, DirectoryIndex> directories)
    {
        if (catalog.CatalogVersion != "1.0.0" || string.IsNullOrWhiteSpace(catalog.CatalogId) ||
            catalog.Statistics is null || catalog.Symbols is null || catalog.Integrity is null || catalog.DirectoryIndexes is null ||
            !catalog.DirectoryIndexes.Order(StringComparer.Ordinal).SequenceEqual(directories.Keys.Order(StringComparer.Ordinal)))
            throw new InvalidDataException("The catalog and its retained directory identities are inconsistent.");
        foreach (var (path, index) in directories)
        {
            ValidateCatalogRelativePath(path, allowRoot: true);
            if (index is null || index.IndexVersion != "1.0.0" || index.RelativePath != path || index.Files is null)
                throw new InvalidDataException("A retained directory snapshot has an invalid identity or format.");
            foreach (var file in index.Files)
            {
                if (file is null)
                    throw new InvalidDataException("A retained file entry is missing.");
                ValidateCatalogRelativePath(file.RelativePath, allowRoot: false);
                var directory = Path.GetDirectoryName(file.RelativePath) ?? string.Empty;
                if (!string.Equals(Path.GetFullPath(Path.Combine(_rootPath, directory)), Path.GetFullPath(Path.Combine(_rootPath, path)),
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new InvalidDataException("A file entry belongs to a different retained directory.");
            }
        }
        BuildSnapshotFileIndex(directories);
    }

    private void ValidateCatalogRelativePath(string path, bool allowRoot)
    {
        if (path is null || Path.IsPathRooted(path) || (!allowRoot && string.IsNullOrWhiteSpace(path)))
            throw new InvalidDataException("A catalog path must be relative to its storage root.");
        var resolved = Path.GetFullPath(Path.Combine(_rootPath, path));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var rootPrefix = Path.EndsInDirectorySeparator(_rootPath) ? _rootPath : _rootPath + Path.DirectorySeparatorChar;
        if (!string.Equals(resolved, _rootPath, comparison) && !resolved.StartsWith(rootPrefix, comparison))
            throw new InvalidDataException("A catalog path escapes its storage root.");
    }

    private static ConcurrentDictionary<string, IndexedFileEntry> BuildSnapshotFileIndex(IReadOnlyDictionary<string, DirectoryIndex> directories)
    {
        var files = new ConcurrentDictionary<string, IndexedFileEntry>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var file in directories.Values.SelectMany(index => index.Files))
            if (!files.TryAdd(file.RelativePath, file))
                throw new InvalidDataException("A catalog generation contains duplicate file identities.");
        return files;
    }

    private void PublishCatalogSnapshot(StorageCatalog catalog, IReadOnlyDictionary<string, DirectoryIndex> directories)
    {
        var owned = directories.ToDictionary(pair => pair.Key, pair => CloneCatalogValue(pair.Value));
        var state = new CatalogReadState(CloneCatalogValue(catalog), new ConcurrentDictionary<string, DirectoryIndex>(owned),
            BuildSnapshotFileIndex(owned));
        Volatile.Write(ref _readState, state);
    }

    private static async Task<(string Json, string Digest)> ReadCatalogFileAsync(string path, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        var offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
        try
        {
            return (new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset), Sha256Digest.Compute(bytes));
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("The retained catalog is not valid UTF-8.", exception);
        }
    }

    private bool HasRetainedCatalogRevisions()
    {
        var path = Path.Combine(_catalogPath, "revisions");
        return Directory.Exists(path) && Directory.EnumerateFiles(path, "*.json").Any();
    }

    private string GetCatalogRevisionPath(string digest) => Path.Combine(_catalogPath, "revisions", digest + ".json");
    private static T CloneCatalogValue<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    private static string CanonicalCatalogJson(JsonNode node)
    {
        static JsonNode? Sort(JsonNode? value) => value switch
        {
            JsonObject obj => new JsonObject(obj.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, Sort(pair.Value)))),
            JsonArray array => new JsonArray(array.Select(Sort).ToArray()),
            _ => value?.DeepClone()
        };
        return Sort(node)!.ToJsonString(MarketDataJsonContext.PrettyPrintOptions);
    }

    private sealed record CatalogReadState(StorageCatalog Catalog, ConcurrentDictionary<string, DirectoryIndex> Directories,
        ConcurrentDictionary<string, IndexedFileEntry> Files);
}
