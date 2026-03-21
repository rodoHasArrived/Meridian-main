# Full Implementation TODO (Non-Assembly Scope)

**Date:** 2026-03-20  
**Purpose:** Consolidated implementation checklist for finishing (a) the remaining gaps in currently shipped functionality and (b) all remaining planned work **except** the assembly-level performance roadmap.

---

## 1. Scope and framing

This document merges the outstanding work scattered across:

- `docs/status/ROADMAP.md`
- `docs/status/FEATURE_INVENTORY.md`
- `docs/status/IMPROVEMENTS.md`
- `docs/plans/trading-workstation-migration-blueprint.md`
- `docs/plans/quant-script-environment-blueprint.md`
- `docs/plans/l3-inference-implementation-plan.md`
- `docs/plans/codebase-audit-cleanup-roadmap.md`
- `docs/plans/readability-refactor-roadmap.md`

**Explicitly excluded:** Phase 16 / assembly-level optimization work and related SIMD / low-level performance tasks.

---

## 2. Current-state findings that affect the plan

### 2.1 Core platform gaps are now narrow but real

The status docs agree that the main remaining product/platform gaps are:

1. **C3** — provider lifecycle consolidation is still incomplete.
2. **G2** — end-to-end OpenTelemetry trace propagation is still incomplete.
3. **I3** — configuration JSON Schema generation is still incomplete.
4. **H2** — multi-instance coordination is still open.
5. **Phase 11–15** — the trading workstation migration is still planned rather than finished.
6. **QuantScript** and **L3 inference / execution simulation** remain blueprint-stage work rather than implemented product capabilities.

### 2.2 Some documentation is slightly ahead/behind code and needs normalization

Two important nuances surfaced during inspection:

- The canonicalization canary is **not zero-state** anymore: `pr-checks.yml` already runs the golden fixture test slice on CI. The remaining work is to upgrade that job into a richer drift-canary that reports newly unmapped values in a directly actionable way instead of only failing generically.
- The workstation migration is **partially represented in navigation language already**: the WPF command palette groups items under `Research`, `Trading`, `Data Ops`, and `Governance`, but the persisted workspace model still uses the older `Monitoring / Backfill / Storage / Analysis / Custom` category set, so the migration is only partial and not yet a true shared product model.

### 2.3 Full implementation now requires three different classes of work

To reach the user's requested end-state, the repo needs all three of these classes completed:

1. **Finish partial implementation debt** in already-shipped capabilities.
2. **Turn documented workflow plans into real product features**.
3. **Finish structural/refactor backlogs** that are explicitly called out as planned work in repository planning docs.

---

## 3. Master TODO list

## Track A — Finish the partially implemented platform items

### A1. Complete C3: provider lifecycle consolidation

- Refactor the NYSE streaming path onto the shared WebSocket lifecycle pattern instead of keeping bespoke connection logic.
- Decide the final StockSharp strategy explicitly:
  - either adapt it to the same shared provider lifecycle abstraction where feasible, or
  - formally document it as a connector-runtime exception and extract a different shared capability model for connector-based transports.
- Remove the remaining duplicated reconnect / heartbeat / subscription-recovery logic once the shared abstraction is final.
- Add/update provider tests that prove parity for reconnect, resubscribe, shutdown, and backpressure behavior.
- Update roadmap / feature-inventory wording so C3 has one authoritative definition.

### A2. Complete G2: end-to-end trace propagation

- Carry `Activity` / trace context from provider receive loops through queueing, pipeline consumption, and sink persistence.
- Add correlation identifiers consistently to structured logs.
- Ensure storage writes and replay/backfill flows preserve or explicitly bridge trace context.
- Add integration tests proving trace continuity across provider -> pipeline -> storage.
- Document collector / Jaeger / Zipkin / OTLP setup for trace visualization.

### A3. Complete I3: config JSON Schema generation

- Generate `config/appsettings.schema.json` from `AppConfig` or equivalent configuration models.
- Add `$schema` linkage in the sample config.
- Make schema generation part of a repeatable build/tooling flow.
- Validate the generated schema in CI.
- Document how IDE validation/autocomplete works for contributors/operators.

### A4. Finish J8 beyond the current basic CI test slice

- Keep the existing golden fixture test job in CI.
- Extend it into a true drift-canary that surfaces newly unmapped condition codes / venue identifiers.
- Add a fixture refresh/update workflow so the canary is maintainable instead of brittle.
- Prefer PR-visible reporting (summary or comment) that explains exactly what drift was detected.
- Update docs so J8 is described as **partial-but-CI-backed**, not merely absent.

### A5. Complete H2: multi-instance coordination

- Design symbol-assignment coordination for two or more collector instances.
- Add distributed locking or leader-election for shared subscription ownership.
- Prevent duplicate subscriptions and conflicting backfill/schedule execution.
- Document a supported multi-node topology and failure/recovery behavior.
- Add tests or simulations for duplicate-prevention and failover of ownership.

---

## Track B — Finish current-functionality hardening and provider completeness

### B1. Provider hardening still needed for “fully implemented” current functionality

- **Polygon**
  - validate WebSocket parsing against recorded/realistic production sessions;
  - add replay-based integration coverage for trades, quotes, aggregates, and status messages;
  - decide whether synthetic/stub behavior remains in production or moves behind a clearly separate stub/test path.
- **NYSE**
  - complete shared lifecycle adoption work (Track A1);
  - add stronger streaming tests and reconnection validation.
- **StockSharp**
  - document connector types, prerequisites, and validated configuration examples;
  - improve the unsupported-path story when connector type or platform prerequisites are missing.
- **Interactive Brokers**
  - add scripted build/bootstrap instructions for the `IBAPI` path;
  - add a smoke-test CI/build path for the compile-constant variant.

### B2. Expand under-tested provider coverage

Add or strengthen tests for providers specifically called out as under-covered in the cleanup roadmap:

- TwelveData
- Nasdaq Data Link
- Alpha Vantage
- Finnhub
- Stooq
- OpenFIGI

Each provider should at minimum have tests for constructor/config validation, parsing, rate-limit handling, and error/lifecycle behavior.

### B3. Backtesting module completeness

- Expand tests around `BacktestEngine`.
- Strengthen `BarMidpointFillModel` and `OrderBookFillModel` coverage.
- Add ordering/invariant coverage for `MultiSymbolMergeEnumerator`.
- Add persistence/readback coverage for `StrategyRunStore`.

### B4. Observability completeness

- Add correlation IDs to logs everywhere the observability docs say they should exist.
- Align metrics, logs, and traces so operational workflows can correlate them reliably.
- Review dashboards/alerts/runbooks for the new tracing and multi-instance features.

---

## Track C — Finish the Trading Workstation migration (ROADMAP Phases 11–13)

### C1. Phase 11: workflow-centric navigation and IA

- Replace page-list-first navigation with durable top-level workspaces:
  - `Research`
  - `Trading`
  - `Data Operations`
  - `Governance`
- Keep all major capabilities reachable from both primary navigation and the command palette.
- Eliminate orphan pages/deep-link-only capabilities.
- Add workspace-level headers, quick actions, and cross-links between related workflows.
- Rewrite status/dashboard docs so they describe workflow completion instead of raw page counts.

### C2. Phase 12: shared run / portfolio / ledger model

- Introduce shared `StrategyRun` contracts and identifiers spanning backtest, paper, and live modes.
- Add shared portfolio read models for cash, exposure, positions, realized/unrealized P&L, commissions, financing, and equity history.
- Add ledger read services for journals, account summaries, trial balance, symbol subledgers, and reconciliation views.
- Build a run browser and run detail flow that can compare runs across engines/modes.
- Normalize result schemas so native backtest, Lean backtest, and paper-trading history feel like the same product family.

### C3. Phase 13: unify research/backtest/paper-trading lifecycle

- Build a unified Backtest Studio with engine selection and run comparison.
- Promote the current live/paper capabilities into a real trading cockpit with orders, fills, positions, exposure, and risk panels.
- Replace scaffold/simple paper fills with feed-aware simulated pricing where possible, while keeping assumptions explicit.
- Add promotion workflow states and safety checks for Backtest -> Paper -> Live progression.
- Ensure portfolio and ledger drill-ins are available directly from research and trading surfaces.

---

## Track D — Finish the Trading Workstation blueprint phases that go deeper than the status roadmap

### D1. Navigation/workspace implementation details

- Create real workspace shells and landing surfaces, not just naming conventions in menus.
- Add cross-workspace workflow links (for example: from backtest results to portfolio view, from paper trading to ledger audit).
- Make workspace state/session persistence aware of the new workflow model instead of the old category model.

### D2. Portfolio and ledger as first-class UX

- Build portfolio overview, positions, exposure, financing, journal, trial balance, and audit-trail views.
- Add “why did equity change?” and reconciliation flows backed by ledger read models.
- Make these destinations primary navigation items rather than buried utility pages.

### D3. Paper-trading cockpit hardening

- Add operational controls: pause, stop, cancel all, flatten, acknowledge risk alerts.
- Surface order lifecycle, exposure, and strategy state in real time.
- Make paper/live badging and safety separation unmistakable.

### D4. Promotion workflow and live-readiness guardrails

- Add explicit approval/check/preflight stages.
- Add environment badges, irreversible-action confirmations, and safety rails.
- Keep live routing opt-in and visibly distinct from research/paper modes.

---

## Track E — Implement the QuantScript blueprint end-to-end

### E1. Foundation

- Create the `Meridian.QuantScript` project and corresponding test project.
- Add required package/dependency entries.
- Add solution wiring and configuration wiring.
- Introduce `QuantScriptOptions` and config defaults.

### E2. Core QuantScript library/API

- Implement price/return series types, statistical engine, technical-indicator extensions, plotting request queue, data context, backtest proxy, and portfolio helpers.
- Resolve the open design decisions called out in the blueprint (especially efficient frontier scope, parameter extraction, and plotting injection model).

### E3. Compilation/runtime pipeline

- Implement compiler + runner abstractions.
- Compile/cache scripts, discover parameters, execute with cancellation/timeout, and capture outputs.
- Wire plotting and console output through a controlled execution context.

### E4. WPF integration

- Add `QuantScriptPage`, `QuantScriptViewModel`, models, navigation registration, and sidebar integration.
- Render console, charts, metrics, trades, and parameter editing in the desktop app.

### E5. Tests, sample content, and docs

- Implement the full planned QuantScript test suite.
- Add sample scripts and contributor/operator documentation.
- Close ADR/documentation/compliance items noted in the blueprint.

---

## Track F — Implement the L3 inference / queue-aware execution simulation plan end-to-end

### F1. Foundations and contracts

- Add contracts/configs/JSON-serializable types for simulation requests, results, policies, and calibration.
- Add test fixtures and synthetic-data generators for queue-truth scenarios.
- Extend replay/storage plumbing where the plan requires stable offsets and merged iterators.

### F2. Reconstruction engine

- Build deterministic L2 timeline reconstruction from stored market-depth + trade data.
- Implement event ordering, book-state timeline, and trade-alignment layers.
- Add deterministic tests/golden cases for replay correctness.

### F3. Inference model and simulator

- Implement the baseline probabilistic queue-ahead model.
- Implement queue-aware execution simulation for market/limit style behavior, partial fills, latency/staleness, and result labeling.
- Produce all planned result artifacts (`fill-tape`, `order-lifecycle`, `summary`, diagnostics).

### F4. Calibration, confidence, and degradation behavior

- Add calibration command/workflow.
- Add confidence scoring, heuristic fallback rules, and clear warnings for low-confidence periods.
- Ensure outputs are always labeled as inferred and degrade gracefully rather than overstating realism.

### F5. CLI/API/UI integration

- Add CLI commands/flags described in the plan.
- Add optional HTTP endpoints if the project still wants them.
- Add the WPF simulation explorer / operator-facing integration described in the plan.
- Preserve compatibility with all existing CLI modes.

### F6. Testing, documentation, and runbooks

- Implement unit/property/golden/performance tests called out in the plan.
- Write the required user and operations documentation.
- Make Phase-1 definition-of-done acceptance criteria executable and observable.

---

## Track G — Complete the codebase audit & cleanup roadmap

### G1. Finish the remaining feature-completion items from that roadmap

- C3 / G2 / J8 closure (covered in Track A).
- WPF placeholder-page audit and consistent placeholder labeling.

### G2. Code cleanup backlog

- Seal the explicitly listed classes that are meant to be non-extensible.
- Resolve unused event declarations and replace suppression-only patterns with either real wiring or removal.
- Keep the provider template as docs/examples only, not production compile input.
- Isolate Polygon stub mode if the team decides production synthetic fallback should not ship.
- Consolidate conditional-compilation fallback helpers for IB/StockSharp.

### G3. Repository organization backlog

- Review and consolidate remaining CI workflows.
- Prune/archive superseded documentation.
- Run a freshness audit so completed plans move out of active-plan folders once done.

### G4. Post-cleanup verification

- Update `CLAUDE.md`, AI instructions, changelog, structure docs, and known-error docs to reflect the post-cleanup repo state.

---

## Track H — Complete the readability refactor roadmap

### H1. Startup/orchestration readability

- Shrink `Program.cs` into an explicit startup/orchestration flow.
- Introduce startup models, startup phases, a startup orchestrator, and focused mode runners.
- Preserve exact runtime behavior while making each mode independently testable.

### H2. Composition-root modularization

- Split `ServiceCompositionRoot` into feature registration modules.
- Keep one top-level composition entry point.
- Introduce host/composition profiles instead of boolean-sprawl registration rules.

### H3. WPF MVVM and typed-contract migration

- Continue the pilot-to-rollout pattern for moving WPF pages away from code-behind business logic.
- Create typed contracts + shared UI services + WPF view models for pages still doing direct transport/JSON work.
- Reduce pages to lifecycle and binding glue.

### H4. Workflow-model extraction for setup/config flows

- Turn the configuration/setup wizard into an explicit step/workflow engine.
- Separate provider metadata from imperative wizard logic.
- Keep the flow reusable across CLI/web/WPF frontends.

### H5. Declarative rules and provider capability extraction

- Convert more validation/scoring/canonicalization logic into clearer transform-oriented modules where justified.
- Rebuild giant provider adapters around reusable capabilities, starting with the documented pilot targets.
- Add tests/architecture checks that enforce the new module boundaries.

---

## Track I — Documentation and status-source convergence

### I1. Make the status docs agree with the codebase again

- Normalize J8 wording to reflect that CI already runs the golden fixture suite, while richer drift reporting still remains.
- Normalize C3 wording so NYSE vs. StockSharp scope is consistent everywhere.
- Normalize workstation migration docs so the current partial state is explicit and consistent.

### I2. Keep plan lifecycle clean

- Move completed plans to archived status once implemented.
- Keep roadmap, feature inventory, improvements, and production-status documents synchronized in the same PR as status changes.

### I3. Add a true “full implementation” exit checklist

Before declaring the repo fully implemented (excluding assembly work), require all of the following:

- no partial items left in status docs unless explicitly deferred by decision;
- no blueprint-only flagship features left unstarted (Trading Workstation, QuantScript, L3 inference);
- current functionality has provider docs/tests and operator docs for every supported mode;
- status/planning docs reflect reality rather than aspiration;
- CI covers the newly added schema, canary, workflow, and simulation surfaces.

---

## 4. Recommended implementation order

## Wave 1 — close the partial items first

1. C3
2. G2
3. I3
4. J8 drift-canary upgrade
5. provider hardening/doc/test gaps that block calling current functionality “fully implemented”

## Wave 2 — finish the workflow-centric desktop/product baseline

1. Phase 11 navigation/workspace completion
2. Phase 12 shared run/portfolio/ledger contracts
3. placeholder/orphan-page cleanup
4. WPF MVVM/readability slices that are prerequisites for the workstation UX

## Wave 3 — finish coherent operator workflows

1. Phase 13 backtest/paper-trading unification
2. deeper trading workstation blueprint items (portfolio + ledger surfaces, promotion workflow, cockpit controls)

## Wave 4 — build the major planned new capabilities

1. QuantScript
2. L3 inference / queue-aware execution simulation

## Wave 5 — close structural/documentation backlogs

1. cleanup roadmap remainder
2. readability roadmap remainder
3. doc freshness / archival / final status convergence

---

## 5. Practical definition of “done” for the requested end-state

The repository can reasonably be described as having **fully implemented current functionality and completed all remaining planned items other than assembly work** only when:

- the status docs no longer list partial/open non-assembly items;
- the workstation migration is implemented in the product, not only in docs/navigation labels;
- shared run/portfolio/ledger concepts exist in code, APIs, and UX;
- QuantScript exists as a real project, test suite, and WPF feature;
- L3 execution simulation exists as a real contract/engine/CLI/doc surface;
- provider completeness/documentation/testing gaps are closed;
- the cleanup/refactor plans are either completed or explicitly retired by a documented decision.

Until then, the repo should be described as **feature-rich and largely complete, but not fully implemented in the absolute sense requested here**.
