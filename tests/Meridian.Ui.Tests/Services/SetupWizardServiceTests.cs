using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

public sealed class SetupWizardServiceTests
{
    [Fact]
    public void GetSetupPresets_UsesStrategyLabelForRetainedResearcherPreset()
    {
        var service = new SetupWizardService();

        var preset = service.GetSetupPresets()
            .Should()
            .ContainSingle(candidate => candidate.Id == "researcher")
            .Subject;

        preset.Name.Should().Be("Strategy Analyst");
        preset.Name.Should().NotContain("Research");
        preset.Description.Should().Contain("strategy-data");
    }
}
