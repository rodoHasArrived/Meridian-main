using Microsoft.Extensions.Logging;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Shared helper for view-model entry points that cannot be async (property setters, event handlers).
/// Observes the task, logs failures through <see cref="ILogger"/>, and reports them to the UI.
/// </summary>
internal static class FireAndForget
{
    /// <summary>
    /// Observes <paramref name="task"/> without blocking. Any exception except cancellation is logged
    /// and forwarded to <paramref name="onError"/>; nothing escapes to the synchronization context.
    /// </summary>
    public static void SafeFireAndForget(
        this Task task,
        ILogger logger,
        string operation,
        Action<Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(logger);

        _ = ObserveAsync(task, logger, operation, onError);
    }

    internal static async Task ObserveAsync(
        Task task,
        ILogger logger,
        string operation,
        Action<Exception>? onError)
    {
        try
        {
            await task.ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("{Operation} was cancelled", operation);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Operation} failed", operation);
            try
            {
                onError?.Invoke(ex);
            }
            catch (Exception callbackEx)
            {
                logger.LogError(callbackEx, "Error callback for {Operation} failed", operation);
            }
        }
    }
}
