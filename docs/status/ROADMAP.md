# Meridian - Project Roadmap

**Version:** 1.7.0
**Last Updated:** 2026-03-20
**Status:** Development / Pilot Ready (hardening, scale-up, and trading workstation migration planning in progress)
**Repository Snapshot:** `src/` files: **832** | `tests/` files: **273** | HTTP route constants: **309** | Remaining stub routes: **0** | Test methods: **~4,093**

This roadmap is refreshed to match the current repository state and focuses on the remaining work required to move from "production-ready" to a more fully hardened v2.0 release posture.

For a complete per-feature status breakdown see [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md). For the workflow-centric UX and shared run-model migration plan, see [`../plans/trading-workstation-migration-blueprint.md`](../plans/trading-workstation-migration-blueprint.md). For the consolidated non-assembly backlog across roadmap + plan documents, see [`FULL_IMPLEMENTATION_TODO_2026_03_20.md`](FULL_IMPLEMENTATION_TODO_2026_03_20.md).

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
- **Polygon `WebSocketProviderBase` adoption** — `PolygonMarketDataClient` now extends `WebSocketProviderBase`, completing C3 for the Polygon provider. NYSE and StockSharp remain.

### What remains

Remaining work is tracked in `docs/status/IMPROVEMENTS.md` and the new [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md):

- **35 tracked improvement items total** (core themes A–G)
  - ✅ Completed: 33
  - 🔄 Partial: 2 (C3 — WebSocket lifecycle consolidation, G2 — OpenTelemetry trace context propagation)
  - 📝 Open: 0
- **8 new theme items** (themes H–I)
  - ✅ Completed: 6 (H1, H3, H4, I1, I2, I4)
  - 🔄 Partial: 1 (I3 — Configuration Schema Validation)
  - 📝 Open: 1 (H2 — Multi-Instance Coordination)
- **8 canonicalization items** (theme J)
  - ✅ Completed: 7 (J1–J7 — design, MarketEvent fields, canonicalizer, condition codes, venue normalization, provider wiring, metrics)
  - 🔄 Partial: 1 (J8 — curated fixtures + golden tests + baseline CI job are present; richer drift reporting / fixture refresh workflow still pending)
- Architecture debt largely resolved; C1/C2 unified provider registry and DI composition path are complete. C3 is now explicitly partial: Polygon uses `WebSocketProviderBase`, NYSE remains pending, and StockSharp is tracked under the separate provider-capability/readability workstream rather than as a straight WebSocket-base migration.
- **Trading workstation migration**: Core functionality exists, but the UX remains page-centric. The command palette already uses `Research`, `Trading`, `Data Ops`, and `Governance` labels, while the persisted workspace model still reflects legacy categories; the next major delivery wave needs to complete that migration in product structure, not only terminology.
- **Provider completeness**: Polygon and StockSharp functional with credentials; IB and NYSE require external setup steps.

---

## Phase Status (Updated)

| Phase | Status | Notes |
|---|---|---|
| Phase 0: Critical Fixes | ✅ Completed | Historical blockers closed. |
| Phase 1: Core Stability & Testing Foundation | ✅ Completed (baseline) | Foundation shipped; deeper coverage remains in active backlog (Theme B). |
| Phase 2: Architecture & Structural Improvements | ✅ Completed (baseline) | Follow-on architectural debt tracked in Theme C open items. |
| Phase 3: API Completeness & Documentation | ✅ Completed | Route implementation gap closed; continuing API polish and schema depth in D4/D7. |
| Phase 4: Desktop App Maturity | ✅ Completed | WPF workflow parity achieved; UWP now legacy/deprecated. |
| Phase 5: Operational Readiness | ✅ Completed | Monitoring/auth/deployment foundations in place. |
| Phase 6: Duplicate & Unused Code Cleanup | ✅ Completed | Cleanup phase closed; residual cleanup now folded into normal maintenance. |
| Phase 7: Extended Capabilities | ⏸️ Optional / rolling | Scheduled as capacity permits. |
| Phase 8: Repository Organization & Optimization | 🔄 In progress (rolling) | Continued doc and code organization improvements. |
| Phase 9: Final Production Release | 🔄 Active target | 94.3% of core improvements complete; C3 lifecycle consolidation partial (Polygon done; NYSE pending, StockSharp re-scoped), G2 trace propagation pending. |
| Phase 10: Scalability & Multi-Instance | 📝 Planned | New phase for horizontal scaling and multi-instance coordination. |
| Phase 11: Trading Workstation Structure | 🔄 Planned / partially represented | Navigation language exists in the command palette, but true workspace-first UX remains to be implemented. |
| Phase 12: Shared Run / Portfolio / Ledger Model | 📝 Planned | Standardize run browser, portfolio summaries, and ledger-first read models. |
| Phase 13: Backtest + Paper Trading Unification | 📝 Planned | Unify native + Lean backtesting and harden paper-trading operator workflows. |
| Phase 14: Configuration Schema & Drift Canary | 🔄 Partially started | I3 and J8 have baseline infrastructure, but schema generation and richer drift reporting remain. |
| Phase 15: Scalability (Optional) | 📝 Planned | Multi-instance coordination remains optional for single-node deployments. |
| Phase 16: Assembly-Level Performance | 📝 Planned | Byte-level SIMD, algorithmic, and allocation improvements from `docs/evaluations/assembly-performance-opportunities.md`. |

---

## Priority Roadmap (Current Delivery Waves)

This section replaces the old sprint-by-sprint narrative with the current backlog ordering implied by the repository state.

### Wave 1 — Close the remaining partial items

- **C3 remainder**: complete NYSE lifecycle consolidation and formally document the StockSharp connector-oriented path.
- **G2 remainder**: propagate trace context provider -> pipeline -> storage and add correlation IDs to logs.
- **I3 remainder**: generate `config/appsettings.schema.json`, link it from sample config, and validate it in CI.
- **J8 remainder**: keep the existing golden-fixture CI slice, then extend it with richer unmapped-code / venue drift reporting and a fixture-refresh workflow.

### Wave 2 — Finish provider completeness / current-functionality hardening

- **Polygon**: validate WebSocket parsing against recorded production-style sessions.
- **StockSharp**: document connector types and ship validated configuration examples.
- **IB**: add scripted `IBAPI` setup/build instructions and a smoke-test path.
- **Testing**: raise coverage for under-tested providers and backtesting modules.

### Wave 3 — Deliver the workflow-centric workstation baseline

- **Phase 11**: convert navigation from page-first to true `Research` / `Trading` / `Data Operations` / `Governance` workspaces.
- **Phase 12**: introduce shared `StrategyRun`, portfolio, and ledger read models plus a comparison-friendly run browser.
- **UX debt cleanup**: eliminate orphan pages and placeholder-only surfaces as part of the workstation migration.

### Wave 4 — Unify research, backtest, and paper-trading workflows

- **Phase 13**: deliver Backtest Studio, a paper-trading cockpit, feed-aware execution realism, and promotion workflow guardrails.
- **Portfolio + ledger UX**: make journal/trial-balance/account-summary views first-class product surfaces.

### Wave 5 — Build the major planned capabilities that are still blueprint-only

- **QuantScript**: create the project, WPF surface, compiler/runtime pipeline, tests, and example scripts.
- **L3 inference / execution simulation**: implement contracts, reconstruction, inference, simulator, CLI/UI integration, and documentation.

### Wave 6 — Optional scale-out and structural closure

- **H2 / Phase 15**: add multi-instance coordination for shared symbol universes and scheduled work ownership.
- **Readability / cleanup roadmap**: finish the remaining structural refactors, CI consolidation, placeholder labeling, and documentation freshness work.

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
| I3 | Configuration Schema Validation at Startup | 🔄 Partial | `SchemaValidationService` validates stored data formats against schema versions at startup (`--validate-schemas`, `--strict-schemas`). Missing: JSON Schema generation from C# models for config file validation. |
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
| J8 | Golden Fixture Test Suite | 🔄 Partial | 8 curated fixture `.json` files added (Alpaca + Polygon: regular, extended-hours, odd-lot, cross-provider XNAS identity). `CanonicalizationGoldenFixtureTests` drives them via `[Theory][MemberData]` using production `condition-codes.json` and `venue-mapping.json`. A baseline CI job already runs the slice; remaining work is richer drift reporting plus fixture refresh/maintenance workflow. |

---

## 2026 Delivery Objectives

### Objective 1: Test Confidence ✅ Achieved

- ✅ Expanded integration and provider tests — 12 provider test files, 219 test files total, ~3,444 test methods.
- ✅ Risk-based coverage with negative-path and schema validation tests.
- ✅ Integration test harness with `FixtureMarketDataClient` and `InMemoryStorageSink`.

### Objective 2: Architectural Sustainability ✅ Substantially Achieved

- ✅ C1/C2 complete — unified `ProviderRegistry` and single DI composition path.
- ✅ Static singletons replaced with injectable `IEventMetrics`.
- ✅ Consolidated configuration validation pipeline.
- 🔄 C3 (provider lifecycle consolidation) remains open — Polygon is migrated, NYSE still needs the shared lifecycle path, and StockSharp now needs a clearly documented connector-runtime strategy rather than ambiguous roadmap wording.

### Objective 3: API Productization ✅ Achieved

- ✅ Quality metrics API fully exposed (`/api/quality/drops`, per-symbol drill-down).
- ✅ Typed OpenAPI annotations across all endpoint families (58+ endpoints).
- ✅ 283 route constants with 0 stubs remaining.

### Objective 4: Operational Hardening 🔄 Mostly Achieved

- ✅ Prometheus metrics, API auth/rate limiting, category-accurate exit codes.
- ✅ OpenTelemetry pipeline instrumentation with activity spans.
- 🔄 End-to-end trace context propagation pending (G2 remainder).

### Objective 5: Scalability 🔄 Partially Achieved

- ✅ Per-provider rate limit enforcement via `ProviderRateLimitTracker`.
- ✅ Provider degradation scoring via `ProviderDegradationScorer`.
- 📝 H2 multi-instance coordination pending (not needed for single-instance).

### Objective 6: Cross-Provider Data Canonicalization ✅ Substantially Achieved

- ✅ Design document complete with provider field audit and 3-phase rollout plan.
- ✅ J2–J7 fully implemented: canonical fields on `MarketEvent`, `EventCanonicalizer`, `ConditionCodeMapper`, `VenueMicMapper`, `CanonicalizingPublisher` decorator with DI wiring, `CanonicalizationMetrics` with API endpoints.
- 🔄 J8 partial: golden fixtures and a baseline CI test job exist; remaining work is richer unmapped-code / venue reporting and fixture refresh automation.
- Target: >= 99.5% canonical identity match rate across providers for US liquid equities.

---

## Success Metrics (Updated Baseline)

| Metric | Current Baseline | 2026 Target |
|---|---:|---:|
| Stub endpoints remaining | 0 | 0 |
| Core improvement items completed | 33 / 35 | 35 / 35 |
| Core improvement items still open | 1 / 35 (C3 — partial, Polygon done) | 0 / 35 |
| New theme items (H/I) completed | 6 / 8 | 7+ / 8 |
| Source files | 779 | — |
| Test files | 266 | 300+ |
| Test methods | ~4,135 | 4,500+ |
| Route constants | 309 | 309 |
| Architecture debt (Theme C completed) | 6 / 7 (C3 partial: Polygon ✅, NYSE/StockSharp pending) | 7 / 7 |
| Provider test coverage | All 5 streaming providers + failover + backfill | Comprehensive |
| OpenTelemetry instrumentation | Pipeline metrics + activity spans | Full trace propagation |
| OpenAPI typed annotations | All endpoint families | Complete with error response types |
| Canonicalization design | Complete | Implementation complete |
| Canonicalization implementation (J2–J8) | 6 / 7 | 7 / 7 |
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

**Goal:** Standardize Meridian around a shared strategy-run lifecycle and elevate portfolio + ledger state to first-class product objects.

| Item | Area | Work |
|------|------|------|
| P12-1 | **Run model** | Introduce shared `StrategyRun` contracts covering backtest, paper, and live modes with common identifiers, timestamps, parameters, metrics, and status |
| P12-2 | **Portfolio model** | Add shared read models for cash, exposure, positions, realized/unrealized P&L, commissions, financing, and equity history |
| P12-3 | **Ledger model** | Add journal, trial-balance, account-summary, and per-symbol ledger read services so accounting is directly visible in product surfaces |
| P12-4 | **Run browser** | Build a comparison-friendly run browser and detail flow reusable by native backtest, Lean backtest, and paper-trading history |

**Exit criteria:** Backtest, paper, and live-facing experiences share a recognizable run model, and users can inspect portfolio and ledger state from product UI surfaces.

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

**Goal:** Close the remaining I3 and J8 items.

| Item | Area | Work |
|------|------|------|
| P14-1 | **I3 Config JSON Schema** | Add a build step (or `dotnet run` tool) that generates `config/appsettings.schema.json` from `AppConfig` using `NJsonSchema` or `System.Text.Json.Schema`; add `$schema` pointer to `appsettings.sample.json`; enables IDE auto-complete and validation |
| P14-2 | **J8 Drift-canary CI** | Extend the existing golden-fixture CI slice so it reports newly unmapped condition codes or venue identifiers in a directly actionable PR-visible summary/comment and supports fixture refresh/maintenance |

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
- `docs/plans/assembly-performance-roadmap.md` — detailed Phase 16 viability assessments and per-item implementation checklists.

---

*Last Updated: 2026-03-20*
