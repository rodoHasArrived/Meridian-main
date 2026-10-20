using Meridian.DataIntegration.Credentials;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Config.Credentials;

/// <summary>
/// Replays credential audit records that a committed vault mutation still owes to the audit log
/// (PRD-002: credential/audit commit ambiguity). The vault also replays lazily on its next
/// mutation; this start-up pass closes the window for hosts that never mutate credentials again.
/// Replay failure is logged and never refuses startup: the journal remains the durable marker.
/// </summary>
public sealed class ProviderCredentialAuditReplayHostedService : IHostedService
{
    private readonly IProviderCredentialStore _store;
    private readonly ILogger<ProviderCredentialAuditReplayHostedService> _logger;

    public ProviderCredentialAuditReplayHostedService(
        IProviderCredentialStore store,
        ILogger<ProviderCredentialAuditReplayHostedService> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_store is not FileProviderCredentialStore vault)
            return;

        try
        {
            var replayed = await vault.ReplayPendingAuditAsync(cancellationToken).ConfigureAwait(false);
            if (replayed > 0)
                _logger.LogInformation("Replayed {ReplayedAuditCount} journaled credential audit record(s) at startup", replayed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Replay is a recovery convenience, never a startup gate: whatever the journal holds, the
            // host starts and the journal is retained for the next attempt or for review.
            _logger.LogWarning(ex, "Credential audit replay at startup did not complete; the journal is retained for the next attempt");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
