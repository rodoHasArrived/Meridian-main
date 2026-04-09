# Plans

**Last Reviewed:** 2026-04-08

This directory contains active blueprints, roadmaps, sprint backlogs, and target-state packages that support Meridian delivery. The canonical wave model lives in [../status/ROADMAP.md](../status/ROADMAP.md); treat the documents here as subordinate execution plans, blueprints, or optional-track follow-ons rather than parallel strategy documents.

## Core Operator-Ready Path (Waves 1-4)

These are the plan documents that directly support the current path to the core operator-ready baseline.

| Document | Purpose |
|----------|---------|
| [meridian-6-week-roadmap.md](meridian-6-week-roadmap.md) | Current six-week execution slice for Waves 1-4 |
| [provider-reliability-data-confidence-wave-1-blueprint.md](provider-reliability-data-confidence-wave-1-blueprint.md) | Wave 1 provider-confidence and checkpoint-evidence blueprint |
| [workstation-release-readiness-blueprint.md](workstation-release-readiness-blueprint.md) | Release-readiness checklist for workstation hardening |
| [workstation-sprint-1-implementation-backlog.md](workstation-sprint-1-implementation-backlog.md) | Sprint backlog for workstation delivery |
| [trading-workstation-migration-blueprint.md](trading-workstation-migration-blueprint.md) | Workstation productization blueprint across web and WPF |
| [governance-fund-ops-blueprint.md](governance-fund-ops-blueprint.md) | Governance, reconciliation, and reporting blueprint on top of shared seams |
| [fund-management-pr-sequenced-roadmap.md](fund-management-pr-sequenced-roadmap.md) | PR-sequenced Wave 4 governance and fund-operations slices |
| [security-master-productization-roadmap.md](security-master-productization-roadmap.md) | Delivered Security Master baseline plus Wave 4 follow-ons |
| [ufl-direct-lending-implementation-roadmap.md](ufl-direct-lending-implementation-roadmap.md) | Direct-lending delivery path on top of the UFL target state |

## Later-Wave Productization (Wave 5+)

These docs matter, but they normally follow the Wave 1-4 operator-readiness path unless explicitly pulled forward.

| Document | Purpose |
|----------|---------|
| [backtest-studio-unification-blueprint.md](backtest-studio-unification-blueprint.md) | Wave 5 blueprint for unifying native and Lean backtesting into one product |
| [backtest-studio-unification-pr-sequenced-roadmap.md](backtest-studio-unification-pr-sequenced-roadmap.md) | PR-sized Wave 5 execution path for Backtest Studio unification |
| [fund-management-product-vision-and-capability-matrix.md](fund-management-product-vision-and-capability-matrix.md) | Product vision and phased capability view for broader fund-management depth |
| [fund-management-module-implementation-backlog.md](fund-management-module-implementation-backlog.md) | Project-by-project backlog for fund-management modules |
| [meridian-database-blueprint.md](meridian-database-blueprint.md) | Database architecture blueprint supporting the broader platform |

## Optional Research And Scale Tracks

These plans can deepen Meridian's research and performance story, but they are not part of the default core operator-readiness gate.

| Document | Purpose |
|----------|---------|
| [quant-script-environment-blueprint.md](quant-script-environment-blueprint.md) | High-level QuantScript blueprint and product-placement reference |
| [quant-script-page-implementation-guide.md](quant-script-page-implementation-guide.md) | Page-level implementation guidance for the QuantScript surface |
| [l3-inference-implementation-plan.md](l3-inference-implementation-plan.md) | L3 inference implementation design and rollout plan |
| [quantscript-l3-multiinstance-round2-roadmap.md](quantscript-l3-multiinstance-round2-roadmap.md) | Multi-instance and L3-focused optional follow-on roadmap |

## Unified Financial Ledger Target States

The `ufl-*` documents define target-state designs for instrument and asset-package coverage in Meridian's Unified Financial Ledger.

Start with [ufl-supported-assets-index.md](ufl-supported-assets-index.md), then use the package-specific files for detail.

### Core UFL Index And High-Signal Packages

| Document | Purpose |
|----------|---------|
| [ufl-supported-assets-index.md](ufl-supported-assets-index.md) | Master index of supported UFL asset packages |
| [ufl-direct-lending-target-state-v2.md](ufl-direct-lending-target-state-v2.md) | Direct-lending specialization of the UFL model |
| [ufl-equity-target-state-v2.md](ufl-equity-target-state-v2.md) | Equity package target state |
| [ufl-option-target-state-v2.md](ufl-option-target-state-v2.md) | Option package target state |
| [ufl-future-target-state-v2.md](ufl-future-target-state-v2.md) | Futures package target state |
| [ufl-bond-target-state-v2.md](ufl-bond-target-state-v2.md) | Bond package target state |
| [ufl-money-market-fund-target-state-v2.md](ufl-money-market-fund-target-state-v2.md) | Money market fund package target state |
| [ufl-repo-target-state-v2.md](ufl-repo-target-state-v2.md) | Repo package target state |
| [ufl-swap-target-state-v2.md](ufl-swap-target-state-v2.md) | Swap package target state |

Additional package docs in this folder cover FX spot, deposits, certificates of deposit, commercial paper, treasury bills, cash sweep, CFDs, commodities, crypto, warrants, and other-security fallback handling.

## Technical Refactors And Platform Work

| Document | Purpose |
|----------|---------|
| [readability-refactor-roadmap.md](readability-refactor-roadmap.md) | Readability refactor execution plan |
| [readability-refactor-baseline.md](readability-refactor-baseline.md) | Baseline measurements before readability work |
| [readability-refactor-technical-design-pack.md](readability-refactor-technical-design-pack.md) | Technical design pack for readability refactoring |
| [codebase-audit-cleanup-roadmap.md](codebase-audit-cleanup-roadmap.md) | Cleanup and simplification roadmap informed by audits |
| [assembly-performance-roadmap.md](assembly-performance-roadmap.md) | Optional advanced-performance track that normally follows the core operator-ready path |

## Related

- [Combined Roadmap](../status/ROADMAP_COMBINED.md)
- [Project Roadmap](../status/ROADMAP.md)
- [Status Docs Index](../status/README.md)
