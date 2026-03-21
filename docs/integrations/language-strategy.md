# Meridian - Language Implementation Strategy

**Document Version:** 1.6.1
**Last Updated:** 2026-01-30
**Status:** Partially Implemented (F# Complete, C++ Pending)

---

## Executive Summary

This document outlines the recommended language strategy for Meridian, proposing a polyglot architecture that leverages the strengths of C#, C++, and F# for different components of the system. The goal is to maximize performance in latency-critical paths while maintaining developer productivity and type safety where it matters most.

### Strategy Overview

| Language | Role | Primary Use Cases |
|----------|------|-------------------|
| **C#** | Orchestration & Integration | Application layer, provider SDKs, web infrastructure |
| **C++** | Ultra-Low-Latency Hot Paths | Order book engine, wire protocol parsing, compression |
| **F#** | Domain Modeling & Type Safety | Domain models, validation logic, pure calculations |

---

## C# - Orchestration & Integration Layer

C# remains the primary language for the system's orchestration and integration components.

### Components Using C#

| Component | Location | Purpose |
|-----------|----------|---------|
| **Application Layer** | `src/Meridian/Application/` | Program.cs, ConfigWatcher, StatusWriter, metrics |
| **Infrastructure** | `src/Meridian/Infrastructure/` | Provider clients (IB, Alpaca), subscription management |
| **Storage Policy** | `src/Meridian/Storage/` | JSONL sinks, retention, tier migration orchestration |
| **Web UI** | `src/Meridian/Application/StatusHttpServer.cs` | ASP.NET dashboard, REST API, Prometheus metrics |
| **Provider Integration** | `src/Meridian/Infrastructure/Adapters/` | Official SDK wrappers (IB API, Alpaca SDK) |
| **WPF Desktop App** | `src/Meridian.Wpf/` | WPF desktop application (Windows) |

### Rationale for C#

1. **Rich Ecosystem Integration**
   - Native compatibility with IB TWS API and Alpaca SDK
   - MassTransit for distributed messaging
   - Entity Framework, Dapper for data access if needed

2. **Async/Await Excellence**
   - First-class async/await for I/O-bound operations
   - `System.Threading.Channels` for high-throughput pipelines
   - ConfigureAwait(false) for library code optimization

3. **Developer Productivity**
   - Familiar syntax for .NET developers
   - Excellent IDE tooling (Visual Studio, Rider, VS Code)
   - Strong refactoring support

4. **Native SDK Compatibility**
   - IB TWS API is C#/.NET native
   - Alpaca provides official .NET SDK
   - Avoids FFI complexity for provider integration

### C# Best Practices for This Project

```csharp
// Use readonly structs for immutable data
public readonly record struct MarketQuote(
    string Symbol,
    decimal BidPrice,
    decimal AskPrice,
    int BidSize,
    int AskSize,
    DateTimeOffset Timestamp);

// Use ValueTask for hot paths
public ValueTask<bool> TryPublishAsync(MarketEvent evt, CancellationToken ct = default);

// Use Span<T> for zero-allocation parsing
public static bool TryParse(ReadOnlySpan<char> input, out Trade trade);
```

---

## C++ - Ultra-Low-Latency Hot Paths

C++ should handle latency-critical components where microsecond-level performance is essential.

### Proposed C++ Components

| Component | Expected Improvement | Priority |
|-----------|---------------------|----------|
| **Order Book Matching Engine** | 10-100x latency reduction | High |
| **Wire Protocol Parser (IB)** | 5-10x for message parsing | Medium |
| **JSON Parser (simdjson)** | 10-50x parsing throughput | High |
| **Lock-Free Ring Buffer** | 2-5x pipeline throughput | Medium |
| **Compression Engine** | 10-20x storage reduction | Low |

### Rationale for C++

1. **Zero GC Pauses**
   - No stop-the-world garbage collection
   - Deterministic memory management
   - Critical for sub-millisecond latency requirements

2. **Cache-Line Optimization**
   - Manual control over memory layout
   - NUMA-aware data structures
   - L1/L2 cache optimization

3. **SIMD Operations**
   - AVX2/AVX-512 for bulk operations
   - simdjson for JSON parsing at 2GB/s+
   - Vectorized price comparisons

4. **Deterministic Latency**
   - Microsecond-level consistency
   - No JIT compilation overhead
   - Lock-free algorithms for contention-free paths

### C++ Integration Strategy

```cpp
// Native C++ order book with P/Invoke wrapper
class alignas(64) OrderBook {
public:
    // Cache-line aligned for L1 performance
    struct Level {
        double price;
        int64_t quantity;
        int32_t order_count;
        int32_t _padding;
    };

    // Lock-free update using atomic operations
    bool try_update(double price, int64_t delta_qty) noexcept;

    // Snapshot for C# consumption
    void snapshot(Level* bids, Level* asks, size_t max_levels) const noexcept;
};

// C export for P/Invoke
extern "C" {
    __declspec(dllexport) OrderBook* orderbook_create();
    __declspec(dllexport) void orderbook_update(OrderBook* ob, double price, int64_t qty, int side);
    __declspec(dllexport) void orderbook_snapshot(OrderBook* ob, Level* bids, Level* asks, size_t n);
    __declspec(dllexport) void orderbook_destroy(OrderBook* ob);
}
```

### C# P/Invoke Wrapper

```csharp
// Safe managed wrapper around native order book
public sealed class NativeOrderBook : IDisposable
{
    [DllImport("Meridian.Native", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr orderbook_create();

    [DllImport("Meridian.Native", CallingConvention = CallingConvention.Cdecl)]
    private static extern void orderbook_update(IntPtr ob, double price, long qty, int side);

    [DllImport("Meridian.Native", CallingConvention = CallingConvention.Cdecl)]
    private static extern void orderbook_snapshot(IntPtr ob,
        [Out] Level[] bids, [Out] Level[] asks, nuint maxLevels);

    [DllImport("Meridian.Native", CallingConvention = CallingConvention.Cdecl)]
    private static extern void orderbook_destroy(IntPtr ob);

    private IntPtr _handle;

    public NativeOrderBook() => _handle = orderbook_create();

    public void Update(double price, long qty, Side side)
        => orderbook_update(_handle, price, qty, (int)side);

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            orderbook_destroy(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
```

---

## F# - Domain Modeling & Type Safety

F# excels at domain modeling with algebraic data types and compiler-enforced type safety.

### Proposed F# Components

| Component | Location | Benefit |
|-----------|----------|---------|
| **Domain Models/Events** | `src/Meridian.FSharp/Domain/` | Discriminated unions with exhaustive pattern matching |
| **Order Book Calculations** | `src/Meridian.FSharp/Calculations/` | Pure functional transformations (mid-price, spread, imbalance) |
| **Validation Logic** | `src/Meridian.FSharp/Validation/` | Railway-Oriented Programming with Result types |
| **Event Pipeline Transforms** | `src/Meridian.FSharp/Pipeline/` | AsyncSeq for declarative stream processing |
| **Aggressor Inference** | `src/Meridian.FSharp/Domain/` | Pattern matching on optional BBO state |
| **Integrity Events** | `src/Meridian.FSharp/Domain/` | Smart constructors for well-formed events |

### Rationale for F#

1. **Compiler-Enforced Type Safety**
   - Discriminated unions eliminate null reference exceptions
   - Exhaustive pattern matching catches missing cases at compile time
   - Option types force explicit handling of missing values

2. **Immutability by Default**
   - All values immutable unless explicitly marked mutable
   - Thread-safe by construction
   - Easier reasoning about concurrent code

3. **Composable Validation**
   - Railway-Oriented Programming for clean error handling
   - Result types for explicit success/failure paths
   - Computation expressions for elegant composition

4. **Eliminates Entire Bug Categories**
   - No null reference exceptions (use Option<T>)
   - No missing case handling (exhaustive matches)
   - No accidental mutation (immutability)

### F# Domain Model Examples

```fsharp
// Domain types with discriminated unions
module Meridian.FSharp.Domain

open System

/// Represents the side of an order or trade
type Side =
    | Buy
    | Sell

/// Trade aggressor determination
type AggressorSide =
    | Buyer
    | Seller
    | Unknown

/// Integrity event types with associated data
type IntegrityEvent =
    | SequenceGap of symbol: string * expected: int64 * received: int64
    | OutOfOrder of symbol: string * lastSeq: int64 * receivedSeq: int64
    | NegativeSpread of symbol: string * bid: decimal * ask: decimal
    | BookCrossed of symbol: string * level: int
    | StaleQuote of symbol: string * staleDuration: TimeSpan

/// Market event with type-safe payload
type MarketEvent =
    | Trade of TradeEvent
    | Quote of QuoteEvent
    | Depth of DepthEvent
    | Integrity of IntegrityEvent

and TradeEvent = {
    Symbol: string
    Price: decimal
    Quantity: int
    Side: AggressorSide
    SequenceNumber: int64
    Timestamp: DateTimeOffset
    ExchangeTimestamp: DateTimeOffset option
}

and QuoteEvent = {
    Symbol: string
    BidPrice: decimal
    BidSize: int
    AskPrice: decimal
    AskSize: int
    Timestamp: DateTimeOffset
}

and DepthEvent = {
    Symbol: string
    Side: Side
    Level: int
    Price: decimal
    Quantity: int
    Timestamp: DateTimeOffset
}
```

### F# Validation with Railway-Oriented Programming

```fsharp
module Meridian.FSharp.Validation

open Meridian.FSharp.Domain

/// Validation error types
type ValidationError =
    | InvalidPrice of value: decimal * reason: string
    | InvalidQuantity of value: int * reason: string
    | InvalidSymbol of value: string * reason: string
    | StaleTimestamp of age: TimeSpan
    | InvalidSequence of current: int64 * last: int64

/// Result type alias for validation
type ValidationResult<'T> = Result<'T, ValidationError list>

/// Validation builder for Railway-Oriented Programming
module Validate =
    let price (p: decimal) : ValidationResult<decimal> =
        if p <= 0m then Error [InvalidPrice(p, "Price must be positive")]
        elif p > 1_000_000m then Error [InvalidPrice(p, "Price exceeds maximum")]
        else Ok p

    let quantity (q: int) : ValidationResult<int> =
        if q <= 0 then Error [InvalidQuantity(q, "Quantity must be positive")]
        elif q > 10_000_000 then Error [InvalidQuantity(q, "Quantity exceeds maximum")]
        else Ok q

    let symbol (s: string) : ValidationResult<string> =
        if String.IsNullOrWhiteSpace s then Error [InvalidSymbol(s, "Symbol cannot be empty")]
        elif s.Length > 10 then Error [InvalidSymbol(s, "Symbol too long")]
        else Ok s

    let timestamp maxAge (ts: DateTimeOffset) : ValidationResult<DateTimeOffset> =
        let age = DateTimeOffset.UtcNow - ts
        if age > maxAge then Error [StaleTimestamp age]
        else Ok ts

/// Validate a complete trade event
let validateTrade (trade: TradeEvent) : ValidationResult<TradeEvent> =
    let (<!>) = Result.map
    let (<*>) f r =
        match f, r with
        | Ok f', Ok r' -> Ok (f' r')
        | Error e1, Error e2 -> Error (e1 @ e2)
        | Error e, _ | _, Error e -> Error e

    let create sym price qty =
        { trade with Symbol = sym; Price = price; Quantity = qty }

    create
    <!> Validate.symbol trade.Symbol
    <*> Validate.price trade.Price
    <*> Validate.quantity trade.Quantity
```

### F# Order Book Calculations

```fsharp
module Meridian.FSharp.Calculations

open Meridian.FSharp.Domain

/// Order book level
type BookLevel = {
    Price: decimal
    Quantity: int
    OrderCount: int
}

/// Order book snapshot
type OrderBookSnapshot = {
    Symbol: string
    Bids: BookLevel list
    Asks: BookLevel list
    Timestamp: DateTimeOffset
}

/// Calculate mid-price from best bid and ask
let midPrice (book: OrderBookSnapshot) : decimal option =
    match book.Bids, book.Asks with
    | bid :: _, ask :: _ -> Some ((bid.Price + ask.Price) / 2m)
    | _ -> None

/// Calculate bid-ask spread
let spread (book: OrderBookSnapshot) : decimal option =
    match book.Bids, book.Asks with
    | bid :: _, ask :: _ -> Some (ask.Price - bid.Price)
    | _ -> None

/// Calculate spread in basis points
let spreadBps (book: OrderBookSnapshot) : decimal option =
    match midPrice book, spread book with
    | Some mid, Some sprd when mid > 0m -> Some (sprd / mid * 10000m)
    | _ -> None

/// Calculate order book imbalance (-1 to +1, positive = buy pressure)
let imbalance (book: OrderBookSnapshot) : decimal option =
    match book.Bids, book.Asks with
    | bid :: _, ask :: _ ->
        let bidQty = decimal bid.Quantity
        let askQty = decimal ask.Quantity
        let total = bidQty + askQty
        if total > 0m then Some ((bidQty - askQty) / total)
        else None
    | _ -> None

/// Infer aggressor side from trade price relative to BBO
let inferAggressor (trade: TradeEvent) (bbo: QuoteEvent option) : AggressorSide =
    match bbo with
    | None -> Unknown
    | Some q when trade.Price >= q.AskPrice -> Buyer
    | Some q when trade.Price <= q.BidPrice -> Seller
    | _ -> Unknown

/// Calculate VWAP from a sequence of trades
let vwap (trades: TradeEvent seq) : decimal option =
    let folder (totalValue, totalQty) trade =
        (totalValue + trade.Price * decimal trade.Quantity,
         totalQty + decimal trade.Quantity)

    let (value, qty) = Seq.fold folder (0m, 0m) trades
    if qty > 0m then Some (value / qty) else None
```

---

## Migration Priority

The following table outlines the recommended migration order based on effort vs. impact analysis:

| Phase | Component | Language | Effort | Impact | Dependencies | Status |
|-------|-----------|----------|--------|--------|--------------|--------|
| 1 | Domain Models | F# | Low | High | None | **COMPLETE** |
| 2 | Validation Logic | F# | Medium | High | Phase 1 | **COMPLETE** |
| 3 | JSON Parser (simdjson) | C++ | Low | High | None | Pending |
| 4 | Lock-Free Ring Buffer | C++ | Medium | Medium | None | Pending |
| 5 | Order Book Calculations | F# | Medium | Medium | Phase 1 | **COMPLETE** |
| 6 | Order Book Engine | C++ | Medium | High | Phase 4 | Pending |
| 7 | Compression Engine | C++ | High | Medium | None | Pending |

### Phase 1: F# Domain Models - **COMPLETE**

**Status:** Implemented 2026-01-03

**Scope:**
- Create `Meridian.FSharp` project
- Define discriminated unions for market events
- Implement smart constructors for type safety
- C# interop through `[<CompiledName>]` attributes

**Deliverables:**
- `Domain/MarketEvents.fs` - Event type definitions with discriminated unions
- `Domain/Integrity.fs` - Integrity event types with smart constructors
- `Domain/Sides.fs` - Side and AggressorSide types with C# interop

**Additional Features Implemented:**
- `Pipeline/Transforms.fs` - Declarative stream processing for market events
- `Interop.fs` - C# wrapper classes and extension methods

### Phase 2: F# Validation - **COMPLETE**

**Status:** Implemented 2026-01-03

**Scope:**
- Implement Railway-Oriented validation
- Port existing validation logic to F#
- Create validation pipeline composition

**Deliverables:**
- `Validation/ValidationTypes.fs` - Core validation types and operators
- `Validation/TradeValidator.fs` - Trade validation with configurable rules
- `Validation/QuoteValidator.fs` - Quote validation with spread checks
- `Validation/ValidationPipeline.fs` - Composable validation pipelines

### Phase 3: C++ JSON Parser (Weeks 5-6)

**Scope:**
- Integrate simdjson library
- Create native parsing for market data JSON
- P/Invoke wrapper for C# consumption

**Deliverables:**
- `native/json_parser.cpp` - simdjson integration
- `Meridian.Native.dll` - Native library
- C# wrapper class

### Phase 4: C++ Lock-Free Ring Buffer (Weeks 7-8)

**Scope:**
- Implement SPSC/MPSC ring buffer
- Cache-line aligned for performance
- Replace Channel<T> in hot paths

**Deliverables:**
- `native/ring_buffer.hpp` - Lock-free implementation
- Performance benchmarks
- C# wrapper with IAsyncEnumerable support

### Phase 5: F# Order Book Calculations - **COMPLETE**

**Status:** Implemented 2026-01-03

**Scope:**
- Pure functional calculation library
- Mid-price, spread, imbalance, VWAP
- Aggressor inference logic

**Deliverables:**
- `Calculations/Spread.fs` - Spread calculations (absolute, bps, relative, effective)
- `Calculations/Imbalance.fs` - Order book imbalance and microprice
- `Calculations/Aggregations.fs` - VWAP, TWAP, volume breakdown, OHLCV bars

**Additional Features Implemented:**
- Trade arrival rate calculations
- Volume-weighted and price-weighted imbalance
- Rolling VWAP and imbalance averages
- Comprehensive statistics aggregation

### Phase 6: C++ Order Book Engine (Weeks 11-14)

**Scope:**
- High-performance order book implementation
- SIMD operations for price level search
- Lock-free updates with atomic operations

**Deliverables:**
- `native/orderbook.cpp` - Core implementation
- Snapshot export for C# consumption
- Integration tests

### Phase 7: C++ Compression Engine (Weeks 15-18)

**Scope:**
- Custom compression for market data
- Delta encoding for prices
- Run-length encoding for quantities

**Deliverables:**
- `native/compression.cpp`
- Streaming compression API
- Storage integration

---

## Project Structure

```
Meridian/
├── src/
│   ├── Meridian/              # C# main project (existing)
│   │   ├── Application/
│   │   ├── Domain/                       # Keep C# domain for compatibility
│   │   ├── Infrastructure/
│   │   └── Storage/
│   │
│   ├── Meridian.FSharp/       # NEW: F# domain library
│   │   ├── Domain/
│   │   │   ├── MarketEvents.fs
│   │   │   ├── Integrity.fs
│   │   │   └── Sides.fs
│   │   ├── Validation/
│   │   │   ├── TradeValidator.fs
│   │   │   ├── QuoteValidator.fs
│   │   │   └── ValidationPipeline.fs
│   │   ├── Calculations/
│   │   │   ├── Spread.fs
│   │   │   ├── Imbalance.fs
│   │   │   └── Aggregations.fs
│   │   └── Pipeline/
│   │       └── Transforms.fs
│   │
│   ├── Meridian.Native/       # NEW: C++ performance library
│   │   ├── include/
│   │   │   ├── orderbook.hpp
│   │   │   ├── ring_buffer.hpp
│   │   │   └── json_parser.hpp
│   │   ├── src/
│   │   │   ├── orderbook.cpp
│   │   │   ├── ring_buffer.cpp
│   │   │   ├── json_parser.cpp
│   │   │   └── compression.cpp
│   │   ├── CMakeLists.txt
│   │   └── vcpkg.json
│   │
│   ├── Meridian.Native.Interop/  # NEW: C# P/Invoke wrappers
│   │   ├── NativeOrderBook.cs
│   │   ├── NativeRingBuffer.cs
│   │   └── NativeJsonParser.cs
│   │
│   └── Meridian.Wpf/          # C# WPF desktop app (existing)
│
├── native/                               # Native build artifacts
│   ├── win-x64/
│   ├── win-arm64/
│   ├── linux-x64/
│   └── osx-arm64/
│
└── tests/
    ├── Meridian.Tests/        # C# tests (existing)
    ├── Meridian.FSharp.Tests/ # F# tests
    └── Meridian.Native.Tests/ # Native benchmarks
```

---

## Interoperability Guidelines

### F# to C# Interop

```fsharp
// Use CLIMutable for C# serialization compatibility
[<CLIMutable>]
type TradeDto = {
    Symbol: string
    Price: decimal
    Quantity: int
}

// Use CompiledName for C#-friendly names
[<CompiledName("ValidateTrade")>]
let validateTrade = // ...

// Expose Option<T> as Nullable<T> for C# consumers
[<Extension>]
type OptionExtensions =
    [<Extension>]
    static member ToNullable(opt: 'T option) : Nullable<'T> =
        match opt with
        | Some v -> Nullable v
        | None -> Nullable()
```

### C++ to C# Interop

```csharp
// Use SafeHandle for deterministic cleanup
public sealed class OrderBookHandle : SafeHandle
{
    public OrderBookHandle() : base(IntPtr.Zero, true) { }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        if (!IsInvalid)
        {
            NativeMethods.orderbook_destroy(handle);
            return true;
        }
        return false;
    }
}

// Use Span<T> for zero-copy data transfer
public unsafe void GetSnapshot(Span<BookLevel> bids, Span<BookLevel> asks)
{
    fixed (BookLevel* bidPtr = bids)
    fixed (BookLevel* askPtr = asks)
    {
        NativeMethods.orderbook_snapshot(_handle, bidPtr, askPtr, (nuint)bids.Length);
    }
}
```

---

## Performance Expectations

### Current C# Performance (Baseline)

| Metric | Current | Target |
|--------|---------|--------|
| Event throughput | 50K/sec | 500K/sec |
| Order book update latency | 100-500 μs | 1-10 μs |
| JSON parse throughput | 100 MB/s | 2 GB/s |
| P99 latency jitter | 5-20 ms | 50-100 μs |

### Expected Improvements by Component

| Component | Technology | Improvement Factor |
|-----------|------------|-------------------|
| JSON Parsing | simdjson (C++) | 10-20x |
| Order Book Updates | Lock-free C++ | 10-100x |
| Ring Buffer | SPSC C++ | 2-5x |
| Validation | F# (parallelizable) | 1.5-2x |
| Compression | Custom C++ | 10-20x |

---

## Risk Assessment

### Technical Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| F#/C# interop friction | Medium | Low | Use `[<CLIMutable>]`, careful API design |
| C++ memory safety issues | Medium | High | Use RAII, sanitizers, extensive testing |
| Build complexity increase | High | Medium | CMake + vcpkg, CI/CD automation |
| Developer unfamiliarity | Medium | Medium | Training, pair programming, documentation |

### Organizational Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Increased maintenance burden | Medium | Medium | Clear module boundaries, good docs |
| Hiring difficulty (F#/C++) | Medium | Low | Most code stays C#, specialists for perf |
| Longer onboarding time | Low | Low | Modular design, incremental adoption |

---

## Decision Criteria for Language Selection

Use this explicit rule when deciding which language to use for a new component:

1. **Use C++** when the component has hard sub-10μs latency requirements or is on a wire-format / lock-free hot path.
2. **Use F# only when all of the following are true:**
   - the subsystem is dominated by rules-heavy, side-effect-light transforms;
   - the logic can be expressed as deterministic input/output functions;
   - the boundary is explicit: contracts in C#, pure transform module in F#, orchestration/integration in C#;
   - the logic does not depend deeply on existing DI/service infrastructure.
3. **Use C# first** for host orchestration, provider integration, storage, UI, web, scheduling, retries, metrics, or other service-heavy workflows.

For the full repo-specific guidance, including the `Pipeline/Transforms.fs` classification and the F# pilot selection rule, see [F# Decision Rule for Meridian](../development/fsharp-decision-rule.md).

---

## Appendix A: Benchmark Methodology

All benchmarks should use BenchmarkDotNet with the following configuration:

```csharp
[Config(typeof(PerformanceConfig))]
public class OrderBookBenchmarks
{
    private class PerformanceConfig : ManualConfig
    {
        public PerformanceConfig()
        {
            AddJob(Job.ShortRun
                .WithRuntime(CoreRuntime.Core80)
                .WithPlatform(Platform.X64)
                .WithJit(Jit.RyuJit)
                .WithGcServer(true)
                .WithGcConcurrent(true));

            AddDiagnoser(MemoryDiagnoser.Default);
            AddDiagnoser(new EtwProfiler());
        }
    }

    [Benchmark(Baseline = true)]
    public void CSharpOrderBook() { /* ... */ }

    [Benchmark]
    public void NativeOrderBook() { /* ... */ }
}
```

---

## Appendix B: References

1. **simdjson** - https://github.com/simdjson/simdjson
2. **F# for Fun and Profit** - https://fsharpforfunandprofit.com/
3. **Lock-Free Programming** - https://preshing.com/20120612/an-introduction-to-lock-free-programming/
4. **Railway-Oriented Programming** - https://fsharpforfunandprofit.com/rop/

---

**Document Owner:** Architecture Team
**Review Cycle:** Quarterly
**See Also:** [Architecture](../architecture/overview.md) | [Production Status](../status/production-status.md) | [Why This Architecture](../architecture/why-this-architecture.md)
