# Provider Test Minimums

## Purpose

This document defines the minimum test contract for every Meridian market-data provider so provider test coverage is comparable across adapters and predictable in CI.

## Scope

The contract applies to provider adapters in `src/Meridian.Infrastructure/Providers` and related tests under `tests/Meridian.Tests`.

- Baseline required test areas for **all providers**:
  1. Happy path request/response.
  2. Throttle handling and retry/backoff behavior.
  3. Timeout and cancellation handling.
  4. Degraded response mapping into provider-health/degradation outcomes.
  5. Replay/backfill scenario correctness.
  6. Schema-shape validation for payload parsing and contract projection.

## Naming Convention And Class Targets (Wave A)

For Wave A providers (Polygon, Alpaca, Interactive Brokers, Robinhood), implement a comparable test envelope using these class targets in `tests/Meridian.Tests`.

### Class naming pattern

Use the following pattern to keep provider coverage parallel and discoverable:

- `<ProviderName>ProviderContractTests`
- Optional split classes for larger providers:
  - `<ProviderName>ProviderContractThrottleTests`
  - `<ProviderName>ProviderContractReplayTests`
  - `<ProviderName>ProviderContractSchemaTests`

### Explicit Wave A targets

- `PolygonProviderContractTests`
- `AlpacaProviderContractTests`
- `InteractiveBrokersProviderContractTests`
- `RobinhoodProviderContractTests`

Each class (or the grouped class set when split) must include test methods covering all six required areas.

## Test Method Convention

Use descriptive scenario-style names:

- `MethodOrScenario_Given<Condition>_When<Action>_Then<Outcome>`

Recommended suffix tags by requirement area:

- Happy path: `..._ThenReturnsExpectedData`
- Throttle handling: `..._ThenHonorsThrottlePolicy`
- Timeout/cancellation: `..._ThenCancelsOrTimesOutDeterministically`
- Degraded mapping: `..._ThenMapsDegradedResponseToProviderHealth`
- Replay/backfill: `..._ThenReplaysOrBackfillsWithoutGaps`
- Schema shape: `..._ThenMatchesExpectedSchemaShape`

## Fixture Requirements (Non-Deterministic External APIs)

To avoid flaky CI dependence on live external services, providers that integrate non-deterministic external APIs must use one of the following in contract tests:

- Synthetic fixtures (generated deterministic payloads), or
- Recorded payload fixtures checked into the repository and replayed in tests.

Live-network calls are allowed only in explicitly separated smoke/integration workflows, not in provider contract tests that run in standard CI.

## Minimum Enforcement Guidance

When adding or promoting a provider, verify `tests/Meridian.Tests` includes the provider contract class envelope and all six required scenario areas before claiming readiness in status/runbook documentation.
