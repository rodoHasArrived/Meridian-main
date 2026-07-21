using FluentAssertions;
using Meridian.Storage.Operations;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Testing;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class DemoTenantProvisionerTests
{
    [Fact]
    public async Task ProvisionAsync_SeedsBreaksAndStrategyRunIntoRealStores()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(ProvisionAsync_SeedsBreaksAndStrategyRunIntoRealStores));
        var breaks = CreateBreaks(artifacts);
        var strategyStore = CreateStrategyStore(artifacts);
        var provisioner = new DemoTenantProvisioner(breaks, strategyStore, NullLogger<DemoTenantProvisioner>.Instance);

        var report = await provisioner.ProvisionAsync();

        report.ReconciliationBreaksSeeded.Should().Be(DemoTenantBlueprint.BreakDefinitions.Count);
        report.StrategyRunSeeded.Should().BeTrue();
        report.Warnings.Should().BeEmpty();

        var seededBreaks = await breaks.GetAllAsync();
        seededBreaks.Should().HaveCount(DemoTenantBlueprint.BreakDefinitions.Count);
        seededBreaks.Select(item => item.BreakId).Should().BeEquivalentTo(
            DemoTenantBlueprint.BreakDefinitions.Select(definition => definition.Id));

        var run = await strategyStore.GetRunByIdAsync(DemoTenantBlueprint.StrategyRunId);
        run.Should().NotBeNull();
        run!.StrategyName.Should().Be(DemoTenantBlueprint.StrategyName);
        run.EndedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ProvisionAsync_IsIdempotent()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(ProvisionAsync_IsIdempotent));
        var breaks = CreateBreaks(artifacts);
        var strategyStore = CreateStrategyStore(artifacts);
        var provisioner = new DemoTenantProvisioner(breaks, strategyStore, NullLogger<DemoTenantProvisioner>.Instance);

        await provisioner.ProvisionAsync();
        var second = await provisioner.ProvisionAsync();

        second.ReconciliationBreaksSeeded.Should().Be(0);
        second.StrategyRunSeeded.Should().BeFalse();
        (await breaks.GetAllAsync()).Should().HaveCount(DemoTenantBlueprint.BreakDefinitions.Count);
    }

    [Fact]
    public async Task ProvisionAsync_WithoutStores_ReturnsEmptyReportWithoutThrowing()
    {
        var provisioner = new DemoTenantProvisioner();

        var report = await provisioner.ProvisionAsync();

        report.ReconciliationBreaksSeeded.Should().Be(0);
        report.StrategyRunSeeded.Should().BeFalse();
        report.Warnings.Should().BeEmpty();
    }

    private static FileReconciliationBreakQueueRepository CreateBreaks(TestArtifactDirectory artifacts) =>
        new(Path.Combine(artifacts.RootPath, "workstation"),
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);

    private static StrategyRunStore CreateStrategyStore(TestArtifactDirectory artifacts) =>
        new(new FileOperationalCaseHistoryStore(Path.Combine(artifacts.RootPath, "ops")));
}
