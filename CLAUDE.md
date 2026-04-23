# CLAUDE.md - AI Assistant Guide for Meridian

## Local Codex Skills

Repo-local Codex skills live under `.codex/skills/`. Use them for Meridian-specific blueprinting, brainstorming, cleanup, code review, provider implementation, and test writing workflows.

**Meridian** is a high-performance .NET 9.0 / C# 13 / F# 8.0 integrated trading platform. It collects real-time and historical market microstructure data from multiple providers, executes trading strategies in real-time, backtests strategies on historical data, and tracks portfolio performance across all runs.

**Version:** 1.7.2 | **Status:** Development / Pilot Ready | **Files:** 1,313 source files (1,262 C# + 51 F#) | **Tests:** ~4,756

### Platform Pillars
- **📡 Data Collection** - Real-time streaming (90+ sources) + historical backfill (10+ providers) with data quality monitoring
- **🔬 Backtesting** - Tick-level strategy replay with fill models, portfolio metrics (Sharpe, drawdown, XIRR), and full audit trail
- **⚡ Real-Time Execution** - Paper trading gateway + brokerage gateway framework (Alpaca, IB, StockSharp) for strategy validation and live integration
- **🗂️ Portfolio Tracking** - Performance metrics, strategy lifecycle management, and multi-run comparison

### Key Capabilities
- Real-time streaming: Interactive Brokers, Alpaca, NYSE, Polygon, StockSharp (90+ sources)
- Historical backfill: 10+ providers with automatic fallback chain
- Symbol search: 7 providers (Alpaca, Edgar, Finnhub, Polygon, OpenFIGI, Robinhood, StockSharp)
- Brokerage gateway framework: Alpaca, IB, StockSharp adapters for order routing
- Current provider implementation inventory documented below for audit parity (streaming, historical, symbol-search, brokerage, base, and template classes)
- Data quality monitoring with SLA enforcement
- WAL + tiered JSONL/Parquet storage
- Backtesting engine with tick-by-tick replay and fill models
- Paper trading gateway with risk rules (position limits, drawdown stops, order rate throttle)
- Portfolio performance tracking and multi-run analysis
- Direct lending module with PostgreSQL persistence
- Desktop-local API host plus shared workstation endpoints
- WPF desktop app (Windows) — **code present in `src/Meridian.Wpf/`, included in solution build; builds a stub on non-Windows for CI compatibility**
- QuantConnect Lean Engine integration
- CppTrader native matching engine integration

---

## Quick Commands

```bash
# Build & Test
dotnet build -c Release
dotnet test tests/Meridian.Tests
dotnet test tests/Meridian.FSharp.Tests
make test                    # All tests via Makefile
make build                   # Build via Makefile

# Run
dotnet run --project src/Meridian/Meridian.csproj -- --mode desktop --http-port 8080
make run

# AI Audit Tools (run before/after changes)
make ai-audit                # Full audit (code, docs, tests, providers)
make ai-audit-code           # Convention violations only
make ai-audit-tests          # Test coverage gaps
make ai-verify               # Build + test + lint
make ai-maintenance-light    # Fast maintenance lane + .ai/maintenance-status.json
make ai-maintenance-full     # Full maintenance lane + .ai/maintenance-status.json
python3 build/scripts/ai-repo-updater.py known-errors   # Avoid past AI mistakes
python3 build/scripts/ai-repo-updater.py diff-summary   # Review uncommitted changes

# Diagnostics
make doctor
make diagnose
dotnet restore /p:EnableWindowsTargeting=true -v diag   # Build issue diagnosis

# Backfill
dotnet run --project src/Meridian -- \
  --backfill --backfill-provider stooq \
  --backfill-symbols SPY,AAPL \
  --backfill-from 2024-01-01 --backfill-to 2024-01-05

```

---

## Standard Execution Flow

For every task, follow this sequence to maximize quality and minimize review cycles:

1. **Restate the requested change** in one sentence.
2. **Identify acceptance criteria** before coding (including required tests).
3. **Make the smallest possible set of edits** that satisfy the task.
4. **Run targeted validation commands** (see "Quick Commands" section above).
5. **Summarize what changed, why, and how it was validated.**

If requirements are ambiguous, document assumptions and propose concrete acceptance criteria before proceeding.

---

## Quality Bar Checklist (Before Opening PR or Marking Work Complete)

Always complete this checklist before submitting a PR or marking work as complete:

1. **Review known errors:** Run `python3 build/scripts/ai-repo-updater.py known-errors` and scan `docs/ai/ai-known-errors.md`. Apply all relevant prevention checks. If this task is related to a past AI mistake, verify the prevention pattern is applied.

2. **Restore and build with Windows targeting:**
   ```bash
   dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
   dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true
   ```
   **Note:** Always use `/p:EnableWindowsTargeting=true` on non-Windows systems to avoid NETSDK1100 errors.

3. **Run tests relevant to touched code:**
   - If you modified `src/Meridian.Domain/**`: `dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true`
   - If you modified `src/Meridian.FSharp/**`: also run `dotnet test tests/Meridian.FSharp.Tests -c Release /p:EnableWindowsTargeting=true`
   - Run `make test` to run all tests if unsure

4. **Update docs when behavior changes:**
   - Public API changes → update interface documentation in relevant `CLAUDE.*.md` file or code comments
   - New feature or workflow change → update `docs/HELP.md` FAQ or relevant architecture doc
   - Provider added/modified → update provider inventory in `CLAUDE.md` or `docs/ai/claude/CLAUDE.providers.md`

5. **Keep PR title and body in sync:**
   - Title should match final implemented behavior
   - Body should include: summary, risks/tradeoffs, validation commands run, and follow-up items

---

## AI Error Prevention

**Required workflow:**

1. **Before making changes**: run `python3 build/scripts/ai-repo-updater.py known-errors` and scan `docs/ai/ai-known-errors.md`
2. **After fixing an agent-caused bug**: add a new entry to `docs/ai/ai-known-errors.md` (symptoms, root cause, prevention, verification command)
3. **Before opening PR**: confirm your change does not repeat any known pattern
4. **For automation environments**: prefer the light/full maintenance lanes, which emit `.ai/maintenance-status.json` and `.ai/MAINTENANCE_STATUS.md`

---

## Repository Layout

The full annotated file tree lives in [`docs/ai/claude/CLAUDE.structure.md`](docs/ai/claude/CLAUDE.structure.md). High-level top-level structure:

- `src/` — production code organised by layer: `Meridian` (host), `Meridian.Application`, `Meridian.Domain`, `Meridian.Core`, `Meridian.Contracts`, `Meridian.Infrastructure`, `Meridian.Storage`, `Meridian.Execution[.Sdk]`, `Meridian.Backtesting[.Sdk]`, `Meridian.Strategies`, `Meridian.Risk`, `Meridian.Ledger`, `Meridian.QuantScript`, `Meridian.Mcp`, `Meridian.McpServer`, `Meridian.ProviderSdk`, `Meridian.Ui.Services`, `Meridian.Ui.Shared`, `Meridian.Wpf`, plus the F# projects `Meridian.FSharp[.Ledger|.Trading|.DirectLending.Aggregates]`.
- `tests/` — xUnit projects mirroring the `src/` layout (`Meridian.Tests`, `Meridian.FSharp.Tests`, `Meridian.Backtesting.Tests`, `Meridian.DirectLending.Tests`, `Meridian.Wpf.Tests`, etc.).
- `benchmarks/Meridian.Benchmarks/` — BenchmarkDotNet suite + `BOTTLENECK_REPORT.md`.
- `build/` — `python/` (build/diagnostics tooling, including `cli/buildctl.py`), `scripts/` (notably `ai-repo-updater.py` and the docs automation), `dotnet/` (DocGenerator, FSharpInteropGenerator), `node/` (diagram + icon generators), `rules/`.
- `docs/` — `adr/` (16 ADRs), `architecture/`, `development/`, `operations/`, `providers/`, `reference/`, `plans/`, `status/`, `ai/` (sub-docs catalogued at the bottom of this file).
- `config/` — `appsettings.sample.json`, JSON schema, condition/venue mapping registries.
- `make/` — modular Makefile fragments (`ai.mk`, `build.mk`, `desktop.mk`, `diagnostics.mk`, `docs.mk`, `install.mk`, `test.mk`).
- `scripts/dev/` — PowerShell helpers including `run-desktop.ps1` (launches host + WPF shell), `desktop-workflows.json`, `run-wave1-provider-validation.ps1`.
- `.claude/` and `.codex/` — repo-local skill packs (blueprint, brainstorm, code-review, provider-builder, test-writer, simulated-user-panel, etc.); see project instructions in skill descriptions.
- `.github/instructions/` — path-scoped guidance applied automatically by Copilot/Claude (see "Path-Specific Instruction Rules" below).
- `native/cpptrader-host/` — C++ matching engine integrated via `Meridian.Infrastructure.CppTrader`.
- `plugins/csharp-dotnet-development/` — bundled .NET skills plugin.

---

## Critical Rules

**Always follow these — violations will cause build errors, deadlocks, or data loss:**

- **ALWAYS** use `CancellationToken` on async methods
- **NEVER** store secrets in code or config — use environment variables
- **ALWAYS** use structured logging: `_logger.LogInformation("Received {Count} bars for {Symbol}", count, symbol)`
- **PREFER** `IAsyncEnumerable<T>` for streaming data
- **ALWAYS** mark classes `sealed` unless designed for inheritance
- **NEVER** log sensitive data (API keys, credentials)
- **NEVER** use `Task.Run` for I/O-bound operations (wastes thread pool)
- **NEVER** block async with `.Result` or `.Wait()` (causes deadlocks)
- **ALWAYS** add `[ImplementsAdr]` attributes when implementing ADR contracts
- **NEVER** add `Version="..."` to `<PackageReference>` — causes NU1008 (see CPM section)

---

## Coding Conventions

### Logging
```csharp
// Good — structured
_logger.LogInformation("Received {Count} bars for {Symbol}", bars.Count, symbol);

// Bad — string interpolation loses structure
_logger.LogInformation($"Received {bars.Count} bars for {symbol}");
```

### Error Handling
- Log all errors with context (symbol, provider, timestamp)
- Use exponential backoff for retries
- Throw `ArgumentException` for bad inputs, `InvalidOperationException` for state errors
- Custom exceptions in `src/Meridian.Core/Exceptions/`: `ConfigurationException`, `ConnectionException`, `DataProviderException`, `RateLimitException`, `SequenceValidationException`, `StorageException`, `ValidationException`, `OperationTimeoutException`

### Naming
- Async methods: suffix `Async`
- Cancellation token param: `ct` or `cancellationToken`
- Private fields: `_prefixed`
- Interfaces: `IPrefixed`

### Performance (hot paths)
- Avoid allocations; use object pooling
- Prefer `Span<T>` / `Memory<T>` for buffer ops
- Use `System.Threading.Channels` for producer-consumer patterns

### Path-Specific Instruction Rules

When working with files in specific paths or types, additional rules apply. Review these before making changes:

**C# source files** (`src/**/*.cs`):
- Use `IOptionsMonitor<T>` (not `IOptions<T>`) for runtime-mutable config
- All JSON serialization must use ADR-014 source generators — call `JsonSerializer.Serialize(value, MyJsonContext.Default.MyType)`
- Use `EventPipelinePolicy.Default.CreateChannel<T>()` for producer-consumer queues (ADR-013)
- All domain exceptions must derive from `MeridianException` in `src/Meridian.Core/Exceptions/`
- Register all new serializable DTOs in the project's `JsonSerializerContext` partial class

**Test files** (`tests/**/*.cs`):
- Keep tests deterministic (no time/network/external dependency flakiness)
- Prefer clear Arrange-Act-Assert structure
- Use existing test utilities and fixtures before introducing new helpers
- Name tests to communicate behavior: `[MethodName]_[Condition]_[Expectation]`
- Run the nearest test project and report exact command used

Complete rules for each path are in `.github/instructions/` directory.

---

## Anti-Patterns to Avoid

| Anti-Pattern | Why It's Bad |
|--------------|--------------|
| Swallowing exceptions silently | Hides bugs, makes debugging impossible |
| Hardcoding credentials | Security risk, inflexible deployment |
| `Task.Run` for I/O | Wastes thread pool threads |
| Blocking async with `.Result` | Causes deadlocks |
| `new HttpClient()` directly | Socket exhaustion, DNS issues |
| String interpolation in logger calls | Loses structured logging benefits |
| Missing `CancellationToken` | Prevents graceful shutdown |
| Missing `[ImplementsAdr]` attribute | Loses ADR traceability |
| `Version="..."` on `PackageReference` | NU1008 build error (CPM violation) |

---

## Central Package Management (CPM)

All package versions live in `Directory.Packages.props`. Project files must **not** include versions.

```xml
<!-- CORRECT -->
<PackageReference Include="Serilog" />

<!-- WRONG — causes error NU1008 -->
<PackageReference Include="Serilog" Version="4.3.0" />
```

**Adding a new package:**
1. Add to `Directory.Packages.props`: `<PackageVersion Include="Pkg" Version="1.0.0" />`
2. Reference in `.csproj` without version: `<PackageReference Include="Pkg" />`

---

## Configuration

### Environment Variables (credentials)
```bash
export ALPACA_KEY_ID=your-key-id
export ALPACA_SECRET_KEY=your-secret-key
export NYSE_API_KEY=your-api-key
export POLYGON_API_KEY=your-api-key
export TIINGO_API_TOKEN=your-token
export FINNHUB_API_KEY=your-api-key
export ALPHA_VANTAGE_API_KEY=your-api-key
export NASDAQ_API_KEY=your-api-key
export ROBINHOOD_ACCESS_TOKEN=your-access-token
```

### appsettings.json
```bash
cp config/appsettings.sample.json config/appsettings.json
```

Key sections: `DataSource`, `Symbols`, `Storage`, `Backfill`, `DataQuality`, `Sla`, `Maintenance`

### Git Hooks Setup (Optional but Recommended)

Pre-commit and commit-msg hooks enforce code formatting and commit message conventions. Install them at repository setup:

```bash
./build/scripts/hooks/install-hooks.sh
```

**What they do:**

| Hook | Behavior |
|------|----------|
| `pre-commit` | Runs `dotnet format` on staged C#/F# files, re-stages any changes, and blocks commit if formatting issues remain |
| `commit-msg` | Validates commit message subject is <= 72 characters, non-empty, and optionally warns about trailing periods |

**Manual installation:**

```bash
cp build/scripts/hooks/pre-commit .git/hooks/pre-commit
cp build/scripts/hooks/commit-msg .git/hooks/commit-msg
chmod +x .git/hooks/pre-commit .git/hooks/commit-msg
```

**To disable hooks temporarily:**

```bash
git commit --no-verify
```

---

## Architecture Decision Records (ADRs)

Located in `docs/adr/`. Use `[ImplementsAdr("ADR-XXX", "reason")]` on implementing classes.

| ADR | Key Points |
|-----|------------|
| ADR-001 | Provider abstraction — `IMarketDataClient`, `IHistoricalDataProvider` contracts |
| ADR-002 | Tiered storage — hot/warm/cold architecture |
| ADR-003 | Monolith-first architecture — reject premature microservice decomposition |
| ADR-004 | Async patterns — `CancellationToken`, `IAsyncEnumerable` |
| ADR-005 | Attribute-based discovery — `[DataSource]`, `[ImplementsAdr]` |
| ADR-006 | Domain events — sealed record wrapper with static factories |
| ADR-007 | WAL + event pipeline durability |
| ADR-008 | Multi-format storage — JSONL + Parquet simultaneous writes |
| ADR-009 | F# type-safe domain with C# interop |
| ADR-010 | `IHttpClientFactory` — never instantiate `HttpClient` directly |
| ADR-011 | Centralized configuration — environment variables for credentials |
| ADR-012 | Unified monitoring — health checks + Prometheus metrics |
| ADR-013 | Bounded channel pipeline policy — consistent backpressure |
| ADR-014 | JSON source generators — no-reflection serialization |
| ADR-015 | Paper trading gateway — risk-free strategy validation for live + backtest parity |
| ADR-016 | Platform architecture migration — repository-wide mandate |

---

## Provider Class Inventory

The following provider-related classes are the current canonical inventory used by the AI docs audit.

### Streaming / hybrid implementations
| Provider Class | Role |
|----------------|------|
| `AlpacaMarketDataClient` | Alpaca real-time streaming market data |
| `IBMarketDataClient` | Interactive Brokers live market data |
| `IBSimulationClient` | Interactive Brokers simulation/testing client |
| `NyseMarketDataClient` | NYSE streaming market data via the unified provider registry |
| `NYSEDataSource` | NYSE direct data source |
| `PolygonMarketDataClient` | Polygon live market data |
| `StockSharpMarketDataClient` | StockSharp streaming market data |
| `SyntheticMarketDataClient` | Deterministic synthetic streaming and symbol-search market data for offline development |
| `FailoverAwareMarketDataClient` | Streaming failover wrapper |
| `RobinhoodMarketDataClient` | Robinhood polling-based BBO quotes (unofficial API, requires `ROBINHOOD_ACCESS_TOKEN`) |

### Historical implementations
| Provider Class | Role |
|----------------|------|
| `AlpacaHistoricalDataProvider` | Alpaca historical bars |
| `AlphaVantageHistoricalDataProvider` | Alpha Vantage historical bars |
| `CompositeHistoricalDataProvider` | Multi-provider historical failover |
| `FredHistoricalDataProvider` | FRED economic time series mapped to synthetic daily bars |
| `FinnhubHistoricalDataProvider` | Finnhub historical bars |
| `IBHistoricalDataProvider` | Interactive Brokers historical bars |
| `NasdaqDataLinkHistoricalDataProvider` | Nasdaq Data Link historical bars |
| `PolygonHistoricalDataProvider` | Polygon historical bars |
| `BuiltHistoricalDataProvider` | Internal delegate-driven historical provider produced by `ProviderBehaviorBuilder` |
| `RobinhoodHistoricalDataProvider` | Robinhood free public end-of-day historical bars |
| `StockSharpHistoricalDataProvider` | StockSharp historical bars |
| `StooqHistoricalDataProvider` | Stooq historical bars |
| `SyntheticHistoricalDataProvider` | Deterministic synthetic historical bars, quotes, trades, auctions, and corporate actions |
| `TiingoHistoricalDataProvider` | Tiingo historical bars |
| `TwelveDataHistoricalDataProvider` | Twelve Data historical bars |
| `YahooFinanceHistoricalDataProvider` | Yahoo Finance historical bars |
| `RobinhoodHistoricalDataProvider` | Robinhood historical bars (unofficial API, requires `ROBINHOOD_ACCESS_TOKEN`) |

### Symbol search implementations
| Provider Class | Role |
|----------------|------|
| `AlpacaSymbolSearchProviderRefactored` | Alpaca symbol search |
| `EdgarSymbolSearchProvider` | SEC EDGAR symbol search |
| `FinnhubSymbolSearchProviderRefactored` | Finnhub symbol search |
| `OpenFigiClient` | OpenFIGI symbol resolution/search |
| `PolygonSymbolSearchProvider` | Polygon symbol search |
| `RobinhoodSymbolSearchProvider` | Robinhood public instruments symbol search |
| `StockSharpSymbolSearchProvider` | StockSharp symbol search |

### Brokerage gateway implementations
| Provider Class | Role |
|----------------|------|
| `BaseBrokerageGateway` | Abstract brokerage adapter base class |
| `BrokerageGatewayAdapter` | Order routing wrapper for `IBrokerageGateway` |
| `AlpacaBrokerageGateway` | Alpaca order routing with fractional quantity support |
| `IBBrokerageGateway` | Interactive Brokers order routing (conditional on IBAPI) |
| `StockSharpBrokerageGateway` | StockSharp connector-based order routing |
| `TemplateBrokerageGateway` | Brokerage adapter scaffold |
| `RobinhoodBrokerageGateway` | Robinhood order routing via unofficial API (requires `ROBINHOOD_ACCESS_TOKEN`) |

### Shared base and template provider classes
| Provider Class | Role |
|----------------|------|
| `BaseHistoricalDataProvider` | Shared historical provider base class |
| `BaseSymbolSearchProvider` | Shared symbol-search provider base class |
| `TemplateHistoricalDataProvider` | Historical provider scaffold |
| `TemplateMarketDataClient` | Streaming provider scaffold |
| `TemplateSymbolSearchProvider` | Symbol-search provider scaffold |

---

## Domain Models

### Core Event Types (Data Collection)
- `Trade` — Tick-by-tick trade prints with sequence validation
- `LOBSnapshot` — Full L2 order book state
- `BboQuote` — Best bid/offer with spread and mid-price
- `OrderFlowStatistics` — Rolling VWAP, imbalance, volume splits
- `IntegrityEvent` — Sequence anomalies (gaps, out-of-order)
- `HistoricalBar` — OHLCV bars from backfill providers

### Execution & Strategy Types
- `Order` — Limit/market orders with timestamp and fill tracking
- `Fill` — Executed trade with price, quantity, and commission
- `StrategyState` — Active/paused/stopped strategy with metadata
- `PortfolioSnapshot` — Position, cash, and performance metrics at point-in-time

### Key Classes
| Class | Location | Purpose |
|-------|----------|---------|
| `EventPipeline` | `Application/Pipeline/` | Bounded channel event routing |
| `TradeDataCollector` | `Domain/Collectors/` | Tick-by-tick trade processing |
| `MarketDepthCollector` | `Domain/Collectors/` | L2 order book maintenance |
| `JsonlStorageSink` | `Storage/Sinks/` | JSONL file persistence |
| `ParquetStorageSink` | `Storage/Sinks/` | Parquet file persistence |
| `WriteAheadLog` | `Storage/Archival/` | WAL for data durability |
| `CompositeHistoricalDataProvider` | `Infrastructure/Adapters/Core/` | Multi-provider backfill with fallback |
| `BacktestEngine` | `Backtesting/` | Tick-by-tick strategy replay with fill models |
| `PaperTradingGateway` | `Execution/` | Paper trading for real-time strategy testing |
| `BaseBrokerageGateway` | `Execution/Adapters/` | Abstract brokerage adapter base class |
| `AlpacaBrokerageGateway` | `Infrastructure/Adapters/Alpaca/` | Alpaca order routing |
| `PortfolioTracker` | `Strategies/` | Multi-run performance metrics and lifecycle |

*All locations relative to `src/Meridian/`*

---

## Build Requirements

- .NET 9.0 SDK
- `EnableWindowsTargeting=true` — set in `Directory.Build.props`, enables cross-platform build of Windows-targeting projects
- Python 3 — build tooling in `build/python/`
- Node.js — diagram generation (optional)

---

## Troubleshooting

```bash
make diagnose      # Build diagnostics
make doctor        # Full diagnostic check
```

| Error | Fix |
|-------|-----|
| NETSDK1100 | Ensure `EnableWindowsTargeting=true` in `Directory.Build.props` |
| NU1008 | Remove `Version="..."` from `<PackageReference>` in failing `.csproj` |
| Credential errors | Check environment variables are set |
| High memory | Check channel capacity in `EventPipeline` |
| Provider rate limits | Check `ProviderRateLimitTracker` logs |

See `docs/HELP.md` for detailed solutions.

---

## Detailed Reference Sub-Documents

Load these on-demand when working in the relevant area — do not read all of them on every task.

| Sub-Document | When to Load |
|--------------|-------------|
| [`docs/ai/claude/CLAUDE.providers.md`](docs/ai/claude/CLAUDE.providers.md) | Adding/modifying data providers, `IMarketDataClient`, `IHistoricalDataProvider`, symbol search |
| [`docs/ai/claude/CLAUDE.storage.md`](docs/ai/claude/CLAUDE.storage.md) | Storage sinks, WAL, archival, packaging, tiered storage |
| [`docs/ai/claude/CLAUDE.testing.md`](docs/ai/claude/CLAUDE.testing.md) | Writing or reviewing tests, test patterns, coverage |
| [`docs/ai/claude/CLAUDE.fsharp.md`](docs/ai/claude/CLAUDE.fsharp.md) | F# domain library, validation pipeline, C# interop |
| [`docs/ai/claude/CLAUDE.api.md`](docs/ai/claude/CLAUDE.api.md) | REST API endpoints, backtesting, strategy management, portfolio tracking, CI/CD pipelines |
| [`docs/ai/claude/CLAUDE.domain-naming.md`](docs/ai/claude/CLAUDE.domain-naming.md) | Naming standard for F#/C# financial-instrument, security master, and reference-data types |
| [`docs/ai/claude/CLAUDE.repo-updater.md`](docs/ai/claude/CLAUDE.repo-updater.md) | Running `ai-repo-updater.py` audit/verify/report commands |
| [`docs/ai/claude/CLAUDE.structure.md`](docs/ai/claude/CLAUDE.structure.md) | Full annotated file tree with backtesting, execution, and strategy projects |
| [`docs/ai/claude/CLAUDE.actions.md`](docs/ai/claude/CLAUDE.actions.md) | GitHub Actions workflows |
| [`docs/ai/claude/CLAUDE.roadmap-learning-log.md`](docs/ai/claude/CLAUDE.roadmap-learning-log.md) | Running log of roadmap items studied on `claude/continue-roadmap-learning-*` branches |
| [`docs/ai/ai-known-errors.md`](docs/ai/ai-known-errors.md) | Known AI agent mistakes — check before starting any task |

### Other Key Docs
| Doc | Purpose |
|-----|---------|
| `docs/adr/` | Architecture Decision Records |
| `docs/development/provider-implementation.md` | Step-by-step data provider guide |
| `docs/development/strategy-implementation.md` | Step-by-step strategy development guide |
| `docs/operations/portable-data-packager.md` | Data packaging and export |
| `docs/operations/strategy-lifecycle.md` | Strategy registration, deployment, and monitoring |
| `docs/architecture/backtesting-design.md` | Backtest engine architecture and fill models |
| `docs/HELP.md` | Complete user guide with FAQ |
| `docs/development/central-package-management.md` | CPM details |
| `docs/status/production-status.md` | Feature implementation status |
| `docs/status/ROADMAP.md` | Project roadmap and future work |

---

*Last Updated: 2026-04-23*
