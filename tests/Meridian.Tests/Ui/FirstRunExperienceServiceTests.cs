using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Core.Config;
using Meridian.Storage.Operations;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Services;
using Meridian.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class FirstRunExperienceServiceTests
{
    [Fact]
    public async Task CompleteAsync_SampleMode_ProvisionsGovernedPackAndOutcomeEvidence()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(CompleteAsync_SampleMode_ProvisionsGovernedPackAndOutcomeEvidence));
        var configPath = Path.Combine(artifacts.RootPath, "appsettings.json");
        var config = new ConfigStore(configPath);
        await config.SaveAsync(new AppConfig(DataRoot: artifacts.RootPath));
        var service = new FirstRunExperienceService(config);

        var result = await service.CompleteAsync("local-admin", new CompleteFirstRunRequestDto(
            "monitor-investments", "personal-portfolio", "sample", true));

        result.IsComplete.Should().BeTrue();
        result.Workspace.IsSample.Should().BeTrue();
        result.Workspace.Badge.Should().Contain("SAMPLE");
        result.Outcomes.Single(item => item.Key == "workspace-opened").IsComplete.Should().BeTrue();
        result.Outcomes.Single(item => item.Key == "data-imported").IsComplete.Should().BeTrue();
        File.Exists(Path.Combine(artifacts.RootPath, "workspaces", "local-admin", "sample-pack.json")).Should().BeTrue();
    }

    [Fact]
    public async Task GetAsync_SampleMode_ExposesPopulatedSampleWorkspace()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(GetAsync_SampleMode_ExposesPopulatedSampleWorkspace));
        var config = new ConfigStore(Path.Combine(artifacts.RootPath, "appsettings.json"));
        await config.SaveAsync(new AppConfig(DataRoot: artifacts.RootPath));
        var service = new FirstRunExperienceService(config);

        await service.CompleteAsync("local-admin", new CompleteFirstRunRequestDto(
            "monitor-investments", "personal-portfolio", "sample", true));
        var status = await service.GetAsync("local-admin");

        status.SampleWorkspace.Should().NotBeNull();
        status.SampleWorkspace!.Holdings.Should().NotBeEmpty();
        status.SampleWorkspace.ReconciliationBreaks.Should().HaveCount(DemoTenantBlueprint.BreakDefinitions.Count);
        status.SampleWorkspace.PortfolioValue.Should().BeGreaterThan(DemoTenantBlueprint.Cash);
        status.SampleWorkspace.Highlights.Should().NotBeEmpty();
        // Highlights may advertise only desks that are genuinely seeded, so a click-through never
        // contradicts onboarding. The illustrative sample portfolio is not advertised as a loaded desk.
        status.SampleWorkspace.Highlights.Select(highlight => highlight.Route).Should()
            .OnlyContain(route => route == DemoTenantBlueprint.ReconciliationRoute
                || route == DemoTenantBlueprint.StrategyRoute);
    }

    [Fact]
    public async Task GetAsync_NonSampleMode_OmitsSampleWorkspace()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(GetAsync_NonSampleMode_OmitsSampleWorkspace));
        var config = new ConfigStore(Path.Combine(artifacts.RootPath, "appsettings.json"));
        await config.SaveAsync(new AppConfig(DataRoot: artifacts.RootPath));
        var service = new FirstRunExperienceService(config);

        await service.CompleteAsync("local-admin", new CompleteFirstRunRequestDto(
            "reconcile-accounts", "accounting-reconciliation", "skip", false));
        var status = await service.GetAsync("local-admin");

        status.Workspace.IsSample.Should().BeFalse();
        status.SampleWorkspace.Should().BeNull();
    }

    [Fact]
    public async Task CompleteAsync_SampleMode_WithProvisioner_SeedsRealDeskStores()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(CompleteAsync_SampleMode_WithProvisioner_SeedsRealDeskStores));
        var config = new ConfigStore(Path.Combine(artifacts.RootPath, "appsettings.json"));
        await config.SaveAsync(new AppConfig(DataRoot: artifacts.RootPath));

        var breaks = new FileReconciliationBreakQueueRepository(
            Path.Combine(artifacts.RootPath, "workstation"),
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var strategyStore = new StrategyRunStore(new FileOperationalCaseHistoryStore(Path.Combine(artifacts.RootPath, "ops")));
        var provisioner = new DemoTenantProvisioner(breaks, strategyStore, NullLogger<DemoTenantProvisioner>.Instance);
        var service = new FirstRunExperienceService(config, provisioner);

        await service.CompleteAsync("local-admin", new CompleteFirstRunRequestDto(
            "monitor-investments", "personal-portfolio", "sample", true));

        (await breaks.GetAllAsync()).Should().HaveCount(DemoTenantBlueprint.BreakDefinitions.Count);
        (await strategyStore.GetRunByIdAsync(DemoTenantBlueprint.StrategyRunId)).Should().NotBeNull();
    }

    [Fact]
    public async Task CompleteAsync_NonSampleMode_DoesNotProvisionDeskStores()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(CompleteAsync_NonSampleMode_DoesNotProvisionDeskStores));
        var config = new ConfigStore(Path.Combine(artifacts.RootPath, "appsettings.json"));
        await config.SaveAsync(new AppConfig(DataRoot: artifacts.RootPath));

        var breaks = new FileReconciliationBreakQueueRepository(
            Path.Combine(artifacts.RootPath, "workstation"),
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var strategyStore = new StrategyRunStore(new FileOperationalCaseHistoryStore(Path.Combine(artifacts.RootPath, "ops")));
        var provisioner = new DemoTenantProvisioner(breaks, strategyStore, NullLogger<DemoTenantProvisioner>.Instance);
        var service = new FirstRunExperienceService(config, provisioner);

        await service.CompleteAsync("local-admin", new CompleteFirstRunRequestDto(
            "reconcile-accounts", "accounting-reconciliation", "skip", false));

        (await breaks.GetAllAsync()).Should().BeEmpty();
        (await strategyStore.GetRunByIdAsync(DemoTenantBlueprint.StrategyRunId)).Should().BeNull();
    }

    [Fact]
    public async Task CompleteOutcomeAsync_RejectsUnknownOutcome()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(CompleteOutcomeAsync_RejectsUnknownOutcome));
        var config = new ConfigStore(Path.Combine(artifacts.RootPath, "appsettings.json"));
        await config.SaveAsync(new AppConfig(DataRoot: artifacts.RootPath));
        var service = new FirstRunExperienceService(config);

        var act = () => service.CompleteOutcomeAsync("operator", "visited-a-page");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Unknown activation outcome*");
    }
}
