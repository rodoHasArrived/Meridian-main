using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Application.Composition;
using Meridian.Contracts.Integrity;
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

public sealed class FileReportingRunStore : IReportingRunStore, INonProductionOnlyService
{
    private const string SnapshotFileName = "reporting-runs.json";
    private const string SchemaVersion = "meridian.reporting.run-store.v2";
    private const string LegacySchemaVersion = "meridian.reporting.run-store.v1";
    private const string LegacyRunRemediation =
        "Read-only legacy run. Preserve for inventory, then freshly recertify from authoritative source data before current use.";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> StoreGates =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, ReportingRunCreateLease> CreateLeases =
        new(StringComparer.Ordinal);

    private readonly ReportingRunStoreOptions _options;
    private readonly ILogger<FileReportingRunStore> _logger;
    private readonly SemaphoreSlim _gate;
    private readonly string _storeKey;
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
        _storeKey = Path.GetFullPath(Path.Combine(_options.RootDirectory, SnapshotFileName));
        _gate = StoreGates.GetOrAdd(
            _storeKey,
            static _ => new SemaphoreSlim(1, 1));
    }

    public IReadOnlyList<ReportingRunSnapshot> ListRuns(int limit = 25) =>
        LoadActiveSnapshot().Runs
            .OrderByDescending(static run => run.UpdatedAtUtc)
            .ThenBy(static run => run.Manifest.OperationalScope?.TenantId, StringComparer.Ordinal)
            .ThenBy(static run => run.Manifest.RunId, StringComparer.Ordinal)
            .Take(Math.Clamp(limit, 1, 200))
            .ToArray();

    public IReadOnlyList<ReportingRunSnapshot> ListRuns(string tenantId, int limit = 25)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var normalizedTenantId = tenantId.Trim();
        return LoadActiveSnapshot().Runs
            .Where(run => string.Equals(
                run.Manifest.OperationalScope?.TenantId,
                normalizedTenantId,
                StringComparison.Ordinal))
            .OrderByDescending(static run => run.UpdatedAtUtc)
            .ThenBy(static run => run.Manifest.RunId, StringComparer.Ordinal)
            .Take(Math.Clamp(limit, 1, 200))
            .ToArray();
    }

    public IReadOnlyList<ReportingRunSnapshot> ListRuns(
        string tenantId,
        string? companyId,
        int limit = 25) =>
        ListRuns(tenantId, companyId, offset: 0, limit: limit);

    public IReadOnlyList<ReportingRunSnapshot> ListRuns(
        string tenantId,
        string? companyId,
        int offset,
        int limit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        var normalizedTenantId = tenantId.Trim();
        var normalizedCompanyId = companyId?.Trim();
        return LoadActiveSnapshot().Runs
            .Where(run => string.Equals(
                    run.Manifest.OperationalScope?.TenantId,
                    normalizedTenantId,
                    StringComparison.Ordinal)
                && (string.IsNullOrWhiteSpace(normalizedCompanyId)
                    || string.Equals(
                    run.Manifest.OperationalScope?.CompanyId,
                    normalizedCompanyId,
                    StringComparison.Ordinal)))
            .OrderByDescending(static run => run.UpdatedAtUtc)
            .ThenBy(static run => run.Manifest.RunId, StringComparer.Ordinal)
            .Skip(offset)
            .Take(Math.Clamp(limit, 1, 200))
            .ToArray();
    }

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

    public string? GetRevision(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var matches = LoadActiveSnapshot().Runs
            .Where(run => string.Equals(
                run.Manifest.RunId,
                runId.Trim(),
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length == 1
            ? ReportingRunStoreRevision.Compute(
                matches[0].Manifest,
                matches[0].AuditTrail)
            : null;
    }

    public string? GetRevision(string tenantId, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var retained = LoadActiveSnapshot().Runs
            .SingleOrDefault(run =>
                string.Equals(
                    run.Manifest.OperationalScope?.TenantId,
                    tenantId.Trim(),
                    StringComparison.Ordinal)
                && string.Equals(
                    run.Manifest.RunId,
                    runId.Trim(),
                    StringComparison.OrdinalIgnoreCase));
        return retained is null
            ? null
            : ReportingRunStoreRevision.Compute(
                retained.Manifest,
                retained.AuditTrail);
    }

    public async Task<ReportingRunCreateClaimResult> TryClaimCreateAsync(
        string tenantId,
        string runId,
        string leaseOwner,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        var normalizedTenantId = tenantId.Trim();
        var normalizedRunId = runId.Trim();
        var normalizedOwner = leaseOwner.Trim();
        var evaluatedAtUtc = DateTimeOffset.UtcNow;
        var claimKey = BuildCreateLeaseKey(normalizedTenantId, normalizedRunId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (LoadActiveSnapshot().Runs.Any(snapshot =>
                    string.Equals(
                        snapshot.Manifest.OperationalScope?.TenantId,
                        normalizedTenantId,
                        StringComparison.Ordinal)
                    && string.Equals(
                        snapshot.Manifest.RunId,
                        normalizedRunId,
                        StringComparison.OrdinalIgnoreCase)))
            {
                CreateLeases.TryRemove(claimKey, out _);
                return new ReportingRunCreateClaimResult(
                    ReportingRunCreateClaimStatus.AlreadyExists);
            }

            if (CreateLeases.TryGetValue(claimKey, out var retained)
                && retained.ExpiresAtUtc > evaluatedAtUtc
                && !string.Equals(retained.Owner, normalizedOwner, StringComparison.Ordinal))
            {
                return new ReportingRunCreateClaimResult(
                    ReportingRunCreateClaimStatus.LeasedByAnotherOwner,
                    retained.ExpiresAtUtc);
            }

            var expiresAtUtc = evaluatedAtUtc.Add(leaseDuration);
            CreateLeases[claimKey] = new ReportingRunCreateLease(
                normalizedOwner,
                expiresAtUtc,
                retained is null ? 1 : checked(retained.Version + 1));
            return new ReportingRunCreateClaimResult(
                ReportingRunCreateClaimStatus.Acquired,
                expiresAtUtc,
                CreateLeases[claimKey].Version);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RenewCreateClaimAsync(
        string tenantId,
        string runId,
        string leaseOwner,
        long leaseVersion,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leaseVersion);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        var claimKey = BuildCreateLeaseKey(tenantId.Trim(), runId.Trim());
        var normalizedOwner = leaseOwner.Trim();
        var evaluatedAtUtc = DateTimeOffset.UtcNow;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!CreateLeases.TryGetValue(claimKey, out var retained)
                || !string.Equals(retained.Owner, normalizedOwner, StringComparison.Ordinal)
                || retained.Version != leaseVersion
                || retained.ExpiresAtUtc <= evaluatedAtUtc)
            {
                return false;
            }

            CreateLeases[claimKey] = retained with
            {
                ExpiresAtUtc = evaluatedAtUtc.Add(leaseDuration)
            };
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReleaseCreateClaimAsync(
        string tenantId,
        string runId,
        string leaseOwner,
        long leaseVersion,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leaseVersion);
        var claimKey = BuildCreateLeaseKey(tenantId.Trim(), runId.Trim());
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (CreateLeases.TryGetValue(claimKey, out var retained)
                && string.Equals(retained.Owner, leaseOwner.Trim(), StringComparison.Ordinal)
                && retained.Version == leaseVersion)
            {
                CreateLeases.TryRemove(claimKey, out _);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task SaveAsync(
        ReportingOutputManifest manifest,
        IReadOnlyList<ReportingRunAuditEntry> auditTrail,
        CancellationToken ct = default) =>
        SaveAsync(manifest, auditTrail, expectedRevision: null, ct: ct);

    public Task SaveAsync(
        ReportingOutputManifest manifest,
        IReadOnlyList<ReportingRunAuditEntry> auditTrail,
        string? expectedRevision,
        CancellationToken ct = default) =>
        SaveCoreAsync(
            manifest,
            auditTrail,
            expectedRevision,
            leaseOwner: null,
            leaseVersion: 0,
            ct);

    public Task SaveClaimedCreateAsync(
        ReportingOutputManifest manifest,
        IReadOnlyList<ReportingRunAuditEntry> auditTrail,
        string leaseOwner,
        long leaseVersion,
        CancellationToken ct = default) =>
        SaveCoreAsync(
            manifest,
            auditTrail,
            expectedRevision: null,
            leaseOwner,
            leaseVersion,
            ct);

    private async Task SaveCoreAsync(
        ReportingOutputManifest manifest,
        IReadOnlyList<ReportingRunAuditEntry> auditTrail,
        string? expectedRevision,
        string? leaseOwner,
        long leaseVersion,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(auditTrail);

        // Normalize every optional ImmutableArray member to Empty up front so the manifest that is
        // validated, hashed, and serialized into the durable snapshot never carries a default
        // (uninitialized) array. A default ImmutableArray throws on serialization, which would abort
        // the whole write; and ValidateManifest rejects a default CertifiedDatasetRows before it
        // reaches its certified-state gate, which would otherwise block persisting failed
        // non-certified runs. Normalization is a no-op for populated manifests, so hashes and stored
        // shape for already-retained runs are unchanged.
        manifest = NormalizeManifestArrays(manifest);
        ValidateManifest(manifest);
        var retainedAudit = auditTrail.ToArray();
        var candidateRevision = ReportingRunStoreRevision.Compute(
            manifest,
            retainedAudit);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tenantId = manifest.OperationalScope?.TenantId;
            var retained = new ReportingRunSnapshot(
                manifest,
                retainedAudit,
                DateTimeOffset.UtcNow,
                ComputeCertifiedRowsHash(manifest.CertifiedDatasetRows),
                ComputeManifestHash(manifest));
            var state = LoadStoreState();
            EnsureLegacyStateArchived(state);
            var existing = state.Snapshot.Runs.SingleOrDefault(
                run => SameIdentity(run.Manifest, tenantId, manifest.RunId));
            var createLeaseKey = tenantId is null
                ? null
                : BuildCreateLeaseKey(tenantId, manifest.RunId);
            if (leaseOwner is not null)
            {
                if (tenantId is null
                    || leaseVersion <= 0
                    || createLeaseKey is null
                    || !CreateLeases.TryGetValue(createLeaseKey, out var createLease)
                    || !string.Equals(
                        createLease.Owner,
                        leaseOwner.Trim(),
                        StringComparison.Ordinal)
                    || createLease.Version != leaseVersion
                    || createLease.ExpiresAtUtc <= DateTimeOffset.UtcNow)
                {
                    throw new ReportingRunCreateClaimException(
                        tenantId ?? string.Empty,
                        manifest.RunId,
                        "The reporting run create lease is missing, expired, or was superseded by another owner.");
                }
            }
            else if (existing is null
                     && createLeaseKey is not null
                     && CreateLeases.TryGetValue(createLeaseKey, out var activeLease)
                     && activeLease.ExpiresAtUtc > DateTimeOffset.UtcNow)
            {
                throw new ReportingRunCreateClaimException(
                    tenantId ?? string.Empty,
                    manifest.RunId,
                    "The reporting run identity has an active durable create owner.");
            }

            if (existing is null)
            {
                if (expectedRevision is not null)
                {
                    throw ReportingRunConcurrencyException.ForMissing(
                        tenantId,
                        manifest.RunId,
                        expectedRevision);
                }
            }
            else
            {
                var currentRevision = ReportingRunStoreRevision.Compute(
                    existing.Manifest,
                    existing.AuditTrail);
                if (expectedRevision is null)
                {
                    if (ReportingRunStoreRevision.Matches(
                            currentRevision,
                            candidateRevision))
                    {
                        return;
                    }

                    throw ReportingRunConcurrencyException.ForConflict(
                        tenantId,
                        manifest.RunId,
                        expectedRevision: null,
                        currentRevision);
                }
                if (!ReportingRunStoreRevision.Matches(
                        currentRevision,
                        expectedRevision))
                {
                    throw ReportingRunConcurrencyException.ForConflict(
                        tenantId,
                        manifest.RunId,
                        expectedRevision,
                        currentRevision);
                }
                if (ReportingRunStoreRevision.Matches(
                        currentRevision,
                        candidateRevision))
                {
                    return;
                }
            }

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
            if (leaseOwner is not null && createLeaseKey is not null)
            {
                CreateLeases.TryRemove(createLeaseKey, out _);
            }
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
                Sha256Digest.Compute(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
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

    private string BuildCreateLeaseKey(string tenantId, string runId) =>
        $"{_storeKey.Length}:{_storeKey}:{tenantId.Length}:{tenantId}:{runId.ToLowerInvariant()}";

    private sealed record ReportingRunCreateLease(
        string Owner,
        DateTimeOffset ExpiresAtUtc,
        long Version);

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
                || !Sha256Digest.IsCanonical(snapshot.PayloadHashSha256)
                || !Sha256Digest.FixedEquals(snapshot.PayloadHashSha256, ComputePayloadHash(snapshot.Runs)))
            {
                throw new InvalidDataException(
                    "Reporting run snapshot schema or canonical payload checksum is invalid.");
            }

            var legacyRuns = snapshot.LegacyRuns
                ?? throw new InvalidDataException("Reporting run snapshot has no legacy inventory collection.");
            if (!Sha256Digest.IsCanonical(snapshot.LegacyPayloadHashSha256)
                || !Sha256Digest.FixedEquals(
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
            Sha256Digest.Compute(Encoding.UTF8.GetBytes(rawPayload)),
            Sha256Digest.Compute(Encoding.UTF8.GetBytes(canonicalPayload)),
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
            && (!Sha256Digest.IsCanonical(run.CertifiedDatasetHashSha256)
                || !Sha256Digest.IsCanonical(run.ManifestHashSha256)
                || !Sha256Digest.FixedEquals(
                    run.CertifiedDatasetHashSha256!,
                    ComputeCertifiedRowsHash(run.Manifest.CertifiedDatasetRows))
                || !Sha256Digest.FixedEquals(run.ManifestHashSha256!, ComputeManifestHash(run.Manifest))))
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
                || !Sha256Digest.FixedEquals(
                    entry.RawPayloadHashSha256,
                    Sha256Digest.Compute(Encoding.UTF8.GetBytes(entry.RawPayloadJson)))
                || !Sha256Digest.FixedEquals(
                    entry.CanonicalPayloadHashSha256,
                    Sha256Digest.Compute(Encoding.UTF8.GetBytes(CanonicalizeJson(entry.RawPayloadJson)))))
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
            || !Sha256Digest.IsCanonical(run.CertifiedDatasetHashSha256)
            || !Sha256Digest.IsCanonical(run.ManifestHashSha256)
            || !Sha256Digest.FixedEquals(
                run.CertifiedDatasetHashSha256!,
                ComputeCertifiedRowsHash(run.Manifest.CertifiedDatasetRows))
            || !Sha256Digest.FixedEquals(run.ManifestHashSha256!, ComputeManifestHash(run.Manifest)))
        {
            throw new InvalidDataException(
                "A retained reporting run has incomplete timestamps or mismatched manifest/dataset checksums.");
        }

        ValidateManifest(run.Manifest);
    }

    private static void ValidateManifest(ReportingOutputManifest manifest) =>
        ReportingCertifiedManifestValidation.Validate(manifest);

    // Normalizes before hashing as well as at the SaveAsync entry point. Save-side manifests are
    // already normalized, so this is a no-op there; on the load/verification path a manifest
    // deserialized from a legacy or externally authored snapshot can still surface a default
    // (or absent → default) array member, and hashing it directly would throw. Normalizing here
    // keeps hashing robust and consistent with the save-side computation.
    private string ComputeManifestHash(ReportingOutputManifest manifest) =>
        Sha256Digest.Compute(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(NormalizeManifestArrays(manifest), _jsonOptions)));

    // A manifest can carry default (uninitialized) ImmutableArray members — most commonly on the
    // run-failure path, before the grid/diff collections are populated. Serializing a default
    // ImmutableArray throws InvalidOperationException, which would abort the durable write instead
    // of persisting the run. Normalize every optional array member to Empty. This is a no-op for
    // populated manifests — a populated array and Empty both serialize to their element JSON — so
    // the stored shape and hashes for already-retained runs are unchanged.
    private static ReportingOutputManifest NormalizeManifestArrays(
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
        Sha256Digest.Compute(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(runs, _jsonOptions)));

    private string ComputeLegacyEntriesHash(IReadOnlyList<LegacyReportingRunEntry> entries) =>
        Sha256Digest.Compute(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entries, _jsonOptions)));

    private string ComputeLegacyPayloadHash(
        IReadOnlyList<LegacyReportingRunEntry> entries,
        ReportingLegacyArchiveReceipt? archiveReceipt) =>
        Sha256Digest.Compute(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
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
            || !Sha256Digest.FixedEquals(receipt.ArchivedPayloadHashSha256, ComputeLegacyEntriesHash(entries)))
        {
            throw new InvalidDataException("Legacy reporting run archive receipt is invalid.");
        }

        var expectedArchiveId = Sha256Digest.Compute(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            kind = "run",
            actor = receipt.ActorPrincipalId,
            reason = receipt.Reason,
            archivedAtUtc = receipt.ArchivedAtUtc,
            archivedPayloadHash = receipt.ArchivedPayloadHashSha256
        })));
        if (!Sha256Digest.FixedEquals(receipt.ArchiveId, expectedArchiveId))
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
        ImmutableArray<IReadOnlyDictionary<string, string>> rows) =>
        ReportingCertifiedManifestValidation.ComputeCertifiedRowsHash(rows);

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
