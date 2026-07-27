using System.Collections.Immutable;
using Meridian.Reporting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

/// <summary>Continuously drains the durable delivery outbox; no tenant-scoped HTTP pump is exposed.</summary>
public sealed class ReportingSecureDistributionHostedService : BackgroundService
{
    private const int DefaultHandoffBatchSize = 100;

    private readonly ReportingScheduleService _scheduleService;
    private readonly Func<
        SecureReportingDeliveryQueueCommand,
        ReportingDistributionAuthority,
        CancellationToken,
        Task<string>> _queueDeliveryAsync;
    private readonly Func<string, CancellationToken, Task> _dispatchDueAsync;
    private readonly Func<CancellationToken, Task<int>> _reconcileFailedGrantsAsync;
    private readonly SecureReportingDistributionOptions _options;
    private readonly ILogger<ReportingSecureDistributionHostedService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly int _handoffBatchSize;
    private readonly ReportingDeliveryWorkerReadinessState? _readiness;
    private ReportingScheduledHandoffCursor? _handoffCursor;

    public ReportingSecureDistributionHostedService(
        ReportingDeliveryDispatcher dispatcher,
        ReportingScheduleService scheduleService,
        ReportingSecureDistributionApplicationService applicationService,
        SecureReportingDistributionOptions options,
        ILogger<ReportingSecureDistributionHostedService> logger)
        : this(
            dispatcher,
            scheduleService,
            applicationService,
            options,
            logger,
            readiness: null)
    {
    }

    public ReportingSecureDistributionHostedService(
        ReportingDeliveryDispatcher dispatcher,
        ReportingScheduleService scheduleService,
        ReportingSecureDistributionApplicationService applicationService,
        SecureReportingDistributionOptions options,
        ILogger<ReportingSecureDistributionHostedService> logger,
        ReportingDeliveryWorkerReadinessState? readiness)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _scheduleService = scheduleService ?? throw new ArgumentNullException(nameof(scheduleService));
        ArgumentNullException.ThrowIfNull(applicationService);
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _queueDeliveryAsync = async (command, authority, cancellationToken) =>
            (await applicationService
                .QueueDeliveryAsync(command, authority, cancellationToken)
                .ConfigureAwait(false)).JobId;
        _dispatchDueAsync = async (workerId, cancellationToken) =>
        {
            _ = await dispatcher
                .DispatchDueAsync(workerId, cancellationToken)
                .ConfigureAwait(false);
        };
        _reconcileFailedGrantsAsync = cancellationToken =>
            applicationService.ReconcileFailedDeliveryAccessGrantsAsync(
                ct: cancellationToken);
        _timeProvider = TimeProvider.System;
        _handoffBatchSize = DefaultHandoffBatchSize;
        _readiness = readiness;
    }

    internal ReportingSecureDistributionHostedService(
        ReportingScheduleService scheduleService,
        Func<
            SecureReportingDeliveryQueueCommand,
            ReportingDistributionAuthority,
            CancellationToken,
            Task<string>> queueDeliveryAsync,
        ILogger<ReportingSecureDistributionHostedService> logger,
        TimeProvider? timeProvider = null,
        int handoffBatchSize = DefaultHandoffBatchSize,
        ReportingDeliveryWorkerReadinessState? readiness = null,
        Func<string, CancellationToken, Task>? dispatchDueAsync = null,
        Func<CancellationToken, Task<int>>? reconcileFailedGrantsAsync = null,
        SecureReportingDistributionOptions? options = null)
    {
        _scheduleService = scheduleService ?? throw new ArgumentNullException(nameof(scheduleService));
        _queueDeliveryAsync = queueDeliveryAsync ?? throw new ArgumentNullException(nameof(queueDeliveryAsync));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = options ?? SecureReportingDistributionOptions.Default;
        _dispatchDueAsync = dispatchDueAsync ?? static (_, _) => Task.CompletedTask;
        _reconcileFailedGrantsAsync =
            reconcileFailedGrantsAsync ?? static _ => Task.FromResult(0);
        _handoffBatchSize = handoffBatchSize is > 0 and <= 500
            ? handoffBatchSize
            : throw new ArgumentOutOfRangeException(nameof(handoffBatchSize));
        _readiness = readiness;
        if (_options.WorkerPollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The reporting delivery worker poll interval must be positive.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerId = $"{_options.WorkerId}:hosted";
        using var timer = new PeriodicTimer(_options.WorkerPollInterval);
        try
        {
            do
            {
                try
                {
                    await EnqueueReleasedScheduleHandoffsOnceAsync(stoppingToken).ConfigureAwait(false);
                    _ = await _reconcileFailedGrantsAsync(stoppingToken).ConfigureAwait(false);
                    await _dispatchDueAsync(workerId, stoppingToken).ConfigureAwait(false);
                    _readiness?.MarkReady(_timeProvider.GetUtcNow());
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _readiness?.MarkCycleFailed();
                    // Deliberately omit the exception message: transport failures can contain provider
                    // material and no bearer-bearing value may enter host logs.
                    _logger.LogError(
                        "Reporting delivery worker cycle failed closed with error type {ErrorType}.",
                        exception.GetType().Name);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        finally
        {
            _readiness?.MarkNotReady();
        }
    }

    internal async Task<ReportingScheduledHandoffBridgeResult> EnqueueReleasedScheduleHandoffsOnceAsync(
        CancellationToken stoppingToken = default)
    {
        var page = _scheduleService.ListPendingReleaseHandoffPageForWorker(
            _handoffCursor,
            _handoffBatchSize);
        if (page.Handoffs.Count == 0 && _handoffCursor is not null)
        {
            _handoffCursor = null;
            page = _scheduleService.ListPendingReleaseHandoffPageForWorker(
                cursor: null,
                _handoffBatchSize);
        }

        var enqueued = 0;
        var failed = 0;
        foreach (var handoff in page.Handoffs)
        {
            stoppingToken.ThrowIfCancellationRequested();
            try
            {
                if (handoff.RequestedFormats is not { Count: > 0 }
                    || handoff.ArtifactIds is not { Count: > 0 })
                {
                    throw new InvalidDataException(
                        "Scheduled reporting handoff has no exact artifact selection.");
                }

                const string actorId = "reporting-scheduled-distribution-worker";
                if (string.IsNullOrWhiteSpace(handoff.RecipientPrincipalId)
                    || !Enum.TryParse<ReportingAccessPrincipalKind>(
                        handoff.RecipientPrincipalKind,
                        ignoreCase: false,
                        out var recipientKind)
                    || !Enum.IsDefined(recipientKind))
                {
                    throw new InvalidDataException(
                        "Scheduled reporting handoff has no valid explicit typed recipient.");
                }

                var delegatedPrincipal = new ReportingAccessPrincipalScope(
                    recipientKind,
                    handoff.RecipientPrincipalId.Trim());
                var authority = new ReportingDistributionAuthority(
                    actorId,
                    handoff.TenantId,
                    handoff.CompanyId,
                    ImmutableArray<string>.Empty,
                    CanView: true,
                    CanDeliver: true,
                    CanAdminister: false,
                    CorrelationId: $"scheduled-handoff:{handoff.HandoffId}",
                    DelegatedPrincipal: delegatedPrincipal);
                var jobId = await _queueDeliveryAsync(
                        new SecureReportingDeliveryQueueCommand(
                            handoff.RunId,
                            handoff.DistributionId,
                            handoff.TransportId,
                            handoff.RecipientPrincipalId,
                            string.Empty,
                            handoff.Subject,
                            handoff.Body,
                            handoff.ArtifactIds,
                            handoff.GrantLifetimeSeconds,
                            handoff.GrantMaxUses,
                            handoff.MaxAttempts,
                            delegatedPrincipal.Kind),
                        authority,
                        stoppingToken)
                    .ConfigureAwait(false);

                _scheduleService.MarkReleaseHandoffEnqueuedForWorker(
                    handoff.HandoffId,
                    handoff.TenantId,
                    handoff.CompanyId,
                    jobId,
                    _timeProvider.GetUtcNow());
                enqueued++;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A not-yet-released run, unavailable relay, or transient store failure leaves the
                // handoff PendingRelease. The next cycle retries the same durable idempotency key.
                _logger.LogWarning(
                    "Scheduled reporting handoff remains pending after error type {ErrorType}.",
                    exception.GetType().Name);
                failed++;
            }
        }

        _handoffCursor = page.NextCursor;
        return new ReportingScheduledHandoffBridgeResult(
            page.Handoffs.Count,
            enqueued,
            failed,
            _handoffCursor);
    }
}

internal sealed record ReportingScheduledHandoffBridgeResult(
    int Attempted,
    int Enqueued,
    int Failed,
    ReportingScheduledHandoffCursor? NextCursor);
