---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-CONTRACTS
path: src/Meridian.Contracts
status: active
owner_lane: Contract Compatibility
last_reviewed: 2026-05-20
---

# src/Meridian.Contracts

## Purpose

Contracts contains shared DTOs and compatibility types used across host, application, UI services, browser dashboard, retained WPF, and integration seams.

## Layer responsibility

This project is the shared contract layer. It must remain dependency-light and must not reference higher-level projects.

## Key folders and files

- DTO and model definitions used by workstation endpoints and shared operator surfaces.
- Compatibility records used by browser and retained desktop consumers.

## Important workflows

Contract changes affect multiple consumers. Treat additive DTO fields, enum growth, serialization behavior, and breaking changes as cross-module work.
The Operations Continuity contract matrix also publishes stable blocker/issue code vocabularies for
Security Master accounting-event coverage, accrual mismatch, and factor-paydown reconciliation
posture so browser, retained WPF, and backend tests can share the same semantics.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-CONTRACTS -->
| Roadmap item | Title |
| --- | --- |
| `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W3-CONT-001` | Research to paper continuity |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W4-RPT-001` | Governed report pack readiness |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-CONTRACTS -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
```

## Change rules

`Contracts` must not reference other Meridian projects. Shared DTOs belong here only when multiple layers need the same transport or compatibility contract.

## Related docs

- `docs/architecture/module-map.md`
- `docs/status/contract-compatibility-matrix.md`
- `docs/source/generated/source-module-index.md`
