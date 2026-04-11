# Meridian - Project Roadmap

<<<<<<< HEAD
**Last Updated:** 2026-04-08
**Status:** Active productization — platform foundations and the delivered Security Master baseline are materially in code; provider trust, cockpit hardening, shared run continuity, and governance depth remain the path to core operator-readiness
**Repository Snapshot (2026-04-08):** solution projects: 37 | `src/` project files: 28 | test projects: 9 | workflow files: 42

Meridian is no longer primarily blocked on missing platform primitives. The repo already contains a strong market-data, storage, replay, backtesting, execution, ledger, and workstation baseline. The remaining delivery problem is now narrower and more product-shaped: close the trust gaps, workflow gaps, and governance gaps that still separate a feature-rich platform from a genuinely operator-ready trading workstation and fund-operations product.

The active roadmap therefore centers on four outcomes:

- prove operator trust with evidence-backed provider, replay, and checkpoint validation
- harden the workstation workflows already present in web and WPF so they feel reliable, connected, and auditable
- make shared run, portfolio, ledger, and the delivered Security Master baseline the default integration path across Research, Trading, Data Operations, and Governance
- deepen governance and fund-operations workflows without forking the architecture into parallel subsystems
=======
**Last Updated:** 2026-04-03
**Status:** Active productization — WPF workstation refresh progressing, provider confidence and paper cockpit on critical path
**Repository Snapshot (2026-04-03):** solution projects: 39 | `src/` projects: 28 | test projects: 9 | workflow files: 38

Meridian is no longer primarily blocked on missing platform primitives. The core platform baseline is now broad and structurally strong across ingestion, storage, backtesting, execution, ledger, workstation contracts, and governance foundations. The main delivery problem has shifted from "can Meridian do this at all?" to "which operator workflows are complete enough to trust and use end to end?"

The active roadmap therefore centers on four outcomes:

- finish the reliability evidence that makes the data and execution layers trustworthy
- turn the existing execution and promotion APIs into a real paper-trading cockpit
- make strategy runs, portfolio, ledger, and reconciliation feel like one connected product
- complete the workstation and governance productization work without forking the architecture
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

Use this document with:

- [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md) - current capability status
- [`FULL_IMPLEMENTATION_TODO_2026_03_20.md`](FULL_IMPLEMENTATION_TODO_2026_03_20.md) - normalized non-assembly backlog
- [`IMPROVEMENTS.md`](IMPROVEMENTS.md) - completed improvement history
<<<<<<< HEAD
- [`production-status.md`](production-status.md) - current readiness posture and provider-confidence gates
- [`OPPORTUNITY_SCAN.md`](OPPORTUNITY_SCAN.md) - prioritized opportunity framing
- [`TARGET_END_PRODUCT.md`](TARGET_END_PRODUCT.md) - concise end-state product summary
- [`ROADMAP_COMBINED.md`](ROADMAP_COMBINED.md) - shortest combined roadmap, opportunity, and target-state entry point
- [`../plans/trading-workstation-migration-blueprint.md`](../plans/trading-workstation-migration-blueprint.md) - workstation target state
- [`../plans/governance-fund-ops-blueprint.md`](../plans/governance-fund-ops-blueprint.md) - governance target state
- [`../plans/meridian-6-week-roadmap.md`](../plans/meridian-6-week-roadmap.md) - current short-horizon execution plan
=======
- [`../plans/trading-workstation-migration-blueprint.md`](../plans/trading-workstation-migration-blueprint.md) - workstation target state
- [`../plans/governance-fund-ops-blueprint.md`](../plans/governance-fund-ops-blueprint.md) - governance target state
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

---

## Summary

<<<<<<< HEAD
Meridian's platform foundations are already broad enough that roadmap priority should now come from operator value, not from generalized platform sprawl. The repo already includes:

- a strong ingestion and storage baseline with bounded channels, WAL durability, JSONL and Parquet sinks, replay, backfill scheduling, gap analysis, packaging, and export
- shared workstation endpoints and a React workstation shell covering `Research`, `Trading`, `Data Operations`, and `Governance`
- shared `StrategyRun`, portfolio, ledger, and reconciliation read paths in `src/Meridian.Strategies/Services/` and `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`
- execution, paper-trading, strategy lifecycle, and promotion seams, including wired `/api/execution/*` and `/api/promotion/*` surfaces
- a WPF workstation baseline with `StrategyRunsPage`, `RunDetailPage`, `RunPortfolioPage`, `RunLedgerPage`, `RunCashFlowPage`, `RunRiskPage`, and `SecurityMasterPage`
- a delivered Security Master platform seam with shared coverage and provenance flowing across Research, Trading, Portfolio, Ledger, Reconciliation, Governance, and WPF drill-ins

That changes the roadmap emphasis. Meridian does not need another broad foundation phase. It needs disciplined closure on trust, workflow continuity, shared-model adoption, and governance productization.
=======
Meridian's core platform pillars remain the same: data collection, backtesting, execution, and portfolio or strategy tracking. What has changed since earlier roadmap versions is the degree of completion underneath them. The repository now includes:

- a large provider and storage baseline with WAL, JSONL, Parquet, backfill scheduling, gap analysis, and data quality monitoring
- unified workstation-oriented contracts and bootstrap endpoints in `src/Meridian.Contracts/Workstation/` and `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`
- a React workstation shell with Research, Trading, Data Operations, and Governance screens in `src/Meridian.Ui/dashboard/src/`
- a broad WPF desktop surface including run browser, run detail, portfolio, ledger, Security Master, and QuantScript view models in `src/Meridian.Wpf/`
- execution, promotion, and strategy lifecycle APIs already wired in `ExecutionEndpoints.cs`, `PromotionEndpoints.cs`, and `StrategyLifecycleEndpoints.cs`
- Security Master, ledger, reconciliation, and direct-lending foundations already in code

That means the roadmap should now prioritize workflow completion, operator trust, and product integration over more platform-sprawl.
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

---

## Current State

### Complete

<<<<<<< HEAD
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
=======
These are conservative "in code and materially usable" claims as of 2026-03-31:

- Core ingestion pipeline: bounded channels, backpressure handling, WAL durability, composite sinks, and graceful shutdown
- Storage foundation: JSONL and Parquet sinks, tiered storage, packaging, export, lineage, catalog, quota enforcement, and lifecycle policies
- Broad provider baseline: streaming providers, historical providers, symbol search, fallback chains, and provider registration through DI
- Data quality foundation: completeness, gap analysis, sequence checks, anomaly detection, freshness SLA monitoring, degradation scoring, and quality reporting
- Backtesting foundation: native replay engine, Lean integration, strategy SDK, run storage, and export
- Execution foundation: paper trading gateway, risk rules, order abstractions, brokerage gateway framework, and REST endpoints for account, orders, sessions, health, and promotion
- Workstation contracts and read models: shared strategy-run, portfolio, ledger, reconciliation, and Security Master DTO surfaces
- React workstation shell: routed Research, Trading, Data Operations, and Governance screens with shared bootstrap loading
- WPF workstation baseline: broad page inventory plus run browser, run detail, portfolio, ledger, Security Master, and QuantScript surfaces
- Governance foundations: ledger kernel, Security Master services, reconciliation DTOs and endpoints, direct-lending vertical slice, and export/reporting primitives
- Improvement portfolio A-G and J: the active improvement tracker marks the historical platform-improvement set as complete
- WPF workstation shell modernization: native Fluent theme via `ThemeMode="System"` (PR #524), SVG icon set replacing emoji glyphs (PR #512), LiveCharts2 candlestick charting on the Charting page (PR #522), and zero-API-key startup via Synthetic provider default (PR #513)
- Route and health endpoint reliability: duplicate DFA route definitions and duplicate health endpoint registrations resolved (PRs #521, #519)
- Workflow guide and live screenshots: `docs/WORKFLOW_GUIDE.md` with live UI screenshots checked in (PR #511); CI screenshot-refresh workflow added (PR #515)
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

### Partial

These areas exist in code but are not yet complete enough to treat as finished operator workflows:

<<<<<<< HEAD
- Provider confidence remains uneven. Polygon, Robinhood, Interactive Brokers, StockSharp, and NYSE now have a stricter evidence gate, but several runtime scenarios are still explicitly bounded by vendor sessions, entitlements, or package/runtime dependencies.
- Backfill reliability and Parquet L2 flush behavior now have repo-backed proof, but the docs and automation need to stay synchronized with the validation matrix.
- The web workstation is no longer just a shell, but the paper-trading cockpit still needs hardening around real-vendor validation, richer audit depth, and clearer daily-use acceptance criteria.
- Shared run coverage spans backtest, paper, and live-aware models in contracts and UI, but portfolio, ledger, cash-flow, and reconciliation continuity are not yet equally deep in every mode.
- Governance workflows now build on a delivered Security Master baseline, but account/entity structure, multi-ledger, cash-flow modeling, report-pack generation, and broader exception handling are still early product layers rather than finished experiences.
- WPF workstation migration is meaningfully underway, but high-traffic page redesign and MVVM extraction remain active rather than complete.
- Live brokerage validation and controlled `Paper -> Live` promotion remain incomplete and should not yet be treated as an operator-ready live-trading claim.
=======
- Provider confidence remains uneven. Polygon replay depth, IB runtime validation, NYSE lifecycle hardening, and StockSharp validated adapter breadth still need stronger evidence.
- Backfill reliability is broadly implemented but still needs longer-run checkpoint validation and clearer operator trust signals across providers and date ranges.
- The React workstation shell is real, but several screens still behave like summary-oriented shells over prefetched data rather than fully closed operator workflows.
- The paper-trading cockpit exists as APIs plus a React trading screen, but end-to-end operator depth, live vendor validation, and session replay still need work.
- Shared run, portfolio, ledger, fills, attribution, and reconciliation read paths exist, but cross-mode run history and comparison depth are not yet a finished product.
- Security Master has real services and workstation models, but full downstream integration into governance, portfolio, ledger, and reconciliation workflows is still incomplete.
- WPF workstation shell modernization has landed (Fluent theme, SVG icons, candlestick charting), but the deeper migration from page-dense navigation to workflow-first orchestration — particularly MVVM extraction on high-traffic pages (Live Data, Provider, Backfill, Data Quality) — is still in progress.
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

### Planned

These are active productization tracks rather than greenfield invention:

<<<<<<< HEAD
- close Wave 1 provider-confidence and checkpoint-evidence gaps
- harden Wave 2 paper-trading cockpit workflows and operator acceptance criteria
- deepen Wave 3 shared run / portfolio / ledger / reconciliation continuity
- productize Wave 4 governance and fund-operations workflows on top of Security Master and shared read-model seams
- unify native and Lean backtesting into one Wave 5 Backtest Studio workflow
- validate at least one broader Wave 6 live brokerage path with explicit audit trail and operator controls

### Optional

These remain valuable, but they are not on the shortest path to Meridian's core operator-readiness path:

- deeper QuantScript workflow integration and broader sample libraries
=======
- complete the web paper-trading cockpit and promotion UX on top of the existing execution and promotion endpoints
- deepen run browser, comparison, portfolio drill-in, attribution, and ledger views around the shared run model
- unify native and Lean backtesting into one Backtest Studio workflow
- validate one or more brokerage adapters against live vendor surfaces with audit trail and human approval controls
- finish Security Master and governance productization around reconciliation, multi-ledger views, cash-flow analysis, and report packs
- continue the workstation migration so Research, Trading, Data Operations, and Governance feel like coherent workspaces rather than grouped pages

### Optional

These remain valuable but are not on the critical path to Meridian's core operator-ready product:

- QuantScript deeper workflow integration and a larger script/sample library
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe
- L3 inference and queue-aware execution simulation
- multi-instance coordination as a supported scale-out topology
- Phase 16 assembly-level performance optimization
- broader Phase 1.5 preferred and convertible equity productization beyond the shipped domain and read-query foundation
- broader advanced research tooling after the core workstation workflows are operator-ready
<<<<<<< HEAD
=======
- Phase 1.5 preferred and convertible equity domain extension: `EquityClassification` discriminated union, `PreferredTerms`, `ConvertibleTerms`, and `LiquidationPreference` types in `src/Meridian.FSharp/Domain/SecurityMaster.fs` (issue tracked in `issues/phase_1_5_1_add_equityclassification_discriminator_and_preferredterms_domain_model.md`)
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

---

## What Is Complete

### Platform baseline

<<<<<<< HEAD
- Meridian's ingestion, storage, replay, export, and data-quality stack is no longer a major roadmap blocker.
- The repo has a credible archival and replay platform, broad provider coverage, and materially stronger operational-readiness foundations than earlier roadmap snapshots.
- The historical improvement backlog is effectively closed for the current baseline, which is a real milestone.
=======
- Meridian's ingestion and storage stack is no longer a major roadmap blocker.
- The repo has a credible archival and replay platform, broad provider coverage, and strong observability and operational-readiness foundations.
- The historical improvement backlog is effectively closed for the current baseline, which is a meaningful shift from earlier roadmap snapshots.
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

### Execution and workflow foundations

<<<<<<< HEAD
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
=======
- The execution side now has more than infrastructure: account, positions, portfolio, orders, sessions, promotion, and lifecycle endpoints are already present.
- Brokerage gateway abstractions and provider-specific adapters are in place, even if live validation remains incomplete.
- Shared strategy-run contracts now give the project a stable seam for unifying backtest, paper, live-adjacent, portfolio, and governance surfaces.

### Workstation and governance foundations

- The four-workspace model is present in both planning and implementation.
- The React dashboard already boots those workspaces from explicit workstation endpoints.
- WPF already contains a broad set of pages and view models that map to the workstation direction.
- Governance is no longer hypothetical: Security Master, reconciliation DTOs, ledger read paths, and direct lending are real foundations in the repo.
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

---

## What Remains

### Wave 1: Provider confidence and checkpoint evidence

<<<<<<< HEAD
- close the remaining replay, runtime, auth, and rate-limit gaps so data trust is explicit rather than inferred from architecture
- keep checkpoint reliability and Parquet L2 flush proof on the executable gate instead of drifting back into documentation-only claims
- keep provider-confidence docs, runtime artifact folders, and `run-wave1-provider-validation.ps1` synchronized with executable evidence rather than summary language

### Wave 2: Paper-trading cockpit hardening

- harden the paper-trading cockpit already present in the web workstation into a dependable daily-use operator lane
- keep `Backtest -> Paper` promotion explicit, auditable, and tied to real operator review
- verify session persistence, replay behavior, and execution-control flows under realistic operator scenarios
=======
- finish provider-confidence validation so data trust is evidence-based, not assumed
- complete the paper-trading cockpit in the web workstation with positions, orders, fills, risk, promotion, and replay depth that operators can actually use
- extend the shared run model into a complete run-history and comparison workflow spanning backtest, paper, and live-adjacent modes
- deepen portfolio, attribution, fills, ledger, and reconciliation drill-ins so they support iteration and audit, not just status summaries
- validate at least one brokerage path against a real vendor surface with audit trail and human-approval controls

### Next major productization layer

- finish Security Master as a shared operator capability rather than a backend-only layer
- connect governance surfaces across reconciliation, cash-flow, multi-ledger, and report-pack workflows
- keep the workstation migration centered on orchestration services and shared read models instead of multiplying page-local logic
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

### Wave 3: Shared run / portfolio / ledger continuity

<<<<<<< HEAD
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
=======
- deepen QuantScript integration into research workflows
- turn multi-instance coordination into an explicitly supported topology
- bring L3 inference and queue-aware execution simulation online once core strategy workflows are stable
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

---

## Opportunities

The priority order below is the same order used in `What Remains`, `Recommended Next Waves`, and the core operator-readiness gates.

<<<<<<< HEAD
### 1. Wave 1: Close provider-confidence and checkpoint-evidence gaps first

**Gap:** Provider breadth is strong, but operator trust still depends on uneven replay, runtime, auth, and rate-limit evidence even after the checkpoint and Parquet proof gaps closed.
=======
**Gap:** Meridian now has many of the right contracts, services, and endpoints, but several operator journeys still end at a summary page or a partial handoff.

**Value:** Closing those journeys produces the biggest visible product gain because the platform already has substantial depth underneath.

**Unlocks:** Credible workstation demos, cleaner handoff from research to trading, and a much clearer definition of "operator-ready."

**Placement:** Critical path.

### 2. Operator UX

**Gap:** The workstation shells exist, especially in the React dashboard, but some areas still read as bootstrap summaries more than fully operational tools.
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

**Value:** Better drill-ins, actions, and workflow continuity will make Meridian feel like one system instead of multiple strong subsystems.

<<<<<<< HEAD
**Placement:** Wave 1, critical path.
=======
**Unlocks:** Faster adoption of the four-workspace model and reduced need to context-switch between pages and APIs.
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

### 2. Wave 2: Harden the paper-trading cockpit already in code

<<<<<<< HEAD
**Gap:** Meridian already has substantial operator-facing trading surfaces, but the paper cockpit still stops short of a clearly dependable daily-use workflow.

**Value:** Closing the cockpit journey creates one of the biggest user-visible gains because much of the underlying capability already exists.

**Unlocks:** A credible `Backtest -> Paper` story and safer future live-readiness work.

**Placement:** Wave 2, critical path.
=======
### 3. Provider readiness

**Gap:** Provider breadth is impressive, but trust is still uneven where replay evidence or real-vendor validation is thin.

**Value:** This directly affects confidence in backtests, paper sessions, and eventual live-readiness claims.

**Unlocks:** Safer execution productization and clearer release gates.
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

### 3. Wave 3: Make shared run / portfolio / ledger continuity the center of gravity

**Gap:** Shared run, portfolio, and ledger seams exist, but not every workspace relies on them with equal depth yet.

<<<<<<< HEAD
**Value:** One run-centered model makes research, trading, portfolio, ledger, and governance behavior easier to follow and trust.

**Unlocks:** Cleaner cross-workspace UX and less duplicated orchestration logic.

**Placement:** Wave 3, critical path.
=======
**Gap:** Most of the reliability mechanisms exist, but checkpoint confidence, provider evidence, and execution audit trail still need stronger closure.

**Value:** Operator trust depends on proving that the system behaves correctly at restart, reconnect, replay, and promotion boundaries.

**Unlocks:** Production-readiness claims that are evidence-backed rather than architectural.
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

### 4. Wave 4: Productize governance and fund-operations on top of the delivered Security Master baseline

**Gap:** Security Master is already the delivered baseline, but the workflows built on top of it still need account/entity, multi-ledger, cash-flow, reconciliation, and reporting depth.

<<<<<<< HEAD
**Value:** This is Meridian's strongest differentiator because it connects strategy workflows with fund-operations discipline inside one product.

**Unlocks:** A credible front-, middle-, and back-office narrative without inventing a second governance stack.

**Placement:** Wave 4, near-term strategic wave.

### 5. Wave 5: Unify Backtest Studio after the core operator-readiness path is stable
=======
**Gap:** Governance has strong ingredients in code, but the finished operator experience across Security Master, reconciliation, multi-ledger, cash-flow, and reporting is not yet complete.

**Value:** This is a flagship differentiator because it extends Meridian beyond strategy tooling into fund-operations workflow.

**Unlocks:** A true front-, middle-, and back-office narrative built on shared platform seams.

**Placement:** Later wave, but strategically high value.
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

**Gap:** Native and Lean backtesting are both present, but they still feel like adjacent tools instead of one Backtest Studio.

<<<<<<< HEAD
**Value:** One backtest experience makes research comparison, promotion review, and engine choice easier to understand.

**Unlocks:** Stronger research ergonomics after Waves 1-4 have closed the core operator-readiness path.

**Placement:** Wave 5, sequenced after Waves 1-4.
=======
**Gap:** The workstation migration risks re-creating page-level orchestration if workflow services and shared read models are not kept as the integration seam.

**Value:** A cleaner seam now will make future workstation and governance work cheaper and safer.

**Unlocks:** Faster iteration across web and WPF without duplicated behavior.
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

### 6. Wave 6: Expand into controlled live integration readiness only after trust and paper-workflow gates are real

<<<<<<< HEAD
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

=======
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe
---

## Target End Product

Meridian's target end state is a self-hosted trading workstation and fund-operations platform with four connected workspaces: `Research`, `Trading`, `Data Operations`, and `Governance`.

Data Operations establishes evidence-backed provider trust, Research turns that data into reviewed runs, Trading promotes approved runs into paper workflows, and Governance operates on the same instruments and records through the delivered Security Master baseline, portfolio, ledger, reconciliation, cash-flow, and reporting workflows.

<<<<<<< HEAD
The product promise is continuity: one operator can move from data trust to research, paper trading, portfolio and ledger review, and governance workflows without leaving Meridian or losing audit context.
=======
- acquire and validate market data from multiple providers with visible confidence and quality signals
- research strategies in one Backtest Studio that spans native and Lean-backed runs
- promote a run into paper trading through an explicit, auditable workflow
- operate paper and later live-adjacent trading from a real cockpit with orders, fills, positions, risk controls, and session history
- inspect run history, portfolio state, attribution, fills, and ledger outcomes through one shared strategy-run model
- resolve Security Master issues, reconciliation breaks, cash-flow questions, and reporting outputs inside the Governance workspace instead of using disconnected tools

The major product surfaces are:

- **Research** for datasets, experiments, backtests, comparisons, and export
- **Trading** for orders, fills, positions, promotion, and risk-managed operation
- **Data Operations** for providers, symbols, backfills, storage, and operational export
- **Governance** for portfolio, ledger, reconciliation, Security Master, cash-flow, and governed reporting

First-class capabilities in the finished product are:

- data trust and provider validation
- backtest to paper workflow continuity
- shared run, portfolio, and ledger visibility
- governance and auditability built on the same platform model

Optional capabilities remain optional:

- L3 inference and queue-aware simulation
- multi-instance scale-out
- deeper QuantScript libraries and advanced research extensions
- assembly-level optimization beyond what the core product requires
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

---

## Recommended Next Waves

<<<<<<< HEAD
Across Waves 2-4, keep WPF workflow-first consolidation, validation coverage, and architecture simplification reinforcing the same read-model and orchestration seams rather than becoming a parallel delivery program.

### Wave 1: Provider confidence and checkpoint evidence

**Why now:** This remains the first dependency for every downstream readiness claim Meridian wants to make.

**Focus:**

- expand Polygon replay coverage across feeds and edge cases and keep live reconnect/throttling explicitly bounded until a transcript exists
- validate Interactive Brokers runtime and bootstrap behavior against real vendor surfaces without conflating smoke builds, simulations, and vendor-runtime modes
- deepen NYSE shared-lifecycle and transport coverage while keeping auth, rate-limit, and cancellation behavior explicit
- keep StockSharp connector guidance aligned with the validated adapter set Meridian is prepared to recommend
- keep checkpoint reliability and Parquet L2 flush behavior on the passing suite list inside `run-wave1-provider-validation.ps1`
- keep provider-confidence docs, runtime artifact folders, and the validation matrix synchronized with executable evidence

**Exit signal:** The Wave 1 matrix has no unexplained `❌` rows, checkpoint/L2 rows are closed in repo tests, each supported validation suite passes, and remaining provider gaps are explicitly bounded instead of implied away.
=======
### Wave 1: Provider confidence and trust closure

**Why now:** This is still the main dependency for every downstream claim Meridian wants to make.

**Focus:**

- expand Polygon replay and live-feed evidence
- keep IB runtime validation aligned with current vendor surfaces
- harden NYSE lifecycle and cancellation behavior
- expand validated StockSharp adapter coverage
- validate backfill checkpoints and gap handling across longer date ranges

**Exit signal:** Every major provider has documented replay or runtime evidence and backfill reliability is validated across representative ranges.
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

### Wave 2: Web paper-trading cockpit completion

**Why now:** Meridian already has the execution, session, and promotion APIs. Product value now depends on finishing the operator cockpit.

**Focus:**

<<<<<<< HEAD
- tighten positions, orders, fills, replay, and risk workflows into a dependable operator lane
- keep promotion evaluation and approval explicitly tied to operator review
- verify session persistence and replay behavior under realistic scenarios
- align cockpit behavior with brokerage-adapter and provider-confidence evidence
=======
- deepen the React trading workspace from summary shell to real operator cockpit
- complete positions, orders, fills, P&L, risk, and promotion actions
- add paper-session replay from persisted history
- verify brokerage adapter behavior through the cockpit flows
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

**Exit signal:** A strategy can move from backtest into a visible, auditable paper-trading workflow in the web workstation.

<<<<<<< HEAD
### Wave 3: Shared run / portfolio / ledger continuity
=======
### Wave 3: Shared run model productization
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

**Why now:** The contracts exist, but the product experience around them is not yet fully realized.

**Focus:**

<<<<<<< HEAD
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
=======
- complete run browser and run-detail depth across backtest, paper, and live-adjacent modes
- strengthen comparison, attribution, fills, and equity-curve workflows
- make portfolio and ledger drill-ins part of the same run-centered experience

**Exit signal:** Strategy runs are Meridian's primary product object across research, trading, and governance.

### Wave 4: Backtest Studio unification

**Why now:** Research remains split until native and Lean-backed workflows feel like one product.
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

**Focus:**

- unify native and Lean results under one result model
- improve comparison and run-diff tooling
- broaden fill-model realism
- improve performance for larger windows where it materially changes operator experience

**Exit signal:** Backtesting feels like one coherent workflow regardless of engine.

### Wave 5: Live integration readiness

**Why now:** Live-adjacent credibility should follow, not precede, a finished paper workflow and validated provider trust.

**Focus:**

- validate at least one broader brokerage path against a real vendor surface
- add execution audit trail and human approval controls
- define safe `Paper -> Live` promotion gates
- formalize operator controls such as manual overrides, circuit breakers, and intervention flows

**Exit signal:** Meridian can support a controlled live-readiness story without overclaiming broad live-trading completion.

### Wave 6: Governance and Security Master productization

**Why now:** This is Meridian's strongest strategic expansion once the core run and execution flows are credible.

**Focus:**

- finish Security Master workflow integration across workstation, portfolio, ledger, and reconciliation reads
- deepen reconciliation queues and resolution workflows
- add multi-ledger, trial-balance, and cash-flow views
- generate governed report packs from shared export and audit seams

**Exit signal:** Governance is a genuine operator workflow, not just a set of strong backend foundations.

### Optional advanced research / scale tracks

**Focus:**

- QuantScript deeper integration
- L3 inference and queue-aware execution simulation
- multi-instance coordination
- Phase 16 performance work
<<<<<<< HEAD
- broader advanced research extensions after the core workstation product is trustworthy and coherent

**Exit signal:** These deepen Meridian's ceiling after the core workstation product is operator-ready.
=======

**Exit signal:** These deepen Meridian's ceiling after the core workstation product is complete.
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

---

## Risks and Dependencies

<<<<<<< HEAD
- **Provider trust is still the first dependency.** Without replay and runtime evidence, downstream workflow polish risks overstating readiness.
- **Stronger tests are not the same as broad live-vendor proof.** Replay, contract, and pipeline evidence materially improve confidence but do not close every vendor-runtime gap by themselves.
- **Cockpit hardening should precede live-readiness claims.** Meridian now has meaningful trading surfaces, but operator trust still matters more than feature count.
- **The shared run model must remain the center of gravity.** If Research, Trading, Portfolio, Ledger, and Governance drift apart again, the workstation migration loses its product logic.
- **Security Master must remain the authoritative seam.** It should enrich portfolio, ledger, reconciliation, and reporting flows rather than being reimplemented inside parallel governance workflows.
- **Governance should extend shared DTOs, not invent a new stack.** Cash-flow, reconciliation, and reporting should reuse the same read-model and export seams already in place.
- **WPF migration should avoid page-level re-fragmentation.** The right move is more orchestration and view-model/service extraction, not more page-local logic.
- **Documentation drift is now a real delivery risk.** The planning set is large enough that roadmap, status, and blueprint documents need deliberate synchronization.
=======
- **Provider trust is still the first dependency.** Without replay and runtime evidence, downstream product polish risks overstating readiness.
- **Cockpit completion should precede live-readiness claims.** Meridian now has the APIs and adapters to make this tempting, but operator workflow depth still matters more than breadth.
- **The shared run model must remain the center of gravity.** If backtest, paper, portfolio, ledger, and governance flows drift apart again, the workstation migration loses its product logic.
- **Security Master must integrate through shared read models.** It should enrich portfolio, ledger, and governance flows rather than becoming its own parallel subsystem.
- **Workstation delivery should avoid page-level duplication.** The right move is more orchestration and shared DTOs, not more disconnected UI logic.
- **Governance should follow credible core workflows.** Its strategic value is high, but it lands best once strategy run, portfolio, and paper-trading seams are trustworthy.
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

---

## Release Gates

Meridian can reasonably claim **core operator-readiness** when the wave-aligned gates below are true:

<<<<<<< HEAD
1. **Wave 1 gates:** major providers have documented replay or runtime validation evidence, checkpoint reliability plus Parquet L2 flush behavior are closed in repo tests, and `run-wave1-provider-validation.ps1` reproduces the offline gate.
2. **Wave 2 gates:** the web workstation exposes a dependable paper-trading cockpit, not just endpoint coverage or partial UI, and `Backtest -> Paper` is explicit and auditable.
3. **Wave 3 gates:** run history, portfolio, fills, attribution, ledger, cash-flow, and reconciliation views are connected through one shared model across backtest and paper flows.
4. **Wave 4 gates:** Security Master remains operator-accessible and governance has concrete account/entity, multi-ledger, cash-flow, reconciliation, and reporting seams built on shared contracts rather than blueprint-only intent.
=======
1. Major providers have replay or runtime validation evidence with documented confidence gaps closed or explicitly bounded.
2. The web workstation exposes a complete paper-trading cockpit on top of the existing execution and promotion APIs.
3. `Backtest -> Paper` is explicit, auditable, and operator-visible.
4. Run history, portfolio, fills, attribution, and ledger views are connected through one shared run model across backtest and paper flows.
5. Backfill checkpoints and data-gap handling are validated across representative providers and date ranges.
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

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
- [`../plans/assembly-performance-roadmap.md`](../plans/assembly-performance-roadmap.md)
