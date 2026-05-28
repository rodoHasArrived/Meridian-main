---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-UI-SHARED
path: src/Meridian.Ui.Shared
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-05-28
---

# src/Meridian.Ui.Shared

## Purpose

UI shared contains shared UI read models and compatibility shims for browser and desktop surfaces.

## Layer responsibility

This module owns cross-surface operator-facing projection types and shared endpoint helpers. Preserve
compatibility across `src/Meridian.Ui.Services`, `src/Meridian.Ui/dashboard`, and
`src/Meridian.Wpf`.

## Key folders and files

- `Evidence/` - shared evidence graph contributors and subject routing for strategy runs, reconciliation reviews, statement runs, report packs, provider trust gates, and retained exports.

- `Endpoints/` - shared workstation endpoint mapping and projection helpers.
- Shared read models - DTOs and compatibility shims consumed by browser and desktop clients.
- Project metadata - UI shared dependencies and build settings.

## Important workflows

Preserve cross-surface compatibility when evolving shared read models. Keep ledger/reconciliation
source-of-truth services authoritative. Workstation endpoint registration is split by domain through
`WorkstationEndpoints.*.cs` partial files. Keep the root `WorkstationEndpoints.cs` file as the
coordinator, route new domain-specific endpoint edits to the matching partial file, and avoid
concurrent branches that both modify the root coordinator or the shared
`WorkstationEndpointsTests.cs` test body. For operations-continuity and reconciliation endpoint
changes, start with focused `MapWorkstationEndpoints_OperationsContinuity` /
`MapWorkstationEndpoints_Reconciliation` filters before broad workstation endpoint validation.
Report-pack workflow state is shared here as well: publication requires sign-off, evidence hash,
retained manifest metadata, and retained evidence links for every report-line provenance pointer so
browser and WPF clients do not invent local lifecycle or no-orphan-evidence rules.
Evidence packet validation also owns the shared SLA/freshness policy and Meridian Assurance Score
calculation for provider validation, replay, reconciliation, approval, and reporting evidence so
client surfaces consume the same readiness posture instead of recalculating it locally.
Provider-ledger reconciliation is shared service/API behavior: it reads the latest brokerage sync
projection, compares it with the internal fund-account balance snapshot, validates Security Master
coverage, and retains the latest detail under workstation data for browser and WPF clients to
consume later without adding client-specific reconciliation logic. Break records preserve stable
keys, owner assignment, tolerance, first/last-observed aging, and sign-off state from the previous
latest detail so repeated provider-ledger variances can be controlled as accounting casework.
Reconciliation details also emit provider-to-Security-Master confidence passports for every
provider position, preserving the resolution path, confidence score, validation issue codes, and
identifier-conflict evidence alongside the persisted accounting detail.
Reconciliation casework hardening is a shared Accounting workflow: break queue read models now carry owner, priority, SLA policy/due-warning-breach state, business age, taxonomy codes, comment/evidence counts, sign-off/reopen metadata, and optimistic concurrency versions. Lifecycle mutations and bulk triage flow through shared workstation endpoints with immutable audit events, cached bulk status/result lookup, and compatibility review/resolve wrappers so WPF and browser clients consume the same casework state.
Reconciliation casework hardening is a shared Accounting workflow: break queue read models now carry owner, priority, SLA policy/due-warning-breach state, business age, taxonomy codes, comment/evidence counts, sign-off/reopen metadata, source-origin metadata, and optimistic concurrency versions. Lifecycle mutations flow through shared workstation endpoints with immutable audit events and compatibility review/resolve wrappers so WPF and browser clients consume the same casework state. Statement-originated breaks project into the same queue with stable material fingerprints so repeated imports of the same statement do not create duplicate open cases; provider-ledger breaks continue to carry provider-ledger source metadata.
Reconciliation casework hardening is a shared Accounting workflow: break queue read models now carry owner, priority, SLA policy/due-warning-breach state, business age, taxonomy codes, comment/evidence counts, sign-off/reopen metadata, and optimistic concurrency versions. Lifecycle mutations flow through shared workstation endpoints with immutable audit events and compatibility review/resolve wrappers so WPF and browser clients consume the same casework state. Statement-run endpoints expose shared contract DTOs for create, detail, validation, run-scoped breaks, and reconcile commands while endpoint lambdas delegate behavior to shared reconciliation services.

Fund-structure endpoints expose `/api/fund-structure/ledger-mapping-view` as the shared accounting
control surface for account ledger mappings. The endpoint returns server-derived assignment source,
unmapped-account issue codes, and recommended action so browser and WPF surfaces do not invent
client-local mapping or posting readiness rules.
When provider-routing capability metadata is registered, the reconciliation service blocks runs
that cannot route account balances, account positions, or reconciliation-feed capability for the
fund account, so unsupported providers fail as accounting break evidence before ledger comparison.
Positioned accounts also require corporate-action capability as a valuation-readiness signal; if
that capability is unavailable, reconciliation can still run but records a degradation break for
controller review.

## Diagrams

See `DIA-BROWSER-WORKSTATION` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-UI-SHARED -->
| Roadmap item | Title |
| --- | --- |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W4-RPT-001` | Governed report pack readiness |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-UI-SHARED -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Security and credentials

`LoginSessionService` stores the authenticated `UserProfile` for each in-memory session so
account-specific permission overrides and role profile names from `MDC_USERS` remain authoritative
for `/api/auth/me` and endpoint authorization checks. Do not reconstruct session users from only the
built-in role label, because that would reapply the full built-in role permission mask and bypass
configured restrictions.

## Validation

```bash
dotnet test tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj /p:EnableWindowsTargeting=true --logger "console;verbosity=normal"
```

## Change rules

Do not put browser-only or WPF-only product logic here. Keep shared read models compatible and route
domain-specific endpoint edits to the matching partial file.

## Related docs

- `src/Meridian.Ui.Services/README.md`
- `docs/status/contract-compatibility-matrix.md`
- `docs/source/generated/source-module-index.md`
