---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-CORE
path: src/Meridian.Core
status: active
owner_lane: Runtime Host
last_reviewed: 2026-06-07
---

# src/Meridian.Core

## Purpose

Core contains cross-cutting primitives used throughout Meridian: configuration, validation,
exceptions, logging, monitoring, scheduling, serialization, redaction, masking, and pipeline
helpers.

## Layer responsibility

This layer provides low-level reusable infrastructure. It must stay independent of application orchestration, provider implementations, UI services, and domain-specific workflows.

## Key folders and files

- `Config/` - shared configuration models, JSON serializer options, JSON Schema generation,
  FluentValidation rules, validation pipeline stages, credential placeholder detection, default
  config-path resolution, environment overrides, configuration templates, config file hot-reload
  watching, and sensitive-value masking primitives published under `Meridian.Core.Config`.
- `Diagnostics/` - low-level runtime redaction helpers used by diagnostic bundles, endpoints, and
  shutdown flows without depending on Application services.
- `Exceptions/` - base exception and error types published under `Meridian.Core.Exceptions`.
- `Serialization/` - source-generated JSON context support published under
  `Meridian.Core.Serialization`.
- `Pipeline/`, `Scheduling/`, `Services/`, and `Monitoring/` - reusable runtime primitives,
  including shared alert/health-check monitoring contracts, the `IFlushable` shutdown/flush
  contract, and optional flush queue-diagnostics contract used by storage sinks, Platform runtime
  shutdown handlers, and Application pipeline components.

## Important workflows

Use this module when a cross-project primitive or runtime helper is required by multiple higher layers.
Runtime feature-capability options live in `Config/FeatureCapabilityOptions.cs` so desktop and host
configuration loading can bind the same `FeatureCapabilities` section without introducing UI
references into Core.
`Config/AppConfigJsonOptions.cs`, `Config/ConfigJsonSchemaGenerator.cs`,
`Config/ConfigValidationHelper.cs`, `Config/IConfigValidator.cs`,
`Config/CredentialPlaceholderDetector.cs`, `Config/DefaultConfigPathResolver.cs`,
`Config/ConfigEnvironmentOverride.cs`, `Config/ConfigTemplateGenerator.cs`, and
`Config/ConfigWatcher.cs` provide shared
configuration JSON options, JSON Schema generation, FluentValidation-based AppConfig validation,
validation pipeline stages, credential placeholder detection, default config-path resolution,
environment-variable override handling, configuration-template generation, and debounced config
file hot-reload watching for Application commands, configuration services, WPF setup flows, and
shared endpoints.
These Core configuration records and helpers use the `Meridian.Core.Config` namespace; Application
keeps only configuration orchestration, credential testing, and deployment/startup adapters.
Core exception, logging, pipeline-policy, subscription model, serialization, and monitoring helper
families live under `Meridian.Core.Exceptions`, `Meridian.Core.Logging`,
`Meridian.Core.Pipeline`, `Meridian.Core.Subscriptions.Models`, `Meridian.Core.Serialization`,
and `Meridian.Core.Monitoring` / `Meridian.Core.Monitoring.Core` respectively.
`Config/SensitiveValueMasker.cs` and `Diagnostics/RuntimeDiagnosticRedactor.cs` provide the shared
redaction baseline for support bundles, endpoint payloads, and runtime logs so UI/shared and
Application consumers do not carry local masking helpers.
`Services/IFlushable.cs` is the shared flush contract for buffered components and optional queue
diagnostics. Storage sinks, Application pipeline components, and Platform runtime shutdown handlers
consume these Core-owned contracts so durable persistence layers do not depend on Application
service namespaces.
`Serialization/MarketDataJsonContext.cs` and `Serialization/SecurityMasterJsonContext.cs` include
source-generated metadata for market-data events, Security Master validation, reference-data,
custom asset profile, custom profile governance, and Passport Workbench governed-write DTOs so
shared services can serialize those payloads without reflection-based fallback.

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
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~Core.Config|FullyQualifiedName~ConfigurationUnificationTests|FullyQualifiedName~ConfigValidatorCliTests|FullyQualifiedName~ConfigurationServiceTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
```

## Change rules

Do not introduce references from Core to Application, Infrastructure, Domain orchestration, or UI projects. Keep helpers deterministic and dependency-light.

## Related docs

- `docs/architecture/module-map.md`
- `docs/developer/build-test-run.md`
- `docs/source/generated/source-module-index.md`
