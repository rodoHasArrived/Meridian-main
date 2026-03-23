using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Application.Canonicalization;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Etl;
using Meridian.Contracts.Pipeline;
using Meridian.Domain.Events;
using Meridian.Storage;
using Meridian.Storage.Etl;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Packaging;
using Serilog;

namespace Meridian.Application.Etl;

public sealed class EtlJobDefinitionStore : IEtlJobDefinitionStore
{
    private readonly string _rootPath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public EtlJobDefinitionStore(string dataRoot)
    {
        _rootPath = Path.Combine(dataRoot, "_etl", "jobs");
        Directory.CreateDirectory(_rootPath);
    }

    public async Task SaveAsync(EtlJobDefinition definition, CancellationToken ct = default)
    {
        var path = GetPath(definition.JobId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(definition, _jsonOptions);
        await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
    }

    public async Task<EtlJobDefinition?> GetAsync(string jobId, CancellationToken ct = default)
    {
        var path = GetPath(jobId);
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<EtlJobDefinition>(json, _jsonOptions);
    }

    public async Task<IReadOnlyList<EtlJobDefinition>> ListAsync(CancellationToken ct = default)
    {
        var list = new List<EtlJobDefinition>();
        foreach (var file in Directory.EnumerateFiles(_rootPath, "etl_*.json"))
        {
            var json = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
            var item = JsonSerializer.Deserialize<EtlJobDefinition>(json, _jsonOptions);
            if (item is not null)
                list.Add(item);
        }

        return list;
    }

    public Task DeleteAsync(string jobId, CancellationToken ct = default)
    {
        var path = GetPath(jobId);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetPath(string jobId) => Path.Combine(_rootPath, $"etl_{jobId}.json");
}

public sealed class PartnerSchemaRegistry : IPartnerSchemaRegistry
{
    private static readonly IReadOnlyDictionary<string, CsvSchemaDefinition> Schemas =
        new Dictionary<string, CsvSchemaDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["partner.trades.csv.v1"] = new()
            {
                SchemaId = "partner.trades.csv.v1",
                HasHeaderRow = true,
                Delimiter = ',',
                Columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["timestamp"] = "timestamp",
                    ["symbol"] = "symbol",
                    ["price"] = "price",
                    ["size"] = "size",
                    ["venue"] = "venue",
                    ["sequence"] = "sequence",
                    ["aggressor"] = "aggressor"
                }
            }
        };

    public CsvSchemaDefinition GetCsvSchema(string schemaId)
        => Schemas.TryGetValue(schemaId, out var schema)
            ? schema
            : throw new InvalidOperationException($"Unsupported ETL schema '{schemaId}'.");

    public bool IsSupported(string schemaId) => Schemas.ContainsKey(schemaId);
}

public sealed class EtlNormalizationService
{
    private readonly IEventCanonicalizer _canonicalizer;

    public EtlNormalizationService(IEventCanonicalizer canonicalizer)
    {
        _canonicalizer = canonicalizer;
    }

    public ValueTask<NormalizationOutcome> NormalizeAsync(EtlJobDefinition definition, PartnerRecordEnvelope record, CancellationToken ct = default)
    {
        if (!string.Equals(definition.PartnerSchemaId, "partner.trades.csv.v1", StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(new NormalizationOutcome
            {
                Disposition = EtlRecordDisposition.Rejected,
                RejectCode = "unsupported_schema",
                RejectMessage = $"Unsupported schema '{definition.PartnerSchemaId}'."
            });
        }

        try
        {
            if (!record.Fields.TryGetValue("timestamp", out var timestampRaw) ||
                !DateTimeOffset.TryParse(timestampRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
            {
                return ValueTask.FromResult(Reject("invalid_timestamp", "Missing or invalid timestamp."));
            }

            if (!record.Fields.TryGetValue("symbol", out var symbol) || string.IsNullOrWhiteSpace(symbol))
                return ValueTask.FromResult(Reject("missing_symbol", "Symbol is required."));

            if (!record.Fields.TryGetValue("price", out var priceRaw) || !decimal.TryParse(priceRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
                return ValueTask.FromResult(Reject("invalid_price", "Price is required and must be numeric."));

            if (!record.Fields.TryGetValue("size", out var sizeRaw) || !long.TryParse(sizeRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
                return ValueTask.FromResult(Reject("invalid_size", "Size is required and must be numeric."));

            var venue = record.Fields.TryGetValue("venue", out var venueRaw) ? venueRaw : null;
            var aggressor = ParseAggressor(record.Fields.TryGetValue("aggressor", out var aggressorRaw) ? aggressorRaw : null);
            var seq = record.Fields.TryGetValue("sequence", out var seqRaw) && long.TryParse(seqRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSeq)
                ? parsedSeq
                : record.RecordIndex;

            var trade = new Trade(timestamp, symbol!, price, size, aggressor, seq, record.SourceFileName, venue);
            var evt = MarketEvent.Trade(timestamp, symbol!, trade, seq, definition.LogicalSourceName).StampReceiveTime(timestamp);
            evt = _canonicalizer.Canonicalize(evt, ct);

            return ValueTask.FromResult(new NormalizationOutcome
            {
                Disposition = EtlRecordDisposition.Accepted,
                Event = evt,
                RecordHash = ComputeRecordHash(record.SourceFileChecksum, record.RecordIndex)
            });
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Reject("normalization_error", ex.Message));
        }
    }

    private static NormalizationOutcome Reject(string code, string message) => new()
    {
        Disposition = EtlRecordDisposition.Rejected,
        RejectCode = code,
        RejectMessage = message
    };

    private static AggressorSide ParseAggressor(string? value)
        => value?.Trim().ToUpperInvariant() switch
        {
            "BUY" or "B" => AggressorSide.Buy,
            "SELL" or "S" => AggressorSide.Sell,
            _ => AggressorSide.Unknown
        };

    private static string ComputeRecordHash(string checksum, long index)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{checksum}:{index}")))[..24];
}

public sealed class EtlExportService : IEtlExportService
{
    private readonly string _dataRoot;
    private readonly ILogger _log = LoggingSetup.ForContext<EtlExportService>();
    private readonly IEnumerable<Infrastructure.Etl.ISftpFilePublisher> _publishers;

    public EtlExportService(string dataRoot, IEnumerable<Infrastructure.Etl.ISftpFilePublisher> publishers)
    {
        _dataRoot = dataRoot;
        _publishers = publishers;
    }

    public async Task<EtlExportResult> ExportAsync(IngestionJob job, EtlJobDefinition definition, CancellationToken ct = default)
    {
        var artifacts = new List<string>();
        PackageResult? packageResult = null;

        if (definition.PublishPortablePackage)
        {
            var packager = new PortableDataPackager(_dataRoot);
            var outputDirectory = Path.Combine(_dataRoot, "_etl", "exports", job.JobId, "packages");
            Directory.CreateDirectory(outputDirectory);
            packageResult = await packager.CreatePackageAsync(new PackageOptions
            {
                Name = $"etl-{job.JobId}",
                OutputDirectory = outputDirectory,
                Symbols = definition.Symbols.Length > 0 ? definition.Symbols : null,
                EventTypes = definition.EventTypes.Length > 0 ? definition.EventTypes : null,
                StartDate = definition.FromDateUtc,
                EndDate = definition.ToDateUtc,
                Format = definition.Destination.PackageFormat == EtlPackageFormat.TarGz ? PackageFormat.TarGz : PackageFormat.Zip
            }, ct).ConfigureAwait(false);

            if (!packageResult.Success)
            {
                return new EtlExportResult { Success = false, Error = packageResult.Error, PackageResult = packageResult };
            }

            if (!string.IsNullOrWhiteSpace(packageResult.PackagePath))
                artifacts.Add(packageResult.PackagePath);
        }

        if (definition.PublishNormalizedExtract)
        {
            var exportRoot = Path.Combine(_dataRoot, "_etl", "exports", job.JobId, "normalized");
            await CopyMatchingStorageFilesAsync(exportRoot, definition, ct).ConfigureAwait(false);
            artifacts.Add(exportRoot);
        }

        if (definition.Destination.Kind == EtlDestinationKind.Local && !string.IsNullOrWhiteSpace(definition.Destination.Location))
        {
            foreach (var artifact in artifacts.ToArray())
            {
                var targetPath = Path.Combine(definition.Destination.Location!, Path.GetFileName(artifact));
                if (Directory.Exists(artifact))
                {
                    var localRoot = targetPath;
                    Directory.CreateDirectory(localRoot);
                    foreach (var file in Directory.EnumerateFiles(artifact, "*", SearchOption.AllDirectories))
                    {
                        var relative = Path.GetRelativePath(artifact, file);
                        var copyPath = Path.Combine(localRoot, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(copyPath)!);
                        File.Copy(file, copyPath, overwrite: definition.Destination.OverwriteIfExists);
                    }
                }
                else
                {
                    Directory.CreateDirectory(definition.Destination.Location!);
                    File.Copy(artifact, targetPath, overwrite: definition.Destination.OverwriteIfExists);
                }
            }
        }

        if (definition.Destination.Kind == EtlDestinationKind.Sftp)
        {
            var publisher = _publishers.FirstOrDefault();
            if (publisher is null)
                return new EtlExportResult { Success = false, Error = "No SFTP publisher is registered." };

            foreach (var artifact in artifacts)
            {
                await publisher.PublishAsync(definition.Destination, artifact, ct).ConfigureAwait(false);
            }
        }

        _log.Information("ETL export produced {ArtifactCount} artifacts for job {JobId}", artifacts.Count, job.JobId);
        return new EtlExportResult { Success = true, ArtifactPaths = artifacts.ToArray(), PackageResult = packageResult };
    }

    private async Task CopyMatchingStorageFilesAsync(string exportRoot, EtlJobDefinition definition, CancellationToken ct)
    {
        Directory.CreateDirectory(exportRoot);
        foreach (var file in Directory.EnumerateFiles(_dataRoot, "*.jsonl", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(_dataRoot, "*.jsonl.gz", SearchOption.AllDirectories))
                     .Where(path => !path.Contains(Path.DirectorySeparatorChar + "_etl" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                     .Where(path => Matches(path, definition)))
        {
            var relative = Path.GetRelativePath(_dataRoot, file);
            var destination = Path.Combine(exportRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            await using var destinationStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await source.CopyToAsync(destinationStream, ct).ConfigureAwait(false);
        }
    }

    private static bool Matches(string path, EtlJobDefinition definition)
    {
        if (definition.Symbols.Length > 0 && !definition.Symbols.Any(symbol => path.Contains(Path.DirectorySeparatorChar + symbol + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || path.Contains(symbol + "_", StringComparison.OrdinalIgnoreCase)))
            return false;

        if (definition.EventTypes.Length > 0 && !definition.EventTypes.Any(type => path.Contains(Path.DirectorySeparatorChar + type + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || path.Contains(type, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }
}

public sealed class EtlJobService : IEtlJobService
{
    private readonly IngestionJobService _ingestionJobService;
    private readonly IEtlJobDefinitionStore _definitionStore;
    private readonly EtlJobOrchestrator _orchestrator;

    public EtlJobService(IngestionJobService ingestionJobService, IEtlJobDefinitionStore definitionStore, EtlJobOrchestrator orchestrator)
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
    private readonly ILogger _log = LoggingSetup.ForContext<EtlJobOrchestrator>();
    private readonly IngestionJobService _ingestionJobService;
    private readonly IEtlJobDefinitionStore _definitionStore;
    private readonly IEnumerable<IEtlSourceReader> _sourceReaders;
    private readonly IPartnerFileParser _parser;
    private readonly EtlNormalizationService _normalizer;
    private readonly EventPipeline _pipeline;
    private readonly IStorageCatalogService _catalog;
    private readonly EtlAuditStore _auditStore;
    private readonly EtlRejectSink _rejectSink;
    private readonly IEtlExportService _exportService;

    public EtlJobOrchestrator(
        IngestionJobService ingestionJobService,
        IEtlJobDefinitionStore definitionStore,
        IEnumerable<IEtlSourceReader> sourceReaders,
        IPartnerFileParser parser,
        EtlNormalizationService normalizer,
        EventPipeline pipeline,
        IStorageCatalogService catalog,
        EtlAuditStore auditStore,
        EtlRejectSink rejectSink,
        IEtlExportService exportService)
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

                await foreach (var record in _parser.ParseAsync(staged, checkpoint, ct).ConfigureAwait(false))
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
            _log.Error(ex, "ETL job {JobId} failed", jobId);
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
