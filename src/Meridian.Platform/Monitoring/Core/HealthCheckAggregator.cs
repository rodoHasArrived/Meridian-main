using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Application.Monitoring.Core;

/// <summary>
/// Default implementation of IHealthCheckAggregator that runs health checks
/// in parallel and aggregates results.
/// </summary>
public sealed class HealthCheckAggregator : IHealthCheckAggregator
{
    private readonly ConcurrentDictionary<string, IHealthCheckProvider> _providers = new();
    private readonly ILogger<HealthCheckAggregator> _log;
    private readonly TimeSpan _checkTimeout;

    /// <summary>
    /// Creates a new health check aggregator.
    /// </summary>
    /// <param name="checkTimeout">Timeout for individual health checks (default: 5 seconds).</param>
    /// <param name="log">Optional logger.</param>
    public HealthCheckAggregator(TimeSpan? checkTimeout = null, ILogger<HealthCheckAggregator>? log = null)
    {
        _checkTimeout = checkTimeout ?? TimeSpan.FromSeconds(5);
        _log = log ?? NullLogger<HealthCheckAggregator>.Instance;
    }

    /// <inheritdoc/>
    public void Register(IHealthCheckProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (_providers.TryAdd(provider.ComponentName, provider))
        {
            _log.LogDebug("Registered health check provider: {ComponentName}", provider.ComponentName);
        }
        else
        {
            _log.LogWarning("Health check provider already registered: {ComponentName}", provider.ComponentName);
        }
    }

    /// <inheritdoc/>
    public void Unregister(string componentName)
    {
        if (_providers.TryRemove(componentName, out _))
        {
            _log.LogDebug("Unregistered health check provider: {ComponentName}", componentName);
        }
    }

    /// <inheritdoc/>
    public async Task<AggregatedHealthReport> CheckAllAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var providers = _providers.Values.ToList();

        if (providers.Count == 0)
        {
            return new AggregatedHealthReport(
                HealthSeverity.Unknown,
                DateTimeOffset.UtcNow,
                TimeSpan.Zero,
                Array.Empty<HealthCheckResult>());
        }

        // Run all health checks in parallel with timeout
        var tasks = providers.Select(p => RunHealthCheckAsync(p, ct)).ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        sw.Stop();

        // Determine overall severity (worst case wins)
        var overallSeverity = results
            .Select(r => r.Severity)
            .DefaultIfEmpty(HealthSeverity.Unknown)
            .Max();

        return new AggregatedHealthReport(
            overallSeverity,
            DateTimeOffset.UtcNow,
            sw.Elapsed,
            results);
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult?> CheckAsync(string componentName, CancellationToken ct = default)
    {
        if (!_providers.TryGetValue(componentName, out var provider))
            return null;

        return await RunHealthCheckAsync(provider, ct).ConfigureAwait(false);
    }

    private async Task<HealthCheckResult> RunHealthCheckAsync(IHealthCheckProvider provider, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_checkTimeout);

            var result = await provider.CheckHealthAsync(cts.Token).ConfigureAwait(false);
            sw.Stop();

            return result with { Duration = sw.Elapsed };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            _log.LogWarning("Health check timed out for {ComponentName}", provider.ComponentName);
            return HealthCheckResult.Unhealthy(
                provider.ComponentName,
                "Health check timed out",
                sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log.LogError(ex, "Health check failed for {ComponentName}", provider.ComponentName);
            return HealthCheckResult.Unhealthy(
                provider.ComponentName,
                $"Health check failed: {ex.Message}",
                sw.Elapsed,
                ex);
        }
    }
}
