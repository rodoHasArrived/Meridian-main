# Meridian - Project Roadmap

**Last Updated:** 2026-04-06
**Status:** Active productization — workstation workflows are now materially in code, with provider trust, cockpit hardening, and governance integration on the critical path
**Repository Snapshot (2026-04-06):** solution projects: 40 | `src/` project files: 28 | test projects: 8 | workflow files: 42

Meridian is no longer primarily blocked on missing platform primitives. The repo already contains a strong market-data, storage, backtesting, execution, ledger, and workstation baseline. The main delivery problem is now narrower and more product-shaped: closing the trust gaps, workflow gaps, and governance gaps that still separate a feature-rich platform from a genuinely operator-ready trading workstation and fund-operations product.

The active roadmap therefore centers on four outcomes:

- prove operator trust with evidence-backed provider, replay, and backfill validation
- harden the workstation workflows that already exist in web and WPF so they feel reliable and connected
- make shared run, portfolio, ledger, and Security Master models the default seam across research, trading, data operations, and governance
- finish the governance and fund-operations workflows without forking the architecture into parallel subsystems

Use this document with:

- [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md) - current capability status
- [`FULL_IMPLEMENTATION_TODO_2026_03_20.md`](FULL_IMPLEMENTATION_TODO_2026_03_20.md) - normalized non-assembly backlog
- [`IMPROVEMENTS.md`](IMPROVEMENTS.md) - completed improvement history
- [`production-status.md`](production-status.md) - current readiness posture and release gates
- [`OPPORTUNITY_SCAN.md`](OPPORTUNITY_SCAN.md) - prioritized opportunity framing
- [`TARGET_END_PRODUCT.md`](TARGET_END_PRODUCT.md) - concise end-state product summary
- [`ROADMAP_COMBINED.md`](ROADMAP_COMBINED.md) - combined roadmap, opportunities, and target-state narrative
- [`../plans/trading-workstation-migration-blueprint.md`](../plans/trading-workstation-migration-blueprint.md) - workstation target state
- [`../plans/governance-fund-ops-blueprint.md`](../plans/governance-fund-ops-blueprint.md) - governance target state
- [`../plans/meridian-6-week-roadmap.md`](../plans/meridian-6-week-roadmap.md) - current time-boxed delivery plan

---

## Summary

Meridian's platform foundations are now broad enough that roadmap priority should come from operator value, not from generalized platform sprawl. The repo already includes:

- a strong ingestion and storage baseline with bounded channels, WAL durability, JSONL and Parquet sinks, replay, backfill scheduling, gap detection, quality scoring, packaging, and export
- shared workstation endpoints and a React workstation shell in `src/Meridian.Ui/dashboard/src/` covering Research, Trading, Data Operations, and Governance
- shared run, portfolio, ledger, and reconciliation read surfaces in `src/Meridian.Strategies/Services/` and `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`
- a WPF workstation baseline with `StrategyRunsPage`, `RunDetailPage`, `RunPortfolioPage`, `RunLedgerPage`, `RunCashFlowPage`, and `RunRiskPage`
- Security Master, reconciliation, direct-lending, and governance foundations already anchored in contracts, services, storage, and workstation-facing endpoints

That changes the roadmap emphasis. Meridian does not need another broad foundation phase. It needs disciplined closure on trust, workflow continuity, product integration, and operator-grade governance.

---

## Current State

### Complete

These are conservative "in code and materially usable" claims as of 2026-04-06:

- Core ingestion pipeline: bounded channels, backpressure handling, WAL durability, composite sinks, graceful shutdown, and structured metrics
- Storage foundation: JSONL and Parquet sinks, tiered storage, replay, packaging, export, lineage, catalog, quota enforcement, and lifecycle policy support
- Broad provider and backfill baseline: streaming providers, historical providers, symbol search, fallback chains, backfill scheduling, checkpointing, gap analysis, and rate-limit handling
- Data quality foundation: completeness, freshness, anomaly detection, sequence checks, degradation scoring, SLO registry, and quality reporting
- Backtesting baseline: native replay engine, Lean integration, strategy SDK, stored run metrics, fill summaries, and export
- Execution baseline: paper gateway, risk rules, order abstractions, brokerage gateway framework, session endpoints, promotion endpoints, and strategy lifecycle endpoints
- Web workstation shell: routed `Research`, `Trading`, `Data Operations`, and `Governance` workspaces in `src/Meridian.Ui/dashboard/src/app.tsx`
- Research workspace depth: run filtering, comparison, diffing, attribution drill-ins, fills drill-ins, promotion evaluation, approval, rejection, and promotion history in `research-screen.tsx`
- Trading workspace depth: positions, orders, fills, risk panels, order actions, paper-session creation and restore, replay controls, and promotion gate flows in `trading-screen.tsx`
- Data Operations workspace depth: provider health, backfill queue detail, trigger and preview flows, export visibility, symbol management, and quality monitoring in `data-operations-screen.tsx`
- Governance workspace depth: reconciliation queue, break review and resolution, trial-balance drill-ins, reporting profile visibility, Security Master search, and identifier-conflict workflows in `governance-screen.tsx`
- Shared run, portfolio, and ledger read-model baseline: `StrategyRunReadService`, `PortfolioReadService`, and `LedgerReadService` now normalize cross-workspace read paths
- WPF workstation shell modernization: native Fluent theme, SVG icon set, candlestick charting, zero-API-key startup, workflow guide, and screenshot-refresh CI
- Desktop delivery momentum: a `ScatterAnalysis` quickstart panel and standalone WPF export workflow now reinforce onboarding and packaging without changing the core wave ordering
- Improvement portfolio A-G and J: the active improvement tracker marks the core platform-improvement set as complete, with Theme K active
- Governance baseline: Security Master services and endpoints, run-scoped reconciliation, direct-lending APIs, export infrastructure, and blueprint-backed fund-ops planning are all present in the repo

### Partial

These areas are real in code but not yet complete enough to treat as fully closed operator workflows:

- Provider confidence remains uneven. Polygon, Interactive Brokers, StockSharp, and NYSE each have stronger replay, contract, and pipeline evidence than earlier snapshots, including the April 6 IB facade contract, NYSE pipeline, and StockSharp edge-case additions, but the validation matrix still includes partial or missing runtime proof.
- Backfill reliability is broadly implemented but still needs longer-run checkpoint evidence and clearer operator confidence signals across providers and date ranges.
- The React workstation is no longer just a shell, but it still needs hardening around real-vendor validation, richer audit depth, and stronger acceptance criteria for daily operator use.
- Shared run coverage now spans backtest, paper, and live-aware models in contracts and UI, but portfolio, ledger, cash-flow, and reconciliation continuity are not yet equally deep in every mode.
- Security Master is now visible in governance workflows, but it is not yet the consistently authoritative instrument layer across research, trading, portfolio, ledger, reconciliation, and reporting.
- Governance workflows now have real seams, but multi-ledger, cash-flow modeling, report-pack generation, and broader exception handling are still early product layers rather than finished experiences.
- WPF workstation migration is meaningfully underway, but high-traffic page redesign and MVVM extraction remain active rather than complete.
- Live brokerage validation and controlled `Paper -> Live` promotion remain incomplete and should not yet be treated as an operator-ready live-trading claim.

### Planned

These are active productization tracks rather than greenfield invention:

- close provider-confidence gaps with replay, runtime, and checkpoint evidence that aligns to the validation matrix
- harden the web paper-trading cockpit that is already in code into a validated daily-use operator surface
- deepen the shared run model into fuller portfolio, ledger, reconciliation, and governance continuity
- continue workstation migration so web and WPF feel like coherent workspaces rather than grouped pages
- productize Security Master and governance workflows around multi-ledger, cash-flow, reconciliation, and reporting
- unify Backtest Studio workflows once the shared run model and workstation flows are stable enough to support it cleanly
- validate at least one brokerage path against a real vendor surface with operator controls and explicit audit trail

### Optional

These remain valuable, but they are not on the shortest path to Meridian's core operator-ready product:

- deeper QuantScript workflow integration and larger sample libraries
- L3 inference and queue-aware execution simulation
- multi-instance coordination as a supported scale-out topology
- Phase 16 assembly-level performance optimization
- broader advanced research tooling after the core workstation workflows are operator-ready
- broader Phase 1.5 preferred and convertible equity productization beyond the new domain, event-model, and preferred-term read/write foundation

---

## What Is Complete

### Platform baseline

- Meridian's ingestion, storage, replay, and export stack is no longer a major roadmap blocker.
- The repo has a credible archival and replay platform, broad provider coverage, and materially stronger observability and operational-readiness foundations than earlier roadmap snapshots.
- The historical improvement backlog is effectively closed for the current baseline, which is a real milestone.

### Workstation baseline

- The four-workspace model is now present in both planning and implementation.
- The React workstation is no longer just navigation and bootstrap summaries; it contains material workflows for research, trading, data operations, and governance.
- WPF already has meaningful workstation-aligned run, portfolio, ledger, and cash-flow surfaces on top of the broader page inventory.

### Shared read-model baseline

- `StrategyRunReadService`, `PortfolioReadService`, and `LedgerReadService` now give Meridian a stable seam for unifying backtest, paper, live-aware, portfolio, and ledger views.
- Workstation endpoints already expose run comparison, diff, fills, attribution, ledger summaries, reconciliation, and Security Master read paths.

### Governance baseline

- Governance is no longer hypothetical. Security Master, reconciliation, direct lending, export profiles, and governance-facing UI/API seams are real and discoverable in the repo.
- The product gap has shifted from "build governance foundations" to "finish governance productization and workflow continuity."

---

## What Remains

### Critical path

- close provider-confidence gaps so data trust is evidence-backed rather than inferred from architecture
- validate backfill checkpoints and data-gap handling across representative providers and date ranges
- harden the paper-trading cockpit from featureful UI into a validated operator workflow with clearer acceptance criteria
- keep the `Backtest -> Paper` promotion flow explicit, auditable, and tied to real operator review
- extend run-history continuity across backtest, paper, and live-aware modes so the shared run model truly becomes Meridian's cross-workspace backbone

### Next productization layer

- make Security Master the authoritative instrument-definition seam across research, trading, portfolio, ledger, reconciliation, and reporting
- deepen portfolio, ledger, reconciliation, and cash-flow workflows from first visible slices into governance-grade operator tools
- add multi-ledger and report-pack flows without creating a parallel governance architecture
- continue WPF workflow consolidation and MVVM extraction on the highest-traffic pages

### Later but still meaningful

- unify native and Lean backtesting into one Backtest Studio workflow after shared read models stabilize
- validate at least one brokerage path against a live vendor surface with full audit trail and human controls
- broaden advanced research, simulation, and scale-out capabilities once the core workstation product is trustworthy

---

## Opportunities

### 1. Workflow completion

**Gap:** Meridian now has substantial operator-facing surfaces in both web and WPF, but some cross-workspace journeys still stop at a partial handoff rather than a finished workflow.

**Value:** Closing those journeys creates the biggest user-visible gain because much of the underlying capability already exists.

**Unlocks:** A clearer definition of "operator-ready" and a more coherent workstation narrative.

**Placement:** Critical path.

### 2. Provider readiness

**Gap:** Provider breadth is strong, but operator trust still depends on uneven replay and runtime evidence.

**Value:** This directly affects confidence in research, paper sessions, promotion decisions, and any future live-readiness claims.

**Unlocks:** Safer execution productization and cleaner release gates.

**Placement:** Critical path.

### 3. Operator UX

**Gap:** The workstation is now materially implemented, but some flows still need stronger workflow continuity, action polish, and acceptance criteria to become dependable operator tools.

**Value:** Better continuity and sharper interaction boundaries will make Meridian feel like one product instead of multiple capable subsystems.

**Unlocks:** Faster adoption of the four-workspace model and reduced context switching.

**Placement:** Critical path.

### 4. Reliability and observability

**Gap:** The underlying mechanisms are strong, but checkpoint evidence, vendor-runtime proof, and operator-facing audit confidence still need closure.

**Value:** Operator trust depends on proving restart, replay, reconnect, and promotion behavior under realistic conditions.

**Unlocks:** Stronger readiness posture and fewer ambiguous production claims.

**Placement:** Critical path.

### 5. Governance productization

**Gap:** Governance now has real seams, but multi-ledger, cash-flow, reconciliation, and reporting are still early product layers rather than finished workflows.

**Value:** This is Meridian's strongest differentiator because it connects strategy workflows with fund-operations discipline inside one platform.

**Unlocks:** A credible front-, middle-, and back-office narrative.

**Placement:** Near-term strategic wave.

### 6. Architecture simplification

**Gap:** The workstation migration could regress into page-local orchestration if shared read models and workflow services stop being the integration seam.

**Value:** Keeping the seam clean now will make later web and WPF work cheaper, safer, and more coherent.

**Unlocks:** Faster iteration without duplicating behavior across surfaces.

**Placement:** Continuous supporting track.

### 7. Testing and validation

**Gap:** The product surface has expanded significantly, but the acceptance story for workstation workflows, replay paths, and cross-workspace operator journeys is still catching up.

**Value:** This reduces regression risk exactly where Meridian is becoming more product-like.

**Unlocks:** Safer roadmap execution and more trustworthy demos and pilot environments.

**Placement:** Critical-path support track.

### 8. Flagship product capabilities

**Gap:** Meridian already has the ingredients for a differentiated self-hosted trading workstation and fund-operations platform, but the story is still fragmented across multiple partially connected seams.

**Value:** Converging strategy runs, portfolio, ledger, Security Master, reconciliation, and reporting into one system is what makes Meridian more than a capable trading stack.

**Unlocks:** A clear end-state product that users can explain and operators can trust.

**Placement:** Shaping force across every active wave.

---

## Target End Product

When this roadmap is finished, Meridian is a workflow-centric trading workstation and fund-operations platform for a self-hosted operator.

The operator can:

- validate provider trust and data quality before relying on research or execution outputs
- run research and backtests in one environment with comparable run history across engines and modes
- promote a strategy from backtest into paper trading through an explicit, auditable workflow
- operate paper and later live-adjacent trading from a cockpit with orders, fills, positions, replay, risk, and session history
- inspect run history, portfolio state, attribution, fills, ledger movements, and reconciliation outcomes through one shared run model
- resolve Security Master issues, governance breaks, cash-flow questions, multi-ledger views, and reporting workflows inside the same product instead of using disconnected tools

The major product surfaces are:

- **Research** for datasets, experiments, backtests, comparisons, fills, attribution, and promotion review
- **Trading** for positions, orders, fills, replay, session control, promotion, and risk-managed operation
- **Data Operations** for providers, symbols, backfills, quality, storage, and export operations
- **Governance** for Security Master, portfolio, ledger, reconciliation, cash-flow, and governed reporting

First-class capabilities in the finished product are:

- evidence-backed data trust and provider validation
- backtest-to-paper workflow continuity
- shared run, portfolio, and ledger visibility across workspaces
- Security Master and governance integrated into the same operator model
- auditable promotion, reconciliation, and reporting workflows

Optional capabilities remain optional:

- L3 inference and queue-aware simulation
- multi-instance scale-out
- deeper QuantScript libraries and advanced research extensions
- assembly-level optimization beyond what the core product requires

---

## Recommended Next Waves

### Wave 1: Provider Reliability and Data Confidence (active)

**Why now:** This is still the main dependency for every downstream readiness claim Meridian wants to make.

**Blueprint:** [`../plans/provider-reliability-data-confidence-wave-1-blueprint.md`](../plans/provider-reliability-data-confidence-wave-1-blueprint.md)

**Focus:**

- expand Polygon replay coverage across feeds and edge cases
- validate Interactive Brokers runtime and bootstrap behavior against real vendor surfaces without conflating simulation, smoke-build, and vendor-runtime modes
- deepen NYSE shared-lifecycle and Level 2 depth coverage while hardening transport behavior around `IHttpClientFactory`, cancellation-safe websocket send, and resubscribe flows
- keep StockSharp connector examples aligned with the adapters Meridian is prepared to validate
- validate backfill checkpoint reliability and gap detection across representative providers and date ranges
- harden the Parquet sink flush path and close remaining ADR-014 cleanup around L2 snapshot persistence
- keep provider-confidence docs and the validation matrix synchronized with executable evidence rather than summary language

**Exit signal:** Every major provider has documented replay or runtime evidence, each supported validation suite passes, and remaining entitlement-bound runtime gaps are explicitly bounded instead of implied away.

### Wave 2: Paper-trading cockpit hardening

**Why now:** Meridian already has a substantial cockpit in code. The highest-value work is hardening and validating it, not inventing it from scratch.

**Focus:**

- tighten positions, orders, fills, P&L, replay, and risk workflows into a dependable operator lane
- keep promotion evaluation and approval explicitly tied to operator review
- verify session persistence and replay behavior under realistic scenarios
- align cockpit behavior with brokerage adapter and provider evidence

**Exit signal:** A strategy can move from backtest into a visible, auditable, dependable paper-trading workflow in the web workstation.

### Wave 3: Shared run, portfolio, and ledger continuity

**Why now:** The shared model already exists and is now the best integration seam in the product.

**Focus:**

- extend run history and comparison depth across backtest, paper, and live-aware modes
- strengthen portfolio, attribution, fills, ledger, and reconciliation continuity
- keep Security Master enrichment tied to the same read-model seam

**Exit signal:** Strategy runs are Meridian's primary cross-workspace product object rather than one of several overlapping representations.

### Wave 4: Governance and Security Master productization

**Why now:** Governance is already visible in code, which makes this a productization problem rather than a foundation problem.

**Focus:**

- make Security Master the authoritative operator-facing instrument layer
- add multi-ledger, cash-flow, reconciliation, and reporting slices on top of shared DTOs and read services
- deepen governance workflows without creating separate reporting or accounting stacks

**Exit signal:** Governance is a real operator workflow with concrete review, drill-in, and export/report seams.

### Wave 5: Backtest Studio unification

**Why now:** Research becomes much stronger once the shared run model is stable enough to unify native and Lean experiences cleanly.

**Focus:**

- unify native and Lean results under one result model
- improve comparison and run-diff tooling
- broaden fill-model realism where it materially changes operator decisions

**Exit signal:** Backtesting feels like one coherent workflow regardless of engine.

### Wave 6: Live integration readiness

**Why now:** Live-readiness should follow, not precede, a trustworthy paper workflow and stable shared operator model.

**Focus:**

- validate at least one brokerage path against a real vendor surface
- add execution audit trail and human approval controls
- define safe `Paper -> Live` gates and operator interventions

**Exit signal:** Meridian can support a controlled live-readiness story without overstating broad live-trading completion.

### Optional wave: Advanced research and scale

**Focus:**

- deeper QuantScript workflow integration
- L3 inference and queue-aware simulation
- multi-instance coordination
- Phase 16 performance work
- broader Phase 1.5 preferred and convertible equity productization beyond the new domain, event-model, and read-query foundation

**Exit signal:** These deepen Meridian's ceiling after the core workstation product is trustworthy and coherent.

---

## Risks and Dependencies

- **Provider trust is still the first dependency.** Without replay and runtime evidence, downstream workflow polish risks overstating readiness.
- **Evidence-strengthening tests are not the same as live-vendor proof.** The April 6 provider additions materially improve confidence, but they do not close IB, StockSharp, Polygon live-runtime, or NYSE auth/rate-limit gaps by themselves.
- **Cockpit hardening should precede live-readiness claims.** Meridian now has meaningful trading surfaces, but operator trust still matters more than feature count.
- **The shared run model must remain the center of gravity.** If research, trading, portfolio, ledger, and governance drift apart again, the workstation migration loses its product logic.
- **Security Master must integrate through shared read models.** It should enrich portfolio, ledger, reconciliation, and reporting flows rather than becoming a parallel subsystem.
- **Governance should extend shared DTOs, not invent a new stack.** Cash-flow, reconciliation, and reporting should reuse the same read-model and export seams already in place.
- **WPF migration should avoid page-level re-fragmentation.** The right move is more orchestration and view-model/service extraction, not more page-local logic.
- **Documentation drift is now a real delivery risk.** The planning set is large enough that roadmap, status, and blueprint documents need deliberate synchronization.

---

## Release Gates

Meridian can reasonably claim core operator-readiness when all of the following are true:

1. Major providers have replay or runtime validation evidence with documented confidence gaps closed or explicitly bounded.
2. Backfill checkpoints and data-gap handling are validated across representative providers and date ranges.
3. The web workstation exposes a dependable paper-trading cockpit, not just endpoint coverage or partial UI.
4. `Backtest -> Paper` is explicit, auditable, and operator-visible.
5. Run history, portfolio, fills, attribution, ledger, and reconciliation views are connected through one shared model across backtest and paper flows.
6. Security Master is operator-accessible and materially integrated into governance-facing workflows.
7. Governance has concrete multi-ledger, reconciliation, cash-flow, and reporting seams built on shared contracts rather than blueprint-only intent.

Until then, Meridian is best described as feature-rich, structurally strong, and actively being productized into its intended workstation end state.

---

## Reference Documents

- [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md)
- [`FULL_IMPLEMENTATION_TODO_2026_03_20.md`](FULL_IMPLEMENTATION_TODO_2026_03_20.md)
- [`IMPROVEMENTS.md`](IMPROVEMENTS.md)
- [`EVALUATIONS_AND_AUDITS.md`](EVALUATIONS_AND_AUDITS.md)
- [`production-status.md`](production-status.md)
- [`OPPORTUNITY_SCAN.md`](OPPORTUNITY_SCAN.md)
- [`TARGET_END_PRODUCT.md`](TARGET_END_PRODUCT.md)
- [`ROADMAP_COMBINED.md`](ROADMAP_COMBINED.md)
- [`../plans/trading-workstation-migration-blueprint.md`](../plans/trading-workstation-migration-blueprint.md)
- [`../plans/governance-fund-ops-blueprint.md`](../plans/governance-fund-ops-blueprint.md)
- [`../plans/meridian-6-week-roadmap.md`](../plans/meridian-6-week-roadmap.md)
- [`../plans/assembly-performance-roadmap.md`](../plans/assembly-performance-roadmap.md)
