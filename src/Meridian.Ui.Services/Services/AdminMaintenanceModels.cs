using System;
using System.Collections.Generic;

namespace Meridian.Ui.Services;

// =====================================================================================
// Admin Maintenance DTOs — shared across desktop applications.
// Extracted from duplicate definitions to provide a single source of truth.
// =====================================================================================

#region Schedule Models

/// <summary>
/// Result of getting the maintenance schedule.
/// </summary>
public sealed class MaintenanceScheduleResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public MaintenanceScheduleResponse? Schedule { get; set; }
}

/// <summary>
/// Response payload describing the current maintenance schedule.
/// </summary>
public sealed class MaintenanceScheduleResponse
{
    public bool Enabled { get; set; }
    public string? CronExpression { get; set; }
    public string? HumanReadable { get; set; }
    public DateTime? NextRunTime { get; set; }
    public DateTime? LastRunTime { get; set; }
    public List<string> EnabledOperations { get; set; } = new();
}

/// <summary>
/// Configuration for updating the maintenance schedule.
/// </summary>
public sealed class MaintenanceScheduleConfig
{
    public bool Enabled { get; set; }
    public string? CronExpression { get; set; }
    public bool RunCompression { get; set; }
    public bool RunCleanup { get; set; }
    public bool RunIntegrityCheck { get; set; }
    public bool RunTierMigration { get; set; }
}

#endregion

#region Maintenance Run Models

/// <summary>
/// Options for triggering a maintenance run.
/// </summary>
public sealed class MaintenanceRunOptions
{
    public bool RunCompression { get; set; } = true;
    public bool RunCleanup { get; set; } = true;
    public bool RunIntegrityCheck { get; set; } = true;
    public bool RunTierMigration { get; set; }
    public bool DryRun { get; set; }
}

/// <summary>
/// Result of a maintenance run operation.
/// </summary>
public sealed class MaintenanceRunResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? RunId { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? Status { get; set; }
    public List<MaintenanceOperation> Operations { get; set; } = new();
}

/// <summary>
/// HTTP response payload for a maintenance run.
/// </summary>
public sealed class MaintenanceRunResponse
{
    public string? RunId { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? Status { get; set; }
    public List<MaintenanceOperation>? Operations { get; set; }
}

/// <summary>
/// Individual maintenance operation within a run.
/// </summary>
public sealed class MaintenanceOperation
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int ItemsProcessed { get; set; }
    public long BytesProcessed { get; set; }
    public string? Error { get; set; }
}

#endregion

#region Maintenance History Models

/// <summary>
/// Result of getting maintenance history.
/// </summary>
public sealed class MaintenanceHistoryResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<MaintenanceRunSummary> Runs { get; set; } = new();
}

/// <summary>
/// HTTP response payload for maintenance history.
/// </summary>
public sealed class MaintenanceHistoryResponse
{
    public List<MaintenanceRunSummary>? Runs { get; set; }
}

/// <summary>
/// Summary of a completed maintenance run.
/// </summary>
public sealed class MaintenanceRunSummary
{
    public string RunId { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public int OperationsCompleted { get; set; }
    public int OperationsFailed { get; set; }
}

#endregion

#region Tier Models

/// <summary>
/// Result of getting tier configuration.
/// </summary>
public sealed class TierConfigResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<StorageTierConfig> Tiers { get; set; } = new();
    public bool AutoMigrationEnabled { get; set; }
    public string? MigrationSchedule { get; set; }
}

/// <summary>
/// HTTP response payload for tier configuration.
/// </summary>
public sealed class TierConfigResponse
{
    public List<StorageTierConfig>? Tiers { get; set; }
    public bool AutoMigrationEnabled { get; set; }
    public string? MigrationSchedule { get; set; }
}

/// <summary>
/// Configuration for a single storage tier.
/// </summary>
public sealed class StorageTierConfig
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int Priority { get; set; }
    public int RetentionDays { get; set; }
    public string CompressionLevel { get; set; } = "Standard";
    public bool Enabled { get; set; }
}

/// <summary>
/// Options for tier migration.
/// </summary>
public sealed class TierMigrationOptions
{
    public DateOnly? OlderThan { get; set; }
    public List<string>? Symbols { get; set; }
    public List<string>? EventTypes { get; set; }
    public bool DryRun { get; set; }
}

/// <summary>
/// Result of a tier migration operation.
/// </summary>
public sealed class TierMigrationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int FilesProcessed { get; set; }
    public long BytesMigrated { get; set; }
    public long SpaceSavedBytes { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// HTTP response payload for tier migration.
/// </summary>
public sealed class TierMigrationResponse
{
    public int FilesProcessed { get; set; }
    public long BytesMigrated { get; set; }
    public long SpaceSavedBytes { get; set; }
    public string[]? Errors { get; set; }
}

/// <summary>
/// Result of getting tier usage statistics.
/// </summary>
public sealed class TierUsageResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<TierUsage> TierUsage { get; set; } = new();
    public long TotalSizeBytes { get; set; }
    public long TotalFiles { get; set; }
}

/// <summary>
/// HTTP response payload for tier usage.
/// </summary>
public sealed class TierUsageResponse
{
    public List<TierUsage>? TierUsage { get; set; }
    public long TotalSizeBytes { get; set; }
    public long TotalFiles { get; set; }
}

/// <summary>
/// Usage statistics for a single storage tier.
/// </summary>
public sealed class TierUsage
{
    public string TierName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public long FileCount { get; set; }
    public double PercentOfTotal { get; set; }
    public DateOnly? OldestData { get; set; }
    public DateOnly? NewestData { get; set; }
}

#endregion

#region Retention Policy Models

/// <summary>
/// Result of getting retention policies.
/// </summary>
public sealed class RetentionPoliciesResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<StorageRetentionPolicy> Policies { get; set; } = new();
    public StorageRetentionPolicy? DefaultPolicy { get; set; }
}

/// <summary>
/// HTTP response payload for retention policies.
/// </summary>
public sealed class RetentionPoliciesResponse
{
    public List<StorageRetentionPolicy>? Policies { get; set; }
    public StorageRetentionPolicy? DefaultPolicy { get; set; }
}

/// <summary>
/// A storage retention policy definition for administrative operations.
/// Note: Renamed from RetentionPolicy to avoid conflict with Meridian.Ui.Services.RetentionPolicy
/// in RetentionAssuranceModels.cs which handles data retention periods.
/// </summary>
public sealed class StorageRetentionPolicy
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int RetentionDays { get; set; }
    public string? SymbolPattern { get; set; }
    public List<string>? EventTypes { get; set; }
    public bool Enabled { get; set; }
    public bool IsDefault { get; set; }
}

/// <summary>
/// Result of applying retention policies.
/// </summary>
public sealed class RetentionApplyResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int FilesDeleted { get; set; }
    public long BytesFreed { get; set; }
    public List<string> AffectedSymbols { get; set; } = new();
    public bool WasDryRun { get; set; }
}

/// <summary>
/// HTTP response payload for retention apply.
/// </summary>
public sealed class RetentionApplyResponse
{
    public int FilesDeleted { get; set; }
    public long BytesFreed { get; set; }
    public string[]? AffectedSymbols { get; set; }
}

#endregion

#region Cleanup Models

/// <summary>
/// Options for file cleanup operations.
/// </summary>
public sealed class CleanupOptions
{
    public bool DeleteEmptyDirectories { get; set; } = true;
    public bool DeleteTempFiles { get; set; } = true;
    public bool DeleteOrphanedFiles { get; set; }
    public bool DeleteCorruptFiles { get; set; }
    public int OlderThanDays { get; set; }
}

/// <summary>
/// Result of previewing cleanup candidates.
/// </summary>
public sealed class CleanupPreviewResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<CleanupFileInfo> FilesToDelete { get; set; } = new();
    public long TotalBytes { get; set; }
    public int TotalFiles { get; set; }
}

/// <summary>
/// HTTP response payload for cleanup preview.
/// </summary>
public sealed class CleanupPreviewResponse
{
    public List<CleanupFileInfo>? FilesToDelete { get; set; }
    public long TotalBytes { get; set; }
    public int TotalFiles { get; set; }
}

/// <summary>
/// Information about a file eligible for cleanup.
/// </summary>
public sealed class CleanupFileInfo
{
    public string Path { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
}

/// <summary>
/// Result of executing cleanup.
/// </summary>
public sealed class MaintenanceCleanupResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int FilesDeleted { get; set; }
    public long BytesFreed { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// HTTP response payload for cleanup execution.
/// </summary>
public sealed class CleanupResultResponse
{
    public int FilesDeleted { get; set; }
    public long BytesFreed { get; set; }
    public string[]? Errors { get; set; }
}

#endregion

#region Permission Models (Non-Diagnostics)

/// <summary>
/// Result of permission validation.
/// </summary>
public sealed class PermissionValidationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public bool CanRead { get; set; }
    public bool CanWrite { get; set; }
    public bool CanDelete { get; set; }
    public List<string> Issues { get; set; } = new();
}

/// <summary>
/// HTTP response payload for permission validation.
/// </summary>
public sealed class PermissionValidationResponse
{
    public bool CanRead { get; set; }
    public bool CanWrite { get; set; }
    public bool CanDelete { get; set; }
    public string[]? Issues { get; set; }
}

// NOTE: SelfTest*, ErrorCodes*, ShowConfig*, QuickCheck* models are defined in DiagnosticsService.cs
// to avoid duplication and maintain single source of truth

#endregion

#region Shared Models

/// <summary>
/// HTTP operation response used for POST mutations.
/// </summary>
public sealed class OperationResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

#endregion
