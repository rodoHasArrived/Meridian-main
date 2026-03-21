using FluentAssertions;
using Meridian.Application.Commands;
using Serilog;
using Xunit;

namespace Meridian.Tests.Application.Commands;

/// <summary>
/// Tests for the SelfTestCommand CLI handler.
/// </summary>
public sealed class SelfTestCommandTests
{
    private static readonly ILogger Logger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.Console()
        .CreateLogger();

    [Fact]
    public void CanHandle_WithSelfTestFlag_ReturnsTrue()
    {
        var cmd = new SelfTestCommand(Logger);
        cmd.CanHandle(new[] { "--selftest" }).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_CaseInsensitive_ReturnsTrue()
    {
        var cmd = new SelfTestCommand(Logger);
        cmd.CanHandle(new[] { "--SELFTEST" }).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WithOtherFlags_ReturnsFalse()
    {
        var cmd = new SelfTestCommand(Logger);
        cmd.CanHandle(new[] { "--help" }).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_EmptyArgs_ReturnsFalse()
    {
        var cmd = new SelfTestCommand(Logger);
        cmd.CanHandle(Array.Empty<string>()).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_RunsSelfTests_ReturnsZero()
    {
        var cmd = new SelfTestCommand(Logger);
        var result = await cmd.ExecuteAsync(new[] { "--selftest" });
        result.ExitCode.Should().Be(0);
    }
}
