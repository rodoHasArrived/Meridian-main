using FluentAssertions;
using Meridian.Application.Commands;
using Meridian.Application.Services;
using Serilog.Core;
using Xunit;

namespace Meridian.Tests.Application.Commands;

public sealed class ConfigCommandsTests
{
    [Theory]
    [InlineData("--quickstart")]
    [InlineData("--setup")]
    [InlineData("--first-run")]
    public void CanHandle_QuickstartAliases_ReturnsTrue(string flag)
    {
        var cmd = new ConfigCommands(new ConfigurationService(log: Logger.None), Logger.None);

        cmd.CanHandle(new[] { flag }).Should().BeTrue();
    }
}
