using Microsoft.Extensions.Logging;

namespace Meridian.Application.Integrations;

internal readonly record struct ProviderIntegrationBoundaryContext(
    string? TenantId = null,
    string? ManifestId = null,
    string? ConnectionId = null,
    string? Capability = null,
    string? EndpointKey = null,
    string? SyncRunId = null);

internal static class ProviderIntegrationServiceBoundary
{
    public static async Task<T> RunAsync<T>(
        ILogger logger,
        string operation,
        ProviderIntegrationBoundaryContext context,
        Func<Task<T>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception ex) when (ShouldLog(ex))
        {
            LogFailure(logger, ex, operation, context);
            throw;
        }
    }

    public static async Task RunAsync(
        ILogger logger,
        string operation,
        ProviderIntegrationBoundaryContext context,
        Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex) when (ShouldLog(ex))
        {
            LogFailure(logger, ex, operation, context);
            throw;
        }
    }

    private static void LogFailure(
        ILogger logger,
        Exception exception,
        string operation,
        ProviderIntegrationBoundaryContext context)
        => logger.LogWarning(
            exception,
            "Provider integration operation {Operation} failed for TenantId {TenantId}, ManifestId {ManifestId}, ConnectionId {ConnectionId}, Capability {Capability}, EndpointKey {EndpointKey}, SyncRunId {SyncRunId}. Preserving fail-fast behavior.",
            operation,
            ValueOrDefault(context.TenantId),
            ValueOrDefault(context.ManifestId),
            ValueOrDefault(context.ConnectionId),
            ValueOrDefault(context.Capability),
            ValueOrDefault(context.EndpointKey),
            ValueOrDefault(context.SyncRunId));

    private static bool ShouldLog(Exception exception)
        => exception is not OperationCanceledException and not OutOfMemoryException;

    private static string ValueOrDefault(string? value)
        => string.IsNullOrWhiteSpace(value) ? "(default)" : value;
}
