using System.Diagnostics;
using Meridian.Contracts.Lifecycle;

namespace Meridian.Application.Composition.Startup;

public readonly record struct RuntimeReadinessCheckResult(
    LifecycleCheckStatus Status,
    string Message);

public interface IRuntimeReadinessCheck
{
    string Id { get; }
    string DisplayName { get; }
    LifecycleCheckRequirement Requirement { get; }

    ValueTask<RuntimeReadinessCheckResult> CheckAsync(CancellationToken ct);
}

public interface IRuntimeReadinessService
{
    ValueTask<RuntimeLifecycleSnapshotDto> EvaluateAsync(CancellationToken ct = default);
}

public sealed class DelegateRuntimeReadinessCheck : IRuntimeReadinessCheck
{
    private readonly Func<CancellationToken, ValueTask<RuntimeReadinessCheckResult>> _check;

    public DelegateRuntimeReadinessCheck(
        string id,
        string displayName,
        LifecycleCheckRequirement requirement,
        Func<CancellationToken, ValueTask<RuntimeReadinessCheckResult>> check)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(check);

        Id = id;
        DisplayName = displayName;
        Requirement = requirement;
        _check = check;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public LifecycleCheckRequirement Requirement { get; }

    public ValueTask<RuntimeReadinessCheckResult> CheckAsync(CancellationToken ct) => _check(ct);
}

public sealed class RuntimeReadinessService : IRuntimeReadinessService
{
    private readonly IRuntimeLifecycleControlPlane _lifecycle;
    private readonly IReadOnlyList<IRuntimeReadinessCheck> _checks;

    public RuntimeReadinessService(
        IRuntimeLifecycleControlPlane lifecycle,
        IEnumerable<IRuntimeReadinessCheck> checks)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _checks = checks?.OrderBy(check => check.Id, StringComparer.Ordinal).ToArray()
            ?? throw new ArgumentNullException(nameof(checks));
    }

    public async ValueTask<RuntimeLifecycleSnapshotDto> EvaluateAsync(CancellationToken ct = default)
    {
        if (_lifecycle.Snapshot.ShutdownRequested)
        {
            return _lifecycle.Snapshot;
        }

        var results = new List<RuntimeLifecycleCheckDto>(_checks.Count);
        foreach (var check in _checks)
        {
            ct.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();
            RuntimeReadinessCheckResult result;

            try
            {
                result = await check.CheckAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = new RuntimeReadinessCheckResult(
                    LifecycleCheckStatus.Failing,
                    $"{check.DisplayName} check failed: {ex.GetType().Name}");
            }

            stopwatch.Stop();
            results.Add(new RuntimeLifecycleCheckDto
            {
                Id = check.Id,
                DisplayName = check.DisplayName,
                Requirement = check.Requirement,
                Status = result.Status,
                Message = result.Message,
                CheckedAtUtc = DateTimeOffset.UtcNow,
                DurationMilliseconds = stopwatch.Elapsed.TotalMilliseconds
            });
        }

        var requiredFailure = results.Any(result =>
            result.Requirement == LifecycleCheckRequirement.Required &&
            result.Status is LifecycleCheckStatus.Failing or LifecycleCheckStatus.Pending);
        var degraded = results.Any(result =>
            result.Status == LifecycleCheckStatus.Degraded ||
            result.Requirement == LifecycleCheckRequirement.Degradable &&
            result.Status == LifecycleCheckStatus.Failing);

        var readiness = requiredFailure
            ? RuntimeReadinessStatus.NotReady
            : degraded
                ? RuntimeReadinessStatus.Degraded
                : RuntimeReadinessStatus.Ready;

        _lifecycle.UpdateReadiness(readiness, results);
        return _lifecycle.Snapshot;
    }
}
