using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Domain.Reconciliation;
using Meridian.Storage.Store;
using Microsoft.Extensions.Logging;

namespace Meridian.FinancialOperations.Reconciliation.Connectors;

/// <summary>A persisted scheduled-fetch configuration for a fetch-capable statement connector.</summary>
public sealed record StatementFetchSchedule(
    string ScheduleId,
    string ConnectorId,
    string ExternalAccountId,
    string FundAccountId,
    string SourceInstitution,
    string? MappingProfileId,
    string ToleranceProfileId,
    int CadenceHours,
    bool Enabled,
    DateTimeOffset? LastRunAtUtc = null,
    string? LastRunStatus = null,
    string SourceKind = "broker",
    DateTimeOffset? LastAttemptAtUtc = null,
    string? TenantId = null,
    string? CompanyId = null,
    DateOnly? PeriodStart = null,
    DateOnly? PeriodEnd = null,
    StatementAccountingScope? AccountingScope = null)
{
    public DateTimeOffset? NextDueAtUtc =>
        !Enabled ? null : (LastAttemptAtUtc ?? LastRunAtUtc)?.AddHours(Math.Max(1, CadenceHours));

    public bool IsDue(DateTimeOffset nowUtc) =>
        Enabled && (NextDueAtUtc is null || NextDueAtUtc is { } due && due <= nowUtc);
}

public sealed record StatementFetchScheduleSnapshot(
    int Version,
    IReadOnlyList<StatementFetchSchedule> Schedules);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(StatementFetchScheduleSnapshot))]
internal sealed partial class StatementFetchScheduleJsonContext : JsonSerializerContext;

public interface IStatementFetchScheduleStore
{
    Task<IReadOnlyList<StatementFetchSchedule>> ListAsync(CancellationToken ct = default);
    Task<StatementFetchSchedule> UpsertAsync(StatementFetchSchedule schedule, CancellationToken ct = default);
    Task<bool> DeleteAsync(string scheduleId, CancellationToken ct = default);
    Task RecordRunAsync(string scheduleId, DateTimeOffset ranAtUtc, string status, CancellationToken ct = default);
    Task RecordFailureAsync(
        string scheduleId,
        DateTimeOffset attemptedAtUtc,
        string status,
        CancellationToken ct = default);
}

/// <summary>
/// File-backed schedule store using the versioned-snapshot pattern with atomic writes.
/// Schedules drive the statement fetch scheduler; duplicate-key idempotency downstream
/// makes overlapping or repeated runs safe.
/// </summary>
public sealed class FileStatementFetchScheduleStore
    : JsonFileSnapshotStore<StatementFetchScheduleSnapshot>, IStatementFetchScheduleStore
{
    private const int SnapshotVersion = 1;

    private readonly ILogger<FileStatementFetchScheduleStore>? _logger;

    public FileStatementFetchScheduleStore(string dataRoot, ILogger<FileStatementFetchScheduleStore>? logger = null)
        : base(GetSnapshotPath(dataRoot), StatementFetchScheduleJsonContext.Default.StatementFetchScheduleSnapshot)
    {
        _logger = logger;
        Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
    }

    public Task<IReadOnlyList<StatementFetchSchedule>> ListAsync(CancellationToken ct = default)
        => ReadSnapshotAsync(static snapshot => snapshot.Schedules, ct);

    public async Task<StatementFetchSchedule> UpsertAsync(StatementFetchSchedule schedule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        Validate(schedule);
        var normalized = schedule with
        {
            ScheduleId = string.IsNullOrWhiteSpace(schedule.ScheduleId)
                ? Guid.NewGuid().ToString("N")
                : schedule.ScheduleId.Trim(),
            SourceKind = schedule.SourceKind.Trim().ToLowerInvariant(),
            TenantId = schedule.TenantId?.Trim(),
            CompanyId = schedule.CompanyId?.Trim(),
            LastRunAtUtc = null,
            LastRunStatus = null,
            LastAttemptAtUtc = null
        };

        return await UpdateSnapshotAsync(snapshot =>
        {
            var existing = snapshot.Schedules.FirstOrDefault(candidate =>
                string.Equals(candidate.ScheduleId, normalized.ScheduleId, StringComparison.OrdinalIgnoreCase));
            var retainedSchedule = existing is not null && HasSameRunAuthority(existing, normalized)
                ? normalized with
                {
                    LastRunAtUtc = existing.LastRunAtUtc,
                    LastRunStatus = existing.LastRunStatus,
                    LastAttemptAtUtc = existing.LastAttemptAtUtc
                }
                : normalized;

            var retained = snapshot.Schedules
                .Where(candidate => !string.Equals(candidate.ScheduleId, retainedSchedule.ScheduleId, StringComparison.OrdinalIgnoreCase))
                .Append(retainedSchedule)
                .OrderBy(static candidate => candidate.ScheduleId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return (new StatementFetchScheduleSnapshot(SnapshotVersion, retained), retainedSchedule);
        }, ct).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string scheduleId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        return await UpdateSnapshotAsync(snapshot =>
        {
            var retained = snapshot.Schedules
                .Where(candidate => !string.Equals(candidate.ScheduleId, scheduleId.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return retained.Length == snapshot.Schedules.Count
                ? (snapshot, false)
                : (new StatementFetchScheduleSnapshot(SnapshotVersion, retained), true);
        }, ct).ConfigureAwait(false);
    }

    public async Task RecordRunAsync(string scheduleId, DateTimeOffset ranAtUtc, string status, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        await UpdateSnapshotAsync(snapshot =>
        {
            var existing = snapshot.Schedules.FirstOrDefault(candidate =>
                string.Equals(candidate.ScheduleId, scheduleId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                return snapshot;
            }

            var updated = existing with
            {
                LastRunAtUtc = ranAtUtc,
                LastAttemptAtUtc = ranAtUtc,
                LastRunStatus = status
            };
            var retained = snapshot.Schedules
                .Select(candidate => string.Equals(candidate.ScheduleId, updated.ScheduleId, StringComparison.OrdinalIgnoreCase)
                    ? updated
                    : candidate)
                .ToArray();
            return new StatementFetchScheduleSnapshot(SnapshotVersion, retained);
        }, ct).ConfigureAwait(false);
    }

    public async Task RecordFailureAsync(
        string scheduleId,
        DateTimeOffset attemptedAtUtc,
        string status,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        await UpdateSnapshotAsync(snapshot =>
        {
            var existing = snapshot.Schedules.FirstOrDefault(candidate =>
                string.Equals(candidate.ScheduleId, scheduleId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                return snapshot;
            }

            var updated = existing with
            {
                LastAttemptAtUtc = attemptedAtUtc,
                LastRunStatus = status
            };
            var retained = snapshot.Schedules
                .Select(candidate => string.Equals(candidate.ScheduleId, updated.ScheduleId, StringComparison.OrdinalIgnoreCase)
                    ? updated
                    : candidate)
                .ToArray();
            return new StatementFetchScheduleSnapshot(SnapshotVersion, retained);
        }, ct).ConfigureAwait(false);
    }

    protected override StatementFetchScheduleSnapshot CreateEmptySnapshot() => new(SnapshotVersion, []);

    protected override StatementFetchScheduleSnapshot OnSnapshotLoaded(StatementFetchScheduleSnapshot snapshot)
    {
        if (snapshot.Version != SnapshotVersion)
        {
            throw new InvalidOperationException(
                $"Statement fetch schedule snapshot version {snapshot.Version} is not supported. Expected {SnapshotVersion}: {SnapshotPath}");
        }

        return snapshot;
    }

    protected override StatementFetchScheduleSnapshot HandleCorruptSnapshot(JsonException exception)
    {
        _logger?.LogWarning(exception, "Statement fetch schedule snapshot is not valid JSON: {Path}", SnapshotPath);
        throw new InvalidOperationException($"Statement fetch schedule snapshot is invalid: {SnapshotPath}", exception);
    }

    private static void Validate(StatementFetchSchedule schedule)
    {
        if (string.IsNullOrWhiteSpace(schedule.ConnectorId)
            || string.IsNullOrWhiteSpace(schedule.ExternalAccountId)
            || string.IsNullOrWhiteSpace(schedule.FundAccountId)
            || string.IsNullOrWhiteSpace(schedule.SourceInstitution))
        {
            throw new InvalidDataException("Fetch schedules require a connector id, external account id, fund account id, and source institution.");
        }

        if (schedule.CadenceHours < 1)
        {
            throw new InvalidDataException("Fetch schedule cadence must be at least one hour.");
        }

        if (!string.Equals(schedule.SourceKind, "broker", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(schedule.SourceKind, "custodian", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Fetch schedule source kind must be broker or custodian.");
        }

        if (string.IsNullOrWhiteSpace(schedule.TenantId)
            || string.IsNullOrWhiteSpace(schedule.CompanyId)
            || !schedule.PeriodStart.HasValue
            || !schedule.PeriodEnd.HasValue
            || schedule.PeriodEnd.Value < schedule.PeriodStart.Value
            || schedule.PeriodEnd.Value == DateOnly.MaxValue
            || schedule.AccountingScope is null
            || schedule.AccountingScope.AsOfDate != schedule.PeriodEnd.Value)
        {
            throw new InvalidDataException(
                "Fetch schedules require tenant, company, an exact statement period, and resolved fund/book/period accounting scope.");
        }
    }

    private static bool HasSameRunAuthority(
        StatementFetchSchedule existing,
        StatementFetchSchedule updated)
        => SameText(existing.ConnectorId, updated.ConnectorId)
           && SameText(existing.ExternalAccountId, updated.ExternalAccountId)
           && SameText(existing.FundAccountId, updated.FundAccountId)
           && SameText(existing.SourceInstitution, updated.SourceInstitution)
           && SameText(existing.MappingProfileId, updated.MappingProfileId)
           && SameText(existing.ToleranceProfileId, updated.ToleranceProfileId)
           && SameText(existing.SourceKind, updated.SourceKind)
           && SameText(existing.TenantId, updated.TenantId)
           && SameText(existing.CompanyId, updated.CompanyId)
           && existing.PeriodStart == updated.PeriodStart
           && existing.PeriodEnd == updated.PeriodEnd
           && Equals(existing.AccountingScope, updated.AccountingScope);

    private static bool SameText(string? left, string? right)
        => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string GetSnapshotPath(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        return Path.Combine(dataRoot, "reconciliation", "statement-fetch-schedules.json");
    }
}
