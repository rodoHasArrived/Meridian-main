using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services;

/// <summary>
/// Interface for administrative and maintenance operations including
/// archive scheduling, tier migration, retention policies, and file cleanup.
/// Shared between WPF desktop applications.
/// </summary>
public interface IAdminMaintenanceService
{
    // Schedule
    Task<MaintenanceScheduleResult> GetMaintenanceScheduleAsync(CancellationToken ct = default);
    Task<OperationResult> UpdateMaintenanceScheduleAsync(MaintenanceScheduleConfig schedule, CancellationToken ct = default);

    // Maintenance runs
    Task<MaintenanceRunResult> RunMaintenanceNowAsync(MaintenanceRunOptions? options = null, CancellationToken ct = default);
    Task<MaintenanceRunResult> GetMaintenanceRunStatusAsync(string runId, CancellationToken ct = default);
    Task<MaintenanceHistoryResult> GetMaintenanceHistoryAsync(int limit = 20, CancellationToken ct = default);

    // Tier management
    Task<TierConfigResult> GetTierConfigurationAsync(CancellationToken ct = default);
    Task<OperationResult> UpdateTierConfigurationAsync(List<StorageTierConfig> tiers, bool autoMigrationEnabled, string? migrationSchedule = null, CancellationToken ct = default);
    Task<TierMigrationResult> MigrateToTierAsync(string targetTier, TierMigrationOptions? options = null, CancellationToken ct = default);
    Task<TierUsageResult> GetTierUsageAsync(CancellationToken ct = default);

    // Retention policies
    Task<RetentionPoliciesResult> GetRetentionPoliciesAsync(CancellationToken ct = default);
    Task<OperationResult> SaveRetentionPolicyAsync(StorageRetentionPolicy policy, CancellationToken ct = default);
    Task<OperationResult> DeleteRetentionPolicyAsync(string policyId, CancellationToken ct = default);
    Task<RetentionApplyResult> ApplyRetentionPoliciesAsync(bool dryRun = false, CancellationToken ct = default);

    // Cleanup
    Task<CleanupPreviewResult> PreviewCleanupAsync(CleanupOptions options, CancellationToken ct = default);
    Task<MaintenanceCleanupResult> ExecuteCleanupAsync(CleanupOptions options, CancellationToken ct = default);

    // Diagnostics
    Task<PermissionValidationResult> ValidatePermissionsAsync(CancellationToken ct = default);
    Task<SelfTestResult> RunSelfTestAsync(SelfTestOptions? options = null, CancellationToken ct = default);
    Task<ErrorCodesResult> GetErrorCodesAsync(CancellationToken ct = default);
    Task<ShowConfigResult> ShowConfigAsync(CancellationToken ct = default);
    Task<QuickCheckResult> RunQuickCheckAsync(CancellationToken ct = default);
}
