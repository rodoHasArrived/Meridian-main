# Service Level Objectives (SLOs)

This document defines the Service Level Objectives for each subsystem of the Meridian. SLOs provide measurable targets for reliability, performance, and data quality, enabling consistent alert calibration and incident severity mapping.

---

## 1. Ingestion Plane

### SLO-ING-001: End-to-End Ingestion Latency

| Attribute | Value |
|-----------|-------|
| **Metric** | `mdc_provider_latency_seconds` |
| **Target** | P95 end-to-end latency < 2 seconds |
| **Critical Threshold** | P99 latency > 5 seconds |
| **Measurement Window** | 5-minute rolling window |
| **Error Budget** | 0.1% of events may exceed 5s in a 30-day period |

**Burn-Rate Thresholds:**

| Window | Budget Consumed | Alert Severity |
|--------|----------------|----------------|
| 1 hour | > 2% | P2 Warning |
| 6 hours | > 5% | P1 Critical |
| 24 hours | > 10% | P1 Critical (page) |

**Incident Priority Mapping:**

- P1: P99 latency exceeds 10s for > 5 minutes
- P2: P95 latency exceeds 2s for > 15 minutes
- P3: P50 latency exceeds 500ms for > 30 minutes

### SLO-ING-002: Event Drop Rate

| Attribute | Value |
|-----------|-------|
| **Metric** | `mdc_pipeline_events_dropped_total` / `mdc_pipeline_events_published_total` |
| **Target** | Drop rate < 0.1% of events over 24 hours |
| **Critical Threshold** | Drop rate > 1% sustained for 5 minutes |
| **Error Budget** | 0.1% total drops per 30-day period |

**Burn-Rate Thresholds:**

| Window | Budget Consumed | Alert Severity |
|--------|----------------|----------------|
| 5 min | > 10x budget rate | P2 Warning |
| 1 hour | > 5x budget rate | P1 Critical |
| 24 hours | > 2x budget rate | P2 Warning |

---

## 2. Data Completeness Plane

### SLO-DC-001: Daily Data Completeness

| Attribute | Value |
|-----------|-------|
| **Metric** | `mdc_data_quality_score` |
| **Target** | >= 95% of expected events received per symbol per trading day |
| **Critical Threshold** | < 80% completeness for any symbol |
| **Measurement Window** | Per trading day (market open to close) |
| **Error Budget** | 5 trading days per quarter with completeness < 95% |

**Burn-Rate Thresholds:**

| Window | Budget Consumed | Alert Severity |
|--------|----------------|----------------|
| 1 day | < 80% completeness | P1 Critical |
| 1 day | < 95% completeness | P2 Warning |
| 1 week | > 2 days below 95% | P2 Warning |

### SLO-DC-002: Data Gap Duration

| Attribute | Value |
|-----------|-------|
| **Metric** | Gap analysis via `/api/quality/gaps` |
| **Target** | No gaps > 5 minutes during market hours |
| **Critical Threshold** | Gap > 15 minutes during market hours |
| **Error Budget** | Max 30 minutes cumulative gap time per trading day |

---

## 3. Availability Plane

### SLO-AV-001: Collector Uptime

| Attribute | Value |
|-----------|-------|
| **Metric** | `up{job="meridian"}` |
| **Target** | 99.9% uptime during market hours (9:30 AM - 4:00 PM ET, Mon-Fri) |
| **Critical Threshold** | Any unplanned downtime > 1 minute during market hours |
| **Error Budget** | 26 minutes per month (~99.9%) |

**Burn-Rate Thresholds:**

| Window | Budget Consumed | Alert Severity |
|--------|----------------|----------------|
| 1 min | Service unreachable | P1 Critical |
| 5 min | > 5% monthly budget | P1 Critical (page) |
| 1 hour | > 20% monthly budget | P1 Critical (page) |

### SLO-AV-002: API Health Endpoint

| Attribute | Value |
|-----------|-------|
| **Metric** | `mdc_health_status` |
| **Target** | Health endpoint returns healthy >= 99.5% of checks |
| **Critical Threshold** | Unhealthy for > 2 consecutive minutes |
| **Measurement** | 10-second health check interval |
| **Error Budget** | 43 minutes unhealthy per month (~99.5%) |

---

## 4. Data Freshness Plane

### SLO-DF-001: Streaming Data Freshness

| Attribute | Value |
|-----------|-------|
| **Metric** | `mdc_data_freshness_age_seconds` |
| **Target** | P95 data freshness < 60 seconds during market hours |
| **Critical Threshold** | Any symbol stale > 5 minutes during market hours |
| **Error Budget** | 1% of symbols may be stale (> 60s) at any point |

**Per-Symbol Overrides:**

| Symbol Tier | Freshness Target | Critical Threshold |
|-------------|------------------|--------------------|
| Core (SPY, QQQ) | 30 seconds | 2 minutes |
| Major (AAPL, MSFT, etc.) | 60 seconds | 5 minutes |
| Standard | 120 seconds | 10 minutes |

### SLO-DF-002: Historical Backfill Freshness

| Attribute | Value |
|-----------|-------|
| **Metric** | Backfill completion time |
| **Target** | Daily backfill completes within 30 minutes of market close |
| **Critical Threshold** | Backfill not completed within 2 hours of market close |

---

## 5. Storage Plane

### SLO-ST-001: Storage Write Reliability

| Attribute | Value |
|-----------|-------|
| **Metric** | `mdc_storage_write_errors_total` |
| **Target** | 0 write errors during normal operation |
| **Critical Threshold** | Any write error sustained for > 1 minute |
| **Error Budget** | 0 (zero tolerance for data loss) |

### SLO-ST-002: Storage Capacity

| Attribute | Value |
|-----------|-------|
| **Metric** | Disk free space on data paths |
| **Target** | >= 20% free space on all storage paths |
| **Warning Threshold** | < 20% free (10 GB minimum) |
| **Critical Threshold** | < 5% free (2 GB minimum) |

---

## 6. Provider Connectivity Plane

### SLO-PC-001: Provider Connection Availability

| Attribute | Value |
|-----------|-------|
| **Metric** | `mdc_provider_connected` |
| **Target** | At least one provider connected >= 99.5% during market hours |
| **Critical Threshold** | All providers disconnected for > 2 minutes |
| **Error Budget** | 43 minutes total disconnection per month |

### SLO-PC-002: Provider Failover Time

| Attribute | Value |
|-----------|-------|
| **Metric** | Time between primary provider failure and backup activation |
| **Target** | Automatic failover within 30 seconds |
| **Critical Threshold** | Failover takes > 2 minutes |

---

## Error Budget Policy

### Budget Exhaustion Actions

| Budget Remaining | Action |
|-----------------|--------|
| > 50% | Normal operations |
| 25-50% | Increase monitoring frequency, review recent changes |
| 10-25% | Freeze non-critical deployments, prioritize reliability work |
| < 10% | All hands on reliability, no feature work until budget recovers |
| Exhausted | Post-incident review required, mandatory reliability sprint |

### Monthly Review

At the start of each month:

1. Review SLO compliance for each subsystem
2. Calculate remaining error budget
3. Identify SLOs at risk of exhaustion
4. Adjust alert thresholds if burn rate is too noisy
5. Update incident priority mappings if needed

---

## Monitoring Implementation

### Prometheus Metrics Required

```promql
# Ingestion latency (histogram)
mdc_provider_latency_seconds_bucket
mdc_provider_latency_seconds_count
mdc_provider_latency_seconds_sum

# Pipeline throughput
mdc_pipeline_events_published_total
mdc_pipeline_events_dropped_total
mdc_pipeline_queue_utilization

# Health and availability
mdc_health_status
up{job="meridian"}

# Data quality
mdc_data_quality_score
mdc_data_freshness_age_seconds

# Storage
mdc_storage_write_errors_total

# Provider connectivity
mdc_provider_connected
```

### Grafana Dashboard Panels

Each SLO should have a corresponding Grafana dashboard panel showing:

1. **Current compliance percentage** (gauge)
2. **Error budget remaining** (gauge, color-coded)
3. **Burn rate trend** (time series, last 7 days)
4. **SLO violations timeline** (annotations on time series)

### Alert Rule Integration

All alerts in `deploy/monitoring/alert-rules.yml` reference their corresponding SLO and include `runbook_url` annotations linking to `docs/operations/operator-runbook.md`.

---

*Last Updated: 2026-02-21*
