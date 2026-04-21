# Environment Designer Runtime Projection and WPF Admin Surface

## Summary

Meridian now has a first-pass environment designer that sits above the existing organization-rooted
fund-structure graph.

The design keeps `Organization` as the umbrella company root, stores environment drafts separately
from the runtime graph, and publishes a compiled runtime projection that the workstation shell can
consume without changing the existing `Research`, `Trading`, `Data Operations`, and `Governance`
workspace IDs.

## Key Components

### Contracts

`src/Meridian.Contracts/EnvironmentDesign/EnvironmentDesignDtos.cs`

- Defines design-time drafts, lanes, nodes, relationships, validation output, publish previews,
  published versions, and runtime projections.
- Reuses existing Meridian primitives through `EnvironmentNodeKind` instead of introducing a new
  parallel graph model.
- Adds `ClientSegmentKind` integration so advisory clients can be classified as
  `IndividualInvestor` or `FamilyOffice` while funds remain first-class `Fund` nodes.

### Application services

`src/Meridian.Application/EnvironmentDesign/`

- `IEnvironmentDesignService`
  Draft CRUD plus published-version queries.
- `IEnvironmentValidationService`
  Structural validation and publish-impact checks.
- `IEnvironmentPublishService`
  Preview, publish, and rollback workflow.
- `IEnvironmentRuntimeProjectionService`
  Read access to the currently published runtime model.
- `EnvironmentDesignerService`
  Local-first JSON-backed implementation for all four seams.

## Persistence Model

The designer persists independently of `fund-structure.json`.

- Storage feature host path:
  `{StorageRoot}/governance/environment-designer.json`
- WPF desktop path:
  `%LocalAppData%/Meridian/environment-designer.json`

This split keeps the existing structure graph as the operational compatibility surface while the
environment designer remains the admin-facing source for drafts, versions, and published runtime
state.

## Publish Flow

1. Admin creates or edits an `EnvironmentDraftDto`.
2. Validation checks:
   - orphaned nodes
   - invalid parent/child combinations
   - duplicate business/client/fund codes
   - invalid lane workspace or landing-page defaults
   - destructive removal of published accounts, portfolios, or ledger groups without remap/approval
3. Preview produces a publish diff.
4. Publish compiles the draft into:
   - `OrganizationStructureGraphDto`
   - lane runtime metadata
   - context mappings
   - runtime ledger-group projections
5. Published versions are versioned and can be rolled back.

## Runtime Compilation Model

`EnvironmentDesignerService` does not mutate `IFundStructureService`.

Instead, publish compiles the environment draft into a `PublishedEnvironmentRuntimeDto` that:

- preserves stable runtime GUIDs for unchanged design nodes across republishes
- emits an organization graph compatible with current workstation and governance read flows
- keeps ledger groups in a dedicated runtime projection because `LedgerGroup` is not part of the
  current `FundStructureNodeKindDto` set
- records lane-to-context mappings so UI surfaces can apply environment-aware defaults

## Workstation Integration

`src/Meridian.Wpf/Services/WorkstationOperatingContextService.cs`

- Prefers `IEnvironmentRuntimeProjectionService` when a published environment exists.
- Builds operating contexts from the compiled runtime graph and runtime ledger groups.
- Applies lane metadata to contexts:
  - `EnvironmentLaneId`
  - `EnvironmentLaneName`
  - `OperatingEnvironmentKind`
  - lane-specific default workspace and landing-page behavior
- Falls back to the existing `IFundStructureService` graph path when no published environment is
  available.

This preserves compatibility with legacy fund-profile contexts while making the workstation shell
lane-aware for published environment designs.

## WPF Admin Surface

`src/Meridian.Wpf/Views/EnvironmentDesignerPage.xaml`

The initial admin surface is a schema-constrained JSON designer:

- left rail for drafts and published versions
- center JSON editor for full draft composition
- right-side inspector for draft summary, validation output, and publish diff/rollback state
- starter-draft actions for blank company umbrellas and advisory-practice seed models

This is intentionally not a free-position graph canvas yet. The first release favors deterministic
editing, validation, and publish behavior over visual node layout.

## Endpoint Surface

`src/Meridian.Ui.Shared/Endpoints/EnvironmentDesignerEndpoints.cs`

New routes under `/api/environment-designer` expose:

- draft list/load/create/save/delete
- validation
- publish preview
- publish
- version list/load/current
- rollback
- current runtime and runtime-by-version

These routes are registered through both shared endpoint mapping paths:

- `MapUiEndpoints(...)`
- `MapUiEndpointsWithStatus(...)`

That keeps the environment designer available in the status-backed desktop-local `UiServer`, not
just in stripped-down test or custom host setups.

## Performance Notes

The environment designer is an admin path, not a hot path.

- The runtime compiler is publish-time only and operates over in-memory draft collections.
- Context generation remains linear over the compiled runtime graph and is bounded by operator
  setup size rather than market-data throughput.
- No streaming, channel, or tick-processing paths were changed.

## Current Limitations

- The runtime projection is compiled in parallel to the existing fund-structure service; publish
  does not yet rewrite or reconcile persisted `fund-structure.json`.
- The WPF designer is JSON-backed rather than a visual graph editor.
- Existing WPF project/test build issues outside this change still block a clean full-desktop
  validation lane, so the verified pass lane for this work is currently the contracts/application
  plus shared-endpoints path.
