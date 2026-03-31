using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services;

/// <summary>
/// Base class for admin maintenance services, providing shared API delegation logic.
/// WPF implementations inherit from this class and add platform-specific behavior.
/// </summary>
public class AdminMaintenanceServiceBase : IAdminMaintenanceService
{
    private static readonly Lazy<AdminMaintenanceServiceBase> _instance = new(() => new AdminMaintenanceServiceBase());

    /// <summary>Shared singleton instance for use in DI registrations and direct access.</summary>
    public static AdminMaintenanceServiceBase Instance => _instance.Value;


    /// <inheritdoc />
    public async Task<MaintenanceScheduleResult> GetMaintenanceScheduleAsync(CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.GetWithResponseAsync<MaintenanceScheduleResponse>(
            "/api/admin/maintenance/schedule", ct);

        if (response.Success && response.Data != null)
        {
            return new MaintenanceScheduleResult { Success = true, Schedule = response.Data };
        }

        return new MaintenanceScheduleResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Failed to get maintenance schedule"
        };
    }

    /// <inheritdoc />
    public async Task<OperationResult> UpdateMaintenanceScheduleAsync(
        MaintenanceScheduleConfig schedule,
        CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<OperationResponse>(
            "/api/admin/maintenance/schedule", schedule, ct);

        return new OperationResult
        {
            Success = response.Success,
            Message = response.Data?.Message,
            Error = response.ErrorMessage
        };
    }

    /// <inheritdoc />
    public async Task<MaintenanceRunResult> RunMaintenanceNowAsync(
        MaintenanceRunOptions? options = null,
        CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<MaintenanceRunResponse>(
            "/api/admin/maintenance/run",
            options ?? new MaintenanceRunOptions(),
            ct);

        if (response.Success && response.Data != null)
        {
            return new MaintenanceRunResult
            {
                Success = true,
                RunId = response.Data.RunId,
                StartTime = response.Data.StartTime,
                Status = response.Data.Status,
                Operations = response.Data.Operations?.ToList() ?? new List<MaintenanceOperation>()
            };
        }

        return new MaintenanceRunResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Failed to run maintenance"
        };
    }

    /// <inheritdoc />
    public async Task<MaintenanceRunResult> GetMaintenanceRunStatusAsync(
        string runId,
        CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.GetWithResponseAsync<MaintenanceRunResponse>(
            $"/api/admin/maintenance/run/{runId}", ct);

        if (response.Success && response.Data != null)
        {
            return new MaintenanceRunResult
            {
                Success = true,
                RunId = response.Data.RunId,
                StartTime = response.Data.StartTime,
                EndTime = response.Data.EndTime,
                Status = response.Data.Status,
                Operations = response.Data.Operations?.ToList() ?? new List<MaintenanceOperation>()
            };
        }

        return new MaintenanceRunResult
        {
            Success = false,
            Error = response.ErrorMessage
        };
    }

    /// <inheritdoc />
    public async Task<MaintenanceHistoryResult> GetMaintenanceHistoryAsync(
        int limit = 20,
        CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.GetWithResponseAsync<MaintenanceHistoryResponse>(
            $"/api/admin/maintenance/history?limit={limit}", ct);

        if (response.Success && response.Data != null)
        {
            return new MaintenanceHistoryResult
            {
                Success = true,
                Runs = response.Data.Runs?.ToList() ?? new List<MaintenanceRunSummary>()
            };
        }

        return new MaintenanceHistoryResult
        {
            Success = false,
            Error = response.ErrorMessage
        };
    }



    /// <inheritdoc />
    public async Task<TierConfigResult> GetTierConfigurationAsync(CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.GetWithResponseAsync<TierConfigResponse>(
            "/api/admin/storage/tiers", ct);

        if (response.Success && response.Data != null)
        {
            return new TierConfigResult
            {
                Success = true,
                Tiers = response.Data.Tiers?.ToList() ?? new List<StorageTierConfig>(),
                AutoMigrationEnabled = response.Data.AutoMigrationEnabled,
                MigrationSchedule = response.Data.MigrationSchedule
            };
        }

        return new TierConfigResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Failed to get tier configuration"
        };
    }

    /// <inheritdoc />
    public async Task<OperationResult> UpdateTierConfigurationAsync(
        List<StorageTierConfig> tiers,
        bool autoMigrationEnabled,
        string? migrationSchedule = null,
        CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<OperationResponse>(
            "/api/admin/storage/tiers",
            new { tiers, autoMigrationEnabled, migrationSchedule },
            ct);

        return new OperationResult
        {
            Success = response.Success,
            Message = response.Data?.Message,
            Error = response.ErrorMessage
        };
    }

    /// <inheritdoc />
    public async Task<TierMigrationResult> MigrateToTierAsync(
        string targetTier,
        TierMigrationOptions? options = null,
        CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<TierMigrationResponse>(
            $"/api/admin/storage/migrate/{targetTier}",
            options ?? new TierMigrationOptions(),
            ct);

        if (response.Success && response.Data != null)
        {
            return new TierMigrationResult
            {
                Success = true,
                FilesProcessed = response.Data.FilesProcessed,
                BytesMigrated = response.Data.BytesMigrated,
                SpaceSavedBytes = response.Data.SpaceSavedBytes,
                Errors = response.Data.Errors?.ToList() ?? new List<string>()
            };
        }

        return new TierMigrationResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Migration failed"
        };
    }

    /// <inheritdoc />
    public async Task<TierUsageResult> GetTierUsageAsync(CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.GetWithResponseAsync<TierUsageResponse>(
            "/api/admin/storage/usage", ct);

        if (response.Success && response.Data != null)
        {
            return new TierUsageResult
            {
                Success = true,
                TierUsage = response.Data.TierUsage?.ToList() ?? new List<TierUsage>(),
                TotalSizeBytes = response.Data.TotalSizeBytes,
                TotalFiles = response.Data.TotalFiles
            };
        }

        return new TierUsageResult
        {
            Success = false,
            Error = response.ErrorMessage
        };
    }



    /// <inheritdoc />
    public async Task<RetentionPoliciesResult> GetRetentionPoliciesAsync(CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.GetWithResponseAsync<RetentionPoliciesResponse>(
            "/api/admin/retention", ct);

        if (response.Success && response.Data != null)
        {
            return new RetentionPoliciesResult
            {
                Success = true,
                Policies = response.Data.Policies?.ToList() ?? new List<StorageRetentionPolicy>(),
                DefaultPolicy = response.Data.DefaultPolicy
            };
        }

        return new RetentionPoliciesResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Failed to get retention policies"
        };
    }

    /// <inheritdoc />
    public async Task<OperationResult> SaveRetentionPolicyAsync(
        StorageRetentionPolicy policy,
        CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<OperationResponse>(
            "/api/admin/retention", policy, ct);

        return new OperationResult
        {
            Success = response.Success,
            Message = response.Data?.Message,
            Error = response.ErrorMessage
        };
    }

    /// <inheritdoc />
    public async Task<OperationResult> DeleteRetentionPolicyAsync(
        string policyId,
        CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<OperationResponse>(
            $"/api/admin/retention/{policyId}/delete", null, ct);

        return new OperationResult
        {
            Success = response.Success,
            Message = response.Data?.Message,
            Error = response.ErrorMessage
        };
    }

    /// <inheritdoc />
    public async Task<RetentionApplyResult> ApplyRetentionPoliciesAsync(
        bool dryRun = false,
        CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<RetentionApplyResponse>(
            "/api/admin/retention/apply", new { dryRun }, ct);

        if (response.Success && response.Data != null)
        {
            return new RetentionApplyResult
            {
                Success = true,
                FilesDeleted = response.Data.FilesDeleted,
                BytesFreed = response.Data.BytesFreed,
                AffectedSymbols = response.Data.AffectedSymbols?.ToList() ?? new List<string>(),
                WasDryRun = dryRun
            };
        }

        return new RetentionApplyResult
        {
            Success = false,
            Error = response.ErrorMessage
        };
    }



    /// <inheritdoc />
    public async Task<CleanupPreviewResult> PreviewCleanupAsync(
        CleanupOptions options,
        CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<CleanupPreviewResponse>(
            "/api/admin/cleanup/preview", options, ct);

        if (response.Success && response.Data != null)
        {
            return new CleanupPreviewResult
            {
                Success = true,
                FilesToDelete = response.Data.FilesToDelete?.ToList() ?? new List<CleanupFileInfo>(),
                TotalBytes = response.Data.TotalBytes,
                TotalFiles = response.Data.TotalFiles
            };
        }

        return new CleanupPreviewResult
        {
            Success = false,
            Error = response.ErrorMessage
        };
    }

    /// <inheritdoc />
    public async Task<MaintenanceCleanupResult> ExecuteCleanupAsync(
        CleanupOptions options,
        CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<CleanupResultResponse>(
            "/api/admin/cleanup/execute", options, ct);

        if (response.Success && response.Data != null)
        {
            return new MaintenanceCleanupResult
            {
                Success = true,
                FilesDeleted = response.Data.FilesDeleted,
                BytesFreed = response.Data.BytesFreed,
                Errors = response.Data.Errors?.ToList() ?? new List<string>()
            };
        }

        return new MaintenanceCleanupResult
        {
            Success = false,
            Error = response.ErrorMessage
        };
    }



    /// <inheritdoc />
    public async Task<PermissionValidationResult> ValidatePermissionsAsync(CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.GetWithResponseAsync<PermissionValidationResponse>(
            "/api/admin/storage/permissions", ct);

        if (response.Success && response.Data != null)
        {
            return new PermissionValidationResult
            {
                Success = true,
                CanRead = response.Data.CanRead,
                CanWrite = response.Data.CanWrite,
                CanDelete = response.Data.CanDelete,
                Issues = response.Data.Issues?.ToList() ?? new List<string>()
            };
        }

        return new PermissionValidationResult
        {
            Success = false,
            Error = response.ErrorMessage
        };
    }

    /// <inheritdoc />
    public async Task<SelfTestResult> RunSelfTestAsync(
        SelfTestOptions? options = null,
        CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<SelfTestResponse>(
            "/api/admin/selftest",
            options ?? new SelfTestOptions(),
            ct);

        if (response.Success && response.Data != null)
        {
            return new SelfTestResult
            {
                Success = response.Data.AllPassed,
                Tests = response.Data.Tests?.ToList() ?? new List<SelfTestItem>(),
                PassedCount = response.Data.PassedCount,
                FailedCount = response.Data.FailedCount,
                SkippedCount = response.Data.SkippedCount
            };
        }

        return new SelfTestResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Self-test failed"
        };
    }

    /// <inheritdoc />
    public async Task<ErrorCodesResult> GetErrorCodesAsync(CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.GetWithResponseAsync<ErrorCodesResponse>(
            "/api/admin/error-codes", ct);

        if (response.Success && response.Data != null)
        {
            return new ErrorCodesResult
            {
                Success = true,
                ErrorCodes = response.Data.ErrorCodes?.ToList() ?? new List<ErrorCodeInfo>()
            };
        }

        return new ErrorCodesResult
        {
            Success = false,
            Error = response.ErrorMessage
        };
    }

    /// <inheritdoc />
    public async Task<ShowConfigResult> ShowConfigAsync(CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.GetWithResponseAsync<ShowConfigResponse>(
            "/api/admin/show-config", ct);

        if (response.Success && response.Data != null)
        {
            return new ShowConfigResult
            {
                Success = true,
                Sections = response.Data.Sections?.ToList() ?? new List<ConfigSection>()
            };
        }

        return new ShowConfigResult
        {
            Success = false,
            Error = response.ErrorMessage
        };
    }

    /// <inheritdoc />
    public async Task<QuickCheckResult> RunQuickCheckAsync(CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.GetWithResponseAsync<QuickCheckResponse>(
            "/api/admin/quick-check", ct);

        if (response.Success && response.Data != null)
        {
            return new QuickCheckResult
            {
                Success = true,
                Overall = response.Data.Overall,
                Checks = response.Data.Checks?.ToList() ?? new List<QuickCheckItem>()
            };
        }

        return new QuickCheckResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Quick check failed"
        };
    }

}
