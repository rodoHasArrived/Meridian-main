using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Application.Logging;
using Meridian.Application.Serialization;
using Meridian.Infrastructure.Contracts;
using Meridian.Storage.Archival;
using Serilog;

namespace Meridian.Storage.Packaging;

/// <summary>
/// Service for creating and importing portable data packages.
/// Packages include all data, metadata, quality reports, and scripts needed
/// for data portability and sharing.
/// Split into partial classes: Creation, Validation, Scripts.
/// </summary>
[ImplementsAdr("ADR-002", "Portable packaging for tiered storage export")]
public sealed partial class PortableDataPackager
{
    private readonly ILogger _log = LoggingSetup.ForContext<PortableDataPackager>();
    private readonly string _dataRoot;
    private readonly CompressionProfileManager? _compressionManager;

    private const string ManifestFileName = "manifest.json";
    private const string DataDirectory = "data";
    private const string MetadataDirectory = "metadata";
    private const string ScriptsDirectory = "scripts";
    private const string QualityReportFileName = "quality_report.json";
    private const string DataDictionaryFileName = "data_dictionary.md";
    private const string ReadmeFileName = "README.md";

    /// <summary>
    /// Event raised to report packaging progress.
    /// </summary>
    public event EventHandler<PackageProgress>? ProgressChanged;

    public PortableDataPackager(string dataRoot, CompressionProfileManager? compressionManager = null)
    {
        _dataRoot = dataRoot;
        _compressionManager = compressionManager;
    }

    /// <summary>
    /// Create a portable data package.
    /// </summary>
    public async Task<PackageResult> CreatePackageAsync(
        PackageOptions options,
        CancellationToken ct = default)
    {
        var result = new PackageResult { StartedAt = DateTime.UtcNow };
        var warnings = new List<string>();

        try
        {
            _log.Information("Starting package creation: {PackageName}", options.Name);
            ReportProgress(result.JobId, PackageStage.Initializing, 0, 0, 0, 0);

            // Ensure output directory exists
            Directory.CreateDirectory(options.OutputDirectory);

            // Scan for source files
            ReportProgress(result.JobId, PackageStage.Scanning, 0, 0, 0, 0);
            var sourceFiles = await ScanSourceFilesAsync(options, ct);

            if (sourceFiles.Count == 0)
            {
                return PackageResult.CreateFailure("No data files found matching the specified criteria");
            }

            _log.Information("Found {FileCount} files to package", sourceFiles.Count);

            // Build manifest
            ReportProgress(result.JobId, PackageStage.GeneratingManifest, 0, sourceFiles.Count, 0, 0);
            var manifest = await BuildManifestAsync(sourceFiles, options, ct);

            // Determine output file name
            var packageFileName = GetPackageFileName(options);
            var packagePath = Path.Combine(options.OutputDirectory, packageFileName);

            // Create the package
            ReportProgress(result.JobId, PackageStage.Writing, 0, sourceFiles.Count, 0, manifest.UncompressedSizeBytes);

            await CreatePackageFileAsync(packagePath, sourceFiles, manifest, options, ct);

            // Compute package checksum
            ReportProgress(result.JobId, PackageStage.ComputingChecksums, sourceFiles.Count, sourceFiles.Count, 0, 0);
            var packageChecksum = await ComputeFileChecksumAsync(packagePath, ct);
            var packageInfo = new FileInfo(packagePath);

            manifest.PackageChecksum = packageChecksum;
            manifest.PackageSizeBytes = packageInfo.Length;

            // Update the manifest inside the package with final checksum
            await UpdateManifestInPackageAsync(packagePath, manifest, options.Format, ct);

            // Build result
            result = PackageResult.CreateSuccess(packagePath, manifest);
            result.FilesIncluded = sourceFiles.Count;
            result.TotalEvents = manifest.TotalEvents;
            result.Symbols = manifest.Symbols;
            result.EventTypes = manifest.EventTypes;
            result.DateRange = manifest.DateRange;
            result.PackageSizeBytes = packageInfo.Length;
            result.UncompressedSizeBytes = manifest.UncompressedSizeBytes;
            result.PackageChecksum = packageChecksum;
            result.Warnings = warnings.ToArray();

            ReportProgress(result.JobId, PackageStage.Complete, sourceFiles.Count, sourceFiles.Count,
                manifest.PackageSizeBytes, manifest.UncompressedSizeBytes);

            _log.Information(
                "Package created successfully: {PackagePath} ({SizeBytes:N0} bytes, {CompressionRatio:F2}x compression)",
                packagePath, packageInfo.Length, result.CompressionRatio);

            return result;
        }
        catch (OperationCanceledException)
        {
            _log.Warning("Package creation cancelled");
            return PackageResult.CreateFailure("Operation cancelled");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Package creation failed");
            ReportProgress(result.JobId, PackageStage.Failed, 0, 0, 0, 0);
            return PackageResult.CreateFailure(ex.Message);
        }
    }

    /// <summary>
    /// Import/extract a portable data package.
    /// </summary>
    public async Task<ImportResult> ImportPackageAsync(
        string packagePath,
        string destinationDirectory,
        bool validateChecksums = true,
        bool mergeWithExisting = false,
        CancellationToken ct = default)
    {
        var result = new ImportResult
        {
            StartedAt = DateTime.UtcNow,
            SourcePath = packagePath,
            DestinationPath = destinationDirectory
        };

        var warnings = new List<string>();
        var validationErrors = new List<ValidationError>();

        try
        {
            _log.Information("Starting package import: {PackagePath}", packagePath);
            ReportProgress(result.JobId, PackageStage.Initializing, 0, 0, 0, 0);

            if (!File.Exists(packagePath))
            {
                return ImportResult.CreateFailure(packagePath, $"Package file not found: {packagePath}");
            }

            // Determine package format
            var format = DetectPackageFormat(packagePath);

            // Extract and read manifest first
            ReportProgress(result.JobId, PackageStage.Scanning, 0, 0, 0, 0);
            var manifest = await ReadManifestFromPackageAsync(packagePath, format, ct);

            if (manifest == null)
            {
                return ImportResult.CreateFailure(packagePath, "Package does not contain a valid manifest");
            }

            result.PackageId = manifest.PackageId;
            result.Manifest = manifest;

            // Create destination directory
            Directory.CreateDirectory(destinationDirectory);

            // Extract files
            ReportProgress(result.JobId, PackageStage.Processing, 0, manifest.TotalFiles, 0, manifest.UncompressedSizeBytes);

            var extractionResult = await ExtractPackageAsync(
                packagePath, destinationDirectory, manifest, format, validateChecksums, ct);

            result.FilesExtracted = extractionResult.FilesExtracted;
            result.BytesExtracted = extractionResult.BytesExtracted;
            result.FilesValidated = extractionResult.FilesValidated;
            result.ValidationFailures = extractionResult.ValidationFailures;
            validationErrors.AddRange(extractionResult.ValidationErrors);

            if (extractionResult.ValidationFailures > 0 && validateChecksums)
            {
                warnings.Add($"{extractionResult.ValidationFailures} files failed checksum validation");
            }

            result.Symbols = manifest.Symbols;
            result.EventTypes = manifest.EventTypes;
            result.DateRange = manifest.DateRange;
            result.Warnings = warnings.ToArray();
            result.ValidationErrors = validationErrors.ToArray();
            result.Success = extractionResult.ValidationFailures == 0 || !validateChecksums;
            result.CompletedAt = DateTime.UtcNow;

            if (!result.Success)
            {
                result.Error = "Some files failed checksum validation";
            }

            ReportProgress(result.JobId, PackageStage.Complete,
                result.FilesExtracted, manifest.TotalFiles, result.BytesExtracted, manifest.UncompressedSizeBytes);

            _log.Information(
                "Package imported: {FilesExtracted} files, {BytesExtracted:N0} bytes, {ValidationFailures} validation failures",
                result.FilesExtracted, result.BytesExtracted, result.ValidationFailures);

            return result;
        }
        catch (OperationCanceledException)
        {
            _log.Warning("Package import cancelled");
            return ImportResult.CreateFailure(packagePath, "Operation cancelled");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Package import failed");
            ReportProgress(result.JobId, PackageStage.Failed, 0, 0, 0, 0);
            return ImportResult.CreateFailure(packagePath, ex.Message);
        }
    }

    /// <summary>
    /// Validate a package without extracting.
    /// </summary>
    public async Task<PackageValidationResult> ValidatePackageAsync(
        string packagePath,
        CancellationToken ct = default)
    {
        try
        {
            _log.Information("Validating package: {PackagePath}", packagePath);

            if (!File.Exists(packagePath))
            {
                return new PackageValidationResult
                {
                    IsValid = false,
                    Error = $"Package file not found: {packagePath}"
                };
            }

            var format = DetectPackageFormat(packagePath);
            var manifest = await ReadManifestFromPackageAsync(packagePath, format, ct);

            if (manifest == null)
            {
                return new PackageValidationResult
                {
                    IsValid = false,
                    Error = "Package does not contain a valid manifest"
                };
            }

            var issues = new List<string>();

            // Validate manifest version
            if (string.IsNullOrEmpty(manifest.PackageVersion))
            {
                issues.Add("Missing package version in manifest");
            }

            // Validate required fields
            if (string.IsNullOrEmpty(manifest.PackageId))
            {
                issues.Add("Missing package ID in manifest");
            }

            if (manifest.Files == null || manifest.Files.Length == 0)
            {
                issues.Add("No files listed in manifest");
            }

            // Verify files exist in package
            var missingFiles = await VerifyFilesInPackageAsync(packagePath, manifest, format, ct);
            if (missingFiles.Count > 0)
            {
                issues.Add($"{missingFiles.Count} files listed in manifest are missing from package");
            }

            return new PackageValidationResult
            {
                IsValid = issues.Count == 0,
                Manifest = manifest,
                Issues = issues.ToArray(),
                MissingFiles = missingFiles.ToArray()
            };
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Package validation failed");
            return new PackageValidationResult
            {
                IsValid = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// List contents of a package without extracting.
    /// </summary>
    public async Task<PackageContents> ListPackageContentsAsync(
        string packagePath,
        CancellationToken ct = default)
    {
        var format = DetectPackageFormat(packagePath);
        var manifest = await ReadManifestFromPackageAsync(packagePath, format, ct);

        if (manifest == null)
        {
            throw new InvalidOperationException("Package does not contain a valid manifest");
        }

        return new PackageContents
        {
            PackageId = manifest.PackageId,
            Name = manifest.Name,
            Description = manifest.Description,
            CreatedAt = manifest.CreatedAt,
            TotalFiles = manifest.TotalFiles,
            TotalEvents = manifest.TotalEvents,
            PackageSizeBytes = manifest.PackageSizeBytes,
            UncompressedSizeBytes = manifest.UncompressedSizeBytes,
            Symbols = manifest.Symbols,
            EventTypes = manifest.EventTypes,
            DateRange = manifest.DateRange,
            Files = manifest.Files,
            Quality = manifest.Quality
        };
    }

    private void ReportProgress(string jobId, PackageStage stage, int filesProcessed, int totalFiles,
        long bytesProcessed, long totalBytes)
    {
        ProgressChanged?.Invoke(this, new PackageProgress
        {
            JobId = jobId,
            Stage = stage,
            FilesProcessed = filesProcessed,
            TotalFiles = totalFiles,
            BytesProcessed = bytesProcessed,
            TotalBytes = totalBytes
        });
    }

    private sealed class SourceFileInfo
    {
        public string FullPath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string? Symbol { get; set; }
        public string? EventType { get; set; }
        public DateTime? Date { get; set; }
        public string? Source { get; set; }
        public string? Format { get; set; }
        public bool IsCompressed { get; set; }
        public string? CompressionType { get; set; }
        public long SizeBytes { get; set; }
    }

    private sealed class ExtractionResult
    {
        public int FilesExtracted { get; set; }
        public long BytesExtracted { get; set; }
        public int FilesValidated { get; set; }
        public int ValidationFailures { get; set; }
        public List<ValidationError> ValidationErrors { get; set; } = new();
    }
}

/// <summary>
/// Result of package validation.
/// </summary>
public sealed class PackageValidationResult
{
    public bool IsValid { get; set; }
    public PackageManifest? Manifest { get; set; }
    public string[]? Issues { get; set; }
    public string[]? MissingFiles { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Summary of package contents.
/// </summary>
public sealed class PackageContents
{
    public string PackageId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TotalFiles { get; set; }
    public long TotalEvents { get; set; }
    public long PackageSizeBytes { get; set; }
    public long UncompressedSizeBytes { get; set; }
    public string[] Symbols { get; set; } = Array.Empty<string>();
    public string[] EventTypes { get; set; } = Array.Empty<string>();
    public PackageDateRange? DateRange { get; set; }
    public PackageFileEntry[] Files { get; set; } = Array.Empty<PackageFileEntry>();
    public PackageQualityMetrics? Quality { get; set; }
}
