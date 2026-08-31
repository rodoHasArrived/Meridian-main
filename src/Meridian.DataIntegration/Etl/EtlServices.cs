using System.Text;
using System.Text.Json;
using Meridian.Contracts.Etl;
using Meridian.Contracts.Coordination;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
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

public sealed partial class EtlJobOrchestrator
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
    private readonly ILeaseManager? _leaseManager;
    private readonly IOperationalCaseHistoryStore? _caseHistoryStore;

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
        ILogger<EtlJobOrchestrator>? logger = null,
        IOperationalCaseHistoryStore? caseHistoryStore = null,
        ILeaseManager? leaseManager = null)
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
        _caseHistoryStore = caseHistoryStore;
        _leaseManager = leaseManager;
        _logger = logger ?? NullLogger<EtlJobOrchestrator>.Instance;
    }

    public async Task<EtlRunResult> RunAsync(string jobId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ct.ThrowIfCancellationRequested();
        var startedAtUtc = DateTimeOffset.UtcNow;
        var operationId = $"etl:{jobId}:{Guid.NewGuid():N}";
        var inputHash = ComputeJobInputHash(jobId);
        EtlJobDefinition? definition = null;
        IngestionJob? job = null;
        IEtlSourceReader? reader = null;
        var admitted = false;
        var leaseResource = $"jobs/etl/{jobId}";
        EtlOwnershipLease? ownership = null;
        var filesProcessed = 0;
        long processed = 0, accepted = 0, rejected = 0;
        var errors = new List<string>();
        var filesReadyForPostProcessing = new List<EtlRemoteFile>();
        EtlExportResult? exportResult = null;
        IReadOnlyList<OperationArtifactReference> verifiedArtifacts = [];
        var dedupBefore = 0L;

        try
        {
            job = _ingestionJobService.GetJob(jobId)
                ?? throw new EtlOperationBlockedException($"Ingestion job '{jobId}' was not found.");
            definition = await _definitionStore.GetAsync(jobId, ct).ConfigureAwait(false)
                ?? throw new EtlOperationBlockedException($"ETL definition for job '{jobId}' was not found.");
            inputHash = ComputeInputHash(definition);

            if (definition.Source.Kind != EtlSourceKind.Local && definition.Source.Kind != EtlSourceKind.Sftp)
                throw new EtlOperationBlockedException($"Unsupported ETL source kind '{definition.Source.Kind}'.");
            if (definition.Destination.TransferMode == EtlTransferMode.ScheduledDelivery)
                throw new EtlOperationBlockedException("Scheduled delivery mode is reserved for a future ETL version.");

            reader = _sourceReaders.FirstOrDefault(x => x.Kind == definition.Source.Kind)
                ?? throw new EtlOperationBlockedException($"No ETL source reader is registered for kind '{definition.Source.Kind}'.");

            if (_leaseManager is not null)
            {
                var acquired = await _leaseManager.TryAcquireAsync(leaseResource, ct).ConfigureAwait(false);
                if (!acquired.Acquired)
                {
                    throw new EtlOperationBlockedException(
                        $"ETL job '{jobId}' is owned by another runner ({acquired.CurrentOwner ?? "unknown"}).");
                }

                ownership = new EtlOwnershipLease(_leaseManager, leaseResource);
            }

            var runningRetained = await _ingestionJobService
                .TransitionAsync(jobId, IngestionJobState.Running, ct: ct)
                .ConfigureAwait(false);
            if (!runningRetained)
                throw new EtlOperationBlockedException($"Ingestion job '{jobId}' could not transition to Running.");
            admitted = true;

            await _auditStore.WriteEventAsync(
                jobId,
                new EtlAuditEvent { Stage = "start", Message = "ETL job started." },
                ct).ConfigureAwait(false);

            var checkpoint = await _auditStore.LoadCheckpointAsync(jobId, ct).ConfigureAwait(false);
            var files = definition.FlowDirection == EtlFlowDirection.Export
                ? Array.Empty<EtlRemoteFile>()
                : await reader.ListFilesAsync(definition.Source, ct).ConfigureAwait(false);
            dedupBefore = _pipeline.DeduplicatedCount;

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
                filesReadyForPostProcessing.Add(file);
            }

            await _pipeline.FlushAsync(ct).ConfigureAwait(false);
            await EnsureOwnershipAsync(leaseResource, ct).ConfigureAwait(false);
            var catalogResult = await _catalog.RebuildCatalogAsync(
                new CatalogRebuildOptions { Recursive = true },
                ct: ct).ConfigureAwait(false);
            if (!catalogResult.Success)
            {
                throw new InvalidOperationException(
                    catalogResult.Errors.FirstOrDefault() ?? "Storage catalog rebuild failed.");
            }

            var deliveryConfigured = IsDeliveryConfigured(definition);
            var exportSucceeded = true;
            if (deliveryConfigured)
            {
                await EnsureOwnershipAsync(leaseResource, ct).ConfigureAwait(false);
                exportResult = await _exportService.ExportAsync(job, definition, ct).ConfigureAwait(false);
                exportSucceeded = exportResult.Success;
                verifiedArtifacts = await BuildVerifiedArtifactReferencesAsync(
                        definition,
                        exportResult,
                        requireArtifacts: exportSucceeded,
                        ct)
                    .ConfigureAwait(false);
            }

            if (!exportSucceeded)
            {
                var exportError = exportResult?.Error ?? "ETL export failed without an error description.";
                var failOpenOptionalDelivery = definition.FlowDirection == EtlFlowDirection.RoundTrip &&
                                               !definition.FailRoundTripOnExportError;
                var auditPath = _auditStore.GetAuditPath(jobId, "events.jsonl");
                var artifactIds = verifiedArtifacts.Select(artifact => artifact.ArtifactId).ToArray();

                if (failOpenOptionalDelivery)
                {
                    var warningMessage = $"Optional round-trip delivery failed: {exportError} Source files were retained for retry.";
                    var completedRetained = await _ingestionJobService
                        .TransitionAsync(jobId, IngestionJobState.Completed, ct: CancellationToken.None)
                        .ConfigureAwait(false);
                    if (!completedRetained)
                    {
                        throw new InvalidOperationException(
                            $"Ingestion job '{jobId}' rejected the Completed terminal transition for its warning outcome.");
                    }

                    await _auditStore.WriteEventAsync(jobId, new EtlAuditEvent
                    {
                        Stage = "completed-with-warnings",
                        Message = warningMessage
                    }, CancellationToken.None).ConfigureAwait(false);
                    var warningCompletedAtUtc = DateTimeOffset.UtcNow;
                    var warningAuditEvidence = AuditEvidence(operationId, jobId, auditPath, warningCompletedAtUtc);
                    var outcome = Validate(new VerifiedOperationOutcome(
                        operationId,
                        "etl.run",
                        OperationTerminalState.CompletedWithWarnings,
                        startedAtUtc,
                        warningCompletedAtUtc,
                        1,
                        jobId,
                        inputHash,
                        [
                            Postcondition("records-published", "Accepted records were flushed through the event pipeline.", OperationPostconditionState.Satisfied, evidenceIds: [warningAuditEvidence.EvidenceId]),
                            Postcondition("catalog-rebuilt", "The storage catalog rebuild completed successfully.", OperationPostconditionState.Satisfied, evidenceIds: [warningAuditEvidence.EvidenceId]),
                            Postcondition("required-delivery", "No required delivery remained incomplete; the failed round-trip delivery was explicitly configured fail-open.", OperationPostconditionState.Satisfied, evidenceIds: [warningAuditEvidence.EvidenceId]),
                            Postcondition("optional-delivery", "The optional export delivery completed.", OperationPostconditionState.NotSatisfied, required: false, evidenceIds: [warningAuditEvidence.EvidenceId]) with { ArtifactIds = artifactIds },
                            Postcondition("source-retained", "Source files were retained for delivery retry.", OperationPostconditionState.Satisfied, evidenceIds: [warningAuditEvidence.EvidenceId])
                        ],
                        [warningAuditEvidence],
                        verifiedArtifacts,
                        [new OperationIssue("optional-delivery-failed", exportError, OperationIssueSeverity.Warning, EvidenceId: warningAuditEvidence.EvidenceId)],
                        [new OperationRecoveryAction(
                            "retry-optional-delivery",
                            "Retry optional delivery",
                            $"Correct the export destination and resume ETL job {jobId}; retained source data will be reused.",
                            Retryable: true,
                            RequiresHumanAction: true,
                            Route: $"etl://jobs/{jobId}")
                        {
                            EvidenceIds = [warningAuditEvidence.EvidenceId],
                            ArtifactIds = artifactIds
                        }]));
                    outcome = await RetainTerminalOutcomeAsync(
                            jobId,
                            outcome,
                            includeCaseHistory: true,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    return CreateResult(
                        outcome,
                        filesProcessed,
                        processed,
                        accepted,
                        rejected,
                        _pipeline.DeduplicatedCount - dedupBefore,
                        exportResult,
                        warnings: [exportError]);
                }

                var failureMessage = $"Required ETL export failed: {exportError} Source files were retained for retry.";
                var failedRetained = await _ingestionJobService
                    .TransitionAsync(jobId, IngestionJobState.Failed, failureMessage, CancellationToken.None)
                    .ConfigureAwait(false);
                if (!failedRetained)
                {
                    throw new InvalidOperationException(
                        $"Ingestion job '{jobId}' rejected the Failed terminal transition after required export failure.");
                }

                await _auditStore.WriteEventAsync(jobId, new EtlAuditEvent
                {
                    Stage = "failed",
                    Message = failureMessage
                }, CancellationToken.None).ConfigureAwait(false);
                var failureCompletedAtUtc = DateTimeOffset.UtcNow;
                var failureAuditEvidence = AuditEvidence(operationId, jobId, auditPath, failureCompletedAtUtc);
                var failedOutcome = Validate(new VerifiedOperationOutcome(
                    operationId,
                    "etl.run",
                    OperationTerminalState.Failed,
                    startedAtUtc,
                    failureCompletedAtUtc,
                    1,
                    jobId,
                    inputHash,
                    [
                        Postcondition("records-published", "Accepted records were flushed through the event pipeline.", OperationPostconditionState.Satisfied, evidenceIds: [failureAuditEvidence.EvidenceId]),
                        Postcondition("catalog-rebuilt", "The storage catalog rebuild completed successfully.", OperationPostconditionState.Satisfied, evidenceIds: [failureAuditEvidence.EvidenceId]),
                        Postcondition("required-delivery", "The required ETL export delivery completed.", OperationPostconditionState.NotSatisfied, evidenceIds: [failureAuditEvidence.EvidenceId]) with { ArtifactIds = artifactIds },
                        Postcondition("source-retained", "Source files were retained for export retry.", OperationPostconditionState.Satisfied, evidenceIds: [failureAuditEvidence.EvidenceId])
                    ],
                    [failureAuditEvidence],
                    verifiedArtifacts,
                    [new OperationIssue("required-delivery-failed", exportError, OperationIssueSeverity.Error, EvidenceId: failureAuditEvidence.EvidenceId)],
                    [new OperationRecoveryAction(
                        "repair-and-retry-export",
                        "Repair and retry export",
                        $"Correct the export destination and resume ETL job {jobId}; do not delete the retained source before the retry succeeds.",
                        Retryable: true,
                        RequiresHumanAction: true,
                        Route: $"etl://jobs/{jobId}")
                    {
                        EvidenceIds = [failureAuditEvidence.EvidenceId],
                        ArtifactIds = artifactIds
                    }]));
                failedOutcome = await RetainTerminalOutcomeAsync(
                        jobId,
                        failedOutcome,
                        includeCaseHistory: true,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                return CreateResult(
                    failedOutcome,
                    filesProcessed,
                    processed,
                    accepted,
                    rejected,
                    _pipeline.DeduplicatedCount - dedupBefore,
                    exportResult,
                    errors: [exportError]);
            }

            await EnsureOwnershipAsync(leaseResource, ct).ConfigureAwait(false);
            if (checkpoint is not null)
            {
                await PersistCheckpointAsync(jobId, checkpoint, ct).ConfigureAwait(false);
            }

            foreach (var file in filesReadyForPostProcessing)
            {
                await reader.PostProcessFileAsync(definition.Source, file, succeeded: true, ct).ConfigureAwait(false);
            }
            var completedStateRetained = await _ingestionJobService
                .TransitionAsync(jobId, IngestionJobState.Completed, ct: CancellationToken.None)
                .ConfigureAwait(false);
            if (!completedStateRetained)
            {
                throw new InvalidOperationException(
                    $"Ingestion job '{jobId}' rejected the Completed terminal transition.");
            }

            await _auditStore.WriteEventAsync(
                jobId,
                new EtlAuditEvent { Stage = "complete", Message = "ETL job completed." },
                CancellationToken.None).ConfigureAwait(false);
            var succeededAtUtc = DateTimeOffset.UtcNow;
            var succeededEvidence = AuditEvidence(operationId, jobId, _auditStore.GetAuditPath(jobId, "events.jsonl"), succeededAtUtc);
            var succeededOutcome = Validate(new VerifiedOperationOutcome(
                operationId,
                "etl.run",
                OperationTerminalState.Succeeded,
                startedAtUtc,
                succeededAtUtc,
                1,
                jobId,
                inputHash,
                [
                    Postcondition("records-published", "Accepted records were flushed through the event pipeline.", OperationPostconditionState.Satisfied, evidenceIds: [succeededEvidence.EvidenceId]),
                    Postcondition("catalog-rebuilt", "The storage catalog rebuild completed successfully.", OperationPostconditionState.Satisfied, evidenceIds: [succeededEvidence.EvidenceId]),
                    Postcondition("required-delivery", "Every configured ETL delivery completed successfully.", OperationPostconditionState.Satisfied, evidenceIds: [succeededEvidence.EvidenceId]) with
                    {
                        ArtifactIds = verifiedArtifacts.Select(artifact => artifact.ArtifactId).ToArray()
                    },
                    Postcondition("source-post-processing", "Processed source files completed their configured success action.", OperationPostconditionState.Satisfied, evidenceIds: [succeededEvidence.EvidenceId])
                ],
                [succeededEvidence],
                verifiedArtifacts,
                [],
                []));
            succeededOutcome = await RetainTerminalOutcomeAsync(
                    jobId,
                    succeededOutcome,
                    includeCaseHistory: true,
                    CancellationToken.None)
                .ConfigureAwait(false);
            return CreateResult(
                succeededOutcome,
                filesProcessed,
                processed,
                accepted,
                rejected,
                _pipeline.DeduplicatedCount - dedupBefore,
                exportResult);
        }
        catch (OperationCanceledException) when (!admitted)
        {
            throw;
        }
        catch (Exception ex)
        {
            var cancelledAfterAdmission = ex is OperationCanceledException;
            var primaryFailureMessage = cancelledAfterAdmission
                ? $"ETL job '{jobId}' was cancelled after admission and did not complete all required stages."
                : ex.Message;
            errors.Add(primaryFailureMessage);
            if (cancelledAfterAdmission)
            {
                _logger.LogWarning(
                    "ETL job {JobId} was cancelled after admission; terminalizing it as Failed",
                    jobId);
            }
            else
            {
                _logger.LogError(ex, "ETL job {JobId} failed", jobId);
            }

            var terminalizationFailures = new List<string>();
            if (admitted && job is not null)
            {
                try
                {
                    var failedRetained = await _ingestionJobService
                        .TransitionAsync(jobId, IngestionJobState.Failed, primaryFailureMessage, CancellationToken.None)
                        .ConfigureAwait(false);
                    if (!failedRetained)
                        terminalizationFailures.Add("The ingestion job coordinator rejected the Failed transition.");
                }
                catch (Exception transitionException)
                {
                    terminalizationFailures.Add($"Failed-state persistence failed: {transitionException.Message}");
                    _logger.LogError(transitionException, "ETL job {JobId} failed-state persistence failed", jobId);
                }
            }

            var failureMessage = $"{primaryFailureMessage} Source files not successfully post-processed remain available for retry.";
            var failureAuditRetained = false;
            try
            {
                await _auditStore.WriteEventAsync(
                    jobId,
                    new EtlAuditEvent { Stage = "failed", Message = failureMessage },
                    CancellationToken.None).ConfigureAwait(false);
                failureAuditRetained = true;
            }
            catch (Exception auditException)
            {
                terminalizationFailures.Add($"Failure-audit persistence failed: {auditException.Message}");
                _logger.LogError(auditException, "ETL job {JobId} failure-audit persistence failed", jobId);
            }

            errors.AddRange(terminalizationFailures);
            var failedAtUtc = DateTimeOffset.UtcNow;
            var fallbackDescription = terminalizationFailures.Count == 0
                ? "The ETL failure was terminalized and recovery guidance was produced."
                : $"The ETL receipt was returned with {terminalizationFailures.Count} secondary persistence failure(s).";
            var fallbackHash = ComputeTextHash(
                $"{operationId}\netl-terminalization\n{fallbackDescription}\n{failedAtUtc:O}");
            var fallbackEvidence = new OperationEvidenceReference(
                $"{operationId}:terminalization",
                "etl-terminalization",
                fallbackDescription,
                Uri: $"urn:sha256:{fallbackHash}",
                ContentHashSha256: fallbackHash,
                CapturedAtUtc: failedAtUtc);
            var failureEvidence = new List<OperationEvidenceReference> { fallbackEvidence };
            if (failureAuditRetained)
            {
                failureEvidence.Add(AuditEvidence(
                    operationId,
                    jobId,
                    _auditStore.GetAuditPath(jobId, "events.jsonl"),
                    failedAtUtc));
            }

            var failureEvidenceIds = failureEvidence.Select(item => item.EvidenceId).ToArray();
            var verifiedArtifactIds = verifiedArtifacts
                .Select(item => item.ArtifactId)
                .ToArray();
            var blocked = ex is EtlOperationBlockedException;
            var failureIssues = new List<OperationIssue>
            {
                new(
                    blocked
                        ? "etl-run-blocked"
                        : cancelledAfterAdmission
                            ? "etl-run-cancelled-after-admission"
                            : "etl-run-failed",
                    primaryFailureMessage,
                    OperationIssueSeverity.Error,
                    ex.GetType().FullName,
                    fallbackEvidence.EvidenceId)
                {
                    IsBlocking = blocked
                }
            };
            failureIssues.AddRange(terminalizationFailures.Select((message, index) => new OperationIssue(
                $"etl-terminalization-warning-{index + 1}",
                message,
                OperationIssueSeverity.Warning,
                EvidenceId: fallbackEvidence.EvidenceId)));
            var failedOutcome = Validate(new VerifiedOperationOutcome(
                operationId,
                "etl.run",
                blocked ? OperationTerminalState.Blocked : OperationTerminalState.Failed,
                startedAtUtc,
                failedAtUtc,
                1,
                jobId,
                inputHash,
                [Postcondition("etl-completed", "The ETL run completed all required stages.", OperationPostconditionState.NotSatisfied, evidenceIds: failureEvidenceIds) with
                {
                    ArtifactIds = verifiedArtifactIds
                }],
                failureEvidence,
                verifiedArtifacts,
                failureIssues,
                [new OperationRecoveryAction(
                    blocked ? "unblock-and-resume-etl" : "repair-and-resume-etl",
                    blocked ? "Unblock and resume ETL" : "Repair and resume ETL",
                    terminalizationFailures.Count == 0
                        ? $"Correct the recorded failure and resume ETL job {jobId} from its retained checkpoint and source evidence."
                        : $"Repair ETL state/audit persistence, correct the recorded failure, and resume job {jobId}; verify source and checkpoint retention first.",
                    Retryable: true,
                    RequiresHumanAction: true,
                    Route: $"etl://jobs/{jobId}")
                {
                    EvidenceIds = failureEvidenceIds,
                    ArtifactIds = verifiedArtifactIds
                }]));
            failedOutcome = await RetainFailureOutcomeAsync(
                    jobId,
                    failedOutcome,
                    includeCaseHistory: ex is not EtlTerminalOutcomePersistenceException
                    {
                        CaseHistoryFailed: true
                    },
                    errors)
                .ConfigureAwait(false);
            return CreateResult(
                failedOutcome,
                filesProcessed,
                processed,
                accepted,
                rejected,
                _pipeline.DeduplicatedCount - dedupBefore,
                exportResult,
                errors: errors.ToArray());
        }
        finally
        {
            if (ownership is not null)
            {
                await ownership.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task EnsureOwnershipAsync(string resourceId, CancellationToken ct)
    {
        if (_leaseManager is null)
        {
            return;
        }

        if (!_leaseManager.HoldsLease(resourceId) ||
            !await _leaseManager.RenewAsync(resourceId, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"ETL ownership lease '{resourceId}' was lost before durable commit.");
        }
    }

    private static EtlRunResult CreateResult(
        VerifiedOperationOutcome outcome,
        int filesProcessed,
        long recordsProcessed,
        long recordsAccepted,
        long recordsRejected,
        long recordsDeduplicated,
        EtlExportResult? exportResult = null,
        string[]? errors = null,
        string[]? warnings = null) => new()
        {
            Outcome = outcome,
            FilesProcessed = filesProcessed,
            RecordsProcessed = recordsProcessed,
            RecordsAccepted = recordsAccepted,
            RecordsRejected = recordsRejected,
            RecordsDeduplicated = recordsDeduplicated,
            Errors = errors ?? [],
            Warnings = warnings ?? [],
            ExportResult = exportResult
        };

    private static OperationPostcondition Postcondition(
        string code,
        string description,
        OperationPostconditionState state,
        bool required = true,
        IReadOnlyList<string>? evidenceIds = null) =>
        new(code, description, state, required, EvidenceIds: evidenceIds ?? []);

    private static OperationEvidenceReference AuditEvidence(
        string operationId,
        string jobId,
        string auditPath,
        DateTimeOffset capturedAtUtc) =>
        new(
            $"{operationId}:audit",
            "etl-audit-log",
            $"Append-only ETL audit events for job {jobId}.",
            Uri: new Uri(Path.GetFullPath(auditPath)).AbsoluteUri,
            CapturedAtUtc: capturedAtUtc);

    private static VerifiedOperationOutcome Validate(VerifiedOperationOutcome outcome) =>
        VerifiedOperationOutcomeValidator.ValidateAndThrow(outcome);

    internal static string ComputeInputHash(EtlJobDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var canonicalBytes = JsonSerializer.SerializeToUtf8Bytes(
            definition,
            EtlOperationJsonContext.Default.EtlJobDefinition);
        return Sha256Digest.Compute(canonicalBytes);
    }

    private static string ComputeJobInputHash(string jobId) =>
        Sha256Digest.ComputeUtf8(jobId);

    private static string ComputeTextHash(string value) =>
        Sha256Digest.ComputeUtf8(value);

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

    private sealed class EtlOwnershipLease(ILeaseManager? manager, string resourceId) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            if (manager is not null)
            {
                await manager.ReleaseAsync(resourceId, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private sealed class EtlOperationBlockedException(string message) : InvalidOperationException(message);
}
