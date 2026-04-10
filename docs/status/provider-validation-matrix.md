# Provider Validation Matrix (Polygon, IB, StockSharp, NYSE)

**Last Updated:** 2026-04-01  
**Scope:** Replay scenarios, reconnect behavior, cancellation handling, auth failure behavior, and rate-limit handling.

This matrix is the execution checklist referenced by `production-status.md` and `FEATURE_INVENTORY.md` for provider readiness gating.

## Legend

- ✅ Validated in-repo with executable evidence link(s)
- ⚠️ Partially validated (coverage exists, but not full live-vendor runtime proof)
- ❌ Not yet validated

## Validation Matrix

| Provider | Replay Scenarios | Reconnect Behavior | Cancellation | Auth Failure | Rate-Limit Handling | Evidence |
|---|---|---|---|---|---|---|
| Polygon | ✅ | ⚠️ | ✅ | ✅ | ⚠️ | `PolygonRecordedSessionReplayTests`, `PolygonMarketDataClientTests`, fixtures under `Fixtures/Polygon` |
| Interactive Brokers (IB) | ⚠️ | ⚠️ | ⚠️ | ⚠️ | ❌ | `IBRuntimeGuidanceTests`, `IBSimulationClientContractTests`, `build-ibapi-smoke.ps1` |
| StockSharp | ⚠️ | ⚠️ | ⚠️ | ⚠️ | ❌ | `StockSharpSubscriptionTests`, `StockSharpMessageConversionTests`, `StockSharpConnectorFactoryTests` |
| NYSE | ⚠️ | ✅ | ⚠️ | ⚠️ | ❌ | `NyseMarketDataClientTests`, `NYSEMessageParsingTests`, `NyseTaqCollectorIntegrationTests` |

## Scenario Notes

### Polygon

- **Replay scenarios** are validated with recorded-session fixtures that include trades, quotes, second/minute aggregates, and status frames through the production parser path.
- **Reconnect behavior** currently has provider-level baseline coverage, but no committed Polygon live reconnect replay transcript yet.
- **Cancellation** is covered at provider client level (`ConnectAsync`/`DisconnectAsync` and cancellation token paths).
- **Auth failure** is validated by status-frame fixtures (`auth_failed`) and missing-key connection exception tests.
- **Rate-limit handling** exists for Polygon REST ingest paths; WebSocket runtime throttling still needs live verification evidence.

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
