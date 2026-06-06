using Meridian.Contracts.Etl;
using Meridian.Contracts.Pipeline;
using Meridian.Storage.Packaging;

namespace Meridian.DataIntegration.Etl;

public sealed class EtlExportService : IEtlExportService
{
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
                    Directory.CreateDirectory(localRoot);
                    foreach (var file in Directory.EnumerateFiles(artifact, "*", SearchOption.AllDirectories))
                    {
                        var relative = Path.GetRelativePath(artifact, file);
                        var copyPath = Path.Combine(localRoot, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(copyPath)!);
                        File.Copy(file, copyPath, overwrite: definition.Destination.OverwriteIfExists);
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
        Directory.CreateDirectory(exportRoot);
        foreach (var file in Directory.EnumerateFiles(_dataRoot, "*.jsonl", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(_dataRoot, "*.jsonl.gz", SearchOption.AllDirectories))
                     .Where(path => !path.Contains(Path.DirectorySeparatorChar + "_etl" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                     .Where(path => Matches(path, definition)))
        {
            var relative = Path.GetRelativePath(_dataRoot, file);
            var destination = Path.Combine(exportRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            await using var destinationStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await source.CopyToAsync(destinationStream, ct).ConfigureAwait(false);
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
