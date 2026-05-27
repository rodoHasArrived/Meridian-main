---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-UI-SERVICES
path: src/Meridian.Ui.Services
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-05-25
---

# src/Meridian.Ui.Services

## Purpose

UI services contains workstation endpoints, UI projections, and operator workflow service support.

## Layer responsibility

This module owns backend read-model and workflow surfaces for operator UI clients. Keep endpoint
contracts aligned with shared UI models and compatibility gates.

## Key folders and files

- Endpoint and projection services - workstation-facing API composition.
- Workflow service support - operator readiness, routing, and read-model aggregation.
- Project metadata - service dependencies shared by browser and desktop surfaces.

## Important workflows

Use this module when changing workstation endpoint behavior, operator workflow read models,
readiness projections, or UI-service orchestration consumed by browser and WPF clients.

## Diagrams

See `DIA-BROWSER-WORKSTATION` and `DIA-PAPER-SESSION-REPLAY` in
`docs/source/data/diagram-index.yml`.

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

Keep endpoint contracts aligned with `src/Meridian.Ui.Shared` and `src/Meridian.Contracts`. Prefer
shared projections over browser-only or WPF-only product logic.

## Related docs

- `src/Meridian.Ui.Shared/README.md`
- `docs/status/contract-compatibility-matrix.md`
- `docs/source/generated/source-module-index.md`
