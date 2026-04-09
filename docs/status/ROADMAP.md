# Meridian - Project Roadmap

**Last Updated:** 2026-04-08
**Status:** Active productization — platform foundations and the delivered Security Master baseline are materially in code; provider trust, cockpit hardening, shared run continuity, and governance depth remain the path to core operator-readiness
**Repository Snapshot (2026-04-08):** solution projects: 37 | `src/` project files: 28 | test projects: 9 | workflow files: 42

Meridian is no longer primarily blocked on missing platform primitives. The repo already contains a strong market-data, storage, replay, backtesting, execution, ledger, and workstation baseline. The remaining delivery problem is now narrower and more product-shaped: close the trust gaps, workflow gaps, and governance gaps that still separate a feature-rich platform from a genuinely operator-ready trading workstation and fund-operations product.

The active roadmap therefore centers on four outcomes:

- prove operator trust with evidence-backed provider, replay, and checkpoint validation
- harden the workstation workflows already present in web and WPF so they feel reliable, connected, and auditable
- make shared run, portfolio, ledger, and the delivered Security Master baseline the default integration path across Research, Trading, Data Operations, and Governance
- deepen governance and fund-operations workflows without forking the architecture into parallel subsystems

Use this document with:

- [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md) - current capability status
- [`FULL_IMPLEMENTATION_TODO_2026_03_20.md`](FULL_IMPLEMENTATION_TODO_2026_03_20.md) - normalized non-assembly backlog
- [`IMPROVEMENTS.md`](IMPROVEMENTS.md) - completed improvement history
- [`production-status.md`](production-status.md) - current readiness posture and provider-confidence gates
- [`OPPORTUNITY_SCAN.md`](OPPORTUNITY_SCAN.md) - prioritized opportunity framing
- [`TARGET_END_PRODUCT.md`](TARGET_END_PRODUCT.md) - concise end-state product summary
- [`ROADMAP_COMBINED.md`](ROADMAP_COMBINED.md) - shortest combined roadmap, opportunity, and target-state entry point
- [`../plans/trading-workstation-migration-blueprint.md`](../plans/trading-workstation-migration-blueprint.md) - workstation target state
- [`../plans/governance-fund-ops-blueprint.md`](../plans/governance-fund-ops-blueprint.md) - governance target state
- [`../plans/meridian-6-week-roadmap.md`](../plans/meridian-6-week-roadmap.md) - current short-horizon execution plan

---

## Summary

Meridian's platform foundations are already broad enough that roadmap priority should now come from operator value, not from generalized platform sprawl. The repo already includes:

- a strong ingestion and storage baseline with bounded channels, WAL durability, JSONL and Parquet sinks, replay, backfill scheduling, gap analysis, packaging, and export
- shared workstation endpoints and a React workstation shell covering `Research`, `Trading`, `Data Operations`, and `Governance`
- shared `StrategyRun`, portfolio, ledger, and reconciliation read paths in `src/Meridian.Strategies/Services/` and `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`
- execution, paper-trading, strategy lifecycle, and promotion seams, including wired `/api/execution/*` and `/api/promotion/*` surfaces
- a WPF workstation baseline with `StrategyRunsPage`, `RunDetailPage`, `RunPortfolioPage`, `RunLedgerPage`, `RunCashFlowPage`, `RunRiskPage`, and `SecurityMasterPage`
- a delivered Security Master platform seam with shared coverage and provenance flowing across Research, Trading, Portfolio, Ledger, Reconciliation, Governance, and WPF drill-ins

That changes the roadmap emphasis. Meridian does not need another broad foundation phase. It needs disciplined closure on trust, workflow continuity, shared-model adoption, and governance productization.

---

## Current State

### Complete

These are conservative "in code and materially usable" claims as of 2026-04-08:

- Core ingestion pipeline: bounded channels, backpressure handling, WAL durability, composite sinks, graceful shutdown, and structured metrics
- Storage and export foundation: JSONL and Parquet sinks, tiered storage, replay, packaging, export, lineage, catalog, quota enforcement, and lifecycle-policy support
- Broad provider and backfill baseline: streaming providers, historical providers, symbol search, fallback chains, backfill scheduling, checkpointing, gap analysis, and rate-limit handling
- Data-quality foundation: completeness, freshness, anomaly detection, sequence checks, degradation scoring, SLO registry, and quality reporting
- Backtesting baseline: native replay engine, Lean integration, strategy SDK, stored run metrics, fill summaries, and export
- Execution baseline: paper gateway, risk rules, order abstractions, brokerage gateway framework, session endpoints, promotion endpoints, and strategy lifecycle endpoints
- Web workstation shell: routed `Research`, `Trading`, `Data Operations`, and `Governance` workspaces in `src/Meridian.Ui/dashboard/src/app.tsx`
- Research workspace workflows: run filtering, comparison, diffing, attribution drill-ins, fills drill-ins, promotion evaluation, approval, rejection, and promotion history
- Trading workspace workflows: positions, orders, fills, risk panels, order actions, paper-session creation and restore, replay controls, and promotion gate flows
- Data Operations workspace workflows: provider health, backfill queue detail, trigger and preview flows, export visibility, symbol management, and quality monitoring
- Governance workspace workflows: reconciliation queue, break review and resolution, trial-balance drill-ins, reporting profile visibility, Security Master search, and identifier-conflict workflows
- Shared run, portfolio, and ledger read-model baseline: `StrategyRunReadService`, `PortfolioReadService`, and `LedgerReadService` normalize cross-workspace read paths
- Security Master platform seam: hardened WPF activation, shared `WorkstationSecurityReference` coverage and provenance, corporate-action and trading-parameter support, conflict handling, and cross-workspace propagation are in code
- WPF workstation shell modernization: native Fluent theme, SVG icon set, candlestick charting, zero-API-key startup, workflow guide, and screenshot-refresh CI are complete
- Governance baseline: run-scoped reconciliation, direct-lending APIs, export seams, and governance-facing workstation endpoints are present in the repo
- Improvement tracker baseline: Themes A-G and J are closed for the current platform baseline, with Theme K active

### Partial

These areas are real in code but not yet complete enough to treat as fully closed operator workflows:

- Provider confidence remains uneven. Polygon, Interactive Brokers, StockSharp, and NYSE each have stronger replay, contract, and pipeline evidence than earlier snapshots, but the validation matrix still includes partial or missing runtime proof.
- Backfill reliability is broadly implemented but still needs longer-run checkpoint evidence and clearer operator confidence signals across providers and date ranges.
- The web workstation is no longer just a shell, but the paper-trading cockpit still needs hardening around real-vendor validation, richer audit depth, and clearer daily-use acceptance criteria.
- Shared run coverage spans backtest, paper, and live-aware models in contracts and UI, but portfolio, ledger, cash-flow, and reconciliation continuity are not yet equally deep in every mode.
- Governance workflows now build on a delivered Security Master baseline, but account/entity structure, multi-ledger, cash-flow modeling, report-pack generation, and broader exception handling are still early product layers rather than finished experiences.
- WPF workstation migration is meaningfully underway, but high-traffic page redesign and MVVM extraction remain active rather than complete.
- Live brokerage validation and controlled `Paper -> Live` promotion remain incomplete and should not yet be treated as an operator-ready live-trading claim.

### Planned

These are active productization tracks rather than greenfield invention:

- close Wave 1 provider-confidence and checkpoint-evidence gaps
- harden Wave 2 paper-trading cockpit workflows and operator acceptance criteria
- deepen Wave 3 shared run / portfolio / ledger / reconciliation continuity
- productize Wave 4 governance and fund-operations workflows on top of Security Master and shared read-model seams
- unify native and Lean backtesting into one Wave 5 Backtest Studio workflow
- validate at least one broader Wave 6 live brokerage path with explicit audit trail and operator controls

### Optional

These remain valuable, but they are not on the shortest path to Meridian's core operator-readiness path:

- deeper QuantScript workflow integration and broader sample libraries
- L3 inference and queue-aware execution simulation
- multi-instance coordination as a supported scale-out topology
- Phase 16 assembly-level performance optimization
- broader Phase 1.5 preferred and convertible equity productization beyond the shipped domain and read-query foundation
- broader advanced research tooling after the core workstation workflows are operator-ready

---

## What Is Complete

### Platform baseline

- Meridian's ingestion, storage, replay, export, and data-quality stack is no longer a major roadmap blocker.
- The repo has a credible archival and replay platform, broad provider coverage, and materially stronger operational-readiness foundations than earlier roadmap snapshots.
- The historical improvement backlog is effectively closed for the current baseline, which is a real milestone.

### Workstation baseline

- The four-workspace model is present in both planning and implementation.
- The React workstation contains material workflows for Research, Trading, Data Operations, and Governance rather than only navigation and summary surfaces.
- WPF already has meaningful run-centered workstation pages on top of the broader desktop page inventory.

### Shared-model baseline

- `StrategyRunReadService`, `PortfolioReadService`, and `LedgerReadService` give Meridian a stable seam for unifying backtest, paper, live-aware, portfolio, and ledger views.
- Workstation endpoints already expose run comparison, diff, fills, attribution, ledger summaries, reconciliation, and Security Master read paths.

### Security Master baseline

- Security Master is no longer a blueprint-only seam. `SecurityMasterPage`, workstation endpoints, shared security references, conflict handling, corporate actions, and trading-parameter flows are materially in code.
- Meridian now has one authoritative instrument-definition seam that already propagates into Research, Trading, Portfolio, Ledger, Reconciliation, Governance, and WPF drill-ins.

### Governance baseline

- Governance is no longer hypothetical. Security Master, reconciliation, direct lending, export profiles, and governance-facing UI and API seams are real and discoverable in the repo.
- The product gap has shifted from "build governance foundations" to "finish governance productization and workflow continuity."

---

## What Remains

### Wave 1: Provider confidence and checkpoint evidence

- close replay, runtime, auth, and checkpoint evidence gaps so data trust is explicit rather than inferred from architecture
- validate backfill checkpoints, data-gap handling, and Parquet L2 flush behavior across representative providers and date ranges
- keep provider-confidence docs and the validation matrix synchronized with executable evidence rather than summary language

### Wave 2: Paper-trading cockpit hardening

- harden the paper-trading cockpit already present in the web workstation into a dependable daily-use operator lane
- keep `Backtest -> Paper` promotion explicit, auditable, and tied to real operator review
- verify session persistence, replay behavior, and execution-control flows under realistic operator scenarios

### Wave 3: Shared run / portfolio / ledger continuity

- deepen run history, comparison, portfolio, fills, attribution, ledger, cash-flow, and reconciliation continuity through the shared read-model seam
- extend the shared model more evenly across backtest, paper, and live-aware modes
- continue WPF workflow-first page work and MVVM extraction where it directly reinforces the shared-model backbone instead of page-local orchestration

### Wave 4: Governance and fund-operations productization on top of the delivered Security Master baseline

- keep Security Master as the delivered baseline and extend it into deeper account/entity and strategy-structure workflows
- productize multi-ledger, cash-flow, reconciliation, and reporting slices on top of shared DTOs, read services, and export seams
- deepen governance and fund-operations workflows without creating a parallel subsystem

### Wave 5: Backtest Studio unification

- unify native and Lean results under one operator-facing result model
- improve comparison and run-diff tooling
- broaden fill-model realism where it materially changes operator decisions

### Wave 6: Live integration readiness

- validate at least one broader brokerage path against a real vendor surface
- add execution audit trail, human approval controls, and safe `Paper -> Live` gates where evidence already supports them
- keep live-readiness claims narrower than the evidence actually supports

### Optional advanced research / scale tracks

- deeper QuantScript workflow integration
- L3 inference and queue-aware simulation
- multi-instance coordination
- Phase 16 performance work
- broader advanced research extensions after the core operator-readiness path is trustworthy and coherent

---

## Opportunities

The priority order below is the same order used in `What Remains`, `Recommended Next Waves`, and the core operator-readiness gates.

### 1. Wave 1: Close provider-confidence and checkpoint-evidence gaps first

**Gap:** Provider breadth is strong, but operator trust still depends on uneven replay, runtime, auth, and checkpoint evidence.

**Value:** This directly affects confidence in research, paper sessions, promotion decisions, and any future live-readiness claims.

**Placement:** Wave 1, critical path.

### 2. Wave 2: Harden the paper-trading cockpit already in code

**Gap:** Meridian already has substantial operator-facing trading surfaces, but the paper cockpit still stops short of a clearly dependable daily-use workflow.

**Value:** Closing the cockpit journey creates one of the biggest user-visible gains because much of the underlying capability already exists.

**Unlocks:** A credible `Backtest -> Paper` story and safer future live-readiness work.

**Placement:** Wave 2, critical path.

### 3. Wave 3: Make shared run / portfolio / ledger continuity the center of gravity

**Gap:** Shared run, portfolio, and ledger seams exist, but not every workspace relies on them with equal depth yet.

**Value:** One run-centered model makes research, trading, portfolio, ledger, and governance behavior easier to follow and trust.

**Unlocks:** Cleaner cross-workspace UX and less duplicated orchestration logic.

**Placement:** Wave 3, critical path.

### 4. Wave 4: Productize governance and fund-operations on top of the delivered Security Master baseline

**Gap:** Security Master is already the delivered baseline, but the workflows built on top of it still need account/entity, multi-ledger, cash-flow, reconciliation, and reporting depth.

**Value:** This is Meridian's strongest differentiator because it connects strategy workflows with fund-operations discipline inside one product.

**Unlocks:** A credible front-, middle-, and back-office narrative without inventing a second governance stack.

**Placement:** Wave 4, near-term strategic wave.

### 5. Wave 5: Unify Backtest Studio after the core operator-readiness path is stable

**Gap:** Native and Lean backtesting are both present, but they still feel like adjacent tools instead of one Backtest Studio.

**Value:** One backtest experience makes research comparison, promotion review, and engine choice easier to understand.

**Unlocks:** Stronger research ergonomics after Waves 1-4 have closed the core operator-readiness path.

**Placement:** Wave 5, sequenced after Waves 1-4.

### 6. Wave 6: Expand into controlled live integration readiness only after trust and paper-workflow gates are real

**Gap:** Live-facing seams exist, but the repo should not widen live-readiness claims until trust, cockpit, and shared-model gates are materially closed.

**Value:** This turns Meridian's later live story into a measured extension of proven paper workflows instead of a premature claim.

**Unlocks:** A controlled, evidence-backed live-readiness narrative.

**Placement:** Wave 6, sequenced after Waves 1-5.

### 7. Optional advanced research / scale tracks

**Gap:** QuantScript expansion, queue-aware simulation, multi-instance scale, and Phase 16 performance work all deepen Meridian's ceiling, but none should outrank the core operator-readiness path.

**Value:** These tracks become higher leverage once the core workstation product is already trustworthy and coherent.

**Unlocks:** Advanced differentiation after the main product story is already finished.

**Placement:** Optional follow-on work, not part of the core operator-readiness path.

Across Waves 1-4, keep WPF consolidation, shared DTOs, read models, workflow services, export seams, and operator-grade validation supporting the active waves rather than becoming separate roadmap lanes.

---

## Target End Product

Meridian's target end state is a self-hosted trading workstation and fund-operations platform with four connected workspaces: `Research`, `Trading`, `Data Operations`, and `Governance`.

Data Operations establishes evidence-backed provider trust, Research turns that data into reviewed runs, Trading promotes approved runs into paper workflows, and Governance operates on the same instruments and records through the delivered Security Master baseline, portfolio, ledger, reconciliation, cash-flow, and reporting workflows.

The product promise is continuity: one operator can move from data trust to research, paper trading, portfolio and ledger review, and governance workflows without leaving Meridian or losing audit context.

---

## Recommended Next Waves

Across Waves 2-4, keep WPF workflow-first consolidation, validation coverage, and architecture simplification reinforcing the same read-model and orchestration seams rather than becoming a parallel delivery program.

### Wave 1: Provider confidence and checkpoint evidence

**Why now:** This remains the first dependency for every downstream readiness claim Meridian wants to make.

**Focus:**

- expand Polygon replay coverage across feeds and edge cases
- validate Interactive Brokers runtime and bootstrap behavior against real vendor surfaces without conflating smoke builds, simulations, and vendor-runtime modes
- deepen NYSE shared-lifecycle and transport coverage while keeping auth, rate-limit, and cancellation behavior explicit
- keep StockSharp connector guidance aligned with the validated adapter set Meridian is prepared to recommend
- validate backfill checkpoint reliability, gap detection, and Parquet L2 flush behavior across representative providers and windows
- keep provider-confidence docs and the validation matrix synchronized with executable evidence

**Exit signal:** Major providers have documented replay or runtime evidence, each supported validation suite passes, and remaining runtime gaps are explicitly bounded instead of implied away.

### Wave 2: Paper-trading cockpit hardening

**Why now:** Meridian already has a substantial cockpit in code. The highest-value work is hardening and validating it, not inventing it from scratch.

**Focus:**

- tighten positions, orders, fills, replay, and risk workflows into a dependable operator lane
- keep promotion evaluation and approval explicitly tied to operator review
- verify session persistence and replay behavior under realistic scenarios
- align cockpit behavior with brokerage-adapter and provider-confidence evidence

**Exit signal:** A strategy can move from backtest into a visible, auditable, dependable paper-trading workflow in the web workstation.

### Wave 3: Shared run / portfolio / ledger continuity

**Why now:** The shared model already exists and is now the best integration seam in the product.

**Focus:**

- deepen run history and comparison depth across backtest, paper, and live-aware modes
- strengthen portfolio, attribution, fills, ledger, cash-flow, and reconciliation continuity
- keep Security Master enrichment and WPF workflow work tied to the same shared read-model seam

**Exit signal:** Strategy runs become Meridian's primary cross-workspace product object rather than one of several overlapping representations.

### Wave 4: Governance and fund-operations productization on top of the delivered Security Master baseline

**Why now:** Governance is already visible in code, and Security Master is already the delivered authoritative instrument seam, which makes this a workflow-deepening problem rather than a foundation problem.

**Focus:**

- add account/entity and strategy-structure workflows on top of the existing governance baseline
- add multi-ledger, cash-flow, reconciliation, and reporting slices on top of shared DTOs, read services, and export seams
- deepen governance workflows without creating separate reporting or accounting stacks

**Exit signal:** Governance becomes a real operator workflow with concrete review, drill-in, and governed-output seams built on the same contracts already used elsewhere in the workstation.

### Wave 5: Backtest Studio unification

**Why now:** Research becomes much stronger once Waves 1-4 have made the shared run model stable enough to unify native and Lean experiences cleanly.

**Focus:**

- unify native and Lean results under one result model
- improve comparison and run-diff tooling
- broaden fill-model realism where it materially changes operator decisions

**Exit signal:** Backtesting feels like one coherent workflow regardless of engine.

### Wave 6: Live integration readiness

**Why now:** Live-readiness should follow, not precede, a trustworthy paper workflow and stable shared operator model.

**Focus:**

- validate at least one broader brokerage path against a real vendor surface
- add execution audit trail and human approval controls
- define safe `Paper -> Live` gates and operator interventions

**Exit signal:** Meridian can support a controlled live-readiness story without overstating broad live-trading completion.

### Optional advanced research / scale tracks

**Focus:**

- deeper QuantScript workflow integration
- L3 inference and queue-aware simulation
- multi-instance coordination
- Phase 16 performance work
- broader advanced research extensions after the core workstation product is trustworthy and coherent

**Exit signal:** These deepen Meridian's ceiling after the core workstation product is operator-ready.

---

## Risks and Dependencies

- **Provider trust is still the first dependency.** Without replay and runtime evidence, downstream workflow polish risks overstating readiness.
- **Stronger tests are not the same as broad live-vendor proof.** Replay, contract, and pipeline evidence materially improve confidence but do not close every vendor-runtime gap by themselves.
- **Cockpit hardening should precede live-readiness claims.** Meridian now has meaningful trading surfaces, but operator trust still matters more than feature count.
- **The shared run model must remain the center of gravity.** If Research, Trading, Portfolio, Ledger, and Governance drift apart again, the workstation migration loses its product logic.
- **Security Master must remain the authoritative seam.** It should enrich portfolio, ledger, reconciliation, and reporting flows rather than being reimplemented inside parallel governance workflows.
- **Governance should extend shared DTOs, not invent a new stack.** Cash-flow, reconciliation, and reporting should reuse the same read-model and export seams already in place.
- **WPF migration should avoid page-level re-fragmentation.** The right move is more orchestration and view-model/service extraction, not more page-local logic.
- **Documentation drift is now a real delivery risk.** The planning set is large enough that roadmap, status, and blueprint documents need deliberate synchronization.

---

## Release Gates

Meridian can reasonably claim **core operator-readiness** when the wave-aligned gates below are true:

1. **Wave 1 gates:** major providers have documented replay or runtime validation evidence, and backfill checkpoints plus gap handling are validated across representative providers and date ranges.
2. **Wave 2 gates:** the web workstation exposes a dependable paper-trading cockpit, not just endpoint coverage or partial UI, and `Backtest -> Paper` is explicit and auditable.
3. **Wave 3 gates:** run history, portfolio, fills, attribution, ledger, cash-flow, and reconciliation views are connected through one shared model across backtest and paper flows.
4. **Wave 4 gates:** Security Master remains operator-accessible and governance has concrete account/entity, multi-ledger, cash-flow, reconciliation, and reporting seams built on shared contracts rather than blueprint-only intent.

Waves 5 and 6 deepen the product and widen later claims, but they are not prerequisites for core operator-readiness.

Until then, Meridian is best described as feature-rich, structurally strong, and actively being productized into its intended workstation and fund-operations end state.

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
