using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Meridian.Application.Logging;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using Serilog;

namespace Meridian.Storage.Export;

/// <summary>
/// Service for exporting collected market data in analysis-ready formats.
/// Split into partial classes: Formats (CSV/Parquet/JSONL/Lean/SQL/XLSX), IO (documentation/loaders/utilities).
/// </summary>
public sealed partial class AnalysisExportService
{
    private readonly ILogger _log = LoggingSetup.ForContext<AnalysisExportService>();
    private readonly string _dataRoot;
    private readonly Dictionary<string, ExportProfile> _profiles;
    private readonly ExportValidator _validator;

    public AnalysisExportService(string dataRoot)
    {
        _dataRoot = dataRoot;
        _profiles = ExportProfile.GetBuiltInProfiles()
            .ToDictionary(p => p.Id, p => p);
        _validator = new ExportValidator(dataRoot);
    }

    /// <summary>
    /// Get all available export profiles.
    /// </summary>
    public IReadOnlyList<ExportProfile> GetProfiles() => _profiles.Values.ToList();

    /// <summary>
    /// Get a specific profile by ID.
    /// </summary>
    public ExportProfile? GetProfile(string profileId) =>
        _profiles.TryGetValue(profileId, out var profile) ? profile : null;

    /// <summary>
    /// Register a custom export profile.
    /// </summary>
    public void RegisterProfile(ExportProfile profile)
    {
        _profiles[profile.Id] = profile;
        _log.Information("Registered export profile: {ProfileId} ({Name})", profile.Id, profile.Name);
    }

    /// <summary>
    /// Preview an export without writing any files.
    /// Returns record counts, file sizes, date ranges, and sample records.
    /// </summary>
    public async Task<ExportPreviewResult> PreviewAsync(ExportRequest request, int sampleSize = 5, CancellationToken ct = default)
    {
        var profile = request.CustomProfile ?? GetProfile(request.ProfileId);
        if (profile is null)
            return new ExportPreviewResult { Error = $"Unknown profile: {request.ProfileId}" };

        try
        {
            var sourceFiles = FindSourceFiles(request);

            var symbols = sourceFiles
                .Where(f => f.Symbol != null)
                .Select(f => f.Symbol!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToArray();

            var eventTypes = sourceFiles
                .Where(f => f.EventType != null)
                .Select(f => f.EventType!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t)
                .ToArray();

            long totalRecords = 0;
            long totalSourceBytes = 0;
            var sampleRecords = new List<Dictionary<string, object?>>();

            foreach (var file in sourceFiles)
            {
                var fileInfo = new FileInfo(file.Path);
                totalSourceBytes += fileInfo.Length;

                await foreach (var record in ReadJsonlRecordsAsync(file.Path, ct))
                {
                    totalRecords++;
                    if (sampleRecords.Count < sampleSize)
                        sampleRecords.Add(record);
                }
            }

            // Estimate output size based on format compression ratios
            var estimatedOutputBytes = profile.Format switch
            {
                ExportFormat.Parquet => (long)(totalSourceBytes * 0.3),
                ExportFormat.Csv => (long)(totalSourceBytes * 0.8),
                ExportFormat.Arrow => (long)(totalSourceBytes * 0.4),
                ExportFormat.Xlsx => (long)(totalSourceBytes * 0.5),
                _ => totalSourceBytes
            };

            var dates = sourceFiles
                .Where(f => f.Date.HasValue)
                .Select(f => f.Date!.Value)
                .ToList();

            return new ExportPreviewResult
            {
                ProfileId = profile.Id,
                ProfileName = profile.Name,
                Format = profile.Format.ToString().ToLowerInvariant(),
                SourceFileCount = sourceFiles.Count,
                TotalRecords = totalRecords,
                TotalSourceBytes = totalSourceBytes,
                EstimatedOutputBytes = estimatedOutputBytes,
                Symbols = symbols,
                EventTypes = eventTypes,
                DateRange = dates.Count > 0
                    ? new ExportDateRange
                    {
                        Start = dates.Min(),
                        End = dates.Max(),
                        TradingDays = CountTradingDays(dates.Min(), dates.Max())
                    }
                    : null,
                SampleRecords = sampleRecords.ToArray()
            };
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Export preview failed for profile {ProfileId}", request.ProfileId);
            return new ExportPreviewResult { Error = ex.Message };
        }
    }

    /// <summary>
    /// Generate a standalone loader script without performing a full export.
    /// </summary>
    public async Task<string> GenerateStandaloneLoaderAsync(
        string outputDir,
        string targetTool,
        string[]? symbols = null,
        CancellationToken ct = default)
    {
        var profile = targetTool.ToLowerInvariant() switch
        {
            "python" or "pandas" => ExportProfile.PythonPandas,
            "r" or "rstats" => ExportProfile.RStats,
            "pyarrow" or "arrow" => ExportProfile.ArrowFeather,
            "postgresql" or "postgres" => ExportProfile.PostgreSql,
            _ => ExportProfile.PythonPandas
        };

        // Build mock file list from what's in storage
        var sourceFiles = FindSourceFiles(new ExportRequest
        {
            Symbols = symbols,
            StartDate = DateTime.MinValue,
            EndDate = DateTime.MaxValue
        });

        var exportedFiles = sourceFiles.Select(f => new ExportedFile
        {
            Path = f.Path,
            RelativePath = Path.GetFileName(f.Path),
            Symbol = f.Symbol,
            EventType = f.EventType,
            Format = profile.Format switch
            {
                ExportFormat.Parquet => "parquet",
                ExportFormat.Csv => "csv",
                ExportFormat.Arrow => "arrow",
                _ => "csv"
            }
        }).ToList();

        Directory.CreateDirectory(outputDir);
        var scriptPath = await GenerateLoaderScriptAsync(outputDir, profile, exportedFiles, ct);
        var dictPath = await GenerateDataDictionaryAsync(
            outputDir, new[] { "Trade", "BboQuote" }, profile, ct);

        return scriptPath;
    }

    /// <summary>
    /// Export data according to the request.
    /// </summary>
    public async Task<ExportResult> ExportAsync(ExportRequest request, CancellationToken ct = default)
    {
        var profile = request.CustomProfile ?? GetProfile(request.ProfileId);
        if (profile is null)
            return ExportResult.CreateFailure(request.ProfileId, $"Unknown profile: {request.ProfileId}");

        var result = ExportResult.CreateSuccess(profile.Id, request.OutputDirectory);

        try
        {
            _log.Information("Starting export with profile {ProfileId} to {OutputDir}",
                profile.Id, request.OutputDirectory);

            // Pre-export validation
            if (request.ValidateBeforeExport)
            {
                var validation = await _validator.ValidateAsync(request, ct);
                if (!validation.IsValid)
                {
                    var errorMessages = string.Join("; ", validation.Errors.Select(e => e.Message));
                    _log.Warning("Pre-export validation failed: {Errors}", errorMessages);
                    return ExportResult.CreateFailure(profile.Id, $"Validation failed: {errorMessages}");
                }

                // Append any warnings to the result
                var warningMessages = validation.Warnings.Select(w => $"[Validation] {w.Message}").ToArray();
                if (warningMessages.Length > 0)
                    result.Warnings = [.. result.Warnings, .. warningMessages];
            }

            // Ensure output directory exists
            Directory.CreateDirectory(request.OutputDirectory);

            // Find source files to export
            var sourceFiles = FindSourceFiles(request);
            if (sourceFiles.Count is 0)
            {
                result.Warnings = [.. result.Warnings, "No source data found for the specified criteria"];
                result.CompletedAt = DateTime.UtcNow;
                return result;
            }

            _log.Information("Found {FileCount} source files to export", sourceFiles.Count);

            // Export based on format
            var exportedFiles = new List<ExportedFile>();

            switch (profile.Format)
            {
                case ExportFormat.Csv:
                    exportedFiles = await ExportToCsvAsync(sourceFiles, request, profile, ct);
                    break;
                case ExportFormat.Parquet:
                    exportedFiles = await ExportToParquetAsync(sourceFiles, request, profile, ct);
                    break;
                case ExportFormat.Jsonl:
                    exportedFiles = await ExportToJsonlAsync(sourceFiles, request, profile, ct);
                    break;
                case ExportFormat.Lean:
                    exportedFiles = await ExportToLeanAsync(sourceFiles, request, profile, ct);
                    break;
                case ExportFormat.Sql:
                    exportedFiles = await ExportToSqlAsync(sourceFiles, request, profile, ct);
                    break;
                case ExportFormat.Xlsx:
                    exportedFiles = await ExportToXlsxAsync(sourceFiles, request, profile, ct);
                    break;
                case ExportFormat.Arrow:
                    exportedFiles = await ExportToArrowAsync(sourceFiles, request, profile, ct);
                    break;
                default:
                    throw new NotSupportedException($"Format {profile.Format} is not supported");
            }

            result.Files = exportedFiles.ToArray();
            result.FilesGenerated = exportedFiles.Count;
            result.TotalRecords = exportedFiles.Sum(f => f.RecordCount);
            result.TotalBytes = exportedFiles.Sum(f => f.SizeBytes);
            result.Symbols = exportedFiles
                .Where(f => f.Symbol != null)
                .Select(f => f.Symbol!)
                .Distinct()
                .ToArray();

            result.DateRange = new ExportDateRange
            {
                Start = request.StartDate,
                End = request.EndDate,
                TradingDays = CountTradingDays(request.StartDate, request.EndDate)
            };

            // Generate supporting files
            if (profile.IncludeDataDictionary)
            {
                var dictPath = await GenerateDataDictionaryAsync(
                    request.OutputDirectory, request.EventTypes, profile, ct);
                result.DataDictionaryPath = dictPath;
            }

            if (profile.IncludeLoaderScript)
            {
                var scriptPath = await GenerateLoaderScriptAsync(
                    request.OutputDirectory, profile, exportedFiles, ct);
                result.LoaderScriptPath = scriptPath;
            }

            // Generate lineage manifest with provenance information
            if (request.IncludeManifest)
            {
                var lineagePath = await GenerateLineageManifestAsync(
                    request.OutputDirectory, request, profile, sourceFiles, exportedFiles, ct);
                result.LineageManifestPath = lineagePath;
            }

            result.CompletedAt = DateTime.UtcNow;
            result.Success = true;

            _log.Information("Export completed: {FileCount} files, {RecordCount:N0} records, {Bytes:N0} bytes",
                result.FilesGenerated, result.TotalRecords, result.TotalBytes);

            return result;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Export failed for profile {ProfileId}", profile.Id);
            result.Success = false;
            result.Error = ex.Message;
            result.CompletedAt = DateTime.UtcNow;
            return result;
        }
    }

    private List<SourceFile> FindSourceFiles(ExportRequest request)
    {
        if (!Directory.Exists(_dataRoot))
            return new List<SourceFile>();

        return new[] { "*.jsonl", "*.jsonl.gz" }
            .SelectMany(pattern => Directory.GetFiles(_dataRoot, pattern, SearchOption.AllDirectories))
            .Select(ParseFileName)
            .Where(f => f is not null)
            .Select(f => f!)
            .Where(f => request.Symbols is not { Length: > 0 } ||
                        request.Symbols.Contains(f.Symbol, StringComparer.OrdinalIgnoreCase))
            .Where(f => request.EventTypes is not { Length: > 0 } ||
                        request.EventTypes.Contains(f.EventType, StringComparer.OrdinalIgnoreCase))
            .Where(f => !f.Date.HasValue ||
                        (f.Date.Value >= request.StartDate.Date && f.Date.Value <= request.EndDate.Date))
            .OrderBy(f => f.Symbol)
            .ThenBy(f => f.Date)
            .ToList();
    }

    private SourceFile? ParseFileName(string path)
    {
        var fileName = Path.GetFileName(path);
        var parts = fileName.Split('.');

        if (parts.Length < 2)
            return null;

        // Handle patterns like: AAPL.Trade.jsonl, SPY.BboQuote.2026-01-03.jsonl.gz
        var result = new SourceFile
        {
            Path = path,
            IsCompressed = fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
        };

        // Try to extract symbol and event type
        result.Symbol = parts[0];

        if (parts.Length >= 3)
        {
            result.EventType = parts[1];

            // Check if there's a date component
            if (parts.Length >= 4 && DateTime.TryParse(parts[2], out var date))
            {
                result.Date = date;
            }
        }

        return result;
    }

    private static int CountTradingDays(DateTime start, DateTime end)
    {
        var count = 0;
        for (var date = start.Date; date <= end.Date; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Groups source files by symbol if requested, otherwise returns a single group.
    /// </summary>
    private static IEnumerable<IGrouping<string?, SourceFile>> GroupBySymbolIfRequired(
        List<SourceFile> files, bool splitBySymbol) =>
        splitBySymbol
            ? files.GroupBy(f => f.Symbol)
            : files.GroupBy(_ => (string?)"combined");

    private class SourceFile
    {
        public string Path { get; set; } = string.Empty;
        public string? Symbol { get; set; }
        public string? EventType { get; set; }
        public DateTime? Date { get; set; }
        public bool IsCompressed { get; set; }
    }
}
