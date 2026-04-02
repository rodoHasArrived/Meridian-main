# Provider Confidence Baseline

**Last Updated:** 2026-03-31
**Scope:** Wave 1 provider-confidence evidence for Polygon, NYSE, Interactive Brokers, and StockSharp

This document is the repo-grounded baseline for provider confidence. It separates what Meridian validates in offline or CI-friendly paths from what still requires local vendor software, credentials, entitlements, or manual runtime checks.

Offline or CI evidence is the minimum required confidence bar for this slice. Live-vendor checks are useful follow-on verification, but they are not the baseline release gate for these providers.

## Validation Matrix

| Provider | Required offline / CI evidence | Optional live / vendor evidence | Known gated or unsupported paths |
|---|---|---|---|
| Polygon | `PolygonRecordedSessionReplayTests`, `PolygonMessageParsingTests`, `PolygonSubscriptionTests`, committed replay fixtures under `tests/Meridian.Tests/Infrastructure/Providers/Fixtures/Polygon/` | Real websocket connectivity with valid Polygon credentials and feed entitlements; plan-tier validation for production rates and delayed vs real-time access | No Level 2 depth; runtime quality still depends on Polygon plan/tier and credentials |
| NYSE | `NyseSharedLifecycleTests`, `NyseMarketDataClientTests`, `NYSEMessageParsingTests`, `NyseNationalTradesCsvParserTests` | NYSE credential validation, websocket auth/connectivity, entitlement checks for premium depth, REST historical checks | Premium or Professional tier is required for Level 2 depth; live behavior depends on NYSE credentials and feed entitlements |
| Interactive Brokers | `IBRuntimeGuidanceTests`, `IBOrderSampleTests`, compile-only smoke build via `scripts/dev/build-ibapi-smoke.ps1` | Official vendor `IBApi` DLL/project path, TWS/Gateway connectivity, server-version validation, market-data entitlement checks | Three modes must not be conflated: non-`IBAPI` simulation/runtime-guidance, `EnableIbApiSmoke=true` compile-only smoke, and official `IBAPI` vendor path for real connectivity |
| StockSharp | `StockSharpSubscriptionTests`, `StockSharpMessageConversionTests`, `StockSharpConnectorFactoryTests` and connector capability assertions for named connectors | Real connector runtime checks for installed packages and locally running vendor software such as IQFeed, CQG, Rithmic, or TWS/Gateway | Default CI builds do not enable `STOCKSHARP`; crypto connectors may require additional packages or crowdfunding access beyond the package surfaces Meridian references today |

## Provider Details

### Polygon

**Baseline evidence**
- Replay fixtures in `tests/Meridian.Tests/Infrastructure/Providers/Fixtures/Polygon/` are committed sample sessions that pass through `PolygonMarketDataClient.ProcessTestMessage`.
- `PolygonRecordedSessionReplayTests` lock expected trade, quote, aggregate, and order-flow output while ensuring malformed or ignored frames do not leak unexpected event types.
- Parsing and subscription seams are additionally covered by `PolygonMessageParsingTests`, `PolygonMarketDataClientTests`, and `PolygonSubscriptionTests`.

**Suggested commands**
```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~PolygonRecordedSessionReplayTests"
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~PolygonMessageParsingTests|FullyQualifiedName~PolygonSubscriptionTests"
```

**Manual follow-on verification**
- Connect with a real Polygon API key and confirm the intended feed (`stocks`, `options`, `forex`, or `crypto`).
- Validate any production assumptions about rate limits, delayed data, or plan-tier entitlements outside CI.

### NYSE

**Baseline evidence**
- `NyseSharedLifecycleTests` cover multi-symbol lifecycle, companion quote subscriptions, mixed trade/depth behavior, malformed or unknown messages, and unsubscribe/resubscribe flows.
- `NyseMarketDataClientTests`, `NYSEMessageParsingTests`, and `NyseNationalTradesCsvParserTests` cover the adapter and parser surfaces around `NyseMarketDataClient` and `NYSEDataSource`.

**Suggested commands**
```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~NyseSharedLifecycleTests"
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~NyseMarketDataClientTests|FullyQualifiedName~NYSEMessageParsingTests|FullyQualifiedName~NyseNationalTradesCsvParserTests"
```

**Manual follow-on verification**
- Validate NYSE REST/websocket auth using real credentials.
- Confirm whether the deployment has entitlement for depth beyond the baseline L1/L2 assumptions.

### Interactive Brokers

**Baseline evidence**
- `IBRuntimeGuidanceTests` lock the operator-facing messages for the simulation/runtime-guidance path and the compile-only smoke path.
- `IBOrderSampleTests` preserve committed sample order shapes under `tests/Meridian.Tests/Infrastructure/Providers/Fixtures/InteractiveBrokers/`.
- `scripts/dev/build-ibapi-smoke.ps1` keeps the gated infrastructure build path compilable without claiming live TWS/Gateway compatibility.

**Suggested commands**
```powershell
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~IBRuntimeGuidanceTests|FullyQualifiedName~IBOrderSampleTests"
./scripts/dev/build-ibapi-smoke.ps1
```

**Manual follow-on verification**
- Use the official vendor `IBApi` DLL or project reference and build with `-p:DefineConstants=IBAPI`.
- Validate TWS/Gateway connectivity, server-version compatibility, and entitlements locally.

### StockSharp

**Baseline evidence**
- `StockSharpSubscriptionTests` verify the non-`STOCKSHARP` guidance path and generic subscription/runtime expectations.
- `StockSharpMessageConversionTests` preserve domain-model output and connector capability metadata for representative connectors including Rithmic, IQFeed, Interactive Brokers, and Kraken.
- `StockSharpConnectorFactoryTests` and the stub factory tests ensure unsupported-package guidance points back to `docs/providers/stocksharp-connectors.md`.

**Suggested commands**
```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~StockSharpSubscriptionTests|FullyQualifiedName~StockSharpMessageConversionTests|FullyQualifiedName~StockSharpConnectorFactoryTests"
```

**Manual follow-on verification**
- Build with `EnableStockSharp=true` and install the required connector package surfaces.
- Validate each connector against the local vendor runtime it depends on, such as IQFeed client, CQG demo/live access, Rithmic certificates, or TWS/Gateway.

## Guidance Rules

- Treat offline/CI evidence as the mandatory baseline.
- Treat live-vendor evidence as explicit manual verification, not something implied by the default build.
- Do not claim live readiness, entitlement coverage, or package availability unless the repo has a code path and a documented validation mode that supports that claim.
- When provider setup or runtime messages change, update this file and the linked provider/setup docs in the same change.
