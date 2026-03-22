# Repository Reorganization Plan

**Version:** 1.0
**Date:** 2026-01-31
**Status:** Proposed
**Scope:** Structure-only changes (zero code modifications)

---

## Executive Summary

This document proposes a comprehensive reorganization of the Meridian repository structure to improve discoverability, conceptual clarity, onboarding speed, and long-term maintainability.

**Key Constraint:** All source code remains byte-for-byte identical. Only folder structures, file locations, and naming are modified.

---

## A. High-Level Diagnosis

### What the Current Structure Communicates

The existing repository structure communicates a **layered architecture** (Application/Domain/Infrastructure/Storage) within a monolithic core project, with supporting UI and shared contract projects. This is generally appropriate, but several structural decisions obscure intent or create navigation friction.

### Where Confusion Arises

| Issue | Location | Impact |
|-------|----------|--------|
| **Mixed concerns in DataQuality/** | `Application/Monitoring/DataQuality/` | HTTP endpoints (`DataQualityEndpoints.cs`) are mixed with business services |
| **Provider type confusion** | `Infrastructure/Providers/` | Streaming providers are flat folders, but historical providers are nested under `Backfill/` |
| **Scheduling layer violation** | `Infrastructure/Providers/Backfill/Scheduling/` | Application-layer scheduling code resides in Infrastructure |
| **StockSharp visibility** | `Infrastructure/Providers/StockSharp/` | 90+ data sources accessible via StockSharp, but only 5 files visible |
| **AI assistant content scattered** | Root, `docs/`, `.github/` | CLAUDE.md, ai-assistants/, agents/, prompts/ are in 4 different locations |
| **Test organization inconsistent** | `tests/Meridian.Tests/` | Some tests at root, some nested; no clear pattern |
| **Build tooling fragmented** | `build-system/`, `scripts/`, `tools/`, root | Python, PowerShell, .NET, Node.js tooling scattered |
| **UI concerns distributed** | Multiple locations | `Application/UI/`, `Meridian.Ui/`, `Meridian.Ui.Shared/` |
| **wwwroot duplication** | Two locations | Both core project and UI project have `wwwroot/` |
| **Documentation entropy** | `docs/` | Many placeholder folders (`uml/`, `diagrams/`, `docfx/`) with only README.md |

### Where Intent is Obscured

1. **Provider Architecture Intent**
   - Current: `Providers/Alpaca/`, `Providers/Backfill/AlpacaHistoricalDataProvider.cs`
   - A new engineer cannot quickly distinguish streaming vs. historical providers
   - The `Backfill/` folder name describes an action, not a capability type

2. **Data Quality Monitoring Intent**
   - Current: Quality services, quality endpoints, quality models all in `Monitoring/DataQuality/`
   - Unclear whether this is a monitoring concern or an HTTP API concern

3. **StockSharp Strategic Importance**
   - Current: One provider folder among many
   - Obscures that StockSharp provides access to 90+ data sources

4. **AI Assistant Context Intent**
   - Current: Four separate locations
   - Unclear which guide applies to which AI tool or workflow

---

## B. Proposed Target Structure

### Root Level (No Changes)

```
Meridian/
├── .claude/                          # (unchanged)
├── .github/                          # (reorganized internally)
├── benchmarks/                       # (unchanged)
├── build/                            # (NEW - consolidated from build-system, scripts, tools)
├── config/                           # (unchanged)
├── deploy/                           # (unchanged)
├── docs/                             # (reorganized internally)
├── src/                              # (reorganized internally)
├── tests/                            # (reorganized internally)
├── CLAUDE.md                         # (unchanged)
├── Directory.Build.props             # (unchanged)
├── Directory.Packages.props          # (unchanged)
├── global.json                       # (unchanged)
├── LICENSE                           # (unchanged)
├── Makefile                          # (unchanged)
├── Meridian.sln           # (unchanged)
├── package.json                      # (unchanged)
├── package-lock.json                 # (unchanged)
└── README.md                         # (unchanged)
```

### Source Code Structure (Proposed)

```
src/
├── Meridian/                        # Core application
│   ├── Application/                            # Application layer
│   │   ├── Backfill/                           # Backfill orchestration (unchanged)
│   │   ├── Config/                             # Configuration (unchanged)
│   │   │   └── Credentials/                    # Credential providers
│   │   ├── Credentials/                        # (unchanged)
│   │   ├── Exceptions/                         # Custom exceptions (unchanged)
│   │   ├── Filters/                            # Event filtering (unchanged)
│   │   ├── Indicators/                         # Technical indicators (unchanged)
│   │   ├── Logging/                            # Logging setup (unchanged)
│   │   ├── Monitoring/                         # Monitoring services
│   │   │   ├── Core/                           # Health checks, alerts (unchanged)
│   │   │   └── DataQuality/                    # Quality services ONLY
│   │   │       ├── AnomalyDetector.cs
│   │   │       ├── CompletenessScoreCalculator.cs
│   │   │       ├── CrossProviderComparisonService.cs
│   │   │       ├── DataFreshnessSlaMonitor.cs
│   │   │       ├── DataQualityModels.cs
│   │   │       ├── DataQualityMonitoringService.cs
│   │   │       ├── DataQualityReportGenerator.cs
│   │   │       ├── GapAnalyzer.cs
│   │   │       ├── IQualityAnalyzer.cs
│   │   │       ├── LatencyHistogram.cs
│   │   │       ├── PriceContinuityChecker.cs
│   │   │       └── SequenceErrorTracker.cs
│   │   │       # NOTE: DataQualityEndpoints.cs MOVES to Http/Endpoints/
│   │   ├── Pipeline/                           # Event pipeline (unchanged)
│   │   ├── Results/                            # Result types (unchanged)
│   │   ├── Scheduling/                         # Application scheduling
│   │   │   ├── IOperationalScheduler.cs        # (existing)
│   │   │   ├── BackfillScheduleManager.cs      # (MOVED from Infrastructure)
│   │   │   ├── ScheduledBackfillService.cs     # (MOVED from Infrastructure)
│   │   │   ├── BackfillExecutionLog.cs         # (MOVED from Infrastructure)
│   │   │   ├── BackfillSchedule.cs             # (MOVED from Infrastructure)
│   │   │   └── CronExpressionParser.cs         # (MOVED from Infrastructure)
│   │   ├── Serialization/                      # JSON config (unchanged)
│   │   ├── Services/                           # Core services (unchanged)
│   │   ├── Subscriptions/                      # Subscription management (unchanged)
│   │   │   ├── Models/
│   │   │   └── Services/
│   │   ├── Testing/                            # Test utilities (unchanged)
│   │   ├── Tracing/                            # Distributed tracing (unchanged)
│   │   └── Http/                               # HTTP endpoints (RENAMED from UI/)
│   │       └── Endpoints/                      # All HTTP endpoint files
│   │           ├── DataQualityEndpoints.cs     # (MOVED from Monitoring/DataQuality/)
│   │           └── (other endpoint files)
│   │
│   ├── Domain/                                 # Domain layer (unchanged)
│   │   ├── Collectors/                         # Data collectors
│   │   ├── Events/                             # Domain events
│   │   └── Models/                             # Domain models
│   │
│   ├── Infrastructure/                         # Infrastructure layer
│   │   ├── Contracts/                          # Core interfaces (unchanged)
│   │   ├── DataSources/                        # Data source abstractions (unchanged)
│   │   ├── Http/                               # HTTP client config (unchanged)
│   │   ├── Performance/                        # Performance utilities (unchanged)
│   │   ├── Resilience/                         # WebSocket resilience (unchanged)
│   │   ├── Shared/                             # Shared utilities (unchanged)
│   │   ├── Utilities/                          # Helper utilities (unchanged)
│   │   └── Providers/                          # Data providers (REORGANIZED)
│   │       │
│   │       ├── Core/                           # Provider infrastructure (unchanged)
│   │       │   ├── ProviderFactory.cs
│   │       │   ├── ProviderRegistry.cs
│   │       │   ├── ProviderTemplate.cs
│   │       │   ├── IProviderMetadata.cs
│   │       │   ├── ProviderServiceExtensions.cs
│   │       │   └── ProviderSubscriptionRanges.cs
│   │       │
│   │       ├── Streaming/                      # NEW: All streaming providers grouped
│   │       │   ├── Alpaca/
│   │       │   │   └── AlpacaMarketDataClient.cs
│   │       │   ├── InteractiveBrokers/
│   │       │   │   ├── IBMarketDataClient.cs
│   │       │   │   ├── IBConnectionManager.cs
│   │       │   │   ├── EnhancedIBConnectionManager.cs
│   │       │   │   ├── EnhancedIBConnectionManager.IBApi.cs
│   │       │   │   ├── IBCallbackRouter.cs
│   │       │   │   ├── IBApiLimits.cs
│   │       │   │   └── ContractFactory.cs
│   │       │   ├── NYSE/
│   │       │   │   ├── NYSEDataSource.cs
│   │       │   │   ├── NYSEOptions.cs
│   │       │   │   └── NYSEServiceExtensions.cs
│   │       │   ├── Polygon/
│   │       │   │   └── PolygonMarketDataClient.cs
│   │       │   └── StockSharp/
│   │       │       ├── StockSharpMarketDataClient.cs
│   │       │       ├── StockSharpConnectorFactory.cs
│   │       │       ├── StockSharpConnectorCapabilities.cs
│   │       │       └── Converters/
│   │       │           ├── MessageConverter.cs
│   │       │           └── SecurityConverter.cs
│   │       │
│   │       ├── Historical/                     # NEW: All historical providers grouped
│   │       │   ├── IHistoricalDataProvider.cs
│   │       │   ├── BaseHistoricalDataProvider.cs
│   │       │   ├── CompositeHistoricalDataProvider.cs
│   │       │   ├── Alpaca/
│   │       │   │   └── AlpacaHistoricalDataProvider.cs
│   │       │   ├── AlphaVantage/
│   │       │   │   └── AlphaVantageHistoricalDataProvider.cs
│   │       │   ├── Finnhub/
│   │       │   │   └── FinnhubHistoricalDataProvider.cs
│   │       │   ├── InteractiveBrokers/
│   │       │   │   └── IBHistoricalDataProvider.cs
│   │       │   ├── NasdaqDataLink/
│   │       │   │   └── NasdaqDataLinkHistoricalDataProvider.cs
│   │       │   ├── Polygon/
│   │       │   │   └── PolygonHistoricalDataProvider.cs
│   │       │   ├── StockSharp/
│   │       │   │   └── StockSharpHistoricalDataProvider.cs
│   │       │   ├── Stooq/
│   │       │   │   └── StooqHistoricalDataProvider.cs
│   │       │   ├── Tiingo/
│   │       │   │   └── TiingoHistoricalDataProvider.cs
│   │       │   ├── YahooFinance/
│   │       │   │   └── YahooFinanceHistoricalDataProvider.cs
│   │       │   ├── RateLimiting/               # Shared rate limiting
│   │       │   │   ├── RateLimiter.cs
│   │       │   │   └── ProviderRateLimitTracker.cs
│   │       │   ├── Queue/                      # Backfill queue infrastructure
│   │       │   │   ├── BackfillWorkerService.cs
│   │       │   │   ├── BackfillJobManager.cs
│   │       │   │   ├── BackfillJob.cs
│   │       │   │   ├── BackfillRequestQueue.cs
│   │       │   │   └── PriorityBackfillQueue.cs
│   │       │   ├── GapAnalysis/                # Gap detection and repair
│   │       │   │   ├── DataGapAnalyzer.cs
│   │       │   │   ├── DataGapRepair.cs
│   │       │   │   └── DataQualityMonitor.cs
│   │       │   └── SymbolResolution/           # (unchanged)
│   │       │       ├── ISymbolResolver.cs
│   │       │       └── OpenFigiSymbolResolver.cs
│   │       │
│   │       ├── SymbolSearch/                   # Symbol search providers (unchanged)
│   │       │   ├── ISymbolSearchProvider.cs
│   │       │   ├── BaseSymbolSearchProvider.cs
│   │       │   ├── AlpacaSymbolSearchProviderRefactored.cs
│   │       │   ├── FinnhubSymbolSearchProviderRefactored.cs
│   │       │   ├── OpenFigiClient.cs
│   │       │   ├── PolygonSymbolSearchProvider.cs
│   │       │   ├── StockSharpSymbolSearchProvider.cs
│   │       │   └── SymbolSearchUtility.cs
│   │       │
│   │       ├── MultiProvider/                  # Multi-provider routing (unchanged)
│   │       └── SubscriptionManager.cs          # (unchanged)
│   │
│   ├── Storage/                                # Storage layer (unchanged)
│   │   ├── Archival/
│   │   ├── Export/
│   │   ├── Interfaces/
│   │   ├── Maintenance/
│   │   ├── Packaging/
│   │   ├── Policies/
│   │   ├── Replay/
│   │   ├── Services/
│   │   ├── Sinks/
│   │   ├── StorageOptions.cs
│   │   └── StorageProfiles.cs
│   │
│   ├── Integrations/                           # External integrations (unchanged)
│   │   └── Lean/
│   │
│   ├── Tools/                                  # Utility tools (unchanged)
│   ├── wwwroot/                                # (unchanged - templates)
│   └── Program.cs                              # Entry point (unchanged)
│
├── Meridian.Contracts/              # (unchanged)
├── Meridian.FSharp/                 # (unchanged)
├── Meridian.Ui/                     # (unchanged)
├── Meridian.Ui.Shared/              # (unchanged)
└── Meridian.Uwp/                    # (unchanged)
```

### Build Tooling Structure (Proposed)

```
build/                                          # NEW: Consolidated build tooling
├── python/                                     # (MOVED from build-system/)
│   ├── cli/
│   │   └── buildctl.py
│   ├── adapters/
│   ├── analytics/
│   ├── core/
│   ├── diagnostics/
│   ├── knowledge/
│   └── __init__.py
│
├── scripts/                                    # (MOVED from scripts/)
│   ├── install/
│   ├── publish/
│   ├── run/
│   ├── lib/
│   └── docs/
│
├── node/                                       # Node.js tooling (MOVED)
│   ├── generate-diagrams.mjs                   # (from scripts/)
│   └── generate-icons.mjs                      # (from scripts/)
│
└── dotnet/                                     # .NET tooling (MOVED from tools/)
    ├── DocGenerator/
    └── FSharpInteropGenerator/
```

### Documentation Structure (Proposed)

```
docs/
├── getting-started/                            # NEW: Onboarding zone
│   ├── README.md                               # First stop for new engineers
│   ├── setup.md                                # (MOVED from guides/getting-started.md)
│   ├── configuration.md                        # (MOVED from guides/configuration.md)
│   └── troubleshooting.md                      # (MOVED from guides/troubleshooting.md)
│
├── architecture/                               # (unchanged)
│   ├── overview.md
│   ├── domains.md
│   ├── storage-design.md
│   ├── crystallized-storage-format.md
│   ├── provider-management.md
│   ├── consolidation.md
│   ├── c4-diagrams.md
│   └── why-this-architecture.md
│
├── adr/                                        # Architecture Decision Records (unchanged)
│
├── providers/                                  # Provider documentation (unchanged)
│
├── operations/                                 # NEW: Production operations zone
│   ├── operator-runbook.md                     # (MOVED from guides/)
│   ├── portable-data-packager.md               # (MOVED from guides/)
│   └── msix-packaging.md                       # (MOVED from guides/)
│
├── development/                                # NEW: Developer guides zone
│   ├── provider-implementation.md              # (MOVED from guides/)
│   ├── uwp-development-roadmap.md              # (MOVED from guides/)
│   ├── uwp-release-checklist.md                # (MOVED from guides/)
│   ├── github-actions-summary.md               # (MOVED from guides/)
│   └── github-actions-testing.md               # (MOVED from guides/)
│
├── integrations/                               # (unchanged)
│
├── reference/                                  # (unchanged)
│
├── ai/                                         # NEW: Consolidated AI assistant docs
│   ├── README.md                               # Index of AI guides
│   ├── claude/                                 # Claude-specific guides
│   │   ├── CLAUDE.providers.md                 # (MOVED from ai-assistants/)
│   │   ├── CLAUDE.storage.md                   # (MOVED from ai-assistants/)
│   │   ├── CLAUDE.fsharp.md                    # (MOVED from ai-assistants/)
│   │   └── CLAUDE.testing.md                   # (MOVED from ai-assistants/)
│   └── copilot/
│       └── instructions.md                     # (MOVED from .github/copilot-instructions.md)
│
├── status/                                     # Project status (unchanged)
│
├── api/                                        # API reference (unchanged)
│
├── structure/                                  # NEW: Repository structure docs
│   └── REPOSITORY_REORGANIZATION_PLAN.md       # This document
│
├── DEPENDENCIES.md                             # (unchanged)
├── HELP.md                                     # (unchanged)
├── README.md                                   # (unchanged)
├── USAGE.md                                    # (unchanged)
└── toc.yml                                     # (unchanged)

# REMOVED (empty/placeholder folders):
# - docs/uml/                  (only README.md)
# - docs/diagrams/             (only README.md)
# - docs/docfx/                (only README.md)
# - docs/generated/            (empty)
# - docs/changelogs/           (merge into status/)
# - docs/analysis/             (merge into reference/)
```

### Test Structure (Proposed)

```
tests/
├── Meridian.Tests/
│   ├── Application/                            # NEW: Mirror Application layer
│   │   ├── Backfill/                           # (MOVED from Backfill/)
│   │   │   ├── ScheduledBackfillTests.cs
│   │   │   ├── HistoricalProviderContractTests.cs
│   │   │   ├── RateLimiterTests.cs
│   │   │   └── BackfillWorkerServiceTests.cs
│   │   ├── Config/                             # (MOVED from Config/)
│   │   │   └── ConfigurationUnificationTests.cs
│   │   ├── Credentials/                        # (unchanged)
│   │   ├── Indicators/                         # (unchanged)
│   │   ├── Monitoring/                         # (unchanged)
│   │   ├── Pipeline/                           # (unchanged)
│   │   └── Services/                           # NEW: For service tests at root
│   │       ├── GracefulShutdownTests.cs        # (MOVED from root)
│   │       └── CliModeResolverTests.cs         # (MOVED from root)
│   │
│   ├── Domain/                                 # NEW: Mirror Domain layer
│   │   ├── Collectors/                         # NEW: Collector tests
│   │   │   ├── TradeDataCollectorTests.cs      # (MOVED from root)
│   │   │   ├── MarketDepthCollectorTests.cs    # (MOVED from root)
│   │   │   └── QuoteCollectorTests.cs          # (MOVED from root)
│   │   └── Models/                             # (MOVED from Models/)
│   │       ├── AggregateBarTests.cs
│   │       ├── HistoricalBarTests.cs
│   │       ├── TradeModelTests.cs              # (MOVED from root)
│   │       ├── OrderBookLevelTests.cs          # (MOVED from root)
│   │       └── BboQuotePayloadTests.cs         # (MOVED from root)
│   │
│   ├── Infrastructure/                         # Mirror Infrastructure layer
│   │   ├── Providers/                          # (unchanged)
│   │   ├── Resilience/                         # NEW
│   │   │   └── WebSocketResiliencePolicyTests.cs # (MOVED from root)
│   │   ├── SymbolSearch/                       # (unchanged)
│   │   └── Shared/                             # (unchanged)
│   │       ├── TempDirectoryFixture.cs         # (MOVED from Infrastructure/)
│   │       └── SymbolNormalizationTests.cs     # (MOVED from Infrastructure/)
│   │
│   ├── Storage/                                # (unchanged)
│   │
│   ├── Integration/                            # (unchanged)
│   │
│   └── Serialization/                          # (unchanged)
│
└── Meridian.FSharp.Tests/           # (unchanged)
```

### GitHub Structure (Proposed)

```
.github/
├── workflows/                                  # (unchanged)
├── actions/                                    # (unchanged)
├── agents/                                     # (unchanged)
├── prompts/                                    # (unchanged)
├── ISSUE_TEMPLATE/                             # (unchanged)
├── PULL_REQUEST_TEMPLATE.md                    # (unchanged)
├── QUICKSTART.md                               # (unchanged)
├── dependabot.yml                              # (unchanged)
├── labeler.yml                                 # (unchanged)
├── labels.yml                                  # (unchanged)
├── markdown-link-check-config.json             # (unchanged)
├── spellcheck-config.yml                       # (unchanged)
└── WORKFLOW_IMPROVEMENTS.md                    # (unchanged)

# MOVED to docs/ai/copilot/:
# - copilot-instructions.md
```

---

## C. Change Map (File Movement Plan)

### Priority 1: Provider Reorganization (High Impact, Low Risk)

#### Create Streaming Provider Grouping

| Change | From | To |
|--------|------|-----|
| Create folder | - | `src/Meridian/Infrastructure/Providers/Streaming/` |
| Move folder | `Infrastructure/Providers/Alpaca/` | `Infrastructure/Providers/Streaming/Alpaca/` |
| Move folder | `Infrastructure/Providers/Polygon/` | `Infrastructure/Providers/Streaming/Polygon/` |
| Move folder | `Infrastructure/Providers/NYSE/` | `Infrastructure/Providers/Streaming/NYSE/` |
| Move folder | `Infrastructure/Providers/InteractiveBrokers/` | `Infrastructure/Providers/Streaming/InteractiveBrokers/` |

**Reason:** Clearly distinguishes streaming (real-time) providers from historical (backfill) providers.

**StockSharp Special Case:**
| Change | From | To |
|--------|------|-----|
| Move folder | `Infrastructure/Providers/StockSharp/` | `Infrastructure/Providers/Streaming/StockSharp/` |
| Keep only | `StockSharpMarketDataClient.cs`, `StockSharpConnectorFactory.cs`, `StockSharpConnectorCapabilities.cs`, `Converters/` | In Streaming/StockSharp/ |

**Risk:** Low - folder moves only, no code changes

#### Create Historical Provider Grouping

| Change | From | To |
|--------|------|-----|
| Create folder | - | `src/Meridian/Infrastructure/Providers/Historical/` |
| Move file | `Backfill/IHistoricalDataProvider.cs` | `Historical/IHistoricalDataProvider.cs` |
| Move file | `Backfill/BaseHistoricalDataProvider.cs` | `Historical/BaseHistoricalDataProvider.cs` |
| Move file | `Backfill/CompositeHistoricalDataProvider.cs` | `Historical/CompositeHistoricalDataProvider.cs` |

**Individual Historical Providers:**
| From | To |
|------|-----|
| `Backfill/AlpacaHistoricalDataProvider.cs` | `Historical/Alpaca/AlpacaHistoricalDataProvider.cs` |
| `Backfill/AlphaVantageHistoricalDataProvider.cs` | `Historical/AlphaVantage/AlphaVantageHistoricalDataProvider.cs` |
| `Backfill/FinnhubHistoricalDataProvider.cs` | `Historical/Finnhub/FinnhubHistoricalDataProvider.cs` |
| `Backfill/IBHistoricalDataProvider.cs` | `Historical/InteractiveBrokers/IBHistoricalDataProvider.cs` |
| `Backfill/NasdaqDataLinkHistoricalDataProvider.cs` | `Historical/NasdaqDataLink/NasdaqDataLinkHistoricalDataProvider.cs` |
| `Backfill/PolygonHistoricalDataProvider.cs` | `Historical/Polygon/PolygonHistoricalDataProvider.cs` |
| `Backfill/StockSharpHistoricalDataProvider.cs` | `Historical/StockSharp/StockSharpHistoricalDataProvider.cs` |
| `Backfill/StooqHistoricalDataProvider.cs` | `Historical/Stooq/StooqHistoricalDataProvider.cs` |
| `Backfill/TiingoHistoricalDataProvider.cs` | `Historical/Tiingo/TiingoHistoricalDataProvider.cs` |
| `Backfill/YahooFinanceHistoricalDataProvider.cs` | `Historical/YahooFinance/YahooFinanceHistoricalDataProvider.cs` |

**Support Infrastructure:**
| From | To |
|------|-----|
| `Backfill/RateLimiter.cs` | `Historical/RateLimiting/RateLimiter.cs` |
| `Backfill/ProviderRateLimitTracker.cs` | `Historical/RateLimiting/ProviderRateLimitTracker.cs` |
| `Backfill/BackfillWorkerService.cs` | `Historical/Queue/BackfillWorkerService.cs` |
| `Backfill/BackfillJobManager.cs` | `Historical/Queue/BackfillJobManager.cs` |
| `Backfill/BackfillJob.cs` | `Historical/Queue/BackfillJob.cs` |
| `Backfill/BackfillRequestQueue.cs` | `Historical/Queue/BackfillRequestQueue.cs` |
| `Backfill/PriorityBackfillQueue.cs` | `Historical/Queue/PriorityBackfillQueue.cs` |
| `Backfill/DataGapAnalyzer.cs` | `Historical/GapAnalysis/DataGapAnalyzer.cs` |
| `Backfill/DataGapRepair.cs` | `Historical/GapAnalysis/DataGapRepair.cs` |
| `Backfill/DataQualityMonitor.cs` | `Historical/GapAnalysis/DataQualityMonitor.cs` |
| `Backfill/SymbolResolution/` | `Historical/SymbolResolution/` |

**Reason:** Renames "Backfill" (action) to "Historical" (capability type), mirrors Streaming structure.

**Structural Benefit:** Provider types are now visually parallel and discoverable.

**Risk:** Medium - requires solution file update, potential build order adjustment

---

### Priority 2: Scheduling Layer Correction (Medium Impact, Low Risk)

| Change | From | To |
|--------|------|-----|
| Move file | `Infrastructure/Providers/Backfill/Scheduling/BackfillScheduleManager.cs` | `Application/Scheduling/BackfillScheduleManager.cs` |
| Move file | `Infrastructure/Providers/Backfill/Scheduling/ScheduledBackfillService.cs` | `Application/Scheduling/ScheduledBackfillService.cs` |
| Move file | `Infrastructure/Providers/Backfill/Scheduling/BackfillExecutionLog.cs` | `Application/Scheduling/BackfillExecutionLog.cs` |
| Move file | `Infrastructure/Providers/Backfill/Scheduling/BackfillSchedule.cs` | `Application/Scheduling/BackfillSchedule.cs` |
| Move file | `Infrastructure/Providers/Backfill/Scheduling/CronExpressionParser.cs` | `Application/Scheduling/CronExpressionParser.cs` |
| Delete folder | `Infrastructure/Providers/Backfill/Scheduling/` | - |

**Reason:** Scheduling is application-layer orchestration, not infrastructure.

**Structural Benefit:** Layer boundaries are now accurate.

**Risk:** Low - files move up one layer, no cross-project moves

---

### Priority 3: HTTP Endpoint Consolidation (Medium Impact, Low Risk)

| Change | From | To |
|--------|------|-----|
| Rename folder | `Application/UI/` | `Application/Http/` |
| Create folder | - | `Application/Http/Endpoints/` |
| Move file | `Application/Monitoring/DataQuality/DataQualityEndpoints.cs` | `Application/Http/Endpoints/DataQualityEndpoints.cs` |

**Reason:** HTTP endpoints belong together, not scattered across business logic folders.

**Structural Benefit:** All HTTP concerns are colocated.

**Risk:** Low - single file move

---

### Priority 4: Test Structure Alignment (Medium Impact, Medium Risk)

| From | To |
|------|-----|
| `tests/.../TradeDataCollectorTests.cs` | `tests/.../Domain/Collectors/TradeDataCollectorTests.cs` |
| `tests/.../MarketDepthCollectorTests.cs` | `tests/.../Domain/Collectors/MarketDepthCollectorTests.cs` |
| `tests/.../QuoteCollectorTests.cs` | `tests/.../Domain/Collectors/QuoteCollectorTests.cs` |
| `tests/.../TradeModelTests.cs` | `tests/.../Domain/Models/TradeModelTests.cs` |
| `tests/.../OrderBookLevelTests.cs` | `tests/.../Domain/Models/OrderBookLevelTests.cs` |
| `tests/.../BboQuotePayloadTests.cs` | `tests/.../Domain/Models/BboQuotePayloadTests.cs` |
| `tests/.../GracefulShutdownTests.cs` | `tests/.../Application/Services/GracefulShutdownTests.cs` |
| `tests/.../CliModeResolverTests.cs` | `tests/.../Application/Services/CliModeResolverTests.cs` |
| `tests/.../ConfigValidatorTests.cs` | `tests/.../Application/Config/ConfigValidatorTests.cs` |
| `tests/.../WebSocketResiliencePolicyTests.cs` | `tests/.../Infrastructure/Resilience/WebSocketResiliencePolicyTests.cs` |
| `tests/.../AlpacaQuoteRoutingTests.cs` | `tests/.../Infrastructure/Providers/AlpacaQuoteRoutingTests.cs` |
| `tests/.../PrometheusMetricsTests.cs` | `tests/.../Application/Monitoring/PrometheusMetricsTests.cs` |
| `tests/.../CompositePublisherTests.cs` | `tests/.../Application/Pipeline/CompositePublisherTests.cs` |
| `tests/.../ConnectionRetryIntegrationTests.cs` | `tests/.../Integration/ConnectionRetryIntegrationTests.cs` |
| `tests/.../FilePermissionsServiceTests.cs` | `tests/.../Storage/FilePermissionsServiceTests.cs` |

**Reason:** Tests should mirror source structure for discoverability.

**Structural Benefit:** Finding tests for a given source file is now predictable.

**Risk:** Medium - test project file updates required

---

### Priority 5: Documentation Consolidation (Low Impact, Low Risk)

#### Create Getting Started Zone

| From | To |
|------|-----|
| `docs/guides/getting-started.md` | `docs/getting-started/setup.md` |
| `docs/guides/configuration.md` | `docs/getting-started/configuration.md` |
| `docs/guides/troubleshooting.md` | `docs/getting-started/troubleshooting.md` |

#### Create Operations Zone

| From | To |
|------|-----|
| `docs/guides/operator-runbook.md` | `docs/operations/operator-runbook.md` |
| `docs/guides/portable-data-packager.md` | `docs/operations/portable-data-packager.md` |
| `docs/guides/msix-packaging.md` | `docs/operations/msix-packaging.md` |

#### Create Development Zone

| From | To |
|------|-----|
| `docs/guides/provider-implementation.md` | `docs/development/provider-implementation.md` |
| `docs/guides/uwp-development-roadmap.md` | `docs/development/uwp-development-roadmap.md` |
| `docs/guides/uwp-release-checklist.md` | `docs/development/uwp-release-checklist.md` |
| `docs/guides/github-actions-summary.md` | `docs/development/github-actions-summary.md` |
| `docs/guides/github-actions-testing.md` | `docs/development/github-actions-testing.md` |
| `docs/guides/project-context.md` | `docs/development/project-context.md` |

#### Consolidate AI Assistant Docs

| From | To |
|------|-----|
| `docs/ai-assistants/CLAUDE.providers.md` | `docs/ai/claude/CLAUDE.providers.md` |
| `docs/ai-assistants/CLAUDE.storage.md` | `docs/ai/claude/CLAUDE.storage.md` |
| `docs/ai-assistants/CLAUDE.fsharp.md` | `docs/ai/claude/CLAUDE.fsharp.md` |
| `docs/ai-assistants/CLAUDE.testing.md` | `docs/ai/claude/CLAUDE.testing.md` |
| `.github/copilot-instructions.md` | `docs/ai/copilot/instructions.md` |

#### Merge Redundant Folders

| Action | Folder |
|--------|--------|
| Merge | `docs/changelogs/CHANGES_SUMMARY.md` → `docs/status/CHANGES_SUMMARY.md` |
| Merge | `docs/analysis/DUPLICATE_CODE_ANALYSIS.md` → `docs/reference/DUPLICATE_CODE_ANALYSIS.md` |
| Delete | `docs/uml/` (placeholder) |
| Delete | `docs/diagrams/` (placeholder) |
| Delete | `docs/docfx/` (placeholder) |
| Delete | `docs/generated/` (empty) |
| Delete | `docs/guides/` (after migration) |
| Delete | `docs/ai-assistants/` (after migration) |
| Delete | `docs/changelogs/` (after merge) |
| Delete | `docs/analysis/` (after merge) |

**Reason:** Documentation is now organized by audience (new users, operators, developers) rather than content type.

**Risk:** Low - markdown files only, no code impact

---

### Priority 6: Build Tooling Consolidation (Low Impact, Medium Risk)

| From | To |
|------|-----|
| `build-system/` | `build/python/` |
| `scripts/` | `build/scripts/` |
| `tools/` | `build/dotnet/` |
| `scripts/generate-diagrams.mjs` | `build/node/generate-diagrams.mjs` |
| `scripts/generate-icons.mjs` | `build/node/generate-icons.mjs` |

**Reason:** All build/tooling concerns are now under one roof.

**Structural Benefit:** Clearer separation between source code and build infrastructure.

**Risk:** Medium - Makefile paths need updating

---

## D. Structural Principles Introduced

### Principle 1: Provider Type Parallelism
> "Streaming and Historical providers are parallel concerns at the same level of abstraction."

**Implementation:** `Providers/Streaming/` and `Providers/Historical/` are siblings.

### Principle 2: Layer Integrity
> "Infrastructure implements technical concerns. Application orchestrates business concerns."

**Implementation:** Scheduling logic moves from Infrastructure to Application.

### Principle 3: HTTP Cohesion
> "All HTTP endpoints belong together, separate from the business logic they expose."

**Implementation:** `Application/Http/Endpoints/` contains all endpoint files.

### Principle 4: Test Structure Mirroring
> "Test folder structure mirrors source folder structure exactly."

**Implementation:** `tests/Domain/Collectors/` mirrors `src/Domain/Collectors/`.

### Principle 5: Documentation by Audience
> "Documentation is organized by who reads it, not by what it contains."

**Implementation:** `docs/getting-started/` (new users), `docs/operations/` (operators), `docs/development/` (developers).

### Principle 6: AI Context Consolidation
> "All AI assistant context lives in one location for easy discovery and maintenance."

**Implementation:** `docs/ai/` contains all AI-related documentation.

### Principle 7: Build Tooling Isolation
> "Build and tooling code is separate from application source code."

**Implementation:** `build/` contains all build tooling (Python, Node.js, .NET, scripts).

### Principle 8: Semantic Folder Naming
> "Folder names describe what the code IS or DOES, not how it's used."

**Implementation:** "Historical" (capability) replaces "Backfill" (action).

---

## E. Migration Safety Checklist

### Pre-Migration Verification

- [ ] Run full build: `dotnet build -c Release`
- [ ] Run all tests: `dotnet test`
- [ ] Verify git status is clean
- [ ] Create backup branch: `git checkout -b backup/pre-reorganization`
- [ ] Document current file count: `find src -name "*.cs" | wc -l`

### Migration Execution (Per Priority)

For each priority section:

1. **Create destination folders first**
   ```bash
   mkdir -p src/Meridian/Infrastructure/Providers/Streaming
   mkdir -p src/Meridian/Infrastructure/Providers/Historical
   # etc.
   ```

2. **Use git mv for all moves** (preserves history)
   ```bash
   git mv src/Meridian/Infrastructure/Providers/Alpaca \
          src/Meridian/Infrastructure/Providers/Streaming/Alpaca
   ```

3. **Verify build after each priority**
   ```bash
   dotnet build -c Release
   ```

4. **Commit each priority separately**
   ```bash
   git commit -m "refactor(structure): reorganize streaming providers into Providers/Streaming/"
   ```

### Post-Migration Verification

- [ ] Run full build: `dotnet build -c Release`
- [ ] Run all tests: `dotnet test`
- [ ] Verify file count matches pre-migration
- [ ] Update solution file if needed
- [ ] Update `CLAUDE.md` Repository Structure section
- [ ] Update `.csproj` files if any include paths changed
- [ ] Run `make doctor` to verify tooling still works
- [ ] Verify CI/CD workflows complete successfully

### Rollback Procedure

If issues arise:
```bash
git checkout backup/pre-reorganization
git branch -D main  # or current branch
git checkout -b main
```

---

## F. Files NOT to Move

The following files are correctly placed and should NOT be moved:

| File/Folder | Reason |
|-------------|--------|
| `CLAUDE.md` (root) | Standard location for AI context |
| `src/Meridian/Domain/` | Clean domain layer |
| `src/Meridian/Storage/` | Well-organized storage layer |
| `src/Meridian/Integrations/Lean/` | Appropriate location |
| `src/Meridian.Contracts/` | Clean DTO project |
| `src/Meridian.FSharp/` | Clean F# project |
| `Meridian.sln` | Standard location |
| `config/` | Standard location |
| `deploy/` | Standard location |

---

## G. Known Limitations

1. **Solution file updates** - Moving folders may require manual `.sln` adjustments
2. **Makefile paths** - Build consolidation requires Makefile updates
3. **CI/CD workflows** - May reference old paths
4. **CLAUDE.md** - Repository Structure section needs update after migration

---

## H. Appendix: Rejected Alternatives

### Alternative 1: Microservices Split
**Rejected:** ADR-003 explicitly chose monolithic architecture. Structure changes should not imply decomposition.

### Alternative 2: Feature Folders
**Rejected:** Provider-based organization is more appropriate for a data collection system than feature-based.

### Alternative 3: StockSharp as Top-Level Project
**Rejected:** Would require code changes (project references). Out of scope.

### Alternative 4: Merge UI Projects
**Rejected:** Would require code changes. Out of scope.

---

*Document prepared as part of repository reorganization initiative.*
*No code changes are proposed or required.*
