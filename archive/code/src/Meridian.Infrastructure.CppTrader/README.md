---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-INFRASTRUCTURE-CPPTRADER
path: src/Meridian.Infrastructure.CppTrader
status: active
owner_lane: Data Confidence and Validation
last_reviewed: 2026-05-20
---

# src/Meridian.Infrastructure.CppTrader

## Purpose

This module isolates CppTrader integration support from the core provider and application layers.

## Layer responsibility

CppTrader-specific infrastructure should stay here so shared provider abstractions and application services do not absorb vendor-specific concerns.

## Key folders and files

- `Meridian.Infrastructure.CppTrader.csproj` - project boundary for the integration.
- Adapter and interop files specific to the CppTrader integration.

## Important workflows

Use this module for CppTrader-specific integration changes and smoke validation.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-INFRASTRUCTURE-CPPTRADER -->
| Roadmap item | Title |
| --- | --- |
| `W1-DATA-001` | Provider trust gate and data confidence baseline |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-INFRASTRUCTURE-CPPTRADER -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.Infrastructure.CppTrader/Meridian.Infrastructure.CppTrader.csproj /p:EnableWindowsTargeting=true
```

## Change rules

Keep vendor-specific code isolated. Promote only reusable abstractions back to ProviderSdk or Contracts.

## Related docs

- `docs/providers/`
- `docs/source/generated/source-module-index.md`
