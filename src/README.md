---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-ROOT
path: src
status: active
owner_lane: Core Team
last_reviewed: 2026-05-20
---

# src

## Purpose

`src/` contains Meridian application code. Registered modules under this tree have local READMEs so maintainers and AI agents can understand purpose, boundaries, validation commands, and roadmap value before editing.

## Layer responsibility

The source tree is layered around host composition, application orchestration, contracts, provider/storage infrastructure, active browser workstation UI, shared UI services, and WPF desktop shell.

## Key folders and files

- `src/Meridian/` - host, CLI, and runtime composition.
- `src/Meridian.Application/` - use cases, orchestration, commands, and pipelines.
- `src/Meridian.Contracts/` - shared DTOs and compatibility contracts.
- `src/Meridian.Ui/dashboard/` - active browser workstation UI lane.
- `src/Meridian.Wpf/` - active desktop shell for operator workflows.

## Important workflows

Use the nearest module README before changing source files. Use `docs/source/data/source-modules.yml` to identify module IDs, owner lanes, roadmap links, diagrams, and validation commands.

## Diagrams

Registered diagrams live in `docs/source/data/diagram-index.yml` and sources live under `docs/architecture/diagrams/`.

## Roadmap traceability

Root-level source traceability is managed through registered child modules.

## TODO checklist

Root-level TODOs should be avoided. Add module-level TODOs in `docs/source/data/source-todos.yml`.

## Validation

```bash
python3 build/scripts/docs/validate-source-readmes.py --summary
python3 build/scripts/docs/render-source-docs.py --summary
```

## Change rules

Do not introduce new product lanes from `src/` without roadmap support. Browser workstation work belongs in `src/Meridian.Ui/dashboard/`; WPF desktop work belongs in `src/Meridian.Wpf/`.

## Related docs

- `docs/source/README.md`
- `docs/source/source-documentation-standard.md`
- `docs/architecture/module-map.md`
- `docs/roadmap/README.md`
