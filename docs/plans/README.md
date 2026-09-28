# Plans Folder (Redirect Stubs Only)

**Status:** transitional-compatibility
**Owner:** core-team
**Reviewed:** 2026-09-28

**This folder holds no active plans.** Every planning document is listed in the single
[Plans and Blueprints Register](../engineering/blueprints/README.md), which names each plan's home:

- code-ready blueprints in [`docs/engineering/blueprints/`](../engineering/blueprints/README.md),
- engineering working plans in [`docs/engineering/plans/`](../engineering/plans/README.md),
- product delivery plans and prioritization inputs in [`docs/product/plans/`](../product/plans/README.md),
- finished or superseded plans in [`archive/docs/plans/`](../../archive/docs/plans/README.md).

Durable roadmap truth belongs in `docs/roadmap/data/*.yml` and generated roadmap views, never in a
plan.

## Why the remaining files stay

Each file left here is a redirect stub. It stays at this path only because tooling or a historical
record reads the path:

| File | Kept because |
|---|---|
| [report-writer-auto-preview-blueprint.md](report-writer-auto-preview-blueprint.md) | The append-only brainstorm ledger (`.claude/skills/meridian-brainstorm/brainstorm-history.jsonl`) records this path; the blueprint itself moved to `docs/engineering/blueprints/` |
| [desktop-workstation-screen-blueprint.md](desktop-workstation-screen-blueprint.md) and [desktop-workstation-screen-blueprint.checklist.json](desktop-workstation-screen-blueprint.checklist.json) | Read by `scripts/dev/desktop_screen_blueprint_checklist.py` and its tests |
| [paper-trading-cockpit-reliability-sprint.md](paper-trading-cockpit-reliability-sprint.md) | Evidence input for the pilot-readiness and paper-replay dashboard generators |
| [codebase-audit-cleanup-roadmap.md](codebase-audit-cleanup-roadmap.md) | Referenced by the meridian-archive-organizer skill evaluation fixtures (`must_exist`) |
| [research-backtest-trust-and-velocity-blueprint.md](research-backtest-trust-and-velocity-blueprint.md) | Redirect for inbound links to the archived plan |

The four archive-migration stubs point at plans removed from the tree by the 2026-09-11 archive
cleanup (`982eea2d`); their links resolve to the last copies at `8a420730`.

## Rules

1. Do not add plans here. File new plans in their home folder and add a register row.
2. Remove a stub once nothing reads its path, and update this table.
3. After any change, run `python build/scripts/docs/validate-docs-structure.py --summary` and the
   link check.
