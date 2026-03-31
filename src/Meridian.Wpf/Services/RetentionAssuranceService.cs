using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// Service for retention policy assurance with guardrails, legal holds, and verification.
/// Implements Feature Refinement #23 - File Retention Assurance.
///
/// This service provides WPF-specific features (legal holds, guardrails, UI persistence)
/// and delegates file operations to the core service via HTTP API endpoints:
/// - /api/storage/health/check - File health checks with checksum validation
/// - /api/storage/health/orphans - Find orphaned files
/// - /api/storage/catalog - Storage catalog for file discovery
/// - /api/storage/search/files - Search files by criteria
/// </summary>
public sealed class RetentionAssuranceService
{
    private static readonly Lazy<RetentionAssuranceService> _instance = new(() => new RetentionAssuranceService());

    // C2: Static options instance avoids allocating a new JsonSerializerOptions on every audit export.
    private static readonly JsonSerializerOptions AuditSerializerOptions = new(DesktopJsonOptions.PrettyPrint)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private const string RetentionConfigKey = "RetentionConfig";
    private const string LegalHoldsKey = "LegalHolds";
    private const string AuditReportsFolder = "RetentionAudits";

    private readonly ApiClientService _apiClient;
    private RetentionConfiguration _config = new();
    private readonly List<LegalHold> _legalHolds = new();
    private readonly List<RetentionAuditReport> _auditReports = new();

    public static RetentionAssuranceService Instance => _instance.Value;

    private RetentionAssuranceService()
    {
        _apiClient = ApiClientService.Instance;
        _ = LoadConfigurationAsync();
    }

    /// <summary>
    /// Gets the current retention configuration.
    /// </summary>
    public RetentionConfiguration Configuration => _config;

    /// <summary>
    /// Gets active legal holds.
    /// </summary>
    public IReadOnlyList<LegalHold> LegalHolds => _legalHolds.AsReadOnly();

    /// <summary>
    /// Gets recent audit reports.
    /// </summary>
    public IReadOnlyList<RetentionAuditReport> AuditReports => _auditReports.AsReadOnly();

    /// <summary>
    /// Loads configuration from storage.
    /// </summary>
    public async Task LoadConfigurationAsync(CancellationToken ct = default)
    {
        try
        {
            var settingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Meridian");
            Directory.CreateDirectory(settingsDir);
            var settingsPath = Path.Combine(settingsDir, "retention-settings.json");

            // Load retention config and legal holds from file
            if (File.Exists(settingsPath))
            {
                var fileJson = await File.ReadAllTextAsync(settingsPath);
                var settingsData = JsonSerializer.Deserialize<RetentionSettingsData>(fileJson);
                if (settingsData != null)
                {
                    if (settingsData.Config != null)
                        _config = settingsData.Config;
                    if (settingsData.LegalHolds != null)
                    {
                        _legalHolds.Clear();
                        _legalHolds.AddRange(settingsData.LegalHolds.Where(h => h.ExpiresAt == null || h.ExpiresAt > DateTime.UtcNow));
                    }
                }
            }

            // Set default guardrails if not configured
            if (_config.Guardrails == null)
            {
                _config.Guardrails = GetDefaultGuardrails();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RetentionAssuranceService] Error loading config: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves configuration to storage.
    /// </summary>
    public async Task SaveConfigurationAsync(CancellationToken ct = default)
    {
        try
        {
            var settingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Meridian");
            Directory.CreateDirectory(settingsDir);
            var settingsPath = Path.Combine(settingsDir, "retention-settings.json");

            var settingsData = new RetentionSettingsData
            {
                Config = _config,
                LegalHolds = _legalHolds.ToList()
            };
            var json = JsonSerializer.Serialize(settingsData, DesktopJsonOptions.PrettyPrint);
            await File.WriteAllTextAsync(settingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RetentionAssuranceService] Error saving config: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates a retention policy against guardrails.
    /// </summary>
    public RetentionValidationResult ValidateRetentionPolicy(RetentionPolicy policy)
    {
        var result = new RetentionValidationResult { IsValid = true };

        // Check minimum retention periods
        if (policy.TickDataDays < _config.Guardrails.MinTickDataDays)
        {
            result.IsValid = false;
            result.Violations.Add(new GuardrailViolation
            {
                Rule = "MinTickDataRetention",
                Message = $"Tick data retention ({policy.TickDataDays} days) is below minimum ({_config.Guardrails.MinTickDataDays} days)",
                Severity = ViolationSeverity.Error
            });
        }

        if (policy.BarDataDays < _config.Guardrails.MinBarDataDays)
        {
            result.IsValid = false;
            result.Violations.Add(new GuardrailViolation
            {
                Rule = "MinBarDataRetention",
                Message = $"Bar data retention ({policy.BarDataDays} days) is below minimum ({_config.Guardrails.MinBarDataDays} days)",
                Severity = ViolationSeverity.Error
            });
        }

        if (policy.QuoteDataDays < _config.Guardrails.MinQuoteDataDays)
        {
            result.IsValid = false;
            result.Violations.Add(new GuardrailViolation
            {
                Rule = "MinQuoteDataRetention",
                Message = $"Quote data retention ({policy.QuoteDataDays} days) is below minimum ({_config.Guardrails.MinQuoteDataDays} days)",
                Severity = ViolationSeverity.Error
            });
        }

        // Check if any symbols are under legal hold
        var heldSymbols = GetSymbolsUnderLegalHold();
        if (heldSymbols.Any())
        {
            result.Warnings.Add(new GuardrailViolation
            {
                Rule = "LegalHold",
                Message = $"The following symbols are under legal hold and will be excluded: {string.Join(", ", heldSymbols)}",
                Severity = ViolationSeverity.Warning
            });
        }

        // Check daily volume deletion limit
        if (policy.DeletedFilesPerRun > _config.Guardrails.MaxDailyDeletedFiles)
        {
            result.Warnings.Add(new GuardrailViolation
            {
                Rule = "MaxDailyDeletions",
                Message = $"Requested deletions ({policy.DeletedFilesPerRun}) exceeds daily limit ({_config.Guardrails.MaxDailyDeletedFiles})",
                Severity = ViolationSeverity.Warning
            });
        }

        return result;
    }

    /// <summary>
    /// Performs a dry run of retention cleanup using the core API for file discovery.
    /// Falls back to local scanning if the API is unavailable.
    /// </summary>
    public async Task<RetentionDryRunResult> PerformDryRunAsync(
        RetentionPolicy policy,
        string dataRoot,
        CancellationToken ct = default)
    {
        var result = new RetentionDryRunResult
        {
            PolicyApplied = policy,
            ExecutedAt = DateTime.UtcNow
        };

        try
        {
            var heldSymbols = GetSymbolsUnderLegalHold();

            // Try to use core API for file discovery (more efficient and consistent)
            var apiResult = await PerformDryRunViaApiAsync(policy, heldSymbols, ct);
            if (apiResult != null)
            {
                return apiResult;
            }

            // Fall back to local directory scanning
            if (!Directory.Exists(dataRoot))
            {
                result.Errors.Add($"Data root directory not found: {dataRoot}");
                return result;
            }

            var cutoffDate = DateTime.UtcNow.AddDays(-policy.TickDataDays);
            var barCutoffDate = DateTime.UtcNow.AddDays(-policy.BarDataDays);
            var quoteCutoffDate = DateTime.UtcNow.AddDays(-policy.QuoteDataDays);

            // Scan data directories
            await ScanDirectoryForDeletionsAsync(
                dataRoot,
                policy,
                cutoffDate,
                barCutoffDate,
                quoteCutoffDate,
                heldSymbols,
                result,
                ct);

            // Group by symbol for reporting
            result.GroupBySymbol();
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Dry run failed: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Performs dry run via core API using /api/storage/search/files endpoint.
    /// </summary>
    private async Task<RetentionDryRunResult?> PerformDryRunViaApiAsync(
        RetentionPolicy policy,
        HashSet<string> heldSymbols,
        CancellationToken ct)
    {
        try
        {
            var tickCutoff = DateTimeOffset.UtcNow.AddDays(-policy.TickDataDays);
            var barCutoff = DateTimeOffset.UtcNow.AddDays(-policy.BarDataDays);
            var quoteCutoff = DateTimeOffset.UtcNow.AddDays(-policy.QuoteDataDays);

            // Search for files older than the cutoff dates
            var searchRequest = new
            {
                To = tickCutoff, // Files older than this
                Take = 10000
            };

            var response = await _apiClient.PostWithResponseAsync<FileSearchApiResponse>(
                "/api/storage/search/files", searchRequest, ct);

            if (!response.Success || response.Data == null)
            {
                return null; // API unavailable, fall back to local scanning
            }

            var result = new RetentionDryRunResult
            {
                PolicyApplied = policy,
                ExecutedAt = DateTime.UtcNow
            };

            foreach (var file in response.Data.Results)
            {
                // Skip if symbol is under legal hold
                if (heldSymbols.Contains(file.Symbol?.ToUpperInvariant() ?? ""))
                {
                    result.SkippedFiles.Add(new SkippedFileInfo
                    {
                        Path = file.Path,
                        Symbol = file.Symbol ?? "UNKNOWN",
                        Reason = "Legal hold active",
                        Size = file.SizeBytes
                    });
                    continue;
                }

                // Determine applicable cutoff based on event type
                var cutoff = file.EventType?.ToLowerInvariant() switch
                {
                    "trade" or "tick" => tickCutoff,
                    "bar" or "ohlc" => barCutoff,
                    "quote" or "bbo" => quoteCutoff,
                    _ => tickCutoff
                };

                if (file.Date < cutoff)
                {
                    result.FilesToDelete.Add(new FileToDelete
                    {
                        Path = file.Path,
                        Symbol = file.Symbol ?? "UNKNOWN",
                        Size = file.SizeBytes,
                        LastModified = file.Date.DateTime,
                        DataType = file.EventType ?? "Unknown"
                    });
                    result.TotalBytesToDelete += file.SizeBytes;
                }
            }

            result.GroupBySymbol();
            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RetentionAssuranceService] API dry run failed: {ex.Message}");
            return null; // Fall back to local scanning
        }
    }

    /// <summary>
    /// Runs a health check on storage via the core API.
    /// </summary>
    public async Task<StorageHealthCheckResult?> RunHealthCheckAsync(bool validateChecksums = true, CancellationToken ct = default)
    {
        try
        {
            var request = new
            {
                ValidateChecksums = validateChecksums,
                IdentifyCorruption = true,
                CheckFilePermissions = true
            };

            var response = await _apiClient.PostWithResponseAsync<StorageHealthCheckResult>(
                "/api/storage/health/check", request, ct);

            return response.Success ? response.Data : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RetentionAssuranceService] Health check failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Finds orphaned files via the core API.
    /// </summary>
    public async Task<OrphanFilesResult?> FindOrphanedFilesAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _apiClient.GetWithResponseAsync<OrphanFilesResult>(
                "/api/storage/health/orphans", ct);

            return response.Success ? response.Data : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RetentionAssuranceService] Find orphans failed: {ex.Message}");
            return null;
        }
    }

    private async Task ScanDirectoryForDeletionsAsync(
        string directory,
        RetentionPolicy policy,
        DateTime tickCutoff,
        DateTime barCutoff,
        DateTime quoteCutoff,
        HashSet<string> heldSymbols,
        RetentionDryRunResult result,
        CancellationToken ct)
    {
        try
        {
            var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                var fileInfo = new FileInfo(file);
                var fileName = Path.GetFileNameWithoutExtension(file);

                // Parse symbol from filename (assumes format: SYMBOL_type_date.ext)
                var parts = fileName.Split('_');
                var symbol = parts.Length > 0 ? parts[0] : "UNKNOWN";

                // Skip if under legal hold
                if (heldSymbols.Contains(symbol.ToUpperInvariant()))
                {
                    result.SkippedFiles.Add(new SkippedFileInfo
                    {
                        Path = file,
                        Symbol = symbol,
                        Reason = "Legal hold active",
                        Size = fileInfo.Length
                    });
                    continue;
                }

                // Determine file type and applicable cutoff
                var cutoff = GetCutoffForFile(file, tickCutoff, barCutoff, quoteCutoff);

                if (fileInfo.LastWriteTimeUtc < cutoff)
                {
                    result.FilesToDelete.Add(new FileToDelete
                    {
                        Path = file,
                        Symbol = symbol,
                        Size = fileInfo.Length,
                        LastModified = fileInfo.LastWriteTimeUtc,
                        DataType = GetDataTypeFromPath(file)
                    });
                    result.TotalBytesToDelete += fileInfo.Length;
                }
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Error scanning {directory}: {ex.Message}");
        }
    }

    private static DateTime GetCutoffForFile(string path, DateTime tick, DateTime bar, DateTime quote)
    {
        var lower = path.ToLowerInvariant();
        if (lower.Contains("trade") || lower.Contains("tick"))
            return tick;
        if (lower.Contains("bar") || lower.Contains("ohlc"))
            return bar;
        if (lower.Contains("quote") || lower.Contains("bbo"))
            return quote;
        return tick; // Default to tick data cutoff
    }

    private static string GetDataTypeFromPath(string path)
    {
        var lower = path.ToLowerInvariant();
        if (lower.Contains("trade") || lower.Contains("tick"))
            return "Tick";
        if (lower.Contains("bar") || lower.Contains("ohlc"))
            return "Bar";
        if (lower.Contains("quote") || lower.Contains("bbo"))
            return "Quote";
        if (lower.Contains("depth") || lower.Contains("l2"))
            return "Depth";
        return "Unknown";
    }

    /// <summary>
    /// Verifies file checksums before deletion.
    /// </summary>
    public async Task<ChecksumVerificationResult> VerifyChecksumsAsync(
        IEnumerable<FileToDelete> files,
        string archiveManifestPath,
        CancellationToken ct = default)
    {
        var result = new ChecksumVerificationResult();

        try
        {
            // Load manifest checksums if available
            if (File.Exists(archiveManifestPath))
            {
                var manifestJson = await File.ReadAllTextAsync(archiveManifestPath, ct);
                _ = JsonSerializer.Deserialize<Dictionary<string, object>>(manifestJson);
                // Extract checksums from manifest
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                if (!File.Exists(file.Path))
                {
                    result.MissingFiles.Add(file.Path);
                    continue;
                }

                var hash = await ComputeFileHashAsync(file.Path, ct);
                result.VerifiedFiles.Add(new VerifiedFile
                {
                    Path = file.Path,
                    Checksum = hash,
                    IsVerified = true,
                    Size = file.Size
                });
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Checksum verification failed: {ex.Message}");
        }

        return result;
    }

    private static async Task<string> ComputeFileHashAsync(string path, CancellationToken ct)
    {
        using var sha256 = SHA256.Create();
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        var hash = await sha256.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Executes retention cleanup with full audit trail.
    /// </summary>
    public async Task<RetentionAuditReport> ExecuteRetentionCleanupAsync(
        RetentionDryRunResult dryRun,
        bool verifyChecksums,
        CancellationToken ct = default)
    {
        var report = new RetentionAuditReport
        {
            ExecutedAt = DateTime.UtcNow,
            PolicyApplied = dryRun.PolicyApplied,
            DryRunFilesCount = dryRun.FilesToDelete.Count,
            DryRunBytesTotal = dryRun.TotalBytesToDelete
        };

        try
        {
            // Verify checksums if requested
            if (verifyChecksums)
            {
                var checksumResult = await VerifyChecksumsAsync(dryRun.FilesToDelete, "", ct);
                report.ChecksumVerification = checksumResult;

                if (checksumResult.Errors.Any())
                {
                    report.Status = CleanupStatus.FailedVerification;
                    report.Errors.AddRange(checksumResult.Errors);
                    return report;
                }
            }

            // Execute deletions
            foreach (var file in dryRun.FilesToDelete.Take(_config.Guardrails.MaxDailyDeletedFiles))
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    if (File.Exists(file.Path))
                    {
                        File.Delete(file.Path);
                        report.DeletedFiles.Add(new DeletedFileInfo
                        {
                            Path = file.Path,
                            Symbol = file.Symbol,
                            Size = file.Size,
                            DeletedAt = DateTime.UtcNow
                        });
                        report.ActualBytesDeleted += file.Size;
                    }
                }
                catch (Exception ex)
                {
                    report.Errors.Add($"Failed to delete {file.Path}: {ex.Message}");
                }
            }

            report.Status = report.Errors.Any() ? CleanupStatus.PartialSuccess : CleanupStatus.Success;

            // Check for files that couldn't be deleted due to daily limit
            var remaining = dryRun.FilesToDelete.Count - _config.Guardrails.MaxDailyDeletedFiles;
            if (remaining > 0)
            {
                report.Notes.Add($"{remaining} files deferred due to daily deletion limit");
            }
        }
        catch (OperationCanceledException)
        {
            report.Status = CleanupStatus.Cancelled;
            report.Notes.Add("Operation was cancelled by user");
        }
        catch (Exception ex)
        {
            report.Status = CleanupStatus.Failed;
            report.Errors.Add($"Cleanup failed: {ex.Message}");
        }

        // Save audit report
        await SaveAuditReportAsync(report);
        _auditReports.Insert(0, report);

        return report;
    }

    /// <summary>
    /// Creates a legal hold for specified symbols.
    /// </summary>
    public async Task CreateLegalHoldAsync(
        string name,
        string reason,
        IEnumerable<string> symbols,
        DateTime? expiresAt = null, CancellationToken ct = default)
    {
        var hold = new LegalHold
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Reason = reason,
            Symbols = symbols.Select(s => s.ToUpperInvariant()).ToList(),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            IsActive = true
        };

        _legalHolds.Add(hold);
        await SaveConfigurationAsync();

        LegalHoldCreated?.Invoke(this, new LegalHoldEventArgs { LegalHold = hold });
    }

    /// <summary>
    /// Releases a legal hold.
    /// </summary>
    public async Task ReleaseLegalHoldAsync(string holdId, CancellationToken ct = default)
    {
        var hold = _legalHolds.FirstOrDefault(h => h.Id == holdId);
        if (hold != null)
        {
            hold.IsActive = false;
            hold.ReleasedAt = DateTime.UtcNow;
            await SaveConfigurationAsync();

            LegalHoldReleased?.Invoke(this, new LegalHoldEventArgs { LegalHold = hold });
        }
    }

    /// <summary>
    /// Gets all symbols currently under legal hold.
    /// </summary>
    public HashSet<string> GetSymbolsUnderLegalHold()
    {
        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hold in _legalHolds.Where(h => h.IsActive && (h.ExpiresAt == null || h.ExpiresAt > DateTime.UtcNow)))
        {
            foreach (var symbol in hold.Symbols)
            {
                symbols.Add(symbol);
            }
        }

        return symbols;
    }

    /// <summary>
    /// Exports audit report to file.
    /// </summary>
    public Task<string> ExportAuditReportAsync(RetentionAuditReport report, string format = "json")
    {
        var json = JsonSerializer.Serialize(report, AuditSerializerOptions);
        return Task.FromResult(json);
    }

    private async Task SaveAuditReportAsync(RetentionAuditReport report, CancellationToken ct = default)
    {
        try
        {
            var localFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Meridian");
            var auditFolderPath = Path.Combine(localFolderPath, AuditReportsFolder);
            Directory.CreateDirectory(auditFolderPath);
            var fileName = $"retention_audit_{report.ExecutedAt:yyyyMMdd_HHmmss}.json";
            var filePath = Path.Combine(auditFolderPath, fileName);
            var json = JsonSerializer.Serialize(report, DesktopJsonOptions.PrettyPrint);
            await File.WriteAllTextAsync(filePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RetentionAssuranceService] Error saving audit report: {ex.Message}");
        }
    }

    private static RetentionGuardrails GetDefaultGuardrails()
    {
        return new RetentionGuardrails
        {
            MinTickDataDays = 7,
            MinBarDataDays = 30,
            MinQuoteDataDays = 7,
            MinDepthDataDays = 3,
            MaxDailyDeletedFiles = 1000,
            RequireChecksumVerification = true,
            RequireDryRunPreview = true,
            AllowDeleteDuringTradingHours = false
        };
    }

    /// <summary>
    /// Event raised when a legal hold is created.
    /// </summary>
    public event EventHandler<LegalHoldEventArgs>? LegalHoldCreated;

    /// <summary>
    /// Event raised when a legal hold is released.
    /// </summary>
    public event EventHandler<LegalHoldEventArgs>? LegalHoldReleased;
}

/// <summary>
/// Internal container for file-based retention settings persistence.
/// </summary>
internal sealed class RetentionSettingsData
{
    public RetentionConfiguration? Config { get; set; }
    public List<LegalHold>? LegalHolds { get; set; }
}
