# Roadmap Governance

## Phase scope markers (retired 2026-11-26)

The `scope-gate` job and `tools/roadmap/enforce_phase_scope.py` were retired. Pull requests no
longer declare a `PR0`…`PR9` phase through a label or a `<!-- phase:PRx -->` body marker, and
`roadmap-source-docs.yml` no longer re-runs on PR body or label edits. Older plans and evidence
that mention phase markers describe the gate as it was when they were written.

Roadmap edits are still validated by the `schema` job in `roadmap-source-docs.yml` (registry,
source README and source TODO validators) and by the required `Meridian CI / quality-gate`.

## Schema evolution policy

Roadmap registry/rendering validators treat unknown top-level fields as errors and enforce explicit required structural fields.

- **Major version change (`vN` -> `vN+1`)**: required when renaming/removing required fields, tightening enum value sets, or changing field semantics in a way that breaks existing items.
- **Minor-compatible change (same major file)**: additive optional fields are allowed only through controlled extension objects (`extensions` with `x-*` keys) or explicitly added optional schema properties.
- **Migration trigger**: if validator output introduces new "missing required field" or "unexpected field" failures on existing data, publish a migration update in the same PR (data update and changelog/governance note) before enabling the stricter rule in automation.
- **Renderer/registry contract**: IDs, ownership, evidence posture, exit criteria, links, and timestamps are treated as structural and must remain explicitly modeled in schema-required fields for deterministic rendering and tracking.
