# Live Trading Engine

**Status:** active
**Owner:** core-team

The live trading engine closes the circuit between promotion governance and execution: a
promoted paper/live `StrategyRunEntry` no longer stops at an audit record — it is activated
against the live market data feed, drives the promoted strategy's callbacks, and routes the
strategy's orders through the governed OMS.

## The loop

```
providers → collectors → IMarketEventPublisher ──┬─→ DualPathEventPipeline → storage (unchanged)
                                                 └─→ LiveTradingMarketEventTap
                                                        ├─→ LiveMarketDataCache   (ILiveFeedAdapter: last trade/quote/book)
                                                        └─→ LiveMarketEventHub    (ILiveMarketEventFeed: per-session fan-out)

PromotionService.ApproveAsync ─→ IPromotedRunLauncher.TryLaunchAsync(newRun)
    LiveTradingEngine
        ├─ resolves ILiveStrategy from ILiveStrategyCatalog (run.StrategyId / "liveStrategyId" parameter)
        ├─ builds LiveStrategyExecutionContext (IBacktestContext + IExecutionContext)
        └─ LiveStrategyRunSession event loop (live analogue of BacktestEngine.RunAsync):
              feed event → OnTrade/OnQuote/OnBar/OnOrderBook → ctx.Place*Order()
              → drained orders → IOrderManager.PlaceOrderAsync (OMS pre-trade gate stack)
              → OMS ExecutionReports (fill increments) → FillEvent → OnOrderFill
              → day boundaries → OnDayEnd → daily equity metrics
        run stop → OnFinished → run recorded Completed with SummaryOnly BacktestResult metrics
```

Key components:

| Concern | Type | Location |
| --- | --- | --- |
| Feed seam + fan-out | `ILiveMarketEventFeed`, `LiveMarketEventHub` | `src/Meridian.Execution/Live/` |
| Last-price cache | `LiveMarketDataCache` (`ILiveFeedAdapter`) | `src/Meridian.Execution/Adapters/` |
| Engine + sessions | `LiveTradingEngine`, `LiveStrategyRunSession` | `src/Meridian.Strategies/Live/` |
| Strategy context | `LiveStrategyExecutionContext` | `src/Meridian.Strategies/Live/` |
| Strategy catalog | `ILiveStrategyCatalog`, `LiveStrategyCatalog` | `src/Meridian.Strategies/Live/` |
| Concrete strategies | `BuyAndHoldLiveStrategy`, `MovingAverageCrossoverLiveStrategy`, `BacktestStrategyLiveAdapter`, `LiveStrategyBase` | `src/Meridian.Strategies/Live/` |
| Promotion hook | `IPromotedRunLauncher` (optional `PromotionService` dependency) | `src/Meridian.Strategies/Interfaces/` |
| Host wiring | `AddLiveTradingEngine`, `LiveTradingMarketEventTap`, `LiveTradingEngineHostedService` | `src/Meridian/LiveTradingEngineHostServiceCollectionExtensions.cs` |

## Strategy resolution

Promotion carries only a strategy id and parameter set, so the catalog is the seam that turns
the governance record into runnable code:

1. Exact registered catalog id (`buy-and-hold`, `moving-average-crossover`, host-registered ids).
2. The run parameter `liveStrategyId` naming a registered factory (the created strategy still
   reports the run's own strategy id for lifecycle/audit alignment).
3. Otherwise the launch is deferred with an audited reason and the run entry stays retained.

Any existing `IBacktestStrategy` can run live via `BacktestStrategyLiveAdapter`; new strategies
extend `LiveStrategyBase`. The trading universe comes from the run parameters `symbols`/`symbol`
(comma/space separated) or `Execution:LiveTradingEngine:DefaultSymbols`.

## Safety posture (paper-first)

- Paper runs may always activate; fills are simulated by the paper gateways, which now price
  market fills from the live feed (last trade, then quote midpoint) before falling back to the
  loud scaffold price.
- Live runs additionally require **both** `Execution:LiveTradingEngine:AllowLiveRuns` and
  `Execution:Brokerage:LiveExecutionEnabled` (bound to `BrokerageConfiguration`); otherwise the
  launch defers and the run stays retained.
- All orders go through `IOrderManager.PlaceOrderAsync`, so the OMS placement gate, live-order
  readiness gate, operator controls (circuit breaker), Security Master gate, and risk validator
  all apply to engine-originated orders. In live mode the resolvable `IOrderGateway` remains the
  OMS-governed read-only view.
- Bracket orders and attached take-profit/stop-loss exits fail closed
  (`NotSupportedException`) instead of being silently dropped.
- Host shutdown stops sessions but leaves run entries open; `LiveTradingEngineHostedService`
  resumes still-open promoted runs on the next start (`ResumePendingRunsAsync`).
- Completed runs retain a `SummaryOnly` `BacktestResult` (Sharpe/drawdown/return computed from
  the session's daily equity series) so paper → live promotion evaluation has honest metrics;
  unavailable closed-trade statistics are reported as coverage warnings, not fabricated.

## Configuration

```jsonc
{
  "Execution": {
    "Brokerage": {            // binds BrokerageConfiguration (default: paper, live disabled)
      "Gateway": "paper",     // "alpaca", "ib"/"ibkr", "robinhood", "stocksharp" once enabled
      "LiveExecutionEnabled": false
    },
    "LiveTradingEngine": {
      "Enabled": true,
      "AllowLiveRuns": false,
      "DefaultSymbols": []
    }
  }
}
```

`UiServer` now invokes `AddHostedBrokerageGateways()` + `AddBrokerageExecution(...)`: the
default paper posture is unchanged, and enabling a named gateway routes live orders to the
registered brokerage gateway behind the OMS. Interactive Brokers still requires the vendor SDK
(`IBAPI` build constant) to route real orders; without it the IB gateway remains non-functional
by design.

## Tests

- `tests/Meridian.Tests/Strategies/LiveTradingEngineTests.cs` — closed-circuit loop, launch
  gating, resume sweep, promotion → launcher handoff.
- `tests/Meridian.Tests/Execution/PaperGatewayLiveFeedPricingTests.cs` — feed-priced paper fills.
- `tests/Meridian.Tests/Ui/LiveTradingEngineHostRegistrationTests.cs` — host tap composition.
