using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for FirstRunService singleton service.
/// Validates singleton access, path configuration, and first-run detection.
/// </summary>
public sealed class FirstRunServiceTests
{
    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        // Arrange & Act
        var instance1 = FirstRunService.Instance;
        var instance2 = FirstRunService.Instance;

        // Assert
        instance1.Should().NotBeNull();
        instance2.Should().NotBeNull();
        instance1.Should().BeSameAs(instance2, "FirstRunService should be a singleton");
    }

    [Fact]
    public void AppDataPath_ShouldNotBeEmpty()
    {
        // Arrange
        var service = FirstRunService.Instance;

        // Act
        var path = service.AppDataPath;

        // Assert
        path.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AppDataPath_ShouldContainMeridian()
    {
        // Arrange
        var service = FirstRunService.Instance;

        // Act
        var path = service.AppDataPath;

        // Assert
        path.Should().Contain("Meridian");
    }

    [Fact]
    public void ConfigFilePath_ShouldNotBeEmpty()
    {
        // Arrange
        var service = FirstRunService.Instance;

        // Act
        var path = service.ConfigFilePath;

        // Assert
        path.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ConfigFilePath_ShouldContainAppSettings()
    {
        // Arrange
        var service = FirstRunService.Instance;

        // Act
        var path = service.ConfigFilePath;

        // Assert
        path.Should().Contain("appsettings.json");
    }

    [Fact]
    public void FirstRunMarkerPath_ShouldNotBeEmpty()
    {
        // Arrange
        var service = FirstRunService.Instance;

        // Act
        var path = service.FirstRunMarkerPath;

        // Assert
        path.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void FirstRunMarkerPath_ShouldContainInitializedMarker()
    {
        // Arrange
        var service = FirstRunService.Instance;

        // Act
        var path = service.FirstRunMarkerPath;

        // Assert
        path.Should().Contain(".initialized");
    }

    [Fact]
    public void FirstRunMarkerPath_ShouldBeUnderAppDataPath()
    {
        // Arrange
        var service = FirstRunService.Instance;

        // Act & Assert
        service.FirstRunMarkerPath.Should().StartWith(service.AppDataPath);
    }

    [Fact]
    public void ConfigFilePath_ShouldBeUnderAppDataPath()
    {
        // Arrange
        var service = FirstRunService.Instance;

        // Act & Assert
        service.ConfigFilePath.Should().StartWith(service.AppDataPath);
    }

    [Fact]
    public void InitializedEvent_Subscription_ShouldNotThrow()
    {
        // Arrange
        var service = FirstRunService.Instance;

        // Act
        var act = () => service.Initialized += (sender, args) => { };

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void FirstRunInitializedEventArgs_ShouldSetProperties()
    {
        // Arrange & Act
        var args = new FirstRunInitializedEventArgs(true);

        // Assert
        args.WasFirstRun.Should().BeTrue();
        args.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void FirstRunInitializedEventArgs_WhenNotFirstRun_ShouldBeFalse()
    {
        // Arrange & Act
        var args = new FirstRunInitializedEventArgs(false);

        // Assert
        args.WasFirstRun.Should().BeFalse();
    }
}
