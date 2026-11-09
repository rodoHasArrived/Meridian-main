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

- **Manual payment breakdowns.** Each manual line is capped at the amount requested, the cash still unallocated, and the bucket's outstanding balance, so a breakdown can never book interest, fee, penalty or principal that is not owed. Excess is reported as `UnappliedAmount`, as in the automatic waterfall. Term-driven handling of that excess (principal prepayment, prepaid interest, tracked unapplied cash) is tracked as `PRD-115`.
- **PRD-115 excess routing.** `applyMixedPayment` routes the cash left after allocation by the loan's `ExcessPaymentApplication` rule (`routeExcess`): unapplied cash by default; principal earliest lot first, with the prepayment penalty charged in the same event, or held with a note when prepayment is not allowed or principal is fully paid; or prepaid interest. Excess gets its own allocation rows. `DirectLendingExcessRoutingTests` covers the rule matrix.
- **PRD-115 balances.** Every servicing command that rebuilds `OutstandingBalancesDto` passes `PrepaidInterest` and `UnappliedCash` through by name. A positional rebuild would silently reset them to the 0 default, which `Every servicing command carries prepaid interest and unapplied cash through unchanged` guards against.

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
toggle. The SCH-DEF-008 characterization now asserts that preservation. No servicing formula or event
shape changes; scheduled PIK/resets still require an explicit persisted schedule/fixing policy and
versioned formula/residual state rather than assumptions based on payment frequency.

That persisted policy now exists as opt-in terms (2026-11-05): `PikCapitalization` (PIK share,
cadence, anchor) and `RateResetSchedule` (index, cadence, lookback, declared-unit spread/floor/cap).
`ServicingSchedule.fs` (`DirectLendingServicingSchedule`) owns the date rules; `ServicingAggregate`
adds `capitalizePik` and `applyScheduledRateReset`, and `postDailyAccrual` defers the PIK share to
`PikInterestAccruedUnpaid` when a policy is present. Loans without the new terms keep the legacy
toggle behaviour and their terms hashes. Formula one and SCH-DEF-006 are unchanged. See
[the blueprint](../../docs/engineering/blueprints/direct-lending-pik-capitalization-and-rate-resets.md).
