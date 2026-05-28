# Operator Observability Dashboard Specification

## Required panels

1. **SLO Burn Rate**
   - 5m and 1h burn-rate gauges.
   - Error-budget remaining trend.
2. **Backlog Growth**
   - Queue depth by pipeline stage.
   - Backlog growth velocity and time-to-drain estimate.
3. **Dependency Degradation Posture**
   - Provider/storage/checkpoint dependency status matrix.
   - Circuit-breaker open/half-open/closed counts.
4. **Latency and Throughput**
   - p50/p95/p99 processing latency.
   - Events processed per second baseline vs surge baseline.

## Data sources

- `/metrics` for queue, latency, CPU, memory, retry, dead-letter counters.
- `/readyz` and `/health/detailed` for dependency-specific degradation flags.
- Workstation status contracts for operator-facing annotations.

## Alert thresholds

- Burn rate > 2.0 (5m) and > 1.0 (1h): page operator.
- Queue depth > 80% bound for 10m: scale stateless workers.
- Dependency unhealthy > 3m: open incident and activate failover runbook.
