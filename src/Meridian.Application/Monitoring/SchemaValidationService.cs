using System.Text.Json;
using Meridian.Application.Logging;
using Meridian.Domain.Events;
using Meridian.Storage.Archival;
using Serilog;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Consolidated schema validation service providing a single entrypoint for
/// schema version validation in the ingestion path.
/// Bridges EventSchemaValidator (runtime) and SchemaVersionManager (archival).
/// </summary>
public sealed class SchemaValidationService : IAsyncDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<SchemaValidationService>();
    private readonly SchemaVersionManager? _versionManager;
    private readonly string? _dataRoot;
    private readonly SchemaValidationOptions _options;
    private bool _startupCheckCompleted;

    /// <summary>
    /// Current schema version for serialized MarketEvent documents.
    /// This is the single source of truth for schema versioning.
    /// </summary>
    public static int CurrentSchemaVersion => EventSchemaValidator.CurrentSchemaVersion;

    /// <summary>
    /// Semantic version string corresponding to the current schema version.
    /// Used for compatibility with SchemaVersionManager.
    /// </summary>
    public static string CurrentSemanticVersion => $"{CurrentSchemaVersion}.0.0";

    public SchemaValidationService(SchemaValidationOptions? options = null, string? dataRoot = null)
    {
        _options = options ?? new SchemaValidationOptions();
        _dataRoot = dataRoot;

        if (_options.EnableVersionTracking && !string.IsNullOrEmpty(dataRoot))
        {
            var schemaDir = Path.Combine(dataRoot, "_schemas");
            _versionManager = new SchemaVersionManager(schemaDir);
            _log.Debug("Schema version tracking enabled at {SchemaDir}", schemaDir);
        }
    }

    /// <summary>
    /// Validates an event for schema compliance. This is the single entrypoint
    /// for all schema validation in the ingestion path.
    /// </summary>
    /// <param name="evt">The market event to validate.</param>
    /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
    public void Validate(MarketEvent evt)
    {
        // Delegate to the lightweight validator for runtime checks
        EventSchemaValidator.Validate(evt);
    }

    /// <summary>
    /// Validates an event and returns a result instead of throwing.
    /// </summary>
    /// <param name="evt">The market event to validate.</param>
    /// <returns>Validation result with success status and any errors.</returns>
    public SchemaCheckResult ValidateSafe(MarketEvent evt)
    {
        try
        {
            Validate(evt);
            return SchemaCheckResult.Success();
        }
        catch (InvalidOperationException ex)
        {
            return SchemaCheckResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// Performs a startup check for stored schema compatibility.
    /// Scans existing data files and verifies they are compatible with current schema.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of the startup compatibility check.</returns>
    public async Task<StartupSchemaCheckResult> PerformStartupCheckAsync(CancellationToken ct = default)
    {
        if (_startupCheckCompleted && !_options.AlwaysRunStartupCheck)
        {
            _log.Debug("Startup schema check already completed, skipping");
            return new StartupSchemaCheckResult(true, "Already completed", Array.Empty<SchemaIncompatibility>());
        }

        var result = new StartupSchemaCheckResult(true, "No data files found", Array.Empty<SchemaIncompatibility>());

        if (string.IsNullOrEmpty(_dataRoot) || !Directory.Exists(_dataRoot))
        {
            _startupCheckCompleted = true;
            return result;
        }

        var incompatibilities = new List<SchemaIncompatibility>();
        var filesChecked = 0;

        _log.Information("Starting schema compatibility check for data in {DataRoot}", _dataRoot);

        try
        {
            // Scan for JSONL files and check schema versions
            var jsonlFiles = Directory.EnumerateFiles(_dataRoot, "*.jsonl", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(_dataRoot, "*.jsonl.gz", SearchOption.AllDirectories))
                .Take(_options.MaxFilesToCheck);

            foreach (var file in jsonlFiles)
            {
                if (ct.IsCancellationRequested)
                    break;

                var checkResult = await CheckFileSchemaAsync(file, ct).ConfigureAwait(false);
                filesChecked++;

                if (!checkResult.IsCompatible)
                {
                    incompatibilities.Add(checkResult);

                    if (_options.FailOnFirstIncompatibility)
                    {
                        break;
                    }
                }
            }

            var success = incompatibilities.Count == 0;
            var message = success
                ? $"Schema check passed for {filesChecked} files"
                : $"Found {incompatibilities.Count} incompatible files out of {filesChecked} checked";

            result = new StartupSchemaCheckResult(success, message, incompatibilities.ToArray());

            if (success)
            {
                _log.Information("Schema compatibility check passed: {FilesChecked} files verified", filesChecked);
            }
            else
            {
                _log.Warning("Schema compatibility check found {IncompatibleCount} incompatible files",
                    incompatibilities.Count);

                foreach (var incompat in incompatibilities.Take(5))
                {
                    _log.Warning("  {FilePath}: version {Version} (expected {Expected})",
                        incompat.FilePath, incompat.DetectedVersion, CurrentSchemaVersion);
                }

                if (incompatibilities.Count > 5)
                {
                    _log.Warning("  ... and {More} more", incompatibilities.Count - 5);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during startup schema check");
            result = new StartupSchemaCheckResult(false, $"Check failed: {ex.Message}", incompatibilities.ToArray());
        }

        _startupCheckCompleted = true;
        return result;
    }

    /// <summary>
    /// Checks the schema version of a data file by sampling its first record.
    /// </summary>
    private async Task<SchemaIncompatibility> CheckFileSchemaAsync(string filePath, CancellationToken ct)
    {
        try
        {
            await using var stream = OpenFileStream(filePath);
            using var reader = new StreamReader(stream);

            var firstLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(firstLine))
            {
                return SchemaIncompatibility.Compatible(filePath);
            }

            using var doc = JsonDocument.Parse(firstLine);
            var root = doc.RootElement;

            // Check for SchemaVersion property
            if (root.TryGetProperty("SchemaVersion", out var versionProp))
            {
                var version = versionProp.GetInt32();
                if (version != CurrentSchemaVersion)
                {
                    return new SchemaIncompatibility(
                        filePath,
                        version,
                        CurrentSchemaVersion,
                        false,
                        CanMigrate(version, CurrentSchemaVersion));
                }
            }
            else
            {
                // No SchemaVersion property - assume version 0 (legacy)
                return new SchemaIncompatibility(
                    filePath,
                    0,
                    CurrentSchemaVersion,
                    false,
                    CanMigrate(0, CurrentSchemaVersion));
            }

            return SchemaIncompatibility.Compatible(filePath);
        }
        catch (JsonException)
        {
            _log.Debug("Could not parse {FilePath} as JSON, skipping schema check", filePath);
            return SchemaIncompatibility.Compatible(filePath);
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Error checking schema for {FilePath}", filePath);
            return SchemaIncompatibility.Compatible(filePath);
        }
    }

    private Stream OpenFileStream(string filePath)
    {
        var stream = File.OpenRead(filePath);
        if (filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            return new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
        }
        return stream;
    }

    private static bool CanMigrate(int fromVersion, int toVersion)
    {
        // Currently only support forward migrations from v0/v1 to v1
        return fromVersion < toVersion && toVersion == CurrentSchemaVersion;
    }

    /// <summary>
    /// Gets the version manager for advanced schema operations.
    /// Returns null if version tracking is disabled.
    /// </summary>
    public SchemaVersionManager? GetVersionManager() => _versionManager;

    /// <summary>
    /// Exports current schema definitions to the schema directory.
    /// </summary>
    public async Task ExportSchemasAsync(CancellationToken ct = default)
    {
        if (_versionManager == null)
        {
            _log.Warning("Cannot export schemas: version tracking is disabled");
            return;
        }

        await _versionManager.ExportAllSchemasAsync(ct).ConfigureAwait(false);
        _log.Information("Exported schema definitions");
    }

    public ValueTask DisposeAsync()
    {
        // No unmanaged resources to dispose
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Options for schema validation service.
/// </summary>
public sealed record SchemaValidationOptions
{
    /// <summary>
    /// Enable schema version tracking with SchemaVersionManager.
    /// </summary>
    public bool EnableVersionTracking { get; init; } = true;

    /// <summary>
    /// Run startup schema check even if previously completed.
    /// </summary>
    public bool AlwaysRunStartupCheck { get; init; } = false;

    /// <summary>
    /// Maximum number of files to check during startup.
    /// </summary>
    public int MaxFilesToCheck { get; init; } = 100;

    /// <summary>
    /// Stop checking on first incompatibility found.
    /// </summary>
    public bool FailOnFirstIncompatibility { get; init; } = false;
}

/// <summary>
/// Result of a schema validation check.
/// </summary>
public sealed record SchemaCheckResult(bool IsValid, string? Error = null)
{
    public static SchemaCheckResult Success() => new(true);
    public static SchemaCheckResult Failed(string error) => new(false, error);
}

/// <summary>
/// Result of startup schema compatibility check.
/// </summary>
public sealed record StartupSchemaCheckResult(
    bool Success,
    string Message,
    SchemaIncompatibility[] Incompatibilities
)
{
    public bool HasIncompatibilities => Incompatibilities.Length > 0;
    public int IncompatibleFileCount => Incompatibilities.Count(i => !i.IsCompatible);
}

/// <summary>
/// Information about a schema incompatibility found in a data file.
/// </summary>
public sealed record SchemaIncompatibility(
    string FilePath,
    int DetectedVersion,
    int ExpectedVersion,
    bool IsCompatible,
    bool CanMigrate
)
{
    public static SchemaIncompatibility Compatible(string filePath)
        => new(filePath, EventSchemaValidator.CurrentSchemaVersion, EventSchemaValidator.CurrentSchemaVersion, true, false);
}
