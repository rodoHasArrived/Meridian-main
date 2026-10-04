using Meridian.Application.Composition;
using Microsoft.Extensions.Hosting;

namespace Meridian.Wpf.Services;

/// <summary>
/// Gates desktop activation on completed migrations/imports and retained-data inspection.
/// The hosted-service phase reuses this result instead of inspecting again behind the shell.
/// </summary>
public sealed class DesktopTenantStartup(
    ITenantCutoverStartupPrerequisites prerequisites,
    TenantCutoverGuardService guard) : IHostedService
{
    private readonly object _gate = new();
    private Task? _readiness;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return (_readiness ??= PrepareAndInspectAsync(cancellationToken)).WaitAsync(cancellationToken);
    }

    public async Task ActivateAsync(Action activateDesktop, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activateDesktop);
        // Preserve the WPF dispatcher context for workspace and window creation.
        await StartAsync(cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        activateDesktop();
    }

    private async Task PrepareAndInspectAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            await prerequisites.PrepareAsync(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
            await guard.StartAsync(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StartupRefusedException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Migration/import errors can contain retained records or credentials. Refuse with
            // a safe diagnostic; never continue into a workspace after an unverified upgrade.
            throw new StartupRefusedException(
                $"Desktop tenant readiness could not be completed ({exception.GetType().Name}). " +
                "The workspace has not been opened. Verify database connectivity and schema migrations, " +
                "then review retained-data attribution using docs/operators/fund-structure-tenant-backfill.md.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
