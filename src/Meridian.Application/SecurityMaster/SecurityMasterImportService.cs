using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Serialization;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Defines the import result for bulk Security Master imports.
/// </summary>
public sealed record SecurityMasterImportResult(
    int Imported,
    int Skipped,
    int Failed,
    int ConflictsDetected,
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
public sealed class SecurityMasterImportService : ISecurityMasterImportService, ISecurityMasterIngestStatusService
{
    private readonly ISecurityMasterService _securityMasterService;
    private readonly SecurityMasterCsvParser _csvParser;
    private readonly ISecurityMasterConflictService? _conflictService;
    private readonly ILogger<SecurityMasterImportService> _logger;
    private readonly object _statusGate = new();
    private Guid? _activeImportId;
    private SecurityMasterActiveImportStatus? _activeImport;
    private SecurityMasterCompletedImportStatus? _lastCompleted;

    private const int ProgressReportInterval = 10;
    private const int DelayBetweenRequestsMs = 50;

    public SecurityMasterImportService(
        ISecurityMasterService securityMasterService,
        SecurityMasterCsvParser csvParser,
        ILogger<SecurityMasterImportService> logger,
        ISecurityMasterConflictService? conflictService = null)
    {
        _securityMasterService = securityMasterService;
        _csvParser = csvParser;
        _conflictService = conflictService;
        _logger = logger;
    }

    public SecurityMasterIngestStatusSnapshot GetSnapshot()
    {
        lock (_statusGate)
        {
            return new SecurityMasterIngestStatusSnapshot(_activeImport, _lastCompleted);
        }
    }

    public async Task<SecurityMasterImportResult> ImportAsync(
        string fileContent,
        string fileExtension,
        IProgress<SecurityMasterImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var startedAtUtc = DateTimeOffset.UtcNow;
        var normalizedFileExtension = NormalizeFileExtension(fileExtension);

        List<CreateSecurityRequest> requests;
        List<string> errors;

        try
        {
            if (normalizedFileExtension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                requests = _csvParser.Parse(fileContent, out var parseErrors)
                    .ToList();
                errors = parseErrors.ToList();
            }
            else if (normalizedFileExtension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                requests = JsonSerializer.Deserialize(
                    fileContent,
                    SecurityMasterJsonContext.Default.ListCreateSecurityRequest)
                    ?? new List<CreateSecurityRequest>();
                errors = new List<string>();
            }
            else
            {
                errors = new List<string> { $"Unsupported file extension: {fileExtension}" };
                return RecordCompleted(
                    importId: null,
                    normalizedFileExtension,
                    total: 0,
                    processed: 0,
                    startedAtUtc,
                    new SecurityMasterImportResult(0, 0, 0, 0, errors));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse import file");
            errors = new List<string> { $"Parse error: {ex.Message}" };
            return RecordCompleted(
                importId: null,
                normalizedFileExtension,
                total: 0,
                processed: 0,
                startedAtUtc,
                new SecurityMasterImportResult(0, 0, 0, 0, errors));
        }

        // Snapshot open conflict count before import so we can report the delta.
        int conflictsBefore = 0;
        if (_conflictService is not null)
        {
            var openBefore = await _conflictService.GetOpenConflictsAsync(ct).ConfigureAwait(false);
            conflictsBefore = openBefore.Count;
        }

        int imported = 0;
        int skipped = 0;
        int failed = 0;
        var total = requests.Count;
        var importId = BeginImport(normalizedFileExtension, total, startedAtUtc);
        var completed = false;

        try
        {
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

                UpdateActiveImport(
                    importId,
                    normalizedFileExtension,
                    total,
                    processed: index + 1,
                    imported,
                    skipped,
                    failed,
                    DateTimeOffset.UtcNow);

                // Delay between requests to avoid overwhelming the service
                if (index < total - 1)
                    await Task.Delay(DelayBetweenRequestsMs, ct).ConfigureAwait(false);

                // Report progress every N rows
                if ((index + 1) % ProgressReportInterval == 0 || index == total - 1)
                {
                    progress?.Report(new SecurityMasterImportProgress(total, index + 1, imported, failed));
                }
            }

            int conflictsDetected = 0;
            if (_conflictService is not null)
            {
                var openAfter = await _conflictService.GetOpenConflictsAsync(ct).ConfigureAwait(false);
                conflictsDetected = Math.Max(0, openAfter.Count - conflictsBefore);
            }

            _logger.LogInformation(
                "Imported {Imported} securities from {Format} ({Failed} failed, {Skipped} skipped, {Conflicts} new conflicts)",
                imported,
                normalizedFileExtension,
                failed,
                skipped,
                conflictsDetected);

            var result = new SecurityMasterImportResult(imported, skipped, failed, conflictsDetected, errors);
            completed = true;
            return RecordCompleted(importId, normalizedFileExtension, total, total, startedAtUtc, result);
        }
        finally
        {
            if (!completed)
            {
                ClearActiveImport(importId);
            }
        }
    }

    private static string NormalizeFileExtension(string fileExtension)
        => string.IsNullOrWhiteSpace(fileExtension)
            ? string.Empty
            : fileExtension.StartsWith(".", StringComparison.Ordinal) ? fileExtension : $".{fileExtension}";

    private Guid BeginImport(string fileExtension, int total, DateTimeOffset startedAtUtc)
    {
        var importId = Guid.NewGuid();
        var activeImport = new SecurityMasterActiveImportStatus(
            FileExtension: fileExtension,
            Total: total,
            Processed: 0,
            Imported: 0,
            Skipped: 0,
            Failed: 0,
            StartedAtUtc: startedAtUtc,
            UpdatedAtUtc: startedAtUtc);

        lock (_statusGate)
        {
            _activeImportId = importId;
            _activeImport = activeImport;
        }

        return importId;
    }

    private void UpdateActiveImport(
        Guid importId,
        string fileExtension,
        int total,
        int processed,
        int imported,
        int skipped,
        int failed,
        DateTimeOffset updatedAtUtc)
    {
        lock (_statusGate)
        {
            if (_activeImportId != importId)
            {
                return;
            }

            _activeImport = new SecurityMasterActiveImportStatus(
                FileExtension: fileExtension,
                Total: total,
                Processed: processed,
                Imported: imported,
                Skipped: skipped,
                Failed: failed,
                StartedAtUtc: _activeImport?.StartedAtUtc ?? updatedAtUtc,
                UpdatedAtUtc: updatedAtUtc);
        }
    }

    private void ClearActiveImport(Guid importId)
    {
        lock (_statusGate)
        {
            if (_activeImportId != importId)
            {
                return;
            }

            _activeImportId = null;
            _activeImport = null;
        }
    }

    private SecurityMasterImportResult RecordCompleted(
        Guid? importId,
        string fileExtension,
        int total,
        int processed,
        DateTimeOffset startedAtUtc,
        SecurityMasterImportResult result)
    {
        var completedAtUtc = DateTimeOffset.UtcNow;
        var completedImport = new SecurityMasterCompletedImportStatus(
            FileExtension: fileExtension,
            Total: total,
            Processed: processed,
            Imported: result.Imported,
            Skipped: result.Skipped,
            Failed: result.Failed,
            ConflictsDetected: result.ConflictsDetected,
            ErrorCount: result.Errors.Count,
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: completedAtUtc);

        lock (_statusGate)
        {
            if (importId is not null && _activeImportId == importId)
            {
                _activeImportId = null;
                _activeImport = null;
            }

            _lastCompleted = completedImport;
        }

        return result;
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
