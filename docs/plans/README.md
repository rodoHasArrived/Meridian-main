# Plans

**Last Reviewed:** 2026-04-29

This directory contains active blueprints, roadmaps, sprint backlogs, and target-state packages that support Meridian delivery. The canonical wave model lives in [../status/ROADMAP.md](../status/ROADMAP.md); treat the documents here as subordinate execution plans, blueprints, or optional-track follow-ons rather than parallel strategy documents.

Current roadmap refresh note: the 2026-04-29 direction positions Meridian as an evidence-backed investment operations platform. New operator UI work stays in the browser dashboard in `src/Meridian.Ui/dashboard/`; WPF plans and automation remain retained support unless a change is required for shared contracts, regression fixes, or compatibility evidence. Waves 2-4 still require cockpit, shared-model, accounting, reconciliation, and governed-report acceptance evidence, now with web-first UI delivery.

Deprecated, superseded, or historical plans belong under `archive/docs/` rather than in this active plans folder. Keep active plan references current, and point to archived files only for historical context.

## Core Operator-Ready Path (Waves 1-4)

These are the plan documents that directly support the current path to the core operator-ready baseline.

| Document | Purpose |
| --- | --- |
| [evidence-backed-investment-operations-plan.md](evidence-backed-investment-operations-plan.md) | Active product-category filter: trusted data -> research -> paper -> books -> reconciliation -> governed report evidence |
| [meridian-6-week-roadmap.md](meridian-6-week-roadmap.md) | Current six-week execution slice for Waves 1-4 |
| [waves-2-4-operator-readiness-addendum.md](waves-2-4-operator-readiness-addendum.md) | Owner-lane, dependency, and exit-criteria addendum for the active Waves 2-4 path |
| [provider-reliability-data-confidence-wave-1-blueprint.md](provider-reliability-data-confidence-wave-1-blueprint.md) | Wave 1 provider-confidence and checkpoint-evidence blueprint |
| [paper-trading-cockpit-reliability-sprint.md](paper-trading-cockpit-reliability-sprint.md) | Wave 2 reliability sprint blueprint for replay confidence, session persistence, risk auditability, and promotion traceability |
| [web-ui-development-pivot.md](web-ui-development-pivot.md) | Active plan for pausing desktop feature work and making the browser dashboard the operator UI lane |
| [trading-workstation-migration-blueprint.md](trading-workstation-migration-blueprint.md) | Historical WPF-first migration blueprint; re-scope open items to web-first workstation delivery |
| [meridian-analytics-productization-blueprint.md](meridian-analytics-productization-blueprint.md) | Research -> Trading -> Governance analytics productization blueprint for notebooks, comparison, exports, handoff, and governed artifacts |
| [governance-fund-ops-blueprint.md](governance-fund-ops-blueprint.md) | Governance, reconciliation, and reporting blueprint on top of shared seams |
| [fund-management-pr-sequenced-roadmap.md](fund-management-pr-sequenced-roadmap.md) | PR-sequenced Wave 4 governance and fund-operations slices |
| [security-master-productization-roadmap.md](security-master-productization-roadmap.md) | Delivered Security Master baseline plus Wave 4 follow-ons |
| [ufl-direct-lending-implementation-roadmap.md](ufl-direct-lending-implementation-roadmap.md) | Direct-lending delivery path on top of the UFL target state |

## Later-Wave Productization (Wave 5+)

These docs matter, but they normally follow the Wave 1-4 operator-readiness path unless explicitly pulled forward.

| Document | Purpose |
| --- | --- |
| [backtest-studio-unification-blueprint.md](backtest-studio-unification-blueprint.md) | Wave 5 blueprint for unifying native and Lean backtesting into one product |
| [backtest-studio-unification-pr-sequenced-roadmap.md](backtest-studio-unification-pr-sequenced-roadmap.md) | PR-sized Wave 5 execution path for Backtest Studio unification |
| [portfolio-level-backtesting-composer-blueprint.md](portfolio-level-backtesting-composer-blueprint.md) | Portfolio-level composition, capital contention, and risk analytics blueprint |
| [fund-management-product-vision-and-capability-matrix.md](fund-management-product-vision-and-capability-matrix.md) | Product vision and phased capability view for broader fund-management depth |
| [fund-management-module-implementation-backlog.md](fund-management-module-implementation-backlog.md) | Project-by-project backlog for fund-management modules |
| [meridian-database-blueprint.md](meridian-database-blueprint.md) | Database architecture blueprint supporting the broader platform |
| [options-roadmap.md](options-roadmap.md) | Options and derivatives roadmap outside the default core operator-ready path |

## Optional Research And Scale Tracks

These plans can deepen Meridian's research and performance story, but they are not part of the default core operator-readiness gate.

| Document | Purpose |
| --- | --- |
| [quant-script-environment-blueprint.md](quant-script-environment-blueprint.md) | High-level QuantScript blueprint and product-placement reference |
| [quant-script-page-implementation-guide.md](quant-script-page-implementation-guide.md) | Page-level implementation guidance for the QuantScript surface |
| [backtesting-quantscript-improvement-plan-2026-04.md](backtesting-quantscript-improvement-plan-2026-04.md) | Integrated improvement plan for backtesting trust/velocity and QuantScript workflow continuity |
| [l3-inference-implementation-plan.md](l3-inference-implementation-plan.md) | L3 inference implementation design and rollout plan |
| [quantscript-l3-multiinstance-round2-roadmap.md](quantscript-l3-multiinstance-round2-roadmap.md) | Multi-instance and L3-focused optional follow-on roadmap |

## Unified Financial Ledger (UFL) Target States

The `ufl-*` documents define target-state designs for each instrument type supported by Meridian's Unified Financial Ledger. Start with [ufl-supported-assets-index.md](ufl-supported-assets-index.md) for the full asset-package index.

Active UFL target-state references include direct lending, equity, options, futures, bonds, money market funds, repos, and swaps. Additional package docs in this folder cover FX spot, deposits, certificates of deposit, commercial paper, treasury bills, cash sweep, CFDs, commodities, crypto, warrants, and other-security fallback handling.

## Technical Refactors And Platform Work

| Document | Purpose |
| --- | --- |
| [readability-refactor-roadmap.md](readability-refactor-roadmap.md) | Readability refactor execution plan |
| [readability-refactor-baseline.md](readability-refactor-baseline.md) | Baseline measurements before readability work |
| [readability-refactor-technical-design-pack.md](readability-refactor-technical-design-pack.md) | Technical design pack for readability refactoring |
| [codebase-audit-cleanup-roadmap.md](codebase-audit-cleanup-roadmap.md) | Cleanup and simplification roadmap informed by audits |
| [assembly-performance-roadmap.md](assembly-performance-roadmap.md) | Optional advanced-performance track that normally follows the core operator-ready path |

## Other Plans

| Document | Purpose |
| --- | --- |
| [meridian-database-blueprint.md](meridian-database-blueprint.md) | Database architecture blueprint |
| [ufl-direct-lending-implementation-roadmap.md](ufl-direct-lending-implementation-roadmap.md) | Direct lending implementation roadmap |

## Archived Planning Context

The following prior planning files have been moved out of the active plans tree:

- [`../../archive/docs/plans/workstation-release-readiness-blueprint.md`](../../archive/docs/plans/workstation-release-readiness-blueprint.md) - historical browser-workstation release checklist
- [`../../archive/docs/plans/workstation-sprint-1-implementation-backlog.md`](../../archive/docs/plans/workstation-sprint-1-implementation-backlog.md) - historical sprint backlog tied to the retired release checklist

---

For high-level project status and roadmap, see [docs/status/ROADMAP.md](../status/ROADMAP.md).
