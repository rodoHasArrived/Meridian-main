using FluentAssertions;
using Meridian.Application.Commands;
using Meridian.Application.Config;
using Serilog;
using Xunit;

namespace Meridian.Tests.Application.Commands;

/// <summary>
/// Tests for the DryRunCommand CLI handler.
/// </summary>
public sealed class DryRunCommandTests
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
    public void CanHandle_WithDryRunFlag_ReturnsTrue()
    {
        var cmd = CreateCommand();
        cmd.CanHandle(new[] { "--dry-run" }).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_CaseInsensitive_ReturnsTrue()
    {
        var cmd = CreateCommand();
        cmd.CanHandle(new[] { "--DRY-RUN" }).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WithOtherFlags_ReturnsFalse()
    {
        var cmd = CreateCommand();
        cmd.CanHandle(new[] { "--help" }).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_EmptyArgs_ReturnsFalse()
    {
        var cmd = CreateCommand();
        cmd.CanHandle(Array.Empty<string>()).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_WithDryRunAndOffline_ReturnsTrue()
    {
        var cmd = CreateCommand();
        cmd.CanHandle(new[] { "--dry-run", "--offline" }).Should().BeTrue();
    }

    private static DryRunCommand CreateCommand()
    {
        var configService = new Meridian.Application.Services.ConfigurationService(Logger);
        return new DryRunCommand(TestConfig, configService, Logger);
    }
}
