# Plans

**Last Reviewed:** 2026-05-29


## Immediate Plan TODOs

- [ ] Keep Strategy Engine, run comparison, and portfolio drill-in improvements framed as W3 baseline maintenance or W5 prep unless they produce accepted W4 governance/report lineage evidence.
- [ ] Attach every non-`complete` provider capability-matrix row to an owner, target sprint, and adapter-readiness evidence packet.
- [ ] Keep the active W2-W4 blocker list in [`wave-implementation-checklists.md`](wave-implementation-checklists.md) aligned with the latest pilot-readiness stage gates.
- [ ] Use [`desktop-ui-workflow-acceptance-matrix.md`](desktop-ui-workflow-acceptance-matrix.md) before accepting WPF work as Wave 2, Wave 3, or Wave 4 workflow progress.
- [ ] Prove the operations-continuity close lane through browser/operator acceptance with external statement or custodian inputs before describing close readiness as delivered.
- [ ] Run stale-doc/hash validation after roadmap, source registry, or generated documentation changes and record failures as TODOs instead of prose caveats.

This directory contains current Meridian execution plans, blueprints, and target-state reference designs. Start with [current-direction-and-status.md](current-direction-and-status.md) for the consolidated planning interpretation. The canonical wave model lives in [../status/ROADMAP.md](../status/ROADMAP.md); the remaining files here are subordinate implementation plans, optional-track designs, or asset-profile specifications.

Active planning direction:

- Meridian is being productized as an evidence-backed investment operations platform.
- Browser and desktop operator workflows are both active: `src/Meridian.Ui/dashboard/` and
  `src/Meridian.Wpf/` should consume shared contracts, local/web API endpoints, and shared read
  models rather than diverging into separate business logic.
- Top-level operator navigation remains `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`; legacy `Research`, `Data Operations`, and `Governance` names are compatibility or planning shorthand.
- No mobile-specific product lane is active; responsive browser validation is allowed only for the browser workstation.
- Completed, superseded, or historical plans belong under `archive/docs/`, not this active folder.

## 2026-05-18 Review Result

All active plan and roadmap files in this directory were reviewed for their current role. The
consolidated current-direction document is now the first planning stop; the classification below
remains the source of truth for detailed follow-up: core Wave 2-4 operator-readiness plans come
first, later-wave productization follows, UFL asset profiles remain reference designs, and
technical/optional tracks must not compete with the core operator-ready path unless a current
roadmap decision pulls them forward.

2026-05-19 follow-up: release/distribution cleanup remains technical support work. Publish-size
investigation, generated-output ignore rules, and TODO-scan cache hygiene do not change the current
plan-file classification or move W2-W4 wave status.

2026-05-19 browser-workstation follow-up: provider setup now exposes provider-routing connection,
binding, credential-source, environment, warning, and trust-snapshot refresh state in Data and
Settings; Strategy Designer route actions now separate browser-openable GET evidence from
reference-only POST backtest/validation mutations; and Reporting export commands now abort
superseded profile exports. These are operator-safety and routing-support deltas, not plan
reclassification or wave exits.

2026-05-21 planning-control follow-up: the active docs mesh now has structured roadmap and source
documentation registries plus stale-doc/hash validation. The new adapter completion plan and
provider capability matrix should guide provider-readiness cleanup, but they do not reopen Wave 1
or change W2-W4 gate status. Strategy Engine definitions and pre-run validation are Wave 3/W5
support evidence for safer strategy execution, not a Backtest Studio exit.

2026-05-21 plan cleanup follow-up: the wave implementation checklist stays active because it is
the current TODO ledger for W2-W4 blockers. The Approach B+ v2 documentation-system plan and the
Covered Call Slice 1 blueprint moved to `archive/docs/plans/` because their active roles are now
covered by the structured docs mesh, Strategy Engine support evidence, the options roadmap, and
the Backtest Studio plans.

2026-05-28 planning follow-up: W2 and W3 are closed baselines in canonical program state, so active
plan interpretation should treat new cockpit, run, portfolio, ledger, and reconciliation work as
baseline preservation unless it directly advances W4 close/report/evidence acceptance. May 28
support evidence added operations-continuity close routing, reconciliation casework, report-pack
workflow/provenance, browser Accounting/Reporting parity, Investment Accounting Transaction Lab
previews, report restatement review, run comparison/drill-in depth, shared route parity, and
screenshot/evidence validation guards. These do not close W4 until the full Lane C close/case/report
acceptance proof is green.

2026-05-29 planning follow-up: the UFL custom-asset composability plan now has partial shared
promotion-candidate assessment evidence through the Security Master governance service and endpoint.
Keep that evidence inside the UFL/custom-asset plan lane; it does not create a new active wave or
replace the W4 close/report/evidence acceptance gate.

## Core Operator-Ready Path

These are the active documents for Waves 2-4 and the current short-horizon execution lane.

| Document | Current role |
| --- | --- |
| [current-direction-and-status.md](current-direction-and-status.md) | Single consolidated planning entry point for current direction, project status, and document roles |
| [evidence-backed-investment-operations-plan.md](evidence-backed-investment-operations-plan.md) | Product-category filter and archive rule for the active investment-operations direction |
| [meridian-6-week-roadmap.md](meridian-6-week-roadmap.md) | Current time-boxed execution slice for Waves 2-4 and trust-gate maintenance |
| [wave-implementation-checklists.md](wave-implementation-checklists.md) | Concrete W1 maintenance and W2-W4 readiness TODO checklist tied to stage-gate blockers |
| [desktop-ui-workflow-acceptance-matrix.md](desktop-ui-workflow-acceptance-matrix.md) | Desktop/WPF acceptance matrix that maps W2 cockpit, W3 continuity, and W4 governance scenarios to shared checks, focused WPF tests, browser parity checks, and release blockers |
| [waves-2-4-operator-readiness-addendum.md](waves-2-4-operator-readiness-addendum.md) | Owner lanes, dependencies, and exit criteria for Waves 2-4 |
| [web-ui-development-pivot.md](web-ui-development-pivot.md) | Browser workstation foundation and desktop/browser coexistence history |
| [meridian-pilot-workflow.md](meridian-pilot-workflow.md) | Golden-path pilot workflow and productization filter |
| [paper-trading-cockpit-reliability-sprint.md](paper-trading-cockpit-reliability-sprint.md) | Wave 2 paper-session, replay, risk, promotion, and cockpit reliability plan |
| [trading-workstation-migration-blueprint.md](trading-workstation-migration-blueprint.md) | Workstation/shared-model migration context for both active operator surfaces |
| [desktop-workstation-screen-blueprint.md](desktop-workstation-screen-blueprint.md) | Desktop workstation screen blueprint with a programmatically validated checklist for WPF operator work |
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
| [adapters-completion-plan.md](adapters-completion-plan.md) | Provider adapter completion roadmap tied to the capability matrix and readiness follow-up |

## UFL And Asset Profiles

Start with [ufl-supported-assets-index.md](ufl-supported-assets-index.md). UFL is now organized around shared capability profiles, maturity levels, conformance evidence, and thin asset deltas over the common model. The individual asset profile documents remain active reference designs for Security Master, ledger, Accounting, Reporting, Data, and controlled workstation work. Keep delivered baseline evidence separate from target-state additions; these profiles are not readiness-closure documents.

| Area | Documents |
| --- | --- |
| Foundation and conformance | [ufl-supported-assets-index.md](ufl-supported-assets-index.md), [ufl-capability-model.md](ufl-capability-model.md), [ufl-conformance-matrix.md](ufl-conformance-matrix.md), [ufl-projection-and-evidence-kernel.md](ufl-projection-and-evidence-kernel.md), [ufl-accounting-impact-model.md](ufl-accounting-impact-model.md), [ufl-asset-profile-template.md](ufl-asset-profile-template.md) |
| Direct lending | [ufl-direct-lending-implementation-roadmap.md](ufl-direct-lending-implementation-roadmap.md), [ufl-direct-lending-target-state-v2.md](ufl-direct-lending-target-state-v2.md) |
| Listed and derivative instruments | [ufl-equity-target-state-v2.md](ufl-equity-target-state-v2.md), [ufl-option-target-state-v2.md](ufl-option-target-state-v2.md), [ufl-future-target-state-v2.md](ufl-future-target-state-v2.md), [ufl-warrant-target-state-v2.md](ufl-warrant-target-state-v2.md), [ufl-cfd-target-state-v2.md](ufl-cfd-target-state-v2.md), [ufl-swap-target-state-v2.md](ufl-swap-target-state-v2.md) |
| Rates, cash, and credit | [ufl-bond-target-state-v2.md](ufl-bond-target-state-v2.md), [ufl-treasury-bill-target-state-v2.md](ufl-treasury-bill-target-state-v2.md), [ufl-commercial-paper-target-state-v2.md](ufl-commercial-paper-target-state-v2.md), [ufl-certificate-of-deposit-target-state-v2.md](ufl-certificate-of-deposit-target-state-v2.md), [ufl-deposit-target-state-v2.md](ufl-deposit-target-state-v2.md), [ufl-cash-sweep-target-state-v2.md](ufl-cash-sweep-target-state-v2.md), [ufl-money-market-fund-target-state-v2.md](ufl-money-market-fund-target-state-v2.md), [ufl-repo-target-state-v2.md](ufl-repo-target-state-v2.md) |
| Other asset coverage | [ufl-fx-spot-target-state-v2.md](ufl-fx-spot-target-state-v2.md), [ufl-commodity-target-state-v2.md](ufl-commodity-target-state-v2.md), [ufl-crypto-target-state-v2.md](ufl-crypto-target-state-v2.md), [ufl-other-security-target-state-v2.md](ufl-other-security-target-state-v2.md), [ufl-custom-asset-composability.md](ufl-custom-asset-composability.md) |

## Technical And Optional Tracks

These are active or consciously deferred engineering tracks. They should not compete with the core operator-ready path unless a concrete blocker or explicit roadmap decision pulls them forward.

| Document | Current role |
| --- | --- |
| [codebase-audit-cleanup-roadmap.md](codebase-audit-cleanup-roadmap.md) | Active cleanup and maintainability backlog |
| [kernel-parity-migration-blueprint.md](kernel-parity-migration-blueprint.md) | C# to F# kernel parity fixture and CI-gate blueprint |
| [l3-inference-implementation-plan.md](l3-inference-implementation-plan.md) | Optional L3 inference and queue-aware execution simulation plan |
| [quantscript-l3-multiinstance-round2-roadmap.md](quantscript-l3-multiinstance-round2-roadmap.md) | Optional QuantScript, L3, and multi-instance follow-on roadmap |
| [assembly-performance-roadmap.md](assembly-performance-roadmap.md) | Optional advanced-performance track after operator-readiness needs justify it |
| [desktop-shell-modularity-roadmap.md](desktop-shell-modularity-roadmap.md) | Active desktop-shell modularity and extensibility roadmap for phased WPF shell architecture improvements |

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
| [../../archive/docs/plans/approach-b-plus-v2-implementation-plan.md](../../archive/docs/plans/approach-b-plus-v2-implementation-plan.md) | Superseded by the active structured roadmap/source registries, docs automation, and stale-doc/hash validation path |
| [../../archive/docs/plans/approach-b-plus-v2-governance.md](../../archive/docs/plans/approach-b-plus-v2-governance.md) | Historical phase-sequencing governance contract retained as implementation context after the docs-mesh rollout completed |
| [../../archive/docs/plans/covered-call-writing-slice-1-blueprint.md](../../archive/docs/plans/covered-call-writing-slice-1-blueprint.md) | Superseded by delivered Covered Call support evidence plus active Strategy Engine, options, and Backtest Studio planning |
| [../../archive/docs/plans/readability-refactor-baseline.md](../../archive/docs/plans/readability-refactor-baseline.md), [../../archive/docs/plans/readability-refactor-roadmap.md](../../archive/docs/plans/readability-refactor-roadmap.md), [../../archive/docs/plans/readability-refactor-technical-design-pack.md](../../archive/docs/plans/readability-refactor-technical-design-pack.md) | Historical readability baseline and draft design pack; active cleanup guidance now lives in the codebase cleanup roadmap |
| [../../archive/docs/summaries/performance-todo-2026-05-21.md](../../archive/docs/summaries/performance-todo-2026-05-21.md) | Dated performance TODO snapshot with environment-specific benchmark blockage notes moved out of active planning |
| [../../archive/docs/assessments/meridian-main-differentiation-report-2026-04-29.md](../../archive/docs/assessments/meridian-main-differentiation-report-2026-04-29.md) | Dated differentiation report superseded by the active evidence-backed investment-operations plan |
| [../../archive/docs/plans/workstation-release-readiness-blueprint.md](../../archive/docs/plans/workstation-release-readiness-blueprint.md), [../../archive/docs/plans/workstation-sprint-1-implementation-backlog.md](../../archive/docs/plans/workstation-sprint-1-implementation-backlog.md) | Historical browser-workstation release checklist and sprint backlog |

For high-level project status, use [../status/ROADMAP.md](../status/ROADMAP.md), [../status/PROGRAM_STATE.md](../status/PROGRAM_STATE.md), and [../status/FEATURE_INVENTORY.md](../status/FEATURE_INVENTORY.md).
