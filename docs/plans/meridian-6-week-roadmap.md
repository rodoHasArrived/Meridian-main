# Meridian 6-Week Roadmap

**Last Updated:** 2026-03-21
**Horizon:** Next 6 weeks
**Status:** Current repo-grounded proposal

This plan assumes the current repository baseline, not the older partial-item baseline. C3, G2, I3, and J8 are already closed. The next six weeks should therefore focus on trust, workflow coherence, and operator-facing productization.

---

## Summary

The highest-value near-term work is:

1. strengthen provider confidence where operator trust still depends on replay evidence or runtime setup knowledge
2. turn the workstation vocabulary into a real workspace-first shell
3. deepen the shared run / portfolio / ledger baseline into broader research and trading workflows
4. establish the first governance/fund-operations product slices on top of the existing Security Master and ledger foundations

Out of scope for this six-week window:

- full QuantScript implementation
- full L3 inference/execution simulation implementation
- optional multi-instance scale-out completion
- Phase 16 assembly/SIMD optimization work

---

## Repo Constraints

- ingestion, WAL, and storage foundations are already in place and should be built on rather than rewritten
- workspace categories already use `Research`, `Trading`, `Data Operations`, and `Governance`, but navigation still carries a page-first registration model
- the first shared run/browser/detail/portfolio/ledger workstation flow exists, but it is still too backtest-first
- Security Master and coordination foundations exist in code, but neither is yet a fully productized operator capability
- provider readiness remains uneven enough that trust work still matters before broader workstation claims

---

## Six-Week Outcomes

### Outcome 1: Provider-confidence baseline is stronger

- broader replay/runtime confidence for Polygon, NYSE, IB, and StockSharp
- cleaner operator/setup guidance aligned with what the repo actually validates

### Outcome 2: Workstation shell feels intentional

- workspace-first navigation is more visible than page-first navigation
- orphan or weakly integrated pages are reduced or re-scoped
- shared run, portfolio, and ledger entry points are easier to discover and follow

### Outcome 3: Governance track starts showing up as product, not just planning

- Security Master and ledger-adjacent capabilities become clearer operator-facing surfaces
- governance workflows begin to connect with research/trading outputs instead of living only in blueprints

---

## Week-by-Week Plan

| Week | Focus | Goals | Deliverables |
|---|---|---|---|
| 1 | Provider trust and navigation inventory | remove ambiguity about the real supported provider/runtime paths and current workstation shell gaps | provider/runtime doc alignment pass; provider test-gap list; workstation navigation/orphan-page inventory |
| 2 | Workspace shell consolidation | make the workspace-first model more real in the desktop shell | clearer workspace landing structure; navigation/command-palette alignment; first orphan-page cleanup decisions |
| 3 | Shared run expansion | extend the current shared run baseline beyond a narrow backtest-first flow | broader run detail/query shape alignment; clearer cross-links from runs to portfolio and ledger surfaces |
| 4 | Portfolio and ledger productization | promote portfolio/ledger from supporting pages to stronger workflow surfaces | richer summary/drill-in path definition; governance-facing entry points from research/trading flows |
| 5 | Governance baseline | connect Security Master and governance planning to real product seams | Security Master workflow framing; first account/entity/reconciliation/reporting slice decisions and docs alignment |
| 6 | Hardening and closeout | make the new six-week baseline easy to understand and continue | docs/status refresh; remaining provider/runtime cleanup; next-wave backlog narrowed to concrete follow-on work |

---

## Workstreams

### Workstream A: Provider confidence

Priorities:

- expand Polygon replay coverage
- strengthen NYSE shared-lifecycle tests
- keep IB bootstrap/smoke-build guidance current
- keep StockSharp connector/runtime guidance aligned with validated paths

### Workstream B: Workstation shell

Priorities:

- move from page-list-first mental model toward workspace-first entry points
- reduce orphan navigation and weakly integrated pages
- keep command palette and navigation hierarchy aligned

### Workstream C: Shared run / portfolio / ledger

Priorities:

- broaden shared run services beyond backtest-first use
- improve research-to-portfolio and research-to-ledger workflow continuity
- make portfolio and ledger feel like primary workflow destinations

### Workstream D: Governance foundation

Priorities:

- make Security Master visible as an operator-facing platform seam
- define the first product slices for account/entity, reconciliation, and reporting work
- keep governance planning docs and status docs synchronized as those slices are promoted

---

## Risks

### Risk 1: Workspace polish outruns shared contracts

Mitigation:

- prefer shell and query/read-service improvements over large disconnected page rewrites

### Risk 2: Provider trust remains documentation-only

Mitigation:

- tie provider claims to replay evidence, smoke-build validation, and specific tested adapters

### Risk 3: Governance stays blueprint-heavy

Mitigation:

- require each governance planning step to identify at least one concrete code seam or operator-facing surface

### Risk 4: Too much broad cleanup crowds out product movement

Mitigation:

- keep cleanup adjacency-driven and focused on areas already changing for workstation/governance work

---

## Exit Criteria After 6 Weeks

- provider/runtime guidance is materially more trustworthy for the currently supported paths
- the workstation shell is more obviously workspace-first than page-first
- shared run, portfolio, and ledger workflows are easier to navigate and extend
- Security Master/governance work is represented by concrete product seams, not only blueprint text
- the next backlog wave is narrower, clearer, and less dependent on reconciliation prose
