using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Core.Config;
using Meridian.Ui.Shared.Services;
using Meridian.Testing;
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
