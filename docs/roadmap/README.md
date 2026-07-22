# Roadmap Registry

**Status:** canonical-registry
**Owner:** core-team
**Reviewed:** 2026-07-19

This directory is the structured source of truth for Meridian roadmap status.
Human-facing status pages may summarize it, but durable roadmap truth belongs in
`docs/roadmap/data/*.yml`.

## Directory contract

- `data/` stores versioned roadmap registries.
- `schemas/` stores schema contracts for registry shape and compatibility.
- `generated/` stores deterministic Markdown views rendered by `build/scripts/docs/render-roadmap-docs.py`.
- Governance, status taxonomy, schema versioning, generated-doc policy, and item templates live beside this README.

## Maintenance

Run the roadmap validation and render lane after editing roadmap data:

```bash
python3 build/scripts/docs/validate-roadmap-registry.py --summary
python3 build/scripts/docs/render-roadmap-docs.py --summary
```

Generated files are derived artifacts. Update the registry or renderer instead of editing generated outputs by hand.
