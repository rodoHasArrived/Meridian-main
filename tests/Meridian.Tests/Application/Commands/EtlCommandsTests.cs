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
    [InlineData("--etl-preview")]
    [InlineData("--etl-list-files")]
    [InlineData("--etl-test-connection")]
    public void CanHandle_WithEtlFlags_ReturnsTrue(string flag)
    {
        var command = new EtlCommands("config/appsettings.json", Serilog.Log.Logger);

        command.CanHandle([flag]).Should().BeTrue();
    }

    [Fact]
    public void TryBuildDefinition_WithoutSourceKind_ReturnsFalse()
    {
        var result = CommandTestConsole.CaptureError(() =>
            EtlCommands.TryBuildDefinition(
                ["--etl-import", "--etl-source-path", "input"],
                out _));

        result.Should().BeFalse();
    }

    [Fact]
    public void TryBuildDefinition_WithoutSourcePath_ReturnsFalse()
    {
        var result = CommandTestConsole.CaptureError(() =>
            EtlCommands.TryBuildDefinition(
                ["--etl-import", "--etl-source-kind", "local"],
                out _));

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

    [Fact]
    public void TryBuildDefinition_WithoutFilePattern_LeavesSourcePatternUnset()
    {
        var result = EtlCommands.TryBuildDefinition(
            [
                "--etl-import",
                "--etl-source-kind",
                "local",
                "--etl-source-path",
                "input",
            ],
            out var definition);

        result.Should().BeTrue();
        definition.Source.FilePattern.Should().BeNull();
    }

    [Fact]
    public void TryBuildDefinition_WithSftpPreviewArgs_BuildsSourceSafetyOptions()
    {
        var result = EtlCommands.TryBuildDefinition(
            [
                "--etl-preview",
                "--etl-source-kind",
                "sftp",
                "--etl-source-path",
                "sftp://bank.example.com/inbound",
                "--etl-schema",
                "bank.statement.csv.v1",
                "--etl-source-username",
                "feed-user",
                "--etl-source-secret-ref",
                "env:BANK_PASSWORD",
                "--etl-source-host-key-sha256",
                "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF",
                "--etl-source-post-processing",
                "archive",
                "--etl-source-archive-path",
                "/archive"
            ],
            out var definition);

        result.Should().BeTrue();
        definition.PartnerSchemaId.Should().Be("bank.statement.csv.v1");
        definition.Source.Kind.Should().Be(EtlSourceKind.Sftp);
        definition.Source.PostProcessingAction.Should().Be(EtlSourcePostProcessingAction.MoveToArchive);
        definition.Source.ArchiveLocation.Should().Be("/archive");
        definition.Source.SecretRef.Should().Be("env:BANK_PASSWORD");
    }

    [Theory]
    [InlineData("--etl-list-files", EtlInspectionMode.ListFiles)]
    [InlineData("--etl-test-connection", EtlInspectionMode.TestConnection)]
    [InlineData("--etl-preview", EtlInspectionMode.Preview)]
    public void ResolveInspectionMode_SeparatesReadOnlyModesFromPreview(string flag, EtlInspectionMode expected)
    {
        EtlCommands.ResolveInspectionMode([flag]).Should().Be(expected);
    }

    [Fact]
    public void FormatListedFile_DescribesSourceFileWithoutPreviewFields()
    {
        var file = new EtlRemoteFile
        {
            Path = "/inbound/positions.csv",
            Name = "positions.csv",
            SizeBytes = 42,
            LastModifiedUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
        };

        EtlCommands.FormatListedFile(file).Should().Be(
            "positions.csv: Path=/inbound/positions.csv; Size=42; LastModified=2026-01-01T00:00:00.0000000+00:00");
    }
}
