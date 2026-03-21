# Architecture Diagrams

**Status:** Active  
**Owner:** Core Team  
**Reviewed:** 2026-03-15

This folder contains architecture diagrams for the Meridian system, updated to reflect the current monolithic runtime, UI options, and provider list. It is the single home for all visual assets:

- **Graphviz DOT diagrams** — C4 context/container/component, data flow, provider and storage architecture (files in this directory)
- **UML diagrams** — Sequence, state, activity, timing, and communication diagrams in [`uml/`](uml/README.md)

---

## Diagram Index

| Diagram | Description | File |
| --------- | ------------- | ------ |
| **C4 Level 1: Context** | System context showing actors and external systems | `c4-level1-context.dot` |
| **C4 Level 2: Containers** | High-level container view (apps, services, storage) | `c4-level2-containers.dot` |
| **C4 Level 3: Components** | Internal component architecture of core collector | `c4-level3-components.dot` |
| **Data Flow** | End-to-end data flow from sources to export | `data-flow.dot` |
| **Provider Architecture** | Data provider abstraction and implementation | `provider-architecture.dot` |
| **Storage Architecture** | Storage pipeline with WAL, compression, tiering | `storage-architecture.dot` |
| **Event Pipeline Sequence** | Detailed event processing sequence | `event-pipeline-sequence.dot` |
| **Resilience Patterns** | Circuit breakers, retry, failover patterns | `resilience-patterns.dot` |
| **Deployment Options** | Standalone and Docker deployment paths | `deployment-options.dot` |
| **Onboarding Flow** | User journey from first-run to operation | `onboarding-flow.dot` |
| **CLI Commands** | All CLI flags and commands reference | `cli-commands.dot` |
| **Project Dependencies** | Project layer dependencies and test coverage | `project-dependencies.dot` |
| **UI Navigation Map** | Auto-generated WPF sidebar/workspace navigation map from source code without hand-maintained drift | `ui-navigation-map.dot` |
| **UI Implementation Flow** | Auto-generated WPF shell/DI/navigation flow from source code without hand-maintained drift | `ui-implementation-flow.dot` |

---

## Diagram Descriptions

### C4 Level 1: System Context

Shows the Meridian in context with:

- **Users**: Operators, Quants/Analysts, and DevOps
- **Streaming Providers**: IB, Alpaca, NYSE Direct, Polygon, StockSharp
- **Historical Providers**: Alpaca, Alpha Vantage, Finnhub, Interactive Brokers, Nasdaq Data Link, Polygon, Stooq, Tiingo, Yahoo Finance, StockSharp (10+ total)
- **Downstream Systems**: QuantConnect Lean, Python/R Analytics, PostgreSQL/TimescaleDB
- **Infrastructure**: Storage (JSONL/Parquet), Monitoring (Prometheus/Grafana), Status HTTP endpoints

### C4 Level 2: Container Diagram

Shows the major deployable units:

- **Presentation Layer**: Web Dashboard, WPF Desktop App, CLI Interface
- **Onboarding & Diagnostics Layer**: Configuration Wizard, Auto-Configuration, Diagnostic Services, Error Formatter
- **Core Collector Service** (.NET 9 console with 100K bounded channel policy)
- **F# Domain Library** (Type-safe validation, discriminated unions)
- **Contracts Library** (Shared DTOs, pipeline contracts)
- **Storage Layer** (WAL → JSONL → Parquet with tiered storage)
- **Observability** (Prometheus, Grafana, structured logs)

### C4 Level 3: Component Diagram

Detailed view of the core collector internals:

- **Infrastructure Layer**: Streaming clients (IB, Alpaca, NYSE, Polygon, StockSharp), Historical providers (10+), Connection/Resilience management, Performance optimizations (source-generated JSON, connection warmup)
- **Domain Layer**: Collectors (Trade, Quote, Depth), Domain models, F# validation pipeline
- **Application Layer**: EventPipeline (100K bounded channel policy), Technical indicators, Config/Monitoring, Backfill service, **Onboarding & Diagnostics** (AutoConfiguration, Wizard, FirstRunDetector, ConnectivityTest, CredentialValidation, ErrorFormatter, ProgressDisplay, StartupSummary)
- **Storage Layer**: Write path (WAL, JSONL, Parquet), Compression profiles, Export service, Quality reporting

### Data Flow Diagram

Shows data moving through the system:

0. **First-Time Setup**: First-run detection → Wizard/Auto-config → Generate appsettings.json
1. **Streaming Sources**: IB, Alpaca, NYSE, Polygon, StockSharp → Real-time ingestion
2. **Historical Sources**: Alpaca, Alpha Vantage, Finnhub, IB, Nasdaq Data Link, Polygon, Stooq, Tiingo, Yahoo Finance → Batch backfill
3. **Processing**: Domain collectors → F# validation → Technical indicators
4. **Pipeline**: Bounded channel (100K policy) → Composite publisher
5. **Storage**: WAL → JSONL (hot) → Compression → Parquet (archive) → Tiered storage
6. **Export**: Python/Pandas, R, QuantConnect Lean, Excel, PostgreSQL
7. **Optional Exports**: Downstream exports to Lean, Python/R, or database targets

### Provider Architecture

Details the provider abstraction:

- **Core Interfaces**: IDataSource, IRealtimeDataSource, IHistoricalDataSource
- **Legacy Interfaces**: IMarketDataClient, IHistoricalDataProvider
- **Streaming Providers (5)**: Interactive Brokers, Alpaca, NYSE, Polygon, StockSharp
- **Historical Providers (10+)**: Alpaca, Alpha Vantage, Finnhub, Interactive Brokers, Nasdaq Data Link, Polygon, StockSharp, Stooq, Tiingo, Yahoo Finance
- **Resilience**: EnhancedIBConnectionManager, WebSocketResiliencePolicy, AutomaticFailoverManager, CircuitBreaker, RateLimiter
- **Symbol Resolution**: OpenFIGI, SymbolMapper, ContractFactory
- **CompositeHistoricalDataProvider**: Automatic failover, rate-limit rotation, priority selection

### Storage Architecture

Details the archival-first storage pipeline:

- **Write-Ahead Log**: Crash-safe journal, SHA256 checksums, NoSync/BatchedSync/EveryWrite modes
- **Hot Storage (JSONL)**: Append-only, daily partitioning, multiple naming conventions
- **Compression Profiles**: LZ4 (real-time), ZSTD Level 3/6/19 (high-volume/warm/cold), Gzip (portable)
- **Archive Storage (Parquet)**: Columnar format, 10-20x compression, schema versioning
- **Tiered Storage**: Hot (SSD, 0-7d) → Warm (HDD, 7-30d) → Cold (S3/Glacier, 30d+)
- **Export Formats**: Python/Pandas, R Statistics, QuantConnect Lean, Excel, PostgreSQL
- **Quality Assessment**: Multi-dimensional scoring, outlier detection (4σ), A+ to F grading

### Event Pipeline Sequence

Shows the detailed event processing sequence:

1. **Data Source** → Raw events from provider WebSocket
2. **Provider Client** → Normalize to domain updates
3. **Domain Collectors** → Process trades/quotes/depth, emit domain events
4. **F# Validation** → Railway-oriented type-safe validation
5. **Event Pipeline** → Bounded channel async write/read
6. **Composite Publisher** → Fanout to configured sinks
7. **Storage Path** → WAL journal → JSONL persist
8. **Observability** → Metrics, traces, status endpoints

### Resilience Patterns

Shows fault tolerance mechanisms:

- **Circuit Breaker**: Closed → Open → Half-Open states, configurable thresholds
- **Retry Pattern**: Exponential backoff with jitter, max 5 attempts
- **Provider Failover**: Priority-based, health-monitored automatic switching
- **Historical Provider Failover**: CompositeProvider with 3-tier priority chain
- **Rate Limiting**: Token bucket per provider, configurable limits
- **Connection Management**: State machine, heartbeat monitoring, auto-reconnect
- **Graceful Degradation**: Full → Partial → Minimal service levels

### Deployment Options

Shows deployment paths from simple to enterprise:

1. **Standalone Console** - Single .NET app, local storage, simplest setup
2. **Docker Compose** - Containerized, service orchestration, team development
3. **System Service** - systemd (Linux) for long-running collectors
4. **Pre-Deployment Setup** - First-time configuration via wizard or auto-config

### Onboarding Flow

Shows the user journey from first-run to operational:

1. **First-Run Detection** - Automatic detection of new installations
2. **Setup Options**:
   - `--wizard` - Interactive 8-step configuration wizard
   - `--auto-config` - Quick auto-configuration from environment variables
   - `--generate-config <type>` - Template generation (minimal, full, alpaca, etc.)
   - `--detect-providers` - Scan and display available providers
3. **Configuration Wizard Steps**:
   - Detect providers → Select use case → Configure data source
   - Configure symbols → Configure storage → Configure backfill
   - Review settings → Save configuration
4. **Validation & Diagnostics**:
   - `--validate-credentials` - Test API keys
   - `--test-connectivity` - Network diagnostics
   - `--quick-check` - Fast health check
   - `--dry-run` - Full test without data collection
5. **Error Handling** - FriendlyErrorFormatter with 24 error codes (Meridian-XXX-NNN)

### CLI Commands Reference

Comprehensive reference for all 24+ CLI flags:

- **Setup Commands**: --wizard, --auto-config, --generate-config, --detect-providers
- **Diagnostic Commands**: --validate-credentials, --test-connectivity, --quick-check, --validate-config, --dry-run, --selftest
- **Operation Commands**: --ui, --http-port, --watch-config, --backfill, --backfill-provider, --backfill-symbols, --backfill-from, --backfill-to
- **Symbol Commands**: --symbols, --symbols-monitored, --symbols-archived, --symbols-add, --symbols-remove, --symbol-status
- **Package Commands**: --package, --import-package, --list-package, --validate-package
- **Documentation Commands**: --show-config, --error-codes, --help, --version
- **Common Workflows**: New installation, CI/CD setup, troubleshooting, backfill operations
- **Exit Codes**: 0 (success), 1 (general error), 2 (config error), 3 (connection error), 4 (auth error), 5 (validation failed)

### Project Dependencies

Shows the project layer dependencies:

- **Layer 0 (Foundation)**: Contracts, F# Domain
- **Layer 1 (Core)**: ProviderSdk, Domain, Core
- **Layer 2 (Infrastructure)**: Infrastructure, Storage
- **Layer 3 (Application)**: Application logic
- **Layer 4 (UI)**: Ui.Shared, Ui.Services, Ui (web), Wpf (desktop)
- **Layer 5 (Entry Point)**: Meridian main app
- **Tests**: 140 main tests, 4 F# tests, 19 WPF tests, 50 UI tests
- **Benchmarks**: BenchmarkDotNet performance tests

### UI Navigation Map _(auto-generated)_

Shows the current WPF sidebar implementation as it exists in source control:

- **Shell source**: `src/Meridian.Wpf/Views/MainPage.xaml`
- **Navigation registry**: `src/Meridian.Wpf/Services/NavigationService.cs`
- **Coverage**: workspace groups, sidebar-visible pages, and registered-but-hidden routes
- **Purpose**: makes navigation drift obvious whenever pages are added, moved, or orphaned

### UI Implementation Flow _(auto-generated)_

Shows how the WPF desktop host wires the UI together:

- **App composition**: `src/Meridian.Wpf/App.xaml.cs`
- **Window shell**: `src/Meridian.Wpf/MainWindow.xaml.cs`
- **Main page shell**: `src/Meridian.Wpf/Views/MainPage.xaml.cs`
- **Page inventory**: `src/Meridian.Wpf/Views/Pages.cs`
- **Purpose**: tracks DI composition, shell responsibilities, and navigation/page inventory as development changes progress while keeping outputs deterministic unless the underlying WPF source changes

---

## Generating Images

### Prerequisites

The repository can regenerate diagrams with the committed Node-based renderer (recommended) or Graphviz. The Node path also refreshes the auto-generated WPF UI diagrams from source code without hand-maintained drift before rendering.

Install Node dependencies:

```bash
npm ci
```

Optional Graphviz install (useful for ad-hoc manual rendering):

```bash
# Ubuntu/Debian
sudo apt-get install graphviz

# macOS
brew install graphviz

# Windows (via Chocolatey)
choco install graphviz
```

### Generate SVG Images (recommended)

```bash
# From the repository root
npm run generate-diagrams
```

This command:

- refreshes `ui-navigation-map.dot` and `ui-implementation-flow.dot` from the current WPF source files
- renders the auto-generated UI diagrams to committed `.svg` artifacts

To render every DOT file through the Node pipeline instead, run:

```bash
npm run generate-diagrams -- --all
```

### Generate with Graphviz manually

```bash
cd docs/diagrams

# Generate all SVGs
for f in *.dot; do
  dot -Tsvg "$f" -o "${f%.dot}.svg"
done
```

### High-DPI PNG (for presentations)

```bash
dot -Tpng -Gdpi=300 c4-level2-containers.dot -o c4-level2-containers-hd.png
```

---

## Color Scheme

The diagrams use a consistent color palette based on Tailwind CSS colors:

| Color | Hex | Usage |
| ------- | ----- | ------- |
| **Actors** | | |
| Dark Blue | `#08427b` | Persons/Users |
| **System Boundary** | | |
| Blue | `#438dd5` | Core system containers |
| Light Blue | `#4a90d9` | Streaming providers |
| Pale Blue | `#dbeafe` / `#bfdbfe` | Infrastructure components |
| **Domain Layer** | | |
| Teal | `#2c7a7b` | Domain and pipeline components |
| Green | `#38a169` | Historical providers |
| Light Green | `#d1fae5` / `#a7f3d0` | Domain components |
| Mint | `#6ee7b7` | F# components |
| **Application Layer** | | |
| Purple | `#805ad5` | Application/Pipeline layer |
| Light Purple | `#ede9fe` / `#ddd6fe` | Application components |
| **Storage Layer** | | |
| Red | `#c53030` / `#e53e3e` | Storage/Critical systems |
| Light Red | `#fee2e2` / `#fecaca` | Storage components |
| Hot tier | `#fc8181` | WAL, Hot storage |
| Warm tier | `#fbd38d` | JSONL, Compression |
| **Infrastructure** | | |
| Orange | `#ed8936` | HTTP/Monitoring servers |
| Gray | `#718096` | External/Shared systems |
| Light Gray | `#e2e8f0` | System services |
| **Onboarding/UX** | | |
| Orange | `#ed8936` | Onboarding services |
| Light Orange | `#fbd38d` | Setup/Config components |
| Pale Orange | `#feebc8` | Wizard steps |
| Cream | `#fffaf0` | Onboarding background |
| **Export/Quality** | | |
| Export Blue | `#2b6cb0` | Export layer |
| Quality Green | `#9ae6b4` | Quality/Observability |
| **Status Indicators** | | |
| Success Green | `#9ae6b4` | Healthy/Production |
| Warning Yellow | `#fef3c7` / `#fbd38d` | Config required/Warning |
| Error Red | `#fecaca` | Stub/Error/Failed |

---

## C4 Model Reference

These diagrams follow the [C4 Model](https://c4model.com/) notation:

- **Level 1 (Context)**: System context - how the system fits into the world
- **Level 2 (Container)**: High-level technology choices and deployment units
- **Level 3 (Component)**: Major structural building blocks within a container
- **Level 4 (Code)**: Not included (use IDE for code-level exploration)

---

## UML Diagrams (PlantUML)

UML diagrams using PlantUML (`.puml` sources + `.png` artifacts) live in [`uml/`](uml/README.md):

| Diagram Type | Description |
| --- | --- |
| Use Case | System actors and high-level use cases |
| Sequence | Real-time data collection and backfill flows |
| Activity | Main collection and backfill processes |
| State | Provider connection, order book, trade sequence, backfill lifecycles |
| Communication | Component-level message exchange |
| Interaction Overview | High-level workflow orchestration |
| Timing | Real-time event and backfill operation timing |

See [uml/README.md](uml/README.md) for the full inventory and rendering instructions.

---

## Related Documentation

- [Architecture Overview](../architecture/overview.md)
- [C4 Diagrams Reference](../architecture/c4-diagrams.md)
- [Production Status](../status/production-status.md)
- [Provider Management](../architecture/provider-management.md)

---

_Graphviz diagrams generated with DOT language. UML diagrams generated with PlantUML._

_Last Updated: 2026-03-15_
