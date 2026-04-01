# Meridian 6-Week Roadmap

**Last Updated:** 2026-03-31
**Horizon:** Next 6 weeks
**Status:** Current repo-grounded proposal aligned to the 2026-03-31 roadmap/status refresh

This plan assumes the current repository baseline, not the older partial-item baseline. The next six weeks should therefore focus on provider trust, visible paper-trading workflows, active workstation refresh work, and the first operator-facing governance seams.

---

## Summary

The highest-value near-term work is:

1. Strengthen provider confidence where operator trust still depends on replay evidence or runtime setup knowledge
2. Turn the paper-trading gateway and promotion endpoints into visible cockpit workflows in the web dashboard
3. Keep the active WPF workstation refresh moving toward a clearer workspace-first shell
4. Deepen the shared run / portfolio / ledger baseline into broader research, trading, and governance workflows
5. Productize Security Master and the first governance/fund-operations seams on top of the existing ledger, reconciliation, and export foundations

Out of scope for this six-week window:

- full live-broker validation and Paper -> Live production readiness
- full Backtest Studio unification across native + Lean
- full report-pack / regulatory reporting implementation
- optional QuantScript, L3 inference, multi-instance scale-out, and Phase 16 assembly optimization work

---

## Repo Constraints

- Ingestion, WAL, and storage foundations are already in place and should be built on rather than rewritten
- Workspace categories already use `Research`, `Trading`, `Data Operations`, and `Governance`, but navigation still carries a page-first registration model
- The first shared run/browser/detail/portfolio/ledger workstation flow exists, but it is still too backtest-first and needs broader paper/governance continuity
- **Brokerage gateway framework** (Alpaca, IB, StockSharp adapters) is now implemented, and the paper-trading/promotion REST endpoints are wired — the next step is visible cockpit UX rather than more hidden infrastructure
- Security Master foundations exist in code, including corporate actions, conflict handling, bulk ingest, and a WPF browser, but the shared operator-facing product layer is still incomplete
- The governance blueprint is active, so six-week work should prefer concrete shared DTO/read-model slices over more blueprint-only expansion
- Provider readiness remains uneven enough that trust work still matters before broader workstation claims
- Direct lending module is operational with PostgreSQL persistence, but governance-grade reporting and reconciliation integration remain

---

## Six-Week Outcomes

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

---

## Week-by-Week Plan

| Week | Focus | Goals | Deliverables |
|---|---|---|---|
| 1 | Provider-confidence closeout and cockpit scope | remove ambiguity about supported provider/runtime paths and narrow the paper-cockpit slice | provider/runtime evidence refresh; provider test-gap list; cockpit panel/service contract inventory |
| 2 | Paper cockpit first slice | make paper trading visible in the web dashboard | positions/orders/fills/risk panel wiring plan; promotion-flow UX contract; replay/audit follow-on list |
| 3 | Workspace shell consolidation | keep the workspace-first model moving in the desktop shell | clearer workspace landing structure; navigation/command-palette alignment; first orphan-page cleanup decisions |
| 4 | Shared run / portfolio / ledger expansion | extend the current shared baseline beyond a narrow backtest-first flow | broader run detail/query shape alignment; clearer cross-links from runs to portfolio, ledger, and governance surfaces |
| 5 | Security Master and governance operator seams | connect Security Master and governance planning to concrete product slices | Security Master workflow framing; enrichment targets for shared read models; first multi-ledger/reconciliation/reporting slice decisions |
| 6 | Hardening and closeout | make the new six-week baseline easy to understand and continue | docs/status refresh; remaining provider/runtime cleanup; next-wave backlog narrowed to concrete follow-on work |

---

## Workstreams

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

### Workstream E: Governance foundation

Priorities:

- make Security Master visible as an operator-facing platform seam across dashboard and workstation flows
- define the first product slices for multi-ledger, reconciliation, and reporting work
- keep governance planning docs and status docs synchronized as those slices are promoted

---

## Risks

### Risk 1: Cockpit polish outruns execution/read-model contracts

Mitigation:

- prefer panel wiring and shared query/read-service improvements over disconnected dashboard polish

### Risk 2: Provider trust remains documentation-only

Mitigation:

- tie provider claims to replay evidence, smoke-build validation, and specific tested adapters

### Risk 3: Governance stays blueprint-heavy

Mitigation:

- require each governance planning step to identify at least one concrete code seam or operator-facing surface

### Risk 4: Workstation polish outruns shared contracts

Mitigation:

- prefer shell and query/read-service improvements over large disconnected page rewrites

### Risk 5: Too much broad cleanup crowds out product movement

Mitigation:

- keep cleanup adjacency-driven and focused on areas already changing for workstation/governance work

---

## Exit Criteria After 6 Weeks

- provider/runtime guidance is materially more trustworthy for the currently supported paths
- the paper-trading cockpit has an agreed first visible slice and promotion-flow contract
- the workstation shell is more obviously workspace-first than page-first
- shared run, portfolio, and ledger workflows are easier to navigate and extend
- Security Master/governance work is represented by concrete product seams, not only blueprint text
- the next backlog wave is narrower, clearer, and less dependent on reconciliation prose
