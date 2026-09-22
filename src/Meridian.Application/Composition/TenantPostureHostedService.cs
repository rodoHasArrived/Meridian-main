using Meridian.Contracts.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition;

/// <summary>
/// Starts an unattributed process worker only under the final deployment-boundary posture.
/// Deferred construction honors instance and factory overrides registered after core composition.
/// </summary>
internal sealed class TenantPostureHostedService<TWorker>(
    IServiceProvider services, TenantScopeEnforcementOptions options,
    ILogger<TenantPostureHostedService<TWorker>> logger) : BackgroundService
    where TWorker : BackgroundService
{
    private TWorker? _worker;
    internal Type? ActiveWorkerType => options.IsFailClosed ? null : typeof(TWorker);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.IsFailClosed)
        {
            logger.LogWarning("{Worker} is withheld because strict tenant reads require retained authority per work item.", typeof(TWorker).Name);
            return;
        }
        _worker = ActivatorUtilities.CreateInstance<TWorker>(services);
        await _worker.StartAsync(cancellationToken).ConfigureAwait(false);
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    // The host must still observe background failures and apply BackgroundServiceExceptionBehavior.
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => _worker?.ExecuteTask ?? Task.CompletedTask;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_worker is not null) await _worker.StopAsync(cancellationToken).ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public override void Dispose()
    {
        _worker?.Dispose();
        _worker = null;
        base.Dispose();
    }
}
