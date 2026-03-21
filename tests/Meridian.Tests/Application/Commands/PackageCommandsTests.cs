using FluentAssertions;
using Meridian.Application.Commands;
using Meridian.Application.Config;
using Serilog;
using Xunit;

namespace Meridian.Tests.Application.Commands;

/// <summary>
/// Tests for the PackageCommands CLI command handler.
/// Validates argument parsing and routing logic.
/// </summary>
public class PackageCommandsTests
{
    private static readonly AppConfig TestConfig = new()
    {
        DataRoot = "test-data"
    };

    private static readonly ILogger Logger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.Console()
        .CreateLogger();

    [Fact]
    public void CanHandle_WithPackageFlag_ReturnsTrue()
    {
        var cmd = new PackageCommands(TestConfig, Logger);
        cmd.CanHandle(new[] { "--package" }).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WithImportPackageFlag_ReturnsTrue()
    {
        var cmd = new PackageCommands(TestConfig, Logger);
        cmd.CanHandle(new[] { "--import-package", "test.zip" }).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WithListPackageFlag_ReturnsTrue()
    {
        var cmd = new PackageCommands(TestConfig, Logger);
        cmd.CanHandle(new[] { "--list-package", "test.zip" }).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WithValidatePackageFlag_ReturnsTrue()
    {
        var cmd = new PackageCommands(TestConfig, Logger);
        cmd.CanHandle(new[] { "--validate-package", "test.zip" }).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WithNoPackageFlags_ReturnsFalse()
    {
        var cmd = new PackageCommands(TestConfig, Logger);
        cmd.CanHandle(new[] { "--help" }).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_CaseInsensitive_ReturnsTrue()
    {
        var cmd = new PackageCommands(TestConfig, Logger);
        cmd.CanHandle(new[] { "--PACKAGE" }).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ImportWithoutPath_ReturnsErrorCode()
    {
        var cmd = new PackageCommands(TestConfig, Logger);
        var result = await cmd.ExecuteAsync(new[] { "--import-package" });
        result.ExitCode.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_ListWithoutPath_ReturnsErrorCode()
    {
        var cmd = new PackageCommands(TestConfig, Logger);
        var result = await cmd.ExecuteAsync(new[] { "--list-package" });
        result.ExitCode.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_ValidateWithoutPath_ReturnsErrorCode()
    {
        var cmd = new PackageCommands(TestConfig, Logger);
        var result = await cmd.ExecuteAsync(new[] { "--validate-package" });
        result.ExitCode.Should().Be(2);
    }
}
