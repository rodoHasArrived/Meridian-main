using Meridian.FinancialOperations.AccountingSystem;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

internal sealed class ExternalGlPostingHostedService(
    ExternalGlPostingDispatcher dispatcher, ILogger<ExternalGlPostingHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await dispatcher.ProcessNextAsync(stoppingToken).ConfigureAwait(false))
                    continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The provider may have received a request. The persisted attempt and lease drive
                // recovery, and provider bodies or credential material never enter worker logs.
                logger.LogWarning("External GL posting worker will resume after a {FailureType}.", ex.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
        }
    }
}
