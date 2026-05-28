---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-APP
path: src/Meridian.Application
status: active
owner_lane: Runtime Host
last_reviewed: 2026-05-25
---

# src/Meridian.Application

## Purpose

Meridian application layer contains use cases, orchestration services, commands, and workflow
coordination.

## Layer responsibility

This module owns application workflows that coordinate providers, storage, execution, ledger,
reporting, and UI-facing services through contracts. Keep transport, persistence implementation,
and UI presentation concerns in their owning layers.

## Key folders and files

- `Commands/` - CLI command handlers and operator workflows.
- `OperationsContinuity/` - account-period continuity aggregate, command transitions, audit
  timeline, and server-derived gate status for broker, Security Master, ledger, reconciliation,
  and approval close lanes. Approval and close commands enforce shared close-checklist control
  approvals before the workflow can become ready for close or close against a report pack. Close
  readiness is scored server-side across Security Master, position, cash, ledger, pricing,
  reconciliation, report, and approval components.
- `Services/` - application use cases and orchestration services.
- `Composition/` - application feature registration and service wiring.

## Important workflows

Use this module when changing command behavior, workflow orchestration, feature registration, or
application service contracts consumed by host and UI surfaces.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-APP -->
| Roadmap item | Title |
| --- | --- |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W2-PROMO-001` | Paper promotion evidence and operator acceptance |
| `W3-CONT-001` | Research to paper continuity |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-APP -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
```

## Change rules

Keep orchestration here. Do not leak transport/UI concerns into this layer or add direct
infrastructure details when an abstraction already exists.

## Related docs

- `docs/architecture/module-map.md`
- `docs/developer/build-test-run.md`
- `docs/source/generated/source-module-index.md`
