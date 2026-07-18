using System.Diagnostics;
using Meridian.Contracts.Lifecycle;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition.Startup;

public interface IRuntimeShutdownParticipant
{
    string Id { get; }
    LifecycleShutdownStage Stage { get; }
    int Order { get; }
    bool IsCritical { get; }

    ValueTask ExecuteAsync(CancellationToken ct);
}

public interface IRuntimeShutdownSequence
{
    ValueTask<LifecycleShutdownReceiptDto> ExecuteAsync(CancellationToken ct = default);
}

public sealed record RuntimeShutdownOptions
{
    public TimeSpan ParticipantTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

public sealed class RuntimeShutdownSequence : IRuntimeShutdownSequence
{
    private readonly IRuntimeLifecycleControlPlane _lifecycle;
    private readonly ILifecycleReceiptStore _receiptStore;
    private readonly IReadOnlyList<IRuntimeShutdownParticipant> _participants;
    private readonly RuntimeShutdownOptions _options;
    private readonly ILogger<RuntimeShutdownSequence> _logger;

    public RuntimeShutdownSequence(
        IRuntimeLifecycleControlPlane lifecycle,
        ILifecycleReceiptStore receiptStore,
        IEnumerable<IRuntimeShutdownParticipant> participants,
        RuntimeShutdownOptions options,
        ILogger<RuntimeShutdownSequence> logger)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _receiptStore = receiptStore ?? throw new ArgumentNullException(nameof(receiptStore));
        _participants = participants?
            .OrderBy(participant => participant.Stage)
            .ThenBy(participant => participant.Order)
            .ThenBy(participant => participant.Id, StringComparer.Ordinal)
            .ToArray() ?? throw new ArgumentNullException(nameof(participants));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<LifecycleShutdownReceiptDto> ExecuteAsync(CancellationToken ct = default)
    {
        var operation = _lifecycle.ActiveShutdownOperation
            ?? throw new InvalidOperationException("Shutdown sequence cannot run without an active operation.");
        var snapshot = _lifecycle.Snapshot;
        var participantReceipts = new List<LifecycleShutdownParticipantReceiptDto>(_participants.Count);
        var failedCriticalParticipant = false;
        var warning = false;

        _lifecycle.AdvanceShutdown(
            LifecycleShutdownStage.StopAcceptingWork,
            LifecycleShutdownOutcome.Succeeded,
            "New runtime work is no longer accepted.");

        foreach (var stageGroup in _participants.GroupBy(participant => participant.Stage))
        {
            _lifecycle.AdvanceShutdown(stageGroup.Key);
            foreach (var participant in stageGroup)
            {
                ct.ThrowIfCancellationRequested();
                var stopwatch = Stopwatch.StartNew();
                var outcome = LifecycleShutdownOutcome.Succeeded;
                string? message = null;

                using var participantCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                participantCts.CancelAfter(_options.ParticipantTimeout);
                try
                {
                    await participant.ExecuteAsync(participantCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested && participantCts.IsCancellationRequested)
                {
                    outcome = LifecycleShutdownOutcome.TimedOut;
                    message = $"Participant exceeded {_options.ParticipantTimeout.TotalSeconds:0.###} seconds.";
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    outcome = LifecycleShutdownOutcome.Failed;
                    message = ex.GetType().Name;
                    _logger.LogError(ex, "Lifecycle shutdown participant {ParticipantId} failed", participant.Id);
                }
                finally
                {
                    stopwatch.Stop();
                }

                failedCriticalParticipant |= participant.IsCritical && outcome != LifecycleShutdownOutcome.Succeeded;
                warning |= !participant.IsCritical && outcome != LifecycleShutdownOutcome.Succeeded;
                participantReceipts.Add(new LifecycleShutdownParticipantReceiptDto
                {
                    ParticipantId = participant.Id,
                    Stage = participant.Stage,
                    Outcome = outcome,
                    Critical = participant.IsCritical,
                    DurationMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                    Message = message
                });
            }
        }

        var finalOutcome = failedCriticalParticipant
            ? LifecycleShutdownOutcome.Failed
            : warning
                ? LifecycleShutdownOutcome.SucceededWithWarnings
                : LifecycleShutdownOutcome.Succeeded;
        var receipt = new LifecycleShutdownReceiptDto
        {
            SessionId = snapshot.SessionId,
            OperationId = operation.OperationId,
            Reason = operation.Reason,
            Outcome = finalOutcome,
            StartedAtUtc = operation.RequestedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            ForcedTermination = false,
            Participants = participantReceipts
        };

        _lifecycle.AdvanceShutdown(LifecycleShutdownStage.PersistingReceipt);
        await _receiptStore.WriteHostReceiptAsync(receipt, ct).ConfigureAwait(false);
        _lifecycle.AdvanceShutdown(
            LifecycleShutdownStage.ReleasingHost,
            finalOutcome,
            "Host termination released after receipt persistence.");
        _lifecycle.CompleteShutdown(receipt);
        return receipt;
    }
}

/// <summary>
/// Runs the authoritative shutdown sequence after a lifecycle request and only then releases the
/// host mode runner.
/// </summary>
public sealed class LifecycleControlPlaneHostedService : BackgroundService
{
    private readonly IRuntimeLifecycleControlPlane _lifecycle;
    private readonly IRuntimeShutdownSequence _sequence;
    private readonly ILogger<LifecycleControlPlaneHostedService> _logger;

    public LifecycleControlPlaneHostedService(
        IRuntimeLifecycleControlPlane lifecycle,
        IRuntimeShutdownSequence sequence,
        ILogger<LifecycleControlPlaneHostedService> logger)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _sequence = sequence ?? throw new ArgumentNullException(nameof(sequence));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var stoppingRegistration = stoppingToken.Register(() =>
        {
            _ = _lifecycle.RequestShutdownAsync(
                new LifecycleShutdownRequestDto
                {
                    Reason = LifecycleShutdownReason.ExternalCancellation,
                    RequestedBy = "generic-host",
                    Detail = "The generic host requested lifecycle shutdown."
                });
        });

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, _lifecycle.StopWorkToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifecycle.StopWorkToken.IsCancellationRequested)
        {
            // Expected: the lifecycle request starts the shutdown sequence.
        }

        try
        {
            await _sequence.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authoritative lifecycle shutdown sequence failed");
            if (_lifecycle.ActiveShutdownOperation is not null)
            {
                _lifecycle.AdvanceShutdown(
                    LifecycleShutdownStage.Failed,
                    LifecycleShutdownOutcome.Failed,
                    ex.GetType().Name);
            }
        }
        finally
        {
            _lifecycle.SignalTermination();
        }
    }
}
