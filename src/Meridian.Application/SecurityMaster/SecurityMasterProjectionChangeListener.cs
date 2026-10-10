using System.Collections.Concurrent;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Keeps this node's <see cref="SecurityMasterProjectionCache"/> coherent with projection writes
/// committed on other nodes. Holds one dedicated PostgreSQL connection that <c>LISTEN</c>s on the
/// schema's projection channel (see <see cref="SecurityProjectionChangeNotification.ChannelFor"/>);
/// <see cref="PostgresSecurityMasterStore"/> issues the matching <c>pg_notify</c> inside every
/// projection-writing transaction, so a notification is delivered only once its change is durable.
/// Each notification is applied by <see cref="SecurityMasterProjectionChangeHandler"/>.
/// </summary>
/// <remarks>
/// PostgreSQL does not replay notifications to a listener that was not connected when they were
/// sent. When the listening connection is lost (or could not be established), the listener
/// reconnects with exponential backoff and then re-synchronises the whole cache, because changes
/// committed while it was away were never announced to it. The first subscription re-synchronises
/// too, closing the window between the startup warm and the first <c>LISTEN</c>. The periodic re-warm
/// (<see cref="SecurityMasterOptions.ProjectionCacheRefreshMinutes"/>) remains a backstop.
/// </remarks>
public sealed class SecurityMasterProjectionChangeListener : BackgroundService
{
    private static readonly TimeSpan ResyncRetryInterval = TimeSpan.FromSeconds(30);

    private readonly SecurityMasterOptions _options;
    private readonly SecurityMasterProjectionChangeHandler _handler;
    private readonly ILogger<SecurityMasterProjectionChangeListener> _logger;

    public SecurityMasterProjectionChangeListener(
        SecurityMasterOptions options,
        SecurityMasterProjectionChangeHandler handler,
        ILogger<SecurityMasterProjectionChangeListener> logger)
    {
        _options = options;
        _handler = handler;
        _logger = logger;
    }

    internal TimeSpan InitialReconnectDelay { get; init; } = TimeSpan.FromSeconds(1);

    internal TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Raised after each successful <c>LISTEN</c> (and any post-reconnect resync); test seam.</summary>
    internal event Action? Listening;

    /// <summary>Raised after each notification is applied; test seam.</summary>
    internal event Action<string, SecurityProjectionChangeOutcome>? NotificationHandled;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.ProjectionCacheNotificationsEnabled || string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            _logger.LogDebug("Security Master projection-change listener is disabled.");
            return;
        }

        var channel = SecurityProjectionChangeNotification.ChannelFor(_options.Schema);
        var connectionString = BuildListenerConnectionString(_options.ConnectionString);
        var reconnectDelay = InitialReconnectDelay;
        // Set whenever notifications may have been missed: after any failed or lost connection,
        // and before the first LISTEN — a write committed between the startup warm and the first
        // subscription was never announced to this node, and the periodic re-warm is off by default.
        var resyncPending = true;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pending = new ConcurrentQueue<string>();
                await using var connection = new NpgsqlConnection(connectionString);
                connection.Notification += (_, e) =>
                {
                    if (string.Equals(e.Channel, channel, StringComparison.Ordinal))
                    {
                        pending.Enqueue(e.Payload);
                    }
                };

                await connection.OpenAsync(stoppingToken).ConfigureAwait(false);
                await using (var listen = new NpgsqlCommand($"LISTEN \"{channel}\";", connection))
                {
                    await listen.ExecuteNonQueryAsync(stoppingToken).ConfigureAwait(false);
                }

                reconnectDelay = InitialReconnectDelay;
                _logger.LogInformation(
                    "Security Master projection-change listener subscribed to channel {Channel}",
                    channel);

                if (resyncPending)
                {
                    resyncPending = !await TryResynchronizeAsync(stoppingToken).ConfigureAwait(false);
                }

                Listening?.Invoke();

                while (true)
                {
                    while (pending.TryDequeue(out var payload))
                    {
                        var outcome = await _handler.HandleAsync(payload, stoppingToken).ConfigureAwait(false);
                        NotificationHandled?.Invoke(payload, outcome);
                    }

                    var notified = await connection.WaitAsync(ResyncRetryInterval, stoppingToken).ConfigureAwait(false);
                    if (!notified && resyncPending)
                    {
                        resyncPending = !await TryResynchronizeAsync(stoppingToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                resyncPending = true;
                _logger.LogWarning(
                    ex,
                    "Security Master projection-change listener lost its connection; reconnecting in {DelayMs} ms and re-synchronising the projection cache afterwards",
                    (long)reconnectDelay.TotalMilliseconds);
                try
                {
                    await Task.Delay(reconnectDelay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                var doubled = TimeSpan.FromTicks(reconnectDelay.Ticks * 2);
                reconnectDelay = doubled > MaxReconnectDelay ? MaxReconnectDelay : doubled;
            }
        }
    }

    private async Task<bool> TryResynchronizeAsync(CancellationToken ct)
    {
        try
        {
            await _handler.ResynchronizeAsync(ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Security Master projection cache re-synchronised after the change listener subscribed");
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Security Master projection cache re-synchronisation after reconnect failed; retrying in {DelaySeconds} s",
                (int)ResyncRetryInterval.TotalSeconds);
            return false;
        }
    }

    private static string BuildListenerConnectionString(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            // A dedicated, never-pooled session: returning it to a pool would UNLISTEN it.
            Pooling = false
        };

        if (builder.KeepAlive <= 0)
        {
            // Probe an otherwise idle listening session so a dead peer surfaces as a connection
            // failure (and a reconnect + resync) instead of an indefinitely silent wait.
            builder.KeepAlive = 30;
        }

        return builder.ConnectionString;
    }
}
