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

Portfolio position read models carry shared mark-freshness assessments. A position snapshot timestamp is not a mark observation date: when historical mark provenance is absent, the projection reports review required rather than treating the snapshot as current valuation evidence.

## Reconciliation source lineage

The durable break queue retains successful complete statement comparisons separately from governed
casework. Source lineage is bound to tenant, company, fund profile, fund account, ledger book,
accounting period, institution, source account, mapping and retained matcher/tolerance policy fingerprint. Amounts, dates,
import IDs and row ordinals do not establish lineage. New and aging observations share an occurrence;
a break reappearing after an observed clearing creates another occurrence under the same lineage.
Comparisons require immutable evidence of available internal populations and identical represented population kinds. Old or equally dated successful runs retain a non-comparable receipt without blocking intake or replacing the newer comparison head.
Run receipts and observation metadata commit in the queue's integrity-checked atomic snapshot and
survive restart. Failed, partial, older, ambiguous or differently scoped comparisons cannot clear
prior observations. Source clearing leaves casework disposition, approval and close blockers intact.
Create, save, and legacy rekey intake enforce the same simulation-provenance gate. Saves cannot
remove a retained non-real mark by changing source labels, and rekeys preserve the strongest
explicit provenance from both records. Create retries compare normalized provenance with the
retained creation payload, so a changed origin conflicts instead of replaying an unmarked case.
New rekeys bind the original admitted request hash in their audit event, preserving exact retries
after restart even when the retained record contributes a stronger provenance mark.

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
Live and paper metrics retain the worst currency decline and worst percentage decline independently;
Calmar and promotion gates use the percentage maximum even when a later, larger currency loss has a
smaller percentage impact. Backtest metrics use the same convention and include initial capital as
the first equity peak.
Unavailable live-summary XIRR is retained as JSON null and restored to its SDK NaN marker, so terminal
metrics survive durable replay. The exact legacy XIRR string `"NaN"` remains readable, while new writes
use null. Defined positive-infinity outcomes for profit factor and Sortino retain their scoped SDK
named-value contract; unrelated metric and input fields retain strict finite-number handling.
`StrategyRunEntry.FundAccountId` retains the authorized execution account independently of fund
profile and financial-account labels. Promotion copies it to the target, durable scoped runs bind it
in the v5 input hash, and replay and lifecycle updates reject account mutation or removal. Production
callers create scoped runs with `StrategyRunEntry.StartForFundAccount`, passing the authorized account
explicitly; the factory computes the v5 hash together with optional evidence and execution realism.
The established `Start` and `StartWithEvidence` overloads retain their unscoped behavior and CLR
signatures. Advanced direct-constructor callers must supply the account-bound hash or leave the hash
unset for the durable store to compute.
Live launch defers when the retained account is absent or empty. Strategy orders carry the retained
account into the production OMS and its account-scoped W7 readiness gate, including after host restart;
run parameters cannot replace it. Legacy unscoped histories continue to replay without acquiring an
execution account.
Scoped recovery attempts keep the account-bound v5 hash, and live recovery compares the retained
account with the originally approved run before external restart. Restored paused sessions retain
that same account and input hash through explicit resume.
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

`LiveStrategyBase` applies the shared F# lifecycle policy and serializes complete asynchronous
operations. Queued commands are evaluated after the prior operation finishes. Warmup must complete
before running, paused starts resume without replacing the retained execution context, and cleanup
must complete before stopped state is exposed. Hook failure or cancellation preserves a fault
reason and requires successful cleanup before restart. `Stopping` is appended to `StrategyStatus`
so existing persisted enum values stay stable. The manager verifies `Running`, `Paused`, or
`Stopped` after the corresponding adapter method returns; a mismatch retains a failed lifecycle
receipt instead of success. Paused resume keeps the same run and attempt through
`StartRequested` → `Resumed`; changing execution mode requires the promotion workflow.
Stopped recovery keeps datasets, feeds, engine, parameters, portfolio/ledger/audit/fund scope,
execution realism, evidence declarations, and the promotion parent, while resetting outcome and
output fields. It retains the original correlation and immediate recovery parent. Live recovery
and resume revalidate retained promotion approval and current routing, override, and breaker
controls. Cleanup after failure or cancellation preserves its original identity and end timestamp.
See `src/Meridian.FSharp.Trading/README.md` for transition semantics.

The live engine and lifecycle manager enforce one active instance per strategy, including launches
with different parameters or execution modes. Controls bind to that instance's exact run ID;
a newer deferred run cannot replace its history. Engine-owned operator stop uses the session's
shutdown path: close outbound order admission, drain admitted execution reports, wake an idle feed,
finish callbacks and cleanup, build metrics, and retain a verified completion receipt. The session
records `StopRequested` as `live-trading-engine` after fills drain; the manager delegates to it.
Concurrent stop callers share that work; cancellation ends a caller's wait while shutdown continues.
Paused engine sessions resume the same run. New runs launch through the engine after the prior session ends.
The engine records `Activated` after promotion, `Suspended` on host shutdown, and `Recovered`
when an interrupted session restarts. It never appends a second `Started` to the same run.
Host shutdown leaves clean runs open for resume, but cannot downgrade an explicit operator stop.
Startup restores the owned paused run without warmup or market callbacks. Explicit resume
(`POST /api/strategies/{strategyId}/resume?runId=<retained-run-id>`) uses its retained context and
returns a verified receipt; restored strategies warm up only when resumed. A second paused run
waits until the current owner stops before it can acquire the strategy's controls.
Cancellation before activation does not invoke finish or stop hooks on an unstarted strategy.
An operator stop at that stage closes the run's retained stop intent and saves zero-fill metrics;
it does not emit F# facts claiming that the strategy entered or completed its stopping state.
Cleanup failures retain a failed outcome and available metrics. Terminal append failures are checked
for an already committed event before retaining `EvidencePersistenceFailed`; failed cleanup remains
failed even when that warning is also needed. If neither record can be retained, shutdown propagates
the persistence failures rather than claiming completion.
Metrics retained after failed cleanup remain diagnostic evidence and cannot qualify a run for
promotion. Promotion evaluation and approval require successful retained completion; a warning
about missing terminal evidence must be reconciled first. External stopped state alone cannot
reconcile away a retained session failure or cancellation.
Retrying failed external cleanup permits a new session once the strategy is stopped, while the
original session receipt and metrics remain failed diagnostic evidence.

F# `EmittedFacts` are consumed by `StrategyLifecycleFactMapping` as ordered evidence in each retained
snapshot's `LifecycleFacts`. Start/restart/resume request and warmup-entry facts map to `StartRequested`,
warmup-completion/running-entry to `Activated`, `Recovered`, `Resumed`, or legacy `Started`,
pause to `Paused`, stop-request/stopping-entry to
`StopRequested`, successful cleanup to `Completed`, and fault facts to the corresponding failed or
cancelled event. Facts describe the policy verdict associated with that intent or verified outcome;
consumers replay the retained lifecycle events and never execute a fact as a command. The fallback
persistence warning preserves its original facts. Retry the unchanged snapshot so its event identity,
fact order, and append idempotency remain stable. Legacy snapshots have an empty fact list; replay
recognizes unchanged typed retries without rewriting their retained bytes or rolling back the latest
projection, and terminal retries return the original retained receipt. Retry identity includes the
exact retained input hash, account, execution realism, and ordered facts. Unknown or mismatched
facts are rejected. Compatibility tests cover C# enum/F# state names, every bridge
transition, fact mapping, retained ordering, and append retries.

Durable metric snapshots write unavailable XIRR as JSON null and restore the SDK's `NaN` sentinel
on read. The exact legacy XIRR string `"NaN"` remains readable; infinite XIRR values remain invalid.
Profit factor and Sortino preserve their defined positive-infinity outcomes through their scoped
SDK attributes, while unrelated nonfinite metrics remain invalid. General SDK and comparison API
JSON serialization retain the named XIRR marker contract; the null override applies only to durable
strategy snapshots.

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
Promotion requires an explicitly successful completed lifecycle; an end timestamp and qualifying
metrics on a failed or cancelled attempt never establish eligibility. `PromotionReadiness.isTerminal`
continues to classify terminal lifecycle states independently of eligibility.
Paper-to-live promotion requires the live approval checklist, explicit evidence references for
each live checklist item, and an active `AllowLivePromotion` manual override. The live checklist
includes paper-validation, reconciliation, accounting-record, governed-reporting, governance
sign-off, exception-handling, rollback or kill-switch, audit-retention, and broker execution
reconciliation evidence before a live readiness claim can create a live run. Live evidence
references must use `TOKEN:retained-evidence` rather than bare checklist tokens, and the
`LIVE_OVERRIDE_REVIEWED` reference must name the active manual override id. Required trust freshness
and completeness gates independently block promotion. The trust provider reads the DK1 packet
referenced by the source run's retained evidence and binds the assessment to its run ID and input
hash. The packet must be complete, observed in the past, and unexpired; its default maximum age is
24 hours (`Dk1TrustGateReadinessOptions.PromotionEvidenceMaximumAge`). Missing or mismatched trust
evidence fails closed; an operator-controls snapshot cannot substitute for it. Approved,
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
Retained reconciliation writes serialize first-observation continuity across workstation processes,
use repository commit order when concurrent runs share one timestamp, and expose a leased latest
snapshot callback for governance decisions. Lease callbacks are non-reentrant and fail fast if they
try to call the same backing repository. Governance holds that lease through its audit append,
rejects impossible break chronology and invalid policy thresholds, repairs a torn final JSONL record
before a durable append, and keeps the established PascalCase JSON evidence-export contract.
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

- [`archive/docs/plans/waves-2-4-operator-readiness-addendum.md`](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/plans/waves-2-4-operator-readiness-addendum.md)
- `docs/source/generated/source-roadmap-traceability.md`
