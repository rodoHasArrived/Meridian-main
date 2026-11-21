# Operations Documentation

**Status:** transitional-compatibility
**Owner:** core-team
**Reviewed:** 2026-11-12

This folder is a compatibility lane. Active operational guidance is in
[Operators](../operators/README.md), but a small set of paths remains for existing documentation, prompts, and historical links.
Do not add new durable guidance here.

## Canonical Operator Paths

- [Operator documentation start](../operators/README.md)
- [Provider setup](../operators/provider-credentials.md)
- [Troubleshooting and runbooks](../operators/reconciliation-operations.md)
- [Deployment standards](../operators/deployment-packaging.md)

## Migration Notes

Full historical copies are retained in
[archive/docs/operations/](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/operations/README.md), and the legacy-path routing
table in [Operators](../operators/README.md) maps old paths to canonical replacements.

The retained files route older links to their current owners:

- [operator-runbook.md](operator-runbook.md) and
  [service-level-objectives.md](service-level-objectives.md) redirect to `docs/operators/`.
  Monitoring registries, deployment alert rules, and documentation rules now use those canonical
  operator paths.
- [live-execution-controls.md](live-execution-controls.md) routes historical links to the
  [canonical manual-override reference](../reference/provider-capability-matrix.md#live-execution-manual-override-routes).
  The route-consistency test reads that reference directly.
- [cleanup-and-maintenance.md](cleanup-and-maintenance.md) retains maintenance guidance while its
  active prompt and index references remain.

Keep these compatibility paths until their remaining links are migrated; do not add new operator
procedures here.
