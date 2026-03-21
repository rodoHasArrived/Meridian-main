# ADR-015: Strategy Execution Contract

**Status:** Accepted
**Date:** 2026-03-18
**Deciders:** Core Team

## Context

The platform has grown beyond market data collection to include a backtesting engine
(`Meridian.Backtesting`) and will expand further to support live and
simulated order execution. Without a canonical execution contract, each broker
integration would grow its own bespoke order-submission API, making it impossible for
strategy code to remain broker-agnostic or to promote a strategy from paper trading to
live without code changes.

Specific gaps in the current design:

- `IBacktestContext` exposes `PlaceMarketOrder`/`PlaceLimitOrder` but these are
  simulation-only — there is no live-mode equivalent.
- No interface defines how a strategy receives a live market feed as opposed to a
  replayed historical feed.
- No contract separates "strategy reasoning" from "broker connectivity", so switching
  brokers requires modifying strategy code.

## Decision

Introduce two new interfaces that form the **Strategy Execution Contract**:

1. **`IOrderGateway`** — the single broker-agnostic abstraction for submitting,
   cancelling, and monitoring orders. All broker adapters (paper trading, Interactive
   Brokers, Alpaca, etc.) implement this interface.
2. **`IExecutionContext`** — the live-mode analogue of `IBacktestContext`. Provides a
   strategy with a unified view of the current feed, portfolio state, and order
   gateway without exposing any broker-specific type.

These interfaces live in `src/Meridian.Execution/Interfaces/` and are
referenced directly by the strategy layer via a project reference to
`Meridian.Execution`.

## Implementation Links

| Component | Location | Purpose |
|-----------|----------|---------|
| Order gateway interface | `src/Meridian.Execution/Interfaces/IOrderGateway.cs` | Broker-agnostic order submission |
| Execution context interface | `src/Meridian.Execution/Interfaces/IExecutionContext.cs` | Unified live-strategy context |
| Live feed adapter interface | `src/Meridian.Execution/Interfaces/ILiveFeedAdapter.cs` | Live market data surface for strategies |
| Order models | `src/Meridian.Execution/Models/` | `OrderRequest`, `OrderAcknowledgement`, `OrderStatusUpdate` |
| Paper trading gateway | `src/Meridian.Execution/Adapters/PaperTradingGateway.cs` | Simulated execution over live Meridian feed |
| Order lifecycle manager | `src/Meridian.Execution/Services/OrderLifecycleManager.cs` | Tracks in-flight orders and state transitions |

## Rationale

### Broker Independence

By coding strategies to `IExecutionContext` rather than any broker type, switching from
paper trading to Interactive Brokers is a configuration change, not a code change. This
mirrors the `IMarketDataClient` / `IHistoricalDataProvider` pattern established in
ADR-001 for the data collection layer.

### Paper-First Safety

The `PaperTradingGateway` implementation provides a safe default that routes no real
orders to any exchange. Strategy authors can validate live-mode behavior using a real
feed at zero financial risk before promoting to a live broker adapter. Promotion is
intentionally gated (see ADR-016 and `BacktestToLivePromoter`).

### Async-First with Streaming Order Updates

`IOrderGateway.StreamOrderUpdatesAsync` returns `IAsyncEnumerable<OrderStatusUpdate>`,
consistent with ADR-004 (async streaming patterns). Strategies receive fills through
the same `OnOrderFill` callback defined in `IBacktestStrategy`, ensuring a uniform
strategy API across backtest and live modes.

## Alternatives Considered

### Alternative 1: Extend IBacktestContext for Live Use

Reuse `IBacktestContext` directly for live execution by making it source-agnostic.

**Pros:** No new interfaces; strategy code unchanged.

**Cons:** `IBacktestContext` carries simulation-specific semantics (deterministic time,
replay cursor). Live context must expose real-time, non-deterministic feeds, which
fundamentally conflicts with the replay model. Mixing these concerns makes both harder
to test.

**Why rejected:** Semantic mismatch is unresolvable without breaking `IBacktestContext`
for its intended purpose.

### Alternative 2: Direct Broker SDK Usage in Strategies

Allow strategies to `using Alpaca.Markets;` directly.

**Pros:** Full access to provider features without abstraction overhead.

**Cons:** Permanent vendor lock-in; impossible to paper-trade or switch brokers;
testing requires live broker credentials.

**Why rejected:** Eliminates the portability and testability that are core platform values.

## Consequences

### Positive

- Strategies written against `IExecutionContext` run unchanged on paper, IB, and Alpaca
- `PaperTradingGateway` enables safe iteration before any real capital is committed
- Consistent `[ImplementsAdr("ADR-015")]` attribute enables build-time ADR compliance checks
- Clear separation makes each broker adapter independently testable with mock contexts

### Negative

- Strategy authors must learn two interfaces (`IBacktestStrategy` for backtesting,
  `IExecutionContext` for live) — mitigated by shared callbacks (`OnBar`, `OnTrade`, etc.)
- Real broker adapters require significant work to map broker-specific state machines
  to the `OrderStatusUpdate` stream

### Neutral

- The `ExecutionMode` enum (Paper / Live) on `IOrderGateway` makes the current mode
  explicit at runtime without requiring separate subclasses

## Compliance

### Code Contracts

```csharp
// All order gateway implementations must carry this attribute:
[ImplementsAdr("ADR-015", "Strategy Execution Contract")]
public sealed class PaperTradingGateway : IOrderGateway { ... }

// Strategies interact only with this context in live mode:
public interface IExecutionContext
{
    IOrderGateway Gateway { get; }
    ILiveFeedAdapter Feed { get; }
    IPortfolioState Portfolio { get; }
}

// The gateway contract:
public interface IOrderGateway : IAsyncDisposable
{
    string BrokerName { get; }
    ExecutionMode Mode { get; }
    Task<OrderAcknowledgement> SubmitAsync(OrderRequest request, CancellationToken ct = default);
    Task<bool> CancelAsync(string orderId, CancellationToken ct = default);
    IAsyncEnumerable<OrderStatusUpdate> StreamOrderUpdatesAsync(CancellationToken ct = default);
}
```

### Runtime Verification

- `[ImplementsAdr("ADR-015")]` attribute on all `IOrderGateway` implementations
- `ExecutionMode.Paper` must be the default; `ExecutionMode.Live` requires explicit opt-in
- No strategy assembly may reference `Meridian.Execution` concrete types directly

## References

- [ADR-001: Provider Abstraction Pattern](001-provider-abstraction.md) — Same interface
  pattern applied to the execution layer
- [ADR-004: Async Streaming Patterns](004-async-streaming-patterns.md) — `IAsyncEnumerable` for order update streams
- [ADR-016: Platform Architecture Migration Mandate](016-platform-architecture-migration.md)
- `src/Meridian.Execution/` — Implementation project

---

*Last Updated: 2026-03-18*
