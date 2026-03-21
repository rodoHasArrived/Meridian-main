namespace Meridian.Ui.Services.Contracts;

/// <summary>
/// Interface for persisting offline tracking data.
/// Shared between WPF desktop applications.
/// </summary>
public interface IOfflineTrackingPersistenceService
{
    Task SaveOfflineDataAsync<T>(string key, T data, CancellationToken cancellationToken = default);
    Task<T?> LoadOfflineDataAsync<T>(string key, CancellationToken cancellationToken = default);
    Task DeleteOfflineDataAsync(string key, CancellationToken cancellationToken = default);
    Task<bool> HasOfflineDataAsync(string key, CancellationToken cancellationToken = default);
}
