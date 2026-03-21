# ADR-012: Unified Monitoring and Alerting Pipeline

**Status:** Accepted
**Date:** 2026-02-02
**Deciders:** Core Team

## Context

Market data ingestion relies on continuous health checks, data quality validation, and operational alerts. Components such as providers, storage, and background services need a consistent way to:

- Report health status with severity and diagnostics
- Aggregate system-wide health in a single snapshot
- Publish alerts that can be filtered and subscribed to by operators or UI modules
- Classify alert severity and category for routing and triage

Previously, health reporting and alerting logic was scattered across services, leading to:

- Inconsistent severity classification (some components used booleans, others used enums)
- Limited visibility into system state across components
- No centralized alert history or statistics
- No subscription mechanism for UI or automation to react to alerts

## Decision

Adopt a unified monitoring pipeline with two core abstractions:

1. **Health checks** via `IHealthCheckProvider` implementations aggregated by `IHealthCheckAggregator` with parallel execution and configurable timeouts.
2. **Alert publishing** via `IAlertDispatcher`, providing centralized alert history, subscription-based routing with filters, and per-category/severity statistics.

### Severity Levels

Four levels for both health checks and alerts:

| Level | Health Checks | Alerts | Log Level |
|-------|---------------|--------|-----------|
| **Healthy / Info** | Component operating normally | Informational event | Information |
| **Degraded / Warning** | Component functional but impaired | Potential issue requiring attention | Warning |
| **Unhealthy / Error** | Component non-functional | Failure requiring action | Error |
| **Unknown / Critical** | Health check failed or timed out | System-wide or data-loss risk | Critical |

### Alert Categories

Eight categories classify alerts by source domain:

| Category | Scope |
|----------|-------|
| `Connection` | Provider connectivity, WebSocket drops, reconnection failures |
| `DataQuality` | Sequence gaps, anomalies, stale data, bad ticks |
| `Performance` | High latency, backpressure, throughput degradation |
| `SystemResources` | Memory, disk space, CPU, thread pool exhaustion |
| `Provider` | Rate limits, authentication failures, API errors |
| `Storage` | Write failures, WAL overflow, tier migration issues |
| `Configuration` | Invalid settings, missing credentials, hot-reload errors |
| `Security` | Credential expiry, unauthorized access attempts |

### Health Check Aggregation

```
IHealthCheckProvider (N providers)
    ├→ StorageHealthCheck
    ├→ AlpacaConnectionHealthCheck
    ├→ IBConnectionHealthCheck
    └→ ...
        ↓ (parallel execution, 5s timeout per check)
IHealthCheckAggregator
    ↓
AggregatedHealthReport
    ├→ OverallSeverity (worst-case of all checks)
    ├→ HealthyComponents: 8
    ├→ DegradedComponents: 1
    └→ UnhealthyComponents: 0
```

## Implementation Links

<!-- These links are verified by the build process -->

| Component | Location | Purpose |
|-----------|----------|---------|
| Health contract | `src/Meridian.Core/Monitoring/Core/IHealthCheckProvider.cs` | Health severity, provider API, and result types |
| Health aggregation | `src/Meridian.Application/Monitoring/Core/HealthCheckAggregator.cs` | Parallel health evaluation with 5s timeout |
| Alert contract | `src/Meridian.Core/Monitoring/Core/IAlertDispatcher.cs` | Alert modeling, severity, categories, filters |
| Alert dispatcher | `src/Meridian.Application/Monitoring/Core/AlertDispatcher.cs` | Centralized alert publishing and subscription |
| Connection monitor | `src/Meridian.Application/Monitoring/ConnectionHealthMonitor.cs` | Provider connection health tracking |
| Data quality monitor | `src/Meridian.Application/Monitoring/DataQuality/DataQualityMonitoringService.cs` | Data quality health checks |
| Health endpoint | `src/Meridian.Ui.Shared/Endpoints/HealthEndpoints.cs` | `/healthz`, `/readyz`, `/livez` probes |
| Prometheus metrics | `src/Meridian.Application/Monitoring/PrometheusMetrics.cs` | Metrics export |

## Rationale

### Consistent Severity Classification

Standard `HealthSeverity` and `AlertSeverity` enums replace ad-hoc booleans and strings across all components. Factory methods enforce correct construction:

```csharp
// Health check results use factory methods
return HealthCheckResult.Healthy("All 5 providers connected");
return HealthCheckResult.Degraded("2 of 5 providers connected", details);
return HealthCheckResult.Unhealthy("No providers available", exception: ex);

// Alerts use factory methods with category
AlertDispatcher.Publish(MonitoringAlert.Warning(
    AlertCategory.DataQuality,
    source: "GapAnalyzer",
    title: "Sequence gap detected",
    message: "SPY missing 42 events between seq 10500-10542"));
```

### Parallel Health Evaluation with Timeouts

`HealthCheckAggregator` runs all registered checks in parallel with a configurable per-check timeout (default: 5 seconds). Timed-out checks return `Unknown` severity rather than blocking the entire report.

```csharp
// Aggregator runs checks in parallel
var report = await aggregator.CheckAllAsync(ct);
// report.OverallSeverity = worst-case across all checks
// report.TotalDuration = wall-clock time (not sum)
```

Overall severity is computed as the worst case across all checks: Unhealthy > Degraded > Healthy > Unknown.

### Subscription-Based Alert Routing

Consumers subscribe to alerts with optional filters. Predefined filters simplify common patterns:

```csharp
// Subscribe to all alerts
dispatcher.Subscribe(alert => HandleAlert(alert));

// Subscribe to critical alerts only
dispatcher.Subscribe(HandleCritical, AlertFilter.CriticalOnly);

// Subscribe to errors and above
dispatcher.Subscribe(HandleErrors, AlertFilter.ErrorsAndAbove);

// Custom filter: connection alerts at Warning+
dispatcher.Subscribe(HandleConnectionIssues, new AlertFilter
{
    MinSeverity = AlertSeverity.Warning,
    Categories = [AlertCategory.Connection]
});
```

Subscriptions return `IDisposable` for automatic cleanup.

### Centralized Alert History and Statistics

The dispatcher retains the most recent 1000 alerts (configurable) and tracks per-severity and per-category counts:

```csharp
var stats = dispatcher.GetStatistics();
// stats.TotalAlerts, stats.AlertsBySeverity, stats.AlertsByCategory

var recent = dispatcher.GetRecentAlerts(count: 50, AlertFilter.ErrorsAndAbove);
```

## Alternatives Considered

### Alternative 1: Ad-hoc logging only

Rely solely on log statements for health and alerting.

**Pros:**
- Minimal implementation effort

**Cons:**
- No structured severity or aggregation
- Harder to build dashboards or automated response
- No subscription mechanism for UI

**Why rejected:** Lacks actionable signal for operations.

### Alternative 2: Direct metric polling per host

Each host polls components and computes its own health status.

**Pros:**
- Host-specific flexibility

**Cons:**
- Duplicated logic, inconsistent thresholds
- Difficult to share alert history across interfaces
- No centralized statistics

**Why rejected:** Centralized pipeline is more reliable and consistent.

### Alternative 3: External monitoring (Prometheus Alertmanager)

Delegate all alerting to an external monitoring stack.

**Pros:**
- Mature tooling, industry standard
- Built-in routing and escalation

**Cons:**
- Requires external infrastructure
- Latency for in-process alerts
- Cannot feed desktop UI or in-process automation

**Why rejected:** In-process alerting is needed for real-time UI updates and automated failover. Prometheus metrics are still exported for external monitoring alongside the in-process pipeline.

## Consequences

### Positive

- Unified health view across all components with a single aggregated report.
- Alert routing is centralized and predictable with filter-based subscriptions.
- Easier operator tooling and UI integration via subscription mechanism.
- Statistics enable trend analysis and alert fatigue detection.

### Negative

- Requires providers to implement health checks explicitly.
- Alert volume must be managed to avoid noise (configurable history cap helps).
- In-process pipeline does not survive process crashes (complements external monitoring).

### Neutral

- Legacy checks can coexist during migration by wrapping them in `IHealthCheckProvider`.
- Health check timeout (5s) may need tuning for slow external providers.
- Alert history retention (1000 alerts) may need adjustment based on operational patterns.

## Compliance

### Code Contracts

```csharp
// Health check providers
public interface IHealthCheckProvider
{
    string ComponentName { get; }
    Task<HealthCheckResult> CheckHealthAsync(CancellationToken ct = default);
}

public interface IHealthCheckAggregator
{
    void Register(IHealthCheckProvider provider);
    void Unregister(string componentName);
    Task<AggregatedHealthReport> CheckAllAsync(CancellationToken ct = default);
    Task<HealthCheckResult?> CheckAsync(string componentName, CancellationToken ct = default);
}

// Alert dispatching
public interface IAlertDispatcher
{
    void Publish(MonitoringAlert alert);
    Task PublishAsync(MonitoringAlert alert, CancellationToken ct = default);
    IDisposable Subscribe(Action<MonitoringAlert> handler, AlertFilter? filter = null);
    IDisposable Subscribe(Func<MonitoringAlert, Task> handler, AlertFilter? filter = null);
    IReadOnlyList<MonitoringAlert> GetRecentAlerts(int count = 100, AlertFilter? filter = null);
    AlertStatistics GetStatistics();
}
```

### Runtime Verification

- `[ImplementsAdr("ADR-001")]` on `IHealthCheckProvider`, `IHealthCheckAggregator`, `IAlertDispatcher`
- Build-time verification via `make verify-adrs`
- Health endpoints (`/healthz`, `/readyz`, `/livez`) exercise aggregation at runtime
- Integration tests verify parallel execution, timeout behavior, and filter matching

## References

- [Project Context](../generated/project-context.md)
- [Operator Runbook](../operations/operator-runbook.md)
- [ADR-011: Centralized Configuration](011-centralized-configuration-and-credentials.md) - Configuration validation feeds health checks
- [ADR-013: Bounded Channel Policy](013-bounded-channel-policy.md) - Backpressure metrics feed alert dispatcher

---

*Last Updated: 2026-02-20*
