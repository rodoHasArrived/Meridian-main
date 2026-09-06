using System.Runtime.CompilerServices;
using System.Text;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Etl;
using Meridian.Contracts.Pipeline;
using Meridian.Core.Config;
using Meridian.DataIntegration.Canonicalization;
using Meridian.DataIntegration.Etl;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Etl;
using Meridian.Platform.Coordination;
using Meridian.Storage;
using Meridian.Storage.Archival;
using Meridian.Storage.Coordination;
using Meridian.Storage.Etl;
using Meridian.Storage.Operations;
using Meridian.Storage.Policies;
using Meridian.Storage.Services;
using Meridian.Storage.Sinks;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.ProcessTestHelper;

internal static partial class Program
{
    private static async Task<int> RunEtlUntilKilledAsync(IReadOnlyList<string> args)
    {
        RequireArgumentCount(args, 4);
        var root = args[1];
        var restarting = args[3] == "durable-restart";
        var durable = args[3].StartsWith("durable-", StringComparison.Ordinal);
        var ingestion = new IngestionJobService(Path.Combine(root, "jobs"));
        if (restarting)
            await ingestion.LoadJobsAsync();
        var definitions = new EtlJobDefinitionStore(root);
        var job = restarting
            ? ingestion.GetJob(await File.ReadAllTextAsync(args[2])) ?? throw new InvalidOperationException("Interrupted job missing.")
            : await ingestion.CreateJobAsync(IngestionWorkloadType.Historical, ["AAPL"], "partner-a");
        if (!restarting)
        {
            await definitions.SaveAsync(new EtlJobDefinition
            {
                JobId = job.JobId,
                FlowDirection = EtlFlowDirection.Import,
                LogicalSourceName = "partner-a",
                PartnerSchemaId = "partner.trades.csv.v1",
                Source = new EtlSourceDefinition
                {
                    Kind = EtlSourceKind.Local,
                    Location = Path.Combine(root, "input"),
                    FilePattern = "*.csv",
                    DeleteAfterSuccess = true
                },
                Destination = new EtlDestinationDefinition { Kind = EtlDestinationKind.StorageCatalog },
                PublishNormalizedExtract = true,
                ContinueOnRecordError = true
            });
            await ingestion.TransitionAsync(job.JobId, IngestionJobState.Queued);
        }
        else if (!await ingestion.TransitionAsync(job.JobId, IngestionJobState.Paused))
            throw new InvalidOperationException("Interrupted job could not become resumable.");
        var gate = new EtlCrashGate(durable ? args[3][8..] : args[3], args[2], job.JobId);
        var options = new StorageOptions { RootPath = Path.Combine(root, "normalized") };
        Directory.CreateDirectory(options.RootPath);
        var sink = new JsonlStorageSink(options, new JsonlStoragePolicy(options),
            new JsonlBatchOptions { BatchSize = 1000, FlushInterval = TimeSpan.FromMinutes(5) });
        await using var ledger = durable ? new PersistentDedupLedger(Path.Combine(options.RootPath, "_dedup")) : null;
        if (ledger is not null)
            await ledger.InitializeAsync();
        await using var pipeline = new EventPipeline(sink, logger: NullLogger<EventPipeline>.Instance,
            wal: durable ? new WriteAheadLog(Path.Combine(options.RootPath, "_wal")) : null,
            dedupLedger: ledger, enablePeriodicFlush: false);
        if (durable)
            await pipeline.RecoverAsync();
        var catalog = new StorageCatalogService(options.RootPath, options);
        await catalog.InitializeAsync();
        var coordination = new CoordinationConfig(InstanceId: $"etl-process-{Environment.ProcessId}",
            LeaseTtlSeconds: durable ? 2 : 30, RenewIntervalSeconds: 1, TakeoverDelaySeconds: 0);
        await using var manager = new LeaseManager(coordination, new SharedStorageCoordinationStore(coordination, root));
        var orchestrator = new EtlJobOrchestrator(
            ingestion, definitions, [new LocalFileSourceReader(new EtlStagingStore(root))],
            new CrashGateParser(new CsvPartnerFileParser(new PartnerSchemaRegistry()), gate),
            new EtlNormalizationService(new EtlPassThroughCanonicalizer()),
            new CrashGatePipeline(pipeline, gate), catalog, new EtlAuditStore(root), new EtlRejectSink(root),
            new CrashGateExport(root, gate), caseHistoryStore: new FileOperationalCaseHistoryStore(root),
            leaseManager: manager);
        var result = await orchestrator.RunAsync(job.JobId);
        if (restarting && result.Success)
        {
            await File.WriteAllTextAsync(Path.Combine(root, "restart-deduplicated"), pipeline.DeduplicatedCount.ToString());
            return 0;
        }
        throw new InvalidOperationException($"ETL returned before crash stage {args[3]}: {string.Join("; ", result.Errors)}");
    }

    private sealed record EtlCrashGate(string Stage, string ReadyPath, string JobId)
    {
        public async Task PauseAtAsync(string stage, CancellationToken ct)
        {
            if (!string.Equals(Stage, stage, StringComparison.Ordinal))
                return;
            await File.WriteAllTextAsync(ReadyPath + ".tmp", JobId, ct);
            File.Move(ReadyPath + ".tmp", ReadyPath);
            // Only process termination releases this gate; graceful cleanup cannot establish evidence.
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
    }

    private sealed class EtlPassThroughCanonicalizer : IEventCanonicalizer
    {
        public MarketEvent Canonicalize(MarketEvent raw, CancellationToken ct = default) => raw;
    }

    private sealed class CrashGateParser(IPartnerFileParser inner, EtlCrashGate gate) : IPartnerFileParser
    {
        public string SchemaId => inner.SchemaId;
        public bool CanParse(EtlStagedFile file) => inner.CanParse(file);
        public async IAsyncEnumerable<PartnerRecordEnvelope> ParseAsync(EtlStagedFile file,
            EtlCheckpointToken? checkpoint, string? schemaId = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await gate.PauseAtAsync("staged", ct);
            await foreach (var record in inner.ParseAsync(file, checkpoint, schemaId, ct))
                yield return record;
        }
    }

    private sealed class CrashGatePipeline(EventPipeline inner, EtlCrashGate gate) : IEtlEventPipeline
    {
        public long DeduplicatedCount => inner.DeduplicatedCount;
        public ValueTask PublishAsync(MarketEvent evt, CancellationToken ct = default) => inner.PublishAsync(evt, ct);
        public async Task FlushAsync(CancellationToken ct = default)
        {
            await inner.FlushAsync(ct);
            await gate.PauseAtAsync("flushed", ct);
        }
    }

    private sealed class CrashGateExport(string root, EtlCrashGate gate) : IEtlExportService
    {
        public async Task<EtlExportResult> ExportAsync(IngestionJob job, EtlJobDefinition definition, CancellationToken ct = default)
        {
            await gate.PauseAtAsync("catalog", ct);
            var path = Path.Combine(root, "export.csv");
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes("symbol\nAAPL\n"), ct);
                await stream.FlushAsync(ct);
                stream.Flush(flushToDisk: true);
            }
            await gate.PauseAtAsync("export-written", ct);
            return new EtlExportResult { Success = true, ArtifactPaths = [path] };
        }
    }
}
