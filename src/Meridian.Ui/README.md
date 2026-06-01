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
Browser workflow target routing keeps desktop compatibility page tags aligned to the canonical operator lanes under `dashboard/`.
The browser Data workspace exposes provider catalog/onboarding discovery through `/data/providers`
while keeping generated host assets in `wwwroot/workstation/` as build output.
The browser app shell projects the design-document primary operator workflow
(`Import`, `Validate`, `Reconcile`, `Investigate`, `Approve`, `Report`) from dashboard view-model
state so host-served workstation assets retain the same financial-operations flow after rebuilds.
The dashboard Accounting Closeout trail also mirrors the design-document Financial Operations flow:
`Receive Activity`, `Match Records`, `Resolve Exceptions`, `Approve Results`, and `Produce Evidence`.
The browser Accounting screen renders that same lane above its detailed closeout panels.
Browser navigation and command-palette root commands canonicalize supplied workspace metadata to
`Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings` so legacy
root names stay compatibility aliases.

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

Do not create mobile product surfaces here. Keep browser operator UI work under `dashboard/` and shared behavior in contracts/services consumed by both active operator surfaces.

## Related docs

- `src/Meridian.Ui/dashboard/README.md`
- `docs/product/meridian-design-document.md`
- `docs/architecture/desktop-layers.md`
- `docs/source/generated/source-module-index.md`
