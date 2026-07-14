# Plans Source-Material Index

**Status:** migration-source
**Owner:** core-team
**Reviewed:** 2026-05-31

This folder is retained for historical and implementation planning references while canonical planning is maintained in:

- [`docs/product/README.md`](../product/README.md)
- [`docs/engineering/README.md`](../engineering/README.md)
- [`docs/operators/README.md`](../operators/README.md)
- [`docs/roadmap/README.md`](../roadmap/README.md)

## Current Role In The Rebuild

- Treat plan files as source material unless explicitly linked from canonical targets.
- High-traffic or completed plans are archived in [`../../archive/docs/plans/README.md`](../../archive/docs/plans/README.md).
- Durable roadmap truth belongs in `docs/roadmap/data/*.yml` and generated roadmap views.
- Durable implementation rules belong in `docs/engineering/README.md` and `docs/reference/` where appropriate.

## Migration Summary

Legacy planning content has been migrated to [`../../archive/docs/plans/`](../../archive/docs/plans/),
and the archive-migration stubs that previously mirrored each archived plan in this folder have been
removed. Use [`../../archive/docs/plans/README.md`](../../archive/docs/plans/README.md) for the full
historical index; migration batch evidence is captured in `docs/documentation-inventory.md` as
`archive` action rows.

The files still present here stay because they are active or consumed by tooling at these paths:

- [security-master-passport-workbench.md](security-master-passport-workbench.md) — active plan.
- [desktop-workstation-screen-blueprint.md](desktop-workstation-screen-blueprint.md) and
  [desktop-workstation-screen-blueprint.checklist.json](desktop-workstation-screen-blueprint.checklist.json)
  — consumed by `scripts/dev/desktop_screen_blueprint_checklist.py` and its tests.
- [paper-trading-cockpit-reliability-sprint.md](paper-trading-cockpit-reliability-sprint.md) —
  evidence input for the pilot-readiness and paper-replay dashboard generators.
- [codebase-audit-cleanup-roadmap.md](codebase-audit-cleanup-roadmap.md) — referenced by the
  meridian-archive-organizer skill evaluation fixtures (`must_exist`).
- [research-backtest-trust-and-velocity-blueprint.md](research-backtest-trust-and-velocity-blueprint.md)
  — referenced by the meridian-simulated-user-panel skill evaluation manifests.

## Migration Rules

1. Add new long-form planning docs outside `docs/plans/` and into canonical targets only.
2. Keep this folder as source material for historical context.
3. Every archive batch must update this file (this index) and the target bucket README.
4. After any migration batch, run `python build/scripts/docs/validate-docs-structure.py --summary` and link repair checks.

