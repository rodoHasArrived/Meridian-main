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

Recent planning content has been migrated to archive stubs in this folder. Use this index for replacement routing and context:

- All markdown files in `docs/plans/` now point to canonical planning owners:
  - [`docs/product/README.md`](../product/README.md)
  - [`docs/engineering/README.md`](../engineering/README.md)
  - [`docs/roadmap/README.md`](../roadmap/README.md)
  - [`../../archive/docs/plans/`](../../archive/docs/plans/)
- Historical rationale for each archived document is maintained in [`../../archive/docs/plans/README.md`](../../archive/docs/plans/README.md).
- Migration batch evidence is captured in `docs/documentation-inventory.md` as `archive` action rows.

## Migration Rules

1. Add new long-form planning docs outside `docs/plans/` and into canonical targets only.
2. Keep this folder as source material for historical context.
3. Every archive batch must update this file (this index) and the target bucket README.
4. After any migration batch, run `python build/scripts/docs/validate-docs-structure.py --summary` and link repair checks.

