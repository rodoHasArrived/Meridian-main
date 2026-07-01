using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using Meridian.Core.Logging;
using Meridian.DataIntegration.Monitoring.DataQuality;
using Meridian.Application.Scheduling;
using Meridian.Infrastructure.Adapters.Core;
using Serilog;
using QualityDataGap = Meridian.DataIntegration.Monitoring.DataQuality.DataGap;
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

public enum BackfillRemediationSlaTier
{
    Standard,
    SameBusinessDay
}

public sealed record BackfillRemediationSlaDecision(
    BackfillRemediationSlaTier Tier,
    DateTimeOffset DueAtUtc,
    bool RequiresOwnerAssignment,
    string DownstreamWorkflow,
    string ReasonCode);

public enum BackfillRemediationSlaStatus
{
    Open,
    DueSoon,
    Overdue,
    Failed,
    Completed
}

public sealed record BackfillRemediationSlaStatusItem(
    string ExecutionId,
    string IdempotencyKey,
    BackfillRemediationSlaTier Tier,
    BackfillRemediationSlaStatus Status,
    DateTimeOffset DueAtUtc,
    bool RequiresOwnerAssignment,
    string DownstreamWorkflow,
    string ReasonCode,
    string Provider,
    IReadOnlyList<string> Symbols,
    DateOnly From,
    DateOnly To,
    ExecutionStatus ExecutionStatus,
    string? LastOutcome,
    TimeSpan TimeRemaining);

public sealed record BackfillRemediationSlaSnapshot(
    DateTimeOffset EvaluatedAtUtc,
    int Total,
    int OverdueCount,
    int DueSoonCount,
    int RequiresOwnerAssignmentCount,
    IReadOnlyList<BackfillRemediationSlaStatusItem> Items);

public sealed record BackfillRemediationSlaPolicy(
    TimeSpan StandardWindow,
    TimeSpan SameBusinessDayWindow)
{
    private static readonly string[] CriticalDownstreamWorkflows =
    [
        "paper",
        "paper-trading",
        "reconciliation",
        "accounting",
        "reporting",
        "governed-reporting"
    ];

    public static BackfillRemediationSlaPolicy Default { get; } = new(
        StandardWindow: TimeSpan.FromHours(48),
        SameBusinessDayWindow: TimeSpan.FromHours(8));

    public BackfillRemediationSlaDecision Classify(
        AutoRemediationTriggerSource source,
        string? severity,
        string? downstreamWorkflow,
        DateTimeOffset observedAtUtc)
    {
        var normalizedWorkflow = NormalizeWorkflow(downstreamWorkflow);
        var criticalWorkflow = CriticalDownstreamWorkflows.Any(workflow =>
            normalizedWorkflow.Contains(workflow, StringComparison.OrdinalIgnoreCase));
        var criticalSeverity = IsCriticalSeverity(severity);
        var alertDriven = source == AutoRemediationTriggerSource.QualityAlert;
        var sameBusinessDay = criticalWorkflow || criticalSeverity || alertDriven;
        var tier = sameBusinessDay
            ? BackfillRemediationSlaTier.SameBusinessDay
            : BackfillRemediationSlaTier.Standard;
        var reasonCode = criticalWorkflow
            ? "CriticalWorkflow"
            : criticalSeverity
                ? "CriticalSeverity"
                : alertDriven
                    ? "QualityAlert"
                    : "StandardGap";

        return new BackfillRemediationSlaDecision(
            tier,
            observedAtUtc.Add(sameBusinessDay ? SameBusinessDayWindow : StandardWindow),
            sameBusinessDay,
            normalizedWorkflow,
            reasonCode);
    }

    private static bool IsCriticalSeverity(string? severity)
        => string.Equals(severity, "Major", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(severity, "Critical", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeWorkflow(string? downstreamWorkflow)
        => string.IsNullOrWhiteSpace(downstreamWorkflow)
            ? "unassigned"
            : downstreamWorkflow.Trim().ToLowerInvariant();
}

public sealed record QualityAlertRemediationSignal(
    string Symbol,
    DateOnly From,
    DateOnly To,
    string? Provider,
    string AlertId,
    string Reason,
    int GapSize = 1,
    string? Severity = null,
    string? DownstreamWorkflow = null);

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
    private readonly BackfillRemediationSlaPolicy _slaPolicy;
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
        ILogger? log = null,
        BackfillRemediationSlaPolicy? slaPolicy = null)
    {
        _backfillGateway = backfillGateway;
        _history = history;
        _qualityMonitoringService = qualityMonitoringService;
        _policy = policy ?? AutoGapRemediationPolicy.Default;
        _slaPolicy = slaPolicy ?? BackfillRemediationSlaPolicy.Default;
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
            [gap.Symbol],
            from,
            to,
            provider ?? _policy.DefaultProvider,
            AutoRemediationTriggerSource.DataQualityGap,
            $"gap:{gap.Severity}:{gap.Duration}",
            (int)Math.Max(gap.EstimatedMissedEvents, 1),
            gap.Severity.ToString(),
            downstreamWorkflow: null,
            ct: ct);
    }

    public async Task HandleGapAnalysisResultAsync(StorageGapAnalysisResult result, string? provider = null, CancellationToken ct = default)
    {
        var candidates = new List<AutoGapRemediationCandidate>();
        var remediationProvider = provider ?? _policy.DefaultProvider;

        foreach (var (symbol, info) in result.SymbolGaps)
        {
            if (!info.HasGaps || info.GapDates.Count < _policy.MinimumGapSize)
            {
                continue;
            }

            foreach (var range in info.GetGapRanges())
            {
                candidates.Add(new AutoGapRemediationCandidate(
                    symbol,
                    range.From,
                    range.To,
                    remediationProvider,
                    AutoRemediationTriggerSource.GapAnalyzerScan,
                    $"scan:{result.Granularity}:{info.GapDates.Count}",
                    info.GapDates.Count));
            }
        }

        foreach (var group in candidates
                     .GroupBy(static candidate => new AutoRemediationBatchKey(
                         candidate.Provider,
                         candidate.From,
                         candidate.To,
                         candidate.Source))
                     .OrderBy(static group => group.Key.From)
                     .ThenBy(static group => group.Key.To)
                     .ThenBy(static group => group.Key.Provider, StringComparer.OrdinalIgnoreCase))
        {
            var symbols = group
                .Select(static candidate => candidate.Symbol)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static symbol => symbol, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var gapSize = group.Sum(static candidate => Math.Max(candidate.GapSize, 1));

            await EnqueueRemediationAsync(
                symbols,
                group.Key.From,
                group.Key.To,
                group.Key.Provider,
                group.Key.Source,
                BuildBatchedReason(group.Select(static candidate => candidate.Reason)),
                gapSize,
                severity: null,
                downstreamWorkflow: null,
                ct: ct).ConfigureAwait(false);
        }
    }

    public Task HandleQualityAlertAsync(QualityAlertRemediationSignal signal, CancellationToken ct = default)
    {
        return EnqueueRemediationAsync(
            [signal.Symbol],
            signal.From,
            signal.To,
            signal.Provider ?? _policy.DefaultProvider,
            AutoRemediationTriggerSource.QualityAlert,
            $"alert:{signal.AlertId}:{signal.Reason}",
            Math.Max(signal.GapSize, 1),
            signal.Severity,
            signal.DownstreamWorkflow,
            ct);
    }

    public BackfillRemediationSlaSnapshot EvaluateRemediationSla(
        DateTimeOffset? nowUtc = null,
        int maxExecutions = 100,
        TimeSpan? dueSoonWindow = null)
    {
        var evaluatedAt = (nowUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();

        if (maxExecutions <= 0)
        {
            return new BackfillRemediationSlaSnapshot(
                evaluatedAt,
                Total: 0,
                OverdueCount: 0,
                DueSoonCount: 0,
                RequiresOwnerAssignmentCount: 0,
                Items: Array.Empty<BackfillRemediationSlaStatusItem>());
        }

        var dueSoonThreshold = dueSoonWindow ?? TimeSpan.FromHours(1);
        if (dueSoonThreshold < TimeSpan.Zero)
        {
            dueSoonThreshold = TimeSpan.Zero;
        }

        var items = _history.GetRecentExecutions(maxExecutions)
            .Where(static execution => execution.Trigger == ExecutionTrigger.AutoRemediation)
            .Select(execution => TryBuildSlaStatusItem(execution, evaluatedAt, dueSoonThreshold))
            .Where(static item => item is not null)
            .Select(static item => item!)
            .OrderBy(static item => SlaStatusPriority(item.Status))
            .ThenBy(static item => item.DueAtUtc)
            .ThenBy(static item => item.Provider, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => string.Join(",", item.Symbols), StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new BackfillRemediationSlaSnapshot(
            evaluatedAt,
            items.Count,
            items.Count(static item => item.Status == BackfillRemediationSlaStatus.Overdue),
            items.Count(static item => item.Status == BackfillRemediationSlaStatus.DueSoon),
            items.Count(static item =>
                item.RequiresOwnerAssignment && item.Status != BackfillRemediationSlaStatus.Completed),
            items);
    }

    private void OnQualityGapDetected(QualityDataGap gap)
    {
        _ = HandleDataQualityGapAsync(gap);
    }

    private async Task EnqueueRemediationAsync(
        IReadOnlyList<string> symbols,
        DateOnly from,
        DateOnly to,
        string provider,
        AutoRemediationTriggerSource source,
        string reason,
        int gapSize,
        string? severity,
        string? downstreamWorkflow,
        CancellationToken ct)
    {
        if (gapSize < _policy.MinimumGapSize)
        {
            return;
        }

        var normalizedSymbols = symbols
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(static symbol => symbol.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedSymbols.Length == 0)
        {
            return;
        }

        var normalizedProvider = NormalizeProvider(provider);
        var now = DateTimeOffset.UtcNow;
        var slaDecision = _slaPolicy.Classify(source, severity, downstreamWorkflow, now);

        if (IsCoolingDown(_providerCooldown, normalizedProvider, _policy.ProviderCooldown, now))
        {
            _log.Debug("Auto-remediation provider cooldown active for {Provider}", normalizedProvider);
            return;
        }

        var eligibleSymbols = normalizedSymbols
            .Where(symbol => !IsCoolingDown(_symbolCooldown, symbol, _policy.SymbolCooldown, now))
            .ToArray();

        if (eligibleSymbols.Length == 0)
        {
            _log.Debug("Auto-remediation symbol cooldown active for {Provider}", provider);
            return;
        }

        var idempotencyKey = BuildIdempotencyKey(eligibleSymbols, normalizedProvider, from, to);

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
            var execution = CreateExecutionLog(
                eligibleSymbols,
                normalizedProvider,
                from,
                to,
                source,
                reason,
                idempotencyKey,
                state.Attempts,
                slaDecision);
            _history.AddExecution(execution);

            try
            {
                var request = new BackfillRequest(normalizedProvider, eligibleSymbols, from, to);
                var result = await _backfillGateway.RunAsync(request, ct).ConfigureAwait(false);

                execution.CompletedAt = DateTimeOffset.UtcNow;
                execution.Status = result.Success ? ExecutionStatus.Completed : ExecutionStatus.Failed;
                execution.ErrorMessage = result.Error;
                execution.Statistics.TotalBarsRetrieved = result.BarsWritten;
                execution.Statistics.TotalSymbols = eligibleSymbols.Length;
                execution.Statistics.SuccessfulSymbols = result.Success ? eligibleSymbols.Length : 0;
                execution.Statistics.FailedSymbols = result.Success ? 0 : eligibleSymbols.Length;
                execution.AutoRemediationLastOutcome = result.Success
                    ? AutoRemediationOutcome.Completed.ToString()
                    : AutoRemediationOutcome.FailedPermanent.ToString();

                UpdateCooldowns(eligibleSymbols, normalizedProvider, now);
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

    private string NormalizeProvider(string? provider)
    {
        var normalized = provider?.Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized.ToLowerInvariant();
        }

        normalized = _policy.DefaultProvider.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? "unknown"
            : normalized.ToLowerInvariant();
    }

    private static string BuildIdempotencyKey(IReadOnlyList<string> symbols, string provider, DateOnly from, DateOnly to)
        => $"{string.Join(",", symbols)}|{provider.ToLowerInvariant()}|{from:yyyy-MM-dd}|{to:yyyy-MM-dd}";

    private static string BuildBatchedReason(IEnumerable<string> reasons)
    {
        var distinctReasons = reasons
            .Where(static reason => !string.IsNullOrWhiteSpace(reason))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static reason => reason, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return distinctReasons.Length switch
        {
            0 => "batch",
            1 => distinctReasons[0],
            _ => string.Join("|", distinctReasons)
        };
    }

    private static bool IsCoolingDown(ConcurrentDictionary<string, DateTimeOffset> state, string key, TimeSpan cooldown, DateTimeOffset now)
        => state.TryGetValue(key, out var lastAttempt) && (now - lastAttempt) < cooldown;

    private static bool IsTransientFailure(Exception ex)
        => ex is HttpRequestException or TimeoutException or OperationCanceledException;

    private static BackfillRemediationSlaStatusItem? TryBuildSlaStatusItem(
        BackfillExecutionLog execution,
        DateTimeOffset evaluatedAt,
        TimeSpan dueSoonThreshold)
    {
        var metadata = ParseWarningMetadata(execution.Warnings);
        if (!metadata.TryGetValue("sla-due-utc", out var dueText) ||
            !DateTimeOffset.TryParse(
                dueText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dueAt))
        {
            return null;
        }

        if (!metadata.TryGetValue("sla-tier", out var tierText) ||
            !Enum.TryParse<BackfillRemediationSlaTier>(tierText, ignoreCase: true, out var tier))
        {
            tier = BackfillRemediationSlaTier.Standard;
        }

        var provider = metadata.TryGetValue("provider", out var providerText)
            ? providerText
            : string.Empty;
        var downstreamWorkflow = metadata.TryGetValue("downstream-workflow", out var workflowText)
            ? workflowText
            : "unassigned";
        var reasonCode = metadata.TryGetValue("sla-reason", out var reasonText)
            ? reasonText
            : "Unknown";
        var requiresOwnerAssignment = metadata.TryGetValue("sla-requires-owner", out var requiresOwnerText) &&
            bool.TryParse(requiresOwnerText, out var requiresOwner) &&
            requiresOwner;
        var idempotencyKey = execution.AutoRemediationIdempotencyKey ??
            BuildIdempotencyKey(execution.Symbols, provider, execution.FromDate, execution.ToDate);
        var status = ResolveSlaStatus(execution, dueAt, evaluatedAt, dueSoonThreshold);

        return new BackfillRemediationSlaStatusItem(
            execution.ExecutionId,
            idempotencyKey,
            tier,
            status,
            dueAt,
            requiresOwnerAssignment,
            downstreamWorkflow,
            reasonCode,
            provider,
            execution.Symbols.ToArray(),
            execution.FromDate,
            execution.ToDate,
            execution.Status,
            execution.AutoRemediationLastOutcome,
            dueAt - evaluatedAt);
    }

    private static Dictionary<string, string> ParseWarningMetadata(IEnumerable<string> warnings)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var warning in warnings)
        {
            var separatorIndex = warning.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex == warning.Length - 1)
            {
                continue;
            }

            var key = warning[..separatorIndex].Trim();
            var value = warning[(separatorIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                metadata[key] = value;
            }
        }

        return metadata;
    }

    private static BackfillRemediationSlaStatus ResolveSlaStatus(
        BackfillExecutionLog execution,
        DateTimeOffset dueAt,
        DateTimeOffset evaluatedAt,
        TimeSpan dueSoonThreshold)
    {
        if (execution.Status == ExecutionStatus.Completed ||
            string.Equals(
                execution.AutoRemediationLastOutcome,
                AutoRemediationOutcome.Completed.ToString(),
                StringComparison.OrdinalIgnoreCase))
        {
            return BackfillRemediationSlaStatus.Completed;
        }

        if (evaluatedAt >= dueAt)
        {
            return BackfillRemediationSlaStatus.Overdue;
        }

        if (execution.Status is ExecutionStatus.Failed or ExecutionStatus.Cancelled or ExecutionStatus.Skipped)
        {
            return BackfillRemediationSlaStatus.Failed;
        }

        return dueAt - evaluatedAt <= dueSoonThreshold
            ? BackfillRemediationSlaStatus.DueSoon
            : BackfillRemediationSlaStatus.Open;
    }

    private static int SlaStatusPriority(BackfillRemediationSlaStatus status)
        => status switch
        {
            BackfillRemediationSlaStatus.Overdue => 0,
            BackfillRemediationSlaStatus.Failed => 1,
            BackfillRemediationSlaStatus.DueSoon => 2,
            BackfillRemediationSlaStatus.Open => 3,
            BackfillRemediationSlaStatus.Completed => 4,
            _ => 5
        };

    private static void UpdateOutcome(AutoRemediationState state, AutoRemediationOutcome outcome)
    {
        lock (state)
        {
            state.LastOutcome = outcome;
        }
    }

    private void UpdateCooldowns(IReadOnlyList<string> symbols, string provider, DateTimeOffset timestamp)
    {
        foreach (var symbol in symbols)
        {
            _symbolCooldown[symbol] = timestamp;
        }

        _providerCooldown[provider] = timestamp;
    }

    private static BackfillExecutionLog CreateExecutionLog(
        IReadOnlyList<string> symbols,
        string provider,
        DateOnly from,
        DateOnly to,
        AutoRemediationTriggerSource source,
        string reason,
        string idempotencyKey,
        int attempt,
        BackfillRemediationSlaDecision slaDecision)
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
            Symbols = symbols.ToList(),
            Status = ExecutionStatus.Running,
            AutoRemediationTriggerReason = reason,
            AutoRemediationAttemptCount = attempt,
            AutoRemediationLastOutcome = AutoRemediationOutcome.None.ToString(),
            AutoRemediationIdempotencyKey = idempotencyKey,
            Warnings =
            {
                $"source={source}",
                $"provider={provider}",
                $"sla-tier={slaDecision.Tier}",
                $"sla-due-utc={slaDecision.DueAtUtc:O}",
                $"sla-requires-owner={slaDecision.RequiresOwnerAssignment.ToString().ToLowerInvariant()}",
                $"downstream-workflow={slaDecision.DownstreamWorkflow}",
                $"sla-reason={slaDecision.ReasonCode}"
            }
        };
    }

    private sealed record AutoGapRemediationCandidate(
        string Symbol,
        DateOnly From,
        DateOnly To,
        string Provider,
        AutoRemediationTriggerSource Source,
        string Reason,
        int GapSize);

    private sealed record AutoRemediationBatchKey(
        string Provider,
        DateOnly From,
        DateOnly To,
        AutoRemediationTriggerSource Source);
}
