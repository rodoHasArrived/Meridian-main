using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.Coordination;
using Meridian.Contracts.Etl;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Pipeline;
using Meridian.DataIntegration.Canonicalization;
using Meridian.DataIntegration.Etl;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Etl;
using Meridian.Storage.Etl;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Operations;
using Meridian.Storage.Packaging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.DataIntegration.Etl;

public sealed partial class EtlJobOrchestratorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-etl-orchestrator-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RunAsync_ImportsLocalCsv_ThroughPipeline()
    {
        Directory.CreateDirectory(_root);
        var inputDir = Path.Combine(_root, "input");
        Directory.CreateDirectory(inputDir);
        await File.WriteAllTextAsync(Path.Combine(inputDir, "input.csv"), "timestamp,symbol,price,size,venue,sequence,aggressor\n2026-01-01T00:00:00Z,AAPL,100.5,10,XNAS,1,BUY\n");

        var ingestion = new IngestionJobService(Path.Combine(_root, "jobs"));
        var definitionStore = new EtlJobDefinitionStore(_root);
        var staging = new EtlStagingStore(_root);
        var audit = new EtlAuditStore(_root);
        var rejects = new EtlRejectSink(_root);
        var parser = new CsvPartnerFileParser(new PartnerSchemaRegistry());
        var canonicalizer = Substitute.For<IEventCanonicalizer>();
        canonicalizer.Canonicalize(Arg.Any<MarketEvent>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<MarketEvent>());
        var normalizer = new EtlNormalizationService(canonicalizer);
        var sink = new InMemorySink();
        await using var pipeline = new EventPipeline(sink, logger: NullLogger<EventPipeline>.Instance, wal: null, enablePeriodicFlush: false);
        var catalog = Substitute.For<IStorageCatalogService>();
        catalog.RebuildCatalogAsync(Arg.Any<CatalogRebuildOptions>(), Arg.Any<IProgress<CatalogRebuildProgress>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CatalogRebuildResult { Success = true }));
        var export = Substitute.For<IEtlExportService>();
        export.ExportAsync(Arg.Any<IngestionJob>(), Arg.Any<EtlJobDefinition>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new EtlExportResult { Success = true }));
        var history = new FileOperationalCaseHistoryStore(_root);
        var orchestrator = new EtlJobOrchestrator(
            ingestion,
            definitionStore,
            [new LocalFileSourceReader(staging)],
            parser,
            normalizer,
            pipeline,
            catalog,
            audit,
            rejects,
            export,
            caseHistoryStore: history);

        var job = await ingestion.CreateJobAsync(IngestionWorkloadType.Historical, ["AAPL"], "partner-a");
        await definitionStore.SaveAsync(new EtlJobDefinition
        {
            JobId = job.JobId,
            FlowDirection = EtlFlowDirection.Import,
            PartnerSchemaId = "partner.trades.csv.v1",
            LogicalSourceName = "partner-a",
            Source = new EtlSourceDefinition { Kind = EtlSourceKind.Local, Location = inputDir, FilePattern = "*.csv;*.xlsx" },
            Destination = new EtlDestinationDefinition { Kind = EtlDestinationKind.StorageCatalog },
            ContinueOnRecordError = true
        });
        await ingestion.TransitionAsync(job.JobId, IngestionJobState.Queued);

        var result = await orchestrator.RunAsync(job.JobId);

        result.Success.Should().BeTrue();
        result.Outcome.State.Should().Be(OperationTerminalState.Succeeded);
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
        result.RecordsAccepted.Should().Be(1);
        sink.Events.Should().HaveCount(1);
        var retained = await history.ReadAsync(new OperationalCaseHistoryQuery
        {
            CaseId = job.JobId,
            CaseType = "etl-run"
        });
        retained.Should().ContainSingle();
        retained[0].TerminalOutcome.Should().BeEquivalentTo(result.Outcome);
        var retainedBytes = JsonSerializer.SerializeToUtf8Bytes(
            retained[0].TerminalOutcome,
            OperationsContractsJsonContext.Default.VerifiedOperationOutcome);
        retained[0].Data["terminalOutcomeHashSha256"].Should().Be(
            Convert.ToHexStringLower(SHA256.HashData(retainedBytes)));
    }

    [Fact]
    public async Task RunAsync_WhenParserFails_RetainsCurrentSourceFile()
    {
        Directory.CreateDirectory(_root);
        var sourceReader = new RecordingSourceReader();
        await using var fixture = CreateOrchestratorFixture(sourceReader, new ThrowingPartnerFileParser(), new RecordingExportService());
        var job = await fixture.Ingestion.CreateJobAsync(IngestionWorkloadType.Historical, ["AAPL"], "partner-a");
        await fixture.DefinitionStore.SaveAsync(CreateDefinition(job.JobId));
        await fixture.Ingestion.TransitionAsync(job.JobId, IngestionJobState.Queued);

        var result = await fixture.Orchestrator.RunAsync(job.JobId);

        result.Success.Should().BeFalse();
        result.Outcome.State.Should().Be(OperationTerminalState.Failed);
        result.FilesProcessed.Should().Be(0);
        result.Status.Should().Be(EtlRunStatus.Failed);
        sourceReader.PostProcessCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_WhenExportFails_DoesNotPostProcessSuccessfulSourceFile()
    {
        Directory.CreateDirectory(_root);
        var sourceReader = new RecordingSourceReader();
        var export = new RecordingExportService(ThrowOnExport: true);
        await using var fixture = CreateOrchestratorFixture(sourceReader, new EmptyPartnerFileParser(), export);
        var job = await fixture.Ingestion.CreateJobAsync(IngestionWorkloadType.Historical, ["AAPL"], "partner-a");
        await fixture.DefinitionStore.SaveAsync(CreateDefinition(job.JobId, publishPortablePackage: true));
        await fixture.Ingestion.TransitionAsync(job.JobId, IngestionJobState.Queued);

        var result = await fixture.Orchestrator.RunAsync(job.JobId);

        result.Success.Should().BeFalse();
        result.Outcome.State.Should().Be(OperationTerminalState.Failed);
        export.ExportCalls.Should().Be(1);
        sourceReader.PostProcessCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_WhenFailClosedExportReturnsFailure_FailsAndPreservesSourceForRetry()
    {
        Directory.CreateDirectory(_root);
        var sourceReader = new RecordingSourceReader();
        var export = new RecordingExportService(Succeed: false);
        DelayedTerminalCoordinator? terminalCoordinator = null;
        await using var fixture = CreateOrchestratorFixture(
            sourceReader,
            new EmptyPartnerFileParser(),
            export,
            inner => terminalCoordinator = new DelayedTerminalCoordinator(inner, IngestionJobState.Failed));
        var job = await fixture.Ingestion.CreateJobAsync(IngestionWorkloadType.Historical, ["AAPL"], "partner-a");
        await fixture.DefinitionStore.SaveAsync(CreateDefinition(job.JobId, publishPortablePackage: true));
        await fixture.Ingestion.TransitionAsync(job.JobId, IngestionJobState.Queued);

        var result = await fixture.Orchestrator.RunAsync(job.JobId);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(EtlRunStatus.Failed);
        result.Outcome.State.Should().Be(OperationTerminalState.Failed);
        result.ExportResult.Should().NotBeNull();
        result.ExportResult!.Success.Should().BeFalse();
        fixture.Ingestion.GetJob(job.JobId)!.State.Should().Be(IngestionJobState.Failed);
        export.ExportCalls.Should().Be(1);
        sourceReader.PostProcessCalls.Should().BeEmpty();
        result.Outcome.Recovery.Should().ContainSingle(action => action.ActionId == "repair-and-retry-export");
        result.Outcome.CompletedAtUtc.Should().BeOnOrAfter(terminalCoordinator!.TerminalTransitionCompletedAtUtc);
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
        (await File.ReadAllTextAsync(fixture.Audit.GetAuditPath(job.JobId, "events.jsonl")))
            .Should().Contain("Source files were retained for retry");
    }

    [Fact]
    public async Task RunAsync_WhenOptionalRoundTripDeliveryFails_CompletesWithWarningAndPreservesSourceForRetry()
    {
        Directory.CreateDirectory(_root);
        var sourceReader = new RecordingSourceReader();
        var export = new RecordingExportService(Succeed: false);
        DelayedTerminalCoordinator? terminalCoordinator = null;
        await using var fixture = CreateOrchestratorFixture(
            sourceReader,
            new EmptyPartnerFileParser(),
            export,
            inner => terminalCoordinator = new DelayedTerminalCoordinator(inner, IngestionJobState.Completed));
        var job = await fixture.Ingestion.CreateJobAsync(IngestionWorkloadType.Historical, ["AAPL"], "partner-a");
        await fixture.DefinitionStore.SaveAsync(CreateDefinition(
            job.JobId,
            publishPortablePackage: true,
            flowDirection: EtlFlowDirection.RoundTrip,
            failRoundTripOnExportError: false));
        await fixture.Ingestion.TransitionAsync(job.JobId, IngestionJobState.Queued);

        var result = await fixture.Orchestrator.RunAsync(job.JobId);

        result.Success.Should().BeTrue();
        result.Outcome.State.Should().Be(OperationTerminalState.CompletedWithWarnings);
        result.ExportResult.Should().NotBeNull();
        result.ExportResult!.Success.Should().BeFalse();
        fixture.Ingestion.GetJob(job.JobId)!.State.Should().Be(IngestionJobState.Completed);
        export.ExportCalls.Should().Be(1);
        sourceReader.PostProcessCalls.Should().BeEmpty();
        result.Warnings.Should().ContainSingle().Which.Should().Be("export failed");
        result.Outcome.CompletedAtUtc.Should().BeOnOrAfter(terminalCoordinator!.TerminalTransitionCompletedAtUtc);
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
        (await File.ReadAllTextAsync(fixture.Audit.GetAuditPath(job.JobId, "events.jsonl")))
            .Should().Contain("Source files were retained for retry");
    }

    [Fact]
    public async Task RunAsync_WhenDefinitionIsMissing_ReturnsBlockedReceiptInsteadOfThrowing()
    {
        Directory.CreateDirectory(_root);
        await using var fixture = CreateOrchestratorFixture(
            new RecordingSourceReader(),
            new EmptyPartnerFileParser(),
            new RecordingExportService());
        var job = await fixture.Ingestion.CreateJobAsync(IngestionWorkloadType.Historical, ["AAPL"], "partner-a");
        await fixture.Ingestion.TransitionAsync(job.JobId, IngestionJobState.Queued);

        var result = await fixture.Orchestrator.RunAsync(job.JobId);

        result.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        result.Outcome.Issues.Should().ContainSingle(issue =>
            issue.IsBlocking && issue.Message.Contains("definition", StringComparison.OrdinalIgnoreCase));
        result.Outcome.Recovery.Should().ContainSingle(action => action.ActionId == "unblock-and-resume-etl");
        fixture.Ingestion.GetJob(job.JobId)!.State.Should().Be(
            IngestionJobState.Queued,
            "a blocked pre-admission attempt remains resumable after its missing definition is restored");
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_WhenStartAuditCannotBePersisted_ReturnsFailedReceiptWithEvidenceRecoveryWarning()
    {
        Directory.CreateDirectory(_root);
        await using var fixture = CreateOrchestratorFixture(
            new RecordingSourceReader(),
            new EmptyPartnerFileParser(),
            new RecordingExportService());
        var job = await fixture.Ingestion.CreateJobAsync(IngestionWorkloadType.Historical, ["AAPL"], "partner-a");
        await fixture.DefinitionStore.SaveAsync(CreateDefinition(job.JobId));
        await fixture.Ingestion.TransitionAsync(job.JobId, IngestionJobState.Queued);
        var jobAuditDirectory = Path.GetDirectoryName(fixture.Audit.GetAuditPath(job.JobId, "events.jsonl"))!;
        await File.WriteAllTextAsync(jobAuditDirectory, "blocks audit directory creation");

        var result = await fixture.Orchestrator.RunAsync(job.JobId);

        result.Outcome.State.Should().Be(OperationTerminalState.Failed);
        result.Errors.Should().Contain(error => error.Contains("Failure-audit persistence failed", StringComparison.Ordinal));
        result.Outcome.Issues.Should().Contain(issue => issue.Code.StartsWith("etl-terminalization-warning", StringComparison.Ordinal));
        result.Outcome.Evidence.Should().ContainSingle(evidence => evidence.Kind == "etl-terminalization");
        result.Outcome.Recovery.Should().ContainSingle(action =>
            action.Guidance.Contains("Repair ETL state/audit persistence", StringComparison.Ordinal));
        fixture.Ingestion.GetJob(job.JobId)!.State.Should().Be(IngestionJobState.Failed);
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_WhenCoordinatorRejectsCompletedTransition_ReturnsFailedReceipt()
    {
        Directory.CreateDirectory(_root);
        await using var fixture = CreateOrchestratorFixture(
            new RecordingSourceReader(),
            new EmptyPartnerFileParser(),
            new RecordingExportService(),
            ingestion => new RejectingTerminalCoordinator(ingestion, IngestionJobState.Completed));
        var job = await fixture.Ingestion.CreateJobAsync(IngestionWorkloadType.Historical, ["AAPL"], "partner-a");
        await fixture.DefinitionStore.SaveAsync(CreateDefinition(job.JobId));
        await fixture.Ingestion.TransitionAsync(job.JobId, IngestionJobState.Queued);

        var result = await fixture.Orchestrator.RunAsync(job.JobId);

        result.Success.Should().BeFalse();
        result.Outcome.State.Should().Be(OperationTerminalState.Failed);
        result.Errors.Should().Contain(error => error.Contains("rejected the Completed terminal transition", StringComparison.Ordinal));
        fixture.Ingestion.GetJob(job.JobId)!.State.Should().Be(IngestionJobState.Failed);
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_WhenCancelledAfterRunningAdmission_ReturnsDurableFailedReceipt()
    {
        Directory.CreateDirectory(_root);
        using var cts = new CancellationTokenSource();
        await using var fixture = CreateOrchestratorFixture(
            new RecordingSourceReader(),
            new EmptyPartnerFileParser(),
            new RecordingExportService(),
            ingestion => new CancelAfterRunningCoordinator(ingestion, cts));
        var job = await fixture.Ingestion.CreateJobAsync(IngestionWorkloadType.Historical, ["AAPL"], "partner-a");
        await fixture.DefinitionStore.SaveAsync(CreateDefinition(job.JobId));
        await fixture.Ingestion.TransitionAsync(job.JobId, IngestionJobState.Queued);

        var result = await fixture.Orchestrator.RunAsync(job.JobId, cts.Token);

        result.Success.Should().BeFalse();
        result.Outcome.State.Should().Be(OperationTerminalState.Failed);
        result.Outcome.Issues.Should().Contain(issue => issue.Code == "etl-run-cancelled-after-admission");
        fixture.Ingestion.GetJob(job.JobId)!.State.Should().Be(IngestionJobState.Failed);
        var audit = await File.ReadAllTextAsync(fixture.Audit.GetAuditPath(job.JobId, "events.jsonl"));
        audit.Should().Contain("cancelled after admission");
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_WhenConfiguredDeliveryReportsNoArtifacts_FailsClosed()
    {
        Directory.CreateDirectory(_root);
        var sourceReader = new RecordingSourceReader();
        await using var fixture = CreateOrchestratorFixture(
            sourceReader,
            new EmptyPartnerFileParser(),
            new RecordingExportService());
        var job = await fixture.Ingestion.CreateJobAsync(
            IngestionWorkloadType.Historical,
            ["AAPL"],
            "partner-a");
        await fixture.DefinitionStore.SaveAsync(CreateDefinition(
            job.JobId,
            destinationKind: EtlDestinationKind.Local));
        await fixture.Ingestion.TransitionAsync(job.JobId, IngestionJobState.Queued);

        var result = await fixture.Orchestrator.RunAsync(job.JobId);

        result.Outcome.State.Should().Be(OperationTerminalState.Failed);
        result.Errors.Should().Contain(error =>
            error.Contains("without declaring any retained artifact paths", StringComparison.Ordinal));
        sourceReader.PostProcessCalls.Should().BeEmpty();
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_WhenConfiguredDeliveryArtifactIsEmpty_FailsClosed()
    {
        Directory.CreateDirectory(_root);
        var artifactPath = Path.Combine(_root, "empty-export.zip");
        await File.WriteAllBytesAsync(artifactPath, []);
        var package = SuccessfulPackage(artifactPath, checksum: null, size: 0);
        await using var fixture = CreateOrchestratorFixture(
            new RecordingSourceReader(),
            new EmptyPartnerFileParser(),
            new RecordingExportService(ArtifactPaths: [artifactPath], ResultPackage: package));
        var job = await fixture.Ingestion.CreateJobAsync(
            IngestionWorkloadType.Historical,
            ["AAPL"],
            "partner-a");
        await fixture.DefinitionStore.SaveAsync(CreateDefinition(job.JobId, publishPortablePackage: true));
        await fixture.Ingestion.TransitionAsync(job.JobId, IngestionJobState.Queued);

        var result = await fixture.Orchestrator.RunAsync(job.JobId);

        result.Outcome.State.Should().Be(OperationTerminalState.Failed);
        result.Errors.Should().Contain(error => error.Contains("is empty", StringComparison.Ordinal));
        result.Outcome.Artifacts.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_WhenConfiguredDeliveryArtifactIsMissing_FailsClosed()
    {
        Directory.CreateDirectory(_root);
        var missingPath = Path.Combine(_root, "missing-export.zip");
        var package = SuccessfulPackage(missingPath, checksum: null, size: 0);
        await using var fixture = CreateOrchestratorFixture(
            new RecordingSourceReader(),
            new EmptyPartnerFileParser(),
            new RecordingExportService(ArtifactPaths: [missingPath], ResultPackage: package));
        var job = await fixture.Ingestion.CreateJobAsync(
            IngestionWorkloadType.Historical,
            ["AAPL"],
            "partner-a");
        await fixture.DefinitionStore.SaveAsync(CreateDefinition(job.JobId, publishPortablePackage: true));
        await fixture.Ingestion.TransitionAsync(job.JobId, IngestionJobState.Queued);

        var result = await fixture.Orchestrator.RunAsync(job.JobId);

        result.Outcome.State.Should().Be(OperationTerminalState.Failed);
        result.Errors.Should().Contain(error =>
            error.Contains("declared missing artifact path", StringComparison.Ordinal));
        result.Outcome.Artifacts.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_WhenPackageReadbackHashDiffersFromDeclaration_FailsClosed()
    {
        Directory.CreateDirectory(_root);
        var artifactPath = Path.Combine(_root, "mismatched-export.zip");
        await File.WriteAllTextAsync(artifactPath, "retained export bytes");
        var package = SuccessfulPackage(artifactPath, new string('0', 64), new FileInfo(artifactPath).Length);
        await using var fixture = CreateOrchestratorFixture(
            new RecordingSourceReader(),
            new EmptyPartnerFileParser(),
            new RecordingExportService(ArtifactPaths: [artifactPath], ResultPackage: package));
        var job = await fixture.Ingestion.CreateJobAsync(
            IngestionWorkloadType.Historical,
            ["AAPL"],
            "partner-a");
        await fixture.DefinitionStore.SaveAsync(CreateDefinition(job.JobId, publishPortablePackage: true));
        await fixture.Ingestion.TransitionAsync(job.JobId, IngestionJobState.Queued);

        var result = await fixture.Orchestrator.RunAsync(job.JobId);

        result.Outcome.State.Should().Be(OperationTerminalState.Failed);
        result.Errors.Should().Contain(error =>
            error.Contains("failed SHA-256 readback verification", StringComparison.Ordinal));
        result.Outcome.Artifacts.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_WhenConfiguredDeliveryArtifactReadbackSucceeds_RetainsExactTerminalReceipt()
    {
        Directory.CreateDirectory(_root);
        var artifactPath = Path.Combine(_root, "verified-export.zip");
        await File.WriteAllTextAsync(artifactPath, "retained export bytes");
        var artifactBytes = await File.ReadAllBytesAsync(artifactPath);
        var artifactHash = Convert.ToHexStringLower(SHA256.HashData(artifactBytes));
        var package = SuccessfulPackage(artifactPath, artifactHash, artifactBytes.Length);
        await using var fixture = CreateOrchestratorFixture(
            new RecordingSourceReader(),
            new EmptyPartnerFileParser(),
            new RecordingExportService(ArtifactPaths: [artifactPath], ResultPackage: package));
        var job = await fixture.Ingestion.CreateJobAsync(
            IngestionWorkloadType.Historical,
            ["AAPL"],
            "partner-a");
        await fixture.DefinitionStore.SaveAsync(CreateDefinition(job.JobId, publishPortablePackage: true));
        await fixture.Ingestion.TransitionAsync(job.JobId, IngestionJobState.Queued);

        var result = await fixture.Orchestrator.RunAsync(job.JobId);

        result.Outcome.State.Should().Be(OperationTerminalState.Succeeded);
        result.Outcome.Artifacts.Should().ContainSingle(artifact =>
            artifact.ContentHashSha256 == artifactHash && artifact.ByteLength == artifactBytes.Length);
        var receiptEvidence = result.Outcome.Evidence.Should().ContainSingle(evidence =>
            evidence.Kind == "etl-terminal-outcome").Subject;
        var receiptPath = new Uri(receiptEvidence.Uri!).LocalPath;
        File.Exists(receiptPath).Should().BeTrue();
        File.Exists(receiptPath + ".sha256").Should().BeTrue();
        var retainedOutcome = JsonSerializer.Deserialize(
            await File.ReadAllBytesAsync(receiptPath),
            OperationsContractsJsonContext.Default.VerifiedOperationOutcome);
        retainedOutcome.Should().BeEquivalentTo(result.Outcome);
        var history = await fixture.History.ReadAsync(new OperationalCaseHistoryQuery
        {
            CaseId = job.JobId,
            CaseType = "etl-run"
        });
        history.Should().ContainSingle(record =>
            record.HistoryEventId == result.Outcome.OperationId &&
            record.TerminalOutcome != null);
    }

    [Fact]
    public async Task RunAsync_WhenLaterPostProcessingFails_RetainsPreviouslyVerifiedArtifactsInFailure()
    {
        Directory.CreateDirectory(_root);
        var artifactPath = Path.Combine(_root, "verified-before-post-processing-failure.zip");
        await File.WriteAllTextAsync(artifactPath, "retained export bytes");
        var artifactBytes = await File.ReadAllBytesAsync(artifactPath);
        var artifactHash = Convert.ToHexStringLower(SHA256.HashData(artifactBytes));
        var sourceReader = new RecordingSourceReader { ThrowOnSuccessfulPostProcess = true };
        await using var fixture = CreateOrchestratorFixture(
            sourceReader,
            new EmptyPartnerFileParser(),
            new RecordingExportService(
                ArtifactPaths: [artifactPath],
                ResultPackage: SuccessfulPackage(artifactPath, artifactHash, artifactBytes.Length)));
        var job = await fixture.Ingestion.CreateJobAsync(
            IngestionWorkloadType.Historical,
            ["AAPL"],
            "partner-a");
        await fixture.DefinitionStore.SaveAsync(CreateDefinition(job.JobId, publishPortablePackage: true));
        await fixture.Ingestion.TransitionAsync(job.JobId, IngestionJobState.Queued);

        var result = await fixture.Orchestrator.RunAsync(job.JobId);

        result.Outcome.State.Should().Be(OperationTerminalState.Failed);
        var artifact = result.Outcome.Artifacts.Should().ContainSingle().Subject;
        artifact.ContentHashSha256.Should().Be(artifactHash);
        result.Outcome.Postconditions.Should().ContainSingle().Which.ArtifactIds.Should().Contain(artifact.ArtifactId);
        result.Outcome.Recovery.Should().ContainSingle().Which.ArtifactIds.Should().Contain(artifact.ArtifactId);
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_WhenSharedTerminalHistoryPersistenceFails_ReturnsDurablyRetainedFailure()
    {
        Directory.CreateDirectory(_root);
        await using var fixture = CreateOrchestratorFixture(
            new RecordingSourceReader(),
            new EmptyPartnerFileParser(),
            new RecordingExportService(),
            caseHistoryStore: new ThrowingOperationalCaseHistoryStore());
        var job = await fixture.Ingestion.CreateJobAsync(
            IngestionWorkloadType.Historical,
            ["AAPL"],
            "partner-a");
        await fixture.DefinitionStore.SaveAsync(CreateDefinition(job.JobId));
        await fixture.Ingestion.TransitionAsync(job.JobId, IngestionJobState.Queued);

        var result = await fixture.Orchestrator.RunAsync(job.JobId);

        result.Outcome.State.Should().Be(OperationTerminalState.Failed);
        result.Errors.Should().Contain(error =>
            error.Contains("shared case history", StringComparison.OrdinalIgnoreCase));
        var receiptEvidence = result.Outcome.Evidence.Should().ContainSingle(evidence =>
            evidence.Kind == "etl-terminal-outcome").Subject;
        var receiptPath = new Uri(receiptEvidence.Uri!).LocalPath;
        var retainedOutcome = JsonSerializer.Deserialize(
            await File.ReadAllBytesAsync(receiptPath),
            OperationsContractsJsonContext.Default.VerifiedOperationOutcome);
        retainedOutcome.Should().BeEquivalentTo(result.Outcome);
        File.Exists(receiptPath + ".sha256").Should().BeTrue();
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
    }

    [Fact]
    public void ComputeInputHash_DelimiterCollisionAndMaterialDeliverySettings_RemainDistinct()
    {
        var left = CreateDefinition(
            "job-1",
            partnerSchemaId: "schema|source",
            logicalSourceName: "logical");
        var right = CreateDefinition(
            "job-1",
            partnerSchemaId: "schema",
            logicalSourceName: "source|logical");
        var remoteAck = CreateDefinition(
            "job-1",
            partnerSchemaId: "schema|source",
            logicalSourceName: "logical",
            requiresRemoteAck: true);

        EtlJobOrchestrator.ComputeInputHash(left).Should().NotBe(
            EtlJobOrchestrator.ComputeInputHash(right));
        EtlJobOrchestrator.ComputeInputHash(left).Should().NotBe(
            EtlJobOrchestrator.ComputeInputHash(remoteAck));
    }

    [Fact]
    public async Task RunAsync_WhenLeaseIsOwnedByAnotherRunner_DoesNoWork()
    {
        Directory.CreateDirectory(_root);
        var sourceReader = new RecordingSourceReader();
        var lease = Substitute.For<ILeaseManager>();
        lease.TryAcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LeaseAcquireResult(false, false, null, "runner-a", DateTimeOffset.UtcNow.AddMinutes(1), "held"));
        await using var fixture = CreateOrchestratorFixture(
            sourceReader,
            new EmptyPartnerFileParser(),
            new RecordingExportService(),
            leaseManager: lease);
        var job = await fixture.Ingestion.CreateJobAsync(IngestionWorkloadType.Historical, ["AAPL"], "partner-a");
        await fixture.DefinitionStore.SaveAsync(CreateDefinition(job.JobId));
        await fixture.Ingestion.TransitionAsync(job.JobId, IngestionJobState.Queued);

        var result = await fixture.Orchestrator.RunAsync(job.JobId);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(message => message.Contains("runner-a", StringComparison.Ordinal));
        sourceReader.StageCalls.Should().Be(0);
        await lease.DidNotReceive().ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private OrchestratorFixture CreateOrchestratorFixture(
        IEtlSourceReader sourceReader,
        IPartnerFileParser parser,
        IEtlExportService export,
        Func<IngestionJobService, IEtlIngestionJobCoordinator>? coordinatorFactory = null,
        IOperationalCaseHistoryStore? caseHistoryStore = null,
        ILeaseManager? leaseManager = null)
    {
        var ingestion = new IngestionJobService(Path.Combine(_root, "jobs"));
        var definitionStore = new EtlJobDefinitionStore(_root);
        var audit = new EtlAuditStore(_root);
        var rejects = new EtlRejectSink(_root);
        var canonicalizer = Substitute.For<IEventCanonicalizer>();
        canonicalizer.Canonicalize(Arg.Any<MarketEvent>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<MarketEvent>());
        var normalizer = new EtlNormalizationService(canonicalizer);
        var sink = new InMemorySink();
        var pipeline = new EventPipeline(sink, logger: NullLogger<EventPipeline>.Instance, wal: null, enablePeriodicFlush: false);
        var catalog = Substitute.For<IStorageCatalogService>();
        catalog.RebuildCatalogAsync(Arg.Any<CatalogRebuildOptions>(), Arg.Any<IProgress<CatalogRebuildProgress>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CatalogRebuildResult { Success = true }));
        var history = caseHistoryStore ?? new FileOperationalCaseHistoryStore(_root);
        var orchestrator = new EtlJobOrchestrator(
            coordinatorFactory?.Invoke(ingestion) ?? ingestion,
            definitionStore,
            [sourceReader],
            parser,
            normalizer,
            pipeline,
            catalog,
            audit,
            rejects,
            export,
            caseHistoryStore: history,
            leaseManager: leaseManager);
        return new OrchestratorFixture(orchestrator, ingestion, definitionStore, audit, history, pipeline);
    }

    private static EtlJobDefinition CreateDefinition(
        string jobId,
        bool publishPortablePackage = false,
        EtlFlowDirection flowDirection = EtlFlowDirection.Import,
        bool failRoundTripOnExportError = true,
        EtlDestinationKind destinationKind = EtlDestinationKind.StorageCatalog,
        string partnerSchemaId = "partner.trades.csv.v1",
        string logicalSourceName = "partner-a",
        bool requiresRemoteAck = false)
        => new()
        {
            JobId = jobId,
            FlowDirection = flowDirection,
            PartnerSchemaId = partnerSchemaId,
            LogicalSourceName = logicalSourceName,
            Source = new EtlSourceDefinition
            {
                Kind = EtlSourceKind.Local,
                Location = "input",
                PostProcessingAction = EtlSourcePostProcessingAction.MoveToError,
                ErrorLocation = "error"
            },
            Destination = new EtlDestinationDefinition
            {
                Kind = destinationKind,
                Location = destinationKind == EtlDestinationKind.StorageCatalog ? null : "delivery",
                RequiresRemoteAck = requiresRemoteAck
            },
            PublishPortablePackage = publishPortablePackage,
            FailRoundTripOnExportError = failRoundTripOnExportError,
            ContinueOnRecordError = true
        };

    private static PackageResult SuccessfulPackage(
        string path,
        string? checksum,
        long size) => new()
        {
            Success = true,
            PackagePath = path,
            PackageChecksum = checksum,
            PackageSizeBytes = size,
            CompletedAt = DateTime.UtcNow
        };

    private sealed record OrchestratorFixture(
        EtlJobOrchestrator Orchestrator,
        IngestionJobService Ingestion,
        EtlJobDefinitionStore DefinitionStore,
        EtlAuditStore Audit,
        IOperationalCaseHistoryStore History,
        EventPipeline Pipeline) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Pipeline.DisposeAsync();
    }

    private sealed class RecordingSourceReader : IEtlSourceReader
    {
        public EtlRemoteFile File { get; } = new()
        {
            Path = "input/positions.csv",
            Name = "positions.csv",
            SizeBytes = 17,
            LastModifiedUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
        };

        public List<(EtlRemoteFile File, bool Succeeded)> PostProcessCalls { get; } = [];
        public int StageCalls { get; private set; }

        public bool ThrowOnSuccessfulPostProcess { get; init; }

        public EtlSourceKind Kind => EtlSourceKind.Local;

        public Task<IReadOnlyList<EtlRemoteFile>> ListFilesAsync(EtlSourceDefinition source, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EtlRemoteFile>>([File]);

        public Task<EtlStagedFile> StageFileAsync(string jobId, EtlSourceDefinition source, EtlRemoteFile file, CancellationToken ct = default)
        {
            StageCalls++;
            return Task.FromResult(new EtlStagedFile
            {
                OriginalPath = file.Path,
                StagedPath = file.Path,
                FileName = file.Name,
                ChecksumSha256 = "checksum",
                SizeBytes = file.SizeBytes
            });
        }

        public Task PostProcessFileAsync(EtlSourceDefinition source, EtlRemoteFile file, bool succeeded, CancellationToken ct = default)
        {
            PostProcessCalls.Add((file, succeeded));
            if (succeeded && ThrowOnSuccessfulPostProcess)
                throw new IOException("successful source post-processing failed");
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingPartnerFileParser : IPartnerFileParser
    {
        public string SchemaId => "partner.trades.csv.v1";
        public bool CanParse(EtlStagedFile file) => true;

        public async IAsyncEnumerable<PartnerRecordEnvelope> ParseAsync(
            EtlStagedFile file,
            EtlCheckpointToken? checkpoint,
            string? schemaId = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            if (!ct.IsCancellationRequested)
                throw new InvalidOperationException("parser failed");

            ct.ThrowIfCancellationRequested();
            yield break;
        }
    }

    private sealed class EmptyPartnerFileParser : IPartnerFileParser
    {
        public string SchemaId => "partner.trades.csv.v1";
        public bool CanParse(EtlStagedFile file) => true;

        public async IAsyncEnumerable<PartnerRecordEnvelope> ParseAsync(
            EtlStagedFile file,
            EtlCheckpointToken? checkpoint,
            string? schemaId = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield break;
        }
    }

    private sealed record RecordingExportService(
        bool ThrowOnExport = false,
        bool Succeed = true,
        string[]? ArtifactPaths = null,
        PackageResult? ResultPackage = null) : IEtlExportService
    {
        public int ExportCalls { get; private set; }

        public Task<EtlExportResult> ExportAsync(IngestionJob job, EtlJobDefinition definition, CancellationToken ct = default)
        {
            ExportCalls++;
            if (ThrowOnExport)
                throw new InvalidOperationException("export failed");
            if (!Succeed)
                return Task.FromResult(new EtlExportResult { Success = false, Error = "export failed" });

            return Task.FromResult(new EtlExportResult
            {
                Success = true,
                ArtifactPaths = ArtifactPaths ?? [],
                PackageResult = ResultPackage
            });
        }
    }

    private sealed class ThrowingOperationalCaseHistoryStore : IOperationalCaseHistoryStore
    {
        public ValueTask<OperationalCaseHistoryRecord> AppendAsync(
            OperationalCaseHistoryAppendRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<OperationalCaseHistoryRecord>(
                new IOException("Shared case history is unavailable."));

        public ValueTask<IReadOnlyList<OperationalCaseHistoryRecord>> ReadAsync(
            OperationalCaseHistoryQuery query,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<OperationalCaseHistoryRecord>>([]);
    }

    private sealed class RejectingTerminalCoordinator(
        IEtlIngestionJobCoordinator inner,
        IngestionJobState rejectedState) : IEtlIngestionJobCoordinator
    {
        public Task<IngestionJob> CreateJobAsync(
            IngestionWorkloadType workloadType,
            string[] symbols,
            string provider,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            IngestionSla? sla = null,
            CancellationToken ct = default) =>
            inner.CreateJobAsync(workloadType, symbols, provider, fromDate, toDate, sla, ct);

        public IngestionJob? GetJob(string jobId) => inner.GetJob(jobId);

        public Task<bool> TransitionAsync(
            string jobId,
            IngestionJobState newState,
            string? errorMessage = null,
            CancellationToken ct = default) =>
            newState == rejectedState
                ? Task.FromResult(false)
                : inner.TransitionAsync(jobId, newState, errorMessage, ct);

        public Task UpdateCheckpointAsync(
            string jobId,
            IngestionCheckpointToken checkpoint,
            CancellationToken ct = default) =>
            inner.UpdateCheckpointAsync(jobId, checkpoint, ct);
    }

    private sealed class DelayedTerminalCoordinator(
        IEtlIngestionJobCoordinator inner,
        IngestionJobState terminalState) : IEtlIngestionJobCoordinator
    {
        public DateTimeOffset TerminalTransitionCompletedAtUtc { get; private set; }

        public Task<IngestionJob> CreateJobAsync(
            IngestionWorkloadType workloadType,
            string[] symbols,
            string provider,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            IngestionSla? sla = null,
            CancellationToken ct = default) =>
            inner.CreateJobAsync(workloadType, symbols, provider, fromDate, toDate, sla, ct);

        public IngestionJob? GetJob(string jobId) => inner.GetJob(jobId);

        public async Task<bool> TransitionAsync(
            string jobId,
            IngestionJobState newState,
            string? errorMessage = null,
            CancellationToken ct = default)
        {
            if (newState == terminalState)
                await Task.Delay(25, ct);
            var retained = await inner.TransitionAsync(jobId, newState, errorMessage, ct);
            if (newState == terminalState && retained)
                TerminalTransitionCompletedAtUtc = DateTimeOffset.UtcNow;
            return retained;
        }

        public Task UpdateCheckpointAsync(
            string jobId,
            IngestionCheckpointToken checkpoint,
            CancellationToken ct = default) =>
            inner.UpdateCheckpointAsync(jobId, checkpoint, ct);
    }

    private sealed class CancelAfterRunningCoordinator(
        IEtlIngestionJobCoordinator inner,
        CancellationTokenSource cancellation) : IEtlIngestionJobCoordinator
    {
        public Task<IngestionJob> CreateJobAsync(
            IngestionWorkloadType workloadType,
            string[] symbols,
            string provider,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            IngestionSla? sla = null,
            CancellationToken ct = default) =>
            inner.CreateJobAsync(workloadType, symbols, provider, fromDate, toDate, sla, ct);

        public IngestionJob? GetJob(string jobId) => inner.GetJob(jobId);

        public async Task<bool> TransitionAsync(
            string jobId,
            IngestionJobState newState,
            string? errorMessage = null,
            CancellationToken ct = default)
        {
            var retained = await inner.TransitionAsync(jobId, newState, errorMessage, ct);
            if (newState == IngestionJobState.Running && retained)
                cancellation.Cancel();
            return retained;
        }

        public Task UpdateCheckpointAsync(
            string jobId,
            IngestionCheckpointToken checkpoint,
            CancellationToken ct = default) =>
            inner.UpdateCheckpointAsync(jobId, checkpoint, ct);
    }

    private sealed class InMemorySink : IStorageSink
    {
        public List<MarketEvent> Events { get; } = new();
        public ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
        {
            Events.Add(evt);
            return ValueTask.CompletedTask;
        }

        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
