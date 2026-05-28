---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-EXECUTION
path: src/Meridian.Execution
status: active
owner_lane: Execution and Fund Accounts
last_reviewed: 2026-05-20
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
