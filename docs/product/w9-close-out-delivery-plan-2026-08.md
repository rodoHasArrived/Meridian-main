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
| `W9-SAFETY-007` | `in_progress` | Fat-finger and price-collar rules absent from the mandatory validator; the two-lane safety-control sweep unaudited; the kill-switch sweep discards per-order cancellation outcomes |
| `W9-GOV-008` | `planned` | 112 mutating routes still process permissionless requests; tenancy enforcement is off by default and reads stay fail-open; accounting/ledger audit events are not hash-chained |
| `W9-INGEST-009` | `planned` | Institutional formats lack golden-file coverage, enforce no parse bounds, and cannot match transactions on the live path |

Closing all three also supplies evidence toward `PRD-006`, `PRD-007`/`PRD-009`, and
`PRD-010`/`PRD-101` in the [production-readiness tracker](implementation-todo-list.md). It does not
substitute for that tracker's P0 release gate.

**One tracker row needs correcting before this wave, not after it.** `PRD-010` is currently marked
implementation-complete on the claim that bounded schema-aware statement parsing is enforced. The
source audit below establishes the opposite for two shipping connectors: camt.053 decodes the whole
payload into an `XDocument` and BAI2 decodes and splits the whole payload, neither enforcing a
record limit, and `StatementImportService` accepts an arbitrary source document so the upload byte
cap does not cover the seam. Either downgrade `PRD-010` or explicitly scope camt.053 and BAI2 out of
the supported envelope until change 10 lands — otherwise the P0 gate can read green on a control
this plan's own evidence contradicts.

## Sequence

Eleven changes in three phases, one roadmap row per change, with the row's registry status and
evidence advanced in the same change.

A criterion is **discharged** only by the change that completes it. Where two changes contribute to
one criterion, the earlier one advances evidence but must not record the criterion as met — the row
stays `in_progress` until the completing change lands.

| # | Phase | Change | Row | Criterion |
| --: | --- | --- | --- | --- |
| 1 | A | Fat-finger quantity and price-deviation rules | `W9-SAFETY-007` | Pre-trade rule catalogue — *contributes* |
| 2 | A | Price-collar rule **and the OMS amendment gate** | `W9-SAFETY-007` | Pre-trade rule catalogue — **discharges** |
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

- **Criterion one is not closed, contrary to what the row implied.** The 2026-08-10 change closed
  the *coupling* gap — opening the breaker now issues the sweep — but the criterion says activation
  *cancels all open orders*, and the sweep cannot establish that. `OrderManagementSystem.CancelAllAsync`
  awaits `CancelOrderCoreAsync` per order inside a `Parallel.ForEachAsync` and **discards the
  returned `OrderResult`**, so a broker that rejects an individual cancellation leaves that order
  working while the sweep completes normally; `ExecutionEndpoints` then audits
  `CircuitBreakerCancelAll` as `Completed`, its `Failed` branch firing only on a thrown exception.
  The existing endpoint test asserts that `CancelAllAsync` was invoked, never that the open book
  emptied. Closing this means aggregating the per-order outcomes, surfacing a failed or partial
  kill-switch state that names the orders still working, and asserting the book rather than the
  call. It is small, it belongs with change 3 (the other criterion-one/three safety work), and
  without it the row can reach acceptance while the kill switch silently half-fires.
  **`W9-SAFETY-007` still reads as though criterion one were closed** — it says the coupling gap is
  closed, presents completed and failed sweep outcomes as proven, and lists only the rule catalogue
  and the UI sweep as remaining. Change 1 writes this correction into the row, because a
  registry-first implementer following live truth would otherwise advance it without ever fixing or
  testing the incomplete cancellation.
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
- **But a quantity ceiling cannot follow that pattern blindly, because `Quantity` is not always a
  unit count.** Alpaca implements `INotionalOrderSizingGateway`, and the OMS replaces the routed
  quantity with a metadata dollar amount for those orders — so comparing `Quantity` against a
  share-or-contract ceiling either rejects a valid $5,000 order against a 1,000-share limit, or
  inspects a placeholder the broker never routes. There are **two** forms: an explicit dollar amount
  in metadata, and a boolean alias meaning "the quantity field is dollars", which the gateway
  accepts in more spellings than `bool.TryParse` does — recognizing fewer of them than the gateway
  is a silent bypass, since `notional=yes` on a 100,000-quantity order in a $0.01 symbol routes
  $100,000 while a unit rail measures 100,000 shares. The rule must resolve both through the same
  seam the gateway reads and skip the ceiling for dollar-sized orders, whose economic size is gated
  by `OrderNotionalRule` instead, with coverage for each form.
- **Sizing and price controls use different reference seams — do not reuse the wrong one.**
  `TryGetExecutablePrice` is deliberately conservative: `AggregatePortfolioExposureProvider` returns
  the larger of mark and touch so a sell never under-measures the short it creates, which on a normal
  book means it returns the **midpoint** for a sell, not the bid. That is right for notional,
  exposure, and concentration. It is wrong for any control comparing an operator's price against the
  market: a sell measured against the mid looks priced through by half the spread, so an ordinary
  marketable sell at the bid is rejected on a wide book. **`IPortfolioExposureProvider` on `main`
  exposes only `TryGetReferencePrice` and `TryGetExecutablePrice`** — the seam a price control needs
  does not exist there yet, and `W9-SAFETY-007.current_summary` on `main` still advertises
  `TryGetExecutablePrice` as the available reference. Change 1 therefore *adds*
  `TryGetTouchPrice(symbol, side)` for the raw crossing side and updates that row in the same change;
  the collar in change 2 depends on change 1 having landed it. Until then, a registry-first
  implementer reading only the row would build the collar on the midpoint-biased method this
  paragraph rejects.
- Each price is measured against the reference it is meaningful to, and each order type contributes
  only the prices it genuinely puts at risk. A plain `Limit`'s limit is measured against the crossing
  touch; a `StopMarket` or `StopLimit` *trigger* against a new `TryGetTriggerReferencePrice` seam
  that resolves **last trade, then bar close, then crossing side** — the exact precedence
  `PaperOrderMatchingPolicy.IsStopTriggered` uses (`LastTradePrice ?? BarClose`, quote last); and a
  `StopLimit`'s limit against **its own trigger**, which is what it is priced off. Auction
  limits (`LimitOnOpen`/`LimitOnClose`) price against a future cross rather than the continuous
  touch, market orders may carry a simulated observation in `LimitPrice` through the paper gateway,
  trailing stops have a broker-derived trigger that moves with the market, and a multi-leg limit is a
  package net not comparable to the top-level symbol's quote — none of those contribute a price.
- A trigger's wrong side is the **mirror** of a limit's: `PaperOrderMatchingPolicy` fires a buy stop
  once the market reaches or passes above it, so a buy stop typed *beneath* the market is already
  crossed and a stop-market order that triggers on acceptance routes unbounded. Change 1 measures
  that mirrored direction under the same band.
- **Changes 1 and 2 gate submission only, and that bound belongs in the plan rather than in a
  reviewer's thread.** `OrderManagementSystem.IsRiskIncreasing` revalidates a quantity increase or a
  numerically *higher* limit or stop price, but the dangerous direction is a *decrease* for a sell
  limit and for a buy stop alike: a sell accepted at $100 can be amended to $1, and a buy stop
  accepted at $110 can be amended to $1 and trigger immediately as an unbounded market order — the
  very outcome the wrong-side trigger limb blocks on submission — with neither rule running. Every
  new limit or stop value needs to re-enter the price controls regardless of whether notional
  increases.
  **Change 2 owns that gate.** Deferring it was the wrong call and this revision reverses it: a rule
  every amendment walks around is not "enforced by the mandatory validator" in any sense the
  criterion means, so leaving the gate out would let change 2 mark the criterion discharged while a
  working order can still be amended straight through both the deviation band and the collar. It is
  genuinely OMS-wide — `IsRiskIncreasing` governs revalidation for *every* rule, so widening it
  changes when position-limit, notional, and exposure rules re-run too — which is an argument for
  landing it deliberately with its own tests, not for landing it never. Change 2 is where both price
  rules exist, so it is where the gate that makes them real belongs. Change 1 additionally writes
  the limitation into `W9-SAFETY-007` itself, so between the two changes the registry states the
  bound rather than only these planning records.
- **The trigger's reference precedence is load-bearing, not a detail.** Any accessor that consults a
  quote before a print disagrees with the matcher whenever the two differ, and it does so in *both*
  directions. With a 100/120 quote and a 100 print, a buy stop at 105 is resting yet reads as crossed
  against the ask or the midpoint — a false rejection. With a 130 print, a buy stop at 125 sits above
  the ask and looks correctly placed against every quote-derived reference while the matcher fires it
  immediately — a false *approval*, and an unbounded market order. Neither `TryGetReferencePrice`
  (quote midpoint first) nor `TryGetTouchPrice` (bid/ask) is safe here on its own. Nor is
  trade-then-quote: **the bar-close leg is load-bearing too**, because a bar-driven session has no
  print at all, and skipping to the quote reproduces the false approval exactly — no print, a 130
  close, a 100 ask, and a buy stop at 125 reads as resting 25% below the market while the matcher
  triggers it. All three legs, in the matcher's order, or the two disagree somewhere. The collar
  inherits every one of these distinctions; reusing the limit's reference or orientation for a
  trigger would reopen the hole change 1 closed.
- **The stop exclusion has a hole that changes 1–2 must close.** "A stop sits away from the market
  by design" is true only for a correctly-sided stop. `PaperOrderMatchingPolicy.IsStopTriggered`
  fires a buy when the market is at or above the stop and a sell when it is at or below it, after
  which a stop-market routes as a **market order**. So with a $100 market, a fat-fingered buy stop
  at $1 or sell stop at $1,000 is already crossed, triggers instantly, and becomes an unbounded
  market order — with no price control on it at all, because the limb skips stop prices entirely.
  An already-crossed or wildly deviated trigger is itself the fat-finger signal. Compare `StopPrice`
  against the **trigger reference** described above — `TryGetTriggerReferencePrice`, resolving last
  trade, then bar close, then crossing side — and reject or escalate it, and for stop-limit validate
  trigger and limit independently rather than excluding the type wholesale. Not the touch: with an
  ask of 124 and a 130 print, a buy stop at 125 reads as resting against the touch while the matcher
  fires it immediately.
- `RiskRuleSeverity` is already decisional and `CompositeRiskValidator` already evaluates every rule
  rather than stopping at the first failure, so a collar can escalate for approval instead of
  hard-blocking. That was not expressible before the risk-engine blueprint's PR 1.
- `Meridian.Risk` has no dedicated test project, but `tests/Meridian.Tests/Risk/` already holds
  per-rule coverage. New rule tests belong there.
- **Registering a rule is not enforcing it, and changes 1–2 owe the difference.**
  `RiskRuleRuntimeService` initialises every portfolio-aware threshold to `null`, and the
  `OrderNotionalRule` pattern approves when its thresholds are unconfigured. A deployment with no
  `risk-rules.json` therefore composes all three new rules and enforces none of them, while tests
  that pass explicit thresholds go green — so the catalogue this criterion demands could be
  "complete" and inert in production simultaneously. These changes owe either safe defaults for the
  quantity, deviation, and collar bands, or a fail-closed preflight that refuses a production
  composition whose catalogue is unconfigured. A test on a clean configuration, not one that
  supplies values, is what proves it.

### `W9-GOV-008` — authorization

- The remediation ratchet lives in
  `tests/Meridian.Tests/Integration/EndpointTests/EndpointAuthorizationCoverageTests.cs` and is
  two-sided: a newly unguarded route fails, and a route that starts rejecting must be removed from
  the baseline or the test fails. Progress is therefore mechanically measurable.
- **Baseline concentration, recounted from current `origin/main` (112 entries).** The 152 figure in
  the row's own 2026-08-10 summary is a historical snapshot: 40 routes have been guarded since, and
  `/api/environment-designer/*`, `/api/diagnostics/*`, `/api/storage/*`, and `/api/export/*` now have **zero**
  entries. Recount before scheduling a tranche — planning against 152 would allocate work to routes
  that are already done.
  Prefixes are written in the `/api/name/*` route-family form on purpose: the API-coverage
  dashboard indexes exact path tokens, so a bare `/api/name` here would credit that specific
  root endpoint as documented when this table says nothing at all about its contract.

  | Prefix | Entries | | Prefix | Entries |
  | --- | ---: | --- | --- | ---: |
  | `/api/fund-structure/*` | 34 | | `/api/quality/*` | 3 |
  | `/api/workstation/*` | 16 | | `/api/subscriptions/*`, `/api/security-master/*`, `/api/schedules/*`, `/api/backfill/*`, `/api/alignment/*` | 2 each |
  | `/api/maintenance/*` | 12 | | 11 single-entry prefixes (`sampling`, `reference-data`, `quant`, `providers`, `plaid`, `options`, `ledger`, `lean`, `health`, `execution`, `compliance`) | 11 |
  | `/api/symbols/*` | 10 | | `/portal/...`, `/hooks/...` | 1 each |
  | `/api/replay/*` | 6 | | | |
  | `/api/packaging/*`, `/api/auth/*` | 4 each | | | |
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
  declare it open — and give whichever path its own baseline. The mutation tranches below do not
  budget for it.
  **Note what the second option cannot be.** The criterion is that the test fails whenever any
  mapped endpoint lacks an explicit policy or permission declaration, so "allowlisted" has to mean
  *declared on the endpoint* — an explicit role, permission, or anonymous marker in
  `EndpointAuthorizationMetadata` — not recorded in a side list that the assertion then skips.
  Writing "these GET families are fine because access is role-level" into a separate baseline
  leaves the endpoints exactly as undeclared as they are today and lets the row reach
  `ready_for_acceptance` without satisfying its own criterion. An endpoint deliberately open to
  every signed-in reader is a legitimate answer; it just has to say so on the endpoint.
- **Guarding a route can also lock out callers that are authorized today, and two of them are
  systemic.** `EndpointAuthorization` resolves a caller's rights from
  `LoginSessionMiddleware.CurrentUserPermissionsKey` or its role key, and nothing else reaches it.
  So: (a) `ApiKeyMiddleware` validates an `MDC_API_KEY` request and records only
  `Items["ApiKey"]`, which means every script and service-to-service client documented in
  `docs/reference/api-reference.md` starts receiving 401 the moment its route is guarded — the
  tranches need a permission- and tenant-bearing machine principal, or an explicit migration or
  retirement of those clients with the disposition under test. And (b) `LoginSessionMiddleware`
  returns early for every `/api/auth` path, so a signed-in browser administrator has no permissions
  in context there at all; those handlers work today only because `AuthEndpoints.ResolveCurrentProfile`
  reads the session cookie itself. Change 5 must narrow that exemption to the bootstrap endpoints
  while hydrating management requests — or back the declaration with a filter that can resolve the
  session — and cover the browser-admin path in its tranche tests. Both are pre-existing conditions
  the burn-down *surfaces*; neither is caused by it, and neither can be discovered after the fact.
- **The unresolved *writes* need a metadata baseline too, and the behavioural one is not it.** The
  112-entry ratchet records observed status codes and fixture failures, not declarations, so a route
  can sit in that baseline while its mapping carries no `EndpointAuthorizationMetadata` at all —
  `POST /api/maintenance/execute` is one. An unconditional metadata assertion landing in change 4
  therefore fails on every write still queued for changes 5–8, not only on reads. Enumerate the
  metadata debt independently in change 4, ratchet it down with each tranche the same way the
  behavioural baseline ratchets, and the assertion can merge with change 4 instead of being weakened
  or deferred to the end of the row.
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
- **Flipping the write gate also assumes the routes are decorated, and the leading tranche's are
  not.** `FundScopedWriteTenantOptions` only affects routes already carrying
  `RequireFundScopedWriteTenant`, and `FundStructureEndpoints` has none — the
  `app.MapGroup("/api/fund-structure")` root and the writes beneath it, `POST
  /api/fund-structure/organizations` among them, never resolve tenant scope at all. So after change
  4 adds permission metadata, an authorized but tenantless session still reaches those handlers even
  once change 9 flips enforcement: the permission gate passes it and no tenant gate exists to stop
  it. Change 9 must enumerate the undecorated write routes and gate them, or allowlist the genuine
  bootstrap operations with a stated reason. This is distinct from the fail-open *read* predicates
  below — those pass rows, these pass requests.
- **The write gate is not the whole criterion.** Flipping `RequireFundScopedWriteTenant` covers only
  the decorated write and evaluate routes. The exit criterion requires cross-tenant *reads* to fail
  closed too, and today the read side is deliberately fail-open in two places: the
  `RequireFundProfileTenantScope()` filter passes a blank fund, a caller with no tenant scope, or an
  unavailable guard, and the storage `TenantReadPredicate` returns rows whose `tenant_id` is null so
  unstamped legacy rows stay visible. Both are correct as written for a single-company deployment
  and both are load-bearing for it. **Both must nonetheless be tightened, with regression tests,
  before the row advances.** The criterion is categorical - cross-tenant reads fail closed and an
  unresolvable scope is rejected rather than defaulted - so documenting the deployment boundary
  explains the current behaviour but cannot discharge it. Reading the write-gate switch as "the
  remaining step" would close the row with the read side still open.
- **But tightening `TenantReadPredicate` needs a data migration before it needs a test.** That
  predicate emits `tenant_id is null or ...` on purpose: `V_ledger_020`, `V_ledger_021`, and
  fund-account migration `003` backfill from the `fund_profile_tenancy` registry, and their own
  headers state that rows they cannot attribute — accounting periods with no `ledger_book_id`, and
  any row the registry never covered — stay null and fail-open until a later slice attributes them.
  Rejecting null tenants without first attributing those rows does not close a leak; it hides
  fund-account, journal/period, and operations-continuity records from *every* scoped reader on an
  existing deployment. Change 9 must therefore carry a deterministic backfill for the remaining
  unstamped rows, plus a quarantine or upgrade-validation path for whatever it still cannot
  attribute, and only then flip the predicate. Regression tests prove the tightened predicate; they
  do not prove the data was ready for it.
- **The same applies to accounts, and not only the legacy ones.** `RequireFundScopedWriteTenant`
  needs a resolvable company on the session, but `InitialAccountBootstrapService` creates the ordinary
  first-run administrator with a null company, and `UserAccountConfig.CompanyId` is optional for both
  stored accounts and `MDC_USERS`. Flipping enforcement therefore returns 403 to the *normal*
  administrator of an existing local installation, not just to the legacy `MDC_USERNAME` fallback.
  Change 9 needs a migration or an explicit company requirement covering every account source.
- Slice 4c's remaining defense-in-depth items (fund-account sub-tables, fund-structure store) are
  not a currently reachable cross-tenant residual and stay out of this wave.

### `W9-GOV-008` — audit chain

- **The Postgres store is not the only accounting audit store, and the other one is the default.**
  `IAccountingActionAuditStore` resolves to `PostgresAccountingConfigurationStore` only where the
  database composition is registered; the workstation and WPF compositions fall through to
  `FileAccountingConfigurationStore`, whose `AppendAsync` adds the event to a list and persists the
  snapshot with no predecessor hash and no chain verification. Anchoring the accounting half of this
  change to the `V_ledger_017`/`018` family alone would let the criterion be declared discharged
  while the *active* desktop and local-workstation accounting audit stays freely deletable and
  reorderable without detection. Change 9 must chain and verify the file-backed store too — or fail
  closed and disable those mutations in that posture — with local/WPF proof either way. This is not
  a secondary path: it is what runs when nobody has stood up PostgreSQL.
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
  `new InternalReconciliationPopulations(positions, cash, [])`, and its own remarks explain why: no
  ledger-book/period scope key reaches it, the journal is double-entry so projecting one
  custodian-visible movement is a modeling choice, only custodian-reconcilable postings should
  project at all, and fund-scoped journal reads are tenant-authorized. It "awaits an authorized
  period-scoped ledger source and an agreed journal→transaction projection."
- **The missing scope is a dropped hand-off, not an absent value — do not invent a new lookup.**
  `StatementAccountingScope` (`FundProfileId`, `LedgerBookId`, `AccountingPeriodId`, `AsOfDate`) is
  already carried on the run request and persisted on the run record, resolved against the
  authenticated tenant and company by the intake authority. It is lost one layer down:
  `InternalReconciliationPopulationContext` declares only fund account, external account, period
  start/end, and base currency, and `StatementRunWorkflowService` builds it purely from the import.
  So change 11's first move is to **propagate that authority-verified scope into the population
  context and require it for ledger reads** — inventing a parallel, potentially unscoped ledger
  lookup would bypass the tenant authorization the retained value already carries. The
  journal→transaction projection semantics are a separate decision on top of that.
- **But "already carried" holds only for the governed intake flow — one live caller cannot supply
  a scope at all.** `POST /api/workstation/reconciliation/statement-runs` creates runs directly, and
  its request body carries no accounting scope to propagate. `StatementRunCreateDto`
  (`src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs`) is the workstation's
  statement-run creation contract, and its full field set is the broker, source institution, fund
  account id, external account id, statement period start and end, source path, original file name,
  mapping profile id, tolerance profile id, and importer, plus an optional source file hash and
  notes — no fund profile, ledger book, accounting period, or as-of date anywhere in it.
  `ReconciliationApiService.ToWorkflowRequest` correspondingly builds the `StatementRunRequest`
  without setting its optional `AccountingScope`. The moment ledger reads require that scope, this
  path either breaks or silently keeps producing runs whose transaction population stays empty —
  which is the current bug wearing a new name. Change 11 must therefore decide this caller's
  disposition explicitly: extend the contract and resolve the added scope through the intake
  authority as the governed flows do, migrate the caller behind that authority, or disable it.
  Leaving it as-is is not one of the options, because the row's criterion is about the live path.
- **And the live matcher is the one-to-one engine.** `StatementRunMatcher` invokes
  `new StatementMatchingEngine()`, not the split-capable `ReconciliationMatchingEngine`.
- **The byte cap has to move ahead of the copy, not just into the parsers.**
  `StatementImportService.CommitAsync` runs `request.Document.Content.ToArray()` before connector
  resolution and before `ParseAsync`, so a limit implemented only inside the camt.053 and BAI2
  parsers still duplicates an arbitrarily large payload into memory before anything rejects it. The
  upload and CLI callers cap bytes on the way in; this service seam does not, and it accepts a
  caller-supplied `StatementSourceDocument` directly. Change 10 therefore checks
  `StatementConnectorLimits.MaxFileBytes` in the shared preview/commit service *before* copying or
  parsing, in addition to the streaming byte and record limits inside the connectors.
- **Splitting the matcher is not enough — the result path cannot record a split either.**
  `StatementMatchResult` carries exactly one `BrokerEvidenceReference` and one
  `InternalEvidenceReference`, so a one-to-many outcome has nowhere to say which records formed it;
  `StatementRunMatcher` then discards every exact and tolerance result after incrementing
  `MatchCount`; and `StatementRunMatchArtifact` persists only that count alongside breaks and cases.
  Two *different* split assignments with the same number of matches therefore produce a
  byte-identical durable artifact. The row's criterion is deterministic one-to-one/one-to-many
  semantics with stable tie-breakers and idempotent re-runs — none of which can be evidenced from an
  artifact that cannot distinguish one grouping from another, however correct the matching is.
  Change 11 must carry group-aware match records with evidence membership through the result type,
  the mapper, and the persisted artifact, or its own acceptance evidence is unobtainable.
- **Widening that artifact is a durability change, not just a shape change.**
  `LoadVerifiedMatchArtifactAsync` deserializes a retained artifact, reserializes it with the
  *current* type, and compares that hash against the stored recovery checkpoint. Adding a field —
  even one defaulting to null — changes the serialized bytes, so every pre-upgrade matched run would
  hash differently and be reported as corrupted on the first retry or re-import after deploying
  change 11. The fix is a schema-versioned reader or a deterministic migration that preserves or
  upgrades the legacy hash, with an upgrade/recovery test that starts from an artifact written by
  the *current* schema rather than one written by the new code.
- **The existing one-to-one path is not deterministic either, so split search alone cannot satisfy
  the criterion.** `MatchStage` takes the first admissible internal item it encounters and
  `MatchBestCandidate` keeps the first candidate at an equal score, so permuting an otherwise
  identical population changes which records get paired. Change 11 has to route pair candidates
  through a total deterministic ordering — `ReconciliationMatchKernel.SelectDeterministicAssignment`
  is the existing one — and prove identical artifacts across input permutations, not merely across
  re-runs of the same ordering.
- **Split candidates must be partitioned by identity before their amounts are summed.**
  `ReconciliationMatchKernel.TryFindSplit` filters candidates by *sign and amount only* — its own
  remarks say so. The one-to-one path avoids nonsense pairings through `SameTransactionIdentity`;
  applied naively, the split primitive would happily sum unmatched ledger transactions from
  different instruments, currencies, types, or dates because they happen to add up to a statement
  row. The primitive's `accept` overload is the seam for this. Change 11 must partition or validate
  every candidate set by the same account, instrument-or-currency, type, and date constraints the
  pair stages apply, with negative cross-identity tests proving a coincidental sum is refused.
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
  blueprints, it takes the next free ordinal (029 today) and shifts the unshipped reservations up —
  which is permitted precisely because they have not shipped. **Shifting means every reference, not
  just the register table:** `incentive-fee-mechanics.md` names 029–030,
  `commitment-and-capital-call-engine.md` names 031, `equalization-and-series-accounting.md` names
  033–035, and the mark blueprint depends on 036–038 applying in phase order. Updating
  [`docs/engineering/blueprints/README.md`](../engineering/blueprints/README.md) alone would leave
  those documents hard-coded to displaced numbers, which is how a collision or a lower-after-higher
  application gets created.
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

**`quality-gate` alone cannot accept change 9 either, for the same class of reason.** Every lane runs
on Ubuntu with `MERIDIAN_DISABLE_DOCKER_TESTS=true` and no PostgreSQL service, so a new ledger
migration, the Postgres-backed accounting/ledger audit store, and cross-process chain serialization
can all go unexecuted while the gate is green. Change 9 requires a named `Production Certification`
run — or a targeted PostgreSQL-backed run — on the candidate commit, linked as evidence, exactly as
change 3 requires named Windows proof.

**And that run must actually select the new tests, which today it would not.**
`production-certification.yml` invokes `dotnet test` with
`--filter "Category=Integration&Category!=LiveProvider"`, but `LedgerDatabaseFactAttribute` is a
plain `FactAttribute` carrying no trait, and `AccountingConfigurationPostgresStoreTests` declares no
integration category — so naming that workflow as evidence would link a green run that never
executed a single database test. Change 9 must either give its new audit and migration tests the
integration trait, or widen the certification filter, and then assert the resulting TRX actually
contains them. A run that silently selected nothing is worse than no run at all: it looks like
proof.
