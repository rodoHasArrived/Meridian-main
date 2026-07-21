# Adversarial Program Review — Meridian (2026-07-21)

**Status:** point-in-time assessment
**Owner:** core-team
**Reviewed:** 2026-07-21
**Scope:** High-level program functionality and end-user value, across onboarding,
browser workstation, ingestion/providers/reconciliation, accounting/ledger/reporting,
and trading/execution/backtesting.

> This is a dated, evidence-backed adversarial review. It is source material for
> prioritization, **not** canonical guidance. Every claim is anchored to a file/line
> that was accurate at the reviewed commit; revalidate against the current checkout
> before acting. Live status belongs to the roadmap registry (`docs/roadmap/data/*.yml`).

---

## Verdict first (so the criticism is calibrated)

**Meridian is a seriously engineered platform, not a demo.** The evidence is unambiguous:
~12,100 tests across ~1,250 files, real Postgres + WAL + Parquet storage with SQL migrations
and atomic tax-lot journaling, ~a dozen genuine market-data adapters, a production-grade
order-management state machine, a real event-driven backtester with three fill models and
honest bias disclosure, and a fail-closed reporting-certification chain with true
segregation-of-duties. The codebase is unusually disciplined — near-zero `TODO`/`NotImplemented`
markers, and its dev-only fixtures loudly announce themselves rather than masquerading as
production data (`src/Meridian.Ui/dashboard/src/lib/api.ts:766-787`, gated by `import.meta.env.DEV`).

The adversarial finding is therefore sharper than "it doesn't work":

> **The strongest engines are unwired, under-fed, or hidden — and the gaps line up almost
> exactly with the product's headline promises.** "Prove the number," "use sample data,"
> "brokers integrated," "Backtesting Studio," and the flagship instrument-level proof
> drill-through are each *the specific place* where the last-mile wiring is thinnest.

That is the good-news version of the problem: the expensive part (the engines) is built. The
value is trapped one integration step away from the user.

---

## The one theme that explains most findings

Meridian is **built breadth-first**: 47 projects, ~188 workstation endpoints, ~249 UI screen
modules spanning trading, execution, backtesting, fund ops, accounting, treasury, direct
lending, and reporting. In several verticals the team built a *correct, general engine* and
then wired the UI to a *narrow or placeholder shim* in front of it. The engine passes its tests;
the user hits the shim. Fixing the shims is far cheaper than what has already been paid for — and
it is where nearly all the user-facing value now sits.

---

## Tier 1 — Promises that currently render nothing or wrong (credibility-critical)

| # | Area | The gap (evidence) | Why it matters |
|---|------|--------------------|----------------|
| **1** | **Onboarding "Use sample data"** | The wizard's *recommended* path writes `sample-pack.json` (Northstar portfolio, holdings, `SAMPLE-BRK` breaks) but **nothing ever reads it back** — `FirstRunExperienceService.cs:144-160` is a write-only path with no consumer in the repo. The believable populated UI is DEV-only fixtures; a test asserts production returns empty (`accounting-screen.view-model.test.ts:1996-2006`). | A low-patience finance-ops user completes onboarding and lands on **empty screens**. The headline promise ("see a portfolio, statement, breaks, report immediately") delivers a badge, not data. Highest-leverage fix. |
| **2** | **Reconciliation ("prove the number")** | The engine wired to the operator UI (`StatementMatchingService`, via `StatementRunWorkflowService.cs:22-23`) is a **single-sided magnitude check with no counterparty**, hardcoded confidences, and **inverted cash/txn semantics** — a $10M line is a "break," a $0.001 line is "matched" (`BrokerStatementInfrastructure.cs:488-513`). The two *real* engines (`StatementMatchingEngine`, `ReconciliationMatchingEngine`) exist but are fed an empty internal book / have **zero production source adapters** (only test doubles). | This is the literal product wedge. The number is **not** currently proven against an internal ledger anywhere the user can see. |
| **3** | **Instrument-level proof drill-through** | The flagship "trace this security's number → source event → approval → posted journal" graph is fully built and cross-validated — but **gated to one hardcoded model**: `FinancialRecordExplorerReadService.InstrumentJournalProof.cs:30` bails unless `ModelKey == "mbs-factor-paydown"`. The underlying `AssetAccountingEventSpine` is model-agnostic. | The demo-able "prove the number" experience is a single MBS slice. Driving it from the spine's registered models turns a demo into a general capability. |
| **4** | **Live/paper fills (Alpaca)** | Correctness bug: `AlpacaBrokerageGateway` **never subscribes to Alpaca's `trade_updates` WebSocket** (`:285-292`); it only re-emits synchronous submit acks. Any fill arriving *after* the ack is missed by the OMS fill funnel (`OrderManagementSystem.cs:970,1124`) unless a separate reconciliation service is run. | Portfolio and accounting state can silently drift from the broker — the worst class of bug for a system whose promise is trustworthy numbers. |

---

## Tier 2 — Surfaces that show numbers that aren't real (trust erosion)

- **PlotTool serves fabricated analytics.** Strategy Lab's regression panel returns **hardcoded**
  `β 0.48 / R² 0.71 / ρ 0.84` and `observationCount = 2184 + runs*9` while labeling itself
  *"API-backed"* (`WorkstationEndpoints.PlotTool.cs`; frontend selector
  `strategy-screen.view-model.ts:1755-1769`). Badge it "Sample" or compute it for real.
- **Provider health metrics silently synthesized.** When live monitoring is down,
  `ProviderEndpoints.cs:511` returns config-derived throughput/latency/quality with no "not live"
  signal. Operators gate go/no-go on this.
- **Family Office = permanent dead-end.** `app.tsx:579` mounts the screen with no `entityStructure`
  prop; it always renders "not connected" (`family-office-screen.tsx:80`). The guardrail that should
  hide unwired routes was **emptied to `new Set()`** (`workspace.ts:113`). Re-add the route to the
  unwired set or wire the read model — and restore the guardrail as a test.
- **"Adjusted" bars that aren't.** `BaseHistoricalDataProvider.GetAdjustedDailyBarsAsync:594-604`
  default just relabels raw bars; Stooq/TwelveData/Fred return **unadjusted prices labeled adjusted** —
  a corporate-action trap that corrupts backtests and NAV marks.
- **Holiday-blind gap detection.** `DataGapAnalyzer.GenerateTradingDays:459-476` excludes only
  weekends, so every market holiday becomes a "Critical missing" gap and triggers wasted provider
  calls (`GapType.Holiday/Weekend` are defined but never produced). Needs a real trading calendar.
- **Two ungoverned lanes that can emit non-reconciled numbers.** The in-memory `Ledger` has no
  chart-of-accounts enforcement and **case-sensitive** account identity ("Cash" ≠ "cash" → phantom
  accounts, `LedgerAccount.cs:22-28`); and legacy `ReportGenerationService.GenerateAsync:36-80`
  **ignores `ReportKind`** and skips the reconciliation gate. Both self-flag as non-authoritative,
  but sit behind ergonomic APIs. Fence them off or route through the governed path.

---

## Tier 3 — Strong assets that are hidden from users (value unlock)

- **Generalize no-code backtesting beyond the one vertical.** The event-driven `BacktestEngine`
  (fill models, walk-forward, XIRR/TCA, bias disclosure) is genuinely strong and *is* already
  reachable no-code for one vertical: `/strategy/covered-call` → `CoveredCallScreen` (`app.tsx:615`)
  → `POST /api/strategies/covered-call/runs` (`CoveredCallEndpoints.cs:25`) →
  `CoveredCallBacktestService` → `BacktestEngine.RunAsync` (`CoveredCallBacktestService.cs:525`), with
  async run/status/result/cancel. But that is a bespoke covered-call flow; there is **no *general*
  config-driven backtest screen** for arbitrary strategies/symbols (most still require QuantScript),
  and the named "Backtesting Studio" engine (`MeridianNativeBacktestStudioEngine`,
  `BacktestStudioRunOrchestrator`) remains **orphaned** — wired to no endpoint/screen. Generalize the
  covered-call pattern into a first-class backtest screen (reusing the existing `EquityCurve.tsx`,
  `DrawdownChart.tsx`, `bias-disclosure-panel.tsx`) or wire the Studio engine.
- **Connect the strategy templates to executable strategies.** The browser Strategy Designer
  *already* ships a one-click starter gallery — 7 `STRATEGY_BUILDER_TEMPLATES`
  (`strategy-designer-screen.view-model.ts:711`: equity-momentum-breakout, investment-grade-income,
  options-payoff, state-machine, concurrent-branch, structured-universe, trade-intent) rendered with
  a "Load" action wired to `loadStrategyBuilderTemplate` — so the "no starters" barrier is largely
  solved for the *visual builder*. The narrower real gap is that the concrete *executable/backtestable*
  strategies (MA-crossover, buy-and-hold in `Strategies/Live`; covered-call, carry in
  `Backtesting.Sdk`) aren't surfaced as one-click *runnable* starters, and the C#
  `BacktestStrategyBase` base is an empty stub — so authored templates don't yet map to real
  executable strategies a non-coder can run.
- **Ship a real sample statement CSV + in-app "Load sample" button.** The most credible first-value
  path (statement → break) currently makes the user *source their own file* — the only statement CSVs
  live under `tests/`. The CLI even advertises a `./statements/sample.csv` that doesn't exist
  (`HelpCommand.cs:150`).
- **Be honest about provider/broker availability.** Two brokers trade out of the box:
  **Alpaca** (REST) and **Robinhood** (registered under `"robinhood"` in
  `HostedBrokerageGatewayServiceCollectionExtensions.cs:46-52`; `RobinhoodBrokerageGateway.SubmitOrderAsync`
  POSTs equity `/orders/` and `/options/orders/`) — though Robinhood is an **unofficial API** and
  should be labeled experimental. By contrast, IB is a **stub in the default build**
  (`#if IBAPI`; ships `UnsupportedIBBrokerageClient`; `IBHistoricalDataProvider.cs:541-627`), and
  TradeStation/Tradier are mapping-only scaffolds — yet they can appear selectable. Mark IB/TradeStation/Tradier
  unavailable and Robinhood experimental so the roster reflects what can actually trade.

---

## Tier 4 — Friction & correctness gaps that suppress adoption

- **Three competing "getting started" trackers** with different steps and storage
  (`FirstRunExperienceService` outcomes vs `app-shell.onboarding.ts:20-53` tour vs the wizard rail).
  Collapse to one.
- **Provider setup is raw-key entry** with a naked "priority" integer, no validate-before-save, an IB
  "read the docs" dead-end, and a required provider-host restart (`add-provider-drawer.tsx:137-199`,
  `provider-setup-panel.tsx:89-107`). Add inline connection-test, a guided IB path, hide priority
  under "advanced."
- **912-line config sample + a CLI still branded "market data collector"** (`MDC_*` env vars,
  placeholder support URL `HelpCommand.cs:712`, banner `:456-457`). Ship a slim default config and
  re-skin the CLI for operational finance.
- **Corporate-action posting has real gaps at mergers/spinoffs.** Dividends *are* posted —
  `SecurityMasterLedgerBridge.PostCorporateActionsAsync` books dividend declaration, withholding
  accrual, and receipt journal entries (`SecurityMasterLedgerBridge.cs:150-175`), and split/RoC/factor
  basis math exists (`LedgerTaxLotBasisAdjuster.cs:64-84`). The real gap is **mergers and spinoffs**,
  which currently produce only symbolic memo postings with **no cost-basis/lot transformation** — a
  genuine functional hole for a fund-accounting product (and DRIP reinvestment is worth confirming).
- **The "accrual" module doesn't accrue** — `AccrualTypes.fs:92-95` is a record + sum; the real
  day-count/interest math lives elsewhere (`DefaultInterestCalculator.cs`, `DayCountConventions.cs`).
  Wire or relocate it.
- **QuantScript "safe mode" is not a sandbox** (advisory substring denylist, self-documented —
  `RoslynScriptCompiler.cs:54-65`). User strategy code is **in-process RCE**. Move to an isolated
  `AssemblyLoadContext`/child process. (Security, not cosmetics.)
- **Paper-trading fidelity < backtest fidelity** — two divergent execution-sim code paths
  (`PaperTradingGateway.cs:13-73` vs `Meridian.Backtesting/FillModels/*`). Have the paper gateway
  consume the backtester's fill models for paper→live parity.

---

## Tier 5 — Strategic shape (the big-picture critique)

1. **Breadth is outrunning depth, diluting the value proposition.** Meridian is simultaneously a
   market-data collector, a trading/execution OMS, a backtesting research tool, a fund-accounting
   ledger, a reconciliation platform, and a governed-reporting system. Each is 70–90% real — but
   **no single persona currently gets one complete, delightful, daily-driver workflow.** The most
   defensible wedge (per the product docs) is the **Close/Reconcile/Prove finance-ops loop** — the
   loop broken by Tier-1 #1 and #2. Pick that persona, make their end-to-end path a 10, and let
   trading/quant be "supported" rather than co-headline.
2. **Split-persona risk.** The README sells to RIAs/family offices/ops teams *and* ships
   trading/quant/backtesting/strategy authoring. Those buyers want opposite things.
3. **Time-to-value vs. self-hosting burden.** The stated audience (non-devops finance teams) faces a
   .NET host + Postgres + React build + 912-line config, then lands on empty screens (#1). A
   one-command "meridian up with seeded demo tenant" would change the evaluation experience entirely.
4. **Governance/documentation ceremony may be exceeding user value.** ~356 docs / ~110 MB / a
   labyrinthine wave taxonomy (W5X-FINOPS, MDIF, stage-gates). Impressive for maintainers; opaque for
   a buyer trying to answer "what can I do on day one?"

---

## If you fix five things, fix these (in order)

1. **Make "sample data" actually load a populated demo tenant** — turns onboarding from a badge into
   a "wow." (Tier 1 #1)
2. **Wire reconciliation against a real internal book and retire the inverted single-sided shim** —
   delivers the actual product promise. (Tier 1 #2)
3. **Generalize the instrument→journal proof beyond the one hardcoded model** — makes "prove the
   number" demonstrable on any security. (Tier 1 #3)
4. **Subscribe to Alpaca `trade_updates`** — closes a genuine money-state correctness bug. (Tier 1 #4)
5. **Generalize no-code backtesting into a first-class screen** — the covered-call vertical already
   runs `BacktestEngine` no-code; extend that pattern to arbitrary strategies (or wire the orphaned
   Studio engine) so the strong backtester isn't limited to one bespoke flow. (Tier 3)

---

## What is genuinely strong (do not "fix" these)

- **Ledger/close/reporting governance:** real double-entry with balanced-posting enforcement
  (`JournalValidation.fs:22-26`), append-only immutability via reversing entries, thread-safe period
  close with TOCTOU defense (`LockedAccountingPeriodBook.cs:196-251`), fail-closed reporting
  certification requiring both authoritative ledger source and a matching reconciliation receipt
  (`ReportingRunCertificationService.cs:47-63`), SHA-256 hash-chained audit trail with `Verify()`,
  and true segregation-of-duties (creator≠approver≠releaser).
- **Accounting depth:** real multi-currency FX revaluation, FIFO/LIFO/HIFO/SpecificId/AverageCost
  tax-lot relief with wash-sale disallowance, depreciation schedules, and a PE distribution waterfall
  with algebraic GP catch-up.
- **Execution/backtesting engines:** production-grade OMS state machine, enforced live-readiness
  governance gate, and a strong bias-aware backtester with walk-forward.
- **Provider tier:** ~a dozen real adapters (Polygon, Alpaca, NYSE, Tiingo, Finnhub, AlphaVantage,
  Stooq, Yahoo, Fred, Edgar, OpenFigi, Plaid) with Polly retry/circuit-breaker, rate limiting, key
  redaction, and cancellation.
- **Robust statement parsing:** culture-aware CSV, tolerant OFX SGML/XML, secure IB Flex XML
  (entity-expansion hardened), Alpaca activity.
- **Engineering discipline:** ~12,100 tests, dev-only fixtures that announce themselves, near-zero
  stub markers, real deployment packaging (docker/k8s/systemd).

---

## Method note

Findings were produced by five parallel evidence-gathering passes (onboarding/first-run, browser
workstation, ingestion/providers/reconciliation, accounting/ledger/reporting,
trading/execution/backtesting) plus a cross-cutting metrics sweep, all read-only. No program
behavior was modified in producing this review.
