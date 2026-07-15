using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Microsoft.Extensions.DependencyInjection;
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
    IReadOnlyList<string> Blockers,
    decimal? EvidenceConfidenceScore = null,
    AutomatedJournalEvidenceQualityDto? EvidenceQuality = null,
    string? NextPeriodId = null,
    DateTimeOffset? NextScheduledForUtc = null);

public sealed record AutomatedJournalScheduledBatchResult(
    DateTimeOffset EvaluatedAtUtc,
    IReadOnlyList<AutomatedJournalScheduledRunResult> Runs);

/// <summary>
/// Deterministic recurring worker for due monthly fee and dividend work. A completed cycle
/// advances the durable period cursor atomically while retaining its run history. The worker
/// only invokes automated intake into the existing manual journal workbench; it never submits,
/// approves, or posts a journal entry.
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
        => await RunDueCoreAsync(nowUtc, tenantId: null, companyId: null, scopeSpecified: false, ct: ct)
            .ConfigureAwait(false);

    /// <summary>
    /// Runs only schedules owned by the exact tenant/company scope. Null values are treated as
    /// the legacy unscoped identity, not as wildcards, so an unscoped operator cannot trigger
    /// another tenant's accounting automation.
    /// </summary>
    public async Task<AutomatedJournalScheduledBatchResult> RunDueForScopeAsync(
        DateTimeOffset nowUtc,
        string? tenantId,
        string? companyId,
        CancellationToken ct = default)
        => await RunDueCoreAsync(nowUtc, NormalizeScope(tenantId), NormalizeScope(companyId), scopeSpecified: true, ct: ct)
            .ConfigureAwait(false);

    private async Task<AutomatedJournalScheduledBatchResult> RunDueCoreAsync(
        DateTimeOffset nowUtc,
        string? tenantId,
        string? companyId,
        bool scopeSpecified,
        CancellationToken ct)
    {
        nowUtc = nowUtc.ToUniversalTime();
        await _runGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var due = (await _store.ListAsync(ct).ConfigureAwait(false))
                .Where(item => !scopeSpecified ||
                    (string.Equals(item.TenantId, tenantId, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(item.CompanyId, companyId, StringComparison.OrdinalIgnoreCase)))
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
            Summary: $"Monthly {item.Kind} schedule '{item.ScheduleId}' is running for {scheduledForUtc:O}.",
            PeriodId: item.PeriodId,
            PeriodStart: item.PeriodStart,
            PeriodEnd: item.PeriodEnd);
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
            if (item.Kind == AutomatedJournalScheduleKind.FeeAccrual)
            {
                var feeEvidence = AutomatedJournalFeeEvidenceEvaluator.Evaluate(
                    item.PeriodId,
                    item.Currency,
                    item.BeginningNav,
                    item.EndingNavBeforeFees,
                    item.HighWaterMark,
                    item.CapitalAccountReconciliation,
                    item.MinimumCapitalAccountConfidence,
                    nowUtc);
                if (!feeEvidence.IsReady)
                {
                    return await CompleteAsync(
                        running,
                        runKey,
                        nowUtc,
                        feeEvidence.FailureState,
                        feeEvidence.Assessment.Summary,
                        [],
                        BuildScheduleEvidenceLinks(running, feeEvidence.EvidenceLinks, nowUtc),
                        feeEvidence.Blockers,
                        feeEvidence.Assessment.ConfidenceScore,
                        feeEvidence.Assessment.Quality,
                        ct).ConfigureAwait(false);
                }
            }

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
                    null,
                    null,
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
                null,
                null,
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
                EvidenceRetainedAtUtc: scheduledForUtc,
                CapitalAccountReconciliation: item.CapitalAccountReconciliation,
                MinimumCapitalAccountConfidence: item.MinimumCapitalAccountConfidence),
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
        var evidenceConfidenceScore = run.EvidenceAssessments.Count == 0
            ? (decimal?)null
            : run.EvidenceAssessments.Values.Min(static assessment => assessment.ConfidenceScore);
        var evidenceQuality = run.EvidenceAssessments.Count == 0
            ? (AutomatedJournalEvidenceQualityDto?)null
            : run.EvidenceAssessments.Values.Min(static assessment => assessment.Quality);

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
            evidenceConfidenceScore,
            evidenceQuality,
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
        decimal? evidenceConfidenceScore,
        AutomatedJournalEvidenceQualityDto? evidenceQuality,
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
            Blockers = blockers,
            PeriodId = running.PeriodId,
            PeriodStart = running.PeriodStart,
            PeriodEnd = running.PeriodEnd,
            EvidenceConfidenceScore = evidenceConfidenceScore,
            EvidenceQuality = evidenceQuality
        };
        var completedCycle = running with
        {
            State = state,
            LastRunAtUtc = nowUtc,
            JournalEntryIds = journalEntryIds,
            LastSummary = summary,
            EvidenceLinks = evidenceLinks,
            Blockers = blockers,
            LastEvidenceConfidenceScore = evidenceConfidenceScore,
            LastEvidenceQuality = evidenceQuality,
            RunHistory = UpsertHistory(running.RunHistory, completedHistory)
        };
        var next = ShouldAdvance(completedCycle, state, journalEntryIds)
            ? AdvanceRecurringCycle(completedCycle)
            : completedCycle;
        var persisted = await _store.SaveAsync(next, ct).ConfigureAwait(false);
        var advanced = !string.Equals(persisted.PeriodId, running.PeriodId, StringComparison.OrdinalIgnoreCase);
        return new AutomatedJournalScheduledRunResult(
            persisted.ScheduleId,
            runKey,
            prior.ScheduledForUtc,
            state,
            summary,
            journalEntryIds,
            blockers,
            evidenceConfidenceScore,
            evidenceQuality,
            advanced ? persisted.PeriodId : null,
            advanced ? persisted.ScheduledForUtc : null);
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

    private static IReadOnlyList<OperationsEvidenceLinkDto> BuildScheduleEvidenceLinks(
        AutomatedJournalScheduleWorkItem item,
        IReadOnlyList<OperationsEvidenceLinkDto> retainedEvidence,
        DateTimeOffset capturedAtUtc)
    {
        var evidence = retainedEvidence
            .Where(static link => !string.IsNullOrWhiteSpace(link.Route))
            .DistinctBy(static link => $"{link.EvidenceId}|{link.Route}", StringComparer.OrdinalIgnoreCase)
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

    private static bool ShouldAdvance(
        AutomatedJournalScheduleWorkItem item,
        AutomatedJournalScheduleStateDto state,
        IReadOnlyList<Guid> journalEntryIds)
        => item.RecurrenceEnabled &&
           (state is AutomatedJournalScheduleStateDto.DraftReady or AutomatedJournalScheduleStateDto.NoDraftRequired ||
            state == AutomatedJournalScheduleStateDto.NeedsInvestigation && journalEntryIds.Count > 0);

    private static AutomatedJournalScheduleWorkItem AdvanceRecurringCycle(
        AutomatedJournalScheduleWorkItem completed)
    {
        var currentToken = completed.PeriodStart.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        var nextPeriodStart = completed.PeriodStart.AddMonths(1);
        var nextToken = nextPeriodStart.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        var tokenIndex = completed.PeriodId.IndexOf(currentToken, StringComparison.Ordinal);
        if (tokenIndex < 0)
        {
            throw new InvalidOperationException(
                $"Recurring schedule '{completed.ScheduleId}' cannot advance period id '{completed.PeriodId}' because it does not contain '{currentToken}'.");
        }

        var nextPeriodId = string.Concat(
            completed.PeriodId.AsSpan(0, tokenIndex),
            nextToken,
            completed.PeriodId.AsSpan(tokenIndex + currentToken.Length));
        var nextPeriodEnd = nextPeriodStart.AddMonths(1).AddDays(-1);
        var dueDaysAfterPeriodEnd = completed.DueDate.DayNumber - completed.PeriodEnd.DayNumber;
        var nextDueDate = nextPeriodEnd.AddDays(dueDaysAfterPeriodEnd);
        return completed with
        {
            PeriodId = nextPeriodId,
            PeriodStart = nextPeriodStart,
            PeriodEnd = nextPeriodEnd,
            DueDate = nextDueDate,
            ScheduledForUtc = null,
            State = AutomatedJournalScheduleStateDto.Scheduled,
            JournalEntryIds = [],
            LastSummary = $"Next monthly {completed.Kind} cycle is scheduled for period '{nextPeriodId}'.",
            EvidenceLinks = [],
            Blockers = [],
            BeginningNav = completed.Kind == AutomatedJournalScheduleKind.FeeAccrual ? null : completed.BeginningNav,
            EndingNavBeforeFees = completed.Kind == AutomatedJournalScheduleKind.FeeAccrual ? null : completed.EndingNavBeforeFees,
            HighWaterMark = completed.Kind == AutomatedJournalScheduleKind.FeeAccrual ? null : completed.HighWaterMark,
            CapitalAccountReconciliation = null,
            LastEvidenceConfidenceScore = null,
            LastEvidenceQuality = null
        };
    }

    private static IReadOnlyList<AutomatedJournalScheduleRunHistory> UpsertHistory(
        IReadOnlyList<AutomatedJournalScheduleRunHistory> history,
        AutomatedJournalScheduleRunHistory entry)
        => history
            .Where(item => !string.Equals(item.RunKey, entry.RunKey, StringComparison.OrdinalIgnoreCase))
            .Append(entry)
            .OrderBy(static item => item.ScheduledForUtc)
            .ToArray();

    private static string? NormalizeScope(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>TimeProvider-driven host loop; <see cref="RunOnceAsync"/> is the deterministic seam.</summary>
public sealed class AutomatedJournalSchedulerHostedService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);
    private readonly IServiceProvider _services;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AutomatedJournalSchedulerHostedService> _logger;

    public AutomatedJournalSchedulerHostedService(
        IServiceProvider services,
        TimeProvider timeProvider,
        ILogger<AutomatedJournalSchedulerHostedService> logger)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<AutomatedJournalScheduledBatchResult> RunOnceAsync(CancellationToken ct = default)
        => _services.GetRequiredService<AutomatedJournalScheduledWorker>()
            .RunDueAsync(_timeProvider.GetUtcNow(), ct);

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
