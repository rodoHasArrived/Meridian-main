using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public sealed record ReportingScheduleStoreOptions(string SnapshotPath);

public interface IReportingScheduleStore
{
    IReadOnlyList<ReportingScheduleRecordDto> Load();

    void Save(IReadOnlyList<ReportingScheduleRecordDto> schedules);
}

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

public sealed class FileReportingScheduleStore : IReportingScheduleStore
{
    private const string SchemaVersion = "meridian.reporting.schedule-store.v2";
    private const string EntrySchemaVersion = "meridian.reporting.schedule-entry.v2";
    private const string LegacySchemaVersion = "meridian.reporting.schedule-store.v1";
    private const string LegacyScheduleRemediation =
        "Read-only legacy schedule. Preserve for inventory, then recapture tenant, company, template, canonical parameters, typed access, recipient, and handoff policy before activation.";

    private readonly ReportingScheduleStoreOptions _options;
    private readonly ILogger<FileReportingScheduleStore> _logger;
    private readonly object _gate = new();
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
                ComputeSha256(JsonSerializer.Serialize(new
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
                || !FixedHashEquals(
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
            ComputeSha256(rawPayload),
            ComputeSha256(CanonicalizeJson(rawPayload)),
            LegacyScheduleRemediation);
    }

    private void ValidateCurrentEntries(IReadOnlyList<ReportingScheduleEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry is null
                || !string.Equals(entry.SchemaVersion, EntrySchemaVersion, StringComparison.Ordinal)
                || entry.Schedule is null
                || !FixedHashEquals(entry.PayloadHashSha256, ComputeCanonicalHash(entry.Schedule)))
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
                || !FixedHashEquals(entry.RawPayloadHashSha256, ComputeSha256(entry.RawPayloadJson))
                || !FixedHashEquals(
                    entry.CanonicalPayloadHashSha256,
                    ComputeSha256(CanonicalizeJson(entry.RawPayloadJson))))
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
            && (!IsSha256(handoff.DeliveryTargetsSnapshotHash)
                || handoff.State == ReportingScheduledReleaseHandoffStateDto.PendingRelease
                && (!IsSha256(schedule.DeliveryTargetsSnapshotHash)
                    || !FixedHashEquals(
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
            || !FixedHashEquals(receipt.ArchivedPayloadHashSha256, payloadHash))
        {
            throw new InvalidDataException("Legacy reporting schedule archive receipt is invalid.");
        }

        var expectedId = ComputeSha256(JsonSerializer.Serialize(new
        {
            kind = "schedule",
            actor = receipt.ActorPrincipalId,
            reason = receipt.Reason,
            archivedAtUtc = receipt.ArchivedAtUtc,
            archivedPayloadHash = receipt.ArchivedPayloadHashSha256
        }));
        if (!FixedHashEquals(receipt.ArchiveId, expectedId))
        {
            throw new InvalidDataException("Legacy reporting schedule archive receipt identity is invalid.");
        }
    }

    private string ComputeSnapshotHash(
        IReadOnlyList<ReportingScheduleEntry> schedules,
        IReadOnlyList<LegacyReportingScheduleEntry> legacySchedules,
        ReportingLegacyArchiveReceipt? archiveReceipt) =>
        ComputeSha256(JsonSerializer.Serialize(new
        {
            schedules,
            legacySchedules,
            archiveReceipt
        }, _jsonOptions));

    private string ComputeCanonicalHash<T>(T value) =>
        ComputeSha256(JsonSerializer.Serialize(value, _jsonOptions));

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool FixedHashEquals(string? left, string right)
    {
        if (!IsSha256(left) || !IsSha256(right))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(left!),
            Convert.FromHexString(right));
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 }
        && value.All(Uri.IsHexDigit)
        && string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

public sealed class ReportingScheduleService
{
    private readonly IReportingOrchestrationService _orchestrationService;
    private readonly GovernedReportingTemplateCatalog? _governedTemplateCatalog;
    private readonly ReportWriterDatasetSourceService? _datasetSourceService;
    private readonly ReportingRunReadinessService? _readinessService;
    private readonly ReportingRunCertificationService? _certificationService;
    private readonly IReportingGovernanceEndpointCoordinator? _governanceCoordinator;
    private readonly IReportingRecipientDestinationResolver _destinationResolver;
    private readonly IReportingScheduleStore? _store;
    private readonly Dictionary<ReportingScheduleIdentity, ReportingScheduleRecordDto> _schedules =
        new(ReportingScheduleIdentityComparer.Instance);
    private readonly HashSet<ReportingScheduleIdentity> _dueRunClaims =
        new(ReportingScheduleIdentityComparer.Instance);
    private readonly object _gate = new();

    public ReportingScheduleService(
        IReportingOrchestrationService orchestrationService,
        IReportingScheduleStore? store = null,
        ReportPackDeliveryService? deliveryService = null,
        GovernedReportingTemplateCatalog? governedTemplateCatalog = null,
        ReportWriterDatasetSourceService? datasetSourceService = null,
        ReportingRunReadinessService? readinessService = null,
        ReportingRunCertificationService? certificationService = null,
        IReportingGovernanceEndpointCoordinator? governanceCoordinator = null,
        IReportingRecipientDestinationResolver? destinationResolver = null)
    {
        _orchestrationService = orchestrationService ?? throw new ArgumentNullException(nameof(orchestrationService));
        // Kept in the signature for source compatibility while scheduled delivery is migrated to
        // the canonical post-release secure queue. It must never be invoked inline from generation.
        _ = deliveryService;
        _governedTemplateCatalog = governedTemplateCatalog;
        _datasetSourceService = datasetSourceService;
        _readinessService = readinessService;
        _certificationService = certificationService;
        _governanceCoordinator = governanceCoordinator;
        _destinationResolver = destinationResolver ?? new RejectingReportingRecipientDestinationResolver();
        _store = store;
        foreach (var schedule in _store?.Load() ?? [])
        {
            var identity = ReportingScheduleIdentity.From(schedule);
            if (identity.TenantId.Length > 0
                && (!HasValidAccessPolicySnapshot(schedule)
                    || !HasValidScheduledExecutionPrincipal(schedule)))
            {
                throw new InvalidDataException(
                    $"Reporting schedule '{schedule.ScheduleId}' has no valid immutable access-policy or execution-principal snapshot.");
            }
            if (!_schedules.TryAdd(identity, schedule))
            {
                throw new InvalidDataException(
                    $"Duplicate reporting schedule identity '{identity.TenantId}/{identity.CompanyId}/{identity.ScheduleId}'.");
            }
        }
    }

    public IReadOnlyList<ReportingScheduleRecordDto> ListSchedules(int limit = 100)
        => ListSchedules(accessContext: null, limit);

    public IReadOnlyList<ReportingScheduleRecordDto> ListSchedules(
        ReportAccessQueryContext? accessContext,
        int limit = 100)
    {
        lock (_gate)
        {
            return _schedules.Values
                .Where(schedule => IsScheduleInScope(schedule, accessContext))
                .Where(schedule => accessContext?.RequireBoundScope != true
                    || HasValidAccessPolicySnapshot(schedule))
                .Where(schedule => accessContext is null
                    || ReportAccessPolicyEvaluator.Evaluate(schedule.AccessPolicySnapshot, accessContext).IsAccessible)
                .OrderBy(static schedule => schedule.DueAtUtc)
                .ThenBy(static schedule => schedule.ScheduleId, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(limit, 1, 500))
                .ToArray();
        }
    }

    public ReportingScheduleRecordDto Upsert(ReportingScheduleUpsertRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Upsert(request, new ReportAccessQueryContext(request.RequestedBy));
    }

    public ReportingScheduleRecordDto Upsert(
        ReportingScheduleUpsertRequestDto request,
        ReportAccessQueryContext? accessContext)
    {
        var prepared = PrepareUpsert(
            request,
            accessContext,
            requireExplicitDeliveryRecipient: accessContext?.RequireBoundScope == true
                                              && request.State == ReportingScheduleStateDto.Active,
            allowLegacyRecipientInference: true);
        EnsureSynchronousUpsertCanCommit(prepared.Candidate, accessContext);
        return CommitPreparedUpsert(prepared);
    }

    public Task<ReportingScheduleRecordDto> UpsertAsync(
        ReportingScheduleUpsertRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return UpsertAsync(
            request,
            new ReportAccessQueryContext(request.RequestedBy),
            cancellationToken);
    }

    public async Task<ReportingScheduleRecordDto> UpsertAsync(
        ReportingScheduleUpsertRequestDto request,
        ReportAccessQueryContext? accessContext,
        CancellationToken cancellationToken = default)
    {
        var prepared = PrepareUpsert(
            request,
            accessContext,
            requireExplicitDeliveryRecipient: accessContext?.RequireBoundScope == true,
            allowLegacyRecipientInference: true);
        await ValidateExternalDeliveryTargetsAsync(prepared.Candidate, cancellationToken)
            .ConfigureAwait(false);
        return CommitPreparedUpsert(prepared);
    }

    private PreparedScheduleUpsert PrepareUpsert(
        ReportingScheduleUpsertRequestDto request,
        ReportAccessQueryContext? accessContext,
        bool requireExplicitDeliveryRecipient,
        bool allowLegacyRecipientInference)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ScheduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CronExpression);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestedBy);
        if (request.MaxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "maxRetries must be zero or greater.");
        }

        EnsureTemplateAccess(request.TemplateId, accessContext);
        EnsureBoundContext(accessContext);
        if (accessContext?.RequireBoundScope == true
            && !string.Equals(request.RequestedBy?.Trim(), accessContext.ActorPrincipalId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "The reporting schedule requester must match the authenticated actor.");
        }
        if (accessContext?.RequireBoundScope == true && request.DatasetRows is { Count: > 0 })
        {
            throw new InvalidOperationException(
                "Governed reporting schedules cannot retain caller-supplied rows; select a server-owned dataset source.");
        }

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var scheduleId = request.ScheduleId.Trim();
            var identity = BuildLookupIdentity(scheduleId, accessContext);
            var existing = _schedules.GetValueOrDefault(identity);
            if (existing is not null)
            {
                EnsureScheduleInScope(existing, accessContext);
            }
            var deliveryTargets = NormalizeDeliveryTargets(
                request.DeliveryTargets ?? existing?.DeliveryTargets);
            var datasetRows = request.DatasetRows is null
                ? existing?.DatasetRows
                : request.DatasetRows;
            var datasetSourceId = request.DatasetSourceId is null
                ? existing?.DatasetSourceId
                : NormalizeDatasetSourceId(request.DatasetSourceId);
            var brandingThemeOverride = request.BrandingThemeOverride ?? existing?.BrandingThemeOverride;
            var brandingThemeId = request.BrandingThemeOverride is not null
                ? NormalizeDatasetSourceId(request.BrandingThemeOverride.ThemeId)
                : request.BrandingThemeId is null
                    ? existing?.BrandingThemeId
                    : NormalizeDatasetSourceId(request.BrandingThemeId);
            var templateSnapshot = ResolveScheduleTemplateSnapshot(
                request.TemplateId,
                request.Template ?? existing?.Template,
                accessContext);
            var runParameters = request.RunParameters ?? existing?.RunParameters;
            if (accessContext?.RequireBoundScope == true
                && request.State == ReportingScheduleStateDto.Active
                && runParameters is null)
            {
                throw new InvalidDataException(
                    "An active governed reporting schedule requires an immutable run-parameter snapshot.");
            }
            if (accessContext?.RequireBoundScope == true && runParameters is not null)
            {
                ValidateScheduledDeliveryFormatAvailability(deliveryTargets, runParameters);
            }
            var accessPolicySnapshot = CaptureTemplateAccessPolicy(
                request.TemplateId,
                templateSnapshot,
                accessContext);
            deliveryTargets = ResolveAndValidateDeliveryRecipients(
                deliveryTargets,
                accessPolicySnapshot,
                accessContext,
                requireExplicitDeliveryRecipient,
                allowLegacyRecipientInference);
            var deliveryTargetsSnapshotHash = ComputeDeliveryTargetsSnapshotHash(deliveryTargets);
            var retainedReleaseDeliveryHandoffs = RetainReleaseDeliveryHandoffsForTargetSnapshot(
                existing,
                deliveryTargetsSnapshotHash);
            var record = new ReportingScheduleRecordDto(
                scheduleId,
                request.TemplateId.Trim(),
                request.CronExpression.Trim(),
                request.NextAsOfDate,
                request.DueAtUtc,
                request.MaxRetries,
                request.RequestedBy.Trim(),
                request.State,
                existing?.CreatedAtUtc ?? now,
                now,
                existing?.LastRunAtUtc,
                existing?.LastRunId,
                existing?.RunCount ?? 0,
                string.IsNullOrWhiteSpace(request.Description) ? existing?.Description : request.Description.Trim(),
                deliveryTargets,
                datasetRows,
                datasetSourceId,
                brandingThemeId,
                brandingThemeOverride,
                Template: templateSnapshot,
                RunParameters: runParameters,
                TenantId: existing?.TenantId ?? EmptyToNull(identity.TenantId),
                CompanyId: existing?.CompanyId ?? EmptyToNull(identity.CompanyId),
                AccessPolicySnapshot: accessPolicySnapshot,
                LastReadiness: existing?.LastReadiness,
                ReleaseDeliveryHandoffs: retainedReleaseDeliveryHandoffs,
                AccessPolicySnapshotHash: ComputeAccessPolicySnapshotHash(accessPolicySnapshot),
                DeliveryTargetsSnapshotHash: deliveryTargetsSnapshotHash);
            return new PreparedScheduleUpsert(identity, existing, record);
        }
    }

    private static IReadOnlyList<ReportingScheduledReleaseHandoffDto>? RetainReleaseDeliveryHandoffsForTargetSnapshot(
        ReportingScheduleRecordDto? existing,
        string? candidateSnapshotHash)
    {
        if (existing?.ReleaseDeliveryHandoffs is not { Count: > 0 } handoffs)
        {
            return existing?.ReleaseDeliveryHandoffs;
        }
        if (string.Equals(
                existing.DeliveryTargetsSnapshotHash,
                candidateSnapshotHash,
                StringComparison.OrdinalIgnoreCase))
        {
            return handoffs;
        }

        // A target edit is also a delivery-authority edit. Pending declarations from the old
        // snapshot must become inert immediately; only already-enqueued immutable audit history
        // survives until an explicit Cancelled handoff state is introduced.
        return handoffs
            .Where(static handoff => handoff.State == ReportingScheduledReleaseHandoffStateDto.Enqueued)
            .ToArray();
    }

    private ReportingScheduleRecordDto CommitPreparedUpsert(PreparedScheduleUpsert prepared)
    {
        lock (_gate)
        {
            var current = _schedules.GetValueOrDefault(prepared.Identity);
            if (!ReferenceEquals(current, prepared.ExpectedExisting))
            {
                throw new InvalidOperationException(
                    "The reporting schedule changed while its recipient directory bindings were being validated; retry the upsert.");
            }

            _schedules[prepared.Identity] = prepared.Candidate;
            try
            {
                PersistSchedules();
            }
            catch
            {
                if (prepared.ExpectedExisting is null)
                {
                    _schedules.Remove(prepared.Identity);
                }
                else
                {
                    _schedules[prepared.Identity] = prepared.ExpectedExisting;
                }

                throw;
            }
            return prepared.Candidate;
        }
    }

    private sealed record PreparedScheduleUpsert(
        ReportingScheduleIdentity Identity,
        ReportingScheduleRecordDto? ExpectedExisting,
        ReportingScheduleRecordDto Candidate);

    public ReportingScheduleRecordDto SetState(string scheduleId, ReportingScheduleStateDto state)
        => SetState(scheduleId, state, accessContext: null);

    public ReportingScheduleRecordDto SetState(
        string scheduleId,
        ReportingScheduleStateDto state,
        ReportAccessQueryContext? accessContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        lock (_gate)
        {
            var identity = ResolveExistingIdentity(scheduleId, accessContext);
            var current = _schedules[identity];

            EnsureScheduleInScope(current, accessContext);
            if (accessContext?.RequireBoundScope == true
                && state == ReportingScheduleStateDto.Active
                && current.RunParameters is null)
            {
                throw new InvalidDataException(
                    "An active governed reporting schedule requires an immutable run-parameter snapshot.");
            }
            if (accessContext?.RequireBoundScope == true
                && state == ReportingScheduleStateDto.Active)
            {
                if (!HasValidDeliveryTargetsSnapshot(current))
                {
                    throw new InvalidDataException(
                        "The reporting schedule delivery-target snapshot is missing or invalid; re-upsert it before activation.");
                }
                foreach (var target in current.DeliveryTargets ?? [])
                {
                    if (string.IsNullOrWhiteSpace(target.RecipientPrincipalId)
                        || target.RecipientPrincipalKind is null
                        || !Enum.IsDefined(target.RecipientPrincipalKind.Value))
                    {
                        throw new InvalidDataException(
                            $"Scheduled distribution '{target.DistributionId}' must be re-upserted with an explicit typed recipient before activation.");
                    }
                }
            }

            var updated = current with
            {
                State = state,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            _schedules[identity] = updated;
            PersistSchedules();
            return updated;
        }
    }

    public Task<ReportingScheduleRunResultDto> RunNowAsync(string scheduleId, string? requestedBy, CancellationToken ct = default) =>
        RunNowAsync(
            scheduleId,
            requestedBy,
            string.IsNullOrWhiteSpace(requestedBy) ? null : new ReportAccessQueryContext(requestedBy),
            ct);

    public async Task<ReportingScheduleRunResultDto> RunNowAsync(
        string scheduleId,
        string? requestedBy,
        ReportAccessQueryContext? accessContext,
        CancellationToken ct = default)
    {
        EnsureBoundContext(accessContext);
        if (accessContext?.RequireBoundScope == true
            && !string.IsNullOrWhiteSpace(requestedBy)
            && !string.Equals(requestedBy.Trim(), accessContext.ActorPrincipalId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "The reporting schedule run actor must match the authenticated actor.");
        }

        ReportingScheduleRecordDto schedule;
        lock (_gate)
        {
            schedule = _schedules[ResolveExistingIdentity(scheduleId, accessContext)];
        }

        EnsureScheduleAccess(schedule, accessContext);
        EnsureScheduleInScope(schedule, accessContext);
        return await RunScheduleAsync(schedule, requestedBy, isDueRun: false, accessContext, ct).ConfigureAwait(false);
    }

    public async Task<ReportingDueScheduleRunResultDto> RunDueAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
        => (await RunDueForWorkerAsync(nowUtc, ct).ConfigureAwait(false)).Result;

    internal Task<ReportingScheduleWorkerBatchResult> RunDueForWorkerAsync(
        DateTimeOffset nowUtc,
        CancellationToken ct = default) =>
        RunDueCoreAsync(
            nowUtc,
            accessContext: null,
            processAllTenants: true,
            continueAfterScheduleFailure: true,
            ct);

    public async Task<ReportingDueScheduleRunResultDto> RunDueAsync(
        DateTimeOffset nowUtc,
        ReportAccessQueryContext? accessContext,
        CancellationToken ct = default)
        => (await RunDueCoreAsync(
                nowUtc,
                accessContext,
                processAllTenants: false,
                continueAfterScheduleFailure: false,
                ct)
            .ConfigureAwait(false)).Result;

    private async Task<ReportingScheduleWorkerBatchResult> RunDueCoreAsync(
        DateTimeOffset nowUtc,
        ReportAccessQueryContext? accessContext,
        bool processAllTenants,
        bool continueAfterScheduleFailure,
        CancellationToken ct)
    {
        KeyValuePair<ReportingScheduleIdentity, ReportingScheduleRecordDto>[] dueSchedules;
        lock (_gate)
        {
            dueSchedules = _schedules
                .Where(static pair => pair.Value.State == ReportingScheduleStateDto.Active)
                .Where(pair => processAllTenants || IsScheduleInScope(pair.Value, accessContext))
                .Where(pair => pair.Value.DueAtUtc <= nowUtc)
                .Where(pair => _dueRunClaims.Add(pair.Key))
                .OrderBy(static pair => pair.Value.DueAtUtc)
                .ThenBy(static pair => pair.Value.ScheduleId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var results = new List<ReportingScheduleRunResultDto>(dueSchedules.Length);
        var failures = new List<ReportingScheduleWorkerFailure>();
        try
        {
            foreach (var pair in dueSchedules)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    results.Add(await RunScheduleAsync(
                            pair.Value,
                            requestedBy: null,
                            isDueRun: true,
                            processAllTenants ? null : accessContext,
                            ct)
                        .ConfigureAwait(false));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (continueAfterScheduleFailure)
                {
                    string? failureRecordingErrorType = null;
                    try
                    {
                        RecordDueScheduleFailure(pair.Key, exception, nowUtc);
                    }
                    catch (Exception failureRecordingException)
                    {
                        failureRecordingErrorType = failureRecordingException.GetType().Name;
                    }
                    failures.Add(new ReportingScheduleWorkerFailure(
                        pair.Key.TenantId,
                        pair.Key.CompanyId,
                        pair.Key.ScheduleId,
                        exception.GetType().Name,
                        failureRecordingErrorType));
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                foreach (var pair in dueSchedules)
                {
                    _dueRunClaims.Remove(pair.Key);
                }
            }
        }

        return new ReportingScheduleWorkerBatchResult(
            new ReportingDueScheduleRunResultDto(nowUtc, results),
            failures);
    }

    private void RecordDueScheduleFailure(
        ReportingScheduleIdentity identity,
        Exception exception,
        DateTimeOffset evaluatedAtUtc)
    {
        lock (_gate)
        {
            if (!_schedules.TryGetValue(identity, out var current))
            {
                return;
            }

            _schedules[identity] = current with
            {
                UpdatedAtUtc = evaluatedAtUtc,
                LastReadiness = exception is ReportingRunReadinessBlockedException blocked
                    ? blocked.Readiness
                    : current.LastReadiness
            };
            try
            {
                PersistSchedules();
            }
            catch
            {
                _schedules[identity] = current;
                throw;
            }
        }
    }

    public IReadOnlyList<ReportingScheduledReleaseHandoffDto> GetPendingReleaseHandoffs(
        string runId,
        ReportAccessQueryContext? accessContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        EnsureBoundContext(accessContext);
        if (accessContext?.RequireBoundScope != true)
        {
            throw new UnauthorizedAccessException(
                "Pending scheduled delivery handoffs require a bound actor, tenant, and company scope.");
        }

        lock (_gate)
        {
            return _schedules.Values
                .Where(schedule => IsScheduleInScope(schedule, accessContext))
                .Where(schedule => ReportAccessPolicyEvaluator
                    .Evaluate(schedule.AccessPolicySnapshot, accessContext)
                    .IsAccessible)
                .SelectMany(schedule => (schedule.ReleaseDeliveryHandoffs ?? [])
                    .Where(handoff => IsPendingReleaseHandoffBoundToCurrentTargets(schedule, handoff)))
                .Where(handoff => string.Equals(handoff.RunId, runId.Trim(), StringComparison.Ordinal)
                    && handoff.State == ReportingScheduledReleaseHandoffStateDto.PendingRelease)
                .OrderBy(static handoff => handoff.CreatedAtUtc)
                .ThenBy(static handoff => handoff.HandoffId, StringComparer.Ordinal)
                .ToArray();
        }
    }

    /// <summary>
    /// Infrastructure-only discovery for the release-triggered secure-distribution consumer. The
    /// consumer must reconstruct a server-owned delivery authority from each retained tenant and
    /// company scope; this method is never mapped to an HTTP endpoint.
    /// </summary>
    internal IReadOnlyList<ReportingScheduledReleaseHandoffDto> ListPendingReleaseHandoffsForWorker(
        int limit = 100) =>
        ListPendingReleaseHandoffPageForWorker(cursor: null, limit).Handoffs;

    /// <summary>
    /// Returns a stable forward-only page. Failed records remain pending, so the hosted bridge
    /// advances this cursor between polls instead of repeatedly selecting the oldest poison page.
    /// </summary>
    internal ReportingScheduledHandoffPage ListPendingReleaseHandoffPageForWorker(
        ReportingScheduledHandoffCursor? cursor,
        int limit = 100)
    {
        lock (_gate)
        {
            var handoffs = _schedules.Values
                .SelectMany(schedule => (schedule.ReleaseDeliveryHandoffs ?? [])
                    .Where(handoff => IsPendingReleaseHandoffBoundToCurrentTargets(schedule, handoff)))
                .Where(static handoff => handoff.State == ReportingScheduledReleaseHandoffStateDto.PendingRelease)
                .Where(handoff => cursor is null
                    || handoff.CreatedAtUtc > cursor.CreatedAtUtc
                    || handoff.CreatedAtUtc == cursor.CreatedAtUtc
                    && string.CompareOrdinal(handoff.HandoffId, cursor.HandoffId) > 0)
                .OrderBy(static handoff => handoff.CreatedAtUtc)
                .ThenBy(static handoff => handoff.HandoffId, StringComparer.Ordinal)
                .Take(Math.Clamp(limit, 1, 500))
                .ToArray();
            var nextCursor = handoffs.Length == 0
                ? cursor
                : new ReportingScheduledHandoffCursor(
                    handoffs[^1].CreatedAtUtc,
                    handoffs[^1].HandoffId);
            return new ReportingScheduledHandoffPage(handoffs, nextCursor);
        }
    }

    internal ReportingScheduledReleaseHandoffDto MarkReleaseHandoffEnqueuedForWorker(
        string handoffId,
        string tenantId,
        string companyId,
        string deliveryJobId,
        DateTimeOffset enqueuedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handoffId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(companyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryJobId);

        lock (_gate)
        {
            foreach (var pair in _schedules)
            {
                var schedule = pair.Value;
                if (!string.Equals(schedule.TenantId, tenantId.Trim(), StringComparison.Ordinal)
                    || !string.Equals(schedule.CompanyId, companyId.Trim(), StringComparison.Ordinal))
                {
                    continue;
                }

                var handoffs = schedule.ReleaseDeliveryHandoffs?.ToArray() ?? [];
                var index = Array.FindIndex(handoffs, handoff =>
                    string.Equals(handoff.HandoffId, handoffId.Trim(), StringComparison.Ordinal)
                    && string.Equals(handoff.TenantId, tenantId.Trim(), StringComparison.Ordinal)
                    && string.Equals(handoff.CompanyId, companyId.Trim(), StringComparison.Ordinal));
                if (index < 0)
                {
                    continue;
                }
                if (handoffs[index].State == ReportingScheduledReleaseHandoffStateDto.PendingRelease
                    && !IsPendingReleaseHandoffBoundToCurrentTargets(schedule, handoffs[index]))
                {
                    throw new InvalidOperationException(
                        "The scheduled release handoff is no longer bound to the current immutable delivery-target snapshot.");
                }

                return CompleteReleaseHandoff(
                    pair.Key,
                    schedule,
                    handoffs,
                    index,
                    deliveryJobId,
                    enqueuedAtUtc);
            }
        }

        throw new KeyNotFoundException("Scheduled release handoff was not found.");
    }

    private ReportingScheduledReleaseHandoffDto CompleteReleaseHandoff(
        ReportingScheduleIdentity identity,
        ReportingScheduleRecordDto schedule,
        ReportingScheduledReleaseHandoffDto[] handoffs,
        int index,
        string deliveryJobId,
        DateTimeOffset enqueuedAtUtc)
    {
        var current = handoffs[index];
        if (current.State == ReportingScheduledReleaseHandoffStateDto.Enqueued)
        {
            if (!string.Equals(
                    current.EnqueuedDeliveryJobId,
                    deliveryJobId.Trim(),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The scheduled release handoff was already completed by another delivery job.");
            }

            return current;
        }

        var completed = current with
        {
            State = ReportingScheduledReleaseHandoffStateDto.Enqueued,
            EnqueuedDeliveryJobId = deliveryJobId.Trim(),
            EnqueuedAtUtc = enqueuedAtUtc
        };
        handoffs[index] = completed;
        _schedules[identity] = schedule with
        {
            UpdatedAtUtc = enqueuedAtUtc,
            ReleaseDeliveryHandoffs = handoffs
        };
        try
        {
            PersistSchedules();
        }
        catch
        {
            _schedules[identity] = schedule;
            throw;
        }
        return completed;
    }

    private async Task<ReportingScheduleRunResultDto> RunScheduleAsync(
        ReportingScheduleRecordDto schedule,
        string? requestedBy,
        bool isDueRun,
        ReportAccessQueryContext? accessContext,
        CancellationToken ct)
    {
        if (schedule.State != ReportingScheduleStateDto.Active)
        {
            throw new InvalidOperationException("Only active reporting schedules can be run.");
        }

        var triggerActor = string.IsNullOrWhiteSpace(requestedBy)
            ? schedule.RequestedBy.Trim()
            : requestedBy.Trim();
        var actor = string.IsNullOrWhiteSpace(schedule.TenantId)
            ? triggerActor
            : schedule.RequestedBy.Trim();
        var effectiveAccessContext = BuildScheduledExecutionAccessContext(schedule, actor, accessContext);
        EnsureScheduleInScope(schedule, effectiveAccessContext);
        EnsureScheduleAccess(schedule, effectiveAccessContext);
        if (effectiveAccessContext.RequireBoundScope && schedule.DatasetRows is { Count: > 0 })
        {
            throw new InvalidDataException(
                "A governed reporting schedule contains caller-supplied dataset rows and cannot execute; select a server-owned dataset source.");
        }
        if (effectiveAccessContext.RequireBoundScope && schedule.RunParameters is null)
        {
            throw new InvalidDataException(
                "A governed reporting schedule has no immutable run-parameter snapshot and cannot execute.");
        }

        var scheduledParameters = schedule.RunParameters is null
            ? null
            : schedule.RunParameters with { AsOfDate = schedule.NextAsOfDate };
        var runRequest = new ReportingRunRequestDto(
            schedule.TemplateId,
            schedule.NextAsOfDate,
            schedule.MaxRetries,
            schedule.ScheduleId,
            actor,
            schedule.DatasetRows,
            schedule.DatasetSourceId,
            RetryReason: isDueRun
                ? $"scheduled due run for {schedule.CronExpression}"
                : $"manual schedule run requested by {triggerActor}",
            AllowRestatement: false,
            Template: schedule.Template,
            Parameters: scheduledParameters);
        var readiness = _readinessService is null
            ? throw new InvalidOperationException("Server reporting readiness is unavailable; scheduled execution is blocked.")
            : await _readinessService.AssessAsync(runRequest, effectiveAccessContext, ct).ConfigureAwait(false);
        var accessPolicy = schedule.AccessPolicySnapshot;
        CertifiedReportingRunContext? certified = null;
        if (effectiveAccessContext.RequireBoundScope)
        {
            var template = (_governedTemplateCatalog
                            ?? throw new InvalidOperationException("The governed reporting template catalog is unavailable."))
                .Get(readiness.ResolvedTemplate, effectiveAccessContext);
            template = template with { AccessPolicy = schedule.AccessPolicySnapshot };
            certified = await (_certificationService
                               ?? throw new InvalidOperationException("Reporting snapshot certification is unavailable; scheduled execution is blocked."))
                .CertifyAsync(template, readiness, effectiveAccessContext, ct)
                .ConfigureAwait(false);
        }

        var effectiveReadiness = certified?.Readiness ?? readiness;
        var isFinal = effectiveReadiness.ResolvedParameters.Finality == ReportingFinalityDto.Final;
        if (!effectiveReadiness.CanGenerateDraft || isFinal && !effectiveReadiness.CanGenerateFinal)
        {
            throw new ReportingRunReadinessBlockedException(effectiveReadiness);
        }

        IReadOnlyList<IReadOnlyDictionary<string, string>>? datasetRows = certified is null
            ? ResolveDatasetRows(schedule, effectiveAccessContext)
            : certified.DatasetRows;
        var datasetSourceEvidence = certified is null
            ? ResolveDatasetSourceEvidence(schedule, effectiveAccessContext)
            : new DatasetSourceEvidence(
                certified.AuthoritativeSource.SourceId,
                certified.AuthoritativeSource.SourceKind);
        var contract = new ReportingJobContract(
            effectiveAccessContext.RequireBoundScope
                ? $"{effectiveAccessContext.TenantId}-{effectiveAccessContext.CompanyId}-{schedule.ScheduleId}"
                : schedule.ScheduleId,
            schedule.TemplateId,
            schedule.NextAsOfDate,
            ReportingRunTrigger.Scheduled,
            schedule.MaxRetries,
            actor,
            DateTimeOffset.UtcNow,
            schedule.CronExpression,
            schedule.ScheduleId,
            datasetRows,
            datasetSourceEvidence.SourceId,
            datasetSourceEvidence.Label,
            BrandingThemeId: ResolveBrandingThemeId(schedule),
            BrandingTheme: schedule.BrandingThemeOverride,
            AccessPolicy: accessPolicy,
            RetryReason: isDueRun
                ? $"scheduled due run for {schedule.CronExpression}"
                : $"manual schedule run requested by {triggerActor}",
            ResolvedTemplate: effectiveReadiness.ResolvedTemplate,
            ResolvedParameters: effectiveReadiness.ResolvedParameters,
            Readiness: effectiveReadiness,
            OperationalScope: certified?.OperationalScope,
            ImmutableAccessScope: certified?.AccessScope,
            CertifiedSnapshot: certified?.Snapshot,
            AuthoritativeSource: certified?.AuthoritativeSource);
        var manifest = certified is null
            ? await _orchestrationService.ExecuteAsync(contract, ct).ConfigureAwait(false)
            : ResolveReusableScheduledManifest(contract, certified)
              ?? await _orchestrationService.ExecuteAsync(contract, ct).ConfigureAwait(false);
        if (certified is not null)
        {
            var governanceCaller = BuildScheduledGovernanceCaller(
                schedule,
                certified,
                manifest.RunId);
            var coordinator = _governanceCoordinator
                ?? throw new InvalidOperationException(
                    "Canonical reporting governance is unavailable; scheduled delivery handoff creation is blocked.");
            GovernedReportingRun governedRun;
            var recoveredExistingGovernance = false;
            try
            {
                governedRun = await coordinator
                    .GetAsync(manifest.RunId, governanceCaller, ct)
                    .ConfigureAwait(false);
                recoveredExistingGovernance = true;
            }
            catch (ReportingGovernanceNotFoundException)
            {
                governedRun = await coordinator
                    .CreateFromCompletedCertifiedManifestAsync(manifest.RunId, governanceCaller, ct)
                    .ConfigureAwait(false);
            }

            ValidateScheduledGovernedRun(
                manifest,
                certified,
                governedRun,
                requireInitialDraft: !recoveredExistingGovernance);
        }

        var run = ProjectRun(manifest, _orchestrationService.GetAudit(manifest.RunId));
        var handoffs = BuildReleaseDeliveryHandoffs(schedule, manifest);
        var advanced = AdvanceSchedule(schedule, manifest, effectiveReadiness.ResolvedParameters) with
        {
            LastReadiness = effectiveReadiness,
            ReleaseDeliveryHandoffs = handoffs
        };
        var warnings = handoffs
            .Where(handoff => string.Equals(handoff.RunId, manifest.RunId, StringComparison.Ordinal)
                && handoff.State == ReportingScheduledReleaseHandoffStateDto.PendingRelease)
            .Select(handoff =>
                $"Delivery target '{handoff.TargetDistributionId}' is awaiting governed release of run '{manifest.RunId}'.")
            .ToArray();
        lock (_gate)
        {
            var identity = ReportingScheduleIdentity.From(advanced);
            var prior = _schedules.GetValueOrDefault(identity);
            _schedules[identity] = advanced;
            try
            {
                PersistSchedules();
            }
            catch
            {
                if (prior is null)
                {
                    _schedules.Remove(identity);
                }
                else
                {
                    _schedules[identity] = prior;
                }
                throw;
            }
        }

        return new ReportingScheduleRunResultDto(advanced, run, [], warnings);
    }

    private ReportingOutputManifest? ResolveReusableScheduledManifest(
        ReportingJobContract contract,
        CertifiedReportingRunContext certified)
    {
        var runSeriesId = $"{contract.JobId}-{contract.AsOfDate:yyyyMMdd}";
        ReportingOutputManifest? reusable = null;
        const int maximumProbeCount = 10_000;
        for (var ordinal = 1; ordinal <= maximumProbeCount; ordinal++)
        {
            var runId = ordinal == 1 ? runSeriesId : $"{runSeriesId}-v{ordinal}";
            var existing = _orchestrationService.GetManifest(runId);
            if (existing is null)
            {
                return reusable;
            }

            if (IsReusableScheduledManifest(existing, contract, certified))
            {
                reusable = existing;
            }
        }

        throw new InvalidDataException(
            $"Scheduled reporting series '{runSeriesId}' exceeds the safe restart-idempotency probe limit.");
    }

    private static bool IsReusableScheduledManifest(
        ReportingOutputManifest existing,
        ReportingJobContract contract,
        CertifiedReportingRunContext certified) =>
        existing.Status == ReportingRunStatus.Draft
            && existing.Trigger == ReportingRunTrigger.Scheduled
            && string.Equals(existing.ScheduleId, contract.ScheduleId, StringComparison.Ordinal)
            && existing.OperationalScope is not null
            && existing.ImmutableAccessScope is not null
            && existing.CertifiedSnapshot is not null
            && existing.AuthoritativeSource is not null
            && string.Equals(existing.OperationalScope.TenantId, certified.OperationalScope.TenantId, StringComparison.Ordinal)
            && string.Equals(existing.OperationalScope.CompanyId, certified.OperationalScope.CompanyId, StringComparison.Ordinal)
            && string.Equals(existing.ImmutableAccessScope.PolicyHash, certified.AccessScope.PolicyHash, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existing.CertifiedSnapshot.SnapshotId, certified.Snapshot.SnapshotId, StringComparison.Ordinal)
            && string.Equals(existing.CertifiedSnapshot.SnapshotHash, certified.Snapshot.SnapshotHash, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existing.AuthoritativeSource.CheckpointId, certified.AuthoritativeSource.CheckpointId, StringComparison.Ordinal)
            && string.Equals(existing.AuthoritativeSource.CheckpointHash, certified.AuthoritativeSource.CheckpointHash, StringComparison.OrdinalIgnoreCase);

    internal static IReadOnlyList<ReportingScheduledReleaseHandoffDto> BuildReleaseDeliveryHandoffs(
        ReportingScheduleRecordDto schedule,
        ReportingOutputManifest manifest)
    {
        var targets = schedule.DeliveryTargets ?? [];
        var existingForRun = (schedule.ReleaseDeliveryHandoffs ?? [])
            .Where(handoff => string.Equals(handoff.RunId, manifest.RunId, StringComparison.Ordinal))
            .ToDictionary(static handoff => handoff.HandoffId, StringComparer.Ordinal);
        var retained = (schedule.ReleaseDeliveryHandoffs ?? [])
            .Where(handoff => !string.Equals(handoff.RunId, manifest.RunId, StringComparison.Ordinal))
            .ToList();
        if (targets.Count == 0)
        {
            return retained;
        }
        if (!HasValidDeliveryTargetsSnapshot(schedule))
        {
            throw new InvalidDataException(
                "Scheduled delivery handoff requires a valid immutable delivery-target snapshot.");
        }

        if (manifest.OperationalScope is null
            || manifest.ImmutableAccessScope is null
            || string.IsNullOrWhiteSpace(schedule.TenantId)
            || string.IsNullOrWhiteSpace(schedule.CompanyId)
            || !string.Equals(manifest.OperationalScope.TenantId, schedule.TenantId, StringComparison.Ordinal)
            || !string.Equals(manifest.OperationalScope.CompanyId, schedule.CompanyId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Scheduled delivery handoff requires a certified run bound to the schedule tenant and company.");
        }

        var createdAtUtc = DateTimeOffset.UtcNow;
        foreach (var target in targets)
        {
            var artifactSelection = ResolveScheduledArtifactSelection(target, manifest);
            var deliveryMode = target.DeliveryMode
                ?? throw new InvalidDataException(
                    $"Scheduled distribution '{target.DistributionId}' has no immutable effective delivery mode.");
            var transportId = deliveryMode == ReportPackDeliveryModeDto.EmailLink
                ? "http-relay"
                : "secure-portal";
            var recipient = ResolveScheduledRecipientPrincipal(
                target,
                manifest.ImmutableAccessScope);
            var handoffId = BuildReleaseHandoffId(
                schedule.TenantId,
                schedule.CompanyId,
                schedule.ScheduleId,
                manifest.RunId,
                target.DistributionId,
                recipient.Kind,
                recipient.PrincipalId);
            var distributionId = $"scheduled:{handoffId}";
            var declaration = new ReportingScheduledReleaseHandoffDto(
                handoffId,
                schedule.TenantId,
                schedule.CompanyId,
                schedule.ScheduleId,
                manifest.RunId,
                manifest.TemplateId,
                distributionId,
                target.DistributionId,
                transportId,
                recipient.PrincipalId,
                string.Empty,
                $"Released Meridian report {manifest.TemplateId}",
                $"Governed scheduled report run {manifest.RunId} is available through Meridian secure reporting.",
                artifactSelection.Formats,
                artifactSelection.ArtifactIds,
                GrantLifetimeSeconds: transportId == "http-relay" ? 1_800 : null,
                GrantMaxUses: transportId == "http-relay" ? 1 : null,
                MaxAttempts: Math.Max(1, schedule.MaxRetries + 1),
                CreatedAtUtc: createdAtUtc,
                RecipientPrincipalKind: recipient.Kind.ToString(),
                DeliveryTargetsSnapshotHash: schedule.DeliveryTargetsSnapshotHash);
            if (existingForRun.TryGetValue(handoffId, out var existing))
            {
                if (!SameReleaseHandoffDeclaration(existing, declaration))
                {
                    throw new InvalidDataException(
                        $"Scheduled release handoff '{handoffId}' conflicts with its retained immutable declaration.");
                }

                retained.Add(existing);
            }
            else
            {
                retained.Add(declaration);
            }
        }

        var pending = retained
            .Where(static handoff => handoff.State == ReportingScheduledReleaseHandoffStateDto.PendingRelease);
        var completed = retained
            .Where(static handoff => handoff.State == ReportingScheduledReleaseHandoffStateDto.Enqueued)
            .OrderByDescending(static handoff => handoff.EnqueuedAtUtc)
            .ThenBy(static handoff => handoff.HandoffId, StringComparer.Ordinal)
            .Take(200);
        return pending
            .Concat(completed)
            .OrderByDescending(static handoff => handoff.CreatedAtUtc)
            .ThenBy(static handoff => handoff.HandoffId, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsPendingReleaseHandoffBoundToCurrentTargets(
        ReportingScheduleRecordDto schedule,
        ReportingScheduledReleaseHandoffDto handoff)
    {
        if (handoff.State != ReportingScheduledReleaseHandoffStateDto.PendingRelease
            || !HasValidDeliveryTargetsSnapshot(schedule)
            || string.IsNullOrWhiteSpace(schedule.TenantId)
            || string.IsNullOrWhiteSpace(schedule.CompanyId)
            || string.IsNullOrWhiteSpace(handoff.DeliveryTargetsSnapshotHash)
            || !string.Equals(
                handoff.DeliveryTargetsSnapshotHash,
                schedule.DeliveryTargetsSnapshotHash,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(handoff.TenantId, schedule.TenantId, StringComparison.Ordinal)
            || !string.Equals(handoff.CompanyId, schedule.CompanyId, StringComparison.Ordinal)
            || !string.Equals(handoff.ScheduleId, schedule.ScheduleId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(handoff.TemplateId, schedule.TemplateId, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var target in schedule.DeliveryTargets ?? [])
        {
            if (!string.Equals(
                    target.DistributionId,
                    handoff.TargetDistributionId,
                    StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(target.RecipientPrincipalId)
                || target.RecipientPrincipalKind is not { } recipientKind
                || !Enum.IsDefined(recipientKind)
                || target.DeliveryMode is not { } deliveryMode
                || !Enum.IsDefined(deliveryMode))
            {
                continue;
            }

            var reportingRecipientKind = ToReportingPrincipalKind(recipientKind);
            var expectedTransportId = deliveryMode == ReportPackDeliveryModeDto.EmailLink
                ? "http-relay"
                : "secure-portal";
            if (!string.Equals(handoff.DistributionId, $"scheduled:{handoff.HandoffId}", StringComparison.Ordinal)
                || !string.Equals(handoff.TransportId, expectedTransportId, StringComparison.Ordinal)
                || !string.Equals(
                    handoff.RecipientPrincipalId,
                    target.RecipientPrincipalId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    handoff.RecipientPrincipalKind,
                    reportingRecipientKind.ToString(),
                    StringComparison.Ordinal)
                || expectedTransportId == "http-relay"
                    && (handoff.GrantLifetimeSeconds != 1_800 || handoff.GrantMaxUses != 1)
                || expectedTransportId != "http-relay"
                    && (handoff.GrantLifetimeSeconds is not null || handoff.GrantMaxUses is not null))
            {
                continue;
            }

            if (target.Formats is { Count: > 0 })
            {
                var retainedFormats = (handoff.RequestedFormats ?? [])
                    .Distinct()
                    .OrderBy(static format => format);
                var targetFormats = target.Formats
                    .Distinct()
                    .OrderBy(static format => format);
                if (!targetFormats.SequenceEqual(retainedFormats))
                {
                    continue;
                }
            }

            return true;
        }

        return false;
    }

    internal static ReportingScheduledArtifactSelection ResolveScheduledArtifactSelection(
        ReportingScheduleDeliveryTargetDto target,
        ReportingOutputManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(manifest);
        var declarations = ReportingArtifactDeclaration.Build(manifest);
        var primary = declarations.Single(static artifact =>
            artifact.Kind == ReportingDeclaredArtifactKind.PrimaryOutput);
        GovernanceReportArtifactFormatDto[] formats = target.Formats is { Count: > 0 }
            ? target.Formats
                .Distinct()
                .OrderBy(static format => format)
                .ToArray()
            : [ResolveArtifactFormat(primary)];
        if (formats.Any(static format => !Enum.IsDefined(format)))
        {
            throw new InvalidDataException(
                $"Scheduled distribution '{target.DistributionId}' requests an unknown artifact format.");
        }

        var primaryFormat = ResolveArtifactFormat(primary);
        if (formats.Length != 1 || formats[0] != primaryFormat)
        {
            throw new InvalidDataException(
                $"Scheduled distribution '{target.DistributionId}' requests unavailable output format(s) for run '{manifest.RunId}'; the exact primary output is {primaryFormat}.");
        }

        return new ReportingScheduledArtifactSelection(formats, [primary.ArtifactId]);
    }

    private static GovernanceReportArtifactFormatDto ResolveArtifactFormat(
        ReportingDeclaredArtifact artifact) =>
        artifact.ContentType switch
        {
            "application/pdf" => GovernanceReportArtifactFormatDto.Pdf,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" =>
                GovernanceReportArtifactFormatDto.Xlsx,
            "text/csv" => GovernanceReportArtifactFormatDto.Csv,
            "text/html" => GovernanceReportArtifactFormatDto.Html,
            _ when artifact.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase) =>
                GovernanceReportArtifactFormatDto.Json,
            _ => throw new InvalidDataException(
                $"Scheduled primary artifact '{artifact.ArtifactId}' has unsupported content type '{artifact.ContentType}'.")
        };

    private static bool SameReleaseHandoffDeclaration(
        ReportingScheduledReleaseHandoffDto retained,
        ReportingScheduledReleaseHandoffDto candidate) =>
        string.Equals(retained.HandoffId, candidate.HandoffId, StringComparison.Ordinal)
        && string.Equals(retained.TenantId, candidate.TenantId, StringComparison.Ordinal)
        && string.Equals(retained.CompanyId, candidate.CompanyId, StringComparison.Ordinal)
        && string.Equals(retained.ScheduleId, candidate.ScheduleId, StringComparison.Ordinal)
        && string.Equals(retained.RunId, candidate.RunId, StringComparison.Ordinal)
        && string.Equals(retained.TemplateId, candidate.TemplateId, StringComparison.Ordinal)
        && string.Equals(retained.DistributionId, candidate.DistributionId, StringComparison.Ordinal)
        && string.Equals(retained.TargetDistributionId, candidate.TargetDistributionId, StringComparison.Ordinal)
        && string.Equals(retained.TransportId, candidate.TransportId, StringComparison.Ordinal)
        && string.Equals(retained.RecipientPrincipalId, candidate.RecipientPrincipalId, StringComparison.Ordinal)
        && string.Equals(
            retained.RecipientPrincipalKind,
            candidate.RecipientPrincipalKind,
            StringComparison.Ordinal)
        && string.Equals(
            retained.DeliveryTargetsSnapshotHash,
            candidate.DeliveryTargetsSnapshotHash,
            StringComparison.OrdinalIgnoreCase)
        && string.Equals(retained.Destination, candidate.Destination, StringComparison.Ordinal)
        && string.Equals(retained.Subject, candidate.Subject, StringComparison.Ordinal)
        && string.Equals(retained.Body, candidate.Body, StringComparison.Ordinal)
        && (retained.RequestedFormats ?? []).SequenceEqual(candidate.RequestedFormats ?? [])
        && (retained.ArtifactIds ?? []).SequenceEqual(candidate.ArtifactIds ?? [], StringComparer.Ordinal)
        && retained.GrantLifetimeSeconds == candidate.GrantLifetimeSeconds
        && retained.GrantMaxUses == candidate.GrantMaxUses
        && retained.MaxAttempts == candidate.MaxAttempts;

    private static string BuildReleaseHandoffId(
        string tenantId,
        string companyId,
        string scheduleId,
        string runId,
        string targetDistributionId,
        ReportingAccessPrincipalKind recipientKind,
        string recipientPrincipalId)
    {
        var canonical = string.Join(
            "\n",
            tenantId,
            companyId,
            scheduleId,
            runId,
            targetDistributionId,
            recipientKind.ToString(),
            recipientPrincipalId.Trim());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static ReportingAccessPrincipalScope ResolveScheduledRecipientPrincipal(
        ReportingScheduleDeliveryTargetDto target,
        ReportingAccessScope access)
    {
        if (string.IsNullOrWhiteSpace(target.RecipientPrincipalId)
            || target.RecipientPrincipalKind is null)
        {
            throw new InvalidDataException(
                $"Scheduled distribution '{target.DistributionId}' has no explicit typed recipient.");
        }

        var recipient = new ReportingAccessPrincipalScope(
            ToReportingPrincipalKind(target.RecipientPrincipalKind.Value),
            target.RecipientPrincipalId.Trim());
        if (access.Mode == ReportingGovernanceAccessMode.CompanyWide)
        {
            return recipient;
        }

        var allowed = (access.Principals.IsDefault
                ? Enumerable.Empty<ReportingAccessPrincipalScope>()
                : access.Principals)
            .Concat(access.AllowOwnerAccess && !string.IsNullOrWhiteSpace(access.OwnerPrincipalId)
                ? [new ReportingAccessPrincipalScope(
                    ReportingAccessPrincipalKind.User,
                    access.OwnerPrincipalId.Trim())]
                : []);
        if (!allowed.Any(candidate =>
                candidate.Kind == recipient.Kind
                && string.Equals(
                    candidate.PrincipalId,
                    recipient.PrincipalId,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                $"Scheduled distribution '{target.DistributionId}' recipient is outside the immutable run access policy.");
        }

        return recipient;
    }

    internal static ReportingAccessPrincipalScope? ResolveScheduledRecipientPrincipal(
        ReportingAccessScope access,
        string policyOwner)
    {
        var candidates = (access.Principals.IsDefault
                ? Enumerable.Empty<ReportingAccessPrincipalScope>()
                : access.Principals)
            .Concat(access.AllowOwnerAccess && !string.IsNullOrWhiteSpace(access.OwnerPrincipalId)
                ? [new ReportingAccessPrincipalScope(
                    ReportingAccessPrincipalKind.User,
                    access.OwnerPrincipalId.Trim())]
                : [])
            .Where(principal =>
                access.Mode != ReportingGovernanceAccessMode.Private
                || principal.Kind == ReportingAccessPrincipalKind.User)
            .DistinctBy(
                static principal => $"{(int)principal.Kind}:{principal.PrincipalId.Trim()}",
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!string.IsNullOrWhiteSpace(policyOwner))
        {
            var ownerMatches = candidates
                .Where(candidate => string.Equals(
                    candidate.PrincipalId,
                    policyOwner.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (ownerMatches.Length == 1)
            {
                return ownerMatches[0];
            }
            if (ownerMatches.Length > 1)
            {
                return null;
            }
        }

        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static ReportAccessQueryContext BuildScheduledExecutionAccessContext(
        ReportingScheduleRecordDto schedule,
        string actor,
        ReportAccessQueryContext? requestContext)
    {
        if (string.IsNullOrWhiteSpace(schedule.TenantId)
            && string.IsNullOrWhiteSpace(schedule.CompanyId))
        {
            return requestContext ?? new ReportAccessQueryContext(actor);
        }

        if (!HasValidAccessPolicySnapshot(schedule)
            || !HasValidScheduledExecutionPrincipal(schedule))
        {
            throw new InvalidDataException(
                "The governed reporting schedule has no valid immutable execution authority snapshot.");
        }

        var immutableGroups = schedule.AccessPolicySnapshot!.Mode == ReportAccessModeDto.Restricted
            ? (schedule.AccessPolicySnapshot.Principals ?? [])
                .Where(static principal => principal.Kind == ReportAccessPrincipalKindDto.Group)
                .Select(static principal => principal.PrincipalId.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static principal => principal, StringComparer.Ordinal)
                .ToArray()
            : [];
        return new ReportAccessQueryContext(
            ActorPrincipalId: actor,
            GroupPrincipalIds: immutableGroups,
            CompanyId: schedule.CompanyId,
            HasGlobalOverride: false,
            TenantId: schedule.TenantId,
            RequireBoundScope: true);
    }

    private static ReportingGovernanceCallerContext BuildScheduledGovernanceCaller(
        ReportingScheduleRecordDto schedule,
        CertifiedReportingRunContext certified,
        string runId)
    {
        var actor = schedule.RequestedBy.Trim();
        var access = certified.AccessScope;
        var actorIsOwner = access.AllowOwnerAccess
            && !string.IsNullOrWhiteSpace(access.OwnerPrincipalId)
            && string.Equals(access.OwnerPrincipalId, actor, StringComparison.OrdinalIgnoreCase);
        var actorIsRestrictedPrincipal = access.Mode == ReportingGovernanceAccessMode.Restricted
            && access.Principals.Any(principal =>
                principal.Kind == ReportingAccessPrincipalKind.User
                && string.Equals(principal.PrincipalId, actor, StringComparison.OrdinalIgnoreCase));
        var actorIsPrivatePrincipal = access.Mode == ReportingGovernanceAccessMode.Private
            && access.Principals.Any(principal =>
                principal.Kind == ReportingAccessPrincipalKind.User
                && string.Equals(principal.PrincipalId, actor, StringComparison.OrdinalIgnoreCase));
        var authorized = access.Mode switch
        {
            ReportingGovernanceAccessMode.Private => actorIsOwner || actorIsPrivatePrincipal,
            ReportingGovernanceAccessMode.Restricted => actorIsOwner || actorIsRestrictedPrincipal,
            ReportingGovernanceAccessMode.CompanyWide => true,
            _ => false
        };
        if (!authorized)
        {
            throw new ReportingGovernanceAuthorizationException(
                "The persisted schedule actor is not the immutable report owner or a named restricted principal.");
        }

        return new ReportingGovernanceCallerContext(
            actor,
            certified.OperationalScope.TenantId,
            certified.OperationalScope.CompanyId,
            UserPermission.ManageReporting,
            ReportingCommandOrigin.ServicePrincipal,
            $"reporting-schedule:{schedule.ScheduleId}:{runId}",
            ImmutableArray<string>.Empty);
    }

    private static void ValidateScheduledGovernedRun(
        ReportingOutputManifest manifest,
        CertifiedReportingRunContext certified,
        GovernedReportingRun governedRun,
        bool requireInitialDraft)
    {
        ArgumentNullException.ThrowIfNull(governedRun);
        if (!string.Equals(governedRun.RunId, manifest.RunId, StringComparison.Ordinal)
            || governedRun.ExecutionState != GovernedReportingExecutionState.Succeeded
            || (requireInitialDraft && governedRun.GovernanceState != GovernedReportingState.Draft)
            || !string.Equals(governedRun.Scope.TenantId, certified.OperationalScope.TenantId, StringComparison.Ordinal)
            || !string.Equals(governedRun.Scope.CompanyId, certified.OperationalScope.CompanyId, StringComparison.Ordinal)
            || !string.Equals(governedRun.Snapshot.SnapshotId, certified.Snapshot.SnapshotId, StringComparison.Ordinal)
            || !string.Equals(governedRun.Snapshot.SnapshotHash, certified.Snapshot.SnapshotHash, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(governedRun.Access.PolicyHash, certified.AccessScope.PolicyHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportingGovernanceException(
                "Canonical governance did not retain the scheduled run as the exact certified Succeeded/Draft aggregate.");
        }
    }

    private static ReportPackDeliveryModeDto ResolveScheduledDeliveryMode(string channel)
    {
        if (channel.Contains("email", StringComparison.OrdinalIgnoreCase))
        {
            return ReportPackDeliveryModeDto.EmailLink;
        }

        if (channel.Contains("portal", StringComparison.OrdinalIgnoreCase))
        {
            return ReportPackDeliveryModeDto.SecurePortal;
        }

        if (channel.Contains("vault", StringComparison.OrdinalIgnoreCase))
        {
            return ReportPackDeliveryModeDto.EvidenceVault;
        }

        return ReportPackDeliveryModeDto.SecurePortal; // non-email scheduled handoffs use the secure-portal transport
    }

    private IReadOnlyList<IReadOnlyDictionary<string, string>>? ResolveDatasetRows(
        ReportingScheduleRecordDto schedule,
        ReportAccessQueryContext? accessContext)
    {
        if (schedule.DatasetRows is { Count: > 0 })
        {
            return schedule.DatasetRows;
        }

        var template = _governedTemplateCatalog?.Get(schedule.TemplateId, accessContext);
        if (_datasetSourceService is null || template?.ReportWriterGrids is not { Count: > 0 })
        {
            return schedule.DatasetRows;
        }

        var resolvedRows = _datasetSourceService.BuildDatasetRowsForSource(
            schedule.DatasetSourceId,
            accessContext);
        return resolvedRows.Count == 0 ? schedule.DatasetRows : resolvedRows;
    }

    private WorkstationReportingRunPayload ProjectRun(
        ReportingOutputManifest manifest,
        IReadOnlyList<ReportingRunAuditEntry> auditTrail) =>
        new(
            manifest.RunId,
            manifest.TemplateId,
            ResolveFamily(manifest.TemplateId),
            manifest.Status.ToString(),
            manifest.Trigger.ToString(),
            manifest.AsOfDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            manifest.AttemptCount,
            manifest.Sections.Length,
            manifest.Sections.Count(static section => !string.IsNullOrWhiteSpace(section.DatasetSnapshotId) && !string.IsNullOrWhiteSpace(section.ReconciliationCheckpointId)),
            manifest.Artifacts.ToArray(),
            auditTrail.Select(static audit => audit.Action).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            manifest.FailureReason,
            DrilldownLinks: [],
            NextActions: [],
            GeneratedReportWriterGrids: BuildGeneratedReportWriterGrids(manifest).ToArray(),
            ReportWriterDatasetSourceId: manifest.ReportWriterDatasetSourceId,
            ReportWriterDatasetSourceLabel: manifest.ReportWriterDatasetSourceLabel,
            ReportWriterDatasetRowCount: manifest.ReportWriterDatasetRowCount,
            RunSeriesId: ResolveRunSeriesId(manifest),
            RunAttemptOrdinal: ResolveRunAttemptOrdinal(manifest),
            PriorRunId: manifest.PriorRunId,
            RetryReason: manifest.RetryReason,
            LatestGeneratedRunId: manifest.RunId,
            IsLatestGenerated: true,
            ComparisonSummary: BuildRunComparisonSummary(manifest),
            ChangedLineCount: CountChangedLines(manifest),
            AddedLineCount: CountAddedLines(manifest),
            RemovedLineCount: CountRemovedLines(manifest));

    private static string ResolveRunSeriesId(ReportingOutputManifest manifest) =>
        string.IsNullOrWhiteSpace(manifest.RunSeriesId) ? manifest.RunId : manifest.RunSeriesId.Trim();

    private static int ResolveRunAttemptOrdinal(ReportingOutputManifest manifest) =>
        manifest.RunAttemptOrdinal is > 0 ? manifest.RunAttemptOrdinal.Value : 1;

    private static string? BuildRunComparisonSummary(ReportingOutputManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.PriorRunId))
        {
            return "First generated attempt in this run series.";
        }

        var changed = CountChangedLines(manifest);
        var added = CountAddedLines(manifest);
        var removed = CountRemovedLines(manifest);
        return manifest.ReportWriterGridDiffs.IsDefaultOrEmpty
            ? $"Compared with {manifest.PriorRunId}; no report-writer line comparison was retained."
            : $"{changed} changed, {added} added, {removed} removed lines compared with {manifest.PriorRunId}.";
    }

    private static int CountChangedLines(ReportingOutputManifest manifest) =>
        manifest.ReportWriterGridDiffs.IsDefaultOrEmpty ? 0 : manifest.ReportWriterGridDiffs.Sum(static diff => diff.ChangedRowCount);

    private static int CountAddedLines(ReportingOutputManifest manifest) =>
        manifest.ReportWriterGridDiffs.IsDefaultOrEmpty ? 0 : manifest.ReportWriterGridDiffs.Sum(static diff => diff.AddedRowCount);

    private static int CountRemovedLines(ReportingOutputManifest manifest) =>
        manifest.ReportWriterGridDiffs.IsDefaultOrEmpty ? 0 : manifest.ReportWriterGridDiffs.Sum(static diff => diff.RemovedRowCount);

    private DatasetSourceEvidence ResolveDatasetSourceEvidence(
        ReportingScheduleRecordDto schedule,
        ReportAccessQueryContext? accessContext)
    {
        var template = _governedTemplateCatalog?.Get(schedule.TemplateId, accessContext);
        if (template?.ReportWriterGrids is not { Count: > 0 })
        {
            return new DatasetSourceEvidence(null, null);
        }

        if (schedule.DatasetRows is { Count: > 0 })
        {
            return new DatasetSourceEvidence("custom-schedule-dataset", "Custom schedule dataset");
        }

        if (_datasetSourceService is null)
        {
            return new DatasetSourceEvidence(null, null);
        }

        var sourceId = string.IsNullOrWhiteSpace(schedule.DatasetSourceId)
            ? "retained-reporting-rows"
            : schedule.DatasetSourceId.Trim();
        return new DatasetSourceEvidence(
            sourceId,
            ReportWriterDatasetSourceService.GetKnownDatasetSourceLabel(sourceId) ?? sourceId);
    }

    private sealed record DatasetSourceEvidence(string? SourceId, string? Label);

    private IEnumerable<WorkstationGeneratedReportWriterGridPayload> BuildGeneratedReportWriterGrids(
        ReportingOutputManifest manifest)
    {
        if (!manifest.ReportWriterGrids.IsDefaultOrEmpty)
        {
            foreach (var grid in manifest.ReportWriterGrids)
            {
                var validation = BuildGeneratedGridValidationSummary(grid.GridId, manifest.RenderedReportWriterGrids);
                yield return new WorkstationGeneratedReportWriterGridPayload(
                    grid.GridId,
                    grid.Title,
                    grid.Kind,
                    grid.Artifact,
                    grid.DimensionCount,
                    grid.MetricCount,
                    grid.FormulaCount,
                    validation.Summary,
                    validation.Passed,
                    validation.Warning,
                    validation.Failed);
            }

            yield break;
        }

        IReadOnlyDictionary<string, ReportWriterGridDefinitionDto> templateGrids = _governedTemplateCatalog?
            .Get(manifest.TemplateId, manifest.OperationalScope)
            .ReportWriterGrids?
            .Where(static grid => !string.IsNullOrWhiteSpace(grid.GridId))
            .GroupBy(static grid => grid.GridId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, ReportWriterGridDefinitionDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var artifact in manifest.Artifacts.Where(static artifact => artifact.StartsWith("report-writer://", StringComparison.OrdinalIgnoreCase)))
        {
            var gridId = ExtractReportWriterGridId(artifact);
            if (gridId is null)
            {
                continue;
            }

            if (templateGrids.TryGetValue(gridId, out var grid))
            {
                var dimensionCount = (grid.RowFields?.Count ?? 0) + (grid.ColumnFields?.Count ?? 0);
                var validation = BuildGeneratedGridValidationSummary(grid.GridId, manifest.RenderedReportWriterGrids);
                yield return new WorkstationGeneratedReportWriterGridPayload(
                    grid.GridId.Trim(),
                    string.IsNullOrWhiteSpace(grid.Title) ? grid.GridId.Trim() : grid.Title.Trim(),
                    grid.Kind.ToString(),
                    artifact,
                    dimensionCount,
                    grid.Metrics?.Count ?? 0,
                    grid.Formulas?.Count ?? 0,
                    validation.Summary,
                    validation.Passed,
                    validation.Warning,
                    validation.Failed);
                continue;
            }

            var generatedValidation = BuildGeneratedGridValidationSummary(gridId, manifest.RenderedReportWriterGrids);
            yield return new WorkstationGeneratedReportWriterGridPayload(
                gridId,
                gridId,
                "Generated",
                artifact,
                0,
                0,
                0,
                generatedValidation.Summary,
                generatedValidation.Passed,
                generatedValidation.Warning,
                generatedValidation.Failed);
        }
    }

    private static (string? Summary, int? Passed, int? Warning, int? Failed) BuildGeneratedGridValidationSummary(
        string gridId,
        System.Collections.Immutable.ImmutableArray<ReportWriterGridRenderDto> renderedGrids)
    {
        if (renderedGrids.IsDefaultOrEmpty)
        {
            return (null, null, null, null);
        }

        var renderedGrid = renderedGrids.FirstOrDefault(grid =>
            string.Equals(grid.GridId, gridId, StringComparison.OrdinalIgnoreCase));
        if (renderedGrid is null)
        {
            return (null, null, null, null);
        }

        var checks = ResolveReportWriterGridValidationChecks(renderedGrid);
        if (checks.Count == 0)
        {
            return (null, null, null, null);
        }

        var passed = checks.Count(static check => string.Equals(check.Status, "Passed", StringComparison.OrdinalIgnoreCase));
        var warning = checks.Count(static check => string.Equals(check.Status, "Warning", StringComparison.OrdinalIgnoreCase));
        var failed = checks.Count - passed - warning;
        var summary = failed > 0
            ? $"{passed} passed / {checks.Count} checks, {failed} failed"
            : warning > 0
                ? $"{passed} passed / {checks.Count} checks, {warning} review"
                : $"{passed} passed / {checks.Count} checks";
        return (summary, passed, warning, failed);
    }

    private static IReadOnlyList<ReportWriterGridValidationCheckDto> ResolveReportWriterGridValidationChecks(
        ReportWriterGridRenderDto grid) =>
        grid.ValidationChecks is { Count: > 0 }
            ? grid.ValidationChecks
            :
            [
                new ReportWriterGridValidationCheckDto(
                    "row-count",
                    grid.Lineage is null || grid.Lineage.OutputRowCount == grid.Rows.Count ? "Passed" : "Failed",
                    grid.Lineage is null
                        ? $"Rendered {grid.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} row(s); no lineage row count was retained."
                        : $"Rendered {grid.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} row(s); lineage expected {grid.Lineage.OutputRowCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}."),
                new ReportWriterGridValidationCheckDto(
                    "column-dictionary",
                    "Passed",
                    $"Dictionary covers {grid.Columns.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} of {grid.Columns.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} rendered column(s)."),
                new ReportWriterGridValidationCheckDto(
                    "source-field-lineage",
                    grid.Lineage is { SourceFields.Count: > 0 } ? "Passed" : "Warning",
                    grid.Lineage is { SourceFields.Count: > 0 }
                        ? $"Lineage retains {grid.Lineage.SourceFields.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} source field(s)."
                        : "No source field lineage was retained with this grid."),
                new ReportWriterGridValidationCheckDto(
                    "warnings",
                    grid.Warnings.Count == 0 ? "Passed" : "Warning",
                    grid.Warnings.Count == 0
                        ? "No render warnings were retained."
                        : $"{grid.Warnings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} render warning(s) retained: {string.Join("; ", grid.Warnings)}")
            ];

    private static string? ExtractReportWriterGridId(string artifact)
    {
        const string marker = "/grids/";
        var index = artifact.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var gridId = artifact[(index + marker.Length)..].Trim();
        return string.IsNullOrWhiteSpace(gridId) ? null : gridId;
    }

    private static ReportingScheduleRecordDto AdvanceSchedule(
        ReportingScheduleRecordDto schedule,
        ReportingOutputManifest manifest,
        ReportingRunParametersDto resolvedParameters)
    {
        var nextDue = ResolveNextDue(schedule.CronExpression, schedule.DueAtUtc);
        var nextAsOfDate = schedule.NextAsOfDate.AddDays(
            Math.Max(1, (nextDue.Date - schedule.DueAtUtc.Date).Days));
        return schedule with
        {
            DueAtUtc = nextDue,
            NextAsOfDate = nextAsOfDate,
            RunParameters = resolvedParameters with { AsOfDate = nextAsOfDate },
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            LastRunAtUtc = DateTimeOffset.UtcNow,
            LastRunId = manifest.RunId,
            RunCount = schedule.RunCount + 1
        };
    }

    private static DateTimeOffset ResolveNextDue(string cronExpression, DateTimeOffset dueAtUtc)
    {
        var cron = cronExpression.Trim();
        if (cron.EndsWith(" 1", StringComparison.Ordinal) || cron.Contains(" * * 5", StringComparison.Ordinal))
        {
            return dueAtUtc.AddDays(7);
        }

        if (cron.Contains(" 1 * *", StringComparison.Ordinal))
        {
            return dueAtUtc.AddMonths(1);
        }

        var next = dueAtUtc.AddDays(1);
        if (cron.EndsWith("1-5", StringComparison.Ordinal))
        {
            while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                next = next.AddDays(1);
            }
        }

        return next;
    }

    private static string ResolveFamily(string templateId)
    {
        if (templateId.StartsWith("investor", StringComparison.OrdinalIgnoreCase))
            return ReportingTemplateFamily.InvestorStatement.ToString();
        if (templateId.StartsWith("sec", StringComparison.OrdinalIgnoreCase))
            return ReportingTemplateFamily.SecFilingPacket.ToString();
        if (templateId.StartsWith("shadow", StringComparison.OrdinalIgnoreCase))
            return ReportingTemplateFamily.ShadowNavPack.ToString();
        if (templateId.StartsWith("performance", StringComparison.OrdinalIgnoreCase))
            return ReportingTemplateFamily.PerformanceReport.ToString();
        if (templateId.StartsWith("holdings", StringComparison.OrdinalIgnoreCase))
            return ReportingTemplateFamily.HoldingsReport.ToString();
        if (templateId.StartsWith("capital", StringComparison.OrdinalIgnoreCase))
            return ReportingTemplateFamily.CapitalAccountStatement.ToString();
        if (templateId.StartsWith("board", StringComparison.OrdinalIgnoreCase))
            return ReportingTemplateFamily.BoardPacket.ToString();
        if (templateId.StartsWith("audit", StringComparison.OrdinalIgnoreCase))
            return ReportingTemplateFamily.AuditPackage.ToString();
        if (templateId.StartsWith("certified", StringComparison.OrdinalIgnoreCase))
            return ReportingTemplateFamily.CertifiedDataset.ToString();
        return ReportingTemplateFamily.CustomReport.ToString();
    }

    private static string? NormalizeDatasetSourceId(string? sourceId)
    {
        var normalized = sourceId?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? ResolveBrandingThemeId(ReportingScheduleRecordDto schedule) =>
        NormalizeDatasetSourceId(schedule.BrandingThemeOverride?.ThemeId) ?? NormalizeDatasetSourceId(schedule.BrandingThemeId);

    private VersionedReportTemplateIdDto? ResolveScheduleTemplateSnapshot(
        string templateId,
        VersionedReportTemplateIdDto? requestedSnapshot,
        ReportAccessQueryContext? accessContext)
    {
        if (accessContext?.RequireBoundScope != true)
        {
            return requestedSnapshot;
        }

        var catalog = _governedTemplateCatalog
            ?? throw new InvalidOperationException(
                "The governed reporting template catalog is unavailable; schedule version pinning is blocked.");
        if (requestedSnapshot is not null
            && !string.Equals(requestedSnapshot.Name, templateId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The reporting schedule template snapshot must match templateId.",
                nameof(requestedSnapshot));
        }

        var resolved = requestedSnapshot is null
            ? catalog.Get(templateId.Trim(), accessContext)
            : catalog.Get(requestedSnapshot, accessContext);
        var majorVersion = resolved.Version.Split('.', 2, StringSplitOptions.TrimEntries)[0];
        if (!int.TryParse(
                majorVersion,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var version)
            || version <= 0)
        {
            throw new InvalidDataException(
                $"Approved reporting template '{resolved.TemplateId}' has no valid major version snapshot.");
        }

        return new VersionedReportTemplateIdDto(resolved.TemplateId, version);
    }

    private ReportAccessPolicyDto? ResolveTemplateAccessPolicy(
        string templateId,
        VersionedReportTemplateIdDto? templateSnapshot,
        ReportAccessQueryContext? accessContext)
    {
        if (_governedTemplateCatalog is null)
        {
            return null;
        }

        try
        {
            return templateSnapshot is null
                ? _governedTemplateCatalog.Get(templateId, accessContext).AccessPolicy
                : _governedTemplateCatalog.Get(templateSnapshot, accessContext).AccessPolicy;
        }
        catch (KeyNotFoundException) when (accessContext?.RequireBoundScope != true)
        {
            return null;
        }
    }

    private ReportAccessPolicyDto CaptureTemplateAccessPolicy(
        string templateId,
        VersionedReportTemplateIdDto? templateSnapshot,
        ReportAccessQueryContext? accessContext)
    {
        var policy = ReportAccessPolicyEvaluator.Normalize(
            ResolveTemplateAccessPolicy(templateId, templateSnapshot, accessContext));
        if (accessContext?.RequireBoundScope == true
            && !string.IsNullOrWhiteSpace(policy.CompanyId)
            && !string.Equals(policy.CompanyId, accessContext.CompanyId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "The reporting schedule template belongs to another company scope.");
        }

        policy = policy.Mode == ReportAccessModeDto.CompanyWide
            && string.IsNullOrWhiteSpace(policy.CompanyId)
            && !string.IsNullOrWhiteSpace(accessContext?.CompanyId)
                ? policy with { CompanyId = accessContext.CompanyId.Trim() }
                : policy;
        if (accessContext?.RequireBoundScope != true)
        {
            return policy;
        }

        var actor = accessContext.ActorPrincipalId!.Trim();
        var withoutOverride = accessContext with { HasGlobalOverride = false };
        var directEvaluation = ReportAccessPolicyEvaluator.Evaluate(policy, withoutOverride);
        if (!directEvaluation.IsAccessible)
        {
            throw new UnauthorizedAccessException(
                "Reporting administrator override cannot create a scheduled execution delegation outside the immutable report audience.");
        }

        if (policy.Mode == ReportAccessModeDto.Private)
        {
            if (policy.AllowOwnerAccess
                && !string.IsNullOrWhiteSpace(policy.OwnerPrincipalId)
                && !string.Equals(policy.OwnerPrincipalId, actor, StringComparison.OrdinalIgnoreCase)
                && !(policy.Principals ?? []).Any(principal =>
                    principal.Kind == ReportAccessPrincipalKindDto.User
                    && string.Equals(principal.PrincipalId, actor, StringComparison.OrdinalIgnoreCase)))
            {
                throw new UnauthorizedAccessException(
                    "A private reporting schedule must be executed by its immutable owner or a named user.");
            }

            if (string.IsNullOrWhiteSpace(policy.OwnerPrincipalId))
            {
                policy = policy with { OwnerPrincipalId = actor };
            }
        }
        else if (policy.Mode == ReportAccessModeDto.Restricted)
        {
            var principals = (policy.Principals ?? [])
                .Append(new ReportAccessPrincipalDto(
                    ReportAccessPrincipalKindDto.User,
                    actor,
                    "Scheduled execution principal"))
                .ToArray();
            policy = policy with { Principals = principals };
        }

        policy = ReportAccessPolicyEvaluator.Normalize(policy);
        var issues = ReportAccessPolicyEvaluator.Validate(policy);
        if (issues.Count > 0)
        {
            throw new InvalidDataException(
                $"The reporting schedule access snapshot is invalid: {string.Join("; ", issues)}");
        }

        return policy;
    }

    private static IReadOnlyList<ReportingScheduleDeliveryTargetDto>? NormalizeDeliveryTargets(
        IReadOnlyList<ReportingScheduleDeliveryTargetDto>? targets)
    {
        if (targets is not { Count: > 0 })
        {
            return [];
        }

        return targets
            .Where(static target => !string.IsNullOrWhiteSpace(target.DistributionId))
            .GroupBy(static target => target.DistributionId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(static group =>
            {
                var target = group.First();
                ReportPackRunReadService.ReportPackDistributionPolicy policy;
                try
                {
                    policy = ReportPackRunReadService.ResolveDistributionPolicy(target.DistributionId);
                }
                catch (KeyNotFoundException ex)
                {
                    throw new ArgumentException(ex.Message, nameof(targets), ex);
                }
                if (target.DeliveryMode is { } deliveryMode && !Enum.IsDefined(deliveryMode))
                {
                    throw new ArgumentException(
                        "A reporting schedule delivery target contains an unknown delivery mode.",
                        nameof(targets));
                }
                if (target.RecipientPrincipalKind is { } principalKind && !Enum.IsDefined(principalKind))
                {
                    throw new ArgumentException(
                        "A reporting schedule delivery target contains an unknown recipient principal kind.",
                        nameof(targets));
                }

                return new ReportingScheduleDeliveryTargetDto(
                    policy.DistributionId,
                    NormalizeDeliveryFormats(target.Formats),
                    target.DeliveryMode ?? ResolveScheduledDeliveryMode(policy.Channel),
                    string.IsNullOrWhiteSpace(target.Note) ? null : target.Note.Trim(),
                    string.IsNullOrWhiteSpace(target.RecipientPrincipalId)
                        ? null
                        : target.RecipientPrincipalId.Trim(),
                    target.RecipientPrincipalKind);
            })
            .ToArray();
    }

    private static IReadOnlyList<ReportingScheduleDeliveryTargetDto>? ResolveAndValidateDeliveryRecipients(
        IReadOnlyList<ReportingScheduleDeliveryTargetDto>? targets,
        ReportAccessPolicyDto accessPolicy,
        ReportAccessQueryContext? accessContext,
        bool requireExplicitRecipient,
        bool allowLegacyInference)
    {
        if (targets is not { Count: > 0 })
        {
            return targets;
        }

        var policy = ReportAccessPolicyEvaluator.Normalize(accessPolicy);
        var allowed = ResolveNamedDeliveryPrincipals(policy);
        return targets
            .Select(target =>
            {
                var principalId = string.IsNullOrWhiteSpace(target.RecipientPrincipalId)
                    ? null
                    : target.RecipientPrincipalId.Trim();
                var principalKind = target.RecipientPrincipalKind;
                if (principalKind is { } kind && !Enum.IsDefined(kind))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(targets),
                        "Scheduled delivery recipient principal kind is invalid.");
                }

                ReportAccessPrincipalDto? recipient = null;
                if (principalId is not null && principalKind is not null)
                {
                    recipient = new ReportAccessPrincipalDto(principalKind.Value, principalId);
                }
                else if (principalId is not null
                         && principalKind is null
                         && allowLegacyInference
                         && policy.Mode != ReportAccessModeDto.CompanyWide)
                {
                    var matches = allowed
                        .Where(candidate => string.Equals(
                            candidate.PrincipalId,
                            principalId,
                            StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    recipient = matches.Length switch
                    {
                        1 => matches[0],
                        > 1 => throw new ArgumentException(
                            $"Scheduled distribution '{target.DistributionId}' recipient kind is required because the identifier appears in multiple principal namespaces.",
                            nameof(targets)),
                        _ => throw new UnauthorizedAccessException(
                            $"Scheduled distribution '{target.DistributionId}' recipient is outside the immutable access policy.")
                    };
                }
                else if (principalId is null && principalKind is null)
                {
                    if (!requireExplicitRecipient)
                    {
                        return target with
                        {
                            RecipientPrincipalId = null,
                            RecipientPrincipalKind = null
                        };
                    }
                    if (policy.Mode == ReportAccessModeDto.CompanyWide)
                    {
                        throw new ArgumentException(
                            $"Company-wide scheduled distribution '{target.DistributionId}' requires an explicit typed recipient.",
                            nameof(targets));
                    }
                    if (!allowLegacyInference || allowed.Count != 1)
                    {
                        throw new ArgumentException(
                            $"Scheduled distribution '{target.DistributionId}' requires an explicit typed recipient because its immutable access policy is ambiguous.",
                            nameof(targets));
                    }

                    recipient = allowed[0];
                }
                else
                {
                    throw new ArgumentException(
                        $"Scheduled distribution '{target.DistributionId}' requires both recipient principal id and kind.",
                        nameof(targets));
                }

                ValidateDeliveryRecipient(policy, accessContext, recipient!, target.DistributionId);
                return target with
                {
                    RecipientPrincipalId = recipient!.PrincipalId,
                    RecipientPrincipalKind = recipient.Kind
                };
            })
            .ToArray();
    }

    private static IReadOnlyList<ReportAccessPrincipalDto> ResolveNamedDeliveryPrincipals(
        ReportAccessPolicyDto policy) =>
        (policy.Principals ?? [])
        .Concat(policy.AllowOwnerAccess && !string.IsNullOrWhiteSpace(policy.OwnerPrincipalId)
            ? [new ReportAccessPrincipalDto(
                ReportAccessPrincipalKindDto.User,
                policy.OwnerPrincipalId.Trim())]
            : [])
        .GroupBy(
            static principal => $"{(int)principal.Kind}:{principal.PrincipalId.Trim()}",
            StringComparer.OrdinalIgnoreCase)
        .Select(static group =>
        {
            var principal = group.First();
            return principal with { PrincipalId = principal.PrincipalId.Trim() };
        })
        .OrderBy(static principal => principal.Kind)
        .ThenBy(static principal => principal.PrincipalId, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static void ValidateDeliveryRecipient(
        ReportAccessPolicyDto policy,
        ReportAccessQueryContext? accessContext,
        ReportAccessPrincipalDto recipient,
        string distributionId)
    {
        if (policy.Mode != ReportAccessModeDto.CompanyWide)
        {
            var allowed = ResolveNamedDeliveryPrincipals(policy);
            if (!allowed.Any(candidate =>
                    candidate.Kind == recipient.Kind
                    && string.Equals(
                        candidate.PrincipalId,
                        recipient.PrincipalId,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new UnauthorizedAccessException(
                    $"Scheduled distribution '{distributionId}' recipient is outside the immutable access policy.");
            }

            return;
        }

        var authorityMatches = recipient.Kind switch
        {
            ReportAccessPrincipalKindDto.User =>
                string.Equals(
                    accessContext?.ActorPrincipalId,
                    recipient.PrincipalId,
                    StringComparison.OrdinalIgnoreCase),
            ReportAccessPrincipalKindDto.Group =>
                (accessContext?.GroupPrincipalIds ?? []).Contains(
                    recipient.PrincipalId,
                    StringComparer.OrdinalIgnoreCase),
            ReportAccessPrincipalKindDto.Company =>
                string.Equals(
                    accessContext?.CompanyId,
                    recipient.PrincipalId,
                    StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(policy.CompanyId)
                    || string.Equals(
                        policy.CompanyId,
                        recipient.PrincipalId,
                        StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
        if (!authorityMatches)
        {
            throw new UnauthorizedAccessException(
                $"Company-wide scheduled distribution '{distributionId}' recipient must be in the authenticated principal scope.");
        }
    }

    private void EnsureSynchronousUpsertCanCommit(
        ReportingScheduleRecordDto candidate,
        ReportAccessQueryContext? accessContext)
    {
        if (accessContext?.RequireBoundScope != true)
        {
            return;
        }

        var externalTargets = (candidate.DeliveryTargets ?? [])
            .Where(IsExternalDeliveryTarget)
            .ToArray();
        if (externalTargets.Length == 0)
        {
            return;
        }
        if (candidate.State == ReportingScheduleStateDto.Active
            || externalTargets.Any(static target =>
                !string.IsNullOrWhiteSpace(target.RecipientPrincipalId)
                || target.RecipientPrincipalKind is not null))
        {
            throw new InvalidOperationException(
                "External scheduled delivery must use the asynchronous upsert path so its exact recipient directory binding is verified before persistence.");
        }
    }

    private async Task ValidateExternalDeliveryTargetsAsync(
        ReportingScheduleRecordDto candidate,
        CancellationToken cancellationToken)
    {
        var externalTargets = (candidate.DeliveryTargets ?? [])
            .Where(IsExternalDeliveryTarget)
            .ToArray();
        if (externalTargets.Length == 0)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(candidate.TenantId)
            || string.IsNullOrWhiteSpace(candidate.CompanyId))
        {
            throw new InvalidDataException(
                "External scheduled delivery requires immutable tenant and company scope.");
        }
        if (!_destinationResolver.IsConfigured)
        {
            throw new InvalidOperationException(
                "External scheduled delivery is disabled until a tenant-bound recipient destination directory is configured.");
        }

        foreach (var target in externalTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(target.RecipientPrincipalId)
                || target.RecipientPrincipalKind is null)
            {
                throw new InvalidDataException(
                    $"External scheduled distribution '{target.DistributionId}' requires an explicit typed recipient.");
            }

            var destination = await _destinationResolver.ResolveDestinationAsync(
                    new ReportingRecipientDestinationRequest(
                        candidate.TenantId,
                        candidate.CompanyId,
                        target.RecipientPrincipalId,
                        "http-relay",
                        ToReportingPrincipalKind(target.RecipientPrincipalKind.Value)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(destination))
            {
                throw new UnauthorizedAccessException(
                    $"Scheduled distribution '{target.DistributionId}' recipient has no exact server-owned destination binding for http-relay.");
            }
        }
    }

    private static bool IsExternalDeliveryTarget(ReportingScheduleDeliveryTargetDto target) =>
        target.DeliveryMode switch
        {
            ReportPackDeliveryModeDto.EmailLink => true,
            ReportPackDeliveryModeDto.SecurePortal
                or ReportPackDeliveryModeDto.EvidenceVault
                or ReportPackDeliveryModeDto.InternalRoute => false,
            _ => throw new InvalidDataException(
                $"Scheduled distribution '{target.DistributionId}' has no immutable effective delivery mode.")
        };

    private static ReportingAccessPrincipalKind ToReportingPrincipalKind(
        ReportAccessPrincipalKindDto kind) =>
        kind switch
        {
            ReportAccessPrincipalKindDto.User => ReportingAccessPrincipalKind.User,
            ReportAccessPrincipalKindDto.Group => ReportingAccessPrincipalKind.Group,
            ReportAccessPrincipalKindDto.Company => ReportingAccessPrincipalKind.Company,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static IReadOnlyList<GovernanceReportArtifactFormatDto>? NormalizeDeliveryFormats(
        IReadOnlyList<GovernanceReportArtifactFormatDto>? formats)
    {
        if (formats is not { Count: > 0 })
        {
            return null;
        }
        if (formats.Any(static format => !Enum.IsDefined(format)))
        {
            throw new ArgumentException(
                "A reporting schedule delivery target contains an unknown artifact format.",
                nameof(formats));
        }

        return formats
            .Distinct()
            .ToArray();
    }

    private static void ValidateScheduledDeliveryFormatAvailability(
        IReadOnlyList<ReportingScheduleDeliveryTargetDto>? targets,
        ReportingRunParametersDto parameters)
    {
        var primaryFormat = parameters.OutputFormat switch
        {
            ReportingOutputFormatDto.Pdf => GovernanceReportArtifactFormatDto.Pdf,
            ReportingOutputFormatDto.Xlsx => GovernanceReportArtifactFormatDto.Xlsx,
            ReportingOutputFormatDto.Csv => GovernanceReportArtifactFormatDto.Csv,
            ReportingOutputFormatDto.EvidenceVault => GovernanceReportArtifactFormatDto.Json,
            _ => throw new InvalidDataException(
                $"Reporting output format '{parameters.OutputFormat}' is unavailable for scheduled delivery.")
        };
        foreach (var target in targets ?? [])
        {
            var requested = (target.Formats ?? [])
                .Distinct()
                .ToArray();
            if (requested.Length > 0
                && (requested.Length != 1 || requested[0] != primaryFormat))
            {
                throw new InvalidDataException(
                    $"Scheduled distribution '{target.DistributionId}' requests unavailable output format(s); the exact primary output is {primaryFormat}.");
            }
        }
    }

    private void EnsureTemplateAccess(string templateId, ReportAccessQueryContext? accessContext)
    {
        var accessEvaluation = _governedTemplateCatalog?.EvaluateAccess(templateId, accessContext)
            ?? ReportAccessPolicyEvaluator.Evaluate((ReportAccessPolicyDto?)null, accessContext);
        if (!accessEvaluation.IsAccessible)
        {
            throw new UnauthorizedAccessException(accessEvaluation.Reason);
        }
    }

    private static void EnsureScheduleAccess(
        ReportingScheduleRecordDto schedule,
        ReportAccessQueryContext? accessContext)
    {
        if (accessContext?.RequireBoundScope == true && !HasValidAccessPolicySnapshot(schedule))
        {
            throw new UnauthorizedAccessException(
                "The reporting schedule has no valid immutable access-policy snapshot.");
        }

        var evaluation = ReportAccessPolicyEvaluator.Evaluate(schedule.AccessPolicySnapshot, accessContext);
        if (!evaluation.IsAccessible)
        {
            throw new UnauthorizedAccessException(evaluation.Reason);
        }
    }

    private static bool IsScheduleInScope(
        ReportingScheduleRecordDto schedule,
        ReportAccessQueryContext? accessContext)
    {
        if (accessContext is null)
        {
            // Compatibility callers without an authority scope may only see legacy unscoped
            // schedules. Returning every tenant's schedules here would turn an internal overload
            // into a cross-tenant list operation.
            return string.IsNullOrWhiteSpace(schedule.TenantId)
                && string.IsNullOrWhiteSpace(schedule.CompanyId);
        }

        var hasTenant = !string.IsNullOrWhiteSpace(accessContext.TenantId);
        var hasCompany = !string.IsNullOrWhiteSpace(accessContext.CompanyId);
        if (accessContext.RequireBoundScope
            && (string.IsNullOrWhiteSpace(accessContext.ActorPrincipalId) || !hasTenant || !hasCompany))
        {
            return false;
        }

        if (hasTenant && hasCompany)
        {
            if (string.IsNullOrWhiteSpace(schedule.TenantId) && string.IsNullOrWhiteSpace(schedule.CompanyId))
            {
                return !accessContext.RequireBoundScope;
            }

            return string.Equals(schedule.TenantId, accessContext.TenantId, StringComparison.Ordinal)
                && string.Equals(schedule.CompanyId, accessContext.CompanyId, StringComparison.Ordinal);
        }

        return !accessContext.RequireBoundScope
            && string.IsNullOrWhiteSpace(schedule.TenantId)
            && string.IsNullOrWhiteSpace(schedule.CompanyId);
    }

    private static void EnsureScheduleInScope(
        ReportingScheduleRecordDto schedule,
        ReportAccessQueryContext? accessContext)
    {
        if (!IsScheduleInScope(schedule, accessContext))
        {
            throw new UnauthorizedAccessException("The reporting schedule is outside the current tenant and company scope.");
        }
    }

    private static void EnsureBoundContext(ReportAccessQueryContext? accessContext)
    {
        if (accessContext is null || !accessContext.RequireBoundScope)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(accessContext.ActorPrincipalId)
            || string.IsNullOrWhiteSpace(accessContext.TenantId)
            || string.IsNullOrWhiteSpace(accessContext.CompanyId))
        {
            throw new UnauthorizedAccessException("A bound actor, tenant, and company scope is required for reporting schedules.");
        }
    }

    private static ReportingScheduleIdentity BuildLookupIdentity(
        string scheduleId,
        ReportAccessQueryContext? accessContext)
    {
        var hasTenant = !string.IsNullOrWhiteSpace(accessContext?.TenantId);
        var hasCompany = !string.IsNullOrWhiteSpace(accessContext?.CompanyId);
        if (hasTenant != hasCompany)
        {
            throw new UnauthorizedAccessException(
                "A reporting schedule lookup requires tenant and company scope together.");
        }

        return ReportingScheduleIdentity.Create(
            hasTenant ? accessContext!.TenantId : null,
            hasCompany ? accessContext!.CompanyId : null,
            scheduleId);
    }

    private ReportingScheduleIdentity ResolveExistingIdentity(
        string scheduleId,
        ReportAccessQueryContext? accessContext)
    {
        EnsureBoundContext(accessContext);
        var identity = BuildLookupIdentity(scheduleId.Trim(), accessContext);
        if (_schedules.ContainsKey(identity))
        {
            return identity;
        }

        throw new KeyNotFoundException("reporting schedule not found");
    }

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;

    internal static bool HasValidAccessPolicySnapshot(ReportingScheduleRecordDto schedule)
    {
        if (schedule.AccessPolicySnapshot is null
            || string.IsNullOrWhiteSpace(schedule.AccessPolicySnapshotHash))
        {
            return false;
        }

        byte[] retained;
        try
        {
            retained = Convert.FromHexString(schedule.AccessPolicySnapshotHash.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        var computed = SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(ReportAccessPolicyEvaluator.Normalize(schedule.AccessPolicySnapshot))));
        return retained.Length == computed.Length
            && CryptographicOperations.FixedTimeEquals(retained, computed);
    }

    internal static bool HasValidScheduledExecutionPrincipal(ReportingScheduleRecordDto schedule)
    {
        if (string.IsNullOrWhiteSpace(schedule.TenantId)
            && string.IsNullOrWhiteSpace(schedule.CompanyId))
        {
            return true;
        }
        if (string.IsNullOrWhiteSpace(schedule.TenantId)
            || string.IsNullOrWhiteSpace(schedule.CompanyId)
            || string.IsNullOrWhiteSpace(schedule.RequestedBy)
            || schedule.AccessPolicySnapshot is null)
        {
            return false;
        }

        var actor = schedule.RequestedBy.Trim();
        var policy = ReportAccessPolicyEvaluator.Normalize(schedule.AccessPolicySnapshot);
        return policy.Mode switch
        {
            ReportAccessModeDto.Private =>
                (policy.AllowOwnerAccess
                    && string.Equals(policy.OwnerPrincipalId, actor, StringComparison.OrdinalIgnoreCase))
                || (policy.Principals ?? []).Any(principal =>
                    principal.Kind == ReportAccessPrincipalKindDto.User
                    && string.Equals(principal.PrincipalId, actor, StringComparison.OrdinalIgnoreCase)),
            ReportAccessModeDto.Restricted =>
                (policy.AllowOwnerAccess
                    && string.Equals(policy.OwnerPrincipalId, actor, StringComparison.OrdinalIgnoreCase))
                || (policy.Principals ?? []).Any(principal =>
                    principal.Kind == ReportAccessPrincipalKindDto.User
                    && string.Equals(principal.PrincipalId, actor, StringComparison.OrdinalIgnoreCase)),
            ReportAccessModeDto.CompanyWide =>
                string.Equals(policy.CompanyId, schedule.CompanyId, StringComparison.Ordinal),
            _ => false
        };
    }

    internal static string ComputeAccessPolicySnapshotHash(ReportAccessPolicyDto policy) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(ReportAccessPolicyEvaluator.Normalize(policy)))))
            .ToLowerInvariant();

    internal static string? ComputeDeliveryTargetsSnapshotHash(
        IReadOnlyList<ReportingScheduleDeliveryTargetDto>? targets)
    {
        if (targets is not { Count: > 0 })
        {
            return null;
        }

        var canonical = targets
            .Select(static target => target with
            {
                DistributionId = target.DistributionId.Trim(),
                Formats = (target.Formats ?? [])
                    .Distinct()
                    .OrderBy(static format => format)
                    .ToArray(),
                Note = string.IsNullOrWhiteSpace(target.Note) ? null : target.Note.Trim(),
                RecipientPrincipalId = string.IsNullOrWhiteSpace(target.RecipientPrincipalId)
                    ? null
                    : target.RecipientPrincipalId.Trim()
            })
            .OrderBy(static target => target.DistributionId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(canonical))))
            .ToLowerInvariant();
    }

    internal static bool HasValidDeliveryTargetsSnapshot(ReportingScheduleRecordDto schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (schedule.DeliveryTargets is not { Count: > 0 })
        {
            return string.IsNullOrWhiteSpace(schedule.DeliveryTargetsSnapshotHash);
        }
        if (string.IsNullOrWhiteSpace(schedule.DeliveryTargetsSnapshotHash))
        {
            return false;
        }

        byte[] retained;
        try
        {
            retained = Convert.FromHexString(schedule.DeliveryTargetsSnapshotHash.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        var computed = Convert.FromHexString(ComputeDeliveryTargetsSnapshotHash(schedule.DeliveryTargets)!);
        return retained.Length == computed.Length
               && CryptographicOperations.FixedTimeEquals(retained, computed);
    }

    private void PersistSchedules()
    {
        _store?.Save(_schedules.Values.ToArray());
    }
}

public sealed record ReportingScheduleWorkerOptions(TimeSpan PollInterval)
{
    public static ReportingScheduleWorkerOptions Default { get; } = new(TimeSpan.FromMinutes(1));
}

internal sealed record ReportingScheduleWorkerFailure(
    string TenantId,
    string CompanyId,
    string ScheduleId,
    string ErrorType,
    string? FailureRecordingErrorType);

internal sealed record ReportingScheduleWorkerBatchResult(
    ReportingDueScheduleRunResultDto Result,
    IReadOnlyList<ReportingScheduleWorkerFailure> Failures);

/// <summary>
/// Server-owned schedule clock. Public due-run mutation routes are retired; this worker discovers
/// due records internally and lets <see cref="ReportingScheduleService"/> reconstruct a separate,
/// exact tenant/company authority for every scheduled run.
/// </summary>
public sealed class ReportingScheduleHostedService : BackgroundService
{
    private readonly ReportingScheduleService _scheduleService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReportingScheduleHostedService> _logger;
    private readonly ReportingScheduleWorkerOptions _options;

    public ReportingScheduleHostedService(
        ReportingScheduleService scheduleService,
        TimeProvider timeProvider,
        ILogger<ReportingScheduleHostedService> logger,
        ReportingScheduleWorkerOptions? options = null)
    {
        _scheduleService = scheduleService ?? throw new ArgumentNullException(nameof(scheduleService));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? ReportingScheduleWorkerOptions.Default;
        if (_options.PollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The reporting schedule worker poll interval must be positive.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.PollInterval);
        do
        {
            try
            {
                var batch = await _scheduleService
                    .RunDueForWorkerAsync(_timeProvider.GetUtcNow(), stoppingToken)
                    .ConfigureAwait(false);
                foreach (var failure in batch.Failures)
                {
                    _logger.LogError(
                        "Scheduled report {ScheduleId} failed closed for tenant {TenantId} and company {CompanyId} with error type {ErrorType}; failure recording error type was {FailureRecordingErrorType}; other due schedules continued.",
                        failure.ScheduleId,
                        failure.TenantId,
                        failure.CompanyId,
                        failure.ErrorType,
                        failure.FailureRecordingErrorType ?? "none");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    "Scheduled reporting cycle failed closed with error type {ErrorType}.",
                    exception.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
