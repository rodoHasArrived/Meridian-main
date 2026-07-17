using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Reporting;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public sealed record ReportingRunStoreOptions(string RootDirectory);

public sealed record ReportingLegacyStateInventoryEntry(
    string RecordKind,
    string RecordId,
    string SourceSchemaVersion,
    string? TenantId,
    string? OrganizationId,
    string? CompanyId,
    string RawPayloadHashSha256,
    string CanonicalPayloadHashSha256,
    bool IsArchived,
    string Remediation);

public sealed record ReportingLegacyArchiveReceipt(
    string ArchiveId,
    string RecordKind,
    int RecordCount,
    string ActorPrincipalId,
    string Reason,
    DateTimeOffset ArchivedAtUtc,
    string ArchivedPayloadHashSha256);

/// <summary>
/// Raised when structurally valid pre-governance state is present but has not yet been explicitly
/// archived. Legacy state is distinguishable from corruption, but it is never promoted to current
/// authority or treated as an empty current store.
/// </summary>
public sealed class ReportingLegacyStateRequiresArchiveException : InvalidOperationException
{
    public ReportingLegacyStateRequiresArchiveException(
        string statePath,
        string recordKind,
        int recordCount)
        : base(
            $"Durable reporting {recordKind} state at '{statePath}' uses the read-only v1 format " +
            $"({recordCount} record(s)). Explicitly archive it, then freshly recertify or recapture " +
            "the records with current immutable governance bindings before reporting can continue.")
    {
        StatePath = statePath;
        RecordKind = recordKind;
        RecordCount = recordCount;
    }

    public string StatePath { get; }

    public string RecordKind { get; }

    public int RecordCount { get; }
}

public sealed class FileReportingRunStore : IReportingRunStore
{
    private const string SnapshotFileName = "reporting-runs.json";
    private const string SchemaVersion = "meridian.reporting.run-store.v2";
    private const string LegacySchemaVersion = "meridian.reporting.run-store.v1";
    private const string LegacyRunRemediation =
        "Read-only legacy run. Preserve for inventory, then freshly recertify from authoritative source data before current use.";

    private readonly ReportingRunStoreOptions _options;
    private readonly ILogger<FileReportingRunStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public FileReportingRunStore(
        ReportingRunStoreOptions options,
        ILogger<FileReportingRunStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.RootDirectory);
        Directory.CreateDirectory(_options.RootDirectory);
    }

    public IReadOnlyList<ReportingRunSnapshot> ListRuns(int limit = 25) =>
        LoadActiveSnapshot().Runs
            .OrderByDescending(static run => run.UpdatedAtUtc)
            .ThenBy(static run => run.Manifest.OperationalScope?.TenantId, StringComparer.Ordinal)
            .ThenBy(static run => run.Manifest.RunId, StringComparer.Ordinal)
            .Take(Math.Clamp(limit, 1, 200))
            .ToArray();

    public ReportingOutputManifest? GetManifest(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var matches = LoadActiveSnapshot().Runs
            .Where(run => string.Equals(run.Manifest.RunId, runId.Trim(), StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0].Manifest : null;
    }

    public ReportingOutputManifest? GetManifest(string tenantId, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        return LoadActiveSnapshot().Runs
            .SingleOrDefault(run =>
                string.Equals(run.Manifest.OperationalScope?.TenantId, tenantId.Trim(), StringComparison.Ordinal)
                && string.Equals(run.Manifest.RunId, runId.Trim(), StringComparison.OrdinalIgnoreCase))
            ?.Manifest;
    }

    public IReadOnlyList<ReportingRunAuditEntry> GetAudit(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var matches = LoadActiveSnapshot().Runs
            .Where(run => string.Equals(run.Manifest.RunId, runId.Trim(), StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0].AuditTrail : [];
    }

    public IReadOnlyList<ReportingRunAuditEntry> GetAudit(string tenantId, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        return LoadActiveSnapshot().Runs
            .SingleOrDefault(run =>
                string.Equals(run.Manifest.OperationalScope?.TenantId, tenantId.Trim(), StringComparison.Ordinal)
                && string.Equals(run.Manifest.RunId, runId.Trim(), StringComparison.OrdinalIgnoreCase))
            ?.AuditTrail ?? [];
    }

    public async Task SaveAsync(
        ReportingOutputManifest manifest,
        IReadOnlyList<ReportingRunAuditEntry> auditTrail,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ValidateManifest(manifest);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tenantId = manifest.OperationalScope?.TenantId;
            var retained = new ReportingRunSnapshot(
                manifest,
                auditTrail.ToArray(),
                DateTimeOffset.UtcNow,
                ComputeCertifiedRowsHash(manifest.CertifiedDatasetRows),
                ComputeManifestHash(manifest));
            var state = LoadStoreState();
            EnsureLegacyStateArchived(state);
            var current = state.Snapshot.Runs
                .Where(run => !SameIdentity(run.Manifest, tenantId, manifest.RunId))
                .Append(retained)
                .OrderByDescending(static run => run.UpdatedAtUtc)
                .ThenBy(static run => run.Manifest.OperationalScope?.TenantId, StringComparer.Ordinal)
                .ThenBy(static run => run.Manifest.RunId, StringComparer.Ordinal)
                .ToArray();
            EnsureUniqueIdentities(current);
            var snapshot = new ReportingRunStoreSnapshot(
                SchemaVersion,
                current,
                ComputePayloadHash(current),
                state.Snapshot.LegacyRuns,
                ComputeLegacyPayloadHash(
                    state.Snapshot.LegacyRuns,
                    state.Snapshot.LegacyArchiveReceipt),
                state.Snapshot.LegacyArchiveReceipt);
            await AtomicFileWriter
                .WriteAsync(SnapshotPath, JsonSerializer.Serialize(snapshot, _jsonOptions), ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool HasLegacyRuns(ReportAccessQueryContext recoveryAuthority)
    {
        var state = LoadStoreState();
        return state.Snapshot.LegacyRuns.Any(entry => CanInspectLegacyEntry(entry, recoveryAuthority));
    }

    public IReadOnlyList<ReportingLegacyStateInventoryEntry> ListLegacyRunInventory(
        ReportAccessQueryContext recoveryAuthority)
    {
        var state = LoadStoreState();
        return BuildLegacyInventory(
            state.Snapshot.LegacyRuns.Where(entry => CanInspectLegacyEntry(entry, recoveryAuthority)),
            !state.RequiresExplicitArchive);
    }

    public string ExportLegacyRun(
        string runId,
        ReportAccessQueryContext recoveryAuthority)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var state = LoadStoreState();
        var entry = state.Snapshot.LegacyRuns.SingleOrDefault(candidate =>
            string.Equals(candidate.RecordId, runId.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException("Legacy reporting run was not found.");
        EnsureCanInspectLegacyEntry(entry, recoveryAuthority);
        return entry.RawPayloadJson;
    }

    public async Task<ReportingLegacyArchiveReceipt> ArchiveLegacySnapshotAsync(
        ReportAccessQueryContext recoveryAuthority,
        string reason,
        CancellationToken ct = default)
    {
        ValidateRecoveryAuthority(recoveryAuthority, requireLocalOperator: true);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var state = LoadStoreState();
            if (!state.RequiresExplicitArchive)
            {
                EnsureCanArchiveAll(state.Snapshot.LegacyRuns, recoveryAuthority);
                return state.Snapshot.LegacyArchiveReceipt
                    ?? throw new InvalidOperationException("No unarchived legacy reporting run snapshot exists.");
            }

            EnsureCanArchiveAll(state.Snapshot.LegacyRuns, recoveryAuthority);
            var archivedAtUtc = DateTimeOffset.UtcNow;
            var archivedPayloadHash = ComputeLegacyEntriesHash(state.Snapshot.LegacyRuns);
            var receipt = new ReportingLegacyArchiveReceipt(
                ComputeSha256(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                {
                    kind = "run",
                    actor = recoveryAuthority.ActorPrincipalId!.Trim(),
                    reason = reason.Trim(),
                    archivedAtUtc,
                    archivedPayloadHash
                }))),
                "run",
                state.Snapshot.LegacyRuns.Count,
                recoveryAuthority.ActorPrincipalId!.Trim(),
                reason.Trim(),
                archivedAtUtc,
                archivedPayloadHash);
            var archived = new ReportingRunStoreSnapshot(
                SchemaVersion,
                [],
                ComputePayloadHash([]),
                state.Snapshot.LegacyRuns,
                ComputeLegacyPayloadHash(state.Snapshot.LegacyRuns, receipt),
                receipt);
            await AtomicFileWriter
                .WriteAsync(SnapshotPath, JsonSerializer.Serialize(archived, _jsonOptions), ct)
                .ConfigureAwait(false);
            return receipt;
        }
        finally
        {
            _gate.Release();
        }
    }

    private ReportingRunStoreSnapshot LoadActiveSnapshot()
    {
        var state = LoadStoreState();
        EnsureLegacyStateArchived(state);
        return state.Snapshot;
    }

    private ReportingRunStoreState LoadStoreState()
    {
        if (!File.Exists(SnapshotPath))
        {
            return new ReportingRunStoreState(
                new ReportingRunStoreSnapshot(
                    SchemaVersion,
                    [],
                    ComputePayloadHash([]),
                    [],
                    ComputeLegacyPayloadHash([], archiveReceipt: null),
                    LegacyArchiveReceipt: null),
                RequiresExplicitArchive: false);
        }

        try
        {
            var json = File.ReadAllText(SnapshotPath);
            using var document = JsonDocument.Parse(json);
            EnsureNoDuplicateProperties(document.RootElement, "reporting run snapshot");
            if (!TryGetPropertyIgnoreCase(document.RootElement, "schemaVersion", out _))
            {
                return LoadLegacyStoreState(document.RootElement);
            }

            var snapshot = JsonSerializer.Deserialize<ReportingRunStoreSnapshot>(json, _jsonOptions)
                ?? throw new JsonException("Reporting run snapshot deserialized to null.");
            if (!string.Equals(snapshot.SchemaVersion, SchemaVersion, StringComparison.Ordinal)
                || snapshot.Runs is null
                || !IsSha256(snapshot.PayloadHashSha256)
                || !FixedHashEquals(snapshot.PayloadHashSha256, ComputePayloadHash(snapshot.Runs)))
            {
                throw new InvalidDataException(
                    "Reporting run snapshot schema or canonical payload checksum is invalid.");
            }

            var legacyRuns = snapshot.LegacyRuns
                ?? throw new InvalidDataException("Reporting run snapshot has no legacy inventory collection.");
            if (!IsSha256(snapshot.LegacyPayloadHashSha256)
                || !FixedHashEquals(
                    snapshot.LegacyPayloadHashSha256!,
                    ComputeLegacyPayloadHash(legacyRuns, snapshot.LegacyArchiveReceipt)))
            {
                throw new InvalidDataException(
                    "Reporting run snapshot legacy inventory checksum is invalid.");
            }

            EnsureUniqueIdentities(snapshot.Runs);
            foreach (var run in snapshot.Runs)
            {
                ValidateSnapshot(run);
            }
            ValidateArchivedLegacyRuns(legacyRuns);
            ValidateArchiveReceipt(legacyRuns, snapshot.LegacyArchiveReceipt);
            return new ReportingRunStoreState(snapshot, RequiresExplicitArchive: false);
        }
        catch (Exception ex) when (ex is IOException
            or JsonException
            or UnauthorizedAccessException
            or InvalidDataException
            or ReportingGovernanceException
            or FormatException)
        {
            _logger.LogCritical(
                ex,
                "Reporting run snapshot at {SnapshotPath} is unreadable or failed canonical integrity validation; reporting is blocked until the state is recovered.",
                SnapshotPath);
            throw new ReportingStateCorruptionException(SnapshotPath, ex);
        }
    }

    private ReportingRunStoreState LoadLegacyStoreState(JsonElement root)
    {
        EnsureLegacyRootShape(root, "runs", "reporting run snapshot");
        if (!TryGetPropertyIgnoreCase(root, "runs", out var runsElement)
            || runsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Legacy reporting run snapshot has no run collection.");
        }

        var legacyRuns = runsElement
            .EnumerateArray()
            .Select(CreateLegacyRunEntry)
            .ToArray();
        ValidateArchivedLegacyRuns(legacyRuns);
        var snapshot = new ReportingRunStoreSnapshot(
            SchemaVersion,
            [],
            ComputePayloadHash([]),
            legacyRuns,
            ComputeLegacyPayloadHash(legacyRuns, archiveReceipt: null),
            LegacyArchiveReceipt: null);
        return new ReportingRunStoreState(snapshot, RequiresExplicitArchive: true);
    }

    private LegacyReportingRunEntry CreateLegacyRunEntry(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Legacy reporting run snapshot contains a non-object run.");
        }

        EnsureNoDuplicateProperties(element, "legacy reporting run");
        var rawPayload = element.GetRawText();
        var run = JsonSerializer.Deserialize<ReportingRunSnapshot>(rawPayload, _jsonOptions)
            ?? throw new InvalidDataException("Legacy reporting run deserialized to null.");
        ValidateLegacySnapshot(run);
        var canonicalPayload = CanonicalizeJson(rawPayload);
        return new LegacyReportingRunEntry(
            LegacySchemaVersion,
            "run",
            run.Manifest.RunId.Trim(),
            run.Manifest.OperationalScope?.TenantId,
            run.Manifest.OperationalScope?.OrganizationId,
            run.Manifest.OperationalScope?.CompanyId,
            rawPayload,
            ComputeSha256(Encoding.UTF8.GetBytes(rawPayload)),
            ComputeSha256(Encoding.UTF8.GetBytes(canonicalPayload)),
            LegacyRunRemediation);
    }

    private void ValidateLegacySnapshot(ReportingRunSnapshot run)
    {
        if (run is null
            || run.Manifest is null
            || run.AuditTrail is null
            || run.UpdatedAtUtc == default
            || run.UpdatedAtUtc.Offset != TimeSpan.Zero
            || string.IsNullOrWhiteSpace(run.Manifest.RunId)
            || string.IsNullOrWhiteSpace(run.Manifest.TemplateId)
            || run.Manifest.AsOfDate == default
            || !Enum.IsDefined(run.Manifest.Status)
            || !Enum.IsDefined(run.Manifest.Trigger)
            || run.Manifest.AttemptCount < 0
            || run.Manifest.Sections.IsDefault
            || run.Manifest.Artifacts.IsDefault
            || run.Manifest.OperationalScope is { } operationalScope
            && (string.IsNullOrWhiteSpace(operationalScope.TenantId)
                || string.IsNullOrWhiteSpace(operationalScope.OrganizationId)
                || string.IsNullOrWhiteSpace(operationalScope.BookId)
                || string.IsNullOrWhiteSpace(operationalScope.PeriodId))
            || run.AuditTrail.Any(entry => entry is null
                || !string.Equals(entry.RunId, run.Manifest.RunId, StringComparison.OrdinalIgnoreCase)
                || entry.TimestampUtc == default
                || entry.TimestampUtc.Offset != TimeSpan.Zero
                || string.IsNullOrWhiteSpace(entry.Action)
                || string.IsNullOrWhiteSpace(entry.Actor)))
        {
            throw new InvalidDataException("Legacy reporting run is structurally invalid.");
        }

        var hasDatasetHash = !string.IsNullOrWhiteSpace(run.CertifiedDatasetHashSha256);
        var hasManifestHash = !string.IsNullOrWhiteSpace(run.ManifestHashSha256);
        if (hasDatasetHash != hasManifestHash
            || hasDatasetHash
            && (!IsSha256(run.CertifiedDatasetHashSha256)
                || !IsSha256(run.ManifestHashSha256)
                || !FixedHashEquals(
                    run.CertifiedDatasetHashSha256!,
                    ComputeCertifiedRowsHash(run.Manifest.CertifiedDatasetRows))
                || !FixedHashEquals(run.ManifestHashSha256!, ComputeManifestHash(run.Manifest))))
        {
            throw new InvalidDataException(
                "Legacy reporting run contains incomplete or mismatched optional integrity checksums.");
        }
    }

    private void ValidateArchivedLegacyRuns(IReadOnlyList<LegacyReportingRunEntry> entries)
    {
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry is null
                || !string.Equals(entry.SourceSchemaVersion, LegacySchemaVersion, StringComparison.Ordinal)
                || !string.Equals(entry.RecordKind, "run", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(entry.RecordId)
                || string.IsNullOrWhiteSpace(entry.TenantId)
                && (!string.IsNullOrWhiteSpace(entry.OrganizationId)
                    || !string.IsNullOrWhiteSpace(entry.CompanyId))
                || !string.IsNullOrWhiteSpace(entry.TenantId)
                && string.IsNullOrWhiteSpace(entry.OrganizationId)
                || string.IsNullOrWhiteSpace(entry.RawPayloadJson)
                || !string.Equals(entry.Remediation, LegacyRunRemediation, StringComparison.Ordinal)
                || !FixedHashEquals(
                    entry.RawPayloadHashSha256,
                    ComputeSha256(Encoding.UTF8.GetBytes(entry.RawPayloadJson)))
                || !FixedHashEquals(
                    entry.CanonicalPayloadHashSha256,
                    ComputeSha256(Encoding.UTF8.GetBytes(CanonicalizeJson(entry.RawPayloadJson)))))
            {
                throw new InvalidDataException(
                    "Archived legacy reporting run inventory checksum or schema is invalid.");
            }

            var run = JsonSerializer.Deserialize<ReportingRunSnapshot>(entry.RawPayloadJson, _jsonOptions)
                ?? throw new InvalidDataException("Archived legacy reporting run deserialized to null.");
            ValidateLegacySnapshot(run);
            if (!string.Equals(entry.RecordId, run.Manifest.RunId.Trim(), StringComparison.Ordinal)
                || !string.Equals(
                    entry.TenantId,
                    run.Manifest.OperationalScope?.TenantId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    entry.OrganizationId,
                    run.Manifest.OperationalScope?.OrganizationId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    entry.CompanyId,
                    run.Manifest.OperationalScope?.CompanyId,
                    StringComparison.Ordinal)
                || !identities.Add(entry.RecordId))
            {
                throw new InvalidDataException(
                    $"Archived legacy reporting run identity or scope index '{entry.RecordId}' is invalid or duplicated.");
            }
        }
    }

    private void EnsureLegacyStateArchived(ReportingRunStoreState state)
    {
        if (state.RequiresExplicitArchive)
        {
            throw new ReportingLegacyStateRequiresArchiveException(
                statePath: SnapshotPath,
                recordKind: "run",
                state.Snapshot.LegacyRuns.Count);
        }
    }

    private void ValidateSnapshot(ReportingRunSnapshot run)
    {
        if (run is null
            || run.Manifest is null
            || run.AuditTrail is null
            || run.UpdatedAtUtc == default
            || run.UpdatedAtUtc.Offset != TimeSpan.Zero
            || !IsSha256(run.CertifiedDatasetHashSha256)
            || !IsSha256(run.ManifestHashSha256)
            || !FixedHashEquals(
                run.CertifiedDatasetHashSha256!,
                ComputeCertifiedRowsHash(run.Manifest.CertifiedDatasetRows))
            || !FixedHashEquals(run.ManifestHashSha256!, ComputeManifestHash(run.Manifest)))
        {
            throw new InvalidDataException(
                "A retained reporting run has incomplete timestamps or mismatched manifest/dataset checksums.");
        }

        ValidateManifest(run.Manifest);
    }

    private static void ValidateManifest(ReportingOutputManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.RunId)
            || string.IsNullOrWhiteSpace(manifest.TemplateId)
            || manifest.CertifiedDatasetRows.IsDefault)
        {
            throw new InvalidDataException("A retained reporting manifest is incomplete.");
        }

        var hasCertifiedState = manifest.OperationalScope is not null
            || manifest.ImmutableAccessScope is not null
            || manifest.CertifiedSnapshot is not null
            || manifest.AuthoritativeSource is not null;
        if (!hasCertifiedState)
        {
            return;
        }

        if (manifest.OperationalScope is not { } scope
            || manifest.ImmutableAccessScope is not { } access
            || manifest.CertifiedSnapshot is not { } snapshot
            || manifest.AuthoritativeSource is not { } source
            || manifest.ResolvedTemplate is null
            || manifest.ResolvedParameters is null
            || manifest.Readiness is null
            || !IsSha256(manifest.Readiness.EvidenceHash)
            || manifest.Readiness.EvaluatedAtUtc == default
            || manifest.Readiness.EvaluatedAtUtc.Offset != TimeSpan.Zero
            || manifest.CertifiedDatasetRows.Length != source.LedgerLineCount
            || !string.Equals(source.TenantId, scope.TenantId, StringComparison.Ordinal)
            || !string.Equals(source.OrganizationId, scope.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(source.CompanyId, scope.CompanyId, StringComparison.Ordinal)
            || !string.Equals(source.FundId, scope.FundId, StringComparison.Ordinal)
            || !string.Equals(source.LedgerBookId, scope.BookId, StringComparison.Ordinal)
            || !string.Equals(source.AccountingPeriodId, scope.PeriodId, StringComparison.Ordinal)
            || !string.Equals(snapshot.SourceCheckpointId, source.CheckpointId, StringComparison.Ordinal)
            || !string.Equals(snapshot.SourceCheckpointHash, source.CheckpointHash, StringComparison.OrdinalIgnoreCase)
            || !IsSha256(source.CheckpointHash)
            || source.EvidenceIds.IsDefaultOrEmpty
            || !source.EvidenceIds.Contains(
                $"reporting-source-checkpoint:{source.CheckpointId}:{source.CheckpointHash}",
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "A retained certified reporting manifest has incomplete or mismatched source bindings.");
        }

        ReportingGovernanceCanonicalValidation.ValidateScope(scope);
        ReportingGovernanceCanonicalValidation.ValidateAccess(access);
        ReportingGovernanceCanonicalValidation.ValidateSnapshot(snapshot, scope);
        var expectedSnapshotHash = ComputeSnapshotHash(manifest);
        if (!FixedHashEquals(snapshot.SnapshotHash, expectedSnapshotHash))
        {
            throw new InvalidDataException(
                "The retained certified snapshot hash does not match its template, scope, access, parameters, source, reconciliation, and readiness binding.");
        }
    }

    private static string ComputeSnapshotHash(ReportingOutputManifest manifest) =>
        ComputeSha256(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            template = new
            {
                manifest.ResolvedTemplate!.Name,
                manifest.ResolvedTemplate.Version
            },
            scope = manifest.OperationalScope,
            access = manifest.ImmutableAccessScope,
            parametersHash = manifest.CertifiedSnapshot!.ParametersHash,
            sourceCheckpointId = manifest.AuthoritativeSource!.CheckpointId,
            sourceCheckpointHash = manifest.AuthoritativeSource.CheckpointHash,
            reconciliationId = manifest.CertifiedSnapshot.ReconciliationCheckpointId,
            reconciliationHash = manifest.CertifiedSnapshot.ReconciliationCheckpointHash,
            readinessHash = manifest.Readiness!.EvidenceHash,
            certifiedDatasetHash = ComputeCertifiedRowsHash(manifest.CertifiedDatasetRows)
        })));

    private string ComputeManifestHash(ReportingOutputManifest manifest) =>
        ComputeSha256(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(NormalizeManifestArraysForHashing(manifest), _jsonOptions)));

    // A manifest can carry default (uninitialized) ImmutableArray members — most commonly on the
    // run-failure path, before the grid/diff collections are populated. Serializing a default
    // ImmutableArray throws InvalidOperationException, which would abort the durable write (and, on
    // load, the integrity verification) instead of persisting the failed run. Normalize every
    // optional array member to Empty before hashing. This is a no-op for populated manifests — a
    // populated array and Empty both serialize to their element JSON — so hashes for already-retained
    // runs are unchanged, keeping save-side and load-side verification consistent.
    private static ReportingOutputManifest NormalizeManifestArraysForHashing(
        ReportingOutputManifest manifest) =>
        manifest with
        {
            Sections = OrEmpty(manifest.Sections),
            Artifacts = OrEmpty(manifest.Artifacts),
            ReportWriterGrids = OrEmpty(manifest.ReportWriterGrids),
            RenderedReportWriterGrids = OrEmpty(manifest.RenderedReportWriterGrids),
            ReportWriterGridDiffs = OrEmpty(manifest.ReportWriterGridDiffs),
            CertifiedDatasetRows = OrEmpty(manifest.CertifiedDatasetRows)
        };

    private static ImmutableArray<T> OrEmpty<T>(ImmutableArray<T> value) =>
        value.IsDefault ? ImmutableArray<T>.Empty : value;

    private string ComputePayloadHash(IReadOnlyList<ReportingRunSnapshot> runs) =>
        ComputeSha256(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(runs, _jsonOptions)));

    private string ComputeLegacyEntriesHash(IReadOnlyList<LegacyReportingRunEntry> entries) =>
        ComputeSha256(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entries, _jsonOptions)));

    private string ComputeLegacyPayloadHash(
        IReadOnlyList<LegacyReportingRunEntry> entries,
        ReportingLegacyArchiveReceipt? archiveReceipt) =>
        ComputeSha256(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            entries,
            archiveReceipt
        }, _jsonOptions)));

    private static IReadOnlyList<ReportingLegacyStateInventoryEntry> BuildLegacyInventory(
        IEnumerable<LegacyReportingRunEntry> entries,
        bool isArchived) =>
        entries
            .OrderBy(static entry => entry.TenantId, StringComparer.Ordinal)
            .ThenBy(static entry => entry.CompanyId, StringComparer.Ordinal)
            .ThenBy(static entry => entry.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new ReportingLegacyStateInventoryEntry(
                "run",
                entry.RecordId,
                entry.SourceSchemaVersion,
                entry.TenantId,
                entry.OrganizationId,
                entry.CompanyId,
                entry.RawPayloadHashSha256,
                entry.CanonicalPayloadHashSha256,
                isArchived,
                entry.Remediation))
            .ToArray();

    private bool CanInspectLegacyEntry(
        LegacyReportingRunEntry entry,
        ReportAccessQueryContext? recoveryAuthority)
    {
        if (recoveryAuthority is null
            || string.IsNullOrWhiteSpace(recoveryAuthority.ActorPrincipalId))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(entry.TenantId)
            && string.IsNullOrWhiteSpace(entry.CompanyId))
        {
            return recoveryAuthority.HasGlobalOverride;
        }

        if (!recoveryAuthority.RequireBoundScope
            || !string.Equals(entry.TenantId, recoveryAuthority.TenantId, StringComparison.Ordinal)
            || !string.Equals(entry.CompanyId, recoveryAuthority.CompanyId, StringComparison.Ordinal))
        {
            return false;
        }

        var run = JsonSerializer.Deserialize<ReportingRunSnapshot>(entry.RawPayloadJson, _jsonOptions);
        return run is not null
            && (recoveryAuthority.HasGlobalOverride
                || ReportAccessPolicyEvaluator
                    .Evaluate(run.Manifest.AccessPolicy, recoveryAuthority)
                    .IsAccessible);
    }

    private void EnsureCanInspectLegacyEntry(
        LegacyReportingRunEntry entry,
        ReportAccessQueryContext recoveryAuthority)
    {
        if (!CanInspectLegacyEntry(entry, recoveryAuthority))
        {
            throw new UnauthorizedAccessException(
                "Legacy reporting run inventory is outside the authenticated tenant/company recovery scope.");
        }
    }

    private static void ValidateRecoveryAuthority(
        ReportAccessQueryContext? recoveryAuthority,
        bool requireLocalOperator)
    {
        if (recoveryAuthority is null
            || string.IsNullOrWhiteSpace(recoveryAuthority.ActorPrincipalId)
            || requireLocalOperator && !recoveryAuthority.HasGlobalOverride)
        {
            throw new UnauthorizedAccessException(
                "Legacy reporting recovery requires an authenticated local operator with global recovery authority.");
        }
    }

    private static void EnsureCanArchiveAll(
        IReadOnlyList<LegacyReportingRunEntry> entries,
        ReportAccessQueryContext recoveryAuthority)
    {
        ValidateRecoveryAuthority(recoveryAuthority, requireLocalOperator: true);
        if (entries.Any(entry => !string.IsNullOrWhiteSpace(entry.TenantId)
            && !string.IsNullOrWhiteSpace(recoveryAuthority.TenantId)
            && !string.Equals(entry.TenantId, recoveryAuthority.TenantId, StringComparison.Ordinal)))
        {
            throw new UnauthorizedAccessException(
                "A tenant-bound recovery authority cannot archive another tenant's legacy reporting runs.");
        }
    }

    private void ValidateArchiveReceipt(
        IReadOnlyList<LegacyReportingRunEntry> entries,
        ReportingLegacyArchiveReceipt? receipt)
    {
        if (entries.Count == 0 && receipt is null)
        {
            return;
        }

        if (receipt is null
            || !string.Equals(receipt.RecordKind, "run", StringComparison.Ordinal)
            || receipt.RecordCount != entries.Count
            || string.IsNullOrWhiteSpace(receipt.ActorPrincipalId)
            || string.IsNullOrWhiteSpace(receipt.Reason)
            || receipt.ArchivedAtUtc == default
            || receipt.ArchivedAtUtc.Offset != TimeSpan.Zero
            || !FixedHashEquals(receipt.ArchivedPayloadHashSha256, ComputeLegacyEntriesHash(entries)))
        {
            throw new InvalidDataException("Legacy reporting run archive receipt is invalid.");
        }

        var expectedArchiveId = ComputeSha256(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            kind = "run",
            actor = receipt.ActorPrincipalId,
            reason = receipt.Reason,
            archivedAtUtc = receipt.ArchivedAtUtc,
            archivedPayloadHash = receipt.ArchivedPayloadHashSha256
        })));
        if (!FixedHashEquals(receipt.ArchiveId, expectedArchiveId))
        {
            throw new InvalidDataException("Legacy reporting run archive receipt identity is invalid.");
        }
    }

    private static string CanonicalizeJson(string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        EnsureNoDuplicateProperties(document.RootElement, "legacy reporting payload");
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static void EnsureLegacyRootShape(
        JsonElement root,
        string collectionProperty,
        string description)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Legacy {description} root is not an object.");
        }

        var properties = root.EnumerateObject().ToArray();
        if (properties.Length != 1
            || !string.Equals(
                properties[0].Name,
                collectionProperty,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Unversioned {description} does not match the committed v1 root shape.");
        }
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static void EnsureNoDuplicateProperties(JsonElement element, string description)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException(
                        $"{description} contains duplicate JSON property '{property.Name}'.");
                }

                EnsureNoDuplicateProperties(property.Value, description);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                EnsureNoDuplicateProperties(item, description);
            }
        }
    }

    internal static string ComputeCertifiedRowsHash(
        ImmutableArray<IReadOnlyDictionary<string, string>> rows)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var row in rows.IsDefault
                         ? ImmutableArray<IReadOnlyDictionary<string, string>>.Empty
                         : rows)
            {
                writer.WriteStartObject();
                foreach (var pair in row.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WriteString(pair.Key, pair.Value);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return ComputeSha256(stream.ToArray());
    }

    private static void EnsureUniqueIdentities(IReadOnlyList<ReportingRunSnapshot> runs)
    {
        for (var index = 0; index < runs.Count; index++)
        {
            if (runs[index] is null || runs[index].Manifest is null)
            {
                throw new InvalidDataException("Reporting run snapshot contains a null run.");
            }
            for (var candidate = index + 1; candidate < runs.Count; candidate++)
            {
                if (runs[candidate] is not null
                    && runs[candidate].Manifest is not null
                    && SameIdentity(
                        runs[candidate].Manifest,
                        runs[index].Manifest.OperationalScope?.TenantId,
                        runs[index].Manifest.RunId))
                {
                    throw new InvalidDataException(
                        $"Reporting run snapshot contains duplicate scoped identity '{runs[index].Manifest.OperationalScope?.TenantId}/{runs[index].Manifest.RunId}'.");
                }
            }
        }
    }

    private static bool SameIdentity(
        ReportingOutputManifest manifest,
        string? tenantId,
        string runId) =>
        string.Equals(manifest.OperationalScope?.TenantId, tenantId, StringComparison.Ordinal)
        && string.Equals(manifest.RunId, runId, StringComparison.OrdinalIgnoreCase);

    private static string ComputeSha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool FixedHashEquals(string left, string right) =>
        IsSha256(left)
        && IsSha256(right)
        && CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(left),
            Convert.FromHexString(right));

    private static bool IsSha256(string? value) =>
        value is { Length: 64 }
        && value.All(Uri.IsHexDigit)
        && string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal);

    private string SnapshotPath => Path.Combine(_options.RootDirectory, SnapshotFileName);

    private sealed record ReportingRunStoreSnapshot(
        string SchemaVersion,
        IReadOnlyList<ReportingRunSnapshot> Runs,
        string PayloadHashSha256,
        IReadOnlyList<LegacyReportingRunEntry> LegacyRuns,
        string LegacyPayloadHashSha256,
        ReportingLegacyArchiveReceipt? LegacyArchiveReceipt);

    private sealed record ReportingRunStoreState(
        ReportingRunStoreSnapshot Snapshot,
        bool RequiresExplicitArchive);

    private sealed record LegacyReportingRunEntry(
        string SourceSchemaVersion,
        string RecordKind,
        string RecordId,
        string? TenantId,
        string? OrganizationId,
        string? CompanyId,
        string RawPayloadJson,
        string RawPayloadHashSha256,
        string CanonicalPayloadHashSha256,
        string Remediation);
}
