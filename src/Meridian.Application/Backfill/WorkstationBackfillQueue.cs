using Meridian.Application.Scheduling;
using Meridian.Contracts.Backfill;
using Meridian.Storage.Backfill;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Backfill;

/// <summary>Retains one-off workstation jobs and executes them through the shared coordinator.</summary>
public sealed class WorkstationBackfillQueue(
    BackfillCoordinator coordinator, BackfillExecutionHistory history, ILogger<WorkstationBackfillQueue> logger,
    TimeProvider? timeProvider = null) : BackgroundService
{
    private const int QueueCapacity = 100;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private readonly SemaphoreSlim _admission = new(1, 1);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<BackfillSubmissionReceipt> SubmitAsync(Guid submissionId, string tenantId, BackfillRequest request,
        DateTimeOffset? runAt, string? alternativeFor, CancellationToken ct = default)
    {
        await _admission.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var id = submissionId.ToString("N");
            var now = _clock.GetUtcNow();
            if (history.GetExecution(id) is { } existing)
            {
                if (!MatchesSubmission(existing.WorkstationSubmission, tenantId, request, runAt, alternativeFor))
                    throw new BackfillSubmissionConflictException("This submission ID is already in use.");
                return ToReceipt(existing, now);
            }

            if (runAt <= now)
                throw new InvalidOperationException("Choose a future start time for a scheduled backfill.");
            if (history.GetWorkstationQueue().Count >= QueueCapacity)
                throw new BackfillSubmissionConflictException("The backfill queue is full. Try again when a job completes.");

            var plan = await coordinator.PlanSubmissionAsync(request, tenantId, alternativeFor, _clock, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            now = plan.PlannedAt;
            if (runAt <= now)
                throw new InvalidOperationException("Choose a future start time for a scheduled backfill.");
            var execution = new BackfillExecutionLog
            {
                ExecutionId = id,
                ScheduleName = "Workstation backfill",
                Trigger = ExecutionTrigger.Api,
                ScheduledAt = runAt ?? now,
                FromDate = plan.Request.From!.Value,
                ToDate = plan.Request.To!.Value,
                Symbols = plan.Request.Symbols.ToList(),
                WorkstationSubmission = new WorkstationBackfillSubmission(tenantId, plan.Request, now, runAt, alternativeFor, plan.Quota)
            };
            history.RecordWorkstationExecution(execution);
            return ToReceipt(execution, now);
        }
        finally
        {
            _admission.Release();
        }
    }

    public static string GetStatus(BackfillExecutionLog execution, DateTimeOffset now) => execution.Status switch
    {
        ExecutionStatus.Pending => execution.ScheduledAt > now ? "Scheduled" : "Queued",
        ExecutionStatus.Running when execution.ThrottledUntil > now => "Throttled",
        _ => execution.Status.ToString()
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var interrupted in history.GetWorkstationQueue().Where(job => job.Status == ExecutionStatus.Running))
        {
            await RetainCompletionAsync(interrupted with
            {
                Status = ExecutionStatus.Failed,
                CompletedAt = _clock.GetUtcNow(),
                ErrorMessage = "The host stopped during this backfill. Review stored coverage before submitting it again."
            }, stoppingToken).ConfigureAwait(false);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = _clock.GetUtcNow();
            var next = history.GetWorkstationQueue().FirstOrDefault(job => job.Status == ExecutionStatus.Pending && job.ScheduledAt <= now);
            if (next is null)
                await Task.Delay(PollInterval, _clock, stoppingToken).ConfigureAwait(false);
            else
                await ExecuteJobAsync(next, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ExecuteJobAsync(BackfillExecutionLog execution, CancellationToken ct)
    {
        var submission = execution.WorkstationSubmission!;
        Func<CancellationToken, Task>? retainProvenance = null;
        try
        {
            var (result, receiptStore) = await coordinator.RunQueuedAsync(
                submission.Request, submission.TenantId, submission.Estimate, submission.AlternativeFor,
                onStarted: () =>
                {
                    var running = execution with { Status = ExecutionStatus.Running, StartedAt = _clock.GetUtcNow() };
                    history.RecordWorkstationExecution(running);
                    execution = running;
                },
                onThrottled: until => execution.ReportThrottle(until), ct).ConfigureAwait(false);

            if (result.ProviderProvenance is { } provenance)
            {
                var retained = new RetainedBackfillResult(provenance, result.Success, result.BarsWritten, result.ProvenanceAccess);
                retainProvenance = token => receiptStore.WriteRetainedResultAsync(retained, token);
            }

            execution = execution with
            {
                Status = result.Success ? ExecutionStatus.Completed : ExecutionStatus.Failed,
                StartedAt = result.StartedUtc,
                CompletedAt = result.CompletedUtc,
                ErrorMessage = result.Success ? null : "One or more symbols could not be completed.",
                Statistics = new ExecutionStatistics { TotalSymbols = result.Symbols.Length, TotalBarsRetrieved = result.BarsWritten },
                QuotaReceipt = result.QuotaReceipt,
                BatchReceipts = result.BatchReceipts?.Select(BackfillBatchReceiptSummary.FromReceipt).ToArray()
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // An undispatched job remains queued. Startup marks interrupted executions as failed.
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not complete workstation backfill {ExecutionId}", execution.ExecutionId);
            execution = execution with
            {
                Status = ExecutionStatus.Failed,
                CompletedAt = _clock.GetUtcNow(),
                ErrorMessage = execution.StartedAt.HasValue ? "The backfill failed. Review stored coverage before retrying." :
                    "The job could not start. Check the provider configuration and approval before retrying."
            };
        }

        await RetainCompletionAsync(execution, ct, retainProvenance).ConfigureAwait(false);
    }

    private async Task RetainCompletionAsync(BackfillExecutionLog execution, CancellationToken ct,
        Func<CancellationToken, Task>? retainProvenance = null)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (retainProvenance is not null)
                {
                    await retainProvenance(ct).ConfigureAwait(false);
                    retainProvenance = null;
                }
                history.RecordWorkstationExecution(execution);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "Could not retain completion of backfill {ExecutionId}", execution.ExecutionId);
                await Task.Delay(PollInterval, _clock, ct).ConfigureAwait(false);
            }
        }
    }

    private static BackfillSubmissionReceipt ToReceipt(BackfillExecutionLog execution, DateTimeOffset now) =>
        new(execution.ExecutionId, GetStatus(execution, now), execution.WorkstationSubmission!.Request.Provider,
            execution.ScheduledAt, execution.WorkstationSubmission.Estimate);

    private static bool MatchesSubmission(WorkstationBackfillSubmission? existing, string tenantId, BackfillRequest request,
        DateTimeOffset? runAt, string? alternativeFor)
    {
        if (existing is null || !string.Equals(existing.TenantId, tenantId, StringComparison.Ordinal) ||
            existing.RunAt != runAt || !string.Equals(existing.AlternativeFor, alternativeFor, StringComparison.OrdinalIgnoreCase))
            return false;

        var normalized = BackfillQuotaPlanner.ResolveDates(request, existing.SubmittedAt);
        var prior = existing.Request;
        return prior.Provider.Equals(normalized.Provider, StringComparison.OrdinalIgnoreCase) && prior.From == normalized.From &&
            prior.To == normalized.To && prior.Granularity == normalized.Granularity &&
            prior.ResumeFromCheckpoint == normalized.ResumeFromCheckpoint &&
            prior.Symbols.SequenceEqual(normalized.Symbols, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class BackfillSubmissionConflictException(string message) : Exception(message);
