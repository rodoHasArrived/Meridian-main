using FluentAssertions;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.Etl;
using Meridian.Contracts.Pipeline;
using Meridian.DataIntegration.Canonicalization;
using Meridian.DataIntegration.Etl;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Etl;
using Meridian.Storage.Etl;
using Meridian.Storage.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.DataIntegration.Etl;

public sealed class EtlJobOrchestratorTests : IDisposable
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
        var orchestrator = new EtlJobOrchestrator(ingestion, definitionStore, [new LocalFileSourceReader(staging)], parser, normalizer, pipeline, catalog, audit, rejects, export);

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
        result.RecordsAccepted.Should().Be(1);
        sink.Events.Should().HaveCount(1);
    }

    [Fact]
    public async Task RunAsync_WhenParserFails_PostProcessesCurrentFileAsFailure()
    {
        Directory.CreateDirectory(_root);
        var sourceReader = new RecordingSourceReader();
        await using var fixture = CreateOrchestratorFixture(sourceReader, new ThrowingPartnerFileParser(), new RecordingExportService());
        var job = await fixture.Ingestion.CreateJobAsync(IngestionWorkloadType.Historical, ["AAPL"], "partner-a");
        await fixture.DefinitionStore.SaveAsync(CreateDefinition(job.JobId));
        await fixture.Ingestion.TransitionAsync(job.JobId, IngestionJobState.Queued);

        var result = await fixture.Orchestrator.RunAsync(job.JobId);

        result.Success.Should().BeFalse();
        result.FilesProcessed.Should().Be(0);
        sourceReader.PostProcessCalls.Should().ContainSingle().Which.Should().Be((sourceReader.File, false));
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
        export.ExportCalls.Should().Be(1);
        sourceReader.PostProcessCalls.Should().BeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private OrchestratorFixture CreateOrchestratorFixture(
        IEtlSourceReader sourceReader,
        IPartnerFileParser parser,
        IEtlExportService export)
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
        var orchestrator = new EtlJobOrchestrator(
            ingestion,
            definitionStore,
            [sourceReader],
            parser,
            normalizer,
            pipeline,
            catalog,
            audit,
            rejects,
            export);
        return new OrchestratorFixture(orchestrator, ingestion, definitionStore, pipeline);
    }

    private static EtlJobDefinition CreateDefinition(string jobId, bool publishPortablePackage = false)
        => new()
        {
            JobId = jobId,
            FlowDirection = EtlFlowDirection.Import,
            PartnerSchemaId = "partner.trades.csv.v1",
            LogicalSourceName = "partner-a",
            Source = new EtlSourceDefinition
            {
                Kind = EtlSourceKind.Local,
                Location = "input",
                PostProcessingAction = EtlSourcePostProcessingAction.MoveToError,
                ErrorLocation = "error"
            },
            Destination = new EtlDestinationDefinition { Kind = EtlDestinationKind.StorageCatalog },
            PublishPortablePackage = publishPortablePackage,
            ContinueOnRecordError = true
        };

    private sealed record OrchestratorFixture(
        EtlJobOrchestrator Orchestrator,
        IngestionJobService Ingestion,
        EtlJobDefinitionStore DefinitionStore,
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

        public EtlSourceKind Kind => EtlSourceKind.Local;

        public Task<IReadOnlyList<EtlRemoteFile>> ListFilesAsync(EtlSourceDefinition source, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EtlRemoteFile>>([File]);

        public Task<EtlStagedFile> StageFileAsync(string jobId, EtlSourceDefinition source, EtlRemoteFile file, CancellationToken ct = default)
            => Task.FromResult(new EtlStagedFile
            {
                OriginalPath = file.Path,
                StagedPath = file.Path,
                FileName = file.Name,
                ChecksumSha256 = "checksum",
                SizeBytes = file.SizeBytes
            });

        public Task PostProcessFileAsync(EtlSourceDefinition source, EtlRemoteFile file, bool succeeded, CancellationToken ct = default)
        {
            PostProcessCalls.Add((file, succeeded));
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

    private sealed record RecordingExportService(bool ThrowOnExport = false) : IEtlExportService
    {
        public int ExportCalls { get; private set; }

        public Task<EtlExportResult> ExportAsync(IngestionJob job, EtlJobDefinition definition, CancellationToken ct = default)
        {
            ExportCalls++;
            if (ThrowOnExport)
                throw new InvalidOperationException("export failed");

            return Task.FromResult(new EtlExportResult { Success = true });
        }
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
