# Ui.Shared Decomposition Plan

**Status:** proposed
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

## Target shape

```text
Meridian.Workstation.Reporting      (new)  report packs, reporting runs/schedules, statements, capital-call notices
Meridian.Workstation.Accounting     (new)  accounting configuration/readiness, ledger provenance, fund-account close, reconciliation reads
Meridian.Workstation.SecurityMaster (new)  security-master workstation services
Meridian.Workstation.Operations     (new)  provider/brokerage connection, backfill coordination, Lean export, trading/risk/strategy reads
Meridian.Workstation.Evidence       (new)  Evidence/ graph, contributors, statement-to-report workflow
Meridian.Ui.Shared                  (kept) Endpoints/, DTO extensions, auth, streaming, serialization, HtmlTemplateGenerator
```

Dependency rules for the new projects:

- They may reference domain projects (`Contracts`, `Application`, `Reporting`, `Storage`, and so
  on) and each other only in one direction: `Evidence` → `Reporting` → `Accounting`.
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

- Generate a dependency inventory of `Services/` and `Evidence/`: which types each file uses,
  which `internal` members cross a proposed cluster boundary, and which DI registration owns them.
  Commit it under `docs/generated/` or keep it as a CI artifact, following
  `docs/engineering/generated-output-delivery.md`.
- Add a ratchet test in `tests/Meridian.Tests/Architecture/` that fails if `Ui.Shared/Services/`
  gains files or lines beyond the current baseline. This stops the project growing while the split
  is under way. Follow the style of `AccountingSemanticsBoundaryTests.cs`.
- Agree the project names and cluster boundaries (this document).

### Phase 1: Reporting (largest cluster, ~36K lines, 2–3 PRs)

- Create `Meridian.Workstation.Reporting`.
- Move `ReportPack*`, `Reporting*`, `Report*` read and command services, `Statement*` and
  `CapitalCall*` in batches, each PR independently green.
- Split the reporting part out of `WorkstationServiceCollectionExtensions` into
  `AddWorkstationReporting(this IServiceCollection)` in the new project, and call it from the
  existing extension so the host's registration order doesn't change.
- Keep the `[Obsolete]` reporting compatibility types (`ReportingScheduleStore`,
  `ReportingScheduleService` members, `ReportingRunCertificationService` legacy overload) as they
  are. Moving them is in scope; removing them is not.
- Validation: targeted test projects that cover reporting
  (`tests/Meridian.Tests` with `FullyQualifiedName~Reporting`), plus a full solution build.

### Phase 2: Accounting, ledger and fund operations (~24K lines, 2 PRs)

- Create `Meridian.Workstation.Accounting` and move the cluster.
- Watch the existing rule in `AccountingSemanticsBoundaryTests`. Extend that test to cover the new
  project rather than weakening it.
- `AccountingProductionReadinessService.cs` (~3.4K lines) moves as one file here. Splitting it
  belongs to `docs/engineering/plans/god-file-burn-down-plan.md`, not this plan.

### Phase 3: Evidence (~16K lines, 1 PR)

- Move `Evidence/` to `Meridian.Workstation.Evidence`, including
  `StatementToReportWorkflowService.Compatibility.cs`.
- The legacy `StatementToReport` HTTP routes stay in `Ui.Shared/Endpoints/`. Only the services
  move.

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
