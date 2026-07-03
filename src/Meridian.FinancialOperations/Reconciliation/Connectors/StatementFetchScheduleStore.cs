using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Storage.Archival;
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
    string? LastRunStatus = null)
{
    public DateTimeOffset? NextDueAtUtc =>
        !Enabled ? null : LastRunAtUtc?.AddHours(Math.Max(1, CadenceHours));

    public bool IsDue(DateTimeOffset nowUtc) =>
        Enabled && (LastRunAtUtc is null || NextDueAtUtc is { } due && due <= nowUtc);
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
    Task RecordFailureAsync(string scheduleId, string status, CancellationToken ct = default);
}

/// <summary>
/// File-backed schedule store using the versioned-snapshot pattern with atomic writes.
/// Schedules drive the statement fetch scheduler; duplicate-key idempotency downstream
/// makes overlapping or repeated runs safe.
/// </summary>
public sealed class FileStatementFetchScheduleStore : IStatementFetchScheduleStore
{
    private const int SnapshotVersion = 1;

    private readonly string _snapshotPath;
    private readonly ILogger<FileStatementFetchScheduleStore>? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileStatementFetchScheduleStore(string dataRoot, ILogger<FileStatementFetchScheduleStore>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _logger = logger;
        var directory = Path.Combine(dataRoot, "reconciliation");
        Directory.CreateDirectory(directory);
        _snapshotPath = Path.Combine(directory, "statement-fetch-schedules.json");
    }

    public async Task<IReadOnlyList<StatementFetchSchedule>> ListAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await LoadCoreAsync(ct).ConfigureAwait(false);
            return snapshot.Schedules;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StatementFetchSchedule> UpsertAsync(StatementFetchSchedule schedule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        Validate(schedule);
        var normalized = schedule with
        {
            ScheduleId = string.IsNullOrWhiteSpace(schedule.ScheduleId)
                ? Guid.NewGuid().ToString("N")
                : schedule.ScheduleId.Trim()
        };

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await LoadCoreAsync(ct).ConfigureAwait(false);
            // Preserve run history across operator edits so cadence changes do not retrigger
            // an immediate fetch.
            var existing = snapshot.Schedules.FirstOrDefault(candidate =>
                string.Equals(candidate.ScheduleId, normalized.ScheduleId, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                normalized = normalized with
                {
                    LastRunAtUtc = existing.LastRunAtUtc,
                    LastRunStatus = existing.LastRunStatus
                };
            }

            var retained = snapshot.Schedules
                .Where(candidate => !string.Equals(candidate.ScheduleId, normalized.ScheduleId, StringComparison.OrdinalIgnoreCase))
                .Append(normalized)
                .OrderBy(static candidate => candidate.ScheduleId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            await PersistAsync(new StatementFetchScheduleSnapshot(SnapshotVersion, retained), ct).ConfigureAwait(false);
            return normalized;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(string scheduleId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await LoadCoreAsync(ct).ConfigureAwait(false);
            var retained = snapshot.Schedules
                .Where(candidate => !string.Equals(candidate.ScheduleId, scheduleId.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (retained.Length == snapshot.Schedules.Count)
            {
                return false;
            }

            await PersistAsync(new StatementFetchScheduleSnapshot(SnapshotVersion, retained), ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordRunAsync(string scheduleId, DateTimeOffset ranAtUtc, string status, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await LoadCoreAsync(ct).ConfigureAwait(false);
            var existing = snapshot.Schedules.FirstOrDefault(candidate =>
                string.Equals(candidate.ScheduleId, scheduleId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                return;
            }

            var updated = existing with { LastRunAtUtc = ranAtUtc, LastRunStatus = status };
            var retained = snapshot.Schedules
                .Select(candidate => string.Equals(candidate.ScheduleId, updated.ScheduleId, StringComparison.OrdinalIgnoreCase)
                    ? updated
                    : candidate)
                .ToArray();
            await PersistAsync(new StatementFetchScheduleSnapshot(SnapshotVersion, retained), ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordFailureAsync(string scheduleId, string status, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await LoadCoreAsync(ct).ConfigureAwait(false);
            var existing = snapshot.Schedules.FirstOrDefault(candidate =>
                string.Equals(candidate.ScheduleId, scheduleId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                return;
            }

            var updated = existing with { LastRunStatus = status };
            var retained = snapshot.Schedules
                .Select(candidate => string.Equals(candidate.ScheduleId, updated.ScheduleId, StringComparison.OrdinalIgnoreCase)
                    ? updated
                    : candidate)
                .ToArray();
            await PersistAsync(new StatementFetchScheduleSnapshot(SnapshotVersion, retained), ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
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
    }

    private async Task<StatementFetchScheduleSnapshot> LoadCoreAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(_snapshotPath))
        {
            return new StatementFetchScheduleSnapshot(SnapshotVersion, []);
        }

        try
        {
            await using var stream = File.OpenRead(_snapshotPath);
            var snapshot = await JsonSerializer.DeserializeAsync(
                    stream,
                    StatementFetchScheduleJsonContext.Default.StatementFetchScheduleSnapshot,
                    ct)
                .ConfigureAwait(false);
            if (snapshot is null)
            {
                return new StatementFetchScheduleSnapshot(SnapshotVersion, []);
            }

            if (snapshot.Version != SnapshotVersion)
            {
                throw new InvalidOperationException(
                    $"Statement fetch schedule snapshot version {snapshot.Version} is not supported. Expected {SnapshotVersion}: {_snapshotPath}");
            }

            return snapshot;
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "Statement fetch schedule snapshot is not valid JSON: {Path}", _snapshotPath);
            throw new InvalidOperationException($"Statement fetch schedule snapshot is invalid: {_snapshotPath}", ex);
        }
    }

    private async Task PersistAsync(StatementFetchScheduleSnapshot snapshot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var json = JsonSerializer.Serialize(snapshot, StatementFetchScheduleJsonContext.Default.StatementFetchScheduleSnapshot);
        await AtomicFileWriter.WriteAsync(_snapshotPath, json, ct).ConfigureAwait(false);
    }
}
