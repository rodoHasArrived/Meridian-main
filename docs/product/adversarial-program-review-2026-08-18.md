# Adversarial Program Review — Meridian (2026-08-18)

**Status:** independent review input; not a governance or roadmap-status document
**Owner:** review author (independent adversarial pass)
**Reviewed:** 2026-08-18
**Scope:** whole-program review of Meridian's high-level functionality, focused on what a real end
user can install, run, trust, and complete — and where improvement would raise end-user value most.
**Method:** seven parallel adversarial investigations over the wired code paths (a systematic
re-test of all 25 headline findings from the 2026-08-10 review, plus fresh passes over the first
mile and CI, the browser workstation, governance and authorization, the trading lane, fund
operations and accounting, and market-data provenance). The ~321 non-merge commits landed since
2026-08-10 were checked against what they claim. The five highest-impact claims were independently
spot-verified a second time. Every finding is anchored to `file:line` at commit `277a6529`.

> This review is deliberately critical; a strengths section gives fair credit at the end. It builds
> on `adversarial-program-review-2026-08.md` (2026-08-10) and its remediation plan
> (`adversarial-review-2026-08-remediation-plan.md`), re-tests every headline finding, and extends
> coverage to areas those passes did not reach (the reconciliation matching population, OFX
> multi-account/sign handling, sequence-number integrity, actor attribution in casework, the
> remediation work itself). Live status stays in the roadmap registry; nothing here competes with it.

## Headline

The 2026-07 headline — **"the codebase is dramatically more capable than the running product"** —
and the 2026-08-10 corollary — **"acceptance statuses drift ahead of wired reality"** — both still
hold. Eight days and 321 commits later, this pass adds a third-order observation:

> **The remediation itself now exhibits the program's core disease: the fix gets built, tested,
> registered — and left unwired at the last seam.** The program is provably good at producing
> capability and provably bad at connecting it to the user, and that pattern now applies to its own
> bug fixes.

Three flagship examples, each independently re-verified:

- **The first-account bootstrap was built end-to-end and is dead on arrival.** Since the last
  review, a complete bootstrap lane landed: a one-use loopback token service
  (`src/Meridian.Ui.Shared/Services/InitialAccountBootstrapService.cs:12-43`), `GET /setup/account`
  + `POST /api/auth/bootstrap` endpoints
  (`src/Meridian.Ui.Shared/Endpoints/InitialAccountBootstrapEndpoints.cs:12-37`), and a supervisor
  that mints `MDC_BOOTSTRAP_TOKEN` and opens the setup page
  (`src/Meridian.LifecycleSupervisor/LifecycleSupervisorRuntime.cs:288-289,820-827`). But the
  middleware-ordering flaw this lane exists to bypass — the `!IsConfigured` fail-closed branch
  returning 503 *before* the `/setup/account` and `/api/auth` exemptions
  (`src/Meridian.Ui.Shared/Endpoints/LoginSessionMiddleware.cs:99-125`) — was never touched (last
  change 2026-07-25, before the prior review). The middleware still never consults the bootstrap
  token. With zero accounts in required mode, the setup page the supervisor opens still 503s.
  Remediation item AR8-01 is still unchecked, and CI cannot see the breakage because the install
  smoke pre-provisions an account before logging in
  (`build/scripts/install/smoke-web-workstation-install.ps1:10-11,440-452`).
- **The WPF desk-safety seam was built, registered, unit-tested — and no button calls it.**
  `TradingSafetyCommandService` (Stop→halt, CancelAll→sweep;
  `src/Meridian.Wpf/Services/TradingSafetyCommandService.cs:31-62`) and
  `ExecutionSafetyControlClient` (`src/Meridian.Wpf/Services/ExecutionSafetyControlClient.cs:54-96`)
  are DI-registered (`src/Meridian.Wpf/Features/Trading/TradingFeatureModule.cs:33-34`), but the
  command-bar click path still maps `"Stop"` to a RunRisk pane split and `"CancelAll"` to a
  floating Position Blotter
  (`src/Meridian.Wpf/Services/TradingWorkspaceShellPresentationService.cs:169-172`) — while the
  Stop button's own description reads "Halt the desk: open the circuit breaker and sweep the open
  book" (`:550`). The roadmap's `W9-SAFETY-007` current summary asserts "Stop and Cancel All now
  route through a new ExecutionSafetyControlClient" — verifiably not the case; the test that
  "proves" wiring only checks a static `IsSafetyCommand` claim
  (`tests/Meridian.Wpf.Tests/Services/TradingSafetyCommandTests.cs:42-52`).
- **The truth-discipline pin was built server-side and the browser client throws it away.**
  `W9-TRUTH-001` sits `ready_for_acceptance` on the claim that the pinned provenance label reaches
  "both workstation shells even when demo heuristics say disabled." True for WPF
  (`src/Meridian.Wpf/Services/StatusServiceBase.cs:302-326`); false for the browser:
  `resolveWorkstationDataProvenance` reads the pinned `provenance` only when `demoMode.enabled` is
  truthy and otherwise returns `"real"`
  (`src/Meridian.Ui/dashboard/src/app-shell.data-provenance-badge.ts:92-101`) — and a unit test
  locks the wrong behavior in. Since the server sets `enabled` from a credentials heuristic
  (`src/Meridian.Ui.Shared/Endpoints/DemoModeEndpoints.cs:82-94`), a host with an Alpaca or Polygon
  key that runs a pinned-Simulated tape renders **no banner** — exactly the credentialed-host case
  the pin was built for.

The pattern matters more than any single instance: acceptance evidence, remediation summaries, and
even a commit title ("Ask the gateways what is working", `0dff4c4d` — a docs-only commit) now
assert wiring that does not exist. The program's registry discipline is real, but it is being fed
claims one seam short of true.

## Re-test scorecard

Of the prior review's headline findings re-tested against current code (plus the four first-mile
items its remediation plan tracked as quick wins):

| Verdict | Count | Items |
| --- | --- | --- |
| Fixed / largely fixed | 8 | `UNWIRED_WORKSTATION_ROUTES` guard; `api-errors.ts` envelope fallback; devcontainer durability; sample-config StockSharp block (+ CI gate); credential-vault POSIX perms (0600, validated on read); backup/restore script + CI drill; period-lock enforcement at the single posting path; PR-lane .NET tests via `meridian-ci.yml` |
| Partially fixed | 12 | account bootstrap (built, unreachable); Strategy Designer (honest-disable, still unwired); casework verbs (triage wired, 3 of 22 clients consumed); provenance labeling (tape-aware degraded-mode banner, binding-derived fallback intact); demo probe (fail-closed direction, still one-shot); promotion gate (permissioned + simulated-runs blocked; evidence still typeable); kill switch (server breaker+sweep strong; no UI trigger either lane); route authorization (466 enforcing filters; no runtime deny; 58 unguarded mutations frozen); fund-economics/FX (currency columns written; kernels still dark); concurrency (canonical 409 contract adopted by 4 endpoint files, zero `ETag`/`If-Match`); journal immutability (tax-lot triggers only); statement connectors (FITID now matched, dedupe still file-level) |
| Still open | 12 | fictional $100k live risk book; paper slippage 0 / no partial fills; `MarketEvent` "IB"/"ALPACA" defaults; Lean fabricated lifecycle; WPF fake version-check/activity feed; in-memory operator inbox (production: absent); JSON-file break queue; IP-partitioned rate limiting (per-user branch still dead); broken `deploy/` manifests (now labeled EXPERIMENTAL); generic XLSX text cells; Evidence Vault extraction ceremony; duplicate legacy `ci.yml` with PR-skips |

Two program-level deltas deserve credit before the findings: **engineering weight moved into
source** (in the last 300 commits, `src/` file touches outnumber `docs/` 10,110 to 2,358 — a
reversal of the prior review's 635-to-1,001), and the authorization burn-down was genuine work
(~360 undeclared routes down to a frozen, test-enforced baseline of 58 unguarded mutations). The
problem is where the remaining effort went: the remediation plan self-reports **6 of 60 items
done, all quick wins**; the first-mile core (AR8-01/02/08) is untouched.

## 1. The first mile is still a closed door

Nothing has shipped, and both honest entry paths now fail — one of them worse than before:

- **The README's flagship launch command now crashes at composition.** A fresh clone running
  `dotnet run … --mode workstation` in the default Production environment hits the governance
  persistence guard: `MERIDIAN_DATABASE_URL`/fund-structure connection strings are required and
  `MERIDIAN_USE_INMEMORY_GOVERNANCE=true` is forbidden in Production
  (`src/Meridian.Application/Composition/Features/StorageFeatureRegistration.cs:625-665`).
  `docs/start/README.md:155-161` still claims stores fall back to in-memory with a red banner —
  false for the default launch row. Only `DemoWorkspaceCli` sets the opt-outs, and its own doc
  comment admits the workstation "would otherwise fail closed at startup"
  (`src/Meridian/DemoWorkspaceCli.cs:115-130`).
- **The packaged install meets the unreachable bootstrap** (headline). No `--create-user` /
  `--hash-password` verbs exist (`src/Meridian.Application/Commands/ConfigCommands.cs` — no
  matches); the 503 body and login page still tell users to set `MDC_USERS` with `passwordHash`
  values (`LoginSessionMiddleware.cs:198`) with no documented recipe anywhere in `docs/`.
  `MDC_BOOTSTRAP_TOKEN` is absent from `docs/reference/environment-variables.md`.
- **`--seed-demo` remains the only working path, and it still relaxes the posture silently** —
  `MDC_AUTH_MODE=optional`, in-memory governance, Development environment, with no console
  disclosure (`src/Meridian/DemoWorkspaceCli.cs:106-131`). The mitigations are real (loopback-only
  bind, pinned `Seeded` provenance, isolated workspace root, CI demo smoke) — but evaluators still
  form their impression under a posture that cannot support any certification claim, then hit the
  wall above.
- **Release evidence is unchanged since July:** zero git tags, zero GitHub releases, all 7
  `desktop-installer-packaging.yml` dispatch runs failed (latest 2026-07-16, none retried in 321
  commits), and the P0 tracker still reads 0 of 21 rows production-certified
  (`docs/product/implementation-todo-list.md:36`). The weekly Production Certification workflow is
  red on main — its three most recent runs (Aug 9, 11, 16) all failed on evidence jobs.
- **`deploy/` is now honestly labeled EXPERIMENTAL and still cannot work:** the Dockerfile copies
  12 of the needed project files (missing Audit, Backtesting, Identity, QuantScript ×2 —
  `deploy/docker/Dockerfile:24-40`), the compose healthcheck probes `/health` which is not in the
  auth-exempt set (`LoginSessionMiddleware.cs:65-74` exempts only `/healthz`-style paths), and the
  systemd unit still runs `dotnet run` from source under `ProtectSystem=strict` with no writable
  build output (`deploy/systemd/meridian.service:34,49-51`). No CI job builds the image.
- **Upgrades are one-way in practice.** `InstallationTransaction` is a solid SHA-256-verified
  staged swap with rollback, but DB migrations auto-run at host startup with no operator gate and
  no schema-version compatibility check in the rollback path
  (`src/Meridian.Application/Composition/FundAccountsStartup.cs:30-52`) — rolling back the binary
  leaves it running against a migrated schema. And these exact paths are the tests wired to no CI
  lane: `run-dotnet-ci-tests.py:29-35` admits `Meridian.Setup.Tests` "was added with the installer
  work but never wired to a lane."
- New: the consumer installer build itself has an undocumented hard prerequisite — a local
  PostgreSQL payload tree for win-x64 *and* win-arm64 (`build/scripts/install/build-consumer-setup.ps1:20-23`)
  that nothing in the repo produces or documents.

## 2. Trading safety: every layer exists, no layer connects

The trading lane made real progress below the surface and none of it reaches an operator's hand at
the moment of incident:

- **No workstation UI on either lane can trip the kill switch.** The server side is now strong — a
  durable fail-closed breaker whose activation couples to a cancel-all sweep with per-order
  `StillWorking` reporting (`src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs:422-524`) — but
  the browser's breaker route constant is called from nowhere
  (`src/Meridian.Ui/dashboard/src/lib/ui-api-routes.generated.ts:480`, no `api.ts` client) and the
  WPF buttons open panes (headline). Only a raw API call can halt the desk. `W9-SAFETY-007` is
  honestly `in_progress`, but its current summary already claims the WPF wiring.
- **The sweep behind the switch is blind after a restart.** `CancelAllAsync` enumerates only the
  in-memory order dictionary (`src/Meridian.Execution/OrderManagementSystem.KillSwitch.cs:43-48`)
  and never asks any gateway for its open orders — after an OMS restart, or for out-of-band
  orders, the sweep reports `Completed` over a non-empty broker book.
- **Live risk still measures a fictional book.** `PaperTradingPortfolio(100_000m)` remains the
  only `IPortfolioState` registration in src, injected unconditionally into the OMS and risk rules
  (`src/Meridian/UiServer.cs:361-367,375,383`). Worse than previously reported:
  `PositionReconciliationService` — the drift detector — is registered in **no host** (test-only
  consumers), so position-level reconciliation is dead code. Mitigation since: the per-order live
  readiness gate does refuse routing on stale brokerage sync or non-ready open-order
  reconciliation (`src/Meridian.Ui.Shared/Services/TradingOperatorLiveOrderReadinessGate.cs:31-81`).
- **The pre-trade rule catalogue ships disarmed.** Fat-finger (both limbs), price collar,
  max-order-notional, gross-exposure, and symbol-concentration all default to `null` = approve
  without measuring (`src/Meridian.Application/Services/RiskRuleRuntimeService.cs:92-107`), and the
  browser risk panel can edit exactly one rule — the drawdown threshold
  (`src/Meridian.Ui/dashboard/src/screens/risk-control-panel.view-model.ts:84`). A fresh install's
  armed protection is a rate throttle plus a drawdown rule measuring the fictional $100k book.
  Bracket/OCO child-leg prices bypass the fat-finger/collar limbs entirely
  (`src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaBrokerageGateway.cs:1108-1126`).
- **A fixed-income fill books 100× its economic value.** Pre-trade notional scales
  percentage-of-par by 1/100 (`src/Meridian.Risk/Rules/OrderNotionalResolver.cs:271`) but fill
  economics never do (`src/Meridian.Execution/Events/TradeExecutedEvent.cs:43` books
  `FilledQuantity * FillPrice` raw into ledger/positions/cash). The roadmap itself records this as
  blocking ("unshippable"); it remains unfixed behind a passing risk check.
- **Paper realism still flatters.** Matching is now honest and versioned (`paper-match/1`; limit
  at-or-better, envelope-clamped, fail-closed on no data) and commissions are non-zero by default —
  genuine W9-PAPER-003 progress — but `SlippageBasisPoints` still defaults 0 with no shipped
  config setting it (backtests default 5bps, so paper systematically beats backtest), partial
  fills are explicitly unmodeled, and there is no staleness bound, market-hours, or halt concept
  anywhere in `src/Meridian.Execution`: a 3am Saturday paper market order fills in full at
  Friday's last print at zero slippage (`PaperMarketObservation` carries no timestamp; the DI
  comment admits "the feed cache keeps prints indefinitely",
  `src/Meridian.Application/Composition/WorkstationServiceCollectionExtensions.cs:394-396`).
- **Promotion evidence is still typeable.** Walk-forward numbers are constructed from the request
  body with domain-only validation; `SourceReference` is stored and never dereferenced
  (`src/Meridian.Ui.Shared/Endpoints/PromotionEndpoints.cs:150-162`); the walk-forward service is
  still registered only in the WPF host, so typing the numbers remains the only way a browser
  operator can clear the live gate; and the retained-evidence cross-check still exempts live
  targets (`src/Meridian.Strategies/Services/PromotionService.cs:985-993`). The promotion record
  pins metrics but not a parameter-set hash, so the audit trail cannot prove after the fact what
  configuration was promoted (`src/Meridian.Strategies/Services/BacktestToLivePromoter.cs:39-41,126-145`).
- Operational sharp edges on the live adapter: submissions are blocked unless the trade-updates
  stream produced an event within 30 seconds, so the first order of a quiet session is rejected
  (`src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaTradeUpdatesClient.cs:70-79`); cancel emits
  a synthetic `Cancelled` on DELETE 2xx before the broker confirms
  (`AlpacaBrokerageGateway.cs:278-291`). The Lean endpoints still fabricate lifecycle state
  (`queued` forever, hardcoded zero results — `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:212-317`).

## 3. The reconciliation wedge cannot reconcile

The product's operating question is "can Meridian prove, book, reconcile, approve, and report an
investment decision?" The middle verb is structurally broken:

- **Statement transaction matching runs against a deliberately empty internal population.** The
  registered population provider returns `new InternalReconciliationPopulations(positions, cash, [])`
  (`src/Meridian.Application/Reconciliation/RetainedInternalReconciliationPopulationProvider.cs:89`
  — its own remarks document why), so `StatementMatchingEngine.MatchTransactions` compares every
  imported trade, fee, and dividend against nothing and **100% of transaction rows break on every
  import**. Positions and cash match against retained snapshots only. A non-GUID fund-account
  label silently degrades *everything* to empty — all rows break. Meanwhile the
  institutional-grade `ReconciliationMatchingEngine` the 2026-08-10 review praised is never
  DI-registered; its consumers are tests. The flagship import→reconcile→close loop is currently a
  break-generation machine, and every surface built on top of it (break queue, casework, close
  readiness) inherits the noise.
- **The break queue those breaks flood into is still one JSON file behind one process-wide
  semaphore** (`src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs:25,51-52`),
  with no Postgres variant — the serialization bottleneck sits exactly where the volume
  concentrates.
- **Casework actions now exist and are misattributed.** Basic triage (review/assign/resolve) is
  wired from the Accounting screen — but assign and resolve send a hardcoded actor:
  `assignedTo: "ops.gov", reviewedBy: "ops.gov"` and `resolvedBy: "ops.gov"` with a canned
  "Reviewed in accounting panel." note
  (`src/Meridian.Ui/dashboard/src/screens/accounting-screen.view-model.ts:4051,4083-4084`),
  regardless of who is operating. In an evidence product, every break disposition in the governed
  audit trail is attributed to a fake user. The remaining 19 of 22 casework clients
  (comment/root-cause/sign-off/reopen/waive/supersede/bulk dry-run+execute) still have zero screen
  consumers, and the top-nav "Casework" destination renders a read-only page whose single action
  button is permanently disabled
  (`src/Meridian.Ui/dashboard/src/screens/finance-standard-pages-screen.tsx:632-690`).
- **Statement intake robustness is unchanged where it counts.** OFX (and every other connector)
  still hard-decodes UTF-8 and discards the SGML header where OFX 1.x declares its charset
  (`src/Meridian.FinancialOperations/Reconciliation/Connectors/Ofx/OfxDocumentParser.cs:266-269`);
  dedupe is still whole-file (fund + period + file hashes), so overlapping or amended statements
  double-import — though FITID now at least maps to `ExternalTransactionId` and participates in
  matching. New findings this pass: a multi-account OFX file books every entry to the **first**
  `ACCTID` found in document order (`OfxDocumentParser.cs:115,228-231`); nothing cross-checks the
  OFX sign convention (TRNTYPE=DEBIT with an unsigned TRNAMT imports with the wrong direction —
  `src/Meridian.FinancialOperations/Reconciliation/StatementBuiltInProfiles.cs:127-128`); and
  layout-drift detection remains structurally inert for exactly the built-in profiles (OFX, IB
  Flex, Alpaca) most exposed to custodian drift
  (`StatementMappingProfileCatalog.cs:107-118` early-returns for built-ins).

## 4. Truth discipline: the label layer improved, the attribution layer did not

- **Every trade from every real vendor is still durably recorded as "IB", every quote as
  "ALPACA".** `MarketEvent.Source` defaults survive (`src/Meridian.Domain/Events/MarketEvent.cs:15,32-122`)
  and three of four collectors still publish sourceless (`TradeDataCollector.cs:237`,
  `QuoteCollector.cs:40`, `MarketDepthCollector.cs:122`; only `L3OrderBookCollector` gained a
  source parameter — and its derived L2 snapshot still defaults). The blast radius grew: the
  storage layer partitions durable files by `evt.Source`
  (`src/Meridian.Storage/Policies/JsonlStoragePolicy.cs:35-48`), so the misattribution is baked
  into on-disk layout, and the newly wired quality suite uses `evt.Source` as its provider label
  (`QualityMonitoringPublisher.cs:135,148`) — the per-provider gap/latency metrics that finally
  turned on are attributed to the wrong vendors.
- **Sequence integrity is partly fictional.** Polygon fabricates sequence numbers client-side via
  `Interlocked.Increment` (`src/Meridian.Infrastructure/Adapters/Polygon/PolygonMarketDataClient.cs:665-674`)
  — continuous by construction, so gap detection can never fire for Polygon; Alpaca stamps the
  exchange trade ID as the sequence, making sequence checks noise.
- **Reconnect losses leave no mark in the tape.** Gaps shorter than the 2-minute remediation floor
  are skipped with a Debug log, and no integrity event is ever published into the pipeline for the
  missed window (`src/Meridian.DataIntegration/Backfill/AutoGapRemediationService.cs:461-468`) — a
  stored day with a 90-second outage reads as a continuous tape.
- **Adjusted and unadjusted history mix silently.** `HistoricalBar` has no adjusted flag; Finnhub
  and NYSE request unadjusted while other providers request adjusted, and the composite failover
  chain can switch regimes mid-series for a split symbol with no discriminator at rest or at read.
  Relatedly, synthetic backfill — opt-in, and honestly stamped "synthetic" at rest — sorts
  *first* in the failover chain (`Priority = 1` vs Alpaca 5, IB 10) and the chart read model
  erases `Source` entirely (`HistoricalBarPoint`,
  `src/Meridian.Ui.Services/Services/HistoricalDataQueryService.cs:862-871`): labeled at rest,
  unlabeled at read.
- Genuine improvements to bank: the degraded-mode probe now inspects the *actual* configured
  streaming sources including failover candidates and renders a red simulated banner in the
  browser (`src/Meridian/UiServer.cs:1058-1149`); the IB simulator self-identifies (`ib-sim`) with
  a loud construction warning; the fixture path renders a persistent SEEDED banner; and the demo
  probe now fails closed toward SIMULATED (though still one-shot, with no re-probe — a transient
  startup failure brands a real session simulated for its lifetime). The WPF lane consumes none of
  the degraded-mode signal (`MarketDataMode` has zero consumers in `src/Meridian.Wpf`), so the
  co-equal desktop lane shows no banner where the browser shows red. The WPF fake version check
  ("1.6.1") and fabricated "Cloud sync completed" activity feed survive verbatim
  (`src/Meridian.Wpf/ViewModels/SettingsViewModel.cs:862-889`). ~3,642 lines of data-quality
  monitors remain dead (`BadTickFilter`, `PriceContinuityChecker`, `SpreadMonitor`, …), the
  freshness-SLA monitor's ingress is still never called (`/api/sla/*` reports healthy-zero
  forever), and `CrossProviderValidator` is still constructed with `enableCrossValidation: false`
  at both call sites.

## 5. Governance: enforced where declared, declared by ratchet, and the database still trusts the app

The W9-GOV-008 burn-down was the biggest single investment of the period and it is real — with a
precise boundary that matters to an auditor:

- **466 routes now carry executing permission filters** over a genuine 27-flag, role-differentiated
  permission model (`src/Meridian.Ui.Shared/Endpoints/EndpointAuthorization.cs:158-218`;
  `src/Meridian.Identity/Contracts/Auth/RolePermissions.cs:10-133`), and the falsely-claimed
  coverage test now actually exists as a composed-endpoint behavioral sweep. But **"undeclared now
  means unguarded" is an accounting invariant, not a runtime deny**: nothing in the request
  pipeline denies a route lacking metadata — only a failing test does. The frozen baseline of
  **58 genuinely unguarded mutations** is reachable by any authenticated session of any role, and
  it includes destructive operations: `POST /api/maintenance/execute` (whose catalog includes
  retention enforcement that deletes expired data —
  `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:484`), all symbol mutations,
  packaging create/import/delete, and replay. ~540 GET routes remain unclassified. Two new
  perimeter findings: the Plaid webhook accepts unauthenticated, unverified events
  (`src/Meridian.Ui.Shared/Endpoints/PlaidEndpoints.cs:154-173` — fabricated banking-sync evidence
  upstream of reconciliation), and the single shared API key executes all 58 unguarded mutations
  with no actor attribution (`ApiKeyMiddleware.cs:60-91`).
- **Tenancy still fails open at every default**: fund-scoped write enforcement defaults off
  (`WorkstationTenantContext.cs:198-201`), the registry guard allows on exception, the read
  predicate passes `tenant_id is null` rows, and the fund-structure store is tenancy-blind — the
  roadmap itself concedes cross-tenant read/mutate on `/api/fund-structure/graph`.
- **The approval-policy matrix became *editable* without becoming real** — `UpsertRuleAsync` now
  persists rules that no enforcement path reads; segregation-of-duties remains hardcoded
  independently in at least seven places. An admin tightening `requiredDistinctApprovals` in
  Settings changes a JSON file and nothing else. This is worse than a static display: edits imply
  effect.
- **The journal is still mutable at the database.** No immutability trigger on
  `journal_entries`/`journal_legs`, `on delete cascade` intact, no entry-level debits=credits
  constraint — while the team's own trigger pattern protects the tax-lot tables three migrations
  away (`V_ledger_027__atomic_tax_lot_posting.sql:166-186`). Real hash chains landed for
  operations-continuity, reporting-governance, and compliance-approval audit trails (genuine,
  fork-resistant, verified on load) — but the accounting action audit on the default file-backed
  path is unchained, and the "tamper-evident" fund-administration event log is a hash chain over
  an in-memory list (`src/Meridian.FinancialOperations/FundAdministration/FundAdministrationEventLog.cs:43-48`).
- Positive counterweight, verified: session and CSRF hygiene is strong (server-side random tokens,
  HttpOnly + SameSite=Strict, server-side logout, double-submit CSRF with constant-time compare,
  HTTPS-or-loopback production posture), secrets are masked at the sampled surfaces, and the new
  pre-binding guard on account administration is real.

## 6. Fund accounting: the spine hardened; the advertised economics remain a library

- **What genuinely works now** — and should be protected: a single guarded posting path where
  every append validates period state under a row lock
  (`src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.cs:100-119`), a coherent period matrix
  (soft-close accepts only closing entries and approved adjustments; hard-close refuses while
  temporary accounts carry residuals; reopen is role-gated with retained restatement evidence),
  fee accruals whose schedule-typed NAV/HWM must exactly reconcile against a server-derived
  tie-out from posted journals and fail closed otherwise
  (`src/Meridian.FinancialOperations/PrivateCapital/LedgerCapitalAccountReconciliationResolver.cs:63-314`),
  and report-pack delivery that structurally cannot ship an unapproved pack
  (`ReportingWorkflowService.cs:1336-1348,1436-1439`).
- **The fund-economics layer the roadmap marks `ready_for_acceptance` still has zero production
  consumers.** `FundEconomicsJournalFactory`, waterfall, preferred return, clawback, equalization,
  NAV-per-unit, shadow-NAV, capital-call builders — all referenced only by themselves and tests.
  `W9-NAV-006`'s exit criteria are worded as live surfaces; the acceptance evidence is source
  files plus golden tests. There is no unitized NAV run, no waterfall run, no capital-call
  issuance reachable from any endpoint or screen. FX is refused rather than handled (the only
  currency-column writer posts functional-only at rate 1 and rejects foreign-currency lines —
  `AccountingPostingCandidatePostService.cs:810-826`). The fee producer that *is* wired books a
  flat un-prorated `BeginningNav × rate` with no day-count, hurdle, or crystallization — the exact
  simplification the remediation plan's own prerequisite warns "posts materially incorrect
  journals."
- **The proof chain dead-ends at its most important link.** From a posted journal entry, the UI's
  evidence link routes to a subject kind (`journal-entry`) the server's resolver does not support
  (`src/Meridian.Ui.Services/Services/EvidenceSubjectResolver.cs:29-45`), and the view-model
  states system-posted entries show no line detail, lifecycle, or evidence at all
  (`journal-entry-detail-screen.view-model.ts:162`). "Walk from the number back to the evidence"
  currently works only for manual-workbench drafts.
- Deliverables: partners' capital XLSX is now typed-numeric with a NAV tie row — good — while
  every other statement still exports pre-formatted strings a controller cannot sum
  (`src/Meridian.Documents/FinancialReportDocumentRenderer.cs:292-297`; the code comment concedes
  it). Evidence Vault extraction remains hand-keyed ceremony (no OCR/text layer in src).

## 7. Daily-driver quality: the workstation still hides its failures

- **The activation ratio did not move: 127 of 406 browser API clients (31.3%) are unreachable from
  any screen** — the prior count was 126; basic break triage got wired while new orphans were
  added. Whole operator capabilities exist server-side with no UI: user administration (password
  reset, session revoke, account disable), admin maintenance/storage/retention, the
  operations-continuity ledger/broker-import lane, bulk casework.
- **Silent wrong answers persist at the worst spots.** 23 of 214 catch sites in `src/screens/`
  swallow errors outright: the close workflow renders an empty valuation-schedule list on fetch
  failure (`accounting-screen.tsx:2131`), record explorers silently downgrade to a static
  fallback that looks intentional (`accounting-screen.tsx:1871,1892`;
  `financial-record-explorer.tsx:83`), and Asset Detail still shows "no corporate actions" during
  an outage (`asset-detail-screen.tsx:249-251`). `AsyncRegion` — the design system's own remedy —
  is adopted by 1 of 68 screens; one render error still blanks the whole workbench route.
- **Confirmation friction is inverted.** Locking a close period fires on a single click with no
  confirm (`accounting-screen.close-cockpit-panels.tsx:406-413`) and statement-fetch schedule
  deletion is one-click, while lower-stakes settings actions get type-to-confirm dialogs. The
  Strategy Designer holds all operator work in volatile state with no persistence and no
  navigation warning. (Credit: the trading order ticket is now exemplary — validation,
  acknowledgement gates, double-submit guard, outcome retention.)
- **Push never arrived for the data spine.** The quote stream is genuinely good (change-driven,
  coalescing, capped), but breaks, approvals, ledger and inbox all still poll on a 30-second
  staleness loop, and `/api/events/stream` — which still re-serializes its full payload every
  2 seconds — now has zero workstation consumers; it burns a serialize loop per connection for a
  legacy HTML page.
- **Weight is flat**: `settings-screen.tsx` 7,391 lines, the accounting route ≈800KB of JS, no
  `manualChunks` (`vite.config.ts:187-190`).

## 8. Verification: the gate got real; the theater moved

- Credit first: `meridian-ci.yml`'s quality-gate now runs the .NET non-integration sweep, browser,
  docs, and workflow lanes on every PR, push, and merge group; the test-skip register is healthy
  (21 registered skips, none past due); WPF tests run on PRs via the path-filtered Windows lane.
- Residuals: the duplicate legacy `ci.yml` still carries its three `if: != 'pull_request'` skips;
  Docker/Postgres integration suites are excluded from PR *and* nightly (weekly certification
  only — currently red); `Meridian.Setup.Tests` runs in no lane at all; and the six
  accounting-readiness tests quarantined against `W9-ASSET-010` remain skipped (registered,
  review-by 2026-11-01).
- The sharper problem is **smoke that certifies around the defect**: the install smoke provisions
  an account before login, so the one flow every new user hits (zero accounts, required auth) is
  the one flow no automation exercises — and `docs/operators/browser-workstation-installer.md`
  claims the bootstrap-token flow is validated. The dashboard's Playwright smoke still mocks the
  entire API, visits only the root URL, and filters API-404 console errors; no test mounts the 48
  routes; the safety-command test asserts a static claim rather than wiring. This is how every
  built-but-unwired finding in this review shipped: the assurance layer measures everything except
  the seam.

## Acceptance-drift ledger

The 2026-08-10 corollary, updated with this pass's evidence — registry or plan claims that current
code contradicts:

| Claim | Where claimed | Wired reality |
| --- | --- | --- |
| Provenance label reaches both shells "even when demo heuristics say disabled" | `W9-TRUTH-001` (ready_for_acceptance) | Browser returns `"real"` when `enabled` is false (`app-shell.data-provenance-badge.ts:92-101`); wrong behavior test-encoded |
| "Stop and Cancel All now route through ExecutionSafetyControlClient" | `W9-SAFETY-007` current summary | Zero production callers; clicks map to pane layouts (`TradingWorkspaceShellPresentationService.cs:169-172`) |
| Unitized NAV and real fee/waterfall/capital-call economics | `W9-NAV-006` (ready_for_acceptance) | Every named kernel has zero production consumers; no run reachable from any endpoint or screen |
| Paper realism with costs | `W9-PAPER-003` (ready_for_acceptance) | Matching honest, but slippage defaults 0 with no shipped config, no partial fills, no staleness/hours/halt bounds |
| "Ask the gateways what is working" | commit `0dff4c4d` | Docs-only commit; the sweep still enumerates only in-memory orders |
| Bootstrap-token first-account flow validated | `docs/operators/browser-workstation-installer.md:24,52` | The smoke pre-provisions an account; the real flow 503s |
| In-memory fallback with red banner for the default launch | `docs/start/README.md:155-161` | Default Production launch throws at composition |

## Prioritized improvement list (by end-user value uplift)

1. **Wire the three built-but-dead flagship fixes.** Exempt `/setup/account` + `/api/auth/bootstrap`
   (token-gated, loopback) before the unconfigured 503 branch; route the WPF Stop/CancelAll clicks
   through the existing `TradingSafetyCommandService`; make `resolveWorkstationDataProvenance`
   honor a pinned label regardless of `enabled`. Three seams, each days-or-less, each completing
   work already paid for — the highest value-per-line changes available in the codebase. Then add
   the structural tests that would have caught them (an HTTP test for zero-accounts+required
   bootstrap; a wiring test that clicks Stop and asserts the client was invoked; the badge test
   corrected).
2. **Make the reconciliation wedge reconcile.** Populate the internal ledger-transaction side of
   statement matching (a period-scoped journal→custodian-transaction projection feeding
   `InternalReconciliationPopulations.LedgerTransactions`), or register the already-built
   `ReconciliationMatchingEngine` on the live path. Until then every import floods the break queue
   and the wedge persona's flagship loop produces noise. Fold in FITID-level idempotency, the OFX
   multi-account/sign/charset fixes, and real actor identity in casework (replace `"ops.gov"` with
   the session user — it is already in `HttpContext.Items`).
3. **Finish the kill switch as one verified end-to-end path, then arm the rails.** Browser breaker
   button on the existing route; WPF wiring (item 1); sweep the union of local orders and every
   gateway's open orders. Ship non-null defaults (or a required setup step) for fat-finger,
   collar, and notional rules with a browser editor beyond the drawdown threshold; extend the
   price limbs to bracket child legs. Fix the fixed-income 100× booking before any live routing.
4. **Make live risk measure the real book.** Seed `IPortfolioState` from broker positions in live
   mode, register `PositionReconciliationService` in the host, and refuse routing while position
   sync is unavailable (the order-level readiness gate is the right pattern — extend it).
5. **Ship the first mile.** Fix the bootstrap (item 1), make the README launch command work or say
   what it needs, retry the installer pipeline (7/7 failures, none retried in a month), wire
   `Meridian.Setup.Tests` into a lane, un-bypass the install smoke, add `--hash-password` /
   `--create-user` verbs, fix-or-archive `deploy/`, and document `MDC_BOOTSTRAP_TOKEN`. Until an
   end user can reach the product under the supported posture, every other improvement is
   invisible.
6. **Fix provenance at the ingress seam.** Remove the `"IB"`/`"ALPACA"` factory defaults, require
   a source on `MarketEvent` publication (the `L3OrderBookCollector` pattern, applied everywhere,
   or adapter-stamped), propagate source through `HistoricalBarPoint`, mark reconnect gaps with an
   integrity event regardless of remediation floor, and carry an adjusted/unadjusted flag on
   historical bars. One seam corrects storage attribution, the quality metrics that just went
   live, and the synthetic-contamination question simultaneously.
7. **Activate the fund-economics and FX kernels or re-status the row.** Wire
   `FundEconomicsJournalFactory` into the intake runner per AR8-25's prerequisite discipline
   (day-count, hurdle, crystallization inputs first — the plan itself warns naive wiring posts
   wrong numbers), FX revaluation into period close, and give unitized NAV one operator-runnable
   surface. Until then, `W9-NAV-006` should not read `ready_for_acceptance`.
8. **Close the governance floor.** Runtime deny for metadata-less routes (the filter
   infrastructure exists); burn down the 58 unguarded mutations (maintenance, symbols, packaging
   first) and verify the Plaid webhook signature; DB-level journal immutability trigger + entry
   balance constraint (the pattern is three migrations away); chain the file-backed accounting
   audit; persist the fund-administration event log; make the approval matrix enforce or remove
   its editor; flip tenancy enforcement on for new deployments.
9. **Stop hiding failure in the workstation.** Adopt `AsyncRegion`/region boundaries on the
   screens where silent degradation costs money (close cockpit, asset detail, explorers); add
   confirmation to period lock and schedule deletion; persist Strategy Designer drafts (the
   endpoint exists); route-mount smoke + orphan-export gate in CI; burn down the 127 orphans
   screen-first (bulk casework and user administration are the highest-frequency wins).
10. **Durability and concurrency floor.** Postgres break queue, durable operator inbox (or a loud
    "not available in production" instead of silent skip), extend the canonical 409 contract
    beyond its 4 adopters toward the 57 files already carrying `ExpectedVersion`, per-user rate
    limiting (populate `HttpContext.User` — one line where the middleware already stamps Items),
    and delete the legacy `ci.yml` duplicate.

Quick wins worth doing this week regardless of sequencing: the provenance-badge one-liner (item 1c);
`"ops.gov"` → session actor; confirm dialog on `lockClosePeriod`; delete or `501` the Lean
endpoints; remove the WPF fake version-check/activity feed (still verbatim after two reviews);
retry the installer workflow; wire `Meridian.Setup.Tests` into the Windows lane; ship a
`PaperTrading:Costs` sample-config block with non-zero slippage.

## What is genuinely strong (do not regress it)

- **The remediation was real engineering, not narrative.** src-dominant commit weight; the
  authorization declaration lane (466 executing filters, a true behavioral sweep, a frozen ratchet
  that mechanically shrinks); vault key permissions with read-time validation; the recovery script
  with a CI-passing drill; devcontainer durability; the sample-config gate.
- **The governed posting spine** — single guarded append path, period matrix, residual-checked
  hard close, evidence-gated reopen — and the **fee tie-out resolver** that refuses to accrue
  against numbers it cannot reconstruct from posted journals. This is the product thesis working.
- **The paper matching policy** — honest, versioned, envelope-clamped, fail-closed on missing
  data — and the **durable breaker** with atomic fail-closed persistence and latching critical
  rules. `CompositeRiskValidator` remains unusually well-argued (fail-closed on unmeasurable,
  reservation rollback, governed escalation).
- **Hash-chained audit where it landed** (operations continuity, reporting governance, compliance
  approvals — persisted, fork-resistant, verified on load), **session/CSRF hygiene**, and the
  **report-pack publication gate** that structurally cannot deliver an unapproved pack.
- **Honesty patterns that took root**: `UNWIRED_WORKSTATION_ROUTES` enforced in nav and palette;
  honest-disable with visible reasons on the Strategy Designer; the SEEDED/SIMULATED trust strip;
  the self-identifying IB simulator; the test-skip register with owners and review-by dates; the
  tape-aware degraded-mode probe.
- **The Alpaca adapter's recovery machinery** (client-order-id idempotency, durable content-hashed
  inbox, watermark-based post-reconnect fill replay) and the newly wired quality-monitoring
  publisher feeding six previously dead analyzers.

## Relationship to existing planning

This review **corroborates** the W9 close-out sequencing (safety → governance → ingestion) and the
W10 pull-forwards, and **confirms** the remediation plan's own prerequisite discipline (AR8-25's
warning against naive fund-economics wiring is exactly right). It **adds** what no register
currently carries: the bootstrap middleware ordering as the single blocker in front of an
otherwise-complete first-account lane; the WPF safety-seam wiring gap and the false
`W9-SAFETY-007` summary claim; the browser provenance-pin discard and the false `W9-TRUTH-001`
acceptance clause; the structurally empty reconciliation transaction population; the `"ops.gov"`
actor fabrication; multi-account OFX misattribution and the unchecked sign convention; Polygon's
fabricated sequence numbers; unmarked sub-2-minute tape gaps; the adjusted/unadjusted mixing; the
dead `PositionReconciliationService` registration; the disarmed-by-default risk rule catalogue;
the Plaid webhook verification gap; the editable-but-unenforced approval matrix; the install
smoke's account pre-provisioning; and the schema/binary rollback asymmetry. Candidates for new
registry rows rather than silent fixes: reconciliation population activation, casework actor
identity, the ingress provenance seam, and risk-rule default arming. The strongest single message
to the program: **stop accepting rows on library evidence — acceptance should require the seam,
and the seam should have a test.**
