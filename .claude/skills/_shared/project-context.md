# Meridian — Shared Project Context

> **Canonical reference.** This file is the single source of truth for project statistics, provider inventory, key abstractions with file paths, and storage design. Both `meridian-brainstorm` and `meridian-code-review` skills reference this file. Update here first; do not maintain separate copies.
>
> **Last verified:** 2026-03-19
> **Refresh command:** `python3 build/scripts/ai-repo-updater.py audit`

---

## Project Statistics

| Metric | Count |
|--------|-------|
| Total Source Files | 868 (856 C# + 12 F#) |
| C# Files | 856 |
| F# Files | 12 (6 modules + 6 interop) |
| Test Projects | 4 |
| Test Files | 261 |
| Test Methods | ~4,135 |
| Documentation Files | 171 |
| Main Projects | 22 (+ 4 test + 1 benchmark) |
| CI/CD Workflows | 33 |
| Makefile Targets | 96 |
| Provider Implementations | 5 streaming, 11 historical |
| Symbol Search Providers | 5 |
| API Route Constants | 309 |
| Endpoint Files | 39 |

---

## Solution Layout (22 main projects)

```
Meridian.sln
├── src/
│   ├── Meridian/                   # Entry point (Program.cs, UiServer.cs)
│   ├── Meridian.Application/       # App services, pipeline, commands, config
│   ├── Meridian.Contracts/         # DTOs + interfaces (LEAF — no upstream deps)
│   ├── Meridian.Core/              # Config, exceptions, logging, serialization
│   ├── Meridian.Domain/            # Collectors, events, models
│   ├── Meridian.FSharp/            # F# 8.0 domain models, validation, calculations
│   ├── Meridian.Infrastructure/    # Provider adapters, resilience, HTTP
│   ├── Meridian.ProviderSdk/       # IMarketDataClient, IHistoricalDataProvider, base SDK
│   ├── Meridian.Storage/           # WAL, sinks, packaging, export, maintenance
│   ├── Meridian.Ui/                # Web dashboard (ASP.NET)
│   ├── Meridian.Ui.Services/       # Shared UI services (platform-neutral)
│   ├── Meridian.Ui.Shared/         # Shared endpoint handlers (platform-neutral)
│   ├── Meridian.Wpf/               # WPF desktop app (recommended Windows client)
│   ├── Meridian.Backtesting/       # Backtest engine, fill models, portfolio metrics
│   ├── Meridian.Backtesting.Sdk/   # IBacktestStrategy, IBacktestContext, BacktestResult SDK
│   ├── Meridian.Execution/         # IOrderGateway, PaperTradingGateway, broker adapters (ADR-015)
│   ├── Meridian.Execution.Sdk/     # IExecutionGateway, IOrderManager, IPositionTracker (broker SDK)
│   ├── Meridian.Ledger/            # Double-entry accounting ledger for P&L tracking
│   ├── Meridian.Mcp/               # MCP server host (Program.cs, tools, prompts)
│   ├── Meridian.McpServer/         # MCP server tools and resources (alternative host)
│   ├── Meridian.Risk/              # IRiskRule, CompositeRiskValidator, pre-trade risk checks
│   └── Meridian.Strategies/        # IStrategyLifecycle, StrategyRunStore, promotion workflow (ADR-016)
├── tests/
│   ├── Meridian.Tests/             # ~261 test files, ~4135 test methods
│   ├── Meridian.FSharp.Tests/      # F# unit tests (expecto/xUnit)
│   ├── Meridian.Wpf.Tests/         # WPF service tests (Windows only)
│   └── Meridian.Ui.Tests/          # UI service tests (Windows only)
└── benchmarks/
    └── Meridian.Benchmarks/        # BenchmarkDotNet performance benchmarks
```

---

## Dependency Graph

**Allowed:**
```
Wpf host        → Ui.Services, Contracts, Core
ViewModels      → Ui.Services, Contracts, Core
Ui.Services     → Contracts, Core, Application
Ui.Shared       → Ui.Services, Contracts (platform-neutral only)
Application     → Contracts, Core, Domain, Infrastructure, Storage
Infrastructure  → Contracts, Core, ProviderSdk
ProviderSdk     → Contracts only
FSharp          → Contracts only
Contracts       → nothing (leaf project)
Storage         → Contracts, Core
Domain          → Contracts, Core
Web host        → Ui.Services, Ui.Shared, Contracts
Backtesting     → Contracts, Core, Backtesting.Sdk, Ledger
Backtesting.Sdk → Contracts, Core, Ledger
Execution       → Contracts, Core, Execution.Sdk  (ADR-015)
Execution.Sdk   → Contracts only  (broker SDK leaf)
Ledger          → nothing (zero-dependency leaf project)
Risk            → Contracts, Core, Execution.Sdk
Strategies      → Contracts, Core, Backtesting.Sdk, Execution.Sdk  (ADR-016)
Mcp / McpServer → Application, Contracts, Core  (MCP server layer)
```

**Forbidden (flag as CRITICAL in reviews):**
```
Ui.Services     → Wpf host types          (reverse dependency)
Ui.Services     → WPF-only APIs           (platform leak)
Ui.Shared       → WPF-only APIs           (platform leak)
Ui.Shared       → UWP/WinRT APIs          (platform leak + deprecated)
Any project     → Meridian.Uwp  (UWP fully removed)
ProviderSdk     → anything except Contracts
Execution.Sdk   → anything except Contracts
Ledger          → any other Meridian project
FSharp          → anything except Contracts
Contracts       → Infrastructure           (dependency inversion)
Core/Domain     → Infrastructure           (dependency inversion)
Backtesting.*   → Execution.*              (backtesting is simulation-only; no live concepts)
Execution.*     → Backtesting.*            (execution must not depend on simulation infra)
Strategies.*    → any concrete Execution.* type  (strategies depend only on IOrderGateway/IExecutionContext)
DataCollection  → Strategies.* or Execution.*   (data layer is infrastructure, not strategy-aware)
```

---

## Key Abstractions & File Paths

### IMarketDataClient (Streaming)
**File:** `src/Meridian.ProviderSdk/IMarketDataClient.cs`

```csharp
public interface IMarketDataClient : IAsyncDisposable
{
    bool IsEnabled { get; }
    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    int SubscribeMarketDepth(SymbolConfig cfg);
    void UnsubscribeMarketDepth(int subscriptionId);
    int SubscribeTrades(SymbolConfig cfg);
    void UnsubscribeTrades(int subscriptionId);
}
```

### IHistoricalDataProvider (Backfill)
**File:** `src/Meridian.Infrastructure/Adapters/Core/IHistoricalDataProvider.cs`

```csharp
public interface IHistoricalDataProvider
{
    string Name { get; }
    string DisplayName { get; }
    HistoricalDataCapabilities Capabilities { get; }
    int Priority { get; }
    Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
        string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default);
}
```

### IStorageSink (Persistence)
**File:** `src/Meridian.Storage/Interfaces/IStorageSink.cs`

```csharp
public interface IStorageSink : IAsyncDisposable
{
    ValueTask WriteAsync(MarketEvent evt, CancellationToken ct = default);
    Task FlushAsync(CancellationToken ct = default);
}
```

### EventPipeline (Hot-Path Coordinator)
**File:** `src/Meridian.Application/Pipeline/EventPipeline.cs`

- BoundedChannel capacity: 50,000 events
- Backpressure policy: `BoundedChannelFullMode.DropOldest`
- Must use `EventPipelinePolicy.*.CreateChannel<T>()` — not `Channel.CreateBounded<T>()` directly (ADR-013)
- WAL written before in-memory queue; flushed on shutdown

### WriteAheadLog (WAL Durability)
**File:** `src/Meridian.Storage/Archival/WriteAheadLog.cs`

- Entries must be flushed (not just written) before acknowledging ingest
- `FlushAsync(ct)` must be called before disposal
- Uses `AtomicFileWriter` for crash-safe writes — never write directly to the file
- ADR-007 governs WAL behavior

### AtomicFileWriter (Crash-Safe Writes)
**File:** `src/Meridian.Storage/Archival/AtomicFileWriter.cs`

- Write to temp file, then rename — guarantees no partial records
- All `IStorageSink` implementations must route through this, not direct `FileStream`

### JsonlStorageSink / ParquetStorageSink
**Files:** `src/Meridian.Storage/Sinks/JsonlStorageSink.cs`, `ParquetStorageSink.cs`

- Must use `AtomicFileWriter` — not raw `File.WriteAllText` or `FileStream`
- Must implement `IFlushable` for orderly shutdown
- Serialization via source-generated `JsonSerializerContext` — never reflection-based (ADR-014)

### BindableBase (MVVM Base)
**File:** `src/Meridian.Wpf/ViewModels/BindableBase.cs`

```csharp
public abstract class BindableBase : INotifyPropertyChanged
{
    protected bool SetProperty<T>(ref T field, T value,
        [CallerMemberName] string? propertyName = null) { ... }
    protected void RaisePropertyChanged([CallerMemberName] string? name = null) { ... }
}
```

### RelayCommand (ICommand)
**File:** `src/Meridian.Wpf/ViewModels/` (or `Ui.Services` shared equivalent)

### MarketDataJsonContext (Source-Generated JSON)
**File:** `src/Meridian.Core/Serialization/MarketDataJsonContext.cs`

- All serialization must reference this context — no `JsonSerializer.Serialize(obj)` without context
- Add new types with `[JsonSerializable(typeof(T))]` on this context

### IOrderGateway (Order Routing)
**File:** `src/Meridian.Execution/Interfaces/IOrderGateway.cs`

- Broker-agnostic interface for submitting, cancelling, and monitoring orders (ADR-015)
- All broker adapters (PaperTradingGateway, IB, Alpaca) implement this
- Strategies depend only on `IOrderGateway` — not any concrete broker type

### IExecutionGateway (Broker Adapter SDK)
**File:** `src/Meridian.Execution.Sdk/IExecutionGateway.cs`

- Lower-level broker adapter contract in `Meridian.Execution.Sdk` (leaf project)
- Exposes `SubmitOrderAsync`, `CancelOrderAsync`, `StreamExecutionReportsAsync`

### IExecutionContext (Live-Strategy Context)
**File:** `src/Meridian.Execution/Interfaces/IExecutionContext.cs`

- Live-mode analogue of `IBacktestContext` — provides a strategy with unified feed + portfolio + gateway
- Enables broker-agnostic strategies: paper → live is a config change, not a code change

### IRiskValidator / IRiskRule (Pre-Trade Risk)
**File:** `src/Meridian.Risk/IRiskValidator.cs`, `src/Meridian.Risk/IRiskRule.cs`

- `CompositeRiskValidator` evaluates all registered `IRiskRule` implementations in sequence
- Rejects the order on first rule failure; logs rejection reason with structured logging

### IStrategyLifecycle (Strategy Management)
**File:** `src/Meridian.Strategies/Interfaces/IStrategyLifecycle.cs`

- Strategy lifecycle contract: register, start, pause, stop, promote (paper → live)
- `StrategyRunStore` at `src/Meridian.Strategies/Storage/StrategyRunStore.cs` archives runs

### Ledger (Double-Entry Accounting)
**File:** `src/Meridian.Ledger/Ledger.cs`

- Zero-dependency double-entry ledger used by Backtesting for P&L tracking
- `IReadOnlyLedger` exposes `Journal`, `GetEntries`, `GetBalance`, `TrialBalance`
- Aliased as `BacktestLedger` in `Meridian.Backtesting` global usings to avoid name collision

---

## Provider Inventory

### Streaming Providers (IMarketDataClient)

| Provider | Class | File Path |
|----------|-------|-----------|
| Alpaca | `AlpacaMarketDataClient` | `src/Meridian.Infrastructure/Adapters/Alpaca/` |
| Polygon | `PolygonMarketDataClient` | `src/Meridian.Infrastructure/Adapters/Polygon/` |
| Interactive Brokers | `IBMarketDataClient` | `src/Meridian.Infrastructure/Adapters/InteractiveBrokers/` |
| StockSharp | `StockSharpMarketDataClient` | `src/Meridian.Infrastructure/Adapters/StockSharp/` |
| NYSE | `NYSEDataSource` | `src/Meridian.Infrastructure/Adapters/NYSE/` |
| Failover | `FailoverAwareMarketDataClient` | `src/Meridian.Infrastructure/Adapters/Failover/` |
| NoOp | `NoOpMarketDataClient` | `src/Meridian.Infrastructure/NoOpMarketDataClient.cs` |

### Historical Providers (IHistoricalDataProvider)

| Provider | Free Tier | Rate Limits |
|----------|-----------|-------------|
| Alpaca | Yes (with account) | 200/min |
| Polygon | Limited | Varies by plan |
| Tiingo | Yes | 500/hour |
| Yahoo Finance | Yes | Unofficial |
| Stooq | Yes | Low |
| StockSharp | Yes | Varies |
| Finnhub | Yes | 60/min |
| Alpha Vantage | Yes | 5/min |
| Nasdaq Data Link | Limited | Varies |
| Interactive Brokers | Yes (with account) | IB pacing rules |
| Twelve Data | Limited free tier | 8/min free |

---

## Storage Architecture

### File Layout
```
data/
├── live/                    # Hot tier (real-time)
│   └── {provider}/{date}/
│       ├── {symbol}_trades.jsonl.gz
│       └── {symbol}_quotes.jsonl.gz
├── historical/              # Backfill data
│   └── {provider}/{date}/{symbol}_bars.jsonl
├── _wal/                    # Write-ahead log
└── _archive/                # Cold tier (Parquet)
    └── parquet/
```

### Naming Conventions (Storage Org Modes)
| Mode | Pattern | Default |
|------|---------|---------|
| BySymbol | `{root}/{symbol}/{type}/{date}.jsonl` | ✓ Recommended |
| ByDate | `{root}/{date}/{symbol}/{type}.jsonl` | |
| ByType | `{root}/{type}/{symbol}/{date}.jsonl` | |
| Flat | `{root}/{symbol}_{type}_{date}.jsonl` | |

### Tiered Storage
| Tier | Purpose | Default Retention |
|------|---------|-------------------|
| Hot | Recent data, fast access | 7 days |
| Warm | Compressed older data | 30 days |
| Cold | Archive (Parquet) | Indefinite |

---

## Architecture Decision Records (Quick Reference)

| ADR | Decision | Enforcement |
|-----|----------|-------------|
| ADR-001 | Provider abstraction via interfaces | `[ImplementsAdr("ADR-001")]` on all providers |
| ADR-004 | Async streaming via `IAsyncEnumerable<T>` | Flag any `IEnumerable<T>` return on hot paths |
| ADR-006 | Domain events: sealed record with static factories | Flag mutable event types |
| ADR-007 | WAL + pipeline durability | `AtomicFileWriter` required for all sink writes |
| ADR-008 | JSONL + Parquet simultaneous writes | `CompositeSink` for dual-format output |
| ADR-009 | F# type-safe domain with C# interop | Handle `FSharpOption<T>` properly at boundary |
| ADR-013 | Bounded channel with `DropOldest` policy | `EventPipelinePolicy.*.CreateChannel<T>()` only |
| ADR-014 | JSON source generators — no reflection | `MarketDataJsonContext` on all `JsonSerializer` calls |
| ADR-015 | Strategy execution contract: `IOrderGateway` + `IExecutionContext` | Strategies code to interfaces only; no concrete broker types |
| ADR-016 | Four-pillar architecture: DataCollection, Backtesting, Execution, Strategies | Enforce cross-pillar dependency rules; see forbidden dependency list |

---

## Naming & Coding Conventions

### General C# / Cross-Layer Rules

| Rule | Good | Bad |
|------|------|-----|
| Async suffix | `LoadDataAsync` | `LoadData` |
| CancellationToken name | `ct` or `cancellationToken` | `token`, `cts` |
| Private fields | `_fieldName` | `fieldName`, `m_field` |
| Structured logging | `_logger.LogInfo("Got {Count}", n)` | `_logger.LogInfo($"Got {n}")` |
| Sealed classes | `public sealed class Foo` | `public class Foo` (unless designed for inheritance) |
| Exception types | `throw new DataProviderException(...)` | `throw new Exception(...)` |
| JSON serialization | `JsonSerializer.Serialize(obj, MyContext.Default.MyType)` | `JsonSerializer.Serialize(obj)` |
| IOptions for hot config | `IOptionsMonitor<T>` for runtime-changeable | `IOptions<T>` only for truly static settings |
| Central packages | No `Version=` in `<PackageReference>` | `<PackageReference Include="Foo" Version="1.0" />` |

### Domain Model Naming Standard (F# + Contracts layer)

> **Full spec:** [`docs/ai/claude/CLAUDE.domain-naming.md`](../../../docs/ai/claude/CLAUDE.domain-naming.md)

The financial domain model and security-master layers follow a stricter naming standard to ensure
names are predictable, stable, and mappable to storage/API contracts. Key rules:

| Concept Class | Required Pattern | Examples |
|---|---|---|
| Identifier types | End in `Id` | `SecurityId`, `CorpActId`, `OptChainId` |
| Entity types | Short singular noun | `Security`, `Issuer`, `CorpAct`, `OptChain` |
| Definition records (term sheets) | End in `Def` | `BondDef`, `EquityDef`, `OptDef`, `FutDef` |
| Classification unions | `Class`, `Family`, `Kind`, `Cat`, `Stat` suffix | `AssetClass`, `IdentifierKind`, `CorpActStat` |
| Trait records (cross-cutting economics) | End in `Tr` | `OwnTr`, `IncTr`, `ConvTr`, `RedTr` |
| Link / join records | End in `Lnk` | `SecIssLnk`, `SecExchLnk`, `CorpActSecLnk` |
| Boolean fields | Begin with `Is` or `Has` | `IsCallable`, `HasVoting`, `IsPrimary` |
| Date fields (new F# code) | End with `Dt` | `MaturityDt`, `IssueDt`, `ExpiryDt` |
| Amount fields | End with `Amt` | `NotionalAmt`, `FaceAmt`, `GrossAmt` |
| Rate fields | End with `Rate` | `CpnRate`, `DivRate`, `FloorRate` |
| Price fields | End with `Px` | `CallPx`, `ConvPx`, `RedPx` |

**Vocabulary roots (Meridian-specific):** Use full words for primary entity names (`Security`,
`Identifier`, `AssetClass`) and abbreviated forms only in compound names that would exceed ~20
characters (`CorpAct`, `OptChain`, `BondDef`). Never introduce an abbreviated synonym next to an
established full-word type name.

**Anti-patterns:** Reject `Data`, `Info`, `Object`, `Model`, `Manager`, `Container`, `Record` as
decorative suffixes. Reject boolean fields without `Is`/`Has`. Reject `Db`, `Api`, `Json` prefixes
on core domain types.

---

## F# Interop Rules (C# consumers)

- `FSharpOption<T>` in C#: use `.IsSome` / `.Value`, not null checks
- Discriminated unions: match ALL cases — the `_ =>` catch-all hides new DU cases
- F# record types are immutable — no property assignment; use `with` expressions from F#
- Never add property setters to F# record types
- `[AllowNull]` needed at nullable boundaries

---

## WPF MVVM Role Assignments

| Concern | Location |
|---------|----------|
| UI state (loading, error text) | ViewModel property |
| Domain data (counts, symbols) | ViewModel property |
| Commands (start/stop/export) | ViewModel `RelayCommand` |
| Timer for periodic refresh | ViewModel `PeriodicTimer` — NOT `DispatcherTimer` in code-behind |
| UI thread marshal | View code-behind (thin) |
| Brush/resource caching | View static field |
| Service dependencies | ViewModel constructor injection — NOT Page constructor |
| Business logic | ViewModel or `Ui.Services` — NEVER in `.xaml.cs` |

**UWP is fully removed.** Flag any `using Windows.*` or `using Meridian.Uwp.*` as CRITICAL.
