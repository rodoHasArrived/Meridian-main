using System.Diagnostics;
using System.Text;
using System.Threading;
using Meridian.Application.Monitoring;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// IHostedService that ensures graceful shutdown by flushing all registered buffers
/// before the application terminates. This prevents data loss during shutdown.
/// </summary>
public sealed class GracefulShutdownService : IHostedService
{
    private readonly IReadOnlyList<IFlushable> _flushables;
    private readonly ILogger _log;
    private readonly TimeSpan _shutdownTimeout;
    private readonly Stopwatch _sessionStopwatch = new();

    /// <summary>
    /// Creates a new graceful shutdown service.
    /// </summary>
    /// <param name="flushables">Collection of flushable components to flush on shutdown</param>
    /// <param name="shutdownTimeout">Maximum time to wait for flush operations (default: 30 seconds)</param>
    public GracefulShutdownService(
        IEnumerable<IFlushable> flushables,
        TimeSpan? shutdownTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(flushables, nameof(flushables));
        _flushables = flushables.ToList();
        _shutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(30);
        _log = Log.ForContext<GracefulShutdownService>();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionStopwatch.Start();
        _log.Information("Graceful shutdown service initialized with {Count} flushable components",
            _flushables.Count);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionStopwatch.Stop();
        _log.Information("Graceful shutdown initiated - flushing all buffers...");

        using var timeoutCts = new CancellationTokenSource(_shutdownTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        var flushTasks = new List<Task>();

        foreach (var flushable in _flushables)
        {
            var name = flushable.GetType().Name;
            _log.Debug("Flushing {Component}...", name);

            flushTasks.Add(FlushWithLoggingAsync(flushable, name, linkedCts.Token));
        }

        try
        {
            await Task.WhenAll(flushTasks).ConfigureAwait(false);
            _log.Information("All {Count} buffers flushed successfully", _flushables.Count);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _log.Warning("Shutdown timeout ({Timeout}s) reached - some data may be lost",
                _shutdownTimeout.TotalSeconds);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during graceful shutdown flush");
        }

        PrintSessionSummary();
    }

    private void PrintSessionSummary()
    {
        try
        {
            var snapshot = Metrics.GetSnapshot();
            var duration = _sessionStopwatch.Elapsed;
            var totalEvents = snapshot.Published + snapshot.Dropped;
            var completeness = totalEvents > 0
                ? (double)snapshot.Published / totalEvents * 100.0
                : 100.0;

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("  ╔══════════════════════════════════════════╗");
            sb.AppendLine($"  ║  Session Summary ({FormatDuration(duration),-21})║");
            sb.AppendLine("  ╠══════════════════════════════════════════╣");
            sb.AppendLine($"  ║  Events collected:  {snapshot.Published,18:N0} ║");
            sb.AppendLine($"  ║  Events dropped:    {snapshot.Dropped,18:N0} ║");
            sb.AppendLine($"  ║  Data completeness: {completeness,17:F1}% ║");

            if (snapshot.Trades > 0)
                sb.AppendLine($"  ║  Trades:            {snapshot.Trades,18:N0} ║");
            if (snapshot.DepthUpdates > 0)
                sb.AppendLine($"  ║  Depth updates:     {snapshot.DepthUpdates,18:N0} ║");
            if (snapshot.Quotes > 0)
                sb.AppendLine($"  ║  Quotes:            {snapshot.Quotes,18:N0} ║");
            if (snapshot.HistoricalBars > 0)
                sb.AppendLine($"  ║  Historical bars:   {snapshot.HistoricalBars,18:N0} ║");
            if (snapshot.Integrity > 0)
                sb.AppendLine($"  ║  Integrity events:  {snapshot.Integrity,18:N0} ║");

            sb.AppendLine("  ╠══════════════════════════════════════════╣");

            // Throughput rates
            if (duration.TotalSeconds > 0)
            {
                var eventsPerSec = snapshot.Published / duration.TotalSeconds;
                sb.AppendLine($"  ║  Avg throughput:    {eventsPerSec,14:N1}/sec ║");
            }

            sb.AppendLine($"  ║  Avg latency:       {snapshot.AverageLatencyUs,15:F1} us ║");

            if (snapshot.MinLatencyUs > 0 || snapshot.MaxLatencyUs > 0)
                sb.AppendLine($"  ║  Latency range:     {snapshot.MinLatencyUs:F0}-{snapshot.MaxLatencyUs:F0} us{"",-1} ║");

            sb.AppendLine($"  ║  Memory usage:      {snapshot.MemoryUsageMb,14:F1} MB ║");
            sb.AppendLine($"  ║  GC collections:    {$"G0={snapshot.Gc0Collections} G1={snapshot.Gc1Collections} G2={snapshot.Gc2Collections}",-18} ║");
            sb.AppendLine("  ╚══════════════════════════════════════════╝");
            sb.AppendLine();

            var summaryText = sb.ToString();
            _log.Information("Session summary:{Summary}", summaryText);
            Console.Write(summaryText);
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Failed to generate session summary");
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
        if (duration.TotalMinutes >= 1)
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        return $"{duration.Seconds}s";
    }

    private async Task FlushWithLoggingAsync(IFlushable flushable, string name, CancellationToken ct)
    {
        try
        {
            await flushable.FlushAsync(ct).ConfigureAwait(false);
            _log.Debug("Successfully flushed {Component}", name);
        }
        catch (OperationCanceledException)
        {
            _log.Warning("Flush of {Component} was cancelled", name);
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to flush {Component}", name);
            throw;
        }
    }
}
