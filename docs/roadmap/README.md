# Roadmap Registry

**Status:** canonical-registry
**Owner:** core-team
**Reviewed:** 2026-10-28

This directory is the structured source of truth for Meridian roadmap status.
Human-facing status pages may summarize it, but durable roadmap truth belongs in
`docs/roadmap/data/*.yml`.

## Read the roadmap

- [Roadmap summary](generated/ROADMAP_SUMMARY.md) lists delivery status, health, priority, and owner lane.
- [Roadmap register](generated/roadmap-register.md) gives each item's current summary, exit criteria, and evidence review date.
- [Development roadmap map](../architecture/diagrams/meridian-development-roadmap.mmd) shows item status and health in generated wave/rank order. Its arrows connect adjacent displayed rows; they do not declare implementation prerequisites or override recorded delivery priorities. Unranked engineering hygiene remains outside the ranked product slate.
- [Product plans](../product/plans/README.md#next-work-determinations) owns the latest prioritization pointer.
- [Implementation and Readiness Tracker](../product/implementation-todo-list.md) owns production-release blockers and the separate P0 evidence gate.

An accepted or done capability does not certify a production release. Snapshot dates describe the
registry view; individual items and the production-readiness baseline retain their own review dates.

## Directory contract

- `data/` stores versioned roadmap registries.
- `schemas/` stores schema contracts for registry shape and compatibility.
- `generated/` stores deterministic Markdown views rendered by `build/scripts/docs/render-roadmap-docs.py`.
- `snapshots/` retains migrated detailed status evidence and reservations; load only
  the item relevant to the current task.
- Governance, status taxonomy, schema versioning, generated-doc policy, and item templates live beside this README.

## Maintenance

Install the standard YAML parser, then validate and regenerate both the Markdown views and the
development roadmap map after editing roadmap data:

```bash
python3 -m pip install --requirement build/scripts/docs/requirements.txt
python3 build/scripts/docs/validate-roadmap-registry.py --summary
python3 build/scripts/docs/render-roadmap-docs.py --summary
python3 build/scripts/docs/render-roadmap-diagrams.py --summary
```

The Markdown renderer also updates the compatibility summary at `docs/status/ROADMAP_SUMMARY.md`
and the generated manifest. The diagram renderer owns
`docs/architecture/diagrams/meridian-development-roadmap.mmd`.
Generated files are derived artifacts. Update the registry or renderer instead of editing generated outputs by hand.

### Compact status contracts

Roadmap and program data use the [v2 structured-status contracts](schema-versioning.md#structured-status-migration-2026-11-14).
Set status, health, evidence posture, decision IDs and PR references in their own
fields. Keep `status_note` within 320 characters; link detailed implementation
boundaries through `status_detail`. The original scalar source remains in each
item's migration snapshot, including historical reservations. Migration does not
change acceptance or release authority.

The production-readiness tracker is generated from
`data/production-readiness.yml`, where implementation state and release completion
are separate. Its full detail remains linked from the compact tracker. To inspect
or repair these generated views locally:

```bash
python3 build/scripts/docs/render-production-readiness.py --summary
python3 scripts/generate_program_state_summary.py
```

The canonical registry validator checks these registries together. Generated
status prose must not become a second hand-maintained source of status.

### Two things that will fail a PR if you miss them

**Declare a phase.** Authored edits under `docs/roadmap/**` are governed by the phase-scope gate in
`tools/roadmap/enforce_phase_scope.py`, which runs as the `scope-gate` check. A PR that edits this
registry needs a `phase:PRx` label or an HTML-comment phase marker in its body, or the gate fails
with "No phase declaration found" — see `PHASE_RULES` in that script for which paths each phase
allows, and prefer the narrowest phase that covers the change. Generated artifacts are exempt, which
is why a PR that only regenerates dashboards passes without one. Note that the gate reads the body
from the triggering event, so adding the marker to an open PR takes effect on the next push rather
than on the edit itself.

**Write valid YAML.** Canonical registries are parsed by PyYAML. Quote text containing colon-space
sequences or use a standard folded/block scalar; quote literal hash signs when they belong to the
text. Malformed input fails validation and rendering with its file path and available line/column
details. A missing parser dependency is an error. No subset-parser recovery applies to these files.
