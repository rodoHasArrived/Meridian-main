# Ui.Shared Decomposition Plan

**Status:** active (Phase 1a in progress)
**Owner:** core-team
**Reviewed:** 2026-11-25

## Problem

`src/Meridian.Ui.Shared` was meant to be a thin layer that adapts service output into workstation
endpoints and DTOs (see `docs/architecture/module-map.md`, "UI Shared"). It has grown into the
largest project in `src/`:

| Folder | Files | Lines |
| --- | ---: | ---: |
| `Services/` | 367 | ~133K |
| `Endpoints/` | 262 | ~77K |
| `Evidence/` | 39 | ~16K |
| `Workflows/`, `Extensibility/`, `Contracts/`, `Streaming/`, `Serialization/`, root files | ~55 | ~6K |

It has 25 project references, which reach almost every domain project. That includes
`Application`, `Storage`, `Execution`, `Risk`, `Reporting`, `FinancialOperations`, `FSharp.Ledger`
and `Strategies`. Most of `Services/` is domain or backend logic, not UI adaptation. The biggest
groups, by file-name prefix:

| Cluster | Main prefixes | Approx. lines |
| --- | --- | ---: |
| Reporting and report packs | `Report*`, `Reporting*`, `ReportPack*`, `Statement*`, `CapitalCall*` | ~36K |
| Accounting, ledger and fund operations | `Accounting*`, `Ledger*`, `Fund*`, `*Provenance*`, `Reconcil*` | ~24K |
| Security master | `Security*` | ~6K |
| Providers and brokerage | `Provider*`, `Brokerage*`, `Alpaca*`, `Backfill*`, `LeanAutoExport*` | ~12K |
| Trading, risk and strategy | `Trading*`, `Risk*`, `Strategy*`, `Execution*`, `Portfolio*`, `Bond*` | ~8K |
| Workstation shell and read models | `Workstation*`, workspace and presentation services | the rest |

Two facts shape the plan:

1. **The WPF client links Ui.Shared directly.** `Meridian.Wpf` and `Meridian.Ui.Services` both
   reference `Meridian.Ui.Shared.csproj`. 41 WPF files import its namespaces, mostly
   `Meridian.Ui.Shared.Services`. That means the desktop client carries an ASP.NET Core project
   (`Microsoft.AspNetCore.App`, Swashbuckle, OpenApi) just to reach domain services. Splitting the
   project fixes this for free.
2. **The two halves are tangled.** Ui.Shared declares 116 `internal` types, and
   `WorkstationServiceCollectionExtensions` / `EvidenceWorkflowServiceCollectionExtensions` wire
   everything together. Moving code out will make some `internal` types cross an assembly
   boundary.

## Goal

Ui.Shared ends up as an **ASP.NET adapter only**: endpoint maps, DTO projection, auth/session
adapters and streaming. Domain read and command services move to projects with no ASP.NET
dependency. Both the web host and WPF consume those projects directly.

Non-goals: changing behaviour, routes, DTO shapes, persisted formats, or the WPF/browser feature
set. Each phase is a pure move plus wiring change.

## Phase 0 findings

The [inventory](ui-shared-decomposition-inventory.md) (from `build/scripts/ui-shared-inventory.py`)
changed the plan in four ways:

1. **Reporting, Accounting and Evidence depend on each other in cycles.** For example, accounting
   readiness reads `ReportPackRunReadService`, and Evidence reads `PostedLedgerAmountProvenanceService`
   and `TrialBalanceAmountProvenanceService`. One-directional projects aren't possible yet, so these
   three clusters (about 93K lines) first move together into one project. They split later, once
   the cycles are broken behind interfaces.
2. **50 movable files reach back into the adapter layer.** Nearly all of them go through a few
   seams: the HTTP tenant-context accessor (`HttpContextWorkstationTenantContextAccessor` /
   `IWorkstationTenantContextAccessor`), `EndpointAuthorization`, enums and DTOs declared next to
   endpoints (`ApprovalState`, `OperationsContinuityWorkflowDto`, `OperationsTimelineEntryDto`), and
   source-generated JSON contexts under `Serialization/`. These have to move down before anything
   else can leave, so they get their own phase (1a).
3. **About 28K lines of services have no domain prefix** (the "Workstation" bucket). These include
   `WorkstationServiceCollectionExtensions`, `WorkstationWorkflowSummaryService`, the audit-trail
   and intercompany authorities, and `OmsIntegrationService`. Each needs triage: stay as adapter, or
   move with a domain.
4. **Internals visibility crosses assemblies.** `Meridian.Application` grants
   `InternalsVisibleTo("Meridian.Ui.Shared")`, and `Ui.Shared` grants it to `Meridian.Tests`,
   `Meridian.Storage.Tests` and `Meridian.Execution.Tests`. Every new project needs the matching
   grants, and moved code that uses Application internals needs an explicit grant from Application.
   43 internal types are used outside their own cluster.

## Target shape

```text
Meridian.Workstation.Abstractions  (new)  tenant-context interface, shared workflow DTOs/enums, JSON contexts used by services
Meridian.Workstation.Finance       (new)  reporting, accounting/ledger/fund operations and Evidence (split later once acyclic)
Meridian.Workstation.SecurityMaster (new) security-master workstation services
Meridian.Workstation.Operations    (new)  provider/brokerage connection, backfill coordination, Lean export, trading/risk/strategy reads
Meridian.Ui.Shared                 (kept) Endpoints/, DTO extensions, HTTP tenant accessor, auth, streaming, HtmlTemplateGenerator
```

Dependency rules for the new projects:

- They may reference domain projects (`Contracts`, `Application`, `Reporting`, `Storage`, and so
  on) and `Workstation.Abstractions`. `Finance`, `SecurityMaster` and `Operations` must not
  reference each other until the inventory shows the needed edges are one-directional.
- They must **not** reference `Meridian.Ui.Shared`, `Microsoft.AspNetCore.App`, or any WPF
  assembly.
- `Meridian.Ui.Shared` references the new projects. `Meridian.Wpf` and `Meridian.Ui.Services` move
  their references from Ui.Shared to the specific new project or projects they need.

The `Meridian.Workstation.*` name is a proposal. Settle it in Phase 0 and check it against
`docs/architecture/module-map.md` naming before creating any project.

**Keep namespaces during moves.** Each moved file keeps its `Meridian.Ui.Shared.Services`
namespace in its first PR, so callers, `using` directives and reflection-based registrations don't
change. A separate, purely mechanical PR per project renames namespaces afterwards (or the old ones
are kept on purpose and documented as legacy).

## Phases

Each phase is one or more PRs, and each must leave `Meridian CI / quality-gate` green.

### Phase 0: Guardrails and inventory (1 PR, no moves)

- Done: `build/scripts/ui-shared-inventory.py` and the reviewed
  [inventory snapshot](ui-shared-decomposition-inventory.md). Findings are summarized above.
- Done: `tests/Meridian.Tests/Architecture/UiSharedServiceGrowthRatchetTests.cs`. It caps the
  number of distinct service-like type names (`*Service`, `*Store`, `*Coordinator` and so on) in
  `Ui.Shared/Services/` (226) and `Evidence/` (16). It counts type names rather than files or
  lines, so bug fixes and the god-file plan's partial-class splits aren't blocked. Each move PR
  lowers the ceilings.
- Done: project names and cluster boundaries approved (2026-11-25).

### Phase 1a: Move the shared seams down (1–2 PRs)

- Create `Meridian.Workstation.Abstractions` (no ASP.NET reference).
- Move `IWorkstationTenantContextAccessor`, the workflow enums and DTOs that services use
  (`ApprovalState`, `OperationsContinuityWorkflowDto` and similar), and the JSON contexts those
  services serialize with. `HttpContextWorkstationTenantContextAccessor` and
  `EndpointAuthorization` stay in `Ui.Shared`. Services that call `EndpointAuthorization` directly
  get an abstraction for it, or stay behind until they do.
- Keep namespaces, so this is a move plus `.csproj` change only.
- Success measure: the inventory's "Movable files that reference the adapter layer" list shrinks to
  the files that genuinely need HTTP types.

Progress:

- PR 1 (done): created `Meridian.Workstation.Abstractions` (references `Contracts`, `Identity` and
  `ProviderSdk` only). Moved `Contracts/` (5 files), the `WorkstationTenantContext` record and
  `IWorkstationTenantContextAccessor`, and 5 JSON contexts (CoveredCall, DirectLending,
  FamilyOffice, IntercompanyOperations, QualityApi). `WorkstationAbstractionsBoundaryTests`
  enforces the no-ASP.NET and no-`Ui.Shared` rules. Back-references fell from 50 files to 25.
- PR 2 (next): replace direct uses of the concrete `HttpContextWorkstationTenantContextAccessor`
  (12 files) with the interface, put `EndpointAuthorization` checks used by services (6 files)
  behind an abstraction, and move the remaining JSON contexts once their payload types can move
  (`WorkstationOperationsJsonContext` needs `Meridian.Strategies`; `PeriodAdjustment`,
  `ExecutionWorkflow` and `ReportingCertifiedArtifact` contexts serialize types still in
  `Services/`, so they move with Phase 1b).

### Phase 1b: Finance (reporting, accounting and evidence, ~93K lines)

- Create `Meridian.Workstation.Finance`. Because of the cycles described above, reporting,
  accounting and Evidence move into this one project.
- A moved file can't reference code left in `Ui.Shared` (that would be a back-reference), so a
  batch must take a whole cycle at once. In practice that's one large pure-move PR for the three
  clusters, plus small follow-ups for stragglers the compiler finds. Re-run the inventory first and
  move only files whose remaining `Ui.Shared` dependencies are already in `Abstractions` or in the
  batch itself.
- Split the reporting part out of `WorkstationServiceCollectionExtensions` into
  `AddWorkstationFinance(this IServiceCollection)` in the new project, and call it from the
  existing extension so the host's registration order doesn't change.
- Keep the `[Obsolete]` reporting compatibility types (`ReportingScheduleStore`,
  `ReportingScheduleService` members, `ReportingRunCertificationService` legacy overload) as they
  are. Moving them is in scope; removing them is not.
- Validation: targeted test projects that cover reporting
  (`tests/Meridian.Tests` with `FullyQualifiedName~Reporting`), plus a full solution build.

### Phase 2 and 3: (merged into Phase 1b)

Accounting, ledger, fund operations and `Evidence/` move with reporting in Phase 1b because of the
cycles. Two notes carried over:

- `AccountingSemanticsBoundaryTests` must be extended to cover `Meridian.Workstation.Finance`,
  not weakened.
- The legacy `StatementToReport` HTTP routes stay in `Ui.Shared/Endpoints/`. Only the services
  move. `AccountingProductionReadinessService.cs` moves whole; splitting it belongs to
  `god-file-burn-down-plan.md`.

### Phase 4: Security master and operations (~18K lines, 2 PRs)

- Move `Security*` to `Meridian.Workstation.SecurityMaster`.
- Move providers, brokerage, backfill, Lean export, trading, risk, strategy and bond reads to
  `Meridian.Workstation.Operations`. Root files `LeanAutoExportService.cs` and
  `LeanSymbolMapper.cs` go with them.

### Phase 5: Cut WPF loose from Ui.Shared (1 PR)

- Point `Meridian.Wpf.csproj` and `Meridian.Ui.Services.csproj` at the specific `Workstation.*`
  projects they use, and remove their `Meridian.Ui.Shared` reference.
- Anything WPF still needs from Ui.Shared at that point (for example `Workflows/` or
  `Contracts.Integrations`) is either moved to a shared non-ASP.NET project or explicitly listed
  as a blocker.
- Add an architecture test asserting `Meridian.Wpf` doesn't reference `Meridian.Ui.Shared` or
  `Microsoft.AspNetCore.App`.

### Phase 6: Trim Ui.Shared and update docs (1 PR)

- Triage the remaining unprefixed "Workstation" services: keep as adapter, or move with a domain.
- Optionally split `Meridian.Workstation.Finance` into reporting, accounting and evidence projects
  once the inventory shows one-directional edges.
- Trim `Meridian.Ui.Shared.csproj` project references down to what endpoints actually need.
- Decide what happens to `HtmlTemplateGenerator*.cs`: document it as the deliberate fallback page,
  or move it into the workstation. That decision is separate from this split.
- Update `docs/architecture/module-map.md`, `docs/architecture/project-structure.md`,
  `src/Meridian.Ui.Shared/README.md`, and the regenerated navigation docs
  (`python build/python/cli/buildctl.py regen --fix`).
- Tighten the Phase 0 ratchet to forbid any domain service in `Ui.Shared/Services/`.

## Working rules for every phase

- **Pure moves.** Use `git mv`, keep namespaces, and make no logic edits in the same PR. A reviewer
  should be able to confirm the diff is moves plus wiring.
- **`internal` types.** When a moved type needs an `internal` member that stays behind (or the
  other way round), first move the dependency into the same project. Use `InternalsVisibleTo` only
  as a temporary bridge, record it in the PR, and remove it before the phase ends.
- **DI order.** Keep service registration order the same. Each new project exposes one
  `Add…(IServiceCollection)` extension, called from the same place the old registrations were.
- **Tests.** Move nothing in `tests/` unless a test project must gain a reference. Tests that
  reflect over `Meridian.Ui.Shared` assemblies (for example by assembly name) need checking in
  Phase 0's inventory.
- **No central package changes.** New projects use existing `Directory.Packages.props` versions.

## Risks

| Risk | Mitigation |
| --- | --- |
| Hidden cycles: a moved service calls back into an endpoint helper or a `DtoExtensions` method in Ui.Shared | Phase 0 inventory flags these. Move the helper down, never add a back-reference. |
| `internal` coupling forces `InternalsVisibleTo` sprawl | Move tightly coupled types together, and remove bridges per phase. |
| Reflection or assembly-scanning registration misses types in the new assembly | Grep for `Assembly.GetExecutingAssembly`, `typeof(...).Assembly` and scan-based registration in Phase 0, and add the new assemblies explicitly. |
| Long-lived branches conflict with active work in Ui.Shared | Keep each PR small (one cluster or sub-cluster) and merge quickly. Avoid phases that overlap the same cluster as in-flight feature work. |
| WPF build only verifiable on Windows | Rely on CI `verify-desktop` for Phase 5. Locally, use `/p:EnableWindowsTargeting=true` builds. |

## Done when

- `Ui.Shared/Services/` contains only endpoint-facing adapters, and the ratchet enforces that.
- `Meridian.Wpf` builds without referencing `Meridian.Ui.Shared` or ASP.NET Core.
- Each new project has a README and appears in `docs/architecture/module-map.md`.
- No route, DTO, or persisted format changed. The existing endpoint and contract tests pass
  unchanged.

## Relationship to other plans

- `god-file-burn-down-plan.md` owns splitting individual large files. This plan only moves them.
- `wpf-web-ui-alignment-plan.md` benefits from Phase 5, because WPF parity surfaces can consume the
  same domain projects as the browser host without pulling in ASP.NET.
