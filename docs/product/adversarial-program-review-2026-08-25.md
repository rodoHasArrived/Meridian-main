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

The single sharpest expression of it: **the two ledgers an operator can reach are split across
disjoint role sets, and neither set is the one the screen is named for.** The trading, analysis, and
reporting roles can open the Accounting screen's trial balance — but it reads the *strategy run's*
ledger. `FundAccountant` and `Controller` hold the permissions for the *posted* book and are refused
that screen. And the endpoint that serves the posted book's trial balance had no client at all.

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
| `ContractMultiplier` on the durable fill record | **Open** | §3 below — and now demonstrably corrupting option session restore |
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
on `ViewStrategies | ManageStrategies` (`WorkstationEndpoints.cs:2405`), but a leaf gate alone does
not decide what an operator can reach. Effective access is the **intersection** of the workspace
payload gate
(`/api/workstation/accounting`, admitting `ViewTrades | ViewDirectLending | ManageDirectLending |
ViewSecurityMaster | ModifySecurityMaster | AdminMaintenance`, `WorkstationEndpoints.cs:453`) and the
leaf-route gate. Evaluated that way against `RolePermissions.cs` — including `DeveloperPermissions`,
which is the computed expression `AdminPermissions & ~UserPermission.ManageUsers` (`:39-40`):

| Role | Accounting workspace | Run-scoped trial balance | Posted-journal trial balance |
| --- | --- | --- | --- |
| Admin · Developer · Accounting | yes | **yes** | **yes** |
| TradeDesk · Analysis · ReportingAnalyst · Executive | yes | **yes** | no |
| **FundAccountant** (`:77-86`) | yes | **no — 403** | **yes** |
| **Controller** (`:96-106`) | yes | **no — 403** | **yes** |
| Compliance | yes | no | no |
| ReadOnly (`:129-133`) | **no** | — | — |

The interesting shape is not a single locked-out role but a **split**: the trading, analysis, and
reporting roles reach only the simulation book; the two roles that own the fund's records reach only
the posted book; and outside Admin, Developer, and Accounting no role can see both. The Fund
Accountant — described in the role catalog as owning "fund-accounting evidence for assigned funds" —
passes the workspace gate and is then refused the screen's trial balance. So is the Controller who
signs the close.

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

## 3. Paper-session restore rescales every option position

The 2026-08-24 review flagged the missing `ContractMultiplier` on the durable fill record. It is
still open — and tracing it end to end shows the defect is larger than "the durable record drops it".
**The multiplier never reaches portfolio economics on any path, live or restored.**

`PaperTradingPortfolio.ApplyFill` carries the multiplier
(`PaperTradingPortfolio.cs:443-448`), and exactly one call site passes a real value:
`OrderManagementSystem.cs:1534`, reading `_orderContractMultipliers`. But follow where that value
goes. `ApplyFillToAccount` forwards it to a single destination —
`pos.AttributeFill(ownerAccountId, signedQty, contractMultiplier)` (`:639`) — and `AttributeFill`
only records it as metadata and tracks per-owner quantities (`:1289-1300`). The economic paths
receive price and quantity alone: `ApplyBuy` computes `var notional = qty * price` (`:620`), and
cost basis, cash, margin borrow, and `pos.MarketPrice = price` all follow from that. No multiplier
enters any of them.

So a paper session holding 10 SPY calls at $2.50 is booked at **$25 of exposure from the very first
live fill**, not on restore. `ContractMultiplier` is attribution metadata that looks like an economic
input and is never used as one.

That reframes the persistence gap rather than erasing it. All three
`PaperSessionPersistenceService` call sites — `:159` (session restore on startup), `:820`
(`ReplaySessionAsync`), `:1190` (candidate projection) — still take the `1m` default, so the
restored book additionally loses the attribution metadata and the per-owner split that depends on
it, and `VerifyReplayAsync` (`:835`) compares two books that agree only because both are wrong the
same way. A continuity proof that passes because live and replay share a defect is not a proof.

**Scope: options only.** Fixed income is *not* affected, and the contrast is the whole lesson.
`ExecutionReport` carries `UsesFaceValuePercentageOfPar` as a first-class persisted field
(`Models.cs:186`), `CloneExecutionReport` preserves it, and `ApplyFill` reads it off the record
regardless of the parameter (`PaperTradingPortfolio.cs:469`) — then applies it to the price before
any cash or cost-basis math runs. Percent-of-par is modeled *and consumed*. The contract multiplier
is neither: it has no field on the record, and even when passed it is routed to metadata instead of
into the arithmetic.

**Improvement — two changes, and the second matters more.** First, apply the multiplier in the
economic paths so a contract's notional is `qty × price × multiplier` for cash, cost basis, margin,
and market value; without this, any persistence fix leaves live option economics wrong. Second, give
`ContractMultiplier` the first-class record treatment `UsesFaceValuePercentageOfPar` already has
(JSON-ignored when default, preserving existing content hashes — the `ChildOrders` pattern at
`Models.cs:170-174`), so no reconstruction path can drop it. `ResolveContractMultiplier`
(`OrderManagementSystem.RiskOutcomes.cs:324`) already derives the value; the gap is that nothing
downstream multiplies by it. A field that is carried but never used is worse than a missing one — it
reads, to every subsequent reviewer, as though the concern were already handled.

## 4. The percent-of-par fix landed; the contract multiplier one parameter away did not

`LiveRunMetricsTracker.RecordFill(FillEvent fill, DateTimeOffset timestamp, bool
usesFaceValuePercentageOfPar = false)` (`LiveRunMetricsTracker.cs:43`) now scales fixed income
correctly:

```csharp
// LiveRunMetricsTracker.cs:53-56
var effectivePrice = usesFaceValuePercentageOfPar ? fill.FillPrice / 100m : fill.FillPrice;
var tradeCashFlow = new TradeCashFlow(..., Amount: -(fill.FilledQuantity * effectivePrice), ...);
```

There is no multiplier parameter, so a live option fill books 1/100 of its cash. The underlying
`FillEvent` (`Meridian.Backtesting.Sdk/FillEvent.cs:4-18`) carries neither a multiplier nor a
percent-of-par flag, so the record cannot express the distinction even if the caller knew it.

**Blast radius: the whole metric set, not just the fill log.** It is tempting to stop at "the
`TradeCashFlow` is wrong" — `Build` derives net P&L and total return from a *passed-in* equity
figure (`:131-132`), drawdown from the `RecordEquity` series (`:96-105`), and Sharpe from
equity-derived daily returns, so none of them read the cash flows. But §3 establishes that the
portfolio producing that equity is itself unscaled: `PaperPosition.MarketValue` is
`Math.Abs(Quantity) * MarketPrice` (`PaperTradingPortfolio.cs:1118`) with no multiplier, and
`LiveStrategyRunSession` feeds exactly that `_context.PortfolioValue` into both `RecordDayEnd`
(`:361-364`) and `Build` (`:772-775`).

So for an options session the equity series is 1/100-scaled at source, and **net P&L, total return,
drawdown, and Sharpe all inherit it**. Only commissions escape, because `_totalCommissions`
accumulates independently of position value. The per-trade record is wrong *and* every session-level
number computed from the book is wrong, for the same root cause.

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

**One inventory, stated precisely.** The 862 constants are the denominator, and every group above is
counted inside it. The **compliance** surface is *not*: its eight endpoints are registered as string
literals in `ComplianceEndpoints.cs:15-117` and never enter `UiApiRoutes.cs` or the generated
mirror, so they are out-of-catalog and must be reported separately rather than folded into the 43%.
Their absence from the catalog is itself a finding — a route that never becomes a constant is
invisible to the drift gate that keeps the mirror honest, so no tooling can notice it has no client.

Counted on its own terms, the compliance surface is a complete, permission-guarded governance
capability with no operator path, in a product whose promise is governed proof. Its **evidence
durability is uneven**, and any activation work has to fix that first: only `actions/evaluate`,
`audit/extract`, and `controls/attestation` touch `ImmutableAuditLogService`
(`ComplianceEndpoints.cs:52,66,70`); approval requests and decisions live in
`FileComplianceApprovalStore`, a JSON snapshot rewritten in place (`ComplianceApprovalStore.cs:247-250`)
— durable but mutable, not append-only; and access reviews are held in a plain
`List<AccessReviewRecord>` (`AccessReviewService.cs:94-95`) that is empty after restart. Wiring a UI
onto the approval and access-review routes as they stand would present retention and tamper-evidence
the storage does not provide. The **data-quality** surface is different in kind: the aggregate
`/api/quality/dashboard` *is* wired — the Data workspace renders its composite health, gap, anomaly,
and completeness evidence through `data-screen.data-quality.view-model.ts` — while the 31 per-symbol
and per-dimension drill-downs behind it have no consumer. So the operator can see that a symbol is
unhealthy and cannot open the evidence that says why. That is a depth gap, not an invisible
capability, and it should be scoped as one.

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
compilation merges green.**

The desktop lane is not untested, and the gap is narrower than "no Windows tests": for changes
matching its path filters, `windows-desktop-build.yml:132-139` runs
`scripts/dev/validate-wpf-dev.ps1` without `-BuildOnly`, and that script executes `dotnet test`
against `Meridian.Wpf.Tests` (`:226-252`) plus, under `-IncludeSupervisorTests`, the
lifecycle-supervisor suite. The defect is the *gating*, not the coverage: that workflow is
path-filtered and absent from `quality-gate`'s `needs`, so its result never blocks a merge, and a
change outside its filters that breaks WPF is compiled by nothing at all.

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
so two operators working the same close see divergent state for up to a minute. That staleness, not
error handling, is the finding here: the accounting surfaces **do** surface failures — the
reconciliation panel renders `view.errorText` (`accounting-screen.reconciliation-panels.tsx:106-118`),
the close cockpit does the same (`close-cockpit-panels.tsx:177,457`), and the trial balance carries a
structured `ApiErrorDisplay`. `RegionErrorState` appears directly in only 3 non-test modules, but it
is also composed by `AsyncRegion` (`async-region.tsx:95,109`), so a raw import count understates
adoption and is not evidence that the rest collapse failures into empty data.

**Improvement.** Route break, approval, and inbox mutations over the existing SSE fan-out rather than
adding polls. On errors the remaining work is consistency rather than absence: three different
primitives (`RegionErrorState`, `StatusBanner`, bespoke `errorText` blocks) express the same state,
so an operator learns three visual vocabularies for "this failed". An empty grid that means "request
failed" is the single most expensive UI lie a
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
  Writes themselves are safe — `JsonFileFundStructureStateStore.SaveAsync` goes through
  `AtomicFileWriter.WriteAsync` (`:16`), which syncs a temp file and renames, so an interrupted write
  cannot truncate the target. The exposure is everything *outside* that seam: external corruption,
  a partial restore, a hand-edited or half-copied data root, a filesystem fault. Any of those turns
  the whole governance working set into an empty one, announced by a warning line. Fail closed on a
  snapshot that exists but will not parse, rather than starting empty and letting the operator
  discover it downstream.
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
2. **Split the overloaded permissions — and split the replacements too.** Add
   `ViewLedgerReports`/`ManageLedgerReports` and `ViewCompliance`/`ManageCompliance`; stop using
   `ManageDirectLending` as the fund-accounting grant and `ManageUsers` as the compliance grant. Note
   the read/write split on both: a single `ManageCompliance` would re-create the same defect one
   level down, forcing an auditor who only reads `/audit/extract`, `/controls/attestation`, and
   `GET /access-reviews` to also hold authority over approval decisions and access-review
   remediation. This is what makes a least-privilege multi-user deployment possible at all. (§2)
3. **Make instrument scale a modeled concept, once.** One value object carrying multiplier and price
   convention, on `ExecutionReport` and `FillEvent` — and *consumed* in `ApplyBuy`/`ApplySellLong`,
   `PaperPosition.MarketValue`, and the three restore sites in `PaperSessionPersistenceService`.
   Carrying it is not the fix; multiplying by it is. Until then every option session's equity, P&L,
   drawdown, and Sharpe are 1/100-scaled. Third consecutive review to find this class in a new
   subsystem. (§3, §4)
4. **Gate the catalogs against each other — with predicates that actually bite.** An
   existential check ("some role can reach it") is useless here: Admin, Developer, and Accounting
   satisfy it while `FundAccountant` and `Controller` stay locked out, so the defect passes. Three
   tests that do bite: (a) a declared **role-to-surface expectation table** — `FundAccountant` and
   `Controller` must reach the Accounting trial balance and P&L — asserted against the workspace ∩
   leaf intersection, so a persona lockout fails the build; (b) every route
   constant must be referenced by a client or appear on a declared headless allowlist — this is the
   only one of the three that would have caught the unconsumed posted-ledger routes, since no
   workspace links to them and no role check can see them; (c) the dark
   count must not grow. These three tests would have caught §1, §5, §6, and §9's reopen gap
   automatically. (§1, §6)
5. **Activate NAV per unit end-to-end.** Valuation → `ShareClassUnitRegisterProjector` → governed
   journal intake → an operator panel, following the path capital-call issuance just proved. Highest
   value-per-line change available, and the plumbing is already validated. (§5)
6. **Put the supported platform in the merge gate — noting the naive fix does not work.**
   `needs:` resolves only job IDs within the same workflow, and `verify-desktop` is a lane-manifest
   ID, not a job: the Windows validation is the `desktop` job in the separate
   `windows-desktop-build.yml`. So either invoke that Windows job from `meridian-ci.yml` (or move it
   there) and add the real job ID to `needs`, or make `Windows Desktop Build / desktop` a required
   status check alongside `quality-gate` — and accept that its path filters must widen, since a
   change outside them can still break WPF. Also promote the bootstrap and role-authorization
   Integration suites into the required lane. (§7)
7. **Surface the evidence that already exists — after checking each surface can bear the weight.**
   31 quality drill-downs sit behind a dashboard that is already wired, so those are genuinely
   cheap: the servers are done and the operator path exists to hang them from. The 8 compliance
   endpoints are not cheap in the same way — exactly **one** route writes to the immutable audit log
   (`actions/evaluate`, via `auditLog.Append` at `ComplianceEndpoints.cs:61`); `audit/extract` and
   `controls/attestation` only read it (`GetAll`/`VerifyIntegrity`), approvals live in a rewritable
   snapshot, and access reviews do not survive restart. Durable retention has to land *before* a UI
   presents any of it as governed proof. Register them as route constants too, so the
   drift gate can see them. (§6)
8. **Fix the freshness gap; standardize the error vocabulary second.** The demonstrated defect is
   staleness — break casework, approvals, and close readiness do not refresh after a mutation, so
   route them over the existing SSE fan-out instead of 60-second polls. The error work is *not*
   restoring missing failure semantics: the reconciliation panel, trial balance, and close cockpit
   already render their failures. Consolidating those bespoke blocks onto `RegionErrorState` is
   visual standardization — worth doing so operators learn one vocabulary for "this failed", but
   lower priority than the staleness it was previously bundled with. (§8)
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

## Corrections applied after automated review

Two rounds of automated review challenged eleven claims across this document. Every one was checked
against the code, **all eleven held**, and the findings above are the corrected text. Recorded here
because a review that demands evidence discipline owes the same discipline about its own errors.

**Round 1 — seven claims:**

| Claim as first written | Why it was wrong | Corrected in |
| --- | --- | --- |
| "A `ReadOnly` user can open Accounting → Trial Balance" | `ReadOnly` holds none of the `/api/workstation/accounting` admission permissions, so it never reaches the screen. Effective access is workspace ∩ leaf, not the leaf gate alone | §1(c) |
| `Developer` marked as lacking `ViewStrategies` | `DeveloperPermissions` is the computed expression `AdminPermissions & ~ManageUsers` (`RolePermissions.cs:39-40`); the first pass parsed only `\|`-joined literals and mis-read it | §1(c) |
| "A fixed-income position restores at 100× its cash" | `ExecutionReport.UsesFaceValuePercentageOfPar` is a persisted field and `ApplyFill` reads it off the record (`PaperTradingPortfolio.cs:469`); percent-of-par survives restore. Only the multiplier defect is real | §3 |
| Missing multiplier corrupts "realized P&L, drawdown, Sharpe, commission ratio" | Those derive from equity observations and an independent commission accumulator, not from the fill cash flow. The corruption is trade-level | §4 |
| "The operator sees none of" the data-quality surface | `/api/quality/dashboard` is wired into the Data workspace; the 31 drill-downs behind it are what lack consumers | §6 |
| "`windows-desktop-build.yml` runs no `dotnet test`" | It runs `validate-wpf-dev.ps1` without `-BuildOnly`, which does run the WPF and supervisor suites. The defect is gating, not coverage | §7 |
| "A truncated write means silent total state loss" | Saves go through `AtomicFileWriter.WriteAsync`, which prevents exactly that. The fail-quiet loader is still real; the scenario was not | §9 |

**Round 2 — four more, on the corrected text:**

| Claim | Why it was wrong | Corrected in |
| --- | --- | --- |
| "Restore rescales a previously correct $2,500 option position" | The live book was never correct. `ApplyFillToAccount` routes the multiplier only to `AttributeFill` metadata; `ApplyBuy` computes `qty * price` with no multiplier, so option economics are wrong from the first live fill. The proposed persistence-only remedy would not have fixed it | §3 — rewritten, remedy expanded |
| `/api/compliance` counted among the 862 route constants | Those eight endpoints are string literals never registered in `UiApiRoutes.cs`; including them mixed two inventories and made the 43% non-reproducible | §6 |
| Compliance surface "backed by `ImmutableAuditLogService`" | Only three of eight routes touch it; approvals use a rewritable JSON snapshot and access reviews an in-memory list emptied on restart | §6, improvement #7 |
| Index entry describing the posted trial balance as unreachable | True at `e232ece1`, stale after PR #2824 merged into this branch | `docs/product/README.md` |

**Round 3 — six more, four of them contradictions introduced by the earlier corrections:**

| Claim | Why it was wrong | Corrected in |
| --- | --- | --- |
| "Session metrics are safe; the corruption is trade-level" | Contradicted §3 in the same commit. `PaperPosition.MarketValue` is `Math.Abs(Quantity) * MarketPrice` (`:1118`) and `LiveStrategyRunSession` feeds that equity into `RecordDayEnd` (`:361-364`) and `Build` (`:772-775`), so net P&L, total return, drawdown, and Sharpe all inherit the 1/100 scale. Only commissions escape | §4 — narrowing reversed |
| "Add `verify-desktop` to `quality-gate`'s `needs`" | Not implementable. `needs:` resolves only job IDs in the same workflow; `verify-desktop` is a lane-manifest ID, and the Windows validation is the `desktop` job in a separate workflow | Improvement #6 |
| The proposed catalog structural test | An existential predicate ("some role can reach it") passes on this very defect — Admin, Developer, and Accounting satisfy it while `FundAccountant` and `Controller` stay locked out — and never examines the posted-ledger routes, which no workspace links to | Improvement #4 — respecified |
| "Three compliance routes write to the immutable log" | One does. `audit/extract` and `controls/attestation` are GETs calling `GetAll`/`VerifyIntegrity` | Improvement #7 |
| "Stop the UI lying by omission" | Contradicted the corrected §8. The named panels already render failures; consolidating them is standardization, not restoration, and was crowding out the real staleness defect | Improvement #8 |
| `ManageCompliance` as a single new grant | Re-creates the §2 overload one level down: an auditor reading `/audit/extract` would also gain authority over approval decisions | Improvement #2 — split into `ViewCompliance`/`ManageCompliance` |

The core findings survive, several in sharper form. Four were materially wrong as first stated — the
role-access table, the fixed-income claim, the multiplier's blast radius, and two of the proposed
remedies — and are rewritten rather than softened; the multiplier correction made the defect
*larger* and the original remedy insufficient, and both the catalog test and the CI `needs` change
were unimplementable as specified. Three method lessons generalize. **A permission gate read in isolation predicts the wrong
access** — the same intersection error this document accuses the codebase of, committed while
describing it. And **a value that is carried but never consumed reads as handled**: `ContractMultiplier`
is threaded through three layers and multiplied by nothing, which is why four consecutive reviews,
this one included, mistook plumbing for correctness. And third: **correcting one section without
re-reading the sections that depend on it introduces fresh contradictions** — round 2's narrowing of
§4 was refuted by §3, which the same commit had just rewritten. Four of round 3's six findings are
damage from rounds 1 and 2, not from the original draft.

## Addendum — remediation landed while this review was in flight

`main` moved to `054e2d27` after this document was written, merging PR #2824 ("Point the Accounting
trial balance at the posted journal"). That branch is merged into this one, so the findings above are
still anchored at `e232ece1` but the code beside them has moved. What actually changed, verified:

**Genuinely fixed — §1(a) and §1(b).** A new `src/Meridian.Ui/dashboard/src/lib/ledger-reports-api.ts`
calls `/api/ledger/periods`, `…/{periodId}/trial-balance`, and `…/{periodId}/pnl-summary`, and a new
`AccountingPostedLedgerSection` (`accounting-screen.posted-ledger-panel.tsx`, mounted at
`accounting-screen.tsx:2894`) renders them. The posted journal's trial balance and P&L now reach an
operator for the first time. That is the right fix, done the right way.

**The class survived, three ways** — the pattern this review's headline describes, one cycle later:

- **The run-scoped panel is still wired, unchanged.** `accounting-screen.view-model.ts:2940` still
  reads `getTrialBalance: (runId) => getRunTrialBalance(runId)` — the exact line §1 cites — still
  gated on `ViewStrategies`. The Accounting screen now carries *two* trial balances over two
  different books, and the accounting roles still receive a 403 on one of them.
- **A second screen was not touched.** `finance-standard-pages-screen.tsx:299` still calls
  `getRunTrialBalance`, so the run-scoped view remains an operator-facing "trial balance" in a
  second place.
- **§2 is untouched, and the fix now depends on it.** The posted-journal endpoints still gate on
  `AdminMaintenance | ManageDirectLending` (`LedgerEndpoints.cs:386,411,436`), and
  `FundAccountant`/`Controller` still lack `ViewStrategies`. The new panel is reachable by those
  roles *only because* `ManageDirectLending` is the overloaded fund-accounting grant §2 names. The
  remediation is load-bearing on the defect.

So §1(c), §2, and the disjoint-permission structural test in improvement #4 all remain open, and the
"two books, one screen name" ambiguity is now more visible rather than less. Retiring the run-scoped
panel from Accounting (or relabelling it as a Strategy-run artifact) and splitting the permission are
the remaining work.

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
