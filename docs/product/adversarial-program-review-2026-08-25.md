# Adversarial Program Review — Meridian (2026-08-25)

**Status:** independent review input; not a governance or roadmap-status document
**Owner:** review author (independent adversarial pass)
**Reviewed:** 2026-08-25
**Scope:** whole-program review of Meridian's high-level functionality, focused on what a real end
user can reach, trust, and finish — and where improvement would raise end-user value most.
**Method:** a fresh pass over the wired code paths at commit `e232ece1`, re-testing the open items
from `adversarial-program-review-2026-08-24.md` against the 39 commits landed since, then extending
into three catalogs the prior passes treated separately: the **authorization model**, the **shared
API surface**, and the **client surface**. Route reachability was measured mechanically across both
operator clients. The browser workstation suite was executed locally (269 test files, green). Every
finding is anchored to `file:line` at `e232ece1`.

> This review is deliberately critical; a strengths section gives fair credit at the end. It builds
> on the 2026-08-24 review and its remediation slate, re-tests what stayed open, and does not
> compete with the roadmap registry for live status.

## Headline

The lineage of headlines: 2026-07 — "the codebase is dramatically more capable than the running
product." 2026-08-10 — "acceptance statuses drift ahead of wired reality." 2026-08-18 — "the
remediation gets built, tested, registered, and left unwired at the last seam." 2026-08-24 — "the
program closes the seam a review names, at the named line, and the defect class survives one seam
over."

This pass confirms that pattern held again — capital-call issuance shipped end-to-end while the
NAV/unit-register alternative named in the same recommendation stayed at zero consumers — and then
finds the layer underneath it:

> **Meridian maintains three catalogs that must agree — permissions, routes, and screens. Each is
> internally coherent and independently excellent. None of them is validated against the others.
> The result is a product whose most governed capability is gated to the wrong role, whose most
> authoritative number has no screen, and whose flagship accounting screen reads a different book
> than the one the accounting subsystem was built to prove.**

The single sharpest expression of it: **a `ReadOnly` user can open Accounting → Trial Balance. The
`FundAccountant` and the `Controller` cannot.** And the numbers on that screen are the strategy
run's ledger, not the fund's posted book — because the endpoint that serves the posted book's trial
balance has no client at all.

That is not a wiring gap one seam over from a fix. It is a category error at the product's centre,
and it is invisible to every gate the repository runs, because no gate compares the three catalogs.

## Re-test scorecard (2026-08-24 open items, at `e232ece1`)

| Prior open item | State now | Evidence |
| --- | --- | --- |
| Reconciliation transaction population | **Landed** | `3c11be3e` projects posted journals into the transaction population; `LedgerJournalInternalTransactionSource.cs:76-93` carries FITID identity |
| Broker-truthful kill-switch sweep | **Landed** | `af05058f` sweeps the union of tracked and broker books; `ExecutionReport.ChildOrders` added (`Models.cs:166-174`) and registered (`OrderManagementSystem.ChildOrders.cs:61-84`) |
| Journal immutability at the database | **Landed** | `V_ledger_030__journal_immutability.sql` |
| Release attachment | **Landed** | tag `eval-v0.1.0-eval.1`; `8e9b11c3` attaches consumer setup to the evaluation prerelease |
| Provenance at the ingress seam | **Partial** | `2361152c` threads real provider identity; the contract still permits an un-sourced print — `MarketTradeUpdate.cs:33` is `string? Source = null` |
| Fund-economics activation | **Partial — the named alternative was skipped** | capital-call issuance wired (`CapitalCallFundingIntake.cs:236`); NAV-per-unit + unit register still at zero consumers |
| `ContractMultiplier` on the durable fill record | **Open** | §1 below — and now demonstrably corrupting session restore |
| WPF state un-fork / desktop test job in the gate | **Open** | §7 below |

## 1. The accounting workstation reads the wrong book, and the accountants are locked out of it

This is the headline finding. It has three independently verified parts that compound.

**(a) The screen reads the simulation ledger.** Accounting → Trial Balance binds to
`getTrialBalance: (runId) => getRunTrialBalance(runId)`
(`src/Meridian.Ui/dashboard/src/screens/accounting-screen.view-model.ts:2940`), which calls
`/api/workstation/runs/{runId}/ledger/trial-balance`
(`src/Meridian.Ui/dashboard/src/lib/api.ts:2888-2890`). That binding sits inside the screen's
**reconciliation** services block, beside `getBreakQueue`, `reviewBreak`, and `resolveBreak`, and it
only fires when a reconciliation run is selected:

```ts
// accounting-screen.view-model.ts:4007-4013
useEffect(() => {
  if (!selectedReconciliation || workstream !== "ledger") {
    setTrialBalance([]);
```

There is no path to a trial balance that is not scoped to a run.

**(b) The posted book's trial balance has no client.** The governed reporting endpoints over the
immutable journal — the very spine `W9-ASSET-010` and `V_ledger_030` were built to make
authoritative — are registered, permission-gated, and consumed by nothing:

| Route | Registered at | Consumers (browser + WPF) |
| --- | --- | --- |
| `/api/ledger/periods/{periodId}/trial-balance` | `LedgerEndpoints.cs:367` | **0** |
| `/api/ledger/periods/{periodId}/trial-balance-report` | `LedgerEndpoints.cs:392` | **0** |
| `/api/ledger/periods/{periodId}/pnl-summary` | `LedgerEndpoints.cs:417` | **0** |
| `/api/ledger/reports/trial-balance` | `LedgerEndpoints.cs:442` | **0** |
| `/api/ledger/reports/pnl-summary` | `LedgerEndpoints.cs:501` | **0** |

The two most fundamental outputs of a general ledger — the trial balance and the P&L — are
unreachable from either operator client.

**(c) The roles that own the book are denied the screen that exists.** The run-scoped endpoint gates
on `ViewStrategies | ManageStrategies` (`WorkstationEndpoints.cs:2405`). Measured against
`RolePermissions.cs`:

| Role | `ViewStrategies` | Can open Accounting → Trial Balance |
| --- | --- | --- |
| Admin | yes | yes |
| TradeDesk | yes | yes |
| Analysis | yes | yes |
| Accounting | yes | yes |
| ReportingAnalyst | yes | yes |
| Executive | yes | yes |
| **ReadOnly** (`:129-133`) | **yes** | **yes** |
| **FundAccountant** (`:77-86`) | **no** | **no — 403** |
| **Controller** (`:96-106`) | **no** | **no — 403** |
| **Compliance** | no | no |
| Developer | no | no |

A minimal read-only account can see the trial balance. The Fund Accountant — described in the role
catalog as owning "fund-accounting evidence for assigned funds" — receives a 403. So does the
Controller who signs the close.

**Why it compounds:** the two halves are exactly swapped. The endpoint the UI calls excludes the
accounting roles. The endpoint the accounting roles *can* call (`AdminMaintenance |
ManageDirectLending`, `LedgerEndpoints.cs:386`) has no UI. Either half alone is a wiring gap; together
they mean the governed accounting lane has no operator path at all, and the screen that carries its
name shows a different ledger.

**Improvement.** Point the Accounting workstream's trial-balance and P&L panels at the period/report
endpoints over the posted journal; keep the run-scoped view as an explicitly labelled *strategy run*
artifact under Strategy, not under Accounting. Then add a structural test that fails when a route's
permission set is disjoint from the permission set of every role whose workspace links to it. That
test is what would have caught all three parts.

## 2. `ManageDirectLending` has become the de facto "fund accounting" grant

The governed ledger reports gate on `AdminMaintenance | ManageDirectLending`
(`LedgerEndpoints.cs:386,411,436`). `FundAccountant`, `Controller`, and `Accounting` reach them only
because all three carry `ManageDirectLending` (`RolePermissions.cs:70,82,101`).

Two consequences for a real deployment:

- A fund with no private-credit book must still grant every accountant "manage direct lending" to
  close its month. Least-privilege deployment is impossible as shipped.
- Conversely, granting a private-credit analyst `ManageDirectLending` silently confers general-ledger
  reporting access they were never meant to hold.

The same overload appears in compliance: `/api/compliance/approval-requests` gates on `ManageUsers`
(`ComplianceEndpoints.cs:24`), so a compliance officer must hold user-administration rights to file
an approval request.

**Improvement.** Introduce `ViewLedgerReports` / `ManageLedgerReports` and `ManageCompliance`
permissions and re-gate these surfaces. The permission enum has 27 flags in a `long` — there is ample
room. This is a small, mechanical change that removes a real blocker to any multi-user deployment.

## 3. Paper-session durability rescales every option and fixed-income position on restart

The 2026-08-24 review flagged the missing `ContractMultiplier` on the durable fill record. It is
still open, and the consequence is larger than "replay is wrong": it corrupts **restore**.

`PaperTradingPortfolio.ApplyFill` carries the correct signature —
`ApplyFill(accountId, report, ownerAccountId, contractMultiplier = 1m, usesFaceValuePercentageOfPar = false)`
(`PaperTradingPortfolio.cs:443-448`). Exactly one call site in the estate passes real values:
`OrderManagementSystem.cs:1534`, which reads `_orderContractMultipliers`.

All three call sites inside `PaperSessionPersistenceService` take the defaults:

| Line | Path | Multiplier used |
| --- | --- | --- |
| `:159` | **session restore on startup** | `1m` |
| `:820` | `ReplaySessionAsync` | `1m` |
| `:1190` | candidate portfolio projection | `1m` |

So a paper session holding 10 SPY calls filled at $2.50 restores as $25 of exposure instead of
$2,500, and a fixed-income position restores at 100× its cash. `VerifyReplayAsync`
(`PaperSessionPersistenceService.cs:835`) — the operator's continuity proof, and the feature whose
whole purpose is to demonstrate that sessions survive restarts — compares live state against replay
state. For any derivative session it either reports a false mismatch, or restore has already
rescaled live state so both agree and it falsely passes. Neither outcome is a proof.

The data needed is already on the record: `ExecutionReport.OptionContract.Multiplier`
(`Models.cs:119`) is present on every fill the persistence layer clones, and
`OrderManagementSystem.RiskOutcomes.cs:324` already contains the parsing logic
(`ResolveContractMultiplier`, defaulting options to `100m`). The fix is to call it at the three
restore sites, or to promote `ContractMultiplier` and the percent-of-par flag to first-class fields
on `ExecutionReport` so no reconstruction path can drop them.

**Improvement.** Promote both to `ExecutionReport` fields (JSON-ignored when default, preserving
existing content hashes — the pattern `ChildOrders` already uses at `Models.cs:170-174`). A durable
record that cannot reconstruct its own economics is not a durable record.

## 4. The percent-of-par fix landed; the contract multiplier one parameter away did not

`LiveRunMetricsTracker.RecordFill(FillEvent fill, DateTimeOffset timestamp, bool
usesFaceValuePercentageOfPar = false)` (`LiveRunMetricsTracker.cs:43`) now scales fixed income
correctly:

```csharp
// LiveRunMetricsTracker.cs:53-56
var effectivePrice = usesFaceValuePercentageOfPar ? fill.FillPrice / 100m : fill.FillPrice;
var tradeCashFlow = new TradeCashFlow(..., Amount: -(fill.FilledQuantity * effectivePrice), ...);
```

There is no multiplier parameter. A live option fill books 1/100 of its cash into every derived
metric — realized P&L, drawdown, Sharpe, commission ratio. The underlying `FillEvent`
(`Meridian.Backtesting.Sdk/FillEvent.cs:4-18`) carries neither a multiplier nor a percent-of-par
flag, so the record cannot express the distinction even if the caller knew it.

This is the same defect shape as §3, in a third subsystem. Three subsystems now model instrument
scale differently: `ExecutionPosition.ContractMultiplier` (`ExecutionPosition.cs:42`), the
`usesFaceValuePercentageOfPar` boolean, and `FillEvent`'s implicit 1×.

**Improvement.** Define one instrument-scale value object (multiplier + price convention) in
`Meridian.Contracts`, carry it on `FillEvent` and `ExecutionReport`, and delete the three parallel
representations. Until instrument scale is a single modeled concept rather than an argument passed
by convention, this class of bug will keep reappearing at each new seam — as it has now for three
consecutive reviews.

## 5. The fund-economics kernel is a closed island — second consecutive review at zero consumers

Measured across all of `src/`:

| Type | Consumers in `src/` | Reachable from either client |
| --- | --- | --- |
| `ShareClassUnitRegisterProjector` (`:54`) | **0** | no |
| `NavPerUnitCalculator` | 1 — only the projector above | no |
| `EqualizationCalculator` | 1 — only the projector above | no |
| `MultiCurrencyLedgerTranslator` (`:6`) | **0** | no |

The identifier `NavPerUnit` does not appear anywhere in `src/Meridian.Ui.Shared`,
`src/Meridian.Ui.Services`, or `src/Meridian.Ui/dashboard`. NAV per unit — the number a fund
administrator exists to produce, and the input every LP statement depends on — cannot be computed by
any operator action. Neither can equalization, the high-water mark, the unit register, or FX
revaluation at period close.

The prior review offered two activation options and asked for one. **Capital-call issuance shipped**
— `CapitalCallFundingIntake.cs:236` reaches `CapitalCallDraftFactory.BuildCapitalCallFundingDraft`,
with a governed journal intake and a browser screen (`8b50a6b7`, `c0e5160e`, `4ab5df03`). That is
real, and it is the pattern to copy. The alternative was skipped, so the NAV lane enters its second
consecutive review at zero consumers.

**Improvement.** Wire NAV-per-unit + the unit register through the same path capital-call issuance
just proved: valuation lane → `ShareClassUnitRegisterProjector` → governed journal intake → a
Portfolio or Accounting panel. It is the highest-value dark asset in the repository, and the
plumbing it needs was built and validated six commits ago. If it will not be wired this cycle,
`W9-NAV-006` should not read `ready_for_acceptance`.

## 6. 43% of the shared API surface is reachable by neither operator client

Method: the generated route catalog (`src/Meridian.Ui/dashboard/src/lib/ui-api-routes.generated.ts`,
mirrored from `src/Meridian.Contracts/Api/UiApiRoutes.cs`) declares **862** route constants. Each was
checked for a reference — by constant name or by literal path — in non-test browser workstation
sources and in `src/Meridian.Wpf`.

- Referenced by the browser workstation: **462** (54%)
- Dark to the browser: **400** (46%)
- **Dark to both clients: 374 (43%)**

WPF rescues 26 routes. That number is itself a statement about the parity lane: the desktop
workstation is not covering meaningful ground the browser does not.

Largest dark groups (routes unreachable from either client):

| Group | Dark routes | What is unreachable |
| --- | --- | --- |
| `/api/quality` | 31 | anomalies, completeness, gaps, drops, latency, cross-provider comparison, per-symbol health |
| `/api/workstation` | 33 | assorted read models |
| `/api/storage` | 24 | — |
| `/api/security-master` | 24 | — |
| `/api/providers` | 23 | — |
| `/api/ledger` | 21 | trial balance, P&L, period journal entries, posting-rule candidates, asset-accounting projections |
| `/api/backfill` | 20 | — |
| `/api/fund-structure` | 17 | — |
| `/api/lean` | 16 | — |
| `/api/compliance` | 8 | access reviews, approval requests and decisions, action evaluation, audit extract, control attestation |

Two of these deserve naming on their own. The **compliance** surface
(`ComplianceEndpoints.cs:15-117`) is complete, permission-guarded, and backed by
`ImmutableAuditLogService` — an entire governance capability with no operator path, in a product
whose promise is governed proof. The **data-quality** surface is 31 endpoints deep — exactly the
evidence a "can I trust this number?" product should be surfacing — and the operator sees none of it.

*Caveat on the method:* some dark routes are legitimately server-to-server or diagnostic, and a
handful of paths are reached through composed URL builders my scan would miss. Spot checks confirmed
both directions: `/api/reconciliation/exceptions` looked dark but is the direct-lending lane whose
workstation equivalent *is* wired, while the five ledger reporting routes in §1 were confirmed dark
by direct grep. Treat 43% as a well-founded estimate of the same phenomenon the prior review measured
at 29%, not as an exact count.

**Improvement.** Add the orphan-export structural test the backlog already specifies, with a
declared allowlist for intentionally headless routes, and fail CI when the unallowed dark count
grows. Without a gate, this ratio only moves one way — and it has.

## 7. The authoritative merge gate never compiles or tests the only supported platform

`CLAUDE.md` names `Meridian CI / quality-gate` the authoritative merge gate, and
`build/ci/lane-manifest.json` confirms it is the sole required status check. It aggregates exactly
four lanes, all `ubuntu-latest` (`meridian-ci.yml:201-212`):

```yaml
needs:
  - verify-dotnet
  - verify-browser
  - verify-docs
  - verify-workflows
```

`verify-desktop` (`windows-latest`) carries `requiredCheckRole: evidence` and is **not** in that
`needs` list. Meanwhile `src/Meridian.Wpf/Meridian.Wpf.csproj:14-21` sets
`EnableDefaultCompileItems=false` off Windows:

```xml
<PropertyGroup Condition="'$(IsWindows)' != 'true'">
  <!-- Exclude all source files on non-Windows -->
  <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
</PropertyGroup>
```

So the desktop workstation — a co-equal UI lane, and the client for the only platform ADR-019
supports (Windows 11 x64) — compiles to an empty stub in the gate. **A change that breaks WPF
compilation merges green.** `windows-desktop-build.yml` exists but is path-filtered and runs no
`dotnet test`.

The .NET lane additionally filters `--filter "Category!=Integration&Category!=Performance"`
(`scripts/ci.sh:161`), excluding **70 integration test files** — among them
`InitialAccountBootstrapEndpointTests`, `AuthEndpointTests`,
`MutationAuthorizationGuardMiddlewareTests`, and `RoleAuthorizationTests`. The suites that verify the
first mile and the authorization model are precisely the ones the merge gate does not run — and
authorization is where §1 and §2 live.

**Improvement.** Add `verify-desktop` to the `quality-gate` `needs` list, and promote a first-mile
subset of the Integration category (bootstrap + role authorization) into the required lane. A gate
that cannot fail on the supported platform or on the authorization model is not measuring the
product's supported surface.

## 8. Freshness is pushed where it is cheap and polled where it is expensive

The SSE machinery exists and works: `lib/quotes-stream.ts` and `lib/report-run-stream.ts` both drive
real `EventSource` consumers. It is used for live quotes and report-run progress.

The surfaces where staleness actually costs an operator money still poll:

- operator inbox / notification centre — 60s (`use-notification-center.ts:20`)
- governed approvals — interval refresh (`trading-screen.governed-approvals.ts:110`)
- lifecycle control — 5s (`lifecycle-control-panel.tsx:57`)

Break casework, approvals, and close-readiness do not refresh after a mutation elsewhere in the app,
so two operators working the same close see divergent state for up to a minute. `RegionErrorState` —
the built, correct error surface — appears in **3** non-test files across ~120 screen modules; the
rest still fall back to empty values on failure, which renders "no breaks" and "zero breaks"
identically.

**Improvement.** Route break, approval, and inbox mutations over the existing SSE fan-out rather than
adding polls, and adopt `RegionErrorState` on the reconciliation queue, break detail, and close
cockpit first. An empty grid that means "request failed" is the single most expensive UI lie a
close-management product can tell.

## 9. Smaller findings worth fixing cheaply

- **Governed period reopen is dark.** `LedgerCloseManagementPeriodReopen`
  (`UiApiRoutes.cs:824`) has zero consumers in either client, while plan, late adjustments,
  task sign-offs, evidence review, and period **lock** are all wired. An operator can lock a period
  from the workstation and has no path to reopen it — the one direction that most needs a governed,
  evidenced UI.
- **Provenance remains optional at the type level.** `MarketTradeUpdate.cs:33` is
  `string? Source = null`. The ingress threading landed; the contract still admits an un-sourced
  print, so the class of defect can be reintroduced by any new adapter. Making `Source` required is
  a compile-time gate that no reviewer has to remember.
- **`InMemoryFundStructureService` is 4,326 lines and persists as one JSON snapshot blob.** The
  Postgres path exists (`PostgresFundStructureService`, `PostgresFundStructureStore`) and the startup
  gate correctly forbids the in-memory profile in Production. But the file-backed loader discards the
  whole governance working set on a malformed snapshot and continues
  (`InMemoryFundStructureService.Persistence.cs:79-88`) — it logs a warning and starts empty. For the
  local/dev profile the README recommends to evaluators, a truncated write means silent total state
  loss on next start.
- **Very large files concentrate risk.** `AccountingConfigureViewModel.cs` (5,356 lines),
  `SecurityMasterWorkbenchQueryService.cs` (4,738), `FundOperationsWorkspaceReadService.cs` (4,646),
  `WorkstationEndpoints.cs` (4,201). §1's defect lives inside a 5,900-line view model, which is a
  large part of why it survived four reviews.

## Prioritized improvement list (by end-user value uplift)

1. **Reconnect the accounting lane to its own ledger, and to its own roles.** Point Accounting →
   Trial Balance and P&L at the period/report endpoints over the posted journal; move the run-scoped
   view under Strategy with an explicit label; grant `FundAccountant` and `Controller` the
   permissions their screens require. Until this lands, the product's flagship persona cannot open
   the product's flagship screen, and the number on it is not the fund's. (§1)
2. **Split the overloaded permissions.** Add `ViewLedgerReports`/`ManageLedgerReports` and
   `ManageCompliance`; stop using `ManageDirectLending` as the fund-accounting grant and `ManageUsers`
   as the compliance grant. This is what makes a least-privilege multi-user deployment possible at
   all. (§2)
3. **Make instrument scale a modeled concept, once.** One value object carrying multiplier and price
   convention, on `ExecutionReport` and `FillEvent`, consumed by the three restore sites in
   `PaperSessionPersistenceService` and by `LiveRunMetricsTracker`. This is the third consecutive
   review to find this defect class in a new subsystem; fixing the instance again will not stop the
   fourth. (§3, §4)
4. **Gate the catalogs against each other.** Three structural tests: (a) every route a workspace
   links to must be callable by at least one role that can reach that workspace; (b) every route
   constant must be referenced by a client or appear on a declared headless allowlist; (c) the dark
   count must not grow. These three tests would have caught §1, §5, §6, and §9's reopen gap
   automatically. (§1, §6)
5. **Activate NAV per unit end-to-end.** Valuation → `ShareClassUnitRegisterProjector` → governed
   journal intake → an operator panel, following the path capital-call issuance just proved. Highest
   value-per-line change available, and the plumbing is already validated. (§5)
6. **Put the supported platform in the merge gate.** Add `verify-desktop` to `quality-gate`'s
   `needs`, and promote the bootstrap and role-authorization Integration suites into the required
   lane. (§7)
7. **Surface the data-quality and compliance evidence that already exists.** 31 quality endpoints and
   8 compliance endpoints are built, guarded, and invisible. For a product whose promise is "prove the
   number," these are the proof — and they are the cheapest large value uplift on this list, because
   the servers are done. (§6)
8. **Stop the UI lying by omission.** `RegionErrorState` on the reconciliation queue, break detail,
   and close cockpit; SSE fan-out for break/approval/inbox mutations instead of 60s polls. (§8)
9. **Close the small governed gaps.** Wire period reopen; make `MarketTradeUpdate.Source` required;
   make the fund-structure snapshot loader fail loudly rather than starting empty. (§9)

## What is genuinely strong (do not regress it)

- **The governance persistence gate is textbook.** `EnsureGovernancePersistenceProfile` fails closed
  with a diagnostic naming the missing variable, and `IsInMemoryGovernanceProfileEnabled` *throws* if
  the in-memory profile is requested in Production
  (`StorageFeatureRegistration.cs:625-665`). This is how a fail-closed default should read.
- **The fixture boundary is enforced structurally**, not by convention:
  `INonProductionOnlyService` + `ProductionServiceRegistrationPolicy.cs:85` reject a
  non-production implementation at registration time. This is exactly the mechanism §4 of this review
  asks for elsewhere — the repository already knows how to build catalog-level gates; it has just not
  pointed one at routes or roles yet.
- **Immutability moved into the database.** `V_ledger_030__journal_immutability.sql` puts the posted
  journal's immutability where application code cannot bypass it.
- **The role catalog is well-modeled.** 11 roles including `FundAccountant`, `Controller`, and
  `Compliance`, with `ApproveReporting` correctly withheld from `FundAccountant` — real segregation of
  duties, thought through. The defect in §1 is not in this model; it is that endpoint guards were
  written without consulting it.
- **Discipline markers are real.** Zero `NotImplementedException` and 4 TODO/FIXME/HACK markers across
  873K lines of C# and 748 TypeScript modules.
- **The browser workstation suite is green and substantial** — 269 test files, executed locally at
  `e232ece1`, exit 0.
- **Generated-contract drift gates already exist** for UI routes and the workspace catalog
  (`scripts/ci.sh:173-175`). The machinery for improvement #4 is built; it needs two more checks
  pointed at reachability and authorization.
- **Capital-call issuance is the model to copy.** Kernel → governed intake → approval queue → operator
  screen → screenshot catalog, in one cycle, with tests. Every dark asset in §5 and §6 should be
  activated this way.

## Relationship to existing planning

This review is independent input. Live delivery status stays in the roadmap registry
(`docs/roadmap/README.md`, `docs/roadmap/data/*.yml`), and release readiness stays in the
[Implementation and Readiness Tracker](implementation-todo-list.md). Where this review's findings
overlap the ranked W9 slate or the
[production-readiness backlog](production-readiness-backlog-2026-08.md), those documents remain
authoritative for sequencing.

Two items are re-raised from the 2026-08-24 pass without progress and are noted here as such rather
than as new findings: the durable-fill contract multiplier (§3, §4) and the fund-economics NAV lane
(§5). One item — the desktop test job in the required gate (§7) — is re-raised with new evidence that
the gap is wider than "no test job": the platform is not compiled either.
