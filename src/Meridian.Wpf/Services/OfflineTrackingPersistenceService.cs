using System;
using System.Threading.Tasks;

namespace Meridian.Wpf.Services;

/// <summary>
/// Service for persisting offline tracking data — the durable pending-operations queue that
/// captures mutations attempted while the backend was unreachable. Initialization restores the
/// queue persisted by the previous session (clean shutdown or crash); persist/load snapshot and
/// reload it on demand. Implements singleton pattern for application-wide offline data management.
/// </summary>
public sealed class OfflineTrackingPersistenceService
{
    private static readonly Lazy<OfflineTrackingPersistenceService> _instance =
        new(() => new OfflineTrackingPersistenceService());

    private bool _initialized;

    /// <summary>
    /// Gets the singleton instance of the OfflineTrackingPersistenceService.
    /// </summary>
    public static OfflineTrackingPersistenceService Instance => _instance.Value;

    /// <summary>
    /// Gets whether the service has been initialized.
    /// </summary>
    public bool IsInitialized => _initialized;

    private OfflineTrackingPersistenceService()
    {
    }

    /// <summary>
    /// Initializes the offline tracking persistence service and performs crash recovery by
    /// restoring the durable pending-operations queue persisted by the previous session.
    /// </summary>
    /// <returns>A task representing the async operation.</returns>
    public async Task InitializeAsync()
    {
        await PendingOperationsQueueService.Instance.InitializeAsync().ConfigureAwait(false);
        _initialized = true;
    }

    /// <summary>
    /// Shuts down the offline tracking persistence service after persisting the current
    /// offline state.
    /// </summary>
    /// <returns>A task representing the async operation.</returns>
    public async Task ShutdownAsync()
    {
        await PersistAsync().ConfigureAwait(false);
        _initialized = false;
    }

    /// <summary>
    /// Persists offline data (the pending-operations queue) to storage.
    /// </summary>
    /// <returns>A task representing the async operation.</returns>
    public Task PersistAsync()
    {
        return PendingOperationsQueueService.Instance.PersistAsync();
    }

    /// <summary>
    /// Loads offline data from storage, restoring persisted pending operations when the queue
    /// has not been initialized yet.
    /// </summary>
    /// <returns>A task representing the async operation.</returns>
    public Task LoadAsync()
    {
        return PendingOperationsQueueService.Instance.InitializeAsync();
    }
}
