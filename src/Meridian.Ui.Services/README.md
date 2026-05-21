---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-UI-SERVICES
path: src/Meridian.Ui.Services
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-05-20
---

# src/Meridian.Ui.Services

## Purpose

UI Services provides workstation endpoints, operator projections, and shared service support for browser and retained desktop workflows.

## Layer responsibility

This layer shapes API/read-model support for UI surfaces. It should depend on contracts and lower-level services without becoming the application orchestration layer.

## Key folders and files

- Endpoint mapping and workstation service code.
- Projection helpers that turn readiness, inbox, reconciliation, and workflow data into operator-facing payloads.

## Important workflows

Use this module for workstation endpoint behavior, operator inbox routing, trading readiness projections, and shared UI service behavior.

## Diagrams

See `DIA-BROWSER-WORKSTATION` and `DIA-PAPER-SESSION-REPLAY` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-UI-SERVICES -->
| Roadmap item | Title |
| --- | --- |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W2-PROMO-001` | Paper promotion evidence and operator acceptance |
| `W3-CONT-001` | Research to paper continuity |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W4-RPT-001` | Governed report pack readiness |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-UI-SERVICES -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~MapWorkstationEndpoints" --logger "console;verbosity=normal"
```

## Change rules

Keep UI Services out of direct `Application` references when the boundary requires contract-only use. Treat endpoint payload changes as shared contract changes.

## Related docs

- `docs/plans/web-ui-development-pivot.md`
- `docs/status/contract-compatibility-matrix.md`
- `docs/source/generated/source-roadmap-traceability.md`
