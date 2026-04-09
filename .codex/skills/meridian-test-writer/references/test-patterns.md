# Meridian Test Patterns

Use this file to pick the right destination and test style quickly.

---

## Test Project Map

- `tests/Meridian.Tests/`: general backend, storage, providers, application services, endpoint coverage, market scenario tests
- `tests/Meridian.FSharp.Tests/`: F# modules and interop-focused coverage
- `tests/Meridian.Ui.Tests/`: shared UI-service behavior
- `tests/Meridian.Wpf.Tests/`: WPF-specific behavior

---

## Component Routing

| Component | Project | Subdirectory |
|-----------|---------|-------------|
| Historical provider | `Meridian.Tests` | `Infrastructure/Providers/` |
| Streaming provider | `Meridian.Tests` | `Infrastructure/Providers/` |
| Storage sink / WAL / `AtomicFileWriter` | `Meridian.Tests` | `Storage/` |
| Pipeline component | `Meridian.Tests` | `Application/Pipeline/` |
| Pure application service | `Meridian.Tests` | `Application/Services/` |
| UI service or config/status service | `Meridian.Ui.Tests` | `Services/` |
| WPF ViewModel or WPF-specific service | `Meridian.Wpf.Tests` | `ViewModels/` or `Services/` |
| F# module or interop boundary | `Meridian.FSharp.Tests` | (root) |
| Endpoint integration | `Meridian.Tests` | `Integration/EndpointTests/` |
| **Market scenario (multi-layer)** | **`Meridian.Tests`** | **`Integration/`** |

---

## Pattern I: Market Scenario Simulation

**Use Pattern I when** the behaviour under test is triggered by a named real-world market event
and spans two or more architectural layers.

**Naming:** `Scenario_MarketCondition_SystemBehavior`

### Market Scenario Catalog

Select a scenario before writing any code.

#### Tier 1 — Data Ingestion (Provider → Pipeline → Storage)

| Scenario | Trigger | Code Paths |
|----------|---------|-----------|
| Normal session open | Burst of trades + BBO quotes at 09:30 ET | Provider parsing → collector → dedup → pipeline → JSONL sink |
| Pre-market gap-up on earnings | High-volume trades above prior close before 09:30 | Same + out-of-hours flag + timestamp monotonicity checker |
| Provider feed interruption | WebSocket disconnect mid-session | Reconnection → sequence gap detection → integrity event emission |
| Rate-limit breach | Provider returns HTTP 429 | Rate limiter → exponential backoff → circuit breaker |
| Stale quote flood | Repeated identical BBO ticks | Dedup store → pipeline drop counter → backpressure signal |
| Crossed market | Bid > Ask received | Quote validation → integrity event → bad-tick filter |
| Flash crash | Price drops 10 %+ within 1 second | Price continuity checker → anomaly detector → alert dispatcher |

#### Tier 2 — Backtesting (Historical Data → Engine → Metrics)

| Scenario | Trigger | Code Paths |
|----------|---------|-----------|
| Single-symbol buy-and-hold | Buy day 1, sell last day | Bar replay → order → fill model → portfolio snapshot → XIRR |
| Multi-symbol rebalance | Portfolio drifts, rebalances weekly | Multiple bar streams → lot tracking → commission → drawdown |
| Stop-loss trigger | Price falls below threshold | Contingent order manager → fill model → PnL |
| Dividend corporate action | Adjusted close deviates from raw | Corporate action adjuster → cost basis recalculation |
| Earnings announcement gap | Open deviates significantly from prior close | Gap detection → position sizing → slippage in fill model |

#### Tier 3 — Execution & Risk (Order → Gateway → Risk)

| Scenario | Trigger | Code Paths |
|----------|---------|-----------|
| Paper trade order lifecycle | Strategy submits limit; fill arrives | Order manager → paper gateway → fill event → portfolio state |
| Position limit breach | Strategy exceeds max position | Risk validator → position limit rule → order rejection |
| Drawdown circuit breaker | Portfolio loss exceeds threshold | Drawdown circuit breaker → strategy halt signal |
| Order rate throttle | Orders exceed allowed rate | Order rate throttle rule → rejection with backoff |
| Multi-account allocation | Block order split across accounts | Block trade allocator → proportional allocation engine |

#### Tier 4 — Storage & Recovery

| Scenario | Trigger | Code Paths |
|----------|---------|-----------|
| WAL crash recovery | Process dies mid-write | WAL write → simulated truncation → WAL replay → integrity check |
| Parquet conversion | JSONL file exceeds threshold | Archival service → Parquet sink → JSONL cleanup |
| Concurrent sink writes | Multiple providers write simultaneously | Composite sink → file locking → atomic writer |
| Storage quota enforcement | Disk usage approaches limit | Quota enforcement service → backpressure signal |

---

## `MarketScenarioBuilder` Helper

Place at `tests/Meridian.Tests/TestHelpers/MarketScenarioBuilder.cs`.

```csharp
internal static class MarketScenarioBuilder
{
    /// <summary>
    /// Mixed burst of trades + BBO quotes for multiple symbols at session open.
    /// </summary>
    public static List<MarketEvent> BuildSessionOpen(
        IReadOnlyList<string> symbols,
        DateTimeOffset openTime,
        int tradesPerSymbol,
        int quotesPerSymbol,
        IReadOnlyDictionary<string, decimal>? basePrice = null)
    {
        var events = new List<MarketEvent>();
        long seq = 1;
        foreach (var symbol in symbols)
        {
            var price = basePrice?.GetValueOrDefault(symbol) ?? 100m;
            events.AddRange(BuildSequentialTrades(symbol, openTime, tradesPerSymbol, seq, price));
            seq += tradesPerSymbol;
            events.AddRange(BuildSequentialQuotes(symbol, openTime, quotesPerSymbol, seq, price));
            seq += quotesPerSymbol;
        }
        return events;
    }

    /// <summary>Deterministic sequential trades with monotonic sequence numbers.</summary>
    public static List<MarketEvent> BuildSequentialTrades(
        string symbol, DateTimeOffset startTime, int count,
        long startSequence, decimal startPrice, decimal priceStep = 0.01m)
    {
        var events = new List<MarketEvent>(count);
        for (var i = 0; i < count; i++)
        {
            var ts = startTime.AddMilliseconds(i * 20);
            var trade = new Trade(Timestamp: ts, Symbol: symbol,
                Price: startPrice + priceStep * i, Size: 100L,
                Aggressor: i % 2 == 0 ? AggressorSide.Buy : AggressorSide.Sell,
                SequenceNumber: startSequence + i, Venue: "XNAS");
            events.Add(MarketEvent.Trade(ts, symbol, trade, seq: startSequence + i, source: "XNAS"));
        }
        return events;
    }

    /// <summary>Deterministic BBO quote sequence around midPrice.</summary>
    public static List<MarketEvent> BuildSequentialQuotes(
        string symbol, DateTimeOffset startTime, int count,
        long startSequence, decimal midPrice, decimal halfSpread = 0.01m)
    {
        var events = new List<MarketEvent>(count);
        for (var i = 0; i < count; i++)
        {
            var ts = startTime.AddMilliseconds(i * 50);
            var quote = new BboQuotePayload
            {
                Symbol = symbol, BidPrice = midPrice - halfSpread,
                AskPrice = midPrice + halfSpread, BidSize = 200, AskSize = 200, Timestamp = ts,
            };
            events.Add(MarketEvent.Quote(ts, symbol, quote, seq: startSequence + i, source: "XNAS"));
        }
        return events;
    }

    /// <summary>Flash crash: price drops dropPct% over durationMs with sell-side aggressor.</summary>
    public static List<MarketEvent> BuildFlashCrash(
        string symbol, DateTimeOffset startTime, decimal preCrashPrice,
        decimal dropPct = 0.10m, int count = 50, int durationMs = 800)
    {
        var events = new List<MarketEvent>(count);
        var dropPerTick = preCrashPrice * dropPct / count;
        for (var i = 0; i < count; i++)
        {
            var ts = startTime.AddMilliseconds(i * durationMs / count);
            var trade = new Trade(Timestamp: ts, Symbol: symbol,
                Price: preCrashPrice - dropPerTick * i, Size: 5_000L,
                Aggressor: AggressorSide.Sell, SequenceNumber: i + 1L, Venue: "XNAS");
            events.Add(MarketEvent.Trade(ts, symbol, trade, seq: i + 1L, source: "XNAS"));
        }
        return events;
    }

    /// <summary>
    /// Feed interruption: trades before gap, sequence gap (simulates missed events), trades after.
    /// </summary>
    public static (List<MarketEvent> BeforeGap, List<MarketEvent> AfterGap) BuildFeedInterruption(
        string symbol, DateTimeOffset startTime,
        int tradesBeforeGap = 5, int gapSize = 10, int tradesAfterGap = 5)
    {
        var before = BuildSequentialTrades(symbol, startTime, tradesBeforeGap,
            startSequence: 1L, startPrice: 100m);
        var resumeSeq = 1L + tradesBeforeGap + gapSize;
        var after = BuildSequentialTrades(symbol, startTime.AddSeconds(1), tradesAfterGap,
            startSequence: resumeSeq, startPrice: 100m);
        return (before, after);
    }
}
```

---

## Default Checklist

- Build a local `CreateSut()` helper where it improves readability.
- Keep assertions semantic and specific (use FluentAssertions).
- Prefer deterministic fakes, `MarketScenarioBuilder`, and temp directories.
- Validate cleanup behavior when file handles, channels, sockets, or timers are involved.
- **[Pattern I]** Add XML `<summary>` doc naming the scenario and the market failure mode guarded.
- **[Pattern I]** Assert on business-observable outcomes, not just absence of exceptions.
