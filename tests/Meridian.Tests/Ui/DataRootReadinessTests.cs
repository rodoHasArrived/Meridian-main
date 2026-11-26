using FluentAssertions;
using Meridian.Contracts.Lifecycle;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// The data-root readiness check: a missing root is Failing (the host is NotReady), a nearly full
/// volume is Degraded (the host keeps accepting work but readiness shows the condition), and an
/// unmeasurable volume passes on the directory alone as it always did.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DataRootReadinessTests
{
    private const long Gib = 1024L * 1024 * 1024;

    [Fact]
    public void AMissingDataRoot_IsFailing()
    {
        var result = UiServer.EvaluateDataRootReadiness(
            "/data", _ => false, _ => 100 * Gib, UiServer.DataRootCriticalFreeBytes);

        result.Status.Should().Be(LifecycleCheckStatus.Failing);
        result.Message.Should().Be("Data root is unavailable.");
    }

    [Fact]
    public void AVolumeBelowTheCriticalThreshold_IsDegraded_NotFailing()
    {
        var result = UiServer.EvaluateDataRootReadiness(
            "/data", _ => true, _ => (long)(1.5 * Gib), UiServer.DataRootCriticalFreeBytes);

        result.Status.Should().Be(LifecycleCheckStatus.Degraded,
            "low space must be visible without taking the host out of service");
        result.Message.Should().Contain("1.5 GB free");
    }

    [Fact]
    public void AVolumeWithAmpleSpace_Passes_AndReportsTheFreeSpace()
    {
        var result = UiServer.EvaluateDataRootReadiness(
            "/data", _ => true, _ => 50 * Gib, UiServer.DataRootCriticalFreeBytes);

        result.Status.Should().Be(LifecycleCheckStatus.Passing);
        result.Message.Should().Be("Data root is available (50.0 GB free).");
    }

    [Fact]
    public void AnUnmeasurableVolume_PassesOnTheDirectoryAlone()
    {
        var result = UiServer.EvaluateDataRootReadiness(
            "/data", _ => true, _ => null, UiServer.DataRootCriticalFreeBytes);

        result.Status.Should().Be(LifecycleCheckStatus.Passing);
        result.Message.Should().Be("Data root is available.");
    }

    [Fact]
    public void TheCriticalThreshold_IsTwoGibibytes()
    {
        UiServer.DataRootCriticalFreeBytes.Should().Be(2 * Gib,
            "it matches SystemHealthConfig's critical disk threshold and the disk alerts");
    }
}
