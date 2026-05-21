# Synthetic Provider Test Harness (Short Guide)

Use `SyntheticProviderTestHarness` for deterministic provider-family tests that need repeatable resilience behavior.

## Location

- Harness: `tests/Meridian.Tests/Infrastructure/Providers/SyntheticProviderTestHarness.cs`
- Example migrated slices:
  - `tests/Meridian.Tests/Infrastructure/Providers/SyntheticMarketDataProviderTests.cs`
  - `tests/Meridian.Tests/Infrastructure/Providers/SyntheticHistoricalProviderContractTests.cs`

## Scenario Controls

`SyntheticScenarioConfig` is available on `SyntheticMarketDataConfig.Scenario` and supports:

- `ThrottleEveryNCalls` + `ThrottleDelayMs`: deterministic soft-throttle cadence.
- `TimeoutEveryNCalls`: deterministic timeout injection cadence.
- `DegradeEveryNEvents`: periodic quote/trade/depth degradation for health-path assertions.
- `ReplayBarsLimit`: cap replay-like history requests.
- `BackfillBarsLimit`: cap backfill-oriented history requests.
- `ApplyToHistorical` / `ApplyToStreaming`: select flow scope.

## Minimal Usage

```csharp
var config = SyntheticProviderTestHarness.BuildScenarioConfig(
    throttleEveryNCalls: 3,
    timeoutEveryNCalls: 5,
    degradeEveryNEvents: 4,
    replayBarsLimit: 25,
    backfillBarsLimit: 100);

var provider = SyntheticProviderTestHarness.CreateHistorical(config);
```

Keep scenario cadence small in unit tests so failure/timeout paths are exercised within 1-3 calls.
