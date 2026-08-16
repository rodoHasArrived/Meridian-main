using System.Text.Json.Serialization;
using Meridian.Core.Logging;
using Meridian.Storage.Policies;
using Meridian.Storage.Replay;
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
    private static readonly string[] JsonlSourcePatterns =
    [
        "*.jsonl",
        "*.jsonl.gz",
        "*.jsonl.gzip",
        "*.jsonl.zst",
        "*.jsonl.lz4",
        "*.jsonl.br"
    ];

    private readonly ILogger _log = LoggingSetup.ForContext<ExportValidator>();
    private readonly string _dataRoot;
    private readonly JsonlStoragePolicy _storagePolicy;
    private readonly PreflightEngine<ExportPreflightContext> _engine;

    public ExportValidator(
        string dataRoot,
        IEnumerable<IPreflightRule<ExportPreflightContext>>? rules = null)
        : this(new StorageOptions { RootPath = dataRoot }, rules)
    {
    }

    public ExportValidator(
        StorageOptions storageOptions,
        IEnumerable<IPreflightRule<ExportPreflightContext>>? rules = null)
    {
        ArgumentNullException.ThrowIfNull(storageOptions);

        _dataRoot = storageOptions.RootPath;
        _storagePolicy = new JsonlStoragePolicy(storageOptions);
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

        var sourceBytes = FindMatchingSourceFiles(request)
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
        foreach (var file in FindMatchingSourceFiles(request))
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
            await using var source = File.OpenRead(path);
            using var reader = new StreamReader(CompressedJsonlStream.Decompress(source, path));
            long lines = 0;
            while (await reader.ReadLineAsync(ct) is not null)
                lines++;
            return lines;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return 0;
        }
    }

    private IEnumerable<string> FindMatchingSourceFiles(ExportRequest request)
    {
        if (!Directory.Exists(_dataRoot))
            return Array.Empty<string>();

        return JsonlSourcePatterns
            .SelectMany(pattern => Directory.GetFiles(_dataRoot, pattern, SearchOption.AllDirectories))
            .Distinct(GetPathComparer())
            .Select(path => (Path: path, Metadata: ParseSourceMetadata(path)))
            .Where(candidate => candidate.Metadata is not null)
            .Where(candidate => request.Symbols is not { Length: > 0 } ||
                                request.Symbols.Contains(
                                    candidate.Metadata!.Symbol,
                                    StringComparer.OrdinalIgnoreCase))
            .Where(candidate => request.EventTypes is not { Length: > 0 } ||
                                request.EventTypes.Contains(
                                    candidate.Metadata!.EventType,
                                    StringComparer.OrdinalIgnoreCase))
            .Where(candidate => !candidate.Metadata!.Date.HasValue ||
                                (candidate.Metadata.Date.Value >= request.StartDate.Date &&
                                 candidate.Metadata.Date.Value <= request.EndDate.Date))
            .Select(static candidate => candidate.Path);
    }

    private SourceMetadata? ParseSourceMetadata(string path)
    {
        var metadata = _storagePolicy.TryParsePath(path);
        if (metadata is not null)
        {
            return new SourceMetadata(
                metadata.Symbol,
                metadata.EventType,
                metadata.Date?.UtcDateTime);
        }

        var parts = Path.GetFileName(path).Split('.');
        if (parts.Length < 2)
            return null;

        DateTime? date = null;
        if (parts.Length >= 4 && DateTime.TryParse(parts[2], out var parsedDate))
            date = parsedDate;

        return new SourceMetadata(
            parts[0],
            parts.Length >= 3 ? parts[1] : string.Empty,
            date);
    }

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private sealed record SourceMetadata(
        string Symbol,
        string EventType,
        DateTime? Date);
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
