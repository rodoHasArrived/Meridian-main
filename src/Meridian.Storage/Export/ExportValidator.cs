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
    private readonly PreflightEngine<ExportPreflightContext> _engine;

    public ExportValidator(
        string dataRoot,
        IEnumerable<IPreflightRule<ExportPreflightContext>>? rules = null)
    {
        _dataRoot = dataRoot;
        _engine = new PreflightEngine<ExportPreflightContext>(rules ?? ExportPreflightRules.DefaultRules);
    }

    /// <summary>
    /// Runs all pre-export checks and returns a <see cref="ExportValidationResult"/>
    /// that describes every issue found. The export should be aborted when
    /// <see cref="ExportValidationResult.IsValid"/> is <c>false</c>.
    /// </summary>
    public async Task<ExportValidationResult> ValidateAsync(
        ExportRequest request,
        CancellationToken ct = default)
    {
        var context = await CollectContextAsync(request, ct);
        var preflightIssues = _engine.Evaluate(context);

        if (context.RecordCount > 0)
            _log.Debug("Pre-export data check: {RecordCount:N0} records available.", context.RecordCount);

        var issues = preflightIssues.Select(MapIssue).ToArray();

        return new ExportValidationResult
        {
            EstimatedRecordCount = context.RecordCount,
            EstimatedSizeBytes = context.EstimatedBytes,
            AvailableDiskSpaceBytes = context.AvailableDiskSpaceBytes,
            Issues = issues,
            IsValid = !issues.Any(i => i.Severity == ExportValidationSeverity.Error)
        };
    }

    private async Task<ExportPreflightContext> CollectContextAsync(ExportRequest request, CancellationToken ct)
    {
        var estimatedBytes = EstimateExportSizeBytes(request);
        var availableBytes = GetAvailableDiskSpaceBytes(request.OutputDirectory);
        var hasWritePermission = string.IsNullOrEmpty(request.OutputDirectory) || HasWritePermission(request.OutputDirectory);
        var recordCount = await CountDataPointsAsync(request, ct);

        return new ExportPreflightContext(
            Request: request,
            EstimatedBytes: estimatedBytes,
            AvailableDiskSpaceBytes: availableBytes,
            HasWritePermission: hasWritePermission,
            RecordCount: recordCount);
    }

    private static ExportValidationIssue MapIssue(PreflightIssue issue)
    {
        return new ExportValidationIssue
        {
            RuleId = issue.RuleId,
            Severity = issue.Severity switch
            {
                PreflightSeverity.Info => ExportValidationSeverity.Info,
                PreflightSeverity.Warning => ExportValidationSeverity.Warning,
                _ => ExportValidationSeverity.Error
            },
            Code = issue.Code,
            Message = issue.Message,
            Remediation = issue.Remediation
        };
    }

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

            if (!Directory.Exists(dir))
                dir = Path.GetDirectoryName(dir) ?? Directory.GetCurrentDirectory();

            var drive = new DriveInfo(Path.GetPathRoot(dir) ?? dir);
            return drive.AvailableFreeSpace;
        }
        catch
        {
            return -1;
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
}

/// <summary>
/// Result of a pre-export validation run.
/// </summary>
public sealed class ExportValidationResult
{
    [JsonPropertyName("isValid")]
    public bool IsValid { get; init; }

    [JsonPropertyName("estimatedRecordCount")]
    public long EstimatedRecordCount { get; init; }

    [JsonPropertyName("estimatedSizeBytes")]
    public long EstimatedSizeBytes { get; init; }

    [JsonPropertyName("availableDiskSpaceBytes")]
    public long AvailableDiskSpaceBytes { get; init; }

    [JsonPropertyName("issues")]
    public IReadOnlyList<ExportValidationIssue> Issues { get; init; } = Array.Empty<ExportValidationIssue>();

    [JsonIgnore]
    public IEnumerable<ExportValidationIssue> Errors =>
        Issues.Where(i => i.Severity == ExportValidationSeverity.Error);

    [JsonIgnore]
    public IEnumerable<ExportValidationIssue> Warnings =>
        Issues.Where(i => i.Severity == ExportValidationSeverity.Warning);
}

/// <summary>
/// A single issue found during pre-export validation.
/// </summary>
public sealed class ExportValidationIssue
{
    [JsonPropertyName("ruleId")]
    public string RuleId { get; init; } = string.Empty;

    [JsonPropertyName("severity")]
    public ExportValidationSeverity Severity { get; init; }

    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("remediation")]
    public string? Remediation { get; init; }
}

/// <summary>
/// Severity level for export validation issues.
/// </summary>
public enum ExportValidationSeverity : byte
{
    Info,
    Warning,
    Error
}
