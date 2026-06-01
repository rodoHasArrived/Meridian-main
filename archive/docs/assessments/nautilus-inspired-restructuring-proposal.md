# Structural Improvement Proposal: nautilus_trader-Inspired Patterns

**Date:** 2026-03-01
**Status:** Partially Implemented — Last Reviewed 2026-03-11
**Author:** Architecture Review
**Inspired By:** [nautechsystems/nautilus_trader](https://github.com/nautechsystems/nautilus_trader)

---

## Executive Summary

This proposal identified **7 structural improvements** and **5 procedural/code enhancements** for the Meridian repository, inspired by organizational patterns in the nautilus_trader trading system. All changes are **backward-compatible** — existing public APIs, namespaces, and build targets remain stable. The goal is to improve developer ergonomics, reduce coupling, and make the provider subsystem more modular and self-documenting.

---

## Implementation Status Summary

As of 2026-03-11, the following proposals have been evaluated against the live codebase:

| # | Proposal | Status | Notes |
|---|----------|--------|-------|
| 1.1 | Unified Per-Provider Directories | ✅ Implemented | Reorganized under `Adapters/` by vendor |
| 1.2 | Provider Template Scaffold | ⚠️ Partial | `ProviderTemplate.cs` factory exists; no `_Template/` scaffold dir |
| 1.3 | Co-located Provider Configuration | ❌ Not Started | Configs remain in `Application/Config/` |
| 1.4 | Explicit Parsing Layer Per Provider | ❌ Not Started | Parsing is still inline within provider clients |
| 1.5 | Per-Provider Factory Classes | ❌ Not Started | Central `ProviderFactory.cs` unchanged |
| 1.6 | Consolidated Domain Enums | ✅ Implemented | 14 enums in `Contracts/Domain/Enums/` |
| 1.7 | Persistence Read/Write/Transform Separation | ⚠️ Partial | Functional separation via `Archival/`, `Replay/`, `Export/` — no literal dirs |
| 2.1 | Component Lifecycle FSM Base Class | ❌ Not Started | No `ComponentBase`; abstract base classes exist without FSM |
| 2.2 | Provider-Local Common Types | ⚠️ Partial | Some providers have internal files (StockSharp `Converters/`, IB `IBApiLimits.cs`) |
| 2.3 | Module-Scoped Message Types | ⚠️ Partial | `Application/Commands/` files exist; no pipeline/backfill command types |
| 2.4 | Credential Isolation at Provider Boundary | ⚠️ Partial | Hybrid: `ICredentialResolver` in `ProviderFactory.cs` + `ProviderCredentialResolver` in Application |
| 2.5 | ArchUnitNET Dependency Rules | ❌ Not Started | No architecture boundary tests in test suite |

---

## Table of Contents

1. [Structural Changes](#1-structural-changes)
   - [1.1 Unified Per-Provider Directories](#11-unified-per-provider-directories)
   - [1.2 Provider Template Scaffold](#12-provider-template-scaffold)
   - [1.3 Co-located Provider Configuration](#13-co-located-provider-configuration)
   - [1.4 Explicit Parsing Layer Per Provider](#14-explicit-parsing-layer-per-provider)
   - [1.5 Per-Provider Factory Classes](#15-per-provider-factory-classes)
   - [1.6 Consolidated Domain Enums](#16-consolidated-domain-enums)
   - [1.7 Persistence Read/Write/Transform Separation](#17-persistence-readwritetransform-separation)
2. [Code & Procedural Enhancements](#2-code--procedural-enhancements)
   - [2.1 Component Lifecycle FSM Base Class](#21-component-lifecycle-fsm-base-class)
   - [2.2 Provider-Local Common Types](#22-provider-local-common-types)
   - [2.3 Module-Scoped Message Types](#23-module-scoped-message-types)
   - [2.4 Credential Isolation at Provider Boundary](#24-credential-isolation-at-provider-boundary)
   - [2.5 ArchUnitNET Dependency Rules](#25-archunitnet-dependency-rules)
3. [Migration Strategy](#3-migration-strategy)
4. [Risk Assessment](#4-risk-assessment)
5. [Appendix: Side-by-Side Comparison](#appendix-side-by-side-comparison)

---

## 1. Structural Changes

### 1.1 Unified Per-Provider Directories

> **Status: ✅ Implemented**

**Original problem:** Provider code for the same vendor (e.g., Alpaca) was split across three separate directory trees (`Streaming/Alpaca/`, `Historical/Alpaca/`, and a flat `SymbolSearch/` folder), with configuration in a fourth location (`Core/Config/AlpacaOptions.cs`).

**Current state:** Providers have been reorganized into unified vendor directories under `Infrastructure/Adapters/`. Each vendor now owns a single directory containing streaming, historical, and symbol-search implementations:

```
Infrastructure/Adapters/
├── Core/                              # shared base classes and factory
│   ├── Backfill/                     # job queue, worker service, priority queue
│   ├── GapAnalysis/                  # gap detection and repair
│   ├── RateLimiting/                 # rate limiter and tracker
│   ├── SymbolResolution/             # symbol resolver interface
│   ├── ProviderFactory.cs
│   ├── ProviderRegistry.cs
│   ├── WebSocketProviderBase.cs
│   └── ProviderTemplate.cs
├── Alpaca/
│   ├── AlpacaMarketDataClient.cs
│   ├── AlpacaHistoricalDataProvider.cs
│   └── AlpacaSymbolSearchProviderRefactored.cs
├── Polygon/
│   ├── PolygonMarketDataClient.cs
│   ├── PolygonHistoricalDataProvider.cs
│   └── PolygonSymbolSearchProvider.cs
├── InteractiveBrokers/
│   ├── IBMarketDataClient.cs
│   ├── IBHistoricalDataProvider.cs
│   ├── IBSimulationClient.cs
│   ├── ContractFactory.cs
│   ├── IBConnectionManager.cs
│   ├── EnhancedIBConnectionManager.cs
│   ├── IBCallbackRouter.cs
│   └── IBApiLimits.cs
├── NYSE/
│   ├── NYSEDataSource.cs
│   ├── NYSEOptions.cs               # co-located config (already was)
│   └── NYSEServiceExtensions.cs
├── StockSharp/
│   ├── StockSharpMarketDataClient.cs
│   ├── StockSharpHistoricalDataProvider.cs
│   ├── StockSharpSymbolSearchProvider.cs
│   ├── StockSharpConnectorFactory.cs
│   ├── StockSharpConnectorCapabilities.cs
│   └── Converters/
│       ├── MessageConverter.cs
│       └── SecurityConverter.cs
├── Finnhub/
│   ├── FinnhubHistoricalDataProvider.cs
│   └── FinnhubSymbolSearchProviderRefactored.cs
├── Tiingo/
│   └── TiingoHistoricalDataProvider.cs
├── YahooFinance/
│   └── YahooFinanceHistoricalDataProvider.cs
├── Stooq/
│   └── StooqHistoricalDataProvider.cs
├── AlphaVantage/
│   └── AlphaVantageHistoricalDataProvider.cs
├── NasdaqDataLink/
│   └── NasdaqDataLinkHistoricalDataProvider.cs
├── OpenFigi/
│   ├── OpenFigiClient.cs
│   └── OpenFigiSymbolResolver.cs
├── Failover/
│   ├── FailoverAwareMarketDataClient.cs
│   ├── StreamingFailoverRegistry.cs
│   └── StreamingFailoverService.cs
└── (no Shared/ — cross-cutting infra lives in Core/)
```

**Remaining gaps vs. proposal:**
- Per-provider `*Config.cs` and `*Factory.cs` files have **not** been created (see 1.3 and 1.5).
- The `AlpacaSymbolSearchProviderRefactored.cs` and `FinnhubSymbolSearchProviderRefactored.cs` still carry the legacy `Refactored` suffix (see Section 5.1, Quick Win #1).
- Namespaces now use `Meridian.Infrastructure.Adapters.<Vendor>` rather than the proposed `Providers.<Vendor>` — this is the canonical namespace going forward.

**Namespace impact:** Internal references use `Meridian.Infrastructure.Adapters.*`. Public `IMarketDataClient` / `IHistoricalDataProvider` interfaces remain unchanged.

---

### 1.2 Provider Template Scaffold

> **Status: ⚠️ Partially Implemented**

**Problem:** The existing `ProviderTemplate.cs` is a metadata record, not a code scaffold. When a developer wants to add a new provider, they must read documentation and manually study existing providers to know which files to create.

**nautilus_trader pattern:** A `_template/` directory contains skeleton files (`core.py`, `data.py`, `execution.py`, `providers.py`) that define the exact file structure every adapter must have.

**Current state:** A `ProviderTemplate.cs` file exists in `Adapters/Core/` as a factory/metadata record that generates provider template data (`ProviderTemplate` record, `ProviderTemplateFactory`, `ProviderRateLimitProfile`). This handles programmatic template creation but is **not** a file-system scaffold that developers can copy to bootstrap a new provider.

**Still needed:** A `_Template/` directory with skeleton C# files:

```
Infrastructure/Adapters/_Template/
├── README.md                          # Step-by-step guide
├── TemplateConfig.cs                  # Configuration skeleton
├── TemplateMarketDataClient.cs        # IMarketDataClient skeleton (streaming)
├── TemplateHistoricalDataProvider.cs  # IHistoricalDataProvider skeleton (backfill)
├── TemplateSymbolSearchProvider.cs    # ISymbolSearchProvider skeleton (search)
└── TemplateFactory.cs                 # Factory skeleton
```

Each skeleton file contains:
- Required attributes (`[DataSource]`, `[ImplementsAdr]`)
- Interface methods with `NotImplementedException` stubs
- Structured logging patterns
- `CancellationToken` on all async methods
- Comments marking required vs. optional implementations

**Example skeleton** (`TemplateMarketDataClient.cs`):

```csharp
// Copy this file to your provider directory and rename.
// Replace "Template" with your provider name throughout.
// Delete capabilities you don't support (e.g., remove depth methods if no L2).

[DataSource("template")]
[ImplementsAdr("ADR-001", "Streaming data provider contract")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
public sealed class TemplateMarketDataClient : IMarketDataClient
{
    private readonly ILogger _log;

    // TODO: Add provider-specific dependencies (HttpClient, config, etc.)

    public bool IsEnabled => true; // TODO: Wire to configuration

    public Task ConnectAsync(CancellationToken ct = default)
        => throw new NotImplementedException("TODO: Implement connection logic");

    public Task DisconnectAsync(CancellationToken ct = default)
        => throw new NotImplementedException("TODO: Implement disconnection logic");

    // ... remaining interface methods
}
```

**Benefits:**
- Self-documenting contract: the template _is_ the specification
- Reduces provider implementation time from "read docs + study examples" to "copy + fill in"
- Enforces consistent patterns (attributes, logging, cancellation) from day one
- The `README.md` replaces the need to hunt through `docs/development/provider-implementation.md`

---

### 1.3 Co-located Provider Configuration

> **Status: ❌ Not Started**

**Problem:** Provider configuration classes are scattered:

| Provider | Config location |
|----------|----------------|
| Alpaca | `Core/Config/AlpacaOptions.cs` (Application layer) |
| StockSharp | `Core/Config/StockSharpConfig.cs` |
| NYSE | `Streaming/NYSE/NYSEOptions.cs` (co-located, inconsistent) |
| Backfill providers | `Application/Config/BackfillConfig.cs` (all 10 in one file) |

NYSE already follows the co-located pattern. The rest don't.

**nautilus_trader pattern:** Each adapter owns a `config.py` with frozen dataclasses for that adapter's configuration. The configuration _source of truth_ lives next to the code that consumes it.

**Proposed change:** Move each provider's configuration into its own provider directory:

```
Providers/Alpaca/AlpacaConfig.cs        # Contains: AlpacaStreamingConfig, AlpacaBackfillConfig
Providers/Polygon/PolygonConfig.cs      # Contains: PolygonStreamingConfig, PolygonBackfillConfig
Providers/InteractiveBrokers/IBConfig.cs
...
```

The global `BackfillConfig.cs` retains the **aggregated** shape for `appsettings.json` deserialization but delegates to per-provider records:

```csharp
// In Application/Config/BackfillConfig.cs (slimmed down):
public sealed record BackfillProvidersConfig(
    AlpacaBackfillConfig? Alpaca,
    PolygonBackfillConfig? Polygon,
    // ... other providers
);

// In Providers/Alpaca/AlpacaConfig.cs (co-located):
public sealed record AlpacaBackfillConfig(
    bool Enabled = true,
    string? KeyId = null,
    string? SecretKey = null,
    string Feed = "iex",
    string Adjustment = "all",
    int Priority = 5,
    int RateLimitPerMinute = 200);
```

**Benefits:**
- Provider authors find configuration next to implementation
- Eliminates scrolling through a multi-hundred-line BackfillConfig to find one provider's settings
- Consistent with NYSE pattern already in use

---

### 1.4 Explicit Parsing Layer Per Provider

> **Status: ❌ Not Started**

**Problem:** Wire-format parsing (JSON deserialization, field mapping, type conversion) is currently embedded inside provider client classes. For example, `AlpacaMarketDataClient.cs` handles both WebSocket connection management and JSON message parsing in the same class.

**nautilus_trader pattern:** Every adapter has a `parsing/` subdirectory with dedicated files per concern (`parsing/data.py`, `parsing/instruments.py`, `parsing/execution.py`).

**Proposed change:** For complex providers (IB, Polygon, StockSharp), extract parsing into named subdirectories:

```
Providers/InteractiveBrokers/
├── Parsing/
│   ├── IBDataParser.cs          # Tick/quote/depth wire format → domain events
│   ├── IBContractParser.cs      # Contract definitions → SymbolConfig
│   └── IBErrorParser.cs         # Error codes → typed exceptions
```

```
Providers/Polygon/
├── Parsing/
│   ├── PolygonMessageParser.cs  # WebSocket JSON → domain events
│   └── PolygonRestParser.cs     # REST responses → HistoricalBar
```

For simpler providers (Stooq, AlphaVantage) with minimal parsing, a separate directory isn't warranted — keep parsing inline.

**Benefits:**
- Client classes focus on connection lifecycle and subscription management
- Parsing logic is independently testable (unit tests with raw JSON fixtures)
- Changes to provider wire format don't touch connection management code

---

### 1.5 Per-Provider Factory Classes

> **Status: ❌ Not Started**

**Problem:** The centralized `ProviderFactory.cs` contains creation logic for **all** providers. Adding a new backfill provider means modifying this file, adding type aliases, and risking merge conflicts with other provider work.

**Current state:** `ProviderFactory.cs` remains the single central factory in `Adapters/Core/`. It exposes `ICredentialResolver` interface and `EnvironmentCredentialResolver` class alongside factory methods for each provider. No per-provider factory classes have been extracted.

**nautilus_trader pattern:** Each adapter has a `factories.py` that knows how to construct that adapter's components. The composition root just calls each factory.

**Proposed change:** Extract per-provider factory classes from `ProviderFactory.cs`:

```csharp
// In Providers/Alpaca/AlpacaFactory.cs
[ImplementsAdr("ADR-001", "Alpaca provider factory")]
public static class AlpacaFactory
{
    public static AlpacaHistoricalDataProvider? CreateBackfillProvider(
        AlpacaBackfillConfig? cfg,
        ICredentialResolver credentials,
        ILogger log)
    {
        if (!(cfg?.Enabled ?? true)) return null;
        var (keyId, secretKey) = credentials.ResolveAlpacaCredentials(cfg?.KeyId, cfg?.SecretKey);
        if (string.IsNullOrEmpty(keyId) || string.IsNullOrEmpty(secretKey)) return null;
        return new AlpacaHistoricalDataProvider(keyId, secretKey, cfg?.Feed ?? "iex", ...);
    }

    public static AlpacaSymbolSearchProvider? CreateSearchProvider(
        AlpacaBackfillConfig? cfg,
        ICredentialResolver credentials,
        ILogger log)
    { ... }
}
```

The central `ProviderFactory.cs` becomes a thin orchestrator:

```csharp
public IReadOnlyList<IHistoricalDataProvider> CreateBackfillProviders()
{
    var providers = new List<IHistoricalDataProvider>();
    TryAdd(providers, () => AlpacaFactory.CreateBackfillProvider(_config.Backfill?.Providers?.Alpaca, _creds, _log));
    TryAdd(providers, () => PolygonFactory.CreateBackfillProvider(_config.Backfill?.Providers?.Polygon, _creds, _log));
    // ... one line per provider
    return providers.OrderBy(p => p.Priority).ToList();
}
```

**Benefits:**
- Adding a new provider doesn't touch existing factory code
- Each factory class is testable in isolation
- Merge conflicts reduced — parallel provider work is in separate files
- Central factory becomes a thin composition list

---

### 1.6 Consolidated Domain Enums

> **Status: ✅ Implemented**

**Original problem:** Domain enumerations were spread across multiple files and namespaces.

**Current state:** All trading domain enums are consolidated in `Contracts/Domain/Enums/` with individual files per enum:

```
Contracts/Domain/Enums/
├── AggressorSide.cs
├── CanonicalTradeCondition.cs
├── ConnectionStatus.cs
├── DepthIntegrityKind.cs
├── DepthOperation.cs
├── InstrumentType.cs
├── IntegritySeverity.cs
├── LiquidityProfile.cs
├── MarketEventTier.cs
├── MarketEventType.cs
├── MarketState.cs
├── OptionRight.cs
├── OptionStyle.cs
└── OrderBookSide.cs
```

`DataSourceKind.cs` and its associated `DataSourceKindConverter.cs` remain in `Core/Config/` (deliberately kept together because the converter has associated logic). This is consistent with the proposal's guidance to "keep individual enum files if they have associated logic."

---

### 1.7 Persistence Read/Write/Transform Separation

> **Status: ⚠️ Partially Implemented**

**Problem:** The Storage project organizes by technical concept (Sinks, Archival, Export) but mixes read and write concerns within some services.

**nautilus_trader pattern:** Persistence has explicit named layers: `loaders.py` (read), `writer.py` (write), `wranglers.py` (transform).

**Current state:** The Storage project has achieved functional separation without the literal `Read/`, `Write/`, `Transform/` directory names:

```
Storage/
├── Archival/       # Write layer (WriteAheadLog, AtomicFileWriter, ArchivalStorageService)
├── Replay/         # Read layer (JsonlReplayer, MemoryMappedJsonlReader)
├── Sinks/          # Write sinks (JsonlStorageSink, ParquetStorageSink, CompositeSink, CatalogSyncSink)
├── Export/         # Transform layer (AnalysisExportService, Arrow/Parquet/Xlsx formats)
├── Services/       # Mixed concerns — 15 services (quality, lineage, catalog, lifecycle, etc.)
├── Maintenance/    # Maintenance scheduling
├── Packaging/      # Package creation and validation
└── Policies/       # Storage policies
```

The `Read/Write/Transform` labels map approximately to `Replay/Archival+Sinks/Export`. The main gap is that `Storage/Services/` remains a large grab-bag with 15 mixed-concern classes.

**Alternative (lower risk):** Keep existing directories but add clear `Read/`, `Write/`, `Transform/` XML doc tags and a `Storage/README.md` mapping file that documents which class handles which concern.

---

## 2. Code & Procedural Enhancements

### 2.1 Component Lifecycle FSM Base Class

> **Status: ❌ Not Started**

**Problem:** Provider components use implicit lifecycle management via `IHostedService` and `IAsyncDisposable`. There's no standard way to query a component's state (is it starting? connected? degraded? stopping?).

**Current state:** No `ComponentBase` class exists. Providers inherit from abstract base classes (`BaseHistoricalDataProvider`, `BaseSymbolSearchProvider`, `WebSocketProviderBase`) that manage initialization but provide no formal finite-state-machine or queryable lifecycle state.

**nautilus_trader pattern:** Every component extends `Component`, which embeds a finite state machine with explicit states: `PRE_INITIALIZED → READY → RUNNING → DEGRADED → STOPPED → DISPOSED`. State transitions are guarded and logged.

**Proposed enhancement:** Create a `ComponentBase` class in `Core/`:
// Core/ComponentBase.cs
public abstract class ComponentBase : IAsyncDisposable
{
    public ComponentState State { get; private set; } = ComponentState.Created;

    protected async Task TransitionToAsync(ComponentState target, CancellationToken ct)
    {
        ValidateTransition(State, target);
        var previous = State;
        State = target;
        _log.LogInformation("{Component} transitioned {From} → {To}", GetType().Name, previous, target);
        await OnStateChangedAsync(previous, target, ct);
    }

    protected virtual Task OnStateChangedAsync(ComponentState from, ComponentState to, CancellationToken ct)
        => Task.CompletedTask;

    private static void ValidateTransition(ComponentState from, ComponentState to) { /* guard table */ }
}

public enum ComponentState
{
    Created,
    Initializing,
    Ready,
    Starting,
    Running,
    Degraded,
    Stopping,
    Stopped,
    Disposed,
    Faulted
}
```

**Adoption:** Streaming providers inherit from `ComponentBase`. This gives the monitoring dashboard observable lifecycle states and enables health checks like "is the Alpaca client in Degraded state?".

**Benefits:**
- Observable component states for dashboards and health checks
- Prevents invalid state transitions (e.g., calling `Connect` on a `Disposed` component)
- Structured lifecycle logging (consistent "X transitioned Running → Stopping" messages)
- Aligns with ADR-012 (Monitoring & Alerting Pipeline)

---

### 2.2 Provider-Local Common Types

> **Status: ⚠️ Partially Implemented**

**Problem:** Provider-specific constants and types (like IB contract types, Polygon message enums, Alpaca feed names) often end up in shared namespaces or as magic strings scattered through client code.

**Current state:** Two providers have provider-local internal files:
- `InteractiveBrokers/IBApiLimits.cs` — IB-specific API limits and constants (matches the intent)
- `StockSharp/StockSharpConnectorCapabilities.cs` — capabilities metadata
- `StockSharp/Converters/` — wire-format conversion types

Other providers (Alpaca, Polygon, Finnhub) still rely on inline magic strings and shared utilities. A consistent `*Constants.cs` pattern has not been adopted across all providers.

**nautilus_trader pattern:** Each adapter has a `common.py` (or `common/` directory) for adapter-internal shared types. These types are **not exported** to the rest of the system.

**Proposed enhancement:** Add a `Common.cs` or `Constants.cs` file to each provider directory:

```csharp
// Providers/InteractiveBrokers/IBConstants.cs
internal static class IBConstants
{
    public const int DefaultPort = 7496;
    public const int GatewayPort = 4001;
    public const int MaxSubscriptionsPerConnection = 100;
    public const string MarketDataType_RealTime = "1";
    public const string MarketDataType_Frozen = "2";
    // ...
}
```

Mark these `internal` so they don't leak into the public API surface.

**Benefits:**
- Magic numbers and strings are named and co-located
- `internal` visibility enforces that provider-specific constants don't couple other code to a specific provider
- Easier to audit for hard-coded values

---

### 2.3 Module-Scoped Message Types

> **Status: ⚠️ Partially Implemented**

**Problem:** The event pipeline uses a single `MarketEvent` wrapper with polymorphic payloads (per ADR-006). While this is architecturally sound, pipeline-specific command/request types (like "subscribe to this symbol" or "trigger a backfill") are not always clearly separated from domain events.

**Current state:** The `Application/Commands/` directory contains several typed command classes covering CLI-level operations:
- `PackageCommands.cs`
- `DiagnosticsCommands.cs`
- `ConfigCommands.cs`
- `SymbolCommands.cs`

However, **pipeline-internal** command types (`SubscribeSymbolCommand`, `UnsubscribeSymbolCommand`) and **backfill-module** commands (`RunBackfillCommand`, `CancelBackfillCommand`) have **not** been created. Backfill operations use parameter-passing through `BackfillRequest` records rather than dispatched command objects.

**nautilus_trader pattern:** Each module defines its own message types in a `messages.py` file (e.g., `data/messages.py` defines `SubscribeData`, `UnsubscribeData`, `RequestData`).

**Proposed enhancement:** Create explicit command types per subsystem:

```csharp
// Application/Pipeline/PipelineCommands.cs
public sealed record SubscribeSymbolCommand(string Symbol, DataFeedType FeedType);
public sealed record UnsubscribeSymbolCommand(string Symbol);

// Application/Backfill/BackfillCommands.cs
public sealed record RunBackfillCommand(string Symbol, DateOnly From, DateOnly To, string? Provider);
public sealed record CancelBackfillCommand(string JobId);
```

These replace ad-hoc parameter bags or string-based dispatching with typed, self-documenting command objects.

**Benefits:**
- Commands are discoverable via "find all references" on the type
- Enables command validation at the type level
- Improves testability — commands can be asserted without parsing strings

---

### 2.4 Credential Isolation at Provider Boundary

> **Status: ⚠️ Partially Implemented**

**Problem:** The current `ICredentialResolver` has methods for every provider (`ResolveAlpacaCredentials`, `ResolvePolygonCredentials`, etc.). Adding a new provider requires modifying this shared interface.

**Current state:** A hybrid approach exists:
1. `ICredentialResolver` / `EnvironmentCredentialResolver` in `Adapters/Core/ProviderFactory.cs` — monolithic per-provider resolution methods, used at factory creation time.
2. `ProviderCredentialResolver` in `Application/Config/Credentials/` — a second centralized resolver with the same per-provider methods but richer configuration support.
3. `ICredentialStore` in `Application/Credentials/` — a newer unified interface (`GetCredentialAsync`, `RefreshCredentialAsync`) that abstracts over vault, environment variables, and config.

The `ICredentialStore` interface represents forward progress toward the proposed pattern, but the per-provider environment-variable-name knowledge has not been moved into individual provider directories.

**nautilus_trader pattern:** Each adapter has its own `credentials.py` or loads environment variables in its `config.py`. Credentials are sourced at the adapter boundary and never passed downstream.

**Proposed enhancement:** Replace the monolithic `ICredentialResolver` with per-provider credential resolution in each factory:

```csharp
// In Providers/Alpaca/AlpacaFactory.cs
private static (string? KeyId, string? SecretKey) ResolveCredentials(AlpacaBackfillConfig? cfg)
{
    var keyId = cfg?.KeyId
        ?? Environment.GetEnvironmentVariable("ALPACA__KEYID")
        ?? Environment.GetEnvironmentVariable("ALPACA_KEY_ID");
    var secretKey = cfg?.SecretKey
        ?? Environment.GetEnvironmentVariable("ALPACA__SECRETKEY")
        ?? Environment.GetEnvironmentVariable("ALPACA_SECRET_KEY");
    return (keyId, secretKey);
}
```

Keep a lightweight `ISecretProvider` interface for vault integration, but move the environment-variable-name knowledge into each provider's own code.

**Benefits:**
- Adding a new provider doesn't require modifying a shared interface
- Environment variable names are co-located with the provider that uses them
- The credential resolution contract is self-contained per provider

---

### 2.5 ArchUnitNET Dependency Rules

> **Status: ❌ Not Started**

**Problem:** Layer boundary violations are documented in `repository-organization-guide.md` and `layer-boundaries.md` but not enforced programmatically. Violations are caught only during code review.

**Current state:** No architecture boundary tests exist in any test project. Known layer violations identified during analysis remain in place (see Section 5.3).

**nautilus_trader pattern:** While nautilus_trader doesn't use ArchUnit specifically, their strict module boundaries (no cross-adapter imports, no persistence importing from adapters) are enforced by Python's import system and CI checks.

**Proposed enhancement:** Add an ArchUnitNET test class that enforces documented dependency rules. Update the `ProviderFactory` type reference in the loader to use `Meridian.Infrastructure.Adapters.Core.ProviderFactory` (reflecting the completed `Adapters/` migration from 1.1):

```csharp
// tests/Meridian.Tests/Architecture/LayerBoundaryTests.cs
public class LayerBoundaryTests
{
    private static readonly Architecture Architecture =
        new ArchLoader().LoadAssemblies(
            typeof(Meridian.Core.Config.AppConfig).Assembly,
            typeof(Meridian.Domain.Collectors.TradeDataCollector).Assembly,
            typeof(Meridian.Infrastructure.Adapters.Core.ProviderFactory).Assembly
        ).Build();

    [Fact]
    public void Domain_Should_Not_Reference_Infrastructure()
    {
        Types().That().ResideInNamespace("Meridian.Domain")
            .Should().NotDependOnAny(
                Types().That().ResideInNamespace("Meridian.Infrastructure"))
            .Check(Architecture);
    }

    [Fact]
    public void Core_Should_Not_Reference_Application()
    {
        Types().That().ResideInNamespace("Meridian.Core")
            .Should().NotDependOnAny(
                Types().That().ResideInNamespace("Meridian.Application"))
            .Check(Architecture);
    }

    [Fact]
    public void Providers_Should_Not_Cross_Reference()
    {
        // Alpaca should not reference Polygon internals, etc.
        Types().That().ResideInNamespace("Meridian.Infrastructure.Adapters.Alpaca")
            .Should().NotDependOnAny(
                Types().That().ResideInNamespace("Meridian.Infrastructure.Adapters.Polygon"))
            .Check(Architecture);
    }
}
```

**Benefits:**
- Layer violations caught at build/test time, not code review
- Documents architecture-as-code alongside the tests
- Prevents gradual erosion of boundaries over time
- Minimal setup — one NuGet package, one test file

---

## 3. Migration Strategy

The phases below have been updated to reflect current implementation status as of 2026-03-11.

### Phase 1: Foundation (Low Risk) — Partially Complete

| Change | Effort | Risk | Status |
|--------|--------|------|--------|
| 1.2 Provider Template Scaffold | Small | None (additive) | ⚠️ Partial |
| 2.2 Provider-Local Common Types | Small | None (additive) | ⚠️ Partial |
| 2.5 ArchUnitNET tests | Small | None (additive) | ❌ Not started |
| 1.6 Consolidated Domain Enums | Medium | Low (moves, no API change) | ✅ Done |

### Phase 2: Provider Restructuring (Medium Risk) — Partially Complete

| Change | Effort | Risk | Status |
|--------|--------|------|--------|
| 1.1 Unified Per-Provider Directories | Large | Medium (namespace changes) | ✅ Done |
| 1.3 Co-located Provider Configuration | Medium | Low (records can stay API-compatible) | ❌ Not started |
| 1.5 Per-Provider Factory Classes | Medium | Low (internal refactoring) | ❌ Not started |

### Phase 3: Enhanced Patterns (Medium Risk) — Not Started

| Change | Effort | Risk | Status |
|--------|--------|------|--------|
| 1.4 Explicit Parsing Layer | Medium | Low (extract, don't rewrite) | ❌ Not started |
| 2.1 Component Lifecycle FSM | Medium | Medium (base class change) | ❌ Not started |
| 2.3 Module-Scoped Message Types | Small | Low (additive) | ⚠️ Partial |
| 2.4 Credential Isolation | Small | Low (internal refactoring) | ⚠️ Partial |

### Phase 4: Storage Reorganization (Lower Priority) — Not Started

| Change | Effort | Risk | Status |
|--------|--------|------|--------|
| 1.7 Persistence Read/Write/Transform | Medium | Medium (file moves) | ⚠️ Partial |

### Migration Approach Per Phase

1. **Create the target directory structure** (empty directories)
2. **Move files one provider at a time** using `git mv` to preserve history
3. **Update namespaces** (use IDE refactoring tools)
4. **Update `using` directives** across consuming projects
5. **Run full build + test suite** after each provider move
6. **Update CLAUDE.md** and `repository-organization-guide.md` to reflect new structure

---

## 4. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Namespace changes break consuming code | Medium | High | Use IDE refactoring; run full test suite after each move |
| Merge conflicts with in-flight PRs | Medium | Medium | Coordinate timing; do restructuring in a single focused sprint |
| Build breaks from moved files | Low | High | Move one provider at a time; verify build between each |
| Test failures from changed namespaces | Low | Medium | Tests reference interfaces (not concrete types); impact limited |
| Documentation becomes outdated | High | Low | Update CLAUDE.md and org guide as part of restructuring PR |

---

## Appendix: Side-by-Side Comparison

### Before vs. After: Finding "Everything About Alpaca"

**Before (4 separate locations):**
```
src/Meridian.Application/Config/AlpacaOptions.cs            # config (still here)
src/Meridian.Infrastructure/Providers/Streaming/Alpaca/     # streaming (old)
src/Meridian.Infrastructure/Providers/Historical/Alpaca/    # backfill (old)
src/Meridian.Infrastructure/Providers/SymbolSearch/Alpaca*  # search (old)
src/Meridian.Infrastructure/Providers/Core/ProviderFactory.cs  # factory (old)
```

**After (1 location for implementation, config still separate):**
```
src/Meridian.Infrastructure/Adapters/Alpaca/
├── AlpacaMarketDataClient.cs
├── AlpacaHistoricalDataProvider.cs
└── AlpacaSymbolSearchProviderRefactored.cs   # rename pending (5.1)

src/Meridian.Application/Config/AlpacaOptions.cs            # config (1.3 not yet done)
src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs # factory (1.5 not yet done)
```

### Before vs. After: Adding a New Provider

**Before (8 steps across multiple locations):**
1. Create streaming client in `Providers/Streaming/NewProvider/`
2. Create historical provider in `Providers/Historical/NewProvider/`
3. Create search provider in `Providers/SymbolSearch/`
4. Add config class in `Core/Config/` or `Application/Config/`
5. Modify `ProviderFactory.cs` to add creation logic
6. Modify `ICredentialResolver` to add credential resolution
7. Register in `ServiceCompositionRoot.cs`
8. Read `docs/development/provider-implementation.md` for guidance

**Current (5 steps, partially simplified):**
1. Create a vendor directory under `Adapters/NewProvider/`
2. Add streaming, historical, and search files within that directory
3. Add config in `Application/Config/`
4. Modify `ProviderFactory.cs` to add creation + credential logic
5. Register in `ServiceCompositionRoot.cs`

**Goal (3 steps, once 1.2/1.3/1.5 are completed):**
1. Copy `Adapters/_Template/` to `Adapters/NewProvider/`
2. Rename classes and implement methods
3. Register in `ServiceCompositionRoot.cs`

---

### nautilus_trader Patterns Not Adopted (And Why)

| Pattern | Reason for Exclusion |
|---------|---------------------|
| `execution.py` per adapter | Meridian doesn't handle order execution |
| Singleton metaclass for catalog | .NET DI container handles singleton lifecycle |
| Cython/Rust FFI layer | Not applicable to .NET; already uses source generators for perf |
| `msgbus/` module | The bounded-channel `EventPipeline` serves this role (ADR-013) |
| Frozen config base class | .NET records with `init` setters achieve similar immutability |
| `actors` pattern | Not applicable; the system uses `IHostedService` + DI |

---

## 5. Structural Issues — Status Update (2026-03-11)

The following concrete issues were identified in the original analysis. Status reflects the codebase as of 2026-03-11.

### 5.1 Provider Organization Issues

| Issue | Severity | Status | Current Location |
|-------|----------|--------|-----------------|
| **Orphaned `BackfillProgressTracker.cs`** from incomplete Backfill→Historical migration | Medium | ❌ Open | `Infrastructure/Adapters/Core/BackfillProgressTracker.cs` |
| **`StockSharpSymbolSearchProvider.cs` in wrong category** | Medium | ✅ Fixed | Now in `Adapters/StockSharp/` (unified vendor dir) |
| **`Refactored` suffix** on `AlpacaSymbolSearchProviderRefactored.cs` and `FinnhubSymbolSearchProviderRefactored.cs` | Low | ❌ Open | `Adapters/Alpaca/`, `Adapters/Finnhub/` |
| **Inconsistent base class naming** — `BaseHistoricalDataProvider` (prefix) vs `WebSocketProviderBase` (suffix) | Low | ❌ Open | `Adapters/Core/` |

### 5.2 Configuration Scattering

| Issue | Severity | Status | Notes |
|-------|----------|--------|-------|
| **Provider-specific options in `Core/Config/`** — `AlpacaOptions.cs`, `StockSharpConfig.cs` | Low | ❌ Open | See 1.3 |
| **`ICredentialStore.cs` isolated** in `Application/Credentials/` | Low | ❌ Open | Separate from `Application/Config/Credentials/` |
| **`Application/Http/` files still use `Application.UI` namespace** — directory renamed but namespaces not updated | Medium | ❌ Open | `ConfigStore.cs`, `BackfillCoordinator.cs`, `HtmlTemplates.cs`, etc. all declare `namespace Meridian.Application.UI` |

### 5.3 Layer Boundary Violations

| Issue | Severity | Status | Notes |
|-------|----------|--------|-------|
| **`Core.csproj` references `Domain`** — Core should be a lower layer | Medium | ❌ Open | |
| **`IHistoricalDataProvider` and `ISymbolSearchProvider` defined in Infrastructure** instead of `ProviderSdk` | Medium | ❌ Open | In `Adapters/Core/` |
| **`ImplementsAdrAttribute` in ProviderSdk** — used by all layers | Low | ❌ Open | |
| **`Results/ErrorCode.cs` in Application** — lower layers can't use error codes without referencing Application | Medium | ❌ Open | |
| **`MigrationDiagnostics.cs`** in `Core/Monitoring/` uses namespace `Meridian.Application.Monitoring` | Medium | ⚠️ Acknowledged | A comment in the file explains the namespace mismatch is intentional for consistency with other monitoring abstractions |

### 5.4 Duplicate/Ambiguous Names

| Issue | Severity | Status | Notes |
|-------|----------|--------|-------|
| **Two `MarketEvent` records** in `Domain/Events/` and `Contracts/Domain/Events/` | Medium | ❌ Open | |
| **`BackfillCoordinator`** — same class name in `Application/Http/` and `Ui.Shared/Services/` | Medium | ❌ Open | |
| **`ConfigStore`** — same class name in `Application/Http/` and `Ui.Shared/Services/` | Medium | ❌ Open | |

### 5.5 Test Structure Issues

| Issue | Severity | Status | Notes |
|-------|----------|--------|-------|
| **Flat `namespace Meridian.Tests;`** in test files that live in subdirectories | Medium | ⚠️ Partial | Some files updated (e.g., `CompositeHistoricalDataProviderTests` uses `...Tests.Application.Backfill`); 5 backfill tests still use `Meridian.Tests.Backfill` |
| **`BackfillWorkerServiceTests.cs` tests Infrastructure code but lives in `Application/Backfill/`** | Medium | ❌ Open | |
| **`BackfillProgressTrackerTests.cs` tests Infrastructure code but lives in `Application/Pipeline/`** | Medium | ❌ Open | |
| **`CronExpressionParserTests.cs` tests `Core/Scheduling/` but lives in `Application/Services/`** | Low | ❌ Open | |
| **5 backfill tests use old namespace `Meridian.Tests.Backfill`** | Low | ❌ Open | |
| **`SymbolSearch/` tests not mirroring source path** | Low | ❌ Open | |

### 5.6 Other Structural Issues

| Issue | Severity | Status | Notes |
|-------|----------|--------|-------|
| **Double-nested `Core/Performance/Performance/`** folder | Low | ❌ Open | Still present |
| **`StorageOptions.cs` and `StorageProfiles.cs` at project root** instead of `Config/` | Low | ❌ Open | |
| **`Storage/Services/` has 15 mixed-concern services** | Low | ❌ Open | |
| **4 endpoints stranded in `Application/Http/Endpoints/`** while 35 live in `Ui.Shared/Endpoints/` | Medium | ❌ Open | |
| **`FSharp` project not referenced** by any other project | Low | ❌ Open | |
| **`DepthBufferSelfTests.cs`** — runtime self-test code in production Application assembly | Low | ❌ Open | |

### Quick Win Priority Order (Remaining)

Items still actionable with minimal risk, in priority order:

1. **Rename `Refactored` suffix files** — `AlpacaSymbolSearchProviderRefactored.cs` → `AlpacaSymbolSearchProvider.cs`, same for Finnhub (2 files, zero API impact)
2. **Fix namespace/folder mismatch in `Application/Http/`** — update `namespace Meridian.Application.UI` → `Meridian.Application.Http` across all files in that directory
3. **Fix double-nested `Core/Performance/Performance/`** — 1 `git mv`
4. **Move orphaned `BackfillProgressTracker.cs`** — from `Adapters/Core/` root to `Adapters/Core/Backfill/`
5. **Update 5 remaining backfill test namespaces** — `Meridian.Tests.Backfill` → `Meridian.Tests.Application.Backfill`
6. **Fix 3 test files testing wrong layer** — move to mirror source structure
7. **Rename ambiguous `BackfillCoordinator`/`ConfigStore` duplicates**

---

## Implementation Update (2026-03-19)

**Overall Status:** 5/12 proposals implemented; 5/12 partial; 2/12 not started

### Progress Summary

| Bucket | Count | Details |
|--------|-------|---------|
| ✅ Fully Implemented | 5 | 1.1, 1.6 (structural); all of these complete and stable |
| ⚠️ Partially Implemented | 5 | 1.2, 1.7, 2.2, 2.3, 2.4 (foundation in place; finishing work needed) |
| ❌ Not Started | 2 | 1.3, 1.4, 1.5, 2.1, 2.5 (5 items actually; requires architectural decisions) |

### Recommended Next Steps (Priority Order)

**Tier 1 — Quick Wins (Low Risk, High Value)**
| Item | Effort | Benefit |
|------|--------|---------|
| 1. Remove `Refactored` suffix files (AlpacaSymbolSearchProvider) | <1 hour | Cleaner provider interface |
| 2. Fix namespace/folder mismatch in `Application/Http/` (Meridian.Application.UI → Meridian.Application.Http) | 2-3 hours | Alignment with folder structure |
| 3. Fix double-nested `Core/Performance/Performance/` folder | <30 min | Clean directory structure |
| 4. Update 5 remaining backfill test namespaces | <1 hour | Consistent test layout |

**Tier 2 — Medium Effort (Moderate Value)**
| Item | Effort | Impact |
|------|--------|--------|
| 5. Implement 1.3 — Co-located Provider Configuration | 4-6 hours | Reduced coupling between Application and Infrastructure |
| 6. Implement 2.1 — Component Lifecycle FSM Base Class | 6-8 hours | Standardized lifecycle management |
| 7. Implement 2.5 — ArchUnitNET Dependency Tests | 3-4 hours | Prevent future boundary violations |

**Tier 3 — High Effort (Architectural)**
| Item | Effort | Impact |
|------|--------|--------|
| 8. Implement 1.4 — Explicit Per-Provider Parsing Layer | 12-16 hours | Testable, modular parsing; ~200-300 LOC extraction per provider |
| 9. Resolve provider interface location (1.5 refactoring) | 8-12 hours | Move `IHistoricalDataProvider` to `ProviderSdk` |

### Roadmap Mapping

| Proposal | Current Roadmap Phase | Recommendation |
|----------|----------------------|-----------------|
| 1.1, 1.6 | ✅ Complete (Phases 5-6) | Stabilize; no further changes |
| 1.2, 1.3, 1.4 | Phase 8 (Repository Organization) | Include Tier 1-2 quick wins |
| 2.1, 2.2, 2.3, 2.4, 2.5 | Phase 8+ (Long-term cleanup) | Roadmap for Q2 2026 |

**Verdict:** Foundation work (1.1, 1.6) is solid. Quick wins in Tier 1 worth prioritizing in next planning cycle. Tier 2-3 architectural work appropriate for Phase 8 (Repository Organization) roadmap phase.

---

*Proposal Date: 2026-03-01 | Last Reviewed: 2026-03-11 | Updated: 2026-03-19*
