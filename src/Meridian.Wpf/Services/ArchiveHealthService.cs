using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Archive;
using Meridian.Storage.Services;
using Meridian.Ui.Services;
using HttpClientFactoryProvider = Meridian.Ui.Services.HttpClientFactoryProvider;
using HttpClientNames = Meridian.Ui.Services.HttpClientNames;

namespace Meridian.Wpf.Services;

/// <summary>
/// Service for monitoring archive health and verification.
/// </summary>
public sealed class ArchiveHealthService
{
    private static readonly Lazy<ArchiveHealthService> _instance = new(() => new ArchiveHealthService());
    public static ArchiveHealthService Instance => _instance.Value;

    private readonly HttpClient _httpClient;
    private readonly string _healthStatusPath;
    private ArchiveHealthStatus? _cachedHealthStatus;
    private DateTime _lastHealthCheck;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);
    private VerificationJob? _currentVerificationJob;
    private CancellationTokenSource? _verificationCts;

    private ArchiveHealthService()
    {
        _httpClient = HttpClientFactoryProvider.CreateClient(HttpClientNames.Default);
        _healthStatusPath = Path.Combine(AppContext.BaseDirectory, "_catalog", "archive_health.json");
    }

    public event EventHandler<ArchiveHealthEventArgs>? HealthStatusUpdated;
    public event EventHandler<VerificationJobEventArgs>? VerificationStarted;
    public event EventHandler<VerificationJobEventArgs>? VerificationCompleted;
    public event EventHandler<ArchiveIssueEventArgs>? IssueResolved;

    public async Task<ArchiveHealthStatus> GetHealthStatusAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh && _cachedHealthStatus != null &&
            DateTime.UtcNow - _lastHealthCheck < _cacheExpiration)
        {
            return _cachedHealthStatus;
        }

        try
        {
            var response = await _httpClient.GetAsync("/api/archive/health");
            if (response.IsSuccessStatusCode)
            {
                var status = await response.Content.ReadFromJsonAsync<ArchiveHealthStatus>();
                if (status != null)
                {
                    _cachedHealthStatus = status;
                    _lastHealthCheck = DateTime.UtcNow;
                    HealthStatusUpdated?.Invoke(this, new ArchiveHealthEventArgs { Status = status });
                    return status;
                }
            }
        }
        catch
        {
            // Fall through to calculate locally
        }

        var localStatus = await CalculateHealthStatusAsync();
        _cachedHealthStatus = localStatus;
        _lastHealthCheck = DateTime.UtcNow;
        HealthStatusUpdated?.Invoke(this, new ArchiveHealthEventArgs { Status = localStatus });
        return localStatus;
    }

    public async Task<VerificationJob> StartFullVerificationAsync(IProgress<VerificationProgress>? progress = null, CancellationToken ct = default)
    {
        if (_currentVerificationJob?.Status == "Running")
        {
            throw new InvalidOperationException("A verification job is already running.");
        }

        _verificationCts = new CancellationTokenSource();
        _currentVerificationJob = new VerificationJob
        {
            Type = "Full",
            Status = "Running",
            StartedAt = DateTime.UtcNow
        };

        VerificationStarted?.Invoke(this, new VerificationJobEventArgs { Job = _currentVerificationJob });

        try
        {
            await RunVerificationAsync(_currentVerificationJob, null, progress, _verificationCts.Token);
            _currentVerificationJob.Status = _currentVerificationJob.FailedFiles > 0 ? "Failed" : "Completed";
            _currentVerificationJob.CompletedAt = DateTime.UtcNow;
        }
        catch (OperationCanceledException)
        {
            _currentVerificationJob.Status = "Cancelled";
            _currentVerificationJob.CompletedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _currentVerificationJob.Status = "Failed";
            _currentVerificationJob.CompletedAt = DateTime.UtcNow;
            _currentVerificationJob.Errors = (_currentVerificationJob.Errors?.ToList() ?? new List<string>())
                .Append(ex.Message).ToArray();
        }

        VerificationCompleted?.Invoke(this, new VerificationJobEventArgs { Job = _currentVerificationJob });
        await GetHealthStatusAsync(true);
        return _currentVerificationJob;
    }

    public async Task<VerificationJob> StartIncrementalVerificationAsync(DateTime since, IProgress<VerificationProgress>? progress = null, CancellationToken ct = default)
    {
        if (_currentVerificationJob?.Status == "Running")
        {
            throw new InvalidOperationException("A verification job is already running.");
        }

        _verificationCts = new CancellationTokenSource();
        _currentVerificationJob = new VerificationJob
        {
            Type = "Incremental",
            Status = "Running",
            StartedAt = DateTime.UtcNow
        };

        VerificationStarted?.Invoke(this, new VerificationJobEventArgs { Job = _currentVerificationJob });

        try
        {
            await RunVerificationAsync(_currentVerificationJob, since, progress, _verificationCts.Token);
            _currentVerificationJob.Status = _currentVerificationJob.FailedFiles > 0 ? "Failed" : "Completed";
            _currentVerificationJob.CompletedAt = DateTime.UtcNow;
        }
        catch (OperationCanceledException)
        {
            _currentVerificationJob.Status = "Cancelled";
            _currentVerificationJob.CompletedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _currentVerificationJob.Status = "Failed";
            _currentVerificationJob.CompletedAt = DateTime.UtcNow;
            _currentVerificationJob.Errors = (_currentVerificationJob.Errors?.ToList() ?? new List<string>())
                .Append(ex.Message).ToArray();
        }

        VerificationCompleted?.Invoke(this, new VerificationJobEventArgs { Job = _currentVerificationJob });
        return _currentVerificationJob;
    }

    public void CancelVerification()
    {
        _verificationCts?.Cancel();
    }

    public VerificationJob? GetCurrentVerificationJob() => _currentVerificationJob;

    public async Task ResolveIssueAsync(string issueId, CancellationToken ct = default)
    {
        var status = await GetHealthStatusAsync();
        var issue = status.Issues?.FirstOrDefault(i => i.Id == issueId);

        if (issue != null)
        {
            issue.ResolvedAt = DateTime.UtcNow;
            await SaveHealthStatusAsync(status);
            IssueResolved?.Invoke(this, new ArchiveIssueEventArgs { Issue = issue });
        }
    }

    public async Task<AuditChainVerifyResult> VerifyAuditChainAsync(CancellationToken ct = default)
    {
        try
        {
            var config = await ConfigService.Instance.LoadConfigAsync();
            var dataRoot = config?.DataRoot ?? "data";
            var basePath = Path.IsPathRooted(dataRoot)
                ? dataRoot
                : Path.Combine(AppContext.BaseDirectory, dataRoot);

            var checksumService = new StorageChecksumService();
            var result = await checksumService.VerifyAuditChainAsync(basePath, ct);

            return result;
        }
        catch (Exception ex)
        {
            return new AuditChainVerifyResult
            {
                IsValid = false,
                EntriesChecked = 0,
                FirstTamperPath = ex.Message
            };
        }
    }

    private async Task<ArchiveHealthStatus> CalculateHealthStatusAsync(CancellationToken ct = default)
    {
        var config = await ConfigService.Instance.LoadConfigAsync();
        var dataRoot = config?.DataRoot ?? "data";
        var basePath = Path.IsPathRooted(dataRoot)
            ? dataRoot
            : Path.Combine(AppContext.BaseDirectory, dataRoot);

        var status = new ArchiveHealthStatus
        {
            LastUpdated = DateTime.UtcNow
        };

        var issues = new List<ArchiveIssue>();

        status.StorageHealthInfo = await GetStorageHealthInfoAsync(basePath);

        if (status.StorageHealthInfo.UsedPercent >= 95)
        {
            issues.Add(new ArchiveIssue
            {
                Severity = "Critical",
                Category = "Storage",
                Message = $"Storage is {status.StorageHealthInfo.UsedPercent:F1}% full. Immediate action required.",
                SuggestedAction = "Free up disk space or move data to cold storage",
                IsAutoFixable = false
            });
        }
        else if (status.StorageHealthInfo.UsedPercent >= 85)
        {
            issues.Add(new ArchiveIssue
            {
                Severity = "Warning",
                Category = "Storage",
                Message = $"Storage is {status.StorageHealthInfo.UsedPercent:F1}% full.",
                SuggestedAction = "Consider archiving older data to cold storage",
                IsAutoFixable = false
            });
        }

        if (Directory.Exists(basePath))
        {
            await Task.Run(() =>
            {
                var files = Directory.GetFiles(basePath, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".jsonl") || f.EndsWith(".jsonl.gz") || f.EndsWith(".parquet"))
                    .ToList();

                status.TotalFiles = files.Count;
                status.TotalSizeBytes = files.Sum(f => new FileInfo(f).Length);

                var emptyFiles = files.Where(f => new FileInfo(f).Length == 0).ToList();
                if (emptyFiles.Any())
                {
                    issues.Add(new ArchiveIssue
                    {
                        Severity = "Warning",
                        Category = "Integrity",
                        Message = $"Found {emptyFiles.Count} empty data files",
                        AffectedFiles = emptyFiles.Take(10).ToArray(),
                        SuggestedAction = "Delete or re-download affected files",
                        IsAutoFixable = true
                    });
                }

                status.PendingFiles = status.TotalFiles - status.VerifiedFiles - status.FailedFiles;
            });
        }

        status.Issues = issues.ToArray();

        var recommendations = new List<string>();
        if (status.PendingFiles > status.TotalFiles * 0.2)
        {
            recommendations.Add("Run a full verification to ensure archive integrity");
        }
        if (status.StorageHealthInfo.DaysUntilFull.HasValue && status.StorageHealthInfo.DaysUntilFull < 30)
        {
            recommendations.Add($"Storage will be full in ~{status.StorageHealthInfo.DaysUntilFull} days. Plan for capacity expansion.");
        }
        if (status.FailedFiles > 0)
        {
            recommendations.Add($"Repair or re-download {status.FailedFiles} failed files");
        }

        status.Recommendations = recommendations.ToArray();
        status.OverallHealthScore = (float)CalculateOverallHealthScore(status);
        status.Status = status.OverallHealthScore switch
        {
            >= 90 => "Healthy",
            >= 70 => "Warning",
            _ => "Critical"
        };

        return status;
    }

    private static Task<StorageHealthInfo> GetStorageHealthInfoAsync(string basePath)
    {
        var info = new StorageHealthInfo();

        try
        {
            var root = Path.GetPathRoot(basePath);
            if (string.IsNullOrEmpty(root))
            {
                root = Path.GetPathRoot(AppContext.BaseDirectory) ?? "C:\\";
            }

            var driveInfo = new DriveInfo(root);
            info.TotalCapacity = driveInfo.TotalSize;
            info.FreeSpace = driveInfo.AvailableFreeSpace;
            info.UsedPercent = (float)((1.0 - (double)driveInfo.AvailableFreeSpace / driveInfo.TotalSize) * 100);
            info.DriveType = driveInfo.DriveType.ToString();
            info.HealthStatus = driveInfo.DriveType switch
            {
                System.IO.DriveType.Fixed or System.IO.DriveType.Removable => "Good",
                System.IO.DriveType.Network => "Unknown",
                _ => "Unknown"
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to get drive info: {ex.Message}");
        }

        return Task.FromResult(info);
    }

    private async Task RunVerificationAsync(VerificationJob job, DateTime? since,
        IProgress<VerificationProgress>? progress, CancellationToken cancellationToken)
    {
        var config = await ConfigService.Instance.LoadConfigAsync();
        var dataRoot = config?.DataRoot ?? "data";
        var basePath = Path.IsPathRooted(dataRoot)
            ? dataRoot
            : Path.Combine(AppContext.BaseDirectory, dataRoot);

        if (!Directory.Exists(basePath)) return;

        var files = Directory.GetFiles(basePath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".jsonl") || f.EndsWith(".jsonl.gz") || f.EndsWith(".parquet"))
            .ToList();

        if (since.HasValue)
        {
            files = files.Where(f => new FileInfo(f).LastWriteTimeUtc >= since.Value).ToList();
        }

        job.TotalFiles = files.Count;
        var errors = new List<string>();
        var startTime = DateTime.UtcNow;

        for (int i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = files[i];
            job.ProcessedFiles = i + 1;

            try
            {
                var isValid = await VerifyFileAsync(file);
                if (isValid)
                {
                    job.VerifiedFiles++;
                }
                else
                {
                    job.FailedFiles++;
                    errors.Add($"Verification failed: {file}");
                }
            }
            catch (Exception ex)
            {
                job.FailedFiles++;
                errors.Add($"{file}: {ex.Message}");
            }

            var elapsed = DateTime.UtcNow - startTime;
            job.ProgressPercent = (float)((double)(i + 1) / files.Count * 100);
            job.FilesPerSecond = (float)((i + 1) / elapsed.TotalSeconds);

            if (i + 1 < files.Count && job.FilesPerSecond > 0)
            {
                job.EstimatedTimeRemainingSeconds = (int)((files.Count - i - 1) / job.FilesPerSecond);
            }

            progress?.Report(new VerificationProgress
            {
                ProcessedFiles = job.ProcessedFiles,
                TotalFiles = job.TotalFiles,
                VerifiedFiles = job.VerifiedFiles,
                FailedFiles = job.FailedFiles,
                ProgressPercent = job.ProgressPercent,
                CurrentFile = Path.GetFileName(file),
                FilesPerSecond = job.FilesPerSecond,
                EstimatedTimeRemainingSeconds = job.EstimatedTimeRemainingSeconds
            });

            if (i % 10 == 0)
            {
                await Task.Delay(1, cancellationToken);
            }
        }

        job.Errors = errors.ToArray();
    }

    private static async Task<bool> VerifyFileAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length == 0) return false;

            using var sha256 = SHA256.Create();
            await using var stream = File.OpenRead(filePath);

            if (filePath.EndsWith(".gz"))
            {
                await using var gzipStream = new System.IO.Compression.GZipStream(stream,
                    System.IO.Compression.CompressionMode.Decompress);
                await sha256.ComputeHashAsync(gzipStream);
            }
            else
            {
                await sha256.ComputeHashAsync(stream);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static double CalculateOverallHealthScore(ArchiveHealthStatus status)
    {
        var score = 100.0;

        if (status.TotalFiles > 0)
        {
            var failedPercent = (double)status.FailedFiles / status.TotalFiles * 100;
            score -= failedPercent * 2;
        }

        if (status.StorageHealthInfo?.UsedPercent >= 95) score -= 30;
        else if (status.StorageHealthInfo?.UsedPercent >= 85) score -= 10;

        var criticalIssues = status.Issues?.Count(i => i.Severity == "Critical") ?? 0;
        score -= criticalIssues * 15;

        var warningIssues = status.Issues?.Count(i => i.Severity == "Warning") ?? 0;
        score -= warningIssues * 5;

        if (status.TotalFiles > 0 && status.PendingFiles > status.TotalFiles * 0.5)
        {
            score -= 10;
        }

        return Math.Max(0, Math.Min(100, score));
    }

    private async Task SaveHealthStatusAsync(ArchiveHealthStatus status, CancellationToken ct = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(_healthStatusPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(status, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await File.WriteAllTextAsync(_healthStatusPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save health status: {ex.Message}");
        }
    }
}

public sealed class ArchiveHealthEventArgs : EventArgs
{
    public ArchiveHealthStatus? Status { get; set; }
}

public sealed class VerificationJobEventArgs : EventArgs
{
    public VerificationJob? Job { get; set; }
}

public sealed class ArchiveIssueEventArgs : EventArgs
{
    public ArchiveIssue? Issue { get; set; }
}

public sealed class VerificationProgress
{
    public int ProcessedFiles { get; set; }
    public int TotalFiles { get; set; }
    public int VerifiedFiles { get; set; }
    public int FailedFiles { get; set; }
    public double ProgressPercent { get; set; }
    public string? CurrentFile { get; set; }
    public double FilesPerSecond { get; set; }
    public int? EstimatedTimeRemainingSeconds { get; set; }
}
