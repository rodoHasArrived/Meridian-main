using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public sealed record AutomatedJournalScheduledRunResult(
    string ScheduleId,
    string RunKey,
    DateTimeOffset ScheduledForUtc,
    AutomatedJournalScheduleStateDto State,
    string Summary,
    IReadOnlyList<Guid> JournalEntryIds,
    IReadOnlyList<string> Blockers);

public sealed record AutomatedJournalScheduledBatchResult(
    DateTimeOffset EvaluatedAtUtc,
    IReadOnlyList<AutomatedJournalScheduledRunResult> Runs);

/// <summary>
/// Deterministic one-shot worker for due monthly fee and dividend work. It only invokes
/// automated intake, which writes to the existing manual journal workbench; it never
/// submits, approves, or posts a journal entry.
/// </summary>
public sealed class AutomatedJournalScheduledWorker
{
    private readonly IAutomatedJournalScheduleStore _store;
    private readonly AutomatedJournalIntakeRunner _intakeRunner;
    private readonly ILogger<AutomatedJournalScheduledWorker> _logger;
    private readonly SemaphoreSlim _runGate = new(1, 1);

    public AutomatedJournalScheduledWorker(
        IAutomatedJournalScheduleStore store,
        AutomatedJournalIntakeRunner intakeRunner,
        ILogger<AutomatedJournalScheduledWorker> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _intakeRunner = intakeRunner ?? throw new ArgumentNullException(nameof(intakeRunner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AutomatedJournalScheduledBatchResult> RunDueAsync(
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        nowUtc = nowUtc.ToUniversalTime();
        await _runGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var due = (await _store.ListAsync(ct).ConfigureAwait(false))
                .Where(static item => item.IsEnabled)
                .Where(static item => item.ScheduledForUtc.HasValue)
                .Where(item => item.ScheduledForUtc!.Value <= nowUtc)
                .Where(item =>
                    item.State == AutomatedJournalScheduleStateDto.Running ||
                    item.LastScheduledForUtc != item.ScheduledForUtc)
                .Where(item => item.State is AutomatedJournalScheduleStateDto.Scheduled or AutomatedJournalScheduleStateDto.Running)
                .OrderBy(static item => item.ScheduledForUtc)
                .ThenBy(static item => item.ScheduleId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var results = new List<AutomatedJournalScheduledRunResult>(due.Length);
            foreach (var item in due)
            {
                ct.ThrowIfCancellationRequested();
                results.Add(await RunWorkItemAsync(item, nowUtc, ct).ConfigureAwait(false));
            }

            return new AutomatedJournalScheduledBatchResult(nowUtc, results);
        }
        finally
        {
            _runGate.Release();
        }
    }

    private async Task<AutomatedJournalScheduledRunResult> RunWorkItemAsync(
        AutomatedJournalScheduleWorkItem item,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var scheduledForUtc = item.ScheduledForUtc!.Value.ToUniversalTime();
        var runKey = BuildRunKey(item, scheduledForUtc);
        var existingRun = item.RunHistory.FirstOrDefault(history => string.Equals(
            history.RunKey,
            runKey,
            StringComparison.OrdinalIgnoreCase));
        var runningHistory = new AutomatedJournalScheduleRunHistory(
            runKey,
            scheduledForUtc,
            existingRun?.StartedAtUtc ?? nowUtc,
            CompletedAtUtc: null,
            State: AutomatedJournalScheduleStateDto.Running,
            Summary: $"Monthly {item.Kind} schedule '{item.ScheduleId}' is running for {scheduledForUtc:O}.");
        var running = item with
        {
            State = AutomatedJournalScheduleStateDto.Running,
            LastScheduledForUtc = scheduledForUtc,
            LastSummary = runningHistory.Summary,
            EvidenceLinks = [],
            Blockers = [],
            RunHistory = UpsertHistory(item.RunHistory, runningHistory)
        };
        running = await _store.SaveAsync(running, ct).ConfigureAwait(false);

        try
        {
            if (item.Kind == AutomatedJournalScheduleKind.DividendCapture && item.Positions.Count == 0)
            {
                const string blocker = "No positions are configured for the monthly dividend-capture scope.";
                return await CompleteAsync(
                    running,
                    runKey,
                    nowUtc,
                    AutomatedJournalScheduleStateDto.Blocked,
                    blocker,
                    [],
                    [],
                    [blocker],
                    ct).ConfigureAwait(false);
            }

            var run = item.Kind == AutomatedJournalScheduleKind.FeeAccrual
                ? await RunFeeAccrualAsync(item, scheduledForUtc, ct).ConfigureAwait(false)
                : await RunDividendCaptureAsync(item, scheduledForUtc, ct).ConfigureAwait(false);
            return await CompleteFromIntakeAsync(running, runKey, run, nowUtc, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Leave the durable Running claim intact. A later one-shot execution retries the
            // same run key and downstream deterministic journal ids prevent duplication.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Scheduled automated journal work failed. ScheduleId={ScheduleId} RunKey={RunKey}",
                item.ScheduleId,
                runKey);
            var blocker = $"Scheduled {item.Kind} work failed: {ex.Message}";
            return await CompleteAsync(
                running,
                runKey,
                nowUtc,
                AutomatedJournalScheduleStateDto.Failed,
                blocker,
                [],
                [],
                [blocker],
                ct).ConfigureAwait(false);
        }
    }

    private Task<AutomatedJournalIntakeRunResult> RunFeeAccrualAsync(
        AutomatedJournalScheduleWorkItem item,
        DateTimeOffset scheduledForUtc,
        CancellationToken ct)
        => _intakeRunner.RunFeeAccrualIntakeAsync(
            new RunFeeAccrualDraftIntakeRequest(
                FundProfileId: item.FundProfileId,
                Currency: item.Currency,
                Actor: item.Actor,
                PeriodId: item.PeriodId,
                BeginningNav: item.BeginningNav!.Value,
                EndingNavBeforeFees: item.EndingNavBeforeFees!.Value,
                HighWaterMark: item.HighWaterMark!.Value,
                ManagementFeeRate: item.ManagementFeeRate!.Value,
                PerformanceFeeRate: item.PerformanceFeeRate!.Value,
                LedgerBookId: item.LedgerBookId,
                EntityId: item.EntityId,
                TenantId: item.TenantId,
                CompanyId: item.CompanyId,
                AsOf: new DateTimeOffset(item.PeriodEnd.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero),
                EvidenceLinks:
                [
                    $"{UiApiRoutes.LedgerJournalAutomationMonthlySchedules}?scheduleId={Uri.EscapeDataString(item.ScheduleId)}"
                ],
                EvidenceRetainedAtUtc: scheduledForUtc),
            ct);

    private Task<AutomatedJournalIntakeRunResult> RunDividendCaptureAsync(
        AutomatedJournalScheduleWorkItem item,
        DateTimeOffset scheduledForUtc,
        CancellationToken ct)
        => _intakeRunner.RunDividendIntakeAsync(
            new RunDividendDraftIntakeRequest(
                FundProfileId: item.FundProfileId,
                Currency: item.Currency,
                Actor: item.Actor,
                Positions: item.Positions,
                WindowStart: item.PeriodStart,
                WindowEnd: item.PeriodEnd,
                LedgerBookId: item.LedgerBookId,
                PeriodId: item.PeriodId,
                EntityId: item.EntityId,
                TenantId: item.TenantId,
                CompanyId: item.CompanyId,
                WithholdingTaxRate: item.WithholdingTaxRate,
                AsOf: scheduledForUtc,
                MinimumEvidenceConfidence: item.MinimumCorporateActionConfidence),
            ct);

    private async Task<AutomatedJournalScheduledRunResult> CompleteFromIntakeAsync(
        AutomatedJournalScheduleWorkItem running,
        string runKey,
        AutomatedJournalIntakeRunResult run,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var duplicateSkips = run.Intake.Skipped
            .Where(static skip => skip.Reason.StartsWith("Draft already exists", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var intakeBlockers = run.Intake.Skipped
            .Except(duplicateSkips)
            .Select(static skip => $"{skip.IdempotencyKey}: {skip.Reason}")
            .ToArray();
        var producerBlockers = run.ProducerSkips
            .Select(static skip => $"{skip.Subject}: {skip.Reason}")
            .ToArray();
        var investigationAssessments = run.EvidenceAssessments.Values
            .Where(static assessment => assessment.RequiresInvestigation)
            .ToArray();
        var investigationBlockers = investigationAssessments
            .SelectMany(static assessment => new[] { assessment.Summary }.Concat(assessment.Reasons))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var validationBlockers = run.Intake.Created
            .Where(static draft => draft.Status == ManualJournalEntryStatusDto.NeedsFix)
            .SelectMany(static draft => draft.ValidationIssues)
            .Where(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical)
            .Select(static issue => issue.Message)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var journalEntryIds = run.Intake.Created.Select(static draft => draft.JournalEntryId)
            .Concat(duplicateSkips.Select(static skip => skip.JournalEntryId))
            .Where(static id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        var evidenceLinks = BuildEvidenceLinks(running, run, nowUtc);

        AutomatedJournalScheduleStateDto state;
        string summary;
        IReadOnlyList<string> blockers;
        if (investigationAssessments.Length > 0 || producerBlockers.Length > 0)
        {
            state = AutomatedJournalScheduleStateDto.NeedsInvestigation;
            blockers = investigationBlockers.Concat(producerBlockers).Concat(intakeBlockers).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            summary = $"Monthly {running.Kind} produced {journalEntryIds.Length} governed draft(s), but source evidence needs investigation before approval.";
        }
        else if (intakeBlockers.Length > 0 || validationBlockers.Length > 0)
        {
            state = AutomatedJournalScheduleStateDto.Blocked;
            blockers = intakeBlockers.Concat(validationBlockers).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            summary = $"Monthly {running.Kind} intake is blocked; no journal can enter approval until the listed issues are resolved.";
        }
        else if (journalEntryIds.Length > 0)
        {
            state = AutomatedJournalScheduleStateDto.DraftReady;
            blockers = [];
            summary = $"Monthly {running.Kind} prepared {journalEntryIds.Length} governed workbench draft(s) awaiting human approval and posting.";
        }
        else if (running.Kind == AutomatedJournalScheduleKind.FeeAccrual)
        {
            state = AutomatedJournalScheduleStateDto.NoDraftRequired;
            blockers = [];
            summary = "Monthly fee calculation completed and both configured fees rounded to zero; no draft is required.";
        }
        else
        {
            state = AutomatedJournalScheduleStateDto.Blocked;
            blockers = ["No eligible corporate-action evidence was found for the configured positions and period."];
            summary = "Monthly dividend capture found no eligible corporate-action evidence; the absence is visible for operator review.";
        }

        return await CompleteAsync(
            running,
            runKey,
            nowUtc,
            state,
            summary,
            journalEntryIds,
            evidenceLinks,
            blockers,
            ct).ConfigureAwait(false);
    }

    private async Task<AutomatedJournalScheduledRunResult> CompleteAsync(
        AutomatedJournalScheduleWorkItem running,
        string runKey,
        DateTimeOffset nowUtc,
        AutomatedJournalScheduleStateDto state,
        string summary,
        IReadOnlyList<Guid> journalEntryIds,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        IReadOnlyList<string> blockers,
        CancellationToken ct)
    {
        var prior = running.RunHistory.First(history => string.Equals(history.RunKey, runKey, StringComparison.OrdinalIgnoreCase));
        var completedHistory = prior with
        {
            CompletedAtUtc = nowUtc,
            State = state,
            Summary = summary,
            JournalEntryIds = journalEntryIds,
            EvidenceLinks = evidenceLinks,
            Blockers = blockers
        };
        var completed = running with
        {
            State = state,
            LastRunAtUtc = nowUtc,
            JournalEntryIds = journalEntryIds,
            LastSummary = summary,
            EvidenceLinks = evidenceLinks,
            Blockers = blockers,
            RunHistory = UpsertHistory(running.RunHistory, completedHistory)
        };
        await _store.SaveAsync(completed, ct).ConfigureAwait(false);
        return new AutomatedJournalScheduledRunResult(
            completed.ScheduleId,
            runKey,
            prior.ScheduledForUtc,
            state,
            summary,
            journalEntryIds,
            blockers);
    }

    private static IReadOnlyList<OperationsEvidenceLinkDto> BuildEvidenceLinks(
        AutomatedJournalScheduleWorkItem item,
        AutomatedJournalIntakeRunResult run,
        DateTimeOffset capturedAtUtc)
    {
        var routes = run.Intake.Created.SelectMany(static draft => draft.EvidenceLinks)
            .Concat(run.EvidenceAssessments.Values.SelectMany(static assessment => assessment.EvidenceLinks))
            .Where(static route => !string.IsNullOrWhiteSpace(route))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var evidence = routes.Select((route, index) => new OperationsEvidenceLinkDto(
                $"automated-journal:{item.ScheduleId}:{index + 1}",
                item.Kind == AutomatedJournalScheduleKind.FeeAccrual ? "Fee calculation evidence" : "Corporate-action evidence",
                route,
                "automated-journal-scheduler",
                capturedAtUtc))
            .ToList();
        evidence.Add(new OperationsEvidenceLinkDto(
            $"automated-journal-schedule:{item.ScheduleId}",
            "Monthly automated-journal schedule and run history",
            $"{UiApiRoutes.LedgerJournalAutomationMonthlySchedules}?scheduleId={Uri.EscapeDataString(item.ScheduleId)}",
            "automated-journal-scheduler",
            capturedAtUtc));
        return evidence;
    }

    private static string BuildRunKey(AutomatedJournalScheduleWorkItem item, DateTimeOffset scheduledForUtc)
        => FormattableString.Invariant(
            $"{item.ScheduleId.Trim().ToLowerInvariant()}|{item.PeriodId.Trim().ToLowerInvariant()}|{scheduledForUtc:O}");

    private static IReadOnlyList<AutomatedJournalScheduleRunHistory> UpsertHistory(
        IReadOnlyList<AutomatedJournalScheduleRunHistory> history,
        AutomatedJournalScheduleRunHistory entry)
        => history
            .Where(item => !string.Equals(item.RunKey, entry.RunKey, StringComparison.OrdinalIgnoreCase))
            .Append(entry)
            .OrderBy(static item => item.ScheduledForUtc)
            .ToArray();
}

/// <summary>TimeProvider-driven host loop; <see cref="RunOnceAsync"/> is the deterministic seam.</summary>
public sealed class AutomatedJournalSchedulerHostedService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);
    private readonly AutomatedJournalScheduledWorker _worker;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AutomatedJournalSchedulerHostedService> _logger;

    public AutomatedJournalSchedulerHostedService(
        AutomatedJournalScheduledWorker worker,
        TimeProvider timeProvider,
        ILogger<AutomatedJournalSchedulerHostedService> logger)
    {
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<AutomatedJournalScheduledBatchResult> RunOnceAsync(CancellationToken ct = default)
        => _worker.RunDueAsync(_timeProvider.GetUtcNow(), ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var batch = await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                if (batch.Runs.Count > 0)
                {
                    _logger.LogInformation(
                        "Monthly automated-journal scheduler executed {RunCount} due work item(s).",
                        batch.Runs.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Monthly automated-journal scheduler tick failed; retrying on the next interval.");
            }

            try
            {
                await Task.Delay(TickInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
