using FluentAssertions;
using Meridian.Storage;

namespace Meridian.Tests.Storage;

public sealed class StorageProfilePresetsTests
{
    [Fact]
    public void GetPresets_UsesCanonicalStrategyLabelForRetainedResearchProfile()
    {
        var preset = StorageProfilePresets.GetPresets()
            .Should()
            .ContainSingle(candidate => candidate.Id == StorageProfilePresets.DefaultProfile)
            .Subject;

        preset.Id.Should().Be("Research");
        preset.Label.Should().Be("Strategy");
        preset.Label.Should().NotContain("Research");
        preset.Description.Should().Contain("strategy analysis");
    }
}
