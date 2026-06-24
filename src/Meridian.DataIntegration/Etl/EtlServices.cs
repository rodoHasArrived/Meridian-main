using Meridian.Contracts.Etl;
using Meridian.Contracts.Pipeline;
using Meridian.Storage.Etl;
using Meridian.Storage.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.DataIntegration.Etl;

public sealed class EtlJobService : IEtlJobService
{
    private readonly IEtlIngestionJobCoordinator _ingestionJobService;
    private readonly IEtlJobDefinitionStore _definitionStore;
    private readonly EtlJobOrchestrator _orchestrator;

    public EtlJobService(IEtlIngestionJobCoordinator ingestionJobService, IEtlJobDefinitionStore definitionStore, EtlJobOrchestrator orchestrator)
    {
        _ingestionJobService = ingestionJobService;
        _definitionStore = definitionStore;
        _orchestrator = orchestrator;
    }

    public async Task<IngestionJob> CreateJobAsync(EtlJobDefinition definition, CancellationToken ct = default)
    {
        var workloadType = definition.FlowDirection switch
        {
            EtlFlowDirection.Import => IngestionWorkloadType.Historical,
            EtlFlowDirection.Export => IngestionWorkloadType.ScheduledBackfill,
            _ => IngestionWorkloadType.GapFill
        };

        var job = await _ingestionJobService.CreateJobAsync(
            workloadType,
            definition.Symbols.Length > 0 ? definition.Symbols : [definition.LogicalSourceName],
            definition.LogicalSourceName,
            definition.FromDateUtc,
            definition.ToDateUtc,
            ct: ct).ConfigureAwait(false);

        var persistedDefinition = new EtlJobDefinition
        {
            JobId = job.JobId,
            FlowDirection = definition.FlowDirection,
            PartnerSchemaId = definition.PartnerSchemaId,
            LogicalSourceName = definition.LogicalSourceName,
            Source = definition.Source,
            Destination = definition.Destination,
            Symbols = definition.Symbols,
            EventTypes = definition.EventTypes,
            FromDateUtc = definition.FromDateUtc,
            ToDateUtc = definition.ToDateUtc,
            PublishPortablePackage = definition.PublishPortablePackage,
            PublishNormalizedExtract = definition.PublishNormalizedExtract,
            ContinueOnRecordError = definition.ContinueOnRecordError,
            ValidateChecksums = definition.ValidateChecksums,
            FailRoundTripOnExportError = definition.FailRoundTripOnExportError,
            CheckpointEveryRecords = definition.CheckpointEveryRecords,
            RejectSampleLimit = definition.RejectSampleLimit,
            CreatedBy = definition.CreatedBy,
            CreatedAtUtc = definition.CreatedAtUtc
        };
        await _definitionStore.SaveAsync(persistedDefinition, ct).ConfigureAwait(false);
        await _ingestionJobService.TransitionAsync(job.JobId, IngestionJobState.Queued, ct: ct).ConfigureAwait(false);
        return job;
    }

    public Task<EtlJobDefinition?> GetDefinitionAsync(string jobId, CancellationToken ct = default)
        => _definitionStore.GetAsync(jobId, ct);

    public Task<EtlRunResult> RunAsync(string jobId, CancellationToken ct = default)
        => _orchestrator.RunAsync(jobId, ct);
}

public sealed class EtlJobOrchestrator
{
    private readonly ILogger<EtlJobOrchestrator> _logger;
    private readonly IEtlIngestionJobCoordinator _ingestionJobService;
    private readonly IEtlJobDefinitionStore _definitionStore;
    private readonly IEnumerable<IEtlSourceReader> _sourceReaders;
    private readonly IPartnerFileParser _parser;
    private readonly EtlNormalizationService _normalizer;
    private readonly IEtlEventPipeline _pipeline;
    private readonly IStorageCatalogService _catalog;
    private readonly EtlAuditStore _auditStore;
    private readonly EtlRejectSink _rejectSink;
    private readonly IEtlExportService _exportService;

    public EtlJobOrchestrator(
        IEtlIngestionJobCoordinator ingestionJobService,
        IEtlJobDefinitionStore definitionStore,
        IEnumerable<IEtlSourceReader> sourceReaders,
        IPartnerFileParser parser,
        EtlNormalizationService normalizer,
        IEtlEventPipeline pipeline,
        IStorageCatalogService catalog,
        EtlAuditStore auditStore,
        EtlRejectSink rejectSink,
        IEtlExportService exportService,
        ILogger<EtlJobOrchestrator>? logger = null)
    {
        _ingestionJobService = ingestionJobService;
        _definitionStore = definitionStore;
        _sourceReaders = sourceReaders;
        _parser = parser;
        _normalizer = normalizer;
        _pipeline = pipeline;
        _catalog = catalog;
        _auditStore = auditStore;
        _rejectSink = rejectSink;
        _exportService = exportService;
        _logger = logger ?? NullLogger<EtlJobOrchestrator>.Instance;
    }

    public async Task<EtlRunResult> RunAsync(string jobId, CancellationToken ct = default)
    {
        var definition = await _definitionStore.GetAsync(jobId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"ETL definition for job '{jobId}' was not found.");
        var job = _ingestionJobService.GetJob(jobId)
            ?? throw new InvalidOperationException($"Ingestion job '{jobId}' was not found.");

        if (definition.Source.Kind != EtlSourceKind.Local && definition.Source.Kind != EtlSourceKind.Sftp)
            throw new InvalidOperationException($"Unsupported ETL source kind '{definition.Source.Kind}'.");
        if (definition.Destination.TransferMode == EtlTransferMode.ScheduledDelivery)
            throw new InvalidOperationException("Scheduled delivery mode is reserved for a future ETL version.");

        var reader = _sourceReaders.FirstOrDefault(x => x.Kind == definition.Source.Kind)
            ?? throw new InvalidOperationException($"No ETL source reader is registered for kind '{definition.Source.Kind}'.");

        await _ingestionJobService.TransitionAsync(jobId, IngestionJobState.Running, ct: ct).ConfigureAwait(false);
        await _auditStore.WriteEventAsync(jobId, new EtlAuditEvent { Stage = "start", Message = "ETL job started." }, ct).ConfigureAwait(false);

        var checkpoint = await _auditStore.LoadCheckpointAsync(jobId, ct).ConfigureAwait(false);
        var files = definition.FlowDirection == EtlFlowDirection.Export
            ? Array.Empty<EtlRemoteFile>()
            : await reader.ListFilesAsync(definition.Source, ct).ConfigureAwait(false);
        var filesProcessed = 0;
        long processed = 0, accepted = 0, rejected = 0;
        var errors = new List<string>();
        var dedupBefore = _pipeline.DeduplicatedCount;

        try
        {
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var staged = await reader.StageFileAsync(jobId, definition.Source, file, ct).ConfigureAwait(false);
                await _auditStore.WriteEventAsync(jobId, new EtlAuditEvent { Stage = "staged", Message = $"Staged {file.Name}." }, ct).ConfigureAwait(false);

                await foreach (var record in _parser.ParseAsync(staged, checkpoint, definition.PartnerSchemaId, ct).ConfigureAwait(false))
                {
                    processed++;
                    var outcome = await _normalizer.NormalizeAsync(definition, record, ct).ConfigureAwait(false);
                    switch (outcome.Disposition)
                    {
                        case EtlRecordDisposition.Accepted when outcome.Event is not null:
                            await _pipeline.PublishAsync(outcome.Event, ct).ConfigureAwait(false);
                            accepted++;
                            checkpoint = new EtlCheckpointToken
                            {
                                CurrentFileName = staged.FileName,
                                CurrentFileChecksum = staged.ChecksumSha256,
                                CurrentRecordIndex = record.RecordIndex,
                                LastSymbol = outcome.Event.EffectiveSymbol,
                                LastTimestampUtc = outcome.Event.Timestamp.UtcDateTime,
                                LastRecordHash = outcome.RecordHash,
                                CapturedAtUtc = DateTime.UtcNow
                            };
                            if (processed % Math.Max(1, definition.CheckpointEveryRecords) == 0)
                            {
                                await PersistCheckpointAsync(jobId, checkpoint, ct).ConfigureAwait(false);
                            }
                            break;
                        case EtlRecordDisposition.Rejected:
                            rejected++;
                            await _rejectSink.AppendAsync(jobId, new EtlRejectRecord
                            {
                                SourceFileName = record.SourceFileName,
                                RecordIndex = record.RecordIndex,
                                RejectCode = outcome.RejectCode ?? "rejected",
                                RejectMessage = outcome.RejectMessage ?? "Rejected",
                                RawLine = record.RawLine
                            }, ct).ConfigureAwait(false);
                            if (!definition.ContinueOnRecordError)
                                throw new InvalidOperationException(outcome.RejectMessage ?? "Record rejected.");
                            break;
                    }
                }

                filesProcessed++;
                if (checkpoint is not null)
                {
                    checkpoint = new EtlCheckpointToken
                    {
                        CurrentFileName = staged.FileName,
                        CurrentFileChecksum = staged.ChecksumSha256,
                        CurrentRecordIndex = null,
                        LastSymbol = checkpoint.LastSymbol,
                        LastTimestampUtc = checkpoint.LastTimestampUtc,
                        LastRecordHash = checkpoint.LastRecordHash,
                        CapturedAtUtc = DateTime.UtcNow
                    };
                }
                if (definition.Source.DeleteAfterSuccess && definition.Source.Kind == EtlSourceKind.Local && File.Exists(file.Path))
                    File.Delete(file.Path);
            }

            await _pipeline.FlushAsync(ct).ConfigureAwait(false);
            if (checkpoint is not null)
                await PersistCheckpointAsync(jobId, checkpoint, ct).ConfigureAwait(false);
            await _catalog.RebuildCatalogAsync(new CatalogRebuildOptions { Recursive = true }, ct: ct).ConfigureAwait(false);

            EtlExportResult? exportResult = null;
            if (definition.PublishPortablePackage || definition.PublishNormalizedExtract || definition.Destination.Kind != EtlDestinationKind.StorageCatalog)
            {
                exportResult = await _exportService.ExportAsync(job, definition, ct).ConfigureAwait(false);
                if (!exportResult.Success && definition.FlowDirection == EtlFlowDirection.RoundTrip && definition.FailRoundTripOnExportError)
                    throw new InvalidOperationException(exportResult.Error ?? "ETL export failed.");
            }

            await _ingestionJobService.TransitionAsync(jobId, IngestionJobState.Completed, ct: ct).ConfigureAwait(false);
            await _auditStore.WriteEventAsync(jobId, new EtlAuditEvent { Stage = "complete", Message = "ETL job completed." }, ct).ConfigureAwait(false);
            return new EtlRunResult
            {
                Success = true,
                FilesProcessed = filesProcessed,
                RecordsProcessed = processed,
                RecordsAccepted = accepted,
                RecordsRejected = rejected,
                RecordsDeduplicated = _pipeline.DeduplicatedCount - dedupBefore,
                ExportResult = exportResult
            };
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            _logger.LogError(ex, "ETL job {JobId} failed", jobId);
            await _ingestionJobService.TransitionAsync(jobId, IngestionJobState.Failed, ex.Message, ct).ConfigureAwait(false);
            await _auditStore.WriteEventAsync(jobId, new EtlAuditEvent { Stage = "failed", Message = ex.Message }, ct).ConfigureAwait(false);
            return new EtlRunResult
            {
                Success = false,
                FilesProcessed = filesProcessed,
                RecordsProcessed = processed,
                RecordsAccepted = accepted,
                RecordsRejected = rejected,
                RecordsDeduplicated = _pipeline.DeduplicatedCount - dedupBefore,
                Errors = errors.ToArray()
            };
        }
    }

    private async Task PersistCheckpointAsync(string jobId, EtlCheckpointToken checkpoint, CancellationToken ct)
    {
        await _auditStore.SaveCheckpointAsync(jobId, checkpoint, ct).ConfigureAwait(false);
        await _ingestionJobService.UpdateCheckpointAsync(jobId, new IngestionCheckpointToken
        {
            LastSymbol = checkpoint.LastSymbol,
            LastDate = checkpoint.LastTimestampUtc,
            LastOffset = checkpoint.CurrentRecordIndex,
            CapturedAt = checkpoint.CapturedAtUtc
        }, ct).ConfigureAwait(false);
    }
}
