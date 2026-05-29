---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-EXECUTION
path: src/Meridian.Execution
status: active
owner_lane: Execution and Fund Accounts
last_reviewed: 2026-05-28
---

# src/Meridian.Execution

## Purpose

Execution owns order routing, execution services, allocation, margin, derivatives, tax-lot, and broker gateway behavior.

## Layer responsibility

This layer implements execution behavior and broker-facing runtime services while preserving paper-first and evidence-backed gating rules.

## Key folders and files

- `Interfaces/` - order gateway and execution abstractions.
- `Services/` - execution services and orchestration.
- `Models/`, `Events/`, and `Serialization/` - execution payloads and event contracts.
- `Allocation/`, `Margin/`, `Derivatives/`, and `TaxLotAccounting/` - execution support domains.

## Important workflows

Use this module for paper session execution, broker gateway behavior, order lifecycle, and execution evidence.
Ledger posting from trade-fill events is Security Master gated: postings require a configured
validation gate, resolved Security Master identity, non-blocked validation, and journal metadata
that preserves the Security Master ID, fill ID, symbol, and gate evidence for provenance.
Live execution controls include persisted circuit-breaker state, position limits, and manual
overrides. Run-scoped manual overrides are matched against order `runId` metadata, and submitted
orders that use an override carry the applied override ID, run/strategy/symbol scope, and control
decision metadata in the execution audit trail. Orders rejected by operator controls carry stable
reject codes such as `CIRCUIT_BREAKER_OPEN`, `POSITION_LIMIT_EXCEEDED`, or `MANUAL_FORCE_BLOCK`
plus the same run/strategy/symbol audit scope so operations can distinguish policy failures from
broker failures during review. The OMS also records durable audit outcomes for
submitted, rejected, cancelled, cancel-rejected, modified, and modify-rejected order lifecycle
events with broker, order, symbol, scope, reject reason, and operation metadata for operations
review. Shared `/api/execution/controls/*` endpoints expose the snapshot plus secured mutations for
the global circuit breaker, default position limit, symbol position limits, and manual override
create/clear actions so browser and desktop clients do not need client-local execution-control
state.

## Diagrams

See `DIA-PAPER-SESSION-REPLAY` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-EXECUTION -->
| Roadmap item | Title |
| --- | --- |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W2-PROMO-001` | Paper promotion evidence and operator acceptance |
| `W6-LIVE-001` | Live-readiness governance |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-EXECUTION -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
```

## Change rules

Keep live actions explicitly gated and paper-first unless roadmap acceptance evidence allows broader scope.

## Related docs

- `docs/plans/paper-trading-cockpit-reliability-sprint.md`
- `docs/source/generated/source-roadmap-traceability.md`
