# Plans Folder (Redirect Stubs Only)

**Status:** transitional-compatibility
**Owner:** core-team
**Reviewed:** 2026-11-25

**This folder holds no active plans.** Every planning document is listed in the single
[Plans and Blueprints Register](../engineering/blueprints/README.md), which names each plan's home:

- code-ready blueprints in [`docs/engineering/blueprints/`](../engineering/blueprints/README.md),
- engineering working plans in [`docs/engineering/plans/`](../engineering/plans/README.md),
- product delivery plans and prioritization inputs in [`docs/product/plans/`](../product/plans/README.md),
- finished or superseded plans in [`archive/docs/plans/`](https://github.com/rodoHasArrived/Meridian-main/blob/9c0c9de471fb1adbff73321c7d1ccfa871eb669a/archive/docs/plans/README.md).

Durable roadmap truth belongs in `docs/roadmap/data/*.yml` and generated roadmap views, never in a
plan.

## Why the remaining files stay

Each file left here is a redirect stub. It stays at this path only because tooling or a historical
record reads the path:

| File | Kept because |
|---|---|
| [report-writer-auto-preview-blueprint.md](report-writer-auto-preview-blueprint.md) | The append-only brainstorm ledger (`.claude/skills/meridian-brainstorm/brainstorm-history.jsonl`) records this path; the blueprint itself moved to `docs/engineering/blueprints/` |
| [paper-trading-cockpit-reliability-sprint.md](paper-trading-cockpit-reliability-sprint.md) | Evidence input for the pilot-readiness and paper-replay dashboard generators |

The archive-migration stub points at a plan removed from the tree by the 2026-09-11 archive
cleanup (`982eea2d`); its link resolves to the last copy at `8a420730`.

Retired 2026-11-18: the `desktop-workstation-screen-blueprint` redirect (nothing read it; its
checklist JSON moved beside the
[checklist reference](../engineering/plans/desktop-workstation-screen-blueprint.md)) and the
`research-backtest-trust-and-velocity-blueprint` redirect (no inbound links).

Retired 2026-11-25: the `codebase-audit-cleanup-roadmap` archive-migration stub. Its only reader was
the evaluation fixture set of the `archive/`-filing skill, which was removed along with the stub
because the repository no longer keeps an `archive/` folder.

## Rules

1. Do not add plans here. File new plans in their home folder and add a register row.
2. Remove a stub once nothing reads its path, and update this table.
3. After any change, run `python build/scripts/docs/validate-docs-structure.py --summary` and the
   link check.
