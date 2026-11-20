using Meridian.Application.Scheduling;
using Meridian.Contracts.Backfill;
using Meridian.Storage.Backfill;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Backfill;

public sealed record BackfillQueuedWork(
    string TenantId,
    BackfillRequest Request,
    BackfillQuotaEstimate Estimate,
    DateTimeOffset? NotBeforeUtc);

/// <summary>Retains one-time submissions and executes their exact scopes through the shared coordinator.</summary>
public sealed class BackfillJobQueue(
    BackfillCoordinator coordinator,
    BackfillExecutionHistory history,
    ILogger<BackfillJobQueue> logger,
    TimeProvider? timeProvider = null) : BackgroundService
{
    private const int MaxPendingJobs = 100;
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly string _dataRoot = coordinator.ResolveDataRoot();
    private readonly MemoryCache _previews = new(new MemoryCacheOptions { SizeLimit = 512 });
    private readonly object _submitLock = new();

    public Guid Prepare(string tenantId, BackfillRequest request, BackfillQuotaEstimate estimate)
    {
        if (request.From is null || request.To is null)
            throw new ArgumentException("Queued backfills require a fixed date range.", nameof(request));

        var id = Guid.NewGuid();
        var normalized = request with { Symbols = BackfillSymbolNormalizer.Normalize(request.Symbols) };
        _previews.Set(id, new BackfillQueuedWork(tenantId, normalized, estimate, null),
            new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15) });
        return id;
    }

    public BackfillJobReceipt Submit(string tenantId, QueueBackfillRequest submission)
    {
        var id = submission.PreviewId.ToString("N");
        var notBefore = submission.NotBeforeUtc?.ToUniversalTime();
        lock (_submitLock)
        {
            if (history.GetExecution(id) is { QueuedWork: { } existingWork } existing &&
                string.Equals(existingWork.TenantId, tenantId, StringComparison.Ordinal))
            {
                if (existingWork.NotBeforeUtc != notBefore)
                    throw new InvalidOperationException("This preview already submitted a job. Preview again to change its schedule.");
                return Project(existing)!;
            }

            if (!_previews.TryGetValue<BackfillQueuedWork>(submission.PreviewId, out var work) || work is null ||
                !string.Equals(work.TenantId, tenantId, StringComparison.Ordinal))
                throw new InvalidOperationException("This backfill preview is unavailable or expired. Preview again before submitting.");

            var now = _clock.GetUtcNow();
            if (notBefore <= now)
                throw new ArgumentException("Choose a future start time for the scheduled backfill.");

            coordinator.ValidateRequest(work.Request, work.TenantId);
            var latestEstimate = coordinator.EstimateQuota(work.Request, work.TenantId);
            if (notBefore is null && !submission.AcceptThrottle && (work.Estimate.ExceedsBudget || latestEstimate.ExceedsBudget))
                throw new InvalidOperationException("The plan exceeds the available backfill budget. Preview again and confirm queueing with throttling.");

            if (history.GetActiveQueuedExecutions().Count >= MaxPendingJobs)
                throw new InvalidOperationException("The backfill queue is full. Try again when a job finishes.");

            var execution = new BackfillExecutionLog
            {
                ExecutionId = id,
                ScheduleName = "Historical backfill",
                Trigger = ExecutionTrigger.Manual,
                ScheduledAt = now,
                FromDate = work.Request.From ?? default,
                ToDate = work.Request.To ?? default,
                Symbols = work.Request.Symbols.ToList(),
                QueuedWork = work with { NotBeforeUtc = notBefore }
            };
            history.StoreQueuedExecution(execution);
            _previews.Remove(submission.PreviewId);
            return Project(execution)!;
        }
    }

    public BackfillJobReceipt? Read(string tenantId, string id) =>
        history.GetExecution(id) is { QueuedWork: { } work } execution &&
        string.Equals(work.TenantId, tenantId, StringComparison.Ordinal)
            ? Project(execution)
            : null;

    public BackfillJobReceipt? Project(BackfillExecutionLog execution)
    {
        if (execution.QueuedWork is not { } work)
            return null;

        var status = execution.Status switch
        {
            ExecutionStatus.Pending when work.NotBeforeUtc > _clock.GetUtcNow() => "Scheduled",
            ExecutionStatus.Pending => "Queued",
            ExecutionStatus.Running when coordinator.IsJobThrottled(execution.ExecutionId) => "Throttled",
            _ => execution.Status.ToString()
        };
        return new BackfillJobReceipt(execution.ExecutionId, status, work.Request.Provider,
            work.Request.Symbols.ToArray(), work.Request.From, work.Request.To, execution.ScheduledAt,
            work.NotBeforeUtc, execution.StartedAt, execution.CompletedAt,
            execution.QuotaReceipt is null ? null : execution.Statistics.TotalBarsRetrieved,
            execution.ErrorMessage, work.Estimate, execution.QuotaReceipt, execution.BatchReceipts, execution.ProviderProvenance);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A host shutdown can occur after bars were written and before settlement. Keep the
        // interruption visible and require a new explicit submission to repeat that scope.
        foreach (var interrupted in history.GetActiveQueuedExecutions().Where(e => e.Status == ExecutionStatus.Running))
        {
            await StoreTerminalAsync(WithStatus(interrupted, ExecutionStatus.Failed,
                completedAt: _clock.GetUtcNow(), error: "Execution was interrupted before the final receipt was recorded."), stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = _clock.GetUtcNow();
            var next = history.GetActiveQueuedExecutions()
                .Where(e => e.Status == ExecutionStatus.Pending &&
                    (e.QueuedWork!.NotBeforeUtc is null || e.QueuedWork.NotBeforeUtc <= now))
                .OrderBy(e => e.QueuedWork!.NotBeforeUtc ?? e.ScheduledAt)
                .ThenBy(e => e.ScheduledAt)
                .FirstOrDefault();
            if (next is not null && await ExecuteJobAsync(next, stoppingToken).ConfigureAwait(false))
                continue;

            await Task.Delay(TimeSpan.FromSeconds(1), _clock, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> ExecuteJobAsync(BackfillExecutionLog execution, CancellationToken ct)
    {
        var work = execution.QueuedWork!;
        var running = execution;

        try
        {
            var result = await coordinator.RunQueuedAsync(work, execution.ExecutionId, _dataRoot, () =>
            {
                var started = WithStatus(execution, ExecutionStatus.Running, startedAt: _clock.GetUtcNow());
                history.StoreQueuedExecution(started);
                running = started;
            }, ct).ConfigureAwait(false);

            var status = result.Success ? ExecutionStatus.Completed : ExecutionStatus.Failed;
            var provenance = result.ProviderProvenance is { } receipt
                ? new RetainedBackfillResult(receipt, result.Success, result.BarsWritten, result.ProvenanceAccess)
                : null;
            await StoreTerminalAsync(WithStatus(running, status, completedAt: result.CompletedUtc,
                error: result.Error, result: result.ForTenant(work.TenantId)), ct, provenance).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Queued backfill {ExecutionId} could not complete", execution.ExecutionId);
            if (running.Status == ExecutionStatus.Pending)
                return false; // Admission was never durably recorded and no provider call was made.

            await StoreTerminalAsync(WithStatus(running, ExecutionStatus.Failed, completedAt: _clock.GetUtcNow(),
                error: "The backfill could not complete. Review host diagnostics."), ct).ConfigureAwait(false);
        }
        return true;
    }

    private async Task StoreTerminalAsync(BackfillExecutionLog execution, CancellationToken ct, RetainedBackfillResult? provenance = null)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                history.StoreQueuedExecution(execution);
                if (provenance is not null)
                    await new BackfillStatusStore(_dataRoot).WriteRetainedResultAsync(provenance, ct).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "Could not persist backfill receipt {ExecutionId}; retrying", execution.ExecutionId);
                await Task.Delay(TimeSpan.FromSeconds(2), _clock, ct).ConfigureAwait(false);
            }
        }
    }

    private static BackfillExecutionLog WithStatus(
        BackfillExecutionLog execution,
        ExecutionStatus status,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? completedAt = null,
        string? error = null,
        BackfillResult? result = null) =>
        new()
        {
            ExecutionId = execution.ExecutionId,
            ScheduleName = execution.ScheduleName,
            Trigger = execution.Trigger,
            ScheduledAt = execution.ScheduledAt,
            FromDate = execution.FromDate,
            ToDate = execution.ToDate,
            Symbols = execution.Symbols,
            QueuedWork = execution.QueuedWork,
            Status = status,
            StartedAt = startedAt ?? execution.StartedAt,
            CompletedAt = completedAt,
            ErrorMessage = error,
            Statistics = new ExecutionStatistics { TotalBarsRetrieved = result?.BarsWritten ?? 0 },
            QuotaReceipt = result?.QuotaReceipt,
            BatchReceipts = result?.BatchReceipts,
            ProviderProvenance = result?.ProviderProvenance
        };

    public override void Dispose()
    {
        base.Dispose();
        _previews.Dispose();
    }
}
