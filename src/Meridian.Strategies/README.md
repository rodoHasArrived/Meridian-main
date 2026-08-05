---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-STRATEGIES
path: src/Meridian.Strategies
status: active
owner_lane: Strategy Analytics
last_reviewed: 2026-08-03
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
Production `StrategyRunStore` composition replays a versioned, source-generated run snapshot from
the shared Contracts-owned operational case-history port. Start, pause, and stop commands retain
intent before invoking the external strategy, then return a validated Succeeded,
CompletedWithWarnings, Failed, or Blocked receipt with recovery guidance. Transient final-evidence
failures retain a warning and can be reconciled against the already-completed external state without
repeating the action. Lifecycle snapshots retain deterministic input hashes, actors, correlation and
attempt lineage, exception details, approvals, evidence, artifacts, legal transition order, and
monotonic timestamps. Version-two input hashes bind parent-run lineage, portfolio, ledger, audit,
and fund-profile scope as well as datasets, feeds, engines, and ordered parameters; replay accepts
verified legacy hashes only for an explicit one-time upgrade and rejects later scope mutation. The
parameterless store remains an explicit in-memory compatibility seam for
isolated tests; browser and WPF production composition and the desktop fallback use the same
data-root-backed durable history store.
`StrategyRunEntry` retains the W6 Backtest Studio evidence loop: operator acceptance criteria,
retained evidence links, accounting-record references, approval references, paper-validation
lineage, and governed-report references are stored with the run so downstream review surfaces do
not need to infer backtest acceptance from dashboard-only state.
Scoped Covered Call runs are visible only through exact tenant/company repository reads. Their
Backtest-to-Paper checklist is projected from the durable promotion record and exact retained Paper
child lineage, not from eligibility or declaration presence: all four canonical Paper checklist
ids require an operator, audit reference, decision time, keyed source-run evidence, and a same-scope
Paper target whose parent and strategy identities match. Unscoped APIs remain limited to genuinely
legacy, non-Covered-Call records.
Promotion decisions are first-decision-wins per source run and target mode. Sequential retries and
concurrent requests across independent hosts targeting the same JSONL authority reuse the original
approved or rejected record under a cross-process authority lease; a conflicting later transition
cannot create another target, append another decision, or repeat launcher and audit side effects.
An approval is retained and audited before its target becomes runnable. If target persistence fails,
a retry repairs the exact retained target id under the same authority lease, while an unconfirmed
decision write leaves no target for the startup resume sweep to activate.
Completed runs may receive a subsequent append-only walk-forward evidence snapshot when the
snapshot changes only that evidence; durable replay preserves the completed lifecycle state while
allowing paper-to-live promotion to evaluate the newly retained out-of-sample evidence.
`LedgerReadService` projects strategy-run trial balance and journal rows with canonical
`LedgerDimensionSetDto` scope for fund, strategy, portfolio, book, account, entity, sleeve,
organization, customer, vendor, project, and `externalGl.*` run-parameter filters so workstation
ledger drill-throughs stay aligned with accounting dimensions instead of a strategy-only scope
vocabulary.
Paper-to-live promotion requires the live approval checklist, explicit evidence references for
each live checklist item, and an active `AllowLivePromotion` manual override. The live checklist
includes paper-validation, reconciliation, accounting-record, governed-reporting, governance
sign-off, exception-handling, rollback or kill-switch, audit-retention, and broker execution
reconciliation evidence before a live readiness claim can create a live run. Live evidence
references must use `TOKEN:retained-evidence` rather than bare checklist tokens, and the
`LIVE_OVERRIDE_REVIEWED` reference must name the active manual override id. Approved,
checklist-blocked, missing-evidence, invalid-evidence, and execution-control-blocked live promotion
attempts are written to the durable execution audit trail with source run, target mode, required
override kind, checklist count, evidence reference count, and control rejection evidence so
operations review can trace human approval gates even when no live run is created.
Reconciliation also projects Security Master accounting inputs for Operations Continuity: fixed
coupon accruals, expected journal previews, and factor-schedule principal paydowns are generated
from resolved Security Master economic definitions before ledger/reconciliation gate posture is
reported. Factor-based instruments distinguish missing schedules from stale prior-period factor
evidence so accounting operations can route principal-paydown blockers precisely. The real
Security Master adapter preserves mortgage-backed, asset-backed, and amortizing-loan asset classes
when normalizing economic definitions so factor paydowns stay principal events instead of being
collapsed into generic unsupported instruments.
External-statement input is an optional reconciliation source. When no provider is configured, the
production-safe null source contributes no statement rows; retained reconciliation run storage and
the configured portfolio, ledger, and banking inputs remain authoritative.
Factor rows retain their evidence link and source-content hash, and the Security Master accounting
event service delegates factor math and deterministic event identity to Instruments
`FactorPaydownProjectionService`. Reconciliation run ids no longer participate in factor event
identity; missing factor evidence blocks the expected event instead of producing an unverifiable
posting preview.
For factor-bearing definitions, the production source adapter resolves a single effective Asset
Operations book position by Security Master identity and account, carries its durable id/version,
and fails the factor event closed when that position cannot be resolved. Multiple factor rows advance
their expected position versions sequentially instead of reusing one optimistic-concurrency token.
`GovernanceExceptionService` classifies ledger reconciliation breaks into strategy-governance
exception severities and dashboard projections from this module instead of the Application layer.
The shared reconciliation break queue also enforces v0.18 reviewed-automation boundaries: assistant
or automation-origin commands may assist triage, comments, and evidence gathering, but resolve,
sign-off, dismiss, and privileged reopen paths fail closed with a retained `MaterialActionDenied`
audit event before case state changes.
Break records carry explicit Value, Quantity, and CostBasis comparisons. A source that cannot
provide a measure retains an unavailable reason instead of substituting zero. Governed casework
supports assign, resolve, waive, and supersede dispositions with evidence hashes, independent
approval for material dispositions, successor lineage, idempotency, and append-only audit history.
The file-backed queue exposes a separate startup authority probe for production Reporting
readiness. The probe discards process cache, reloads the retained snapshot, and reruns envelope,
snapshot, audit-chain, and close-scope integrity checks. A failed probe clears the attempted cache
again, so repeated checks remain fail-closed until the durable snapshot is actually repaired; it
does not silently accept the last in-memory state.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-STRATEGIES -->
| Roadmap item | Title |
| --- | --- |
| `W2-PROMO-001` | Paper promotion evidence and operator acceptance |
| `W3-CONT-001` | Research to paper continuity |
| `W6-BTSTUDIO-001` | Backtesting studio evidence loop |
| `W7-LIVE-001` | Live-readiness governance |
| `W10-RECON-001` | Durable break lineage identity and run-over-run break diff |
| `W10-RECON-002` | Break clustering and bulk-resolution activation |
| `W10-RECON-004` | Operator-taught match rules with promotion gate |
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

- `archive/docs/plans/waves-2-4-operator-readiness-addendum.md`
- `docs/source/generated/source-roadmap-traceability.md`
