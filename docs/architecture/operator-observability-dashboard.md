# Operator Observability Dashboard Specification

**Status:** draft
**Owner:** core-team

This is a dashboard design specification, not a declaration that every panel or threshold is
currently deployed. The source of deployed SLO definitions is
[`SloDefinitionRegistry`](../../src/Meridian.Platform/Monitoring/Core/SloDefinitionRegistry.cs);
configured alerts live in [`alert-rules.yml`](../../deploy/monitoring/alert-rules.yml). Use the
[operator SLO guide](../operators/service-level-objectives.md) for operational interpretation.

Maintenance check 2026-10-05: clarified specification versus deployed policy and verified these
owner paths. The numeric targets below have not been recalibrated or validated in this maintenance pass.

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

Proposed dashboard targets for owner review; reconcile them with deployed SLO and alert policy
before using them to page operators or initiate recovery.

- Burn rate > 2.0 (5m) and > 1.0 (1h): page operator.
- Queue depth > 80% bound for 10m: investigate saturation and evaluate supported capacity changes
  under the [runtime state boundaries](runtime-component-state-boundaries.md#scaling-policy-anchors);
  do not assume workers or hosts can be safely replicated.
- Dependency unhealthy > 3m: open incident and activate failover runbook.
