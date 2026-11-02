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

## Audit hardening (2026-11)

Each manual line is capped at the amount requested, the cash still unallocated, and the bucket's
outstanding balance. The shared `DirectLendingPaymentResolver` preserves that allocation and the
automatic waterfall, then routes only the residual under the canonical `ExcessPaymentApplication`
term. A null rule means `HoldUnapplied` and preserves the legacy terms hash. Permitted excess
principal prepayment reduces drawdown lots earliest first; prohibited excess prepayment stays
unapplied with a note. A configured penalty is assessed automatically on the excess principal
actually applied, after receipt allocations, as a separate event in the command's atomic write.

Every receipt is represented by receivable/principal allocations and retained unapplied-cash or
prepaid-interest liabilities. Prepaid-interest receipt lots retain their receipt identity, date,
original amount, remaining balance, and canonical terms hash. Subsequent cash-interest accruals
consume those balances without an additional contractual election or maturity cap, preserving
gross accrual and exact consumption evidence. PIK, fees, and penalties do not consume prepaid
interest. The implementation retains a strict later-date boundary; same-day application remains
an explicit Accounting review item. Amendments cannot rewrite an existing receipt's terms
evidence, and all other aggregate transitions preserve retained cash balances and receipt lots.

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
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj /p:FSharpTestSlice=DirectLending --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.DirectLending.Tests/Meridian.DirectLending.Tests.csproj --logger "console;verbosity=normal"
```

## Change rules

Preserve accounting evidence and test direct-lending calculations with scenario data.

## Related docs

- `docs/source/generated/source-roadmap-traceability.md`
- `docs/status/contract-compatibility-matrix.md`

Contract activation and amendment preserve collateral, unamortized discount/premium and the PIK
toggle. The SCH-DEF-008 characterization asserts that preservation. Existing schedule formulas
remain unchanged; scheduled PIK/resets still require an explicit persisted schedule/fixing policy and
versioned formula/residual state rather than assumptions based on payment frequency.
