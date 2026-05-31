# Plans Source-Material Index

**Status:** migration-source
**Owner:** core-team
**Reviewed:** 2026-05-30

This folder is retained as source material for implementation plans, blueprints, target-state designs, and historical execution roadmaps. It is no longer the canonical documentation front door for planning in the rebuild.

Use current guidance first:

- [Documentation Front Door](../README.md)
- [Product Documentation](../product/README.md)
- [Engineering Documentation](../engineering/README.md)
- [Roadmap Registry](../roadmap/README.md)
- [Documentation Ownership Contract](../documentation-ownership.md)
- [Plans Archive](../../archive/docs/plans/README.md)

## Current Role In The Rebuild

- Treat plan files as source material unless linked from `docs/product/`, `docs/engineering/`, `docs/operators/`, `docs/reference/`, `docs/roadmap/`, or `docs/README.md` as current guidance.
- Keep durable roadmap truth in `docs/roadmap/data/*.yml` and generated roadmap views.
- Extract verified-current product direction into `docs/product/`.
- Extract durable architecture, validation, and implementation rules into `docs/engineering/` or `docs/reference/`.
- Archive completed, superseded, abandoned, or historical plans in `archive/docs/plans/` or `archive/docs/summaries/`.

## Current Planning Inputs To Mine First

| Document | Target lane | Notes |
| --- | --- | --- |
| [current-direction-and-status.md](current-direction-and-status.md) | product/roadmap | Consolidated planning interpretation; mine for product/status summary while preserving evidence caveats. |
| [evidence-backed-investment-operations-plan.md](evidence-backed-investment-operations-plan.md) | product | Product-category filter for the rebuilt stakeholder docs. |
| [web-ui-development-pivot.md](web-ui-development-pivot.md) | engineering/product | Browser/desktop coexistence context; extract shared-contract UI rules. |
| [governance-fund-ops-blueprint.md](governance-fund-ops-blueprint.md) | product/engineering/operators | Wave 4 governance, reconciliation, and reporting direction. |
| [wave-implementation-checklists.md](wave-implementation-checklists.md) | product/roadmap/archive | Checklist content must be reconciled against current roadmap registry and evidence gates. |
| [desktop-ui-workflow-acceptance-matrix.md](desktop-ui-workflow-acceptance-matrix.md) | engineering/operators | WPF/browser acceptance rules; extract current validation requirements before archive. |
| [ufl-supported-assets-index.md](ufl-supported-assets-index.md) | reference/product | UFL target-state index; migrate durable lookup facts to reference/product lanes. |

## Later Migration Groups

| Group | Examples | Migration direction |
| --- | --- | --- |
| Core operator readiness | W2/W3/W4 plans, cockpit, governance, ledger, portfolio, report-pack plans | Extract current direction to product/engineering, then archive superseded implementation narratives. |
| Later-wave productization | Backtest Studio, portfolio backtesting, options, database, adapter completion | Reconcile with roadmap registry before keeping active. |
| UFL and asset profiles | UFL target-state and conformance docs | Move stable lookup material to reference; archive draft target-state narratives when replaced. |
| Technical/optional tracks | Cleanup, kernel parity, L3, performance, desktop modularity | Keep only when linked from engineering or roadmap; otherwise archive as plans. |

## Archived Plan Redirects

| Document | Archive copy | Reason |
| --- | --- | --- |
| [Desktop Shell Modularity and Extensibility Roadmap](desktop-shell-modularity-roadmap.md) | [archive/docs/plans/desktop-shell-modularity-roadmap.md](../../archive/docs/plans/desktop-shell-modularity-roadmap.md) | Historical WPF shell modularity roadmap retained for rationale; current architecture guidance now lives in `docs/engineering/README.md` and active source code. |
| [Performance TODO 2026-05-21](performance-todo-2026-05-21.md) | [archive/docs/plans/performance-todo-2026-05-21.md](../../archive/docs/plans/performance-todo-2026-05-21.md) | Environment-specific benchmark blocker/TODO snapshot; current performance work requires engineering guidance plus fresh benchmark evidence. |
| [Kernel Parity Migration Blueprint](kernel-parity-migration-blueprint.md) | [archive/docs/plans/kernel-parity-migration-blueprint.md](../../archive/docs/plans/kernel-parity-migration-blueprint.md) | Optional parity-program blueprint retained for historical context; current parity status remains evidence-driven through active CI/status reporting. |
| [Paper Trading Cockpit Reliability Sprint](paper-trading-cockpit-reliability-sprint.md) | [archive/docs/plans/paper-trading-cockpit-reliability-sprint.md](../../archive/docs/plans/paper-trading-cockpit-reliability-sprint.md) | Completed Wave 2 detail contract; current status belongs in product docs, roadmap registry, and retained evidence packets. |
| [L3 Inference Implementation Plan](l3-inference-implementation-plan.md) | [archive/docs/plans/l3-inference-implementation-plan.md](../../archive/docs/plans/l3-inference-implementation-plan.md) | Historical L3 queue-aware execution plan retained for rationale; active L3 priorities and acceptance evidence now belong in status/registry-driven planning and implementation evidence. |
| [Meridian Database Blueprint](meridian-database-blueprint.md) | [archive/docs/plans/meridian-database-blueprint.md](../../archive/docs/plans/meridian-database-blueprint.md) | Historical database blueprint archived for rationale; active database architecture guidance is maintained in canonical engineering docs and status/risk evidence. |
| [Options Functionality Roadmap](options-roadmap.md) | [archive/docs/plans/options-roadmap.md](../../archive/docs/plans/options-roadmap.md) | Implementation roadmap retained for historical reference; active options priorities should use registry/status-driven planning evidence. |
| [Runbook Template Registry Modernization](runbook-template-registry-modernization-plan.md) | [archive/docs/plans/runbook-template-registry-modernization-plan.md](../../archive/docs/plans/runbook-template-registry-modernization-plan.md) | Draft/speculative platform plan; current runbook scope must come from operator guidance, engineering contracts, and roadmap registry evidence. |
| [Portfolio-Level Backtesting Composer Blueprint](portfolio-level-backtesting-composer-blueprint.md) | [archive/docs/plans/portfolio-level-backtesting-composer-blueprint.md](../../archive/docs/plans/portfolio-level-backtesting-composer-blueprint.md) | Proposed future composer scope; current Backtest Studio work should stay aligned to Wave 5 unification unless registry evidence pulls this forward. |
| [Assembly-Level Performance Roadmap](assembly-performance-roadmap.md) | [archive/docs/plans/assembly-performance-roadmap.md](../../archive/docs/plans/assembly-performance-roadmap.md) | Optional advanced-performance track; current performance work requires current source and benchmark evidence. |
| [SFO MVP Implementation Design](sfo-mvp-implementation-design.md) | [archive/docs/plans/sfo-mvp-implementation-design.md](../../archive/docs/plans/sfo-mvp-implementation-design.md) | Historical SFO implementation design retained for rationale; active execution posture is now maintained in canonical product/engineering/docs and status evidence. |
| [Meridian Pilot Workflow](meridian-pilot-workflow.md) | [archive/docs/plans/meridian-pilot-workflow.md](../../archive/docs/plans/meridian-pilot-workflow.md) | Historical pilot workflow planning retained for archival context; active pilot-path claims and prioritization now come from product/roadmap status surfaces. |
| [Trading Workstation Migration Blueprint](trading-workstation-migration-blueprint.md) | [archive/docs/plans/trading-workstation-migration-blueprint.md](../../archive/docs/plans/trading-workstation-migration-blueprint.md) | Historical workstation migration narrative retained; current evidence and execution posture are tracked in canonical product/engineering/status artifacts. |
| [Fund Management PR-Sequenced Roadmap](fund-management-pr-sequenced-roadmap.md) | [archive/docs/plans/fund-management-pr-sequenced-roadmap.md](../../archive/docs/plans/fund-management-pr-sequenced-roadmap.md) | Historical PR-sequenced fund-management roadmap retained; active priorities and sequencing are extracted into canonical planning/status evidence. |
| [Backtest Studio Unification Blueprint](backtest-studio-unification-blueprint.md) | [archive/docs/plans/backtest-studio-unification-blueprint.md](../../archive/docs/plans/backtest-studio-unification-blueprint.md) | Historic backtest-studio planning narrative retained for context; active W5 unification work should remain registry-aligned. |
| [Backtest Studio PR-Sequenced Roadmap](backtest-studio-unification-pr-sequenced-roadmap.md) | [archive/docs/plans/backtest-studio-unification-pr-sequenced-roadmap.md](../../archive/docs/plans/backtest-studio-unification-pr-sequenced-roadmap.md) | Historical sequencing draft retained for context; active Backtest Studio posture is now tracked via roadmap registry and current status artifacts. |
| [Approach B+ v2 Implementation Plan](approach-b-plus-v2-implementation-plan.md) | [archive/docs/plans/approach-b-plus-v2-implementation-plan.md](../../archive/docs/plans/approach-b-plus-v2-implementation-plan.md) | Draft v2 implementation sequencing retained for historical planning context; active direction remains registry-driven and evidence-backed through current product/operator/engineering docs. |
| [Covered-Call Writing Slice 1 Blueprint](covered-call-writing-slice-1-blueprint.md) | [archive/docs/plans/covered-call-writing-slice-1-blueprint.md](../../archive/docs/plans/covered-call-writing-slice-1-blueprint.md) | Option-writing implementation blueprint retained as historical reference; current options priorities are determined by roadmap and execution evidence. |
| [Quantscript L3 Multi-Instance Round 2 Roadmap](quantscript-l3-multiinstance-round2-roadmap.md) | [archive/docs/plans/quantscript-l3-multiinstance-round2-roadmap.md](../../archive/docs/plans/quantscript-l3-multiinstance-round2-roadmap.md) | Historical L3 multi-instance plan retained; active priorities now come from registry-backed engineering status and active execution evidence. |
| [Waves 2-4 Operator Readiness Addendum](waves-2-4-operator-readiness-addendum.md) | [archive/docs/plans/waves-2-4-operator-readiness-addendum.md](../../archive/docs/plans/waves-2-4-operator-readiness-addendum.md) | Legacy operator-readiness snapshot retained for background; current operator posture remains in canonical operator docs and active evidence lanes. |
| [Research Backtest Trust and Velocity Blueprint](research-backtest-trust-and-velocity-blueprint.md) | [archive/docs/plans/research-backtest-trust-and-velocity-blueprint.md](../../archive/docs/plans/research-backtest-trust-and-velocity-blueprint.md) | Historical research/trust workflow planning retained for context; active backtest priorities are now governed by roadmap registry evidence and product guidance. |
| [Desktop UI Workflow Acceptance Matrix](desktop-ui-workflow-acceptance-matrix.md) | [archive/docs/plans/desktop-ui-workflow-acceptance-matrix.md](../../archive/docs/plans/desktop-ui-workflow-acceptance-matrix.md) | WPF acceptance criteria planning retained as historical context; active operator validation posture lives in canonical engineering and operator guidance. |
| [Desktop Workstation Screen Blueprint](desktop-workstation-screen-blueprint.md) | [archive/docs/plans/desktop-workstation-screen-blueprint.md](../../archive/docs/plans/desktop-workstation-screen-blueprint.md) | Historical workstation screen blueprint retained for reference; current UI requirements are derived from canonical engineering and product guidance. |
| [Entity-Aware Workstation Capability Blueprint](entity-aware-workstation-capability-blueprint.md) | [archive/docs/plans/entity-aware-workstation-capability-blueprint.md](../../archive/docs/plans/entity-aware-workstation-capability-blueprint.md) | Historical workstation capability planning retained as background; active capability guidance remains in canonical engineering/operators layers. |
| [Web UI Development Pivot](web-ui-development-pivot.md) | [archive/docs/plans/web-ui-development-pivot.md](../../archive/docs/plans/web-ui-development-pivot.md) | Historical browser/desktop pivot planning retained; active product strategy remains evidence-backed through current product and engineering materials. |
| [Brokerage Portfolio Sync Blueprint](brokerage-portfolio-sync-blueprint.md) | [archive/docs/plans/brokerage-portfolio-sync-blueprint.md](../../archive/docs/plans/brokerage-portfolio-sync-blueprint.md) | Historical portfolio-sync planning retained; active provider and synchronization status now lives in canonical operator/docs and roadmap evidence. |
| [Fund Management Module Implementation Backlog](fund-management-module-implementation-backlog.md) | [archive/docs/plans/fund-management-module-implementation-backlog.md](../../archive/docs/plans/fund-management-module-implementation-backlog.md) | Historical module backlog retained as planning context; active module-level execution now lives in canonical planning/status evidence. |
| [Codebase Audit Cleanup Roadmap](codebase-audit-cleanup-roadmap.md) | [archive/docs/plans/codebase-audit-cleanup-roadmap.md](../../archive/docs/plans/codebase-audit-cleanup-roadmap.md) | Historical cleanup roadmap retained for traceability; active cleanup posture should be managed through registry-backed evidence and canonical operator/engineering guidance. |
| [Meridian 6-Week Roadmap](meridian-6-week-roadmap.md) | [archive/docs/plans/meridian-6-week-roadmap.md](../../archive/docs/plans/meridian-6-week-roadmap.md) | Historical short-horizon slice retained; active roadmap posture now lives in canonical roadmap registry and product/operator summaries. |
| [Wave Implementation Checklists](wave-implementation-checklists.md) | [archive/docs/plans/wave-implementation-checklists.md](../../archive/docs/plans/wave-implementation-checklists.md) | Historical wave checklist ledger retained; active TODO and evidence gates now in canonical product/roadmap status artifacts. |

## Migration Rules

1. Do not add new long-form planning docs here by default.
2. Use `docs/product/` for stakeholder direction, `docs/engineering/` for implementation guidance, and `docs/roadmap/data/*.yml` for roadmap truth.
3. Leave redirect stubs for high-traffic paths only after replacement links exist.
4. Update [Documentation Inventory](../documentation-inventory.md) and the relevant archive bucket index with each archive batch.
5. Run roadmap/source/docs validation after any plan migration that changes canonical direction.
