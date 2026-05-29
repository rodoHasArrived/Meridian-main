# Entity-Aware Workstation and Capability Module Architecture Blueprint

**Last Updated:** 2026-05-29
**Status:** Proposed implementation blueprint
**Primary surfaces:** `src/Meridian.Wpf/`, `src/Meridian.Ui/dashboard/`, `src/Meridian.Ui.Services/`, `src/Meridian.Ui.Shared/`
**Planning parents:** [`current-direction-and-status.md`](current-direction-and-status.md), [`sfo-mvp-implementation-design.md`](sfo-mvp-implementation-design.md), [`trading-workstation-migration-blueprint.md`](trading-workstation-migration-blueprint.md)

## Summary

Implement an entity-aware workstation architecture that keeps Meridian's visible root navigation as
`Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`, while making
the modules inside each workspace conditional on the operating entity the user is working with.
Family-office, fund, and financial-advisor entities should therefore share the same workstation
contract and reusable capability modules, but receive different module availability, copy, default
pane layouts, commands, disabled reasons, and scope-strip evidence.

The design goal is to move Meridian from a long page-directory model toward a composable operator
workstation model:

```text
Selected entity context
  -> workspace root
    -> resolved capability modules
      -> WPF page/pane or browser route implementation
```

This blueprint deliberately does not create separate products such as a family-office app, fund app,
or advisor app. It creates a shared capability-resolution layer that both desktop and browser
workstations can consume.

## Scope

### In scope

- A shared entity/workstation capability contract in `src/Meridian.Ui.Shared/`.
- A UI service resolver in `src/Meridian.Ui.Services/` that converts entity context, workspace,
  role, feature flags, environment state, and data readiness into resolved modules.
- A WPF integration path that wraps or extends `ShellNavigationCatalog` and feature-module
  descriptors without breaking existing page tags, deep links, command-palette entries, or saved
  workspace layouts.
- A browser workstation integration path that consumes the same resolved contract for `/workstation/`
  navigation, route cards, disabled reasons, and entity-aware landing content.
- Sample entity profiles for family office, fund, and financial advisor workflows.
- A migration path from static catalog-first navigation to entity-aware capability resolution.
- Tests that prove visibility, disabled reasons, route parity, saved-layout compatibility, and shared
  desktop/browser contract behavior.

### Out of scope for the first implementation

- Tenant billing, licensing enforcement, or commercial entitlements beyond static feature flags.
- A full RBAC/ABAC security system. The first slice accepts role inputs but does not replace
  existing authentication or authorization work.
- Dynamic admin editing of module availability. The first slice can expose read-only profile
  diagnostics; writeable profile administration should follow after the resolution contract is
  stable.
- Mobile-specific navigation, native mobile surfaces, MAUI, React Native, Flutter, or mobile-first
  workflows.
- Replacing `ShellNavigationCatalog` in one PR. Existing page tags, aliases, and routing remain
  authoritative during migration.

### Assumptions

- Entity type is available before the shell resolves the visible module set. The first slice may use
  deterministic fixture entities until persistence and user selection are wired.
- Top-level navigation remains the seven canonical workspaces, even when an entity type does not use
  every workspace heavily.
- Existing WPF and browser route tags stay stable. Capability IDs are introduced as a layer above
  page tags, not as immediate replacements.
- Disabled or unavailable modules should be explainable in the command palette, Settings diagnostics,
  and test assertions rather than silently disappearing.

## Design Inputs and Prioritization

This blueprint folds the brainstormed redesign ideas into one delivery model. The table below keeps
the original idea ranking visible so implementation planning can separate core architecture from
follow-on operator experience work.

| # | Workstream | Effort | Primary audience | Impact | Primary dependency | Blueprint treatment |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Entity-Aware Workstation Shell | Large | All operator personas | Very high | Capability registry, entity context | Core shell objective; required for the first shared resolver contract. |
| 2 | Capability Modules Instead of Static Pages | Medium/Large | Developers and operators | Very high | Navigation metadata refactor | Core abstraction; implemented through capability definitions layered over existing page tags. |
| 3 | Entity Module Matrix | Medium | Product, design, and development | High | Clear entity taxonomy | Static family-office, fund, advisor, and unknown profiles in the first slice. |
| 4 | Contextual Workspace Presets | Medium | Power users | High | Dock/layout persistence | Profile-driven default panes after route compatibility is proven. |
| 5 | “Why Is This Hidden?” Module Explainer | Small/Medium | Operators and admins | Medium/High | Disabled-reason metadata | Required read-model field; surfaced in command palette and diagnostics before broad UI rollout. |
| 6 | Entity Switcher + Scope Strip | Medium | All users | High | Workstation context service | Shared `WorkstationScopeStripReadModel` plus entity-switch data flow. |
| 7 | Role + Entity Composition Rules | Large | Enterprise/admin users | High | Auth/permissions model | Modeled as presentation overlays now; backend authorization remains a later security integration. |
| 8 | Shared Browser/WPF Workstation Contract | Medium/Large | Platform maintainers | Very high | UI shared read models | Non-negotiable contract boundary for preventing WPF/browser drift. |

### Dependency ordering

1. **Foundational contract:** entity context, capability definitions, resolved module read models, and
   shared schema versioning.
2. **Profile matrix:** static entity profiles that prove family-office, fund, advisor, and unknown
   module differences without changing page implementations.
3. **Catalog adapter:** bridge `ShellNavigationCatalog` into the capability catalog so current WPF
   page tags remain stable.
4. **Explainability:** disabled/unavailable reason codes, command-palette diagnostics, and route
   diagnostics.
5. **Shell composition:** WPF and browser shells consume the same resolved model for navigation,
   scope strip, and default panes.
6. **Role overlays:** role-aware presentation and permission hints after entity-aware parity is green;
   security enforcement remains outside this UI-shaping layer until an authorization design lands.

## Architecture

### Layer responsibilities

| Layer | Responsibility |
| --- | --- |
| `Meridian.Ui.Shared.Workstations` | Own DTOs, enums, read models, and source-generated JSON context for entity-aware workstation contracts. |
| `Meridian.Ui.Services.Workstations` | Resolve entity profiles, module rules, role overlays, data-readiness overlays, and route targets into shared read models. |
| `Meridian.Wpf` | Adapt resolved modules into shell tiles, workspace navigation tiers, dock/default panes, command-palette rows, and disabled-reason UI. |
| `Meridian.Ui/dashboard` | Render resolved modules as browser workspace routes, cards, side navigation, command-palette/search entries, and disabled states. |
| Existing domain/application/storage/provider layers | Continue to own business behavior. They must not depend on UI capability resolution. |

### Conceptual model

```text
WorkstationEntityContext
  - selected operating entity
  - entity kind: FamilyOffice, Fund, FinancialAdvisor, Household, Account, Vehicle, Client
  - currency/accounting/date context
  - environment and data-freshness posture

WorkstationCapabilityDefinition
  - canonical capability id
  - supported entity kinds
  - workspace placement
  - page tag / browser route target
  - required features, roles, and data readiness
  - default copy, keywords, related capabilities, and automation id seed

WorkstationCapabilityResolution
  - visible / disabled / hidden / unavailable state
  - reason code and operator-facing reason text
  - presentation overrides for the selected entity
  - default pane placement and navigation tier

ResolvedWorkstationReadModel
  - selected entity summary
  - scope strip
  - workspace summaries
  - resolved module lists
  - command/search entries
  - route targets and evidence links
```

### Resolution order

Capability resolution must be deterministic and testable. The recommended order is:

1. Load or construct the selected `WorkstationEntityContext`.
2. Load the static or configured `WorkstationEntityProfile` for that entity kind.
3. Start from registered `WorkstationCapabilityDefinition` entries.
4. Filter by canonical workspace root.
5. Apply entity support rules.
6. Apply feature/module enablement rules.
7. Apply role or permission overlays when available.
8. Apply data-readiness and environment overlays.
9. Resolve presentation overrides: title, subtitle, section label, command text, empty-state copy,
   disabled reasons, and route parameters.
10. Resolve layout defaults: primary/secondary/overflow tiers, dock panes, home modules, and
    command-palette visibility.
11. Emit one shared read model for WPF and browser consumers.

No WPF page, React component, or XAML code-behind should independently reimplement this decision
tree.

## Long-Term Extensibility and Maintainability Requirements

The first implementation must avoid becoming another static navigation catalog with entity checks
spread across WPF, React, and service code. Treat the resolver as a long-lived platform subsystem
with the following constraints.

### Stable identifiers and lifecycle metadata

- Capability IDs are stable product contracts, not display labels. They should be lowercase,
  kebab-case, and never reused for a different business capability.
- Page tags and browser routes can change behind aliases, but capability IDs should remain stable
  across saved layouts, deep links, telemetry, tests, and documentation.
- Every capability should carry owner, maturity, introduced version, optional deprecated version, and
  replacement capability metadata before broad rollout.
- Deprecated capabilities should remain searchable with explanatory copy for at least one migration
  window before they are hidden from normal navigation.

### Contributor-based registration

Avoid a single large static array that every future module must edit. Use contributor registration so
features can declare their capabilities near the owning module while the resolver still emits one
deterministic catalog snapshot.

```csharp
public interface IWorkstationCapabilityContributor
{
    string ContributorId { get; }
    IReadOnlyList<WorkstationCapabilityDefinition> DescribeCapabilities();
}

public sealed record WorkstationCapabilityCatalogSnapshot(
    string CatalogVersion,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<WorkstationCapabilityDefinition> Capabilities,
    IReadOnlyList<WorkstationCapabilityCatalogDiagnostic> Diagnostics);

public sealed record WorkstationCapabilityCatalogDiagnostic(
    string Code,
    string Severity,
    string Message,
    string? CapabilityId);
```

Catalog construction must be deterministic: sort by workspace, tier, order, and capability ID after
contributors run. Duplicate IDs, duplicate route targets without an alias relationship, orphan
related-capability links, missing owners, and unsupported workspace IDs should be diagnostics that
can fail tests before runtime.

### Profile composition instead of profile copy-paste

Entity profiles should compose overlays rather than duplicate entire workspace matrices. Recommended
composition order:

1. `Unknown` fallback profile.
2. Entity-kind base profile, such as `FamilyOffice`, `Fund`, or `FinancialAdvisor`.
3. Optional entity-subtype overlay, such as `SingleFamilyOffice`, `ClosedEndFund`, or `RIA`.
4. Environment overlay, such as fixture/offline, paper, live-readiness, or degraded-provider mode.
5. Role/permission presentation overlay.
6. Per-entity operator preference overlay for default panes and favorites.

Each overlay should state only the capabilities it changes. This keeps new entity kinds maintainable
and prevents the family-office, fund, and advisor profiles from drifting into three unrelated
products.

### Pure resolution pipeline

`IWorkstationCapabilityResolver` should be implemented as a pipeline of pure, individually tested
steps rather than one large method. Suggested steps:

1. Normalize entity context and role inputs.
2. Load catalog snapshot.
3. Compose entity profile overlays.
4. Apply entity support.
5. Apply feature and environment gates.
6. Apply role/permission presentation overlays.
7. Apply route/page availability checks.
8. Build layout/default-pane model.
9. Emit diagnostics, search entries, and a resolution trace.

The pipeline should be deterministic for the same inputs. Avoid wall-clock-dependent labels inside
the resolver except for the envelope `GeneratedAt`; freshness labels should come from input read
models so tests can pin expected output.

### Configuration, caching, and invalidation

- Built-in capability definitions should live in code next to owning feature modules. Persisted
  profile overrides should live behind a future store interface rather than mutating built-in
  definitions.
- Cache resolved workstations by entity ID, entity kind, role set, feature set, catalog version, and
  profile version. Invalidate when any of those keys changes.
- Cache entries should be small read models, not live view models or page instances.
- All async resolver APIs must accept `CancellationToken` and ignore late completions after entity
  switch, route switch, or role switch.
- On resolver failure, use the last known good resolved model when available; otherwise fall back to
  the static catalog with an explicit warning.

### Observability and supportability

Add a resolver diagnostics surface before enabling admin-editable profiles. Operators and support
engineers should be able to answer:

- Which entity profile and overlays were applied?
- Why is a capability visible, disabled, hidden, or unavailable?
- Which feature flag, role, route, or data-readiness input made the decision?
- Which WPF page tag or browser route will be opened?
- Which catalog/profile version generated the current shell?

Diagnostics must not expose credentials or sensitive account data. Keep them at the capability,
route, feature, and role-label level.

### Compatibility and deprecation gates

- Do not remove existing page tags until route-parity tests, saved-layout migration tests, and
  command-palette alias tests prove the replacement path.
- Any capability rename requires alias metadata, migration copy, and tests proving old saved layouts
  still land on a valid module.
- Contract additions should be backward-compatible whenever possible. Breaking response-shape
  changes require a new schema version, browser fixture update, WPF fixture update, and release note
  in this plan or its implementation PR.

## Interfaces and Models

### Shared enums and value objects

Namespace: `Meridian.Ui.Shared.Workstations`

```csharp
public enum WorkstationEntityKind
{
    Unknown = 0,
    FamilyOffice,
    Fund,
    FinancialAdvisor,
    Household,
    Client,
    Account,
    Vehicle
}

public enum WorkstationCapabilityAvailability
{
    Visible = 0,
    Disabled,
    Hidden,
    Unavailable
}

public enum WorkstationCapabilityReasonCode
{
    None = 0,
    UnsupportedEntityKind,
    FeatureDisabled,
    MissingRole,
    MissingPermission,
    DataNotReady,
    EnvironmentUnavailable,
    RequiresConfiguration,
    RouteUnavailable
}

public enum WorkstationNavigationTier
{
    Primary = 0,
    Secondary,
    Overflow,
    Diagnostics
}
```

### Entity context DTOs

Namespace: `Meridian.Ui.Shared.Workstations`

```csharp
public sealed record WorkstationEntityContextDto(
    string EntityId,
    string DisplayName,
    WorkstationEntityKind Kind,
    string BaseCurrency,
    string ScopeLabel,
    string AccountingBasisLabel,
    string AsOfLabel,
    WorkstationEnvironmentDto Environment,
    IReadOnlyList<string> EnabledFeatureIds,
    IReadOnlyList<string> OperatorRoleIds);

public sealed record WorkstationEnvironmentDto(
    string ModeLabel,
    string DataFreshnessLabel,
    string ReviewStateLabel,
    string AlertLabel,
    string Tone);
```

### Capability definitions

Namespace: `Meridian.Ui.Shared.Workstations`

```csharp
public sealed record WorkstationCapabilityDefinition(
    string CapabilityId,
    string DefaultTitle,
    string DefaultSubtitle,
    string WorkspaceId,
    string SectionLabel,
    string Glyph,
    WorkstationNavigationTier DefaultTier,
    string? WpfPageTag,
    string? BrowserRoute,
    IReadOnlyList<WorkstationEntityKind> SupportedEntityKinds,
    IReadOnlyList<string> RequiredFeatureIds,
    IReadOnlyList<string> RequiredRoleIds,
    IReadOnlyList<string> SearchKeywords,
    IReadOnlyList<string> RelatedCapabilityIds,
    string AutomationIdSeed,
    WorkstationCapabilityLifecycle Lifecycle);

public sealed record WorkstationCapabilityLifecycle(
    string Owner,
    string Maturity,
    string IntroducedInVersion,
    string? DeprecatedInVersion,
    string? ReplacementCapabilityId);
```

Existing `ShellPageDescriptor` values should feed this definition during migration. The first slice
can add an adapter rather than rewriting all descriptors.

### Resolved module read models

Namespace: `Meridian.Ui.Shared.Workstations`

```csharp
public sealed record ResolvedWorkstationReadModel(
    WorkstationEntityContextDto Entity,
    WorkstationScopeStripReadModel ScopeStrip,
    IReadOnlyList<ResolvedWorkspaceReadModel> Workspaces,
    IReadOnlyList<ResolvedCapabilitySearchEntry> SearchEntries,
    IReadOnlyList<ResolvedCapabilityDiagnostic> Diagnostics);

public sealed record ResolvedWorkspaceReadModel(
    string WorkspaceId,
    string Title,
    string Summary,
    string HomeCapabilityId,
    string HomePageTag,
    IReadOnlyList<ResolvedCapabilityModuleReadModel> Modules,
    IReadOnlyList<ResolvedPaneReadModel> DefaultPanes);

public sealed record ResolvedCapabilityModuleReadModel(
    string CapabilityId,
    string Title,
    string Subtitle,
    string WorkspaceId,
    string SectionLabel,
    string Glyph,
    WorkstationNavigationTier Tier,
    WorkstationCapabilityAvailability Availability,
    WorkstationCapabilityReasonCode ReasonCode,
    string ReasonText,
    string? WpfPageTag,
    string? BrowserRoute,
    IReadOnlyList<string> SearchKeywords,
    IReadOnlyList<string> RelatedCapabilityIds,
    string AutomationId);

public sealed record WorkstationScopeStripReadModel(
    string EntityName,
    string EntityKindLabel,
    string ScopeLabel,
    string CurrencyLabel,
    string FreshnessLabel,
    string ReviewStateLabel,
    string AlertLabel,
    string Tone,
    IReadOnlyList<WorkstationScopeActionReadModel> Actions);

public sealed record WorkstationScopeActionReadModel(
    string ActionId,
    string Label,
    string TargetWorkspaceId,
    string? TargetCapabilityId,
    string? BrowserRoute,
    string? WpfPageTag,
    bool IsEnabled,
    string DisabledReason);

public sealed record ResolvedPaneReadModel(
    string CapabilityId,
    string PaneRole,
    string DockAction,
    bool OpenWithoutEntityParameter);

public sealed record ResolvedCapabilitySearchEntry(
    string CapabilityId,
    string Title,
    string WorkspaceId,
    WorkstationCapabilityAvailability Availability,
    string ReasonText,
    string? BrowserRoute,
    string? WpfPageTag,
    IReadOnlyList<string> Keywords);

public sealed record ResolvedCapabilityDiagnostic(
    string CapabilityId,
    WorkstationCapabilityReasonCode ReasonCode,
    string Message,
    string? RequiredActionId);

public sealed record ResolvedRouteTarget(
    string CapabilityId,
    string? BrowserRoute,
    string? WpfPageTag,
    bool IsNavigable,
    string DisabledReason);
```

### Service interfaces

Namespace: `Meridian.Ui.Services.Workstations`

```csharp
public interface IWorkstationEntityContextProvider
{
    ValueTask<WorkstationEntityContextDto> GetCurrentAsync(CancellationToken cancellationToken);
    ValueTask<WorkstationEntityContextDto?> GetByIdAsync(string entityId, CancellationToken cancellationToken);
}

public interface IWorkstationCapabilityCatalog
{
    IReadOnlyList<WorkstationCapabilityDefinition> GetCapabilities();
}

public interface IWorkstationEntityProfileProvider
{
    ValueTask<WorkstationEntityProfile> GetProfileAsync(
        WorkstationEntityKind entityKind,
        CancellationToken cancellationToken);
}

public interface IWorkstationCapabilityResolver
{
    ValueTask<ResolvedWorkstationReadModel> ResolveAsync(
        WorkstationEntityContextDto entityContext,
        CancellationToken cancellationToken);
}

public interface IWorkstationRouteTargetResolver
{
    ResolvedRouteTarget Resolve(ResolvedCapabilityModuleReadModel module);
}
```

### Entity profile model

Namespace: `Meridian.Ui.Services.Workstations`

```csharp
public sealed record WorkstationEntityProfile(
    WorkstationEntityKind EntityKind,
    string DisplayName,
    IReadOnlyDictionary<string, WorkstationWorkspaceProfile> Workspaces,
    IReadOnlyDictionary<string, WorkstationCapabilityPresentationOverride> CapabilityOverrides);

public sealed record WorkstationWorkspaceProfile(
    string WorkspaceId,
    string HomeCapabilityId,
    IReadOnlyList<string> PrimaryCapabilityIds,
    IReadOnlyList<string> SecondaryCapabilityIds,
    IReadOnlyList<string> OverflowCapabilityIds,
    IReadOnlyList<ResolvedPaneReadModel> DefaultPanes);

public sealed record WorkstationCapabilityPresentationOverride(
    string CapabilityId,
    string? Title,
    string? Subtitle,
    string? SectionLabel,
    IReadOnlyList<string> AdditionalSearchKeywords,
    string? DisabledReasonOverride);
```

## DTO and Endpoint Contracts

### Shared browser/desktop endpoint

The first shared endpoint should return the same resolved read model for browser and desktop bridge
consumers:

```http
GET /api/workstation/context/current
GET /api/workstation/context/{entityId}
GET /api/workstation/context/{entityId}/workspaces
```

The recommended first implementation can expose only `GET /api/workstation/context/current`, with
fixture profile selection derived from development configuration or a query parameter reserved for
fixture builds. Mutating module-configuration endpoints are intentionally deferred.

### JSON generation

Add the new DTOs to the existing shared UI JSON source-generation context rather than relying on
reflection-heavy serialization. If the current shared UI context is split by feature area, create a
`WorkstationJsonContext` partial in `src/Meridian.Ui.Shared/Workstations/` and register the read
models there.

### Versioning

The endpoint response should include a small schema marker once implemented:

```csharp
public sealed record ResolvedWorkstationEnvelope(
    string SchemaVersion,
    DateTimeOffset GeneratedAt,
    ResolvedWorkstationReadModel Workstation);
```

Use `SchemaVersion = "entity-workstation.v1"` for the first released contract. Versioning is useful
because both WPF and browser surfaces will consume the same response and may roll forward at
different speeds during development.

## WPF Shell Integration

### Integration goal

WPF should use entity-aware capability resolution without breaking existing page construction,
feature modules, saved layouts, screenshot workflows, command-palette tags, or deep links.

### Adapter-first migration

Introduce an adapter that maps existing `ShellNavigationCatalog` descriptors into capability
definitions:

```csharp
public sealed class ShellNavigationCapabilityCatalog : IWorkstationCapabilityCatalog
{
    public IReadOnlyList<WorkstationCapabilityDefinition> GetCapabilities()
    {
        // Build from ShellNavigationCatalog workspace/page descriptors during migration.
    }
}
```

This allows `ShellNavigationCatalog` to remain the source of existing page metadata while the new
resolver becomes the source of entity-aware availability and presentation.

### Main shell changes

Add a WPF service adapter in `src/Meridian.Wpf/Services/`:

```csharp
public interface IWpfResolvedWorkstationService
{
    ValueTask<ResolvedWorkstationReadModel> GetCurrentAsync(CancellationToken cancellationToken);
}
```

`MainPageViewModel` should consume this service to populate:

- workspace tiles
- current workspace page groups
- command-palette rows
- related workflow links
- scope/evidence strip copy
- disabled/unavailable module explanations

Existing page tags remain routable through `NavigationService`. The resolver can mark a capability
as disabled or unavailable, but it must not delete the underlying route until the migration explicitly
removes that route.

### WPF default panes

`WorkspaceShellDefinition.DefaultPanes` and `PresetPanes` should be resolved through the entity
profile once a selected entity is active. The migration should preserve the current defaults as the
`Unknown` or fallback profile. Family-office, fund, and advisor profiles then override only the
pane lists that differ.

### WPF command palette

The command palette should include visible capabilities and optionally include disabled/unavailable
capabilities as explainable results. Search result rows should show:

- title
- target workspace
- current availability
- disabled or unavailable reason
- role/configuration/data-readiness requirement when known

This prevents operators from mistaking conditional availability for missing functionality.

### WPF failure behavior

If the resolver fails:

1. Log the resolver failure through the existing WPF logging service.
2. Fall back to the existing `ShellNavigationCatalog` static descriptors.
3. Show a neutral shell context warning such as `Entity-aware module profile unavailable; using default workspace catalog.`
4. Keep existing deep links and automation routes functioning.

## Browser Workstation Integration

### Integration goal

The browser workstation should consume the same resolved contract and render entity-specific module
sets without hardcoding family-office, fund, or advisor conditions inside React route components.

### Route contract

Add or extend a dashboard API client for:

```ts
GET /api/workstation/context/current
```

Recommended TypeScript model names:

```ts
export interface ResolvedWorkstationEnvelope { ... }
export interface ResolvedWorkstationReadModel { ... }
export interface ResolvedWorkspaceReadModel { ... }
export interface ResolvedCapabilityModuleReadModel { ... }
```

Use generated or hand-maintained TypeScript types consistent with the existing dashboard API-client
pattern. Keep route strings in the shared API route catalog rather than scattering `/api/*` literals
through components.

### Browser shell behavior

The browser workstation should use the resolved model to render:

- workspace landing cards
- left/secondary navigation groups
- route cards and disabled states
- command/search entries
- scope strip and review/freshness badges
- related module links
- empty states for unsupported modules

React components should receive resolved view-model data. They should not decide whether `NAV Close`
is a fund-only module or whether `Household Overview` is advisor-only.

### Browser failure behavior

If the endpoint fails, the browser should:

1. Preserve current route rendering when possible.
2. Show a recoverable banner with retry.
3. Avoid rendering a false empty state while the API failure is unresolved.
4. Fall back to the current static workstation route metadata only for navigation safety, not as a
   claim that entity-aware configuration succeeded.

## Migration Path from `ShellNavigationCatalog`

### Phase 0: Document and freeze compatibility rules

- Existing page tags remain stable.
- Existing aliases continue to resolve.
- Existing WPF screenshot and workflow automation tags remain valid.
- Static shell behavior remains the fallback when entity resolution fails.

### Phase 1: Introduce shared models and static fixture profiles

- Add shared DTO/read-model types.
- Add a static `WorkstationEntityProfileProvider` with `FamilyOffice`, `Fund`, `FinancialAdvisor`,
  and `Unknown` profiles.
- Add unit tests for profile shape and capability availability.
- Do not alter visible UI yet.

### Phase 2: Adapt existing WPF catalog into capability definitions

- Implement `ShellNavigationCapabilityCatalog` from current workspace/page descriptors.
- Map `ShellNavigationVisibilityTier` to `WorkstationNavigationTier`.
- Map page tags to capability IDs using a deterministic convention:
  - existing page tag: `FundLedger`
  - transitional capability ID: `fund-ledger`
  - future reusable capability ID when generalized: `ledger`
- Add route-parity tests proving every resolved WPF module with a page tag remains registered.

### Phase 3: Resolve WPF navigation through the entity-aware service

- Populate `MainPageViewModel` shell lists from `ResolvedWorkstationReadModel`.
- Keep `ShellNavigationCatalog` for page creation and fallback route lookup.
- Add disabled/unavailable command-palette rows.
- Preserve saved layouts by storing page tags and optional capability IDs side by side.

### Phase 4: Expose shared workstation context endpoint

- Add `/api/workstation/context/current` returning `ResolvedWorkstationEnvelope`.
- Add source-generated JSON coverage.
- Add API tests for family-office, fund, advisor, and unknown profiles.

### Phase 5: Move browser workstation navigation to the shared model

- Update browser API client and route metadata bridge.
- Render resolved module groups in workstation landing surfaces.
- Keep current static route metadata as a fallback while tests prove parity.

### Phase 6: Generalize selected high-value pages into capability modules

Start with Portfolio because the persona difference is clearest and risk is contained:

- family office: consolidated/entity exposure
- fund: fund holdings and risk
- financial advisor: household/client portfolios and model drift

Then expand to Accounting and Reporting after the resolution model is stable.

## Sample Entity Profiles

### Family Office profile

Default promise: consolidated balance sheet, entity exposure, private investments, cash needs,
approvals, and family reporting.

| Workspace | Primary modules | Secondary/overflow modules |
| --- | --- | --- |
| Portfolio | Consolidated Net Worth, Entity Exposure, Private Investments | Direct Lending, Tax Lots, Vehicle Holdings |
| Accounting | Entity Ledger, Cash Movement Review, Reconciliation | Trust Distributions, Tax Package Evidence |
| Reporting | Family Council Pack, Entity Statements, Tax Package | Document Lineage, Approval History |
| Strategy | Scenario Planning, Manager/Fund Research | Backtest Review, Allocation Alternatives |
| Data | Provider Health, Private Asset Ingestion, Data Lineage | Document Intake, Quality Exceptions |
| Trading | Restricted Order Review, Approval Staging | Execution History |
| Settings | Entity Structure, Users/Roles, Approval Policy | Provider Credentials |

Recommended default Portfolio panes:

```text
EntityTree | ConsolidatedExposure | PrivateInvestments | UpcomingCashNeeds
```

### Fund profile

Default promise: operate fund positions, risk, books, NAV, capital activity, reconciliation,
investor reporting, and audit evidence.

| Workspace | Primary modules | Secondary/overflow modules |
| --- | --- | --- |
| Portfolio | Fund Portfolio, Position Blotter, Exposure Review | Risk Limits, Investor Allocation |
| Accounting | Fund Ledger, Trial Balance, NAV Close | Capital Activity, Audit Trail |
| Reporting | Investor Report Pack, Factsheet, Regulatory/Audit Pack | Restatement Review, Evidence Workbench |
| Strategy | Strategy Runs, Backtest Studio, Run Compare | Promotion Review |
| Data | Market Data Health, Fund Admin Imports, Security Master | Backfill Queue, Provider Routing |
| Trading | Order Staging, Risk Checks, Paper/Live Controls | Trading Hours, Execution Audit |
| Settings | Fund Terms, NAV Calendar, Investor Classes | Approval Policy |

Recommended default Accounting panes:

```text
FundLedger | FundReconciliation | FundTrialBalance | FundAuditTrail
```

### Financial Advisor profile

Default promise: manage households and client accounts through suitability, model alignment,
rebalancing, reviews, billing, and compliance evidence.

| Workspace | Primary modules | Secondary/overflow modules |
| --- | --- | --- |
| Portfolio | Household Overview, Client Portfolios, Model Drift | Account Restrictions, Tax Lots |
| Accounting | Billing/Fee Review, Custodian Reconciliation | Cash Movement Exceptions |
| Reporting | Client Review Pack, IPS Report, Proposal Pack | Compliance Attestation |
| Strategy | Model Portfolio Research, Scenario Review | Client Suitability Checks |
| Data | Custodian Feeds, CRM Imports, Data Quality | Provider Health |
| Trading | Rebalance Queue, Client Trade Lists | Order Review, Restrictions |
| Settings | Advisory Firm Settings, Client Segmentation, Billing Rules | User Roles |

Recommended default Portfolio panes:

```text
HouseholdList | ClientPortfolio | ModelDrift | ReviewTasks
```

## Data Flow

### Startup / entity selection

1. Shell starts with the last selected entity ID, a launch parameter, or the default fixture entity.
2. `IWorkstationEntityContextProvider` loads `WorkstationEntityContextDto`.
3. `IWorkstationCapabilityResolver` resolves the full `ResolvedWorkstationReadModel`.
4. WPF and browser shells render workspace navigation from the resolved model.
5. Existing page/view models load their business data normally after navigation.

### Entity switch

1. User selects a different entity from the shell context switcher.
2. Shell cancels in-flight context resolution for the previous entity.
3. Resolver emits a new read model for the selected entity.
4. Shell remaps workspace/home/default panes.
5. If the current page is unsupported for the new entity, the shell navigates to the new workspace
   home and shows a recoverable explanation.
6. User-custom layouts are restored only when their saved layout key matches both workspace and
   entity ID or entity kind.

### Command palette search

1. User opens command palette.
2. Palette reads `SearchEntries` from the resolved model.
3. Visible entries navigate normally.
4. Disabled/unavailable entries show reason text and optional configuration/action links.
5. Hidden entries appear only when the profile marks them searchable for diagnostics.

## Edge Cases and Risks

| Risk | Mitigation |
| --- | --- |
| Page tags drift from capability IDs | Store both during migration and add route-parity tests. |
| React or XAML components start hardcoding entity checks | Keep entity conditions inside resolver tests and shared read models. |
| Entity switch destroys user layouts | Key layouts by entity ID/kind plus workspace; fall back to profile defaults only when no matching custom layout exists. |
| Unsupported modules vanish without explanation | Preserve disabled/unavailable reason text and searchable diagnostics. |
| Browser and WPF diverge | Make `ResolvedWorkstationReadModel` the shared contract and add parity tests for route/page targets. |
| Resolver endpoint failure blocks the app | Static catalog fallback remains available, with a visible neutral warning. |
| Static profiles become a second product taxonomy | Treat profiles as defaults; later move configuration into a controlled settings/admin workflow. |
| Role overlays are mistaken for security | First slice can shape UI only; backend authorization must remain enforced separately when added. |

## Test Plan

### Shared service tests

Target project: existing UI-service test project or a new focused test fixture under `tests/` if no
suitable project exists.

Required scenarios:

- Family-office profile resolves consolidated/entity/private-investment modules under Portfolio.
- Fund profile resolves Fund Portfolio, Fund Ledger, NAV Close, Investor Report Pack, and fund
  Trading modules.
- Financial-advisor profile resolves Household Overview, Model Drift, Rebalance Queue, Client Review
  Pack, and Billing/Fee Review.
- Unsupported capability returns `Unavailable` with `UnsupportedEntityKind` and operator-facing
  reason text.
- Feature-disabled capability returns `Disabled` with stable reason text.
- Unknown/default profile preserves current catalog-equivalent modules.
- All emitted module automation IDs are stable and non-empty.
- Catalog validation rejects duplicate capability IDs, orphan related-capability links, missing owners,
  unsupported workspace IDs, and route targets that lack either a page tag or browser route.
- Profile overlay composition is deterministic and changes only the capabilities named by the overlay.
- Deprecated capability aliases remain searchable and route to replacement modules during migration.

### WPF tests

Target project: `tests/Meridian.Wpf.Tests/`.

Required scenarios:

- `ShellNavigationCapabilityCatalog` maps every registered page descriptor into a capability
  definition.
- Every resolved visible WPF module with `WpfPageTag` is navigable by `NavigationService`.
- Command palette includes disabled/unavailable explanatory results when configured.
- Saved layout restoration prefers entity-specific layout keys and falls back to profile defaults.
- Resolver failure falls back to static catalog shell data and surfaces a neutral warning.
- Existing workflow automation and page-tag aliases continue to route.
- Last-known-good fallback is used when resolver refresh fails after a previously successful load.

### Browser tests

Target location: `src/Meridian.Ui/dashboard`.

Required scenarios:

- API client requests the shared workstation-context route through the shared API route catalog.
- Workstation landing renders module groups from the resolved model.
- Disabled modules expose visible inline reason text and accessible descriptions.
- Endpoint failure renders retry/recoverable banner and does not show a false empty state.
- Family-office, fund, and advisor fixture payloads produce different module sets without component
  hardcoded entity checks.
- Browser fixture tests pin schema version, deprecated capability copy, and route fallback behavior.

### Contract/parity tests

Required scenarios:

- Shared DTO serialization round-trips all enum values and nested module lists.
- WPF page tags and browser routes in `ResolvedCapabilityModuleReadModel` are either valid or carry
  `RouteUnavailable` diagnostics.
- Search entries reference existing capabilities.
- Default panes reference visible or disabled modules, never hidden/unavailable modules unless the
  pane has an explicit fallback.
- Resolution trace diagnostics explain every non-visible module without exposing credentials or
  account-sensitive data.

### Validation commands

Use the narrowest commands that match the files changed in an implementation PR. Likely commands:

```bash
dotnet test tests/Meridian.Wpf.Tests -c Release /p:EnableWindowsTargeting=true
npm --prefix src/Meridian.Ui/dashboard run test
python3 build/scripts/docs/validate-doc-hashes.py --summary
```

Run broader solution validation only when shared contracts or route registration changes affect many
projects.

## First Implementation Slice

### Goal

Prove the architecture with read-only static profiles and WPF/browser-safe contracts before changing
large amounts of shell behavior.

### Deliverables

1. Add shared DTO/read-model definitions under `src/Meridian.Ui.Shared/Workstations/`.
2. Add `IWorkstationCapabilityCatalog`, `IWorkstationEntityProfileProvider`, and
   `IWorkstationCapabilityResolver` under `src/Meridian.Ui.Services/Workstations/`.
3. Implement static profiles for `FamilyOffice`, `Fund`, `FinancialAdvisor`, and `Unknown`.
4. Implement an adapter that converts existing WPF shell/page descriptors into capability
   definitions without deleting or renaming page tags.
5. Add a small read-only endpoint returning `ResolvedWorkstationEnvelope` for the current or fixture
   entity context.
6. Add focused tests for profile resolution and WPF route parity.
7. Add browser fixture consumption behind existing workstation routing without removing static route
   fallback.
8. Add catalog/profile validation diagnostics for duplicate IDs, missing lifecycle metadata, orphan
   links, invalid workspace IDs, and route mismatches.
9. Add a small resolution trace model so tests and diagnostics can explain why modules are visible,
   disabled, hidden, or unavailable.

### Acceptance criteria

- Existing WPF navigation and browser workstation routes still work with the default/unknown profile.
- Family-office, fund, and advisor fixtures resolve visibly different module sets from the same
  capability catalog.
- Disabled/unavailable modules include reason codes and human-readable reason text.
- Every visible WPF module maps to a registered page tag.
- Browser components render resolved module lists without hardcoded entity-type conditionals.
- Catalog output is deterministic for the same inputs and fails validation when contributors emit
  duplicate IDs, missing lifecycle metadata, orphan links, invalid workspace IDs, or route mismatches.
- Resolver diagnostics explain disabled, hidden, and unavailable modules without exposing credentials
  or account-sensitive data.
- Documentation and tests make clear that UI shaping is not backend authorization.

### Suggested PR sequence

1. **Shared contract PR**: DTOs, enums, JSON context, contributor contracts, lifecycle metadata,
   static fixture profiles, resolver tests, and catalog-validation tests.
2. **WPF catalog adapter PR**: map `ShellNavigationCatalog` descriptors, route parity tests, no UI
   behavior change except optional diagnostics.
3. **Endpoint PR**: expose resolved workstation context through UI services with API tests.
4. **WPF shell PR**: consume resolver for shell lists and command-palette explanatory rows while
   retaining static fallback.
5. **Browser PR**: consume the endpoint for workstation landing/navigation cards with static fallback.
6. **Portfolio profile PR**: entity-specific Portfolio module presentation and default pane profiles.

## Open Questions

- Which persisted entity source should become authoritative after fixture profiles: existing fund
  structure services, a new organization/entity registry, or a shared account/entity service?
- Should saved workspace layouts be keyed by entity ID, entity kind, or both? The recommended default
  is both: entity ID first, entity kind fallback second.
- Should disabled/unavailable modules appear in command-palette search by default, or only under a
  diagnostics toggle? The recommended first slice is to include them when searched directly but keep
  them out of normal primary navigation.
- How should role overlays integrate with future backend authorization? The first slice should treat
  role overlays as presentation hints only and avoid security claims.
