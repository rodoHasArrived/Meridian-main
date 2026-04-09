# Provider Validation Matrix (Alpaca, Polygon, Robinhood, IB, StockSharp, NYSE)

**Last Updated:** 2026-04-08  
**Scope:** Wave 1 provider-confidence gate across replay scenarios, reconnect behavior, cancellation handling, auth failure behavior, rate-limit handling, and stable-seam execution validation for execution- and streaming-oriented provider readiness.

This matrix is the primary Wave 1 evidence checklist referenced by `production-status.md`, `ROADMAP.md`, and `FEATURE_INVENTORY.md` for provider-readiness gating.
Historical-only and Security Master support providers such as FRED and EDGAR are tracked in `FEATURE_INVENTORY.md` rather than this execution-oriented matrix.

## Legend

- ✅ Validated in-repo with executable evidence link(s)
- ⚠️ Partially validated (coverage exists, but not full live-vendor runtime proof)
- ❌ Not yet validated

## Validation Matrix

| Provider | Replay Scenarios | Reconnect Behavior | Cancellation | Auth Failure | Rate-Limit Handling | Evidence |
|---|---|---|---|---|---|---|
| Alpaca | ⚠️ | ✅ | ⚠️ | ✅ | ⚠️ | `ExecutionGovernanceEndpointsTests.AlpacaExecutionPath_SubmitsOrderThroughStableExecutionSeam`, `AlpacaCredentialAndReconnectTests` |
| Polygon | ✅ | ⚠️ | ✅ | ✅ | ⚠️ | `PolygonRecordedSessionReplayTests`, `PolygonMarketDataClientTests`, fixtures under `Fixtures/Polygon` |
| Robinhood | ⚠️ | ⚠️ | ⚠️ | ✅ | ❌ | `ExecutionGovernanceEndpointsTests.RobinhoodExecutionPath_SubmitsOrderThroughStableExecutionSeam`, `RobinhoodBrokerageGatewayTests`, `RobinhoodMarketDataClientTests`, `RobinhoodHistoricalDataProviderTests`, `RobinhoodSymbolSearchProviderTests` |
| Interactive Brokers (IB) | ⚠️ | ⚠️ | ⚠️ | ⚠️ | ❌ | `IBRuntimeGuidanceTests`, `IBSimulationClientContractTests`, `build-ibapi-smoke.ps1` |
| StockSharp | ⚠️ | ⚠️ | ⚠️ | ⚠️ | ❌ | `StockSharpSubscriptionTests`, `StockSharpMessageConversionTests`, `StockSharpConnectorFactoryTests` |
| NYSE | ⚠️ | ✅ | ⚠️ | ⚠️ | ❌ | `NyseMarketDataClientTests`, `NYSEMessageParsingTests`, `NyseTaqCollectorIntegrationTests` |

## Scenario Notes

### Alpaca

- The stable `/api/execution/*` seam is now exercised end to end against the concrete Alpaca brokerage gateway using stubbed Trading API responses.
- Current evidence proves live-gateway connect, order submit, health, and audit-trail wiring without claiming credentialed vendor-paper runtime proof in CI.
- Cancellation and broader rate-limit behavior still need a dedicated Alpaca brokerage test matrix beyond the current submit-path validation.

### Polygon

- **Replay scenarios** are validated with recorded-session fixtures that include trades, quotes, second/minute aggregates, and status frames through the production parser path.
- **Reconnect behavior** currently has provider-level baseline coverage, but no committed Polygon live reconnect replay transcript yet.
- **Cancellation** is covered at provider client level (`ConnectAsync`/`DisconnectAsync` and cancellation token paths).
- **Auth failure** is validated by status-frame fixtures (`auth_failed`) and missing-key connection exception tests.
- **Rate-limit handling** exists for Polygon REST ingest paths; WebSocket runtime throttling still needs live verification evidence.

### Robinhood

- Robinhood is implemented as an unofficial broker-backed surface spanning quote polling, historical daily bars, symbol search, options chains, and brokerage reads/orders.
- The stable `/api/execution/*` seam is exercised end to end against the concrete Robinhood brokerage gateway with stubbed broker responses.
- Auth and token-failure behavior are covered in market-data, historical, and brokerage tests, but reconnect and cancellation remain only partially validated because there is no committed live broker-session replay transcript.
- Robinhood does not expose a public WebSocket market-data feed in this adapter set, so quote readiness is based on polling-path validation rather than transport replay fixtures.
- Explicit provider-level rate-limit fixtures are still missing even though the provider catalog and adapter comments document broker-session throttling constraints.

### Interactive Brokers (IB)

- Current repo baseline is mostly **guidance/smoke validation**, not full live runtime:
  - Non-`IBAPI` guidance tests
  - compile-only smoke build
  - simulated client contract tests
- Missing evidence for sustained reconnect/cancellation/auth/rate-limit behavior against a real TWS/IB Gateway session transcript in CI artifacts.

### StockSharp

- Current baseline validates:
  - stub guidance and setup messaging when packages are missing
  - connector factory support and conversion contracts
- At least one connector profile can be validated end-to-end in local environments, but this is not yet represented as CI-captured evidence in repo artifacts.

### NYSE

- Reconnect lifecycle behavior is covered in unit/integration tests for connection-loss handling and subscription intent retention.
- Auth-failure and rate-limit evidence are not yet represented as explicit pass/fail fixtures in the NYSE test suite.
