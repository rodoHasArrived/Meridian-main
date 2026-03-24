using Meridian.Contracts.Export;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for ExportPresetService singleton service.
/// Validates singleton access, inheritance, and preset management.
/// </summary>
public sealed class ExportPresetServiceTests
{
    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        // Arrange & Act
        var instance1 = ExportPresetService.Instance;
        var instance2 = ExportPresetService.Instance;

        // Assert
        instance1.Should().NotBeNull();
        instance2.Should().NotBeNull();
        instance1.Should().BeSameAs(instance2, "ExportPresetService should be a singleton");
    }

    [Fact]
    public void Instance_ShouldInheritFromExportPresetServiceBase()
    {
        // Arrange & Act
        var service = ExportPresetService.Instance;

        // Assert
        service.Should().BeAssignableTo<ExportPresetServiceBase>();
    }

    [Fact]
    public void Presets_ShouldNotBeNull()
    {
        // Arrange
        var service = ExportPresetService.Instance;

        // Act
        var presets = service.Presets;

        // Assert
        presets.Should().NotBeNull();
    }

    [Fact]
    public void Presets_ShouldBeReadOnlyList()
    {
        // Arrange
        var service = ExportPresetService.Instance;

        // Act
        var presets = service.Presets;

        // Assert
        presets.Should().BeAssignableTo<IReadOnlyList<ExportPreset>>();
    }

    [Fact]
    public void PresetsChanged_EventSubscription_ShouldNotThrow()
    {
        // Arrange
        var service = ExportPresetService.Instance;

        // Act
        var act = () => service.PresetsChanged += (sender, args) => { };

        // Assert
        act.Should().NotThrow();
    }
}
