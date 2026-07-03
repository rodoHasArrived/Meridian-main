using Meridian.Contracts.Workstation;
using Microsoft.Extensions.Logging;

namespace Meridian.FinancialOperations.Reconciliation.Connectors;

/// <summary>
/// Executes scheduled statement fetches: pulls the remote document through the connector,
/// commits it into the reconciliation queue, and records the run outcome on the schedule.
/// Downstream duplicate-key idempotency makes overlapping or repeated runs safe, so the
/// scheduler can be simple.
/// </summary>
public sealed class StatementFetchScheduleRunner(
    IStatementFetchScheduleStore scheduleStore,
    StatementImportService importService,
    ILogger<StatementFetchScheduleRunner>? logger = null)
{
    /// <summary>Runs every enabled, due schedule; failures are recorded per schedule, never thrown.</summary>
    public async Task<int> RunDueSchedulesAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var schedules = await scheduleStore.ListAsync(ct).ConfigureAwait(false);
        var ran = 0;
        foreach (var schedule in schedules.Where(schedule => schedule.IsDue(nowUtc)))
        {
            ct.ThrowIfCancellationRequested();
            await RunScheduleCoreAsync(schedule, nowUtc, ct).ConfigureAwait(false);
            ran++;
        }

        return ran;
    }

    /// <summary>Runs one schedule immediately (operator "Run now"), due or not.</summary>
    public async Task<StatementImportCommitResultDto?> RunScheduleAsync(
        string scheduleId,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        var schedules = await scheduleStore.ListAsync(ct).ConfigureAwait(false);
        var schedule = schedules.FirstOrDefault(candidate =>
            string.Equals(candidate.ScheduleId, scheduleId?.Trim(), StringComparison.OrdinalIgnoreCase));
        return schedule is null
            ? null
            : await RunScheduleCoreAsync(schedule, nowUtc, ct).ConfigureAwait(false);
    }

    private async Task<StatementImportCommitResultDto?> RunScheduleCoreAsync(
        StatementFetchSchedule schedule,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        try
        {
            var since = schedule.LastRunAtUtc ?? nowUtc.AddHours(-Math.Max(1, schedule.CadenceHours));
            var document = await importService.FetchDocumentAsync(
                    new StatementFetchRequest(
                        schedule.ConnectorId,
                        schedule.ExternalAccountId,
                        since,
                        schedule.MappingProfileId),
                    ct)
                .ConfigureAwait(false);

            var result = await importService.CommitAsync(
                    new StatementImportCommitRequest(
                        document,
                        schedule.ConnectorId,
                        SourceKind: "broker",
                        SourceInstitution: schedule.SourceInstitution,
                        FundAccountId: schedule.FundAccountId,
                        ExternalAccountId: schedule.ExternalAccountId,
                        PeriodStart: DateOnly.FromDateTime(since.UtcDateTime),
                        PeriodEnd: DateOnly.FromDateTime(nowUtc.UtcDateTime),
                        ToleranceProfileId: schedule.ToleranceProfileId,
                        ImportedBy: "statement-fetch-scheduler"),
                    ct)
                .ConfigureAwait(false);

            var status = result.Duplicate
                ? "Duplicate: statement already imported for this period."
                : $"Imported run {result.RunId}: {result.RecordCount} record(s), {result.CaseCount} case(s).";
            await scheduleStore.RecordRunAsync(schedule.ScheduleId, nowUtc, status, ct).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(
                ex,
                "Scheduled statement fetch failed for schedule {ScheduleId} (connector {ConnectorId}, account {ExternalAccountId})",
                schedule.ScheduleId,
                schedule.ConnectorId,
                schedule.ExternalAccountId);
            await scheduleStore.RecordFailureAsync(schedule.ScheduleId, $"Failed: {ex.Message}", ct).ConfigureAwait(false);
            return null;
        }
    }
}
