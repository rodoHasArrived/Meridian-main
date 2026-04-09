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

## Provider Wire-Format Catalog

**For every test that exercises a provider's parsing path, use authentic wire-format data.**
Before writing the test, locate the recorded-session fixture (if any) and read the official docs:

```
tests/Meridian.Tests/Infrastructure/Providers/Fixtures/
├── InteractiveBrokers/   (8 IB order JSON fixtures)
└── Polygon/              (7 Polygon recorded WebSocket session fixtures)
```

### Official API Documentation

| Provider | Type | Docs |
|----------|------|------|
| Alpaca | Streaming | https://docs.alpaca.markets/reference/stockstrades · https://docs.alpaca.markets/reference/stocksquotes |
| Alpaca | Historical | https://docs.alpaca.markets/reference/stockbars |
| Polygon | Streaming | https://polygon.io/docs/stocks/ws_stocks_t · https://polygon.io/docs/stocks/ws_stocks_q |
| Polygon | Historical | https://polygon.io/docs/stocks/get_v2_aggs_ticker__stocksticker__range__multiplier___timespan___from___to |
| Finnhub | Historical | https://finnhub.io/docs/api/stock-candles |
| Alpha Vantage | Historical | https://www.alphavantage.co/documentation/#time-series-daily |
| Tiingo | Historical | https://www.tiingo.com/documentation/end-of-day |
| Twelve Data | Historical | https://twelvedata.com/docs#time-series |
| Nasdaq Data Link | Historical | https://docs.data.nasdaq.com/docs/time-series |
| Interactive Brokers | Streaming + Historical | https://interactivebrokers.github.io/tws-api/ |
| FRED | Historical | https://fred.stlouisfed.org/docs/api/fred/series_observations.html |

### Alpaca Streaming Wire Format

```json
// Connection / auth
[{"T":"success","msg":"connected"}]
[{"T":"success","msg":"authenticated"}]
// Trade — nanosecond ISO 8601; string exchange code; string-array conditions
[{"T":"t","S":"AAPL","p":213.45,"s":100,"t":"2025-06-02T13:30:00.123456789Z","i":"71620539","x":"V","c":["@"],"z":"C"}]
// BBO quote
[{"T":"q","S":"AAPL","bx":"V","bp":213.44,"bs":300,"ax":"V","ap":213.46,"as":200,"t":"2025-06-02T13:30:00.456Z","z":"C"}]
```

Key differentiators from Polygon: lowercase `"T"` type discriminator, `"S"` for symbol,
nanosecond ISO 8601 timestamps, **string** exchange codes, **string-array** condition codes.

### Polygon Streaming Wire Format

```json
// Connection / auth
[{"ev":"status","status":"connected","message":"Connected Successfully"}]
[{"ev":"status","status":"auth_success","message":"authenticated"}]
// Trade — millisecond epoch; integer exchange code; integer-array conditions
[{"ev":"T","sym":"AAPL","p":213.45,"s":100,"t":1748871000123,"i":"71620539","x":4,"c":[12,37]}]
// BBO quote
[{"ev":"Q","sym":"AAPL","bp":213.44,"bs":300,"ap":213.46,"as":200,"t":1748871000456,"x":4}]
// Minute aggregate (window: s → e)
[{"ev":"AM","sym":"AAPL","o":213.10,"h":213.50,"l":213.00,"c":213.45,"v":58000,"vw":213.28,"s":1748870940000,"e":1748871000000,"n":900}]
```

Key differentiators from Alpaca: uppercase `"ev"` type discriminator, `"sym"` for symbol,
millisecond epoch timestamps, **integer** exchange codes, **integer-array** condition codes.

### Alpaca Historical (REST)

```json
{"bars":[{"t":"2025-06-02T13:30:00Z","o":213.10,"h":213.50,"l":213.00,"c":213.45,"v":58000,"vw":213.28,"n":900,"S":"AAPL"}],"symbol":"AAPL","next_page_token":null}
```

### Polygon Historical (REST)

```json
{"ticker":"AAPL","adjusted":true,"results":[{"v":58000,"vw":213.28,"o":213.10,"c":213.45,"h":213.50,"l":213.00,"t":1748871000000,"n":25000}],"status":"OK","count":1}
```

### Finnhub Historical (REST)

```json
{"c":[213.45,214.20],"h":[213.50,214.30],"l":[213.00,213.90],"o":[213.10,213.70],"s":"ok","t":[1748871000,1748957400],"v":[58000,47500]}
```

Parallel arrays; `t` is Unix epoch **seconds**.

### Alpha Vantage Historical (REST)

```json
{"Time Series (Daily)":{"2025-06-02":{"1. open":"213.10","2. high":"213.50","3. low":"213.00","4. close":"213.45","5. adjusted close":"213.45","6. volume":"58000","7. dividend amount":"0.0000","8. split coefficient":"1.0"}}}
```

All numeric values are **strings**; keys use numbered prefix notation.

### Tiingo Historical (REST)

```json
[{"date":"2025-06-02T00:00:00+00:00","open":213.10,"high":213.50,"low":213.00,"close":213.45,"volume":58000,"adjClose":213.45,"divCash":0.0,"splitFactor":1.0}]
```

### Twelve Data Historical (REST)

```json
{"values":[{"datetime":"2025-06-02","open":"213.10000","high":"213.50000","low":"213.00000","close":"213.45000","volume":"58000"}],"status":"ok"}
```

All numeric fields in `values[]` are **strings**.

### FRED Historical (REST)

```json
{"observations":[{"date":"2025-06-02","value":"5.33"}]}
```

---

## Default Checklist

- Build a local `CreateSut()` helper where it improves readability.
- Keep assertions semantic and specific (use FluentAssertions).
- Prefer deterministic fakes, `MarketScenarioBuilder`, and temp directories.
- Validate cleanup behavior when file handles, channels, sockets, or timers are involved.
- **[Pattern A/B — provider tests]** Study the provider wire format (catalog above) before constructing test inputs.
- **[Pattern I]** Add XML `<summary>` doc naming the scenario and the market failure mode guarded.
- **[Pattern I]** Assert on business-observable outcomes, not just absence of exceptions.
