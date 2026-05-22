# Desktop Shell Modularity & Extensibility Roadmap

**Last Updated:** 2026-05-22

## Summary

This roadmap captures eight concrete architectural improvements that make the Meridian WPF desktop
shell easier to extend, update, and maintain as new feature workspaces come online. The ideas were
produced in the brainstorm session of 2026-05-21 (`meridian-brainstorm`, Architecture / Refactoring
mode) and are sequenced here into four delivery phases that can each be merged independently.

The driving problem: adding a new workspace or page has historically required touching shared
infrastructure in multiple places — `ShellNavigationCatalog` static partial files, the
`DesktopFeatureModuleRegistry` array, and module `Register()` implementations when workspace
services are introduced. Phase 1 and Phase 2 have moved the shell toward module-owned page
contributions, convention-based view model wiring, and runtime capability gates. The remaining
architecture gap is lifecycle ownership: workspace-owned polling, streaming, and view-model state
still need a complete scoped lifetime and state-token persistence story.

Resolving these issues unlocks a sustainable pattern where each new workspace is a single,
self-contained module that contributes its own pages, services, capability declarations, state
serialization, and shell-slot content — with nothing else to edit in shared infrastructure.

**Current implementation snapshot (2026-05-22)**

- Phase 1 is implemented, with Trading/Data/Settings on module-owned descriptors and the remaining
  workspaces still using the temporary catalog fallback until their modules are split out.
- Phase 2 is implemented: convention-based page/view-model wiring, feature capability declarations,
  runtime overrides, Settings capability toggles, and focused tests are in place.
- Phase 3a is partially implemented: workspace scopes are created and disposed by
  `WorkspaceService`, docked workspace content can resolve through the active scope, and focused
  lifecycle/navigation tests pass. Remaining Phase 3a work is the service-lifetime audit, direct
  frame-navigation scope handoff, and a clean full-suite WPF run.
- Phase 3b and Phase 4 remain planned.

---

## Delivery Phases

### Phase 1 — Module Boundary Hygiene *(Effort: S — days)*

This phase creates the foundation. Everything in later phases depends on a clean, extensible module
contract and a runtime-assembled page registry.

**Goals**
- Extend `IDesktopFeatureModule` to carry page and workspace descriptions alongside DI registration.
- Replace the five static `ShellNavigationCatalog` partial files (`.Trading`, `.Research`,
  `.DataOperations`, `.Governance`, `.Workspaces`) plus the root `ShellNavigationCatalog.cs` with a
  runtime `ShellPageRegistryBuilder` that collects contributions from each feature module.
- Eliminate the need to touch shared catalog infrastructure when adding a new workspace or page.

**Scope**
- `src/Meridian.Wpf/Features/IDesktopFeatureModule.cs`
- `src/Meridian.Wpf/Features/DesktopFeatureModuleRegistry.cs`
- `src/Meridian.Wpf/Features/Trading/TradingFeatureModule.cs`
- `src/Meridian.Wpf/Features/Data/DataFeatureModule.cs`
- `src/Meridian.Wpf/Features/Settings/SettingsFeatureModule.cs`
- `src/Meridian.Wpf/Models/ShellNavigationCatalog.cs` (and all partial files)
- `src/Meridian.Wpf/Services/NavigationService.cs` (`RegisterAllPages` call site)
- New: `src/Meridian.Wpf/Shell/Services/ShellPageRegistryBuilder.cs`
- New: `src/Meridian.Wpf/Shell/Services/IShellPageRegistry.cs`
- Tests: `tests/Meridian.Wpf.Tests/` — update `ShellNavigationCatalogTests` to bootstrap via modules

**Status:** Implemented on 2026-05-21. The runtime registry and module-owned
Trading/Data/Settings descriptors are in place; Portfolio, Accounting, Reporting, and Strategy
remain on the temporary fallback path until their feature modules are split out.

**TODO checklist — Phase 1**

- [x] Extend `IDesktopFeatureModule` with two new optional methods:
  `IReadOnlyList<ShellPageDescriptor> DescribePages()` and
  `WorkspaceCapabilityDescriptor? DescribeWorkspace()`.
- [x] Create `IShellPageRegistry` with `Pages`, `WorkspaceCapabilities`, and
  `WorkspaceShells` read properties.
- [x] Create `ShellPageRegistryBuilder` that accepts `Contribute(IEnumerable<ShellPageDescriptor>)`
  and `ContributeCapability(WorkspaceCapabilityDescriptor)` calls, then emits an
  `IShellPageRegistry` via `Build()`.
- [x] Call `ShellPageRegistryBuilder` from `DesktopFeatureModuleRegistry.AddMeridianWpfFeatureModules()`
  so all module page contributions are collected before the DI container is built.
- [x] Wire `ShellNavigationCatalog`'s lazy builders (`BuildPages`, `BuildWorkspaceShells`,
  `BuildWorkspaceCapabilities`) to read from the assembled `IShellPageRegistry` instead of
  the existing static partial arrays. Keep the partial files as temporary fallback until
  each workspace migrates.
- [x] Migrate `TradingFeatureModule` to implement `DescribePages()` carrying the contents of
  `ShellNavigationCatalog.Trading.cs`. Delete the partial file when complete.
- [x] Migrate `DataFeatureModule` and `SettingsFeatureModule` in the same way.
- [x] Update `NavigationService.RegisterAllPages()` to read from `IShellPageRegistry`
  instead of `ShellNavigationCatalog.Pages` directly.
- [x] Update `ShellNavigationCatalogTests` to bootstrap via the module registry; assert page
  counts and tag uniqueness from the assembled registry.
- [x] Add a startup validator in `DesktopShellCoordinator` that logs a warning for any
  `WorkspaceCapabilityDescriptor` declared in a module but missing a matching DI registration.
- [x] Run focused WPF validation and confirm `ShellNavigationCatalogTests` and
  `NavigationServiceTests` still pass. Full unfiltered `tests/Meridian.Wpf.Tests` timed out
  locally after six minutes without a completed result; rerun in a quiet build window before using
  full-suite completion as release evidence.

**Phase 1 validation evidence**

```bash
dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true /p:UseSharedCompilation=false -maxcpucount:1 -v:minimal
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~ShellNavigationCatalogTests|FullyQualifiedName~NavigationServiceTests|FullyQualifiedName~AppServiceRegistrationTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true /p:UseSharedCompilation=false -maxcpucount:1 --logger "console;verbosity=minimal"
```

Result: build passed; focused WPF validation passed with 70 tests.

---

### Phase 2 — Convention Wiring + Runtime Capability Gates *(Effort: S — days)*

Phase 2 adds two independent quality-of-life improvements that both require Phase 1's module
boundary to work cleanly.

#### 2a — `IViewModelViewResolver` (convention-based DataContext wiring)

Removes the `services.AddTransient<MyPage>()` + explicit `DataContext` wiring boilerplate. A
developer adding a new page needs only to declare `MyPage.xaml` and `MyPageViewModel.cs` in their
module — the resolver handles the rest by naming convention (`*Page` → `*ViewModel`).

**Scope**
- New: `src/Meridian.Wpf/Services/ViewModelViewResolver.cs`
- `src/Meridian.Wpf/Services/NavigationService.cs` — call `AutoWire()` in `CreatePageContentCore`
- `src/Meridian.Wpf/Services/WpfShellServiceCollectionExtensions.cs` — register the resolver

#### 2b — `IFeatureCapabilityGate` (runtime feature toggles)

Lets feature modules declare capability keys and lets operators (and developers) toggle them at
runtime without recompiling. The Settings workspace auto-generates a toggle list from registered
capabilities.

**Scope**
- New: `src/Meridian.Wpf/Services/IFeatureCapabilityGate.cs`
- New: `src/Meridian.Wpf/Services/FeatureCapabilityGateService.cs`
- New: `src/Meridian.Wpf/Services/FeatureCapabilityOptions.cs`
- `src/Meridian.Wpf/Features/IDesktopFeatureModule.cs` — add optional
  `IReadOnlyList<FeatureCapabilityDescriptor> DeclareCapabilities()` method
- `config/appsettings.json` — add `"FeatureCapabilities": {}` section
- `src/Meridian.Wpf/ViewModels/SettingsViewModel.cs` — surface registered capabilities as a
  toggle list
- Tests: `tests/Meridian.Wpf.Tests/` — add `FeatureCapabilityGateTests`

**TODO checklist — Phase 2a (ViewModel resolver)**

- [x] Create `IViewModelViewResolver` with `ResolveViewModelType(Type pageType)` and
  `AutoWire(FrameworkElement page, IServiceProvider scope)`.
- [x] Implement `ViewModelViewResolver` using `{PageName}ViewModel` naming convention.
  Skip pages where `DataContext` is already set.
- [x] Add startup validation that logs a warning for pages in `IShellPageRegistry` where no
  matching ViewModel type is found in the DI container.
- [x] Call `AutoWire()` in `NavigationService.CreatePageContentCore()` after page instantiation.
- [x] Register `IViewModelViewResolver` as a singleton in `WpfShellServiceCollectionExtensions`.
- [x] Confirm existing pages that set their own DataContext in XAML or code-behind are unaffected.
- [x] Run navigation smoke tests confirming no DataContext regressions on the five primary pages.

**TODO checklist — Phase 2b (capability gate)**

- [x] Define `FeatureCapabilityDescriptor` record: `CapabilityKey`, `DisplayName`, `Description`,
  `DefaultEnabled`, `IsPermanent`.
- [x] Create `IFeatureCapabilityGate` with `IsEnabled(string key)`, `SetEnabled(string key, bool)`,
  and `IObservable<CapabilityChangedEvent> Changes`.
- [x] Implement `FeatureCapabilityGateService` backed by `IOptionsMonitor<FeatureCapabilityOptions>`;
  persist overrides via `ConfigStore` using `AtomicFileWriter`.
- [x] Extend `IDesktopFeatureModule` with optional `DeclareCapabilities()` method; collect
  declarations in `DesktopFeatureModuleRegistry`.
- [x] Add `FeatureCapabilities` section to `config/appsettings.json`.
- [x] Add a generated "Capabilities" tab in `SettingsViewModel` showing registered toggles.
- [x] Add `FeatureCapabilityGateTests` covering enable/disable, persistence round-trip, and
  change-notification delivery.
- [x] Run the focused resolver, capability-gate, settings, and primary-navigation smoke tests.

**Phase 2 validation evidence**

Focused WPF resolver, capability-gate, Settings, and navigation smoke coverage passed on
2026-05-22. A full unfiltered WPF test pass remains intentionally tracked in Phase 3 because the
local suite has recurring shared-output/testhost contention and should be treated as a lifecycle
acceptance gate rather than Phase 2 scope.

---

### Phase 3 — Workspace Lifecycle + Session State *(Effort: M — 1-2 weeks)*

This phase gives each workspace an explicit DI scope and the ability to serialize its state so
operators keep their context when switching workspaces.

#### 3a — Workspace-scoped `IServiceScope`

Expensive ViewModels, API polling loops, and streaming subscriptions are tied to the workspace's
scope lifetime. Switching away from a workspace disposes its scope; switching back creates a
fresh one.

**Scope**
- `src/Meridian.Wpf/Services/WorkspaceService.cs` — add scope creation/disposal on workspace
  activation/deactivation
- `src/Meridian.Wpf/Services/WpfShellServiceCollectionExtensions.cs` — introduce
  `AddWorkspaceScoped<T>()` extension
- `src/Meridian.Wpf/Services/NavigationService.cs` — accept optional scope in
  `CreatePageContent()`
- `src/Meridian.Wpf/Views/WorkspaceShellPageBase.cs` — pass active workspace scope into docked
  page creation
- `src/Meridian.Wpf/ViewModels/MainPageViewModel.cs` — planned direct frame-navigation scope handoff

#### 3b — `IWorkspaceStateToken` (serialize/restore session)

Each workspace shell ViewModel implements a lightweight serialization contract so its visible
operator state survives workspace switches and app restarts.

**Scope**
- New: `src/Meridian.Wpf/Contracts/IWorkspaceStateToken.cs`
- `src/Meridian.Wpf/ViewModels/TradingWorkspaceShellViewModel.cs` — implement token
- `src/Meridian.Wpf/ViewModels/ResearchWorkspaceShellViewModel.cs` — implement token
- `src/Meridian.Wpf/Services/WorkspaceService.cs` — serialize on deactivate, restore on activate
- Persistence: `%LocalAppData%\Meridian\workspace-state.json` via `AtomicFileWriter`

**TODO checklist — Phase 3a (scoped DI)**

- [x] Add `AddWorkspaceScoped<T>(this IServiceCollection)` extension that tags services with a
  workspace-lifetime marker (attribute or marker interface).
- [x] In `WorkspaceService.ActivateWorkspaceAsync()`, create an `IServiceScope` keyed to
  `workspaceId` and store it in a `Dictionary<string, IServiceScope>`.
- [x] Dispose the scope on workspace deactivation (when a different workspace is activated).
- [ ] Audit existing services for correct lifetime: platform-owned singletons (`NavigationService`,
  `FundContextService`, `ThemeService`) stay in the root container; workspace-owned polling/
  streaming services are converted to `AddWorkspaceScoped`.
- [x] Update `NavigationService.CreatePageContent()` to accept an optional `IServiceScope` and
  resolve pages from it when provided.
- [ ] Update `MainPageViewModel` to pass the active workspace scope on every navigation call.
  Current implementation passes the active scope from workspace shell pages into
  `CreatePageContent`; direct frame navigation still uses the root provider.
- [x] Add tests in `WorkspaceServiceTests` covering scope creation, isolation between workspaces,
  and disposal on deactivation.
- [ ] Run `dotnet test tests/Meridian.Wpf.Tests/` and resolve remaining full-suite harness or
  shared-output issues before accepting the Phase 3 workspace lifecycle changes.

**Phase 3a validation evidence**

```bash
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~NavigationServiceTests|FullyQualifiedName~WorkspaceServiceTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~MainShellViewModelTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~MainShellViewModelTests|FullyQualifiedName~WorkspaceServiceTests|FullyQualifiedName~NavigationServiceTests|FullyQualifiedName~AppServiceRegistrationTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
```

Result: focused workspace/navigation lifecycle validation passed with 125 tests; the main shell view
model slice passed with 37 tests; the combined focused Phase 3a slice passed with 168 tests.
`git diff --check` passed. The full WPF suite has not been accepted yet.

**TODO checklist — Phase 3b (state tokens)**

- [ ] Define `IWorkspaceStateToken` with `WorkspaceId`, `Version`, `Serialize(Utf8JsonWriter)`,
  and `Restore(ref Utf8JsonReader)`. Add a migration hook for version mismatches.
- [ ] Implement `IWorkspaceStateToken` on `TradingWorkspaceShellViewModel` (active symbol,
  selected run context, pane layout).
- [ ] Implement `IWorkspaceStateToken` on `ResearchWorkspaceShellViewModel`.
- [ ] Extend `WorkspaceService` to call `Serialize()` on deactivation and `Restore()` on
  activation for any ViewModel that implements the interface.
- [ ] Persist token store to `%LocalAppData%\Meridian\workspace-state.json` via `AtomicFileWriter`.
- [ ] Add defensive handling for stale token references (paper session IDs no longer present,
  deleted symbols) in each `Restore()` implementation.
- [ ] Add `WorkspaceStateTokenTests` covering round-trip, version mismatch graceful handling,
  and null stale-reference recovery.
- [ ] Run `dotnet test tests/Meridian.Wpf.Tests/` and confirm `TradingWorkspaceShellPageTests`
  and `ResearchWorkspaceShellPageTests` pass.

---

### Phase 4 — Page Activation Lifetime + Composable Shell Slots *(Effort: M-L)*

The final phase addresses two remaining extensibility gaps: costly ViewModel initialization
happening in constructors rather than on page activation, and shell presentation slots (context
strip, action bar) being hardwired in shared XAML templates.

#### 4a — `IPageActivationLifetime` (deferred activation)

Streaming subscriptions, polling loops, and API calls that currently start in ViewModel
constructors are moved to `OnPageActivated()`. Pages that are not currently visible do not hold
active connections.

**Scope**
- New: `src/Meridian.Wpf/Contracts/IPageActivationLifetime.cs`
- `src/Meridian.Wpf/Services/NavigationService.cs` — call `OnPageActivated` / `OnPageDeactivated`
- ViewModels that start connections in constructors: `OrderBookViewModel`, `LiveDataViewerViewModel`,
  `BacktestViewModel`, `ProviderHealthViewModel` (audit pass required)

#### 4b — Composable shell slots (context strip + action bar)

Feature modules contribute lightweight `IContextStripContributor` or `IShellActionBarContributor`
implementations that are assembled at runtime into the workspace chrome. No shared XAML edits
required when a new workspace adds an attention badge or action.

**Scope**
- New: `src/Meridian.Wpf/Contracts/IContextStripContributor.cs`
- New: `src/Meridian.Wpf/Contracts/IShellActionBarContributor.cs`
- New: `src/Meridian.Wpf/Shell/Services/ShellSlotAssembler.cs`
- `src/Meridian.Wpf/Shell/ViewModels/` — bind assembler collections
- XAML: workspace context-strip templates switched from static to `ItemsControl` + contributor
  `DataTemplate`

**TODO checklist — Phase 4a (activation lifetime)**

- [ ] Define `IPageActivationLifetime` with `OnPageActivated(CancellationToken)` and
  `OnPageDeactivated()`. Add optional `OnPreload()` for background pre-fetch before activation.
- [ ] In `NavigationService`, after `_frame.Navigate()` succeeds call `OnPageActivated()` on the
  new page's ViewModel (if it implements the interface). Call `OnPageDeactivated()` on the
  outgoing page's ViewModel before navigation.
- [ ] Audit `OrderBookViewModel` — move streaming subscription start out of constructor into
  `OnPageActivated()`; cancel subscription in `OnPageDeactivated()`.
- [ ] Audit `LiveDataViewerViewModel` — same pattern.
- [ ] Audit `BacktestViewModel` — move progress-polling setup into `OnPageActivated()`.
- [ ] Audit `ProviderHealthViewModel` — move health-polling timer into `OnPageActivated()`.
- [ ] Add `OrderBookViewModelTests` coverage asserting subscription is not started before
  activation and is cancelled on deactivation.
- [ ] Measure baseline memory allocation on a 5-workspace cycling test before and after; record
  the delta in this document.
- [ ] Run the full WPF test suite and confirm `OrderBookViewModelTests`, `ProviderHealthViewModelTests`,
  and navigation smoke tests pass.

**TODO checklist — Phase 4b (shell slot composition)**

- [ ] Define `IContextStripContributor` with `WorkspaceId`, `Order`, and
  `CreateSlotContent(IServiceProvider)`.
- [ ] Define `IShellActionBarContributor` with the same shape.
- [ ] Create `ShellSlotAssembler` singleton that holds
  `Dictionary<string, ObservableCollection<IContextStripContributor>>` and
  `Dictionary<string, ObservableCollection<IShellActionBarContributor>>` keyed by workspace ID.
- [ ] Register `ShellSlotAssembler` in `WpfShellServiceCollectionExtensions`; populate from module
  registrations during startup.
- [ ] Migrate the Trading workspace context strip to an `ItemsControl` bound to
  `ShellSlotAssembler.GetStripContributors("trading")`. Implement the existing trading
  session badge as the first `IContextStripContributor`.
- [ ] Migrate the action bar in `WorkspaceShellContextStripControl` in the same way.
- [ ] Extend `IDesktopFeatureModule` with optional `ContributeShellSlots(IShellSlotBuilder)` method.
- [ ] Run `WorkspaceShellContextStripControlTests` and confirm no visual regressions.
- [ ] Document the slot contribution pattern in `src/Meridian.Wpf/README.md` for future workspace
  developers.

---

## Sequencing Summary

```
Phase 1 (module boundaries + open registry)
  └─► Phase 2a (ViewModel resolver)          [parallel with 2b]
  └─► Phase 2b (capability gates)            [parallel with 2a]
        └─► Phase 3a (workspace DI scope)
              └─► Phase 3b (state tokens)
                    └─► Phase 4a (activation lifetime)
                    └─► Phase 4b (shell slot composition)    [parallel with 4a]
```

Phases 2a and 2b are independent and can be merged in either order. Phases 4a and 4b are
independent once Phase 3 is complete.

---

## Reference Files

| Area | Primary file |
|------|-------------|
| Module registration | `src/Meridian.Wpf/Features/DesktopFeatureModuleRegistry.cs` |
| Module contract | `src/Meridian.Wpf/Features/IDesktopFeatureModule.cs` |
| Shell navigation catalog | `src/Meridian.Wpf/Models/ShellNavigationCatalog.cs` |
| Navigation service | `src/Meridian.Wpf/Services/NavigationService.cs` |
| Workspace service | `src/Meridian.Wpf/Services/WorkspaceService.cs` |
| Shell service extensions | `src/Meridian.Wpf/Services/WpfShellServiceCollectionExtensions.cs` |
| Main page ViewModel | `src/Meridian.Wpf/ViewModels/MainPageViewModel.cs` |
| Context strip control | `src/Meridian.Wpf/Shell/` |
| WPF test suite | `tests/Meridian.Wpf.Tests/` |

## Validation Commands

```bash
dotnet restore tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true
dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Release --no-restore /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true /p:WindowsPackageType=None
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj -c Release --no-restore /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ShellNavigationCatalogTests|FullyQualifiedName~NavigationServiceTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
```

## Related Plans

- [`docs/plans/trading-workstation-migration-blueprint.md`](trading-workstation-migration-blueprint.md) — active workstation migration context
- [`docs/plans/codebase-audit-cleanup-roadmap.md`](codebase-audit-cleanup-roadmap.md) — parallel cleanup backlog
- [`docs/status/ROADMAP.md`](../status/ROADMAP.md) — wave sequencing and delivery gates
