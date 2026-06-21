---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-STRATEGIES
path: src/Meridian.Strategies
status: active
owner_lane: Strategy Analytics
last_reviewed: 2026-06-06
---

# src/Meridian.Strategies

## Purpose

Strategies owns strategy lifecycle, run storage, promotion records, strategy read services, and
strategy-facing reconciliation governance.

## Layer responsibility

This layer should preserve strategy lineage from research through paper validation and promotion review.
Promotion evaluation and designer warnings use operator promotion-review wording while retained
policy outcome names and legacy cell kinds remain compatibility inputs.

## Key folders and files

- `Interfaces/` - strategy lifecycle contracts.
- `Models/` and `Serialization/` - strategy run payloads and serialization support.
- `Promotions/` - promotion evidence and review records.
- `Services/` and `Storage/` - strategy read, persistence, reconciliation governance, and
  promotion support services.

## Important workflows

Use this module for strategy run evidence, promotion lineage, and research-to-paper continuity.
`LedgerReadService` projects strategy-run trial balance and journal rows with canonical
`LedgerDimensionSetDto` scope for fund, strategy, portfolio, book, account, entity, sleeve, and
`externalGl.*` run-parameter filters so workstation ledger drill-throughs stay aligned with
accounting dimensions instead of a strategy-only scope vocabulary.
Paper-to-live promotion requires the live approval checklist, explicit evidence references for
each live checklist item, and an active `AllowLivePromotion` manual override. Approved and
execution-control-blocked live promotion attempts are written to the durable execution audit
trail with source run, target mode, required override kind, checklist count, evidence reference
count, and control rejection evidence so operations review can trace human approval gates even
when no live run is created.
Reconciliation also projects Security Master accounting inputs for Operations Continuity: fixed
coupon accruals, expected journal previews, and factor-schedule principal paydowns are generated
from resolved Security Master economic definitions before ledger/reconciliation gate posture is
reported. Factor-based instruments distinguish missing schedules from stale prior-period factor
evidence so accounting operations can route principal-paydown blockers precisely. The real
Security Master adapter preserves mortgage-backed, asset-backed, and amortizing-loan asset classes
when normalizing economic definitions so factor paydowns stay principal events instead of being
collapsed into generic unsupported instruments.
`GovernanceExceptionService` classifies ledger reconciliation breaks into strategy-governance
exception severities and dashboard projections from this module instead of the Application layer.
The shared reconciliation break queue also enforces v0.18 reviewed-automation boundaries: assistant
or automation-origin commands may assist triage, comments, and evidence gathering, but resolve,
sign-off, dismiss, and privileged reopen paths fail closed with a retained `MaterialActionDenied`
audit event before case state changes.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-STRATEGIES -->
| Roadmap item | Title |
| --- | --- |
| `W2-PROMO-001` | Paper promotion evidence and operator acceptance |
| `W3-CONT-001` | Research to paper continuity |
| `W6-BTSTUDIO-001` | Backtesting studio evidence loop |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-STRATEGIES -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
```

## Change rules

Preserve evidence lineage and avoid breaking promotion compatibility across browser and desktop workstation consumers.

## Related docs

- `docs/plans/waves-2-4-operator-readiness-addendum.md`
- `docs/source/generated/source-roadmap-traceability.md`
