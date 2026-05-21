---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-APP
path: src/Meridian.Application
status: active
owner_lane: Runtime Host
last_reviewed: 2026-05-20
---

# src/Meridian.Application

## Purpose

The application layer coordinates Meridian use cases, command handlers, orchestration services, and pipeline workflows.

## Layer responsibility

This layer should express application behavior and orchestration without owning shared DTO definitions, UI rendering, provider adapter implementation, or storage engine internals.

## Key folders and files

- `Commands/` - CLI and operator command implementations.
- `Composition/` - application service registration.
- Pipeline and workflow services - runtime coordination across lower-level contracts and infrastructure.

## Important workflows

Use this module for workflow orchestration, command behavior, readiness coordination, and service-level validation.
Operations Continuity workflow orchestration lives under `OperationsContinuity/`; keep gate posture,
approval blockers, ledger-posting safeguards, audit writes, and server-side status derivation in
that application-layer aggregate/service rather than workstation clients.
Runtime diagnostics live under `Services/` and `Monitoring/`; diagnostic bundle generation should
export sanitized summaries, metrics, and recent tracked errors without raw provider payloads,
credentials, account identifiers, or portfolio/trade detail.
Shutdown lifecycle coordination in `GracefulShutdownService` and `GracefulShutdownHandler` should
keep structured operation names, correlation IDs, elapsed timings, recovery actions, and sanitized
failure reasons together so operators can diagnose incomplete flushes, duplicate shutdown requests,
and disposal failures without exposing secrets.
`ShutdownDiagnosticsService` owns the latest in-process shutdown-sequence support snapshot consumed
by diagnostic bundles and diagnostics endpoints. Keep that snapshot low-cardinality and sanitized:
correlation ID, status, reason, timings, incomplete flush count, warning count, short warning
summary, component counts, and duplicate-request count only.

## Diagrams

See `DIA-ASSURANCE-LOOP` and paper-readiness diagrams in `docs/source/data/diagram-index.yml`.

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

Respect layer boundaries: application code can depend on lower layers and contracts, but domain, core, and contracts must not depend back on application code.

## Related docs

- `docs/architecture/module-map.md`
- `docs/HELP.md`
- `docs/source/generated/source-roadmap-traceability.md`
