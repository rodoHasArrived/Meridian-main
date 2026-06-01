# ADR-016: Platform Architecture Migration Mandate

**Status:** Accepted
**Date:** 2026-03-18
**Deciders:** Core Team

## Context

The project has evolved from a focused market data collection tool into a broader
algorithmic trading platform encompassing data collection, backtesting, live execution,
and strategy lifecycle management. The original naming (`Meridian.*`) and
the original dependency graph (documented in the shared project context) only captured
one pillar of what is now a four-pillar platform.

Without an explicit architecture mandate that names the pillars, defines their allowed
dependencies, and prohibits cross-pillar contamination, the codebase will accrete
ad-hoc couplings as new capabilities land — the same pattern that caused LEAN and
Backtrader to accumulate monolithic `cerebro`-style orchestrators over time.

## Decision

The platform is divided into **four named pillars** plus shared cross-cutting layers:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                          UI Layer                                        │
│         (Meridian.Wpf, .Ui, .Ui.Services, .Ui.Shared)        │
└─────┬───────────────┬──────────────────┬────────────────────────────────┘
      │               │                  │
      ▼               ▼                  ▼
┌──────────┐  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────┐
│  DATA    │  │  BACKTESTING │  │  EXECUTION   │  │     STRATEGIES       │
│COLLECTION│  │              │  │              │  │                      │
│          │  │ .Backtesting │  │ .Execution   │  │ .Strategies          │
│ .Infra.. │  │ .Backtesting │  │              │  │                      │
│ .Domain  │  │   .Sdk       │  │              │  │                      │
│ .App..   │  │              │  │              │  │                      │
│ .Storage │  │              │  │              │  │                      │
│ .Provider│  │              │  │              │  │                      │
│   Sdk    │  │              │  │              │  │                      │
└──────────┘  └──────────────┘  └──────────────┘  └──────────────────────┘
      │               │                  │                   │
      └───────────────┴──────────────────┴───────────────────┘
                                  │
                     ┌────────────▼────────────┐
                     │     SHARED FOUNDATION    │
                     │  .Contracts  .Core .FSharp│
                     └──────────────────────────┘
```

### Pillar Definitions

| Pillar | Projects | Responsibility |
|--------|----------|----------------|
| **Data Collection** | `Meridian`, `.Application`, `.Domain`, `.Infrastructure`, `.Storage`, `.ProviderSdk` | Streaming ingestion, historical backfill, storage, data quality monitoring |
| **Backtesting** | `Meridian.Backtesting`, `.Backtesting.Sdk` | Historical replay, fill simulation, portfolio tracking, strategy metrics |
| **Execution** | `Meridian.Execution` | Live and simulated order routing, broker adapters, `IOrderGateway` |
| **Strategies** | `Meridian.Strategies` | Strategy lifecycle management, run archive, promotion workflow |
| **Shared Foundation** | `Meridian.Contracts`, `.Core`, `.FSharp` | Cross-cutting types, configuration, F# domain models |
| **UI** | `Meridian.Wpf`, `.Ui`, `.Ui.Services`, `.Ui.Shared` | Desktop and web surfaces |

### Allowed Dependencies (per-pillar)

```
Shared Foundation  ←  all pillars depend on this
Data Collection    ←  depends on Shared Foundation only
Backtesting        ←  depends on Shared Foundation + Data Collection (read-only storage access)
Execution          ←  depends on Shared Foundation + Data Collection (live feed access)
Strategies         ←  depends on Shared Foundation + Backtesting.Sdk + Execution interfaces only
UI                 ←  depends on all pillars via service layer (Ui.Services acts as anti-corruption layer)
```

### Forbidden Dependencies (flag as CRITICAL violations)

| From | To | Reason |
|------|----|--------|
| `Backtesting.*` | `Execution.*` | Backtesting is simulation-only; execution concepts must not leak in |
| `Execution.*` | `Backtesting.*` | Execution must not depend on simulation infrastructure |
| `Strategies.*` | Any concrete `Execution.*` type | Strategies depend only on `IOrderGateway` / `IExecutionContext` interfaces |
| `Data Collection` | `Strategies.*` or `Execution.*` | Data layer is infrastructure; it must not be strategy-aware |
| `Shared Foundation` | Any pillar | Contracts/Core are leaves — no upstream pillar dependencies |
| Any pillar | `Meridian.Wpf` | WPF is a UI host, not a library |
| `Ui.Services` / `Ui.Shared` | WPF host types | Reverse dependency (pre-existing rule, reaffirmed) |

## Implementation Links

| Component | Location | Purpose |
|-----------|----------|---------|
| Execution project | `src/Meridian.Execution/` | New pillar — order gateway and broker adapters |
| Strategies project | `src/Meridian.Strategies/` | New pillar — strategy lifecycle and run archive |
| Solution file | `Meridian.sln` | Solution folders mirror the four pillars |
| IOrderGateway | `src/Meridian.Execution/Interfaces/IOrderGateway.cs` | ADR-015 canonical interface |
| IStrategyLifecycle | `src/Meridian.Strategies/Interfaces/IStrategyLifecycle.cs` | Strategy lifecycle contract |
| StrategyRunStore | `src/Meridian.Strategies/Storage/StrategyRunStore.cs` | Run archive and history |

## Rationale

### Naming Stability

The `Meridian.*` assembly prefix is retained for all projects during the
current version to avoid breaking CI/CD pipelines, NuGet references, and
`[InternalsVisibleTo]` attributes across 27 workflows. Pillar boundaries are expressed
through solution folders and enforced via dependency rules — not through namespace
renames. A namespace migration (e.g., to a platform-level prefix) is deferred to a
future major version and is out of scope for this ADR.

### Pillar Isolation Over Microservices

Each pillar is a set of .NET projects within a single solution, not a separate
microservice. This keeps the deployment model simple (single-process or monorepo) while
still providing the logical separation that prevents coupling. The microservices option
(ADR-003) remains available as a future scaling path but is not required today.

### Paper-First Execution Gate

All live broker adapters in the Execution pillar are gated behind an
`ExecutionMode.Live` flag that defaults to off. The `PaperTradingGateway` is the only
adapter enabled by default, ensuring no accidental live order routing is possible
without explicit user action.

## Alternatives Considered

### Alternative 1: Keep All Code in Existing Projects

Add execution and strategy management as sub-folders within existing projects.

**Pros:** Zero project structure change; easier in the short term.

**Cons:** Namespace pollution, no enforced separation, impossible to reference just the
execution layer from an external strategy DLL, no clear ownership per pillar.

**Why rejected:** The codebase is already large enough (779 source files) that
undifferentiated growth will cause onboarding and maintenance problems within months.

### Alternative 2: Full Microservices Split

Deploy each pillar as a separate process communicating via gRPC.

**Pros:** Maximum isolation, independent scaling, language flexibility per service.

**Cons:** Dramatically increases operational complexity for a development-phase project;
introduces network latency on critical hot paths between the data feed and execution layers.

**Why rejected:** Premature. The logical boundaries established by this ADR are the
prerequisite for microservices — get the interfaces right first, split the processes
later.

## Consequences

### Positive

- New contributors immediately understand the four-pillar structure from the solution
  folder layout
- Forbidden dependency list can be machine-enforced via `NetArchTest` or a custom
  Roslyn analyzer in CI
- The Execution and Strategies pillars can be omitted from deployments that only need
  data collection (e.g., a headless server running in cloud)
- Strategy plugin DLLs only need to reference `.Backtesting.Sdk` or `.Execution`
  interfaces — not the full infrastructure graph

### Negative

- Two new projects add initial scaffolding overhead
- Contributors must learn the pillar dependency rules before adding cross-pillar
  references
- Solution file grows; Visual Studio solution folder navigation requires discipline

### Neutral

- `Meridian.*` prefix is retained — existing ADRs, CI badges, and
  documentation references remain valid

## Compliance

### Solution Organisation

The `Meridian.sln` solution currently organises all source projects under a
single `src` solution folder, which mirrors the existing project layout. Pillar
isolation is enforced through project reference rules (see Allowed/Forbidden dependency
graph above), not through separate Visual Studio solution folders. This keeps the
solution compatible with existing build pipelines and avoids restructuring the full
project hierarchy.

A future cleanup sprint may introduce per-pillar solution folders
(`[DataCollection]`, `[Backtesting]`, `[Execution]`, `[Strategies]`, `[UI]`, `[Tests]`)
if Visual Studio navigation becomes a priority. Adding those folders is a non-breaking
IDE-only change that does not affect build, test, or CI behaviour.

### Attribute Enforcement

All new pillar root types carry `[ImplementsAdr("ADR-016")]` to make ADR traceability
visible at runtime.

## References

- [ADR-001: Provider Abstraction Pattern](001-provider-abstraction.md)
- [ADR-003: Microservices Decomposition](003-microservices-decomposition.md) — future scaling option
- [ADR-005: Attribute-Based Discovery](005-attribute-based-discovery.md)
- [ADR-015: Strategy Execution Contract](015-strategy-execution-contract.md)

---

*Last Updated: 2026-03-18*
