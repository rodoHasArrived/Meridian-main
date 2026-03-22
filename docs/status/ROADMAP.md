# Meridian - Project Roadmap

**Version:** 1.7.0
**Last Updated:** 2026-03-21
**Status:** Development / Pilot Ready (hardening and workstation delivery in progress; scale-out remains optional)
**Repository Snapshot:** Solution projects: **32** | `src/` projects: **26** | test projects: **7** | workflow files: **37** | source files (`*.cs`, `*.fs`, `*.xaml` under `src/`): **1,421** | test source files (`*.cs`, `*.fs` under `tests/`): **347** | test methods: **~4,369**

This roadmap is refreshed to match the current repository state and focuses on the remaining work required to move from "production-ready" to a more fully hardened v2.0 release posture.

For a complete per-feature status breakdown see [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md). For the formal target-state product definition and phased capability view, see [`../plans/fund-management-product-vision-and-capability-matrix.md`](../plans/fund-management-product-vision-and-capability-matrix.md). For the module-by-module implementation backlog mapped to projects and file anchors, see [`../plans/fund-management-module-implementation-backlog.md`](../plans/fund-management-module-implementation-backlog.md). For the PR-sequenced execution plan with concurrency lanes, see [`../plans/fund-management-pr-sequenced-roadmap.md`](../plans/fund-management-pr-sequenced-roadmap.md). For the workflow-centric UX and shared run-model migration plan, see [`../plans/trading-workstation-migration-blueprint.md`](../plans/trading-workstation-migration-blueprint.md). For the consolidated non-assembly backlog across roadmap + plan documents, see [`FULL_IMPLEMENTATION_TODO_2026_03_20.md`](FULL_IMPLEMENTATION_TODO_2026_03_20.md).

The active planning set is intentionally normalized around three source-of-truth status documents:

- `ROADMAP.md` for delivery waves and phase ordering
- `FEATURE_INVENTORY.md` for current-vs-target capability status
- `FULL_IMPLEMENTATION_TODO_2026_03_20.md` for the consolidated non-assembly execution backlog

---

## Current State Summary

### What is complete

- **Phases 0–6 are complete** (critical bug fixes, API route implementation, desktop workflow completion, operations baseline, and duplicate-code cleanup).
- **All previously declared stub HTTP routes have been implemented**; `StubEndpoints.MapStubEndpoints()` is intentionally empty and retained as a guardrail for future additions.
- **WPF is the sole desktop client**; UWP has been fully removed.
- **Operational baseline is in place** (API auth/rate limiting, Prometheus export, deployment docs, alerting assets).
- **OpenTelemetry pipeline instrumentation** wired through `TracedEventMetrics` decorator with OTLP-compatible meters.
- **Provider unit tests** expanded for Polygon subscription/reconnect and StockSharp lifecycle scenarios.
- **OpenAPI typed annotations** added to all endpoint families (status, health, backfill, config, providers).
- **Negative-path and schema validation integration tests** added for health/status/config/backfill/provider endpoints.
- **Provider lifecycle consolidation** now covers the active WebSocket-style paths: Polygon uses `WebSocketProviderBase`, NYSE is bridged through a shared-lifecycle client, and StockSharp is explicitly treated as a connector-runtime exception rather than a forced WebSocket-base migration target.

### What remains

Remaining work is tracked in `docs/status/IMPROVEMENTS.md` and the new [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md):

- **35 tracked improvement items total** (core themes A-G)
  - Completed: 35
  - Partial: 0
  - Open: 0
- **8 new theme items** (themes H-I)
  - Completed: 7 (H1, H3, H4, I1, I2, I3, I4)
  - Partial: 0
  - Open: 1 (H2 - Multi-Instance Coordination)
- **8 canonicalization items** (theme J)
  - Completed: 8 (J1-J8 - design, MarketEvent fields, canonicalizer, condition codes, venue normalization, provider wiring, metrics, fixtures/drift canary)
- Architecture debt largely resolved; C1/C2/C3 unified provider registration and lifecycle hardening are complete. StockSharp remains tracked under provider-capability/readability work rather than as a WebSocket-base migration target.
- **Fund-management productization**: Core functionality exists, but the UX remains partly page-centric. The desktop command palette, workspace shells, and persisted workspace/session model now align on `Research`, `Trading`, `Data Operations`, and `Governance`, and the first shared run browser/detail/portfolio/ledger workstation flow is now live in WPF. The next major delivery wave needs to extend that baseline beyond backtest-first data into paper/live workflows, account and entity operations, trade-management flows, investor reporting, and richer cockpit shells.
- **Provider completeness**: Polygon and StockSharp functional with credentials; IB and NYSE require external setup steps.

---

## Phase Status (Updated)

| Phase | Status | Notes |
|---|---|---|
| Phase 0: Critical Fixes | ✅ Completed | Historical blockers closed. |
| Phase 1: Core Stability & Testing Foundation | ✅ Completed (baseline) | Foundation shipped; deeper coverage remains in active backlog (Theme B). |
| Phase 2: Architecture & Structural Improvements | ✅ Completed (baseline) | Follow-on architectural debt tracked in Theme C open items. |
| Phase 3: API Completeness & Documentation | ✅ Completed | Route implementation gap closed; continuing API polish and schema depth in D4/D7. |
| Phase 4: Desktop App Maturity | ✅ Completed | WPF workflow parity achieved; UWP has been removed and WPF is the sole desktop client. |
| Phase 5: Operational Readiness | ✅ Completed | Monitoring/auth/deployment foundations in place. |
| Phase 6: Duplicate & Unused Code Cleanup | ✅ Completed | Cleanup phase closed; residual cleanup now folded into normal maintenance. |
| Phase 7: Extended Capabilities | ⏸️ Optional / rolling | Scheduled as capacity permits. |
| Phase 8: Repository Organization & Optimization | 🔄 In progress (rolling) | Continued doc and code organization improvements. |
| Phase 9: Final Production Release | In progress | Core improvement themes A-G are complete; remaining work is now focused on provider completeness, scale-out, and workstation/product-surface delivery. |
| Phase 10: Scalability & Multi-Instance | 📝 Planned | New phase for horizontal scaling and multi-instance coordination. |
| Phase 11: Trading Workstation Structure | 🔄 Planned / partially represented | Navigation language exists in the command palette, but true workspace-first UX remains to be implemented. |
| Phase 12: Shared Run / Portfolio / Ledger Model | 🔄 In progress | Shared workstation DTOs, read services, and the first WPF browser/detail/portfolio/ledger surfaces are now in code; broader engine and paper/live wiring remain. |
| Phase 13: Backtest + Paper Trading Unification | 📝 Planned | Unify native + Lean backtesting and harden paper-trading operator workflows. |
| Phase 14: Configuration Schema & Drift Canary | Complete | Checked-in config schema generation is authoritative and CI-validated; canonicalization drift reporting and fixture-maintenance workflow are in place. |
| Phase 15: Scalability (Optional) | 📝 Planned | Multi-instance coordination remains optional for single-node deployments. |
| Phase 16: Assembly-Level Performance | 📝 Planned | Byte-level SIMD, algorithmic, and allocation improvements from `docs/evaluations/assembly-performance-opportunities.md`. |

---

### Phase 6: Duplicate & Unused Code Cleanup

- Status: **Completed**. The cleanup phase folded duplicate implementations into shared contracts, retired UWP artifacts, and created the lean baseline that delivered the completed Phases 0-5 narrative.

### Phase 8: Repository Organization & Optimization

- Status: **In progress**. This phase covers documentation refreshes, workspace cleanup, and automation improvements that keep the planning docs, TOCs, and automation scripts aligned with the current repo structure and archive strategy.

## Priority Roadmap (Current Delivery Waves)

This section replaces the old sprint-by-sprint narrative with the current backlog ordering implied by the repository state.

### Wave 1 - Close the remaining current-functionality gaps

- **Provider completeness**: initial Polygon recorded-session replay validation is now committed. The remaining work is broader fixture coverage across feeds and edge cases.
- **Observability polish**: OTLP collector / Jaeger operator setup is now documented. The remaining work is broader host auto-wiring and deeper replay/backfill trace examples.
- **Scale-out planning**: design H2 multi-instance coordination before calling the collector horizontally scalable.

### Wave 2 — Finish provider completeness / current-functionality hardening

- **Polygon**: expand recorded-session validation beyond the initial stock-feed replay fixture.
- **StockSharp**: expand connector validation coverage beyond the newly added connector guide/examples.
- **StockSharp**: keep connector validation coverage and runtime guidance aligned as more named/custom adapters are validated.
- **IB**: keep the new scripted `IBAPI` setup/build instructions and smoke-build path aligned with official vendor releases.
- **Testing**: raise coverage for under-tested providers and backtesting modules.

### Wave 3 — Deliver the workflow-centric workstation baseline

- **Phase 11**: convert navigation from page-first to true `Research` / `Trading` / `Data Operations` / `Governance` workspaces.
- **Phase 12**: shared `StrategyRun`, portfolio, and ledger read models plus comparison-oriented run-service APIs are now in code, and WPF now exposes a first workstation browser/detail/portfolio/ledger flow; next work is broadening those surfaces to paper/live sources and richer cockpit UX.
- **Security Master**: elevate the existing contracts, services, storage, and domain models into an explicit platform track so research, governance, portfolio, and ledger workflows share one authoritative instrument-definition layer.
- **UX debt cleanup**: eliminate orphan pages and placeholder-only surfaces as part of the workstation migration.

### Wave 4 — Unify research, backtest, and paper-trading workflows

- **Phase 13**: deliver Backtest Studio, a paper-trading cockpit, feed-aware execution realism, and promotion workflow guardrails.
- **Portfolio + ledger UX**: make cash-flow, journal, trial-balance, account-summary, and multi-ledger views first-class product surfaces.

### Wave 5 — Build the major planned capabilities that are still blueprint-only

- **QuantScript**: create the project, WPF surface, compiler/runtime pipeline, tests, and example scripts.
- **L3 inference / execution simulation**: implement contracts, reconstruction, inference, simulator, CLI/UI integration, and documentation.

### Wave 6 — Optional scale-out and structural closure

- **H2 / Phase 15**: add multi-instance coordination for shared symbol universes and scheduled work ownership.
- **Readability / cleanup roadmap**: finish the remaining structural refactors, CI consolidation, placeholder labeling, and documentation freshness work.

## Highest-Value Opportunities

These are the best near-term opportunities implied by the current repository state and planning documents.

### 1. Turn the workstation taxonomy into real operator shells

- **Gap:** Meridian now speaks in `Research`, `Trading`, `Data Operations`, and `Governance`, but too much of the experience still falls back to page-centric flows.
- **Value:** This is the point where the product starts to feel like one connected fund-management system instead of a large toolkit.
- **Unlocks:** Cleaner run-browser adoption, cockpit UX, portfolio and ledger productization, and more coherent web parity.
- **Track:** Critical path.

### 2. Extend the shared run / portfolio / ledger model to paper and live history

- **Gap:** Shared workstation DTOs and first WPF drill-ins exist, but they are still backtest-first.
- **Value:** Operators get one mental model for strategy runs, performance, positions, and audit trails across research and trading.
- **Unlocks:** Promotion workflow, cockpit views, portfolio/ledger-first governance workflows, and better cross-engine comparisons.
- **Track:** Critical path.

### 3. Finish provider-confidence hardening where operator trust still depends on docs and replay evidence

- **Gap:** Polygon replay coverage, StockSharp connector validation breadth, and IB/NYSE setup confidence still need ongoing hardening.
- **Value:** Research and paper/live workflows become more trustworthy, supportable, and easier to demo or operate.
- **Unlocks:** Execution-realism work, broader provider claims, and lower-risk workstation rollout.
- **Track:** Critical path.

### 4. Productize portfolio and ledger as first-class governance surfaces

- **Gap:** Shared read models exist, but governance workflows still lag behind research-facing progress.
- **Value:** Meridian becomes stronger as an auditable operator platform, not just a data and backtesting tool.
- **Unlocks:** Reconciliation flows, promotion safety, account-summary UX, and clearer product differentiation.
- **Track:** Later wave, but strategically important.

### 5. Land one flagship capability on top of the stabilized workstation shell

- **Gap:** QuantScript and L3 simulation are still blueprint-only.
- **Value:** Either capability would deepen the Research workspace and make Meridian feel materially more differentiated.
- **Unlocks:** Script-backed experimentation, richer execution analysis, and a stronger end-to-end strategy lifecycle story.
- **Track:** After the workstation baseline is stable.

### Desktop improvements

- **WPF is the sole client** now, and the navigation shells have been aligned around the Research, Trading, Data Operations, and Governance workspaces described in the workstation blueprint.
- **Shared run/portfolio/ledger surfaces** now live inside the desktop experience, with fans for drill-ins, ledger views, and reconciliation helpers that are being polished in the current delivery wave.
- **Operational polish** (observability, configuration, autop-run guards) keeps the desktop app aligned with the platform hardening tracked in `IMPROVEMENTS.md` and the Execution/Observation themes, so QA and operator testing have a clear checklist.

## Target End Product

The intended end product is a comprehensive self-hosted fund management platform for an operator who moves through one connected lifecycle: discover data, run research, manage accounts and entities, compare strategy runs, implement portfolio decisions, manage trades, inspect portfolio and ledger impact, model cash movement, analyze trial-balance and multi-ledger state, reconcile internal and external records, generate investor and stakeholder reports, promote safely into paper trading, and eventually operate live workflows with explicit guardrails.

In that finished state, `Research`, `Trading`, `Data Operations`, and `Governance` are real product surfaces rather than navigation labels. Backtests, paper sessions, and live-facing history share one recognizable run model; account, strategy, portfolio, trade, and ledger views are first-class destinations; Security Master provides the authoritative instrument-definition layer; and provider, replay, storage, diagnostics, observability, reconciliation, and reporting systems support those fund-management workflows instead of feeling like separate tools.

Optional capabilities such as multi-instance scale-out and assembly-level performance work can deepen the platform, but they are not required for Meridian to feel complete as a coherent fund-management system.

## New Improvement Themes

### Theme H: Scalability & Reliability (New)

| ID | Title | Status | Description |
|----|-------|--------|-------------|
| H1 | Per-Provider Backfill Rate Limiting | ✅ Complete | Rate limits are tracked and enforced via `ProviderRateLimitTracker` in the `CompositeHistoricalDataProvider` and `BackfillWorkerService`. |
| H2 | Multi-Instance Symbol Coordination | 📝 Open | Support running multiple collector instances without duplicate subscriptions. Requires distributed locking or leader election for symbol assignment. |
| H3 | Event Replay Infrastructure | ✅ Complete | `JsonlReplayer` and `MemoryMappedJsonlReader` for high-performance replay. `EventReplayService` provides pause/resume/seek controls. CLI `--replay` flag and desktop `EventReplayPage` for UI-based replay. |
| H4 | Graceful Provider Degradation Scoring | ✅ Complete | `ProviderDegradationScorer` computes composite health scores from latency, error rate, connection health, and reconnect frequency. Automatically deprioritizes degraded providers. |

### Theme I: Developer Experience (New)

| ID | Title | Status | Description |
|----|-------|--------|-------------|
| I1 | Integration Test Harness with Fixture Providers | ✅ Complete | `FixtureMarketDataClient` and `InMemoryStorageSink` enable full pipeline integration testing without live API connections. See `tests/.../Integration/FixtureProviderTests.cs`. |
| I2 | CLI Progress Reporting | ✅ Complete | `ProgressDisplayService` provides progress bars with ETA/throughput, Unicode spinners, multi-step checklists, and formatted tables. Supports interactive and CI/CD (non-interactive) modes. |
| I3 | Configuration Schema Validation at Startup | Complete | `SchemaValidationService` validates stored data formats at startup and the checked-in `config/appsettings.schema.json` is generated from config models, linked from the sample config, and validated in CI. |
| I4 | Provider SDK Documentation Generator | ✅ Complete | `generate-structure-docs.py` `extract_providers()` now reads from the correct `src/Meridian.Infrastructure/Providers` path, handles both positional and named `[DataSource]` attribute params, and emits a richer table with Class/Type/Category columns. Historical providers fall back to a curated static list. Run via `make gen-providers`. |

### Theme J: Data Canonicalization (New)

| ID | Title | Status | Description |
|----|-------|--------|-------------|
| J1 | Deterministic Canonicalization Design | ✅ Complete | Design document with provider field audit, condition code mapping, venue normalization, and 3-phase rollout plan. See [deterministic-canonicalization.md](../architecture/deterministic-canonicalization.md). |
| J2 | MarketEvent Canonical Fields | ✅ Complete | `CanonicalSymbol`, `CanonicalizationVersion`, `CanonicalVenue` fields added to `MarketEvent`. `EffectiveSymbol` property for downstream consumers. `MarketDataJsonContext` updated. |
| J3 | EventCanonicalizer Implementation | ✅ Complete | `IEventCanonicalizer` interface and `EventCanonicalizer` class. Resolves symbols via `CanonicalSymbolRegistry`, maps venues, extracts venue from typed payloads. |
| J4 | Condition Code Mapping Registry | ✅ Complete | `ConditionCodeMapper` with `config/condition-codes.json` — 17 Alpaca, 19 Polygon, 8 IB mappings to canonical enum. `FrozenDictionary` for hot-path performance. |
| J5 | Venue Normalization to ISO 10383 MIC | ✅ Complete | `VenueMicMapper` with `config/venue-mapping.json` — 29 Alpaca, 17 Polygon, 17 IB venue mappings to ISO 10383 MIC codes. |
| J6 | Provider Adapter Wiring | ✅ Complete | `CanonicalizingPublisher` decorator wraps `IMarketEventPublisher` with DI registration in `ServiceCompositionRoot`. Pilot symbol filtering, dual-write mode, lock-free metrics. |
| J7 | Canonicalization Metrics & Monitoring | ✅ Complete | `CanonicalizationMetrics` with per-provider parity stats. API endpoints for status, parity, and config. Thread-safe counters for success/fail/unresolved. |
| J8 | Golden Fixture Test Suite | Complete | 8 curated fixture `.json` files are CI-backed, the PR workflow emits an actionable canonicalization drift report for unmapped condition codes/venues, and a manual fixture-maintenance workflow preserves the suite. |

---

## 2026 Delivery Objectives

### Objective 1: Test Confidence ✅ Achieved

- ✅ Expanded integration and provider tests — 12 provider test files, 273 test files total, ~4,093 test methods.
- ✅ Risk-based coverage with negative-path and schema validation tests.
- ✅ Integration test harness with `FixtureMarketDataClient` and `InMemoryStorageSink`.

### Objective 2: Architectural Sustainability ✅ Substantially Achieved

- ✅ C1/C2 complete — unified `ProviderRegistry` and single DI composition path.
- ✅ Static singletons replaced with injectable `IEventMetrics`.
- ✅ Consolidated configuration validation pipeline.
- C3 (provider lifecycle consolidation) is closed for the current platform baseline: Polygon uses the shared WebSocket base, NYSE is bridged onto the shared lifecycle path, and StockSharp is explicitly documented as a connector-runtime exception.

### Objective 3: API Productization ✅ Achieved

- ✅ Quality metrics API fully exposed (`/api/quality/drops`, per-symbol drill-down).
- ✅ Typed OpenAPI annotations across all endpoint families (58+ endpoints).
- ✅ 283 route constants with 0 stubs remaining.

### Objective 4: Operational Hardening 🔄 Mostly Achieved

- ✅ Prometheus metrics, API auth/rate limiting, category-accurate exit codes.
- ✅ OpenTelemetry pipeline instrumentation with activity spans.
- End-to-end trace context propagation is in place across collector ingress, pipeline queueing/consumption, and storage append paths, with correlation IDs added to structured logs.

### Objective 5: Scalability 🔄 Partially Achieved

- ✅ Per-provider rate limit enforcement via `ProviderRateLimitTracker`.
- ✅ Provider degradation scoring via `ProviderDegradationScorer`.
- 📝 H2 multi-instance coordination pending (not needed for single-instance).

### Objective 6: Cross-Provider Data Canonicalization ✅ Substantially Achieved

- ✅ Design document complete with provider field audit and 3-phase rollout plan.
- ✅ J2–J7 fully implemented: canonical fields on `MarketEvent`, `EventCanonicalizer`, `ConditionCodeMapper`, `VenueMicMapper`, `CanonicalizingPublisher` decorator with DI wiring, `CanonicalizationMetrics` with API endpoints.
- J8 is complete: golden fixtures are CI-backed, unmapped canonicalization drift is reported in PR-visible artifacts/summary output, and fixture-maintenance automation is available.
- Target: >= 99.5% canonical identity match rate across providers for US liquid equities.

---

## Success Metrics (Updated Baseline)

| Metric | Current Baseline | 2026 Target |
|---|---:|---:|
| Stub endpoints remaining | 0 | 0 |
| Core improvement items completed | 35 / 35 | 35 / 35 |
| Core improvement items still open | 0 / 35 | 0 / 35 |
| New theme items (H/I) completed | 7 / 8 | 8 / 8 |
| Source files | 832 | — |
| Test files | 273 | 300+ |
| Test methods | ~4,093 | 4,500+ |
| Route constants | 309 | 309 |
| Architecture debt (Theme C completed) | 7 / 7 | 7 / 7 |
| Provider test coverage | All 5 streaming providers + failover + backfill | Comprehensive |
| OpenTelemetry instrumentation | Trace context propagated across collector ingress, pipeline, and storage with correlation-log scopes | Full trace propagation |
| OpenAPI typed annotations | All endpoint families | Complete with error response types |
| Canonicalization design | Complete | Implementation complete |
| Canonicalization implementation (J2-J8) | 7 / 7 | 7 / 7 |
| Cross-provider canonical identity match | N/A | >= 99.5% |
| WPF pages with live data | ~45 / 51 | 51 / 51 |

---

## Phases 11–13: Trading Workstation Migration Roadmap

These phases convert Meridian from a broad feature suite into a workflow-centric trading workstation. The target state is documented in [`../plans/trading-workstation-migration-blueprint.md`](../plans/trading-workstation-migration-blueprint.md).

### Phase 11: Trading Workstation Structure

**Goal:** Restructure the desktop and web UX around operator workflows instead of a long list of loosely-related pages.

| Item | Area | Work |
|------|------|------|
| P11-1 | **Navigation / IA** | Reorganize primary navigation into `Research`, `Trading`, `Data Operations`, and `Governance` workspaces; keep existing pages reachable through workspace tabs and command palette actions |
| P11-2 | **Discoverability** | Register all major trading/backtesting pages consistently in WPF navigation and command palette; eliminate orphan functionality that exists only as a page type or deep-link target |
| P11-3 | **Workflow shell** | Add workspace-level headers, quick actions, and cross-links between backtest, portfolio import, live monitoring, and diagnostics flows |
| P11-4 | **Documentation / UX truth** | Replace placeholder-page-centric status tracking with workflow-centric status tracking in docs and status dashboards |

**Exit criteria:** All major capabilities are discoverable through the four top-level workspaces, and workflow entry points exist for research, paper trading, and governance.

---

### Phase 12: Shared Run / Portfolio / Ledger Model

Current baseline now in code:

- Shared workstation DTOs exist for `StrategyRun`, portfolio summaries, ledger summaries, trial-balance rows, journal rows, and run comparison views.
- `StrategyRunReadService`, `PortfolioReadService`, and `LedgerReadService` derive comparison-friendly read models from recorded strategy/backtest results.
- `StrategyRunStore` now exposes all-run enumeration so a run browser can be built on one shared query path.
- WPF now ships `StrategyRuns`, `RunDetail`, `RunPortfolio`, and `RunLedger` surfaces, with backtest completion feeding the shared workstation browser and drill-ins from research/trading entry points.

**Goal:** Standardize Meridian around a shared strategy-run lifecycle and elevate portfolio + ledger state to first-class product objects.

| Item | Area | Work |
|------|------|------|
| P12-1 | **Run model** | Shared DTOs and normalized run summary/detail services are implemented; extend them beyond the current backtest-first baseline into paper/live history inputs |
| P12-2 | **Portfolio model** | Shared portfolio summary/position read models are implemented from recorded run results; add equity-history, cash-flow views, and broader engine/live sources |
| P12-3 | **Ledger model** | Shared ledger summary, journal, and trial-balance read models are implemented; add richer account-summary, cash-flow modeling, multi-ledger tracking, and reconciliation-oriented views |
| P12-4 | **Run browser** | Comparison-friendly run query services plus first-pass WPF browser/detail/portfolio/ledger UI flows are implemented; extend them into paper/live run history and richer workstation interactions |

**Exit criteria:** Backtest, paper, and live-facing experiences share a recognizable run model, and users can inspect portfolio and ledger state from product UI surfaces.

---

### Phase 12A: Security Master Productization

Current baseline now in code:

- Security Master contracts, query/service abstractions, storage mappings, migrations, and projection services exist in the repository.
- F# domain modules now cover security identifiers, classification, economic definitions, commands, events, and legacy upgrade logic.

**Goal:** Make Security Master an explicit platform capability that feeds research, governance, portfolio, ledger, and future cash-flow modeling workflows.

| Item | Area | Work |
|------|------|------|
| P12A-1 | **Authoritative identifiers** | Promote Security Master queries and resolver flows into the shared operator model so portfolio, ledger, and governance surfaces use one authoritative instrument identity layer |
| P12A-2 | **Economic definitions** | Surface economic-definition metadata needed for instrument classification, attribution, and future cash-flow projections |
| P12A-3 | **Workflow integration** | Wire Security Master into workstation-facing research, governance, and portfolio/ledger paths instead of leaving it as backend-only infrastructure |

**Exit criteria:** Security Master is explicitly represented in the roadmap and actively used as the authoritative metadata layer for shared run, portfolio, ledger, and governance workflows.

---

### Phase 13: Backtest + Paper Trading Unification

**Goal:** Make research and execution feel like one lifecycle rather than parallel systems.

| Item | Area | Work |
|------|------|------|
| P13-1 | **Backtest Studio** | Unify native Meridian backtests and Lean backtests behind a single operator-facing Backtest Studio with engine selection, parameters, comparisons, and “open portfolio / open ledger” actions |
| P13-2 | **Trading cockpit** | Promote current live viewer and execution primitives into a proper paper-trading cockpit with strategies, orders, fills, positions, exposure, and risk panels |
| P13-3 | **Execution realism** | Replace scaffold-only paper fill assumptions with feed-aware simulated pricing and make execution assumptions visible in the UI |
| P13-4 | **Promotion workflow** | Add controlled workflow states and safety checks for Backtest → Paper → Live progression |

**Exit criteria:** Meridian presents one coherent strategy lifecycle from research through paper trading, with explicit promotion and audit surfaces.

---

### Phase 14: Configuration Schema & Test Completeness

**Goal:** Capture the now-completed I3 and J8 closure so the workflow remains visible in the roadmap history.

| Item | Area | Work |
|------|------|------|
| P14-1 | **I3 Config JSON Schema** | Completed: `--generate-config-schema` produces the checked-in `config/appsettings.schema.json`, the sample config references it, and CI fails on drift |
| P14-2 | **J8 Drift-canary CI** | Completed: PR checks generate a canonicalization drift report artifact/summary and a manual maintenance workflow supports fixture upkeep |

**Exit criteria:** `appsettings.schema.json` present and linked; IDE shows validation on `appsettings.json`. CI fails on unrecognized condition codes or venues and reports the detected drift clearly enough for operators/developers to act on it.

---

### Phase 15: Scalability (Optional)

**Goal:** Support multiple collector instances without subscription conflicts.

| Item | Area | Work |
|------|------|------|
| P15-1 | **H2 Multi-instance coordination** | Design and implement distributed locking for symbol subscription assignment (Redis or file-based lock); leader election for scheduled backfill; documented topology for 2-node active/active deployment |

**Exit criteria:** Two collector instances can run simultaneously against the same symbol universe without duplicate subscriptions or conflicting backfill jobs.

---

### Phase 16: Assembly-Level Performance Optimizations

**Goal:** Apply the highest-ROI performance improvements from
[`docs/evaluations/assembly-performance-opportunities.md`](../evaluations/assembly-performance-opportunities.md)
to CPU-bound hot paths, guided by `BenchmarkDotNet` evidence. All changes must include
before/after benchmark results and must keep scalar fallback paths that produce identical output.

#### Viability Assessment

The evaluation document identifies 7 candidates. The table below scores each against delivery
risk and expected impact to inform prioritization.

| # | Candidate | Viability | Risk | Expected Gain | Priority |
|---|-----------|-----------|------|---------------|----------|
| P16-1 | Vectorized `\n` scan — `MemoryMappedJsonlReader` | **High** | Low — `SearchValues<byte>` portable fallback available | 1.5–4× scan throughput on large replay files | 1 |
| P16-2 | Deferred UTF-16 decode — `MemoryMappedJsonlReader` | **Medium** | Medium — requires caller API change to pass `ReadOnlyMemory<byte>` | Eliminates millions of per-line `string` allocations for 1 GB+ uncompressed replays | 2 |
| P16-3 | UTF-8 sequence extraction — `DataQualityScoringService` | **High** | Low — change isolated to one method | Eliminates 10 000 `string` allocations per scored file; removes double `OrdinalIgnoreCase` scan | 3 |
| P16-4 | Binary-search buckets + circular sample ring — `LatencyHistogram` | **High** | Low — pure algorithmic drop-in, no SIMD required | Removes hidden O(n) copy under lock on every event; reduces GC pressure | 4 |
| P16-5 | Ring-buffer `EventBuffer.Drain(maxCount)` | **Medium** | Medium — data-structure replacement required | Eliminates `GetRange` + front-shift cost during per-symbol sink flushes | 5 |
| P16-6 | `DrainBySymbol` symbol-interning + in-place partition | **High** | Low — `SymbolTable` already exists in `Core/Performance/` | Eliminates two list allocations and a full-buffer copy per drain call | 6 |
| P16-7 | SIMD rolling statistics — `AnomalyDetector.SymbolStatistics` | **Low–Medium** | High — only profitable if the path is CPU-hot (unconfirmed) | Modest gain in high-symbol-count configs; profile-first rule applies | 7 |

**Key viability principle:** P16-1 through P16-6 are independently deliverable — each is
localized to a single class or method and validated by the existing test suite without
modification. P16-7 must be deferred until profiling confirms it is a hot path under a
realistic workload.

#### Implementation Items

| Item | File(s) | Work |
|------|---------|------|
| P16-1 | `src/Meridian.Storage/Replay/MemoryMappedJsonlReader.cs` | Replace scalar `buffer[i] == '\n'` loop with `ReadOnlySpan<byte>.IndexOf` backed by `SearchValues<byte>`; add AVX2 fast path gated by `Avx2.IsSupported` with a static readonly dispatch delegate |
| P16-2 | `src/Meridian.Storage/Replay/MemoryMappedJsonlReader.cs` | Defer `Encoding.UTF8.GetString` — pass `ReadOnlyMemory<byte>` slices to `DeserializeLines`; decode only after a line passes downstream filters. Deliver after P16-1 is merged and benchmarked |
| P16-3 | `src/Meridian.Storage/Services/DataQualityScoringService.cs` | Replace `File.ReadLinesAsync` + `string.IndexOf` + `char.IsDigit` with a `PipeReader` / `ArrayPool<byte>` reader and a `TryExtractSequenceUtf8(ReadOnlySpan<byte>, out long)` helper; use `u8` string literals for key probes |
| P16-4a | `src/Meridian.Application/Monitoring/LatencyHistogram.cs` | Replace linear `for` bucket scan with `Array.BinarySearch`; add `[MethodImpl(AggressiveInlining)]` |
| P16-4b | `src/Meridian.Application/Monitoring/LatencyHistogram.cs` | Replace `List<LatencySample>` + `RemoveAt(0)` overflow with a fixed-size `LatencySample[]` circular ring indexed by `_sampleHead % MaxSamples` |
| P16-5 | `src/Meridian.Storage/Services/EventBuffer.cs` | Introduce a ring-buffer backed `Drain(int maxCount)` overload using a power-of-two array with `_head`/`_tail` indices; retain existing path for callers that pass no pooled buffer |
| P16-6 | `src/Meridian.Storage/Services/EventBuffer.cs` | Replace `DrainBySymbol` two-list reconstruction with an in-place partition using `SymbolTable.GetOrAdd` for integer symbol comparison; expose `DrainBySymbolId(int)` as the preferred API |
| P16-7 | `src/Meridian.Application/Monitoring/DataQuality/AnomalyDetector.cs` | **Profile first.** If `SymbolStatistics.RecordTrade` is confirmed CPU-hot, apply `Vector<double>` horizontal-add for rolling mean/stddev and switch price window to struct-of-arrays layout |
| P16-8 | `benchmarks/Meridian.Benchmarks/` | Add benchmark cases: newline scan (1 KB / 64 KB / 4 MB), sequence parse (1 K / 10 K lines), bucket selection (8 / 16 / 32 boundaries), `Drain` ring vs `GetRange`, `DrainBySymbol` two-list vs in-place partition |

#### Delivery Notes

- **P16-1 and P16-3** are the recommended starting points: both are fully localized, have
  complete implementation sketches in the evaluation document, and carry the lowest rollback risk.
- **P16-4** (binary search + circular ring) should be a single PR — both sub-items are in the
  same class and complement each other.
- **P16-2** (deferred UTF-16) should follow P16-1: extend the same method once the scan
  refactor is merged and its benchmark improvement is confirmed.
- Every optimization PR description must include `BenchmarkDotNet` before/after results (P16-8
  additions serve as the benchmark harness for all other items).
- P16-7 is gated on profiling evidence and should not be started without it.

**Exit criteria:** `BenchmarkDotNet` before/after results attached to each PR show a measurable
improvement on representative datasets. All existing tests pass with every optimized path
active. Scalar fallback paths produce bit-identical results to SIMD paths. GC-allocated bytes
per operation do not regress relative to the pre-optimization baseline.

---

## Governance and Fund Operations Expansion

This section turns the Meridian governance/fund-ops discussion into a concrete roadmap slice. It is intended to deliver a Meridian-native capability set that supports FundStudio-style workflows without abandoning Meridian's local-first workstation model.

### Product intent

Target Meridian outcome:

- Security Master acts as the authoritative instrument-definition layer.
- Governance exposes cash-flow modeling, trial-balance analysis, and multi-ledger tracking as first-class surfaces.
- Governance includes a dedicated reconciliation engine and report generation toolchain.
- Portfolio, ledger, compliance, reconciliation, and reporting workflows share one operator-facing model instead of fragmented utilities.

### Suggested implementation split

- **Prefer F# for core domain kernels:** Security Master domain logic, cash-flow projection rules, fund-accounting transforms, multi-ledger consolidation rules, trial-balance math, and policy/state-machine logic.
- **Prefer C# for orchestration and product surfaces:** query services, workflow coordination, DI wiring, storage integration, HTTP endpoints, WPF view models, and export/report generation.

### Phase F1: Security Master as a Product Platform

**Goal:** Move Security Master from backend capability to explicit platform infrastructure used by Research, Governance, portfolio, and ledger flows.

| Epic | User outcome | Suggested anchors |
|---|---|---|
| F1-1 Authoritative identifier workflow | Operators can resolve, inspect, and trust canonical instrument identity across portfolio, ledger, and governance views | `src/Meridian.Contracts/SecurityMaster/SecurityDtos.cs`, `src/Meridian.Application/SecurityMaster/SecurityMasterQueryService.cs`, `src/Meridian.Application/SecurityMaster/SecurityResolver.cs`, `src/Meridian.FSharp/Domain/SecurityIdentifiers.fs`, `src/Meridian.FSharp/Domain/SecurityMaster.fs` |
| F1-2 Economic-definition enrichment | Governance and research surfaces can classify instruments by economic definition, not only symbol strings | `src/Meridian.Application/SecurityMaster/SecurityEconomicDefinitionAdapter.cs`, `src/Meridian.FSharp/Domain/SecurityEconomicDefinition.fs`, `src/Meridian.FSharp/Domain/SecurityClassification.fs`, `src/Meridian.FSharp/Domain/SecurityTermModules.fs` |
| F1-3 Security Master workstation integration | Users can open Security Master-backed details from portfolio, run, and governance surfaces | `src/Meridian.Contracts/Workstation/`, `src/Meridian.Strategies/Services/PortfolioReadService.cs`, `src/Meridian.Strategies/Services/LedgerReadService.cs`, `src/Meridian.Wpf/ViewModels/StrategyRunDetailViewModel.cs`, `src/Meridian.Wpf/Services/StrategyRunWorkspaceService.cs` |

**Exit criteria:** Security Master metadata is visible in workstation-facing flows and is the default authority for instrument identity, classification, and economic-definition lookups.

### Phase F2: Multi-Ledger Governance Foundation

**Goal:** Turn the existing ledger capability into a governance-grade multi-ledger model suitable for funds, sleeves, vehicles, and entity-level reporting.

| Epic | User outcome | Suggested anchors |
|---|---|---|
| F2-1 Ledger grouping and entity model | Users can track separate ledgers for funds, sleeves, strategies, or legal entities | `src/Meridian.Ledger/LedgerBookKey.cs`, `src/Meridian.Ledger/ProjectLedgerBook.cs`, `src/Meridian.Ledger/LedgerQuery.cs`, `src/Meridian.Strategies/Services/LedgerReadService.cs` |
| F2-2 Consolidated trial balance | Users can inspect per-ledger and consolidated trial-balance views | `src/Meridian.Ledger/LedgerSnapshot.cs`, `src/Meridian.Ledger/LedgerBalancePoint.cs`, `src/Meridian.Ledger/LedgerAccountSummary.cs`, `src/Meridian.Wpf/ViewModels/StrategyRunLedgerViewModel.cs`, `src/Meridian.Wpf/Views/RunLedgerPage.xaml` |
| F2-3 Cross-ledger reconciliation | Governance can trace transfers, eliminations, and reconciliation breaks across ledgers | `src/Meridian.Ledger/JournalEntry.cs`, `src/Meridian.Ledger/LedgerEntry.cs`, `src/Meridian.Ledger/Ledger.cs`, `src/Meridian.Strategies/Services/LedgerReadService.cs` |
| F2-4 Reconciliation engine baseline | Operations can run rule-based reconciliations between portfolio, ledger, cash, positions, and external statements with explicit break queues | `src/Meridian.Strategies/Services/LedgerReadService.cs`, `src/Meridian.Strategies/Services/PortfolioReadService.cs`, new `src/Meridian.Application/Services/ReconciliationEngineService.cs`, new `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs`, new `src/Meridian.FSharp.Ledger/ReconciliationRules.fs` |

**Exit criteria:** Meridian supports trial balance, rule-based reconciliation, and multi-ledger drill-ins in Governance.

### Phase F3: Cash-Flow Modeling and Projection

**Goal:** Add forward-looking cash intelligence on top of portfolio, ledger, and Security Master state.

| Epic | User outcome | Suggested anchors |
|---|---|---|
| F3-1 Projected cash ladder | Users can see expected inflows/outflows by day, source, and vehicle | `src/Meridian.Strategies/Services/PortfolioReadService.cs`, `src/Meridian.Strategies/Services/LedgerReadService.cs`, `src/Meridian.FSharp.Ledger/`, `src/Meridian.FSharp/Promotion/`, new `src/Meridian.FSharp/Domain/CashFlowProjection.fs` |
| F3-2 Instrument-aware cash events | Coupons, financing, fees, distributions, and instrument-specific cash events use Security Master economic definitions | `src/Meridian.Application/SecurityMaster/SecurityEconomicDefinitionAdapter.cs`, `src/Meridian.FSharp/Domain/SecurityEconomicDefinition.fs`, new `src/Meridian.FSharp/Domain/CashFlowRules.fs` |
| F3-3 Governance cash views | Governance surfaces expose sources/uses, projected liquidity gaps, and realized-vs-projected cash | `src/Meridian.Contracts/Workstation/`, `src/Meridian.Wpf/ViewModels/StrategyRunPortfolioViewModel.cs`, `src/Meridian.Wpf/ViewModels/StrategyRunLedgerViewModel.cs`, `src/Meridian.Wpf/Views/RunPortfolioPage.xaml`, `src/Meridian.Wpf/Views/RunLedgerPage.xaml` |

**Exit criteria:** Governance can present projected cash movement and reconcile it against realized ledger activity.

### Phase F4: Fund Operations Workstation

**Goal:** Give operations, PM, and governance users one coherent workflow for fund-style operations.

| Epic | User outcome | Suggested anchors |
|---|---|---|
| F4-1 Governance dashboard | Users can open one workspace for ledger health, cash status, valuation exceptions, and reconciliation breaks | `src/Meridian.Wpf/Services/WorkspaceService.cs`, `src/Meridian.Wpf/ViewModels/RunMatViewModel.cs`, `src/Meridian.Wpf/Views/RunMatPage.xaml`, `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs` |
| F4-2 NAV and attribution baseline | Users can inspect fund-level valuation and contribution drivers | `src/Meridian.Strategies/Services/PortfolioReadService.cs`, `src/Meridian.Strategies/Services/StrategyRunReadService.cs`, `src/Meridian.Ledger/LedgerAccountSummary.cs`, new `src/Meridian.Application/Services/NavAttributionService.cs` |
| F4-3 Reporting/export pack | Governance can export auditable cash-flow, trial-balance, portfolio, and ledger packs | `src/Meridian.Storage/Export/AnalysisExportService.cs`, `src/Meridian.Storage/Export/ExportProfile.cs`, `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs`, `src/Meridian.Wpf/ViewModels/StrategyRunLedgerViewModel.cs` |
| F4-4 Report generation tools | Users can generate board, investor, ops, and compliance report packs from one governed reporting workflow | `src/Meridian.Storage/Export/AnalysisExportService.cs`, `src/Meridian.Storage/Export/AnalysisExportService.Formats.Xlsx.cs`, `src/Meridian.Storage/Export/ExportProfile.cs`, `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs`, new `src/Meridian.Application/Services/ReportGenerationService.cs`, new `src/Meridian.Contracts/Workstation/ReportPackDtos.cs` |

**Exit criteria:** Meridian exposes a Governance workflow that feels like a fund-operations cockpit rather than a collection of separate pages.

### Phase F5: Compliance and Policy Overlay

**Goal:** Use Security Master, portfolio, ledger, and risk abstractions to support fund-style mandate monitoring.

| Epic | User outcome | Suggested anchors |
|---|---|---|
| F5-1 Classification-aware mandate rules | Users can monitor issuer, sector, geography, leverage, and liquidity constraints | `src/Meridian.Risk/IRiskRule.cs`, `src/Meridian.Risk/CompositeRiskValidator.cs`, `src/Meridian.FSharp/Risk/`, `src/Meridian.FSharp/Domain/SecurityClassification.fs` |
| F5-2 Governance exception queue | Compliance and ops can review breaches, near-breaches, and approved overrides | `src/Meridian.Contracts/Workstation/`, `src/Meridian.Wpf/ViewModels/NotificationCenterViewModel.cs`, `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs`, new `src/Meridian.Application/Services/GovernanceExceptionService.cs` |
| F5-3 Promotion-aware governance gates | Promotion from Backtest to Paper to Live checks governance and accounting readiness | `src/Meridian.Strategies/Promotions/BacktestToLivePromoter.cs`, `src/Meridian.FSharp/Promotion/PromotionPolicy.fs`, `src/Meridian.Strategies/Services/StrategyLifecycleManager.cs` |

**Exit criteria:** Governance and promotion workflows include explicit policy checks informed by Security Master and fund-accounting state.

### Sequencing guidance

Recommended order:

1. F1 Security Master as a Product Platform
2. F2 Multi-Ledger Governance Foundation
3. F3 Cash-Flow Modeling and Projection
4. F4 Fund Operations Workstation
5. F5 Compliance and Policy Overlay

### Delivery notes

- Treat F1 and F2 as enabling phases for almost everything else.
- Treat the reconciliation engine as part of the governance foundation, not a later reporting add-on.
- Keep new F# kernels small and pure; expose them through narrow C# service boundaries.
- Reuse existing workstation surfaces before inventing a separate “fund admin” shell.
- Prefer extending `PortfolioReadService`, `LedgerReadService`, and Security Master query services before creating parallel read paths.

---

## Cross-Cutting Dependency Map

The remaining roadmap items are no longer independent feature buckets. The critical-path dependencies are:

| Depends On | Unlocks | Why it matters |
|---|---|---|
| Phase 11 workspace navigation | Phase 12 browser/detail flows, Phase 13 cockpit UX | The workstation surfaces need stable operator entry points before deeper run/portfolio/ledger UX lands cleanly. |
| Phase 12 shared run / portfolio / ledger read models | Phase 13 promotion workflow, portfolio/ledger productization, web run browser work | Without shared read contracts, Meridian keeps duplicating engine-specific result handling. |
| Provider hardening and replay coverage | Phase 13 execution realism, L3 simulation validation, operator trust | Paper/live workflow credibility depends on accurate provider behavior and replayable evidence. |
| H2 multi-instance coordination | Optional horizontal scale-out, shared scheduler ownership, collector topology docs | Scale-out remains optional, but once introduced it becomes foundational infrastructure rather than a side feature. |
| Readability refactor slices (`Program`, composition root, WPF MVVM) | Faster delivery of workstation, QuantScript, and simulation surfaces | Several planned features are blocked less by missing ideas than by host/UI concentration that slows safe iteration. |
| QuantScript shared data/query plumbing | Research workspace depth, script-backed experiment workflows | QuantScript belongs inside the workstation model, not as an isolated page with parallel data semantics. |
| L3 simulation contracts + replay timeline | Phase 13 realism work, execution audit views, strategy validation workflows | The simulator is both a flagship feature and an execution-quality dependency for later trading surfaces. |

### Critical-path takeaway

If the goal is "full non-assembly implementation" rather than isolated wins, the next efficient order is:

1. close provider/runtime hardening and H2 design;
2. finish Phase 11/12 shared workstation structure;
3. deepen Phase 13 operator flows on top of those shared models;
4. then land QuantScript and L3 simulation on the stabilized product shell.

---

## Non-Assembly Release Gates

These gates define when Meridian can reasonably claim the non-assembly roadmap is complete.

### Gate A: Current Functionality Fully Hardened

- Provider/runtime guidance is current for Polygon, StockSharp, Interactive Brokers, and NYSE.
- Replay-backed tests exist for the remaining provider edge cases called out in `FEATURE_INVENTORY.md`.
- Operational tracing/docs/runbooks cover the now-shipped trace propagation path and any added scale-out topology.

### Gate B: Trading Workstation Baseline Complete

- The desktop shell behaves as four durable workspaces, not just renamed navigation buckets.
- Shared run browser/detail/portfolio/ledger flows cover backtest, paper, and at least the first live-facing history path.
- Portfolio and ledger surfaces are first-class navigation targets rather than side pages.

### Gate C: Unified Research-to-Trading Lifecycle Complete

- Backtest Studio unifies native and Lean backtests behind one operator-facing flow.
- Paper trading exposes cockpit-grade controls, risk state, fills, positions, and promotion checkpoints.
- Promotion from Backtest to Paper to Live is explicit, auditable, and safety-gated.

### Gate D: Planned Flagship Capabilities Landed

- QuantScript exists as a real project, test suite, and WPF feature with sample scripts and docs.
- L3 inference / execution simulation exists as a real contract/engine/CLI/doc surface with confidence labeling and exported artifacts.

### Gate E: Structural and Documentation Closure

- Readability and cleanup roadmap items are either completed or retired by explicit decision.
- Status documents agree on current state, remaining work, and what is optional versus mandatory.
- No non-assembly flagship capability remains blueprint-only unless explicitly deferred by a documented ADR or roadmap decision.

---

## Reference Documents

- `docs/status/FEATURE_INVENTORY.md` — **new** comprehensive feature inventory with per-area status.
- `docs/status/IMPROVEMENTS.md` — canonical improvement tracking and sprint recommendations.
- `docs/status/EVALUATIONS_AND_AUDITS.md` — consolidated architecture evaluations, code audits, and assessments.
- `docs/status/production-status.md` — production readiness assessment narrative.
- `docs/status/CHANGELOG.md` — change log by release snapshot.
- `docs/status/TODO.md` — TODO/NOTE extraction for follow-up.
- `docs/status/FULL_IMPLEMENTATION_TODO_2026_03_20.md` — consolidated non-assembly implementation backlog spanning roadmap + plan documents.
- `docs/evaluations/` — detailed evaluation source documents (summarized in EVALUATIONS_AND_AUDITS.md).
- `docs/audits/` — detailed audit source documents (summarized in EVALUATIONS_AND_AUDITS.md).
- `docs/architecture/deterministic-canonicalization.md` — cross-provider canonicalization design.
- `docs/plans/governance-fund-ops-blueprint.md` — implementation blueprint for Security Master, multi-ledger governance, cash-flow modeling, reconciliation, and reporting workflows.
- `docs/plans/assembly-performance-roadmap.md` — detailed Phase 16 viability assessments and per-item implementation checklists.

---

*Last Updated: 2026-03-21*


