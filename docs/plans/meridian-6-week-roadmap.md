# Meridian 6-Week Roadmap

**Last Updated:** 2026-04-07
**Horizon:** Next 6 weeks
**Status:** Current repo-grounded proposal aligned to the 2026-04-07 roadmap refresh

This plan assumes the current repository baseline, not the older page-shell or partial-foundation framing. The next six weeks should therefore focus on trust closure, cockpit hardening, shared-model continuity, and the first genuinely connected governance slices.

---

## Summary

The highest-value near-term work is:

1. close provider and backfill trust gaps with stronger replay, runtime, and checkpoint evidence
2. harden the paper-trading cockpit that is already present in the web workstation
3. deepen the shared run, portfolio, ledger, and reconciliation continuity now that the baseline is in code
4. keep the WPF workstation migration moving toward workflow-first orchestration on the highest-traffic pages
5. deepen governance and fund-operations workflows on top of the delivered Security Master seam, shared read models, and export stack

Out of scope for this six-week window:

- broad live-broker rollout or general live-trading readiness claims
- full Backtest Studio unification across native and Lean
- full report-pack and regulatory reporting implementation
- optional QuantScript expansion, L3 inference, multi-instance scale-out, and Phase 16 assembly optimization work

---

## Starting Point

This six-week plan starts from the current repo state:

- the workstation shell is active in web and materially aligned in WPF
- the React workstation already includes meaningful research, trading, data-operations, and governance flows
- the paper-trading cockpit is partially productized, not greenfield
- shared run, portfolio, and ledger read services already exist and feed workstation surfaces
- Security Master now acts as the authoritative instrument-definition seam across workstation and governance surfaces
- governance has concrete seams for reconciliation, cash-flow summaries, reporting profiles, and direct-lending foundations
- provider readiness remains uneven enough that trust work is still the main release gate

---

## Six-Week Outcomes

### Outcome 1: Provider-confidence baseline is materially stronger

- Polygon replay coverage, IB runtime/bootstrap validation, NYSE lifecycle-depth coverage, and StockSharp validated-adapter guidance are materially stronger and tied to executable evidence
- backfill checkpoints, gap detection, and Parquet L2 flush behavior move from assumed reliability to explicit pass/fail validation across representative windows
- the active Wave 1 scope is locked in [`provider-reliability-data-confidence-wave-1-blueprint.md`](provider-reliability-data-confidence-wave-1-blueprint.md) and kept synchronized with the validation matrix and provider-confidence baseline

### Outcome 2: Paper-trading workflow is dependable, not just visible

- the web workstation cockpit is tightened around positions, orders, fills, replay, and risk flows already in code
- `Backtest -> Paper` promotion remains explicit, auditable, and easier to exercise end to end
- session persistence and replay behavior have clearer acceptance criteria and fewer ambiguity gaps

### Outcome 3: Shared run continuity is stronger across workspaces

- research, trading, and governance all rely more consistently on the shared run, portfolio, and ledger model
- run comparison, fills, attribution, ledger, and reconciliation flows feel like one system rather than adjacent slices

### Outcome 4: Workstation shell feels more intentional

- workspace-first navigation and workflow entry points are stronger in active WPF migration areas
- high-traffic page work reduces page-local orchestration and improves operator continuity

### Outcome 5: Governance track shows up as product, not just planning

- Security Master remains the authoritative seam while reconciliation, portfolio, ledger, cash-flow, and reporting workflows deepen on top of it
- the next governance slice is defined in terms of shared DTOs, read models, and operator surfaces

---

## Week-by-Week Plan

| Week | Focus | Goals | Deliverables |
|---|---|---|---|
| 1 | Provider evidence refresh | close ambiguity around validated provider/runtime paths, checkpoint reliability, and L2 persistence evidence | Wave 1 blueprint; refreshed provider/runtime evidence list; narrowed checkpoint and Parquet test targets; exact open validation gaps |
| 2 | Cockpit hardening slice | tighten the existing trading cockpit into a more dependable operator lane | cockpit hardening checklist; session/replay acceptance criteria; promotion workflow gap list |
| 3 | Shared run continuity | reduce cross-workspace seams between research, trading, and governance | run-model continuity backlog; clearer fills/attribution/ledger/reconciliation linkage; query/read-model alignment notes |
| 4 | WPF workflow refresh | keep the desktop migration focused on workflow-first orchestration | high-traffic WPF page priority order; MVVM extraction targets; workspace-first navigation refinements |
| 5 | Governance productization seam | connect the delivered Security Master seam to concrete governance product slices | account/entity and strategy-structure targets; first multi-ledger/cash-flow/reconciliation slice decisions; reporting/profile follow-ons |
| 6 | Hardening and closeout | make the six-week baseline easy to continue from | docs/status refresh; acceptance criteria review; narrowed follow-on backlog for the next wave |

---

## Workstreams

### Workstream A: Provider trust

Priorities:

- expand Polygon replay coverage across feeds and edge cases
- validate Interactive Brokers runtime and bootstrap behavior against real vendor surfaces
- strengthen NYSE shared lifecycle, depth coverage, and explicit auth/rate-limit/cancellation proof
- harden NYSE transport behavior around `IHttpClientFactory`, websocket send, and resubscribe shutdown paths
- keep StockSharp examples aligned with the validated adapter set Meridian is prepared to recommend
- validate backfill checkpoints and gap handling across representative windows
- harden Parquet L2 flush-path behavior and close remaining ADR-014 cleanup tied to snapshot persistence

### Workstream B: Paper cockpit hardening

Priorities:

- harden the existing execution and promotion flows in the web workstation
- keep replay, session, and audit behavior tied to realistic operator use
- prefer reliability and workflow continuity over new cockpit surface area

### Workstream C: Shared run / portfolio / ledger continuity

Priorities:

- broaden shared run services beyond a mostly backtest-first feel
- improve research-to-trading and trading-to-governance continuity
- keep Security Master enrichment tied to the same shared read-model seam

### Workstream D: WPF workstation migration

Priorities:

- move high-traffic WPF surfaces toward workflow-first orchestration
- continue MVVM extraction where pages still depend heavily on code-behind
- keep navigation, command-palette, and workspace framing aligned

### Workstream E: Governance productization on top of Security Master

Priorities:

- keep Security Master authoritative while extending its use across governance workflows
- define the next concrete slices for multi-ledger, cash-flow, reconciliation, and reporting work
- keep governance work grounded in shared DTOs, read models, and export seams

---

## Risks

### Risk 1: Cockpit polish outruns evidence

Mitigation:

- prefer acceptance criteria, test coverage, and workflow hardening over adding more surface area

### Risk 2: Provider trust remains documentation-only

Mitigation:

- tie provider claims to replay evidence, runtime proof, and exact validation artifacts

### Risk 3: Governance stays blueprint-heavy

Mitigation:

- require each governance step to name at least one concrete read-model seam or operator-facing surface

### Risk 4: WPF work re-fragments the workstation

Mitigation:

- favor workflow services and view-model extraction over page-local orchestration

### Risk 5: Too much broad cleanup crowds out product movement

Mitigation:

- keep cleanup adjacency-driven and focused on areas already changing for trust, cockpit, or governance work

---

## Exit Criteria After 6 Weeks

- provider/runtime guidance is materially more trustworthy for the currently supported paths
- backfill checkpoint and gap-handling confidence is backed by clearer evidence
- the paper-trading cockpit has a tighter, more dependable operator story
- shared run, portfolio, ledger, and reconciliation flows are easier to follow across workspaces
- WPF workstation work is more obviously workflow-first on the highest-traffic pages
- Security Master-backed governance work is represented by concrete product seams, not only planning language
- the next backlog wave is narrower, clearer, and less dependent on document reconciliation
