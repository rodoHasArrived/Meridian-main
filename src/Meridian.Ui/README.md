---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-UI
path: src/Meridian.Ui
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-05-20
---

# src/Meridian.Ui

## Purpose

Meridian.Ui contains the browser workstation source folder and built host-served workstation assets.

## Layer responsibility

This folder owns browser workstation packaging and host-served assets. Active source work belongs under `src/Meridian.Ui/dashboard`.

## Key folders and files

- `dashboard/` - active browser workstation application.
- `wwwroot/workstation/` - built workstation assets served by the Meridian host.

## Important workflows

Use `dashboard/` for source changes and generated `wwwroot/workstation/` assets only when rebuilding the host-served workstation bundle is part of the task.

## Diagrams

See `DIA-BROWSER-WORKSTATION` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-UI -->
| Roadmap item | Title |
| --- | --- |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W3-CONT-001` | Research to paper continuity |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-UI -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
npm --prefix src/Meridian.Ui/dashboard run build
```

## Change rules

Do not create mobile product surfaces here. Keep active operator UI work browser-first under `dashboard/`.

## Related docs

- `src/Meridian.Ui/dashboard/README.md`
- `docs/plans/web-ui-development-pivot.md`
- `docs/source/generated/source-module-index.md`
