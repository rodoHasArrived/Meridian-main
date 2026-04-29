# Meridian 6-Week Roadmap

**Last Updated:** 2026-04-29
**Horizon:** Next 6 weeks
**Status:** Short-horizon execution slice derived from the canonical roadmap and current DK readiness dashboard

This document is the six-week execution slice of [`ROADMAP.md`](../status/ROADMAP.md). It is intentionally narrower than the canonical roadmap and advances the active Wave 2-4 core operator-readiness path while keeping the closed Wave 1 trust gate synchronized.

Use this with [`waves-2-4-operator-readiness-addendum.md`](waves-2-4-operator-readiness-addendum.md) when assigning owners, sequencing dependencies, or checking workstream-level exit criteria inside the active Waves 2-4 path.

---

## Summary

The next six weeks should focus on four outcomes:

1. keep the closed Wave 1 provider-confidence and checkpoint-evidence gate green and preserve the signed DK1 pilot parity packet around the emitted Alpaca/Robinhood/Yahoo `pilotReplaySampleSet`, freshly generated date-stamped parity-packet artifacts, and valid packet-bound sign-off evidence
2. harden the Wave 2 paper-trading cockpit that is already visible in the workstation
3. deepen Wave 3 shared run / portfolio / ledger continuity across workspaces while moving new operator UI validation through the active web dashboard and retaining WPF as support evidence
4. land the first Wave 4 governance and fund-operations slices on top of the delivered Security Master baseline

Explicit non-goals in this window:

- Wave 5 Backtest Studio unification
- broader Wave 6 live integration readiness expansion beyond clarifying prerequisites
- optional advanced research / scale tracks such as deeper QuantScript expansion beyond the delivered local run-history handoff slice, L3 inference, multi-instance coordination, preferred-equity follow-ons, and Phase 16 performance work
- broad cleanup or parallel UX programs that do not directly move Waves 1-4

---

## Repo Constraints

This plan starts from the current repo state:

- the web dashboard in `src/Meridian.Ui/dashboard/` is now the active operator UI lane, with built assets served from `src/Meridian.Ui/wwwroot/workstation/`; the retained WPF shell remains support evidence for compatibility and shared-contract regression checks
- the web Research run library already has a support slice for retained-run review, two-run compare/diff readiness, promotion-history loading, command-error alerts, component/view-model coverage, and refreshed built assets; canonical seven-workspace web navigation, strategy-aware launch/preflight, persisted sweep grouping, and Backtest Studio unification remain open
- the WPF workstation shell is already organized around `Research`, `Trading`, `Data Operations`, and `Governance`; it remains useful support evidence for retained desktop workflows and shared workstation contracts rather than the default lane for new operator features
- the current repo contains the WPF shell/navigation baseline in `ShellNavigationCatalog`, workspace shell pages, `MainPageViewModel`, `DesktopLaunchArguments` startup/deep-link parsing, deep-page hosting, context strips, shell/navigation smoke tests, and focused coverage for Batch Backtest results empty-state guidance, Position Blotter, Notification Center history recovery, Trading Hours session and holiday-calendar guidance, Welcome, Storage archive posture, System Health triage, Activity Log triage/export/clear state, Watchlist posture plus pinned-first display, Messaging Hub delivery posture with refresh recency, StrategyRuns filter-aware recovery/run-scope presentation and comparison guidance, QuantScript run-history handoffs, Security Master runtime/search recovery, Fund Accounts account-queue/provider-routing/shared-data and balance-evidence briefing states, workspace queue tone styles, the workspace shell context strip, route-aware operator queue button state with shell-context attention cues, the Trading desk briefing hero's stale-replay count detail, the Research desk briefing hero's run-detail / portfolio / promotion-review handoffs, the Data Operations desk briefing hero's provider / backfill / storage / session / export / environment-mode handoffs, the Provider Health posture briefing, local single-instance mutex plus launch-argument forwarding behavior, and workflow page-state automation markers, so this window should validate workflow value rather than start a second desktop UX track
- the latest shell-support evidence adds Welcome readiness progress for connection/symbol/storage posture, Storage archive posture for daily growth/capacity/last-scan handoffs, Storage preview scope/guidance for archive-path decisions, OrderBook order-flow posture for depth/tape/spread monitoring, compact shared deep-page command chrome that preserves related-workflow and trust-state context, actionable shell-context attention details, a provider-degradation next action that opens `ProviderHealth`, brokerage-sync queue routing into `AccountPortfolio`, bounded run review-packet queue items, and Trading hero attention states for warning or critical shared work items; treat these as validation support for operator orientation and routing, not as separate readiness exits
- fixture/offline desktop workflow mode is now presented as neutral demo data and isolated workflow automation restores shared project assets without pinning the WPF target framework before building the desktop shell with the pinned WPF framework and confirming page tags, so test evidence should distinguish demo-state validation from operational readiness
- the WPF screenshot-refresh workflow now has scheduled, push, and manual dispatch coverage for catalog/manual capture groups, least-privilege default permissions, diagnostic artifacts, and a single post-matrix commit job; use this as screenshot/manual evidence plumbing, not as proof of Wave 2-4 workflow acceptance
- the paper-trading cockpit is partially productized, not greenfield, and now has a shared `/api/workstation/trading/readiness` contract for session, replay consistency/freshness, controls, recent risk/control audit evidence, missing-field explainability warnings, promotion, DK1 trust-gate packet/sign-off projection, brokerage-sync, acceptance-gate/overall-readiness posture, and stable operator work items, plus an initial `/api/workstation/operator/inbox` aggregation contract for readiness, actionable latest-run review-packet, and reconciliation work items that the WPF main shell consumes through route-aware queue-button navigation, Account Portfolio routing for brokerage-sync blockers, run review-packet routing, and active-account `fundAccountId` propagation; `PromotionApprovalChecklist` defines required review items for paper and live promotion approvals, and the run review packet now emits stable route-aware work items for cross-workspace blockers
- shared `StrategyRun`, portfolio, and ledger read services already exist and feed workstation surfaces
- promotion endpoints and workstation promotion surfaces are already in code
- Security Master is already the authoritative instrument-definition baseline across workstation and governance surfaces
- governance already has concrete seams for reconciliation, cash-flow summaries, reporting profiles, and direct-lending foundations
- the closed Wave 1 trust gate remains the first release gate for every downstream claim
- DK1 provider-trust status is ready for Wave 2 handoff: the 2026-04-27 parity packet is signed by Data Operations, Provider Reliability, and Trading with valid packet binding, all pilot samples ready, validated evidence documents, and validated explainability/calibration contracts. Future provider-evidence changes still require a fresh date-stamped packet and matching `packetReview` sign-off; promotion handoff is early in progress through cockpit audit-feedback hardening, export is early in progress through governed report-pack schema/version checks, and reconciliation DK2 is now early in progress through a file-backed break queue with review, resolve/dismiss, audit-history routes, seeded exception-route/tolerance/sign-off metadata, and calibration-summary profile rollups

---

## Wave Alignment

### Active in this window

- **Wave 1:** closed trust-gate maintenance
- **Wave 2:** paper-trading cockpit hardening
- **Wave 3:** shared run / portfolio / ledger continuity
- **Wave 4:** governance and fund-operations productization on top of the delivered Security Master baseline

### Delivery guardrails in this window

- keep WPF workflow-first consolidation and MVVM extraction limited to work that directly supports active Wave 2-4 flows
- keep desktop launch/deep-link routing, screenshot workflows, single-instance forwarding, UI-automation page-state markers, and fixture/demo-mode cues aligned to the four workspace shell routes so automation verifies the same surfaces operators use; screenshot-refresh CI should remain a validation evidence lane with diagnostic artifact retention and a single final commit step
- keep validation and documentation synchronized with executable evidence, not summary language; the DK1 `pilotReplaySampleSet` is now part of that evidence contract
- keep shared DTOs, read models, workflow services, and export seams as the integration boundary across active work
- treat current shell workflow validation as open until the delivered shell/navigation baseline is clearly wired into run-centered workflows

### Explicitly deferred beyond this window

- **Wave 5:** Backtest Studio unification across native and Lean
- **Wave 6:** live integration readiness, except where Wave 1-2 work clarifies prerequisites
- optional advanced research / scale tracks

---

## Six-Week Outcomes

### Outcome 1: The closed Wave 1 trust gate stays reproducible and synchronized

- Alpaca and Yahoo remain repo-closed, Robinhood remains explicitly runtime-bounded, and deferred providers stay clearly outside the active gate
- the DK1 pilot replay/sample-set contract is emitted by `scripts/dev/run-wave1-provider-validation.ps1`, packaged by `scripts/dev/generate-dk1-pilot-parity-packet.ps1`, bound to operator approvals by `scripts/dev/prepare-dk1-operator-signoff.ps1 -PacketPath`, and reviewed through the DK1 pilot parity runbook
- backfill checkpoints, gap detection, and Parquet L2 flush behavior remain on the passing command matrix instead of drifting back into assumed reliability
- the active Wave 1 scope stays synchronized with the provider-validation matrix, provider-confidence language, generated validation summaries, and the signed 2026-04-27 DK1 packet; future generated `artifacts/provider-validation/` packets remain run-specific evidence and require matching sign-off when used for review
- the Data Operations desk briefing hero and Provider Health posture briefing remain consumers of shared provider, backfill, storage, session, and export state instead of becoming separate operational-readiness models
- Storage archive posture remains a Data Operations support improvement: it should project daily growth, capacity horizon, last-scan, and scan-failure guidance from already-loaded analytics without becoming a separate storage readiness gate

### Outcome 2: Wave 2 paper trading is dependable, not just visible

- the shared workstation cockpit is tightened around positions, orders, fills, replay, sessions, risk flows, and Position Blotter selection/action-readiness flows already in code
- the WPF Trading desk briefing hero is validated as a consumer of shared active-run, workflow-summary, and operator-readiness state rather than a separate cockpit model
- `Backtest -> Paper` remains explicit, auditable, and easier to exercise end to end
- session persistence, replay behavior, and stale-replay recovery have clearer operator acceptance criteria
- the trading cockpit now surfaces a single operator acceptance contract for session persistence, replay confidence, audit/control evidence, risk/control explainability warnings, promotion-review readiness, DK1 trust posture, brokerage-sync posture, overall readiness, and operator work items, with an initial shared operator-inbox endpoint, route-aware WPF shell queue-button consumption, Account Portfolio routing for brokerage-sync blockers, run review-packet routing for actionable latest-run blockers, and shell-context attention cues available for readiness, Security Master, and reconciliation queue aggregation
- the local replay-audit hardening slice now records replay consistency, compared fill/order/ledger evidence counts, last-persisted timestamps, and primary mismatch reason so readiness reconstruction has durable audit metadata to read from, and the readiness gate drops back to review-required when those compared counts no longer match the active session

### Outcome 3: Wave 3 shared-model continuity is stronger across workspaces

- `Research`, `Trading`, and `Governance` rely more consistently on the shared run, portfolio, and ledger model
- run comparison, fills, attribution, ledger, cash-flow, and reconciliation flows feel more like one system than adjacent slices
- WPF refinements in scope reinforce the same shared orchestration seams instead of introducing new page-local logic
- the Research desk briefing hero remains a shared-model consumer for selected runs, portfolio drill-ins, and `Backtest -> Paper` promotion review instead of becoming a separate research-only orchestration path
- StrategyRuns filter recovery and comparison guidance remain shared-run support improvements: they should recover already-loaded run rows, clarify visible-versus-recorded scope, and prevent invalid compare pairs without becoming a separate run-store workflow
- QuantScript Run History remains a shared-run support improvement: it should expose local execution records and only hand off to Strategy Runs, run detail, or compare flows when a mirrored run exists
- RunCashFlow empty-state guidance remains a shared-run support improvement: it should explain selected-run, missing-run, no-event, and loaded retained cash-flow states without becoming a substitute for governance-wide cash-flow modeling
- current shell-navigation work is validated as a workflow-first improvement rather than just a visual reshuffle
- desktop launch/deep-link and screenshot workflow evidence uses the same canonical workspace tags operators use (`ResearchShell`, `TradingShell`, `DataOperationsShell`, `GovernanceShell`) and confirms page state through the hidden-but-present `ShellAutomationState` marker
- screenshot/manual catalog automation can now be selected through workflow dispatch and committed once after matrix capture, which reduces evidence churn but does not change the operator-readiness gates

### Outcome 4: Wave 4 governance work shows up as product, not just planning

- Security Master remains the delivered baseline while account/entity, reconciliation, cash-flow, multi-ledger, and reporting-adjacent workflows deepen on top of it
- the next governance slice is defined in terms of shared DTOs, read models, export seams, account/provider-routing evidence, reconciliation break-queue state, calibration-summary rollups, seeded exception-route/tolerance/sign-off metadata, and operator surfaces rather than a parallel governance stack

---

## Week-by-Week Plan

| Week | Focus | Goals | Deliverables |
| --- | --- | --- | --- |
| 1 | DK1 / Wave 1 closeout confirmation | preserve the signed trust gate and remove planning contradictions around the active provider set and pilot replay/sample standard | refreshed validation summary with `pilotReplaySampleSet`; signed 2026-04-27 DK1 parity packet; valid packet-bound sign-off evidence; synchronized provider/runtime evidence list; explicit deferred-provider wording; dashboard evidence links; future-review rerun guidance |
| 2 | Wave 2 entry | keep the trust gate green while starting cockpit hardening | cockpit hardening checklist; operator acceptance targets tied back to the passing Wave 1 gate |
| 3 | Wave 2 operator lane | tighten the existing trading cockpit into a more dependable operator workflow | session and replay acceptance criteria; promotion approval/rejection rationale checklist; cockpit operator-path checklist |
| 4 | Wave 3 continuity | reduce cross-workspace seams between Research, Trading, and Governance while validating the active web dashboard direction and retained WPF support | run-model continuity backlog; fills/attribution/ledger/reconciliation linkage notes; web/workstation validation targets tied to active flows |
| 5 | Wave 4 governance slice | connect the delivered Security Master baseline to concrete governance product slices | account/entity and strategy-structure targets; first multi-ledger/cash-flow/reconciliation slice decisions; reporting/profile follow-ons |
| 6 | Hardening and closeout | make the six-week baseline easy to continue from without widening scope | docs/status refresh; acceptance-criteria review; narrowed follow-on backlog that still stays within Waves 1-4 |

---

## Workstreams

### Workstream A: Wave 1 trust-gate maintenance

Priorities:

- keep Alpaca and Yahoo explicit as repo-closed rows and Robinhood explicit as the only runtime-bounded active row
- keep the DK1 `pilotReplaySampleSet` synchronized across the validation script, generated summaries, signed generated parity packet, packet-bound sign-off evidence, provider-validation matrix, and pilot parity runbook
- keep deferred-provider guidance synchronized so Polygon, Interactive Brokers, NYSE, and StockSharp do not drift back into the active gate by prose alone
- rerun `run-wave1-provider-validation.ps1` whenever provider, checkpoint, or Parquet proof surfaces change
- keep provider-confidence docs, generated runtime outputs, and validation summaries synchronized

### Workstream B: Wave 2 paper-trading cockpit hardening

Priorities:

- harden the existing execution and promotion flows through the shared workstation readiness contract
- keep replay, session, audit, and risk behavior tied to realistic operator use
- keep recent risk/control audit evidence explainable with actor, scope, rationale, and operator-visible missing-field warnings
- prefer reliability and workflow continuity over new cockpit surface area
- define operator-visible acceptance criteria for the paper workflow already in code
- keep the cockpit readiness contract aligned with DK1 acceptance language so session, replay, audit, promotion-review, brokerage-sync, operator-work-item, or future trust-packet/sign-off gaps are visible during daily operation with stable work-item IDs across refreshes
- keep run review-packet work items in the operator inbox bounded to actionable warning/critical latest-run blockers so the queue improves triage without becoming a broad run-history browser

### Workstream C: Wave 3 shared run / portfolio / ledger continuity

Priorities:

- deepen shared run services beyond a mostly backtest-first feel
- improve research-to-trading and trading-to-governance continuity
- keep Security Master enrichment tied to the same shared read-model seam
- use WPF workflow work only where it reinforces the same run-centered orchestration path

### Workstream D: Wave 4 governance and fund-operations productization

Priorities:

- keep Security Master authoritative while extending its use across governance workflows
- define the next concrete slices for account/entity, multi-ledger, cash-flow, calibrated reconciliation, and reporting work
- treat the Fund Accounts operator brief as Wave 4 support evidence only while it remains a projection of shared account, provider-routing, retained balance-history, and shared-data-access state
- keep governance work grounded in shared DTOs, read models, and export seams rather than a separate subsystem

### Supporting discipline: Workflow-first WPF consolidation and validation

Priorities:

- prioritize high-traffic WPF pages and shell surfaces that directly support active cockpit, shared-model, or governance work
- continue MVVM extraction where pages still depend heavily on code-behind orchestration in active areas
- keep navigation, command-palette entries, and workspace framing aligned with the same workstation model used by the WPF shell and retained desktop-local API contracts
- treat the Trading desk briefing hero, OrderBook posture strip, and Position Blotter selection-review rail as Wave 2 support evidence only when they continue to reflect shared execution, readiness, replay, controls, trust-gate, depth/tape, and brokerage-sync posture without duplicating service logic
- treat the Research desk briefing hero as Wave 3 support evidence only when it keeps selected-run, run-detail, portfolio, and paper-promotion handoffs tied to shared workstation read models
- treat StrategyRuns filter recovery and comparison guidance as Wave 3 support evidence only when they clarify shared run scope, recover hidden retained rows, and prevent invalid compare pairs without duplicating run-read service state
- treat BatchBacktest results empty guidance as Wave 3 support evidence only when it helps operators distinguish idle, validation-blocked, running, failed, cancelled, and populated sweep states without inventing a separate results model
- treat QuantScript Run History as Wave 3 support evidence only when it keeps script/notebook execution records local while using shared Research surfaces for mirrored run handoffs
- treat the Data Operations desk briefing hero and Provider Health posture briefing as Wave 1/DK1 support evidence only when they keep provider, backfill, storage, session, and export handoffs tied to shared operational services
- treat Storage archive posture as Data Operations support evidence only when it reflects already-loaded storage analytics and helps operators decide retention, packaging, or backfill next steps without replacing storage validation gates
- treat System Health triage as support-triage evidence only when it summarizes provider, storage, and retained event posture and distinguishes pending scans from confirmed empty snapshots without substituting for readiness gates, provider validation, or durable incident queues
- treat Notification Center filter recovery as governance/operator-triage support evidence only when it helps recover retained history without substituting for durable work-item queues
- validate the current `ShellNavigationCatalog`, workspace-shell, deep-page host, and shell-context-strip baseline against active run-centered workflows before widening it further
- keep isolated desktop workflow restore/build behavior deterministic so shared library assets and WPF target-framework builds do not drift during screenshot/manual evidence capture
- keep fixture/demo-mode state explicit in desktop workflow evidence so demo payloads help reproduce UI states without satisfying readiness exit criteria
- pull validation and contradiction checks forward whenever workstation or governance surfaces expand

---

## Risks

### Risk 1: Cockpit polish outruns execution/read-model contracts

Mitigation:

- keep the closed Wave 1 trust gate ahead of broad cockpit claims and tie Wave 2 acceptance criteria to real evidence

### Risk 2: Provider trust remains documentation-only

Mitigation:

- require replay evidence, runtime proof, or explicit gap documentation for every provider claim carried forward

### Risk 3: Governance stays blueprint-heavy

Mitigation:

- require each Wave 4 step to name at least one shared read-model seam and one operator-facing surface

### Risk 4: Workstation polish outruns shared contracts

Mitigation:

- favor workflow services and view-model extraction over page-local orchestration and limit WPF work to active-wave support

### Risk 5: Too much broad cleanup crowds out product movement

Mitigation:

- keep cleanup adjacency-driven and focused on areas already changing for trust, cockpit, shared-model, or governance work

---

## Exit Criteria After 6 Weeks

- provider/runtime guidance for the active Wave 1 gate remains reproducible and contradiction-free, including the emitted DK1 pilot replay/sample-set contract, generated parity packet, packet-bound sign-off template, and explicit operator-signoff status
- backfill checkpoint and gap-handling confidence remains backed by passing evidence instead of only document claims
- the paper-trading cockpit has a tighter, more dependable operator story
- shared run, portfolio, ledger, cash-flow, and reconciliation flows are easier to follow across workspaces
- the active web dashboard direction is better validated against real workflows, and retained WPF support remains clearly subordinate to Waves 1-4 rather than becoming a parallel program
- at least one concrete governance slice is clearly defined or landed on top of the delivered Security Master baseline
- the next follow-on slice remains clearly bounded to Waves 1-4, with Wave 5+, broader live-readiness claims, and optional advanced research / scale tracks still deferred
