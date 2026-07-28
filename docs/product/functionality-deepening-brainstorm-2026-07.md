# Functionality Deepening Brainstorm — Existing Subsystems (2026-07)

> **Mode:** Open Exploration, constrained to deepening — the request asks for high-value depth in
> what the program already does, not new product surfaces. Ideas are filtered through the W9-era
> scope gate (data confidence, reconciliation, approvals, accounting records, close support,
> retained evidence, workflow controls, governed reporting) and the active lanes
> (`W5X-EVIDENCE-001`, `W5X-STMT-ONBOARD-001`, `W7-LIVE-001`, `W8-WPF-PARITY-001`).
>
> **Grounding:** a fresh code-depth survey of eight subsystems (risk, reconciliation, ledger/NAV,
> order management, portfolio surfaces, alerting, strategy lifecycle, backfill) plus direct reads
> of the approval-policy matrix, data-lineage service, and statement-reconciliation seams.
> Roadmap snapshot 2026-07-28 (`docs/roadmap/data/program-state.yml`); prior-session exclusions
> from the brainstorm ledger (15 sessions), `docs/product/high-value-code-brainstorm-2026-07.md`,
> `docs/product/data-provider-accounting-brainstorm-2026-07.md`, and
> `docs/product/adversarial-program-review-2026-07.md`.
>
> **Relationship to the W9 slate:** none of these ideas re-propose a W9 item. Where a W9 item
> claims a lane (paper realism `W9-PAPER-003`, kill-switch/fat-finger `W9-SAFETY-007`, sided
> matcher `W9-INGEST-009`, NAV economics `W9-NAV-006`), the idea here deepens the layer *beneath
> or around* it and says so explicitly.
>
> This is a dated working design input, not a canonical status source. Use the roadmap registry
> for live status.

---

## The Pattern This Session Found

The adversarial program review named the platform's main value-loss pattern "built but not
wired." The depth survey behind this session found its sibling: **declared but dead** — seams
that promise depth the implementation never delivers, and in two places UI copy that describes
controls which do not exist in code:

- `RiskContext` declares `PortfolioExposure` and `RecentOrderRate`; **no risk rule ever reads
  either field**. `RiskDecision.Escalate` is never constructed anywhere in the repo.
  `RiskRuleSeverity` is logged and never acted on — a `Warning` rejects exactly as hard as a
  `Critical`.
- `PromotionCriteria` documents four walk-forward/out-of-sample gates
  (`MinOutOfSampleSharpe`, `MinWalkForwardDegradationRatio`, `RequireWalkForwardEvidenceForLive`,
  `MaxOutOfSampleDrawdownPercent`); **none of the four is passed to the evaluator** in
  `BacktestToLivePromoter`.
- The Trading workspace hardcodes guardrail sentences ("Single-name concentration cap set at 30%
  notional.", "Auto-throttle activates above 70% intraday buying power.") in
  `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.Trading.cs` — **controls that do not
  exist in code** — next to a literal `Var95: "—"`.
- Three well-designed alerting registries (`IAlertDispatcher`, `AlertRunbookRegistry`,
  `SloDefinitionRegistry` with error budgets) are **never registered in DI, never connected to
  each other, and never connected to delivery**.
- Portfolio marks silently fall back to `AverageCostBasis` when no live mark exists, reporting
  `0` unrealized P&L instead of flagging a stale mark.

Deepening, for this codebase, mostly means **making existing declarations true** — which is also
exactly the truthful-posture direction `W9-TRUTH-001` set. That makes these ideas unusually
cheap for their value: the seams, registries, stores, and UI surfaces already exist.

---

## Ideas at a Glance

| # | Idea | Effort | Audience | Impact | Depends On |
|---|------|--------|----------|--------|------------|
| 1 | Portfolio-aware risk engine: real exposure aggregation, severity semantics, honest guardrail rail | M | I, H | High | — |
| 2 | One alerting spine: SLO evaluation loop + dispatcher wiring + dual-stack merge | M | I, H, Q | High | — |
| 3 | Promotion contract monitor: wire dead criteria, live-vs-paper drift, auto-demotion | M | I, H | High | 2 (delivery) |
| 4 | Reconciliation matching floor: best-match assignment, split matching, business calendar | M | I | High | — |
| 5 | Break intelligence: recurrence fingerprints + match-suggestion learning | L | I | Med-High | 4 |
| 6 | Approval matrix that enforces: route binding, governed policy changes, delegation/escalation | M | I | High | — |
| 7 | Period evidence-completeness certificate (close data gate) | M | I | High | 8 (partial) |
| 8 | Statement coverage calendar + missing-statement chase | S | I | Med-High | — |
| 9 | Closed-loop backfill: market-calendar sweeps + post-remediation verification | M | H, Q, I | Med-High | — |
| 10 | Honest marks: stale-mark provenance on every position surface | S | I, H, Q | High | — |
| 11 | Order lifecycle state machine (replace last-write-wins cache) | S | I, H | Med | — |

Effort: **S** = days, **M** = 1–2 weeks, **L** = 1+ month. Audience: **H** = hobbyist quant,
**Q** = academic, **I** = institutional/fund-ops.

---

## The Ideas

### 1. Portfolio-aware risk engine — make the risk rail true

`src/Meridian.Risk/` is the widest promise/delivery gap in the platform: an
`IRiskRule`/`CompositeRiskValidator` architecture with priorities, severities, a shared
`RiskContext`, and an F# policy kernel — delivered as three rules totaling ~250 LOC.
`RiskContext.PortfolioExposure` and `RecentOrderRate` are never read; `RiskDecision.Escalate` is
never constructed; severity is cosmetic; and the Trading workspace's risk rail *describes
controls that don't exist* while showing `Var95: "—"`. Meanwhile the competitive matrix claims
pre-trade risk as a Meridian differentiator that only Bloomberg matches.

**What to build.** Three moves, each on an existing seam:

- **Feed and read the context.** Populate `RiskContext.PortfolioExposure` from
  `AggregatePortfolioService` / `PortfolioRegistry` (both exist in
  `src/Meridian.Strategies/Services/` and `src/Meridian.Execution/Services/`), and ship the
  rules the context was designed for: gross/net exposure limit, single-name concentration cap,
  per-asset-class notional cap, order-rate window fed by `RecentOrderRate`. Each rule is
  runtime-tunable through the existing 537-line `RiskRuleRuntimeService` and its
  `/api/risk/rules/{name}/config` endpoint — the tuning surface is already built and waiting
  for rules worth tuning.
- **Give severity and `Escalate` semantics.** `Warning` → allow and flag on the order record;
  `Error` → reject; `Critical` → reject and trip the existing
  `ExecutionOperatorControlService` circuit breaker. Construct `Escalate` at last: an escalated
  order parks in a pending-approval state routed through the operations approval matrix (idea
  6), so a limit breach within tolerance becomes a governed override instead of a hard stop.
  This turns the dead three-way `RiskDecision` type into the product feature it was modeling.
- **Make the rail honest.** Replace the hardcoded guardrail strings in
  `WorkstationEndpoints.Trading.cs` with descriptions *generated from the live rule registry* —
  each rail row shows the actual rule, its current threshold, its utilization (e.g. "Gross
  exposure 62% of $5.0M limit"), and links to the tuning surface. Drop `Var95` until something
  computes it; a dash pretending to be a metric is exactly what `W9-TRUTH-001` exists to purge.

**The user moment.** An operator glances at the Trading workspace risk rail and sees three
utilization bars — gross exposure, largest single name, order rate — each with its real limit
and headroom. Submitting an order that would breach concentration gets a structured rejection
naming the rule, current value, and limit; a `Warning`-band order goes through with an amber
flag the blotter retains. The rail never says anything the engine can't enforce.

**Tradeoffs.** Exposure aggregation must be fast enough for the order path — pre-computed
snapshots refreshed on fills (not per-validation recomputation) keep `TryEvaluate`'s sync fast
path honest. Scope discipline matters: this is *not* the deferred enterprise-risk-platform lane
— no VaR engine, no Greeks, no factor models. It is making the existing order-gate rail real.
`W9-SAFETY-007` adds kill-switch and fat-finger rules into this same composite; this idea makes
the engine those rules deserve, and neither blocks the other.

---

### 2. One alerting spine — wire the three registries to each other and to delivery

The monitoring layer contains an `IAlertDispatcher` with filtered subscriptions and statistics
(`src/Meridian.Core/Monitoring/Core/`, impl in `src/Meridian.Platform/Monitoring/Core/`), an
`AlertRunbookRegistry` mapping alerts to probable causes and actions, and an
`SloDefinitionRegistry` with targets, windows, and error budgets. None of the three is
registered in DI; nothing evaluates metrics against the SLO definitions; the dispatcher never
reaches the one delivery implementation that exists (`DailySummaryWebhook`, which already
formats Slack/Discord/Teams payloads). A *second*, unrelated alert stack — the 608-line static
`AlertService` in `Meridian.Ui.Services` with dedup, snooze, suppression, and playbooks — is
WPF-only. Two `AlertSeverity` enums, two histories, zero connection.

**What to build.** A background evaluation loop that walks `SloDefinitionRegistry` against the
metrics already exported (backpressure fill levels, provider health, `DetailedHealthCheck`
consecutive-failure state), publishes `MonitoringAlert`s through a DI-registered dispatcher, and
fans out to (a) `IMonitoringWebhookSink` for Slack/Discord/Teams, and (b) a shared alert-inbox
read model in `Meridian.Ui.Shared` that both the browser workstation and WPF shell consume —
which is also where the WPF-only stack's genuinely good features (dedup windows, snooze,
suppression rules, playbook links) migrate so both lanes get them. Every delivered alert carries
its runbook entry: probable causes, immediate actions, rollback criteria. Fix the dispatcher's
fire-and-forget async handler invocation (unobserved exceptions) while in there.

**The user moment.** A provider starts flapping at 2:14pm. The operator gets one deduplicated
alert in the workstation inbox — not forty — with severity, the SLO it burned, and the runbook's
first two actions inline. The same alert hit the team's Slack channel. Acknowledging it in
either UI lane marks it acknowledged in both, because there is one spine.

**Tradeoffs.** Unifying two severity enums and two alert models is a breaking internal refactor
with mechanical-but-wide call-site changes. The evaluation loop must not become a second
metrics system — it reads what Prometheus/health checks already produce. The survey's judgment
holds: the abstraction quality is high enough that this gap is almost entirely "wire it up and
add an evaluator," which is why the impact/effort ratio here is the best in this session.

---

### 3. Promotion contract monitor — the promotion record becomes a living contract

Paper→live promotion currently evaluates exactly three numbers (`MinSharpeRatio`,
`MaxAllowedDrawdownPercent`, `MinTotalReturn`) — while `PromotionCriteria` publicly documents
four more walk-forward/out-of-sample gates that are **never passed to the evaluator**
(`src/Meridian.Strategies/Promotions/BacktestToLivePromoter.cs`). The 14-item live checklist in
`PromotionApprovalChecklist` is real governance, but its items are operator-asserted strings,
not machine-verified conditions. And after promotion, nothing watches: `LiveRunMetricsTracker`
already builds a `BacktestResult` from live fills — the exact comparable artifact — and nothing
consumes it. A strategy that qualified at Sharpe 1.8 can decay to 0.3 and nothing in the
platform notices, demotes, or even annotates.

**What to build.**

- **First, the embedded quick fix (S, do immediately):** pass the four declared criteria through
  to the evaluator, or delete them. Dead governance knobs on a promotion contract are worse
  than absent ones — they document rigor that isn't happening.
- **Then the monitor:** a post-promotion conformance service that periodically compares the
  live envelope (rolling Sharpe, realized drawdown, slippage vs. paper assumptions, hit rate)
  from `LiveRunMetricsTracker` against the qualifying thresholds pinned in the strategy's
  `StrategyPromotionRecord`. Grace periods and minimum-sample rules prevent day-two
  overreaction. Breaches emit through the alerting spine (idea 2); a sustained breach
  auto-pauses via `StrategyLifecycleManager` and writes a **demotion record** — symmetric to
  the promotion record, with the evidence that triggered it — after which re-promotion goes
  back through the full checklist.

**The user moment.** The Strategy workspace shows each live strategy with a "promotion
conformance" chip: green (within envelope), amber (degrading, 3 of 5 windows below qualifying
Sharpe), red (breached — auto-paused pending review). Clicking it shows live-vs-qualifying
metrics side by side, the same layout the promotion review used. The paper-first governance
story (`W7-LIVE-001`) stops ending at the moment of promotion.

**Tradeoffs.** Statistical honesty is the hard part: short live windows have huge Sharpe
variance, so the monitor must compare distributions with confidence bands, not point values —
otherwise it cries wolf and operators disable it. Auto-pause must respect the existing operator
override model (`ExecutionManualOverride`) so a human can hold a strategy live through a known
regime event, with that override itself audit-recorded.

---

### 4. Reconciliation matching floor — give the superstructure a foundation to stand on

Reconciliation is the deepest subsystem in the repo (~14k LOC): versioned tolerance profiles,
`MatchEvidence` with rule and profile lineage, a 5,400-line break-queue repository, business-hour
SLA math, casework workflow. But the floor under it is thin: cash matching in
`ReconciliationMatchingEngine` is O(n²) and takes the *first* near-tolerance candidate rather
than the best; position matching handles only exact-tuple groups (no partial-quantity splits,
no one-to-many); `DefaultReconciliationIngestionScheduler` is a 20-line sequential `foreach`
with no retry or partial-failure handling; `IAccountingCalendar` has no real business-calendar
implementation; and there are two competing `IFxRateProvider` contracts (a sync no-cancellation
one in reconciliation, a richer async one in `Meridian.Execution.MultiCurrency`).

**What to build.** Best-match assignment: score all candidate pairs within tolerance (amount
distance, date distance, reference similarity) and solve the assignment stably instead of
grabbing `near[0]` — a greedy score-sorted pass covers realistic volumes without a full
Hungarian solver. One-to-many and many-to-one split matching (one wire settles three trades;
three custodian lines sum to one ledger entry) recorded as explicit split groups in
`MatchEvidence` so casework sees why items grouped. A real `IAccountingCalendar` implementation
(weekends + configurable holiday calendars per market) so date tolerances mean business days —
this also serves the SLA calculator and the statement coverage calendar (idea 8). Retire the
sync FX contract in favor of the async execution-side provider, which already does as-of
selection that refuses future quotes. Replace the scheduler `foreach` with bounded-concurrency
ingestion with per-adapter retry and partial-failure reporting.

**The user moment.** The operator sees fewer false breaks after every statement import — items
that used to break because the matcher paired them badly now match with a visible score and,
for splits, a grouping explanation ("3 statement lines → 1 journal, sum within tolerance").
Match rate becomes a number the team trusts enough to put on the close checklist.

**Tradeoffs.** Matching changes reshuffle which items break, so this needs a golden-file
regression pack of known statement/ledger fixtures before touching the engine — match-rate
changes must be explainable, not silent. `W9-INGEST-009`'s sided matcher lands on exactly this
floor; doing the floor first (or together) means the sided matcher inherits assignment quality
instead of layering on `near[0]` semantics.

---

### 5. Break intelligence — the break queue starts learning from its operators

The break-queue and casework layers are production-grade, and every match already carries
`MatchEvidence` with rule id and tolerance-profile version. What no one has built: memory.
Every operator decision — match accepted, break resolved with reason code, tolerance overridden
— is discarded as workflow exhaust instead of being treated as labeled data.

**What to build.** Three layers on the existing queue:

- **Recurrence fingerprints.** Hash stable break features (account, counterparty, instrument,
  amount pattern, day-of-cycle) into a fingerprint; cluster breaks across runs. The queue gains
  a "recurring" facet: "this custody-fee break has appeared 6 of the last 6 months, resolved
  the same way each time" — with the prior resolutions inline.
- **Match suggestions ranked by history.** When a break has candidate matches, rank them by how
  often operators accepted structurally similar pairings. Suggestions carry their evidence
  ("similar to 14 accepted matches") and are one-click to accept — never auto-applied.
- **Tolerance amendment proposals.** When a tolerance profile generates repeated breaks that
  operators consistently resolve as immaterial, propose the amendment ("13 of 15 breaks in this
  profile within 2bp — propose widening from 1bp") — routed through the approval matrix (idea
  6) because tolerance changes are control changes.

**The user moment.** Monday's recon run produces 40 breaks. The queue opens with 28 of them
badged "recurring — prior resolution available" and 9 with high-confidence suggested matches.
The operator handles the genuinely novel 3 first, then works the recurring set in minutes using
prior-resolution context. Aging stops being driven by re-investigation of known noise.

**Tradeoffs.** This is deterministic statistics over the queue's own history — not an ML
service and deliberately not the session-9 MCP break-resolution *agent* idea; it is the data
layer such an agent would later consume. Suggestion quality depends on capturing decisions with
reason codes from day one, so the schema change to record decisions-as-labels should land early
even if the intelligence ships later. Cold start is real: the feature earns trust over its
first two or three close cycles, not its first day. Depends on idea 4 only in the sense that
suggestions built on bad assignment semantics learn the wrong lessons.

---

### 6. An approval matrix that enforces — binding rows to routes, governing changes to itself

`OperationsApprovalPolicyMatrixService` (`src/Meridian.FinancialOperations/OperationsContinuity/`)
holds seven default policy rows — distinct-approval counts, independent-reviewer flags, evidence
requirements, each naming the route it governs — plus an upsert API with audit events and
atomic persistence. Two things keep it from being a control: nothing programmatically binds a
row to enforcement at its route (each governed endpoint hand-rolls its checks, and the matrix
describes them), and **changing the approval policy itself takes effect immediately with no
approval** — a single actor can lower `RequiredDistinctApprovals` from 2 to 1 and the change is
live, with only an audit DTO to show for it.

**What to build.** An enforcement filter resolved per-route: governed endpoints declare their
policy key, the filter loads the matrix row and enforces distinct-approver count and
independent-reviewer identity against `Meridian.Identity` before the handler runs. A coverage
view in Settings lists every governed route and its matrix row — and any route claiming
governance without a row **fails closed**, turning the matrix from documentation into the
single source of enforcement truth (this is the approvals-layer sibling of `W9-GOV-008`'s
route-level authorization). Second: matrix mutations become two-person operations — an upsert
creates a *pending* rule that a distinct approver activates, using the matrix's own machinery
on itself. Third: time-boxed delegation ("reviewer role X delegates to Y until Friday, reason
attached") and escalation timers that page through the alerting spine (idea 2) when an approval
sits idle past its SLA.

**The user moment.** In Settings → Operations Control, every governed action shows its live
policy row and a green "enforced" badge with the route it binds to. An admin edits a rule and
sees "pending second approval" instead of silent effect. When the assigned reviewer is out, the
submitter sees exactly who holds delegated authority and until when.

**Tradeoffs.** Retrofitting enforcement onto endpoints that hand-roll checks risks
double-enforcement or drift during migration — the coverage view exists precisely to make that
migration auditable, route by route. Self-governance needs a bootstrap escape hatch
(break-glass with `AdminMaintenance` + incident reference, mirroring the existing reopen row)
so a mis-edit can't lock the matrix permanently.

---

### 7. Period evidence-completeness certificate — the close gate that checks the data, not just the checklist

The close workflow already gates on approvals, report packs, and checklist control approvals
(the approval matrix's `operations-continuity.close` row requires all three). What no gate
checks: whether the *data under the period* is complete. Market-data completeness scoring
exists (`CompletenessScoreCalculator`), gap remediation SLA state exists
(`AutoGapRemediationService`), statement checkpoints exist, recon runs and break counts exist,
evidence artifacts exist (`IEvidenceArtifactStore`) — but nothing aggregates them into a
per-period verdict, so a close can proceed over a hole nobody surfaced.

**What to build.** A certificate service that, for an accounting period and entity, aggregates:
(a) market-data completeness for every held instrument's session range, with unremediated gaps
listed; (b) statement coverage — every expected statement received and imported (fed by idea
8); (c) reconciliation freshness — runs current through period end, open breaks above severity
threshold enumerated; (d) evidence linkage — every import in the period has its retained
artifact. The result is a signed, hash-stamped certificate document (stored through
`IEvidenceArtifactStore` like any other evidence) rendered as a gate row in the close workflow:
green with drill-down, or red with the exact list of what's missing. Proceeding over a red
certificate is possible — as a governed override through the approval matrix, which is what
makes this a control rather than a dashboard.

**The user moment.** Before submitting June close, the controller opens the period and sees
"Evidence completeness: 96% — 2 blockers: custodian statement for account X not received;
14-minute data gap on IWM June 18 unremediated." One click opens the chase task (idea 8) or the
remediation queue (idea 9). At sign-off, the certificate PDF lands in the evidence vault next
to the report pack — provable data diligence, which is precisely the `W5X-EVIDENCE-001`
productization story extended from "we retained the documents" to "we can prove the period was
whole."

**Tradeoffs.** "Expected" is the hard word: expected statements need account terms (idea 8),
and expected market-data sessions need the trading calendar (idea 4's `IAccountingCalendar`
work). Thresholds must be tunable per fund — a 100%-or-red certificate would train operators to
override it routinely, which is worse than not having one. Distinct from the session-12
shadow-close readiness score (parallel-books onboarding comparison); this certifies upstream
data and evidence presence for any period, every period.

---

### 8. Statement coverage calendar + missing-statement chase

Statement ingestion is genuinely built — BAI2 and CAMT.053 connectors, checkpoint stores, an
evidence bridge, casework handoff (`src/Meridian.FinancialOperations/Reconciliation/`,
`src/Meridian.Ui.Shared/Evidence/`). But the platform only knows about statements that
*arrived*. Nothing models which statements are *expected*, so a custodian quietly skipping an
account for a month is invisible until reconciliation fails or close stalls.

**What to build.** Per-account statement terms (source, frequency, expected arrival lag — e.g.
"camt.053 daily, T+1 by 07:00") feeding a coverage matrix read model: accounts × periods,
each cell received/late/missing, with received cells linking to their import evidence. Missing
cells past the expected lag auto-open a chase task through the existing casework handoff
(`StatementReconciliationCaseworkHandoffService`), with escalation through the alerting spine.
The coverage matrix is the direct feed for certificate item (b) in idea 7.

**The user moment.** The Accounting workspace shows a coverage strip: 47 of 48 expected
statements received for July; the missing cell is amber at T+2 with a chase task already open
and assigned. Nobody discovers a missing statement during close week anymore.

**Tradeoffs.** Small, contained, and mostly a modeling exercise — the risk is terms drift
(custodians change delivery schedules), so terms need an owner and a "last confirmed" date.
Deepens `W5X-STMT-ONBOARD-001` directly: onboarding a connector now ends by declaring what it's
expected to deliver, which is what makes the connector's silence detectable.

---

### 9. Closed-loop backfill — scheduled sweeps and verified remediation

`AutoGapRemediationService` is 1,206 lines of real depth: idempotency keys, cooldowns, SLA
tiers, multiple entry points. But it is purely *reactive* — there is no scheduled backfill
anywhere: no nightly sweep, no catch-up-on-startup, no market-calendar awareness. The direct
gap-backfill path is fire-and-forget with no retry and a hardcoded `"composite"` provider
label. And remediation is open-loop: success means "the backfill request didn't error," not
"the gap is closed" — nothing re-runs gap analysis over the remediated window, and the 622-line
`CrossSourceBackfillReconciliationService` that could verify against a second source is never
invoked by the remediation path.

**What to build.** A `BackgroundService` sweep: on startup and on a market-calendar schedule
(after each session close), run gap analysis over the active symbol universe and enqueue
remediation for findings — turning "the collector was down overnight" from a silent hole into a
morning-report line item. Convert the fire-and-forget path into a durable bounded queue with
retry policy and real provider attribution. Close the loop: after each remediation, re-run gap
analysis on the window and only then mark the gap closed; for instruments feeding accounting
periods, optionally chain cross-source verification so the certificate (idea 7) can cite
"remediated and verified" rather than "remediation requested."

**The user moment.** The Data workspace's gap panel gains a lifecycle: detected → queued →
backfilled → **verified**, with timestamps. An operator arriving after a feed outage sees the
overnight sweep already queued 14 windows, 11 verified closed, 3 awaiting a provider's data
availability — instead of discovering the outage themselves and clicking backfill by hand.

**Tradeoffs.** Scheduled sweeps must respect provider rate budgets (the cost estimator and
cooldown machinery already exist — reuse them, don't bypass). Verification doubles read load on
remediated windows; scope it to instruments that feed valuation/recon rather than everything.
Distinct from the prior data-provider brainstorm's "backfill feedback loop" (live progress UX
and typed SLA metadata); this is about *scheduling and closing the loop*, not progress display.

---

### 10. Honest marks — provenance and staleness on every position surface

`WorkstationEndpoints.Trading.cs` falls back to `AverageCostBasis` when no live mark exists —
silently reporting zero unrealized P&L as if it were information. The ledger side already has
`StalePricePolicy` and `FairValueLevel`; the portfolio surfaces have neither. This is the
smallest idea in the session and arguably the most brand-critical: a fund-ops platform whose
position screen can quietly show cost basis as a mark has a truth problem on its most-viewed
number.

**What to build.** A mark-provenance field on every position read model: source (live tick /
official close / carried-forward / **cost-basis fallback**), as-of timestamp, and staleness
against the instrument's expected update cadence. Surfaces render it as a small badge with the
fallback case loud (amber "no mark — showing cost basis; unrealized P&L not meaningful"), and
portfolio-level headers aggregate it ("2 of 41 positions on stale marks"). Both UI lanes get it
through the shared read model, so it's WPF-parity-friendly by construction.

**The user moment.** A position with a dead feed stops looking flat and starts looking *broken*
— which is what it is. The portfolio header's "marked as of 16:02, 41/41 fresh" chip becomes
the two-second health glance the design principles call for.

**Tradeoffs.** Almost none technically — the main work is threading provenance through read
models and both UI lanes without breaking existing consumers. The cultural tradeoff is the
point: screens get visually noisier exactly when data is bad. That is `W9-TRUTH-001`'s
fail-closed posture applied to valuation, and it should ship early because ideas 1 and 7 both
cite mark freshness.

---

### 11. Order lifecycle state machine — replace the last-write-wins cache

`OrderManagementSystem` is 1,833 lines of genuine governance depth, but its lifecycle companion
`OrderLifecycleManager` is a 100-line `Dictionary<string, OrderStatusUpdate>` — last write
wins, no transition validation, no timeout detection, no history. An out-of-order execution
report (Filled arriving before PartiallyFilled) silently regresses state. `BrokerageCapabilities`
meanwhile advertises `"simple,bracket,oco,oto"` order classes the platform cannot construct —
another declared-but-dead string to retire.

**What to build.** A proper state machine over `OrderStatus` with a legal-transition table:
illegal transitions are rejected, logged, and flagged on the order record instead of applied;
every transition appends to a lifecycle event history (which the blotter's order drill-down can
finally render as a timeline); stale-state detection flags orders sitting in `PendingNew` or
`PendingCancel` beyond gateway-appropriate timeouts and raises through the alerting spine.
Trim the capability strings to what's real.

**The user moment.** An operator drills into an order and sees its full lifecycle timeline —
submitted 09:31:02, acked 09:31:02, partial 300/1000 09:31:47, replaced 09:33:10 — instead of
only a current status. A hung order surfaces itself in amber after 30 seconds instead of being
found manually at day end.

**Tradeoffs.** Small and contained, but it sits under live fill streaming (`W9-ALPACA-004`), so
it should land *before* real broker reports start flowing — out-of-order and duplicate reports
are exactly what live gateways produce, and a validated state machine is the difference between
flagging them and corrupting order state.

---

## Synthesis

**Highest-leverage idea: #2 (alerting spine).** The registries, the delivery formatter, and the
health checks all exist; the gap is registration, an evaluation loop, and a shared inbox. It is
the best impact/effort ratio in the session, and four other ideas (3, 6, 8, 9) deliver their
"something needs attention" moments through it — building them first would mean building
ad-hoc notification paths that the spine then obsoletes.

**Strategic anchor: #1 (portfolio-aware risk).** The competitive matrix claims pre-trade risk
as a moat column only Bloomberg matches. Three rules and a hardcoded rail don't back that
claim; real exposure aggregation with severity semantics does, and it is the deepening that
makes `W9-SAFETY-007`'s rules land in an engine worthy of them.

**Platform bets.**
- The **approval matrix as enforcement point (#6)** is load-bearing for three other ideas:
  risk `Escalate` routing (#1), tolerance amendments (#5), and certificate overrides (#7) all
  need a governed approval seam. Building #6 first means those features inherit governance
  instead of reimplementing it.
- The **business calendar** (inside #4) serves recon date tolerances, the SLA calculator,
  statement expectations (#8), and expected-session logic for the certificate (#7). One
  implementation, four consumers.

**Cross-cutting theme: retire "declared but dead."** Dead `RiskContext` fields, an
unconstructed `Escalate`, four unpassed promotion criteria, capability strings for absent order
classes, guardrail copy describing nonexistent controls, `Var95: "—"` — each is a small
truthfulness debt, and together they are the same defect `W9-TRUTH-001` targets at the
simulation boundary. A one-day sweep that wires-or-deletes each of these is the cheapest
credibility purchase available to the program.

**Sequencing.**

1. **Days, not weeks:** #10 honest marks, #3's criteria-wiring fix, #11 lifecycle state
   machine, #8 statement coverage — four small, independent truth/coverage wins, two of which
   (#11, #10) should land before `W9-ALPACA-004` puts live data behind them.
2. **The spine:** #2 alerting, then #6 approval enforcement — the two seams later ideas
   deliver through.
3. **The floors:** #1 risk engine and #4 matching floor (parallelizable; different subsystems).
4. **The loops:** #9 verified backfill, #7 completeness certificate (consumes #8, #9, #4's
   calendar), #3's full drift monitor (consumes #2).
5. **The long game:** #5 break intelligence, once #4's floor is stable and decision-labeling
   has accumulated cycles.

**Competitive signals.** Bloomberg's two borrow-worthy features per the landscape reference —
data lineage tagging and real-time anomaly flagging — map directly to #10's mark provenance and
#2's spine; shipping them in a self-hosted platform is differentiation Bloomberg can't follow.
Arcesium's productized "Reconciliation Agent" and the industry's ~51% automation gains in recon
workflows validate #4/#5 — and Meridian's edge is that #5's operator-decision labels are
exactly the proprietary training data a future agent needs, accumulated on the customer's own
infrastructure rather than a vendor's. Databento/Polygon have no answer to any of this layer;
the front-to-back moat (collection → books → close) is defended precisely by the connective
depth these ideas add.

---

## Appendix: Session Ledger Entry

The brainstorm skill's session ledger lives at
`.claude/skills/meridian-brainstorm/brainstorm-history.jsonl`, which sits outside the `PR1`
docs phase scope enforced on this pull request by the roadmap-source-docs gate. To keep the
dedup record without widening the phase, the session's ledger line is preserved here; append it
to the ledger verbatim from a change that is not phase-constrained:

```json
{"session_date": "2026-07-28", "mode": "Open Exploration (deepening-constrained)", "themes": ["portfolio-aware risk engine (exposure aggregation, severity semantics, Escalate-to-approval routing, honest guardrail rail)", "unified alerting spine (SLO evaluation loop, dispatcher DI wiring, dual-stack merge, shared alert inbox)", "promotion contract monitor (wire dead PromotionCriteria knobs, live-vs-paper drift, auto-demotion records)", "reconciliation matching floor (best-match assignment, split matching, IAccountingCalendar implementation, FX contract unification)", "break intelligence (recurrence fingerprints, operator-decision match-suggestion learning, tolerance amendment proposals)", "approval matrix enforcement binding (route filter, governed policy self-changes, delegation and escalation timers)", "period evidence-completeness certificate (close data gate over gaps, statements, recon freshness, evidence linkage)", "statement coverage calendar with missing-statement chase", "closed-loop backfill (market-calendar sweeps, durable queue, post-remediation verification)", "honest marks (mark-source provenance, stale-mark flagging on position surfaces)", "order lifecycle state machine (legal transitions, event history, stale-state detection)"], "ideas_count": 11, "document_updated": "docs/product/functionality-deepening-brainstorm-2026-07.md", "notes": "Deepening-of-existing-functionality session grounded in a fresh 8-subsystem code-depth survey. Central finding: 'declared but dead' seams. All 11 ideas positioned to compose with (not duplicate) the W9 slate. Avoided all 15 prior-session theme sets plus data-provider-accounting brainstorm lanes, adversarial-review items, and cash-ladder blueprint. Platform bets: alerting spine and approval-matrix enforcement. Sequencing: truth quick wins -> spine -> floors -> loops -> break intelligence."}
```
