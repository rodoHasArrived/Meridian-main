---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-IBAPI-SMOKESTUB
path: src/Meridian.IbApi.SmokeStub
status: active
owner_lane: Data Confidence and Validation
last_reviewed: 2026-05-20
---

# src/Meridian.IbApi.SmokeStub

## Purpose

The IB API smoke stub provides local support for Interactive Brokers integration smoke checks.

## Layer responsibility

This module is provider test support. It should not become production provider logic or shared application behavior.

## Key folders and files

- `Meridian.IbApi.SmokeStub.csproj` - smoke stub project boundary.
- Stub implementation files for IB API smoke validation.

## Important workflows

Use this module for local IB API smoke validation and integration test support.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-IBAPI-SMOKESTUB -->
| Roadmap item | Title |
| --- | --- |
| `W1-DATA-001` | Provider trust gate and data confidence baseline |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-IBAPI-SMOKESTUB -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.IbApi.SmokeStub/Meridian.IbApi.SmokeStub.csproj /p:EnableWindowsTargeting=true
```

## Change rules

Keep smoke stubs deterministic and clearly separated from production provider adapters.

## Related docs

- `docs/providers/`
- `docs/status/provider-validation-matrix.md`
