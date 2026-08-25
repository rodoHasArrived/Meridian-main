# Adversarial Program Review — Meridian (2026-08-25)

**Status:** independent review input; not a governance or roadmap-status document
**Owner:** review author (independent adversarial pass)
**Reviewed:** 2026-08-25
**Scope:** whole-program review of Meridian's high-level functionality, focused on what a real end
user can reach, trust, and finish — and where improvement would raise end-user value most.
**Method:** a fresh pass over the wired code paths at commit `e232ece1`, re-testing the open items
from `adversarial-program-review-2026-08-24.md` against the 39 commits landed since, then extending
into three catalogs the prior passes treated separately: the **authorization model**, the **shared
API surface**, and the **client surface**. Route reachability was measured mechanically across all
three client layers — the browser workstation, `src/Meridian.Wpf`, and the shared
`src/Meridian.Ui.Services` clients the desktop calls through. The browser workstation suite was executed locally (269 test files, green). Every
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

The single sharpest expression of it: **the two roles named for owning the fund's books are the ones
refused the screen that shows a trial balance.** `FundAccountant` and `Controller` hold the
permissions for the *posted* book and are denied the Accounting screen's panel, which reads the
*strategy run's* ledger and gates on `ViewStrategies`. Trading, Analysis, ReportingAnalyst and
Executive see only that strategy-run book; Compliance sees neither. Admin, Developer and Accounting do
reach both — so the split is not clean, and calling the role sets "disjoint" (as an earlier draft of
this line did) overstates a matrix that shows otherwise. The defect is a persona lockout at the
centre of the accounting lane, not a partition. And the endpoint serving the posted book's trial
balance had no client at all.

That is not a wiring gap one seam over from a fix. It is a category error at the product's centre,
and it is invisible to every gate the repository runs, because no gate compares the three catalogs.

## Re-test scorecard (2026-08-24 open items, at `e232ece1`)

| Prior open item | State now | Evidence |
| --- | --- | --- |
| Reconciliation transaction population | **Landed** | `3c11be3e` projects posted journals into the transaction population; `LedgerJournalInternalTransactionSource.cs:76-93` carries FITID identity |
| Broker-truthful kill-switch sweep | **Landed** | `af05058f` sweeps the union of tracked and broker books; `ExecutionReport.ChildOrders` added (`Models.cs:166-174`) and registered (`OrderManagementSystem.ChildOrders.cs:61-84`) |
| Journal immutability at the database | **Landed** | `V_ledger_030__journal_immutability.sql` |
| Release attachment | **Landed** | tag `eval-v0.1.0-eval.1`; `8e9b11c3` attaches consumer setup to the evaluation prerelease |
| Provenance at the ingress seam | **Landed at runtime; type-level hardening outstanding** | `2361152c` threads real provider identity, and `TradeDataCollector.OnTrade` rejects a missing `Source` with a `MissingSource` integrity event before storing (`:117-134`, tested). `MarketTradeUpdate.cs:33` is still `string? Source = null`, so the remaining work is compile-time, not behavioural |
| Fund-economics activation | **Partial — the named alternative was skipped** | capital-call issuance wired (`CapitalCallFundingIntake.cs:236`); NAV-per-unit + unit register still at zero consumers |
| `ContractMultiplier` on the durable fill record | **Open — and wider than reported** | §3–§4 below: the multiplier reaches the two exposure projections (`AggregatePortfolioExposureProvider:571-582`, `WorkstationEndpoints.BuildExposureReport`) and nothing else — not the paper book's transaction branches, valuation projections, persistence sites or either margin model — so option position value is understated live as well as on restore (see §4 for the per-metric breakdown, which is not a uniform 1/100 on equity or Sharpe) |
| WPF state un-fork | **Partial** | reconciliation posture no longer reads desktop-local state and the remaining local fund-setup lane is labelled with a provenance badge (`AccountingFeatureModule.cs:53-59`); the scheduler host loops were removed from the desktop process and now run server-side (`:196-202`). Residual: fund-account and fund-structure services still persist JSON under `%LOCALAPPDATA%`, along with drafts and schedules |
| Desktop test job in the required gate | **Open** | §7 below. Bundling this with the state un-fork, as an earlier draft did, hid the remediation above and meant neither half was actually re-tested |

## 1. The accounting workstation reads the wrong book, and the accountants are locked out of it

This is the headline finding. It has three independently verified parts that compound.

> **Superseded in part.** PR #2824 landed after this was written and wired the posted-journal trial
> balance and P&L into a new `AccountingPostedLedgerSection`, so **(b) below is history, not current
> state**. (a) and (c) still stand: the run-scoped panel remains wired on `ViewStrategies`, which
> `FundAccountant` and `Controller` do not hold. See the addendum at the end for the verified split.

**(a) The screen reads the strategy-run ledger.** Accounting → Trial Balance binds to
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

That run scope is **not simulation-only**, and the distinction matters for who is harmed.
`StrategyRunReadService` serves "backtest, paper, and live history" (`:14-16`), `LoadRunsAsync`
filters the Accounting reconciliation population by fund profile alone, with no run-mode predicate
(`FundOperationsWorkspaceReadService.cs:796-812`), and live sessions persist as `BrokerLive`
(`StrategyRunEntry.cs:138`). So when a live run is selected this panel shows a **live strategy-run
subledger**, not a simulated book. The defect is therefore not "operators are shown fake numbers" —
it is that the Accounting screen's only trial balance is scoped to a *strategy run* in any mode,
while the posted journal that the fund's books actually close on has no screen at all.

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
| **FundAccountant** (`:77-86`) | yes | **no — withheld, see below** | **yes** |
| **Controller** (`:96-106`) | yes | **no — withheld, see below** | **yes** |
| Compliance | yes | no | no |
| ReadOnly (`:129-133`) | **no** | — | — |

**The lockout does not surface as a 403, and that is worse.** Earlier versions of this table said
`FundAccountant` and `Controller` receive a 403 on the run-scoped trial balance. They do not — they
never issue the request. `ResolveAccountingWorkspaceReadScope` sets `StrategyRuns` only for
`ViewStrategies | ManageStrategies` (`WorkstationEndpoints.AccountingWorkspace.cs:255-258`), and the
accounting payload then returns `ReconciliationQueue: Array.Empty<WorkstationAccountingRunRecord>()`
when that scope is false (`:132-137`) — under a comment stating the queue is "withheld exactly as the
empty-run branch above renders them, so a caller without run authority sees the shape the workspace
already has when there is nothing to show". The browser derives its selection from that queue
(`accounting-screen.view-model.ts:3875-3892`), finds nothing, and returns before ever calling
`getRunTrialBalance` (`:4007-4019`).

So the operator sees **an empty reconciliation queue that is indistinguishable from having no runs**.
A 403 is at least an error a UI can render; a withheld-as-empty projection tells the accountant
their fund has nothing to reconcile. That is the same defect class as §8's "no breaks" versus
"request failed", sitting at the centre of §1 — and it means the role-to-surface test in improvement
#4 cannot check permission sets alone. It has to assert on the **projected payload**, because the
permissions are working exactly as written and the damage is in what the projection substitutes.

The interesting shape is not a single locked-out role but a **split**: the trading, analysis, and
reporting roles reach only the strategy-run book; the two roles that own the fund's records reach only
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
artifact under Strategy, not under Accounting. Then add the **role-to-surface expectation table**
described in improvement #4 — a declared assertion that `FundAccountant` and `Controller` reach the
Accounting trial balance and P&L, evaluated against the workspace ∩ leaf intersection. An earlier
draft proposed instead a test that fails when a route's permission set is *disjoint from every* role
whose workspace links to it; that predicate is existential and this very defect satisfies it, since
Admin, Developer and Accounting all reach both surfaces. It is kept here only as a record of what
does not work.

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
**The multiplier reaches two downstream consumers — both exposure projections — and none of the
paper book's own economics, live or restored.**

`PaperTradingPortfolio.ApplyFill` carries the multiplier
(`PaperTradingPortfolio.cs:443-448`), and exactly one call site passes a real value:
`OrderManagementSystem.cs:1534`, reading `_orderContractMultipliers`. But follow where that value
goes. `ApplyFillToAccount` forwards it to a single destination —
`pos.AttributeFill(ownerAccountId, signedQty, contractMultiplier)` (`:639`) — and `AttributeFill`
only records it as metadata and tracks per-owner quantities (`:1289-1300`). The economic paths
receive price and quantity alone, and that is true of **every** transaction branch, not just the
long ones: `ApplyBuy` computes `var notional = qty * price` (`:620`); `ApplyShortSell` computes
`var proceeds = qty * price` (`:822`); `ApplyCoverShort` computes `var coverCost = coverQty * price`
(`:753`) and derives `realised` from it. Cost basis, cash, margin borrow, and
`pos.MarketPrice = price` all follow. The projections are unscaled too —
`PaperPosition.UnrealisedPnl` is `(MarketPrice - CostBasis) * Quantity` (`:1128`), which sums into
`AccountState.UnrealisedPnl` (`:1039`) and from there into every account snapshot (`:1081`). No
multiplier enters any of them.

**The short side is worse, and in a way the long side is not.** On a Reg-T account `ApplyShortSell`
sizes the collateral off that same unscaled figure — `additionalMargin = proceeds *
(regt.ShortInitialRate - 1m)` and `pos.MarginBorrowed -= proceeds * regt.ShortInitialRate`
(`:829-834`) — so a short option book posts **1/100 of the collateral it owes**, and the risk system
reads a naked short as fully margined when it is not. Both short branches also post their ledger
entries at the unscaled figure: `ApplyShortSell` credits `Cash` and `ShortSecuritiesPayable` with
`proceeds` (`:847-851`), and `PostCoverShortEntry` posts `proceedsRemoved`, `coverCost` and
`realised` (`:892-910`). The defect therefore reaches the double-entry ledger, not only the
in-memory book.

So a paper session holding 10 SPY calls at $2.50 is booked at **$25 of exposure from the very first
live fill**, not on restore.

**Two downstream paths already consume it, and the remedy must not touch either.**
`PaperPosition.ToExecutionPosition` preserves the multiplier (`:1324`) and
`AggregatePortfolioService.SplitByOwner` carries it into each contribution. From there:

1. `AggregatePortfolioExposureProvider` multiplies by it —
   `var price = unitPrice * (contribution.ContractMultiplier > 0m ? contribution.ContractMultiplier : 1m)`
   (`:571-582`), under a comment making precisely this section's argument: "an option position of
   100 contracts at a $5 premium is $50k of exposure, not $500".
2. `WorkstationEndpoints.BuildExposureReport` multiplies **independently** —
   `Math.Abs(contribution.Quantity) * Math.Abs(contribution.CostBasis) * (ContractMultiplier > 0m ? … : 1m)`
   (`WorkstationEndpoints.PortfolioAggregation.cs:96-123`) — and its `/api/portfolio/exposure`
   result is operator-facing: the WPF `AggregatePortfolioViewModel` consumes it (`:246`).

**Both exposure projections are therefore already correct**, and a remediation applied
indiscriminately would scale them twice. The defective consumers are the paper book's own cash, cost
basis, P&L and account snapshots, and the two margin models — not everything downstream.

The count here has now been wrong twice in the same direction. The original text said *nothing*
consumes the multiplier; round 14 corrected that to *one*; this is the second. Each correction was
made by finding a consumer rather than by enumerating them, which is why the number kept moving.
The reliable statement is structural: **the multiplier is consumed by the exposure projections and
ignored by everything that produces the numbers an operator reads** — and any remediation needs an
exhaustive consumer list, not another spot-check.

That reframes the persistence gap rather than erasing it. All three
`PaperSessionPersistenceService` call sites — `:159` (session restore on startup), `:820`
(`ReplaySessionAsync`), `:1190` (candidate projection) — still take the `1m` default, and
`VerifyReplayAsync` (`:835`) compares two books that agree only because both are wrong the same way.
A continuity proof that passes because live and replay share a defect is not a proof.

**A second, independent gap sits at the same three call sites.** They also omit `ownerAccountId`, and
that one is *not* fixed by a multiplier field: `AttributeFill` builds the per-owner split from its
own `ownerAccountId` argument, and neither `ExecutionReport` nor `PaperSessionFillRecord`
(`IPaperSessionStore.cs:72-76`) persists it. So every fund-scoped fill restores unattributed, and
fund-scoped portfolio views are incomplete after any restart. Two missing pieces of durable identity,
one record — and fixing either alone still leaves the restored book wrong.

**Scope: options only.** Fixed income is *not* affected, and the contrast is the whole lesson.
`ExecutionReport` carries `UsesFaceValuePercentageOfPar` as a first-class persisted field
(`Models.cs:186`), `CloneExecutionReport` preserves it, and `ApplyFill` reads it off the record
regardless of the parameter (`PaperTradingPortfolio.cs:469`) — then applies it to the price before
any cash or cost-basis math runs. Percent-of-par is modeled *and consumed*. The contract multiplier
is neither: it has no field on the record, and even when passed it is routed to metadata instead of
into the arithmetic.

**Improvement — two changes, and the second matters more.** First, apply the multiplier in the
economic paths so a contract's notional is `qty × price × multiplier` for **monetary amounts only**:
cash, proceeds, realized/unrealized P&L, margin, and market value. *Not* cost basis — the stored
`AverageCostBasis` is documented as a FIFO-weighted **entry price** (`ExecutionPosition.cs:14`) and
downstream consumers already apply the multiplier themselves (`OptionPosition.cs:57`,
`FuturePosition.cs:61`, and `AggregatePortfolioService.cs:174-179`, which passes price and multiplier
onward as separate values). Multiplying at storage while leaving those consumers alone would report
$250,000 of exposure for ten $2.50 calls instead of $2,500 — a 100× error in the opposite direction.
Keep lot and average prices in per-unit terms; migrate every consumer together or not at all.
Second, give `ContractMultiplier` a persisted field so no reconstruction path can drop it — **and
version the canonical hash when you do, or every existing durable session stops loading.**
`PaperSessionFillRecord.Validate` recomputes `ComputeCanonicalHash(Fill)` by re-serializing the whole
report through `ExecutionJsonContext` and **throws `InvalidDataException`** on any mismatch
(`IPaperSessionStore.cs:149-175`). That context sets `DefaultIgnoreCondition = WhenWritingNull`
(`ExecutionJsonContext.cs:18`), so a non-nullable `decimal` defaulting to `1m` is always written: a
legacy record persisted before the field existed deserializes to `1m`, re-serializes with
`contractMultiplier: 1`, hashes differently, and fails validation. This is a hard restore failure,
not a warning. The fix needs a schema-versioned hash path or an explicit migration, with a
legacy-hash compatibility test. `ResolveContractMultiplier`
(`OrderManagementSystem.RiskOutcomes.cs:324`) already derives the value; the gap is that the
transaction, valuation and margin paths do not multiply by it. **Scope the fix to those** — the
two exposure projections already apply it — `AggregatePortfolioExposureProvider:571-582` and
`WorkstationEndpoints.BuildExposureReport` (`PortfolioAggregation.cs:96-123`, consumed by the WPF
`AggregatePortfolioViewModel`) — and scaling either again would overstate exposure by the multiplier,
the same double-count the cost-basis caveat above warns about.
A field consumed in one projection and ignored in the rest is worse than a missing one: it reads, to
every subsequent reviewer, as though the concern were already handled.

**Third — and this one is not about scale at all.** Persisting and consuming `ContractMultiplier`
leaves the restored book *still* wrong for fund-scoped sessions, because the same three replay sites
omit `ownerAccountId` and `AttributeFill` receives null (see the per-owner gap above). Restore the
owning fund too: either persist it alongside the fill, or join the durable order history's
`OrderState.FundAccountId` through the fill's order identity, and pass it at every replay and
projection site. A two-step remedy that fixes only scale produces a book with correct numbers and no
idea whose they are.

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

**Both legs are unscaled, and that changes the answer.** `ApplyBuy` deducts the same unscaled
`notional` from cash that it books into the position (`PaperTradingPortfolio.cs:701-715`:
`account.Cash -= notional + commission`). The cash and position errors are therefore *paired*, and
the effect on each metric follows from that pairing rather than from the position error alone. Worked
for a cash account, zero commission, 10 calls at $2.50 against $100,000 of starting cash:

| Quantity | Effect |
| --- | --- |
| Trade cash leg and position value | Each 1/100 of correct — the errors offset |
| Portfolio equity **at entry** | **Exactly correct.** $100,000 either way; the two errors cancel |
| Portfolio equity **after a move** | Deviates by 99% of unrealised P&L, and **overstated on losses**: at a $2.00 mark, $99,995 against a correct $99,500 |
| Net P&L, total return | **Exactly 1/100** ($−5 against $−500) |
| Drawdown | ≈1/100, since both the peak and the trough carry the same 1/100 P&L error over a nearly-correct cash base |
| Sharpe (`mean / stdDev * √252`, `:143`) | **Distorted, and not by a clean factor.** `RecordDayEnd` divides each change by the *previous* equity, and the two books' denominators diverge as P&L accumulates — buggy is `cash + P&L`, correct is `cash + 100×P&L`. Over a six-mark series the per-day ratio drifts (0.010000, 0.009950, 0.010049, 0.009901, 0.010148) and `mean/stdDev` lands **17.9% below** correct. Preserved only while cumulative option P&L stays negligible against initial cash — not because the book is pure-option |
| Commissions | Unaffected — already dollar amounts, accumulated independently |

So the damage is severe where it is easiest to miss: an options session's realized P&L is off by two
orders of magnitude while its **equity still looks plausible** — right at entry, and wrong in the
*flattering* direction on losses. Sharpe is wrong too, but by a drifting amount rather than a
recognisable factor, which is worse than either being right or being obviously broken.

This review has now mis-stated that blast radius **four times**: too narrow (round 2), too broad
(round 3), wrongly reversed on equity and Sharpe (round 4), and only correct once the paired cash leg
was traced and the arithmetic actually run (round 7). Every earlier version reasoned about the
position error in isolation. That is the same mistake the document accuses the codebase of — reading
one side of a seam and inferring the other — and it took four attempts to stop making it.

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

## 6. 29% of the shared API surface is reachable by no operator client

Method: the generated route catalog (`src/Meridian.Ui/dashboard/src/lib/ui-api-routes.generated.ts`,
mirrored from `src/Meridian.Contracts/Api/UiApiRoutes.cs`) declares **862** route constants. Each was
checked for a reference — by constant name or literal path — across all **three** client layers:
the browser workstation, `src/Meridian.Wpf`, and `src/Meridian.Ui.Services`, the shared API-client
layer the desktop calls through.

| Layer | Routes reached |
| --- | --- |
| Browser workstation | 465 |
| `src/Meridian.Ui.Services` (shared clients) | 203 |
| `src/Meridian.Wpf` | 121 |
| **Union — reachable by some client** | **612** |
| **Dark to all three** | **250 (29%)** |

> **Correction.** The first version of this section reported 43% (374 routes) because the scan omitted
> `src/Meridian.Ui.Services` entirely. That layer alone accounts for 122 of the routes previously
> counted dark — including **all 16 `/api/lean` routes**, which the earlier table listed as
> unreachable and which are in fact consumed by `LeanIntegrationService`. The corrected figure, 29%,
> happens to match what the 2026-08-24 review measured; the earlier claim that the ratio had grown
> was an artifact of a worse method, not a real regression. A measurement that omits a client layer
> does not overstate the problem slightly — it manufactures a trend.

Largest dark groups (routes unreachable from either client):

| Group | Dark | What is unreachable |
| --- | --- | --- |
| `/api/workstation` | 33 | assorted read models |
| `/api/quality` | 27 | history, statistics, ranked views, comparison and reports behind the wired dashboard and drill-down |
| `/api/security-master` | 24 | — |
| `/api/ledger` | 19 | period journal entries, posting-rule candidates, asset-accounting projections |
| `/api/fund-structure` | 17 | — |
| `/api/providers` | 13 | — |
| `/api/execution` · `/api/messaging` · `/api/options` | 11 each | — |
| `/api/backfill` · `/api/config` | 9 each | — |

**One inventory, stated precisely.** The 862 constants are the denominator, and every group above is
counted inside it. The **compliance** surface is *not*: its eight endpoints are registered as string
literals in `ComplianceEndpoints.cs:15-117` and never enter `UiApiRoutes.cs` or the generated
mirror, so they are out-of-catalog and must be reported separately rather than folded into the 29%.
Their absence from the catalog is itself a finding — a route that never becomes a constant is
invisible to the drift gate that keeps the mirror honest, so no tooling can notice it has no client.

Counted on its own terms the compliance surface is permission-guarded and has no operator path — but
it is **not** a complete governance capability, and two things have to land server-side before any UI
work starts.

*It has no discovery contract for approvals.* The eight routes are POST `approval-requests`, POST
`approval-requests/{id}/decisions`, POST `actions/evaluate`, GET `audit/extract`, GET
`controls/attestation`, POST `access-reviews/assess`, POST `access-reviews/run`, and GET
`access-reviews` (`ComplianceEndpoints.cs:15,26,48,66,70,85,101,117`). There is **no GET for approval
requests** — not a list, not a read-by-id — and the gap is deeper than a missing route:
`IComplianceApprovalStore` exposes only `CreateRequest`, `RecordDecision`, and single-id `Resolve`
(`ComplianceApprovalStore.cs:12-27`), with no enumeration at all. An approver therefore cannot
discover what is pending; they must already hold the approval id. The review queue this surface
implies cannot be built from what exists, so activation is not "persistence plus UI" — a scoped
list/read contract has to be added first.

*Its evidence durability is uneven.* Only `actions/evaluate`, `audit/extract`, and
`controls/attestation` touch `ImmutableAuditLogService` (`:48,66,70`); approval requests and
decisions live in `FileComplianceApprovalStore`, whose `Persist` serializes the whole snapshot and
hands it to `AtomicFileWriter.Write` (`ComplianceApprovalStore.cs:247-250`). That is an **atomic
replace, not an in-place rewrite** — a correction from an earlier draft, and the same
`AtomicFileWriter` misreading this document already made once about the fund-structure loader. There
is no torn-write exposure; the objection is that the snapshot is mutable and not append-only, so it
cannot carry tamper-evidence. Access reviews are weaker still: a plain `List<AccessReviewRecord>`
(`AccessReviewService.cs:94-95`) that is empty after restart. Wiring a UI onto the approval and
access-review routes as they stand would present retention and tamper-evidence the storage does not
provide. The **data-quality** surface is different in kind, and narrower than an earlier draft of this
section claimed. The aggregate `/api/quality/dashboard` is wired, *and so is the per-symbol
drill-down*: the mounted `DataQualityRegion` expands each symbol into component scores with
explanations, provider freshness, open gaps with remediation actions, and current issues
(`data-screen.data-regions.tsx:236-359`), and WPF opens an equivalent panel through
`DataQualityViewModel.ShowSymbolDrilldown` (`:298-320`). Both project from the composite payload —
`data-screen.data-quality.view-model.ts` imports `getQualityDashboard` and nothing else. So an
operator **can** already open the evidence for why a symbol is unhealthy; the earlier claim that they
could not was wrong.

What the 27 dark quality routes add is a different axis — **history, distribution, ranking,
cross-provider comparison, and reporting**: `gaps/timeline/{symbol}`, `latency/{symbol}/histogram`,
five `*/statistics` endpoints, the ranked cross-symbol views (`errors/top-symbols`,
`completeness/low`, `health/unhealthy`, `latency/high`, `anomalies/stale`),
`comparison/discrepancies`, and `reports/daily|weekly|export`. The composite carries *current state
for one symbol*; nothing in it carries a trend, a distribution, a cross-symbol ranking, or an
exportable report. That is the gap worth scoping, and scoping it as a missing drill-down would
duplicate an operator path that already exists.

*Caveat on the method, and it cuts both ways:* this measures **reference**, not operator reach, and
the error is unbounded in each direction, so **29% is an estimate — not a floor and not a ceiling.**

*Undercounting:* a route counts as reached if any client-layer file names it, including a wrapper
nothing calls. Three browser exports prove it — `getQualityCompleteness`, `getQualityGaps` and
`getQualityAnomalies` have **zero call sites outside `lib/api.ts`**, yet their routes are counted as
reached. Every dead wrapper hides a dark route.

*Overcounting:* some dark routes are legitimately server-to-server or diagnostic and were never
meant to have a client, and a few live paths are assembled through composed URL builders the scan
cannot follow, so they are recorded dark while an operator does reach them.

Spot checks confirmed both directions, which is exactly why the figure is not a bound: an earlier
version of this caveat opened "it cuts one way" and then conceded both in the same paragraph.
Establishing a real number needs transitive reachability from mounted routes and views — which is
precisely what the CI gate in improvement #4 would have to compute, so the measurement debt and the
gate are the same piece of work, and the gate's baseline must be set from a measurement that does
not yet exist rather than from 250. Note also that the first attempt at this number was wrong by
half in the *other* direction, because it enumerated two client layers out of three.

**Improvement.** Add the orphan-export structural test the backlog already specifies, with a
declared allowlist for intentionally headless routes, and fail CI when the unallowed dark count
grows. There is no evidence the ratio *has* grown — the corrected 29% matches the prior review, and
the apparent increase was an artifact of the broken scan. The gate's value is preventing future
drift, not arresting an observed slide.

## 7. The authoritative merge gate never compiles or tests the proposed support platform

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

That guard is what lets a Windows-targeted *restore* succeed off Windows — but it is **not** the CI
mechanism, and an earlier draft of this section had that wrong. The required lane never compiles
`Meridian.Wpf` at all, and the reason takes two steps to state accurately.

`scripts/ci.sh` restores the full solution (`:135`), then builds one filter —
`dotnet build Meridian.WebWorkstation.slnf` (`:156`) — whose contents are the single project
`src/Meridian/Meridian.csproj`. **That is not the whole build, though.** The very next step invokes
`run-dotnet-ci-tests.py` (`:159`), whose `main` calls `run_builds` (`:324-343,430`) and issues a real
`dotnet build` for every default test project. `tests/Meridian.Tests/Meridian.Tests.csproj` alone
carries **30 `ProjectReference`s** against 38 projects under `src/`, so most of the solution *is*
compiled — through the test projects' closures rather than the web filter. A previous version of this
section claimed the gate "compiles one project's closure out of the solution"; that over-read the
`.slnf` and ignored the builds happening one line below it.

What is genuinely absent is narrower and unchanged: `Meridian.Wpf` and the Windows-only projects.
`run-dotnet-ci-tests.py:28-33` lists `Meridian.Wpf.Tests`, `Meridian.LifecycleSupervisor.Tests` and
`Meridian.Setup.Tests` as `WINDOWS_ONLY_TEST_PROJECTS`, excluded from the ubuntu lane — under a
comment stating that `Meridian.Wpf.Tests` "compiles an empty stub off-Windows
(`EnableDefaultCompileItems=false`)". The desktop *app* project is in no test project's closure
either, so nothing on this lane compiles it.

So the desktop workstation — a co-equal UI lane, and the client for the platform ADR-019 *proposes*
as the v1 production envelope (Windows 11 x64) — is **absent from the gate's build**, while the rest
of the solution is covered. The finding is the one it always was; only the mechanism needed
correcting, twice.
That ADR is still **"Proposed (awaiting core-team sign-off)"** (`019-production-support-matrix-and-deployment-posture.md:3`),
and its context records that no support declaration exists yet and that `PRD-000` blocks every
supported production release until one is signed (`:11-14`) — so Windows is the *target*, not a
ratified guarantee. The gating argument does not depend on the difference: whichever platform is
ratified, the lane that compiles WPF is not the lane that blocks the merge. **A change that breaks WPF
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

**Improvement.** *Not* by adding `verify-desktop` to `quality-gate`'s `needs` list — an earlier draft
of this block said exactly that, and it cannot work: `needs:` resolves only job IDs within one
workflow, `verify-desktop` is a lane-manifest ID rather than a job, and the Windows validation is the
`desktop` job in a separate workflow. That was established in round 3 and stated correctly in
improvement #6, but this block was never updated to match — the second time in this document a
refuted remedy survived in the section that first proposed it while the summary carried the fix.
The two viable options are to invoke the Windows job *from* `meridian-ci.yml` and add its real job ID
to `needs`, or to make its emitted check required directly — verifying the context string first,
since the workflow overrides the job's display name to `verify-desktop (build/test WPF)`. Also
promote a first-mile subset of the Integration category (bootstrap + role authorization) into the
required lane. A gate that cannot fail on the supported platform or on the authorization model is
not measuring the product's supported surface.

## 8. Freshness is pushed where it is cheap and polled where it is expensive

The SSE machinery exists and works: `lib/quotes-stream.ts` and `lib/report-run-stream.ts` both drive
real `EventSource` consumers. It is used for live quotes and report-run progress.

The surfaces where staleness actually costs an operator money still poll:

- governed approvals — 15s (`trading-screen.governed-approvals.ts:47`)
- lifecycle control — 5s (`lifecycle-control-panel.tsx:57`)
- operator inbox / notification centre — 60s (`use-notification-center.ts:20`)
- **accounting break casework — no poll at all.** `usePollingInterval` is installed only for
  Trading, provider routing, and Portfolio (`use-workstation-data.ts:731-733`); Accounting is not
  among them, and the break-queue fetch runs only when its effect dependencies change
  (`accounting-screen.view-model.ts:3903-3912`)

**Break casework is the unbounded case, and it is the only one established here.** Approvals *do*
refresh on their own: `useGovernedApprovalsViewModel` installs a `setInterval` at
`DEFAULT_APPROVAL_REFRESH_MS = 15_000` (`trading-screen.governed-approvals.ts:47,101-112`), so a
second operator's escalation appears within fifteen seconds — stale, but bounded. An earlier draft of
this paragraph swept approvals and close-readiness into the same claim as break casework; that
overstates it, and for close-readiness the evidence gathered here establishes nothing either way.
Break casework is different in kind: `usePollingInterval` does not cover Accounting, and the
break-queue fetch runs only when its effect dependencies change, so a second operator's assignment or
resolution stays invisible **until the first triggers a manual refresh or remounts the screen** —
divergence with no upper bound at all. That is a materially worse defect than the 15s/60s polls
elsewhere, and it is the finding here, not error handling: the accounting surfaces **do** surface failures — the
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

- **The wired reopen reopens a *workflow*, not an accounting period — and the difference is the
  whole finding.** The mounted `/accounting/operations-continuity` screen does render a "Governed
  period reopen command" and submit incident, approval, justification, impact, correlation and
  evidence through `reopenOperationsContinuityWorkflow`
  (`operations-continuity-screen.tsx:1464-1504,2263-2366`), with test coverage. But the endpoint it
  reaches calls `IOperationsContinuityWorkflowService.ReopenWorkflowAsync` **directly**
  (`WorkstationEndpoints.cs:1346-1354`), which transitions workflow state and nothing else. The real
  ledger path is `AccountingCloseManagementService.cs:1295-1325`: it runs
  `ReopenAndQueueClosingReversalsAsync` under the posting and consistency gates *first*, and only
  then performs the same workflow transition. `LedgerCloseManagementPeriodReopen`
  (`UiApiRoutes.cs:824`) — the route that reaches it — has zero consumers in either client.

  So the operator-visible result is worse than a missing screen: the UI can report a **reopened
  workflow while the ledger period and its closing entries remain locked**, with no indication that
  the accounting half did not happen. An operator can lock a period from the workstation and still
  has no governed path to reopen it. A previous draft of this bullet accepted a correction that the
  capability "is not dark" on the strength of the client call site alone; tracing the endpoint shows
  the original finding was substantially right.
- **Provenance is optional at the type level but fail-closed at runtime — this is hardening, not a
  gap.** `MarketTradeUpdate.cs:33` is still `string? Source = null`, so the *contract* admits an
  un-sourced print. The *runtime* does not: `TradeDataCollector.OnTrade` checks
  `MarketDataSources.IsMissing(update.Source)`, publishes an `IntegrityEvent.MissingSource` carrying
  an explicit `UNKNOWN` sentinel, and **returns before the trade is created or stored**
  (`TradeDataCollector.cs:117-134`), with
  `CollectorSourceProvenanceTests.TradeCollector_MissingSource_RejectsWithMissingSourceIntegrityEvent`
  covering it. An earlier draft said a new adapter could silently reintroduce an un-sourced print;
  at this shared collector seam it cannot — the print is rejected loudly. Making `Source` required
  is worth doing as **compile-time hardening of an already fail-closed control**, moving the
  rejection from run time to build time, but it should be ranked as such rather than as an open
  governed gap. (Scope note: this verifies the collector seam, which the code documents as the
  shared singleton serving every active adapter; whether any path writes trades without passing
  through it is not something this review established either way.)
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

1. **Finish reconnecting the accounting lane to its own roles.** PR #2824 already did the first
   half — the posted journal's trial balance and P&L now render in
   `AccountingPostedLedgerSection`. What remains: retire the run-scoped panel from Accounting (or
   relabel it a Strategy-run artifact and move it there), so the screen stops showing two books
   under one name; and re-gate the accounting artifact on a **ledger-specific read permission**
   (`ViewLedgerReports` from improvement #2), so the roles that own the records stop being served an
   empty queue in place of their runs. **Do not close it by granting `ViewStrategies`** — that flag
   gates eleven endpoint files,
   among them `CoveredCallEndpoints`, `LeanEndpoints`, `QuantLabEndpoints`, `PromotionEndpoints` and
   `StrategyLifecycleEndpoints`, so it would hand both personas covered-call results, Lean
   configuration and algorithm history, strategy-designer drafts, run fills and attribution, and the
   Strategy workspace. It is also self-defeating alongside the first half of this item: once the
   run-scoped panel leaves Accounting, accountants need no strategy grant at all. An earlier draft
   said simply "grant them the permissions their screens require", which read literally means
   exactly the over-grant this note now rules out. Until this lands, the flagship persona still
   cannot open half of the flagship screen. (§1, §2)
2. **Split the overloaded permissions — and split the replacements too.** Add
   `ViewLedgerReports`/`ManageLedgerReports` and `ViewCompliance`/`ManageCompliance`; stop using
   `ManageDirectLending` as the fund-accounting grant and `ManageUsers` as the compliance grant. Note
   the read/write split on both: a single `ManageCompliance` would re-create the same defect one
   level down, forcing an auditor who only reads `/audit/extract`, `/controls/attestation`, and
   `GET /access-reviews` to also hold authority over approval decisions and access-review
   remediation. This is what makes a least-privilege multi-user deployment possible at all. (§2)
3. **Make instrument scale a modeled concept, once.** One value object carrying multiplier and price
   convention, on `ExecutionReport` and `FillEvent` — and *consumed* in **every** transaction branch,
   not a subset: `ApplyBuy`, `ApplySellLong`, `ApplyShortSell` and `ApplyCoverShort` (the last two
   also feed Reg-T collateral and the ledger postings), the `MarketValue`/`SignedMarketValue`/
   `UnrealisedPnl` projections on `PaperPosition`, the three restore sites in
   `PaperSessionPersistenceService`, **and both margin models** — `RegTMarginModel:93,135` and
   `PortfolioMarginModel:55-57,93-97` compute notional, maintenance and the stressed legs from
   `position.Quantity * price` and never read the `ContractMultiplier` that
   `PaperPosition.ToExecutionPosition` already hands them, so margin requirements and
   excess-liquidity checks stay at 1/100 even after every transaction branch is fixed. That is a
   *separate* margin path from the short-sale collateral in §3: one is `pos.MarginBorrowed` inside
   the portfolio, the other is the `IMarginModel` requirement computed over it. Both need the
   multiplier, and both need regression coverage.
   Carrying it is not the fix; multiplying by it is. Until then an option session's P&L and total
   return are off by the multiplier, its equity is wrong in the flattering direction on losses, and
   its Sharpe drifts — see §4 for the per-metric breakdown, which is *not* a uniform 1/100. Third
   consecutive review to find this class in a new subsystem. (§3, §4)
4. **Gate the catalogs against each other — with predicates that actually bite.** An
   existential check ("some role can reach it") is useless here: Admin, Developer, and Accounting
   satisfy it while `FundAccountant` and `Controller` stay locked out, so the defect passes. Three
   tests that do bite: (a) a declared **role-to-surface expectation table** — `FundAccountant` and
   `Controller` must reach the Accounting trial balance and P&L — asserted **against the projected
   payload, not the permission sets**, because §1's lockout is a withheld-as-empty
   `ReconciliationQueue` rather than a denial: every gate passes and the operator still sees nothing;
   (b) every route constant must be reachable **transitively from a mounted route or view**, or
   appear on a declared headless allowlist — *reference alone is not enough*, since the three dead
   quality wrappers in §6 are referenced by `lib/api.ts` and called by nothing, so a
   reference-only check would certify part of the very dark surface it exists to find. This is
   still the only one of the three that would have caught the unconsumed posted-ledger routes, since
   no workspace links to them and no role check can see them; (c) the dark count must not grow,
   measured the same transitive way so the baseline and the gate agree. These three catch §1, §6, and §9's unreconciled reopen routes
   automatically — but **not
   §5**, and the reason is worth stating because it exposes a limit of this whole review's thesis.
   The NAV kernel has no route constant and no endpoint registration:
   `ShareClassUnitRegisterProjector` and `NavPerUnitCalculator` appear only in
   `src/Meridian.Ledger/`, its README, their tests, and planning docs. A cross-catalog test can
   detect that catalogs *disagree*; it is blind to a capability absent from all of them. Catching §5
   needs a fourth, different invariant: a **declared-capability-to-surface** check that fails when a
   declared capability has no registered route and no client consumer. Scope it carefully, because
   the obvious scoping misses the case it was built for: `W9-NAV-006` is **not** `done` — it is
   `status: ready_for_acceptance` with `evidence_posture: implementation_complete`
   (`docs/roadmap/data/roadmap-items.yml`), and the roadmap decision log reserves `done` until
   operator acceptance. A gate reading only *shipped* capabilities would stay green on the NAV kernel
   until after the acceptance it is supposed to inform. It must include **acceptance candidates whose
   exit criteria or evidence claim a runnable operator surface** — which is exactly what
   `W9-NAV-006`'s criteria do. (§1, §5, §6)
5. **Activate NAV per unit end-to-end.** Valuation → `ShareClassUnitRegisterProjector` → governed
   journal intake → an operator panel, following the path capital-call issuance just proved. Highest
   value-per-line change available, and the plumbing is already validated. (§5)
6. **Put the supported platform in the merge gate — noting the naive fix does not work.**
   `needs:` resolves only job IDs within the same workflow, and `verify-desktop` is a lane-manifest
   ID, not a job: the Windows validation is the `desktop` job in the separate
   `windows-desktop-build.yml`. So either invoke that Windows job from `meridian-ci.yml` (or move it
   there) and add the real job ID to `needs`, or make the Windows workflow's check a required
   status check alongside `quality-gate` — noting that the required context uses the job's *display*
   name, not its key: `windows-desktop-build.yml:106-107` declares job `desktop` but renders it as
   **`verify-desktop (build/test WPF)`**, so configuring `Windows Desktop Build / desktop` would
   match nothing and silently leave WPF non-blocking. Verify the emitted context string before
   configuring it, and accept that the workflow's path filters must widen, since a change outside
   them can still break WPF. Also promote the bootstrap and role-authorization
   Integration suites into the required lane. (§7)
7. **Surface the evidence that already exists — after checking each surface can bear the weight.**
   27 quality routes carrying history, statistics, ranked views, comparison and reports sit behind
   a dashboard *and* a per-symbol drill-down that are already wired, so those are genuinely cheap:
   the servers are done and the operator path exists to hang them from. The 8 compliance
   endpoints are not cheap in the same way, for two independent reasons. First, there is **no
   discovery contract for approvals**: no GET for approval requests, and `IComplianceApprovalStore`
   has no enumeration method (`ComplianceApprovalStore.cs:12-27`), so the approver queue cannot be
   built from what exists — that is server work, not UI work. Second, the evidence is uneven:
   exactly **one** route writes to the immutable audit log (`actions/evaluate`, via
   `auditLog.Append` at `ComplianceEndpoints.cs:61`); `audit/extract` and `controls/attestation`
   only read it (`GetAll`/`VerifyIntegrity`); approvals live in an atomically-replaced but mutable
   snapshot; and access reviews do not survive restart. A list/read contract and durable retention
   both have to land *before* a UI presents any of it as governed proof. Register them as route constants too, so the
   drift gate can see them. (§6)
8. **Fix the freshness gap; standardize the error vocabulary second.** The demonstrated defect is
   staleness — break casework, approvals, and close readiness do not refresh after a mutation, so
   route them over the existing SSE fan-out instead of 60-second polls. The error work is *not*
   restoring missing failure semantics: the reconciliation panel, trial balance, and close cockpit
   already render their failures. Consolidating those bespoke blocks onto `RegionErrorState` is
   visual standardization — worth doing so operators learn one vocabulary for "this failed", but
   lower priority than the staleness it was previously bundled with. (§8)
9. **Close the small governed gaps.** Wire an operator path to *ledger period* reopen — the mounted
   command reopens only the continuity workflow, leaving the period and its closing reversals locked,
   and `LedgerCloseManagementPeriodReopen` (the route that runs the posting-gated path) has no
   client; make `MarketTradeUpdate.Source` required as compile-time hardening — the collector already
   rejects a sourceless print at run time (`TradeDataCollector.cs:117-134`), so this moves an existing
   control from run time to build time rather than closing an open hole;
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

Twenty-one rounds of automated review challenged **63 claims** across this document. Every one was checked
against the code, **all 63 held**, and the findings above are the corrected text. **Four more were
caught by re-measuring and re-reading rather than by a reviewer** — the quality-route count (wrong at
31 in three places), a refuted remedy still standing in §1, the re-test table's categorical multiplier
claim, and §3's own lead sentence — and each is recorded as a row below, marked *(self-detected)*.
The table therefore holds **67 rows: 63 raised by review, 4 found here.** Noted here because a review that demands evidence discipline
owes the same discipline about its own errors.

This header was itself stale from round 3 until round 7, still reading "two rounds / eleven claims"
while the table below it grew to seven. The section documenting a failure to keep dependent text in
sync was out of sync — the seventh instance of that pattern in this document, and the one that best
explains why the remedy is a mechanical grep of every surface rather than an intention to be
careful.

**Round 1 — seven claims:**

| Claim as first written | Why it was wrong | Corrected in |
| --- | --- | --- |
| "A `ReadOnly` user can open Accounting → Trial Balance" | `ReadOnly` holds none of the `/api/workstation/accounting` admission permissions, so it never reaches the screen. Effective access is workspace ∩ leaf, not the leaf gate alone | §1(c) |
| `Developer` marked as lacking `ViewStrategies` | `DeveloperPermissions` is the computed expression `AdminPermissions & ~ManageUsers` (`RolePermissions.cs:39-40`); the first pass parsed only `\|`-joined literals and mis-read it | §1(c) |
| "A fixed-income position restores at 100× its cash" | `ExecutionReport.UsesFaceValuePercentageOfPar` is a persisted field and `ApplyFill` reads it off the record (`PaperTradingPortfolio.cs:469`); percent-of-par survives restore. Only the multiplier defect is real | §3 |
| Missing multiplier corrupts "realized P&L, drawdown, Sharpe, commission ratio" | Those derive from equity observations and an independent commission accumulator, not from the fill cash flow. The corruption is trade-level | §4 |
| "The operator sees none of" the data-quality surface | `/api/quality/dashboard` is wired into the Data workspace; 27 routes behind it are what lack consumers | §6 |
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

**Round 4 — three more, all self-inflicted:**

| Claim | Why it was wrong | Corrected in |
| --- | --- | --- |
| "The restored book loses the per-owner split *that depends on* the multiplier" | It does not depend on it. `AttributeFill` builds the split from a separate `ownerAccountId` argument, and neither `ExecutionReport` nor `PaperSessionFillRecord` persists that — so the proposed multiplier field would leave every fund-scoped fill unattributed. Two independent gaps, not one | §3 |
| "Net P&L, total return, drawdown, and Sharpe all inherit the 1/100 scale" | Overcorrected in the opposite direction from round 2. Equity is cash + positions and cash is intact, so equity is not 1/100 of correct; and Sharpe is `mean / stdDev`, which a *uniform* scale would cancel — the real effect is nonlinear distortion, which is worse than a clean scale because it looks plausible | §4 — replaced with a per-metric table |
| Addendum marking "§1(a) and §1(b)" fixed | Only (b) was. §1(a) is the run-scoped binding, which the same addendum confirms is still mounted — so the label contradicted the paragraph four lines below it | Addendum |

**Round 5 — three more, including the headline statistic:**

| Claim | Why it was wrong | Corrected in |
| --- | --- | --- |
| **"43% of routes are dark"** | The scan enumerated two client layers out of three, omitting `src/Meridian.Ui.Services` — the shared API-client layer the desktop calls through. That layer reaches 203 routes, 122 of which were counted dark, **including all 16 `/api/lean` routes** listed as unreachable. True figure: **250 of 862, 29%** | §6 — remeasured |
| "Apply the multiplier to cash, **cost basis**, margin, and market value" | `AverageCostBasis` is a per-unit entry price and consumers apply the multiplier themselves (`OptionPosition.cs:57`, `AggregatePortfolioService.cs:174-179`). Multiplying at storage would report $250,000 for ten $2.50 calls — a 100× error the other way | §3 |
| "JSON-ignore when default, as `ChildOrders` does" | Neither existing pattern transfers. `ChildOrders` is `WhenWritingNull` on a nullable reference; `UsesFaceValuePercentageOfPar` is `WhenWritingDefault` on a `bool` whose semantic default *is* the CLR default. A `decimal` with semantic default `1m` matches neither, so every legacy record would gain `contractMultiplier: 1` and change its hash | §3 |

**Round 6 — three more:**

| Claim | Why it was wrong | Corrected in |
| --- | --- | --- |
| "Without a gate this ratio only moves one way — **and it has**" | Contradicted the correction printed 50 lines above it. The growth was an artifact of the broken scan; 29% matches the prior review. Sixth instance in this document of fixing a finding and leaving a dependent claim | Improvement #6 |
| "Make `Windows Desktop Build / desktop` a required check" | The required context uses the job's *display* name. `windows-desktop-build.yml:106-107` declares job `desktop` but renders it `verify-desktop (build/test WPF)`, so that string matches nothing — the recommendation would silently leave WPF non-blocking, which is the exact failure §7 is about | Improvement #6 |
| "Operators diverge for **up to a minute**" | Understated. `usePollingInterval` covers Trading, provider routing and Portfolio only (`use-workstation-data.ts:731-733`) — **not Accounting**. Break casework has no poll at all, so divergence is unbounded until manual refresh or remount | §8 — the finding is worse than reported |

**Round 7 — one, and it reversed two rows of the round-4 table:**

| Claim | Why it was wrong | Corrected in |
| --- | --- | --- |
| "Equity is not 1/100 — cash is intact"; "Sharpe is distorted nonlinearly" | Only *initial* cash is intact. `ApplyBuy` deducts the same unscaled `notional` it books into the position (`:701-715`), so the legs are paired. Worked arithmetic: equity is **exactly correct at entry** and **overstated on losses**; net P&L and total return are **exactly 1/100**; and daily returns carry a *uniform* 1/100 factor, so **Sharpe cancels it and is approximately correct** in a pure-option book — distorted only in a mixed one | §4 — rewritten from the arithmetic |

**Round 8 — three, two of them inside the round-7 fix** *(this block was omitted from the table
until round 12 caught it; the rows are restored here in the order they were raised):*

| Claim | Why it was wrong | Corrected in |
| --- | --- | --- |
| "Sharpe cancels the 1/100 factor and is approximately correct in a pure-option book" — round 7's own fix | Round 7 ran the arithmetic for a *single step from entry*, where P&L is zero and both books share a denominator, then generalized to a series. `RecordDayEnd` divides each change by the **previous** equity, so the denominators diverge as P&L accumulates: buggy is `cash + P&L`, correct is `cash + 100×P&L`. Over a six-mark series the per-day ratio drifts (0.010000, 0.009950, 0.010049, 0.009901, 0.010148) and `mean/stdDev` lands **17.9% below** correct. Preserved only while option P&L stays negligible against initial cash — not because the book is pure-option. Asserting rigour is not rigour | §4 — rewritten from a multi-step simulation |
| The headline called the two ledgers' role sets **disjoint** | The matrix four sections below it shows Admin, Developer and Accounting reaching both books. It is a persona lockout, not a partition | §1 headline |
| 29% counts routes referenced by wrappers nothing calls | Reachability measures **reference**, not operator reach: `getQualityCompleteness`, `getQualityGaps` and `getQualityAnomalies` have zero call sites outside `lib/api.ts` yet their routes count as reached. Round 8 framed the consequence as "29% is a lower bound" — which **round 10 then refuted**, since composed URL builders push the error the other way too | §6 caveat — twice |

**Round 9 — three from review, one self-detected:**

| Claim | Why it was wrong | Corrected in |
| --- | --- | --- |
| The run trial balance is a "simulation ledger"/"simulation book" | `StrategyRunReadService` serves backtest, paper **and live** history (`:14-16`); the Accounting run population applies no mode filter (`FundOperationsWorkspaceReadService.cs:796-812`); live runs persist as `BrokerLive`. A live selection shows a live subledger | §1(a), §1(c), headline |
| "The operator can see that a symbol is unhealthy and cannot open the evidence that says why" | The per-symbol drill-down *is* mounted in both clients (`data-screen.data-regions.tsx:236-359`; `DataQualityViewModel.ShowSymbolDrilldown:298-320`), projected from the composite payload. The real gap is history, distribution, ranking, comparison and reports | §6 — reframed |
| Windows 11 x64 is "the only platform ADR-019 supports" | ADR-019 is **Proposed (awaiting core-team sign-off)** and records that no support declaration exists; `PRD-000` blocks supported release until it is signed. Windows is the proposed envelope, not a ratified one | §7 heading and body |
| *(self-detected)* "31" quality drill-downs lack consumers (3 places) | Re-measuring under this document's own three-layer method gives **27**; the table in §6 already said 27, so the document contradicted itself. Self-detected while verifying the reframe above | §6, improvement #7, round-1 row |

The round-9 measurement is worth one more note, because the first two attempts at it were both
wrong and wrong in opposite directions. A scan that `re.escape`d route literals into `grep` turned
every braced route (`/api/quality/latency/{symbol}`) into a BRE interval and reported 7 dark; a
second scan that folded `Meridian.Ui.Shared` into the "client" corpus reported 0, because that
project is where the 385 `app.Map*` registrations live — it is the **server**. Counting a route as
reached because the server declares it would make every route reachable by construction. The
document's original scan excluded it correctly; only this round's scratch check did not. Third
attempt, restricted to the three genuine client layers, gives 27.

**Round 10 — two from review, one self-detected:**

| Claim | Why it was wrong | Corrected in |
| --- | --- | --- |
| "29% is a **lower bound** on the dark surface, not an estimate of it" | A bound requires error in one direction. Dead wrappers undercount the dark surface; composed URL builders and legitimately headless routes overcount it. The same paragraph opened "it cuts one way" and then conceded both directions three sentences later | §6 caveat — rewritten as an estimate |
| The three catalog tests "would have caught §1, **§5**, §6, and §9" | The NAV kernel has no route constant and no endpoint — `ShareClassUnitRegisterProjector` and `NavPerUnitCalculator` live only in `src/Meridian.Ledger/`, its README, tests and planning docs. Route- and role-based tests have nothing to inspect | Improvement #4 — §5 dropped, fourth invariant added |
| *(self-detected)* §1's improvement still proposed the disjoint-permission test | Round 4 refuted that predicate as existential, and improvement #4 says so explicitly — but §1's own remedy block was never updated, so the document recommended in one place the test it calls useless in another | §1 improvement |

The §5 correction is the more interesting of the two, because it marks a limit of this review's own
thesis rather than an error inside it. Cross-catalog testing detects catalogs that *disagree*. The
NAV kernel is absent from every catalog — no route, no permission, no screen — so there is nothing
for a cross-catalog invariant to compare, and the most complete built-and-unexposed capability in
the codebase is exactly the one the proposed gate cannot see. A capability that never entered a
catalog is invisible to catalog consistency by construction.

The self-detected one repeats the pattern this section exists to record: round 4 refuted the
existential test and the correction landed in improvement #4, while §1 — the section that first
proposed it — kept it for six more rounds. Correcting the summary and leaving the source is the same
failure as correcting the source and leaving the summary.

**Round 11 — one, and it caught a remedy that was right about the sites it named:**

| Claim | Why it was wrong | Corrected in |
| --- | --- | --- |
| The multiplier remedy named `ApplyBuy`/`ApplySellLong` and `MarketValue` | The short branches carry the identical defect and were omitted: `ApplyShortSell` computes `proceeds = qty * price` (`:822`), `ApplyCoverShort` derives `coverCost` and `realised` the same way (`:753`), and `PaperPosition.UnrealisedPnl` (`:1128`) sums unscaled into every account snapshot. Implementing the list as written would have left short options wrong | §3, improvement #3 |

Verifying it surfaced something the document had not said, and it is the sharpest consequence in §3.
On the short side the unscaled figure sizes **Reg-T collateral** — `MarginBorrowed -= proceeds *
ShortInitialRate` (`:829-834`) — so a short option book posts 1/100 of the collateral it owes and
reads as fully margined while naked. Both short branches also post to the **double-entry ledger** at
the unscaled figure (`:847-851`, `:892-910`), so the defect is not confined to the in-memory book.
An under-collateralized short option book is a materially worse failure than a mis-stated P&L, and
four reviews of this defect — including the three earlier rounds of this one — traced only the long
path and never reached it.

The lesson is narrower than the earlier ones and worth keeping separate: **an enumerated remedy
inherits the blind spots of the trace that produced it.** §3 was built by following a long buy from
`ApplyFillToAccount` to `AttributeFill`, so the remedy listed the sites that trace passed through.
Every correction since has sharpened the *description* of the defect while leaving the *site list*
exactly as the original walk left it.

**Round 12 — four, one of them about this table:**

| Claim | Why it was wrong | Corrected in |
| --- | --- | --- |
| **Round 8 was missing from this section entirely** | The table jumped from round 7 to round 9, so the visible headings accounted for 33 of the 36 challenged claims and the totals did not reconcile. Worse, it left round 7's *refuted* pure-option Sharpe explanation standing as the last recorded word on §4 — the audit trail of a document about audit trails, with a round missing | Restored above |
| The compliance surface is "a complete, permission-guarded governance capability" | It has **no discovery contract for approvals**: no GET for approval requests, and `IComplianceApprovalStore` exposes only `CreateRequest`, `RecordDecision` and single-id `Resolve` with no enumeration (`ComplianceApprovalStore.cs:12-27`). An approver cannot find what is pending. Activation is not "persistence plus UI" | §6, improvement #7 |
| The approval snapshot is "rewritten in place" | `Persist` hands the serialized snapshot to `AtomicFileWriter.Write` (`:247-250`) — an atomic replace. The mutability objection stands; the implied torn-write exposure does not. **The same `AtomicFileWriter` misreading this document already corrected once in round 1**, about the fund-structure loader | §6 |
| "Grant `FundAccountant` and `Controller` the permissions their screens require" | Read literally against the current gate that means `ViewStrategies`, which covers eleven endpoint files — covered calls, Lean configuration and algorithms, QuantLab, promotions, lifecycle, strategy-designer drafts, run fills and attribution. It is also self-defeating: the same improvement moves the panel out of Accounting, after which accountants need no strategy grant at all | Improvement #1 |

Two of these four are repeats of classes this document had already corrected elsewhere — the
`AtomicFileWriter` misreading from round 1, and a remedy phrased loosely enough to authorize the
opposite of what it intends. The missing round 8 is the one that should worry a reader most, because
it is not a claim about the codebase at all: the section whose entire purpose is to make the
document's own error history reproducible had a round-shaped hole in it for four rounds, and the
totals in its header did not reconcile against the rows beneath. A ledger that does not foot is the
defect this review opens by describing.

**Round 13 — five, including one defect the review itself had not found:**

| Claim | Why it was wrong | Corrected in |
| --- | --- | --- |
| The multiplier remedy still omitted the margin models | `RegTMarginModel:93,135` and `PortfolioMarginModel:55-57,93-97` compute notional, maintenance and stressed legs from `position.Quantity * price`, never reading the `ContractMultiplier` that `ToExecutionPosition` hands them. Margin requirements and excess-liquidity checks stay at 1/100 after every transaction branch is fixed — a *second* margin path, distinct from the short-sale collateral found in round 11 | Improvement #3 |
| "Governed period reopen is dark… an operator has no path to reopen" | The mounted `/accounting/operations-continuity` screen renders a governed reopen command and submits through `reopenOperationsContinuityWorkflow` (`:1464-1504,2263-2366`), with tests. The `LedgerCloseManagementPeriodReopen` route is unconsumed, but the capability is not dark. Prioritizing a reopen workflow would have duplicated one that exists | §9, improvement #9 |
| The re-test table bundled "WPF state un-fork / desktop test job" as one **Open** item | The state half is **partial**: reconciliation posture no longer reads desktop-local state and the local fund lane carries a provenance badge (`AccountingFeatureModule.cs:53-59`), and the scheduler host loops now run server-side (`:196-202`). Bundling hid landed remediation and meant neither half was re-tested | Re-test table — split in two |
| The docs index stated 250/862 as settled fact | §6 had been corrected to call 29% an estimate with unbounded error in both directions while the stakeholder-facing index still presented it flatly. The same dependent-text failure this section exists to record, on the surface the round-10 sweep had checked for the phrase "lower bound" and not for the unqualified number | `docs/product/README.md` |
| The addendum recorded the run-scoped panel as "still wired, unchanged" | PR #2824 *did* change it, and introduced a defect this review had not found: the panel is now labelled "Simulation artifact" and "Strategy run (simulation)" (`accounting-screen.tsx:2899-2904`), while §1 establishes the population carries no run-mode filter and includes `BrokerLive` runs. A live operator is now explicitly told they are looking at a simulation | Addendum |

The last of those is the one worth reading twice, because the causal chain runs through this review.
Round 9 established that the run ledger is not simulation-only and made this document stop calling
it one. PR #2824, remediating §1(b), went the other way and stamped "simulation" onto the panel in
two places. The label is wrong for exactly the runs §1 identified, and it is worse than the ambiguity
it replaced: an unlabelled panel invites the question "which book is this?", while a confidently
wrong label forecloses it. A reviewer who only re-checked the claims it had already made would have
recorded the panel as unchanged and missed it — which is what this document did until round 13.

**Round 14 — one, and it is the only round where a remedy would have broken working code:**

| Claim | Why it was wrong | Corrected in |
| --- | --- | --- |
| "Nothing downstream multiplies by `ContractMultiplier`" | One path does. `ToExecutionPosition` preserves it (`:1324`), `AggregatePortfolioService.SplitByOwner` carries it into each contribution, and `AggregatePortfolioExposureProvider` computes `unitPrice * contribution.ContractMultiplier` (`:571-582`) under a comment making this section's own argument. **Aggregate pre-trade exposure is already correct**, so a remediation applied indiscriminately would scale it twice | §3, improvement #3, and the method lesson that rested on it |

Every previous round corrected a claim. This one corrects a *remedy that would have introduced a
defect* — scaling a path that is already right, overstating pre-trade exposure by the multiplier.
That is a different and more serious category, and it is the second time this same provider has
caught the same over-generalization: round 5 ruled out multiplying stored cost basis *because*
`AggregatePortfolioExposureProvider` already multiplies it. That correction landed in the cost-basis
caveat and never propagated to the sweeping "nothing multiplies by it" sentence three paragraphs
above — so the document held the specific fact and the contradicting generalization simultaneously
for nine rounds.

It also sharpens the method lesson rather than weakening it. "A value carried but never consumed
reads as handled" was the wrong formulation; the true one is worse. **A value consumed in exactly one
place and ignored everywhere else reads as handled everywhere**, because the first grep finds it
being multiplied and the search stops. That single correct consumer is what let this defect survive
four consecutive reviews.

**Round 15 — three, one of which reverses a correction from round 13:**

| Claim | Why it was wrong | Corrected in |
| --- | --- | --- |
| §7's remedy still said "Add `verify-desktop` to the `quality-gate` `needs` list" | Round 3 established that `needs:` cannot cross workflow boundaries, and improvement #6 says so correctly — but §7's own remedy block was never updated. **Second occurrence of the identical structural failure**: a refuted remedy surviving in the section that first proposed it while the summary carries the fix (the first was §1's disjoint-permission test, caught in round 12) | §7 |
| "The capability is not dark — an operator *can* reopen" (round 13's own correction) | The wired command calls `ReopenWorkflowAsync` **directly** (`WorkstationEndpoints.cs:1346-1354`), transitioning workflow state only. The ledger path (`AccountingCloseManagementService.cs:1295-1325`) runs `ReopenAndQueueClosingReversalsAsync` under the posting and consistency gates first. The UI can report a reopened workflow while the period and its closing entries stay locked | §9, improvement #9 — original finding substantially restored |
| The addendum certified PR #2824's posted-ledger wiring as "the right fix, done the right way" | It is not fund-scoped. The client never sends `fundProfileId`, the periods route checks only `HasLedgerReadPermission`, the trial-balance and P&L routes carry a global `ManageDirectLending`, and `LedgerEndpoints.cs` makes **zero** `HasScopedPermissionAsync` calls. Any holder of that flag reads every fund's posted book | Addendum |

The middle row is the one that costs this review most. Round 13 said the reopen capability existed;
I verified the client call site, found `reopenOperationsContinuityWorkflow` wired with tests, and
accepted it — **without tracing what the endpoint does**. It transitions a workflow and never touches
the ledger. That is precisely the failure this document named as its own method lesson in round 7:
*inference from one side of a seam is not evidence.* Round 7 committed it while making a claim; round
13 committed it while **accepting a correction**, which is the harder case to notice, because a
correction arrives with the authority of having caught you once already. Verifying a refutation
deserves the same standard as verifying an assertion.

The third row matters for a different reason: it is the second defect found in PR #2824's
remediation, after the simulation mislabel in round 13. A fix landing mid-review has had two
problems in two rounds, both found by re-examining the fix rather than the original finding.

**Round 16 — one:**

| Claim | Why it was wrong | Corrected in |
| --- | --- | --- |
| "The contract still admits an un-sourced print, so the class of defect can be reintroduced by any new adapter" | The runtime is fail-closed at the shared ingress seam: `TradeDataCollector.OnTrade` rejects a missing `Source`, publishes an `IntegrityEvent.MissingSource`, and returns **before the trade is created or stored** (`:117-134`), with a dedicated test. The nullable contract is real, but it is compile-time hardening of a control that already holds at run time — not an open governed gap | §9, re-test table |

This one is worth noting for what it says about how the finding was originally framed. The type
signature was read, the enforcement was not, and the gap between them was filled with the plausible
inference that a nullable field means an unchecked field. That is the same one-sided reading the
round-7 lesson names, applied to a type declaration instead of a code path — and it produced a
finding that was directionally reasonable and materially wrong about the risk.

**Round 17 — three, and every one is this document's own stale dependent text:**

| Claim | Why it was wrong | Corrected in |
| --- | --- | --- |
| The docs index still said the multiplier "never reaches portfolio economics" | Round 14 established that `AggregatePortfolioExposureProvider` already scales aggregate pre-trade exposure. The round-14 sweep corrected §3, improvement #3 and the method lesson — and missed the index | `docs/product/README.md` |
| The round-7 narrative still concluded Sharpe is "approximately *unaffected*" | Round 8 refuted exactly that with a six-mark series showing 17.9% error. The corrections *table* recorded the refutation; the corrections *prose* two screens below kept the refuted conclusion as a closing takeaway | Round-7 lesson paragraph |
| The addendum still referred implementers to "the disjoint-permission structural test" | Improvement #4 rejects that predicate as existential, and round 12 replaced it with the role-to-surface expectation table. The addendum was never updated | Addendum |
| *(self-detected)* The re-test table carried the same categorical multiplier claim | Found by sweeping for the conclusion rather than the phrasing, in the same commit | Re-test table |
| *(self-detected)* §3's own bolded lead sentence said the multiplier "never reaches portfolio economics on any path" | Round 14 added its qualifying paragraph directly beneath this sentence and never corrected the sentence itself | §3 lead |

Three rounds ago the reviewer was finding defects in the codebase. This round it found none — all
three items are places where a correction landed in one part of this document and a dependent
sentence elsewhere was left standing. That is the failure this section exists to record, and it has
now happened often enough to be the document's most reliable defect: **corrections propagate to the
text being corrected and to the summary, and stop there.** The cross-surface grep adopted in round 8
catches restated *phrases*; it does not catch a *conclusion* restated in different words two sections
away, which is what all three of these are.

Applying that lesson in the same commit immediately found **two more instances the reviewer had not
flagged**: the re-test table, and §3's own bolded lead sentence — *"the multiplier never reaches
portfolio economics on any path"* — which round 14 qualified in the very next paragraph without
correcting the categorical claim three lines above it. A sweep for the *conclusion* rather than the
phrasing is what surfaced them; both are fixed here.

The useful signal is what it implies about convergence: the review's claims about the code are
holding, and the remaining churn is internal consistency in a document that has been rewritten
seventeen times.

**Round 18 — four, including one that corrects this review's evidence rather than its conclusion:**

| Claim | Why it was wrong | Corrected in |
| --- | --- | --- |
| "`Meridian.Wpf` compiles to an empty stub in the gate" | The required lane never compiles it at all. `scripts/ci.sh` restores the full solution (`:135`) but builds only `dotnet build Meridian.WebWorkstation.slnf` (`:156`), and that filter contains one project — `src/Meridian/Meridian.csproj`. Only its reference closure is built, and a WPF app is not in a web host's closure; `run-dotnet-ci-tests.py:29` then drops the desktop test suite explicitly. The `EnableDefaultCompileItems` guard makes the Windows-targeted *restore* work off-Windows; it is not the CI mechanism | §7 — evidence replaced. The widening this round added was itself withdrawn in round 20 |
| "Break casework, approvals, and close-readiness do not refresh after a mutation elsewhere" | Approvals poll: `useGovernedApprovalsViewModel` installs a `setInterval` at `DEFAULT_APPROVAL_REFRESH_MS = 15_000` (`:47,101-112`), so they are stale but **bounded**. Only break casework is unbounded, and close-readiness is not established either way by the evidence gathered here | §8 — narrowed to the case that holds |
| The §3 remedy's two steps fix only scale | The same three replay sites also omit `ownerAccountId`, so `AttributeFill` receives null and the restored book stays unattributed even after the multiplier is persisted and consumed. Round 4 established the gap; the remedy was never updated to close it | §3 — third step added |
| The corrections total still did not foot | The header counted 53 raised + 2 self-detected, while round 17's prose described two further self-detected fixes that appeared in no row and no total. Recorded as rows and counted | This header, round-17 block |

The first row is the one that matters, because it is the first time in eighteen rounds that the
*evidence* under a finding was wrong while the finding itself held. §7 said WPF compiles to a stub
because the csproj disables default compile items off Windows. The csproj guard is real, but it is
not why the gate misses WPF — the gate misses WPF because it builds a one-project solution filter.
The corrected mechanism makes the finding **wider**: the required lane does not compile most of the
solution, not merely the desktop lane.

The last row is the second time the audit ledger has failed to foot, and the cause is a variant of
the first: round 12 found a missing round, and this time the missing entries were corrections
described in *prose* but never entered as *rows*. Recording a fix in narrative form is not recording
it in the ledger, which is a distinction a document about evidence discipline should not have needed
twice.

**Round 19 — two, both places where the remedy would regress working code:**

| Claim | Why it was wrong | Corrected in |
| --- | --- | --- |
| "The multiplier reaches **exactly one** downstream consumer" | There are two. `WorkstationEndpoints.BuildExposureReport` multiplies quantity × cost basis × `ContractMultiplier` independently (`PortfolioAggregation.cs:96-123`), and its `/api/portfolio/exposure` output is operator-facing — the WPF `AggregatePortfolioViewModel` consumes it (`:246`). Both must be in the do-not-double-scale inventory | §3, improvement #3, re-test table |
| The remedy said only "give `ContractMultiplier` a persisted field" | Doing that alone **breaks every existing durable session**. `PaperSessionFillRecord.Validate` recomputes the canonical hash from the re-serialized report and throws `InvalidDataException` on mismatch (`IPaperSessionStore.cs:149-175`); `ExecutionJsonContext` ignores only nulls (`:18`), so a legacy record defaulting to `1m` serializes with `contractMultiplier: 1` and no longer matches its stored hash. Round 5 noticed the hash problem while rejecting a different serialization pattern, but the implementation instruction was never updated | §3 remedy — versioned hash / migration now required |

The consumer count has now been wrong twice in the same direction: *none* → *one* → *two*. Both
corrections came from someone finding a consumer, not from anyone enumerating them, which is exactly
why the number kept moving — and it is a warning about the shape of the remedy rather than about
this document alone. A "scale everything downstream" instruction is only as safe as the list of
things already scaled, and that list has been under-counted at every revision.

The second row is the same failure in a different register: round 5 established that adding this
field changes the canonical hash of every legacy record, recorded it in the corrections table, and
left the implementation instruction saying "add the field". A hazard noted in the audit trail but
absent from the instruction is a hazard an implementer will hit.

**Round 20 — one, correcting the correction from round 18:**

| Claim | Why it was wrong | Corrected in |
| --- | --- | --- |
| "The merge gate compiles one project's closure out of the solution" (round 18's own widening) | `scripts/ci.sh:159` invokes `run-dotnet-ci-tests.py`, whose `main` calls `run_builds` and issues a real `dotnet build` for every default test project (`:324-343,430`). `tests/Meridian.Tests/Meridian.Tests.csproj` alone has **30 `ProjectReference`s** against 38 projects under `src/`, so most of the solution is compiled through the test closures. Only `Meridian.Wpf` and the three `WINDOWS_ONLY_TEST_PROJECTS` are absent | §7 — mechanism corrected, widening withdrawn |

Three versions of one mechanism, and the failure mode is identical each time. The original said WPF
"compiles to an empty stub" — plausible, because `Meridian.Wpf.csproj` really does disable default
compile items off Windows, and `run-dotnet-ci-tests.py`'s own comment really does describe the *test*
project that way. Round 18 corrected the mechanism and then over-generalized from the `.slnf`, in the
same breath. Round 20 removes the over-generalization.

The detail worth keeping is that I had already read `run-dotnet-ci-tests.py` twice — in round 16 for
the provenance test, and in round 18 to cite line 29's exclusion list — and never asked what the file
*builds*, only what it *skips*. Reading a file for the fact you came to find is not reading it. The
finding itself has survived all three versions unchanged: **WPF is not compiled on the lane that
gates the merge.**

**Round 21 — three, all about the proposed gates, and one rewrites §1's mechanism:**

| Claim | Why it was wrong | Corrected in |
| --- | --- | --- |
| `FundAccountant` and `Controller` receive a **403** on the run-scoped trial balance | They never issue the request. `ResolveAccountingWorkspaceReadScope` sets `StrategyRuns` only for `ViewStrategies\|ManageStrategies` (`:255-258`), and the payload substitutes `Array.Empty<WorkstationAccountingRunRecord>()` for `ReconciliationQueue` when that scope is false (`:132-137`). The browser derives selection from that queue, finds nothing, and returns before calling `getRunTrialBalance`. The operator sees an empty queue indistinguishable from "no runs" | §1 table and a new mechanism paragraph |
| Test (b): "every route constant must be **referenced** by a client" | Reference is the false-positive this document already demonstrated: the three dead quality wrappers are referenced by `lib/api.ts` and called by nothing. §6's caveat says the real gate needs transitive reachability from mounted routes; improvement #4's spec never said so, and as written would certify part of the dark surface it exists to catch | Improvement #4 (b) and (c) |
| The capability gate reads "the roadmap register's **shipped** domain capabilities" | `W9-NAV-006` is `ready_for_acceptance` with `evidence_posture: implementation_complete`, not `done`, and the roadmap reserves `done` until operator acceptance. A shipped-only gate stays green on the NAV kernel until after the acceptance it exists to inform — the one capability it was designed for | Improvement #4's fourth invariant |

The first row is the most consequential correction in the last ten rounds, because §1 is the
headline and the 403 has been in its table since the first draft — through twenty rounds that
scrutinised the permission matrix repeatedly and never asked what the endpoint *returns* to a role
that fails the scope check. The answer is worse than a denial: the queue is withheld and replaced
with an empty array, which the UI renders as "nothing to reconcile". An accountant is not told they
lack access; they are told their fund is clean. That is §8's "no breaks means request failed" defect
appearing at the centre of §1, and it went unnoticed because the whole section was framed around
authorization rather than projection.

The other two share a shape worth naming: **all three proposed gates were specified loosely enough
to pass on the exact defects they were written for.** Test (b) accepts a reference from a dead
wrapper; the capability check reads a status the target item does not have; and test (a) checked
permissions when the failure is in the payload. A gate that would not have caught the finding that
motivated it is not a gate, and this document has now produced three of them.

The core findings survive, several in sharper form. Four were materially wrong as first stated — the
role-access table, the fixed-income claim, the multiplier's blast radius, and two of the proposed
remedies — and are rewritten rather than softened; the multiplier correction made the defect
*larger* and the original remedy insufficient, and both the catalog test and the CI `needs` change
were unimplementable as specified. Three method lessons generalize. **A permission gate read in isolation predicts the wrong
access** — the same intersection error this document accuses the codebase of, committed while
describing it. And **a value consumed in one place and ignored in the rest reads as handled everywhere**:
`ContractMultiplier` is threaded through three layers, applied correctly by
`AggregatePortfolioExposureProvider`, and ignored by every transaction, valuation and margin path —
which is why four consecutive reviews, this one included, mistook partial plumbing for correctness.
The single correct consumer is what makes the defect so durable: anyone who greps for the identifier
finds it being multiplied and stops looking. And third: **correcting one section without
re-reading the sections that depend on it introduces fresh contradictions** — round 2's narrowing of
§4 was refuted by §3, which the same commit had just rewritten; round 3's replacement then
overcorrected past the evidence, and round 4 had to pull it back. Seven of the nine findings in
rounds 3 and 4 are damage from earlier corrections, not defects in the original draft.

The §4 blast radius alone was mis-stated three times — too narrow, too broad, then finally
enumerated per metric. That is the same failure the document diagnoses in the codebase: each fix was
correct about the line it touched and wrong about the line next to it. Writing about a defect class
turns out to be no protection against committing it.

Round 5 adds a fourth lesson, and it is the one that should outlive this document. **The headline
statistic was wrong by half because the measurement enumerated two client layers out of three.** The
review's whole thesis is that a system fails when nobody checks one catalog against another — and its
central number was produced by a scan that silently omitted a catalog. It reported a *worsening*
trend (29% → 43%) that never existed. An unvalidated measurement does not merely mis-size a problem;
it invents one, and then everything built on it — the group table, the CI gate proposal, the
"cheapest uplift" ranking — inherits the error. Any structural test proposed in improvement #4 must
therefore enumerate its own inputs explicitly and fail when a client layer is added that it does not
know about.

Round 6 supplied the sharpest illustration of the whole pattern. This document recommended making a
Windows check required by a context string that does not exist — because the workflow overrides its
job's display name. A review whose central finding is *"the catalogs disagree and no gate checks
them"* proposed a gate configured against the wrong catalog entry. The recommendation would have
looked applied and changed nothing, which is precisely the built-but-dead failure the review exists
to name.

Round 7 closed the §4 loop and is worth stating as the fifth lesson. Every earlier version of that
table reasoned about the position leg and *inferred* the cash leg. Reading `ApplyBuy` to the end —
`account.Cash -= notional` — and then running the numbers produced a materially different and partly
opposite answer: equity is right at entry and overstated on losses, and P&L is exactly 1/100.
Round 7 also concluded that Sharpe was approximately unaffected, and **round 8 refuted that**:
`RecordDayEnd` divides by the *previous* equity, the two books' denominators diverge as P&L
accumulates, and over a six-mark series `mean/stdDev` lands 17.9% below correct even in a pure-option
book. The settled position is §4's — Sharpe is distorted by a drifting amount, which is worse than a
clean factor because it still looks plausible. **The metrics an operator would use to sanity-check an
options book are precisely the ones this defect leaves looking plausible.** Five wrong versions of
one table, each confidently reasoned, only the last of them arithmetic over a series — inference from
one side of a seam is not evidence, which is the thesis of this entire document applied to its own
author.

## Addendum — remediation landed while this review was in flight

`main` moved to `054e2d27` after this document was written, merging PR #2824 ("Point the Accounting
trial balance at the posted journal"). That branch is merged into this one, so the findings above are
still anchored at `e232ece1` but the code beside them has moved. What actually changed, verified:

**Genuinely fixed — §1(b) only.** §1(a) is the Accounting screen's run-scoped
`getRunTrialBalance` binding, and that is still mounted and unchanged (see below), so the two-book
ambiguity is *not* remediated. What did land: a new `src/Meridian.Ui/dashboard/src/lib/ledger-reports-api.ts`
calls `/api/ledger/periods`, `…/{periodId}/trial-balance`, and `…/{periodId}/pnl-summary`, and a new
`AccountingPostedLedgerSection` (`accounting-screen.posted-ledger-panel.tsx`, mounted at
`accounting-screen.tsx:2894`) renders them. The posted journal's trial balance and P&L now reach an
operator for the first time. That is the right endpoint, reached the right way — **but it is not yet
scoped to the funds a role is assigned.**

`AccountingPostedLedgerSection` opens with `getLedgerPeriods()` and the client signature admits only
`{ ledgerBookId?, status? }` (`ledger-reports-api.ts:19`) — no `fundProfileId`, though the endpoint
accepts one (`LedgerEndpoints.cs:154`). Server-side the periods route checks only
`HasLedgerReadPermission(context)` (`:161`), and the trial-balance and P&L routes carry
`RequireAnyPermission(AdminMaintenance, ManageDirectLending)` (`:386,411,436`) — a **global flag with
no fund dimension**. `LedgerEndpoints.cs` contains **zero** calls to
`EndpointAuthorization.HasScopedPermissionAsync`, the mechanism `FundStructureEndpoints`,
`ExecutionEndpoints` and `WorkstationEndpoints.StatementReconciliationReport` use for exactly this.

So any holder of `ManageDirectLending` can enumerate and open **every fund's posted ledger**, not
just the funds they are assigned. That compounds §2 rather than sitting beside it: the grant is
already overloaded, and the new panel makes it a cross-fund read of the book of record. A
`FundAccountant` "owning fund-accounting evidence for assigned funds" is precisely the persona this
should bind, and it does not. Certifying this wiring as complete needs an assigned-fund selector on
the client and scoped authorization on the routes.

**The class survived, three ways** — the pattern this review's headline describes, one cycle later:

- **The run-scoped panel is still wired, and its new labelling introduced a fresh defect.**
  `accounting-screen.view-model.ts:2940` still reads
  `getTrialBalance: (runId) => getRunTrialBalance(runId)` — the exact line §1 cites — still gated on
  `ViewStrategies`. The Accounting screen now carries *two* trial balances over two different books,
  and the accounting roles still get an empty reconciliation queue in place of one of them.

  The panel was not left untouched, though, and the change made one thing worse. PR #2824 retitled
  it "Strategy Run Ledger Explorer" and added two labels that are **factually wrong for live runs**:
  the description reads "Simulation artifact: this explorer reads the selected
  strategy/reconciliation run's ledger…" and a metadata row reads "Strategy run (simulation) — not
  the posted journal" (`accounting-screen.tsx:2899-2904`). §1 establishes that the selected
  population carries no run-mode filter and includes `BrokerLive` runs, so **an operator inspecting a
  live run is now explicitly told it is a simulation.** The relabelling was meant to disambiguate the
  two books and it does disambiguate *which ledger*, but it asserts a run mode the panel does not
  check. Correct labelling has to read the run's actual mode, not assume one. Ranked against the
  original ambiguity this is arguably worse: an unlabelled panel invites a question, a confidently
  wrong label prevents it.
- **A second screen was not touched.** `finance-standard-pages-screen.tsx:299` still calls
  `getRunTrialBalance`, so the run-scoped view remains an operator-facing "trial balance" in a
  second place.
- **§2 is untouched, and the fix now depends on it.** The posted-journal endpoints still gate on
  `AdminMaintenance | ManageDirectLending` (`LedgerEndpoints.cs:386,411,436`), and
  `FundAccountant`/`Controller` still lack `ViewStrategies`. The new panel is reachable by those
  roles *only because* `ManageDirectLending` is the overloaded fund-accounting grant §2 names. The
  remediation is load-bearing on the defect.

So §1(c), §2, and improvement #4's catalog gate all remain open, and the "two books, one screen
name" ambiguity is now more visible rather than less. On that gate: an earlier draft of this line
pointed at the *disjoint-permission* structural test, which improvement #4 rejects as an existential
predicate this very defect satisfies — Admin, Developer and Accounting all pass it while
`FundAccountant` and `Controller` stay locked out. The test that replaced it is the **declared
role-to-surface expectation table**, asserted against the workspace ∩ leaf intersection. Retiring the run-scoped
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
