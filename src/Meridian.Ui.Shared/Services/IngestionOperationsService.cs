using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Pipeline;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Evidence;

namespace Meridian.Ui.Shared.Services;

public sealed class IngestionOperationsService
{
    private static readonly TimeSpan IdempotencyRetention = TimeSpan.FromHours(24);
    private readonly IngestionJobService _jobs;
    private readonly IEvidenceArtifactStore _evidence;
    private readonly ConcurrentDictionary<string, CachedAction> _idempotency = new(StringComparer.Ordinal);

    public IngestionOperationsService(IngestionJobService jobs, IEvidenceArtifactStore evidence)
    {
        _jobs = jobs;
        _evidence = evidence;
    }

    public IngestionOperationsSnapshotDto GetSnapshot(string? state, string? workload, string? provider, bool resumableOnly)
    {
        IngestionJobState? stateFilter = Enum.TryParse<IngestionJobState>(state, true, out var parsedState) ? parsedState : null;
        IngestionWorkloadType? workloadFilter = Enum.TryParse<IngestionWorkloadType>(workload, true, out var parsedWorkload) ? parsedWorkload : null;
        var rows = _jobs.GetJobs(stateFilter, workloadFilter)
            .Where(job => string.IsNullOrWhiteSpace(provider) || string.Equals(job.Provider, provider, StringComparison.OrdinalIgnoreCase))
            .Where(job => !resumableOnly || job.IsResumable)
            .OrderByDescending(job => job.CreatedAt)
            .Select(ToRow)
            .ToArray();
        var summary = _jobs.GetSummary();
        return new IngestionOperationsSnapshotDto(
            DateTimeOffset.UtcNow,
            new IngestionOperationsSummaryDto(
                summary.TotalJobs,
                summary.QueuedJobs,
                summary.RunningJobs,
                summary.PausedJobs,
                summary.FailedJobs,
                summary.CompletedJobs,
                summary.CancelledJobs,
                _jobs.GetResumableJobs().Count),
            rows,
            _jobs.GetJobs().Select(job => job.Provider).Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray());
    }

    public IngestionOperationDetailDto? GetDetail(string jobId)
    {
        var job = _jobs.GetJob(jobId);
        if (job is null)
            return null;

        var checkpoint = job.CheckpointToken is null ? null : new IngestionCheckpointDto(
            job.CheckpointToken.LastSymbol,
            ToOffset(job.CheckpointToken.LastDate),
            job.CheckpointToken.LastOffset,
            ToOffset(job.CheckpointToken.GapFillWindowStart),
            ToOffset(job.CheckpointToken.CapturedAt)!.Value);
        var progress = job.SymbolProgress.Select(item => new IngestionSymbolProgressDto(
            item.Symbol,
            item.State.ToString(),
            item.DataPointsProcessed,
            item.ExpectedDataPoints,
            item.ProgressPercent,
            ToOffset(item.LastCommittedDate),
            item.RetryCount,
            item.ErrorMessage)).ToArray();
        var evidenceRoute = EvidenceRoute(job.JobId);
        return new IngestionOperationDetailDto(
            ToRow(job),
            checkpoint,
            progress,
            [new OperationsEvidenceLinkDto(job.JobId, "Ingestion job evidence", evidenceRoute, "EvidenceVault", null)]);
    }

    public async Task<IngestionOperationActionResultDto?> ApplyActionAsync(
        string jobId,
        string action,
        IngestionOperationActionRequestDto request,
        string actor,
        string tenantId,
        string companyId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(companyId);
        tenantId = tenantId.Trim();
        companyId = companyId.Trim();
        CleanupIdempotencyCache();
        var idempotencyKey = $"{tenantId}:{companyId}:{jobId}:{action}:{request.IdempotencyKey.Trim()}";
        if (_idempotency.TryGetValue(idempotencyKey, out var prior))
            return prior.Result;

        var job = _jobs.GetJob(jobId);
        if (job is null)
            return null;

        var target = ResolveTarget(job, action);
        var previous = job.State;
        if (!await _jobs.TransitionAsync(jobId, target, ct: ct).ConfigureAwait(false))
            throw new InvalidOperationException($"Job '{jobId}' changed before action '{action}' could be applied.");

        var recordedAt = DateTimeOffset.UtcNow;
        var intake = await RetainActionEvidenceAsync(
            job,
            action,
            previous,
            target,
            request.Rationale.Trim(),
            actor,
            tenantId,
            companyId,
            recordedAt,
            ct).ConfigureAwait(false);
        var result = new IngestionOperationActionResultDto(
            jobId,
            action,
            previous.ToString(),
            target.ToString(),
            recordedAt,
            intake?.VaultIdentity.VaultId,
            EvidenceRoute(jobId));
        _idempotency.TryAdd(idempotencyKey, new CachedAction(recordedAt, result));
        return result;
    }

    private static IngestionJobState ResolveTarget(IngestionJob job, string action) => action.ToLowerInvariant() switch
    {
        "pause" when job.State == IngestionJobState.Running && job.CheckpointToken is not null => IngestionJobState.Paused,
        "resume" when job.State == IngestionJobState.Paused && job.CheckpointToken is not null => IngestionJobState.Running,
        "retry" when job.State == IngestionJobState.Failed && !job.RetryEnvelope.IsExhausted => IngestionJobState.Queued,
        "cancel" when job.State is IngestionJobState.Queued or IngestionJobState.Running or IngestionJobState.Paused => IngestionJobState.Cancelled,
        _ => throw new InvalidOperationException($"Action '{action}' is not available while job '{job.JobId}' is {job.State}.")
    };

    private async Task<EvidenceVaultIntakeResponseDto?> RetainActionEvidenceAsync(
        IngestionJob job,
        string action,
        IngestionJobState previous,
        IngestionJobState current,
        string rationale,
        string actor,
        string tenantId,
        string companyId,
        DateTimeOffset recordedAt,
        CancellationToken ct)
    {
        var receipt = JsonSerializer.Serialize(new
        {
            jobId = job.JobId,
            action,
            previousState = previous.ToString(),
            currentState = current.ToString(),
            rationale,
            actor,
            recordedAt,
            provider = job.Provider,
            workloadType = job.WorkloadType.ToString(),
            symbols = job.Symbols
        });
        return await _evidence.WriteIntakeArtifactAsync(new EvidenceVaultIntakeRequestDto(
            "run",
            job.JobId,
            "WorkstationOperation",
            $"ingestion-{job.JobId}-{recordedAt:yyyyMMddHHmmssfff}.json",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(receipt)),
            "application/json",
            "Meridian.IngestionOperations",
            job.JobId,
            actor,
            Linkage: new EvidenceSubjectLinkageDto($"IngestionJob:{job.JobId}", job.JobId, null, null, null))
        {
            Classification = EvidenceDocumentClassificationDto.AuditRequestSupport,
            Actor = actor,
            TenantId = tenantId,
            Scope = companyId
        }, ct).ConfigureAwait(false);
    }

    private static IngestionOperationRowDto ToRow(IngestionJob job)
    {
        var expected = job.SymbolProgress.Sum(static item => item.ExpectedDataPoints);
        var processed = job.SymbolProgress.Sum(static item => item.DataPointsProcessed);
        var progress = expected > 0 ? Math.Round(processed * 100d / expected, 2) : job.State == IngestionJobState.Completed ? 100d : 0d;
        return new IngestionOperationRowDto(
            job.JobId,
            job.WorkloadType.ToString(),
            job.State.ToString(),
            job.Provider,
            job.Symbols,
            ToOffset(job.CreatedAt)!.Value,
            ToOffset(job.StartedAt),
            ToOffset(job.CompletedAt),
            progress,
            job.IsResumable,
            job.RetryEnvelope.AttemptCount,
            job.RetryEnvelope.MaxRetries,
            ToOffset(job.RetryEnvelope.NextRetryAt),
            job.ErrorMessage,
            EvidenceRoute(job.JobId),
            BuildActions(job));
    }

    private static IReadOnlyList<IngestionOperationActionDto> BuildActions(IngestionJob job) =>
    [
        Action("pause", "Pause", job.State == IngestionJobState.Running && job.CheckpointToken is not null, "A durable checkpoint is required before pausing."),
        Action("resume", "Resume", job.State == IngestionJobState.Paused && job.CheckpointToken is not null, "Only a paused job with a durable checkpoint can resume."),
        Action("retry", "Retry", job.State == IngestionJobState.Failed && !job.RetryEnvelope.IsExhausted, job.RetryEnvelope.IsExhausted ? "The retry policy is exhausted." : "Only failed jobs can be retried."),
        Action("cancel", "Cancel", job.State is IngestionJobState.Queued or IngestionJobState.Running or IngestionJobState.Paused, "Only queued, running, or paused jobs can be cancelled.")
    ];

    private static IngestionOperationActionDto Action(string action, string label, bool enabled, string disabledReason) =>
        new(action, label, enabled, enabled ? null : disabledReason);

    private static DateTimeOffset? ToOffset(DateTime? value) => value.HasValue
        ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
        : null;

    private static string EvidenceRoute(string jobId) => $"/data/evidence?subjectKind=run&subjectId={Uri.EscapeDataString(jobId)}";

    private void CleanupIdempotencyCache()
    {
        var cutoff = DateTimeOffset.UtcNow - IdempotencyRetention;
        foreach (var item in _idempotency.Where(item => item.Value.RecordedAt < cutoff))
            _idempotency.TryRemove(item.Key, out _);
    }

    private sealed record CachedAction(DateTimeOffset RecordedAt, IngestionOperationActionResultDto Result);
}
