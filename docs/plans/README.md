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

## Migration Rules

1. Do not add new long-form planning docs here by default.
2. Use `docs/product/` for stakeholder direction, `docs/engineering/` for implementation guidance, and `docs/roadmap/data/*.yml` for roadmap truth.
3. Leave redirect stubs for high-traffic paths only after replacement links exist.
4. Update [Documentation Inventory](../documentation-inventory.md) and the relevant archive bucket index with each archive batch.
5. Run roadmap/source/docs validation after any plan migration that changes canonical direction.
