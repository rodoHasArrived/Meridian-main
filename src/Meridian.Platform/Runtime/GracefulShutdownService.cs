using System.Diagnostics;
using System.Text;
using System.Threading;
using Meridian.Core.Services;
using Meridian.Platform.Tracing;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// IHostedService that ensures graceful shutdown by flushing all registered buffers
/// before the application terminates. This prevents data loss during shutdown.
/// </summary>
public sealed class GracefulShutdownService : IHostedService
{
    private const string ShutdownOperationName = "runtime.shutdown.flush";

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
        _log.Information(
            "Graceful shutdown service initialized for {OperationName}; flushableComponents={FlushableCount}; shutdownTimeoutSeconds={ShutdownTimeoutSeconds}",
            ShutdownOperationName,
            _flushables.Count,
            _shutdownTimeout.TotalSeconds);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionStopwatch.Stop();
        var correlationId = Guid.NewGuid().ToString("N");
        var shutdownStopwatch = Stopwatch.StartNew();
        _log.Information(
            "Graceful shutdown initiated for {OperationName} with {CorrelationId}; flushableComponents={FlushableCount}; uptimeMs={UptimeMs}",
            ShutdownOperationName,
            correlationId,
            _flushables.Count,
            _sessionStopwatch.ElapsedMilliseconds);

        using var timeoutCts = new CancellationTokenSource(_shutdownTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        var flushTasks = new List<Task<FlushOutcome>>();

        foreach (var flushable in _flushables)
        {
            var name = flushable.GetType().Name;
            _log.Debug(
                "Flushing {ComponentName} for {OperationName} with {CorrelationId}",
                name,
                ShutdownOperationName,
                correlationId);

            flushTasks.Add(FlushWithLoggingAsync(flushable, name, correlationId, linkedCts.Token));
        }

        FlushOutcome[] outcomes = [];
        try
        {
            outcomes = await Task.WhenAll(flushTasks).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error(
                ex,
                "Unexpected error collecting flush outcomes for {OperationName} with {CorrelationId}",
                ShutdownOperationName,
                correlationId);
        }
        finally
        {
            shutdownStopwatch.Stop();
            LogShutdownOutcome(correlationId, outcomes, timeoutCts.IsCancellationRequested, shutdownStopwatch.Elapsed);
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

    private void LogShutdownOutcome(
        string correlationId,
        IReadOnlyCollection<FlushOutcome> outcomes,
        bool timedOut,
        TimeSpan elapsed)
    {
        var succeeded = outcomes.Count(static outcome => outcome.Status == FlushStatus.Succeeded);
        var failed = outcomes.Count(static outcome => outcome.Status == FlushStatus.Failed);
        var cancelled = outcomes.Count(static outcome => outcome.Status == FlushStatus.Cancelled);
        var missing = _flushables.Count - outcomes.Count;

        if (timedOut || cancelled > 0 || missing > 0)
        {
            _log.Warning(
                "Graceful shutdown completed with incomplete flushes for {OperationName} with {CorrelationId}; succeeded={Succeeded}; failed={Failed}; cancelled={Cancelled}; missing={Missing}; elapsedMs={ElapsedMs}; timeoutSeconds={TimeoutSeconds}; recoveryAction={RecoveryAction}",
                ShutdownOperationName,
                correlationId,
                succeeded,
                failed,
                cancelled,
                missing,
                elapsed.TotalMilliseconds,
                _shutdownTimeout.TotalSeconds,
                "Review component logs before trusting latest buffered data");
            return;
        }

        if (failed > 0)
        {
            _log.Error(
                "Graceful shutdown completed with flush failures for {OperationName} with {CorrelationId}; succeeded={Succeeded}; failed={Failed}; elapsedMs={ElapsedMs}; recoveryAction={RecoveryAction}",
                ShutdownOperationName,
                correlationId,
                succeeded,
                failed,
                elapsed.TotalMilliseconds,
                "Inspect failed component and rerun affected ingestion or export");
            return;
        }

        _log.Information(
            "Graceful shutdown completed for {OperationName} with {CorrelationId}; succeeded={Succeeded}; failed=0; cancelled=0; elapsedMs={ElapsedMs}",
            ShutdownOperationName,
            correlationId,
            succeeded,
            elapsed.TotalMilliseconds);
    }

    private async Task<FlushOutcome> FlushWithLoggingAsync(
        IFlushable flushable,
        string name,
        string correlationId,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await flushable.FlushAsync(ct).ConfigureAwait(false);
            stopwatch.Stop();
            _log.Debug(
                "Successfully flushed {ComponentName} for {OperationName} with {CorrelationId} in {ElapsedMs} ms",
                name,
                ShutdownOperationName,
                correlationId,
                stopwatch.ElapsedMilliseconds);
            return new FlushOutcome(name, FlushStatus.Succeeded, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _log.Warning(
                "Flush of {ComponentName} was cancelled for {OperationName} with {CorrelationId} after {ElapsedMs} ms",
                name,
                ShutdownOperationName,
                correlationId,
                stopwatch.ElapsedMilliseconds);
            return new FlushOutcome(name, FlushStatus.Cancelled, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _log.Error(
                ex,
                "Failed to flush {ComponentName} for {OperationName} with {CorrelationId} after {ElapsedMs} ms",
                name,
                ShutdownOperationName,
                correlationId,
                stopwatch.ElapsedMilliseconds);
            return new FlushOutcome(name, FlushStatus.Failed, stopwatch.Elapsed);
        }
    }

    private sealed record FlushOutcome(string ComponentName, FlushStatus Status, TimeSpan Elapsed);

    private enum FlushStatus
    {
        Succeeded,
        Failed,
        Cancelled
    }
}
