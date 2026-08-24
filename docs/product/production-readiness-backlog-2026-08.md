# Production-Readiness Backlog (2026-08)

**Status:** active working plan; not a governance or roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-08-18
**Source:** the ranked improvement list of the
[Adversarial Program Review (2026-08-18)](adversarial-program-review-2026-08-18.md), which re-tests
the [2026-08-10 pass](adversarial-program-review-2026-08.md) and its
[remediation plan](adversarial-review-2026-08-remediation-plan.md) against the ~321 commits landed
since. Independently re-verified against current source on 2026-08-18.

This is the ten-item production-readiness ordering of that estate. Every claim below was re-checked
against the working tree before being recorded; where a review-era claim went stale, the correction
is stated rather than repeated. `AR8-nn` references point into the remediation plan, which keeps
the code-ready implementation detail. Live roadmap truth stays in `docs/roadmap/data/*.yml`.

The 2026-08-18 review's headline is the frame for item 1: **the remediation itself now exhibits the
program's core disease — the fix gets built, tested, registered, and left unwired at the last
seam.** All three of its flagship examples are closed by this branch.

**Landed with this document** (branch `claude/production-readiness-backlog-bjz32o`): the three
built-but-dead fixes in item 1 — bootstrap ordering, the WPF safety seam, and the browser
provenance pin — each with regression tests.

---

## 1. Wire the three built-but-dead fixes — **DONE in this change**

Three capabilities were fully built, registered, and tested — and reachable from nothing. Each was
completed here by connecting the last seam:

- **Bootstrap ordering** *(AR8-01a)* — `LoginSessionMiddleware.InvokeAsync` returned the
  unconfigured 503 before its own `/setup/account` and `/api/auth/bootstrap` exemptions, so a
  fresh install could never create its first account even though the whole bootstrap pipeline
  (supervisor token → setup page → `InitialAccountBootstrapService`) existed and was mapped.
  **Fixed:** the bootstrap surface is exempted inside the fail-closed branch; the endpoints keep
  their own gating (loopback-only, fixed-time one-use `MDC_BOOTSTRAP_TOKEN`, refusal once any
  account exists). Regression tests:
  `tests/Meridian.Tests/Integration/EndpointTests/InitialAccountBootstrapEndpointTests.cs`.
  Remaining from AR8-01: the `--hash-password` / `--create-user` CLI verbs for source launches.
- **WPF safety seam** *(AR8-09)* — `TradingSafetyCommandService` and
  `ExecutionSafetyControlClient` were implemented, DI-registered
  (`src/Meridian.Wpf/Features/Trading/TradingFeatureModule.cs:33-34`), and unit-tested — and
  consumed by zero production code: pressing the enabled Stop/Cancel All buttons only opened
  panes. **Fixed:** `TradingWorkspaceShellViewModel.ExecuteCommandActionAsync` now routes safety
  command ids through the service and writes the service's own verdict into
  `DeskActionStatusText`; pane layout stays with the mapper. ViewModel-level dispatch tests added
  (`tests/Meridian.Wpf.Tests/Services/TradingSafetyCommandTests.cs`) — the prior suite only
  exercised the service in isolation, which is exactly how the dead seam survived.
- **Browser provenance pin** *(AR8-19 plus the 2026-08-18 flagship)* — two distinct defects, both
  closed:
  - *The pin was discarded.* `resolveWorkstationDataProvenance` read the server's pinned
    `provenance` only when `demoMode.enabled` was truthy and otherwise returned `"real"`. Since
    `enabled` comes from a credentials heuristic, a host holding an Alpaca or Polygon key while
    replaying a pinned-Simulated tape rendered **no banner at all** — exactly the credentialed-host
    case the pin was built for — and a unit test locked that behavior in. **Fixed:** the pinned
    label is authoritative whenever present, regardless of `enabled`; the test is corrected and
    extended to the simulated/seeded/sample pins.
  - *A failed probe branded a real install SIMULATED.* The demo-mode probe ran once with an empty
    catch, so one dropped request showed the non-dismissable red banner for the whole session,
    teaching operators to ignore the product's lead safety signal. **Fixed:** the probe retries
    with backoff, and `DataProvenanceKind` gains an explicit `unknown` state distinct from
    `simulated` (a wire token `"unknown"` still fails closed to simulated — `unknown` is a client
    transport state, never a server claim), rendered as a warning with a "Retry live data" control
    while confirmed simulated keeps the red danger banner and no controls. A disabled response
    that pins no provenance at all is also `unknown` rather than `real`.

  The pre-built `app-shell.development-fixture-notice.ts` (seeded-fixture case) remains unmounted —
  tracked under item 9, not silently claimed.

## 2. Make reconciliation actually reconcile

The matching engine is institutional-grade; the wired path starves and mis-attributes it.

- **Ledger-transaction population is hardcoded empty.**
  `src/Meridian.Application/Reconciliation/RetainedInternalReconciliationPopulationProvider.cs:89`
  passes `[]` as `LedgerTransactions` (deliberately, per its own comment at `:40-44`: sourcing it
  needs a period-scoped ledger source and a journal→transaction projection). Positions and cash
  do resolve; every statement *transaction* row (trades, fees, dividends) therefore matches
  against nothing and becomes a break. Note the review named `ReconciliationMatchingEngine` —
  that engine is unwired entirely (only consumer `ReconciliationRunOrchestrator`, constructed
  only by tests); the live engine is `StatementMatchingEngine` via `StatementRunMatcher`.
- **OFX multi-account silently cross-books.** `OfxDocumentParser.cs:114-117` takes the *first*
  `ACCTID` in the document and stamps it on **every** collected entry (`:228-231`), so a
  two-account file reconciles account B's transactions into account A's book — and the
  wrong-account guard in `StatementRunMatcher.ValidateStatementAccounts` cannot see it because
  rows are relabeled before it runs. CAMT.053 refuses this shape
  (`Camt053StatementConnector.cs:64-80`); OFX needs the same guard or real per-block accounts.
- **OFX charset is hardcoded UTF-8.** `SkipSgmlHeader` (`OfxDocumentParser.cs:265-269`) discards
  the header where `CHARSET`/`ENCODING` are declared; the golden fixture even carries
  `CHARSET:1252` and passes only because its payload is ASCII. Windows-1252 exports mojibake into
  matching evidence. *(Correction: transaction **sign** handling is largely correct — `TRNAMT`
  passes through signed and tests pin it; the residual gap is that `TRNTYPE=DEBIT` never
  validates or derives sign for non-conformant unsigned exports, unlike CAMT/BAI2.)*
- **Real session actor.** Intake and casework endpoints are fixed (401 + server-resolved actor),
  but `POST …/reconciliation/break-queue/bulk`
  (`WorkstationEndpoints.Reconciliation.cs:580`) prefers the **client-supplied** `request.Actor`
  over the session, falls back to a literal `"system"`, and then never persists any actor at all —
  bulk break actions land with zero attribution and zero test coverage. The margin-certification
  endpoint signs with a literal `"operator"` fallback
  (`WorkstationEndpoints.cs:4341-4354`) instead of 401.

*Refs:* AR8-15, AR8-29; statement-connector gaps extend §6 of the review. **Effort:** M–L.

## 3. Finish the kill switch end-to-end; arm the risk rails; fix fixed-income booking first

- **Kill switch:** server-side breaker + coupled cancel sweep are complete and genuinely halt
  routing (`ExecutionEndpoints.cs:422-525`, `OrderManagementSystem.cs:310-333`). The browser
  still has **no trip/reset control** — `ExecutionControlsCircuitBreaker` exists only as a
  generated constant (`ui-api-routes.generated.ts:480`, zero importers), no `api.ts` client, and
  the one wired action (cancel-all) empties the book without halting the next order. An operator
  can currently only trip the breaker via the risk engine or a raw API call. *(WPF Stop now trips
  it — item 1.)* Finish: a breaker button beside cancel-all plus an "also halt routing" affordance
  on cancel-all *(AR8-09 browser half; closes `W9-SAFETY-007`'s no-dead-buttons criterion)*.
- **Risk rails:** *(correction — the rules exist)* `FatFingerRule` and `PriceCollarRule` are
  implemented and unconditionally registered (`WorkstationServiceCollectionExtensions.cs:417-479`)
  but **six of eight rules ship with null thresholds and approve without measuring**
  (`RiskRuleRuntimeService.cs:92-107`). Arming = seeding enforceable defaults (fat-finger
  quantity/deviation, collar percent, gross-exposure, concentration, notional) so a fresh install
  cannot route any quantity at any price. *(AR8-10 is now "default the thresholds on", not "write
  the rules".)* **Landed 2026-08-24:** `RiskRuleFirstRunDefaults.Conservative` seeds all six rails
  when no operator snapshot exists, opted in by the workstation composition; a persisted snapshot
  (including an explicit clear) still wins. Regressions:
  `tests/Meridian.Tests/Ui/RiskRuleRuntimeFirstRunDefaultsTests.cs`. Still open: the browser
  editor for the fat-finger/collar thresholds.
- **Fixed-income booking (blocker for live routing):** the risk path knows bond prices are
  percent-of-par (`OrderNotionalResolver.cs:265-272`, `/100m`); the booking path does not —
  `TradeExecutedEvent.GrossValue => FilledQuantity * FillPrice`
  (`src/Meridian.Execution/Events/TradeExecutedEvent.cs:43`) flows unscaled into
  `LedgerPostingConsumer.cs:545-576`. 100,000 face at 101.25 books **$10,125,000 instead of
  $101,250** — balanced, silent, 100× wrong — and no accrued-interest leg is ever posted against
  the clean fill price. Same defect in `PaperTradingPortfolio.cs:654,784`. This sits downstream
  of every risk gate; no rail catches it. **New finding — no AR8 row; propose a registry row.**
  **Landed 2026-08-24 (scaling half):** the OMS carries the gateway-resolved face-value
  classification through fill processing, `TradeExecutedEvent.GrossValue` scales the clean price
  to a fraction of par, and `PaperTradingPortfolio.ApplyFill` converts once at the fill seam, so
  the booked cash equals the notional the risk gate approved — round-trip proof in
  `tests/Meridian.Tests/Execution/FixedIncomeFillBookingTests.cs`; recorded on `W9-SAFETY-007`.
  The accrued-interest leg remains open.

## 4. Live risk on the real book

- `PaperTradingPortfolio(100_000m)` is still registered as the authoritative `IPortfolioState` /
  `IPositionTracker` **outside** the paper-gateway conditional (`src/Meridian/UiServer.cs:361-367`
  vs the conditional closing at `:360`), so live position limits, exposure, notional, and drawdown
  are measured against a hardcoded empty $100k book. **Landed 2026-08-24:** the registration is
  now paper-scoped; a live brokerage composition registers a fail-closed `IPortfolioState` that
  refuses startup with an actionable message until a broker-backed implementation exists. The
  broker sync itself (next bullet) stays open.
- The drift detector is `PositionReconciliationService`
  (`src/Meridian.Execution/Services/PositionReconciliationService.cs:40`): report-only, registered
  in **no host**, not an `IHostedService`, and unconstructible in practice — its required
  `IBrokeragePositionSync` has **zero implementations** in `src/`. Seeding live portfolio state
  from the broker (AR8-16) therefore includes implementing the sync interface, not just wiring.
- Fail closed: refuse live routing when the broker sync is unavailable, matching the gateway
  posture. *Refs:* AR8-16 (**Propose row**). **Effort:** L.

## 5. Ship the first mile

- **Bootstrap fix:** middleware half landed (item 1); the `--hash-password` / `--create-user`
  verbs and `--quickstart` first-admin path remain *(AR8-01b-d)*.
- **Honest README launch:** the flagship `dotnet run … --mode workstation` recipe still walls a
  fresh clone behind PBKDF2 hashes it gives no way to produce; update the launch section to the
  supported paths *(AR8-01d)*.
- **Retry the installer:** seven `workflow_dispatch` runs of `desktop-installer-packaging.yml`
  failed between 2026-06-15 and 2026-07-16 and none has been retried since; the tag path has never
  run. Diagnose the seven failures first — they are the only evidence of why the lane cannot
  build — then drive `PRD-013/014/016` as one push *(AR8-08)*.
- **Un-bypass the install smoke:** `Meridian.Setup.Tests` runs in no CI lane (added to the
  coverage-gate exemption list after failing it), and the WPF suite never runs on the main gate —
  the code that can destroy a customer's install (`InstallationTransaction.Promote/Recover`) is
  covered only by tests that never execute *(AR8-34/38)*.

## 6. Provenance at the ingress seam

One fix corrects three consumers at once *(AR8-17)*: `MarketEvent` factory defaults still stamp
`Source = "IB"` / `"ALPACA"` (`src/Meridian.Domain/Events/MarketEvent.cs:14,32,38`) and the four
shared collectors publish without a source, so storage attribution is wrong per vendor, the
per-provider quality metrics attribute to the wrong feed, and synthetic ticks wear a real
vendor's name (contamination the simulated-origin safety net cannot see). The collectors are
singletons shared across provider clients — the provenance must ride each ingress call (or a
provider-scoped facade), **not** a collector constructor, or failover re-mislabels everything.
Downstream banner derivation from the active tape is AR8-18 and depends on this. **Effort:** M,
blast radius every collector/storage consumer — do before AR8-18.

## 7. Activate the fund-economics/FX kernels or re-status W9-NAV-006

`FundEconomicsJournalFactory` — whose doc comment says it exists "so the fund-economics kernels
post real ledger entries instead of living only in tests" — still has exactly one test-file
consumer and nothing in `src/`; the wired fee path posts a caller-typed `decimal`
(`AutomatedJournalDraftProjector.cs:100-109`). The multi-currency translator's consumers are all
in one test file; the journal-leg currency columns exist and nothing writes them. Either wire the
kernels through the intake runner / period close (AR8-25/26 — note both have hard prerequisites:
day-count basis, hurdle, crystallization posture, governed FX rate evidence, or the wiring posts
materially wrong governed journals), or re-status `W9-NAV-006` from `ready_for_acceptance` — the
registry row currently claims a capability with zero production consumers (the review's headline
corollary). **Effort:** L (fee wiring M; full kernel sequence XL).

## 8. Governance floor

Post-declaration-lane state (PR #2779 closed the *bookkeeping* gap, not the *enforcement* gap):

- **Runtime deny for undeclared routes:** `EndpointAuthorizationMetadata` is written but read by
  no production code — enforcement is two test-time ratchets with frozen baselines still carrying
  **58 undeclared + 62 unguarded + 21 allowlisted** mutating routes (including ledger period
  close and order submit). Add the global deny-without-metadata endpoint filter *(AR8-33)*.
- **Journal immutability trigger:** `journal_entries`/`journal_legs` still have no append-only
  trigger, keep `on delete cascade` (`V_ledger_001__journal_entries.sql:29`), and have no
  debits=credits DB constraint — while the identical trigger pattern protects the tax-lot tables
  (`V_ledger_027:164-183`) and reporting/asset-ops schemas *(AR8-32; cheapest half of
  `W9-GOV-008`)*.
- **Chain the accounting audit:** hash chaining is proven in-repo (compliance
  `ImmutableAuditLogService`, reporting, fund-admin event log) but
  `accounting_action_audit_events` carries only before/after content hashes — no `previous_hash`,
  no chain — and `journal_entries` has no hash column at all. Extend the existing pattern.
- **Fail-closed tenancy:** the write gate still defaults `Enforce: false`
  (`WorkstationTenantContext.cs:200`, env-var opt-in) and `RegistryFundProfileTenantGuard`
  still **allows on registry exception** (`:63-72`) — an outage opens cross-tenant writes
  *(AR8-33 second half)*.

## 9. Stop hiding failure in the UI

- *(Correction)* The close cockpit itself does **not** swallow errors — all 11 catch sites
  surface danger-toned alerts. The real swallows sit beside it:
  `accounting-screen.governance.view-model.ts:453-455,462-464` silently substitute stale
  readiness artifacts and an empty worker-plan list on fetch failure — failure indistinguishable
  from "nothing to show", the exact class the review flagged on Asset Detail.
- **No confirm on period lock — holds.** `lockClosePeriod` fires on one click
  (`accounting-screen.close-cockpit-panels.tsx:402-414`) while deleting a single notebook cell
  uses a two-click armed pattern. Locking an accounting period deserves at least the codebase's
  own confirmation idiom.
- **Orphaned API clients — holds at ~124/406** (re-measured: 406 exports, 124 unreachable from
  non-test code, 72 referenced nowhere at all — including the entire casework verb set and both
  bulk-action clients). No reachability gate exists (`AR8-41` unbuilt, `AR8-42` open); the count
  has not moved in 321 commits because nothing measures it. Add the orphan-export structural test
  first, then burn down screen-first (casework drawer = AR8-15 lights nine endpoints at once).
  The unmounted development-fixture notice (item 1c) belongs to this burn-down list.

## 10. Durability/concurrency floor

All four holds re-verified *(AR8-27/28/29/30)*:

- **Postgres break queue:** `FileReconciliationBreakQueueRepository` (one JSON file, process-wide
  semaphore) is still the sole implementation, registered unconditionally
  (`WorkstationServiceCollectionExtensions.cs:967-974`); Banking, FundAccounts, and
  OperationsContinuity all have Postgres variants. Prerequisite for casework at volume.
- **Durable inbox:** `InMemoryOperatorInboxService` is the only implementation and is
  non-production-only — in production composition the interface is **not registered at all** and
  the endpoint silently skips it (`WorkstationEndpoints.OperatorInbox.cs:33`), so "no work" and
  "no service" are indistinguishable. Add durable stores or return 503 with a named cause.
- **Extend the 409 contract:** 23 `ExpectedVersion` sites (almost all reporting governance)
  against ~485 mutating routes; zero `ETag`/`If-Match` anywhere in `src/`. Generalize the
  existing `ExpectedVersion` pattern with the compare-and-write **inside** the store lock —
  AR8-28's note stands: a filter-level check alone still loses updates.
- **Per-user rate limiting:** partition is still `RemoteIpAddress` at 10/min
  (`UiEndpoints.cs:331-333`); the per-user branch is provably dead because nothing in `src/` ever
  populates `HttpContext.User` (identity rides `HttpContext.Items`). Populate a `ClaimsPrincipal`
  in `LoginSessionMiddleware` — which also un-deadens `EndpointAuthorization.TryResolveActor`'s
  fallback *(AR8-30)*.

---

## Sequencing note

Items 3 and 4 gate any live-routing widening (irreversible-loss failure modes); item 3's
fixed-income booking defect is the sharpest single finding in this pass — a silent 100× ledger
misstatement no existing rail can catch — and is new since the review (**propose a registry
row**, alongside AR8-12/13, AR8-16, and AR8-33 already flagged as row candidates). Items 1 and 5
convert already-paid-for capability into the first user-reachable value; 2, 6, and 7 make the
wedge workflows truthful; 8–10 are the floor everything else stands on.

---

## Landed after 2026-08-18 (release-readiness follow-up, 2026-08-24)

The 2026-08-19 release-readiness assessment independently confirmed several findings from this
backlog and surfaced four adjacent ones. All were closed on branch
`claude/document-issues-hijmes`, alongside the item-3 scaling/arming and item-4 scoping fixes
noted inline above:

- **`/health` and `/metrics` auth exemption** — both `LoginSessionMiddleware` and
  `ApiKeyMiddleware` exempted only the `/healthz` family, so the shipped docker-compose
  healthcheck (`/health`) and Prometheus scrape (`/metrics`) failed in any authenticated
  deployment. Both exact paths are now exempt; `/health/detailed` stays authenticated.
- **Synthetic backfill outranked real vendors** — `config/appsettings.sample.json` shipped
  `Providers.Synthetic` at priority 1 (Alpaca 5, Yahoo 22) and the composite selector takes the
  first success, so adding real API keys still produced fabricated bars. Synthetic now sits at
  priority 99 as the last-resort offline fallback.
- **WPF Data Browser fabricated data unlabeled** — the screen renders 240 records from
  `new Random(42)` with no service dependency. It now carries the shared, non-dismissable
  `DataProvenanceBadge` (the same signal the browser lane renders), and the generator documents
  that badge and data must be removed together when the grid is wired to real storage.
- **Simulated-origin filter missed structured evidence** — the ledger append boundary matched
  exact tokens only, so a mark retaining `daily-close:AAPL:2026-08-18:synthetic` derived
  provenance "real" and posted clean. `DataProvenanceExtensions.CarriesSimulatedOriginToken`
  now matches colon-delimited segments across every structured evidence field
  (`AccountingPostingCommandValidator`), with regressions in
  `tests/Meridian.Tests/Storage/AssetAccountingPostingEvidenceValidatorTests.cs` and
  `tests/Meridian.Tests/Contracts/DataProvenanceTests.cs`.
