using FluentAssertions;
using Meridian.Application.Canonicalization;
using Meridian.Application.Etl;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.Etl;
using Meridian.Contracts.Pipeline;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Etl;
using Meridian.Storage.Etl;
using Meridian.Storage.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Application.Etl;

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
            Source = new EtlSourceDefinition { Kind = EtlSourceKind.Local, Location = inputDir, FilePattern = "*.csv" },
            Destination = new EtlDestinationDefinition { Kind = EtlDestinationKind.StorageCatalog },
            ContinueOnRecordError = true
        });
        await ingestion.TransitionAsync(job.JobId, IngestionJobState.Queued);

        var result = await orchestrator.RunAsync(job.JobId);

        result.Success.Should().BeTrue();
        result.RecordsAccepted.Should().Be(1);
        sink.Events.Should().HaveCount(1);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
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
