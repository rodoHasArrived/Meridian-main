using System;
using System.Collections.Generic;
using System.Linq;

namespace Meridian.Ui.Services;

#region Models

/// <summary>
/// Retention configuration.
/// </summary>
public sealed class RetentionConfiguration
{
    public RetentionGuardrails Guardrails { get; set; } = new();
    public bool EnableScheduledCleanup { get; set; }
    public string CleanupSchedule { get; set; } = "0 3 * * 0"; // Weekly at 3 AM Sunday
    public bool NotifyOnCleanup { get; set; } = true;
    public bool RequireApproval { get; set; }
}

/// <summary>
/// Retention guardrails to prevent accidental data loss.
/// </summary>
public sealed class RetentionGuardrails
{
    public int MinTickDataDays { get; set; } = 7;
    public int MinBarDataDays { get; set; } = 30;
    public int MinQuoteDataDays { get; set; } = 7;
    public int MinDepthDataDays { get; set; } = 3;
    public int MaxDailyDeletedFiles { get; set; } = 1000;
    public bool RequireChecksumVerification { get; set; } = true;
    public bool RequireDryRunPreview { get; set; } = true;
    public bool AllowDeleteDuringTradingHours { get; set; }
}

/// <summary>
/// Retention policy definition.
/// </summary>
public sealed class RetentionPolicy
{
    public int TickDataDays { get; set; } = 30;
    public int BarDataDays { get; set; } = 365;
    public int QuoteDataDays { get; set; } = 30;
    public int DepthDataDays { get; set; } = 7;
    public int DeletedFilesPerRun { get; set; } = 100;
    public bool CompressBeforeDelete { get; set; } = true;
    public bool ArchiveToCloud { get; set; }
    public string? CloudArchiveDestination { get; set; }
}

/// <summary>
/// Result of retention policy validation.
/// </summary>
public sealed class RetentionValidationResult
{
    public bool IsValid { get; set; }
    public List<GuardrailViolation> Violations { get; set; } = new();
    public List<GuardrailViolation> Warnings { get; set; } = new();
}

/// <summary>
/// A guardrail violation or warning.
/// </summary>
public sealed class GuardrailViolation
{
    public string Rule { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public ViolationSeverity Severity { get; set; }
}

/// <summary>
/// Severity level for violations.
/// </summary>
public enum ViolationSeverity : byte
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Legal hold definition.
/// </summary>
public sealed class LegalHold
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public List<string> Symbols { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public string? CreatedBy { get; set; }
}

/// <summary>
/// Result of a dry run.
/// </summary>
public sealed class RetentionDryRunResult
{
    public RetentionPolicy PolicyApplied { get; set; } = new();
    public DateTime ExecutedAt { get; set; }
    public List<FileToDelete> FilesToDelete { get; set; } = new();
    public List<SkippedFileInfo> SkippedFiles { get; set; } = new();
    public long TotalBytesToDelete { get; set; }
    public Dictionary<string, SymbolDeletionSummary> BySymbol { get; set; } = new();
    public List<string> Errors { get; set; } = new();

    public void GroupBySymbol()
    {
        BySymbol = FilesToDelete
            .GroupBy(f => f.Symbol)
            .ToDictionary(
                g => g.Key,
                g => new SymbolDeletionSummary
                {
                    Symbol = g.Key,
                    FileCount = g.Count(),
                    TotalBytes = g.Sum(f => f.Size),
                    DataTypes = g.Select(f => f.DataType).Distinct().ToList()
                });
    }
}

/// <summary>
/// File marked for deletion.
/// </summary>
public sealed class FileToDelete
{
    public string Path { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string DataType { get; set; } = string.Empty;
}

/// <summary>
/// File that was skipped.
/// </summary>
public sealed class SkippedFileInfo
{
    public string Path { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public long Size { get; set; }
}

/// <summary>
/// Summary of deletions by symbol.
/// </summary>
public sealed class SymbolDeletionSummary
{
    public string Symbol { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public long TotalBytes { get; set; }
    public List<string> DataTypes { get; set; } = new();
}

/// <summary>
/// Result of checksum verification.
/// </summary>
public sealed class ChecksumVerificationResult
{
    public List<VerifiedFile> VerifiedFiles { get; set; } = new();
    public List<string> MissingFiles { get; set; } = new();
    public List<string> MismatchedFiles { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// A verified file with checksum.
/// </summary>
public sealed class VerifiedFile
{
    public string Path { get; set; } = string.Empty;
    public string Checksum { get; set; } = string.Empty;
    public bool IsVerified { get; set; }
    public long Size { get; set; }
}

/// <summary>
/// Audit report for retention cleanup.
/// </summary>
public sealed class RetentionAuditReport
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime ExecutedAt { get; set; }
    public CleanupStatus Status { get; set; }
    public RetentionPolicy PolicyApplied { get; set; } = new();
    public int DryRunFilesCount { get; set; }
    public long DryRunBytesTotal { get; set; }
    public List<DeletedFileInfo> DeletedFiles { get; set; } = new();
    public long ActualBytesDeleted { get; set; }
    public ChecksumVerificationResult? ChecksumVerification { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}

/// <summary>
/// Cleanup status.
/// </summary>
public enum CleanupStatus : byte
{
    Pending,
    Success,
    PartialSuccess,
    Failed,
    FailedVerification,
    Cancelled
}

/// <summary>
/// Information about a deleted file.
/// </summary>
public sealed class DeletedFileInfo
{
    public string Path { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime DeletedAt { get; set; }
}

/// <summary>
/// Legal hold event args.
/// </summary>
public sealed class LegalHoldEventArgs : EventArgs
{
    public LegalHold? LegalHold { get; set; }
}

#endregion

#region API Response Models

/// <summary>
/// Response from /api/storage/search/files endpoint.
/// </summary>
public sealed class FileSearchApiResponse
{
    public int TotalMatches { get; set; }
    public List<FileSearchResult> Results { get; set; } = new();
}

/// <summary>
/// File search result from core API.
/// </summary>
public sealed class FileSearchResult
{
    public string Path { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public string? EventType { get; set; }
    public string? Source { get; set; }
    public DateTimeOffset Date { get; set; }
    public long SizeBytes { get; set; }
    public long EventCount { get; set; }
    public double QualityScore { get; set; }
}

/// <summary>
/// Response from /api/storage/health/check endpoint.
/// </summary>
public sealed class StorageHealthCheckResult
{
    public string? ReportId { get; set; }
    public DateTime GeneratedAt { get; set; }
    public long ScanDurationMs { get; set; }
    public HealthSummary? Summary { get; set; }
    public List<HealthIssue> Issues { get; set; } = new();
}

/// <summary>
/// Health summary from core API.
/// </summary>
public sealed class HealthSummary
{
    public int TotalFiles { get; set; }
    public long TotalBytes { get; set; }
    public int HealthyFiles { get; set; }
    public int WarningFiles { get; set; }
    public int CorruptedFiles { get; set; }
    public int OrphanedFiles { get; set; }
}

/// <summary>
/// Health issue from core API.
/// </summary>
public sealed class HealthIssue
{
    public string Severity { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string RecommendedAction { get; set; } = string.Empty;
    public bool AutoRepairable { get; set; }
}

/// <summary>
/// Response from /api/storage/health/orphans endpoint.
/// </summary>
public sealed class OrphanFilesResult
{
    public DateTime GeneratedAt { get; set; }
    public List<OrphanedFileInfo> OrphanedFiles { get; set; } = new();
    public long TotalOrphanedBytes { get; set; }
}

/// <summary>
/// Orphaned file info from core API.
/// </summary>
public sealed class OrphanedFileInfo
{
    public string Path { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
}

#endregion
