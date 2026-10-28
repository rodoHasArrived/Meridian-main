# Module Map

**Status:** Active
**Owner:** Core Team
**Reviewed:** 2026-10-28 (bounded source-owner and Security Master seam refresh)

This map gives maintainers a quick layer-oriented view of Meridian.

For complete product design document adaptation, use
[`design-document-adaptation.md`](design-document-adaptation.md). For the physical bounded-context
module structure, use [`design-module-conformance.md`](design-module-conformance.md). Together they
bridge design names such as `Meridian.FinancialOperations` to the current source projects listed
below.

## Runtime Flow

```mermaid
flowchart LR
  Setup["Consumer setup"] --> Launcher["Installed Meridian launcher"]
  Launcher -->|request and verified outcome| Supervisor["Lifecycle supervisor"]
  Supervisor -->|owns process and readiness| Host
  Supervisor --> Database["Dedicated PostgreSQL"]
  Supervisor -->|opens after verified readiness| Web
  Host["Meridian host and CLI"] --> Application["Application workflows"]
  Application --> Domain["Domain events and models"]
  Application --> Storage["Storage and WAL"]
  Application --> Providers["Provider adapters"]
  Application --> Execution["Execution and risk"]
  Application --> Ledger["Ledger and accounting records"]
  Application --> FinancialOperations["Financial Operations workflows"]
  Application --> Instruments["Instrument economics and projections"]
  Application --> ReferenceData["Reference-data catalogs and classification"]
  Application --> Reporting["Reporting and report packs"]
  Application --> Identity["Identity and scoped access"]
  Application --> UiServices["UI services and shared endpoints"]
  Ledger --> UiServices
  Reporting --> UiServices
  Identity --> UiServices
  UiServices --> Web["Browser workstation"]
  UiServices --> Wpf["WPF desktop shell"]
```

## Layer Responsibilities

| Layer | Projects | Rule |
| --- | --- | --- |
| Host | `src/Meridian` | Compose services, expose CLI/API modes, and host workstation endpoints |
| Installed runtime | `src/Meridian.Launcher`, `src/Meridian.LifecycleSupervisor`, `src/Meridian.Setup` | Setup installs the payload; the launcher verifies the request-bound startup outcome; the supervisor owns host/database processes, readiness, and browser opening |
| Application | `src/Meridian.Application` | Coordinate workflows; keep UI and provider specifics out |
| Domain/Core/Contracts | `src/Meridian.Domain`, `src/Meridian.Core`, `src/Meridian.Contracts` | Keep business and contract types UI-independent, including workstation, private-capital, reporting, and identity payloads consumed by multiple surfaces |
| Providers/Infrastructure | `src/Meridian.Infrastructure*`, `src/Meridian.ProviderSdk` | Isolate external API integration behind provider contracts |
| Storage | `src/Meridian.Storage` | Preserve WAL and atomic-write durability expectations |
| Execution/Risk | `src/Meridian.Execution*`, `src/Meridian.Risk` | Isolate broker gateways, paper/live controls, and pre-trade validation |
| Accounting and Ledger | `src/Meridian.Ledger`, `src/Meridian.FSharp.Ledger` | Own ledger behavior, private-capital fund-event reconstruction, capital-account subledger impact, treasury ledger metadata, and accounting-record boundaries |
| Financial Operations | `src/Meridian.FinancialOperations` | Own close, reconciliation, accounting-basis workflows, and preparation of governed ledger instructions |
| Instruments | `src/Meridian.Instruments` | Own instrument economics, Security Master-keyed Asset Operations, cash-flow projections, and factor-paydown projections |
| Reference Data | `src/Meridian.ReferenceData` | Own asset-class/instrument-kind mapping, profile catalog contracts, and seeded approved asset-profile templates |
| Reporting | `src/Meridian.Reporting` | Own governed report-pack behavior, report-writer grids, filters, formulas, lineage, publication, restatement, and report-generation semantics |
| Identity and Access | `src/Meridian.Identity` | Own sessions, role profiles, scoped access, fund/account traversal authorization, and company-scoped user account/profile state |
| Strategy/Backtesting | `src/Meridian.Strategies`, `src/Meridian.Backtesting*`, `src/Meridian.QuantScript` | Keep strategy lifecycle, replay, and scripting reusable outside UI |
| UI Shared | `src/Meridian.Ui.Services`, `src/Meridian.Ui.Shared` | Own workstation DTO projection and endpoint/read-model support for accounting configuration, private-capital drill-through, report packs, auth/session adapters, and shared browser/WPF workflows |
| UI Surfaces | `src/Meridian.Ui/dashboard`, `src/Meridian.Wpf` | Keep views thin; put state, labels, disabled reasons, and commands in view models |

## Boundary Checks

- The installed launcher accepts only `start` and `open`. Maintenance commands go directly
  to `Meridian.LifecycleSupervisor.exe`; keep process ownership and readiness checks in the supervisor.
- Domain, core, storage, providers, execution, strategy, and backtesting projects
  must not depend on UI projects.
- Browser workstation logic should prefer view-model/read-model seams instead of
  hardcoding workflow state in React components.
- Browser workstation work and WPF desktop workstation work are two active co-equal operator UI lanes over shared contracts; WPF's current focus is web-UI parity (`W8-WPF-PARITY-001`).
- Browser surfaces and retained WPF compatibility should consume shared DTOs, services, and route definitions for accounting, reporting, private-capital, and identity workflows instead of duplicating business rules.
- Security Master query and command contracts are owned by `Meridian.Contracts.SecurityMaster`.
  Application implementations, host registration, and workstation callers use that single contract;
  Application does not declare a parallel query interface.
- Typed schedule generation lives in `src/Meridian.Contracts/SecurityMaster/Schedules/`;
  open-lot factor-paydown contracts live in `src/Meridian.Contracts/Accounting/Lots/`.
  `src/Meridian.Instruments/AssetOperations/FactorPaydownProjectionService.cs` projects the economics,
  and `src/Meridian.FinancialOperations/Ledger/FactorPaydownLotInstructionService.cs` prepares the
  ledger and lot instructions. Application composition registers this preparation service, but no
  operator route calls it yet; composition does not establish workstation activation.
- Design tokens and shared UI patterns should come from the Meridian Design
  System or local shared dashboard primitives, not one-off screen styling.

## OMS Runtime Ownership and Lifecycle

`OrderManagementSystem` implements `IOrderManagementLifecycle`. Construction restores retained
identity evidence without starting background work. `StartAsync` starts gateway-report intake and
retained accounting-handoff replay once; order operations require startup. `StopAsync` closes
admission and shares the existing asynchronous disposal drain, including admitted operations and
accepted fills. Cancellation stops the caller's wait, while disposal still waits for the drain.
A stopped instance cannot restart.

`AddBrokerageExecution` registers the hosted lifecycle owner for the resolved `IOrderManager`.
Direct callers must start the OMS explicitly and await stop/disposal before releasing its gateway
or accounting dependencies.

Internal collaborators under `Meridian.Execution/OrderManagement` now own bounded parts of the
runtime. The facade retains public commands, live order state and sidecars, the shared broker-report
and pre-trade admission gates, and command/risk-reservation/amendment/cancellation/kill-switch
coordination.

```mermaid
flowchart LR
  OMS["OMS facade and lifecycle"] --> Policy["OrderAdmissionPolicy / OrderRiskSizingPolicy"]
  OMS --> Recovery["OrderBrokerIdentityIndex / BrokerageOrderRecoveryJournal"]
  OMS --> Fills["OrderFillProcessor"]
  Fills --> Context["OrderFillContext: read-only state access"]
  Fills --> Accounting["AccountingHandoffCoordinator"]
  Fills --> Reports["ExecutionReportHub"]
  Fills --> Audit["OrderExecutionAudit / OrderAuditMetadata"]
  Reports --> Failure["ExecutionFailureState"]
  OMS --> Failure
  OMS --> Audit
```

The recovery journal owns durable checkpoints, permanent dispatched identities, tombstones, and
the recovery-version lock; the identity index owns active broker IDs and replacement aliases.
The fill processor owns progress and pending exposure while reading order ownership/sizing
through `OrderFillContext`. Accounting acceptance and replay belong to the handoff coordinator;
subscriber delivery and observer publication belong to the report hub. Admission and delivery
share the first terminal consumer failure. Audit helpers consume explicit snapshots.

The facade's shared report gate still protects live reports, recovery, amendment resolution, and
retention. The journal's guarded compaction persists the tombstone and performs expected-state
removal under one recovery lock; the facade then removes sidecars. Existing APIs, persistence
formats, alias and fill identity rules, effect ordering, and shutdown drain semantics are
preserved. These are scoped ownership boundaries, with command coordination still in the facade;
see the [execution module README](../../src/Meridian.Execution/README.md#oms-ownership) for the
detailed ownership map.

## Operational Record Boundaries

The current product direction is the W1-W5 operational record baseline: data confidence, retained source evidence, reconciliation, approvals, accounting records, multi-asset operational coverage, and governed reports. Within that baseline:

- `src/Meridian.Contracts` declares shared workstation, ledger, private-capital, report-pack, report-writer, and identity-facing payloads and routes.
- `src/Meridian.Ledger` reconstructs posted private-capital fund-event ledger state, capital-account subledger impact, treasury context, and period/report handoff metadata.
- `src/Meridian.FinancialOperations` owns accounting-close, reconciliation, accounting-record, approval, casework, and close-workflow semantics that coordinate ledger evidence into operational records.
- `src/Meridian.Reporting` renders governed report packs and no-code report-writer grids with saved filters, formulas, filtered-input counts, and lineage.
- `src/Meridian.Ui.Shared` adapts those source-owned services into endpoint/read-model support for the browser workstation and WPF shell.
- External GL and provider systems remain evidence inputs where configured; Meridian-owned ledger records, retained evidence, approvals, and report outputs are the operational record surface.
- Storage journal records, UI projections, report-pack links, and workflow records are intentionally separate persistence or presentation surfaces. They may carry source-owned identifiers, evidence links, state labels, and DTO snapshots, but they must not redefine posting rules, trial-balance math, close-readiness gates, reconciliation classifications, or accounting approval semantics outside `src/Meridian.Ledger` and `src/Meridian.FinancialOperations`.
- New accounting-like models outside the ledger or Financial Operations owners must be projection-, persistence-, reporting-, or workflow-specific, named for that role, and covered by the `AccountingSemanticBoundaryTests` allowlist so reviewers can distinguish intentional boundary surfaces from duplicated accounting semantics.

## Fund Structure Service Refactor Boundaries (Staged Migration)

- `IFundStructureService` remains the caller-facing application contract for UI/API layers.
- Command handling and workflow orchestration stay in `InMemoryFundStructureService` and `PostgresFundStructureService` so existing endpoint behavior and method contracts remain stable during migration.
- `FundStructureProjector` owns the shared, pure organization graph, legacy fund graph, advisory, fund operating, and accounting projections. Each service supplies a captured structure snapshot, authorized accounts, explicit effective time, and resolved shared-data metadata; the projector does not perform I/O or mutate state.
- Persistence concerns are now isolated behind `IFundStructureStateStore` with adapters:
  - `JsonFileFundStructureStateStore` for durable local snapshots.
  - `InMemoryFundStructureStateStore` for test/dev ephemeral state.
- Validation/policy rules that do not require storage are owned by `IFundStructurePolicyService` (`FundStructurePolicyService`) so PostgreSQL-backed services can reuse identical domain checks.
- During PostgreSQL adoption, new persistence adapters should implement dedicated persistence ports and be injected into orchestration services rather than embedding storage calls inside domain rule paths.

### Method Category Map (Current Fund Structure Services)

- `IFundStructureService`
  - Creation commands: `CreateOrganizationAsync`, `CreateBusinessAsync`, `CreateClientAsync`, `CreateFundAsync`, `CreateSleeveAsync`, `CreateVehicleAsync`, `CreateLegalEntityAsync`, `CreateInvestmentPortfolioAsync`.
  - Ownership lifecycle commands: `LinkNodesAsync`, `UpdateOwnershipLinkAsync`, `ExpireOwnershipLinkAsync`, `ReplaceOwnershipLinkAsync`, `ValidateOwnershipGraphAsync`.
  - Assignment commands: `AssignNodeAsync`.
  - Query projection: `GetOrganizationStructureAsync`, `GetFundStructureGraphAsync`, `GetAdvisoryViewAsync`, `GetFundOperatingViewAsync`, `GetAccountingViewAsync`, `GetCashFlowViewAsync`.
- `InMemoryFundStructureService`
  - Command handling: implements the full `IFundStructureService` creation, ownership lifecycle, validation, and assignment surface.
  - Query projection: delegates organization graph, legacy fund graph, advisory, fund operating, and accounting views to `FundStructureProjector`; retains cash-flow orchestration and uses the shared scope helpers.
  - Validation/policy: delegates pure portfolio-parent rules to `IFundStructurePolicyService`; owns existence checks, ownership-link lifecycle checks, and cycle validation.
  - Workflow orchestration: parent-link creation, ownership projection rebuilds after link lifecycle changes, shared-data synchronization, cross-service composition with account/security-master services.
  - Storage concerns: snapshot capture/load/save via `IFundStructureStateStore`, including updated, expired, and replacement ownership links.
- `PostgresFundStructureService`
  - Command handling: implements the same `IFundStructureService` creation, ownership lifecycle, validation, and assignment surface over `IFundStructureStore`.
  - Query projection: delegates the same graph, advisory, fund operating, and accounting views to `FundStructureProjector` after loading and tenant filtering; cash-flow views retain the snapshot-backed in-memory service adapter.
  - Validation/policy: reuses `FundStructurePolicyService` for pure rules and service-local ownership existence/cycle validation for persisted links.
  - Workflow orchestration: loads a mutable snapshot, applies lifecycle mutations, rebuilds ownership projections, and upserts changed rows through the store.
  - Storage concerns: persists organizations, businesses, clients, funds, sleeves, vehicles, entities, portfolios, ownership links, and assignments via `IFundStructureStore`.
- `InMemoryFundAccountService` (peer)
  - Command handling: account creation/update/deactivation and reconciliation write flows.
  - Query projection: account query/read methods and filtered projections.
  - Validation/policy: account status policy (`EnsureAllowed`) and request invariants.
  - Workflow orchestration: account lifecycle sequencing around reconciliation and synchronization state.
  - Storage concerns: snapshot capture/load/save and in-memory aggregate persistence.

## Shared outstanding-principal calculation

`Meridian.Application/SecurityMaster/CashFlow/SecurityPrincipalBalance.cs` owns the
opening outstanding face used by calculated cash flows and quoted-price valuation.
Dated factors absorb principal through their evidence date; later completed principal
payments reduce the balance. Future-dated scalar factors do not price an earlier date,
and an undated current factor is not reduced twice. This calculation is separate from
posting a lot-factor paydown and does not activate its operator route.
