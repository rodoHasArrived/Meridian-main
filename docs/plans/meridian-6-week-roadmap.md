# Meridian 6-Week Roadmap

**Last Updated:** 2026-04-26
**Horizon:** Next 6 weeks
**Status:** Short-horizon execution slice derived from the canonical roadmap and current DK readiness dashboard

This document is the six-week execution slice of [`ROADMAP.md`](../status/ROADMAP.md). It is intentionally narrower than the canonical roadmap and advances the active Wave 2-4 core operator-readiness path while keeping the closed Wave 1 trust gate synchronized.

Use this with [`waves-2-4-operator-readiness-addendum.md`](waves-2-4-operator-readiness-addendum.md) when assigning owners, sequencing dependencies, or checking workstream-level exit criteria inside the active Waves 2-4 path.

---

## Summary

The next six weeks should focus on four outcomes:

1. keep the closed Wave 1 provider-confidence and checkpoint-evidence gate green and complete operator review/sign-off for the DK1 pilot parity packet around the emitted Alpaca/Robinhood/Yahoo `pilotReplaySampleSet` and generated parity-packet artifacts
2. harden the Wave 2 paper-trading cockpit that is already visible in the workstation
3. deepen Wave 3 shared run / portfolio / ledger continuity across workspaces while consolidating the active WPF shell direction
4. land the first Wave 4 governance and fund-operations slices on top of the delivered Security Master baseline

Explicit non-goals in this window:

- Wave 5 Backtest Studio unification
- broader Wave 6 live integration readiness expansion beyond clarifying prerequisites
- optional advanced research / scale tracks such as deeper QuantScript expansion, L3 inference, multi-instance coordination, preferred-equity follow-ons, and Phase 16 performance work
- broad cleanup or parallel UX programs that do not directly move Waves 1-4

---

## Repo Constraints

This plan starts from the current repo state:

- the WPF workstation shell is the primary operator shell and is already organized around `Research`, `Trading`, `Data Operations`, and `Governance`; the retained local API/web surfaces remain supporting consumers of the same workstation contracts
- the current repo contains the WPF shell/navigation baseline in `ShellNavigationCatalog`, workspace shell pages, `MainPageViewModel`, `DesktopLaunchArguments` startup/deep-link parsing, deep-page hosting, context strips, shell/navigation smoke tests, and focused coverage for Batch Backtest, Position Blotter, Notification Center, Welcome, Activity Log triage, Watchlist posture, workspace queue tone styles, the workspace shell context strip, the Trading desk briefing hero, the Research desk briefing hero's run-detail / portfolio / promotion-review handoffs, the Data Operations desk briefing hero's provider / backfill / storage / session / export / environment-mode handoffs, the Provider Health posture briefing, local single-instance mutex plus launch-argument forwarding behavior, and workflow page-state automation markers, so this window should validate workflow value rather than start a second desktop UX track
- fixture/offline desktop workflow mode is now presented as neutral demo data and isolated workflow automation restores shared project assets without pinning the WPF target framework before building the desktop shell with the pinned WPF framework and confirming page tags, so test evidence should distinguish demo-state validation from operational readiness
- the paper-trading cockpit is partially productized, not greenfield, and now has a shared `/api/workstation/trading/readiness` contract for session, replay, controls, recent risk/control audit evidence, missing-field explainability warnings, promotion, DK1 trust-gate packet/sign-off projection, brokerage-sync, acceptance-gate/overall-readiness posture, and operator work items, with `PromotionApprovalChecklist` defining required review items for paper and live promotion approvals
- shared `StrategyRun`, portfolio, and ledger read services already exist and feed workstation surfaces
- promotion endpoints and workstation promotion surfaces are already in code
- Security Master is already the authoritative instrument-definition baseline across workstation and governance surfaces
- governance already has concrete seams for reconciliation, cash-flow summaries, reporting profiles, and direct-lending foundations
- the closed Wave 1 trust gate remains the first release gate for every downstream claim
- DK1 status is active but not closed: the latest generated parity packet is `ready-for-operator-review`, while operator sign-off plus workflow-facing explainability/calibration review and shared interop readiness are still in progress / at risk in `docs/status/kernel-readiness-dashboard.md`; promotion handoff is early in progress through cockpit audit-feedback hardening, export is early in progress through governed report-pack schema/version checks, and reconciliation DK2 is now early in progress through a file-backed break queue with review, resolve/dismiss, and audit-history routes

---

## Wave Alignment

### Active in this window

- **Wave 1:** closed trust-gate maintenance
- **Wave 2:** paper-trading cockpit hardening
- **Wave 3:** shared run / portfolio / ledger continuity
- **Wave 4:** governance and fund-operations productization on top of the delivered Security Master baseline

### Delivery guardrails in this window

- keep WPF workflow-first consolidation and MVVM extraction limited to work that directly supports active Wave 2-4 flows
- keep desktop launch/deep-link routing, screenshot workflows, single-instance forwarding, UI-automation page-state markers, and fixture/demo-mode cues aligned to the four workspace shell routes so automation verifies the same surfaces operators use
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
- the DK1 pilot replay/sample-set contract is emitted by `scripts/dev/run-wave1-provider-validation.ps1`, packaged by `scripts/dev/generate-dk1-pilot-parity-packet.ps1`, and reviewed through the DK1 pilot parity runbook
- backfill checkpoints, gap detection, and Parquet L2 flush behavior remain on the passing command matrix instead of drifting back into assumed reliability
- the active Wave 1 scope stays synchronized with the provider-validation matrix, provider-confidence language, and generated validation summaries
- the Data Operations desk briefing hero and Provider Health posture briefing remain consumers of shared provider, backfill, storage, session, and export state instead of becoming separate operational-readiness models

### Outcome 2: Wave 2 paper trading is dependable, not just visible

- the shared workstation cockpit is tightened around positions, orders, fills, replay, sessions, and risk flows already in code
- the WPF Trading desk briefing hero is validated as a consumer of shared active-run, workflow-summary, and operator-readiness state rather than a separate cockpit model
- `Backtest -> Paper` remains explicit, auditable, and easier to exercise end to end
- session persistence and replay behavior have clearer operator acceptance criteria
- the trading cockpit now surfaces a single operator acceptance contract for session persistence, replay confidence, audit/control evidence, risk/control explainability warnings, promotion-review readiness, DK1 trust posture, brokerage-sync posture, overall readiness, and operator work items
- the local replay-audit hardening slice now records replay consistency, compared fill/order/ledger evidence counts, last-persisted timestamps, and primary mismatch reason so readiness reconstruction has durable audit metadata to read from

### Outcome 3: Wave 3 shared-model continuity is stronger across workspaces

- `Research`, `Trading`, and `Governance` rely more consistently on the shared run, portfolio, and ledger model
- run comparison, fills, attribution, ledger, cash-flow, and reconciliation flows feel more like one system than adjacent slices
- WPF refinements in scope reinforce the same shared orchestration seams instead of introducing new page-local logic
- the Research desk briefing hero remains a shared-model consumer for selected runs, portfolio drill-ins, and `Backtest -> Paper` promotion review instead of becoming a separate research-only orchestration path
- current shell-navigation work is validated as a workflow-first improvement rather than just a visual reshuffle
- desktop launch/deep-link and screenshot workflow evidence uses the same canonical workspace tags operators use (`ResearchShell`, `TradingShell`, `DataOperationsShell`, `GovernanceShell`) and confirms page state through the hidden-but-present `ShellAutomationState` marker

### Outcome 4: Wave 4 governance work shows up as product, not just planning

- Security Master remains the delivered baseline while account/entity, reconciliation, cash-flow, multi-ledger, and reporting-adjacent workflows deepen on top of it
- the next governance slice is defined in terms of shared DTOs, read models, export seams, reconciliation break-queue state, and operator surfaces rather than a parallel governance stack

---

## Week-by-Week Plan

| Week | Focus | Goals | Deliverables |
| --- | --- | --- | --- |
| 1 | DK1 / Wave 1 closeout confirmation | rerun the closed trust gate and remove planning contradictions around the active provider set and pilot replay/sample standard | refreshed validation summary with `pilotReplaySampleSet`; generated `ready-for-operator-review` DK1 parity packet; synchronized provider/runtime evidence list; explicit deferred-provider wording; dashboard evidence links; operator sign-off follow-up |
| 2 | Wave 2 entry | keep the trust gate green while starting cockpit hardening | cockpit hardening checklist; operator acceptance targets tied back to the passing Wave 1 gate |
| 3 | Wave 2 operator lane | tighten the existing trading cockpit into a more dependable operator workflow | session and replay acceptance criteria; promotion approval/rejection rationale checklist; cockpit operator-path checklist |
| 4 | Wave 3 continuity | reduce cross-workspace seams between Research, Trading, and Governance while validating the active WPF shell direction | run-model continuity backlog; fills/attribution/ledger/reconciliation linkage notes; shell-navigation validation targets tied to active flows |
| 5 | Wave 4 governance slice | connect the delivered Security Master baseline to concrete governance product slices | account/entity and strategy-structure targets; first multi-ledger/cash-flow/reconciliation slice decisions; reporting/profile follow-ons |
| 6 | Hardening and closeout | make the six-week baseline easy to continue from without widening scope | docs/status refresh; acceptance-criteria review; narrowed follow-on backlog that still stays within Waves 1-4 |

---

## Workstreams

### Workstream A: Wave 1 trust-gate maintenance

Priorities:

- keep Alpaca and Yahoo explicit as repo-closed rows and Robinhood explicit as the only runtime-bounded active row
- keep the DK1 `pilotReplaySampleSet` synchronized across the validation script, generated summaries, generated parity packet, provider-validation matrix, and pilot parity runbook
- keep deferred-provider guidance synchronized so Polygon, Interactive Brokers, NYSE, and StockSharp do not drift back into the active gate by prose alone
- rerun `run-wave1-provider-validation.ps1` whenever provider, checkpoint, or Parquet proof surfaces change
- keep provider-confidence docs, runtime artifacts, and validation summaries synchronized

### Workstream B: Wave 2 paper-trading cockpit hardening

Priorities:

- harden the existing execution and promotion flows through the shared workstation readiness contract
- keep replay, session, audit, and risk behavior tied to realistic operator use
- keep recent risk/control audit evidence explainable with actor, scope, rationale, and operator-visible missing-field warnings
- prefer reliability and workflow continuity over new cockpit surface area
- define operator-visible acceptance criteria for the paper workflow already in code
- keep the cockpit readiness contract aligned with DK1 acceptance language so unresolved trust-gate sign-off, session, replay, audit, promotion-review, brokerage-sync, or operator-work-item gaps are visible during daily operation

### Workstream C: Wave 3 shared run / portfolio / ledger continuity

Priorities:

- deepen shared run services beyond a mostly backtest-first feel
- improve research-to-trading and trading-to-governance continuity
- keep Security Master enrichment tied to the same shared read-model seam
- use WPF workflow work only where it reinforces the same run-centered orchestration path

### Workstream D: Wave 4 governance and fund-operations productization

Priorities:

- keep Security Master authoritative while extending its use across governance workflows
- define the next concrete slices for account/entity, multi-ledger, cash-flow, reconciliation, and reporting work
- keep governance work grounded in shared DTOs, read models, and export seams rather than a separate subsystem

### Supporting discipline: Workflow-first WPF consolidation and validation

Priorities:

- prioritize high-traffic WPF pages and shell surfaces that directly support active cockpit, shared-model, or governance work
- continue MVVM extraction where pages still depend heavily on code-behind orchestration in active areas
- keep navigation, command-palette entries, and workspace framing aligned with the same workstation model used by the WPF shell and retained desktop-local API contracts
- treat the Trading desk briefing hero as Wave 2 support evidence only when it continues to reflect shared readiness, replay, controls, trust-gate, and brokerage-sync posture without duplicating service logic
- treat the Research desk briefing hero as Wave 3 support evidence only when it keeps selected-run, run-detail, portfolio, and paper-promotion handoffs tied to shared workstation read models
- treat the Data Operations desk briefing hero and Provider Health posture briefing as Wave 1/DK1 support evidence only when they keep provider, backfill, storage, session, and export handoffs tied to shared operational services
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

- provider/runtime guidance for the active Wave 1 gate remains reproducible and contradiction-free, including the emitted DK1 pilot replay/sample-set contract, generated parity packet, and explicit operator-signoff status
- backfill checkpoint and gap-handling confidence remains backed by passing evidence instead of only document claims
- the paper-trading cockpit has a tighter, more dependable operator story
- shared run, portfolio, ledger, cash-flow, and reconciliation flows are easier to follow across workspaces
- the active WPF shell direction is better validated against real workflows and remains clearly subordinate to Waves 1-4 rather than becoming a parallel program
- at least one concrete governance slice is clearly defined or landed on top of the delivered Security Master baseline
- the next follow-on slice remains clearly bounded to Waves 1-4, with Wave 5+, broader live-readiness claims, and optional advanced research / scale tracks still deferred
