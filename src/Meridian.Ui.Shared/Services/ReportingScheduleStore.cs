using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Application.Composition;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Core.Scheduling;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Services;

public sealed record ReportingScheduleStoreOptions(string SnapshotPath);

/// <summary>
/// Stores reporting schedules and coordinates execution claims and leases.
/// </summary>
[Obsolete(
    "Use Meridian.Reporting.IReportingScheduleStore. This compatibility interface will be removed after legacy UI Shared callers migrate.")]
public interface IReportingScheduleStore : Meridian.Reporting.IReportingScheduleStore
{
}

#pragma warning disable CS0618
internal sealed class ReportingScheduleStoreCompatibilityAdapter(
    Meridian.Reporting.IReportingScheduleStore inner)
    : IReportingScheduleStore
{
    private readonly Meridian.Reporting.IReportingScheduleStore _inner =
        inner ?? throw new ArgumentNullException(nameof(inner));

    public bool IsDurableAuthority => _inner.IsDurableAuthority;

    public IReadOnlyList<ReportingScheduleRecordDto> Load() => _inner.Load();

    public void Save(IReadOnlyList<ReportingScheduleRecordDto> schedules) =>
        _inner.Save(schedules);

    public void Upsert(ReportingScheduleRecordDto schedule) =>
        _inner.Upsert(schedule);

    public void Upsert(
        ReportingScheduleRecordDto schedule,
        DateTimeOffset? expectedUpdatedAtUtc) =>
        _inner.Upsert(schedule, expectedUpdatedAtUtc);

    public ReportingScheduleExecutionLease? TryClaimExecution(
        ReportingScheduleRecordDto schedule,
        string leaseOwner,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration) =>
        _inner.TryClaimExecution(schedule, leaseOwner, nowUtc, leaseDuration);

    public ReportingScheduleExecutionLease? RenewExecutionLease(
        ReportingScheduleRecordDto schedule,
        ReportingScheduleExecutionLease lease,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration) =>
        _inner.RenewExecutionLease(schedule, lease, nowUtc, leaseDuration);

    public void ReleaseExecutionLease(
        string tenantId,
        string companyId,
        string scheduleId,
        ReportingScheduleExecutionLease lease) =>
        _inner.ReleaseExecutionLease(
            tenantId,
            companyId,
            scheduleId,
            lease);

    public void UpsertClaimedExecution(
        ReportingScheduleRecordDto schedule,
        DateTimeOffset expectedUpdatedAtUtc,
        ReportingScheduleExecutionLease lease) =>
        _inner.UpsertClaimedExecution(
            schedule,
            expectedUpdatedAtUtc,
            lease);

    public bool Delete(
        string tenantId,
        string companyId,
        string scheduleId,
        DateTimeOffset expectedUpdatedAtUtc) =>
        _inner.Delete(
            tenantId,
            companyId,
            scheduleId,
            expectedUpdatedAtUtc);
}
#pragma warning restore CS0618

internal readonly record struct ReportingScheduleIdentity(
    string TenantId,
    string CompanyId,
    string ScheduleId)
{
    public static ReportingScheduleIdentity From(ReportingScheduleRecordDto schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        return Create(schedule.TenantId, schedule.CompanyId, schedule.ScheduleId);
    }

    public static ReportingScheduleIdentity Create(string? tenantId, string? companyId, string scheduleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        var tenant = Normalize(tenantId);
        var company = Normalize(companyId);
        if ((tenant.Length == 0) != (company.Length == 0))
        {
            throw new InvalidDataException(
                $"Reporting schedule '{scheduleId.Trim()}' has an incomplete tenant/company identity.");
        }

        return new ReportingScheduleIdentity(tenant, company, scheduleId.Trim());
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}

internal sealed record ReportingScheduledHandoffCursor(
    DateTimeOffset CreatedAtUtc,
    string HandoffId);

internal sealed record ReportingScheduledHandoffPage(
    IReadOnlyList<ReportingScheduledReleaseHandoffDto> Handoffs,
    ReportingScheduledHandoffCursor? NextCursor);

internal sealed record ReportingScheduledArtifactSelection(
    IReadOnlyList<GovernanceReportArtifactFormatDto> Formats,
    IReadOnlyList<string> ArtifactIds);

internal sealed class ReportingScheduleIdentityComparer : IEqualityComparer<ReportingScheduleIdentity>
{
    public static ReportingScheduleIdentityComparer Instance { get; } = new();

    public bool Equals(ReportingScheduleIdentity x, ReportingScheduleIdentity y) =>
        StringComparer.Ordinal.Equals(x.TenantId, y.TenantId)
        && StringComparer.Ordinal.Equals(x.CompanyId, y.CompanyId)
        && StringComparer.OrdinalIgnoreCase.Equals(x.ScheduleId, y.ScheduleId);

    public int GetHashCode(ReportingScheduleIdentity obj)
    {
        var hash = new HashCode();
        hash.Add(obj.TenantId, StringComparer.Ordinal);
        hash.Add(obj.CompanyId, StringComparer.Ordinal);
        hash.Add(obj.ScheduleId, StringComparer.OrdinalIgnoreCase);
        return hash.ToHashCode();
    }
}

#pragma warning disable CS0618
/// <summary>
/// Persists reporting schedules, execution authority, and legacy schedule recovery evidence.
/// </summary>
public sealed partial class FileReportingScheduleStore : IReportingScheduleStore, INonProductionOnlyService
{
    private const string SchemaVersion = "meridian.reporting.schedule-store.v2";
    private const string EntrySchemaVersion = "meridian.reporting.schedule-entry.v2";
    private const string LegacySchemaVersion = "meridian.reporting.schedule-store.v1";
    private const string LegacyScheduleRemediation =
        "Read-only legacy schedule. Preserve for inventory, then recapture tenant, company, template, canonical parameters, typed access, recipient, and handoff policy before activation.";
    private static readonly ConcurrentDictionary<string, object> StoreGates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ReportingScheduleStoreOptions _options;
    private readonly ILogger<FileReportingScheduleStore> _logger;
    private readonly object _gate;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public FileReportingScheduleStore(
        ReportingScheduleStoreOptions options,
        ILogger<FileReportingScheduleStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.SnapshotPath);
        _storeKey = Path.GetFullPath(_options.SnapshotPath);
        _gate = StoreGates.GetOrAdd(
            _storeKey,
            static _ => new object());
    }

    public IReadOnlyList<ReportingScheduleRecordDto> Load()
    {
        lock (_gate)
        {
            var state = LoadStoreState();
            EnsureLegacyStateArchived(state);
            return state.Snapshot.Schedules.Select(static entry => entry.Schedule).ToArray();
        }
    }

    public void Save(IReadOnlyList<ReportingScheduleRecordDto> schedules)
    {
        ArgumentNullException.ThrowIfNull(schedules);
        lock (_gate)
        {
            var state = LoadStoreState();
            EnsureLegacyStateArchived(state);
            ValidateCurrentSchedules(schedules);
            var entries = schedules
                .OrderBy(static schedule => schedule.TenantId, StringComparer.Ordinal)
                .ThenBy(static schedule => schedule.CompanyId, StringComparer.Ordinal)
                .ThenBy(static schedule => schedule.DueAtUtc)
                .ThenBy(static schedule => schedule.ScheduleId, StringComparer.OrdinalIgnoreCase)
                .Select(schedule => new ReportingScheduleEntry(
                    EntrySchemaVersion,
                    schedule,
                    ComputeCanonicalHash(schedule)))
                .ToArray();
            var snapshot = new ReportingScheduleSnapshot(
                SchemaVersion,
                entries,
                state.Snapshot.LegacySchedules,
                ComputeSnapshotHash(
                    entries,
                    state.Snapshot.LegacySchedules,
                    state.Snapshot.LegacyArchiveReceipt),
                state.Snapshot.LegacyArchiveReceipt);
            AtomicFileWriter.Write(_options.SnapshotPath, JsonSerializer.Serialize(snapshot, _jsonOptions));
        }
    }

    public void Upsert(ReportingScheduleRecordDto schedule) =>
        Upsert(schedule, expectedUpdatedAtUtc: null);

    public void Upsert(
        ReportingScheduleRecordDto schedule,
        DateTimeOffset? expectedUpdatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        lock (_gate)
        {
            var identity = ReportingScheduleIdentity.From(schedule);
            var schedules = Load();
            var current = schedules.SingleOrDefault(candidate =>
                ReportingScheduleIdentityComparer.Instance.Equals(
                    ReportingScheduleIdentity.From(candidate),
                    identity));
            if (current is null)
            {
                if (expectedUpdatedAtUtc is not null)
                {
                    throw ReportingScheduleConcurrencyException.ForMissing(
                        schedule,
                        expectedUpdatedAtUtc.Value);
                }
            }
            else
            {
                var currentHash = ComputeCanonicalHash(current);
                var candidateHash = ComputeCanonicalHash(schedule);
                if (expectedUpdatedAtUtc is null)
                {
                    if (Sha256Digest.FixedEquals(currentHash, candidateHash))
                    {
                        return;
                    }

                    throw ReportingScheduleConcurrencyException.ForConflict(
                        current,
                        expectedUpdatedAtUtc: null);
                }
                if (current.UpdatedAtUtc != expectedUpdatedAtUtc.Value)
                {
                    throw ReportingScheduleConcurrencyException.ForConflict(
                        current,
                        expectedUpdatedAtUtc.Value);
                }
                if (Sha256Digest.FixedEquals(currentHash, candidateHash))
                {
                    return;
                }
                if (schedule.UpdatedAtUtc <= current.UpdatedAtUtc)
                {
                    throw new ArgumentException(
                        "A changed reporting schedule must advance UpdatedAtUtc beyond the retained revision.",
                        nameof(schedule));
                }
            }

            Save(schedules
                .Where(candidate => !ReportingScheduleIdentityComparer.Instance.Equals(
                    ReportingScheduleIdentity.From(candidate),
                    identity))
                .Append(schedule)
                .ToArray());
        }
    }

    public bool Delete(
        string tenantId,
        string companyId,
        string scheduleId,
        DateTimeOffset expectedUpdatedAtUtc)
    {
        var identity = ReportingScheduleIdentity.Create(tenantId, companyId, scheduleId);
        lock (_gate)
        {
            var schedules = Load();
            var current = schedules.SingleOrDefault(candidate =>
                ReportingScheduleIdentityComparer.Instance.Equals(
                    ReportingScheduleIdentity.From(candidate),
                    identity));
            if (current is null)
            {
                return false;
            }
            if (current.UpdatedAtUtc != expectedUpdatedAtUtc)
            {
                throw ReportingScheduleConcurrencyException.ForConflict(
                    current,
                    expectedUpdatedAtUtc);
            }

            Save(schedules
                .Where(candidate => !ReportingScheduleIdentityComparer.Instance.Equals(
                    ReportingScheduleIdentity.From(candidate),
                    identity))
                .ToArray());
            return true;
        }
    }

    public bool HasLegacySchedules(ReportAccessQueryContext recoveryAuthority)
    {
        lock (_gate)
        {
            return LoadStoreState().Snapshot.LegacySchedules.Any(entry =>
                CanInspectLegacyEntry(entry, recoveryAuthority));
        }
    }

    public IReadOnlyList<ReportingLegacyStateInventoryEntry> ListLegacyScheduleInventory(
        ReportAccessQueryContext recoveryAuthority)
    {
        lock (_gate)
        {
            var state = LoadStoreState();
            return state.Snapshot.LegacySchedules
                .Where(entry => CanInspectLegacyEntry(entry, recoveryAuthority))
                .OrderBy(static entry => entry.TenantId, StringComparer.Ordinal)
                .ThenBy(static entry => entry.CompanyId, StringComparer.Ordinal)
                .ThenBy(static entry => entry.RecordId, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new ReportingLegacyStateInventoryEntry(
                    "schedule",
                    entry.RecordId,
                    entry.SourceSchemaVersion,
                    entry.TenantId,
                    null,
                    entry.CompanyId,
                    entry.RawPayloadHashSha256,
                    entry.CanonicalPayloadHashSha256,
                    !state.RequiresExplicitArchive,
                    entry.Remediation))
                .ToArray();
        }
    }

    public string ExportLegacySchedule(
        string scheduleId,
        ReportAccessQueryContext recoveryAuthority)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        lock (_gate)
        {
            var entry = LoadStoreState().Snapshot.LegacySchedules.SingleOrDefault(candidate =>
                string.Equals(candidate.RecordId, scheduleId.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException("Legacy reporting schedule was not found.");
            if (!CanInspectLegacyEntry(entry, recoveryAuthority))
            {
                throw new UnauthorizedAccessException(
                    "Legacy reporting schedule is outside the authenticated tenant/company recovery scope.");
            }

            return entry.RawPayloadJson;
        }
    }

    public ReportingLegacyArchiveReceipt ArchiveLegacySnapshot(
        ReportAccessQueryContext recoveryAuthority,
        string reason)
    {
        ValidateRecoveryAuthority(recoveryAuthority, requireLocalOperator: true);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        lock (_gate)
        {
            var state = LoadStoreState();
            if (!state.RequiresExplicitArchive)
            {
                EnsureCanArchiveAll(state.Snapshot.LegacySchedules, recoveryAuthority);
                return state.Snapshot.LegacyArchiveReceipt
                    ?? throw new InvalidOperationException("No unarchived legacy reporting schedule snapshot exists.");
            }

            EnsureCanArchiveAll(state.Snapshot.LegacySchedules, recoveryAuthority);
            var archivedAtUtc = DateTimeOffset.UtcNow;
            var archivedPayloadHash = ComputeCanonicalHash(state.Snapshot.LegacySchedules);
            var receipt = new ReportingLegacyArchiveReceipt(
                Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new
                {
                    kind = "schedule",
                    actor = recoveryAuthority.ActorPrincipalId!.Trim(),
                    reason = reason.Trim(),
                    archivedAtUtc,
                    archivedPayloadHash
                })),
                "schedule",
                state.Snapshot.LegacySchedules.Count,
                recoveryAuthority.ActorPrincipalId!.Trim(),
                reason.Trim(),
                archivedAtUtc,
                archivedPayloadHash);
            var snapshot = new ReportingScheduleSnapshot(
                SchemaVersion,
                [],
                state.Snapshot.LegacySchedules,
                ComputeSnapshotHash([], state.Snapshot.LegacySchedules, receipt),
                receipt);
            AtomicFileWriter.Write(_options.SnapshotPath, JsonSerializer.Serialize(snapshot, _jsonOptions));
            return receipt;
        }
    }

    private ReportingScheduleStoreState LoadStoreState()
    {
        if (!File.Exists(_options.SnapshotPath))
        {
            return new ReportingScheduleStoreState(
                new ReportingScheduleSnapshot(
                    SchemaVersion,
                    [],
                    [],
                    ComputeSnapshotHash([], [], archiveReceipt: null),
                    LegacyArchiveReceipt: null),
                RequiresExplicitArchive: false);
        }

        try
        {
            var json = File.ReadAllText(_options.SnapshotPath);
            using var document = JsonDocument.Parse(json);
            EnsureNoDuplicateProperties(document.RootElement, "reporting schedule snapshot");
            if (!TryGetPropertyIgnoreCase(document.RootElement, "schemaVersion", out _))
            {
                return LoadLegacyStoreState(document.RootElement);
            }

            var snapshot = JsonSerializer.Deserialize<ReportingScheduleSnapshot>(json, _jsonOptions)
                ?? throw new InvalidDataException("The reporting schedule snapshot has no root object.");
            if (!string.Equals(snapshot.SchemaVersion, SchemaVersion, StringComparison.Ordinal)
                || snapshot.Schedules is null
                || snapshot.LegacySchedules is null
                || !Sha256Digest.FixedEquals(
                    snapshot.PayloadHashSha256,
                    ComputeSnapshotHash(
                        snapshot.Schedules,
                        snapshot.LegacySchedules,
                        snapshot.LegacyArchiveReceipt)))
            {
                throw new InvalidDataException(
                    "Reporting schedule snapshot schema or canonical payload checksum is invalid.");
            }

            ValidateCurrentEntries(snapshot.Schedules);
            ValidateArchivedLegacySchedules(snapshot.LegacySchedules);
            ValidateArchiveReceipt(snapshot.LegacySchedules, snapshot.LegacyArchiveReceipt);
            return new ReportingScheduleStoreState(snapshot, RequiresExplicitArchive: false);
        }
        catch (Exception ex) when (ex is IOException
            or JsonException
            or InvalidDataException
            or ArgumentException
            or UnauthorizedAccessException
            or FormatException)
        {
            _logger.LogCritical(
                ex,
                "Reporting schedule snapshot at {SnapshotPath} is unreadable or failed canonical integrity validation; execution is blocked.",
                _options.SnapshotPath);
            throw new ReportingStateCorruptionException(_options.SnapshotPath, ex);
        }
    }

    private ReportingScheduleStoreState LoadLegacyStoreState(JsonElement root)
    {
        EnsureLegacyRootShape(root, "schedules", "reporting schedule snapshot");
        if (!TryGetPropertyIgnoreCase(root, "schedules", out var schedulesElement)
            || schedulesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Legacy reporting schedule snapshot has no schedule collection.");
        }

        var legacySchedules = schedulesElement
            .EnumerateArray()
            .Select(CreateLegacyScheduleEntry)
            .ToArray();
        ValidateArchivedLegacySchedules(legacySchedules);
        return new ReportingScheduleStoreState(
            new ReportingScheduleSnapshot(
                SchemaVersion,
                [],
                legacySchedules,
                ComputeSnapshotHash([], legacySchedules, archiveReceipt: null),
                LegacyArchiveReceipt: null),
            RequiresExplicitArchive: true);
    }

    private LegacyReportingScheduleEntry CreateLegacyScheduleEntry(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "Legacy reporting schedule snapshot contains a non-object schedule.");
        }

        EnsureNoDuplicateProperties(element, "legacy reporting schedule");
        var rawPayload = element.GetRawText();
        var schedule = JsonSerializer.Deserialize<ReportingScheduleRecordDto>(rawPayload, _jsonOptions)
            ?? throw new InvalidDataException("Legacy reporting schedule deserialized to null.");
        ValidateLegacySchedule(schedule);
        return new LegacyReportingScheduleEntry(
            LegacySchemaVersion,
            schedule.ScheduleId.Trim(),
            NormalizeOptional(schedule.TenantId),
            NormalizeOptional(schedule.CompanyId),
            rawPayload,
            Sha256Digest.ComputeUtf8(rawPayload),
            Sha256Digest.ComputeUtf8(CanonicalizeJson(rawPayload)),
            LegacyScheduleRemediation);
    }

    private void ValidateCurrentEntries(IReadOnlyList<ReportingScheduleEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry is null
                || !string.Equals(entry.SchemaVersion, EntrySchemaVersion, StringComparison.Ordinal)
                || entry.Schedule is null
                || !Sha256Digest.FixedEquals(entry.PayloadHashSha256, ComputeCanonicalHash(entry.Schedule)))
            {
                throw new InvalidDataException(
                    "Reporting schedule entry schema or canonical payload checksum is invalid.");
            }
        }

        ValidateCurrentSchedules(entries.Select(static entry => entry.Schedule).ToArray());
    }

    private static void ValidateCurrentSchedules(IReadOnlyList<ReportingScheduleRecordDto> schedules)
    {
        var identities = new HashSet<ReportingScheduleIdentity>(ReportingScheduleIdentityComparer.Instance);
        var handoffIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var schedule in schedules)
        {
            if (schedule is null)
            {
                throw new InvalidDataException("The reporting schedule snapshot contains a null schedule.");
            }

            var identity = ReportingScheduleIdentity.From(schedule);
            ValidateScheduleCore(schedule);
            if (identity.TenantId.Length == 0
                || identity.CompanyId.Length == 0
                || !identities.Add(identity))
            {
                throw new InvalidDataException(
                    $"Current reporting schedule identity '{identity.TenantId}/{identity.CompanyId}/{identity.ScheduleId}' is unscoped or duplicated.");
            }

            if (schedule.Template is null
                || string.IsNullOrWhiteSpace(schedule.Template.Name)
                || schedule.Template.Version <= 0
                || !string.Equals(schedule.Template.Name, schedule.TemplateId, StringComparison.Ordinal)
                || schedule.AccessPolicySnapshot is null
                || !ReportingScheduleService.HasValidAccessPolicySnapshot(schedule)
                || !ReportingScheduleService.HasValidScheduledExecutionPrincipal(schedule)
                || !ReportingScheduleService.HasValidDeliveryTargetsSnapshot(schedule)
                || schedule.State == ReportingScheduleStateDto.Active
                && !HasValidRunParameters(schedule.RunParameters))
            {
                throw new InvalidDataException(
                    $"Reporting schedule '{schedule.ScheduleId}' has incomplete template, canonical parameters, access-policy, or execution-principal binding.");
            }

            ValidateDeliveryTargets(
                schedule,
                requireTypedRecipient: schedule.State == ReportingScheduleStateDto.Active);

            foreach (var handoff in schedule.ReleaseDeliveryHandoffs ?? [])
            {
                ValidateHandoff(schedule, handoff, requireTypedRecipient: true);
                if (!handoffIds.Add(handoff.HandoffId))
                {
                    throw new InvalidDataException(
                        $"The reporting schedule snapshot contains duplicate release handoff '{handoff.HandoffId}'.");
                }
            }
        }
    }

    private void ValidateArchivedLegacySchedules(IReadOnlyList<LegacyReportingScheduleEntry> entries)
    {
        var identities = new HashSet<ReportingScheduleIdentity>(ReportingScheduleIdentityComparer.Instance);
        var handoffIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry is null
                || !string.Equals(entry.SourceSchemaVersion, LegacySchemaVersion, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(entry.RecordId)
                || (string.IsNullOrWhiteSpace(entry.TenantId) != string.IsNullOrWhiteSpace(entry.CompanyId))
                || string.IsNullOrWhiteSpace(entry.RawPayloadJson)
                || !string.Equals(entry.Remediation, LegacyScheduleRemediation, StringComparison.Ordinal)
                || !Sha256Digest.FixedEquals(entry.RawPayloadHashSha256, Sha256Digest.ComputeUtf8(entry.RawPayloadJson))
                || !Sha256Digest.FixedEquals(
                    entry.CanonicalPayloadHashSha256,
                    Sha256Digest.ComputeUtf8(CanonicalizeJson(entry.RawPayloadJson))))
            {
                throw new InvalidDataException(
                    "Archived legacy reporting schedule inventory checksum or schema is invalid.");
            }

            var schedule = JsonSerializer.Deserialize<ReportingScheduleRecordDto>(entry.RawPayloadJson, _jsonOptions)
                ?? throw new InvalidDataException("Archived legacy reporting schedule deserialized to null.");
            ValidateLegacySchedule(schedule);
            var identity = ReportingScheduleIdentity.From(schedule);
            if (!string.Equals(entry.RecordId, schedule.ScheduleId.Trim(), StringComparison.Ordinal)
                || !string.Equals(entry.TenantId, NormalizeOptional(schedule.TenantId), StringComparison.Ordinal)
                || !string.Equals(entry.CompanyId, NormalizeOptional(schedule.CompanyId), StringComparison.Ordinal)
                || !identities.Add(identity))
            {
                throw new InvalidDataException(
                    $"Archived legacy reporting schedule identity '{entry.RecordId}' is invalid or duplicated.");
            }

            foreach (var handoff in schedule.ReleaseDeliveryHandoffs ?? [])
            {
                ValidateHandoff(schedule, handoff, requireTypedRecipient: false);
                if (!handoffIds.Add(handoff.HandoffId))
                {
                    throw new InvalidDataException(
                        $"Archived legacy reporting schedule contains duplicate release handoff '{handoff.HandoffId}'.");
                }
            }
        }
    }

    private static void ValidateLegacySchedule(ReportingScheduleRecordDto schedule)
    {
        ValidateScheduleCore(schedule);
        _ = ReportingScheduleIdentity.From(schedule);
        ValidateDeliveryTargets(schedule, requireTypedRecipient: false);
        if (!string.IsNullOrWhiteSpace(schedule.AccessPolicySnapshotHash)
            && !ReportingScheduleService.HasValidAccessPolicySnapshot(schedule))
        {
            throw new InvalidDataException(
                $"Legacy reporting schedule '{schedule.ScheduleId}' has a mismatched retained access-policy checksum.");
        }
        if (!string.IsNullOrWhiteSpace(schedule.DeliveryTargetsSnapshotHash)
            && !ReportingScheduleService.HasValidDeliveryTargetsSnapshot(schedule))
        {
            throw new InvalidDataException(
                $"Legacy reporting schedule '{schedule.ScheduleId}' has a mismatched retained delivery-target checksum.");
        }
    }

    private static void ValidateScheduleCore(ReportingScheduleRecordDto schedule)
    {
        if (schedule is null
            || string.IsNullOrWhiteSpace(schedule.ScheduleId)
            || string.IsNullOrWhiteSpace(schedule.TemplateId)
            || string.IsNullOrWhiteSpace(schedule.CronExpression)
            || string.IsNullOrWhiteSpace(schedule.RequestedBy)
            || schedule.NextAsOfDate == default
            || schedule.DueAtUtc == default
            || schedule.DueAtUtc.Offset != TimeSpan.Zero
            || schedule.CreatedAtUtc == default
            || schedule.CreatedAtUtc.Offset != TimeSpan.Zero
            || schedule.UpdatedAtUtc == default
            || schedule.UpdatedAtUtc.Offset != TimeSpan.Zero
            || schedule.UpdatedAtUtc < schedule.CreatedAtUtc
            || schedule.LastRunAtUtc is { } lastRunAtUtc
            && (lastRunAtUtc == default || lastRunAtUtc.Offset != TimeSpan.Zero)
            || schedule.MaxRetries < 0
            || schedule.RunCount < 0
            || !Enum.IsDefined(schedule.State))
        {
            throw new InvalidDataException("Reporting schedule is structurally invalid.");
        }

        if (!CronExpressionParser.TryParse(schedule.CronExpression, out var cronSchedule)
            || cronSchedule.GetNextOccurrenceOrNull(schedule.DueAtUtc, TimeZoneInfo.Utc) is null)
        {
            throw new InvalidDataException(
                $"Reporting schedule '{schedule.ScheduleId}' has an invalid cron expression or no future UTC occurrence.");
        }
    }

    private static bool HasValidRunParameters(ReportingRunParametersDto? parameters) =>
        parameters is not null
        && parameters.Scope is not null
        && !string.IsNullOrWhiteSpace(parameters.Scope.FundProfileId)
        && !string.IsNullOrWhiteSpace(parameters.PeriodId)
        && parameters.AsOfDate != default
        && parameters.LedgerBook is not null
        && (parameters.LedgerBook.LedgerBookId is not null
            || !string.IsNullOrWhiteSpace(parameters.LedgerBook.LedgerBookCode))
        && Enum.IsDefined(parameters.AccountingBasis)
        && !string.IsNullOrWhiteSpace(parameters.PresentationCurrency)
        && Enum.IsDefined(parameters.ConsolidationLevel)
        && Enum.IsDefined(parameters.OutputFormat)
        && Enum.IsDefined(parameters.Finality)
        && parameters.TemplateParameters.All(pair =>
            !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null);

    private static void ValidateDeliveryTargets(
        ReportingScheduleRecordDto schedule,
        bool requireTypedRecipient)
    {
        foreach (var target in schedule.DeliveryTargets ?? [])
        {
            if (target is null
                || string.IsNullOrWhiteSpace(target.DistributionId)
                || target.Formats is { } formats
                && (formats.Count == 0
                    || formats.Any(static format => !Enum.IsDefined(format))
                    || formats.Distinct().Count() != formats.Count)
                || target.DeliveryMode is { } deliveryMode && !Enum.IsDefined(deliveryMode)
                || requireTypedRecipient
                && (string.IsNullOrWhiteSpace(target.RecipientPrincipalId)
                    || target.RecipientPrincipalKind is not { } recipientKind
                    || !Enum.IsDefined(recipientKind)))
            {
                throw new InvalidDataException(
                    $"Reporting schedule '{schedule.ScheduleId}' contains an invalid or untyped delivery target.");
            }
        }
    }

    private static void ValidateHandoff(
        ReportingScheduleRecordDto schedule,
        ReportingScheduledReleaseHandoffDto handoff,
        bool requireTypedRecipient)
    {
        if (handoff is null
            || string.IsNullOrWhiteSpace(handoff.HandoffId)
            || string.IsNullOrWhiteSpace(handoff.RunId)
            || string.IsNullOrWhiteSpace(handoff.DistributionId)
            || string.IsNullOrWhiteSpace(handoff.TargetDistributionId)
            || string.IsNullOrWhiteSpace(handoff.TransportId)
            || string.IsNullOrWhiteSpace(handoff.Subject)
            || string.IsNullOrWhiteSpace(handoff.Body)
            || requireTypedRecipient
            && (string.IsNullOrWhiteSpace(handoff.RecipientPrincipalId)
                || string.IsNullOrWhiteSpace(handoff.RecipientPrincipalKind)
                || !Enum.TryParse<ReportingAccessPrincipalKind>(
                    handoff.RecipientPrincipalKind,
                    ignoreCase: false,
                    out _))
            || requireTypedRecipient
            && (!Sha256Digest.IsCanonical(handoff.DeliveryTargetsSnapshotHash)
                || handoff.State == ReportingScheduledReleaseHandoffStateDto.PendingRelease
                && (!Sha256Digest.IsCanonical(schedule.DeliveryTargetsSnapshotHash)
                    || !Sha256Digest.FixedEquals(
                        handoff.DeliveryTargetsSnapshotHash,
                        schedule.DeliveryTargetsSnapshotHash!)))
            || handoff.Destination is null
            || handoff.Destination.Length != 0
            || handoff.RequestedFormats is not { Count: > 0 }
            || handoff.RequestedFormats.Any(static format => !Enum.IsDefined(format))
            || handoff.RequestedFormats.Distinct().Count() != handoff.RequestedFormats.Count
            || handoff.ArtifactIds is not { Count: > 0 }
            || handoff.ArtifactIds.Any(string.IsNullOrWhiteSpace)
            || handoff.ArtifactIds.Distinct(StringComparer.Ordinal).Count() != handoff.ArtifactIds.Count
            || !Enum.IsDefined(handoff.State)
            || handoff.MaxAttempts <= 0
            || handoff.CreatedAtUtc == default
            || handoff.CreatedAtUtc.Offset != TimeSpan.Zero
            || !string.Equals(handoff.TenantId, schedule.TenantId, StringComparison.Ordinal)
            || !string.Equals(handoff.CompanyId, schedule.CompanyId, StringComparison.Ordinal)
            || !string.Equals(handoff.ScheduleId, schedule.ScheduleId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(handoff.TemplateId, schedule.TemplateId, StringComparison.Ordinal)
            || ContainsPersistedBearer(handoff.Destination)
            || ContainsPersistedBearer(handoff.Subject)
            || ContainsPersistedBearer(handoff.Body))
        {
            throw new InvalidDataException(
                $"Reporting schedule '{schedule.ScheduleId}' contains an invalid release-delivery handoff.");
        }

        var isEnqueued = handoff.State == ReportingScheduledReleaseHandoffStateDto.Enqueued;
        var hasDeliveryJob = !string.IsNullOrWhiteSpace(handoff.EnqueuedDeliveryJobId);
        var hasEnqueuedAt = handoff.EnqueuedAtUtc is not null;
        if (isEnqueued && (!hasDeliveryJob
                || handoff.EnqueuedAtUtc is not { Offset: var offset }
                || offset != TimeSpan.Zero)
            || !isEnqueued && (hasDeliveryJob || hasEnqueuedAt))
        {
            throw new InvalidDataException(
                $"Release handoff '{handoff.HandoffId}' has inconsistent enqueue state.");
        }
    }

    private bool CanInspectLegacyEntry(
        LegacyReportingScheduleEntry entry,
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

        var schedule = JsonSerializer.Deserialize<ReportingScheduleRecordDto>(
            entry.RawPayloadJson,
            _jsonOptions);
        return schedule is not null
            && (recoveryAuthority.HasGlobalOverride
                || schedule.AccessPolicySnapshot is not null
                && ReportAccessPolicyEvaluator
                    .Evaluate(schedule.AccessPolicySnapshot, recoveryAuthority)
                    .IsAccessible);
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
        IReadOnlyList<LegacyReportingScheduleEntry> entries,
        ReportAccessQueryContext recoveryAuthority)
    {
        ValidateRecoveryAuthority(recoveryAuthority, requireLocalOperator: true);
        if (entries.Any(entry => !string.IsNullOrWhiteSpace(entry.TenantId)
            && !string.IsNullOrWhiteSpace(recoveryAuthority.TenantId)
            && !string.Equals(entry.TenantId, recoveryAuthority.TenantId, StringComparison.Ordinal)))
        {
            throw new UnauthorizedAccessException(
                "A tenant-bound recovery authority cannot archive another tenant's legacy reporting schedules.");
        }
    }

    private void EnsureLegacyStateArchived(ReportingScheduleStoreState state)
    {
        if (state.RequiresExplicitArchive)
        {
            throw new ReportingLegacyStateRequiresArchiveException(
                _options.SnapshotPath,
                "schedule",
                state.Snapshot.LegacySchedules.Count);
        }
    }

    private void ValidateArchiveReceipt(
        IReadOnlyList<LegacyReportingScheduleEntry> entries,
        ReportingLegacyArchiveReceipt? receipt)
    {
        if (entries.Count == 0 && receipt is null)
        {
            return;
        }

        var payloadHash = ComputeCanonicalHash(entries);
        if (receipt is null
            || !string.Equals(receipt.RecordKind, "schedule", StringComparison.Ordinal)
            || receipt.RecordCount != entries.Count
            || string.IsNullOrWhiteSpace(receipt.ActorPrincipalId)
            || string.IsNullOrWhiteSpace(receipt.Reason)
            || receipt.ArchivedAtUtc == default
            || receipt.ArchivedAtUtc.Offset != TimeSpan.Zero
            || !Sha256Digest.FixedEquals(receipt.ArchivedPayloadHashSha256, payloadHash))
        {
            throw new InvalidDataException("Legacy reporting schedule archive receipt is invalid.");
        }

        var expectedId = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new
        {
            kind = "schedule",
            actor = receipt.ActorPrincipalId,
            reason = receipt.Reason,
            archivedAtUtc = receipt.ArchivedAtUtc,
            archivedPayloadHash = receipt.ArchivedPayloadHashSha256
        }));
        if (!Sha256Digest.FixedEquals(receipt.ArchiveId, expectedId))
        {
            throw new InvalidDataException("Legacy reporting schedule archive receipt identity is invalid.");
        }
    }

    private string ComputeSnapshotHash(
        IReadOnlyList<ReportingScheduleEntry> schedules,
        IReadOnlyList<LegacyReportingScheduleEntry> legacySchedules,
        ReportingLegacyArchiveReceipt? archiveReceipt) =>
        Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new
        {
            schedules,
            legacySchedules,
            archiveReceipt
        }, _jsonOptions));

    private string ComputeCanonicalHash<T>(T value) =>
        Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(value, _jsonOptions));

    private static string CanonicalizeJson(string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        EnsureNoDuplicateProperties(document.RootElement, "legacy reporting schedule payload");
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

    private static bool ContainsPersistedBearer(string value) =>
        value.Contains("token=", StringComparison.OrdinalIgnoreCase)
        || value.Contains("access_token=", StringComparison.OrdinalIgnoreCase)
        || value.Contains("bearer=", StringComparison.OrdinalIgnoreCase)
        || value.Contains("grant=", StringComparison.OrdinalIgnoreCase)
        || value.Contains("secret=", StringComparison.OrdinalIgnoreCase);

    private sealed record ReportingScheduleSnapshot(
        string SchemaVersion,
        IReadOnlyList<ReportingScheduleEntry> Schedules,
        IReadOnlyList<LegacyReportingScheduleEntry> LegacySchedules,
        string PayloadHashSha256,
        ReportingLegacyArchiveReceipt? LegacyArchiveReceipt);

    private sealed record ReportingScheduleStoreState(
        ReportingScheduleSnapshot Snapshot,
        bool RequiresExplicitArchive);

    private sealed record ReportingScheduleEntry(
        string SchemaVersion,
        ReportingScheduleRecordDto Schedule,
        string PayloadHashSha256);

    private sealed record LegacyReportingScheduleEntry(
        string SourceSchemaVersion,
        string RecordId,
        string? TenantId,
        string? CompanyId,
        string RawPayloadJson,
        string RawPayloadHashSha256,
        string CanonicalPayloadHashSha256,
        string Remediation);
}
#pragma warning restore CS0618
