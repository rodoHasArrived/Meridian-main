# Source Documentation Mesh

This directory maps Meridian source code to plain-English module ownership,
roadmap traceability, TODOs, diagrams, and validation commands.

## Source of truth

- `data/source-modules.yml` lists registered code modules.
- `data/source-todos.yml` lists registry-backed implementation follow-ups.
- `data/diagram-index.yml` links diagrams to modules and roadmap items.
- `data/source-readme-coverage.yml` tracks README coverage.

## Generated outputs

`build/scripts/docs/render-source-docs.py` writes deterministic views under
`docs/source/generated/` and updates only marked generated blocks in source READMEs.

## AI workflow

When editing `src/**`, assistants must read the nearest source README, identify
the module ID, update registry records when ownership or behavior changes, and
report the narrow validation command used.
