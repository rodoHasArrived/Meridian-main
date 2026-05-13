# Plans

**Last Reviewed:** 2026-05-13

This directory contains current Meridian execution plans, blueprints, and target-state packages. The canonical wave model lives in [../status/ROADMAP.md](../status/ROADMAP.md); the files here are subordinate implementation plans, optional-track designs, or asset-package specifications.

Active planning direction:

- Meridian is being productized as an evidence-backed investment operations platform.
- New operator UI work belongs in the browser workstation under `src/Meridian.Ui/dashboard/`.
- Retained WPF work is limited to shared contracts, regression fixes, compatibility evidence, and retained desktop support.
- Top-level operator navigation remains `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`; legacy `Research`, `Data Operations`, and `Governance` names are compatibility or planning shorthand.
- Completed, superseded, or historical plans belong under `archive/docs/`, not this active folder.

## Core Operator-Ready Path

These are the active documents for Waves 2-4 and the current short-horizon execution lane.

| Document | Current role |
| --- | --- |
| [evidence-backed-investment-operations-plan.md](evidence-backed-investment-operations-plan.md) | Product-category filter and archive rule for the active investment-operations direction |
| [meridian-6-week-roadmap.md](meridian-6-week-roadmap.md) | Current time-boxed execution slice for Waves 2-4 and trust-gate maintenance |
| [waves-2-4-operator-readiness-addendum.md](waves-2-4-operator-readiness-addendum.md) | Owner lanes, dependencies, and exit criteria for Waves 2-4 |
| [web-ui-development-pivot.md](web-ui-development-pivot.md) | Browser-first operator UI direction and retained desktop policy |
| [meridian-pilot-workflow.md](meridian-pilot-workflow.md) | Golden-path pilot workflow and productization filter |
| [paper-trading-cockpit-reliability-sprint.md](paper-trading-cockpit-reliability-sprint.md) | Wave 2 paper-session, replay, risk, promotion, and cockpit reliability plan |
| [trading-workstation-migration-blueprint.md](trading-workstation-migration-blueprint.md) | Workstation/shared-model migration context interpreted through the browser-first pivot |
| [brokerage-portfolio-sync-blueprint.md](brokerage-portfolio-sync-blueprint.md) | Brokerage and custodian account-sync design for Wave 3-4 continuity |
| [governance-fund-ops-blueprint.md](governance-fund-ops-blueprint.md) | Wave 4 governance, reconciliation, and report-pack blueprint |
| [ledger.md](ledger.md) | Ledger execution roadmap for period management, persistence, multi-ledger depth, accruals, and reporting |
| [fund-management-pr-sequenced-roadmap.md](fund-management-pr-sequenced-roadmap.md) | PR-sized governance and fund-operations implementation path |
| [fund-management-module-implementation-backlog.md](fund-management-module-implementation-backlog.md) | Module-by-module fund-management implementation backlog |
| [fund-management-product-vision-and-capability-matrix.md](fund-management-product-vision-and-capability-matrix.md) | Product vision and phased capability matrix for broader fund-management depth |

## Later-Wave Productization

These plans remain useful, but they normally follow the Wave 2-4 operator-readiness path unless explicitly pulled forward.

| Document | Current role |
| --- | --- |
| [backtest-studio-unification-blueprint.md](backtest-studio-unification-blueprint.md) | Wave 5 blueprint for unifying native and Lean backtesting |
| [backtest-studio-unification-pr-sequenced-roadmap.md](backtest-studio-unification-pr-sequenced-roadmap.md) | PR-sized Wave 5 Backtest Studio execution path |
| [research-backtest-trust-and-velocity-blueprint.md](research-backtest-trust-and-velocity-blueprint.md) | Focused Research workflow hardening blueprint before full Backtest Studio unification |
| [portfolio-level-backtesting-composer-blueprint.md](portfolio-level-backtesting-composer-blueprint.md) | Portfolio-level backtest composition, capital contention, and risk analytics design |
| [options-roadmap.md](options-roadmap.md) | Options and derivatives roadmap outside the default core operator-ready gate |
| [meridian-database-blueprint.md](meridian-database-blueprint.md) | Database architecture direction supporting platform depth |
| [runbook-template-registry-modernization-plan.md](runbook-template-registry-modernization-plan.md) | Runbook/template registry modernization plan |

## UFL And Asset Packages

Start with [ufl-supported-assets-index.md](ufl-supported-assets-index.md). The individual target-state packages remain active reference designs for Security Master, ledger, accounting, and reporting work.

| Area | Documents |
| --- | --- |
| Index and direct lending | [ufl-supported-assets-index.md](ufl-supported-assets-index.md), [ufl-direct-lending-implementation-roadmap.md](ufl-direct-lending-implementation-roadmap.md), [ufl-direct-lending-target-state-v2.md](ufl-direct-lending-target-state-v2.md) |
| Listed and derivative instruments | [ufl-equity-target-state-v2.md](ufl-equity-target-state-v2.md), [ufl-option-target-state-v2.md](ufl-option-target-state-v2.md), [ufl-future-target-state-v2.md](ufl-future-target-state-v2.md), [ufl-warrant-target-state-v2.md](ufl-warrant-target-state-v2.md), [ufl-cfd-target-state-v2.md](ufl-cfd-target-state-v2.md), [ufl-swap-target-state-v2.md](ufl-swap-target-state-v2.md) |
| Rates, cash, and credit | [ufl-bond-target-state-v2.md](ufl-bond-target-state-v2.md), [ufl-treasury-bill-target-state-v2.md](ufl-treasury-bill-target-state-v2.md), [ufl-commercial-paper-target-state-v2.md](ufl-commercial-paper-target-state-v2.md), [ufl-certificate-of-deposit-target-state-v2.md](ufl-certificate-of-deposit-target-state-v2.md), [ufl-deposit-target-state-v2.md](ufl-deposit-target-state-v2.md), [ufl-cash-sweep-target-state-v2.md](ufl-cash-sweep-target-state-v2.md), [ufl-money-market-fund-target-state-v2.md](ufl-money-market-fund-target-state-v2.md), [ufl-repo-target-state-v2.md](ufl-repo-target-state-v2.md) |
| Other asset coverage | [ufl-fx-spot-target-state-v2.md](ufl-fx-spot-target-state-v2.md), [ufl-commodity-target-state-v2.md](ufl-commodity-target-state-v2.md), [ufl-crypto-target-state-v2.md](ufl-crypto-target-state-v2.md), [ufl-other-security-target-state-v2.md](ufl-other-security-target-state-v2.md) |

## Technical And Optional Tracks

These are active or consciously deferred engineering tracks. They should not compete with the core operator-ready path unless a concrete blocker or explicit roadmap decision pulls them forward.

| Document | Current role |
| --- | --- |
| [codebase-audit-cleanup-roadmap.md](codebase-audit-cleanup-roadmap.md) | Active cleanup and maintainability backlog |
| [kernel-parity-migration-blueprint.md](kernel-parity-migration-blueprint.md) | C# to F# kernel parity fixture and CI-gate blueprint |
| [l3-inference-implementation-plan.md](l3-inference-implementation-plan.md) | Optional L3 inference and queue-aware execution simulation plan |
| [quantscript-l3-multiinstance-round2-roadmap.md](quantscript-l3-multiinstance-round2-roadmap.md) | Optional QuantScript, L3, and multi-instance follow-on roadmap |
| [assembly-performance-roadmap.md](assembly-performance-roadmap.md) | Optional advanced-performance track after operator-readiness needs justify it |

## Archived Or Superseded Planning Context

The following files were removed from the active plans tree because they are completed, superseded, or historical:

| Archived document | Reason |
| --- | --- |
| [../../archive/docs/plans/provider-reliability-data-confidence-wave-1-blueprint.md](../../archive/docs/plans/provider-reliability-data-confidence-wave-1-blueprint.md) | Wave 1 trust gate is closed; active evidence now lives in status/provider-validation docs and generated review packets |
| [../../archive/docs/plans/security-master-productization-roadmap.md](../../archive/docs/plans/security-master-productization-roadmap.md) | Security Master productization baseline is delivered; Wave 4 follow-ons live in governance plans and status docs |
| [../../archive/docs/plans/quant-script-environment-blueprint.md](../../archive/docs/plans/quant-script-environment-blueprint.md) | QuantScript baseline and follow-on wave are delivered |
| [../../archive/docs/plans/quant-script-page-implementation-guide.md](../../archive/docs/plans/quant-script-page-implementation-guide.md) | Completed QuantScript page implementation guide retained as historical reference |
| [../../archive/docs/plans/backtesting-quantscript-improvement-plan-2026-04.md](../../archive/docs/plans/backtesting-quantscript-improvement-plan-2026-04.md) | Superseded by the Research trust/velocity and Backtest Studio plans plus delivered QuantScript baseline |
| [../../archive/docs/plans/meridian-analytics-productization-blueprint.md](../../archive/docs/plans/meridian-analytics-productization-blueprint.md) | Superseded by the evidence-backed investment-operations plan and browser-first productization path |
| [../../archive/docs/plans/readability-refactor-baseline.md](../../archive/docs/plans/readability-refactor-baseline.md), [../../archive/docs/plans/readability-refactor-roadmap.md](../../archive/docs/plans/readability-refactor-roadmap.md), [../../archive/docs/plans/readability-refactor-technical-design-pack.md](../../archive/docs/plans/readability-refactor-technical-design-pack.md) | Historical readability baseline and draft design pack; active cleanup guidance now lives in the codebase cleanup roadmap |
| [../../archive/docs/assessments/meridian-main-differentiation-report-2026-04-29.md](../../archive/docs/assessments/meridian-main-differentiation-report-2026-04-29.md) | Dated differentiation report superseded by the active evidence-backed investment-operations plan |
| [../../archive/docs/plans/workstation-release-readiness-blueprint.md](../../archive/docs/plans/workstation-release-readiness-blueprint.md), [../../archive/docs/plans/workstation-sprint-1-implementation-backlog.md](../../archive/docs/plans/workstation-sprint-1-implementation-backlog.md) | Historical browser-workstation release checklist and sprint backlog |

For high-level project status, use [../status/ROADMAP.md](../status/ROADMAP.md), [../status/PROGRAM_STATE.md](../status/PROGRAM_STATE.md), and [../status/FEATURE_INVENTORY.md](../status/FEATURE_INVENTORY.md).
