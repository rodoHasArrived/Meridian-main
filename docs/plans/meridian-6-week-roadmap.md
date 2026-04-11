# Meridian 6-Week Roadmap

<<<<<<< HEAD
**Last Updated:** 2026-04-08
**Horizon:** Next 6 weeks
**Status:** Short-horizon execution slice derived from the 2026-04-08 canonical roadmap refresh

This document is the six-week execution slice of [`ROADMAP.md`](../status/ROADMAP.md). It is intentionally narrower than the canonical roadmap and advances only the Wave 1-4 core operator-readiness path: provider confidence, paper-trading cockpit hardening, shared-model continuity, and the first deeper governance slices on top of the delivered Security Master baseline.
=======
**Last Updated:** 2026-03-31
**Horizon:** Next 6 weeks
**Status:** Current repo-grounded proposal aligned to the 2026-03-31 roadmap/status refresh

This plan assumes the current repository baseline, not the older partial-item baseline. The next six weeks should therefore focus on provider trust, visible paper-trading workflows, active workstation refresh work, and the first operator-facing governance seams.
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

---

## Summary

The next six weeks should focus on four outcomes:

<<<<<<< HEAD
1. strengthen Wave 1 provider-confidence and checkpoint-evidence closure
2. harden the Wave 2 paper-trading cockpit that is already visible in the workstation
3. deepen Wave 3 shared run / portfolio / ledger continuity across workspaces
4. land the first Wave 4 governance and fund-operations slices on top of the delivered Security Master baseline
=======
1. Strengthen provider confidence where operator trust still depends on replay evidence or runtime setup knowledge
2. Turn the paper-trading gateway and promotion endpoints into visible cockpit workflows in the web dashboard
3. Keep the active WPF workstation refresh moving toward a clearer workspace-first shell
4. Deepen the shared run / portfolio / ledger baseline into broader research, trading, and governance workflows
5. Productize Security Master and the first governance/fund-operations seams on top of the existing ledger, reconciliation, and export foundations
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

Explicit non-goals in this window:

<<<<<<< HEAD
- Wave 5 Backtest Studio unification
- broader Wave 6 live integration readiness expansion beyond clarifying prerequisites
- optional advanced research / scale tracks such as deeper QuantScript expansion, L3 inference, multi-instance coordination, and Phase 16 performance work
- broad cleanup or parallel UX programs that do not directly move Waves 1-4
=======
- full live-broker validation and Paper -> Live production readiness
- full Backtest Studio unification across native + Lean
- full report-pack / regulatory reporting implementation
- optional QuantScript, L3 inference, multi-instance scale-out, and Phase 16 assembly optimization work
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

---

## Repo Constraints

<<<<<<< HEAD
This plan starts from the current repo state:

- the web and WPF workstation shells are active and already organized around `Research`, `Trading`, `Data Operations`, and `Governance`
- the paper-trading cockpit is partially productized, not greenfield
- shared `StrategyRun`, portfolio, and ledger read services already exist and feed workstation surfaces
- promotion endpoints and dashboard promotion surfaces are already in code
- Security Master is already the authoritative instrument-definition baseline across workstation and governance surfaces
- governance already has concrete seams for reconciliation, cash-flow summaries, reporting profiles, and direct-lending foundations
- provider trust and checkpoint proof remain the first release gate for every downstream claim

---

## Wave Alignment

### Active in this window

- **Wave 1:** provider confidence and checkpoint evidence
- **Wave 2:** paper-trading cockpit hardening
- **Wave 3:** shared run / portfolio / ledger continuity
- **Wave 4:** governance and fund-operations productization on top of the delivered Security Master baseline

### Delivery guardrails in this window

- keep WPF workflow-first consolidation and MVVM extraction limited to work that directly supports active Wave 2-4 flows
- keep validation and documentation synchronized with executable evidence, not summary language
- keep shared DTOs, read models, workflow services, and export seams as the integration boundary across active work

### Explicitly deferred beyond this window

- **Wave 5:** Backtest Studio unification across native and Lean
- **Wave 6:** live integration readiness, except where Wave 1-2 work clarifies prerequisites
- optional advanced research / scale tracks
=======
- Ingestion, WAL, and storage foundations are already in place and should be built on rather than rewritten
- Workspace categories already use `Research`, `Trading`, `Data Operations`, and `Governance`, but navigation still carries a page-first registration model
- The first shared run/browser/detail/portfolio/ledger workstation flow exists, but it is still too backtest-first and needs broader paper/governance continuity
- **Brokerage gateway framework** (Alpaca, IB, StockSharp adapters) is now implemented, and the paper-trading/promotion REST endpoints are wired — the next step is visible cockpit UX rather than more hidden infrastructure
- Security Master foundations exist in code, including corporate actions, conflict handling, bulk ingest, and a WPF browser, but the shared operator-facing product layer is still incomplete
- The governance blueprint is active, so six-week work should prefer concrete shared DTO/read-model slices over more blueprint-only expansion
- Provider readiness remains uneven enough that trust work still matters before broader workstation claims
- Direct lending module is operational with PostgreSQL persistence, but governance-grade reporting and reconciliation integration remain
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

---

## Six-Week Outcomes

<<<<<<< HEAD
### Outcome 1: Wave 1 trust gates are materially stronger

- Polygon replay coverage, IB runtime/bootstrap validation, NYSE lifecycle-depth coverage, and StockSharp validated-adapter guidance are tied to clearer executable evidence
- backfill checkpoints, gap detection, and Parquet L2 flush behavior move from assumed reliability to explicit pass/fail validation across representative windows
- the active Wave 1 scope stays synchronized with the provider-validation matrix and provider-confidence language

### Outcome 2: Wave 2 paper trading is dependable, not just visible

- the web workstation cockpit is tightened around positions, orders, fills, replay, sessions, and risk flows already in code
- `Backtest -> Paper` remains explicit, auditable, and easier to exercise end to end
- session persistence and replay behavior have clearer operator acceptance criteria

### Outcome 3: Wave 3 shared-model continuity is stronger across workspaces

- `Research`, `Trading`, and `Governance` rely more consistently on the shared run, portfolio, and ledger model
- run comparison, fills, attribution, ledger, cash-flow, and reconciliation flows feel more like one system than adjacent slices
- WPF refinements in scope reinforce the same shared orchestration seams instead of introducing new page-local logic

### Outcome 4: Wave 4 governance work shows up as product, not just planning

- Security Master remains the delivered baseline while account/entity, reconciliation, cash-flow, multi-ledger, and reporting-adjacent workflows deepen on top of it
- the next governance slice is defined in terms of shared DTOs, read models, export seams, and operator surfaces rather than a parallel governance stack
=======
### Outcome 1: Provider-confidence baseline is stronger

- broader replay/runtime confidence for Polygon, NYSE, IB, and StockSharp
- cleaner operator/setup guidance aligned with what the repo actually validates

### Outcome 2: Paper-trading workflow is visible and coherent

- the web dashboard exposes positions, orders, fills, P&L, and risk state as an actual cockpit surface
- `Backtest -> Paper` promotion is visible to operators rather than hidden behind only REST endpoints
- session replay and audit trail requirements are narrowed to the next concrete slice

### Outcome 3: Workstation shell feels intentional

- workspace-first navigation is more visible than page-first navigation
- orphan or weakly integrated pages are reduced or re-scoped
- shared run, portfolio, and ledger entry points are easier to discover and follow

### Outcome 4: Governance track starts showing up as product, not just planning

- Security Master becomes easier to browse, join, and reason about from operator-facing surfaces
- governance workflows begin to connect with research/trading outputs instead of living only in blueprints
- the next governance slice is defined in terms of shared DTOs, read models, and UI seams rather than aspirational categories
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

---

## Week-by-Week Plan

| Week | Focus | Goals | Deliverables |
|---|---|---|---|
<<<<<<< HEAD
| 1 | Wave 1 evidence refresh | close ambiguity around validated provider/runtime paths, checkpoint reliability, and L2 persistence evidence | refreshed provider/runtime evidence list; narrowed checkpoint and Parquet test targets; exact open validation gaps |
| 2 | Wave 1 closeout + Wave 2 entry | turn trust work into explicit pass/fail gates while starting cockpit hardening | provider-confidence decision list; checkpoint validation targets; cockpit hardening checklist |
| 3 | Wave 2 operator lane | tighten the existing trading cockpit into a more dependable operator workflow | session and replay acceptance criteria; promotion workflow gap list; cockpit operator-path checklist |
| 4 | Wave 3 continuity | reduce cross-workspace seams between Research, Trading, and Governance | run-model continuity backlog; fills/attribution/ledger/reconciliation linkage notes; WPF extraction targets tied to active flows |
| 5 | Wave 4 governance slice | connect the delivered Security Master baseline to concrete governance product slices | account/entity and strategy-structure targets; first multi-ledger/cash-flow/reconciliation slice decisions; reporting/profile follow-ons |
| 6 | Hardening and closeout | make the six-week baseline easy to continue from without widening scope | docs/status refresh; acceptance criteria review; narrowed follow-on backlog that still stays within Waves 1-4 |
=======
| 1 | Provider-confidence closeout and cockpit scope | remove ambiguity about supported provider/runtime paths and narrow the paper-cockpit slice | provider/runtime evidence refresh; provider test-gap list; cockpit panel/service contract inventory |
| 2 | Paper cockpit first slice | make paper trading visible in the web dashboard | positions/orders/fills/risk panel wiring plan; promotion-flow UX contract; replay/audit follow-on list |
| 3 | Workspace shell consolidation | keep the workspace-first model moving in the desktop shell | clearer workspace landing structure; navigation/command-palette alignment; first orphan-page cleanup decisions |
| 4 | Shared run / portfolio / ledger expansion | extend the current shared baseline beyond a narrow backtest-first flow | broader run detail/query shape alignment; clearer cross-links from runs to portfolio, ledger, and governance surfaces |
| 5 | Security Master and governance operator seams | connect Security Master and governance planning to concrete product slices | Security Master workflow framing; enrichment targets for shared read models; first multi-ledger/reconciliation/reporting slice decisions |
| 6 | Hardening and closeout | make the new six-week baseline easy to understand and continue | docs/status refresh; remaining provider/runtime cleanup; next-wave backlog narrowed to concrete follow-on work |
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

---

## Workstreams

<<<<<<< HEAD
### Workstream A: Wave 1 provider confidence and checkpoint evidence

Priorities:

- expand Polygon replay coverage across feeds and edge cases
- validate Interactive Brokers runtime and bootstrap behavior against real vendor surfaces
- strengthen NYSE shared lifecycle, depth coverage, and explicit auth, rate-limit, cancellation, and transport proof
- keep StockSharp examples and runtime guidance aligned with the validated adapter set Meridian is prepared to recommend
- validate backfill checkpoints and gap handling across representative providers and longer windows
- harden Parquet L2 flush-path behavior and close remaining ADR-014 cleanup tied to snapshot persistence
- keep provider-confidence docs and validation artifacts synchronized

### Workstream B: Wave 2 paper-trading cockpit hardening

Priorities:

- harden the existing execution and promotion flows in the web workstation
- keep replay, session, audit, and risk behavior tied to realistic operator use
- prefer reliability and workflow continuity over new cockpit surface area
- define operator-visible acceptance criteria for the paper workflow already in code

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
=======
### Workstream A: Provider confidence

Priorities:

- expand Polygon replay coverage
- strengthen NYSE shared-lifecycle tests
- keep IB bootstrap/smoke-build guidance current
- keep StockSharp connector/runtime guidance aligned with validated paths

### Workstream B: Paper cockpit and promotion flow

Priorities:

- expose the existing execution and promotion endpoints through an operator-friendly dashboard flow
- define the minimum viable cockpit panels before broader live-trading ambitions
- keep replay, audit, and safety-gate work sequenced behind visible operator value

### Workstream C: Workstation shell

Priorities:

- move from page-list-first mental model toward workspace-first entry points
- reduce orphan navigation and weakly integrated pages
- keep command palette and navigation hierarchy aligned

### Workstream D: Shared run / portfolio / ledger

Priorities:

- broaden shared run services beyond backtest-first use
- improve research-to-portfolio and research-to-ledger workflow continuity
- make portfolio and ledger feel like primary workflow destinations
- define the next governance joins on top of those same shared models
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

### Supporting discipline: Workflow-first WPF consolidation and validation

Priorities:

<<<<<<< HEAD
- prioritize high-traffic WPF pages that directly support active cockpit, shared-model, or governance work
- continue MVVM extraction where pages still depend heavily on code-behind orchestration in active areas
- keep navigation, command-palette entries, and workspace framing aligned with the same workstation model used in the web shell
- pull validation and contradiction checks forward whenever workstation or governance surfaces expand
=======
- make Security Master visible as an operator-facing platform seam across dashboard and workstation flows
- define the first product slices for multi-ledger, reconciliation, and reporting work
- keep governance planning docs and status docs synchronized as those slices are promoted
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

---

## Risks

### Risk 1: Cockpit polish outruns execution/read-model contracts

Mitigation:

<<<<<<< HEAD
- keep Wave 1 trust closure ahead of broad cockpit claims and tie Wave 2 acceptance criteria to real evidence
=======
- prefer panel wiring and shared query/read-service improvements over disconnected dashboard polish
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

### Risk 2: Provider trust remains documentation-only

Mitigation:

<<<<<<< HEAD
- require replay evidence, runtime proof, or explicit gap documentation for every provider claim carried forward
=======
- tie provider claims to replay evidence, smoke-build validation, and specific tested adapters
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

### Risk 3: Governance stays blueprint-heavy

Mitigation:

<<<<<<< HEAD
- require each Wave 4 step to name at least one shared read-model seam and one operator-facing surface
=======
- require each governance planning step to identify at least one concrete code seam or operator-facing surface
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

### Risk 4: Workstation polish outruns shared contracts

Mitigation:

<<<<<<< HEAD
- favor workflow services and view-model extraction over page-local orchestration and limit WPF work to active-wave support
=======
- prefer shell and query/read-service improvements over large disconnected page rewrites
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

### Risk 5: Too much broad cleanup crowds out product movement

Mitigation:

<<<<<<< HEAD
- keep cleanup adjacency-driven and focused on areas already changing for trust, cockpit, shared-model, or governance work
=======
- keep cleanup adjacency-driven and focused on areas already changing for workstation/governance work
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

---

## Exit Criteria After 6 Weeks

- provider/runtime guidance is materially more trustworthy for the currently supported paths
<<<<<<< HEAD
- backfill checkpoint and gap-handling confidence is backed by clearer evidence
- the paper-trading cockpit has a tighter, more dependable operator story
- shared run, portfolio, ledger, cash-flow, and reconciliation flows are easier to follow across workspaces
- at least one concrete governance slice is clearly defined or landed on top of the delivered Security Master baseline
- the next follow-on slice remains clearly bounded to Waves 1-4, with Wave 5+, broader live-readiness claims, and optional advanced research / scale tracks still deferred
=======
- the paper-trading cockpit has an agreed first visible slice and promotion-flow contract
- the workstation shell is more obviously workspace-first than page-first
- shared run, portfolio, and ledger workflows are easier to navigate and extend
- Security Master/governance work is represented by concrete product seams, not only blueprint text
- the next backlog wave is narrower, clearer, and less dependent on reconciliation prose
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe
