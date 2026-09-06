using System.Globalization;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Etl;
using Meridian.Contracts.Pipeline;
using Meridian.Core.Config;
using Meridian.DataIntegration.Etl;
using Meridian.Infrastructure.Etl;
using Meridian.Platform.Coordination;
using Meridian.Storage.Coordination;
using Meridian.Storage.Etl;
using Meridian.Storage.Interfaces;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.DataIntegration.Etl;

/// <summary>
/// Protects partner trade imports against duplicate operator starts and lease takeover during parsing.
/// </summary>
public sealed partial class EtlJobOrchestratorTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task RunAsync_DuplicateStart_DoesNotReleaseActiveRunnersLease()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var config = OwnershipConfig("runner-a");
        var store = new SharedStorageCoordinationStore(config, _root);
        await using var manager = new LeaseManager(config, store);
        var parser = new GatedTradeParser();
        var source = new RecordingSourceReader();
        await using var fixture = CreateOrchestratorFixture(source, parser, new RecordingExportService(), leaseManager: manager);
        var job = await fixture.Ingestion.CreateJobAsync(IngestionWorkloadType.Historical, ["AAPL"], "partner-a", ct: timeout.Token);
        await fixture.DefinitionStore.SaveAsync(CreateDefinition(job.JobId), timeout.Token);
        await fixture.Ingestion.TransitionAsync(job.JobId, IngestionJobState.Queued, ct: timeout.Token);

        var first = fixture.Orchestrator.RunAsync(job.JobId, timeout.Token);
        try
        {
            await parser.Entered.Task.WaitAsync(timeout.Token);
            var duplicate = await fixture.Orchestrator.RunAsync(job.JobId, timeout.Token);
            var retainedLease = await store.GetLeaseAsync($"jobs/etl/{job.JobId}", timeout.Token);
            parser.Resume.TrySetResult();
            var original = await first;

            duplicate.Success.Should().BeFalse();
            retainedLease.Should().NotBeNull("a rejected duplicate does not own the active run's lease");
            original.Success.Should().BeTrue();
            fixture.Pipeline.PublishedCount.Should().Be(1);
            source.StageCalls.Should().Be(1);
            source.PostProcessCalls.Should().ContainSingle();
        }
        finally
        {
            parser.Resume.TrySetResult();
            await first;
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RunAsync_LeaseTransferredDuringParsing_StaleRunnerDoesNotPublishOrCleanSource()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var config = OwnershipConfig("runner-a");
        var store = new SharedStorageCoordinationStore(config, _root);
        await using var originalManager = new LeaseManager(config, store);
        await using var successorManager = new LeaseManager(config with { InstanceId = "runner-b" }, store);
        var parser = new GatedTradeParser();
        var source = new RecordingSourceReader();
        await using var fixture = CreateOrchestratorFixture(source, parser, new RecordingExportService(), leaseManager: originalManager);
        var job = await fixture.Ingestion.CreateJobAsync(IngestionWorkloadType.Historical, ["AAPL"], "partner-a", ct: timeout.Token);
        await fixture.DefinitionStore.SaveAsync(CreateDefinition(job.JobId), timeout.Token);
        await fixture.Ingestion.TransitionAsync(job.JobId, IngestionJobState.Queued, ct: timeout.Token);
        var resource = $"jobs/etl/{job.JobId}";

        var staleRun = fixture.Orchestrator.RunAsync(job.JobId, timeout.Token);
        try
        {
            await parser.Entered.Task.WaitAsync(timeout.Token);
            // Expire the stored lease deterministically while the original parser is suspended.
            var originalLease = (await store.GetLeaseAsync(resource, timeout.Token))!;
            (await store.RenewLeaseAsync(resource, originalLease.InstanceId, TimeSpan.FromSeconds(-1), timeout.Token)).Should().BeTrue();
            var takeover = await successorManager.TryAcquireAsync(resource, timeout.Token);
            takeover.Acquired.Should().BeTrue();
            takeover.TakenOver.Should().BeTrue();
            parser.Resume.TrySetResult();
            var result = await staleRun;

            result.Success.Should().BeFalse();
            fixture.Pipeline.PublishedCount.Should().Be(0, "the successor owns publication before the parser resumes");
            source.PostProcessCalls.Should().BeEmpty();
            fixture.Ingestion.GetJob(job.JobId)!.State.Should().Be(IngestionJobState.Running,
                "a stale run cannot write terminal state on behalf of the current owner");
            result.Outcome.Issues.Should().Contain(issue => issue.Code == "etl-terminal-receipt-not-retained");
            (await fixture.Audit.LoadCheckpointAsync(job.JobId, timeout.Token)).Should().BeNull();
            (await store.GetLeaseAsync(resource, timeout.Token))!.InstanceId.Should().Be("runner-b");
        }
        finally
        {
            parser.Resume.TrySetResult();
            await staleRun;
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RunAsync_RecoveryResumesOnAnotherInstance_OnlySuccessorPublishesAndCompletes()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var config = OwnershipConfig("runner-a");
        var store = new SharedStorageCoordinationStore(config, _root);
        await using var originalManager = new LeaseManager(config, store);
        await using var successorManager = new LeaseManager(config with { InstanceId = "runner-b" }, store);
        var originalParser = new GatedTradeParser();
        var originalSource = new RecordingSourceReader();
        await using var original = CreateOrchestratorFixture(originalSource, originalParser,
            new RecordingExportService(), leaseManager: originalManager);
        var job = await original.Ingestion.CreateJobAsync(IngestionWorkloadType.Historical, ["AAPL"], "partner-a", ct: timeout.Token);
        await original.DefinitionStore.SaveAsync(CreateDefinition(job.JobId), timeout.Token);
        await original.Ingestion.TransitionAsync(job.JobId, IngestionJobState.Queued, ct: timeout.Token);
        var successorParser = new GatedTradeParser();
        var successorSource = new RecordingSourceReader();
        await using var successor = CreateOrchestratorFixture(successorSource, successorParser,
            new RecordingExportService(), _ => original.Ingestion, leaseManager: successorManager);

        var originalRun = original.Orchestrator.RunAsync(job.JobId, timeout.Token);
        Task<EtlRunResult>? successorRun = null;
        try
        {
            await originalParser.Entered.Task.WaitAsync(timeout.Token);
            var resource = $"jobs/etl/{job.JobId}";
            var expired = (await store.GetLeaseAsync(resource, timeout.Token))!;
            (await store.RenewLeaseAsync(resource, expired.InstanceId, TimeSpan.FromSeconds(-1), timeout.Token)).Should().BeTrue();
            // An operator marks the interrupted import resumable; admission still requires a new owner.
            (await original.Ingestion.TransitionAsync(job.JobId, IngestionJobState.Paused, ct: timeout.Token)).Should().BeTrue();
            successorRun = successor.Orchestrator.RunAsync(job.JobId, timeout.Token);
            await successorParser.Entered.Task.WaitAsync(timeout.Token);
            var retained = (await store.GetLeaseAsync(resource, timeout.Token))!;
            retained.InstanceId.Should().StartWith("runner-b/execution/");
            retained.LeaseVersion.Should().BeGreaterThan(expired.LeaseVersion);

            originalParser.Resume.TrySetResult();
            (await originalRun).Success.Should().BeFalse();
            original.Ingestion.GetJob(job.JobId)!.State.Should().Be(IngestionJobState.Running);
            successorParser.Resume.TrySetResult();
            (await successorRun).Success.Should().BeTrue();

            original.Pipeline.PublishedCount.Should().Be(0);
            successor.Pipeline.PublishedCount.Should().Be(1);
            originalSource.PostProcessCalls.Should().BeEmpty();
            successorSource.PostProcessCalls.Should().ContainSingle();
            original.Ingestion.GetJob(job.JobId)!.State.Should().Be(IngestionJobState.Completed);
            (await successor.Audit.LoadCheckpointAsync(job.JobId, timeout.Token)).Should().NotBeNull();
        }
        finally
        {
            originalParser.Resume.TrySetResult();
            successorParser.Resume.TrySetResult();
            await originalRun;
            if (successorRun is not null)
                await successorRun;
        }
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(true, EtlSourcePostProcessingAction.Delete)]
    [InlineData(true, EtlSourcePostProcessingAction.MoveToArchive)]
    [InlineData(false, EtlSourcePostProcessingAction.Delete)]
    [InlineData(false, EtlSourcePostProcessingAction.MoveToArchive)]
    public async Task RunAsync_RequiredCommitReportsFailure_RetainsPhysicalSourceAndCheckpoint(
        bool catalogFails, EtlSourcePostProcessingAction cleanup)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var input = Path.Combine(_root, "input");
        Directory.CreateDirectory(input);
        var sourcePath = Path.Combine(input, "trades.csv");
        var evt = MarketScenarioBuilder.BuildSessionOpen(["AAPL"], DateTimeOffset.Parse("2026-01-05T14:30:00Z"), 1, 0).Single();
        var trade = (Trade)evt.Payload;
        var csv = "timestamp,symbol,price,size\n" + FormattableString.Invariant($"{evt.Timestamp:O},{evt.Symbol},{trade.Price},{trade.Size}\n");
        await File.WriteAllTextAsync(sourcePath, csv, timeout.Token);
        var export = new RecordingExportService(Succeed: false);
        await using var fixture = CreateOrchestratorFixture(
            new LocalFileSourceReader(new EtlStagingStore(_root)),
            new CsvPartnerFileParser(new PartnerSchemaRegistry()), export,
            catalogResult: new CatalogRebuildResult
            {
                Success = !catalogFails,
                Errors = catalogFails ? ["catalog commit unavailable"] : []
            });
        var job = await fixture.Ingestion.CreateJobAsync(IngestionWorkloadType.Historical, ["AAPL"], "partner-a", ct: timeout.Token);
        var archive = Path.Combine(_root, "archive");
        await fixture.DefinitionStore.SaveAsync(new EtlJobDefinition
        {
            JobId = job.JobId,
            FlowDirection = EtlFlowDirection.Import,
            PartnerSchemaId = "partner.trades.csv.v1",
            LogicalSourceName = "partner-a",
            Source = new EtlSourceDefinition
            {
                Kind = EtlSourceKind.Local,
                Location = input,
                FilePattern = "*.csv",
                PostProcessingAction = cleanup,
                ArchiveLocation = archive
            },
            Destination = new EtlDestinationDefinition { Kind = EtlDestinationKind.StorageCatalog },
            PublishPortablePackage = true,
            ContinueOnRecordError = true
        }, timeout.Token);
        await fixture.Ingestion.TransitionAsync(job.JobId, IngestionJobState.Queued, ct: timeout.Token);

        var result = await fixture.Orchestrator.RunAsync(job.JobId, timeout.Token);

        result.Success.Should().BeFalse();
        fixture.Pipeline.PublishedCount.Should().Be(1, "the failure occurs after normalization and publication");
        export.ExportCalls.Should().Be(catalogFails ? 0 : 1);
        (await File.ReadAllTextAsync(sourcePath, timeout.Token)).Should().Be(csv);
        File.Exists(Path.Combine(_root, "_etl", "staging", job.JobId, "trades.csv")).Should().BeTrue();
        File.Exists(Path.Combine(archive, "trades.csv")).Should().BeFalse();
        (await fixture.Audit.LoadCheckpointAsync(job.JobId, timeout.Token)).Should().BeNull();
        fixture.Ingestion.GetJob(job.JobId)!.State.Should().Be(IngestionJobState.Failed);
    }

    private CoordinationConfig OwnershipConfig(string instanceId) => new(
        Enabled: true, Mode: CoordinationMode.SharedStorage, InstanceId: instanceId,
        LeaseTtlSeconds: 60, RenewIntervalSeconds: 3600, TakeoverDelaySeconds: 0,
        RootPath: Path.Combine(_root, "coordination"));

    private sealed class GatedTradeParser : IPartnerFileParser
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string SchemaId => "partner.trades.csv.v1";
        public bool CanParse(EtlStagedFile file) => true;

        public async IAsyncEnumerable<PartnerRecordEnvelope> ParseAsync(
            EtlStagedFile file, EtlCheckpointToken? checkpoint, string? schemaId = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            Entered.TrySetResult();
            await Resume.Task.WaitAsync(ct);
            var marketEvent = MarketScenarioBuilder.BuildSessionOpen(
                ["AAPL"], DateTimeOffset.Parse("2026-01-05T14:30:00Z"), 1, 0).Single();
            var trade = (Trade)marketEvent.Payload;
            yield return new PartnerRecordEnvelope
            {
                PartnerSchemaId = SchemaId,
                SourceFileName = file.FileName,
                SourceFileChecksum = file.ChecksumSha256,
                RecordIndex = 1,
                Fields = new Dictionary<string, string?>
                {
                    ["timestamp"] = marketEvent.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                    ["symbol"] = marketEvent.Symbol,
                    ["price"] = trade.Price.ToString(CultureInfo.InvariantCulture),
                    ["size"] = trade.Size.ToString(CultureInfo.InvariantCulture)
                }
            };
        }
    }
}
