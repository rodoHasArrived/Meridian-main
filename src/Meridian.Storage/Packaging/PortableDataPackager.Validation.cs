using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Meridian.Core.Serialization;
using Meridian.Storage.Archival;
using Meridian.Contracts.Integrity;

namespace Meridian.Storage.Packaging;

/// <summary>
/// Package validation, manifest reading, import/extraction, and utility methods.
/// </summary>
public sealed partial class PortableDataPackager
{
    private async Task<PackageManifest?> ReadManifestFromPackageAsync(
        string packagePath,
        PackageFormat format,
        CancellationToken ct)
    {
        if (format == PackageFormat.Zip)
        {
            using var stream = File.OpenRead(packagePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            ValidateReservedEntryPaths(archive);

            var manifestEntry = archive.GetEntry(ManifestFileName);
            if (manifestEntry == null)
                return null;

            await using var entryStream = manifestEntry.Open();
            return await JsonSerializer.DeserializeAsync<PackageManifest>(entryStream, cancellationToken: ct);
        }

        // Non-Zip packages have no implemented on-disk format (the removed pseudo-tar.gz writer
        // corrupted binary content); callers reject them before reaching this point.
        return null;
    }

    private Task<List<string>> VerifyFilesInPackageAsync(
        string packagePath,
        PackageManifest manifest,
        PackageFormat format,
        CancellationToken ct)
    {
        var missingFiles = new List<string>();

        if (format == PackageFormat.Zip)
        {
            using var stream = File.OpenRead(packagePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            var entryNames = archive.Entries.Select(e => e.FullName).ToHashSet();

            foreach (var file in manifest.Files)
            {
                if (!entryNames.Contains(file.Path))
                {
                    missingFiles.Add(file.Path);
                }
            }
        }

        return Task.FromResult(missingFiles);
    }

    private async Task<ExtractionResult> ExtractPackageAsync(
        string packagePath,
        string destinationDirectory,
        PackageManifest manifest,
        PackageFormat format,
        bool validateChecksums,
        bool mergeWithExisting,
        CancellationToken ct)
    {
        var result = new ExtractionResult();
        var validationErrors = new List<ValidationError>();
        var extractionRoot = Path.GetFullPath(destinationDirectory);
        var stagingRoot = Path.Combine(extractionRoot, $".meridian-import-{Guid.NewGuid():N}");

        if (format == PackageFormat.Zip)
        {
            using var stream = File.OpenRead(packagePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var allowedEntries = BuildAllowedEntryPaths(manifest);

            // Pre-scan all entries for path traversal and undeclared content before extracting
            // anything, so no file reaches the destination directory if any entry is malicious.
            long declaredBytes = 0;
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue; // Skip directories

                ResolveSafeExtractionPath(stagingRoot, entry.FullName);
                if (!allowedEntries.Contains(entry.FullName) && !IsRecognizedSupplementaryPath(entry.FullName))
                {
                    throw new InvalidDataException(
                        $"Package contains unexpected entry '{entry.FullName}' that is not declared in the manifest.");
                }

                declaredBytes += entry.Length;
            }

            EnsureDiskSpaceForImport(extractionRoot, declaredBytes);
            Directory.CreateDirectory(stagingRoot);
            var movedDestinationDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // Phase 1: stage and validate every entry. Nothing reaches the destination yet, so
                // a checksum failure or cancellation part way through leaves no partial import.
                var staged = new List<(ZipArchiveEntry Entry, string StagedPath)>();
                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();

                    if (string.IsNullOrEmpty(entry.Name))
                        continue; // Skip directories

                    var stagedPath = ResolveSafeExtractionPath(stagingRoot, entry.FullName);
                    var stagedDir = Path.GetDirectoryName(stagedPath);
                    if (!string.IsNullOrEmpty(stagedDir))
                    {
                        Directory.CreateDirectory(stagedDir);
                    }

                    await ExtractEntryBoundedAsync(entry, stagedPath, ct);

                    if (validateChecksums)
                    {
                        var manifestEntry = manifest.Files.FirstOrDefault(f => f.Path == entry.FullName);
                        if (manifestEntry != null && !string.IsNullOrEmpty(manifestEntry.ChecksumSha256))
                        {
                            var actualChecksum = await ComputeFileChecksumAsync(stagedPath, ct);
                            if (actualChecksum == manifestEntry.ChecksumSha256)
                            {
                                result.FilesValidated++;
                            }
                            else
                            {
                                result.ValidationFailures++;
                                validationErrors.Add(new ValidationError
                                {
                                    FilePath = entry.FullName,
                                    ErrorType = "ChecksumMismatch",
                                    ExpectedValue = manifestEntry.ChecksumSha256,
                                    ActualValue = actualChecksum,
                                    Message = "File checksum does not match manifest"
                                });
                                continue;
                            }
                        }
                    }

                    staged.Add((entry, stagedPath));
                }

                if (result.ValidationFailures > 0)
                {
                    // Import all-or-nothing: a package that fails validation leaves the
                    // destination exactly as it was.
                    result.ValidationErrors = validationErrors;
                    return result;
                }

                // Phase 2: never overwrite existing files. A byte-identical file is already
                // present and is skipped. A differing file is a conflict: without merge the whole
                // import is refused before anything moves; with merge the existing file is kept.
                var toMove = new List<(ZipArchiveEntry Entry, string StagedPath, string DestinationPath)>();
                var conflicts = new List<string>();
                foreach (var (entry, stagedPath) in staged)
                {
                    var destinationPath = ResolveSafeExtractionPath(extractionRoot, entry.FullName);
                    if (File.Exists(destinationPath))
                    {
                        if (await FilesHaveSameContentAsync(stagedPath, destinationPath, ct))
                            continue;

                        conflicts.Add(entry.FullName);
                        validationErrors.Add(new ValidationError
                        {
                            FilePath = entry.FullName,
                            ErrorType = "DestinationConflict",
                            Message = mergeWithExisting
                                ? "A different file already exists at this path; the existing file was kept."
                                : "A different file already exists at this path."
                        });
                        continue;
                    }

                    toMove.Add((entry, stagedPath, destinationPath));
                }

                if (conflicts.Count > 0 && !mergeWithExisting)
                {
                    throw new InvalidOperationException(
                        $"Import would overwrite {conflicts.Count} existing file(s) (first: '{conflicts[0]}'). " +
                        "Import into an empty destination, or request a merge to keep existing files and add only new ones.");
                }

                foreach (var (entry, stagedPath, destinationPath) in toMove)
                {
                    ct.ThrowIfCancellationRequested();
                    var destinationDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destinationDir))
                    {
                        Directory.CreateDirectory(destinationDir);
                    }

                    File.Move(stagedPath, destinationPath, overwrite: false);
                    if (!string.IsNullOrEmpty(destinationDir))
                    {
                        movedDestinationDirs.Add(destinationDir);
                    }

                    result.FilesExtracted++;
                    result.BytesExtracted += entry.Length;
                }

                // Persist the rename metadata like every other durability path does: without a
                // directory fsync a crash can lose the moves even though the writes completed.
                foreach (var dir in movedDestinationDirs)
                {
                    await AtomicFileWriter.SyncDirectoryAsync(dir, ct);
                }
            }
            finally
            {
                if (Directory.Exists(stagingRoot))
                {
                    try
                    {
                        Directory.Delete(stagingRoot, recursive: true);
                    }
                    catch (IOException)
                    {
                        // Best effort cleanup for temporary quarantine directory.
                    }
                }
            }
        }

        result.ValidationErrors = validationErrors;
        return result;
    }

    /// <summary>
    /// Extracts one entry with cancellation, refusing to write more bytes than the entry's
    /// declared uncompressed length so a crafted archive cannot expand without bound.
    /// </summary>
    private static async Task ExtractEntryBoundedAsync(ZipArchiveEntry entry, string path, CancellationToken ct)
    {
        await using var source = entry.Open();
        await using var target = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81920, FileOptions.Asynchronous);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > entry.Length)
            {
                throw new InvalidDataException(
                    $"Package entry '{entry.FullName}' expands beyond its declared size of {entry.Length:N0} bytes.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        await target.FlushAsync(ct);
        target.Flush(flushToDisk: true);
    }

    private static void EnsureDiskSpaceForImport(string destinationRoot, long declaredBytes)
    {
        long available;
        try
        {
            var root = Path.GetPathRoot(destinationRoot);
            if (string.IsNullOrEmpty(root))
                return;
            available = new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return; // Free space is unknown; the per-entry bound still applies.
        }

        // Staging and the final location share a volume, so the declared size is needed once.
        if (declaredBytes > available)
        {
            throw new IOException(
                $"Package declares {declaredBytes:N0} uncompressed bytes but only {available:N0} bytes are free at the destination.");
        }
    }

    private async Task<bool> FilesHaveSameContentAsync(string left, string right, CancellationToken ct)
    {
        if (new FileInfo(left).Length != new FileInfo(right).Length)
            return false;

        return string.Equals(
            await ComputeFileChecksumAsync(left, ct),
            await ComputeFileChecksumAsync(right, ct),
            StringComparison.Ordinal);
    }

    private static HashSet<string> BuildAllowedEntryPaths(PackageManifest manifest)
    {
        var allowedEntries = new HashSet<string>(StringComparer.Ordinal);
        allowedEntries.Add(ManifestFileName);

        foreach (var file in manifest.Files)
        {
            if (!string.IsNullOrWhiteSpace(file.Path))
                allowedEntries.Add(file.Path);
        }

        if (manifest.SupplementaryFiles != null)
        {
            foreach (var file in manifest.SupplementaryFiles)
            {
                if (!string.IsNullOrWhiteSpace(file.Path))
                    allowedEntries.Add(file.Path);
            }
        }

        return allowedEntries;
    }

    private static bool IsRecognizedSupplementaryPath(string packageRelativePath)
    {
        return packageRelativePath.Equals(ReadmeFileName, StringComparison.Ordinal) ||
               packageRelativePath.StartsWith($"{MetadataDirectory}/", StringComparison.Ordinal) ||
               packageRelativePath.StartsWith($"{ScriptsDirectory}/", StringComparison.Ordinal);
    }

    private static string ResolveSafeExtractionPath(string rootDirectory, string packageRelativePath)
    {
        if (Path.IsPathRooted(packageRelativePath))
            throw new InvalidDataException($"Package entry '{packageRelativePath}' uses an absolute path.");

        var normalizedRoot = Path.GetFullPath(rootDirectory);
        var candidatePath = Path.GetFullPath(Path.Combine(normalizedRoot, packageRelativePath));
        var rootWithSeparator = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!candidatePath.StartsWith(rootWithSeparator, comparison))
        {
            throw new InvalidDataException(
                $"Package entry '{packageRelativePath}' would extract outside the destination directory.");
        }

        return candidatePath;
    }

    private PackageFormat DetectPackageFormat(string path)
    {
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return PackageFormat.Zip;
        if (path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
            return PackageFormat.TarGz;
        if (path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
            return PackageFormat.SevenZip;

        return PackageFormat.Zip; // Default
    }

    private string GetPackageFileName(PackageOptions options)
    {
        var baseName = SanitizeFileName(options.Name);
        var extension = options.Format switch
        {
            PackageFormat.Zip => ".zip",
            PackageFormat.TarGz => ".tar.gz",
            PackageFormat.SevenZip => ".7z",
            _ => ".zip"
        };

        return $"{baseName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}{extension}";
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
    }

    private async Task<string> ComputeFileChecksumAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await Sha256Digest.ComputeBytesAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static int CountTradingDays(DateTime start, DateTime end)
    {
        var count = 0;
        for (var date = start.Date; date <= end.Date; date = date.AddDays(1))
        {
            if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
                count++;
        }
        return count;
    }

    private Dictionary<string, PackageSchema> BuildSchemas(HashSet<string> eventTypes)
    {
        var schemas = new Dictionary<string, PackageSchema>();

        foreach (var eventType in eventTypes)
        {
            var schema = eventType.ToLowerInvariant() switch
            {
                "trade" => new PackageSchema
                {
                    EventType = "Trade",
                    Version = "1.0",
                    Fields = new[]
                    {
                        new PackageSchemaField { Name = "Timestamp", Type = "datetime", Description = "Event timestamp in UTC" },
                        new PackageSchemaField { Name = "Symbol", Type = "string", Description = "Ticker symbol" },
                        new PackageSchemaField { Name = "Price", Type = "decimal", Description = "Trade price" },
                        new PackageSchemaField { Name = "Size", Type = "long", Description = "Trade size in shares" },
                        new PackageSchemaField { Name = "Side", Type = "string", Nullable = true, Description = "Aggressor side (Buy/Sell)" },
                        new PackageSchemaField { Name = "Exchange", Type = "string", Nullable = true, Description = "Exchange code" },
                        new PackageSchemaField { Name = "SequenceNumber", Type = "long", Nullable = true, Description = "Sequence number for ordering" }
                    }
                },
                "bboquote" or "quote" => new PackageSchema
                {
                    EventType = "BboQuote",
                    Version = "1.0",
                    Fields = new[]
                    {
                        new PackageSchemaField { Name = "Timestamp", Type = "datetime", Description = "Event timestamp in UTC" },
                        new PackageSchemaField { Name = "Symbol", Type = "string", Description = "Ticker symbol" },
                        new PackageSchemaField { Name = "BidPrice", Type = "decimal", Description = "Best bid price" },
                        new PackageSchemaField { Name = "BidSize", Type = "long", Description = "Bid size in shares" },
                        new PackageSchemaField { Name = "AskPrice", Type = "decimal", Description = "Best ask price" },
                        new PackageSchemaField { Name = "AskSize", Type = "long", Description = "Ask size in shares" },
                        new PackageSchemaField { Name = "Spread", Type = "decimal", Nullable = true, Description = "Bid-ask spread" },
                        new PackageSchemaField { Name = "MidPrice", Type = "decimal", Nullable = true, Description = "Mid price" }
                    }
                },
                _ => new PackageSchema
                {
                    EventType = eventType,
                    Version = "1.0",
                    Fields = Array.Empty<PackageSchemaField>()
                }
            };

            schemas[eventType] = schema;
        }

        return schemas;
    }
}
