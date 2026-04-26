using FluentAssertions;
using Meridian.Ui.Services.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="FixtureModeDetector"/> scenario-switching behaviour.
/// </summary>
public sealed class FixtureModeDetectorTests : IDisposable
{
    private readonly FixtureModeDetector _detector = FixtureModeDetector.Instance;

    // ── ScenarioLabel ────────────────────────────────────────────────────────

    [Fact]
    public void ScenarioLabel_ReturnsNonEmptyLabel()
    {
        // The label should always be a non-empty human-readable string.
        _detector.ScenarioLabel.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(FixtureScenario.Connected)]
    [InlineData(FixtureScenario.Disconnected)]
    [InlineData(FixtureScenario.Degraded)]
    [InlineData(FixtureScenario.Error)]
    [InlineData(FixtureScenario.Loading)]
    public void ScenarioLabel_ReflectsActiveScenarioFromFixtureDataService(FixtureScenario scenario)
    {
        // Arrange
        FixtureDataService.Instance.SetScenario(scenario);

        // Act
        var label = _detector.ScenarioLabel;

        // Assert
        label.Should().Be(FixtureDataService.GetScenarioLabel(scenario));
    }

    // ── ActiveScenario ───────────────────────────────────────────────────────

    [Fact]
    public void ActiveScenario_ReflectsFixtureDataServiceActiveScenario()
    {
        // Arrange
        FixtureDataService.Instance.SetScenario(FixtureScenario.Degraded);

        // Act
        var scenario = _detector.ActiveScenario;

        // Assert
        scenario.Should().Be(FixtureScenario.Degraded);
    }

    // ── CycleScenario ────────────────────────────────────────────────────────

    [Fact]
    public void CycleScenario_WhenFixtureModeOff_DoesNotChangeScenario()
    {
        // Arrange
        _detector.SetFixtureMode(false);
        FixtureDataService.Instance.SetScenario(FixtureScenario.Connected);

        // Act
        var returned = _detector.CycleScenario();

        // Assert – scenario should not have advanced
        returned.Should().Be(FixtureScenario.Connected,
            "CycleScenario is a no-op when fixture mode is off");
        FixtureDataService.Instance.ActiveScenario.Should().Be(FixtureScenario.Connected);
    }

    [Fact]
    public void CycleScenario_WhenFixtureModeOn_AdvancesScenario()
    {
        // Arrange
        _detector.SetFixtureMode(true);
        FixtureDataService.Instance.SetScenario(FixtureScenario.Connected);

        // Act
        var next = _detector.CycleScenario();

        // Assert
        next.Should().Be(FixtureScenario.Disconnected);
        _detector.ActiveScenario.Should().Be(FixtureScenario.Disconnected);
    }

    [Fact]
    public void CycleScenario_WhenFixtureModeOn_RaisesModeChangedEvent()
    {
        // Arrange
        _detector.SetFixtureMode(true);
        FixtureDataService.Instance.SetScenario(FixtureScenario.Connected);
        var eventRaised = false;
        _detector.ModeChanged += (_, _) => eventRaised = true;

        // Act
        _detector.CycleScenario();

        // Assert
        eventRaised.Should().BeTrue("ModeChanged should be raised so the banner refreshes");
    }

    [Fact]
    public void ModeKindAndLabel_ShouldDistinguishDemoDataFromOffline()
    {
        _detector.SetFixtureMode(true);
        _detector.UpdateBackendReachability(true);

        _detector.ModeKind.Should().Be(FixtureModeKind.Fixture);
        _detector.ModeLabel.Should().Contain("Demo data mode");
        _detector.BannerColor.Should().Be("#2563EB");

        _detector.SetFixtureMode(false);
        _detector.UpdateBackendReachability(false);

        _detector.ModeKind.Should().Be(FixtureModeKind.Offline);
        _detector.ModeLabel.Should().Contain("Offline");
        _detector.BannerColor.Should().Be("#F44336");
    }

    // ── Cleanup ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        // Reset shared singleton state so we don't affect other test classes.
        FixtureDataService.Instance.SetScenario(FixtureScenario.Connected);
        _detector.SetFixtureMode(false);
        _detector.UpdateBackendReachability(true);
    }
}
