using FluentAssertions;
using Meridian.Application.Commands;
using Meridian.Contracts.Etl;
using Meridian.Contracts.Operations;
using Meridian.DataIntegration.Etl;
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

    [Fact]
    public void TryBuildDefinition_WithExplicitExportContinuation_ConfiguresOptionalRoundTripDelivery()
    {
        var result = EtlCommands.TryBuildDefinition(
            [
                "--etl-roundtrip",
                "--etl-source-kind",
                "local",
                "--etl-source-path",
                "input",
                "--etl-continue-on-export-error"
            ],
            out var definition);

        result.Should().BeTrue();
        definition.FlowDirection.Should().Be(EtlFlowDirection.RoundTrip);
        definition.FailRoundTripOnExportError.Should().BeFalse();
    }

    [Theory]
    [InlineData(OperationTerminalState.Succeeded, true)]
    [InlineData(OperationTerminalState.CompletedWithWarnings, true)]
    [InlineData(OperationTerminalState.Failed, false)]
    [InlineData(OperationTerminalState.Blocked, false)]
    public void ToCliResult_UsesVerifiedTerminalState(OperationTerminalState state, bool expectedSuccess)
    {
        var outcome = Outcome(state);
        VerifiedOperationOutcomeValidator.Validate(outcome).Should().BeEmpty();

        var result = EtlCommands.ToCliResult(new EtlRunResult { Outcome = outcome });

        result.Success.Should().Be(expectedSuccess);
    }

    [Theory]
    [InlineData("--etl-list-files", nameof(EtlInspectionMode.ListFiles))]
    [InlineData("--etl-test-connection", nameof(EtlInspectionMode.TestConnection))]
    [InlineData("--etl-preview", nameof(EtlInspectionMode.Preview))]
    public void ResolveInspectionMode_SeparatesReadOnlyModesFromPreview(string flag, string expected)
    {
        EtlCommands.ResolveInspectionMode([flag]).ToString().Should().Be(expected);
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

    private static VerifiedOperationOutcome Outcome(OperationTerminalState state)
    {
        var now = DateTimeOffset.UtcNow;
        var evidence = new OperationEvidenceReference(
            "etl-test-evidence",
            "etl-terminal-state",
            "The ETL terminal state was captured.",
            Uri: $"urn:sha256:{new string('E', 64)}",
            ContentHashSha256: new string('E', 64),
            CapturedAtUtc: now);
        var issues = state switch
        {
            OperationTerminalState.CompletedWithWarnings =>
                new[] { new OperationIssue("warning", "Review the retained warning.", OperationIssueSeverity.Warning, EvidenceId: evidence.EvidenceId) },
            OperationTerminalState.Failed =>
                [new OperationIssue("failed", "The ETL run failed.", OperationIssueSeverity.Error, EvidenceId: evidence.EvidenceId)],
            OperationTerminalState.Blocked =>
                [new OperationIssue("blocked", "The ETL run is blocked.", OperationIssueSeverity.Error, EvidenceId: evidence.EvidenceId) { IsBlocking = true }],
            _ => []
        };
        var recovery = state == OperationTerminalState.Succeeded
            ? []
            : new[]
            {
                new OperationRecoveryAction(
                    "review-etl",
                    "Review ETL evidence",
                    "Review the retained evidence and retry when appropriate.",
                    Retryable: true,
                    RequiresHumanAction: true)
                {
                    EvidenceIds = [evidence.EvidenceId]
                }
            };

        return new VerifiedOperationOutcome(
            "etl:test",
            "etl.run",
            state,
            now,
            now,
            1,
            "job-1",
            new string('A', 64),
            [new OperationPostcondition(
                "terminal",
                "Terminal state was recorded.",
                state is OperationTerminalState.Succeeded or OperationTerminalState.CompletedWithWarnings
                    ? OperationPostconditionState.Satisfied
                    : OperationPostconditionState.NotSatisfied,
                Required: true,
                EvidenceIds: [evidence.EvidenceId])],
            [evidence],
            [],
            issues,
            recovery);
    }
}
