using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Application.Serialization;

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

            var manifestEntry = archive.GetEntry(ManifestFileName);
            if (manifestEntry == null)
                return null;

            await using var entryStream = manifestEntry.Open();
            return await JsonSerializer.DeserializeAsync<PackageManifest>(entryStream, cancellationToken: ct);
        }

        // For tar.gz, simplified reading
        await using var fileStream = File.OpenRead(packagePath);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzipStream);

        var line = await reader.ReadLineAsync(ct);
        if (line?.StartsWith("__MANIFEST__:") != true)
            return null;

        var jsonBuilder = new StringBuilder();
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            if (line == "__END_MANIFEST__")
                break;
            jsonBuilder.AppendLine(line);
        }

        return JsonSerializer.Deserialize<PackageManifest>(jsonBuilder.ToString());
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

            // Pre-scan all entries for path traversal before extracting anything.
            // This ensures no files reach the destination directory if any entry is malicious.
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue; // Skip directories

                ResolveSafeExtractionPath(stagingRoot, entry.FullName);
            }

            Directory.CreateDirectory(stagingRoot);

            try
            {
                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();

                    if (string.IsNullOrEmpty(entry.Name))
                        continue; // Skip directories

                    if (!allowedEntries.Contains(entry.FullName) && !IsRecognizedSupplementaryPath(entry.FullName))
                    {
                        throw new InvalidDataException(
                            $"Package contains unexpected entry '{entry.FullName}' that is not declared in the manifest.");
                    }

                    var stagedPath = ResolveSafeExtractionPath(stagingRoot, entry.FullName);
                    var stagedDir = Path.GetDirectoryName(stagedPath);
                    if (!string.IsNullOrEmpty(stagedDir))
                    {
                        Directory.CreateDirectory(stagedDir);
                    }

                    entry.ExtractToFile(stagedPath, overwrite: true);

                    // Validate checksum while still in quarantine
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

                                File.Delete(stagedPath);
                                continue;
                            }
                        }
                    }

                    var destinationPath = ResolveSafeExtractionPath(extractionRoot, entry.FullName);
                    var destinationDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destinationDir))
                    {
                        Directory.CreateDirectory(destinationDir);
                    }

                    File.Move(stagedPath, destinationPath, overwrite: true);
                    result.FilesExtracted++;
                    result.BytesExtracted += entry.Length;
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
        var hash = await SHA256.HashDataAsync(stream, ct);
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
