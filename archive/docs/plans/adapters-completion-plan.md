# Meridian Infrastructure Adapters Completion Plan

_Last updated: 2026-05-20_

## Scope

This plan covers completion work for `src/Meridian.Infrastructure/Adapters/` and its provider families:

- Core adapter infrastructure (provider contracts, backfill, rate limiting, registry/factory)
- Market-data adapters (Alpaca, Finnhub, Polygon, Robinhood, IB, NYSE, Synthetic, etc.)
- Historical/reference/symbol-search adapters (Edgar, OpenFigi, Yahoo, Tiingo, Stooq, Nasdaq Data Link, etc.)
- Failover orchestration components

## Completion Goals

1. Every adapter folder should expose a clear production-readiness contract: feature surface, dependency requirements, quality gates, and degradation behavior.
2. Provider implementations should be aligned on shared contracts (`IMarketDataClient`, `IHistoricalDataProvider`, `ISymbolSearchProvider`, `ICorporateActionProvider`) and consistent resilience patterns.
3. Adapter coverage should include deterministic tests for happy-path, throttling, degraded providers, and replay/backfill scenarios.
4. Operator-facing readiness evidence should be generated for Wave 1/Wave 2 workflows where provider posture affects trading readiness and inbox routing.

---

## Phase 0 — Inventory and Baseline (1 sprint)

### Deliverables

- Build a provider capability matrix for every adapter subfolder:
  - streaming
  - historical
  - symbol search
  - options chain
  - corporate actions
  - brokerage sync / execution
- Tag each adapter state: **complete**, **partial**, **experimental**, or **template-only**.
- Map adapter ownership and dependency risks (external API limits, credential setup, non-deterministic network dependencies).

### Work Items

- Enumerate all files and classify by capability.
- Record missing integration points (e.g., registration, DI wiring, factory support, options binding).
- Record test gaps by adapter family.

### Exit Criteria

- A single source-of-truth matrix exists under `docs/status/`.
- Every adapter has an explicit state label and next action.

---

## Phase 1 — Contract and Infrastructure Hardening (1–2 sprints)

### Deliverables

- Normalize Core adapter behavior:
  - consistent cancellation/timeout semantics
  - consistent retry and rate-limit behavior
  - shared response parsing/error mapping patterns
- Validate all adapter registrations in provider factory/registry paths.
- Harden failover paths for streaming providers and provider health signaling.

### Work Items

- Review and unify reusable primitives in `Adapters/Core/`.
- Close mismatch between adapter runtime behavior and provider metadata/capability flags.
- Add focused tests around:
  - rate limiting
  - backfill queue orchestration
  - failover registry selection
  - provider data quality validator behavior

### Exit Criteria

- Core primitives are the primary path for provider behaviors (minimal bespoke logic per adapter).
- All adapters can be resolved/created from configured provider identifiers.

---

## Phase 2 — Provider Family Completion Waves (2–4 sprints)

### Wave A (Critical Path)

- **Polygon, Alpaca, InteractiveBrokers, Robinhood**
- Ensure production-grade parity for the capabilities already surfaced in code.

Focus:

- historical + streaming coherence
- symbol/corporate-action/options coverage completeness
- brokerage gateway and sync reliability where applicable

### Wave B (Support Providers)

- **Finnhub, NYSE, Edgar, OpenFigi, YahooFinance, Tradier, TwelveData, AlphaVantage, Tiingo, Stooq, Fred, NasdaqDataLink**

Focus:

- deterministic data-shape compatibility
- backfill + search stability
- calibration of provider degradation and quality signals

### Wave C (Synthetic + Templates)

- **Synthetic, Templates**

Focus:

- synthetic provider as deterministic test harness
- template folders aligned with current provider-builder standards

### Exit Criteria

- Each provider family has:
  - passing targeted unit/integration slice
  - documented credential and environment assumptions
  - clear readiness status for operator usage

---

## Phase 3 — Evidence and Operational Readiness (1 sprint)

### Deliverables

- Add/update provider validation runs and summaries for affected adapters.
- Ensure readiness endpoints and operator inbox dependencies have trustworthy provider-derived signals.
- Capture regression evidence pack for Wave 2 paper-trading cockpit dependencies.

### Work Items

- Run and archive provider validation scripts.
- Reconcile readiness-related DTO/projection impacts from adapter changes.
- Produce concise operator sign-off packet for changed providers.

### Exit Criteria

- Provider changes are reflected in status dashboards and readiness artifacts.
- No adapter change ships without corresponding evidence updates.

---

## Prioritized Backlog Structure

For each adapter subfolder, use this checklist:

1. **Contract completeness** (interfaces, options, DI registration)
2. **Behavior completeness** (streaming/historical/search/etc. implementation)
3. **Reliability** (timeouts, retries, throttling, failover, error mapping)
4. **Data quality** (validation, gap detection/repair, schema consistency)
5. **Tests** (unit, integration slice, deterministic synthetic path)
6. **Docs & operations** (setup, help commands, validation evidence)

---

## Recommended Execution Order

1. Core + Failover hardening
2. Critical provider wave (Polygon/Alpaca/IB/Robinhood)
3. Support provider wave
4. Synthetic/template modernization
5. Evidence and status/doc convergence

This sequence reduces rework by stabilizing shared infrastructure before broad adapter-specific completion.

---

## Risks and Mitigations

- **External API volatility**: use adapter-level contract tests and synthetic fixtures to reduce live API dependency in CI.
- **Credential drift across providers**: standardize options validation and fail-fast startup diagnostics.
- **Inconsistent provider capability claims**: enforce matrix-driven audits between registration metadata and implementation.
- **Regression spillover into readiness surfaces**: run focused readiness/inbox endpoint tests whenever adapter DTO dependencies change.

---

## Definition of Done (Adapters Folder)

Completion for `src/Meridian.Infrastructure/Adapters/` means:

- Every active provider folder is classified, tested, and documented.
- Shared adapter infrastructure is the default mechanism for resilience and rate-control behavior.
- Provider readiness can be evidenced through repeatable validation scripts and status artifacts.
- Remaining non-critical providers are explicitly marked optional/deferred with rationale.
