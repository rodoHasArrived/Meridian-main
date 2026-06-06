---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-CORE
path: src/Meridian.Core
status: active
owner_lane: Runtime Host
last_reviewed: 2026-06-06
---

# src/Meridian.Core

## Purpose

Core contains cross-cutting primitives used throughout Meridian: configuration, exceptions,
logging, monitoring, scheduling, serialization, redaction, masking, and pipeline helpers.

## Layer responsibility

This layer provides low-level reusable infrastructure. It must stay independent of application orchestration, provider implementations, UI services, and domain-specific workflows.

## Key folders and files

- `Config/` - shared configuration models, defaults, environment overrides, configuration
  templates, and sensitive-value masking primitives.
- `Diagnostics/` - low-level runtime redaction helpers used by diagnostic bundles, endpoints, and
  shutdown flows without depending on Application services.
- `Exceptions/` - base exception and error types.
- `Serialization/` - source-generated JSON context support.
- `Pipeline/`, `Scheduling/`, `Services/`, and `Monitoring/` - reusable runtime primitives,
  including the `IFlushable` shutdown/flush contract used by storage sinks and Application
  shutdown orchestration.

## Important workflows

Use this module when a cross-project primitive or runtime helper is required by multiple higher layers.
Runtime feature-capability options live in `Config/FeatureCapabilityOptions.cs` so desktop and host
configuration loading can bind the same `FeatureCapabilities` section without introducing UI
references into Core.
`Config/ConfigEnvironmentOverride.cs` and `Config/ConfigTemplateGenerator.cs` provide shared
environment-variable override handling and configuration-template generation for Application
commands, configuration services, and shared endpoints.
`Config/SensitiveValueMasker.cs` and `Diagnostics/RuntimeDiagnosticRedactor.cs` provide the shared
redaction baseline for support bundles, endpoint payloads, and runtime logs so UI/shared and
Application consumers do not carry local masking helpers.
`Services/IFlushable.cs` is the shared flush contract for buffered components. Storage sinks and
Application shutdown services consume this Core-owned contract so durable persistence layers do not
depend on Application service namespaces.
`Serialization/SecurityMasterJsonContext.cs` includes source-generated metadata for Security Master
validation, reference-data, custom asset profile, and custom profile governance DTOs so shared
services can serialize those payloads without reflection-based fallback.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-CORE -->
| Roadmap item | Title |
| --- | --- |
| `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W7-LIVE-001` | Live-readiness governance |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-CORE -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.Core/Meridian.Core.csproj /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~ConfigEnvironmentOverrideTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
```

## Change rules

Do not introduce references from Core to Application, Infrastructure, Domain orchestration, or UI projects. Keep helpers deterministic and dependency-light.

## Related docs

- `docs/architecture/module-map.md`
- `docs/developer/build-test-run.md`
- `docs/source/generated/source-module-index.md`
