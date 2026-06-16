using FluentAssertions;
using Meridian.Application.Commands;
using Meridian.Application.Services;
using Meridian.Core.Config;
using Meridian.Platform.Results;
using Serilog;
using Xunit;

namespace Meridian.Tests.Application.Commands;

/// <summary>
/// Tests for diagnostics CLI routing.
/// </summary>
public sealed class DiagnosticsCommandsTests
{
    private static readonly ILogger Logger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.Console()
        .CreateLogger();

    [Fact]
    public void CanHandle_WithDiagnosticsFlag_ReturnsTrue()
    {
        var cmd = CreateCommand();

        cmd.CanHandle(new[] { "--diagnostics" }).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithDiagnosticsFlag_RunsKnownDiagnosticsPath()
    {
        var cmd = CreateCommand();

        var result = await cmd.ExecuteAsync(new[] { "--diagnostics" });

        result.Error.Should().NotBe(ErrorCode.Unknown);
    }

    private static DiagnosticsCommands CreateCommand()
    {
        var config = new AppConfig
        {
            DataRoot = "test-data"
        };

        return new DiagnosticsCommands(
            config,
            "config/appsettings.json",
            new ConfigurationService(Logger),
            Logger);
    }
}
