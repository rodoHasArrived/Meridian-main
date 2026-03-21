namespace Meridian.Ui.Services.Contracts;

/// <summary>
/// Interface for queuing and managing pending operations.
/// Shared between WPF desktop applications.
/// </summary>
public interface IPendingOperationsQueueService
{
    Task EnqueueAsync<T>(T operation, CancellationToken cancellationToken = default);
    Task<T?> DequeueAsync<T>(CancellationToken cancellationToken = default);
    Task<int> GetQueueLengthAsync(CancellationToken cancellationToken = default);
    Task ClearQueueAsync(CancellationToken cancellationToken = default);
}
