using System.Text.Json;
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
    public async Task GetAsync_SampleMode_WhenProvisioningLoadsNothing_OmitsDeskHighlights()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(GetAsync_SampleMode_WhenProvisioningLoadsNothing_OmitsDeskHighlights));
        var config = new ConfigStore(Path.Combine(artifacts.RootPath, "appsettings.json"));
        await config.SaveAsync(new AppConfig(DataRoot: artifacts.RootPath));
        // A provisioner with no stores loads nothing into the desks, so the Ready screen must not
        // advertise loaded desks the user would then find empty.
        var service = new FirstRunExperienceService(config, new DemoTenantProvisioner());

        await service.CompleteAsync("local-admin", new CompleteFirstRunRequestDto(
            "monitor-investments", "personal-portfolio", "sample", true));
        var status = await service.GetAsync("local-admin");

        status.SampleWorkspace.Should().NotBeNull();
        status.SampleWorkspace!.Highlights.Should().BeEmpty();
        // The illustrative sample portfolio preview is still shown.
        status.SampleWorkspace.Holdings.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CompleteAsync_SampleMode_WhenProvisioningLoadsNothing_PersistsNoDeskHighlights()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(CompleteAsync_SampleMode_WhenProvisioningLoadsNothing_PersistsNoDeskHighlights));
        var config = new ConfigStore(Path.Combine(artifacts.RootPath, "appsettings.json"));
        await config.SaveAsync(new AppConfig(DataRoot: artifacts.RootPath));
        // During a demo-store outage, the durable sample artifact must agree with the Ready screen
        // and must not advertise desks that remain empty.
        var service = new FirstRunExperienceService(config, new DemoTenantProvisioner());

        await service.CompleteAsync("local-admin", new CompleteFirstRunRequestDto(
            "monitor-investments", "personal-portfolio", "sample", true));

        var packPath = Path.Combine(artifacts.RootPath, "workspaces", "local-admin", "sample-pack.json");
        using var pack = JsonDocument.Parse(await File.ReadAllTextAsync(packPath));

        pack.RootElement.GetProperty("workspace").GetProperty("highlights").GetArrayLength().Should().Be(0);
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
    public async Task GetAsync_ProviderDataChoice_LeadsWithProviderSetup()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(GetAsync_ProviderDataChoice_LeadsWithProviderSetup));
        var config = new ConfigStore(Path.Combine(artifacts.RootPath, "appsettings.json"));
        await config.SaveAsync(new AppConfig(DataRoot: artifacts.RootPath));
        var service = new FirstRunExperienceService(config);

        // Setup tells a "Connect a provider" user that Meridian guides the next step, so the Ready
        // screen must lead with provider setup rather than with a desk that has no data yet.
        await service.CompleteAsync("local-admin", new CompleteFirstRunRequestDto(
            "collect-market-data", "market-data", "provider", false));
        var status = await service.GetAsync("local-admin");

        status.DataChoice.Should().Be("provider");
        status.RecommendedActions[0].Route.Should().Be("/settings/providers");
        status.RecommendedActions.Should().Contain(action => action.Route == "/data");
    }

    [Fact]
    public async Task GetAsync_UploadDataChoice_LeadsWithStatementImport()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(GetAsync_UploadDataChoice_LeadsWithStatementImport));
        var config = new ConfigStore(Path.Combine(artifacts.RootPath, "appsettings.json"));
        await config.SaveAsync(new AppConfig(DataRoot: artifacts.RootPath));
        var service = new FirstRunExperienceService(config);

        await service.CompleteAsync("local-admin", new CompleteFirstRunRequestDto(
            "monitor-investments", "personal-portfolio", "upload", false));
        var status = await service.GetAsync("local-admin");

        status.DataChoice.Should().Be("upload");
        status.RecommendedActions[0].Route.Should().Be("/accounting/statement-import");
        status.RecommendedActions[0].Label.Should().Contain("Import");
    }

    [Fact]
    public async Task GetAsync_SkipDataChoice_LeadsWithTheChosenDesk()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(GetAsync_SkipDataChoice_LeadsWithTheChosenDesk));
        var config = new ConfigStore(Path.Combine(artifacts.RootPath, "appsettings.json"));
        await config.SaveAsync(new AppConfig(DataRoot: artifacts.RootPath));
        var service = new FirstRunExperienceService(config);

        await service.CompleteAsync("local-admin", new CompleteFirstRunRequestDto(
            "research-strategies", "strategy-research", "skip", false));
        var status = await service.GetAsync("local-admin");

        status.RecommendedActions[0].Route.Should().Be("/strategy");
        status.RecommendedActions.Should().Contain(action => action.Route == "/accounting/statement-import");
    }

    [Fact]
    public async Task GetAsync_SampleDataChoice_KeepsDeskFirstAndPointsAtTheSampleStatement()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(GetAsync_SampleDataChoice_KeepsDeskFirstAndPointsAtTheSampleStatement));
        var config = new ConfigStore(Path.Combine(artifacts.RootPath, "appsettings.json"));
        await config.SaveAsync(new AppConfig(DataRoot: artifacts.RootPath));
        var service = new FirstRunExperienceService(config);

        // Sample workspaces are already populated, so the desk stays the lead action.
        await service.CompleteAsync("local-admin", new CompleteFirstRunRequestDto(
            "monitor-investments", "personal-portfolio", "sample", true));
        var status = await service.GetAsync("local-admin");

        status.RecommendedActions[0].Route.Should().Be("/portfolio");
        status.RecommendedActions[1].Label.Should().Be("Review the sample statement");
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
