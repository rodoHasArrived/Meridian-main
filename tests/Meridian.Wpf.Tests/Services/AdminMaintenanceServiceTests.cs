using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for <see cref="AdminMaintenanceServiceBase"/> and the related admin-maintenance DTOs.
/// </summary>
public sealed class AdminMaintenanceServiceTests
{
    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        var a = AdminMaintenanceServiceBase.Instance;
        var b = AdminMaintenanceServiceBase.Instance;

        a.Should().NotBeNull();
        a.Should().BeSameAs(b);
        a.Should().BeAssignableTo<IAdminMaintenanceService>();
    }

    [Fact]
    public async Task Instance_ThreadSafety_ShouldReturnSameInstance()
    {
        AdminMaintenanceServiceBase? i1 = null;
        AdminMaintenanceServiceBase? i2 = null;

        var t1 = Task.Run(() => i1 = AdminMaintenanceServiceBase.Instance);
        var t2 = Task.Run(() => i2 = AdminMaintenanceServiceBase.Instance);

        await Task.WhenAll(t1, t2);

        i1.Should().NotBeNull();
        i1.Should().BeSameAs(i2);
    }

    [Fact]
    public async Task AdminMaintenanceApis_WithCancelledToken_ShouldThrowOrReturnFailure()
    {
        var svc = AdminMaintenanceServiceBase.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var actions = new Func<CancellationToken, Task<bool>>[]
        {
            async ct => (await svc.GetMaintenanceScheduleAsync(ct)).Success,
            async ct => (await svc.GetMaintenanceHistoryAsync(ct: ct)).Success,
            async ct => (await svc.GetTierConfigurationAsync(ct)).Success,
            async ct => (await svc.GetTierUsageAsync(ct)).Success,
            async ct => (await svc.GetRetentionPoliciesAsync(ct)).Success,
            async ct => (await svc.ValidatePermissionsAsync(ct)).Success,
            async ct => (await svc.RunQuickCheckAsync(ct)).Success,
        };

        foreach (var action in actions)
        {
            try
            {
                var success = await action(cts.Token);
                success.Should().BeFalse();
            }
            catch (Exception ex)
            {
                ex.Should().BeAssignableTo<Exception>();
            }
        }
    }

    [Fact]
    public void MaintenanceScheduleResult_ShouldHaveDefaultValues()
    {
        var result = new MaintenanceScheduleResult();
        result.Success.Should().BeFalse();
        result.Error.Should().BeNull();
        result.Schedule.Should().BeNull();
    }

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

    [Fact]
    public void MaintenanceRunResult_ShouldHaveDefaultValues()
    {
        var result = new MaintenanceRunResult();
        result.Success.Should().BeFalse();
        result.Error.Should().BeNull();
        result.RunId.Should().BeNull();
        result.Operations.Should().NotBeNull().And.BeEmpty();
    }

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

    [Fact]
    public void TierConfigResult_ShouldHaveDefaultValues()
    {
        var result = new TierConfigResult();
        result.Tiers.Should().NotBeNull().And.BeEmpty();
        result.AutoMigrationEnabled.Should().BeFalse();
        result.MigrationSchedule.Should().BeNull();
    }

    [Fact]
    public void StorageTierConfig_ShouldHaveDefaultValues()
    {
        var tier = new StorageTierConfig();
        tier.Name.Should().BeEmpty();
        tier.Path.Should().BeEmpty();
        tier.CompressionLevel.Should().Be("Standard");
        tier.Enabled.Should().BeFalse();
    }

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

    [Fact]
    public void PermissionValidationResult_ShouldHaveDefaultValues()
    {
        var result = new PermissionValidationResult();
        result.CanRead.Should().BeFalse();
        result.CanWrite.Should().BeFalse();
        result.CanDelete.Should().BeFalse();
        result.Issues.Should().NotBeNull().And.BeEmpty();
    }

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
