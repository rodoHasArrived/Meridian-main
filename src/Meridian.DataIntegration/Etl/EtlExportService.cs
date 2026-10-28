using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Meridian.Contracts.Etl;
using Meridian.Contracts.Pipeline;
using Meridian.Storage.Packaging;

namespace Meridian.DataIntegration.Etl;

/// <summary>Stages and publishes normalized extracts and portable packages to configured ETL delivery destinations.</summary>
public sealed class EtlExportService : IEtlExportService
{
    private static readonly object DirectoryPublicationLock = new();
    private readonly string _dataRoot;
    private readonly IEnumerable<ISftpFilePublisher> _publishers;

    public EtlExportService(string dataRoot, IEnumerable<ISftpFilePublisher> publishers)
    {
        _dataRoot = dataRoot;
        _publishers = publishers;
    }

    public async Task<EtlExportResult> ExportAsync(IngestionJob job, EtlJobDefinition definition, CancellationToken ct = default)
    {
        var artifacts = new List<string>();
        PackageResult? packageResult = null;

        if (definition.PublishPortablePackage)
        {
            var packager = new PortableDataPackager(_dataRoot);
            var outputDirectory = Path.Combine(_dataRoot, "_etl", "exports", job.JobId, "packages");
            Directory.CreateDirectory(outputDirectory);
            packageResult = await packager.CreatePackageAsync(new PackageOptions
            {
                Name = $"etl-{job.JobId}",
                OutputDirectory = outputDirectory,
                Symbols = definition.Symbols.Length > 0 ? definition.Symbols : null,
                EventTypes = definition.EventTypes.Length > 0 ? definition.EventTypes : null,
                StartDate = definition.FromDateUtc,
                EndDate = definition.ToDateUtc,
                Format = definition.Destination.PackageFormat == EtlPackageFormat.TarGz ? PackageFormat.TarGz : PackageFormat.Zip
            }, ct).ConfigureAwait(false);

            if (!packageResult.Success)
            {
                return new EtlExportResult { Success = false, Error = packageResult.Error, PackageResult = packageResult };
            }

            if (!string.IsNullOrWhiteSpace(packageResult.PackagePath))
                artifacts.Add(packageResult.PackagePath);
        }

        if (definition.PublishNormalizedExtract)
        {
            var exportRoot = Path.Combine(_dataRoot, "_etl", "exports", job.JobId, "normalized");
            await CopyMatchingStorageFilesAsync(exportRoot, definition, ct).ConfigureAwait(false);
            artifacts.Add(exportRoot);
        }

        if (definition.Destination.Kind == EtlDestinationKind.Local && !string.IsNullOrWhiteSpace(definition.Destination.Location))
        {
            foreach (var artifact in artifacts.ToArray())
            {
                var targetPath = Path.Combine(definition.Destination.Location!, Path.GetFileName(artifact));
                if (Directory.Exists(artifact))
                {
                    var localRoot = targetPath;
                    if (PathsOverlap(artifact, localRoot))
                        throw new IOException("The export source and destination directories must not overlap.");
                    var localStaging = localRoot + ".staging-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        Directory.CreateDirectory(localStaging);
                        foreach (var file in Directory.EnumerateFiles(artifact, "*", SearchOption.AllDirectories))
                        {
                            ct.ThrowIfCancellationRequested();
                            var relative = Path.GetRelativePath(artifact, file);
                            var copyPath = Path.Combine(localStaging, relative);
                            Directory.CreateDirectory(Path.GetDirectoryName(copyPath)!);
                            File.Copy(file, copyPath);
                        }
                        ct.ThrowIfCancellationRequested();
                        PublishDirectory(localStaging, localRoot, definition.Destination.OverwriteIfExists);
                    }
                    finally
                    {
                        if (Directory.Exists(localStaging))
                            Directory.Delete(localStaging, recursive: true);
                    }
                }
                else
                {
                    Directory.CreateDirectory(definition.Destination.Location!);
                    File.Copy(artifact, targetPath, overwrite: definition.Destination.OverwriteIfExists);
                }
            }
        }

        if (definition.Destination.Kind == EtlDestinationKind.Sftp)
        {
            var publisher = _publishers.FirstOrDefault();
            if (publisher is null)
                return new EtlExportResult { Success = false, Error = "No SFTP publisher is registered." };

            foreach (var artifact in artifacts)
            {
                await publisher.PublishAsync(definition.Destination, artifact, ct).ConfigureAwait(false);
            }
        }

        return new EtlExportResult { Success = true, ArtifactPaths = artifacts.ToArray(), PackageResult = packageResult };
    }

    private async Task CopyMatchingStorageFilesAsync(string exportRoot, EtlJobDefinition definition, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var stagingRoot = exportRoot + ".staging-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stagingRoot);
        try
        {
            foreach (var file in Directory.EnumerateFiles(_dataRoot, "*.jsonl", SearchOption.AllDirectories)
                         .Concat(Directory.EnumerateFiles(_dataRoot, "*.jsonl.gz", SearchOption.AllDirectories))
                         .Where(path => !path.Contains(Path.DirectorySeparatorChar + "_etl" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                         .Where(path => Matches(path, definition)))
            {
                var relative = Path.GetRelativePath(_dataRoot, file);
                var destination = Path.Combine(stagingRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (definition.FromDateUtc.HasValue || definition.ToDateUtc.HasValue)
                {
                    await CopyDateRangeAsync(file, destination, definition, ct).ConfigureAwait(false);
                    continue;
                }
                await using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                await using var destinationStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await source.CopyToAsync(destinationStream, ct).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
            PublishDirectory(stagingRoot, exportRoot, overwrite: true);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
        }
    }

    private static bool PathsOverlap(string first, string second)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var firstPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(first));
        var secondPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(second));
        return firstPath.Equals(secondPath, comparison) ||
            firstPath.StartsWith(secondPath + Path.DirectorySeparatorChar, comparison) ||
            secondPath.StartsWith(firstPath + Path.DirectorySeparatorChar, comparison);
    }

    private static void PublishDirectory(string staging, string destination, bool overwrite)
    {
        string? previous = null;
        // Serialize the short publication/rollback sequence; all I/O to build the snapshot is complete.
        lock (DirectoryPublicationLock)
        {
            if (Directory.Exists(destination))
            {
                if (!overwrite)
                    throw new IOException($"Export destination already exists: {destination}");
                previous = destination + ".previous-" + Guid.NewGuid().ToString("N");
                Directory.Move(destination, previous);
            }
            try
            {
                Directory.Move(staging, destination);
            }
            catch
            {
                if (previous is not null)
                    Directory.Move(previous, destination);
                throw;
            }
        }
        if (previous is not null)
            Directory.Delete(previous, recursive: true);
    }

    private static async Task CopyDateRangeAsync(string sourcePath, string destination, EtlJobDefinition definition, CancellationToken ct)
    {
        // Bounds are inclusive UTC calendar dates, matching the portable-package contract.
        var from = definition.FromDateUtc?.Date;
        var to = definition.ToDateUtc?.Date;
        var temporaryPath = destination + ".tmp";
        var recordsWritten = 0;
        try
        {
            await using (var source = File.OpenRead(sourcePath))
            await using (var target = File.Create(temporaryPath))
            {
                var compressed = sourcePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
                await using var input = compressed ? new GZipStream(source, CompressionMode.Decompress, leaveOpen: true) : (Stream)source;
                await using var output = compressed ? new GZipStream(target, CompressionLevel.Optimal, leaveOpen: true) : (Stream)target;
                using var reader = new StreamReader(input);
                await using var writer = new StreamWriter(output);
                while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
                {
                    ct.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if ((!root.TryGetProperty("timestamp", out var timestamp) && !root.TryGetProperty("Timestamp", out timestamp)) ||
                        timestamp.ValueKind != JsonValueKind.String ||
                        !DateTimeOffset.TryParse(timestamp.GetString(), CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal, out var instant))
                    {
                        throw new InvalidDataException($"Cannot apply the export date range to a record without a valid timestamp in {Path.GetFileName(sourcePath)}.");
                    }

                    var date = instant.UtcDateTime.Date;
                    if ((from.HasValue && date < from.Value) || (to.HasValue && date > to.Value))
                        continue;

                    await writer.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
                    recordsWritten++;
                }
            }

            ct.ThrowIfCancellationRequested();
            if (recordsWritten > 0)
                File.Move(temporaryPath, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static bool Matches(string path, EtlJobDefinition definition)
    {
        if (definition.Symbols.Length > 0 && !definition.Symbols.Any(symbol => path.Contains(Path.DirectorySeparatorChar + symbol + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || path.Contains(symbol + "_", StringComparison.OrdinalIgnoreCase)))
            return false;

        if (definition.EventTypes.Length > 0 && !definition.EventTypes.Any(type => path.Contains(Path.DirectorySeparatorChar + type + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || path.Contains(type, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }
}
