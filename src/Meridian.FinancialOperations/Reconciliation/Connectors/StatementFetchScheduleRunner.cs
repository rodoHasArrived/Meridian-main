using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Microsoft.Extensions.Logging;

namespace Meridian.FinancialOperations.Reconciliation.Connectors;

public sealed record StatementFetchAuthorizationCommand(
    string TenantId,
    string CompanyId,
    string FundAccountId,
    string ExternalAccountId,
    string SourceInstitution,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    StatementAccountingScope AccountingScope);

public sealed record StatementFetchIngestionCommand(
    StatementSourceDocument Document,
    string ConnectorId,
    string SourceKind,
    string SourceInstitution,
    string FundAccountId,
    string ExternalAccountId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string? ToleranceProfileId,
    string ImportedBy,
    string TenantId,
    string CompanyId,
    StatementAccountingScope AccountingScope)
{
    public StatementFetchAuthorizationCommand ToAuthorizationCommand()
        => new(
            TenantId,
            CompanyId,
            FundAccountId,
            ExternalAccountId,
            SourceInstitution,
            PeriodStart,
            PeriodEnd,
            AccountingScope);
}

/// <summary>
/// Compatibility seam used by the connector scheduler to reauthorize retained account ownership
/// before provider access and then enter the existing statement reconciliation report workflow.
/// Implementations must not commit through a parallel import path.
/// </summary>
public interface IStatementFetchIngestionAuthority
{
    Task<StatementAccountingScope> AuthorizeAsync(
        StatementFetchAuthorizationCommand command,
        CancellationToken ct = default);

    Task<StatementImportCommitResultDto> IngestAsync(
        StatementFetchIngestionCommand command,
        CancellationToken ct = default);
}

/// <summary>
/// Executes scheduled statement fetches: pulls the remote document through the connector,
/// delegates ingestion to the canonical statement reconciliation report workflow, and records the
/// run outcome on the schedule.
/// Downstream duplicate-key idempotency makes overlapping or repeated runs safe, so the
/// scheduler can be simple.
/// </summary>
public sealed class StatementFetchScheduleRunner(
    IStatementFetchScheduleStore scheduleStore,
    StatementImportService importService,
    IStatementFetchIngestionAuthority? ingestionAuthority = null,
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

    /// <summary>
    /// Runs the exact tenant-scoped schedule already authorized by an endpoint or other caller.
    /// </summary>
    public Task<StatementImportCommitResultDto?> RunScheduleAsync(
        StatementFetchSchedule schedule,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        return RunScheduleCoreAsync(schedule, nowUtc, ct);
    }

    private async Task<StatementImportCommitResultDto?> RunScheduleCoreAsync(
        StatementFetchSchedule schedule,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        try
        {
            EnsureAuthoritativeScope(schedule);
            if (ingestionAuthority is null)
            {
                throw new InvalidOperationException(
                    "Statement reconciliation report ingestion authority is not registered.");
            }

            var authorizedScope = await ingestionAuthority
                .AuthorizeAsync(
                    new StatementFetchAuthorizationCommand(
                        schedule.TenantId!,
                        schedule.CompanyId!,
                        schedule.FundAccountId,
                        schedule.ExternalAccountId,
                        schedule.SourceInstitution,
                        schedule.PeriodStart!.Value,
                        schedule.PeriodEnd!.Value,
                        schedule.AccountingScope!),
                    ct)
                .ConfigureAwait(false);
            if (!AccountingScopesEqual(authorizedScope, schedule.AccountingScope!))
            {
                throw new InvalidOperationException(
                    "Statement fetch schedule account authority changed before provider access.");
            }

            var periodStartUtc = new DateTimeOffset(
                schedule.PeriodStart!.Value,
                TimeOnly.MinValue,
                TimeSpan.Zero);
            var periodEndExclusiveUtc = new DateTimeOffset(
                schedule.PeriodEnd!.Value.AddDays(1),
                TimeOnly.MinValue,
                TimeSpan.Zero);
            var document = await importService.FetchDocumentAsync(
                    new StatementFetchRequest(
                        schedule.ConnectorId,
                        schedule.ExternalAccountId,
                        Since: periodStartUtc,
                        MappingProfileId: schedule.MappingProfileId,
                        Datasets: StatementFetchDatasets.Activity,
                        UntilExclusive: periodEndExclusiveUtc),
                    ct)
                .ConfigureAwait(false);

            var result = await ingestionAuthority.IngestAsync(
                    new StatementFetchIngestionCommand(
                        document,
                        schedule.ConnectorId,
                        schedule.SourceKind,
                        schedule.SourceInstitution,
                        schedule.FundAccountId,
                        schedule.ExternalAccountId,
                        schedule.PeriodStart!.Value,
                        schedule.PeriodEnd!.Value,
                        schedule.ToleranceProfileId,
                        "statement-fetch-scheduler",
                        schedule.TenantId!,
                        schedule.CompanyId!,
                        authorizedScope),
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
            // ExternalAccountId can be an IBAN, bank id, or broker account number. Do not attach
            // either that value or a provider exception that may echo it to the application log.
            // The retained schedule id and stable exception type preserve correlation without
            // copying account data into diagnostics.
            logger?.LogWarning(
                "Scheduled statement fetch failed for schedule {ScheduleId} (connector {ConnectorId}); failure type {FailureType}",
                schedule.ScheduleId,
                schedule.ConnectorId,
                ex.GetType().Name);
            await scheduleStore.RecordFailureAsync(
                    schedule.ScheduleId,
                    nowUtc,
                    $"Failed: {ex.GetType().Name}",
                    ct)
                .ConfigureAwait(false);
            return null;
        }
    }

    private static void EnsureAuthoritativeScope(StatementFetchSchedule schedule)
    {
        if (string.IsNullOrWhiteSpace(schedule.TenantId)
            || string.IsNullOrWhiteSpace(schedule.CompanyId)
            || !schedule.PeriodStart.HasValue
            || !schedule.PeriodEnd.HasValue
            || schedule.PeriodEnd.Value < schedule.PeriodStart.Value
            || schedule.PeriodEnd.Value == DateOnly.MaxValue
            || schedule.AccountingScope is null
            || schedule.AccountingScope.AsOfDate != schedule.PeriodEnd.Value)
        {
            throw new InvalidOperationException(
                "Statement fetch schedule does not retain exact tenant, company, fund, book, and accounting-period authority. Edit and save the schedule before running it.");
        }
    }

    private static bool AccountingScopesEqual(
        StatementAccountingScope left,
        StatementAccountingScope right)
        => string.Equals(left.FundProfileId, right.FundProfileId, StringComparison.OrdinalIgnoreCase)
           && left.LedgerBookId == right.LedgerBookId
           && left.AccountingPeriodId == right.AccountingPeriodId
           && left.AsOfDate == right.AsOfDate;
}
