---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-CORE
path: src/Meridian.Core
status: active
owner_lane: Runtime Host
last_reviewed: 2026-05-20
---

# src/Meridian.Core

## Purpose

Core contains cross-cutting primitives used throughout Meridian: configuration, exceptions, logging, monitoring, scheduling, serialization, and pipeline helpers.

## Layer responsibility

This layer provides low-level reusable infrastructure. It must stay independent of application orchestration, provider implementations, UI services, and domain-specific workflows.

## Key folders and files

- `Config/` - shared configuration models and defaults.
- `Exceptions/` - base exception and error types.
- `Serialization/` - source-generated JSON context support.
- `Pipeline/`, `Scheduling/`, and `Monitoring/` - reusable runtime primitives.

## Important workflows

Use this module when a cross-project primitive or runtime helper is required by multiple higher layers.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-CORE -->
| Roadmap item | Title |
| --- | --- |
| `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W6-LIVE-001` | Live-readiness governance |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-CORE -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
```

## Change rules

Do not introduce references from Core to Application, Infrastructure, Domain orchestration, or UI projects. Keep helpers deterministic and dependency-light.

## Related docs

- `docs/architecture/module-map.md`
- `docs/developer/build-test-run.md`
- `docs/source/generated/source-module-index.md`
