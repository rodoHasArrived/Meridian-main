# Meridian - Project Roadmap

**Last Updated:** 2026-04-17
**Status:** Active productization — Waves 1-4 remain the core operator-readiness path, and the current working tree shows active WPF workspace-shell consolidation on top of the delivered platform baseline
**Repository Snapshot (2026-04-13 working tree):** solution projects: 39 | `src/` project files: 27 | test projects: 9 | workflow files: 42

Meridian is no longer primarily blocked on missing platform primitives. The repo already contains strong market-data, storage, replay, backtesting, execution, ledger, workstation, and Security Master foundations. The remaining delivery problem is now narrower and more product-shaped: prove operator trust, close workflow gaps, and deepen governance without letting the product split into parallel subsystems.

The active roadmap therefore centers on four outcomes:

- prove operator trust with evidence-backed provider, checkpoint, and replay validation
- harden the paper-trading cockpit already visible in the workstation
- make shared run / portfolio / ledger continuity the default integration path across `Research`, `Trading`, `Data Operations`, and `Governance`
- productize governance and fund-operations on top of the delivered Security Master baseline

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
- [`../plans/brokerage-portfolio-sync-blueprint.md`](../plans/brokerage-portfolio-sync-blueprint.md) - external brokerage and custodian account-sync design
- [`../plans/meridian-6-week-roadmap.md`](../plans/meridian-6-week-roadmap.md) - current short-horizon execution plan

---

## Summary

Meridian's platform foundations are already broad enough that roadmap priority should now come from operator value and readiness evidence, not from generalized platform sprawl. The repo already includes:

- a strong ingestion and storage baseline with bounded channels, WAL durability, JSONL and Parquet sinks, replay, backfill scheduling, gap analysis, packaging, lineage, and export
- shared workstation endpoints and a workstation model organized around `Research`, `Trading`, `Data Operations`, and `Governance`
- shared `StrategyRun`, portfolio, ledger, and reconciliation read paths in `src/Meridian.Strategies/Services/` and `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`
- execution, paper-trading, strategy lifecycle, and promotion seams, including wired `/api/execution/*`, `/api/promotion/*`, and `/api/strategies/*` surfaces
- a WPF workstation baseline with run-centered pages, Security Master drill-ins, and desktop shell modernization already landed
- a delivered Security Master platform seam with shared coverage and provenance flowing across research, trading, portfolio, ledger, reconciliation, governance, and WPF drill-ins

The meaningful repo delta since the April 8 planning refresh is not a new product direction. It is stronger evidence that WPF workflow-first consolidation is actively moving. The current working tree now includes `ShellNavigationCatalog`, workspace shell pages, `MainPageViewModel` shell orchestration, and new shell smoke coverage in `tests/Meridian.Wpf.Tests/Views/`. That is meaningful K1 progress, but it should still be treated as in-flight rather than a closed migration milestone.

---

## Current State

### Complete

These are conservative "in code and materially usable" claims as of 2026-04-13:

- Core ingestion pipeline: bounded channels, backpressure handling, WAL durability, composite sinks, graceful shutdown, and structured metrics
- Storage and export foundation: JSONL and Parquet sinks, tiered storage, replay, packaging, export, lineage, catalog, quota enforcement, and lifecycle-policy support
- Broad provider and backfill baseline: streaming providers, historical providers, symbol search, fallback chains, backfill scheduling, checkpointing, gap analysis, and rate-limit handling
- Data-quality foundation: completeness, freshness, anomaly detection, sequence checks, degradation scoring, SLO registry, and quality reporting
- Backtesting baseline: native replay engine, Lean integration, strategy SDK, stored run metrics, fill summaries, and export
- Execution baseline: paper gateway, risk rules, order abstractions, brokerage gateway framework, session endpoints, promotion endpoints, and strategy lifecycle endpoints
- Web workstation shell: routed `Research`, `Trading`, `Data Operations`, and `Governance` workspaces
- Research workspace workflows: run filtering, comparison, diffing, attribution drill-ins, fills drill-ins, promotion evaluation, approval, rejection, and promotion history
- Trading workspace workflows: positions, orders, fills, risk panels, paper-session creation and restore, replay controls, and promotion gate flows
- Data Operations workflows: provider health, backfill queue detail, trigger and preview flows, export visibility, symbol management, and quality monitoring
- Governance workflows: reconciliation queue detail, trial-balance drill-ins, reporting-profile visibility, Security Master search, and identifier-conflict workflows
- Shared run, portfolio, and ledger read-model baseline: `StrategyRunReadService`, `PortfolioReadService`, and `LedgerReadService` normalize cross-workspace read paths
- Security Master platform seam: workstation coverage, provenance, corporate-action support, trading-parameter support, conflict handling, and cross-workspace propagation are in code
- WPF shell modernization baseline: Fluent theme, SVG icon set, candlestick charting, zero-API-key startup, workflow guide, and screenshot-refresh CI are complete
- Governance baseline: run-scoped reconciliation, direct-lending APIs, export seams, and governance-facing workstation endpoints are present in the repo
- Improvement tracker baseline: Themes A-G and J are closed for the current platform baseline, with Theme K active

### Partial

These areas exist in code but are not yet complete enough to treat as finished operator workflows:

- Provider confidence remains intentionally narrow. The active Wave 1 gate is Alpaca, Robinhood, and Yahoo; Alpaca and Yahoo are repo-closed, Robinhood remains runtime-bounded, and Polygon, Interactive Brokers, NYSE, and StockSharp are deferred from the active gate.
- Backfill reliability and Parquet L2 flush behavior now have repo-backed proof, but the docs, scripts, and operator-facing acceptance language still need to stay synchronized with the validation matrix.
- The web workstation is no longer just a shell, but the paper-trading cockpit still needs stronger daily-use acceptance criteria, replay confidence, audit visibility, and clearer hardening boundaries.
- Shared run coverage spans backtest, paper, and live-aware models in contracts and UI, but portfolio, ledger, cash-flow, and reconciliation continuity are not yet equally deep in every mode.
- External brokerage and custodian account state still reaches Meridian mostly through paper sessions, statement ingestion, and read-model joins; first-class brokerage portfolio sync into fund-account, ledger, and governance workflows is not yet productized.
- Governance workflows now build on a delivered Security Master baseline, but account/entity structure, multi-ledger, cash-flow modeling, report-pack generation, and broader exception handling are still early product layers rather than finished experiences.
- WPF workstation migration is meaningfully underway. The current working tree shows workspace shell descriptors, richer shell navigation, related-workflow routing, and new shell smoke coverage, but that should still count as active K1 delivery work rather than closed migration.
- Live brokerage validation and controlled `Paper -> Live` promotion remain incomplete and should not yet be treated as an operator-ready live-trading claim.

### Planned

These are active productization tracks rather than greenfield invention:

- close the active Wave 1 provider-confidence and checkpoint-evidence gaps for Alpaca, Robinhood, and Yahoo
- harden Wave 2 paper-trading cockpit workflows and operator acceptance criteria
- deepen Wave 3 shared run / portfolio / ledger / reconciliation continuity
- add brokerage and custodian portfolio-sync ingestion through execution and fund-account seams without moving portfolio logic into market-data providers
- productize Wave 4 governance and fund-operations workflows on top of Security Master and shared read-model seams
- unify native and Lean backtesting into one Wave 5 Backtest Studio workflow
- validate at least one broader Wave 6 live brokerage path with explicit audit trail and operator controls

### Optional

These remain valuable, but they are not on the shortest path to Meridian's core operator-readiness path:

- deeper QuantScript workflow integration and broader sample libraries
- L3 inference and queue-aware execution simulation
- multi-instance coordination as a supported scale-out topology
- Phase 16 assembly-level performance optimization
- broader preferred and convertible equity productization beyond the shipped domain and read-query foundation
- broader advanced research tooling after the core workstation workflows are operator-ready

---

## What Is Complete

### Platform baseline

- Meridian's ingestion, storage, replay, export, and data-quality stack is no longer a major roadmap blocker.
- The repo has a credible archival and replay platform, broad provider coverage, and materially stronger operational-readiness foundations than earlier roadmap snapshots.
- The historical improvement backlog is effectively closed for the current platform baseline, which is a real milestone.

### Execution and workflow foundations

- The four-workspace model is present in both planning and implementation.
- The web workstation contains material workflows for `Research`, `Trading`, `Data Operations`, and `Governance` rather than only navigation and summary surfaces.
- WPF already has meaningful run-centered workstation pages on top of the broader desktop page inventory.

### Shared-model baseline

- `StrategyRunReadService`, `PortfolioReadService`, and `LedgerReadService` give Meridian a stable seam for unifying backtest, paper, live-aware, portfolio, and ledger views.
- Workstation endpoints already expose run comparison, diff, fills, attribution, ledger summaries, reconciliation, and Security Master read paths.

### Security Master baseline

- Security Master is no longer a blueprint-only seam. The WPF browser, workstation endpoints, shared security references, conflict handling, corporate actions, and trading-parameter flows are materially in code.
- Meridian now has one authoritative instrument-definition seam that already propagates into Research, Trading, Portfolio, Ledger, Reconciliation, Governance, and WPF drill-ins.

### Governance baseline

- Governance is no longer hypothetical. Security Master, reconciliation, direct lending, export profiles, and governance-facing UI and API seams are real and discoverable in the repo.
- The product gap has shifted from "build governance foundations" to "finish governance productization and workflow continuity."

---

## What Remains

- **Wave 1:** keep the active provider-confidence, checkpoint, and Parquet evidence gate aligned around Alpaca, Robinhood, and Yahoo
- **Wave 2:** turn the current paper-trading cockpit from "visible" into "dependable"
- **Wave 3:** make run history, portfolio, ledger, cash-flow, and reconciliation behave like one cross-workspace model
- **Wave 4:** deepen governance and fund-operations workflows on top of the delivered Security Master baseline
- **Wave 5:** unify native and Lean workflows into one Backtest Studio once the shared model is stable enough to support it cleanly
- **Wave 6:** expand into controlled live integration readiness only after trust and paper-workflow gates are materially closed
- **Optional:** pursue advanced research, simulation, scale-out, and performance tracks only after the core workstation product is coherent and trustworthy

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

- keep Alpaca provider and stable execution seam evidence explicit as the repo-closed core provider baseline
- keep Robinhood supported-surface evidence aligned with its bounded runtime artifact set without overstating live readiness
- formalize Yahoo as a historical-only core provider row backed by deterministic repo tests
- keep checkpoint reliability and Parquet L2 flush behavior on the passing suite list inside `run-wave1-provider-validation.ps1`
- keep provider-confidence docs, deferred-provider language, runtime artifact folders, and the validation matrix synchronized with executable evidence

**Exit signal:** The Wave 1 matrix, roadmap, and status docs all describe the same active provider set, Alpaca and Yahoo remain repo-closed, Robinhood remains explicitly bounded, checkpoint and L2 rows stay closed in repo tests, and deferred providers are not implied to be current blockers.

### Wave 2: Web paper-trading cockpit completion

**Why now:** Meridian already has the execution, session, and promotion APIs. Product value now depends on finishing the operator cockpit.

**Focus:**

- tighten positions, orders, fills, replay, sessions, and risk workflows into a dependable operator lane
- keep promotion evaluation and approval explicitly tied to operator review
- verify session persistence and replay behavior under realistic scenarios
- align cockpit behavior with brokerage-adapter and provider-confidence evidence

**Exit signal:** A strategy can move from backtest into a visible, auditable paper-trading workflow in the web workstation.

### Wave 3: Shared run / portfolio / ledger continuity

**Why now:** The contracts exist, but the product experience around them is not yet fully realized.

**Focus:**

- deepen run history and comparison depth across backtest, paper, and live-aware modes
- strengthen portfolio, attribution, fills, ledger, cash-flow, and reconciliation continuity
- land brokerage and custodian account-sync ingestion that feeds the same shared portfolio, ledger, and reconciliation seams
- keep Security Master enrichment and WPF workflow work tied to the same shared read-model seam

**Exit signal:** Strategy runs become Meridian's primary cross-workspace product object rather than one of several overlapping representations.

### Wave 4: Governance and fund-operations productization on top of the delivered Security Master baseline

**Why now:** Governance is already visible in code, and Security Master is already the delivered authoritative instrument seam. This is now a workflow-deepening problem rather than a missing-foundation problem.

**Focus:**

- add account/entity and strategy-structure workflows on top of the existing governance baseline
- add multi-ledger, cash-flow, reconciliation, and reporting slices on top of shared DTOs, read services, and export seams
- connect external brokerage account state to fund-account review, cash movement, and reconciliation workflows through shared projections
- deepen governance workflows without creating separate reporting or accounting stacks

**Exit signal:** Governance becomes a real operator workflow with concrete review, drill-in, and governed-output seams built on the same contracts already used elsewhere in the workstation.

### Wave 5: Backtest Studio unification

**Why now:** Research becomes much stronger once Waves 1-4 have made the shared run model stable enough to unify native and Lean experiences cleanly.

**Focus:**

- unify native and Lean results under one result model
- improve comparison and run-diff tooling
- broaden fill-model realism
- improve performance for larger windows where it materially changes operator experience

**Exit signal:** Backtesting feels like one coherent workflow regardless of engine.

### Wave 6: Live integration readiness

**Why now:** Live-adjacent credibility should follow, not precede, a finished paper workflow and validated provider trust.

**Focus:**

- validate at least one broader brokerage path against a real vendor surface
- add execution audit trail and human approval controls
- define safe `Paper -> Live` promotion gates
- formalize operator controls such as manual overrides, circuit breakers, and intervention flows

**Exit signal:** Meridian can support a controlled live-readiness story without overclaiming broad live-trading completion.

### Optional advanced research / scale tracks

**Focus:**

- QuantScript deeper integration
- L3 inference and queue-aware execution simulation
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
- **WPF migration should avoid page-level re-fragmentation.** The right move is more orchestration and view-model or service extraction, not more page-local logic.
- **Documentation drift is now a real delivery risk.** The planning set is large enough that roadmap, status, blueprint, and short-horizon docs need deliberate synchronization.

---

## Release Gates

Meridian can reasonably claim **core operator-readiness** when the wave-aligned gates below are true:

1. **Wave 1 gates:** the active provider gate for Alpaca, Robinhood, and Yahoo is documented in executable evidence, checkpoint reliability plus Parquet L2 flush behavior are closed in repo tests, and `run-wave1-provider-validation.ps1` reproduces the offline gate.
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
- [`../plans/trading-workstation-migration-blueprint.md`](../plans/trading-workstation-migration-blueprint.md)
- [`../plans/governance-fund-ops-blueprint.md`](../plans/governance-fund-ops-blueprint.md)
- [`../plans/meridian-6-week-roadmap.md`](../plans/meridian-6-week-roadmap.md)
- [`../plans/assembly-performance-roadmap.md`](../plans/assembly-performance-roadmap.md)
