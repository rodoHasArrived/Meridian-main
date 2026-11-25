using System.Collections.Concurrent;
using Meridian.Contracts.Workstation;
using Prometheus;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Prometheus metrics for the scheduled three-way reconciliation, served on the existing
/// <c>/metrics</c> scrape endpoint. <c>mdc_reconciliation_health</c> is the alertable signal:
/// 0 healthy, 1 degraded (blocked or overdue run), 2 unhealthy (unbooked broker fills open).
/// </summary>
public sealed class ThreeWayReconciliationMetrics(TimeProvider? timeProvider = null)
{
    public const double Healthy = 0, Degraded = 1, Unhealthy = 2;

    /// <summary>A daily run older than this degrades health; it allows one missed day plus slack.</summary>
    public static readonly TimeSpan MaximumRunAge = TimeSpan.FromHours(36);

    private static readonly Counter Runs = Metrics.CreateCounter(
        "mdc_reconciliation_runs_total", "Completed three-way reconciliation runs by result status.",
        new CounterConfiguration { LabelNames = ["status"] });

    private static readonly Counter Checks = Metrics.CreateCounter(
        "mdc_reconciliation_checks_total", "Three-way reconciliation checks by difference classification and status.",
        new CounterConfiguration { LabelNames = ["classification", "status"] });

    private static readonly Gauge OpenBreaks = Metrics.CreateGauge(
        "mdc_reconciliation_last_run_breaks", "Breaks and blocked checks in each account's latest three-way run.",
        new GaugeConfiguration { LabelNames = ["account"] });

    private static readonly Gauge LastCutoff = Metrics.CreateGauge(
        "mdc_reconciliation_last_completed_cutoff_timestamp_seconds", "Cutoff of each account's latest completed three-way run.",
        new GaugeConfiguration { LabelNames = ["account"] });

    private static readonly Gauge Health = Metrics.CreateGauge(
        "mdc_reconciliation_health", "Three-way reconciliation health: 0 healthy, 1 degraded, 2 unhealthy.",
        new GaugeConfiguration { LabelNames = ["account"] });

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly DateTimeOffset _startedAt = (timeProvider ?? TimeProvider.System).GetUtcNow();
    private readonly ConcurrentDictionary<Guid, (DateTimeOffset RecordedAt, double Severity)> _latest = new();

    /// <summary>Records a completed or blocked run and sets the account's health from its checks.</summary>
    public void RecordRun(ThreeWayReconciliationResultDto result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Runs.WithLabels(result.Status).Inc();
        foreach (var check in result.Checks)
            Checks.WithLabels(check.Classification ?? check.Measure, check.Status).Inc();
        var account = Label(result.Scope.FinancialAccountId);
        OpenBreaks.WithLabels(account).Set(result.Checks.Count(check => check.Status is "Break" or "Blocked"));
        LastCutoff.WithLabels(account).Set(result.CutoffUtc.ToUnixTimeSeconds());
        var severity = result.Checks.Any(check => check.Status == "Break"
                && check.Classification == ThreeWayFillClassification.MissingFillInLedger) ? Unhealthy
            : result.Status == "Blocked" ? Degraded : Healthy;
        _latest[result.Scope.FinancialAccountId] = (_time.GetUtcNow(), severity);
        Health.WithLabels(account).Set(severity);
    }

    /// <summary>
    /// Called on each scheduler tick for an enabled schedule. A daily run that has not completed within
    /// <see cref="MaximumRunAge"/> degrades health; a fresh process gets the same grace before reporting.
    /// </summary>
    public void ObserveSchedule(Guid accountId)
    {
        var now = _time.GetUtcNow();
        var known = _latest.TryGetValue(accountId, out var latest);
        var overdue = now - (known ? latest.RecordedAt : _startedAt) > MaximumRunAge;
        var severity = known ? latest.Severity : Healthy;
        Health.WithLabels(Label(accountId)).Set(overdue ? Math.Max(Degraded, severity) : severity);
    }

    /// <summary>The current health value for an account, for tests and diagnostics.</summary>
    public static double HealthValue(Guid accountId) => Health.WithLabels(Label(accountId)).Value;

    private static string Label(Guid accountId) => accountId.ToString("D");
}
