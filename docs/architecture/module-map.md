# Module Map

**Status:** Active
**Owner:** Core Team
**Reviewed:** 2026-05-18

This map gives maintainers a quick layer-oriented view of Meridian.

## Runtime Flow

```mermaid
flowchart LR
  Host["Meridian host and CLI"] --> Application["Application workflows"]
  Application --> Domain["Domain events and models"]
  Application --> Storage["Storage and WAL"]
  Application --> Providers["Provider adapters"]
  Application --> Execution["Execution and risk"]
  Application --> UiServices["UI services and shared endpoints"]
  UiServices --> Web["Browser workstation"]
  UiServices --> Wpf["WPF desktop shell"]
```

## Layer Responsibilities

| Layer | Projects | Rule |
| --- | --- | --- |
| Host | `src/Meridian` | Compose services, expose CLI/API modes, and host workstation endpoints |
| Application | `src/Meridian.Application` | Coordinate workflows; keep UI and provider specifics out |
| Domain/Core/Contracts | `src/Meridian.Domain`, `src/Meridian.Core`, `src/Meridian.Contracts` | Keep business and contract types UI-independent |
| Providers/Infrastructure | `src/Meridian.Infrastructure*`, `src/Meridian.ProviderSdk` | Isolate external API integration behind provider contracts |
| Storage | `src/Meridian.Storage` | Preserve WAL and atomic-write durability expectations |
| Execution/Risk | `src/Meridian.Execution*`, `src/Meridian.Risk` | Isolate broker gateways, paper/live controls, and pre-trade validation |
| Strategy/Backtesting | `src/Meridian.Strategies`, `src/Meridian.Backtesting*`, `src/Meridian.QuantScript` | Keep strategy lifecycle, replay, and scripting reusable outside UI |
| UI Shared | `src/Meridian.Ui.Services`, `src/Meridian.Ui.Shared` | Own workstation DTO projection and endpoint/read-model support |
| UI Surfaces | `src/Meridian.Ui/dashboard`, `src/Meridian.Wpf` | Keep views thin; put state, labels, disabled reasons, and commands in view models |

## Boundary Checks

- Domain, core, storage, providers, execution, strategy, and backtesting projects
  must not depend on UI projects.
- Browser workstation logic should prefer view-model/read-model seams instead of
  hardcoding workflow state in React components.
- WPF is the active desktop shell; new desktop workflow lanes are expected and encouraged.
- Design tokens and shared UI patterns should come from the Meridian Design
  System or local shared dashboard primitives, not one-off screen styling.

## Fund Structure Service Refactor Boundaries (Staged Migration)

- `IFundStructureService` remains the caller-facing application contract for UI/API layers.
- Command handling and workflow orchestration stay in `InMemoryFundStructureService` so existing endpoint behavior and method contracts remain stable during migration.
- Persistence concerns are now isolated behind `IFundStructureStateStore` with adapters:
  - `JsonFileFundStructureStateStore` for durable local snapshots.
  - `InMemoryFundStructureStateStore` for test/dev ephemeral state.
- Validation/policy rules that do not require storage are owned by `IFundStructurePolicyService` (`FundStructurePolicyService`) so PostgreSQL-backed services can reuse identical domain checks.
- During PostgreSQL adoption, new persistence adapters should implement dedicated persistence ports and be injected into orchestration services rather than embedding storage calls inside domain rule paths.

### Method Category Map (Current In-Memory Services)

- `InMemoryFundStructureService`
  - Command handling: `Create*Async`, `UpdateOwnershipLinkAsync`, `AssignAccountAsync`, `SynchronizeWithSharedDataAsync`.
  - Query projection: `Get*Async`, `Query*Async`, `GetGovernanceCashFlowAsync`.
  - Validation/policy: delegated to `IFundStructurePolicyService` for pure rules; existence/graph invariants remain service-local for now.
  - Workflow orchestration: parent-link creation, shared-data synchronization, cross-service composition with account/security-master services.
  - Storage concerns: snapshot capture/load/save via `IFundStructureStateStore`.
- `InMemoryFundAccountService` (peer)
  - Command handling: account creation/update/deactivation and reconciliation write flows.
  - Query projection: account query/read methods and filtered projections.
  - Validation/policy: account status policy (`EnsureAllowed`) and request invariants.
  - Workflow orchestration: account lifecycle sequencing around reconciliation and synchronization state.
  - Storage concerns: snapshot capture/load/save and in-memory aggregate persistence.
