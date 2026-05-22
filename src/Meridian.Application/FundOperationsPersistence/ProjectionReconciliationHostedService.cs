using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.FundOperationsPersistence;

public sealed class ProjectionReconciliationHostedService(
    IEnumerable<IDomainProjectionReconciliationJob> jobs,
    ILogger<ProjectionReconciliationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var job in jobs)
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

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken).ConfigureAwait(false);
        }
    }
}
