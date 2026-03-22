# Meridian — Architecture Reference

**Last Updated:** 2026-03-21

> **Last verified:** 2026-03-16 | **Refresh:** `python3 build/scripts/ai-repo-updater.py audit`
>
> Read this file when you need deep context on project structure, dependency rules, or specific subsystem design to give accurate review feedback. For current statistics, provider list, and key abstraction file paths, see [`../_shared/project-context.md`](../_shared/project-context.md) which is the single authoritative source.

## Table of Contents

1. [Project Overview](#1-project-overview)
2. [Solution Layout](#2-solution-layout)
3. [Dependency Graph](#3-dependency-graph)
4. [Core Abstractions](#4-core-abstractions)
5. [Pipeline Architecture](#5-pipeline-architecture)
6. [WPF Desktop App Layer](#6-wpf-desktop-app-layer)
7. [Provider & Backfill Architecture](#7-provider--backfill-architecture)
8. [F# Domain Models](#8-f-domain-models)
9. [Testing Architecture](#9-testing-architecture)
10. [Naming & Coding Conventions](#10-naming--coding-conventions)
11. [ADR Quick Reference](#11-adr-quick-reference)
12. [Storage & Pipeline Integrity Rules](#12-storage--pipeline-integrity-rules)

---

## 1. Project Overview

Meridian is a .NET 9 / C# 13 application (with F# 8.0 domain models) for capturing real-time market microstructure data (trades, quotes, L2 order books) from multiple providers. It also supports historical backfill from 10+ providers with automatic failover. It persists data via a backpressured pipeline to JSONL/Parquet storage with WAL durability. Two UI surfaces: a WPF desktop app (recommended) and a web dashboard; they share services through a layered architecture. **UWP was fully removed.**

**Performance contract**: millisecond-accurate timestamping of market events. UI work must never starve the data pipeline.

**Scale**: 779 source files (769 C#, 14 F#), 266 test files (~4,135 test methods across 4 test projects), 163 documentation files.

---

## 2. Solution Layout

```
Meridian.sln
├── .claude/                             # AI assistant settings
├── .github/                             # CI/CD workflows (17), Dependabot, AI prompts
├── benchmarks/
│   └── Meridian.Benchmarks/  # BenchmarkDotNet performance benchmarks
├── build/                               # Build tooling (Python, Node.js, .NET generators)
├── config/                              # Configuration files (appsettings.json, samples)
├── deploy/                              # Docker, systemd, monitoring configs
├── docs/                                # 104 documentation files
│   ├── adr/                             # Architecture Decision Records
│   ├── ai/                              # AI assistant guides (F#, providers, storage, testing)
│   ├── architecture/                    # System architecture, C4 diagrams, domains
│   ├── development/                     # GitHub Actions docs, dev guides
│   ├── getting-started/                 # Setup guides
│   ├── integrations/                    # Lean engine integration
│   └── operations/                      # Operator runbook, production deploy
├── src/
│   ├── Meridian/             # Core application (entry point)
│   │   ├── Domain/                      # Business logic, collectors, events, models
│   │   │   ├── Collectors/              # Provider-specific collector implementations
│   │   │   ├── Events/                  # Market event types, integrity events
│   │   │   └── Models/                  # Trade, Quote, Bar, OrderBook, etc.
│   │   ├── Infrastructure/              # Provider implementations
│   │   │   ├── Alpaca/                  # Alpaca WebSocket + REST
│   │   │   ├── InteractiveBrokers/      # IB Gateway TWS integration
│   │   │   ├── NYSE/                    # NYSE direct feed
│   │   │   ├── Polygon/                 # Polygon aggregates + streaming
│   │   │   ├── StockSharp/              # Multi-exchange connectors
│   │   │   └── Storage/                 # JSONL, Parquet, WAL implementations
│   │   ├── Application/                 # Startup, config, DI, services
│   │   └── Integrations/
│   │       └── Lean/                    # QuantConnect Lean backtesting integration
│   │
│   ├── Meridian.FSharp/      # F# 8.0 domain models (17 files)
│   │                                    # Discriminated unions, record types for
│   │                                    # type-safe market data representation
│   │
│   ├── Meridian.Contracts/   # Shared DTOs and contracts (LEAF project)
│   │                                    # IMarketDataClient, IStorageSink, etc.
│   │                                    # NO upstream dependencies allowed
│   │
│   ├── Meridian.ProviderSdk/ # Provider SDK interfaces & base classes
│   │                                    # Rate limit tracking, reconnection helpers
│   │                                    # Depends on Contracts only
│   │
│   ├── Meridian.Ui/          # Web dashboard (ASP.NET)
│   │
│   ├── Meridian.Ui.Shared/   # Shared UI endpoint handlers
│   │                                    # Must be platform-neutral (no WPF/UWP APIs)
│   │
│   ├── Meridian.Ui.Services/ # Shared UI service abstractions
│   │                                    # CollectorService, status, backfill logic
│   │                                    # Must be platform-neutral
│   │
│   ├── Meridian.Wpf/         # WPF desktop app (RECOMMENDED)
│   │   ├── App.xaml / App.xaml.cs
│   │   ├── Views/                       # XAML pages and windows
│   │   │   ├── DashboardPage.xaml(.cs)
│   │   │   ├── SettingsPage.xaml(.cs)
│   │   │   └── SymbolsPage.xaml(.cs)
│   │   ├── ViewModels/
│   │   │   ├── BindableBase.cs          # INotifyPropertyChanged base
│   │   │   ├── RelayCommand.cs          # ICommand implementation
│   │   │   └── [Domain]ViewModel.cs
│   │   └── Services/                    # WPF-specific service wrappers
│   │
│
├── tests/                               # 266 test files, ~4,135 test methods
│   ├── Meridian.Tests/       # Main test project (unit + integration)
│   ├── Meridian.FSharp.Tests/# F# domain tests
│   ├── Meridian.Wpf.Tests/   # WPF service tests (Windows only, 324 tests)
│   └── Meridian.Ui.Tests/    # UI service tests (Windows only, 927 tests)
│
├── CLAUDE.md                            # Main AI assistant guide
├── Directory.Build.props                # Shared build properties
├── Directory.Packages.props             # Central package management
├── Makefile                             # Build automation (96 targets)
├── global.json                          # SDK version pinning
└── Meridian.sln
```

---

## 3. Dependency Graph

**Allowed:**
```
Wpf host       → Ui.Services, Contracts, Core.Models
ViewModels     → Ui.Services, Contracts, Core.Models
Ui.Services    → Contracts, Core.Models, Pipeline
Ui.Shared      → Ui.Services, Contracts (platform-neutral only)
Pipeline       → Contracts, Infrastructure
ProviderSdk    → Contracts (only)
FSharp         → Contracts (only)
Contracts      → nothing (leaf project)
Infrastructure → Contracts, ProviderSdk
Web host       → Ui.Services, Ui.Shared, Contracts
```

**Forbidden (review violations to flag):**
```
Ui.Services    → Wpf host types         (reverse dependency)
Ui.Services    → Uwp host types         (reverse dependency)
Ui.Shared      → WPF-only APIs          (platform leak)
Ui.Shared      → UWP/WinRT APIs         (platform leak — UWP is legacy)
Wpf            ↔ Web                    (host-to-host coupling)
Wpf            ↔ Uwp                   (host-to-host coupling)
Core/Contracts → Infrastructure         (dependency inversion violation)
ProviderSdk    → anything except Contracts  (keep SDK thin)
FSharp         → anything except Contracts  (domain models are pure)
Any shared proj → UWP-specific APIs     (UWP is deprecated for new dev)
```

---

## 4. Core Abstractions

### IMarketDataClient
```csharp
public interface IMarketDataClient : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct = default);
    Task SubscribeAsync(IEnumerable<string> symbols, CancellationToken ct = default);
    IAsyncEnumerable<MarketEvent> StreamEventsAsync(CancellationToken ct = default);
}
```

### IStorageSink
```csharp
public interface IStorageSink : IAsyncDisposable
{
    ValueTask WriteAsync(MarketEvent evt, CancellationToken ct = default);
    Task FlushAsync(CancellationToken ct = default);
}
```

### EventPipeline
The central coordinator. Uses `System.Threading.Channels.BoundedChannel<T>` internally.
- Default capacity: 50,000 events
- Backpressure policy: `BoundedChannelFullMode.DropOldest`
- Batch flush: configurable size + timeout
- WAL: written before in-memory queue, flushed on shutdown

### BindableBase
```csharp
public abstract class BindableBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        RaisePropertyChanged(propertyName);
        return true;
    }

    protected void RaisePropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

### RelayCommand
```csharp
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) { ... }
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
```

---

## 5. Pipeline Architecture

```
Provider(s)  →  IMarketDataClient
                     │
                     ▼ (IAsyncEnumerable<MarketEvent>)
              EventPipeline.IngestLoop
                     │
          BoundedChannel<MarketEvent>  ←── backpressure (50K capacity, DropOldest)
                     │
              BatchFlushLoop
                     │
            ┌────────┴────────┐
            ▼                 ▼
         WAL File        IStorageSink(s)
       (durability)    (JSONL / Parquet)
```

**Hot-path rules** (enforce in reviews):
- No allocations per-event beyond the `MarketEvent` struct itself
- No LINQ in the ingest or flush loop
- No logging at Debug/Trace in production hot paths — use counters instead
- `Span<T>` / `Memory<T>` for buffer operations
- No reflection-based JSON serialization — source generators only (ADR-014)

---

## 6. WPF Desktop App Layer

### MVVM Role Assignments

| Concern | Correct Location |
|---|---|
| UI state (loading, error messages) | ViewModel property |
| Domain data (event counts, symbols) | ViewModel property |
| Start/stop collector | ViewModel command → Ui.Services |
| Timer for periodic refresh | ViewModel (`PeriodicTimer`) |
| Dispatcher marshal for UI update | View code-behind (or ViewModel with injected dispatcher) |
| Named element access (`x:Name`) | Only for `InitializeComponent()` and event wireup |
| Brush/resource caching | View static field |
| Service DI | ViewModel constructor, not Page constructor |

### DashboardPage — Known Technical Debt

The primary refactoring target. Current violations:
- Event rate calculation (`_previousPublished` diff) in code-behind
- `DispatcherTimer` managed in code-behind
- 5 service dependencies injected into the Page constructor
- `DashboardActivityItem` / `SymbolPerformanceItem` defined inside the Page class
- Direct element writes: `PublishedCount.Text = FormatNumber(status.Published)`

### ViewModel Extraction Pattern

```csharp
// BEFORE (code-behind violation)
private void OnLiveStatusReceived(CollectorStatus status)
{
    var rate = (status.Published - _previousPublished) / _refreshInterval.TotalSeconds;
    PublishedCount.Text = FormatNumber(status.Published);
    EventRateLabel.Text = $"{rate:F1}/s";
    _previousPublished = status.Published;
}

// AFTER (ViewModel)
public class DashboardViewModel : BindableBase
{
    private long _previousPublished;
    private string _publishedCount = "0";
    private string _eventRate = "0.0/s";

    public string PublishedCount
    {
        get => _publishedCount;
        private set => SetProperty(ref _publishedCount, value);
    }

    public string EventRate
    {
        get => _eventRate;
        private set => SetProperty(ref _eventRate, value);
    }

    public void UpdateStatus(CollectorStatus status, double elapsedSeconds)
    {
        var rate = (status.Published - _previousPublished) / elapsedSeconds;
        PublishedCount = FormatNumber(status.Published);
        EventRate = $"{rate:F1}/s";
        _previousPublished = status.Published;
    }
}
```

---

## 7. Provider & Backfill Architecture

### Provider Implementation Pattern

All providers implement `IMarketDataClient` from the `ProviderSdk` project. Key requirements:

```csharp
public class AlpacaClient : IMarketDataClient
{
    // Must handle reconnection on WebSocket disconnect
    // Must respect CancellationToken in all methods
    // Must use ProviderRateLimitTracker for API throttling
    // StreamEventsAsync must yield via IAsyncEnumerable (ADR-004)
    // DisposeAsync must: cancel CTS, flush, dispose WebSocket/HTTP clients
}
```

### Backfill Architecture

```
BackfillOrchestrator
    │
    ├── ProviderPriority config (appsettings.json)
    │   e.g., ["alpaca", "polygon", "tiingo", "yahoo"]
    │
    ├── For each symbol + date range:
    │   ├── Try provider[0] → success? → write to storage
    │   ├── Catch transient error → try provider[1]
    │   ├── ... continue through failover chain
    │   └── All failed → log error, skip symbol
    │
    ├── Rate limit tracking per provider
    │   ├── Alpaca: 200/min
    │   ├── Polygon: varies by plan
    │   ├── Tiingo: 500/hour
    │   ├── Yahoo Finance: unofficial (be conservative)
    │   ├── Finnhub: 60/min
    │   └── Alpha Vantage: 5/min
    │
    └── Progress reporting via /api/backfill/* endpoints
```

### Hot Config Reload

The project supports `--watch-config` for live configuration changes:
- Symbol subscriptions can be added/removed without restart
- Use `IOptionsMonitor<T>` for settings that change at runtime
- `IOptions<T>` is cached at startup — only use for truly static settings
- ADR-021: UI refresh interval is configurable via `IOptions<UiSettings>`

---

## 8. F# Domain Models

`Meridian.FSharp` contains 17 F# 8.0 files with type-safe domain representations:

**Key interop rules for C# consumers:**
- F# `option<T>` becomes `FSharpOption<T>` in C# — use `FSharpOption.get_IsSome()` / `FSharpOption.get_Value()`, not null checks
- F# discriminated unions require pattern matching via the generated `Is*` properties and `Item` fields
- F# record types are immutable — `with` expressions create new instances, don't try property assignment from C#
- Nullable reference type annotations matter at the boundary — add `[AllowNull]` where needed

---

## 9. Testing Architecture

**85 test files** across unit and integration test projects.

### Test Organization
```
tests/
├── Unit/                    # Fast, isolated tests
│   ├── Pipeline/            # Channel, batching, backpressure tests
│   ├── Providers/           # Mock provider behavior tests
│   ├── Storage/             # Serialization, file format tests
│   ├── ViewModels/          # ViewModel state + command tests
│   └── Domain/              # Model validation, event type tests
└── Integration/             # Slower, may need external resources
    ├── Provider/            # Real API endpoint tests (require keys)
    └── Pipeline/            # End-to-end pipeline tests
```

### Test Conventions
- Framework: xUnit (C#), expecto or xUnit (F#)
- Naming: `MethodUnderTest_Scenario_ExpectedBehavior`
- Structure: Arrange-Act-Assert
- Async: `async Task` (never `async void`)
- Cancellation: Tests should pass `CancellationToken` with timeout
- Mocking: Prefer simple test doubles for project interfaces; mocking frameworks for external dependencies
- Pipeline tests: Use deterministic scheduling, not `Task.Delay`
- Disposables: Always `using` / `await using` for test subjects implementing `IDisposable` / `IAsyncDisposable`

---

## 10. Naming & Coding Conventions

| Rule | Example |
|---|---|
| Async methods end with `Async` | `LoadDataAsync`, `StopCollectorAsync` |
| CancellationToken param name | `ct` (short form) or `cancellationToken` |
| Private fields | `_fieldName` |
| Interfaces | `IMarketDataClient` |
| Structured logging | `_logger.LogInformation("Published {Count} events", count)` |
| No string interpolation in logs | ~~`_logger.LogInformation($"Published {count}")`~~ |
| Use custom exceptions | `throw new PipelineException(...)` not `throw new Exception(...)` |
| Prefer `Span<T>` / `Memory<T>` | For buffer slicing and parsing |
| JSON source generators | `[JsonSerializable(typeof(T))]` — no reflection serialization |
| Hot reload settings | `IOptionsMonitor<T>` for runtime-changeable settings |
| UWP code | Legacy only — no new features, no WinRT in shared projects |

---

## 11. ADR Quick Reference

| ADR | Decision |
|---|---|
| ADR-001 | Provider abstraction via `IMarketDataClient` and `IHistoricalDataProvider` interfaces |
| ADR-004 | Async streaming via `IAsyncEnumerable<T>` for all market data feeds |
| ADR-006 | Domain events: sealed record wrapper with static factories |
| ADR-007 | WAL + pipeline durability: write WAL before in-memory enqueue; `AtomicFileWriter` for all sink writes |
| ADR-008 | JSONL + Parquet simultaneous writes via `CompositeSink` |
| ADR-009 | F# type-safe domain with C# interop via `FSharpOption<T>` and generated interop layer |
| ADR-013 | All internal queues must use `BoundedChannel` with `DropOldest` policy via `EventPipelinePolicy.*.CreateChannel<T>()` |
| ADR-014 | JSON serialization uses source generators (`[JsonSerializable]` on `MarketDataJsonContext`) — no reflection |
| ADR-017 | WPF layer must not contain business logic; all logic goes in `Ui.Services` or `Core` |
| ADR-021 | UI refresh interval is configurable via `IOptions<UiSettings>` — no hardcoded timers |

---

## 12. Storage & Pipeline Integrity Rules

This section covers correctness invariants that the Storage & Pipeline Integrity lens (Lens 7) enforces.

### Write Safety — AtomicFileWriter

All `IStorageSink` implementations MUST route file writes through `AtomicFileWriter`:

```csharp
// WRONG — direct write produces partial JSONL on crash
await using var fs = new FileStream(path, FileMode.Append);
await JsonSerializer.SerializeAsync(fs, evt, ctx);

// CORRECT — AtomicFileWriter writes to temp then renames
await _atomicWriter.WriteAsync(path, async stream =>
    await JsonSerializer.SerializeAsync(stream, evt, MarketDataJsonContext.Default.MarketEvent, ct));
```

### WAL Write Ordering

The WAL entry MUST be written (and flushed) BEFORE the event is enqueued into the bounded channel:

```csharp
// CORRECT order — WAL first, then channel
await _wal.WriteAsync(evt, ct);          // step 1: durable
await _wal.FlushAsync(ct);               // step 2: fsync
await _channel.Writer.WriteAsync(evt, ct); // step 3: in-memory
```

Reversing steps 1 and 3 means events are lost on crash between enqueue and WAL write.

### Shutdown Flush Ordering

On `DisposeAsync`, the correct order is:

```csharp
public async ValueTask DisposeAsync()
{
    _cts.Cancel();                          // 1. signal stop
    _channel.Writer.Complete();             // 2. no more writes
    await _flushLoop;                       // 3. drain remaining events
    await _sink.FlushAsync(CancellationToken.None); // 4. flush sink
    await _wal.FlushAsync(CancellationToken.None);  // 5. flush WAL
    await _wal.DisposeAsync();              // 6. close WAL
    await _sink.DisposeAsync();             // 7. close sink
}
```

Skipping step 4 or 5 causes in-flight event loss on graceful shutdown.

### IFlushable Contract

All `IStorageSink` implementations must implement `IFlushable` (from `Core/Services/IFlushable.cs`):

```csharp
public interface IFlushable
{
    Task FlushAsync(CancellationToken ct = default);
}
```

This allows the shutdown coordinator (`GracefulShutdownService`) to call `FlushAsync` in the correct order on all sinks.

### Storage Sink Registration

Sinks must be decorated with `[StorageSink]` for auto-discovery:

```csharp
[StorageSink("jsonl", description: "JSONL line-per-event storage")]
public sealed class JsonlStorageSink : IStorageSink, IFlushable { ... }
```

Without this attribute, `StorageSinkRegistry` will not discover the sink in DI.
