# 2026-08 W9 Close-Out Delivery Plan

**Status:** accepted planning input; live status lives in the roadmap registry
**Owner:** core-team
**Reviewed:** 2026-08-11
**Registry decision:** `DEC-W9-CLOSEOUT-001` in [`docs/roadmap/data/decision-log.yml`](../roadmap/data/decision-log.yml)
**Registry rows:** `W9-SAFETY-007`, `W9-GOV-008`, `W9-INGEST-009` in [`docs/roadmap/data/roadmap-items.yml`](../roadmap/data/roadmap-items.yml)

This document records the delivery sequence adopted on 2026-08-11 for the three open rows of the
2026-07 first-order improvement slate (`DEC-PRIORITY-SLATE-001`). It carries the sequencing
rationale and the source constraints found while checking each row against current code, so the
ordering stays explainable and blueprint work does not have to rediscover them. Treat the roadmap
registry as live truth; nothing here advances a row's status.

## Why this wave, and why now

The W9 slate is the registry's declared priority order, and it is mostly closed: `W9-ASSET-010` is
`done`, and `W9-TRUTH-001`, `W9-DEMO-002`, `W9-PAPER-003`, `W9-ALPACA-004`, `W9-REPORT-005`, and
`W9-NAV-006` are `ready_for_acceptance`. Three rows remain open.

`DEC-DEPTH-SLATE-001` sequences the entire W10 depth slate behind W9, so a partly-open slate blocks
the accepted next slate. Each open row also leaves a live defect rather than a missing feature:

| Row | Status entering the wave | What is actually open |
| --- | --- | --- |
| `W9-SAFETY-007` | `in_progress` | Fat-finger and price-collar rules absent from the mandatory validator; the two-lane safety-control sweep unaudited |
| `W9-GOV-008` | `planned` | 152 mutating routes process permissionless requests; tenancy enforcement is off by default; accounting/ledger audit events are not hash-chained |
| `W9-INGEST-009` | `planned` | Golden-file, bounded-ingress, and matcher-determinism evidence missing for the institutional formats |

Closing all three also supplies evidence toward `PRD-006`, `PRD-007`/`PRD-009`, and
`PRD-010`/`PRD-101` in the [production-readiness tracker](implementation-todo-list.md). It does not
substitute for that tracker's P0 release gate.

## Sequence

Eleven changes in three phases, one roadmap row per change, with the row's registry status and
evidence advanced in the same change.

| # | Phase | Change | Row | Criterion discharged |
| --: | --- | --- | --- | --- |
| 1 | A | Fat-finger quantity and price-deviation rules | `W9-SAFETY-007` | Pre-trade rule catalogue |
| 2 | A | Price-collar rule | `W9-SAFETY-007` | Pre-trade rule catalogue |
| 3 | A | Two-lane safety-control sweep | `W9-SAFETY-007` | No dead safety buttons |
| 4 | B | Declarative authorization assertion + `/api/fund-structure` tranche | `W9-GOV-008` | Route authorization coverage |
| 5 | B | `/api/workstation` + `/api/auth` tranche | `W9-GOV-008` | Route authorization coverage |
| 6 | B | Operations-surface tranche | `W9-GOV-008` | Route authorization coverage |
| 7 | B | Data and diagnostics tranche | `W9-GOV-008` | Route authorization coverage |
| 8 | B | Remainder to zero | `W9-GOV-008` | Route authorization coverage |
| 9 | B | Fail-closed tenancy + hash-chained accounting audit | `W9-GOV-008` | Tenancy rejection; tamper-evident audit |
| 10 | C | Golden-file packs and bounded-ingress proof | `W9-INGEST-009` | Connector evidence; PRD-010 limits |
| 11 | C | Deterministic sided matching and casework feed | `W9-INGEST-009` | Match determinism; casework feed |

### Ordering rationale

**Safety first (1–3).** `W9-SAFETY-007` is the only `in_progress` row and the smallest. Rules
precede the control sweep because the sweep's acceptance argument is that every surfaced control
reaches a real service, and the rule catalogue is part of what those controls govern.

**Instrument before burn-down (4).** The mechanical sweep is the measurement, so the declarative
assertion lands with the first tranche rather than after the count reaches zero. Sharpening the
instrument once the baseline is already empty would prove nothing about the routes fixed before it.

**Tranches by owner lane, largest first (4–8).** Each tranche maps to one owner lane so review stays
tractable, and `/api/fund-structure` leads because it is 34 of the 152 and is mapped as a single
route group. `/api/auth` is pulled early despite its small count: account disable, password reset,
and access revocation are the most sensitive unguarded mutations in the set.

**Tenancy and audit last within the row (9).** Both are behaviour-changing rather than additive, and
both are easier to reason about once every route in the sweep declares a permission.

**Ingestion last (10–11).** It is evidence work against code that already ships, so it carries the
least risk of colliding with the other two rows.

## Posture relative to production readiness

Production readiness stays `blocked` in
[`program-state.yml`](../roadmap/data/program-state.yml), gated on every P0 row in the
[production-readiness tracker](implementation-todo-list.md) completing on one release commit. This
wave advances evidence for several P0 controls but does not close the gate and does not certify a
release. Change 9's cross-process audit-chain proof is shared with `PRD-009`; change 10's bounded
parsing is shared with `PRD-010`; changes 1–3 are shared with `PRD-006`.

## What this wave is not

- It is not a completion claim. Each row moves only to `ready_for_acceptance`; `done` requires
  operator acceptance.
- It does not open W10. `W10-MARK-001` keeps its reserved migration ordinals (036–038) and its
  blueprint; this wave takes 039 or higher.
- It does not reopen deferred lanes, add a root workspace, or extend the risk-engine blueprint
  beyond its shipped PR 1. The decision journal, `/api/risk/decisions` read surface, and their WPF
  parity remain design-only and are recorded as a deliberate deferral on `W9-SAFETY-007`.
- It does not renegotiate the tranche boundaries as scope. A route that is legitimately
  permissionless moves to the documented allowlist with a stated reason; it does not stay in the
  remediation baseline.

## Known constraints from source

Verified against current code on 2026-08-11. **Read this section before blueprinting any change
above** — several of these are why a change is scoped the way it is.

### `W9-SAFETY-007` — safety controls

- `src/Meridian.Wpf/Services/TradingWorkspaceShellPresentationService.cs` is the only file in
  `src/Meridian.Wpf/` that mentions cancel-all or kill-switch. Its Trading command bar publishes
  `Pause`, `Stop`, and `Flatten` as primary commands and `CancelAll` and `AcknowledgeRisk` as
  secondary ones, and `ResolveActionRequest` maps every one of them to a pane-layout change plus a
  confirmation message. None reaches an execution-control service. These are the dead safety buttons
  the row's third exit criterion forbids, so the sweep is a WPF rewire, not a two-lane audit.
- The browser lane is already the reference implementation:
  `src/Meridian.Ui/dashboard/src/screens/trading-screen.view-model.ts` routes cancel-all through the
  workstation API and publishes a disabled flag, a disabled reason, and an aria label. Mirror that
  contract in WPF rather than defining a second one.
- `AcknowledgeRisk` reports that acknowledgement is "captured locally for this workstation session".
  Under the `W9-TRUTH-001` doctrine that is a truth problem in its own right: make it durable, or
  say plainly that it is not.
- The shared seam already exists in `src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs` —
  cancel-all, circuit-breaker, manual-override create and clear, and the position-close routes.
  `POST /orders/cancel-all` is mapped on the group without a `UiApiRoutes` constant, so a
  route-constant search misses it.

### `W9-SAFETY-007` — rules

- `OrderNotionalRule` is the pattern: threshold accessors as `Func<decimal?>`, escalation banding,
  and an unmeasurable order rejected *as unmeasurable* rather than as a breach so a pricing gap
  cannot trip the circuit breaker. New rules should preserve that distinction.
- The reference-price seam is `IPortfolioExposureProvider.TryGetExecutablePrice(symbol, side)`,
  which prices the touch rather than the midpoint.
- `RiskRuleSeverity` is already decisional and `CompositeRiskValidator` already evaluates every rule
  rather than stopping at the first failure, so a collar can escalate for approval instead of
  hard-blocking. That was not expressible before the risk-engine blueprint's PR 1.
- `Meridian.Risk` has no dedicated test project, but `tests/Meridian.Tests/Risk/` already holds
  per-rule coverage. New rule tests belong there.

### `W9-GOV-008` — authorization

- The remediation ratchet lives in
  `tests/Meridian.Tests/Integration/EndpointTests/EndpointAuthorizationCoverageTests.cs` and is
  two-sided: a newly unguarded route fails, and a route that starts rejecting must be removed from
  the baseline or the test fails. Progress is therefore mechanically measurable.
- Baseline concentration: `/api/fund-structure` 34, `/api/workstation` 16, `/api/maintenance` 12,
  `/api/symbols` 10, then `/api/lean`, `/api/environment-designer`, and `/api/diagnostics` at 7
  each, `/api/storage`, `/api/replay`, and `/api/export` at 6 each, `/api/auth` 5, and a long tail
  of singletons plus two non-`/api` routes.
- `EndpointAuthorization.RequirePermission` and `RequireAnyPermission` are generic over
  `IEndpointConventionBuilder`, so they apply to route groups as well as routes.
  `FundStructureEndpoints` maps its whole cluster under one group whose sub-groups already chain
  `RequireWorkstationTenantScope()`, so most of that tranche is a group-level change.
- `UserPermission` already carries every flag the burn-down needs. No new permission values.
- The criterion asks the test to fail "when a route lacks an explicit policy or permission
  declaration". The current sweep proves *behaviour* (a 401/403 response), not *declaration*.
  `RequirePermission` already stamps `EndpointAuthorizationMetadata`, so a metadata assertion is the
  truer discharge and should sit alongside the behavioural sweep, not replace it.
- The sweep records a test-host DI resolution failure as a violation rather than skipping it, so
  guarding a route can surface a fixture gap that must be closed with it.
- `FundProfileScopeEndpointFilters` establishes that an unauthorized caller receives a uniform 403
  rather than a signal about what exists. Preserve that when placing permission filters ahead of
  ownership checks.
- Guarding a route breaks any existing test that exercises it with an insufficient permission set,
  because `EndpointTestFixture` pre-authorizes through an explicit test-permissions header. Budget
  each tranche for the endpoint-test fan-out.

### `W9-GOV-008` — tenancy

- The gates already exist in `src/Meridian.Ui.Shared/Endpoints/WorkstationTenantContext.cs`:
  `RequireWorkstationTenantScope`, the unconditional `RequireWorkstationTenantCompanyScope`, and
  `RequireFundScopedWriteTenant`. The last is detection-first and **off by default** — a tenantless
  caller's write proceeds and is logged.
- `docs/security/security-remediation-backlog.md` (SEC-005) names the remaining step and its
  prerequisite: every authenticated session must carry a tenant before enforcement is enabled.
  `UserProfile.CompanyId` is optional and populated from the account config, and the legacy
  environment-variable admin path supplies only a username and password hash, so that profile
  resolves to a null tenant today. Give it a deployment-default company in the same change that
  flips the default, or local and demo startup break.
- Slice 4c's remaining defense-in-depth items (fund-account sub-tables, fund-structure store) are
  not a currently reachable cross-tenant residual and stay out of this wave.

### `W9-GOV-008` — audit chain

- `src/Meridian.Storage/Services/AuditChainService.cs` hash-chains *files* — path, file hash, and
  predecessor hash — with in-process and cross-process serialization and copy-on-write appends, and
  exposes chain verification. It does not cover accounting or ledger events.
- `src/Meridian.Storage/Reporting/PostgresReportingArtifactAuditStore.cs` verifies a chain head
  inside the write transaction. That is the database-side precedent to follow rather than inventing
  a second scheme.
- Accounting audit rows live in the accounting-configuration audit family established by
  `V_ledger_017` and `V_ledger_018`.

### `W9-INGEST-009`

- The 2026-07 adversarial review recorded that the live statement path ran a per-row self-check that
  never consulted the internal book. **That premise is stale.** `StatementRunWorkflowService` now
  takes an internal-population provider that defaults fail-closed to an empty book — every row
  becomes a break — and both production compositions replace it with the retained ledger-side
  provider. The row is criterion-level evidence work, not a rewiring.
- Both connectors and the sided kernel family already exist under
  `src/Meridian.FinancialOperations/Reconciliation/`, with unit coverage under
  `tests/Meridian.Tests/Reconciliation/`.
- Golden-file precedent exists under `tests/fixtures/` and `tests/Meridian.Tests/TestData/Golden/`;
  `W9-NAV-006` shipped the most recent worked-example pack. Follow it: retain the raw input beside
  the expected canonical records so a normalization change fails loudly.

## Delivery constraints

- **Migration ordinals** are a global shared resource. Ordinals 029–038 are reserved by in-flight
  blueprints (incentive fee, commitments, equalization, mark freshness). Any migration in this wave
  takes 039 or higher and records the reservation in
  [`docs/engineering/blueprints/README.md`](../engineering/blueprints/README.md).
- **Lane collisions.** Change 3 touches WPF Trading surfaces owned by `W8-WPF-PARITY-001`, and
  change 4 touches reporting groups that `W8-UX-CONSOL-001` is consolidating. Refresh
  [`docs/development/wpf-web-ui-alignment-plan.md`](../development/wpf-web-ui-alignment-plan.md)
  from whichever change lands first rather than editing it from both lanes.
- **Generated artifacts** must be regenerated in the same commit or the drift gate fails.
- **Scope gate.** Every change here authors roadmap data, so a phase marker is mandatory, and once
  declared it is validated against *all* changed files rather than only the roadmap ones.
- WPF builds as a stub on Linux; desktop proof comes from the Windows lanes.

## Validation

```bash
python3 build/scripts/docs/validate-roadmap-registry.py --summary
python3 build/scripts/docs/render-roadmap-docs.py --summary
python3 build/scripts/docs/render-source-docs.py --summary
bash scripts/ci.sh
```

A row moves to `ready_for_acceptance` only when its registry entry links source and test paths for
every exit criterion, generated roadmap docs are re-rendered on the same commit, and the
GitHub-hosted `Meridian CI / quality-gate` check is green.
