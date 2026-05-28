---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-PROVIDER-SDK
path: src/Meridian.ProviderSdk
status: active
owner_lane: Data Confidence and Validation
last_reviewed: 2026-05-20
---

# src/Meridian.ProviderSdk

## Purpose

ProviderSdk defines provider-facing abstractions for streaming, historical, and symbol-search integrations.

## Layer responsibility

This layer is the plugin contract for provider adapters. It should expose stable interfaces without owning concrete provider implementation details.

## Key folders and files

- Provider-facing interfaces such as market data client contracts.
- Shared provider metadata and attribute types.

## Important workflows

Use this module when a provider abstraction must be consumed by multiple adapters or higher-level services.
Provider routing capabilities are contract-level workflow gates; `FactorSchedule` is distinct from
generic `CorporateActions` so accounting workflows can degrade fixed-income, structured-credit,
amortization, and paydown evidence when a provider cannot route the required factor/coupon feed.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-PROVIDER-SDK -->
| Roadmap item | Title |
| --- | --- |
| `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `W6-LIVE-001` | Live-readiness governance |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-PROVIDER-SDK -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
```

## Change rules

Keep provider abstractions stable and implementation-neutral. Concrete adapters belong in Infrastructure.

## Related docs

- `docs/providers/`
- `docs/architecture/module-map.md`
- `docs/source/generated/source-module-index.md`
