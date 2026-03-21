using Meridian.Application.Config;
using Meridian.Application.ResultTypes;
using Meridian.Storage.Packaging;
using Serilog;

namespace Meridian.Application.Commands;

/// <summary>
/// Handles all package-related CLI commands:
/// --package, --import-package, --list-package, --validate-package
/// </summary>
internal sealed class PackageCommands : ICliCommand
{
    private readonly AppConfig _cfg;
    private readonly ILogger _log;

    public PackageCommands(AppConfig cfg, ILogger log)
    {
        _cfg = cfg;
        _log = log;
    }

    public bool CanHandle(string[] args)
    {
        return CliArguments.HasFlag(args, "--package") ||
            CliArguments.HasFlag(args, "--import-package") ||
            CliArguments.HasFlag(args, "--list-package") ||
            CliArguments.HasFlag(args, "--validate-package");
    }

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        if (CliArguments.HasFlag(args, "--package"))
            return await RunCreateAsync(args, ct);

        if (CliArguments.HasFlag(args, "--import-package"))
        {
            var path = CliArguments.RequireValue(args, "--import-package", "--import-package ./packages/data.zip");
            if (path is null)
                return CliResult.Fail(ErrorCode.RequiredFieldMissing);
            return await RunImportAsync(path, args, ct);
        }

        if (CliArguments.HasFlag(args, "--list-package"))
        {
            var path = CliArguments.RequireValue(args, "--list-package", "--list-package ./packages/data.zip");
            if (path is null)
                return CliResult.Fail(ErrorCode.RequiredFieldMissing);
            return await RunListAsync(path, ct);
        }

        if (CliArguments.HasFlag(args, "--validate-package"))
        {
            var path = CliArguments.RequireValue(args, "--validate-package", "--validate-package ./packages/data.zip");
            if (path is null)
                return CliResult.Fail(ErrorCode.RequiredFieldMissing);
            return await RunValidateAsync(path, ct);
        }

        return CliResult.Fail(ErrorCode.Unknown);
    }

    private async Task<CliResult> RunCreateAsync(string[] args, CancellationToken ct)
    {
        _log.Information("Creating portable data package...");

        var options = new PackageOptions
        {
            Name = CliArguments.GetValue(args, "--package-name") ?? $"market-data-{DateTime.UtcNow:yyyyMMdd}",
            Description = CliArguments.GetValue(args, "--package-description"),
            OutputDirectory = CliArguments.GetValue(args, "--package-output") ?? "packages",
            IncludeQualityReport = !CliArguments.HasFlag(args, "--no-quality-report"),
            IncludeDataDictionary = !CliArguments.HasFlag(args, "--no-data-dictionary"),
            IncludeLoaderScripts = !CliArguments.HasFlag(args, "--no-loader-scripts"),
            VerifyChecksums = !CliArguments.HasFlag(args, "--skip-checksums")
        };

        var symbolsArg = CliArguments.GetValue(args, "--package-symbols");
        if (!string.IsNullOrWhiteSpace(symbolsArg))
            options.Symbols = symbolsArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var eventTypesArg = CliArguments.GetValue(args, "--package-events");
        if (!string.IsNullOrWhiteSpace(eventTypesArg))
            options.EventTypes = eventTypesArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var fromArg = CliArguments.GetValue(args, "--package-from");
        if (DateTime.TryParse(fromArg, out var from))
            options.StartDate = from;

        var toArg = CliArguments.GetValue(args, "--package-to");
        if (DateTime.TryParse(toArg, out var to))
            options.EndDate = to;

        var formatArg = CliArguments.GetValue(args, "--package-format");
        if (!string.IsNullOrWhiteSpace(formatArg))
        {
            options.Format = formatArg.ToLowerInvariant() switch
            {
                "zip" => PackageFormat.Zip,
                "tar.gz" or "targz" or "tgz" => PackageFormat.TarGz,
                "7z" or "7zip" => PackageFormat.SevenZip,
                _ => PackageFormat.Zip
            };
        }

        var compressionArg = CliArguments.GetValue(args, "--package-compression");
        if (!string.IsNullOrWhiteSpace(compressionArg))
        {
            options.CompressionLevel = compressionArg.ToLowerInvariant() switch
            {
                "none" => PackageCompressionLevel.None,
                "fast" => PackageCompressionLevel.Fast,
                "balanced" => PackageCompressionLevel.Balanced,
                "maximum" or "max" => PackageCompressionLevel.Maximum,
                _ => PackageCompressionLevel.Balanced
            };
        }

        var packager = new PortableDataPackager(_cfg.DataRoot);
        packager.ProgressChanged += (_, progress) =>
        {
            var percent = progress.TotalFiles > 0
                ? (double)progress.FilesProcessed / progress.TotalFiles * 100
                : 0;
            Console.Write($"\r[{progress.Stage}] {progress.FilesProcessed}/{progress.TotalFiles} files ({percent:F1}%)    ");
        };

        var result = await packager.CreatePackageAsync(options, ct);
        Console.WriteLine();

        if (result.Success)
        {
            Console.WriteLine();
            Console.WriteLine($"  Package: {result.PackagePath}");
            Console.WriteLine($"  Size: {result.PackageSizeBytes:N0} bytes");
            Console.WriteLine($"  Files: {result.FilesIncluded:N0}");
            Console.WriteLine($"  Events: {result.TotalEvents:N0}");
            Console.WriteLine($"  Symbols: {string.Join(", ", result.Symbols)}");

            if (result.Warnings.Length > 0)
            {
                Console.WriteLine("Warnings:");
                foreach (var warning in result.Warnings)
                    Console.WriteLine($"  - {warning}");
            }

            _log.Information("Package created: {PackagePath} ({SizeBytes:N0} bytes)",
                result.PackagePath, result.PackageSizeBytes);
            return CliResult.Ok();
        }

        Console.Error.WriteLine($"Error: {result.Error}");
        _log.Error("Package creation failed: {Error}", result.Error);
        return CliResult.Fail(ErrorCode.WriteFailed);
    }

    private async Task<CliResult> RunImportAsync(string packagePath, string[] args, CancellationToken ct)
    {
        _log.Information("Importing package: {PackagePath}", packagePath);

        var destinationDir = CliArguments.GetValue(args, "--import-destination") ?? _cfg.DataRoot;
        var validateChecksums = !CliArguments.HasFlag(args, "--skip-validation");
        var mergeWithExisting = CliArguments.HasFlag(args, "--merge");

        var packager = new PortableDataPackager(_cfg.DataRoot);
        packager.ProgressChanged += (_, progress) =>
        {
            var percent = progress.TotalFiles > 0
                ? (double)progress.FilesProcessed / progress.TotalFiles * 100
                : 0;
            Console.Write($"\r[{progress.Stage}] {progress.FilesProcessed}/{progress.TotalFiles} files ({percent:F1}%)    ");
        };

        var result = await packager.ImportPackageAsync(packagePath, destinationDir, validateChecksums, mergeWithExisting, ct);
        Console.WriteLine();

        if (result.Success)
        {
            Console.WriteLine($"  Source: {result.SourcePath}");
            Console.WriteLine($"  Files Extracted: {result.FilesExtracted:N0}");
            Console.WriteLine($"  Bytes Extracted: {result.BytesExtracted:N0}");

            if (result.Warnings.Length > 0)
            {
                Console.WriteLine("Warnings:");
                foreach (var warning in result.Warnings)
                    Console.WriteLine($"  - {warning}");
            }

            _log.Information("Package imported: {FilesExtracted} files", result.FilesExtracted);
            return CliResult.Ok();
        }

        Console.Error.WriteLine($"Error: {result.Error}");
        if (result.ValidationErrors?.Length > 0)
        {
            Console.Error.WriteLine("\nValidation Errors:");
            foreach (var error in result.ValidationErrors)
                Console.Error.WriteLine($"  - {error.FilePath}: {error.Message}");
        }

        _log.Error("Package import failed: {Error}", result.Error);
        return CliResult.Fail(ErrorCode.WriteFailed);
    }

    private async Task<CliResult> RunListAsync(string packagePath, CancellationToken ct)
    {
        _log.Information("Listing package contents: {PackagePath}", packagePath);

        var packager = new PortableDataPackager(".");

        try
        {
            var contents = await packager.ListPackageContentsAsync(packagePath, ct);

            Console.WriteLine($"  Name: {contents.Name}");
            Console.WriteLine($"  Package ID: {contents.PackageId}");
            if (!string.IsNullOrEmpty(contents.Description))
                Console.WriteLine($"  Description: {contents.Description}");
            Console.WriteLine($"  Files: {contents.TotalFiles:N0}");
            Console.WriteLine($"  Events: {contents.TotalEvents:N0}");
            Console.WriteLine($"  Symbols: {string.Join(", ", contents.Symbols)}");
            Console.WriteLine($"  Event Types: {string.Join(", ", contents.EventTypes)}");
            Console.WriteLine();

            foreach (var file in contents.Files.Take(20))
            {
                var size = file.SizeBytes > 1024 * 1024
                    ? $"{file.SizeBytes / (1024.0 * 1024.0):F1} MB"
                    : $"{file.SizeBytes / 1024.0:F1} KB";
                Console.WriteLine($"    {file.Path} ({size}, {file.EventCount:N0} events)");
            }

            if (contents.Files.Length > 20)
                Console.WriteLine($"    ... and {contents.Files.Length - 20} more files");

            return CliResult.Ok();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error reading package: {ex.Message}");
            _log.Error(ex, "Failed to list package contents");
            return CliResult.Fail(ErrorCode.ReadFailed);
        }
    }

    private async Task<CliResult> RunValidateAsync(string packagePath, CancellationToken ct)
    {
        _log.Information("Validating package: {PackagePath}", packagePath);

        var packager = new PortableDataPackager(".");
        var result = await packager.ValidatePackageAsync(packagePath, ct);

        if (result.IsValid)
        {
            Console.WriteLine($"  Package: {packagePath} - VALID");
            if (result.Manifest != null)
            {
                Console.WriteLine($"  Name: {result.Manifest.Name}");
                Console.WriteLine($"  Files: {result.Manifest.TotalFiles:N0}");
            }
            _log.Information("Package validation passed: {PackagePath}", packagePath);
            return CliResult.Ok();
        }

        Console.WriteLine($"  Package: {packagePath} - INVALID");
        if (!string.IsNullOrEmpty(result.Error))
            Console.WriteLine($"  Error: {result.Error}");

        if (result.Issues?.Length > 0)
        {
            Console.WriteLine("\n  Issues:");
            foreach (var issue in result.Issues)
                Console.WriteLine($"    - {issue}");
        }

        if (result.MissingFiles?.Length > 0)
        {
            Console.WriteLine("\n  Missing Files:");
            foreach (var file in result.MissingFiles.Take(10))
                Console.WriteLine($"    - {file}");
        }

        _log.Warning("Package validation failed: {PackagePath}", packagePath);
        return CliResult.Fail(ErrorCode.ValidationFailed);
    }
}
