using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Defines the import result for bulk Security Master imports.
/// </summary>
public sealed record SecurityMasterImportResult(
    int Imported,
    int Skipped,
    int Failed,
    IReadOnlyList<string> Errors);

/// <summary>
/// Progress update for ongoing Security Master imports.
/// </summary>
public sealed record SecurityMasterImportProgress(
    int Total,
    int Processed,
    int Imported,
    int Failed);

/// <summary>
/// Service for importing securities from CSV or JSON files.
/// Coordinates CSV parsing and calls ISecurityMasterService for each security.
/// </summary>
public interface ISecurityMasterImportService
{
    /// <summary>
    /// Imports securities from a file.
    /// </summary>
    /// <param name="fileContent">The raw file content (CSV or JSON)</param>
    /// <param name="fileExtension">File extension (csv or json)</param>
    /// <param name="progress">Optional progress reporter</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Import result with statistics and errors</returns>
    Task<SecurityMasterImportResult> ImportAsync(
        string fileContent,
        string fileExtension,
        IProgress<SecurityMasterImportProgress>? progress = null,
        CancellationToken ct = default);
}

/// <summary>
/// Implementation of bulk security import service.
/// </summary>
public sealed class SecurityMasterImportService : ISecurityMasterImportService
{
    private readonly ISecurityMasterService _securityMasterService;
    private readonly SecurityMasterCsvParser _csvParser;
    private readonly ILogger<SecurityMasterImportService> _logger;

    private const int ProgressReportInterval = 10;
    private const int DelayBetweenRequestsMs = 50;

    public SecurityMasterImportService(
        ISecurityMasterService securityMasterService,
        SecurityMasterCsvParser csvParser,
        ILogger<SecurityMasterImportService> logger)
    {
        _securityMasterService = securityMasterService;
        _csvParser = csvParser;
        _logger = logger;
    }

    public async Task<SecurityMasterImportResult> ImportAsync(
        string fileContent,
        string fileExtension,
        IProgress<SecurityMasterImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        List<CreateSecurityRequest> requests;
        List<string> errors;

        try
        {
            if (fileExtension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                requests = _csvParser.Parse(fileContent, out var parseErrors)
                    .ToList();
                errors = parseErrors.ToList();
            }
            else if (fileExtension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                requests = JsonSerializer.Deserialize<List<CreateSecurityRequest>>(fileContent)
                    ?? new List<CreateSecurityRequest>();
                errors = new List<string>();
            }
            else
            {
                errors = new List<string> { $"Unsupported file extension: {fileExtension}" };
                return new SecurityMasterImportResult(0, 0, 0, errors);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse import file");
            errors = new List<string> { $"Parse error: {ex.Message}" };
            return new SecurityMasterImportResult(0, 0, 0, errors);
        }

        int imported = 0;
        int skipped = 0;
        int failed = 0;
        var total = requests.Count;

        foreach (var (index, request) in requests.WithIndex())
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await _securityMasterService.CreateAsync(request, ct).ConfigureAwait(false);
                imported++;
                _logger.LogDebug("Imported security {Ticker}", request.Identifiers.FirstOrDefault()?.Value ?? "?");
            }
            catch (Exception ex)
            {
                // Check if it's a duplicate (409 or similar)
                if (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    _logger.LogDebug("Skipped duplicate security: {Message}", ex.Message);
                }
                else
                {
                    failed++;
                    errors.Add($"Security {request.Identifiers.FirstOrDefault()?.Value ?? "?"}: {ex.Message}");
                    _logger.LogError(ex, "Failed to import security");
                }
            }

            // Delay between requests to avoid overwhelming the service
            if (index < total - 1)
                await Task.Delay(DelayBetweenRequestsMs, ct).ConfigureAwait(false);

            // Report progress every N rows
            if ((index + 1) % ProgressReportInterval == 0 || index == total - 1)
            {
                progress?.Report(new SecurityMasterImportProgress(total, index + 1, imported, failed));
            }
        }

        _logger.LogInformation(
            "Imported {Imported} securities from {Format} ({Failed} failed, {Skipped} skipped)",
            imported,
            fileExtension,
            failed,
            skipped);

        return new SecurityMasterImportResult(imported, skipped, failed, errors);
    }
}

/// <summary>
/// Extension method for enumerating with index.
/// </summary>
internal static class EnumerableExtensions
{
    public static IEnumerable<(int Index, T Item)> WithIndex<T>(this IEnumerable<T> source)
    {
        int index = 0;
        foreach (var item in source)
        {
            yield return (index, item);
            index++;
        }
    }
}
