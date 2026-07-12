using Meridian.FinancialOperations.Reconciliation.Connectors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Minimal scheduled-fetch loop for statement connectors: every minute, runs any enabled
/// fetch schedule that is due (fetch → commit → reconciliation queue). Failures are
/// recorded on the schedule and never crash the loop, and downstream duplicate-key
/// idempotency makes repeated or overlapping runs safe.
/// </summary>
public sealed class StatementFetchSchedulerService(
    IServiceProvider serviceProvider,
    ILogger<StatementFetchSchedulerService> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var runner = serviceProvider.GetService<StatementFetchScheduleRunner>();
        if (runner is null)
        {
            logger.LogDebug("Statement fetch scheduler idle: connector services are not registered in this host.");
            return;
        }

        using var timer = new PeriodicTimer(TickInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var ran = await runner.RunDueSchedulesAsync(DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false);
                if (ran > 0)
                {
                    logger.LogInformation("Statement fetch scheduler executed {Count} due schedule(s).", ran);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Statement fetch scheduler tick failed; retrying on the next interval.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

public static class StatementFetchSchedulerServiceCollectionExtensions
{
    public static IServiceCollection AddStatementFetchSchedulerHostedService(this IServiceCollection services)
    {
        services.TryAddSingleton<StatementFetchSchedulerService>();
        services.AddHostedService(sp => sp.GetRequiredService<StatementFetchSchedulerService>());
        return services;
    }
}
