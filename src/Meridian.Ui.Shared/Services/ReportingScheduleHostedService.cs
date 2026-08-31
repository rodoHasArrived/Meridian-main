using Meridian.Contracts.Workstation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public sealed record ReportingScheduleWorkerOptions(TimeSpan PollInterval)
{
    public static ReportingScheduleWorkerOptions Default { get; } = new(TimeSpan.FromMinutes(1));
}

internal sealed record ReportingScheduleWorkerFailure(
    string TenantId,
    string CompanyId,
    string ScheduleId,
    string ErrorType,
    string? FailureRecordingErrorType);

internal sealed record ReportingScheduleWorkerBatchResult(
    ReportingDueScheduleRunResultDto Result,
    IReadOnlyList<ReportingScheduleWorkerFailure> Failures);

/// <summary>
/// Server-owned schedule clock. Public due-run mutation routes are retired; this worker discovers
/// due records internally and lets <see cref="ReportingScheduleService"/> reconstruct a separate,
/// exact tenant/company authority for every scheduled run.
/// </summary>
public sealed class ReportingScheduleHostedService : BackgroundService
{
    private readonly Func<
        DateTimeOffset,
        CancellationToken,
        Task<ReportingScheduleWorkerBatchResult>> _runDueAsync;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReportingScheduleHostedService> _logger;
    private readonly ReportingScheduleWorkerOptions _options;
    private readonly ReportingScheduleWorkerReadinessState _readiness;

    public ReportingScheduleHostedService(
        ReportingScheduleService scheduleService,
        TimeProvider timeProvider,
        ILogger<ReportingScheduleHostedService> logger,
        ReportingScheduleWorkerOptions? options = null)
        : this(scheduleService, timeProvider, logger, options, readiness: null)
    {
    }

    public ReportingScheduleHostedService(
        ReportingScheduleService scheduleService,
        TimeProvider timeProvider,
        ILogger<ReportingScheduleHostedService> logger,
        ReportingScheduleWorkerOptions? options,
        ReportingScheduleWorkerReadinessState? readiness)
    {
        ArgumentNullException.ThrowIfNull(scheduleService);
        _runDueAsync = scheduleService.RunDueForWorkerAsync;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? ReportingScheduleWorkerOptions.Default;
        _readiness = readiness ?? new ReportingScheduleWorkerReadinessState();
        ValidateOptions(_options, nameof(options));
    }

    internal ReportingScheduleHostedService(
        Func<
            DateTimeOffset,
            CancellationToken,
            Task<ReportingScheduleWorkerBatchResult>> runDueAsync,
        TimeProvider timeProvider,
        ILogger<ReportingScheduleHostedService> logger,
        ReportingScheduleWorkerOptions options,
        ReportingScheduleWorkerReadinessState readiness)
    {
        _runDueAsync = runDueAsync ?? throw new ArgumentNullException(nameof(runDueAsync));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        ValidateOptions(_options, nameof(options));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(_options.PollInterval);
            do
            {
                try
                {
                    var batch = await _runDueAsync(
                            _timeProvider.GetUtcNow(),
                            stoppingToken)
                        .ConfigureAwait(false);
                    foreach (var failure in batch.Failures)
                    {
                        _logger.LogError(
                            "Scheduled report {ScheduleId} failed closed for tenant {TenantId} and company {CompanyId} with error type {ErrorType}; failure recording error type was {FailureRecordingErrorType}; other due schedules continued.",
                            failure.ScheduleId,
                            failure.TenantId,
                            failure.CompanyId,
                            failure.ErrorType,
                            failure.FailureRecordingErrorType ?? "none");
                    }

                    _readiness.MarkReady(_timeProvider.GetUtcNow());
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _readiness.MarkCycleFailed();
                    _logger.LogError(
                        "Scheduled reporting cycle failed closed with error type {ErrorType}.",
                        exception.GetType().Name);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        finally
        {
            _readiness.MarkNotReady();
        }
    }

    private static void ValidateOptions(
        ReportingScheduleWorkerOptions options,
        string parameterName)
    {
        if (options.PollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "The reporting schedule worker poll interval must be positive.");
        }
    }
}
