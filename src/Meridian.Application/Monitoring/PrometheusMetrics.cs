using System.Collections.Concurrent;
using System.Threading;
using Meridian.Application.Canonicalization;
using Meridian.Application.Subscriptions.Models;
using Prometheus;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Prometheus metrics exporter for Meridian.
/// Implements best practices from prometheus-net documentation and
/// production patterns from Grafana dashboard templates.
///
/// Naming conventions follow Prometheus best practices:
/// - snake_case for metric names
/// - _total suffix for counters
/// - Descriptive help text
/// - Appropriate metric types (Counter, Gauge, Histogram)
///
/// Cardinality guards: Symbol-labeled metrics are gated by a configurable
/// allowlist. Symbols not in the allowlist are aggregated under "__other__".
/// This prevents metric explosion with dynamic/large symbol universes.
/// </summary>
public static class PrometheusMetrics
{
    /// <summary>
    /// Maximum number of symbols allowed as metric labels before aggregation kicks in.
    /// </summary>
    private static int _maxSymbolLabels = 100;

    /// <summary>
    /// Tracks which symbols have been admitted to labeled metrics.
    /// Once the cap is reached, new symbols go to "__other__".
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte> _admittedSymbols = new(StringComparer.OrdinalIgnoreCase);

    private const string OtherSymbolLabel = "__other__";

    /// <summary>
    /// Configures the maximum number of distinct symbol labels allowed in metrics.
    /// Symbols beyond this limit are aggregated under "__other__".
    /// </summary>
    public static void SetMaxSymbolLabels(int max)
    {
        _maxSymbolLabels = Math.Max(1, max);
    }

    /// <summary>
    /// Pre-admits specific symbols to the metrics label set.
    /// Call during startup for known monitored symbols.
    /// </summary>
    public static void AdmitSymbol(string symbol)
    {
        if (_admittedSymbols.Count < _maxSymbolLabels)
        {
            _admittedSymbols.TryAdd(symbol, 0);
        }
    }

    /// <summary>
    /// Returns the symbol label to use for metrics. If the symbol is admitted,
    /// returns it directly. Otherwise returns "__other__" to prevent cardinality explosion.
    /// </summary>
    private static string GetSymbolLabel(string symbol)
    {
        if (_admittedSymbols.ContainsKey(symbol))
            return symbol;

        // Try to admit if under cap
        if (_admittedSymbols.Count < _maxSymbolLabels)
        {
            _admittedSymbols.TryAdd(symbol, 0);
            return symbol;
        }

        return OtherSymbolLabel;
    }

    // Event counters
    private static readonly Counter PublishedEvents = Prometheus.Metrics.CreateCounter(
        "mdc_events_published_total",
        "Total number of market events published to the event pipeline");

    private static readonly Counter DroppedEvents = Prometheus.Metrics.CreateCounter(
        "mdc_events_dropped_total",
        "Total number of events dropped due to backpressure or pipeline capacity");

    private static readonly Counter IntegrityEvents = Prometheus.Metrics.CreateCounter(
        "mdc_integrity_events_total",
        "Total number of data integrity validation events (gaps, out-of-order, etc.)");

    private static readonly Counter TradeEvents = Prometheus.Metrics.CreateCounter(
        "mdc_trade_events_total",
        "Total number of trade events processed");

    private static readonly Counter DepthUpdateEvents = Prometheus.Metrics.CreateCounter(
        "mdc_depth_update_events_total",
        "Total number of market depth update events processed");

    private static readonly Counter QuoteEvents = Prometheus.Metrics.CreateCounter(
        "mdc_quote_events_total",
        "Total number of quote events processed");

    private static readonly Counter HistoricalBarEvents = Prometheus.Metrics.CreateCounter(
        "mdc_historical_bar_events_total",
        "Total number of historical bar events processed");

    // Gauges for current state
    private static readonly Gauge EventsPerSecond = Prometheus.Metrics.CreateGauge(
        "mdc_events_per_second",
        "Current rate of events published per second");

    private static readonly Gauge TradesPerSecond = Prometheus.Metrics.CreateGauge(
        "mdc_trades_per_second",
        "Current rate of trades processed per second");

    private static readonly Gauge DepthUpdatesPerSecond = Prometheus.Metrics.CreateGauge(
        "mdc_depth_updates_per_second",
        "Current rate of depth updates processed per second");

    private static readonly Gauge HistoricalBarsPerSecond = Prometheus.Metrics.CreateGauge(
        "mdc_historical_bars_per_second",
        "Current rate of historical bars processed per second");

    private static readonly Gauge DropRatePercent = Prometheus.Metrics.CreateGauge(
        "mdc_drop_rate_percent",
        "Percentage of events dropped (0-100)");

    // Latency metrics (using Histogram for percentile calculations)
    private static readonly Histogram ProcessingLatency = Prometheus.Metrics.CreateHistogram(
        "mdc_processing_latency_microseconds",
        "Event processing latency in microseconds",
        new HistogramConfiguration
        {
            // Buckets optimized for microsecond-level latency (1µs to 10ms)
            Buckets = new double[] { 1, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000 }
        });

    private static readonly Gauge AverageLatencyUs = Prometheus.Metrics.CreateGauge(
        "mdc_average_latency_microseconds",
        "Average event processing latency in microseconds");

    private static readonly Gauge MinLatencyUs = Prometheus.Metrics.CreateGauge(
        "mdc_min_latency_microseconds",
        "Minimum event processing latency in microseconds");

    private static readonly Gauge MaxLatencyUs = Prometheus.Metrics.CreateGauge(
        "mdc_max_latency_microseconds",
        "Maximum event processing latency in microseconds");

    // GC and memory metrics
    private static readonly Counter Gc0Collections = Prometheus.Metrics.CreateCounter(
        "mdc_gc_gen0_collections_total",
        "Total number of Generation 0 garbage collections");

    private static readonly Counter Gc1Collections = Prometheus.Metrics.CreateCounter(
        "mdc_gc_gen1_collections_total",
        "Total number of Generation 1 garbage collections");

    private static readonly Counter Gc2Collections = Prometheus.Metrics.CreateCounter(
        "mdc_gc_gen2_collections_total",
        "Total number of Generation 2 garbage collections");

    private static readonly Gauge MemoryUsageMb = Prometheus.Metrics.CreateGauge(
        "mdc_memory_usage_megabytes",
        "Current memory usage in megabytes");

    private static readonly Gauge HeapSizeMb = Prometheus.Metrics.CreateGauge(
        "mdc_heap_size_megabytes",
        "Current GC heap size in megabytes");

    // Resubscription / Reconnection metrics
    private static readonly Counter ResubscribeAttempts = Prometheus.Metrics.CreateCounter(
        "mdc_resubscribe_attempts_total",
        "Total number of auto-resubscription attempts triggered by integrity events");

    private static readonly Counter ResubscribeSuccesses = Prometheus.Metrics.CreateCounter(
        "mdc_resubscribe_successes_total",
        "Total number of successful auto-resubscription attempts");

    private static readonly Counter ResubscribeFailures = Prometheus.Metrics.CreateCounter(
        "mdc_resubscribe_failures_total",
        "Total number of failed auto-resubscription attempts");

    private static readonly Counter RateLimitedSkips = Prometheus.Metrics.CreateCounter(
        "mdc_resubscribe_rate_limited_total",
        "Total number of resubscription attempts skipped due to rate limiting or circuit breaker");

    private static readonly Counter CircuitBreakerOpens = Prometheus.Metrics.CreateCounter(
        "mdc_circuit_breaker_opens_total",
        "Total number of times the circuit breaker has opened");

    private static readonly Counter CircuitBreakerCloses = Prometheus.Metrics.CreateCounter(
        "mdc_circuit_breaker_closes_total",
        "Total number of times the circuit breaker has closed");

    private static readonly Counter CircuitBreakerHalfOpens = Prometheus.Metrics.CreateCounter(
        "mdc_circuit_breaker_half_opens_total",
        "Total number of times the circuit breaker has transitioned to half-open");

    private static readonly Gauge CircuitBreakerState = Prometheus.Metrics.CreateGauge(
        "mdc_circuit_breaker_state",
        "Current circuit breaker state (0=Closed, 1=Open, 2=HalfOpen)");

    private static readonly Gauge SymbolsInCooldown = Prometheus.Metrics.CreateGauge(
        "mdc_symbols_in_cooldown",
        "Number of symbols currently in resubscription cooldown");

    private static readonly Gauge SymbolsCircuitOpen = Prometheus.Metrics.CreateGauge(
        "mdc_symbols_circuit_open",
        "Number of symbols with open per-symbol circuit breakers");

    private static readonly Gauge ResubscribeSuccessRate = Prometheus.Metrics.CreateGauge(
        "mdc_resubscribe_success_rate_percent",
        "Resubscription success rate as a percentage (0-100)");

    private static readonly Gauge AverageResubscribeTimeMs = Prometheus.Metrics.CreateGauge(
        "mdc_resubscribe_avg_time_milliseconds",
        "Average time taken for successful resubscriptions in milliseconds");

    // SLA metrics (ADQ-4.5)
    private static readonly Counter SlaViolationsTotal = Prometheus.Metrics.CreateCounter(
        "mdc_sla_violations_total",
        "Total number of data freshness SLA violations");

    private static readonly Gauge SlaCurrentViolations = Prometheus.Metrics.CreateGauge(
        "mdc_sla_current_violations",
        "Current number of active SLA violations");

    private static readonly Gauge SlaFreshnessScore = Prometheus.Metrics.CreateGauge(
        "mdc_sla_freshness_score",
        "Overall data freshness score (0-100)");

    private static readonly Gauge SlaHealthySymbols = Prometheus.Metrics.CreateGauge(
        "mdc_sla_healthy_symbols",
        "Number of symbols within SLA thresholds");

    private static readonly Gauge SlaViolationSymbols = Prometheus.Metrics.CreateGauge(
        "mdc_sla_violation_symbols",
        "Number of symbols currently in SLA violation");

    private static readonly Histogram SlaFreshnessMs = Prometheus.Metrics.CreateHistogram(
        "mdc_sla_freshness_milliseconds",
        "Distribution of data freshness (time since last event) in milliseconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "symbol" },
            Buckets = new double[] { 100, 500, 1000, 5000, 10000, 30000, 60000, 120000, 300000 }
        });

    // WAL recovery metrics
    private static readonly Counter WalRecoveryEventsTotal = Prometheus.Metrics.CreateCounter(
        "mdc_wal_recovery_events_total",
        "Total number of events recovered from WAL on startup");

    private static readonly Gauge WalRecoveryDurationSeconds = Prometheus.Metrics.CreateGauge(
        "mdc_wal_recovery_duration_seconds",
        "Duration of WAL recovery on startup in seconds");

    // Provider reconnection metrics (labeled by provider and outcome)
    private static readonly Counter ProviderReconnectionAttemptsTotal = Prometheus.Metrics.CreateCounter(
        "mdc_provider_reconnection_attempts_total",
        "Total reconnection attempts per provider and outcome",
        new CounterConfiguration { LabelNames = new[] { "provider", "outcome" } });

    // Migration diagnostics counters (Phase 0 — temporary observability for migration)
    private static readonly Counter MigrationStreamingFactoryHits = Prometheus.Metrics.CreateCounter(
        "mdc_migration_streaming_factory_hits_total",
        "Total streaming client factory invocations (migration diagnostics)");

    private static readonly Counter MigrationBackfillFactoryHits = Prometheus.Metrics.CreateCounter(
        "mdc_migration_backfill_factory_hits_total",
        "Total backfill provider creation calls (migration diagnostics)");

    private static readonly Counter MigrationReconnectAttempts = Prometheus.Metrics.CreateCounter(
        "mdc_migration_reconnect_attempts_total",
        "Total reconnect attempts across all providers (migration diagnostics)");

    private static readonly Counter MigrationReconnectSuccesses = Prometheus.Metrics.CreateCounter(
        "mdc_migration_reconnect_successes_total",
        "Total successful reconnections (migration diagnostics)");

    private static readonly Counter MigrationReconnectFailures = Prometheus.Metrics.CreateCounter(
        "mdc_migration_reconnect_failures_total",
        "Total failed reconnections (migration diagnostics)");

    private static readonly Gauge MigrationProvidersRegistered = Prometheus.Metrics.CreateGauge(
        "mdc_migration_providers_registered",
        "Total providers registered in the registry (migration diagnostics)");

    private static readonly Gauge MigrationStreamingFactoriesRegistered = Prometheus.Metrics.CreateGauge(
        "mdc_migration_streaming_factories_registered",
        "Total streaming factories registered (migration diagnostics)");

    // Canonicalization metrics (Phase 2)
    private static readonly Counter CanonicalizationEventsTotal = Prometheus.Metrics.CreateCounter(
        "mdc_canonicalization_events_total",
        "Total number of events processed through canonicalization");

    private static readonly Counter CanonicalizationSkippedTotal = Prometheus.Metrics.CreateCounter(
        "mdc_canonicalization_skipped_total",
        "Total number of events skipped by canonicalization (non-pilot or heartbeat)");

    private static readonly Counter CanonicalizationUnresolvedTotal = Prometheus.Metrics.CreateCounter(
        "mdc_canonicalization_unresolved_total",
        "Total number of events with unresolved canonical symbols");

    private static readonly Counter CanonicalizationDualWritesTotal = Prometheus.Metrics.CreateCounter(
        "mdc_canonicalization_dual_writes_total",
        "Total number of events dual-written (raw + canonical) during Phase 2 validation");

    private static readonly Counter CanonicalizationQuarantinedTotal = Prometheus.Metrics.CreateCounter(
        "mdc_canonicalization_quarantined_total",
        "Total number of unresolved-symbol events written to the quarantine sink");

    private static readonly Histogram CanonicalizationDurationSeconds = Prometheus.Metrics.CreateHistogram(
        "mdc_canonicalization_duration_seconds",
        "Canonicalization processing time per event",
        new HistogramConfiguration
        {
            // Microsecond-level buckets (in seconds): 1µs to 1ms
            Buckets = new double[] { 0.000001, 0.000005, 0.00001, 0.000025, 0.00005, 0.0001, 0.00025, 0.0005, 0.001 }
        });

    private static readonly Gauge CanonicalizationVersionActive = Prometheus.Metrics.CreateGauge(
        "mdc_canonicalization_version_active",
        "Currently active canonicalization mapping version");

    // Validation pipeline metrics
    private static readonly Counter ValidationRejectedTotal = Prometheus.Metrics.CreateCounter(
        "mdc_validation_rejected_total",
        "Total number of events rejected by the F# validation stage, labelled by error type",
        new CounterConfiguration { LabelNames = new[] { "error_type" } });

    private static readonly Gauge ValidationPassRatePercent = Prometheus.Metrics.CreateGauge(
        "mdc_validation_pass_rate_percent",
        "Percentage of validated events that passed (0–100). 100 when validation is disabled or no events seen.");

    // Symbol-level metrics (with labels)
    private static readonly Counter TradesBySymbol = Prometheus.Metrics.CreateCounter(
        "mdc_trades_by_symbol_total",
        "Total number of trades per symbol",
        new CounterConfiguration
        {
            LabelNames = new[] { "symbol", "venue" }
        });

    private static readonly Gauge LastTradePrice = Prometheus.Metrics.CreateGauge(
        "mdc_last_trade_price",
        "Last trade price per symbol",
        new GaugeConfiguration
        {
            LabelNames = new[] { "symbol" }
        });

    private static readonly Histogram TradeSizeDistribution = Prometheus.Metrics.CreateHistogram(
        "mdc_trade_size",
        "Distribution of trade sizes",
        new HistogramConfiguration
        {
            LabelNames = new[] { "symbol" },
            Buckets = new double[] { 1, 10, 50, 100, 500, 1000, 5000, 10000, 50000 }
        });

    // Kernel quality and trustworthiness metrics
    private static readonly Histogram KernelExecutionLatencyMs = Prometheus.Metrics.CreateHistogram(
        "mdc_kernel_execution_latency_milliseconds",
        "Kernel execution latency in milliseconds by domain",
        new HistogramConfiguration
        {
            LabelNames = new[] { "domain" },
            Buckets = new double[] { 0.1, 0.5, 1, 2, 5, 10, 25, 50, 100, 250, 500, 1000 }
        });

    private static readonly Counter KernelExecutionsTotal = Prometheus.Metrics.CreateCounter(
        "mdc_kernel_executions_total",
        "Total kernel executions by domain",
        new CounterConfiguration { LabelNames = new[] { "domain" } });

    private static readonly Gauge KernelThroughputPerMinute = Prometheus.Metrics.CreateGauge(
        "mdc_kernel_throughput_per_minute",
        "Observed kernel throughput per minute by domain",
        new GaugeConfiguration { LabelNames = new[] { "domain" } });

    private static readonly Gauge KernelLatencyPercentileMs = Prometheus.Metrics.CreateGauge(
        "mdc_kernel_latency_percentile_milliseconds",
        "Kernel latency percentile in milliseconds by domain and percentile",
        new GaugeConfiguration { LabelNames = new[] { "domain", "percentile" } });

    private static readonly Counter KernelDeterminismChecksTotal = Prometheus.Metrics.CreateCounter(
        "mdc_kernel_determinism_checks_total",
        "Total kernel determinism checks by domain and outcome",
        new CounterConfiguration { LabelNames = new[] { "domain", "outcome" } });

    private static readonly Gauge KernelReasonCoveragePercent = Prometheus.Metrics.CreateGauge(
        "mdc_kernel_reason_code_coverage_percent",
        "Percentage of kernel outputs with structured reason codes by domain",
        new GaugeConfiguration { LabelNames = new[] { "domain" } });

    private static readonly Gauge KernelDriftScore = Prometheus.Metrics.CreateGauge(
        "mdc_kernel_drift_score",
        "Distribution-shift drift score by domain and metric",
        new GaugeConfiguration { LabelNames = new[] { "domain", "metric" } });

    private static readonly Gauge KernelCriticalSeverityRate = Prometheus.Metrics.CreateGauge(
        "mdc_kernel_critical_severity_rate",
        "Current critical-severity rate (0-1) by domain",
        new GaugeConfiguration { LabelNames = new[] { "domain" } });

    private static readonly Gauge KernelCriticalSeverityJumpActive = Prometheus.Metrics.CreateGauge(
        "mdc_kernel_critical_severity_jump_active",
        "Whether a critical-severity jump alert is currently active for a kernel domain (0 or 1)",
        new GaugeConfiguration { LabelNames = new[] { "domain" } });

    private static readonly Counter KernelCriticalSeverityJumpAlertsTotal = Prometheus.Metrics.CreateCounter(
        "mdc_kernel_critical_severity_jump_alerts_total",
        "Total alerts raised for sudden jumps in kernel critical-severity rate by domain",
        new CounterConfiguration { LabelNames = new[] { "domain" } });

    /// <summary>
    /// Updates all Prometheus metrics from the current Metrics snapshot.
    /// Should be called periodically (e.g., every 1-5 seconds) to keep metrics current.
    /// </summary>
    public static void UpdateFromSnapshot()
    {
        var combined = Metrics.GetCombinedSnapshot();
        var snapshot = combined.Core;
        var resubSnapshot = combined.Resubscription;

        // Update counters (Prometheus counters only increase, so we set to current value)
        PublishedEvents.IncTo(snapshot.Published);
        DroppedEvents.IncTo(snapshot.Dropped);
        IntegrityEvents.IncTo(snapshot.Integrity);
        TradeEvents.IncTo(snapshot.Trades);
        DepthUpdateEvents.IncTo(snapshot.DepthUpdates);
        QuoteEvents.IncTo(snapshot.Quotes);
        HistoricalBarEvents.IncTo(snapshot.HistoricalBars);

        // Update rate gauges
        EventsPerSecond.Set(snapshot.EventsPerSecond);
        TradesPerSecond.Set(snapshot.TradesPerSecond);
        DepthUpdatesPerSecond.Set(snapshot.DepthUpdatesPerSecond);
        HistoricalBarsPerSecond.Set(snapshot.HistoricalBarsPerSecond);
        DropRatePercent.Set(snapshot.DropRate);

        // Update latency gauges
        AverageLatencyUs.Set(snapshot.AverageLatencyUs);
        MinLatencyUs.Set(snapshot.MinLatencyUs);
        MaxLatencyUs.Set(snapshot.MaxLatencyUs);

        // Update GC counters
        Gc0Collections.IncTo(snapshot.Gc0Collections);
        Gc1Collections.IncTo(snapshot.Gc1Collections);
        Gc2Collections.IncTo(snapshot.Gc2Collections);

        // Update memory gauges
        MemoryUsageMb.Set(snapshot.MemoryUsageMb);
        HeapSizeMb.Set(snapshot.HeapSizeMb);

        // Update resubscription / reconnection metrics
        ResubscribeAttempts.IncTo(resubSnapshot.ResubscribeAttempts);
        ResubscribeSuccesses.IncTo(resubSnapshot.ResubscribeSuccesses);
        ResubscribeFailures.IncTo(resubSnapshot.ResubscribeFailures);
        RateLimitedSkips.IncTo(resubSnapshot.RateLimitedSkips);
        CircuitBreakerOpens.IncTo(resubSnapshot.CircuitBreakerOpens);
        CircuitBreakerCloses.IncTo(resubSnapshot.CircuitBreakerCloses);
        CircuitBreakerHalfOpens.IncTo(resubSnapshot.CircuitBreakerHalfOpens);
        CircuitBreakerState.Set((int)resubSnapshot.CurrentCircuitState);
        SymbolsInCooldown.Set(resubSnapshot.SymbolsInCooldown);
        SymbolsCircuitOpen.Set(resubSnapshot.SymbolsCircuitOpen);
        ResubscribeSuccessRate.Set(resubSnapshot.SuccessRate);
        AverageResubscribeTimeMs.Set(resubSnapshot.AverageResubscribeTimeMs);

        // Update migration diagnostics counters (Phase 0 observability)
        var migrationSnapshot = MigrationDiagnostics.GetSnapshot();
        MigrationStreamingFactoryHits.IncTo(migrationSnapshot.StreamingFactoryHits);
        MigrationBackfillFactoryHits.IncTo(migrationSnapshot.BackfillFactoryHits);
        MigrationReconnectAttempts.IncTo(migrationSnapshot.ReconnectAttempts);
        MigrationReconnectSuccesses.IncTo(migrationSnapshot.ReconnectSuccesses);
        MigrationReconnectFailures.IncTo(migrationSnapshot.ReconnectFailures);
        MigrationProvidersRegistered.Set(migrationSnapshot.ProvidersRegistered);
        MigrationStreamingFactoriesRegistered.Set(migrationSnapshot.StreamingFactoriesRegistered);

        // Update canonicalization metrics
        var canonSnapshot = Canonicalization.CanonicalizationMetrics.GetSnapshot();
        CanonicalizationVersionActive.Set(canonSnapshot.ActiveVersion);

        // Update validation pipeline metrics
        UpdateValidationMetrics(ValidationMetrics.GetSnapshot());
    }

    /// <summary>
    /// Updates Prometheus validation metrics from a <see cref="ValidationMetricsSnapshot"/>.
    /// </summary>
    public static void UpdateValidationMetrics(ValidationMetricsSnapshot snapshot)
    {
        ValidationPassRatePercent.Set(snapshot.PassRatePercent);

        foreach (var kvp in snapshot.RejectedByErrorType)
        {
            ValidationRejectedTotal.WithLabels(kvp.Key).IncTo(kvp.Value);
        }
    }

    /// <summary>
    /// Records a trade event with symbol and venue labels.
    /// Uses cardinality guard to prevent label explosion with many symbols.
    /// </summary>
    public static void RecordTrade(string symbol, string venue, decimal price, int size)
    {
        var safeSymbol = GetSymbolLabel(symbol);
        TradesBySymbol.WithLabels(safeSymbol, venue).Inc();
        LastTradePrice.WithLabels(safeSymbol).Set((double)price);
        TradeSizeDistribution.WithLabels(safeSymbol).Observe(size);
    }

    /// <summary>
    /// Records event processing latency in microseconds.
    /// </summary>
    public static void RecordProcessingLatency(double latencyMicroseconds)
    {
        ProcessingLatency.Observe(latencyMicroseconds);
    }

    /// <summary>
    /// Records kernel execution latency and throughput for a specific domain.
    /// </summary>
    public static void RecordKernelExecution(string domain, double latencyMilliseconds)
    {
        var safeDomain = string.IsNullOrWhiteSpace(domain) ? "unknown" : domain.Trim().ToLowerInvariant();
        KernelExecutionsTotal.WithLabels(safeDomain).Inc();
        KernelExecutionLatencyMs.WithLabels(safeDomain).Observe(Math.Max(0, latencyMilliseconds));
    }

    /// <summary>
    /// Sets kernel throughput per minute for a domain.
    /// </summary>
    public static void SetKernelThroughputPerMinute(string domain, double throughputPerMinute)
    {
        var safeDomain = string.IsNullOrWhiteSpace(domain) ? "unknown" : domain.Trim().ToLowerInvariant();
        KernelThroughputPerMinute.WithLabels(safeDomain).Set(Math.Max(0, throughputPerMinute));
    }

    /// <summary>
    /// Sets kernel latency percentile in milliseconds for a domain.
    /// </summary>
    public static void SetKernelLatencyPercentile(string domain, string percentile, double latencyMilliseconds)
    {
        var safeDomain = string.IsNullOrWhiteSpace(domain) ? "unknown" : domain.Trim().ToLowerInvariant();
        var safePercentile = string.IsNullOrWhiteSpace(percentile) ? "unknown" : percentile.Trim().ToLowerInvariant();
        KernelLatencyPercentileMs.WithLabels(safeDomain, safePercentile).Set(Math.Max(0, latencyMilliseconds));
    }

    /// <summary>
    /// Records one determinism check outcome for a kernel domain.
    /// </summary>
    public static void RecordKernelDeterminismCheck(string domain, bool isMatch)
    {
        var safeDomain = string.IsNullOrWhiteSpace(domain) ? "unknown" : domain.Trim().ToLowerInvariant();
        KernelDeterminismChecksTotal.WithLabels(safeDomain, isMatch ? "match" : "mismatch").Inc();
    }

    /// <summary>
    /// Sets kernel reason-code coverage percentage for a domain.
    /// </summary>
    public static void SetKernelReasonCoverage(string domain, double coveragePercent)
    {
        var safeDomain = string.IsNullOrWhiteSpace(domain) ? "unknown" : domain.Trim().ToLowerInvariant();
        KernelReasonCoveragePercent.WithLabels(safeDomain).Set(Math.Clamp(coveragePercent, 0, 100));
    }

    /// <summary>
    /// Sets kernel drift score for a given domain/metric pair.
    /// </summary>
    public static void SetKernelDriftScore(string domain, string metric, double driftScore)
    {
        var safeDomain = string.IsNullOrWhiteSpace(domain) ? "unknown" : domain.Trim().ToLowerInvariant();
        var safeMetric = string.IsNullOrWhiteSpace(metric) ? "unknown" : metric.Trim().ToLowerInvariant();
        KernelDriftScore.WithLabels(safeDomain, safeMetric).Set(Math.Max(0, driftScore));
    }

    /// <summary>
    /// Sets current critical severity rate for a domain and optionally records alert count.
    /// </summary>
    public static void SetKernelCriticalSeverityRate(string domain, double criticalRate, bool raiseJumpAlert)
        => SetKernelCriticalSeverityRate(domain, criticalRate, jumpAlertActive: raiseJumpAlert, raiseJumpAlert);

    /// <summary>
    /// Sets current critical severity rate for a domain, tracks whether the jump alert is active,
    /// and optionally records a newly triggered alert.
    /// </summary>
    public static void SetKernelCriticalSeverityRate(
        string domain,
        double criticalRate,
        bool jumpAlertActive,
        bool raiseJumpAlert)
    {
        var safeDomain = string.IsNullOrWhiteSpace(domain) ? "unknown" : domain.Trim().ToLowerInvariant();
        KernelCriticalSeverityRate.WithLabels(safeDomain).Set(Math.Clamp(criticalRate, 0, 1));
        KernelCriticalSeverityJumpActive.WithLabels(safeDomain).Set(jumpAlertActive ? 1 : 0);
        if (raiseJumpAlert)
        {
            KernelCriticalSeverityJumpAlertsTotal.WithLabels(safeDomain).Inc();
        }
    }

    /// <summary>
    /// Updates SLA metrics from a DataFreshnessSlaMonitor snapshot (ADQ-4.5).
    /// </summary>
    public static void UpdateSlaMetrics(DataQuality.SlaStatusSnapshot snapshot)
    {
        SlaViolationsTotal.IncTo(snapshot.TotalViolations);
        SlaCurrentViolations.Set(snapshot.ViolationSymbols);
        SlaFreshnessScore.Set(snapshot.OverallFreshnessScore);
        SlaHealthySymbols.Set(snapshot.HealthySymbols);
        SlaViolationSymbols.Set(snapshot.ViolationSymbols);

        // Record per-symbol freshness (with cardinality guard)
        foreach (var status in snapshot.SymbolStatuses)
        {
            if (status.FreshnessMs < double.MaxValue)
            {
                var safeSymbol = GetSymbolLabel(status.Symbol);
                SlaFreshnessMs.WithLabels(safeSymbol).Observe(status.FreshnessMs);
            }
        }
    }

    /// <summary>
    /// Records a provider reconnection attempt with outcome.
    /// </summary>
    /// <param name="provider">Provider name (e.g., "Alpaca", "Polygon").</param>
    /// <param name="success">Whether the reconnection attempt succeeded.</param>
    public static void RecordReconnectionAttempt(string provider, bool success)
    {
        var outcome = success ? "success" : "failure";
        ProviderReconnectionAttemptsTotal.WithLabels(provider, outcome).Inc();
    }

    /// <summary>
    /// Records WAL recovery metrics after startup recovery completes.
    /// </summary>
    public static void RecordWalRecovery(long recoveredEvents, double durationSeconds)
    {
        WalRecoveryEventsTotal.IncTo(recoveredEvents);
        WalRecoveryDurationSeconds.Set(durationSeconds);
    }

    /// <summary>
    /// Records SLA freshness for a specific symbol.
    /// </summary>
    public static void RecordSlaFreshness(string symbol, double freshnessMs)
    {
        var safeSymbol = GetSymbolLabel(symbol);
        SlaFreshnessMs.WithLabels(safeSymbol).Observe(freshnessMs);
    }

    /// <summary>
    /// Updates canonicalization metrics from a <see cref="CanonicalizationMetricsSnapshot"/>.
    /// Called periodically by the metrics updater.
    /// </summary>
    public static void UpdateCanonicalizationMetrics(
        CanonicalizationMetricsSnapshot snapshot,
        int activeVersion)
    {
        CanonicalizationEventsTotal.IncTo(snapshot.Canonicalized);
        CanonicalizationSkippedTotal.IncTo(snapshot.Skipped);
        CanonicalizationUnresolvedTotal.IncTo(snapshot.Unresolved);
        CanonicalizationDualWritesTotal.IncTo(snapshot.DualWrites);
        CanonicalizationQuarantinedTotal.IncTo(snapshot.Quarantined);
        CanonicalizationVersionActive.Set(activeVersion);

        // Record average duration as a histogram observation if available
        if (snapshot.AverageDurationUs > 0)
        {
            CanonicalizationDurationSeconds.Observe(snapshot.AverageDurationUs / 1_000_000);
        }
    }

    /// <summary>
    /// Gets the Prometheus metrics registry for HTTP export.
    /// Use this with Prometheus.MetricServer or ASP.NET Core middleware.
    /// </summary>
    public static CollectorRegistry Registry => Prometheus.Metrics.DefaultRegistry;
}

/// <summary>
/// <see cref="IReconnectionMetrics"/> implementation that delegates to
/// <see cref="PrometheusMetrics.RecordReconnectionAttempt"/>.
/// Registered as a singleton in DI by the composition root.
/// </summary>
public sealed class PrometheusReconnectionMetrics : IReconnectionMetrics
{
    public void RecordAttempt(string provider, bool success)
        => PrometheusMetrics.RecordReconnectionAttempt(provider, success);
}

/// <summary>
/// Background service that periodically updates Prometheus metrics.
/// </summary>
public sealed class PrometheusMetricsUpdater : IAsyncDisposable
{
    private readonly PeriodicTimer _timer;
    private readonly Task _updateTask;
    private readonly CancellationTokenSource _cts = new();

    public PrometheusMetricsUpdater(TimeSpan updateInterval)
    {
        _timer = new PeriodicTimer(updateInterval);
        _updateTask = UpdateLoopAsync();
    }

    private async Task UpdateLoopAsync(CancellationToken ct = default)
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(_cts.Token))
            {
                PrometheusMetrics.UpdateFromSnapshot();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _timer.Dispose();
        try
        {
            await _updateTask;
        }
        catch
        {
            // Ignore
        }
        _cts.Dispose();
    }
}
