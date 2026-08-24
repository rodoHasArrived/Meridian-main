# Adversarial Program Review — Meridian (2026-08-24)

**Status:** independent review input; not a governance or roadmap-status document
**Owner:** review author (independent adversarial pass)
**Reviewed:** 2026-08-24
**Scope:** whole-program review of Meridian's high-level functionality, focused on what a real end
user can install, run, trust, and complete — and where improvement would raise end-user value most.
**Method:** eight parallel adversarial investigations over the wired code paths (a systematic
re-test of every headline finding from the 2026-08-18 review, plus fresh passes over the first mile
and CI, the trading-safety lane, the reconciliation wedge, market-data provenance, governance and
authorization, fund accounting and the evidence chain, the browser workstation as a daily driver,
and the WPF lane with the durability floor). The 218 commits landed since 2026-08-18 were checked
against what they claim. Every finding is anchored to `file:line` at commit `780aeb9e`.

> This review is deliberately critical; a strengths section gives fair credit at the end. It builds
> on `adversarial-program-review-2026-08-18.md`, its remediation plan
> (`adversarial-review-2026-08-remediation-plan.md`), and the production-readiness backlog
> (`production-readiness-backlog-2026-08.md`), re-tests their claims, and extends coverage to areas
> those passes did not reach. Live status stays in the roadmap registry; nothing here competes with
> it.

## Headline

The lineage of headlines: 2026-07 — "the codebase is dramatically more capable than the running
product." 2026-08-10 — "acceptance statuses drift ahead of wired reality." 2026-08-18 — "the
remediation itself gets built, tested, registered — and left unwired at the last seam."

Six days and 218 commits later, this pass records a genuine turn — and names its successor disease:

> **The program demonstrably closes the seams a review names, at the named line, with real tests —
> and stops there. The defect class survives one seam over.** Every flagship built-but-dead finding
> from 2026-08-18 is now verifiably wired. And in nearly every case, the sibling defect of the same
> class ships unfixed next door.

Verified this pass, with credit: the first-account bootstrap works end-to-end
(`src/Meridian.Ui.Shared/Endpoints/LoginSessionMiddleware.cs:215-223`, with an integration test
that walks the middleware); the WPF Stop/Cancel-All buttons genuinely reach the server and report
the server's own verdict (`src/Meridian.Wpf/ViewModels/TradingWorkspaceShellViewModel.cs:512-544`);
the browser provenance pin is authoritative regardless of the demo heuristic
(`src/Meridian.Ui/dashboard/src/app-shell.data-provenance-badge.ts:108-132`); the unguarded-mutation
baseline is **zero** with a runtime pre-binding deny
(`src/Meridian.Ui.Shared/Endpoints/MutationAuthorizationGuardMiddleware.cs:82-127`); the
fixed-income 100× booking is fixed coherently from pre-trade through ledger, marks, and replay; a
live brokerage composition now fails closed at startup instead of measuring risk against the
fictional $100k book (`src/Meridian/UiServer.cs:380-386`); fresh installs arm conservative risk
rails; and the Plaid webhook has textbook signature verification.

The instance-vs-class pattern, four ways:

- **Face value was fixed; the contract multiplier was not.** `TradeExecutedEvent` now carries the
  percent-of-par flag end-to-end, but `ExecutionReport` still has no `ContractMultiplier` field —
  session replay reconstructs option fills at 1/100 of reality with `contractMultiplier: 1m` and no
  owner (`src/Meridian.Application/Services/PaperSessionPersistenceService.cs:159,820,1190`), and
  `LiveRunMetricsTracker.cs:48` still books fixed-income cash flows raw. The exact bug shape that
  was just fixed, one record away.
- **The manual kill switch sweeps; the automatic one does not.** The operator endpoint couples
  breaker activation to a cancel-all sweep, but `CompositeRiskValidator.TripCircuitBreakerAsync`
  (`src/Meridian.Risk/CompositeRiskValidator.cs:780-802`) — the path that fires when the desk is
  actually losing money — opens the breaker and leaves every resting order working.
- **`ReviewedBy` was fixed; `AssignedTo` was not.** The server now rewrites reviewer/resolver
  identity from the session (`WorkstationEndpoints.Reconciliation.cs:487,542`), but the browser's
  hardcoded `assignedTo: "ops.gov"` still persists verbatim
  (`accounting-screen.view-model.ts:4051`; `FileReconciliationBreakQueueRepository.cs:709`) — and
  the repository derives the audit *actor* as `AssignedTo ?? ReviewedBy ?? ResolvedBy`, so the fake
  user can still surface as the actor of record.
- **The browser banner was fixed; the desktop blind spot was not.** The provenance pin now reaches
  both shells, but `DegradedModeStatus.MarketDataMode` — the signal that says the *tick feed* is
  simulated — still has zero consumers in `src/Meridian.Wpf`, and a WPF Order Book that cannot
  reach the API silently swaps in a fabricated depth ladder and trade tape with no label
  (`src/Meridian.Wpf/ViewModels/OrderBookViewModel.cs:341-355,430-519`).

Where no review pointed, almost nothing moved: exactly **4 of 218 commits** touched the browser
dashboard; the fund-economics kernels enter their second consecutive review with zero production
consumers (and the orphan set grew); the `"IB"`/`"ALPACA"` ingress defaults survive their fourth
review — and now corrupt trade-condition semantics, because canonicalization decodes every
provider's condition codes through the IB table (`EventCanonicalizer.cs:47,60,74`).

## Re-test scorecard

Of the 2026-08-18 review's headline findings re-tested against current code:

| Verdict | Count | Items |
| --- | --- | --- |
| Fixed / largely fixed | 10 | first-account bootstrap (code + integration test); browser provenance pin; demo-probe one-shot → retry + honest `unknown`; WPF Stop/Cancel-All wiring; runtime authorization deny with all three ratchet baselines empty (58 unguarded mutations → 0); Plaid webhook verification; fixed-income face-value booking; live-book fail-closed refusal; installer PostgreSQL payload documented + CI-materialized; `/health` healthcheck exemption |
| Partially fixed | 14 | install smoke (still pre-provisions the account); README honest but `docs/start/README.md` still claims in-memory fallback; credential tooling (token documented; still no `--hash-password`/`--create-user`; 503 strings unchanged); `--seed-demo` posture (loopback-contained, now silently grants anonymous **Admin**); release evidence (first tag, first installer success, first green certification — still zero production releases); kill-switch sweep (hardened, still in-memory-only; browser can cancel-all but cannot trip the breaker); risk rails (armed for **fresh** installs only; browser edits only drawdown; bracket legs bypass); casework actor (`ReviewedBy` fixed, `AssignedTo` not); reconciliation population (positions/cash real, transactions still `[]`); break queue (hardened file store, no DB variant); tenancy (real scoping landed; write gate still default-off, registry allows on exception, null-tenant rows pass); API key (ReadOnly default + method cap; still one shared key, actor `"api-key"`); WPF provenance (workspace pin consumed; degraded-mode feed signal not); `Meridian.Setup.Tests` (wired to the installer lane, not to PR) |
| Still open | 24 | `MarketEvent` `"IB"`/`"ALPACA"` defaults + sourceless collectors (4th review); Polygon fabricated sequences (now generalized: `QuoteCollector` overwrites all providers' quote sequences); unmarked sub-2-minute reconnect gaps; adjusted/unadjusted mixing + `Source` erased at read; dead quality monitors / SLA ingress never called / cross-validation disabled at every site; WPF fake "1.6.1" version check + fabricated activity feed (4th review); paper slippage 0 / no partial fills / no staleness-hours-halt bounds; typeable promotion evidence (walk-forward still WPF-only, live-target exemption, no parameter hash); Alpaca quiet-session first-order rejection + synthetic `Cancelled`; Lean fabricated lifecycle; OFX charset/multi-account/sign + whole-file dedupe + drift inert for built-ins; `ReconciliationMatchingEngine` unregistered; 16 of 20 casework verbs dark; fund-economics kernels zero consumers; FX refused, fees un-prorated; journal-entry evidence dead-end (new dead-end kinds shipped); string-cell exports; no OCR; approval matrix editable-but-unenforced (SoD hardcoded in ≥7 sites); journal DB-mutable (`on delete cascade` intact); per-user rate limiting dead; operator inbox absent in production; broken `deploy/` manifests; ungated auto-migrations with no downgrade guard |

Program-level deltas worth banking before the findings: the **first green scheduled Production
Certification** on main (2026-08-23: real Postgres, integration suites, backup/restore drill,
anti-skip gate), the **first successful installer pipeline run ever** (run #17 after 16 diagnosed
failures, producing the first `Meridian-Setup.exe`), the **first release artifact** (prerelease
`eval-v0.1.0-eval.1`, honestly labeled), and an authorization estate whose three ratchet baselines
are all empty with a runtime deny behind them. The remediation plan self-reports 7 of 60 items
done; what actually landed (the three flagship seams plus the seven "alpha blocker" fixes) is
larger than that count suggests — and narrower than the estate needs.

## 1. The first mile: the door now opens onto an empty shelf

The bootstrap lane is genuinely fixed, and the packaged flow is coherent end-to-end (supervisor
mints a one-use token, opens `/setup/account#token=…`, the endpoint enforces loopback + ≥12-char
password + refuses once any account exists). What remains is distribution and honesty around it:

- **The only downloadable artifact dead-ends for its audience.** The sole release ships the WPF
  MSIX *without* the lifecycle supervisor, bundled PostgreSQL, or any bootstrap-token flow; an
  evaluator who installs it lands on required auth with zero users and the message "Set `MDC_USERS`
  with `passwordHash` values" — with no shipped hashing tool
  (`src/Meridian.Wpf/Services/DesktopAuthenticationSession.cs:67-69`). The artifact that solves all
  of this — `Meridian-Setup.exe`, which CI can now build — is attached to **no release**; it exists
  only as a CI artifact from run #17.
- **The certified path is not the shipped path.** The install smoke still pre-provisions
  `MDC_USERNAME`/`MDC_PASSWORD_HASH` before logging in
  (`build/scripts/install/smoke-web-workstation-install.ps1:415-422,438-452`); no install-level
  lane exercises the zero-account `/setup/account` flow the packaged product actually uses.
- **First-mile regression coverage runs weekly, not per PR.** 38 of 50 endpoint test files —
  including the bootstrap regression suite — carry `Category=Integration`, which the PR gate,
  push lane, and nightly all filter out; only Sunday's certification runs them. A PR
  reintroducing exactly the dead-bootstrap class of bug merges green and is caught up to 7 days
  later. These are in-proc TestServer tests; nothing technical keeps them off the PR gate.
  Similarly, `demo-smoke.yml` — the one workflow that boots the real host with the documented
  command — is path-filtered to demo-adjacent files and does not trigger on changes to
  `LoginSessionMiddleware`, `StorageFeatureRegistration`, or `UiServer`.
- **`docs/start/README.md` still tells the pre-guard story**: line 142 lists bare
  `--mode workstation` as a working launch and lines 153-161 claim in-memory fallback with a red
  banner — a launch that throws `InvalidOperationException` at composition. (Top-level `README.md`
  is fixed and honest.) The 503/login strings still point at `MDC_USERS` with no recipe and never
  mention the bootstrap path that now exists (`LoginSessionMiddleware.cs:314-317`).
- **`--seed-demo` now silently grants anonymous Admin.** `DemoWorkspaceCli.cs:147-150` auto-sets
  `MDC_ANONYMOUS_ROLE=Admin` alongside the existing silent posture relaxations, with no console
  disclosure. Loopback binding contains it; the silence is the problem.
- `deploy/` remains a guaranteed failure: the Dockerfile copies 12 of the needed project files
  (missing Audit, Backtesting, Identity, QuantScript ×2 — `deploy/docker/Dockerfile:24-38`) and
  `install.sh --docker` builds exactly that Dockerfile, so the interactive Linux install path
  costs an evaluator their first hour. Migrations still auto-run at startup with no operator gate
  and no schema-newer-than-binary guard — MSIX rollback is drilled, schema rollback does not
  exist; that is the dangerous combination.

## 2. Trading safety: the switch works when a human pulls it

The safety lane made the most verifiable progress of the period — and its remaining gaps
concentrate at exactly the moments no human is watching:

- **An automated critical trip does not sweep the book.** Only the operator HTTP endpoint couples
  breaker + cancel-all (`ExecutionEndpoints.cs:444-460`); `TripCircuitBreakerAsync` fired by a
  drawdown or exposure breach opens the breaker and leaves resting orders filling until someone
  notices (`CompositeRiskValidator.cs:780-802`).
- **Bracket children are invisible to every safety system.** TP/SL legs are metadata passthrough,
  never risk-validated (`AlpacaBrokerageGateway.cs:1108-1126`); their broker-side IDs are never
  registered, so their execution reports are dropped as "not tracked by this OMS"
  (`OrderManagementSystem.cs:1401-1406`). A sweep can truthfully report the OMS book empty while
  live TP/SL legs rest at the broker. The sweep itself still enumerates only the in-memory
  dictionary (`OrderManagementSystem.KillSwitch.cs:43-48`) — `IBrokerageGateway.GetOpenOrdersAsync`
  exists and feeds the readiness gate, but never the sweep, so a post-restart kill switch reports
  `Empty` over a loaded broker book.
- **The browser still cannot trip the breaker** (cancel-all is wired with confirm + ack; the
  breaker route constant has zero dashboard callers), and the WPF Stop discards the sweep verdict
  it is handed — `HaltTradingAsync` checks only `CircuitBreaker.IsOpen` and shows success-toned
  copy while 12 orders may still be working (`ExecutionSafetyControlClient.cs:92-94`). The lane
  that renders evidence can't halt; the lane that halts doesn't render the evidence.
- **Upgraded installs stay disarmed.** First-run rail arming is skipped whenever any persisted
  snapshot loads (`RiskRuleRuntimeService.cs:154-157`) — a snapshot from the disarmed era
  deserializes with null rails and wins forever. The exact population the prior review flagged
  gets no benefit from the fix; nothing prompts them. The browser risk panel still edits only the
  drawdown threshold (`risk-control-panel.view-model.ts:84,201`).
- **Live trading is now honestly unavailable rather than dishonestly available.** The fail-closed
  throw at `UiServer.cs:380-386` is the right interim answer, but no production
  `IBrokeragePositionSync` implementation exists and `PositionReconciliationService` is still
  registered in no host — the product's headline live-trading capability structurally cannot be
  turned on with measured risk.
- Paper realism is unchanged where it counts: slippage defaults 0 with no shipped config while
  backtests default 5bps (paper systematically beats backtest), partial fills are unmodeled by
  policy, `PaperMarketObservation` still has no timestamp, and there is no market-hours or halt
  concept in `src/Meridian.Execution` — the 3am-Saturday market order still fills at Friday's
  cached print. Promotion evidence is still typeable (walk-forward numbers from the request body,
  `SourceReference` never dereferenced, live targets exempt from the retained-evidence
  cross-check, no parameter-set hash on the promotion record — the docstring claims one). The
  Lean endpoints still fabricate lifecycle. Alpaca still rejects the first order of a genuinely
  quiet session and still emits synthetic `Cancelled` on DELETE 2xx.

## 3. The reconciliation wedge: two guaranteed-break machines

The middle verb of "prove, book, reconcile, approve, report" is still structurally broken — now in
two independent ways, and the noise now carries governance weight:

- **The transaction population is still deliberately empty.** Positions and cash now match against
  genuinely retained internal records (real progress), but
  `RetainedInternalReconciliationPopulationProvider.cs:89` still returns `[]` for ledger
  transactions — every imported trade, fee, and dividend row breaks on every import. The
  institutional `ReconciliationMatchingEngine` remains registered nowhere; the live path runs the
  simpler `StatementRunMatcher`.
- **New: OFX bank movements are classified as cash *balances*, not transactions.** The built-in
  profile maps every `STMTTRN` type to activity `"cash"`
  (`StatementBuiltInProfiles.cs:127-140`), which `StatementRunMatcher.Classify` routes into the
  CashBalance lane (`StatementRunMatcher.cs:333-348`) — so each movement becomes an "ending
  balance" that can never match (mid-period balances fail the period-end identity check by
  construction). Every bank transaction in an OFX file is a guaranteed `CASH_UNMATCHED` break,
  mislabeled, mis-tolerated, and unreachable by the FITID rule.
- **The noise now blocks the close with authority.** The intake authority publishes every open
  break into the governed queue with `BlockedOutputs: ["FinalReport","PeriodClose",
  "ClientDelivery"]` (`StatementReconciliationIntakeAuthority.cs:277-299`,
  `ReconciliationBreakQueueProjection.cs:88-90`), and close readiness treats open cases as
  blockers. A routine 500-transaction custodian statement creates ~500 PeriodClose blockers per
  import — and with dedupe still whole-file, an amended statement doubles them.
- **Real-world matching still breaks on real-world data.** Cash/position identity requires exact
  as-of-date equality with the internal record's own stamp
  (`StatementMatchingEngine.cs:493-502`) — a statement closing Sunday the 31st against a book last
  stamped Friday the 29th produces false breaks on *both* sides despite agreeing amounts. The live
  path silently truncates tolerance profiles to rule\[0\] and drops basis-point rules entirely
  (`StatementRunMatcher.cs:296-307`). OFX charset, multi-account attribution (everything booked to
  the first `ACCTID`), and the unchecked DEBIT sign convention are all unchanged — while the new
  CAMT.053/BAI2 connectors implement fail-closed sign handling, proving the team knows how.
- **Casework governance has holes at the edges.** A legacy `POST …/breaks/bulk` endpoint mutates
  via direct repository save, bypassing the state machine, rationale requirements, and the
  governed bulk machinery — and records no operator identity at all (the resolved `actor` variable
  is computed and never used — `WorkstationEndpoints.Reconciliation.cs:560-613`). There is no
  reconciliation permission: break mutation rides on `AdminMaintenance | ManageDirectLending |
  ModifySecurityMaster` (`WorkstationEndpoints.cs:4089-4094`), so granting someone security-master
  edit silently grants break resolution. Close-readiness account scoping relies on a substring
  heuristic (`BreakId.Contains(accountIdCompact)`) that can never match the current SHA-256 break
  ids (`FundAccountCloseReadinessService.cs:169-175`).

## 4. Market-data truth: labeled at the edges, misattributed at the core

The label layer kept improving — the pin, the retry-not-brand probe, the seeded/simulated badges —
while the attribution seam under it survives its fourth review and now does semantic damage:

- **Every trade is still durably "IB", every quote "ALPACA"** (`MarketEvent.cs:15,32,38`; three of
  four collectors publish sourceless), the storage layer still partitions the durable tape by that
  fiction, the newly wired per-provider quality metrics attribute gaps and latency to the wrong
  vendors — and **canonicalization resolves venue and trade-condition mappings by `raw.Source`**
  (`EventCanonicalizer.cs:47,60,74`), so Polygon/Alpaca/NYSE condition codes are decoded through
  the IB table in the tier explicitly marketed as trustworthy.
- **Sequence integrity is now fictional for everyone.** Polygon still fabricates sequence numbers
  client-side; `QuoteCollector.cs:55-58` now *overwrites* any provider-supplied quote sequence
  with a local counter, so quote-stream gap detection can never fire for any provider.
- **Known coverage holes leave no mark on the tape.** Sub-2-minute reconnect gaps are skipped at
  Debug (`AutoGapRemediationService.cs:461-468`); a mid-session provider failover logs to Serilog
  and publishes no integrity event (`FailoverAwareMarketDataClient.cs:513-626`); malformed JSONL
  lines and torn compressed tails are swallowed at read (`JsonlMarketDataStore.cs:113-139`). A
  stored day with an outage, a vendor switch, and a truncated file reads back as a continuous,
  single-vendor, clean tape.
- **Adjusted and unadjusted history still mix silently** (Finnhub/NYSE unadjusted vs everyone else
  adjusted; the composite can switch regimes mid-series for a split symbol;
  `AdjustedHistoricalBar.ToHistoricalBar()` erases the regime it was built to carry), and the
  composite backfill path stamps `evt.Source = "composite"`, erasing the winning vendor from the
  envelope and the disk layout. Quality monitors that are wired use fixed-UTC market hours —
  correct only during EDT, so freshness and completeness numbers are systematically wrong four-plus
  months a year (`DataFreshnessSlaMonitor.cs:42-47,339-351`).
- **Synthetic streaming does not trigger the pin.** Provenance is forced only for in-memory stores
  or explicit declarations; a durable-store host running the Synthetic streaming source resolves
  **Real** — only the browser degraded-mode banner covers it, and WPF consumes that signal not at
  all. Combined with the WPF Order Book's unlabeled fabricated ladder, the co-equal desktop lane
  can show an operator a fully synthetic market with zero warnings.

## 5. Governance: the ratchet closed; the paper controls are still paper

The W9-GOV-008 burn-down completed and it is real: a pre-binding runtime guard refuses undeclared
mutating routes 403 fail-closed before any body byte is parsed, all three baselines (unguarded
mutations, undeclared mutations, undeclared reads) are empty, and a two-way drift lock ties the
tested and enforced exemption lists together. The residual estate is now the controls that *look*
enforced:

- **The approval-policy matrix is still editable theater.** Persisted rules are read only by the
  settings display; segregation-of-duties remains independently hardcoded in ≥7 sites. An admin
  tightening `requiredDistinctApprovals` changes a JSON file and nothing else — the most deceptive
  surface in the product for a controller and their auditor.
- **The journal is still mutable at the database.** No immutability trigger on
  `journal_entries`/`journal_legs`, `on delete cascade` intact, no entry-level debits=credits
  constraint — while the team's own trigger pattern protects tax lots two migrations away.
  Migration 029 (currency backfill) itself `UPDATE`s `journal_legs` in place, possible precisely
  because nothing at the DB blocks it. The file-backed accounting action audit is unchained; the
  "tamper-evident" fund-administration event log is still a hash chain over an in-memory list.
- **Tenancy still fails open at every default** (write gate off unless
  `MERIDIAN_FUND_SCOPED_WRITE_TENANT_REQUIRED=true`, registry guard allows on exception, read
  predicate passes `tenant_id is null` rows, fund-structure store tenancy-blind) — though real
  scoping work landed (collateral partitioning, report explorers with authorize-before-cap, and
  the team caught and fixed its own bypass within a day). New: ownership is claimed by first
  successful write, so in a shared deployment a tenant can squat an unbound fund id by writing
  first.
- **New regression: `/metrics` and `/health` are now fully unauthenticated** (added to both
  middleware exempt lists by the alpha-blockers commit) while the route's `DeclareOpenRead` reason
  still asserts they sit behind "the session or API key a configured deployment already requires"
  (`StatusEndpoints.cs:110`). `/metrics` enumerates up to 100 symbol-labeled counters — the
  deployment's watched/traded universe — to any network caller. The declaration ratchet checks
  that a reason exists, never that it is true; this is the drift class it cannot catch.
- The enforcement sweep is blind to production-only routes (the fixture maps three endpoint
  groups; `POST /api/system/shutdown` carries an exemption marker that is not in the allowlist —
  provably unseen by the sweep), `RemoteIpAddress == null` passes the loopback checks (a
  misconfigured proxy degrades "loopback-only" to token-only), per-user rate limiting is still
  dead (`HttpContext.User` is never populated; one office NAT shares one 10/min mutation budget),
  and the operator inbox is still absent in production — period-close sign-off requests from the
  ledger book service are silently dropped in exactly the composition where they matter.

## 6. Fund accounting: the spine is excellent; the storefront is still empty

- **The advertised economics remain a zero-consumer library — second consecutive review.**
  Waterfall, preferred return, clawback, equalization, NAV-per-unit, shadow-NAV, capital-call
  builders: no reference outside `src/Meridian.Ledger/` except tests, no DI registrations, no
  endpoint, no screen. The orphan set *grew* (`MultiCurrencyLedgerTranslator` — the only FX
  revaluation code, `WaterfallMarkPriceSource` — the only multi-tier pricing,
  `ShadowBookValuationService` — the only real shadow-NAV recompute,
  `PartnershipInvestorAccountingProjector`). FX is still refused at posting (rate-1 functional
  only); the wired fee producer still books flat un-prorated `BeginningNav × rate` with no
  day-count, hurdle, or crystallization.
- **New: the one wired accrual lane silently under-accrues.** `DailyAccrualWorker` computes only
  *today*, skips anything already-accrued, and never backfills: worker downtime, a transient
  failure, or a period-blocked day (routed to an inbox that doesn't exist in production, marked
  failed, never retried) permanently drops interest days with no gap detector
  (`DailyAccrualWorker.cs:47-104,190-229`). This is the one place in the wired accounting surface
  where the numbers are silently *wrong* today — precisely the failure mode "proven numbers"
  promises away.
- **New: close-readiness components vouch for checks they don't run.** The "Pricing" component
  passes on the Security-Master gate alone — valuation coverage is never inspected — and "Cash" is
  byte-identical to "Positions" (`OperationsContinuityWorkflowService.cs:2646-2717`). A close can
  score 100 with "pricing and valuation coverage complete" backed by zero distinct evidence.
  Similarly, the fund-operations "Shadow NAV variance" tile compares reported NAV against net
  securities exposure — structurally nonzero for any real fund — while the real recompute engine
  sits unwired (`FundOperationsWorkspaceReadService.cs:2906,3044`).
- **The proof chain still dead-ends at its most important link, and the dead-end grew.** The UI
  links journal entries to evidence subject kind `journal-entry`, which `EvidenceSubjectResolver`
  still does not support; system-posted entries still show no line detail, lifecycle, or evidence.
  Three more unresolvable kinds shipped since (`accounting-exceptions`, `reconciliation-break`,
  `run`). And the evidence workbench's headline "chain coverage %" is keyword bucketing — nodes
  are classified into the nine proof layers by substring matching on ids and summaries
  (`EvidenceProofChainBuilder.cs:202-228`); a "Ready" chain asserts each bucket contains
  *something*, not that any figure traces through.
- Deliverables: still only partners' capital exports typed numerics; every other statement ships
  pre-formatted strings a controller cannot sum (the code comment still concedes it). Evidence
  extraction is still hand-keyed ceremony. Genuine credit: the posting spine held and hardened —
  dispatch now re-verifies release authorization at the moment of transport against a content
  digest with audit-chain verification, synthetic marks can no longer impersonate Level 1 fact,
  and an append-path currency data-loss bug was root-caused and repaired with an exemplary
  evidence-gated backfill (migration 029).

## 7. The workstations: one fixed line, and the daily driver stood still

- **Browser: 4 of 218 commits, one behavioral change** (the provenance pin — the right one). The
  activation ratio is on its third review without a gate: 125 of 426 API clients (29.3%) are
  unreachable from any screen, including all user administration, admin maintenance/retention, the
  operations-continuity ledger lane, all Strategy Designer clients, and the entire casework verb
  set plus both bulk clients. The named silent-failure sites are unchanged at the same line
  numbers (`accounting-screen.tsx:2131`; `asset-detail-screen.tsx:249-251`; explorer static
  downgrades), `AsyncRegion` adoption is still 1 of 68 screens, period lock still fires on a
  single unconfirmed click, breaks/approvals/inbox still poll on 30-60s loops, and
  `settings-screen.tsx` is byte-identical at 7,391 lines.
- **New: the journey breaks right after the work.** Resolving a break patches only the local queue
  row; the close cockpit refreshes only on mount, so the operator who just cleared the blocking
  break opens the cockpit and still sees it blocking (`accounting-screen.view-model.ts:4086-4087`;
  `close-cockpit.view-model.ts:192-193`). The close-adjacent governance panel substitutes stale
  data for failure. Error taxonomy has no retryable/fatal distinction; `FreshnessChip` — a good
  primitive — is adopted by 5 of 68 screens, and the trading blotter, break queue, and data grids
  show no staleness signal at all.
- **WPF: the safety seam fix is exemplary and the lane around it undermines it.** The Order Book
  fabricates a realistic ladder and tape on any API failure, unlabeled, while the connection dot
  shows "Connected" (`OrderBookViewModel.cs:341-355,430-519`) — on the screen whose stated purpose
  is pricing orders. The fake "1.6.1" update check and the fabricated "Cloud sync completed"
  activity feed survive their fourth review (`SettingsViewModel.cs:860-889`) while the assembly
  version is actually 1.0.0. The accounting lane **forks product state**: fund accounts,
  fund structure, journal drafts, valuation schedules, and an evidence store all live in
  `%LOCALAPPDATA%` JSON with in-process schedulers whose ledger dependencies resolve null
  (`AccountingFeatureModule.cs:54-122,162-194,280`) — one screen shows breaks from the server API
  beside "open breaks" computed from a desktop-local fund universe. This directly contradicts the
  co-equal-lanes contract ("neither client forks product state"). And the ~1,697 WPF tests run
  only in a path-filtered Windows lane that the authoritative `quality-gate` does not require —
  a shared-contract change can ship a broken desktop green.

## 8. Verification: the gate is real; the seams it skips are the ones that failed

Credit first: the PR quality-gate runs the .NET sweep, browser, docs, and workflow lanes; the
skip register is governed; the weekly certification is green with real infrastructure; the
installer lane executes `Meridian.Setup.Tests`; `demo-smoke` runs the literal README command with
env-stripping self-skepticism. The residual theater is precisely shaped like this review's
findings: the install smoke certifies a credential path the packaged product doesn't use; the
first-mile auth/bootstrap suites gate nothing until Sunday; the dashboard Playwright smoke still
mocks the entire API, filters 404 console errors, and visits one URL — none of the 48 routes is
mounted by any test against a real backend; the WPF lane is outside the merge gate; the duplicate
legacy `ci.yml` still carries its PR-skips; and the declaration ratchet verifies that reasons
exist, not that they are true.

## Acceptance-drift ledger

Claims that current code contradicts, updated this pass:

| Claim | Where claimed | Wired reality |
| --- | --- | --- |
| "Pricing and valuation coverage complete" / "cash coverage complete" close-readiness components | close-readiness scoring | Pricing passes on the Security-Master gate alone; Cash is byte-identical to Positions (`OperationsContinuityWorkflowService.cs:2646-2717`) |
| `/metrics`/`/health` sit behind "the session or API key a configured deployment already requires" | `StatusEndpoints.cs:110` open-read declaration | Both fully unauthenticated in both middlewares since the alpha-blockers commit |
| In-memory fallback with red banner for bare `--mode workstation` | `docs/start/README.md:142,153-161` | Default Production launch throws at composition (`StorageFeatureRegistration.cs:625-665`) |
| 92/97 WPF screens "Evidence current" | `docs/status/wpf-screen-development-tracker.md` | Tracker measures registry entry + screenshot + text-scan test reference; "done" screens include the fabricating Order Book and the desktop-local accounting fork |
| Evidence workbench "chain coverage %" | evidence workbench UI | Substring-keyword bucketing over evidence ids/summaries, not lineage (`EvidenceProofChainBuilder.cs:202-228`) |
| Installer doc's "supported release artifact" | `docs/operators/browser-workstation-installer.md:17-21` | No release carries `Meridian-Setup.exe`; the only release is an MSIX that lands on a locked door |
| Promotion record "pins … parameters" | `BacktestToLivePromoter.cs` docstring | The record has no parameter-set hash field |
| "Shadow NAV variance" tile | fund-operations workspace | Reported NAV minus net securities exposure — structurally nonzero; the recompute engine has zero consumers |
| Unitized NAV and real fee/waterfall/capital-call economics | `W9-NAV-006` (ready_for_acceptance) | Zero production consumers, second consecutive review; orphan set grew |

## Prioritized improvement list (by end-user value uplift)

1. **Make the reconciliation wedge produce a break list an operator can trust.** Fix the OFX
   `"cash"` misclassification so movements flow through the transaction lane; feed the
   ledger-transaction population (the period-scoped journal→custodian projection the provider's
   own remarks specify) or stop publishing guaranteed `TRANSACTION_UNMATCHED` rows as
   PeriodClose-blocking casework; add FITID/row-level idempotency so amended statements re-open
   only changed rows; relax cash/position identity to at-or-before as-of dates; honor full
   tolerance profiles on the live path. Until this lands, every real statement import makes the
   product look broken, floods the close with false blockers, and buries the breaks that matter —
   this is the flagship persona's flagship loop.
2. **Finish the kill switch as a broker-truthful system.** Sweep the union of the in-memory book
   and every gateway's open orders (the API exists and already feeds the readiness gate); couple
   `TripCircuitBreakerAsync` to the same sweep so automated halts cancel the book; register
   bracket child IDs with the OMS and risk-validate their limbs; give the browser its breaker
   trigger; surface the sweep verdict in the WPF success copy; arm the rails for upgraded installs
   (or surface a blocking "rails unconfigured" readiness item). The control a user pulls during an
   incident must not report `Completed` over a loaded broker book.
3. **Ship the release and certify the real first-run.** Attach `Meridian-Setup.exe` (now
   buildable) to an `eval-v*` release; convert the install smoke to drive the zero-account
   `/setup/account` token flow instead of pre-provisioned env credentials; promote the
   auth/bootstrap Integration suites (or a first-mile subset) into the PR gate and widen
   `demo-smoke`'s path filter to the middleware/composition files; fix `docs/start/README.md` and
   point the 503/login strings at the bootstrap path (or ship the ~20-line `--hash-password`
   verb). Until a real end user can download and reach the product, every other improvement is
   invisible.
4. **Make the wired numbers right before wiring new ones.** Fix `DailyAccrualWorker` to iterate
   `LastAccrualDate+1..today` with retry of period-blocked days and an accrual-gap readiness
   check; carry `ContractMultiplier` and owning account on the durable fill record (the exact
   pattern just applied to face value) and fix `LiveRunMetricsTracker`'s raw cash flows; make the
   close-readiness Pricing/Cash components measure what they claim or say what they measure.
   These are the places where the product's numbers are silently wrong *today* on wired paths.
5. **Activate one fund-economics kernel end-to-end — or re-status the row.** Highest-leverage
   first wire: capital-call issuance (`CapitalCallDraftFactory` → the existing
   `AutomatedJournalIntakeRunner` approval queue; event kinds and projector plumbing already
   exist) or NAV-per-unit + the unit register behind the existing valuation lane; wire
   `MultiCurrencyLedgerTranslator`'s FX revaluation into period close. Second consecutive review
   at zero consumers: either the seam ships or `W9-NAV-006` should not read `ready_for_acceptance`.
6. **Fix provenance at the ingress seam — the class, not the instance.** Require a source on
   every `MarketTradeUpdate`/`MarketQuoteUpdate` (the `L3OrderBookCollector` pattern), delete the
   `"IB"`/`"ALPACA"` factory defaults, stamp the winning inner provider through the composite,
   stop overwriting provider sequences in `QuoteCollector`, and publish integrity events for
   reconnect gaps, failover windows, and read-side corruption. One seam corrects the durable disk
   layout, the quality metrics now shown in the UI, canonicalization's condition/venue semantics,
   and replay fidelity simultaneously. Add `IsAdjusted` + `Source` to bars end-to-end and refuse
   or label mid-series regime switches — the highest-stakes silent-wrongness a trading platform
   can carry.
7. **Stop fabrication on operator surfaces.** Delete the WPF Order Book's demo fallback (render
   the existing empty state) or gate it behind fixture mode with the shared badge; delete the fake
   "1.6.1" check and the fabricated activity feed (fourth review — a ten-minute fix that keeps
   failing the truth-discipline claim); replace the browser's named value-fallback catches with
   the already-built `RegionErrorState`; add the armed-confirm idiom to period lock and schedule
   deletion; consume the degraded-mode signal in WPF.
8. **Attribute work to real people, everywhere.** Server-rewrite `AssignedTo` like `ReviewedBy`;
   drop `"ops.gov"`/`"operator"` literals client-side; delete or receipt-gate the legacy `/bulk`
   bypass; remove the request-body actor fallback on SoD-relevant ledger paths
   (`LedgerEndpoints.cs:2141-2144`); introduce a reconciliation permission so break casework stops
   riding on security-master write grants; then light the 16 dark casework verbs with the
   case-detail drawer + bulk actions the backlog already specifies (AR8-15) — "show my breaks"
   becomes possible for the first time.
9. **Close the governance floor where it is still paper.** Route the ≥7 hardcoded SoD checks
   through the persisted approval matrix or render its editor honestly inert; apply the
   V_ledger_027 trigger pattern to `journal_entries`/`journal_legs` (drop cascade, entry-balance
   constraint); default tenancy enforcement on when any multi-tenant signal exists; give the
   operator inbox a durable production store (period-close sign-off requests are currently
   dropped); fix the `/metrics` declaration-vs-posture drift; populate `HttpContext.User` so
   per-user rate limiting exists.
10. **Close the loop in the daily driver and gate the ratios.** Refresh the close cockpit,
    reconciliation summary, and control tower after break mutations (or route break/approval/inbox
    events over the existing SSE machinery instead of 30-60s polls); adopt `FreshnessChip` on the
    blotter, break queue, and data grids; add the orphan-export structural test so the 29% dark
    ratio finally has a gate; un-fork the WPF accounting lane onto the shared API clients that
    already exist (or badge its local-first surfaces); add a Windows WPF-test job to the
    quality-gate's required checks; mount the 48 browser routes in at least one non-mocked smoke.

Quick wins worth doing this week regardless of sequencing: the WPF fake version-check/activity
feed deletion; `assignedTo` session rewrite; confirm dialog on `lockClosePeriod`; the `/metrics`
declaration one-liner (or an auth decision); attach the already-built installer to a release;
`docs/start/README.md` corrections; a `PaperTrading:Costs` sample block with non-zero slippage;
delete-or-`501` the Lean endpoints; add `journal-entry` to `EvidenceSubjectResolver.SupportedKinds`
so the proof chain's last hop stops 404ing; disclose `--seed-demo`'s posture (including anonymous
Admin) in its console output.

## What is genuinely strong (do not regress it)

- **The program can close seams when they are named — and closes them well.** The bootstrap flow
  (loopback + one-use token + refuse-once-configured + fail-closed at the endpoint, with a test
  named for the prior failure mode); the WPF safety command pattern (outcome-not-invocation,
  verdicts passed through verbatim, disabled-with-reason for unwireable controls, tests that hunt
  the dead-seam failure mode); the pre-binding mutation guard (decides before any body byte,
  mirrors filter bodies, fails closed even for AdminMaintenance, two-way drift lock). These are
  patterns the rest of the estate should copy, not just fixes.
- **First real release evidence**: green scheduled Production Certification (real Postgres,
  integration suites, encrypted backup/restore drill with RPO/RTO bounds, anti-skip gate); the
  installer lane's runs #8–#17 debugging trail shows root-cause engineering integrity; the eval
  prerelease is honestly labeled with SBOM and checksums; `demo-smoke` distrusts its own
  TestServer and runs the literal documented command.
- **The governed posting spine** — single guarded append path under a row lock, the period
  matrix, residual-checked atomic hard close, evidence-gated reopen, the fee tie-out resolver that
  refuses to accrue against numbers it cannot reconstruct — plus two new hardenings: dispatch-time
  re-verification of release authorization against a content digest, and provenance-aware fair-value
  leveling so synthetic marks can no longer impersonate Level 1 fact or validate a deliverable NAV.
- **The statement-run workflow's evidence engineering** — checkpointed stages, hash-verified
  artifacts, deterministic break ids, immutable casework envelopes with chain validation — and the
  new CAMT.053/BAI2 connectors' fail-closed sign handling. The import wizard (preview → commit,
  tenant- and ownership-checked) is a real operator surface.
- **The OMS submit pipeline and durable breaker** (reservation settlement on every path,
  fail-closed latching, close-only bypass semantics that refuse crossing through flat), and the
  fixed-income fix's engineering quality (hash-stable serialization, replay-stable ids, gateway
  classification parity, a 376-line regression suite).
- **Truth-discipline machinery that works where wired**: the provenance pin honored on both
  shells, `unknown`-with-retry instead of false claims in either direction, `UNWIRED_WORKSTATION_ROUTES`
  enforced in nav and palette with per-route rationale, the self-identifying IB simulator, the
  governed skip register, migration 029's corroborating-evidence repair discipline, and the
  browser's quotes-SSE spine (owner/follower connection sharing, fail-closed, bounded re-probe).
- **Session/CSRF hygiene remains strong** after the auth burn-down (method-based CSRF still
  covers POST-reads; anonymous principals get the read-only method cap; API keys cannot inherit
  user scoped assignments or administer accounts), and the DuckDB workbench is properly sandboxed.

## Relationship to existing planning

This review **corroborates** the production-readiness backlog's ordering and verifies its item 1
("wire the three built-but-dead fixes") actually landed — all three, properly, with tests. It
**confirms** the W9 close-out sequencing and the remediation plan's prerequisite discipline. It
**adds** what no register currently carries: the automated-trip/sweep decoupling; bracket-children
invisibility; the contract-multiplier replay gap and `LiveRunMetricsTracker`'s raw fixed-income
cash flows; snapshot-era installs keeping disarmed rails; the OFX cash-balance misclassification
and the break-flood's PeriodClose authority; the exact-date matching brittleness and truncated
tolerance profiles; the legacy bulk-endpoint governance bypass and the missing reconciliation
permission; canonicalization keyed off fabricated sources; unmarked failover/corruption windows;
DST-broken quality clocks; the `/metrics` declaration drift and the sweep's blindness to
production-only routes; first-writer tenancy claiming; `DailyAccrualWorker`'s dropped days; the
proxy close-readiness components; the keyword-bucketed proof chain; the shadow-NAV category error;
the WPF Order Book fabrication and the desktop-local accounting fork; the WPF lane's absence from
the merge gate; the release that dead-ends and the weekly-only first-mile coverage. Candidates for
new registry rows rather than silent fixes: the automated-sweep coupling, the reconciliation
population/classification pair, contract-multiplier persistence, accrual gap catch-up, and the
WPF state un-fork.

The strongest single message to the program: **the review-fix loop now works — point it at
classes, not instances.** When a seam is named, fix it, then sweep for its siblings (the same
default, the same fallback, the same missing field, one record or one shell over) and add the
class-level test in the same change. The prior review asked for acceptance to require the seam;
this one asks for remediation to require the sweep.
