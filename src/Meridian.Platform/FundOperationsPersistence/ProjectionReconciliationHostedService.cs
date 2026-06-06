using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Platform.FundOperationsPersistence;

public sealed class ProjectionReconciliationHostedService(
    IEnumerable<IDomainProjectionReconciliationJob> jobs,
    ILogger<ProjectionReconciliationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        try
        {
            do
            {
                foreach (var job in jobs)
                {
                    try
                    {
                        var discrepancies = await job.ReconcileAsync(stoppingToken).ConfigureAwait(false);
                        foreach (var discrepancy in discrepancies)
                        {
                            logger.LogWarning(
                                "Shadow projection discrepancy in {Domain} {Projection}/{EntityKey}: {Summary}",
                                discrepancy.Domain,
                                discrepancy.ProjectionName,
                                discrepancy.EntityKey,
                                discrepancy.Summary);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Projection reconciliation job {JobType} failed.", job.GetType().FullName);
                    }
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Projection reconciliation hosted service encountered an unexpected error.");
        }
    }
}
