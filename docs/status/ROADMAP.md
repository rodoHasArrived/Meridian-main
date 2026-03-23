# Meridian - Project Roadmap

**Last Updated:** 2026-03-22  
**Status:** Active roadmap refresh  
**Repository Snapshot (2026-03-22):** solution projects: 31 | `src/` projects: 23 | test projects: 5 | workflow files: 37 | source files under `src/` (`*.cs`, `*.fs`, `*.xaml`): 1,466 | test source files under `tests/` (`*.cs`, `*.fs`): 352

This refresh reconciles the active status/planning set updated on 2026-03-21 with the governance and UFL planning artifacts added on 2026-03-22. Meridian is no longer primarily blocked by core platform completeness. The remaining work is to finish the workflow-centric workstation, deepen governance and fund-operations product surfaces, harden provider confidence, and decide which optional scale and flagship tracks are part of the near-term product story.

Use this document with:

- [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md) for current-vs-target capability status
- [`FULL_IMPLEMENTATION_TODO_2026_03_20.md`](FULL_IMPLEMENTATION_TODO_2026_03_20.md) for the normalized non-assembly backlog
- [`../plans/trading-workstation-migration-blueprint.md`](../plans/trading-workstation-migration-blueprint.md) for the workstation migration target
- [`../plans/governance-fund-ops-blueprint.md`](../plans/governance-fund-ops-blueprint.md) for governance, reconciliation, and reporting productization
- [`../plans/ufl-supported-assets-index.md`](../plans/ufl-supported-assets-index.md) for the current Security Master-backed UFL asset package set
- [`../plans/ufl-direct-lending-implementation-roadmap.md`](../plans/ufl-direct-lending-implementation-roadmap.md) for the first deep governance/fund-ops vertical

## Summary

Meridian already has the foundations of a serious local-first trading platform: collection, storage, replay, backtesting, paper-trading primitives, ledger infrastructure, Security Master foundations, and a large WPF and web surface area. The main gap is not "can the platform do things?" but "does the product feel like one coherent operator workflow?"

The roadmap now centers on four outcomes:

1. Finish the `Research`, `Trading`, `Data Operations`, and `Governance` workstation model as the primary user experience.
2. Extend the shared run, portfolio, ledger, and reconciliation seams from backtest-first coverage into broader paper, live-adjacent, and governance workflows.
3. Turn Security Master, governance, reporting, and UFL packages into first-class product capabilities rather than mostly blueprint-level direction.
4. Keep optional work optional: multi-instance scale-out and Phase 16 performance optimizations should deepen Meridian, not block the non-assembly product baseline.

## Current State

### Complete

- Core improvement themes A-G are closed in [`IMPROVEMENTS.md`](IMPROVEMENTS.md).
- Data canonicalization theme J is closed through deterministic mappings, metrics, drift reporting, and golden fixtures.
- Provider registration, DI composition, route coverage, API auth/rate limiting, observability baseline, and deployment assets are in place.
- WPF workspace/session persistence is live and already aligned around `Research`, `Trading`, `Data Operations`, and `Governance`.
- Shared workstation contracts and read services exist for runs, portfolio, and ledger baselines.
- Coordination primitives for future multi-instance ownership already exist in `src/Meridian.Application/Coordination/`.
- Security Master services, storage, and domain modules already exist as a platform foundation.

### Partial

- Provider confidence is uneven: Polygon replay breadth, IB runtime validation, NYSE validation depth, and StockSharp connector breadth still need evidence and operator confidence work.
- The workstation taxonomy exists, but too much of the UX still depends on page-first flows and partial shells.
- Shared run, portfolio, and ledger flows are real, but still too backtest-first.
- Governance productization has begun, but cash-flow, multi-ledger, reconciliation, reporting, and account/entity flows are not yet complete product surfaces.
- A first direct-lending vertical slice and run-scoped reconciliation seam now exist in the repo, but both remain early relative to the target state.

### Planned

- Backtest Studio unification and the full `Backtest -> Paper -> Live` promotion workflow.
- A full paper-trading cockpit with clearer positions, orders, fills, controls, and risk state.
- QuantScript as a real project, runtime, test suite, and workstation entry point.
- L3 inference and queue-aware execution simulation as a real engine and operator-facing workflow.

### Optional

- Multi-instance coordination and horizontal scale-out for collector/runtime ownership.
- Phase 16 assembly-level performance optimizations.
- Additional advanced analytics or specialized product tracks that do not change Meridian's core operator story.

## What Is Complete

The following repo-grounded claims are safe to treat as complete as of 2026-03-22:

- The old platform-hardening backlog is no longer the main blocker. `ROADMAP`, `FEATURE_INVENTORY`, and `FULL_IMPLEMENTATION_TODO_2026_03_20` all agree that the remaining work is productization, not basic platform survival.
- The workstation vocabulary is established in code, including persisted workspace/session behavior in [`../src/Meridian.Wpf/Services/WorkspaceService.cs`](../src/Meridian.Wpf/Services/WorkspaceService.cs).
- Shared run and drill-in foundations are present through `StrategyRunReadService`, `PortfolioReadService`, `LedgerReadService`, workstation contracts, and WPF browser/detail/portfolio/ledger view models.
- Governance and Security Master are no longer greenfield concepts. The repo already contains `Meridian.Application/SecurityMaster`, `Meridian.Contracts/Workstation/ReconciliationDtos.cs`, and run-scoped reconciliation services/endpoints.
- The UFL planning layer has expanded beyond a single note: the asset package index and direct-lending implementation roadmap are now checked in on 2026-03-22.

## What Remains

### Phase 11: Trading Workstation Structure

Meridian still needs to make workspace-first navigation the default mental model instead of a thin layer over many page registrations.

Remaining work:

- make workspace landing shells feel intentional and durable
- align command palette, navigation hierarchy, and quick actions
- remove or re-scope orphan and placeholder-only surfaces
- keep web and WPF workstation seams aligned where practical

### Phase 12: Shared Run / Portfolio / Ledger Model

The shared read-model baseline is live, but it is not the finished operator model yet.

Remaining work:

- broaden run history beyond backtest-first usage into paper and live-adjacent history
- deepen portfolio and ledger drill-ins into governance-grade views
- connect reconciliation, account/entity, and cash-flow seams to the same shared model family
- enrich workstation read paths with Security Master-backed metadata

### Phase 13: Backtest + Paper Trading Unification

The product still needs one obvious lifecycle from research to trading.

Remaining work:

- build Backtest Studio as one experience across native and Lean engines
- add paper-trading cockpit surfaces for fills, positions, orders, and controls
- make promotion explicit, auditable, and safety-gated from `Backtest -> Paper -> Live`

### Governance and UFL Productization

This is now a real roadmap track, not only a vision document.

Remaining work:

- turn Security Master into a workstation-visible platform layer
- add multi-ledger, trial-balance, and cash-flow views inside Governance
- complete reconciliation workflows beyond the first run-scoped seam
- add report-pack generation and governed exports
- use direct lending as the first deep vertical, then extend the UFL package model across supported Security Master asset classes

### Provider Confidence and Runtime Hardening

The platform is feature-rich, but operator trust still depends on a few incomplete validation areas.

Remaining work:

- expand Polygon replay coverage across more feeds and edge cases
- keep IB runtime/bootstrap validation current against real vendor surfaces
- strengthen NYSE shared-lifecycle coverage
- expand StockSharp connector/runtime examples and validated adapters
- continue provider and strategy/backtest persistence test expansion where coverage is still thinner

## Opportunities

| Category | Opportunity | Gap | Value | Unlocks | Track |
|---|---|---|---|---|---|
| Workflow completion | Finish workspace-first shells | Workspaces exist in name and persistence, but not yet as the clear primary UX | Meridian starts feeling like one product instead of a toolkit | cleaner run browsing, governance adoption, web/WPF alignment | Critical path |
| Operator UX | Promote run/portfolio/ledger/reconciliation into one operator flow | Shared read models exist, but paper/live and governance continuity are incomplete | users get one recognizable mental model from research through audit | promotion workflow, cockpit UX, report packs | Critical path |
| Provider readiness | Close the remaining trust gaps in replay/runtime evidence | Some provider claims still rely too heavily on docs or narrow coverage | lowers rollout risk and demo/operator friction | paper/live workflow credibility, execution realism | Critical path |
| Reliability and observability | Turn reconciliation and governance seams into auditable operational workflows | Reconciliation DTOs and services exist, but the product surface is still early | stronger operator trust and post-incident explainability | governed exports, exception queues, audit trails | Critical path |
| Flagship product capabilities | Use direct lending as the first deep UFL vertical | The repo now has a real direct-lending slice and roadmap, but it is not yet a complete governed workflow | proves Meridian can host asset-specific middle/back-office workflows | broader UFL package execution, differentiated governance story | Later wave |
| Architecture simplification | Keep query/orchestration seams ahead of UI sprawl | Page-local logic and broad composition areas still slow safe iteration | reduces future delivery cost and drift | faster workstation, governance, and flagship delivery | Later wave |
| Testing and validation | Expand focused validation where new product seams are landing | Workstation, governance, and direct-lending seams need stronger regression evidence as they deepen | keeps roadmap claims conservative and supportable | safer iteration across WPF, API, and F# kernels | Continuous |
| Flagship product capabilities | Land QuantScript and L3 simulation on the stabilized shell | Both remain blueprint-only | differentiates the Research workspace once the core operator loop is coherent | stronger strategy lifecycle story | After critical path |

## Target End Product

The finished Meridian product is a workflow-centric, self-hosted trading workstation for research, trading operations, data operations, and governance. An operator can move through one connected lifecycle: select or define instruments, validate data, run and compare strategies, review portfolio and ledger effects, reconcile expected versus realized outcomes, govern cash and accounting state, generate report packs, promote safely into paper trading, and eventually operate live workflows under explicit controls.

In that end state:

- `Research`, `Trading`, `Data Operations`, and `Governance` are real product surfaces, not only labels.
- Backtests, paper sessions, and live-adjacent history share one recognizable run model with first-class portfolio, ledger, and reconciliation drill-ins.
- Security Master is the authoritative instrument-definition layer across research, portfolio, ledger, governance, and UFL asset packages.
- Governance supports cash-flow, trial balance, multi-ledger, reconciliation, reporting, and exception workflows inside Meridian rather than as disconnected exports or future ideas.
- UFL verticals, beginning with direct lending, demonstrate how asset-specific operational workflows sit on top of the common workstation and governance platform.

The non-assembly product baseline does not require optional scale-out or Phase 16 optimization work. Those tracks remain valuable, but they are depth multipliers, not prerequisites for Meridian to feel complete.

## Recommended Next Waves

### Wave 1: Trust and shell closure

Focus:

- provider confidence and runtime hardening
- workspace-first shell consolidation
- navigation and quick-action cleanup

Exit signal:

Meridian is easier to trust operationally, and the workstation shell feels more obviously workspace-first than page-first.

### Wave 2: Shared model expansion

Focus:

- extend run, portfolio, ledger, and comparison flows beyond backtest-first coverage
- connect Security Master metadata to shared read models
- harden early reconciliation seams

Exit signal:

Research, paper-history, portfolio, ledger, and reconciliation flows look like one family instead of adjacent features.

### Wave 3: Governance baseline

Focus:

- governance-facing multi-ledger and trial-balance paths
- cash-flow and reporting seams
- account/entity and exception-queue framing

Exit signal:

Governance stops reading as a blueprint-heavy concept and starts reading as a product surface with auditable workflows.

### Wave 4: Direct lending and UFL vertical execution

Focus:

- complete the direct-lending slice in dependency-aware PR lanes
- keep UFL asset packages aligned with Security Master and governance infrastructure
- prove the platform can host asset-specific operations without forking the product model

Exit signal:

Direct lending is no longer only a promising slice; it becomes a demonstrable governed workflow and a template for later UFL package execution.

### Wave 5: Research differentiation

Focus:

- QuantScript
- L3 inference and queue-aware execution simulation

Exit signal:

Meridian has at least one differentiated flagship capability layered on top of a coherent operator shell instead of adding more blueprint-only ambition.

### Optional Wave: Scale and hot-path depth

Focus:

- multi-instance ownership and topology support
- assembly-level performance work where profiling proves it matters

Exit signal:

Scale and performance work deepen the platform without rewriting the product story or destabilizing the operator baseline.

## Risks and Dependencies

- Workspace polish must not outrun shared contracts. If shell work gets ahead of query/read-model seams, Meridian will recreate the same page-local fragmentation under new labels.
- Governance productization depends on Security Master and shared ledger/read-model enrichment staying authoritative. Parallel read paths will create drift fast.
- Reconciliation and direct lending can expand quickly. Keep them dependency-aware and PR-sized so they prove the platform direction without becoming a second product architecture.
- Provider trust remains a gating dependency for broader paper/live credibility. Meridian should not overclaim operator readiness where replay/runtime evidence is still thin.
- Documentation convergence is part of the roadmap, not an afterthought. `ROADMAP`, `FEATURE_INVENTORY`, the backlog docs, and the blueprint set need to move together.

## Non-Assembly Release Gates

Meridian can reasonably claim the non-assembly roadmap is complete only when all of the following are true:

- provider/runtime claims are backed by current docs, tests, and replay/runtime validation
- the workstation experience is centered on real `Research`, `Trading`, `Data Operations`, and `Governance` shells
- shared run, portfolio, ledger, and reconciliation concepts cover research, paper, and live-adjacent workflows through one recognizable model family
- Security Master, governance, reporting, and at least the first UFL vertical are product surfaces rather than only foundations or plans
- any remaining cleanup, scale-out, or Phase 16 work is explicitly optional or deferred by decision rather than implied as hidden mandatory scope

<a id="desktop-improvements"></a>
## Desktop Improvements

Historical desktop-improvement items are largely complete. The active desktop roadmap is now the workstation migration itself: improve workflow discoverability, keep provenance and trust cues strong, and move deeper product logic into durable read-model, orchestration, and view-model seams instead of multiplying page-local behavior.

<a id="phase-6-duplicate--unused-code-cleanup"></a>
## Phase 6: Duplicate & Unused Code Cleanup

Status: closed.

This historical phase is complete. Remaining cleanup work belongs to adjacent feature delivery or ongoing maintenance, not to a dedicated cleanup phase.

<a id="phase-8-repository-organization--optimization"></a>
## Phase 8: Repository Organization & Optimization

Status: rolling maintenance.

This phase now covers documentation alignment, generated-doc freshness, archive hygiene, and readability work that supports the active workstation and governance roadmap without competing with product-critical delivery.

<a id="phase-16-assembly-level-performance-optimizations"></a>
## Phase 16: Assembly-Level Performance Optimizations

Status: optional and profile-gated.

Use [`../plans/assembly-performance-roadmap.md`](../plans/assembly-performance-roadmap.md) for the detailed Phase 16 viability assessment. Phase 16 should start only when profiling proves a hot path is worth the complexity. It is not on the critical path for Meridian's non-assembly product baseline.

## Reference Documents

- [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md)
- [`IMPROVEMENTS.md`](IMPROVEMENTS.md)
- [`FULL_IMPLEMENTATION_TODO_2026_03_20.md`](FULL_IMPLEMENTATION_TODO_2026_03_20.md)
- [`EVALUATIONS_AND_AUDITS.md`](EVALUATIONS_AND_AUDITS.md)
- [`../plans/trading-workstation-migration-blueprint.md`](../plans/trading-workstation-migration-blueprint.md)
- [`../plans/governance-fund-ops-blueprint.md`](../plans/governance-fund-ops-blueprint.md)
- [`../plans/ufl-supported-assets-index.md`](../plans/ufl-supported-assets-index.md)
- [`../plans/ufl-direct-lending-implementation-roadmap.md`](../plans/ufl-direct-lending-implementation-roadmap.md)
- [`../plans/assembly-performance-roadmap.md`](../plans/assembly-performance-roadmap.md)
