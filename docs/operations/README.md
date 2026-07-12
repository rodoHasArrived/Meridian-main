# Operations Documentation

**Status:** source-material
**Owner:** core-team
**Reviewed:** 2026-05-31

This folder is now migration source material. Active operational guidance is in [operators/README.md](../operators/README.md).

## Canonical Operator Paths

- [Operator documentation start](../operators/README.md)
- [Provider setup](../operators/provider-credentials.md)
- [Troubleshooting and runbooks](../operators/reconciliation-operations.md)
- [Deployment standards](../operators/deployment-packaging.md)

## Migration Notes

The legacy archive-migration stubs that previously lived in this folder have been removed; full
historical copies are retained in [archive/docs/operations/](../../archive/docs/operations/README.md),
and the legacy-path routing table in [operators/README.md](../operators/README.md) maps each old
`docs/operations/*` path to its canonical replacement.

The files still present here stay because active tooling consumes them at these paths:

- [operator-runbook.md](operator-runbook.md) — alert/SLO runbook anchor referenced by
  `src/Meridian.Platform/Monitoring` registries, `deploy/monitoring/alert-rules.yml`, and
  `build/rules/doc-rules.yaml`.
- [service-level-objectives.md](service-level-objectives.md) — SLO doc anchor referenced by
  `SloDefinitionRegistry`.
- [live-execution-controls.md](live-execution-controls.md) — route-consistency test input
  (`tests/scripts/test_live_execution_controls_route_consistency.py`).
- [cleanup-and-maintenance.md](cleanup-and-maintenance.md) — retained maintenance guidance.