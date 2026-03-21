using FluentAssertions;
using Meridian.Application.Commands;
using Serilog;
using Xunit;

namespace Meridian.Tests.Application.Commands;

/// <summary>
/// Tests for the ValidateConfigCommand CLI handler.
/// </summary>
public sealed class ValidateConfigCommandTests
{
    private static readonly ILogger Logger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.Console()
        .CreateLogger();

    [Fact]
    public void CanHandle_WithValidateConfigFlag_ReturnsTrue()
    {
        var cmd = CreateCommand();
        cmd.CanHandle(new[] { "--validate-config" }).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_CaseInsensitive_ReturnsTrue()
    {
        var cmd = CreateCommand();
        cmd.CanHandle(new[] { "--VALIDATE-CONFIG" }).Should().BeTrue();
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

    private static ValidateConfigCommand CreateCommand()
    {
        // ConfigurationService requires a logger; we just need CanHandle to work
        var configService = new Meridian.Application.Services.ConfigurationService(Logger);
        return new ValidateConfigCommand(configService, "appsettings.json", Logger);
    }
}
