---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-HOST
path: src/Meridian
status: active
owner_lane: Runtime Host
last_reviewed: 2026-05-28
---

# src/Meridian

## Purpose

Meridian host is the application host, CLI entrypoint, and runtime composition root.

## Layer responsibility

This module owns process startup, dependency-injection composition, configuration bootstrap, CLI
routing, and local API hosting. Keep startup orchestration and process-host concerns isolated from
domain and application logic.

## Key folders and files

- `Meridian.csproj` - primary host project.
- `Program.cs` and host startup files - runtime composition and CLI entry.
- `Properties/` - launch and host metadata.

## Important workflows

Use this module when changing host startup, CLI routing, runtime hosting, configuration bootstrap,
or local workstation API process behavior.

Hosted brokerage composition registers concrete Alpaca and Interactive Brokers gateways by keyed
runtime ID (`alpaca`, `ib`, `ibkr`) and registers StockSharp only when the connector runtime type is
present. `HostedBrokerageGatewayRuntimeSurfaceCatalog` reports the hosted registration surface for
Alpaca, Interactive Brokers, the `ibkr` alias, and optional StockSharp: concrete gateway type,
declared gateway id, runtime-key match status, account/portfolio/activity sync support,
order-modification and partial-fill capability, supported asset classes, validation issues, and
missing-runtime notes. This is offline DI/runtime surface validation; it does not connect to live
broker APIs or prove credentialed trading readiness. Do not add placeholder StockSharp services
when the connector package is absent.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-HOST -->
| Roadmap item | Title |
| --- | --- |
| `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W7-LIVE-001` | Live-readiness governance |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-HOST -->
| TODO | Title | Status | Priority |
| --- | --- | --- | --- |
| `TODO-SRC-HOST-001` | Complete W7 live-readiness paper-first guard enforcement | open | medium |
<!-- source-todos:end -->

## Validation

```bash
python3 build/python/cli/buildctl.py build --project src/Meridian/Meridian.csproj --configuration Debug --isolation-key codex-host
```

## Change rules

Do not move business rules, provider implementation details, or UI-specific behavior into the host.
Route orchestration changes through `src/Meridian.Application` and shared contracts through
`src/Meridian.Contracts`.

## Related docs

- `docs/developer/build-test-run.md`
- `docs/architecture/module-map.md`
- `docs/source/generated/source-module-index.md`
