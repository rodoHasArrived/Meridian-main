# Operations Documentation

**Status:** transitional-compatibility
**Owner:** core-team
**Reviewed:** 2026-07-19

This folder is a compatibility lane. Active operational guidance is in
[Operators](../operators/README.md), but a small set of paths remains because tests, monitoring
registries, rules, or strong links still consume them. Do not add new durable guidance here.

## Canonical Operator Paths

- [Operator documentation start](../operators/README.md)
- [Provider setup](../operators/provider-credentials.md)
- [Troubleshooting and runbooks](../operators/reconciliation-operations.md)
- [Deployment standards](../operators/deployment-packaging.md)

## Migration Notes

Full historical copies are retained in
[archive/docs/operations/](../../archive/docs/operations/README.md), and the legacy-path routing
table in [Operators](../operators/README.md) maps old paths to canonical replacements.

The files still present here stay because active tooling consumes them at these paths:

- [operator-runbook.md](operator-runbook.md) — alert/SLO runbook anchor referenced by
  `src/Meridian.Platform/Monitoring` registries, `deploy/monitoring/alert-rules.yml`, and
  `build/rules/doc-rules.yaml`.
- [service-level-objectives.md](service-level-objectives.md) — SLO doc anchor referenced by
  `SloDefinitionRegistry`.
- [live-execution-controls.md](live-execution-controls.md) — route-consistency test input
  (`tests/scripts/test_live_execution_controls_route_consistency.py`).
- [cleanup-and-maintenance.md](cleanup-and-maintenance.md) — retained maintenance guidance.
