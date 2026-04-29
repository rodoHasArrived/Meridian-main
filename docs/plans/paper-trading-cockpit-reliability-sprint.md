# Paper Trading Cockpit Reliability Sprint

**Last Reviewed:** 2026-04-29

## Summary

Wave 2 should treat the current paper-trading cockpit as a reliability sprint, not a net-new feature sprint.
The repo already contains the main seams needed for an operator lane:

- `src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs` exposes sessions, replay verification, audit, and execution controls.
- `src/Meridian.Execution/Services/PaperSessionPersistenceService.cs` persists session metadata, fills, order history, and ledger journals, and can verify replay continuity.
- `src/Meridian.Execution/Services/ExecutionAuditTrailService.cs` provides durable execution audit storage backed by WAL.
- `src/Meridian.Execution/OrderManagementSystem.cs` audits order submission, rejection, and gateway-connect behavior.
- `src/Meridian.Ui.Shared/Endpoints/PromotionEndpoints.cs` and `src/Meridian.Strategies/Services/PromotionService.cs` expose evaluation, approval, and promotion history seams.
- `src/Meridian.Ui.Shared/Services/Dk1TrustGateReadinessService.cs` projects the latest generated DK1 parity packet into the cockpit readiness lane.
- `scripts/dev/prepare-dk1-operator-signoff.ps1 -PacketPath` now binds the operator sign-off template to the reviewed parity packet, so stale or copied approvals cannot satisfy DK1 exit preflight.
- `src/Meridian.Ui/dashboard/src/screens/trading-screen.tsx` already surfaces sessions, replay, audit, and promotion controls in one cockpit.
- `src/Meridian.Wpf/Views/TradingWorkspaceShellPage.xaml` now projects a Trading desk briefing hero from the same active-run, workflow-summary, and shared operator-readiness inputs.

The gap is not basic visibility. The gap is dependable operator proof:

- replay confidence is only partially exposed as a workflow gate
- session persistence is stronger in the service layer than in the cockpit acceptance story
- risk state is visible, but not yet fully explainable from audited control and policy decisions
- promotion traceability now has durable storage and cockpit operator fields, but approval and rejection paths must keep enforcing a complete operator, rationale, lineage, and audit-reference packet

This sprint closes that gap with four explicit acceptance gates:

1. replay confidence
2. session persistence
3. risk auditability
4. promotion traceability

## Scope

### In Scope

- harden the shared workstation paper-trading cockpit around paper-session create, restore, verify, close, and promotion review, with the web dashboard as the active operator lane and retained WPF/API surfaces as supporting consumers
- make replay verification an operator-facing readiness signal instead of a hidden service capability
- tighten order, control, and risk audit correlation so decisions can be explained from the cockpit
- make `Backtest -> Paper` traceability durable and make `Paper -> Live` readiness explicit without widening live-readiness claims
- add repo-backed tests that prove restart, replay, audit, and promotion continuity

### Out Of Scope

- new broker adapters or wider live-broker certification
- new research, portfolio, ledger, or governance feature families beyond what Wave 2 needs to prove the operator lane
- WPF shell redesign beyond consuming already-hardened shared seams
- WPF-only readiness semantics that diverge from `/api/workstation/trading/readiness` or the shared active-run/workflow services
- Wave 3 continuity work except where it is required for paper-cockpit traceability

### Assumptions

- Wave 1 remains the trust boundary for provider confidence and is not reopened here.
- Wave 2 is a shared workstation cockpit hardening effort: the web dashboard is now the active operator shell, and retained WPF/API surfaces should consume the same readiness, session, replay, control, audit, and promotion seams rather than defining separate readiness semantics.
- Later `Paper -> Live` work should only inherit controls from this sprint; it should not claim live readiness by default.

## Acceptance Gates

| Gate | Exit Signal | Repo Seams | Blocking Failure Modes |
| --- | --- | --- | --- |
| Replay confidence | Operators can verify a selected paper session and see whether replay matches current state after normal use and after restart. | `PaperSessionPersistenceService.VerifyReplayAsync`, `ExecutionEndpoints`, trading cockpit replay panel | replay mismatch with no surfaced reason; replay proves portfolio only while order-history or ledger-journal drift remains invisible; no durable audit of verification |
| Session persistence | A paper session can be created, restored after restart, verified, and closed without losing symbol scope, order history, or ledger continuity. | `PaperSessionPersistenceService.InitialiseAsync`, session endpoints, session detail DTOs | session metadata survives but order or ledger continuity does not; order updates are not durably awaited; restore path cannot prove continuity |
| Risk auditability | Every material order/control outcome is explainable by audited evidence visible from the cockpit. | `OrderManagementSystem`, `ExecutionAuditTrailService`, `ExecutionOperatorControlService`, trading-screen audit view | risk state only shown as summary copy; rejects or control blocks lack actor or scope; cockpit cannot see manual overrides or breaker state |
| Promotion traceability | Every promotion decision yields one durable trace chain from source run to target run when present, operator, rationale, override, decision state, and audit reference. | `PromotionService`, `PromotionEndpoints`, `StrategyRunEntry`, workstation run read models | request omits operator or rationale; rejection path is not audit-linked; source and target runs are linked but not reviewable as one chain |

## Architecture

### Current State

The codebase already has most of the needed primitives, but they are not yet assembled into a single operator gate:

- Session persistence is durable in `PaperSessionPersistenceService`, but the trading cockpit does not treat session continuity as a first-class readiness check.
- Replay verification compares current and replayed portfolio state, persisted order history, and the persisted ledger journal, so the readiness lane can block on ledger-sidecar drift instead of silently accepting portfolio-only continuity.
- Execution audit durability exists and order submissions can be audited, but the cockpit does not yet expose the control state that explains why risk decisions were allowed or blocked.
- Promotion approvals and rejections write durable JSONL promotion history through `IPromotionRecordStore`, and `PromotionService.GetPromotionHistoryAsync()` reloads history after restart.
- `docs/operations/live-execution-controls.md` documents manual-override flows, and `src/Meridian.Contracts/Api/UiApiRoutes.cs` now exposes the matching pluralized execution-control manual-override routes.
- The cockpit surfaces now send operator, approval/rejection rationale, review notes, and manual override IDs, and their acceptance cards should treat promotion review as ready only when the latest history record includes decision, operator, rationale, lineage, and audit reference.
- `WorkstationEndpoints` now routes both `/api/workstation/trading/readiness` and the embedded `/api/workstation/trading` readiness payload through `TradingOperatorReadinessService`; minimal endpoint hosts that omit the DI registration construct the same service from the request service provider instead of using an endpoint-local fallback builder.

### Target Shape

Wave 2 should converge on one operator lane with four visible checkpoints:

1. `Session active`
2. `Replay verified`
3. `Risk state explainable`
4. `Promotion review trace complete`

The cockpit should remain the orchestration surface, and WPF shell elements such as the Trading desk briefing hero should be treated as consumers of the same lane rather than separate acceptance surfaces. They should read from shared services rather than duplicate logic:

- session continuity from `PaperSessionPersistenceService`
- order and control evidence from `ExecutionAuditTrailService` and `ExecutionOperatorControlService`
- promotion readiness and linkage from `PromotionService` plus `StrategyRunReadService`

### Delivery Slices

#### Slice 1: Session continuity and replay gate

- make replay verification a named readiness block in the cockpit, not just a secondary detail panel
- extend replay verification to report the evidence compared, not just match or mismatch
- ensure session restore proves symbol scope, portfolio state, order history visibility, and ledger visibility after startup reload

#### Slice 2: Risk and control explainability

- surface execution-control state in the cockpit using the existing `/api/execution/controls` seam
- keep the implemented manual-override endpoints aligned with the documented operator flow
- attach enough metadata to order and control audits to explain who acted, on what scope, and why

#### Slice 3: Promotion trace chain

- make promotion approval requests carry full operator context
- keep durable promotion records as the acceptance source for cockpit readiness
- keep decision state, source run, target run when created, audit reference, rationale, operator, and manual override visible together in the trading and research surfaces

## Interfaces And Models

### Reuse And Extend

- extend `PaperSessionReplayVerificationDto` rather than inventing a second replay-status DTO
- extend `StrategyPromotionRecord` rather than creating a parallel promotion-history payload
- reuse `ExecutionControlSnapshot` and `ExecutionAuditEntry` for cockpit risk explainability
- keep `StrategyRunEntry.ParentRunId` and `StrategyRunEntry.AuditReference` as the base promotion-link seam

### Landed interfaces and remaining acceptance work

#### `TradingOperatorReadinessDto`

`GET /api/workstation/trading/readiness` is the shared acceptance-lane contract for Wave 2. It aggregates:

- active and historical paper session readiness
- latest replay verification evidence from the execution audit trail
- execution-control state, including circuit breaker and manual overrides
- durable promotion decision state and trace completeness
- DK1 provider trust-gate packet posture, sample/evidence counts, blockers, and operator sign-off status from the generated parity packet plus packet-bound sign-off validation
- DK1 pilot sample rows, evidence-document rows, trust-rationale contract status, and baseline-threshold contract status so explainability/calibration review is visible from the same cockpit contract
- explicit acceptance gates plus an overall readiness status / paper-operation readiness flag
- recent risk/control audit evidence with actor, scope, rationale, and missing-field warnings
- optional brokerage sync status when a fund account is supplied
- operator work items and warnings

Operator work items emitted by the shared readiness service use stable, scoped `WorkItemId`
values such as `paper-session-missing`, `paper-replay-missing-{sessionId}`,
`dk1-operator-signoff-pending`, and `execution-evidence-incomplete`. This lets the WPF shell, retained web cockpit, and initial operator-inbox endpoint refresh the same blocker without creating a new random item on every poll.
`GET /api/workstation/operator/inbox` now aggregates those readiness work items with actionable
warning/critical run review-packet work items from the latest runs plus open or in-review
reconciliation breaks, adds workspace/page/route navigation hints, and is now consumed by the WPF
main shell queue action. The review-packet merge is bounded to the latest six runs and only promotes
items that already require operator attention, so ready runs do not flood the queue. The shell
resolves known target routes before target page tags, so
reconciliation work items open `FundReconciliation`, Security Master coverage opens
`SecurityMaster`, brokerage-sync blockers open `AccountPortfolio`, and broad trading-readiness
items stay in `TradingShell`. When the active WPF operating context is an account, the shell passes
that account as `fundAccountId` so brokerage-sync and account-scoped readiness blockers remain
visible in the same queue, while promotion-review packets route to
`/api/workstation/runs/{runId}/review-packet`. Broader end-to-end queue acceptance remains a
cockpit-hardening task rather than a completed workflow.
The Trading WPF shell also passes the active account context into its readiness request, so the
cockpit status card and shell queue use the same account-scoped brokerage-sync posture.
The retained web cockpit also renders the readiness contract's operator work items and warnings
beside the acceptance gates, so API diagnostics can show the same replay, promotion, trust-gate,
brokerage-sync, reconciliation, and execution-control blockers that desktop operators see through
the shared queue.
If reconciliation break queue storage cannot seed or load, the endpoint keeps the trading-readiness
items available and adds a stable `reconciliation-break-queue-unavailable` warning routed to
`GovernanceShell` instead of failing the whole operator inbox.

`OverallStatus` is `Ready`, `ReviewRequired`, or `Blocked`; `ReadyForPaperOperation=true` is the
only green cockpit state. `AcceptanceGates` currently contains `session`, `replay`,
`audit-controls`, `promotion`, and `dk1-trust` entries so web and desktop clients render the same
pass/review/blocked decision instead of reconstructing acceptance differently in each surface.
The WPF Trading desk briefing hero must use that shared overall readiness result, not only
`TrustGate.ReadyForOperatorReview`, so a DK1 packet that still needs operator sign-off cannot make
the shell look ready.
The Trading desk briefing hero also treats warning or critical `WorkItems` from the shared
readiness payload as first-class blockers before it can show a ready active-run state; it does not
depend only on the mirrored warning strings. When those work items include route metadata, the hero
resolves known shared routes such as reconciliation, Security Master, and brokerage-sync targets to
concrete workbenches such as `FundReconciliation`, `SecurityMaster`, and `AccountPortfolio` before
falling back to broad workspace shell tags.
A future DK1 packet without valid operator sign-off, replay mismatch, open circuit breaker, or incomplete promotion trace keeps the lane in review or blocked state even when lower-level endpoint data is visible.
Legacy DK1 packets that omit the validated explainability or calibration contract are treated as
blocked trust-gate evidence rather than silently inheriting `ready-for-operator-review`.
Material order/control audit rows that omit actor, scope, or rationale now keep the
`audit-controls` gate in review and surface an `ExecutionControl` work item so the cockpit can
explain why a decision was allowed, rejected, or manually overridden.

`GET /api/workstation/trading` also includes the same readiness payload so workstation consumers can render session, replay, DK1 trust-gate, audit/control, and promotion decisions from one operator-ready lane. When a future generated DK1 packet is `ready-for-operator-review` but lacks valid sign-off, the readiness payload adds a `ProviderTrustGate` work item instead of letting the cockpit look fully accepted.

Replay readiness is rebuilt from durable execution-audit evidence, so replay verification audit entries persist `isConsistent`, compared fill/order/ledger counts, last-persisted timestamps, and the primary mismatch reason. This keeps the shared readiness lane specific after restart and when verification was triggered through the service layer instead of the endpoint wrapper.
The replay gate now treats those compared counts as a freshness contract: if the active session's fill, order, or ledger-entry counts diverge after verification, the gate drops back to review-required and emits a stable `paper-replay-stale-{sessionId}` work item until replay verification is run again.
The WPF cockpit acceptance card now renders those stale replay counts beside session state, showing active-session order/fill/ledger counts and the latest verified replay counts instead of treating a count-stale but otherwise consistent replay audit as green.

Reconciliation break queue items now carry calibrated governance routing metadata: exception route,
tolerance profile, tolerance band, required sign-off role, and sign-off status. The operator inbox
projects those fields into reconciliation work-item details so open and in-review breaks can be
routed for governance handling without losing the shared trading-readiness work items.

#### `PaperSessionReplayVerificationDto`

Replay verification now carries fields that make replay evidence operator-readable:

- `ComparedFillCount`
- `ComparedOrderCount`
- `ComparedLedgerEntryCount`
- `LastPersistedFillAt`
- `LastPersistedOrderUpdateAt`
- `VerificationAuditId`

This keeps the current verify endpoint but makes the result strong enough to act as a gate; WPF now
wires those evidence counts into its acceptance status so stale active-vs-verified replay counts are
visible beside session state.

#### `IPromotionRecordStore`

Durable promotion-record storage now lives in the strategies layer:

- load promotion history on startup
- append approved and rejected promotion decisions
- return history without depending on process memory

Current implementation:

- `src/Meridian.Strategies/Interfaces/IPromotionRecordStore.cs`
- `src/Meridian.Strategies/Storage/JsonlPromotionRecordStore.cs`

`StrategyPromotionRecord` carries:

- `SourceRunId`
- `TargetRunId`
- `ApprovalReason`
- `ReviewNotes`
- `Decision`

#### Execution-control routes

The execution-control routes documented by operations guidance are implemented in
`UiApiRoutes` and `ExecutionEndpoints`:

- `POST /api/execution/controls/manual-overrides`
- `POST /api/execution/controls/manual-overrides/{overrideId}/clear`

These map directly to `ExecutionOperatorControlService.CreateManualOverrideAsync()` and
`ClearManualOverrideAsync()`.

#### `TradingControlReadinessDto`

The shared trading readiness payload now includes a compact `RecentEvidence` list for material
control and order outcomes. Each evidence row carries the audit id, category/action/outcome,
actor, scope, rationale, and any missing fields. The WPF trading shell uses the same evidence to
populate the workflow status card, risk rail, and desk-briefing hero when control explainability is
incomplete.
`OrderSubmitted` audit rows require explicit rationale through the audit message or
reason/rationale metadata; action/outcome alone is not enough to mark the `audit-controls` gate
ready.

#### Frontend promotion request

The frontend helper uses a full `PromotionApprovalRequest` payload so the cockpit can submit:

- `runId`
- `approvedBy`
- `approvalReason`
- `approvalChecklist`
- `reviewNotes`
- `manualOverrideId`

The promotion service now rejects approvals that omit required checklist items. The canonical
`Backtest -> Paper` checklist is `DK1_TRUST_PACKET_REVIEWED`, `RUN_LINEAGE_REVIEWED`,
`PORTFOLIO_LEDGER_CONTINUITY_REVIEWED`, and `RISK_CONTROLS_REVIEWED`; `Paper -> Live` also
requires `LIVE_OVERRIDE_REVIEWED`.

The shared trading readiness lane only treats promotion history as current when the durable
promotion record links to the latest run through `SourceRunId` or `TargetRunId`. Older promotion
records for unrelated or prior same-strategy runs must leave the `promotion` gate in review so the
cockpit cannot inherit stale approval evidence.

Rejection uses the same operator-review packet shape through `RejectPromotionRequest`:

- `runId`
- `reason`
- `rejectedBy`
- `reviewNotes`
- `manualOverrideId`

## Data Flow

### 1. Session Create And Restore

1. Operator creates a paper session from the cockpit.
2. Session metadata is durably saved before the cockpit treats the session as established.
3. Order updates, fills, and ledger entries are durably appended during the session.
4. On restart, `PaperSessionPersistenceService.InitialiseAsync()` reloads metadata, replays fills, restores order history, and reconstructs ledger state.
5. Cockpit restore shows the same session scope and continuity evidence instead of a fresh session shell.

### 2. Replay Verification

1. Operator selects a session and requests verification.
2. Backend replays durable evidence into a fresh portfolio snapshot.
3. Backend compares current and replayed state and records an audit entry with the result.
4. Cockpit shows pass or mismatch, evidence counts, and last verification time.
5. Any mismatch becomes a blocking Wave 2 gate failure until explained or fixed.

### 3. Order And Risk Explainability

1. Operator submits, cancels, or modifies an order.
2. `OrderManagementSystem` evaluates operator controls, security-master approval, and risk validation.
3. Audit entries record the outcome with actor, broker, run, symbol, correlation, and reject reason where applicable.
4. Cockpit risk panels show both current summarized state and recent explaining evidence.
5. Control changes such as breaker toggles or overrides are immediately visible in the same lane.

### 4. Promotion Review

1. Operator evaluates a completed run for `Backtest -> Paper` or `Paper -> Live`.
2. Policy evaluation returns readiness, review status, blocking reasons, and override requirements.
3. Approval or rejection request includes operator identity, rationale, optional review notes, and optional manual override ID; `/api/promotion/*` also falls back to `X-Meridian-Actor` when the body omits an operator.
4. `PromotionService` rejects missing operator/rationale context, persists a durable promotion record, writes audit evidence, and creates the target run with `ParentRunId` and `AuditReference` for approvals.
5. Cockpit and shared run read models can reconstruct the full promotion chain after restart.

## Edge Cases And Risks

- Replay verification now records compared fill, order, and ledger counts and blocks on ledger-journal count, line-count, and trial-balance drift; the remaining risk is richer semantic ledger parity beyond those checks.
- Promotion history now has durable JSONL storage and production DI registration; the remaining risk is drift between promotion history, readiness reconstruction, and cockpit display.
- Manual-override routes are now visible in `UiApiRoutes`; the remaining risk is that future operations guidance, route constants, endpoint mappings, and frontend helpers drift apart.
- Order audit quality still depends on metadata such as actor, correlation, and run scope being present on requests. The cockpit should stop relying on optional ad hoc metadata for critical trace fields.
- Workstation risk copy in `WorkstationEndpoints` is currently derived from portfolio heuristics. That is useful, but it is not sufficient as an operator evidence trail by itself.
- This sprint must not widen live-readiness language. The goal is trustworthy paper operations and promotion traceability, not silent promotion of live claims.

## Test Plan

### Backend

- maintain `tests/Meridian.Tests/Execution/PaperSessionPersistenceServiceTests.cs`
  - restart reload preserves order history and ledger continuity
  - replay verification includes evidence counts and mismatch details
- maintain `tests/Meridian.Tests/Ui/ExecutionWriteEndpointsTests.cs`
  - session create, restore, replay verify, and close remain auditable end to end
  - replay verification audit entry returns a stable audit identifier
- maintain `tests/Meridian.Tests/Ui/WorkstationEndpointsTests.cs`
  - trading readiness joins session, replay, audit/control, promotion, and DK1 trust state
  - replay readiness becomes review-required when a session changes after its latest replay audit
  - operator inbox includes readiness, actionable run review-packet, brokerage-sync, and
    reconciliation break work items with stable navigation targets
- maintain `tests/Meridian.Tests/Strategies/PromotionServiceTests.cs`
  - promotion history survives service restart through the durable record store
  - approval record contains source run, target run, approver, reason, and audit reference
- maintain `tests/Meridian.Tests/Strategies/PromotionServiceLiveGovernanceTests.cs`
  - `Paper -> Live` approval fails without the required override
  - success path records manual override and approval reason durably
- maintain `tests/Meridian.Tests/Ui/ExecutionGovernanceEndpointsTests.cs`
  - manual-override endpoints match the documented operator flow

### Frontend

- extend `src/Meridian.Ui/dashboard/src/screens/trading-screen.test.tsx`
  - controls state is visible beside risk state
  - replay verification shows evidence counts and last verification time
  - promotion approval posts full operator context
  - promotion history remains visible after a reload fetch
  - server readiness work items and warnings remain visible beside acceptance gates
- extend `src/Meridian.Ui/dashboard/src/lib/api.trading.test.ts`
  - approval helper posts the full request body
  - control/manual-override helpers call the expected routes

### Validation Commands

```bash
dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true --filter "FullyQualifiedName~PaperSession|FullyQualifiedName~PromotionService|FullyQualifiedName~ExecutionGovernanceEndpoints|FullyQualifiedName~ExecutionWriteEndpoints"
dotnet test tests/Meridian.FSharp.Tests -c Release /p:EnableWindowsTargeting=true --filter "FullyQualifiedName~PromotionPolicy"
```

If frontend tests are touched:

```bash
npm --prefix src/Meridian.Ui/dashboard test -- trading-screen api.trading
```

## Open Questions

- Answered 2026-04-26: replay readiness records compared fill, order, and ledger counts, and cockpit hardening should block on unexplained divergence rather than portfolio-only replay confidence.
- Answered 2026-04-26: durable promotion records live in the strategies layer through `IPromotionRecordStore` / `JsonlPromotionRecordStore`; execution audit remains supporting evidence rather than the promotion-history store.
- Answered 2026-04-26: `Backtest -> Paper` approvals require operator and rationale context now, while `Paper -> Live` additionally requires live-override review.
- Answered 2026-04-26: expose shared operator work items through `GET /api/workstation/operator/inbox`, seeded from trading readiness plus reconciliation break-queue state, instead of making each client build its own blocker queue.
- Answered 2026-04-25: use `GET /api/workstation/trading/readiness` as the single cockpit readiness endpoint, while keeping the existing focused session, replay, controls, audit, and promotion routes for drill-in and write actions.
