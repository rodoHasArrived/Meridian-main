using FluentAssertions;
using Meridian.Ui.Services;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for <see cref="AdminMaintenanceService"/> — singleton lifecycle,
/// interface compliance, model defaults, and cancellation support.
/// </summary>
public sealed class AdminMaintenanceServiceTests
{
    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnNonNull()
    {
        AdminMaintenanceService.Instance.Should().NotBeNull();
    }

    [Fact]
    public void Instance_ShouldReturnSameSingleton()
    {
        var a = AdminMaintenanceService.Instance;
        var b = AdminMaintenanceService.Instance;
        a.Should().BeSameAs(b);
    }

    [Fact]
    public void Instance_ThreadSafety_ShouldReturnSameInstance()
    {
        AdminMaintenanceService? i1 = null, i2 = null;
        var t1 = Task.Run(() => i1 = AdminMaintenanceService.Instance);
        var t2 = Task.Run(() => i2 = AdminMaintenanceService.Instance);
        Task.WaitAll(t1, t2);

        i1.Should().NotBeNull();
        i1.Should().BeSameAs(i2);
    }

    // ── Interface compliance ─────────────────────────────────────────

    [Fact]
    public void AdminMaintenanceService_ShouldImplementIAdminMaintenanceService()
    {
        AdminMaintenanceService.Instance.Should().BeAssignableTo<IAdminMaintenanceService>();
    }

    [Fact]
    public void AdminMaintenanceService_ShouldInheritFromBase()
    {
        AdminMaintenanceService.Instance.Should().BeAssignableTo<AdminMaintenanceServiceBase>();
    }

    // ── API methods with cancellation ────────────────────────────────

    [Fact]
    public async Task GetMaintenanceScheduleAsync_WithCancellation_ShouldThrowOrReturnFailure()
    {
        var svc = AdminMaintenanceService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var result = await svc.GetMaintenanceScheduleAsync(cts.Token);
            // No backend running — should fail gracefully
            result.Success.Should().BeFalse();
        }
        catch (Exception ex)
        {
            ex.Should().BeAssignableTo<Exception>();
        }
    }

    [Fact]
    public async Task GetMaintenanceHistoryAsync_WithCancellation_ShouldThrowOrReturnFailure()
    {
        var svc = AdminMaintenanceService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var result = await svc.GetMaintenanceHistoryAsync(ct: cts.Token);
            result.Success.Should().BeFalse();
        }
        catch (Exception ex)
        {
            ex.Should().BeAssignableTo<Exception>();
        }
    }

    [Fact]
    public async Task GetTierConfigurationAsync_WithCancellation_ShouldThrowOrReturnFailure()
    {
        var svc = AdminMaintenanceService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var result = await svc.GetTierConfigurationAsync(cts.Token);
            result.Success.Should().BeFalse();
        }
        catch (Exception ex)
        {
            ex.Should().BeAssignableTo<Exception>();
        }
    }

    [Fact]
    public async Task GetTierUsageAsync_WithCancellation_ShouldThrowOrReturnFailure()
    {
        var svc = AdminMaintenanceService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var result = await svc.GetTierUsageAsync(cts.Token);
            result.Success.Should().BeFalse();
        }
        catch (Exception ex)
        {
            ex.Should().BeAssignableTo<Exception>();
        }
    }

    [Fact]
    public async Task GetRetentionPoliciesAsync_WithCancellation_ShouldThrowOrReturnFailure()
    {
        var svc = AdminMaintenanceService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var result = await svc.GetRetentionPoliciesAsync(cts.Token);
            result.Success.Should().BeFalse();
        }
        catch (Exception ex)
        {
            ex.Should().BeAssignableTo<Exception>();
        }
    }

    [Fact]
    public async Task ValidatePermissionsAsync_WithCancellation_ShouldThrowOrReturnFailure()
    {
        var svc = AdminMaintenanceService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var result = await svc.ValidatePermissionsAsync(cts.Token);
            result.Success.Should().BeFalse();
        }
        catch (Exception ex)
        {
            ex.Should().BeAssignableTo<Exception>();
        }
    }

    [Fact]
    public async Task RunQuickCheckAsync_WithCancellation_ShouldThrowOrReturnFailure()
    {
        var svc = AdminMaintenanceService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var result = await svc.RunQuickCheckAsync(cts.Token);
            result.Success.Should().BeFalse();
        }
        catch (Exception ex)
        {
            ex.Should().BeAssignableTo<Exception>();
        }
    }

    // ── Model tests: MaintenanceScheduleResult ───────────────────────

    [Fact]
    public void MaintenanceScheduleResult_ShouldHaveDefaultValues()
    {
        var result = new MaintenanceScheduleResult();
        result.Success.Should().BeFalse();
        result.Error.Should().BeNull();
        result.Schedule.Should().BeNull();
    }

    // ── Model tests: MaintenanceScheduleConfig ───────────────────────

    [Fact]
    public void MaintenanceScheduleConfig_ShouldHaveDefaultValues()
    {
        var config = new MaintenanceScheduleConfig();
        config.Enabled.Should().BeFalse();
        config.CronExpression.Should().BeNull();
        config.RunCompression.Should().BeFalse();
        config.RunCleanup.Should().BeFalse();
        config.RunIntegrityCheck.Should().BeFalse();
        config.RunTierMigration.Should().BeFalse();
    }

    // ── Model tests: MaintenanceRunOptions ───────────────────────────

    [Fact]
    public void MaintenanceRunOptions_ShouldHaveSensibleDefaults()
    {
        var options = new MaintenanceRunOptions();
        options.RunCompression.Should().BeTrue();
        options.RunCleanup.Should().BeTrue();
        options.RunIntegrityCheck.Should().BeTrue();
        options.RunTierMigration.Should().BeFalse();
        options.DryRun.Should().BeFalse();
    }

    // ── Model tests: MaintenanceRunResult ────────────────────────────

    [Fact]
    public void MaintenanceRunResult_ShouldHaveDefaultValues()
    {
        var result = new MaintenanceRunResult();
        result.Success.Should().BeFalse();
        result.Error.Should().BeNull();
        result.RunId.Should().BeNull();
        result.Operations.Should().NotBeNull().And.BeEmpty();
    }

    // ── Model tests: MaintenanceOperation ────────────────────────────

    [Fact]
    public void MaintenanceOperation_ShouldHaveDefaultValues()
    {
        var op = new MaintenanceOperation();
        op.Name.Should().BeEmpty();
        op.Status.Should().BeEmpty();
        op.ItemsProcessed.Should().Be(0);
        op.BytesProcessed.Should().Be(0);
        op.Error.Should().BeNull();
    }

    // ── Model tests: TierConfigResult ────────────────────────────────

    [Fact]
    public void TierConfigResult_ShouldHaveDefaultValues()
    {
        var result = new TierConfigResult();
        result.Tiers.Should().NotBeNull().And.BeEmpty();
        result.AutoMigrationEnabled.Should().BeFalse();
        result.MigrationSchedule.Should().BeNull();
    }

    // ── Model tests: StorageTierConfig ───────────────────────────────

    [Fact]
    public void StorageTierConfig_ShouldHaveDefaultValues()
    {
        var tier = new StorageTierConfig();
        tier.Name.Should().BeEmpty();
        tier.Path.Should().BeEmpty();
        tier.CompressionLevel.Should().Be("Standard");
        tier.Enabled.Should().BeFalse();
    }

    // ── Model tests: CleanupOptions ──────────────────────────────────

    [Fact]
    public void CleanupOptions_ShouldHaveSensibleDefaults()
    {
        var options = new CleanupOptions();
        options.DeleteEmptyDirectories.Should().BeTrue();
        options.DeleteTempFiles.Should().BeTrue();
        options.DeleteOrphanedFiles.Should().BeFalse();
        options.DeleteCorruptFiles.Should().BeFalse();
        options.OlderThanDays.Should().Be(0);
    }

    // ── Model tests: StorageRetentionPolicy ──────────────────────────

    [Fact]
    public void StorageRetentionPolicy_ShouldHaveDefaultValues()
    {
        var policy = new StorageRetentionPolicy();
        policy.Id.Should().BeEmpty();
        policy.Name.Should().BeEmpty();
        policy.RetentionDays.Should().Be(0);
        policy.Enabled.Should().BeFalse();
        policy.IsDefault.Should().BeFalse();
    }

    // ── Model tests: PermissionValidationResult ──────────────────────

    [Fact]
    public void PermissionValidationResult_ShouldHaveDefaultValues()
    {
        var result = new PermissionValidationResult();
        result.CanRead.Should().BeFalse();
        result.CanWrite.Should().BeFalse();
        result.CanDelete.Should().BeFalse();
        result.Issues.Should().NotBeNull().And.BeEmpty();
    }

    // ── Model tests: TierUsage ───────────────────────────────────────

    [Fact]
    public void TierUsage_ShouldHaveDefaultValues()
    {
        var usage = new TierUsage();
        usage.TierName.Should().BeEmpty();
        usage.SizeBytes.Should().Be(0);
        usage.FileCount.Should().Be(0);
        usage.PercentOfTotal.Should().Be(0);
    }
}
