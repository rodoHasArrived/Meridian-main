# Source Documentation Standard

Source documentation explains code ownership in plain English for maintainers, operators, and AI agents.

## Core artifacts

Every registered source module is represented in:

- `docs/source/data/source-modules.yml`
- `docs/source/data/source-readme-coverage.yml`
- The owning `src/**/README.md`

## Required source README front matter

```markdown
---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-UI-DASHBOARD
path: src/Meridian.Ui/dashboard
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-05-20
---
```

## Required sections

Each registered source README must include these headings:

- `## Purpose`
- `## Layer responsibility`
- `## Key folders and files`
- `## Important workflows`
- `## Diagrams`
- `## Roadmap traceability`
- `## TODO checklist`
- `## Validation`
- `## Change rules`
- `## Related docs`

Humans own the prose. Generators own only marked blocks under roadmap traceability and TODO checklist.

## Generated blocks

```markdown
<!-- source-roadmap-traceability:begin module=SRC-UI-DASHBOARD -->
Generated content.
<!-- source-roadmap-traceability:end -->

<!-- source-todos:begin module=SRC-UI-DASHBOARD -->
Generated content.
<!-- source-todos:end -->
```

Run validation locally:

```bash
python3 build/scripts/docs/validate-source-readmes.py --summary
python3 build/scripts/docs/render-source-docs.py --summary
```
