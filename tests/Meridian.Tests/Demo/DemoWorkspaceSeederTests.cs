using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Operations;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Testing;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Demo;

/// <summary>
/// Verifies that <c>--seed-demo</c> writes durable, Seeded-labelled records that survive a restart —
/// modelled as fresh store instances reading the same demo root, exactly as a new process would.
/// </summary>
public sealed class DemoWorkspaceSeederTests
{
    [Fact]
    public void WriteDemoConfig_WhenRemoteUrlEnvironmentVariableIsSet_UsesLoopbackBinding()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(WriteDemoConfig_WhenRemoteUrlEnvironmentVariableIsSet_UsesLoopbackBinding));
        var demoRoot = Path.Combine(artifacts.RootPath, "demo-workspace");
        Directory.CreateDirectory(demoRoot);

        var configPath = DemoWorkspaceCli.WriteDemoConfig(demoRoot);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_URLS"] = "http://0.0.0.0:8080",
            })
            .AddJsonFile(configPath)
            .Build();

        var options = ApiHostOptions.FromConfiguration(configuration, port: 8080);

        options.DeploymentMode.Should().Be(MeridianApiDeploymentMode.LocalWorkstation);
        options.Urls.Should().Equal("http://localhost:8080");
    }

    [Fact]
    public async Task SeedAsync_ProducesSeededProvenanceThatSurvivesRestart()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(SeedAsync_ProducesSeededProvenanceThatSurvivesRestart));
        var baseRoot = Path.Combine(artifacts.RootPath, "data");
        var seeder = new DemoWorkspaceSeeder(baseRoot);

        var report = await seeder.SeedAsync();

        report.Provenance.Should().Be(DemoTenantBlueprint.SeededProvenanceLabel);
        report.Provisioning.ReconciliationLoaded.Should().BeTrue();
        report.Provisioning.StrategyRunLoaded.Should().BeTrue();
        File.Exists(DemoWorkspaceLayout.ResolveSentinelPath(seeder.DemoRoot)).Should().BeTrue();

        // Simulate a process restart: brand-new store instances over the SAME durable demo root, with
        // no shared in-memory state. Reading the records back proves they were persisted, not cached.
        var breaks = new FileReconciliationBreakQueueRepository(
            Path.Combine(seeder.DemoRoot, "workstation"),
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var reloaded = await breaks.GetAllAsync(new ReconciliationBreakQueueScope(
            DemoTenantBlueprint.TenantId,
            DemoTenantBlueprint.CompanyId));

        reloaded.Should().HaveCount(DemoTenantBlueprint.BreakDefinitions.Count);
        reloaded.Should().OnlyContain(item =>
            item.SourceType == DemoTenantBlueprint.SeededSourceType &&
            item.SourceSystem == DemoTenantBlueprint.SeededSourceSystem &&
            item.TenantId == DemoTenantBlueprint.TenantId &&
            item.CompanyId == DemoTenantBlueprint.CompanyId);

        var strategy = new StrategyRunStore(new FileOperationalCaseHistoryStore(seeder.DemoRoot));
        var run = await strategy.GetRunByIdAsync(DemoTenantBlueprint.StrategyRunId);
        run.Should().NotBeNull();
        run!.StrategyName.Should().Be(DemoTenantBlueprint.StrategyName);
        run.EndedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(SeedAsync_IsIdempotent));
        var baseRoot = Path.Combine(artifacts.RootPath, "data");
        var seeder = new DemoWorkspaceSeeder(baseRoot);

        await seeder.SeedAsync();
        var second = await seeder.SeedAsync();

        // Re-running never duplicates casework, but both desks remain loaded.
        second.Provisioning.ReconciliationBreaksSeeded.Should().Be(0);
        second.Provisioning.ReconciliationLoaded.Should().BeTrue();
        second.Provisioning.StrategyRunLoaded.Should().BeTrue();

        var breaks = new FileReconciliationBreakQueueRepository(
            Path.Combine(seeder.DemoRoot, "workstation"),
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        (await breaks.GetAllAsync()).Should().HaveCount(DemoTenantBlueprint.BreakDefinitions.Count);
    }

}
