# Roadmap Registry

**Status:** canonical-registry
**Owner:** core-team
**Reviewed:** 2026-09-28

This directory is the structured source of truth for Meridian roadmap status.
Human-facing status pages may summarize it, but durable roadmap truth belongs in
`docs/roadmap/data/*.yml`.

## Directory contract

- `data/` stores versioned roadmap registries.
- `schemas/` stores schema contracts for registry shape and compatibility.
- `generated/` stores deterministic Markdown views rendered by `build/scripts/docs/render-roadmap-docs.py`.
- Governance, status taxonomy, schema versioning, generated-doc policy, and item templates live beside this README.

## Maintenance

Install the standard YAML parser, then run the validation and render lane after editing roadmap data:

```bash
python3 -m pip install --requirement build/scripts/docs/requirements.txt
python3 build/scripts/docs/validate-roadmap-registry.py --summary
python3 build/scripts/docs/render-roadmap-docs.py --summary
```

Generated files are derived artifacts. Update the registry or renderer instead of editing generated outputs by hand.

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
