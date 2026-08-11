# Adversarial Review 2026-08 — Remediation Todos and Implementation Plans

**Status:** active working plan; not a governance or roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-08-11
**Source:** [Adversarial Program Review (2026-08)](adversarial-program-review-2026-08.md)

This document turns every finding in the 2026-08 adversarial review into a tracked todo with a
code-ready implementation plan. It is a **working plan**, not a status source: live roadmap truth
stays in `docs/roadmap/data/*.yml`, and release-gate truth stays in the
[Implementation and Readiness Tracker](implementation-todo-list.md). Where a todo overlaps an
existing `PRD-*`, `W9-*`, or `W10-*` row, the row is named so work is not duplicated or
double-counted.

## How to use this document

- `AR8-nn` identifiers are local to this plan. They are **not** roadmap rows and must not be cited
  as acceptance evidence. Four items flagged **Propose row** are candidates for real registry rows.
- Every plan states **Evidence** (verified `file:line`), **Change** (what to actually do),
  **Verify** (the command or test that proves it), **Effort**, and **Depends on**.
- Effort: **S** ≤ 1 day · **M** 1–3 days · **L** 1–2 weeks · **XL** > 2 weeks.
- Checkboxes are the todo list. Tick one only when its **Verify** step passes on the same commit.

## Sequencing

Workstreams are ordered by end-user value uplift, matching the review's ranked list. Two rules
govern order:

1. **W1 gates everything.** Until a non-developer can install and run the product, no other
   improvement reaches a user. Nothing in W1 depends on another workstream.
2. **W2 and W5 precede any live-trading widening.** Safety controls that do not act, and risk
   limits measured against a fictional book, are the two findings whose failure mode is
   irreversible financial loss.

| Wave | Workstreams | Rationale |
|---|---|---|
| 1 | W0 quick wins, W1 first mile, W2 safety | Ship something installable; stop the controls that lie. |
| 2 | W3 promotion evidence, W5 live risk, W9 authorization | Close the gates that accept assertions instead of evidence. |
| 3 | W4 casework, W6 truth discipline, W7 fund economics | Activation: convert built capability into operator value. |
| 4 | W8 durability, W10 CI, W11 UX, W12 data integrity | Make the foundation hold under real multi-operator use. |
| 5 | W13 program hygiene | Keep the status surface honest once the substance is fixed. |

---

## W0 — Quick wins

Six changes that are each a line or a deletion, carry no design risk, and remove active
misinformation. Do these first regardless of sequencing; they cost under a day in total.

- [x] **AR8-Q1 — Populate `UNWIRED_WORKSTATION_ROUTES`.** *(Done — see also AR8-44: the Quant Lab Formulas tab had to be withdrawn too, since the set only filters nav and the palette.)*
  **Evidence:** `src/Meridian.Ui/dashboard/src/lib/workspace.ts:141-149` declares the set with a
  doc comment naming Family Office and the Formula Workbench, then initializes it empty.
  **Change:** add `/portfolio/family-office` and `/strategy/quant-lab?view=formulas` (route key
  form) to the set. Filtering already exists in `workspace-nav.view-model.ts:253,256,263` and
  `command-palette.view-model.ts:726`.
  **Verify:** `npm --prefix src/Meridian.Ui/dashboard run test`; assert both routes are absent from
  nav and palette results.
  **Effort:** S

- [x] **AR8-Q2 — Read the `error` field in the client error normalizer.** *(Done, with regression tests for both body shapes.)*
  **Evidence:** `src/Meridian.Ui/dashboard/src/lib/api-errors.ts:55` reads only `detail`,
  `message`, `title`; 395 endpoints return `{ error: "..." }`.
  **Change:** append `readString(parsed.error)` to the fallback chain.
  **Verify:** unit test asserting a `{error}` body surfaces its text, not `Request failed (400)`.
  **Effort:** S · **Note:** partial relief for AR8-42; the full fix is the server-side rewrite.

- [x] **AR8-Q3 — Give the devcontainer a durable money path.** *(Done — `MERIDIAN_DATABASE_URL` added; the two explicit per-domain strings still win for their domains.)*
  **Evidence:** `.devcontainer/docker-compose.yml:27-28` exports only security-master and
  direct-lending connection strings, so the ledger runs `PERSISTENCE: PARTIAL`.
  **Change:** add `MERIDIAN_DATABASE_URL: postgres://dev:devpass@db:5432/meridian`;
  `ApplyUnifiedDatabaseUrl` fans it out to all ten domains.
  **Verify:** container boot logs report `PERSISTENCE: FULL`; `/readyz` lists no missing domain.
  **Effort:** S

- [x] **AR8-Q4 — Wire or disable the Strategy Designer buttons.** See AR8-43. *(Done: both render
  disabled with reasons; wiring is blocked on a missing document mapper — see AR8-43.)*
- [ ] **AR8-Q5 — Correct the misleading dashboard metrics.** See AR8-24. *(Not a deletion: the
  dashboards are live automation output — see the corrected item.)*
- [x] **AR8-Q6 — Delete the StockSharp sample-config block.** See AR8-05. *(Done, with a new CI gate.)*

---

## W1 — Ship the first mile

**Goal:** a non-developer can install, launch, back up, and upgrade Meridian.
**Why first:** the tracker records 0 of 21 P0 rows production-certified and no successful artifact
build in seven attempts, so today's supported user count is structurally zero.

- [ ] **AR8-01 — Give source launches a first-account path.** *(P0-adjacent; supports `PRD-000`)*
  **Evidence:** a fresh clone defaults to `Production`, auth resolves to `Required`
  (`src/Meridian.Identity/Application/AuthenticationMode.cs:42-44`), and every request 503s asking
  for `MDC_USERS` with PBKDF2 hashes (`src/Meridian.Ui.Shared/Endpoints/LoginSessionMiddleware.cs:198`).
  `--quickstart` never touches auth (`src/Meridian.Application/Services/ConfigurationWizard.cs:133-217`).
  The installed path *does* have bootstrap machinery
  (`src/Meridian.LifecycleSupervisor/LifecycleSupervisorRuntime.cs:288` → `/api/auth/bootstrap` →
  `src/Meridian.Ui.Shared/Services/InitialAccountBootstrapService.cs:10`) — **but it is unreachable
  as written.** In `LoginSessionMiddleware.InvokeAsync`, the `!sessionService.IsConfigured`
  fail-closed block returns 503 at lines 98-115, *before* the `/setup/account` and `/api/auth`
  exemptions at lines 118-125. On a fresh install no accounts exist, so `IsConfigured` is false;
  `AllowAnonymousWhenUnconfigured` is true only in `Optional` mode (`LoginSessionService.cs:56`) and
  packaged builds default to `Required`; the only earlier bypass, `IsLifecycleTokenRequest`
  (`:201-223`), covers just `/api/system/lifecycle` and `/api/system/shutdown*`, and
  `MDC_BOOTSTRAP_TOKEN` is never consulted by the middleware. **So the first-account gap is not
  scoped to source launches — installed users are blocked by the same 503.**
  **Change:** (a) reorder the middleware so `/setup/account` and `/api/auth/bootstrap` are exempt
  *before* the unconfigured fail-closed branch — or allow them conditionally while unconfigured,
  gated on a valid `MDC_BOOTSTRAP_TOKEN` and a loopback caller, mirroring `IsLifecycleTokenRequest`;
  (b) add `--hash-password` and `--create-user <name>` verbs to
  `src/Meridian.Application/Commands/ConfigCommands.cs` so source launches have a path too;
  (c) extend `--quickstart` to offer first-admin creation or mint a bootstrap token; (d) update the
  README launch section.
  **Verify:** two tests — a fresh installed profile with zero accounts reaches `/setup/account` and
  completes `/api/auth/bootstrap`, and a clean clone reaches an authenticated workstation via the
  new verb. Both must fail against today's middleware ordering.
  **Effort:** M · **Priority note:** this is the single highest-value fix in W1 — without it the
  installer lane in AR8-08 produces an artifact whose first run cannot create an account.

- [ ] **AR8-02 — Make the demo state its own posture.**
  **Evidence:** `src/Meridian/DemoWorkspaceCli.cs:106-131` sets `MERIDIAN_USE_INMEMORY_GOVERNANCE`,
  `DOTNET_ENVIRONMENT=Development`, `MDC_AUTH_MODE=optional`. ADR-019 leaves Development
  composition unchanged, so this is legitimate — but non-production and uncertifiable, and the
  output says nothing.
  **Change:** emit a banner from `SeedAsync` listing each relaxed default, the reason, and the
  exact command to graduate to the supported posture. Echo the same text in the seeded workspace's
  first-run panel.
  **Verify:** golden-file test on the CLI banner; a demo run shows the notice.
  **Effort:** S · **Related:** `W9-DEMO-002`

- [ ] **AR8-03 — Repair or retire the deployment manifests.**
  **Evidence:** `deploy/docker/Dockerfile:24-41` copies 12 `.csproj` files while
  `src/Meridian/Meridian.csproj:70-84` declares 14+ project references, so restore fails; the
  compose healthcheck probes `/health`, which is not in
  `src/Meridian.Ui.Shared/Endpoints/LoginSessionMiddleware.cs:66-75`, and `curl -f` passes on the
  302; compose has no PostgreSQL service; `deploy/systemd/meridian.service` runs `dotnet run` from
  source under `ProtectSystem=strict` with no writable `obj/`.
  **Change:** decide per ADR-019 whether Linux container deployment is in or out of the envelope.
  *In:* fix the csproj list (generate it from `dotnet list reference` in a build step), add a
  `postgres` service plus `MERIDIAN_DATABASE_URL`, set `ASPNETCORE_ENVIRONMENT`, probe `/healthz`,
  and replace the systemd `ExecStart` with a published binary. *Out:* move `deploy/docker`,
  `deploy/k8s`, `deploy/systemd` to `archive/` with a pointer to the supported installer lane.
  **Verify:** if kept, add a `docker build` smoke job to `meridian-ci.yml` that fails on restore
  error — the gap that let this rot undetected.
  **Effort:** M (repair) / S (archive) · **Related:** `PRD-013`

- **AR8-04 — Devcontainer durability.** Alias of AR8-Q3, not a separate todo; tick the checkbox
  there so the work is counted once.

- [x] **AR8-05 — Remove the phantom provider and plaintext secrets from the sample config.** *(Done 2026-08-11.)*
  **Evidence:** `config/appsettings.sample.json:88,585-619` lists `"StockSharp"` (absent from
  `src/Meridian.Core/Config/DataSourceKind.cs`, and the converter throws on unknown values at
  `DataSourceKindConverter.cs:27-28`) and ships `Rithmic.Password` / `CQG.Password` fields,
  contradicting the file's own "never store secrets here" banner.
  **Change:** delete the StockSharp block and its nine `MDC_STOCKSHARP_*` entries. Add a CI check
  asserting every `DataSource` value named in the sample resolves to a `DataSourceKind` member.
  **Verify:** new check fails on a deliberately reintroduced bad value; `bash scripts/ci.sh --lane verify-docs`.
  **Effort:** S

- [ ] **AR8-06 — Ship an operator backup and restore path.**
  **Evidence:** `build/scripts/install/build-consumer-setup.ps1:37-43` validates only
  `postgres.exe`, `pg_ctl.exe`, `initdb.exe`; the canonical recovery script is not in the payload;
  `docs/operators/failover-and-recovery.md:96-101` requires a verified backup before any
  destructive migration.
  **Change:** add `pg_dump.exe` (and `pg_restore.exe`) to the payload validation list. Add
  `backup` and `restore` verbs to `Meridian.LifecycleSupervisor` alongside
  `start|stop|restart|status|preflight`, writing the same JSON receipt the recovery drill produces.
  **The two captures must share one coordinated point in time.** `pg_dump` yields a
  transaction-consistent database snapshot, but copying the data root while the host still accepts
  writes captures a different instant, so a restore can pair journal state with older or newer
  retained files and evidence — calling them "one recovery unit" in a shared receipt does not make
  them atomic. Require a supervisor stop, or a read-only barrier that quiesces writes, and bind both
  captures to that barrier before the receipt may claim the backup is valid. Surface a "Back up now"
  action in Settings that performs the barrier rather than a live copy.
  **Verify:** `tests/Meridian.Setup.Tests` (once running per AR8-34) covers backup → destructive
  change → restore → integrity check.
  **Effort:** L · **Depends on:** AR8-38 (Windows job) so `Meridian.Setup.Tests` actually runs · **Related:** `PRD-015`

- [ ] **AR8-07 — Enforce private permissions on the credential key.**
  **Evidence:** `src/Meridian.DataIntegration/Credentials/FileProviderCredentialStore.cs:639-724`
  writes the raw 32-byte key beside the vault with only a Windows `Hidden` attempt; no
  `File.SetUnixFileMode` call exists in the credential path, so the mode follows the umask and is
  never validated on read.
  **Change:** create the key with owner-only permissions **before the first byte is written** —
  chmod-after-write is not sufficient. `AtomicFileWriter` creates its temporary file in the shared
  `.mdc` directory with default permissions, flushes, and renames, so a post-write chmod leaves a
  window in which another local user can read either the temporary file or the destination. Either
  open the temp file with `UnixFileMode.UserRead | UserWrite` at creation and preserve the mode
  across the rename, or restrict the containing directory to owner-only first so the window is not
  reachable. On read, refuse to load a key whose mode grants group or other access, with a
  diagnostic naming the fix. Correct the WPF copy at `src/Meridian.Wpf/Views/CredentialManagementPage.xaml:237`,
  which claims DPAPI unconditionally. File a follow-up for OS keyring backends.
  **Verify:** unit test asserting mode 0600 after write and a hard failure on a 0644 key.
  **Effort:** S

- [ ] **AR8-08 — Close the publish → sign → install evidence chain.** *(tracker-owned)*
  **Evidence:** `docs/product/implementation-todo-list.md:37` — 0 production-certified; seven
  `workflow_dispatch` runs of `desktop-installer-packaging.yml` between 2026-06-15 and 2026-07-16,
  all failed; no git tag, so the tag path has never run.
  **Change:** this is `PRD-013`/`PRD-014`/`PRD-016` and is blocked on human actions, not
  engineering — ADR-019/020 sign-off, signing secret and ARM64 runner provisioning, a frozen-commit
  re-dispatch, and required-check activation. Drive the four as one scheduled push and diagnose the
  seven historical failures first; they are the only evidence of why the lane does not build.
  **Verify:** one tagged pre-release produces a signed `web-workstation`/`win-x64` artifact that
  installs on a clean VM.
  **Effort:** L (mostly coordination) · **Related:** `PRD-013`, `PRD-014`, `PRD-016`

---

## W2 — Safety controls that actually act

**Goal:** no control in either client claims an action it does not perform.
**Why:** this is the only workstream whose failure mode is an operator trusting a button during a
runaway position. `W9-SAFETY-007` names "no dead safety buttons" as its own exit criterion and is
still `in_progress`.

- [ ] **AR8-09 — Sweep every safety control in both clients.** *(closes part of `W9-SAFETY-007`)*
  **Evidence:** `src/Meridian.Wpf/Services/TradingWorkspaceShellPresentationService.cs:164-167` —
  Pause, Stop, Flatten, and CancelAll each return a pane-layout change plus a reassuring toast;
  none calls `IOrderManager.CancelAllAsync` or the breaker, and Flatten carries
  `WorkspaceTone.Danger`. In the browser, the durable breaker route exists only as a generated
  constant (`src/Meridian.Ui/dashboard/src/lib/ui-api-routes.generated.ts:477`) called from
  nowhere; the wired cancel-all cancels the book without halting new routing.
  **Change:** (a) enumerate every command binding in `src/Meridian.Wpf` and the browser trading
  screens; (b) for each, either bind it to `ExecutionOperatorControlService` / the circuit-breaker
  endpoint, or render it disabled with a `disabledReason` and strip the danger tone; (c) add a
  breaker toggle to the trading screen's confirm-action set beside cancel-all, and offer "also halt
  routing" on cancel-all. **A disabled honest button beats an enabled fake one — demote first, wire
  second.**
  **Verify:** a test asserting every command in the trading surfaces resolves to a real service call
  or is disabled; manual drill: trip the breaker from the UI and confirm a subsequent order is
  rejected.
  **Effort:** M

- [ ] **AR8-10 — Add the missing pre-trade rules.** *(remainder of `W9-SAFETY-007`)*
  **Evidence:** the mandatory validator has no fat-finger quantity, price-deviation, or price-collar
  rule (`W9-SAFETY-007` exit criteria; `src/Meridian.Risk/CompositeRiskValidator.cs` rule set).
  **Change:** implement the three rules against the existing `IRiskRule` seam so they inherit
  severity mapping and the fail-closed breaker latch. Default them **on**; source the reference
  price from the same quote path the collar checks.
  **Verify:** `dotnet test tests/Meridian.Tests --filter "FullyQualifiedName~Risk"` with cases for a
  1000× quantity typo and a 40%-off limit price.
  **Effort:** M · **Depends on:** the severity work already shipped in the risk-engine blueprint

- [ ] **AR8-11 — Derive go-live gating from artifacts, not booleans.**
  **Evidence:** `src/Meridian.Execution.Sdk/BrokerageOrderPlacementGate.cs:83-117` trusts four
  `appsettings` booleans and two `File.Exists` checks; content, freshness, and run linkage are never
  inspected.
  **Change:** parse the two artifacts, verify schema, run id, and a maximum age — **but parsing
  alone is not sufficient and the gate must not stop there.** A well-formed file with a current
  timestamp and the expected run id is indistinguishable from genuine job output, and hashing the
  same file proves only self-consistency, so a parse-only gate is still protected by a
  caller-authored assertion. Bind the artifact to something the caller cannot mint: a signature or
  MAC over its contents, or resolution of its hash and run id against an authoritative retained
  validation receipt held outside the supplied files. Keep the existing fail-closed refusal for the
  IB non-vendor build (`IBBrokerageGateway.cs:96-106`) — that part is correct.
  **Verify:** a stale artifact fails; a hand-created but well-formed artifact fails **because its
  signature or retained receipt does not resolve**. Without that binding the second case cannot be
  tested at all, which is the tell that the gate is not real.
  **Effort:** M

---

## W3 — Promotion evidence that cannot be typed in

**Goal:** the paper→live gate measures retained evidence, not caller assertions.

- [ ] **AR8-12 — Register the walk-forward harness in the workstation host.** **Propose row**
  **Evidence:** `IWalkForwardService` has exactly one DI registration, in the WPF app
  (`src/Meridian.Wpf/Features/Strategy/StrategyFeatureModule.cs:82`); the browser host registers
  `PromotionService` but not the harness.
  **Change:** register it in `WorkstationServiceCollectionExtensions`, expose a "Run walk-forward"
  action that executes server-side and writes the evidence itself. This is the enabling fix: without
  it the architecture *forces* the fabrication path in AR8-13.
  **Verify:** integration test — run walk-forward from the browser API, assert a
  `StrategyRunWalkForwardEvidence` row is persisted with a resolvable source reference.
  **Effort:** M

- [ ] **AR8-13 — Bind walk-forward evidence to a retained report.** **Propose row**
  **Evidence:** `src/Meridian.Ui.Shared/Endpoints/PromotionEndpoints.cs:150-162` builds the evidence
  record straight from the request body and validates only that numbers are finite;
  `SourceReference` is optional and never dereferenced. The policy thresholds it feeds are at
  `src/Meridian.Strategies/Services/PromotionService.cs:153-160`.
  **Change:** make `SourceReference` required; resolve it to a retained `WalkForwardReport`;
  recompute or hash-verify `OutOfSampleSharpeRatio`, `DegradationRatio`,
  `OutOfSampleMaxDrawdownPercent`, and `WindowCount` against that artifact; reject on mismatch. The
  endpoint's own comment already names this risk — make the code match it.
  **Verify:** test that a hand-posted `{"outOfSampleSharpeRatio": 2.5}` with no resolvable source is
  rejected, and that a genuine harness run passes.
  **Effort:** M · **Depends on:** AR8-12

- [ ] **AR8-14 — Apply the evidence cross-check to live promotions.**
  **Evidence:** `src/Meridian.Strategies/Services/PromotionService.cs:985-993` returns `[]` unless
  the target is Paper, so for paper→live 13 of 15 checklist items pass on any non-empty string after
  the colon (`:943-953`). Only the override id (`:955-966`) and a substring test on the execution
  model (`:971-976`) are real.
  **Change:** run `GetInvalidSourceRunEvidenceReferences` for `RunType.Live` as well; resolve
  `PAPER_EXECUTION_MODEL_REVIEWED` against the cited paper session's recorded
  `MatchingModelVersion`/`CostModelVersion` instead of substring-matching.
  **Verify:** test that `RECONCILIATION_EVIDENCE_REVIEWED:x` no longer clears a live promotion.
  **Effort:** S

---

## W4 — Reconciliation casework activation

**Goal:** the wedge persona can complete a working day in the product.

- [ ] **AR8-15 — Ship a case-detail drawer and bulk actions.**
  **Evidence:** of 19 break-workflow client functions only queue/review/resolve are wired
  (`src/Meridian.Ui/dashboard/src/screens/accounting-screen.view-model.ts:2935-2938`); assign,
  transition, comment (add/edit/delete), root cause, resolution, sign-off, reopen, and both bulk
  actions have no UI path, while "Casework" is a top-level Accounting nav item. The bulk endpoint is
  complete with dry-run, idempotency key, partial success, and retained receipts; the server caps
  bulk at 100 (`src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs:17`).
  **Change:** (a) build one case-detail drawer exposing assign, comment, root cause, resolution,
  sign-off, reopen — nine endpoints lit by one surface; (b) add multi-select plus a bulk action bar
  that calls dry-run first and renders the preview before executing; (c) raise the cap or chunk
  client-side so 500 breaks are one operator gesture, and log any truncation rather than silently
  capping.
  **Verify:** `npm --prefix src/Meridian.Ui/dashboard run test` covering the drawer's nine actions;
  plus a test that **one operator gesture** clears 250 breaks. Assert the gesture, not the call
  count — both designs above are supported, and client-side chunking against the retained 100-item
  server cap would fail a single-call assertion. If the cap is raised instead, the same test passes
  with one call. Either way, assert that chunks are sequenced correctly, that the idempotency key
  makes a retry safe, and that any truncation is logged rather than silent.
  **Effort:** L · **Sequence:** assign + comment + sign-off first (highest frequency)

---

## W5 — Live risk measured against the real book

- [ ] **AR8-16 — Seed and reconcile live portfolio state from the broker.** **Propose row**
  **Evidence:** `src/Meridian/UiServer.cs:361-367` registers
  `PaperTradingPortfolio(100_000m)` as the authoritative `IPortfolioState` and `IPositionTracker`
  **outside** the `usesPaperGateway` conditional that closes at `:360`. Those back
  `PositionLimitRule`, `GrossExposureRule`, `SymbolConcentrationRule`, `OrderNotionalRule`, and the
  drawdown guardrail. `src/Meridian.Execution/Services/PositionReconciliationService.cs:73-169`
  only reports drift; nothing writes broker positions back.
  **Change:** in live mode, resolve `IPortfolioState` from a broker-backed implementation seeded via
  `IBrokeragePositionSync` at startup and refreshed on each reconciliation cycle; refuse to start
  live routing when the sync is unavailable (fail closed, matching the existing gateway posture).
  Keep the paper portfolio bound only under `usesPaperGateway`.
  **Verify:** test that live composition without a reachable position sync fails host construction,
  and that a pre-existing broker position counts against `PositionLimitRule`.
  **Effort:** L

---

## W6 — Truth discipline end to end

**Goal:** nothing the product displays is fabricated, mislabeled, or silently substituted.

- [ ] **AR8-17 — Give `MarketEvent` real provenance.**
  **Evidence:** `src/Meridian.Domain/Events/MarketEvent.cs:14,32,38` defaults `Source` to `"IB"` for
  trades and `"ALPACA"` for quotes; the four shared collectors publish without passing a source
  (`Collectors/TradeDataCollector.cs:237`, `QuoteCollector.cs:40`, `MarketDepthCollector.cs:122`,
  `L3OrderBookCollector.cs:189`), so Polygon trades are stored as Interactive Brokers.
  **Change:** add a required provenance member (`DataProvenance` or at minimum `IsSimulated` plus a
  provider id) to `MarketEvent` and **remove the vendor-name defaults** so omitting a source is a
  compile error. **Do not pass the provider id into the collector constructor** — an earlier
  revision said to, and it cannot work: `CollectorFeatureRegistration.cs:19-42` registers
  `QuoteCollector`, `TradeDataCollector`, and `MarketDepthCollector` as singletons, and
  `ProviderFeatureRegistration.Registry.cs:32-94` injects those same instances into the IB, Alpaca,
  Polygon, and NYSE clients. A constructor-level id would stamp every event with whichever provider
  happened to construct it and would never change when failover switches — leaving the mislabeling
  in place while looking fixed. Instead, either (a) have each adapter pass its provenance on every
  ingress call, so the shared collector stamps what it is actually given, or (b) give each adapter a
  thin provider-scoped facade that stamps calls before forwarding to the shared collector. (b) keeps
  the adapter call sites unchanged and is the smaller diff; (a) is harder to get wrong later.
  **Verify:** the compile break proves no caller can omit it; a test asserts a Polygon-sourced event
  carries `polygon` **while an Alpaca client shares the same collector instance**, and that a
  failover switch changes subsequent events' provenance. A single-provider test would pass against
  the broken constructor design and prove nothing.
  **Effort:** M · **Blast radius:** every collector and storage consumer — do this before AR8-18.

- [ ] **AR8-18 — Derive the provenance banner from the active tape.**
  **Evidence:** `src/Meridian.Application/Composition/ProductionServiceRegistrationPolicy.cs:214-226`
  resolves non-real provenance **only** from in-memory store bindings, so durable stores plus
  `DataSource = Synthetic` (the default, `src/Meridian.Core/Config/AppConfig.cs:48`) reports `Real`
  and renders no banner. `src/Meridian.Ui.Shared/Endpoints/DemoModeEndpoints.cs:80-95` falls back to
  an Alpaca/Polygon key heuristic, so a working Tiingo key is mislabeled `Seeded`.
  **Change:** derive provenance from the providers that actually supply the tape — the **active**
  streaming client (`FailoverAwareMarketDataClient.ActiveClient`) and the historical provider that
  served the data — not from every registered provider. A mixed registration (real primary plus an
  inactive synthetic fallback) is the normal case, and failing on *any* registered simulated
  provider would recreate exactly the false-red-banner problem AR8-19 fixes. Fail closed to
  `Simulated` when the active provider reports `IsSimulated` or when a failover switches onto a
  simulated backup. **Do not coerce unresolved provenance to `Simulated`** — that contradicts
  AR8-19, which needs uncertainty to stay distinguishable from confirmed synthetic data. Propagate
  a third state, `unknown`, when provenance cannot be resolved, so the client can offer a retry
  instead of showing the non-dismissable red warning on a real install. `Simulated` is reserved for
  positively identified simulated providers. Delete the key heuristic. Hoist `IsSimulated`
  from `IMarketDataClient`
  (`src/Meridian.ProviderSdk/IMarketDataClient.cs:36`) to the shared provider metadata interface so
  `IHistoricalDataProvider` carries it too — `SyntheticHistoricalDataProvider` currently advertises
  `FullFeatured` with no simulation marker.
  **Verify:** test matrix over {durable, in-memory} × {real, synthetic} provider sets, **plus the
  mixed case** — a real active primary with a registered synthetic fallback must report `Real`, and
  the same host must flip to `Simulated` once failover promotes the synthetic backup.
  **Effort:** M · **Depends on:** AR8-17

- [ ] **AR8-19 — Stop the browser branding real installs SIMULATED.**
  **Evidence:** `src/Meridian.Ui/dashboard/src/app.tsx:205-207` fetches demo mode once with an empty
  catch and no retry; null provenance renders the non-dismissable red banner
  (`app-shell.data-provenance-badge.ts:101`, `components/meridian/data-provenance-banner.tsx:28-36`).
  A fixture notice with a working "Retry live data" button exists and is rendered by nothing
  (`app-shell.development-fixture-notice.ts`).
  **Change:** retry the probe with backoff; add an explicit `unknown` state to `DataProvenanceKind`
  distinct from `simulated`; render the already-written retry control for `unknown`. Keep the
  fail-closed default for a *confirmed* simulated response.
  **Verify:** test that a transient probe failure yields `unknown` with a retry affordance and that
  a confirmed simulated response still shows the red banner.
  **Effort:** S

- [ ] **AR8-20 — Make the IB simulation announce itself and stop silent no-ops.**
  **Evidence:** without the vendor SDK, `src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBMarketDataClient.cs:61-118`
  delegates to `IBSimulationClient` while reporting `ProviderDisplayName => "Interactive Brokers"`
  and full capabilities including `Level2Book`. The simulator registers depth subscriptions it never
  services (`IBSimulationClient.cs:178,208`) and always emits `SequenceNumber: 0`.
  **Change:** in non-`IBAPI` builds, append "(simulation)" to the display name, drop `Level2Book`
  and `TickByTick` from advertised capabilities, and **throw** on `SubscribeMarketDepth` instead of
  returning a live-looking handle. Emit a real monotonic sequence.
  **Verify:** test asserting the non-vendor build refuses depth subscription and never advertises L2.
  **Effort:** S

- [ ] **AR8-21 — Fix the synthetic catalog's borrowed identity.**
  **Evidence:** `src/Meridian.Infrastructure/Adapters/Synthetic/SyntheticReferenceDataCatalog.cs:44-49`
  attaches genuine FIGI/ISIN/CUSIP values and a perpetually fresh `LastUpdated`; `:265` aliases any
  unknown ticker to SPY's economics under the requested name.
  **Change:** replace real identifiers with reserved test-range values; make `GetProfileOrDefault`
  return an explicit unknown-symbol result instead of silently substituting SPY. Also fix the demo
  endpoints' fabricated series (`src/Meridian.Ui.Shared/Endpoints/DemoModeEndpoints.cs:24-60,161-183`):
  independent uniform draws under `DateTime.UtcNow` stamps, seeded by `string.GetHashCode()` which
  is per-process randomized — derive from `SyntheticHistoricalDataProvider` with an explicit integer
  seed.
  **Verify:** test that no synthetic profile carries a real-range ISIN/CUSIP, that an unknown symbol
  does not return SPY prices, and that demo bars are byte-identical across two processes.
  **Effort:** M

- [ ] **AR8-22 — Delete the WPF fabricated status surfaces.**
  **Evidence:** `src/Meridian.Wpf/ViewModels/SettingsViewModel.cs:860-863` shows a hardcoded "You
  are running the latest version (1.6.1)" with no network call, against
  `src/Meridian.Wpf/Meridian.Wpf.csproj:38` (`1.0.0`); `:865-889` hardcodes three activity entries
  including "Cloud sync completed" for a product with no cloud sync.
  **Change:** delete both methods. Bind Recent Activity to the real audit trail **through the shared
  API seam, not the service directly**: `ImmutableAuditLogService` is registered in the host
  (`src/Meridian/UiServer.cs:306-308`) and has zero references anywhere in `src/Meridian.Wpf`, so in
  the installed topology it lives in a different process — resolving it from the WPF container would
  fail or, worse, construct a second empty log. Expose retained activity as a read model over the
  existing endpoint surface and consume that, preserving the shared-contract boundary both clients
  are required to sit on. Either implement a signed release-manifest check or remove the Check for
  Updates button (`src/Meridian.Wpf/Views/SettingsPage.xaml:1262`).
  **Verify:** `dotnet test tests/Meridian.Wpf.Tests --filter "FullyQualifiedName~Settings"`.
  **Effort:** S

- [ ] **AR8-23 — Stop the Lean endpoints reporting fabricated lifecycle.** *(closes part of `PRD-020`)*
  **Evidence:** `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:250-276` writes `"queued"` into a
  process-wide static dictionary; no path ever sets running or completed; `:300-313` returns
  hardcoded zeros for a state that is unreachable. A full WPF surface sits on top.
  **Change:** return `501 Not Implemented` from the lifecycle endpoints until a real Lean process
  launcher exists, and disable the WPF surface with a reason. Do not leave a queue that never drains.
  **Verify:** test asserting `501`; the WPF page renders its unavailable state.
  **Effort:** S

- [ ] **AR8-24 — Fix the misleading dashboard metrics (do *not* delete the dashboards).**
  **Correction:** the review called these "dead dashboards" and inferred that from the
  `1970-01-01` stamps. **That inference was wrong.** `build/scripts/docs/dashboard_rendering.py:13`
  defines `STABLE_GENERATED_AT = "1970-01-01T00:00:00+00:00"` with `current_utc_timestamp()`
  documented as returning "a stable ISO-8601 UTC timestamp for generated docs" — a deliberate
  determinism choice so regeneration does not churn the tree on every run. (The same class of
  problem bit this branch: a drifting coverage snapshot failed `regenerate-docs`.) These files are
  live automation output — `run-docs-automation.py --profile core` regenerates the health, metrics,
  API-contract, and example-validation artifacts, and the documentation workflow reads
  `docs/status/doc-health-dashboard.json` to compute readiness deltas. Deleting them would break
  those consumers and destroy CI evidence.
  **What survives the correction:** the *numbers* are still misleading. `metrics-dashboard.md`
  reports 0 workflow runs / 0 tests / 0.0% success across every workflow while CI demonstrably runs;
  `doc-health-dashboard.md` prints "80/100 — Rating: Good" over 254 orphaned files; and
  `docs/status/workflow-validation-summary.json` reports `"clean"` across 12 workflows while
  `.github/workflows/` holds 28.
  **Change:** (a) label the stable timestamp in the rendered header ("deterministic placeholder —
  see the workflow run for actual generation time") so no reader treats it as a staleness signal;
  (b) fix or retire the zero-valued metrics feed so the dashboard reports real run data or states
  plainly that it has none; (c) re-derive the health grade from inputs that justify it, or drop the
  letter grade; (d) widen the workflow manifest to all 28 workflows or record its scope in the file.
  **Verify:** `bash scripts/ci.sh --lane verify-docs`; no dashboard reports a grade or "clean"
  status over inputs it did not measure.
  **Effort:** M · **Related:** `PRD-017`, `PRD-114`

---

## W7 — Activate the fund-economics and multi-currency kernels

**Goal:** the tested math becomes the wired path. The review's headline corollary lives here:
`W9-NAV-006` is `ready_for_acceptance` while its own factory has no production caller.

- [ ] **AR8-25 — Wire `FundEconomicsJournalFactory` into the automated-journal path.**
  **Evidence:** `src/Meridian.Ledger/FundEconomicsJournalFactory.cs:31` is referenced by exactly one
  test file and nothing in `src/`, despite a doc comment saying it exists "so the fund-economics
  kernels post real ledger entries instead of living only in tests". The wired fee path posts a
  caller-typed `decimal` (`src/Meridian.Ledger/AutomatedJournalDraftProjector.cs:100-109` over
  `AutomatedJournalEvent.Amount`). Also dark: capital-call draft/plan/schedule builders,
  `EuropeanDistributionWaterfall`, `PartnershipWaterfallProjector`,
  `CarriedInterestClawbackCalculator`, `PreferredReturnCalculator`, `EqualizationCalculator`,
  `NavPerUnitCalculator`, `ShadowNavValidator`, `ShareClassUnitRegisterProjector`,
  `DepreciationScheduleCalculator`, `FixedAssetDepreciationProjector`.
  **Change:** call the factory from `AutomatedJournalIntakeRunner` / `DailyValuationScheduler` so
  management and performance fees are computed by the day-weighted, hurdle-aware kernels rather than
  supplied. The draft → `IAutomatedJournalPostingTarget` → `DurableAutomatedJournalPoster` pipe
  already exists, so this is genuinely wiring. Then sequence the remaining kernels by operator value:
  NAV-per-unit striking → capital calls → waterfall/carry → equalization, each needing a DTO, an
  endpoint, and a screen.
  **Prerequisite — do this first.** The factory cannot be dropped in behind the current request:
  `RunFeeAccrualDraftIntakeRequest` carries NAV, high-water mark, and two rates, while the kernels
  additionally require an accrual day-count basis for management fees and a hurdle amount plus
  crystallization posture for performance fees. Wiring without them forces an implementer either to
  invent defaults or to keep passing the caller-supplied amount — both of which post materially
  incorrect governed fee journals, which is worse than the un-prorated calculation being replaced.
  Extend the request and the retained fee-term evidence with those inputs, sourced from the fund's
  governing terms, before routing any draft through the factory.
  **Verify:** integration test posting a governed fee journal whose amount is *derived*; assert the
  un-prorated path is no longer reachable, and that a request missing day-count basis, hurdle, or
  crystallization posture is rejected rather than defaulted.
  **Effort:** L (fee wiring M; full kernel sequence XL) · **Related:** `W9-NAV-006`

- [ ] **AR8-26 — Post FX revaluation at period close.**
  **Evidence:** `MultiCurrencyJournalInput`, `MultiCurrencyJournalProjector`,
  `MultiCurrencyLedgerTranslator`, `LedgerCurrencyExposure`, `LedgerCurrencyTranslation` have
  consumers only in `tests/Meridian.Tests/Ledger/LedgerIntegrationTests.cs:2206-2453`; the DB columns
  exist (`src/Meridian.Storage/Ledger/Migrations/V_ledger_026__journal_leg_currency.sql:6-7`) and
  nothing writes them.
  **Change:** wire `BuildUnrealizedFxRevaluationLines` into the period-close draft path; populate the
  journal-leg currency columns on every posting; surface CTA on the close surface.
  **Verify:** integration test: two-currency book, rate move, balanced revaluation lines drafted and
  posted at close.
  **Effort:** M

---

## W8 — Durability and concurrency floor

**Goal:** operator work survives a restart and a colleague.

- [ ] **AR8-27 — Make the operator inbox and projections durable, or fail loudly.**
  **Evidence:** `src/Meridian.Ui.Shared/Services/InMemoryOperatorInboxService.cs:9` is the only
  `IOperatorInboxService` implementation, registered only when not production
  (`WorkstationServiceCollectionExtensions.cs:197-201`); the endpoint silently skips contribution
  when the service is null, so "no work" and "not registered" are indistinguishable. Position and
  asset-event projections default to an in-memory dictionary (`:232-248`). The OMS integration
  surface keeps messages, audit queue, and signing keys in process memory and seeds
  `"meridian-local-integration-signing-key"`, accepting unsigned requests
  (`src/Meridian.Ui.Services/Services/Integrations/OmsIntegrationApiHandler.cs:17-25,174`) — and its
  name lets it slip past the ADR-019 in-memory guard.
  **Change:** add file-backed local and Postgres production stores for the inbox (mirror
  `FileComplianceApprovalStore`) and the projections; return `503` with a named cause instead of
  silently omitting. Mark `OmsIntegrationApiHandler` `[NonProductionOnlyImplementation]`, back it
  with the WAL/JSONL pattern, and fail startup when no signing key is configured.
  **Verify:** restart test — triage an inbox item, restart, assert it persists; composition test
  asserting the OMS handler is rejected by the production policy.
  **Effort:** L

- [ ] **AR8-28 — Add optimistic concurrency to the snapshot stores.**
  **Evidence:** of ~484 mutating routes only 21 sites use `ExpectedVersion` (reporting governance,
  statement casework, `AutomatedJournalScheduleStore.cs:139`); no `ETag`/`If-Match` handling exists
  anywhere in `Meridian.Ui.Shared`; `src/Meridian.Storage/Store/JsonFileSnapshotStore.cs:17`
  serializes with an in-process `SemaphoreSlim` only, so a second host process can lose writes.
  **Change:** add an `ETag`/`If-Match` endpoint filter over the `JsonFileSnapshotStore` family — the
  snapshot hash is already computable — returning `409` on mismatch, and surface a conflict-resolution
  prompt in the client. Generalize the existing `ExpectedVersion` pattern rather than inventing a
  second mechanism. Replace the in-process lock with a cross-process file lock.
  **Verify:** concurrent-write test from two host instances asserting one `409` and no lost update.
  **Effort:** L

- [ ] **AR8-29 — Give the break queue a durable, concurrent store.**
  **Evidence:** `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs:17-53` is
  the sole `IReconciliationBreakQueueRepository`; state is one JSON file guarded by a process-wide
  semaphore and a lock file; Banking, FundAccounts, and OperationsContinuity all have Postgres
  variants and this does not.
  **Change:** add `PostgresReconciliationBreakQueueRepository` mirroring `PostgresOperationsContinuityStore`,
  with a migration. This is also the prerequisite for AR8-15's bulk actions at real volume.
  **Verify:** the Docker-gated suite (running per AR8-31) covers 500-break bulk transitions.
  **Effort:** M · **Depends on:** AR8-37 (PostgreSQL in the PR lane) so the Docker-gated suite is not skipped

- [ ] **AR8-30 — Partition rate limits by user.**
  **Evidence:** `src/Meridian.Ui.Shared/Endpoints/UiEndpoints.cs:333` partitions by
  `RemoteIpAddress` at 10/minute; the per-user branch at `:344` is dead because
  `LoginSessionMiddleware` never populates `HttpContext.User` (it writes only to `HttpContext.Items`).
  **Change:** populate `HttpContext.User` with a `ClaimsPrincipal` from the session profile in
  `LoginSessionMiddleware` — which also fixes `EndpointAuthorization.TryResolveActor`'s dead fallback
  — then partition by username with an IP fallback for anonymous routes.
  **Verify:** test that two users behind one IP get independent budgets.
  **Effort:** S · **Enables:** cleaner actor resolution for AR8-33

- [ ] **AR8-31 — Give migrations operator control and a compatibility gate.**
  **Evidence:** nine feature schemas call `EnsureMigratedAsync` during composition across 94 SQL
  files; there are no down-migrations, no `--migrate` verb, and nothing prints the pending set.
  `src/Meridian.Setup/InstallationTransaction.cs:142-181` promotes any payload over any installed
  version without comparison, so a downgrade silently runs an older host against a newer schema.
  **Change:** (a) **gate the implicit startup migration** — a `--migrate --plan` verb alone gives an
  operator nothing while the nine `EnsureMigratedAsync` composition paths still apply schema changes
  the moment the new host launches. Put automatic migration behind an explicit opt-in (an apply verb
  or a configuration flag), and have startup *detect* pending migrations and refuse to serve with a
  named diagnostic instead of silently applying them; (b) add `--migrate --plan` to print the
  pending set and `--migrate --apply` to execute it; (c) record `ProductVersion` and a
  minimum-compatible schema ordinal **per migration domain**, and refuse startup on a backwards
  mismatch — note this only protects a later downgrade and is not a substitute for (a). One ledger
  is not enough: fund accounts, fund structure, security master, banking, money market, asset
  operations, direct lending, reporting, identity access, and the journal each configure a distinct
  `LedgerTableName` in their own `*MigrationRunner`, so an older host could clear a single-ledger
  check and then read an incompatible schema in another domain. Either validate every domain's
  ordinal at startup or keep one central manifest covering all of them. The runner itself (advisory
  lock, single transaction, SHA-256 drift detection) is sound and should not be rewritten.
  **Verify:** two tests — a host with pending migrations and no explicit apply refuses to serve and
  leaves the schema untouched; an older host against a newer schema fails closed with the
  diagnostic.
  **Effort:** M · **Depends on:** AR8-06 for a real rollback story

- [ ] **AR8-32 — Enforce journal immutability in the database.**
  **Evidence:** `src/Meridian.Storage/Ledger/Migrations/V_ledger_001__journal_entries.sql:31` —
  `journal_entries`/`journal_legs` have no immutability trigger and legs carry `on delete cascade`,
  while the team applied exactly that trigger to the tax-lot tables
  (`V_ledger_027__atomic_tax_lot_posting.sql:170-183`). No DB-level debits=credits constraint exists;
  balance is enforced only in `src/Meridian.FSharp.Ledger/JournalValidation.fs:25`.
  **Change:** add the same append-only trigger to both journal tables, drop the cascade in favor of
  restrict, and add a deferred per-entry balance constraint. This is the property an auditor tests
  first and the cheapest half of `W9-GOV-008`.
  **Verify:** Docker-gated tests asserting (i) `UPDATE`/`DELETE` on a posted journal raises, and
  (ii) a transaction inserting legs whose aggregate debits and credits differ **fails to commit**,
  while a balanced multi-leg insert succeeds. (i) alone only proves the append-only triggers — the
  item could be marked complete while direct SQL still commits an unbalanced journal, which is the
  half that matters to an auditor.
  **Effort:** M · **Related:** `W9-GOV-008`

---

## W9 — Authorization and governance

- [ ] **AR8-33 — Make authorization coverage universal and self-enforcing.** **Propose row**
  **Evidence:** ~360 of ~1,158 mapped routes carry no permission, role, or tenant check — including
  `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:98,141` (destructive maintenance,
  schedule deletion), `StorageEndpoints.cs:274,434` (cleanup, tier migration), `ExportEndpoints.cs`,
  `DataQualityEndpoints.cs`. `EndpointAuthorization.cs:11-13` claims coverage tests prove *every*
  mapped route declares a requirement; the scoped tests that exist
  (`tests/Meridian.Tests/Integration/EndpointTests/ConfigDirectLendingAuthorizationTests.cs:142-154`,
  `tests/Meridian.Tests/Ui/EvidenceWorkflowFabricTests.cs:3086`) are each pinned to a hand-listed
  route set, so the ungated routes fall outside every list.
  **Change:** (a) add a global endpoint filter that **denies** any route lacking
  `EndpointAuthorizationMetadata`, making the gap fail closed — but drive it from an explicit,
  reviewed anonymous allow-list, **the same list the test uses**. Applied literally without one,
  the filter would break the health and readiness probes (`/healthz`, `/readyz`, `/livez`, which
  carry no authorization metadata) and re-block `/setup/account` and `/api/auth/bootstrap`, undoing
  AR8-01. Assert in a test that the runtime list and the test list stay identical, so neither can
  drift; (b) generalize the existing scoped test to enumerate the full `EndpointDataSource` against
  that shared allow-list;
  (c) add the missing gates file by file, highest-risk first (archive maintenance, storage, export).
  **Verify:** the generalized test fails on a deliberately ungated new route.
  **Effort:** L · **Related:** `W9-GOV-008` — this is its cheapest, most separable half

- [ ] **AR8-34 — Flip tenancy to fail closed.**
  **Evidence:** the write gate defaults to `Enforce: false`
  (`src/Meridian.Ui.Shared/Endpoints/WorkstationTenantContext.cs:198-200`), enforcement requires an
  env var; `RegistryFundProfileTenantGuard.cs:63-71` returns `Allow` on any registry exception and
  its own doc says a blank scope, a tenantless caller, and an unavailable registry are all allowed;
  only 167 of ~1,158 routes carry a tenant filter.
  **Change:** default `Enforce` to true behind a documented migration flag; make the guard **deny**
  on registry failure; extend tenant filters using the Evidence Vault's fail-closed scoped pattern
  (`FileEvidenceArtifactStore.VaultAccess.cs:102-194`), which is the one place tenancy is already
  enforced properly.
  **Verify:** test that a tenantless session cannot read or write a fund-scoped record.
  **Effort:** M · **Related:** `W9-GOV-008`

- [ ] **AR8-35 — Make the approval policy matrix decisional.**
  **Evidence:** `IOperationsApprovalPolicyMatrixService.GetMatrix()` is consumed only by a GET and an
  upsert (`WorkstationEndpoints.cs:579,610`); no transition path consults it. Its
  `requiredDistinctApprovals` and `requiresIndependentReviewer` fields
  (`OperationsApprovalPolicyMatrixService.cs:136-137`) are read by nothing; real segregation-of-duties
  is hardcoded in two places (`OperationsContinuityWorkflow.cs:889,901`,
  `FileReconciliationBreakQueueRepository.Casework.cs:1113,1125`).
  **Change:** have both transition validators read the matrix; the rule shape already carries every
  field needed. Remove the hardcoded constants so there is one policy implementation, not two.
  **Verify:** test that raising `requiredDistinctApprovals` to 2 blocks a single-approver transition.
  **Effort:** M

---

## W10 — Make CI verify what users run

- [ ] **AR8-36 — Fix the CI topology.**
  **Evidence:** `.github/workflows/ci.yml:31,190,249` carry `if: github.event_name != 'pull_request'`
  on the dotnet, browser, and docs jobs, so only secret-scan runs on a PR while the workflow still
  reports on it; `ci.yml` and `meridian-ci.yml` duplicate the same three lanes on push to main.
  `quality-gate` has never been activated as a required check.
  **Change:** delete `ci.yml` or reduce it to the nightly `verify-full` job; make `meridian-ci`'s
  `quality-gate` the single required check. Operator note: `quality-gate` runs with `if: always()`
  and treats a **cancelled** lane as failure, so concurrent re-dispatches produce spurious red.
  **Keep it that way** — an earlier revision suggested excluding `cancelled`, which is wrong. Once
  `quality-gate` is the only required check, ignoring cancellation would let a timed-out or
  dependency-cancelled lane report green having produced no test result at all. Superseded runs are
  already handled by workflow-level `cancel-in-progress`; the latest run must still require every
  lane to equal `success`. The fix for the spurious red is to stop re-dispatching over a live run,
  not to weaken the gate.
  **Verify:** open a scratch PR and confirm exactly one workflow reports, with real lanes.
  **Effort:** S · **Related:** `PRD-016`

- [ ] **AR8-37 — Run the real database on every PR.**
  **Evidence:** `ci.yml:26` and `meridian-ci.yml:25` set `MERIDIAN_DISABLE_DOCKER_TESTS: "true"`;
  `scripts/ci.sh:155` filters out `Category=Integration` (69 tests); the weekly Production
  Certification lane certifies 788 of ~14,243 tests, and its schema evidence was header-only because
  teardown ran before capture.
  **Change:** add a PostgreSQL service container to `meridian-ci.yml`'s `verify-dotnet` and run the
  ledger, fund-account, and asset-operations Docker-gated suites per PR; capture schema **before**
  teardown; run Production Certification nightly rather than weekly and require two consecutive
  greens before required-check activation. Add a guard failing if the deterministic count drops
  below 788.
  **Verify:** a PR touching ledger code runs the Postgres suites and fails on a seeded regression.
  **Effort:** M

- [ ] **AR8-38 — Put the Windows lane on the gate.**
  **Evidence:** `build/scripts/ci/run-dotnet-ci-tests.py:25-33` compiles `Meridian.Wpf.Tests` as an
  empty stub off-Windows and lists `Meridian.Setup.Tests` and `Meridian.LifecycleSupervisor.Tests`
  as windows-only; `Meridian.Setup.Tests` runs in **no** lane at all — the comment admits it was
  added to the exemption list after failing the coverage gate — so `InstallationTransaction.Promote`
  and `RecoverInterruptedPromotion` are covered by tests that never execute. `windows-desktop-build.yml`
  is path-filtered, so a shared-contract change that breaks 1,697 WPF tests merges green.
  **Change:** add a Windows job to `meridian-ci.yml` running the WPF, Setup, and Supervisor suites.
  **Run it on every PR, or derive its path filter from the transitive project graph** — do not
  hand-maintain a short list. `Meridian.Wpf.csproj` also references `Meridian.Identity`,
  `Meridian.Backtesting`, `Meridian.Infrastructure`, `Meridian.Reporting`, `Meridian.Platform`,
  `Meridian.Storage`, `Meridian.Workflow`, `Meridian.Strategies`, and `Meridian.QuantScript`, so a
  four-path filter would leave the desktop lane skipped for most changes that can break it —
  recreating in a narrower form the exact gap this item exists to close. Make the exemption list assert each entry is
  actually invoked by some workflow.
  **Verify:** a shared-contract edit triggers the Windows job.
  **Effort:** M

- [ ] **AR8-39 — Re-adjudicate the quarantined accounting tests before 2026-11-01.**
  **Evidence:** six `[Fact(Skip=…)]` all cite commit `a3a01eff` (the `W9-ASSET-010` spine, registry
  `done`, priority `critical`) — `tests/Meridian.Tests/Ui/AccountingSystemIntegrationServiceTests.cs:770,1075,1474,1647,5461`
  and `tests/Meridian.Wpf.Tests/ViewModels/AccountingConfigureViewModelTests.cs:470`. One records
  that `HasLedgerBookScopedTenantAdministrationEvidence` became a no-op stub, so readiness control
  counts fell from 23 to 2.
  **What already exists — do not rebuild it:** `build/scripts/ci/check-test-skip-register.py` is a
  fail-closed register that is *stronger* than a skip-delta gate. All six entries are present in
  `build/config/testing/test-skip-register.json` with an owner, `category: quarantined`,
  `tracking: W9-ASSET-010`, `review_by: 2026-11-01`, and a substantive reason; the gate fails when a
  skip is unregistered, when a register entry has no matching skip, and **when `review_by` passes**.
  **Change:** the mechanism is working — the open question is the acceptance decision it exposes, a
  `done`/`critical` row shipping with six of its own readiness tests quarantined. Before the
  2026-11-01 expiry, decide per test whether the assertion or the implementation is wrong, then
  restore the tenant-administration evidence helper or deliberately restate the readiness contract.
  Do not let the register absorb a second renewal. The one real gap is scope: Python suites under
  `tests/scripts` are outside the register (already tracked as `PRD-112`).
  **Verify:** `python3 build/scripts/ci/check-test-skip-register.py`; zero quarantined entries
  tracking `W9-ASSET-010` remain.
  **Effort:** M

- [ ] **AR8-40 — Enforce the repo's own test-quality rules.**
  **Evidence:** `docs/ai/ai-known-errors.md:280-291` marks `AI-20260215` "fixed" while the tree holds
  117 tautological assertions, 59 bare `catch` blocks in test bodies, and 24 base-`Exception`
  assertions; 3,224 `Should().NotBeNull()` calls and 171 mock-heavy files mean a large share of the
  estate cannot fail. `tests/Meridian.Wpf.Tests/Views/DesktopWorkflowScriptTests.cs:321,343` asserts
  on README and script text rather than behavior.
  **Change:** add a `build/scripts/ci/check-test-quality.py` ratchet failing on tautological
  assertions, bare catches, and base-`Exception` assertions in `tests/**`. Model it directly on
  `build/scripts/ci/check-file-size.py`, which already implements the shrink-only baseline pattern
  against a checked-in JSON baseline — reuse that shape rather than inventing a second one.
  **Replace, do not delete, the self-referential tests.** `DesktopWorkflowScriptTests.cs:321,343`
  looks like a test asserting on prose, but it is the only thing keeping
  `windows-desktop-build.yml` and `.github/workflows/README.md` describing the same validation
  filter and commands — and AR8-38 changes that very workflow, so removing the check while editing
  the thing it guards is the worst possible order. Swap the brittle string assertions for a
  structured manifest or generator-backed parity check that survives rewording.
  **Verify:** the ratchet fails on a seeded `true.Should().BeTrue()`.
  **Effort:** M

- [ ] **AR8-41 — Add the two structural tests that would have caught this review.**
  **Evidence:** no test mounts every route, and none asserts that buttons have handlers or that
  `api.ts` exports have consumers — which is precisely why AR8-38/39/40-class defects shipped
  repeatedly. The only browser automation is the mocked-API Playwright smoke
  (`src/Meridian.Ui/dashboard/scripts/smoke-workstation.mjs`), which checks the seven nav roots.
  **Change:** (a) a smoke test mounting every route in the typed catalog. **Assert more than
  non-empty content** — a hardcoded not-connected card is non-empty, so both Family Office and the
  former Formula Workbench placeholder would have passed the naive version, which is precisely the
  class this item claims to catch. Assert instead that a discoverable route does not resolve to a
  permanent unavailable/placeholder state, and cross-check mounted routes against
  `UNWIRED_WORKSTATION_ROUTES` so a dead end is either registered as unwired or fails the test; (b) a static reachability assertion over `api.ts` exports and
  `components/meridian/*.tsx`, failing on new orphans with a frozen baseline; (c) a DI-resolution
  test asserting every registered interface with a concrete implementation resolves from the
  production container — that one test would have caught the unregistered recurring-journal service
  and AR8-12's unregistered walk-forward harness.
  **Verify:** each new test fails against the pre-fix tree and passes after W11's fixes.
  **Effort:** M · **Highest leverage in this workstream**

---

## W11 — Workstation UX quality

- [ ] **AR8-42 — Burn down the orphaned client surface.**
  **Evidence:** 126 of 406 exported functions in `src/Meridian.Ui/dashboard/src/lib/api.ts` are
  unreachable — 74 with zero references anywhere, 52 test-only. Clusters: the entire Strategy
  Designer client (`api.ts:3891-3920`), user administration (`api.ts:1203+`), storage/retention
  maintenance (`api.ts:1039+`), data-quality gap/anomaly/completeness (`api.ts:3691+`),
  `getEvidenceGraph` (`api.ts:1684`).
  **Change:** triage all 126 into *wire a screen* or *delete*; do not leave a third category. The
  reachability gate from AR8-41 freezes the result.
  **Verify:** the gate's baseline drops to the triaged number and cannot grow.
  **Effort:** L · **Depends on:** AR8-41

- [~] **AR8-43 — Wire or disable the Strategy Designer's primary actions.** *(Partial, deliberately left open. Shipped: both actions are disabled, the blocker lives in the view-model so status/summary/aria-label/button agree, each reason is visible text wired via `aria-describedby` for keyboard and screen-reader operators, and a regression test covers it. **Not** shipped, so the box stays unticked: the ESLint rule banning handler-less buttons, and the save round-trip — wiring needs a `StrategyBuilderDocument` → `StrategyDesignDocument` mapper that does not exist, and a run needs six governed evidence arrays this screen cannot collect.)*
  **Evidence:** `src/Meridian.Ui/dashboard/src/screens/strategy-designer-screen.tsx:112-115` — "Save
  draft" has no `onClick`, no `asChild`, no submit; `:116-125` — "Run backtest proof" wires
  `disabled`/`disabledReason` but no handler, so it is *enabled* when validation passes and clicking
  is a silent no-op that loses work on navigation. The client functions already exist
  (`api.ts:3907,3919`); the view-model makes no network calls at all.
  **Change:** bind both to the existing functions; if the backend is not ready, render both disabled
  with a reason (the `Button` component already supports this — see
  `operator-readiness-console.tsx:405`). Add an ESLint rule banning a `<Button>` with no handler,
  `asChild`, `type="submit"`, or `disabled`.
  **Verify:** for the accepted disabled outcome — a test asserts both actions are disabled and that
  each reason is reachable through `aria-describedby`, not only the `title` attribute (shipped). For
  the remaining work — the lint rule fails on a seeded handler-less button, and a test asserts the
  save round trip once a document mapper exists.
  **Effort:** S (shipped part) / M (mapper plus wiring)

- [~] **AR8-44 — Remove the navigable dead ends.** *(Partial. The Quant Lab formulas dead end is genuinely closed — the tab is withdrawn and a stale `?view=formulas` link degrades to the script lab. Family Office is only **suppressed from discovery**: out of nav and the palette, but `app.tsx` still mounts `FamilyOfficeScreen` on `/portfolio/family-office`, so an existing bookmark still lands on the permanent not-connected screen — deliberately, so old links resolve. Still open: wire `entityStructure` from the fund-structure read model or redirect the route, and mount the built formula workbench.)* Covered by AR8-Q1 for the routing guard; also
  remove the command-palette entry that advertises the unmounted Formula Workbench
  (`command-palette.view-model.ts:254-258`) or mount the existing 319-line
  `components/meridian/strategy-formula-workbench.tsx`, and either wire `entityStructure` into
  `FamilyOfficeScreen` (`app.tsx:763`, `family-office-screen.tsx:80,175`) from the fund-structure
  read model or keep it out of nav.
  **Effort:** S

- [ ] **AR8-45 — Stop converting API failures into empty data.**
  **Evidence:** `src/Meridian.Ui/dashboard/src/screens/asset-detail-screen.tsx:247-252` swallows
  three of four sibling request failures into `null`/`[]` and writes them straight to state, so a
  down corporate-actions service renders as *no corporate actions* — indistinguishable from clean.
  The same pattern appears at `accounting-screen.tsx:2131`. 22 catch sites discard the error object;
  14 of 21 error-handling screens offer no retry; `components/ui/async-region.tsx` — which
  implements exactly this contract — is adopted by 1 of 68 screens, while 18 screens hand-roll
  `useEffect` + `useState` fetching.
  **Change:** replace every `.catch(() => [])` with a per-region error state; migrate the 18
  hand-rolled screens to `AsyncRegion` with `onRetry`; pipe `describeApiError(err)` into the detail
  line. This single migration fixes the silent-wrong-answer class, the missing-retry class, and
  restores per-panel containment so one panel's failure stops replacing the whole route.
  **Verify:** test that a failed corporate-actions call renders an error region, not an empty list.
  **Effort:** L · **Highest user-visible value in this workstream**

- [ ] **AR8-46 — Give operators the reason, not the status code.**
  **Evidence:** 395 sites return `new { error = "..." }` (e.g.
  `src/Meridian.Ui.Shared/Endpoints/HistoricalEndpoints.cs:35,57,75,87,97,116,121`), which the
  client normalizer never reads (`api-errors.ts:55`); `UseStatusCodePages`
  (`src/Meridian/UiServer.cs:542`) cannot help because a body is already written. Only 116 sites use
  the RFC-7807 `ApiProblemDetails` helper.
  **Change:** AR8-Q2 gives immediate relief; then mechanically rewrite the 395 sites to
  `ApiProblemDetails.Validation/NotFound/Conflict` so there is one error contract.
  **Verify:** a contract test asserting every mapped route's error body parses as problem+json.
  **Effort:** M

- [ ] **AR8-47 — Push the events operators wait on.**
  **Evidence:** only three SSE endpoints exist; reconciliation breaks, approvals, ledger postings,
  and job progress all poll via `use-workstation-data.ts:1028-1045`. `/api/events/stream`
  (`src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:225-258`) re-serializes the entire status
  payload every 2s regardless of change.
  **Change:** reuse the existing `StreamBroadcaster`/`StreamTopic` fan-out
  (`src/Meridian.Ui.Shared/Streaming/`) for operator-inbox and reconciliation-break topics; convert
  `/api/events/stream` to change-triggered emission with a heartbeat, as
  `WorkstationEndpoints.Stream.cs:71-101` already does correctly.
  **Scope the topic key before adding either stream.** `StreamBroadcaster<TPayload>` builds one
  payload per topic and sends that identical object to every subscriber, with no per-subscriber
  authorization filtering. The existing `report-run:<id>` stream is safe because tenant and company
  are encoded into the `StreamTopic`; inbox items and reconciliation breaks carry owners, evidence,
  approvals, and amounts, so an unscoped topic would fan one tenant's queue out to another's
  session. Encode tenant, company, and any permission-sensitive scope into both the topic key and
  the query that builds the payload, and add a test that two tenants subscribed simultaneously
  never receive each other's items.
  **Effort:** M

- [ ] **AR8-48 — Split the monolith screens and cap the route bundles.**
  **Evidence:** `settings-screen.tsx` is 7,397 lines with 22 root `useState` calls;
  `accounting-screen.tsx` is 6,126 and its view-model 7,147; the Accounting route costs ~782 KB of
  JS before render; `vite.config.ts:173-176` sets no `manualChunks` or size limit. 81 routes, 53 nav
  destinations, 13 legacy redirect tombstones, `AssetDetailScreen` mounted at two routes.
  **What already exists:** `build/scripts/ci/check-file-size.py` is a no-new-god-file ratchet that
  keeps oversized files from growing. It caps the problem but does not shrink it, so the lever here
  is lowering its baseline as sections move out — not adding a new gate.
  **Change:** split `settings-screen.tsx` along the six sections already enumerated in
  `settings-route-state.ts:32-46`, lazy-load per `?view=`, and drop the ratchet baseline with each
  split; add a `chunkSizeWarningLimit` and a CI bundle-budget check; de-duplicate the
  double-mounted route. **Keep the 13 redirect tombstones.** An earlier revision proposed deleting
  them and letting the 404 panel absorb stale bookmarks; that was wrong. `legacyWorkspaceRedirect`
  deliberately preserves query and hash state while mapping supported old paths
  (`/accounting/trial-balance`, `/accounting/evidence`, `/data/watchlist`, and the legacy workspace
  aliases) to their current destinations, with explicit regression tests. They are a few lines of
  routing, not meaningful bundle weight, and deleting them would break saved links. If they must
  go, use a versioned deprecation with a migration path — not a 404.
  **Effort:** L · **Related:** `W8-UX-CONSOL-001`

---

## W12 — Data and deliverable integrity

- [ ] **AR8-49 — Wire or archive the dead data-quality estate.**
  **Evidence:** seven modules under `src/Meridian.DataIntegration/Monitoring/` (~3,600 lines:
  `TimestampMonotonicityChecker` 865, `BadTickFilter` 650, `PriceContinuityChecker` 491,
  `SpreadMonitor` 417, `TickSizeValidator` 388, `DataLossAccounting` 214, `ClockSkewEstimator` 115)
  have no consumer outside their own tests. `DataFreshnessSlaMonitor`'s two ingress methods
  (`.../DataQuality/DataFreshnessSlaMonitor.cs:184,213`) are never called, so `/api/sla/*` reports
  "healthy, zero symbols" forever; `PrometheusMetrics.UpdateSlaMetrics`/`RecordProcessingLatency`
  (`src/Meridian.Application/Monitoring/PrometheusMetrics.cs:669,591`) have no production callers, so
  the stale-data alert can never fire and `alert-rules.yml:229-233` carries a `> 0` guard to suppress
  it. `ProviderDataQualityValidator`'s stale threshold is omitted by both real callers.
  **Change:** wire the monitors into `QualityMonitoringPublisher` (already registered at
  `PipelineFeatureRegistration.cs:245`) behind config flags, call `RegisterSymbol`/`RecordEvent` from
  the subscription orchestrator and publisher, and give the validator a non-null default threshold
  derived from the provider's declared cadence. Anything not wired moves to `archive/`. Extend
  `build/scripts/ci/validate-observability-contract.py` to **fail on any declared metric with no
  writer** — the gate that would have caught this.
  **Verify:** a frozen feed raises an SLA violation and trips the alert rule.
  **Effort:** L

- [ ] **AR8-50 — Make cross-provider comparison reachable.**
  **Evidence:** `CrossProviderValidator` is constructed with `enableCrossValidation: false` at every
  call site (`src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs:484`,
  `src/Meridian.Application/Backfill/BackfillCoordinator.cs:401`, defaulted false at
  `BackfillWorkerService.cs:1295`); `CrossProviderComparisonService` needs two concurrent providers
  while `src/Meridian.Infrastructure/Adapters/Failover/FailoverAwareMarketDataClient.cs:47` holds one
  active client and switches rather than fanning out.
  **Change:** add a shadow-subscription mode to the failover client that streams the backup provider
  read-only into the comparison service. **Do not simply flip `enableCrossValidation` to true on the
  backfill composite.** Every successful primary request would synchronously issue a second
  full-range request to another provider (`CrossProviderValidator.cs:51-52`) while the validator
  compares only the first five bars (`:57`) — doubling foreground latency, vendor quota, and
  rate-limit pressure on every request for a five-bar sample, which can stall or exhaust a
  multi-year backfill. Sample instead: compare a bounded number of sessions, and make comparison
  opt-in with an explicit rate budget rather than an unconditional default.
  **Verify:** test that divergent prices from two providers raise a comparison warning.
  **Effort:** M

- [ ] **AR8-51 — Make exports and evidence usable.**
  **Evidence:** `LedgerReportTable.Rows` is `IReadOnlyList<IReadOnlyList<string>>`
  (`LedgerReportPresentation.cs:9`) and the ClosedXML renderer assigns strings directly
  (`src/Meridian.Documents/FinancialReportDocumentRenderer.cs:295`), so every statement except
  partners' capital exports text cells; the dependency-free fallback emits `t="inlineStr"` for every
  cell. Evidence Vault has no extraction — no OCR or PDF text-layer dependency exists — and
  `ExtractedFields` is caller-supplied (`src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs:565`),
  so its six-state lifecycle wraps hand-keyed data; intake is base64-in-JSON, not multipart.
  **Change:** carry `decimal` through `LedgerReportTable` alongside the formatted string and apply
  the existing `MoneyNumberFormat` generically. For evidence, add a text-layer PDF extractor feeding
  `ExtractedFields` so the review/accept workflow earns its keep, and accept multipart upload.
  **Verify:** an exported balance sheet sums in a spreadsheet; a text-layer PDF populates fields.
  **Effort:** M (XLSX) / L (extraction)

- [ ] **AR8-52 — Harden statement intake.**
  **Evidence:** all five connectors decode with hardcoded `Encoding.UTF8.GetString`
  (`CsvStatementConnector.cs:56`, `Ofx/OfxStatementConnector.cs:57`, `Bai2/…:40`, `Camt/…:41`,
  `IbFlex/…:81`), and `Ofx/OfxDocumentParser.cs:265-269` discards the SGML header that declares the
  charset; UTF-8 replacement fallback means bad bytes become U+FFFD silently. Dedupe is file-level
  only (`src/Meridian.Domain/Reconciliation/BrokerStatementModels.cs:39-51`) despite every built-in
  profile mapping an external transaction id, so overlapping or amended statements double-import.
  `StatementMappingProfileCatalog.cs:69-115` compares column-name sets only and returns early for
  built-in profiles, so drift detection never activates out of the box.
  **Change:** read the OFX `CHARSET`/`ENCODING` header and the XML declaration before decoding,
  default unknown bytes to Windows-1252 with a `StatementParseIssue.Warning`, and use
  `new UTF8Encoding(false, throwOnInvalidBytes: true)` so undecodable input errors instead of
  mojibaking. Add transaction-level idempotency — but **not** a bare
  `(externalAccountId, externalTransactionId)` pair: `Bai2StatementConnector.cs:113` and
  `Camt053StatementConnector.cs:146` both emit `ExternalTransactionId: null` for valid records, and
  `ResolveReference`/`EntryReference` are nullable elsewhere, so ignoring nulls preserves duplicates
  while normalizing them to empty collapses distinct transactions. Namespace non-empty ids by
  provider and accounting scope, and define a deterministic fallback fingerprint (account, value
  date, amount, sign, normalized description) for records whose source supplies no identifier.
  **Do not use a file-local ordinal as identity.** Two overlapping statements can carry different
  subsets of otherwise identical no-id transactions, so a transaction that is `#2` in one file
  becomes `#1` in the next while a genuinely new identical transaction also claims `#1` — the key
  then either re-imports an existing row or silently discards a real one. Prefer stable source
  evidence where the format supplies it (sequence number, running balance, posting order within a
  statement page), and route transactions that remain genuinely indistinguishable to operator
  review rather than resolving them automatically. Let built-in profiles carry a
  per-account accepted fingerprint and compare order and type, not just the name set.
  **Verify:** a Windows-1252 OFX imports with correct accents; re-importing an overlapping statement
  adds no duplicate rows.
  **Effort:** M · **Related:** `W9-INGEST-009`

---

## W13 — Program hygiene

- [ ] **AR8-53 — Reconcile the status surface.**
  **Evidence:** the P0 tracker says 21 rows with "nine open W9 rows planned" while the registry shows
  6 `ready_for_acceptance` and 3 otherwise; `docs/engineering/production-certification-evidence-chain.md:11`
  says 18 P0 rows against the tracker's 21; the README presents nine "Complete baseline" capabilities
  above the disclaimer that no P0 is certified; `status: accepted` is used by **zero** registry rows,
  so the acceptance gate in the taxonomy has never been exercised.
  **Change:** let one generator own every count and have the tracker, evidence chain, and README
  render from it. Either exercise `status: accepted` or remove it from the taxonomy. Put the
  production-readiness disclaimer *inside* the README capability table.
  **Verify:** `scripts/check_status_delivery_claims.py` extended to fail when two active docs report
  different P0 totals.
  **Effort:** M

- [ ] **AR8-54 — Make WPF parity honest in-product.**
  **Evidence:** `docs/development/wpf-web-ui-alignment-plan.md` shows 15 of 29 screens "Partial" and
  2 hard gaps, assessed 2026-07-06. The desktop lane does carry real governed actions — journal
  approve/post/reverse and rules-studio promotion approval
  (`src/Meridian.Wpf/ViewModels/Accounting/AccountingConfigureViewModel.cs:360-375`), close-evidence
  review and period locking (`.../AccountingCloseViewModel.cs:1425-1468`) — but
  operations-continuity approval/close/reopen and Evidence Vault accept/reject remain browser-first.
  **Change:** label browser-first surfaces in the WPF shell so a desktop operator is never silently
  short of a mutation; refresh the matrix's assessment date with each fold; keep the parity decision
  explicit per screen (achieve parity, or declare browser-first).
  **Effort:** M · **Related:** `W8-WPF-PARITY-001`

- [ ] **AR8-55 — Re-enable the known-errors register.**
  **Evidence:** `docs/ai/ai-known-errors.md` is stale (newest entry `AI-20260318`, ~5 months), its
  intake job is archived, and `build/scripts/ai-repo-updater.py known-errors` returns empty
  `prevention_checklist` and `verification_commands` for every entry — the extractor is broken
  against its own format. The register is weighted toward compiler errors and silent on the failure
  modes the reviews actually find (unwired services, fabricated status, in-memory fallbacks).
  **Change:** fix the extractor, and add entries for the three recurring product-risk classes this
  review found so future contributors are warned about them rather than only about CS0246.
  **Verify:** `python3 build/scripts/ai-repo-updater.py known-errors` returns populated fields.
  **Effort:** S

---

## Proposed registry rows

These four are net-new scope rather than activation, so per the scope gate they warrant real
registry rows rather than silent fixes:

| Proposed | Covers | Rationale |
|---|---|---|
| Promotion evidence integrity | AR8-12, AR8-13, AR8-14 | The gate protecting real money currently accepts typed numbers; the fix changes a governance contract. |
| Live portfolio-state seeding | AR8-16 | Live risk correctness depends on a new broker-state dependency and a fail-closed startup rule. |
| Endpoint authorization coverage | AR8-33 | ~360 routes plus a global filter is a cross-cutting security change; the cheapest half of `W9-GOV-008`. |
| Statement transaction-level idempotency | AR8-52 | Dedupe keys are a durable data contract, not a bug fix. |

## Coverage check

Counts are mechanical, not asserted — regenerate them by counting `^- \*\*` bullets per section in
the review and `AR8-` identifiers per workstream here.

**The review carries 51 findings** (an earlier revision of this section said 49; that was a
miscount): §1 first mile 7 · §2 activation 7 · §3 truth 7 · §4 gates 9 · §5 durability 5 ·
§6 UX 7 · §7 assurance 6 · program-level 3.

**This plan carries 54 numbered todos**, `AR8-01`–`AR8-55` with `AR8-04` reserved as an alias of
`AR8-Q3` rather than a separate item: W1 (7) · W2 (3) · W3 (3) · W4 (1) · W5 (1) · W6 (8) ·
W7 (2) · W8 (6) · W9 (3) · W10 (6) · W11 (7) · W12 (4) · W13 (3) = 54. The six `AR8-Q*` quick wins
are sequencing aliases of items that also appear in a workstream, so they are not counted again.

**Reconciliation of 51 findings to 54 todos** — three todos have no 1:1 finding because they are
mechanisms the findings imply rather than describe:

| Section | Findings | Todos |
|---|---|---|
| §1 first mile | 7 | `AR8-01`, `AR8-02`, `AR8-03`, `AR8-Q3`, `AR8-05`, `AR8-06`, `AR8-07` **+ `AR8-08`** (the P0 publish→sign→install chain named in the section's opening) |
| §2 activation | 7 | `AR8-42`, `AR8-43`, `AR8-15`, `AR8-25`, `AR8-26`, `AR8-49`, `AR8-50`, `AR8-12` |
| §3 truth | 7 | `AR8-17`, `AR8-20`, `AR8-21`, `AR8-19`, `AR8-22`, `AR8-23`, `AR8-24` **+ `AR8-18`** (the provenance-derivation mechanism behind the banner finding) |
| §4 gates | 9 | `AR8-13`, `AR8-14`, `AR8-16`, `AR8-09`, `AR8-11`, `AR8-35`, `AR8-32`, `AR8-33`, `AR8-34` **+ `AR8-10`** (the fat-finger/collar rules named as `W9-SAFETY-007`'s remainder) |
| §5 durability | 5 | `AR8-27`, `AR8-28`, `AR8-29`, `AR8-30`, `AR8-31` |
| §6 UX | 7 | `AR8-44`, `AR8-45`, `AR8-46`, `AR8-47`, `AR8-48`, `AR8-51`, `AR8-52` |
| §7 assurance | 6 | `AR8-36`, `AR8-37`, `AR8-38`, `AR8-39`, `AR8-40`, `AR8-41` |
| program-level | 3 | `AR8-53`, `AR8-54`, `AR8-55` |

51 findings + 3 implied mechanisms = 54 todos, with every finding represented.

Strengths named in the review — the ADR-019 composition policy, the bias-disclosure report, the
reconciliation matching engine, OMS pre-trade enforcement, the durability primitives, and the auth
primitives — are **not** to be regressed by any item above; several plans deliberately extend them
rather than replace them. `AR8-24` and `AR8-40` were rewritten after review to *preserve* two
controls an earlier draft proposed deleting.
