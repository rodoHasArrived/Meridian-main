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

# src/Meridian.Ui/dashboard

## Purpose

The dashboard is Meridian's active browser-first operator workstation. It renders Trading, Portfolio, Accounting, Reporting, Strategy, Data, and Settings workflows under the host-served workstation route.

## Layer responsibility

This layer owns browser UI behavior, screen state, and operator interactions. Shared contracts and endpoint behavior belong in `src/Meridian.Ui.Shared` and `src/Meridian.Ui.Services`.

## Key folders and files

- `src/` - React application source, screens, view models, routing, and shared UI helpers.
- `package.json` - dashboard-local test, build, and screenshot commands.
- `vite.config.*` - workstation bundle and local development configuration.

## Important workflows

Use the dashboard for new operator UI development. Keep state labels, disabled reasons, and accessibility status in view-model seams where existing patterns support it.

## Diagrams

See `DIA-BROWSER-WORKSTATION` and `DIA-PAPER-SESSION-REPLAY` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-UI-DASHBOARD -->
| Roadmap item | Title |
| --- | --- |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W2-PROMO-001` | Paper promotion evidence and operator acceptance |
| `W3-CONT-001` | Research to paper continuity |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-UI-DASHBOARD -->
| TODO | Title | Status | Priority |
| --- | --- | --- | --- |
| `TODO-SRC-UI-DASHBOARD-001` | Add browser workstation route diagram coverage for paper readiness | open | medium |
<!-- source-todos:end -->

## Validation

```bash
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
```

## Change rules

Do not create mobile-specific product lanes from this module. Responsive browser validation is allowed for the browser workstation. Shared DTO or endpoint changes must be reflected in the owning shared module and registry.

## Related docs

- `docs/plans/web-ui-development-pivot.md`
- `docs/ai/codex/README.md`
- `docs/source/generated/source-roadmap-traceability.md`
