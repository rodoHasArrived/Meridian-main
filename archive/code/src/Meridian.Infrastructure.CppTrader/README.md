---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-INFRASTRUCTURE-CPPTRADER
path: archive/code/src/Meridian.Infrastructure.CppTrader
status: archived
owner_lane: Data Confidence and Validation
last_reviewed: 2026-05-29
---

# Archived: src/Meridian.Infrastructure.CppTrader

## Archive status

This optional CppTrader integration was moved out of active source on 2026-05-29.
Default Meridian live execution does not depend on this project: normal host builds used
`EnableCppTraderIntegration=false`, the CppTrader gateway was simulation-oriented, and the active
execution lane is served by the managed execution projects.

## Purpose

This module isolated CppTrader integration support from the core provider and application layers.

## Layer responsibility

CppTrader-specific infrastructure stayed here so shared provider abstractions and application services did not absorb vendor-specific concerns.

## Key folders and files

- `Meridian.Infrastructure.CppTrader.csproj` - project boundary for the integration.
- Adapter and interop files specific to the CppTrader integration.

## Important workflows

Do not use this module for active Meridian execution work. Restore it from `archive/code/` only if
CppTrader becomes an explicitly supported integration again.

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
dotnet build archive/code/src/Meridian.Infrastructure.CppTrader/Meridian.Infrastructure.CppTrader.csproj /p:EnableWindowsTargeting=true
```

## Change rules

Keep this archived unless a product decision reintroduces CppTrader as an active supported bridge.

## Related docs

- `docs/providers/`
- `docs/source/generated/source-module-index.md`
