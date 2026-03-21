using System.Text.Json;
using Meridian.Storage.Packaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Meridian.Application.UI;

/// <summary>
/// HTTP API endpoints for the Portable Data Packager.
/// </summary>
public static class PackagingEndpoints
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Maps all packaging-related endpoints.
    /// </summary>
    public static void MapPackagingEndpoints(this IEndpointRouteBuilder app, string dataRoot)
    {
        var packager = new PortableDataPackager(dataRoot);

        // ==================== PACKAGE CREATION ====================

        // Create a new portable data package.
        // POST /api/packaging/create
        app.MapPost("/api/packaging/create", async (PackageRequest request, CancellationToken ct) =>
        {
            try
            {
                var options = new PackageOptions
                {
                    Name = request.Name ?? $"market-data-{DateTime.UtcNow:yyyyMMdd}",
                    Description = request.Description,
                    OutputDirectory = request.OutputDirectory ?? "packages",
                    Symbols = request.Symbols,
                    EventTypes = request.EventTypes,
                    StartDate = request.StartDate,
                    EndDate = request.EndDate,
                    Format = ParseFormat(request.Format),
                    CompressionLevel = ParseCompression(request.CompressionLevel),
                    IncludeQualityReport = request.IncludeQualityReport ?? true,
                    IncludeDataDictionary = request.IncludeDataDictionary ?? true,
                    IncludeLoaderScripts = request.IncludeLoaderScripts ?? true,
                    VerifyChecksums = request.VerifyChecksums ?? true,
                    Tags = request.Tags,
                    CustomMetadata = request.CustomMetadata
                };

                var result = await packager.CreatePackageAsync(options, ct);

                return result.Success
                    ? Results.Json(result, s_jsonOptions)
                    : Results.BadRequest(new { error = result.Error, warnings = result.Warnings });
            }
            catch (OperationCanceledException)
            {
                return Results.StatusCode(499); // Client Closed Request
            }
            catch (Exception ex)
            {
                return Results.Problem($"Package creation failed: {ex.Message}");
            }
        });

        // ==================== PACKAGE IMPORT ====================

        // Import a package into storage.
        // POST /api/packaging/import
        app.MapPost("/api/packaging/import", async (ImportRequest request, CancellationToken ct) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.PackagePath))
                {
                    return Results.BadRequest(new { error = "PackagePath is required" });
                }

                if (request.PackagePath.Contains("..") || request.PackagePath.Contains('\0'))
                {
                    return Results.BadRequest(new { error = "Invalid PackagePath: traversal sequences not allowed" });
                }

                if (request.DestinationDirectory is not null &&
                    (request.DestinationDirectory.Contains("..") || request.DestinationDirectory.Contains('\0')))
                {
                    return Results.BadRequest(new { error = "Invalid DestinationDirectory: traversal sequences not allowed" });
                }

                var result = await packager.ImportPackageAsync(
                    request.PackagePath,
                    request.DestinationDirectory ?? dataRoot,
                    request.ValidateChecksums ?? true,
                    request.MergeWithExisting ?? false,
                    ct);

                return result.Success
                    ? Results.Json(result, s_jsonOptions)
                    : Results.BadRequest(new { error = result.Error, validationErrors = result.ValidationErrors });
            }
            catch (OperationCanceledException)
            {
                return Results.StatusCode(499);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Package import failed: {ex.Message}");
            }
        });

        // ==================== PACKAGE VALIDATION ====================

        // Validate a package without extracting.
        // POST /api/packaging/validate
        app.MapPost("/api/packaging/validate", async (ValidateRequest request, CancellationToken ct) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.PackagePath))
                {
                    return Results.BadRequest(new { error = "PackagePath is required" });
                }

                if (request.PackagePath.Contains("..") || request.PackagePath.Contains('\0'))
                {
                    return Results.BadRequest(new { error = "Invalid PackagePath: traversal sequences not allowed" });
                }

                var result = await packager.ValidatePackageAsync(request.PackagePath, ct);

                return Results.Json(new
                {
                    isValid = result.IsValid,
                    packagePath = request.PackagePath,
                    manifest = result.Manifest,
                    issues = result.Issues,
                    missingFiles = result.MissingFiles,
                    error = result.Error
                }, s_jsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Package validation failed: {ex.Message}");
            }
        });

        // ==================== PACKAGE CONTENTS ====================

        // List contents of a package.
        // GET /api/packaging/contents?path={packagePath}
        app.MapGet("/api/packaging/contents", async (string path, CancellationToken ct) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return Results.BadRequest(new { error = "Path query parameter is required" });
                }

                // Validate path stays within the data root
                if (path.Contains("..") || path.Contains('\0'))
                {
                    return Results.BadRequest(new { error = "Invalid path: traversal sequences not allowed" });
                }

                var contents = await packager.ListPackageContentsAsync(path, ct);

                return Results.Json(contents, s_jsonOptions);
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { error = $"Package not found: {path}" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to read package: {ex.Message}");
            }
        });

        // ==================== LIST PACKAGES ====================

        // List available packages in the packages directory.
        // GET /api/packaging/list
        app.MapGet("/api/packaging/list", (string? directory) =>
        {
            try
            {
                var defaultPackagesDir = Path.GetFullPath(Path.Combine(dataRoot, "..", "packages"));
                string packagesDir;

                if (directory is not null)
                {
                    // Validate user-supplied directory stays within the project boundary
                    if (directory.Contains("..") || directory.Contains('\0'))
                    {
                        return Results.BadRequest(new { error = "Invalid directory: traversal sequences not allowed" });
                    }
                    packagesDir = Path.GetFullPath(directory);
                }
                else
                {
                    packagesDir = defaultPackagesDir;
                }

                if (!Directory.Exists(packagesDir))
                {
                    return Results.Json(new { packages = Array.Empty<object>() }, s_jsonOptions);
                }

                var packages = Directory.GetFiles(packagesDir, "*.zip")
                    .Concat(Directory.GetFiles(packagesDir, "*.tar.gz"))
                    .Concat(Directory.GetFiles(packagesDir, "*.7z"))
                    .Select(p => new
                    {
                        path = p,
                        fileName = Path.GetFileName(p),
                        sizeBytes = new FileInfo(p).Length,
                        createdAt = File.GetCreationTimeUtc(p),
                        modifiedAt = File.GetLastWriteTimeUtc(p)
                    })
                    .OrderByDescending(p => p.createdAt)
                    .ToArray();

                return Results.Json(new { packages }, s_jsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to list packages: {ex.Message}");
            }
        });

        // ==================== DELETE PACKAGE ====================

        // Delete a package file.
        // DELETE /api/packaging/{fileName}
        app.MapDelete("/api/packaging/{fileName}", (string fileName, string? directory) =>
        {
            try
            {
                var packagesDir = directory ?? Path.Combine(dataRoot, "..", "packages");
                var packagePath = Path.Combine(packagesDir, fileName);

                // Security: ensure the path is within the packages directory
                var fullPath = Path.GetFullPath(packagePath);
                var fullPackagesDir = Path.GetFullPath(packagesDir);

                if (!fullPath.StartsWith(fullPackagesDir, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.BadRequest(new { error = "Invalid file path" });
                }

                if (!File.Exists(fullPath))
                {
                    return Results.NotFound(new { error = $"Package not found: {fileName}" });
                }

                File.Delete(fullPath);

                return Results.Ok(new { message = $"Package deleted: {fileName}" });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to delete package: {ex.Message}");
            }
        });

        // ==================== PACKAGE DOWNLOAD ====================

        //
        // Download a package file.
        // GET /api/packaging/download/{fileName}
        app.MapGet("/api/packaging/download/{fileName}", (string fileName, string? directory) =>
        {
            try
            {
                var packagesDir = directory ?? Path.Combine(dataRoot, "..", "packages");
                var packagePath = Path.Combine(packagesDir, fileName);

                // Security: ensure the path is within the packages directory
                var fullPath = Path.GetFullPath(packagePath);
                var fullPackagesDir = Path.GetFullPath(packagesDir);

                if (!fullPath.StartsWith(fullPackagesDir, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.BadRequest("Invalid file path");
                }

                if (!File.Exists(fullPath))
                {
                    return Results.NotFound($"Package not found: {fileName}");
                }

                var contentType = fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    ? "application/zip"
                    : fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
                        ? "application/gzip"
                        : "application/octet-stream";

                return Results.File(fullPath, contentType, fileName);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to download package: {ex.Message}");
            }
        });
    }

    private static PackageFormat ParseFormat(string? format)
    {
        return format?.ToLowerInvariant() switch
        {
            "zip" => PackageFormat.Zip,
            "tar.gz" or "targz" or "tgz" => PackageFormat.TarGz,
            "7z" or "7zip" => PackageFormat.SevenZip,
            _ => PackageFormat.Zip
        };
    }

    private static PackageCompressionLevel ParseCompression(string? level)
    {
        return level?.ToLowerInvariant() switch
        {
            "none" => PackageCompressionLevel.None,
            "fast" => PackageCompressionLevel.Fast,
            "balanced" => PackageCompressionLevel.Balanced,
            "maximum" or "max" => PackageCompressionLevel.Maximum,
            _ => PackageCompressionLevel.Balanced
        };
    }
}

/// <summary>
/// Request model for package creation.
/// </summary>
public sealed record PackageRequest(
    string? Name,
    string? Description,
    string? OutputDirectory,
    string[]? Symbols,
    string[]? EventTypes,
    DateTime? StartDate,
    DateTime? EndDate,
    string? Format,
    string? CompressionLevel,
    bool? IncludeQualityReport,
    bool? IncludeDataDictionary,
    bool? IncludeLoaderScripts,
    bool? VerifyChecksums,
    string[]? Tags,
    Dictionary<string, string>? CustomMetadata
);

/// <summary>
/// Request model for package import.
/// </summary>
public sealed record ImportRequest(
    string PackagePath,
    string? DestinationDirectory,
    bool? ValidateChecksums,
    bool? MergeWithExisting
);

/// <summary>
/// Request model for package validation.
/// </summary>
public sealed record ValidateRequest(
    string PackagePath
);
