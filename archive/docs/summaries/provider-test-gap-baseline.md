# Provider Test Gap Baseline (Wave A)

_Last updated: 2026-05-20 (UTC)_

## Scope and intent

This baseline inventories **current automated coverage** and **missing deterministic test slices** for Wave A providers:

- Polygon
- Alpaca
- InteractiveBrokers
- Robinhood

The focus is deterministic, CI-safe coverage for:

1. Happy path behavior
2. Throttling / rate-limit handling
3. Degraded responses and failure-mode posture
4. Replay/backfill correctness

## Severity rubric

- **S0 Critical**: Missing coverage on production-critical flow; regressions could silently corrupt state or readiness evidence.
- **S1 High**: Meaningful operational risk; failures likely surface during normal operations.
- **S2 Medium**: Quality/reliability risk; gaps reduce confidence but have mitigations.
- **S3 Low**: Nice-to-have hardening or additional characterization.

## Polygon (Wave A)

### Current tests (deterministic slices)

- Streaming adapter and parser behavior in `PolygonMarketDataClientTests`.
- Provider contract and historical response parsing in `PolygonProviderContractTests`.
- Recorded websocket replay fixture tests in `PolygonRecordedSessionReplayTests`.
- Corporate action fetcher startup/config path in `PolygonCorporateActionFetcherTests`.

### Missing deterministic slices

| Gap | Severity | Why it matters | Target sprint |
|---|---|---|---|
| Deterministic **throttling backoff progression** assertions for repeated 429 + recovery (including jitter bounds with seeded clock/random abstractions) | S0 | Prevents retry storms and unstable readiness under provider pressure | Sprint 1 |
| Historical/backfill **pagination + resume idempotency** matrix (duplicate boundary bars, cursor rewind, partial page retry) | S0 | Backfill replay correctness and duplicate suppression are core evidence gates | Sprint 1 |
| Degraded streaming path with mixed malformed + valid frames and deterministic drop/continue expectations | S1 | Ensures parser hardening and stable ingest under noisy feeds | Sprint 2 |
| Deterministic contract parity checks between replayed websocket payloads and persisted normalized events | S1 | Protects replay evidence equivalence and audit confidence | Sprint 2 |

### Commitment summary

- **Sprint 1 commit**: close both S0 gaps.
- **Sprint 2 commit**: close remaining S1 gaps.

## Alpaca (Wave A)

### Current tests (deterministic slices)

- Message parsing and duplicate-delivery handling in `AlpacaMessageParsingTests`.
- End-to-end quote-pipeline golden subset in `AlpacaQuotePipelineGoldenTests`.
- Corporate action HTTP handling in `AlpacaCorporateActionProviderTests`.

### Missing deterministic slices

| Gap | Severity | Why it matters | Target sprint |
|---|---|---|---|
| Explicit **rate-limit throttle contract** tests (429 handling, retry-after precedence, bounded retry budget) | S0 | Prevents uncontrolled retries and protects provider trust posture | Sprint 1 |
| Historical/backfill deterministic **time-slice stitching** and resume semantics across market-session boundaries | S1 | Reduces risk of gaps/overlaps in replay and downstream accounting | Sprint 1 |
| Degraded-response matrix (auth expiry, partial payload, schema drift) with deterministic fallback assertions | S1 | Maintains resilient behavior under realistic broker/API churn | Sprint 2 |
| Replay equivalence tests between golden JSONL slices and normalized projection outputs | S2 | Improves confidence in long-lived replay artifacts | Sprint 2 |

### Commitment summary

- **Sprint 1 commit**: ship S0 + first S1 backfill coverage.
- **Sprint 2 commit**: complete degraded/replay S1-S2 hardening.

## InteractiveBrokers (Wave A)

### Current tests (deterministic slices)

- Brokerage gateway behavior and guardrails in `IBBrokerageGatewayTests`.
- API version gate validation in `IBApiVersionValidatorTests`.
- Simulation and provider-id coverage in `IBSimulationClientTests`.
- Market-data client contract shape in `IBMarketDataClientContractTests`.

### Missing deterministic slices

| Gap | Severity | Why it matters | Target sprint |
|---|---|---|---|
| Deterministic reconnect/re-subscribe sequencing after transport interruption (including duplicate callback suppression) | S0 | IB transport churn is frequent; state drift risk is high | Sprint 1 |
| Deterministic throttling envelope for order/account polling and market-data pacing-limit responses | S1 | Prevents pacing violations and unstable polling behavior | Sprint 1 |
| Replay/backfill parity tests for IB-originated execution/order lifecycle events against audit projections | S1 | Needed for paper-session replay trust and readiness gates | Sprint 2 |
| Degraded-response handling for partial account snapshots and stale position deltas | S2 | Improves operator confidence during broker incident windows | Sprint 2 |

### Commitment summary

- **Sprint 1 commit**: S0 reconnect slice + S1 pacing envelope.
- **Sprint 2 commit**: replay parity and degraded-account hardening.

## Robinhood (Wave A)

### Current tests (deterministic slices)

- Brokerage gateway behavior in `RobinhoodBrokerageGatewayTests`.
- HttpClient registration coverage for symbol-search wiring in `HttpClientConfigurationTests`.

### Missing deterministic slices

| Gap | Severity | Why it matters | Target sprint |
|---|---|---|---|
| Deterministic token-expiry and re-auth fallback behavior under 401/403 sequences | S0 | Critical to avoid broken brokerage reads during normal auth churn | Sprint 1 |
| Deterministic throttling and cooldown handling for quote/options endpoints | S1 | Prevents temporary bans and user-visible instability | Sprint 1 |
| Replay/backfill characterization for quote and option-chain snapshots used in paper workflows | S1 | Needed to validate cockpit replay assumptions and derived readiness | Sprint 2 |
| Degraded payload schema variation tests (missing Greeks/legs/quote fields) | S2 | Reduces fragility for unofficial API response variability | Sprint 2 |

### Commitment summary

- **Sprint 1 commit**: close auth S0 and throttle S1.
- **Sprint 2 commit**: replay + degraded schema hardening.

## Cross-provider deterministic harness work (shared prerequisite)

| Shared work item | Severity | Target sprint |
|---|---|---|
| Seeded time/random + virtual scheduler test harness for retry/backoff assertions | S0 | Sprint 1 |
| Unified fixture runner for replay/backfill parity assertions across providers | S1 | Sprint 1 |
| Shared degraded-response fixture catalog (HTTP + websocket/event callbacks) | S1 | Sprint 2 |

## Wave A gap ranking (portfolio view)

1. **S0 (must close first)**
   - Polygon throttling/backfill idempotency
   - Alpaca rate-limit contract
   - IB reconnect/re-subscribe determinism
   - Robinhood auth-expiry fallback
2. **S1 (close immediately after S0)**
   - Provider-specific degraded-response and replay/backfill parity slices
3. **S2+ (hardening)**
   - Additional schema-variance and golden-projection equivalence characterization

## Delivery commitment snapshot

- **Sprint 1**
  - Close all Wave A S0 gaps.
  - Land initial S1 replay/backfill deterministic harness foundations.
- **Sprint 2**
  - Close remaining S1 gaps.
  - Complete S2 hardening slices where practical.

