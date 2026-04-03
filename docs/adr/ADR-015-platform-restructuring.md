# ADR-015: Platform Restructuring — Meridian → Meridian

## Status
Superseded by [ADR-015: Strategy Execution Contract](015-strategy-execution-contract.md) and [ADR-016: Platform Architecture Migration Mandate](016-platform-architecture-migration.md)

## Date
2026-03-18

## Context

The project has evolved from a focused market data collection tool into a broader
algorithmic trading platform encompassing data collection, backtesting, live execution,
and strategy lifecycle management. The original naming (`Meridian.*`) and
the original dependency graph (documented in the shared project context) only captured
one pillar of what is now a four-pillar platform.

The decisions in this document were subsequently refined and split into two separate
accepted ADRs:

- **[ADR-015: Strategy Execution Contract](015-strategy-execution-contract.md)** —
  defines `IOrderGateway`, `IExecutionContext`, and the paper-first execution model.
- **[ADR-016: Platform Architecture Migration Mandate](016-platform-architecture-migration.md)** —
  defines the four named pillars (Data Collection, Backtesting, Execution, Strategies),
  allowed dependency rules, and forbidden cross-pillar couplings.

The proposed rename of `Meridian.*` to a shorter prefix was **not carried out**; all
assembly and namespace names remain `Meridian.*` to preserve CI/CD pipelines, NuGet
references, and existing documentation. ADR-016 documents this naming decision.

This document is retained for historical context only.

## Decision

### 1. Rename: `Meridian` → `Meridian`

**Why "Meridian":**
- Concise (8 chars) — shorter than `Meridian` (19 chars)
- Evokes navigation, direction, and precision — fitting for a quantitative trading platform
- Unique enough to avoid namespace collisions
- Works well as a namespace prefix: `Meridian.Core`, `Meridian.Execution`, etc.

### 2. Project Mapping

| Old Project | New Project | Notes |
|-------------|-------------|-------|
| `Meridian` | `Meridian` | Entry point / CLI / web host |
| `Meridian.Application` | `Meridian.Application` | Commands, pipeline, monitoring |
| `Meridian.Domain` | `Meridian.Domain` | Collectors, event publishers |
| `Meridian.Core` | `Meridian.Core` | Config, exceptions, logging |
| `Meridian.Contracts` | `Meridian.Contracts` | API models, domain types, interfaces |
| `Meridian.Infrastructure` | `Meridian.Infrastructure` | Provider adapters |
| `Meridian.Storage` | `Meridian.Storage` | WAL, sinks, archival |
| `Meridian.ProviderSdk` | `Meridian.ProviderSdk` | Provider contracts |
| `Meridian.FSharp` | `Meridian.FSharp` | F# domain models |
| `Meridian.Backtesting` | `Meridian.Backtesting` | Backtest engine |
| `Meridian.Backtesting.Sdk` | `Meridian.Backtesting.Sdk` | Strategy SDK |
| `Meridian.Ui` | `Meridian.Ui` | Web UI entry point |
| `Meridian.Ui.Shared` | `Meridian.Ui.Shared` | Shared endpoints |
| `Meridian.Ui.Services` | `Meridian.Ui.Services` | Desktop UI services |
| `Meridian.Wpf` | `Meridian.Wpf` | WPF desktop app |
| `Meridian.McpServer` | `Meridian.McpServer` | MCP server |
| `Meridian.Mcp` | `Meridian.Mcp` | Legacy MCP |
| *(new)* | `Meridian.Execution` | Order management, execution gateway |
| *(new)* | `Meridian.Execution.Sdk` | Execution contracts |
| *(new)* | `Meridian.Risk` | Risk management |

### 3. New Projects for Strategy Execution

**Meridian.Execution.Sdk** (Tier 0 — contracts only):
- `IExecutionGateway` — broker adapter contract for order routing
- `IOrderManager` — order lifecycle management
- `IPositionTracker` — real-time position state
- `ExecutionReport` — fill/rejection/cancel reports
- `OrderRequest` — new order, modify, cancel requests
- `ExecutionMode` enum — `Paper`, `Live`, `Simulation`

**Meridian.Execution** (Tier 2 — implementation):
- `OrderManagementSystem` — central OMS with order state machine
- `PaperTradingGateway` — simulated execution for testing
- `ExecutionRouter` — routes orders to appropriate gateway
- `PositionManager` — tracks live positions with P&L
- `ExecutionPipeline` — bounded channel for execution events

**Meridian.Risk** (Tier 2 — risk management):
- `IRiskManager` — pre-trade risk check contract
- `PositionLimitRule` — max position size per symbol
- `DrawdownCircuitBreaker` — halt trading on drawdown threshold
- `OrderRateThrottle` — order frequency limits
- `RiskAggregator` — portfolio-level risk metrics

### 4. Migration Strategy

**Phase 1 (this PR):** Rename everything
- Rename all directories, project files, namespaces
- Update solution file, build configs, CI/CD
- Scaffold new Execution and Risk projects

**Phase 2 (future):** Implement execution engine
- Build out OMS, execution gateways, risk management
- Integrate with existing backtesting infrastructure
- Add live trading mode to WPF and web UI

## Consequences

### Positive
- Name accurately reflects platform scope
- Clean namespace hierarchy for future growth
- Clear separation between data, strategy, and execution concerns
- Shorter namespace prefix reduces visual noise

### Negative
- Large diff affecting 773+ files
- External references (Docker images, CI/CD, docs) all need updating
- Git history becomes harder to follow across the rename
- Any in-flight branches will need rebasing

### Risks Mitigated
- Using `git mv` preserves file history tracking
- Mechanical find-replace for namespace changes (low risk of logic bugs)
- Build verification after rename ensures nothing is broken
