using FluentAssertions;
using Meridian.Application.Commands;
using Meridian.Contracts.Etl;
using Xunit;

namespace Meridian.Tests.Application.Commands;

public sealed class EtlCommandsTests
{
    [Theory]
    [InlineData("--etl-import")]
    [InlineData("--etl-export")]
    [InlineData("--etl-roundtrip")]
    [InlineData("--etl-resume")]
    public void CanHandle_WithEtlFlags_ReturnsTrue(string flag)
    {
        var command = new EtlCommands("config/appsettings.json", Serilog.Log.Logger);

        command.CanHandle([flag]).Should().BeTrue();
    }

    [Fact]
    public void TryBuildDefinition_WithoutSourceKind_ReturnsFalse()
    {
        var result = EtlCommands.TryBuildDefinition(
            ["--etl-import", "--etl-source-path", "input"],
            out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void TryBuildDefinition_WithoutSourcePath_ReturnsFalse()
    {
        var result = EtlCommands.TryBuildDefinition(
            ["--etl-import", "--etl-source-kind", "local"],
            out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void TryBuildDefinition_WithExportArgs_BuildsTypedDefinition()
    {
        var result = EtlCommands.TryBuildDefinition(
            [
                "--etl-export",
                "--etl-source-kind",
                "local",
                "--etl-source-path",
                "input",
                "--etl-destination-kind",
                "local",
                "--etl-destination-path",
                "output",
                "--etl-symbols",
                "AAPL,MSFT",
                "--etl-publish-normalized",
            ],
            out var definition);

        result.Should().BeTrue();
        definition.FlowDirection.Should().Be(EtlFlowDirection.Export);
        definition.Source.Kind.Should().Be(EtlSourceKind.Local);
        definition.Source.Location.Should().Be("input");
        definition.Destination.Kind.Should().Be(EtlDestinationKind.Local);
        definition.Destination.Location.Should().Be("output");
        definition.Symbols.Should().Equal("AAPL", "MSFT");
        definition.PublishNormalizedExtract.Should().BeTrue();
    }
}
