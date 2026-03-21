using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Storage.Export;

/// <summary>
/// Verifies the integrity of a completed export by re-reading the
/// <c>lineage_manifest.json</c> and checking each exported file against
/// the checksums and record counts recorded at export time.
/// </summary>
public sealed class ExportVerifier
{
    private const string ManifestFileName = "lineage_manifest.json";

    private readonly ILogger _log = LoggingSetup.ForContext<ExportVerifier>();

    /// <summary>
    /// Verifies all files in <paramref name="exportDirectory"/> against the
    /// <c>lineage_manifest.json</c> written there during export.
    /// </summary>
    /// <param name="exportDirectory">Directory that contains the export output.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="ExportVerificationResult"/> with per-file details and aggregate flags.
    /// </returns>
    public async Task<ExportVerificationResult> VerifyExportAsync(
        string exportDirectory,
        CancellationToken ct = default)
    {
        var issues = new List<string>();

        // ---- 1. Load manifest ---------------------------------------------------
        var manifestPath = Path.Combine(exportDirectory, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return ExportVerificationResult.Failure(
                $"Manifest file not found: {manifestPath}");
        }

        JsonDocument manifest;
        try
        {
            var json = await File.ReadAllTextAsync(manifestPath, ct);
            manifest = JsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            return ExportVerificationResult.Failure(
                $"Failed to parse manifest: {ex.Message}");
        }

        // ---- 2. Extract the outputs array from the manifest --------------------
        if (!manifest.RootElement.TryGetProperty("outputs", out var outputsElement) ||
            outputsElement.ValueKind != JsonValueKind.Array)
        {
            return ExportVerificationResult.Failure(
                "Manifest does not contain an 'outputs' array.");
        }

        var fileResults = new List<ExportFileVerificationResult>();
        bool checksumsFailed = false;
        bool recordCountsFailed = false;

        foreach (var outputEntry in outputsElement.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();

            var relativePath = outputEntry.TryGetProperty("relativePath", out var rp)
                ? rp.GetString() : null;
            var expectedChecksum = outputEntry.TryGetProperty("checksumSha256", out var cs)
                ? cs.GetString() : null;
            var expectedRecords = outputEntry.TryGetProperty("recordCount", out var rc)
                ? rc.GetInt64() : (long?)null;

            if (string.IsNullOrEmpty(relativePath))
                continue;

            var filePath = Path.Combine(exportDirectory, relativePath);
            var fileResult = new ExportFileVerificationResult { RelativePath = relativePath };

            if (!File.Exists(filePath))
            {
                fileResult.Issues.Add($"File not found: {relativePath}");
                issues.Add($"Missing file: {relativePath}");
                checksumsFailed = true;
                fileResults.Add(fileResult);
                continue;
            }

            // Checksum verification
            if (!string.IsNullOrEmpty(expectedChecksum))
            {
                var actualChecksum = await ComputeChecksumAsync(filePath, ct);
                fileResult.ChecksumExpected = expectedChecksum;
                fileResult.ChecksumActual = actualChecksum;
                fileResult.ChecksumValid = string.Equals(
                    actualChecksum, expectedChecksum, StringComparison.OrdinalIgnoreCase);

                if (!fileResult.ChecksumValid)
                {
                    var msg = $"Checksum mismatch for {relativePath}: " +
                              $"expected {expectedChecksum}, got {actualChecksum}";
                    fileResult.Issues.Add(msg);
                    issues.Add(msg);
                    checksumsFailed = true;
                }
            }
            else
            {
                fileResult.ChecksumValid = true; // No expected checksum — skip
            }

            // Record-count verification
            if (expectedRecords.HasValue)
            {
                var actualRecords = await CountRecordsAsync(filePath, ct);
                fileResult.RecordCountExpected = expectedRecords.Value;
                fileResult.RecordCountActual = actualRecords;
                fileResult.RecordCountValid = actualRecords == expectedRecords.Value;

                if (!fileResult.RecordCountValid)
                {
                    var msg = $"Record count mismatch for {relativePath}: " +
                              $"expected {expectedRecords.Value:N0}, got {actualRecords:N0}";
                    fileResult.Issues.Add(msg);
                    issues.Add(msg);
                    recordCountsFailed = true;
                }
            }
            else
            {
                fileResult.RecordCountValid = true;
            }

            fileResults.Add(fileResult);
        }

        _log.Information(
            "Export verification: {FileCount} files, checksums {ChecksumStatus}, record counts {RecordCountStatus}",
            fileResults.Count,
            checksumsFailed ? "FAILED" : "OK",
            recordCountsFailed ? "FAILED" : "OK");

        return new ExportVerificationResult
        {
            IsValid = issues.Count == 0,
            ChecksumsValid = !checksumsFailed,
            RecordCountsValid = !recordCountsFailed,
            SchemaValid = true, // Schema checks are advisory — treated as always valid for now
            Issues = issues,
            FileResults = fileResults
        };
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task<string> ComputeChecksumAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<long> CountRecordsAsync(string path, CancellationToken ct)
    {
        try
        {
            // For text-based formats (CSV, JSONL, SQL) count non-blank lines.
            // Line-by-line reading is deliberately used here for accuracy rather than
            // estimation; exports are typically tens of MB, making the overhead acceptable
            // compared to the cost of writing a corrupt export silently.
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".csv" or ".jsonl" or ".sql")
            {
                long lines = 0;
                using var reader = new StreamReader(path);
                while (await reader.ReadLineAsync(ct) is { } line)
                    if (!string.IsNullOrWhiteSpace(line))
                        lines++;

                // CSV has one header row that does not count as a record
                return ext == ".csv" ? Math.Max(0, lines - 1) : lines;
            }

            // For binary formats we trust the manifest record count (return -1
            // to signal that the count could not be independently verified).
            return -1;
        }
        catch
        {
            return -1;
        }
    }
}

/// <summary>
/// Aggregate result of an export verification run.
/// </summary>
public sealed class ExportVerificationResult
{
    /// <summary>
    /// <c>true</c> when every file passed all checks.
    /// </summary>
    [JsonPropertyName("isValid")]
    public bool IsValid { get; init; }

    /// <summary>
    /// <c>true</c> when all file checksums matched the manifest.
    /// </summary>
    [JsonPropertyName("checksumsValid")]
    public bool ChecksumsValid { get; init; }

    /// <summary>
    /// <c>true</c> when all record counts matched the manifest.
    /// </summary>
    [JsonPropertyName("recordCountsValid")]
    public bool RecordCountsValid { get; init; }

    /// <summary>
    /// <c>true</c> when schema checks passed.  Currently always <c>true</c>
    /// (advisory — reserved for future schema-level validation).
    /// </summary>
    [JsonPropertyName("schemaValid")]
    public bool SchemaValid { get; init; }

    /// <summary>
    /// Human-readable descriptions of every issue found.
    /// </summary>
    [JsonPropertyName("issues")]
    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Per-file verification details.
    /// </summary>
    [JsonPropertyName("fileResults")]
    public IReadOnlyList<ExportFileVerificationResult> FileResults { get; init; } =
        Array.Empty<ExportFileVerificationResult>();

    /// <summary>
    /// Creates a result that represents a top-level failure (e.g. missing manifest).
    /// </summary>
    public static ExportVerificationResult Failure(string reason) => new()
    {
        IsValid = false,
        ChecksumsValid = false,
        RecordCountsValid = false,
        SchemaValid = false,
        Issues = new[] { reason }
    };
}

/// <summary>
/// Verification result for a single exported file.
/// </summary>
public sealed class ExportFileVerificationResult
{
    /// <summary>Relative path of the file within the export directory.</summary>
    [JsonPropertyName("relativePath")]
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>SHA-256 checksum recorded in the manifest.</summary>
    [JsonPropertyName("checksumExpected")]
    public string? ChecksumExpected { get; set; }

    /// <summary>SHA-256 checksum computed from the actual file.</summary>
    [JsonPropertyName("checksumActual")]
    public string? ChecksumActual { get; set; }

    /// <summary>Whether the checksums matched.</summary>
    [JsonPropertyName("checksumValid")]
    public bool ChecksumValid { get; set; }

    /// <summary>Record count recorded in the manifest.</summary>
    [JsonPropertyName("recordCountExpected")]
    public long? RecordCountExpected { get; set; }

    /// <summary>Record count computed from the actual file.</summary>
    [JsonPropertyName("recordCountActual")]
    public long? RecordCountActual { get; set; }

    /// <summary>Whether the record counts matched.</summary>
    [JsonPropertyName("recordCountValid")]
    public bool RecordCountValid { get; set; }

    /// <summary>Issues found for this specific file.</summary>
    [JsonPropertyName("issues")]
    public List<string> Issues { get; } = new();
}
