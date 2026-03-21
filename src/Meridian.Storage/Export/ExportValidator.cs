using System.Text.Json.Serialization;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Storage.Export;

/// <summary>
/// Validates an export request before any files are written.
/// Catches problems such as insufficient disk space, missing write permissions,
/// and empty result sets early so users are never surprised by a silent failure
/// mid-export.
/// </summary>
public sealed class ExportValidator
{
    private readonly ILogger _log = LoggingSetup.ForContext<ExportValidator>();
    private readonly string _dataRoot;

    public ExportValidator(string dataRoot)
    {
        _dataRoot = dataRoot;
    }

    /// <summary>
    /// Runs all pre-export checks and returns a <see cref="ExportValidationResult"/>
    /// that describes every issue found.  The export should be aborted when
    /// <see cref="ExportValidationResult.IsValid"/> is <c>false</c>.
    /// </summary>
    public async Task<ExportValidationResult> ValidateAsync(
        ExportRequest request,
        CancellationToken ct = default)
    {
        var issues = new List<ExportValidationIssue>();

        // --- 1. Disk-space check --------------------------------------------------
        var estimatedBytes = EstimateExportSizeBytes(request);
        var multiplier = request.ValidationRules?.DiskSpaceMultiplier ?? 1.2;
        var requiredBytes = (long)(estimatedBytes * multiplier);
        var availableBytes = GetAvailableDiskSpaceBytes(request.OutputDirectory);

        if (availableBytes >= 0 && availableBytes < requiredBytes)
        {
            issues.Add(new ExportValidationIssue
            {
                Severity = ExportValidationSeverity.Error,
                Code = "DISK_SPACE",
                Message = $"Insufficient disk space. Need {requiredBytes / (1024.0 * 1024 * 1024):F2} GB " +
                          $"({multiplier:P0} safety margin), " +
                          $"have {availableBytes / (1024.0 * 1024 * 1024):F2} GB available."
            });
        }

        // --- 2. Write-permission check --------------------------------------------
        if (!string.IsNullOrEmpty(request.OutputDirectory) && !HasWritePermission(request.OutputDirectory))
        {
            issues.Add(new ExportValidationIssue
            {
                Severity = ExportValidationSeverity.Error,
                Code = "WRITE_PERMISSION",
                Message = $"No write permission for output path: {request.OutputDirectory}"
            });
        }

        // --- 3. Data-existence check ----------------------------------------------
        var recordCount = await CountDataPointsAsync(request, ct);
        if (recordCount == 0)
        {
            var requireData = request.ValidationRules?.RequireData ?? false;
            issues.Add(new ExportValidationIssue
            {
                Severity = requireData ? ExportValidationSeverity.Error : ExportValidationSeverity.Warning,
                Code = "NO_DATA",
                Message = "No data found for the specified date range, symbols and event types."
            });
        }
        else
        {
            _log.Debug("Pre-export data check: {RecordCount:N0} records available.", recordCount);
        }

        // --- 4. CSV + complex-type warning ----------------------------------------
        var profile = request.CustomProfile;
        var warnCsv = request.ValidationRules?.WarnCsvComplexTypes ?? true;
        if (warnCsv && profile?.Format == ExportFormat.Csv && HasComplexEventTypes(request))
        {
            issues.Add(new ExportValidationIssue
            {
                Severity = ExportValidationSeverity.Warning,
                Code = "CSV_COMPLEX_TYPES",
                Message = "CSV format may lose nested data structures (e.g. order-book depth). " +
                          "Consider using Parquet or JSONL to preserve all fields."
            });
        }

        return new ExportValidationResult
        {
            EstimatedRecordCount = recordCount,
            EstimatedSizeBytes = estimatedBytes,
            AvailableDiskSpaceBytes = availableBytes,
            Issues = issues,
            IsValid = !issues.Any(i => i.Severity == ExportValidationSeverity.Error)
        };
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private long EstimateExportSizeBytes(ExportRequest request)
    {
        if (!Directory.Exists(_dataRoot))
            return 0;

        var profile = request.CustomProfile;
        var ratio = profile?.Format switch
        {
            ExportFormat.Parquet => 0.3,
            ExportFormat.Arrow => 0.4,
            ExportFormat.Xlsx => 0.5,
            ExportFormat.Csv => 0.8,
            _ => 1.0
        };

        var sourceBytes = new[] { "*.jsonl", "*.jsonl.gz" }
            .SelectMany(p => Directory.GetFiles(_dataRoot, p, SearchOption.AllDirectories))
            .Sum(f => new FileInfo(f).Length);

        return (long)(sourceBytes * ratio);
    }

    private static long GetAvailableDiskSpaceBytes(string path)
    {
        try
        {
            var dir = string.IsNullOrEmpty(path) ? Directory.GetCurrentDirectory() : path;

            // Ensure the directory exists so DriveInfo can resolve the root.
            if (!Directory.Exists(dir))
                dir = Path.GetDirectoryName(dir) ?? Directory.GetCurrentDirectory();

            var drive = new DriveInfo(Path.GetPathRoot(dir) ?? dir);
            return drive.AvailableFreeSpace;
        }
        catch
        {
            return -1; // Unable to determine — skip the check
        }
    }

    private static bool HasWritePermission(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probe = Path.Combine(path, $".write_probe_{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<long> CountDataPointsAsync(ExportRequest request, CancellationToken ct)
    {
        if (!Directory.Exists(_dataRoot))
            return 0;

        long count = 0;
        // Line-by-line counting is deliberate: source files are JSONL records stored
        // during live collection, typically a few MB each.  Accuracy matters here
        // so that the "no data" check is reliable.  Very large source files will be
        // dominated by the export itself, making this pre-check cost negligible.
        var files = new[] { "*.jsonl", "*.jsonl.gz" }
            .SelectMany(p => Directory.GetFiles(_dataRoot, p, SearchOption.AllDirectories))
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                var parts = name.Split('.');

                var symbol = parts.Length >= 1 ? parts[0] : null;
                var eventType = parts.Length >= 2 ? parts[1] : null;

                if (request.Symbols is { Length: > 0 } &&
                    !request.Symbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
                    return false;

                if (request.EventTypes is { Length: > 0 } &&
                    !request.EventTypes.Contains(eventType, StringComparer.OrdinalIgnoreCase))
                    return false;

                return true;
            });

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            count += await CountLinesAsync(file, ct);
        }

        return count;
    }

    private static async Task<long> CountLinesAsync(string path, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(path);
            long lines = 0;
            while (await reader.ReadLineAsync(ct) is not null)
                lines++;
            return lines;
        }
        catch
        {
            return 0;
        }
    }

    private static bool HasComplexEventTypes(ExportRequest request) =>
        request.EventTypes.Any(t =>
            t.Equals("LOBSnapshot", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("OrderBook", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Result of a pre-export validation run.
/// </summary>
public sealed class ExportValidationResult
{
    /// <summary>Whether the export can proceed (no error-level issues).</summary>
    [JsonPropertyName("isValid")]
    public bool IsValid { get; init; }

    /// <summary>Estimated number of records that would be exported.</summary>
    [JsonPropertyName("estimatedRecordCount")]
    public long EstimatedRecordCount { get; init; }

    /// <summary>Estimated output size in bytes.</summary>
    [JsonPropertyName("estimatedSizeBytes")]
    public long EstimatedSizeBytes { get; init; }

    /// <summary>Available disk space at the output path in bytes. -1 when unknown.</summary>
    [JsonPropertyName("availableDiskSpaceBytes")]
    public long AvailableDiskSpaceBytes { get; init; }

    /// <summary>All issues found during validation.</summary>
    [JsonPropertyName("issues")]
    public IReadOnlyList<ExportValidationIssue> Issues { get; init; } = Array.Empty<ExportValidationIssue>();

    /// <summary>Issues with <see cref="ExportValidationSeverity.Error"/> severity that block the export.</summary>
    [JsonIgnore]
    public IEnumerable<ExportValidationIssue> Errors =>
        Issues.Where(i => i.Severity == ExportValidationSeverity.Error);

    /// <summary>Issues with <see cref="ExportValidationSeverity.Warning"/> severity.</summary>
    [JsonIgnore]
    public IEnumerable<ExportValidationIssue> Warnings =>
        Issues.Where(i => i.Severity == ExportValidationSeverity.Warning);
}

/// <summary>
/// A single issue found during pre-export validation.
/// </summary>
public sealed class ExportValidationIssue
{
    /// <summary>How serious the issue is.</summary>
    [JsonPropertyName("severity")]
    public ExportValidationSeverity Severity { get; init; }

    /// <summary>Machine-readable issue code (e.g. "DISK_SPACE").</summary>
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    /// <summary>Human-readable description of the issue.</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Severity level for export validation issues.
/// </summary>
public enum ExportValidationSeverity : byte
{
    /// <summary>Informational — export can proceed.</summary>
    Info,
    /// <summary>Warning — export can proceed but the user should be aware.</summary>
    Warning,
    /// <summary>Error — export must be aborted.</summary>
    Error
}
