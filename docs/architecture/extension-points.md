# Extension Points & Module Patterns

**Status:** Active
**Owner:** Core Team
**Reviewed:** 2026-06-16

This document inventories every place Meridian discovers, registers, or composes a unit of
behaviour — providers, storage sinks, strategies, feature modules, workflows, report
templates, risk rules, and the composition root itself. It is the driving reference for the
**modularity & extensibility consolidation** effort: the goal is to converge today's several
divergent "module" abstractions onto one coherent contract while preserving observable
behaviour.

It complements:

- [`layer-boundaries.md`](layer-boundaries.md) — allowed dependency directions.
- [`module-map.md`](module-map.md) — layer ownership and runtime flow.

## Why this matters

Meridian is already a layered modular monolith with enforced boundaries and a single
composition root. The friction is **not** a lack of modularity; it is that the same job —
"declare a unit of behaviour, discover it, validate it, register it, describe its
capabilities" — is solved **five or six different ways**. New contributors must learn each
pattern, and several behaviour catalogs are hard-coded so they cannot be extended without
editing core types.

## Extension-point inventory

Maturity legend:

- **Pluggable** — attribute/interface contract with automatic discovery; add behaviour
  without editing a central list.
- **Registered** — interface contract, but additions require manual DI wiring.
- **Hard-coded** — behaviour lives in a fixed `Dictionary`/array; extension means editing
  the catalog type.

| Extension point | Contract | Discovery / registration | Key files | Maturity |
| --- | --- | --- | --- | --- |
| Composition (service features) | `IServiceFeatureRegistration` | Static ordered array in the composition root, gated by `CompositionOptions` | `src/Meridian.Application/Composition/ServiceCompositionRoot.cs`, `src/Meridian.Application/Composition/Features/IServiceFeatureRegistration.cs` | Registered |
| Data providers | `[DataSource]` + `IDataSource`/`IMarketDataClient`; `IProviderModule` for multi-capability | `DataSourceRegistry.DiscoverFromAssemblies` (reflection) + `PluginLoaderService` (DLL scan) + `ProviderModuleLoader` | `src/Meridian.ProviderSdk/DataSourceAttribute.cs`, `src/Meridian.ProviderSdk/DataSourceRegistry.cs`, `src/Meridian.ProviderSdk/PluginLoaderService.cs`, `src/Meridian.Infrastructure/Adapters/Core/IProviderModule.cs` | **Pluggable** |
| Storage sinks | `[StorageSink]` + `IStorageSink` | `StorageSinkRegistry.DiscoverFromAssemblies`; activated by `Storage.Sinks` config list | `src/Meridian.Storage/StorageSinkAttribute.cs`, `src/Meridian.Storage/StorageSinkRegistry.cs` | **Pluggable** |
| Backtest strategies | `IBacktestStrategy` + `[StrategyParameter]` | `AssemblyLoadContext`-isolated load of external assemblies | `src/Meridian.Backtesting/Plugins/StrategyPluginLoader.cs` | **Pluggable** (ALC) |
| Desktop feature modules | `IDesktopFeatureModule` | **Static hard-coded array** iterated at startup | `src/Meridian.Wpf/Features/IDesktopFeatureModule.cs`, `src/Meridian.Wpf/Features/DesktopFeatureModuleRegistry.cs` | Registered |
| Workflow definitions | `IWorkflowDefinitionProvider` | Aggregated via DI; built-in provider + manual additions | `src/Meridian.Ui.Shared/Workflows/IWorkflowDefinitionProvider.cs`, `src/Meridian.Ui.Shared/Workflows/WorkflowRegistry.cs` | Registered |
| Reporting templates | `IReportingTemplateCatalog` | Hard-coded dictionary | `src/Meridian.Reporting/DefaultReportingTemplateCatalog.cs` | Hard-coded |
| Evidence templates | (none) | Hard-coded `IReadOnlyList<>` | `src/Meridian.Ui.Shared/Evidence/EvidenceTemplateRegistry.cs` | Hard-coded |
| Risk rules | `IRiskRule` | Manual DI registration | `src/Meridian.Risk/IRiskRule.cs` | Registered |

## Observations

1. **The strongest pattern already exists.** The `[DataSource]` →
   `DataSourceRegistry.DiscoverFromAssemblies` → `PluginLoaderService` trio (mirrored by
   `[StorageSink]`) is a complete, tested attribute-discovery model. It is the natural
   blueprint for a reusable `ExtensionRegistry<TContract, TAttribute>`.
2. **Capability declaration is reinvented per pattern.** `IProviderModule.Capabilities`,
   `IDesktopFeatureModule.DeclareCapabilities()`, and `FeatureCapabilityDescriptor` all
   express "what this unit offers" in incompatible shapes.
3. **Ordering and validation are ad hoc.** The composition root encodes order as a comment
   and array position; `IProviderModule` exposes `ValidateAsync`; other patterns have
   neither.
4. **Three catalogs are hard-coded** (reporting templates, evidence templates, risk-rule
   wiring) — these are the concrete "cannot extend without editing core" pain points.

## Convergence target

One canonical module contract (working name `IMeridianModule`) plus a reusable
`ExtensionRegistry<TContract, TAttribute>`, both housed in a new low-level
`Meridian.Modularity` library (depends only on `Contracts` + DI abstractions). Existing
contracts are **adapted, not rewritten**: `IServiceFeatureRegistration`, `IProviderModule`,
and `IDesktopFeatureModule` implementations are wrapped so the composition root and the WPF
registry delegate to one ordered loader. `CompositionOptions` presets remain the gating
mechanism. Hard-coded catalogs migrate to the shared registry one at a time, preserving the
exact current built-in set.

See the approved refactor plan for the wave-by-wave sequence.

## Known coupling drifts (remediation targets)

These dependency edges contradict [`layer-boundaries.md`](layer-boundaries.md) and are
scheduled for repair as part of the consolidation. They are recorded here so they are
visible and tracked, not silently tolerated.

| Drift | Evidence | Intended state |
| --- | --- | --- |
| `Meridian.Execution` → `Meridian.Application` | `ProjectReference` in `src/Meridian.Execution/Meridian.Execution.csproj` | Execution depends only on layers at/below it; the services it needs are exposed via an abstraction owned in Contracts/Domain. |
| `Meridian.Risk` → `Meridian.Execution` (→ Application, transitively) | `ProjectReference` in `src/Meridian.Risk/Meridian.Risk.csproj` | Risk stays below Application once the Execution edge is inverted. |
| `Meridian.Storage` → `Meridian.Ledger` | `ProjectReference` in `src/Meridian.Storage/Meridian.Storage.csproj` | Ledger persistence adapters live in `Meridian.Ledger`; Storage exposes seams, Ledger consumes them. |
| `Meridian.Ui.Shared` wide fan-in (~19 projects) | `ProjectReference` list in `src/Meridian.Ui.Shared/Meridian.Ui.Shared.csproj` | Endpoint surface split into domain-aligned modules registered through the unified module loader; routes unchanged. |

When each drift is repaired, add a matching guardrail test under
`tests/Meridian.Tests/Architecture/LayerBoundaryTests.cs` so it cannot regress.
</content>
