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

`DEC-DEPTH-SLATE-001` sequences the W10 depth slate behind W9, so a partly-open slate blocks the
accepted next slate — with two deliberate exceptions the same decision records: `W10-MARK-001` and
`W10-SEAM-001` are pulled forward because they serve the release gate rather than compete with it,
and nothing here defers them. Each open W9 row also leaves a live defect rather than a missing
feature:

| Row | Status entering the wave | What is actually open |
| --- | --- | --- |
| `W9-SAFETY-007` | `in_progress` | Fat-finger and price-collar rules absent from the mandatory validator; the two-lane safety-control sweep unaudited |
| `W9-GOV-008` | `planned` | 112 mutating routes still process permissionless requests; tenancy enforcement is off by default and reads stay fail-open; accounting/ledger audit events are not hash-chained |
| `W9-INGEST-009` | `planned` | Institutional formats lack golden-file coverage, enforce no parse bounds, and cannot match transactions on the live path |

Closing all three also supplies evidence toward `PRD-006`, `PRD-007`/`PRD-009`, and
`PRD-010`/`PRD-101` in the [production-readiness tracker](implementation-todo-list.md). It does not
substitute for that tracker's P0 release gate.

## Sequence

Eleven changes in three phases, one roadmap row per change, with the row's registry status and
evidence advanced in the same change.

A criterion is **discharged** only by the change that completes it. Where two changes contribute to
one criterion, the earlier one advances evidence but must not record the criterion as met — the row
stays `in_progress` until the completing change lands.

| # | Phase | Change | Row | Criterion |
| --: | --- | --- | --- | --- |
| 1 | A | Fat-finger quantity and price-deviation rules | `W9-SAFETY-007` | Pre-trade rule catalogue — *contributes* |
| 2 | A | Price-collar rule | `W9-SAFETY-007` | Pre-trade rule catalogue — **discharges** |
| 3 | A | Two-lane safety-control sweep | `W9-SAFETY-007` | No dead safety buttons |
| 4 | B | Declarative authorization assertion + `/api/fund-structure` tranche | `W9-GOV-008` | Route authorization coverage |
| 5 | B | `/api/workstation` + `/api/auth` tranche | `W9-GOV-008` | Route authorization coverage |
| 6 | B | Operations-surface tranche | `W9-GOV-008` | Route authorization coverage |
| 7 | B | Data and diagnostics tranche | `W9-GOV-008` | Route authorization coverage |
| 8 | B | Remainder to zero | `W9-GOV-008` | Route authorization coverage |
| 9 | B | Fail-closed tenancy + hash-chained accounting **and ledger** audit | `W9-GOV-008` | Tenancy rejection on reads and writes; tamper-evident audit |
| 10 | C | Golden-file packs and bounded-ingress **implementation** | `W9-INGEST-009` | Connector evidence; PRD-010 limits |
| 11 | C | Ledger-transaction population, live split matcher, and casework feed | `W9-INGEST-009` | Match determinism; casework feed |

### Ordering rationale

**Safety first (1–3).** `W9-SAFETY-007` is the only `in_progress` row and the smallest. Rules
precede the control sweep because the sweep's acceptance argument is that every surfaced control
reaches a real service, and the rule catalogue is part of what those controls govern.

**Instrument before burn-down (4).** The mechanical sweep is the measurement, so the declarative
assertion lands with the first tranche rather than after the count reaches zero. Sharpening the
instrument once the baseline is already empty would prove nothing about the routes fixed before it.

**Tranches by owner lane, largest first (4–8).** Each tranche maps to one owner lane so review stays
tractable, and `/api/fund-structure` leads because it is 34 of the remaining 112 and is mapped as a
single route group. `/api/auth` is pulled early despite its small count: account disable, password reset,
and access revocation are the most sensitive unguarded mutations in the set.

**Tenancy and audit last within the row (9).** Both are behaviour-changing rather than additive, and
both are easier to reason about once every route in the sweep declares a permission.

**Ingestion last (10–11).** Originally sequenced last on the belief that it was evidence-only; that
belief was wrong (see the correction under its source constraints). It stays last because it is the
row least likely to collide with the other two, but change 11 carries real wiring — sourcing the
ledger-transaction population and moving the live path onto the split-capable matcher — and its
population-projection decision needs a domain owner before it starts.

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
- It does not open W10. `W10-MARK-001` keeps its blueprint and its place in the ordinal
  reservation table; this wave re-derives its own ordinal from disk rather than pre-assigning one
  above reservations that have not shipped (see Delivery constraints).
- It does not reopen deferred lanes, add a root workspace, or extend the risk-engine blueprint
  beyond its shipped PR 1. The decision journal, `/api/risk/decisions` read surface, and their WPF
  parity remain design-only. Because live truth is the registry and not this page, change 1 writes
  that deferral into the `W9-SAFETY-007` row itself — otherwise the follow-on work is either
  silently lost or wrongly treated as a W9 acceptance requirement, since the deferral would exist
  only here and in a draft blueprint.
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
  the row's third exit criterion forbids, and they are the largest known gap.
- **The audit is still two-lane.** The exit criterion covers *every* WPF and browser safety control,
  and the browser lane's cancel-all being wired says nothing about the rest of its safety surface.
  The evidence this change owes is a full inventory of both lanes — each control listed with the
  service it invokes or the explicit not-wired state it carries — even where the browser inventory
  produces no code change. Treating the browser lane as "already done" because one control checks
  out is exactly how a dead button survives a sweep.
- The browser lane is the reference *contract*, not a reason to skip it:
  `src/Meridian.Ui/dashboard/src/screens/trading-screen.view-model.ts` routes cancel-all through the
  workstation API and publishes a disabled flag, a disabled reason, and an aria label. Mirror that
  shape in WPF rather than defining a second one.
- `AcknowledgeRisk` reports that acknowledgement is "captured locally for this workstation session",
  which is a `W9-TRUTH-001` problem in its own right — but **truthful copy is not one of the two
  permitted outcomes.** The criterion allows only "invokes the real shared service" or "disabled
  with an explicit not-wired state"; an enabled control that merely admits it does nothing is the
  third option the criterion exists to forbid, and rewording it would leave an operator able to
  click a safety action that changes nothing. Either wire it to durable acknowledgement or disable
  it with the not-wired state. The same test applies to every control in the sweep.
- The shared seam already exists in `src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs` —
  cancel-all, circuit-breaker, manual-override create and clear, and the position-close routes.
  `POST /orders/cancel-all` is mapped on the group without a `UiApiRoutes` constant, so a
  route-constant search misses it.

### `W9-SAFETY-007` — rules

- `OrderNotionalRule` is the pattern: threshold accessors as `Func<decimal?>`, escalation banding,
  and an unmeasurable order rejected *as unmeasurable* rather than as a breach so a pricing gap
  cannot trip the circuit breaker. New rules should preserve that distinction.
- **Sizing and price controls use different reference seams — do not reuse the wrong one.**
  `TryGetExecutablePrice` is deliberately conservative: `AggregatePortfolioExposureProvider` returns
  the larger of mark and touch so a sell never under-measures the short it creates, which on a normal
  book means it returns the **midpoint** for a sell, not the bid. That is right for notional,
  exposure, and concentration. It is wrong for any control comparing an operator's price against the
  market: a sell measured against the mid looks priced through by half the spread, so an ordinary
  marketable sell at the bid is rejected on a wide book. Change 1 added
  `IPortfolioExposureProvider.TryGetTouchPrice(symbol, side)` for the raw crossing side; the price
  collar in change 2 uses that, not `TryGetExecutablePrice`.
- The price limb applies only to a plain `Limit` order on a single symbol. Stop and stop-limit
  limits are priced off the trigger, auction limits (`LimitOnOpen`/`LimitOnClose`) price against a
  future cross rather than the continuous touch, market orders may carry a simulated observation in
  `LimitPrice` through the paper gateway, and a multi-leg limit is a package net not comparable to
  the top-level symbol's quote. Change 1 establishes these exclusions; the collar inherits them.
- **The stop exclusion has a hole that changes 1–2 must close.** "A stop sits away from the market
  by design" is true only for a correctly-sided stop. `PaperOrderMatchingPolicy.IsStopTriggered`
  fires a buy when the market is at or above the stop and a sell when it is at or below it, after
  which a stop-market routes as a **market order**. So with a $100 market, a fat-fingered buy stop
  at $1 or sell stop at $1,000 is already crossed, triggers instantly, and becomes an unbounded
  market order — with no price control on it at all, because the limb skips stop prices entirely.
  An already-crossed or wildly deviated trigger is itself the fat-finger signal. Compare `StopPrice`
  against the touch and reject or escalate it, and for stop-limit validate trigger and limit
  independently rather than excluding the type wholesale.
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
- **Baseline concentration, recounted from current `origin/main` (112 entries).** The 152 figure in
  the row's own 2026-08-10 summary is a historical snapshot: 40 routes have been guarded since, and
  `/api/environment-designer`, `/api/diagnostics`, `/api/storage`, and `/api/export` now have **zero**
  entries. Recount before scheduling a tranche — planning against 152 would allocate work to routes
  that are already done.

  | Prefix | Entries | | Prefix | Entries |
  | --- | ---: | --- | --- | ---: |
  | `/api/fund-structure` | 34 | | `/api/quality` | 3 |
  | `/api/workstation` | 16 | | `/api/subscriptions`, `/api/security-master`, `/api/schedules`, `/api/backfill`, `/api/alignment` | 2 each |
  | `/api/maintenance` | 12 | | 11 single-entry prefixes (`sampling`, `reference-data`, `quant`, `providers`, `plaid`, `options`, `ledger`, `lean`, `health`, `execution`, `compliance`) | 11 |
  | `/api/symbols` | 10 | | `/portal/...`, `/hooks/...` | 1 each |
  | `/api/replay` | 6 | | | |
  | `/api/packaging`, `/api/auth` | 4 each | | | |
- `EndpointAuthorization.RequirePermission` and `RequireAnyPermission` are generic over
  `IEndpointConventionBuilder`, so they apply to route groups as well as routes.
- **Declare on permission-homogeneous groups only, never on the `/api/fund-structure` root.** That
  root mixes structure mutations with reporting routes governed by `ViewReporting`,
  `ManageReporting`, `ApproveReporting`, and `DeliverReporting`, and with read routes. A root
  `ManageFundStructure` declaration would deny legitimate reporting callers; a broad any-of
  declaration would let an unrelated reporting permission satisfy an unguarded structure mutation
  *and* still satisfy the metadata sweep, which is worse than leaving the route in the baseline —
  it would look remediated. Push declarations down to the sub-groups that share one permission
  (`FundStructureEndpoints` already maps `reportingGroup` and `legacyReportingGroup` separately), or
  onto individual routes where a family is genuinely mixed. The group mechanism saves repetition,
  not analysis: every one of the 34 routes still needs its permission chosen deliberately.
- `UserPermission` already carries every flag the burn-down needs. No new permission values.
- The criterion asks the test to fail "when a route lacks an explicit policy or permission
  declaration". The current sweep proves *behaviour* (a 401/403 response), not *declaration*.
  `RequirePermission` already stamps `EndpointAuthorizationMetadata`, so a metadata assertion is the
  truer discharge and should sit alongside the behavioural sweep, not replace it.
- **Read routes need their own decision, taken up front.** The existing sweep is mutation-only —
  `MutatingMethods` is POST/PUT/PATCH/DELETE, and reads are excluded by design because the
  workstation grants broad read access by role. But the criterion says *every mapped endpoint*
  declares a policy or permission, and many GET routes carry no `EndpointAuthorizationMetadata`
  today. So a metadata assertion over every endpoint fails immediately on existing reads, while a
  mutation-only assertion cannot discharge the criterion as written. Neither outcome is acceptable
  by accident. Decide explicitly in change 4 — inventory and remediate the read surface, or
  allowlist it with a stated reason per family and record why role-level read access satisfies the
  criterion — and give whichever path its own baseline. The mutation tranches below do not budget
  for it.
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
- **The write gate is not the whole criterion.** Flipping `RequireFundScopedWriteTenant` covers only
  the decorated write and evaluate routes. The exit criterion requires cross-tenant *reads* to fail
  closed too, and today the read side is deliberately fail-open in two places: the
  `RequireFundProfileTenantScope()` filter passes a blank fund, a caller with no tenant scope, or an
  unavailable guard, and the storage `TenantReadPredicate` returns rows whose `tenant_id` is null so
  unstamped legacy rows stay visible. Both are correct as written for a single-company deployment
  and both are load-bearing for it, so this change owes an explicit decision per path — tighten, or
  record why the deployment boundary still carries it — plus the tests that prove whichever way it
  lands. Reading the write-gate switch as "the remaining step" would close the row with the read
  side still open.
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
  `V_ledger_017` and `V_ledger_018`. **That family is the accounting half only.** The exit criterion
  names accounting *and ledger* audit events, so the journal-append and other ledger-event paths are
  in scope for the same chain — anchoring the change on the accounting-action audit table alone
  would let the criterion be declared discharged while ledger events stay outside the chain. Name
  the ledger event authority, its persistence, and its tamper-detection tests as part of this
  change, not as a follow-on.

### `W9-INGEST-009`

> **Correction (2026-08-11).** An earlier revision of this document claimed this row was
> "criterion-level evidence work, not a rewiring," on the grounds that the 2026-07 adversarial
> review's premise was stale. **That conclusion was wrong** and is retracted here so it is not
> re-inherited. What was checked was the dependency-injection registration; what was not checked was
> what the registered provider actually returns, or which engine the live matcher calls. Both are
> recorded below. This row carries real wiring work.

- The 2026-07 review's *wording* is outdated — `StatementRunWorkflowService` does take an
  internal-population provider, it defaults fail-closed to an empty book, and both production
  compositions replace it with `RetainedInternalReconciliationPopulationProvider`. Positions and
  cash genuinely do come from the retained book.
- **But the ledger-transaction population is empty by design.** That provider returns
  `new InternalReconciliationPopulations(positions, cash, [])`, and its own remarks explain why:
  the reconciliation context carries no ledger-book/period scope key, the journal is double-entry so
  projecting one custodian-visible movement is a modeling choice, only custodian-reconcilable
  postings should project at all, and fund-scoped journal reads are tenant-authorized. It "awaits an
  authorized period-scoped ledger source and an agreed journal→transaction projection."
- **And the live matcher is the one-to-one engine.** `StatementRunMatcher` invokes
  `new StatementMatchingEngine()`, not the split-capable `ReconciliationMatchingEngine`.
- Together these decide the row's shape. camt.053 and BAI2 are *transaction* statements, so with an
  empty transaction population every bank row fails closed to a break, and one-to-many outcomes can
  never reach live casework no matter what the standalone kernels do. Tests written against those
  kernels would pass while the shipping path matched nothing. **Sourcing the ledger-transaction
  population and giving the live path deterministic split matching are in scope for this row**, not
  follow-on work — and the population decision is a domain modeling call that needs an owner before
  change 11 starts.
- **Do not read "split-capable engine" as swapping `StatementMatchingEngine` for
  `ReconciliationMatchingEngine`.** That would lose transaction matching outright:
  `StatementRunMatcher` builds statement, cash, *and* internal ledger-transaction populations and
  converts `StatementMatchResult` values into casework, while `ReconciliationMatchingEngine.Run`
  handles positions and cash only and has no transaction model at all. The deterministic
  one-to-many primitives live in `ReconciliationMatchKernel`. Change 11 therefore extends
  `StatementMatchingEngine` with those primitives, or introduces an adapter that preserves all
  three populations — it does not replace the live engine.
- Both connectors and the sided kernel family already exist under
  `src/Meridian.FinancialOperations/Reconciliation/`, with unit coverage under
  `tests/Meridian.Tests/Reconciliation/`.
- Golden-file precedent exists under `tests/fixtures/` and `tests/Meridian.Tests/TestData/Golden/`;
  `W9-NAV-006` shipped the most recent worked-example pack. Follow it: retain the raw input beside
  the expected canonical records so a normalization change fails loudly.

## Delivery constraints

- **Migration ordinals** are a global shared resource, and this wave must follow the register's own
  rule rather than pre-assigning a number. 029–038 are *reserved* by in-flight blueprints (incentive
  fee, commitments, equalization, mark freshness) but **none of them has shipped** — the highest
  ordinal on disk is still 028. Hard-coding 039 for change 9 would therefore claim a number above
  ten absent migrations, and those would later apply after a higher ordinal had already been
  recorded, which the mark blueprint treats as invalid ordering.

  The register's contract is explicit: *"Re-derive the next free ordinal from disk at implementation
  time and update this table if an unrelated lane lands first. Do not renumber a migration that has
  already shipped."* So change 9 re-derives from disk when it is written. If it lands before those
  blueprints, it takes the next free ordinal (029 today) and updates the reservation table in
  [`docs/engineering/blueprints/README.md`](../engineering/blueprints/README.md) to shift the
  unshipped reservations up — which is permitted precisely because they have not shipped.
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

**`quality-gate` alone cannot accept change 3.** Its lanes run on Ubuntu, where the WPF project
builds as a stub, and the Windows desktop workflows are tag- or manual-dispatch only. A green gate
plus linked test paths would therefore let `W9-SAFETY-007` advance without the rewired desktop code
ever having been compiled, let alone executed — on the one criterion whose whole subject is WPF.
Change 3 additionally requires a **named Windows WPF build-and-test result** linked as evidence on
the row. **The dispatch inputs are part of the requirement, because the defaults prove nothing:**
`targeted-test.yml` defaults `runner` to `ubuntu-latest` and `enable_full_wpf_build` to `false`, and
passes that value through explicitly — which defeats `Meridian.Wpf.Tests.csproj`'s own
Windows-detection default, leaving `EnableDefaultCompileItems` false so **no test sources compile at
all**. A default run against `tests/Meridian.Wpf.Tests` therefore goes green having executed
nothing. Accept only:

```bash
gh workflow run targeted-test.yml --ref <branch> \
  -f mode=dotnet-filtered -f runner=windows-latest -f enable_full_wpf_build=true \
  -f dotnet_project=tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj \
  -f dotnet_filter="FullyQualifiedName~TradingWorkspaceShell"
```

or the Windows-only `mode=wpf-dev-loop`, or the desktop workflow dispatched on the candidate commit.
Without a run whose inputs actually compiled the WPF test sources, the criterion is not discharged.
