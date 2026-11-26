using Meridian.Contracts.Ledger;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Runs every active accrual schedule on an interval. Each run drafts only completed periods without a draft; a
/// refused period is logged and retried next time. A failing schedule never stops the others.
/// </summary>
public sealed class IntercompanyAccrualScheduleRunner(IServiceProvider services, ILogger<IntercompanyAccrualScheduleRunner> logger,
    TimeProvider? clock = null) : BackgroundService
{
    /// <summary>How often active schedules are checked.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>Delay before the first pass, so host start-up is never slowed by a run.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        { await Task.Delay(StartupDelay, _clock, stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            try
            { await Task.Delay(Interval, _clock, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One pass over every active schedule as of today (UTC).</summary>
    public async Task<IReadOnlyList<IntercompanyAccrualScheduleRunDto>> RunOnceAsync(CancellationToken ct)
    {
        var store = services.GetService<IIntercompanyAccrualScheduleStore>();
        var schedules = services.GetService<IntercompanyAccrualScheduleService>();
        if (store is null || schedules is null)
            return [];
        var asOf = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var results = new List<IntercompanyAccrualScheduleRunDto>();
        IReadOnlyList<IntercompanyAccrualScheduleDto> active;
        try
        { active = await store.ListActiveAsync(ct).ConfigureAwait(false); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "Accrual schedules could not be listed; the run is skipped");
            return [];
        }
        foreach (var schedule in active)
        {
            try
            {
                var run = await schedules.RunScheduledAsync(schedule, asOf, ct).ConfigureAwait(false);
                results.Add(run);
                foreach (var window in run.Windows.Where(item => item.Outcome is IntercompanyAccrualScheduleOutcomeDto.Drafted or IntercompanyAccrualScheduleOutcomeDto.Refused))
                    logger.LogInformation("Accrual schedule {ScheduleId} {Outcome} {AccrualFrom}..{AccrualTo}: {Reason}",
                        schedule.ScheduleId, window.Outcome, window.AccrualFrom, window.AccrualTo, window.Reason ?? window.DraftId?.ToString("D"));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogWarning(error, "Accrual schedule {ScheduleId} failed; the other schedules still run", schedule.ScheduleId);
            }
        }
        return results;
    }
}
