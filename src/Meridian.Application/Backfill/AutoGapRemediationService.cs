using System.Collections.Concurrent;
using System.Net.Http;
using Meridian.Application.Logging;
using Meridian.Application.Monitoring.DataQuality;
using Meridian.Application.Scheduling;
using Meridian.Infrastructure.Adapters.Core;
using Serilog;
using QualityDataGap = Meridian.Application.Monitoring.DataQuality.DataGap;
using StorageGapAnalysisResult = Meridian.Infrastructure.Adapters.Core.GapAnalysisResult;

namespace Meridian.Application.Backfill;

public enum AutoRemediationOutcome
{
    None,
    Completed,
    FailedTransient,
    FailedPermanent,
    Skipped
}

public enum AutoRemediationTriggerSource
{
    DataQualityGap,
    GapAnalyzerScan,
    QualityAlert
}

public sealed record AutoGapRemediationPolicy(
    TimeSpan MinimumGapDuration,
    int MinimumGapSize,
    TimeSpan SymbolCooldown,
    TimeSpan ProviderCooldown,
    int MaxConcurrentRemediations,
    string DefaultProvider)
{
    public static AutoGapRemediationPolicy Default { get; } = new(
        MinimumGapDuration: TimeSpan.FromMinutes(2),
        MinimumGapSize: 1,
        SymbolCooldown: TimeSpan.FromMinutes(5),
        ProviderCooldown: TimeSpan.FromMinutes(1),
        MaxConcurrentRemediations: 2,
        DefaultProvider: "stooq");
}

public sealed record QualityAlertRemediationSignal(
    string Symbol,
    DateOnly From,
    DateOnly To,
    string? Provider,
    string AlertId,
    string Reason,
    int GapSize = 1);

internal sealed class AutoRemediationState
{
    public int Attempts { get; set; }
    public DateTimeOffset LastAttemptAt { get; set; }
    public AutoRemediationOutcome LastOutcome { get; set; }
}

/// <summary>
/// Coordinates automatic data-gap remediation requests from quality/gap signals.
/// Applies guardrails and executes through the backfill coordinator.
/// </summary>
public sealed class AutoGapRemediationService : IDisposable
{
    private readonly IBackfillExecutionGateway _backfillGateway;
    private readonly BackfillExecutionHistory _history;
    private readonly AutoGapRemediationPolicy _policy;
    private readonly ILogger _log;
    private readonly ConcurrentDictionary<string, AutoRemediationState> _idempotency = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _symbolCooldown = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _providerCooldown = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _concurrencyGate;
    private readonly DataQualityMonitoringService? _qualityMonitoringService;
    private bool _disposed;

    public AutoGapRemediationService(
        IBackfillExecutionGateway backfillGateway,
        BackfillExecutionHistory history,
        DataQualityMonitoringService? qualityMonitoringService = null,
        AutoGapRemediationPolicy? policy = null,
        ILogger? log = null)
    {
        _backfillGateway = backfillGateway;
        _history = history;
        _qualityMonitoringService = qualityMonitoringService;
        _policy = policy ?? AutoGapRemediationPolicy.Default;
        _log = log ?? LoggingSetup.ForContext<AutoGapRemediationService>();
        _concurrencyGate = new SemaphoreSlim(Math.Max(1, _policy.MaxConcurrentRemediations));

        if (_qualityMonitoringService is not null)
        {
            _qualityMonitoringService.OnGapDetected += OnQualityGapDetected;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_qualityMonitoringService is not null)
        {
            _qualityMonitoringService.OnGapDetected -= OnQualityGapDetected;
        }

        _concurrencyGate.Dispose();
        _disposed = true;
    }

    public Task HandleDataQualityGapAsync(QualityDataGap gap, string? provider = null, CancellationToken ct = default)
    {
        if (gap.Duration < _policy.MinimumGapDuration)
        {
            _log.Debug("Skipping remediation for {Symbol}: gap {Duration} below minimum {Minimum}", gap.Symbol, gap.Duration, _policy.MinimumGapDuration);
            return Task.CompletedTask;
        }

        var from = DateOnly.FromDateTime(gap.GapStart.UtcDateTime);
        var to = DateOnly.FromDateTime(gap.GapEnd.UtcDateTime);
        return EnqueueRemediationAsync(
            gap.Symbol,
            from,
            to,
            provider ?? _policy.DefaultProvider,
            AutoRemediationTriggerSource.DataQualityGap,
            $"gap:{gap.Severity}:{gap.Duration}",
            (int)Math.Max(gap.EstimatedMissedEvents, 1),
            ct);
    }

    public async Task HandleGapAnalysisResultAsync(StorageGapAnalysisResult result, string? provider = null, CancellationToken ct = default)
    {
        foreach (var (symbol, info) in result.SymbolGaps)
        {
            if (!info.HasGaps || info.GapDates.Count < _policy.MinimumGapSize)
            {
                continue;
            }

            foreach (var range in info.GetGapRanges())
            {
                await EnqueueRemediationAsync(
                    symbol,
                    range.From,
                    range.To,
                    provider ?? _policy.DefaultProvider,
                    AutoRemediationTriggerSource.GapAnalyzerScan,
                    $"scan:{result.Granularity}:{info.GapDates.Count}",
                    info.GapDates.Count,
                    ct).ConfigureAwait(false);
            }
        }
    }

    public Task HandleQualityAlertAsync(QualityAlertRemediationSignal signal, CancellationToken ct = default)
    {
        return EnqueueRemediationAsync(
            signal.Symbol,
            signal.From,
            signal.To,
            signal.Provider ?? _policy.DefaultProvider,
            AutoRemediationTriggerSource.QualityAlert,
            $"alert:{signal.AlertId}:{signal.Reason}",
            Math.Max(signal.GapSize, 1),
            ct);
    }

    private void OnQualityGapDetected(QualityDataGap gap)
    {
        _ = HandleDataQualityGapAsync(gap);
    }

    private async Task EnqueueRemediationAsync(
        string symbol,
        DateOnly from,
        DateOnly to,
        string provider,
        AutoRemediationTriggerSource source,
        string reason,
        int gapSize,
        CancellationToken ct)
    {
        if (gapSize < _policy.MinimumGapSize)
        {
            return;
        }

        var normalizedSymbol = symbol.ToUpperInvariant();
        var idempotencyKey = BuildIdempotencyKey(normalizedSymbol, provider, from, to);
        var now = DateTimeOffset.UtcNow;

        if (IsCoolingDown(_symbolCooldown, normalizedSymbol, _policy.SymbolCooldown, now) ||
            IsCoolingDown(_providerCooldown, provider, _policy.ProviderCooldown, now))
        {
            _log.Debug("Auto-remediation cooldown active for {Symbol}/{Provider}", normalizedSymbol, provider);
            return;
        }

        var state = _idempotency.GetOrAdd(idempotencyKey, _ => new AutoRemediationState());
        lock (state)
        {
            if (state.LastOutcome is AutoRemediationOutcome.Completed or AutoRemediationOutcome.Skipped &&
                now - state.LastAttemptAt < _policy.SymbolCooldown)
            {
                return;
            }

            state.Attempts++;
            state.LastAttemptAt = now;
            state.LastOutcome = AutoRemediationOutcome.None;
        }

        if (!await _concurrencyGate.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false))
        {
            UpdateOutcome(state, AutoRemediationOutcome.Skipped);
            return;
        }

        try
        {
            var execution = CreateExecutionLog(normalizedSymbol, provider, from, to, source, reason, idempotencyKey, state.Attempts);
            _history.AddExecution(execution);

            try
            {
                var request = new BackfillRequest(provider, new[] { normalizedSymbol }, from, to);
                var result = await _backfillGateway.RunAsync(request, ct).ConfigureAwait(false);

                execution.CompletedAt = DateTimeOffset.UtcNow;
                execution.Status = result.Success ? ExecutionStatus.Completed : ExecutionStatus.Failed;
                execution.ErrorMessage = result.Error;
                execution.Statistics.TotalBarsRetrieved = result.BarsWritten;
                execution.Statistics.TotalSymbols = 1;
                execution.Statistics.SuccessfulSymbols = result.Success ? 1 : 0;
                execution.Statistics.FailedSymbols = result.Success ? 0 : 1;
                execution.AutoRemediationLastOutcome = result.Success
                    ? AutoRemediationOutcome.Completed.ToString()
                    : AutoRemediationOutcome.FailedPermanent.ToString();

                UpdateCooldowns(normalizedSymbol, provider, now);
                UpdateOutcome(state, result.Success ? AutoRemediationOutcome.Completed : AutoRemediationOutcome.FailedPermanent);
            }
            catch (Exception ex)
            {
                execution.CompletedAt = DateTimeOffset.UtcNow;
                execution.Status = ExecutionStatus.Failed;
                execution.ErrorMessage = ex.Message;
                execution.AutoRemediationLastOutcome = IsTransientFailure(ex)
                    ? AutoRemediationOutcome.FailedTransient.ToString()
                    : AutoRemediationOutcome.FailedPermanent.ToString();

                var outcome = IsTransientFailure(ex)
                    ? AutoRemediationOutcome.FailedTransient
                    : AutoRemediationOutcome.FailedPermanent;

                UpdateOutcome(state, outcome);

                if (outcome == AutoRemediationOutcome.FailedTransient)
                {
                    _idempotency.TryRemove(idempotencyKey, out _);
                }
            }
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    private static string BuildIdempotencyKey(string symbol, string provider, DateOnly from, DateOnly to)
        => $"{symbol}|{provider.ToLowerInvariant()}|{from:yyyy-MM-dd}|{to:yyyy-MM-dd}";

    private static bool IsCoolingDown(ConcurrentDictionary<string, DateTimeOffset> state, string key, TimeSpan cooldown, DateTimeOffset now)
        => state.TryGetValue(key, out var lastAttempt) && (now - lastAttempt) < cooldown;

    private static bool IsTransientFailure(Exception ex)
        => ex is HttpRequestException or TimeoutException or OperationCanceledException;

    private static void UpdateOutcome(AutoRemediationState state, AutoRemediationOutcome outcome)
    {
        lock (state)
        {
            state.LastOutcome = outcome;
        }
    }

    private void UpdateCooldowns(string symbol, string provider, DateTimeOffset timestamp)
    {
        _symbolCooldown[symbol] = timestamp;
        _providerCooldown[provider] = timestamp;
    }

    private static BackfillExecutionLog CreateExecutionLog(
        string symbol,
        string provider,
        DateOnly from,
        DateOnly to,
        AutoRemediationTriggerSource source,
        string reason,
        string idempotencyKey,
        int attempt)
    {
        return new BackfillExecutionLog
        {
            ScheduleId = "auto-gap-remediation",
            ScheduleName = "Auto Gap Remediation",
            Trigger = ExecutionTrigger.AutoRemediation,
            ScheduledAt = DateTimeOffset.UtcNow,
            StartedAt = DateTimeOffset.UtcNow,
            FromDate = from,
            ToDate = to,
            Symbols = { symbol },
            Status = ExecutionStatus.Running,
            AutoRemediationTriggerReason = reason,
            AutoRemediationAttemptCount = attempt,
            AutoRemediationLastOutcome = AutoRemediationOutcome.None.ToString(),
            AutoRemediationIdempotencyKey = idempotencyKey,
            Warnings = { $"source={source}", $"provider={provider}" }
        };
    }
}
