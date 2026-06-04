---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-FSHARP-DIRECTLENDING
path: src/Meridian.FSharp.DirectLending.Aggregates
status: active
owner_lane: Accounting and Ledger
last_reviewed: 2026-05-20
---

# src/Meridian.FSharp.DirectLending.Aggregates

## Purpose

This module contains functional aggregate calculations for direct lending and fund-structure workflows.

## Layer responsibility

It should keep direct-lending aggregate logic isolated from UI and application orchestration while remaining consumable by accounting and reporting workflows.
Direct lending is modeled as private-credit depth inside the Security Master storage lane, not as
an independent instrument master. Runtime persistence defaults to the Security Master connection
and schema; direct-lending-specific connection variables are legacy overrides for isolated test
databases or controlled migration windows.

## Key folders and files

- `Meridian.FSharp.DirectLending.Aggregates.fsproj` - direct-lending aggregate project boundary.
- F# modules for fund and lending aggregate calculations.

## Important workflows

Use this module for direct-lending calculation changes and fund-structure aggregate behavior.
When changing persistence-facing behavior, keep loan state, accrual, cash-flow, workflow audit,
and servicer-report projections tied to Security Master identity and lineage so loan planning,
servicing, and accounting evidence can reconcile through the same instrument source of truth.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-FSHARP-DIRECTLENDING -->
| Roadmap item | Title |
| --- | --- |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W4-RPT-001` | Governed report pack readiness |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-FSHARP-DIRECTLENDING -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.DirectLending.Tests/Meridian.DirectLending.Tests.csproj --logger "console;verbosity=normal"
```

## Change rules

Preserve accounting evidence and test direct-lending calculations with scenario data.

## Related docs

- `docs/source/generated/source-roadmap-traceability.md`
- `docs/status/contract-compatibility-matrix.md`
