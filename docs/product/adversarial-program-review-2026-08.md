# Adversarial Program Review — Meridian (2026-08)

**Status:** independent review input; not a governance or roadmap-status document
**Owner:** review author (independent adversarial pass)
**Reviewed:** 2026-08-10
**Scope:** whole-program review of Meridian's high-level functionality, focused on what a real end
user can install, run, trust, and complete — and where improvement would raise end-user value most.
**Method:** seven parallel adversarial investigations over the wired code paths (browser
workstation, server/API seam, market data and providers, trading lane, fund operations and
accounting, install/run/operate, and the program's own self-assessment plus test/CI quality). The
highest-impact claims were independently spot-verified a second time. Every finding is anchored to
`file:line` so it is directly actionable.

> This review is deliberately critical; a strengths section gives fair credit at the end. It builds
> on the 2026-07 review (`adversarial-program-review-2026-07.md`) and its 2026-07-26 follow-up
> (`archive/docs/assessments/adversarial-program-review-2026-07-26.md`), re-tests their headline,
> and extends coverage to areas those passes did not reach (concurrency, statement-connector
> robustness, CI truthfulness, the installer/operations lane, and the browser client's error
> handling). Live status stays in the roadmap registry; nothing here competes with it.

## Headline

The 2026-07 headline — **"the codebase is dramatically more capable than the running product"** —
is still the single best description of the program, and it now has a second-order corollary:

> **The roadmap's own acceptance statuses are starting to drift ahead of wired reality.** Six W9
> rows are `ready_for_acceptance`, yet inside several of those same lanes the capability the row
> names is still not the wired operator path. The program's rule that "unwired and finished must
> never look identical" (design charter §2.1) now applies to its own registry.

Three examples of the corollary:

- `W9-NAV-006` (fund economics) is `ready_for_acceptance` with golden-file evidence, but
  `FundEconomicsJournalFactory` — whose own XML doc says it exists "so the fund-economics kernels
  post real ledger entries instead of living only in tests" — is referenced by exactly one test
  file and nothing in `src/` (`src/Meridian.Ledger/FundEconomicsJournalFactory.cs:31`). The wired
  fee path still posts a caller-typed `decimal`
  (`src/Meridian.Ledger/AutomatedJournalDraftProjector.cs:100-109`).
- `W9-TRUTH-001` (truthful simulation posture) is `ready_for_acceptance`, but the provenance banner
  is computed from persistence bindings, not from which tape is playing
  (`src/Meridian.Application/Composition/ProductionServiceRegistrationPolicy.cs:214-226`): a host
  with durable file stores and `DataSource = Synthetic` (the config default,
  `src/Meridian.Core/Config/AppConfig.cs:48`) reports `DataProvenance.Real` and renders no banner.
- `W9-PAPER-003` (paper realism) landed a real shared matching policy, but defaults still fill
  instantly, in full, at top-of-book, with `SlippageBasisPoints` defaulting to zero
  (`src/Meridian.Execution/PaperMatching/PaperTradingCostOptions.cs:54`,
  `src/Meridian.Execution/PaperMatching/PaperOrderMatchingPolicy.cs:56,194-199`) — no partial
  fills, no latency, no size constraint.

The rest of this review is organized by end-user value area. Each finding names the user harm and
the concrete improvement.

## 1. The first mile: a new user cannot reach value

This is the program's most severe end-user problem, and its own P0 tracker agrees: **0 of 21 P0
rows are production-certified** and nothing has ever shipped (`docs/product/implementation-todo-list.md:37`;
no git tag exists, so the installer pipeline `desktop-installer-packaging.yml` has never run).

- **The README's flagship launch command fails closed with no escape.** A fresh clone running
  `dotnet run --project src/Meridian/Meridian.csproj -- --mode workstation` defaults to
  `Production`, so auth resolves to `Required`
  (`src/Meridian.Identity/Application/AuthenticationMode.cs:42-44`) and every request 503s with an
  instruction to set `MDC_USERS` with `passwordHash` values
  (`src/Meridian.Ui.Shared/Endpoints/LoginSessionMiddleware.cs:198`) — but no CLI verb, endpoint,
  or documented recipe produces that PBKDF2 hash; the only callers of
  `PasswordHashing.HashPassword` are internal (`src/Meridian.Identity/Infrastructure/UserAccountStore.cs:703`).
  *Improvement:* add `--create-user` / `--hash-password` verbs to `ConfigCommands` and have
  `--quickstart` offer to create the first local admin.
- **The one command that works, works by relaxing the safety posture.** `--seed-demo` reaches a
  populated screen by setting `MERIDIAN_USE_INMEMORY_GOVERNANCE=true`,
  `DOTNET_ENVIRONMENT=Development`, and `MDC_AUTH_MODE=optional`
  (`src/Meridian/DemoWorkspaceCli.cs:106-131`) — a configuration ADR-019 marks unsupported.
  Evaluators form their "it works" impression in a posture the project forbids.
  *Improvement:* print a banner from `SeedAsync` naming each relaxed default and the graduation
  command.
- **Every experimental deployment manifest is broken, not merely unsupported.** The Dockerfile
  copies 12 `.csproj` files but the host references 14+, so `dotnet restore` fails before source
  copy (`deploy/docker/Dockerfile:24-41` vs `src/Meridian/Meridian.csproj:70-84`); the container
  would then fail transport validation anyway; the compose healthcheck probes `/health`, which is
  not auth-exempt (`src/Meridian.Ui.Shared/Endpoints/LoginSessionMiddleware.cs:66-75`), and `curl
  -f` treats the 302 as success; compose has no PostgreSQL service; the systemd unit runs `dotnet
  run` from source under `ProtectSystem=strict` with no writable `obj/`
  (`deploy/systemd/meridian.service`). *Improvement:* fix or move `deploy/docker|k8s|systemd` to
  `archive/` until PRD-013 reopens them; a `docker build` smoke job would have caught this the day
  it broke.
- **The blessed devcontainer leaves the money path in memory.** It exports only the
  security-master and direct-lending connection strings (`.devcontainer/docker-compose.yml:27-28`),
  so the ledger, approvals, and reconciliations run `PERSISTENCE: PARTIAL` and vanish on restart.
  One `MERIDIAN_DATABASE_URL` line makes all ten domains durable via `ApplyUnifiedDatabaseUrl`.
- **The shipped sample config advertises a provider that does not exist and invites plaintext
  broker passwords for it.** `config/appsettings.sample.json:88,585-619` names `"StockSharp"` (not
  a `DataSourceKind` member — the converter throws on it,
  `src/Meridian.Core/Config/DataSourceKindConverter.cs:27-28`) and ships `Rithmic.Password` /
  `CQG.Password` JSON fields, contradicting its own "never store secrets here" banner.
  *Improvement:* delete the block; CI-check sample config values against the enum.
- **No operator backup/restore exists for the installed product.** The consumer installer embeds a
  private PostgreSQL runtime but not `pg_dump`, and the canonical recovery script is not in the
  payload (`build/scripts/install/build-consumer-setup.ps1:31-53`); the recovery runbook requires a
  verified backup the user cannot produce (`docs/operators/failover-and-recovery.md:96-101`).
  *Improvement:* ship `pg_dump` and add `backup`/`restore` verbs to the lifecycle supervisor.
- **On non-Windows, the credential vault stores its AES key next to the vault, world-readable.**
  `FileProviderCredentialStore` writes the raw 32-byte key to the data root with only a Windows
  `Hidden`-attribute attempt (`src/Meridian.DataIntegration/Credentials/FileProviderCredentialStore.cs:639-724`);
  no `File.SetUnixFileMode` call exists in the credential path. *Improvement:* chmod 600 both
  files, refuse group/other-readable keys, plan for OS keyrings.

## 2. The activation gap: built capability the user cannot reach

The W10 slate already concedes "six of the eleven rows wire existing code rather than write new
code." Independent measurement says the gap is wider than the slate:

- **126 of 406 browser API client functions (31%) are unreachable from any screen** — 74 with zero
  references anywhere, 52 referenced only by tests (analysis over
  `src/Meridian.Ui/dashboard/src/lib/api.ts`). Orphaned clusters include the entire Strategy
  Designer client (`api.ts:3891-3920`), user administration (`api.ts:1203+`), storage/retention
  maintenance, and data-quality gap/anomaly/completeness reports.
- **The Strategy Designer's two primary buttons are wired to nothing.** "Save draft" has no
  `onClick`, no submit, no link (`src/Meridian.Ui/dashboard/src/screens/strategy-designer-screen.tsx:112-115`);
  "Run backtest proof" wires `disabled`/`disabledReason` but no handler (`:116-125`). When
  validation passes, clicking is a silent no-op and navigation loses the work. The matching client
  functions exist (`api.ts:3907,3919`). This is worse than a disabled button: it signals success.
- **Reconciliation casework is read-only in practice.** Of 19 break-workflow client functions, only
  queue/review/resolve are wired (`src/Meridian.Ui/dashboard/src/screens/accounting-screen.view-model.ts:2935-2938`);
  assign, comment, root cause, resolution code, sign-off, reopen, and both bulk actions have no UI
  path — while "Casework" is a top-level Accounting nav item. Clearing 500 breaks is 500 clicks;
  the fully-built bulk endpoint (dry-run, idempotency key, partial success —
  `ApplyBulkCaseworkAsync`) is called by no screen, and the server caps bulk at 100
  (`src/Meridian.FinancialOperations/Reconciliation/FileReconciliationBreakQueueRepository.cs:17`).
- **The fund-economics spine is dark** (headline corollary above): capital calls, European
  waterfall, preferred return, clawback, equalization, NAV-per-unit, shadow-NAV validation,
  depreciation projectors — 30+ types in `Meridian.Ledger` with no production consumer. Multi-
  currency translation and FX revaluation likewise have consumers only in
  `tests/Meridian.Tests/Ledger/LedgerIntegrationTests.cs:2206-2453`; the DB columns exist
  (`database/.../V_ledger_026__journal_leg_currency.sql:6-7`) and nothing writes them.
- **~3,600 lines of data-quality monitors are dead code.** `TimestampMonotonicityChecker`,
  `BadTickFilter`, `PriceContinuityChecker`, `SpreadMonitor`, `TickSizeValidator`,
  `DataLossAccounting`, `ClockSkewEstimator` (`src/Meridian.DataIntegration/Monitoring/`) are
  referenced by nothing but their own unit tests. The freshness-SLA monitor is registered and
  exposed via five endpoints, but its two ingress methods are never called
  (`src/Meridian.Application/Monitoring/DataFreshnessSlaMonitor.cs:184,213`), so `/api/sla/*`
  reports "healthy, zero symbols" forever, and the corresponding Prometheus gauges are declared
  but never written (`src/Meridian.Application/Monitoring/PrometheusMetrics.cs:591,669`) — the
  "is my data stale?" alert can never fire.
- **Cross-provider price validation is structurally unreachable.** `CrossProviderValidator` is
  constructed with `enableCrossValidation: false` at every call site
  (`src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs:484`,
  `src/Meridian.Application/Backfill/BackfillCoordinator.cs:401`), and the comparison service
  requires two concurrent providers while failover holds exactly one active client
  (`src/Meridian.Infrastructure/Failover/FailoverAwareMarketDataClient.cs:47`).
- **The walk-forward harness is not registered in the browser host at all** — its only DI
  registration is in the WPF app (`src/Meridian.Wpf/Features/Strategy/StrategyFeatureModule.cs:82`),
  so a browser-only operator cannot generate legitimate out-of-sample evidence (see §4 for what
  that forces).

*Improvement for the whole section:* treat activation as a measured, gated quantity, as the design
charter already prescribes (Activation Ratio, charter §1.5): add a CI reachability gate over
`api.ts` exports and a DI-resolution test asserting every registered interface with a concrete
implementation is resolvable from the production container — the latter alone would have caught the
unregistered recurring-journal service the W10 slate documents. Then burn down the orphan list
screen-first (casework verbs and bulk actions are the highest-frequency wins).

## 3. Truth discipline: the brand is half-delivered

"Meridian proves the number" — but several wired paths still fabricate, mislabel, or silently
substitute:

- **Tick provenance is wrong at the source.** `MarketEvent` has no provenance field, and its
  factory defaults stamp `Source = "IB"` for trades and `"ALPACA"` for quotes
  (`src/Meridian.Domain/Events/MarketEvent.cs:14,32,38`); the four shared collectors publish
  without passing a source (`src/Meridian.Domain/Collectors/TradeDataCollector.cs:237`,
  `QuoteCollector.cs:40`, `MarketDepthCollector.cs:122`, `L3OrderBookCollector.cs:189`), so a
  Polygon trade is stored attributed to Interactive Brokers. Every origin-aware consumer —
  per-provider quality attribution, replay, the simulated-origin safety net
  (`src/Meridian.Contracts/Operations/DataProvenance.cs:92-102`) — reads a wrong vendor name.
- **The default build's "Interactive Brokers" is a random walk that also silently drops depth
  subscriptions.** Without the opt-in vendor SDK, `IBMarketDataClient` delegates to
  `IBSimulationClient` while still reporting `ProviderDisplayName => "Interactive Brokers"` and
  full capabilities including `Level2Book` (`src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBMarketDataClient.cs:61-118`);
  the simulator registers depth subscriptions it never services
  (`IBSimulationClient.cs:178,208`) and always emits `SequenceNumber: 0`, defeating gap detection.
- **Synthetic reference data wears real identifiers, and unknown symbols silently become SPY.**
  The synthetic catalog carries genuine FIGI/ISIN/CUSIP values for its five symbols and aliases
  any other ticker to SPY's economics under the requested name
  (`src/Meridian.Infrastructure/Adapters/Synthetic/SyntheticReferenceDataCatalog.cs:44-49,265`).
  Request `BRK.A`, get a well-formed fake with a real ISIN and no warning.
- **A single failed probe permanently brands a real install as SIMULATED.** The demo-mode fetch
  runs once with an empty catch and no retry (`src/Meridian.Ui/dashboard/src/app.tsx:205-207`);
  null provenance renders the non-dismissable red "SIMULATED — do not treat P&L as real" banner
  for the whole session (`app-shell.data-provenance-badge.ts:101`,
  `components/meridian/data-provenance-banner.tsx:28-36`). A well-built fixture notice with a
  "Retry live data" button exists and is rendered by nothing
  (`app-shell.development-fixture-notice.ts`). Operators will learn to ignore the banner — the
  exact safety signal the product leads with. The symmetric failure also exists: fail-open
  `Real` when synthetic streaming feeds durable stores (headline corollary).
- **The WPF shell fabricates operator-facing status.** "Check for Updates" shows a hardcoded "You
  are running the latest version (1.6.1)" with no network call — while the assembly version is
  1.0.0 — and "Recent Activity" hardcodes three fake entries including "Cloud sync completed"
  for a product with no cloud sync (`src/Meridian.Wpf/ViewModels/SettingsViewModel.cs:860-889`).
- **The Lean integration returns fabricated lifecycle state.** `POST /api/lean/backtest/start`
  writes `"queued"` into a process-local static dictionary; no code path ever sets
  running/completed, and results return hardcoded zeros
  (`src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs:250-313`). A full WPF surface sits on top.
  *Improvement:* return `501` until a real launcher exists (also the P0 tracker's `PRD-020`).
- **Dead status dashboards still publish optimistic numbers.** Seven `docs/status/` dashboards are
  stamped `1970-01-01`; `metrics-dashboard.md` reports 0 runs/0 tests/0.0% success, and
  `doc-health-dashboard.md` still prints "80/100 — Rating: Good". *Improvement:* delete them; a
  missing dashboard is honest.

## 4. Gates that do not gate: governance and safety enforcement

For a governance product, the sharpest findings are the gates that accept assertions instead of
evidence:

- **The live-promotion gate's strongest quantitative check accepts caller-typed numbers.** The
  promotion policy blocks paper→live on walk-forward thresholds
  (`src/Meridian.Strategies/Services/PromotionService.cs:153-160`), but the evidence is written by
  an endpoint that constructs the record straight from the request body and validates only that
  numbers are finite (`src/Meridian.Ui.Shared/Endpoints/PromotionEndpoints.cs:150-162`);
  `SourceReference` is optional and never dereferenced. Combined with the unregistered
  walk-forward service (§2), the architecture *forces* the fabrication path: typing
  `{"outOfSampleSharpeRatio": 2.5}` is the only way a browser operator can clear the gate.
  *Improvement:* require `SourceReference` to resolve to a retained `WalkForwardReport` and
  recompute or hash-verify before persisting.
- **The paper→live checklist cross-check exists but exempts live.** Evidence-reference validation
  returns `[]` unless the target is Paper (`PromotionService.cs:985-993`); for live promotion, 13
  of 15 required items pass on any non-empty string after the colon
  (`PromotionService.cs:943-953`). The weaker gate is enforced against retained evidence; the one
  protecting real money is not.
- **Live risk rails measure a fictional book.** `PaperTradingPortfolio(100_000m)` is registered as
  the authoritative `IPortfolioState` and `IPositionTracker` *outside* the paper-gateway
  conditional (`src/Meridian/UiServer.cs:361-367`), so with live Alpaca routing enabled, position
  limits, gross exposure, notional caps, and drawdown are computed against a hardcoded $100k
  portfolio that starts empty and never learns pre-existing broker positions;
  `PositionReconciliationService` only reports drift, it does not correct state
  (`src/Meridian.Execution/Services/PositionReconciliationService.cs:73-169`).
  *Improvement:* in live mode, seed and continuously reconcile `IPortfolioState` from the broker
  and refuse to route when the sync is unavailable.
- **Desk safety commands are pane layouts.** WPF "Pause / Stop / Flatten / Cancel All" each open a
  pane and toast reassurance; none calls `IOrderManager.CancelAllAsync` or the breaker
  (`src/Meridian.Wpf/Services/TradingWorkspaceShellPresentationService.cs:164-167`), with Flatten
  toned Danger. The browser has the mirror-image gap: the true kill switch (durable breaker +
  sweep, landed server-side in W9-SAFETY-007) has no button — the generated route constant is
  called from nowhere in the dashboard
  (`src/Meridian.Ui/dashboard/src/lib/ui-api-routes.generated.ts:477`), while the wired
  cancel-all cancels the book *without* halting new routing. `W9-SAFETY-007` is honestly
  `in_progress`; this review confirms the dead-button sweep it calls for is still outstanding and
  cheap (a one-day audit).
- **Go-live gating reduces to config booleans plus two `File.Exists` checks.**
  `BrokerageOrderPlacementGate` trusts `appsettings` booleans and file presence — content,
  freshness, and linkage are never inspected
  (`src/Meridian.Execution.Sdk/BrokerageOrderPlacementGate.cs:83-117`).
- **The approval policy matrix is a settings screen, not an engine.** `requiredDistinctApprovals`
  and `requiresIndependentReviewer` are read by nothing; actual segregation-of-duties checks are
  hardcoded in two places (`src/Meridian.FinancialOperations/OperationsContinuity/OperationsContinuityWorkflow.cs:889,901`,
  `FileReconciliationBreakQueueRepository.Casework.cs:1113,1125`). An admin editing policy changes
  a display, not behavior — false assurance for an auditor.
- **"Immutable journal" is an application convention, not a database guarantee.**
  `journal_entries`/`journal_legs` have no immutability trigger and legs carry
  `on delete cascade` (`database/.../V_ledger_001__journal_entries.sql:31`); the team knows the
  pattern — it protects the tax-lot tables (`V_ledger_027__atomic_tax_lot_posting.sql:170-183`) —
  it just is not applied to the journal. No DB-level debits=credits constraint exists either
  (balance is enforced in `src/Meridian.FSharp.Ledger/JournalValidation.fs:25`). `W9-GOV-008`
  (hash-chained audit, route authorization, fail-closed tenancy) remains `planned`; until it
  lands, an auditor cannot rely on the store itself.
- **~360 of ~1,158 mapped routes carry no permission, role, or tenant check** — including
  destructive archive maintenance, storage cleanup, tier migration, and bulk export
  (`src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs:98,141`,
  `StorageEndpoints.cs:274,434`, `ExportEndpoints.cs`, `DataQualityEndpoints.cs`). The code
  comments claim "coverage tests inspect this metadata to prove every mapped route declares an
  explicit authorization requirement" (`src/Meridian.Ui.Shared/Endpoints/EndpointAuthorization.cs:11-13`);
  no such test exists — the only endpoint-enumerating test asserts name uniqueness
  (`tests/Meridian.Tests/Integration/EndpointTests/EndpointMetadataTests.cs:22-37`).
  *Improvement:* a global endpoint filter denying routes without authorization metadata plus the
  ~30-line coverage test the comment already promises.
- **Tenancy fails open on both read and write.** The write gate defaults to
  `Enforce: false` (`src/Meridian.Ui.Shared/Services/WorkstationTenantContext.cs:198-200`); the
  ownership guard allows on registry exception by design
  (`RegistryFundProfileTenantGuard.cs:63-71`); only 167 routes carry any tenant filter.

## 5. Durability and multi-operator reality

- **Operator work is lost on restart in the default posture — and silently absent in production.**
  The Operator Inbox's only implementation is a `ConcurrentDictionary`
  (`src/Meridian.Ui.Shared/Services/InMemoryOperatorInboxService.cs:9`), registered only when not
  production (`WorkstationServiceCollectionExtensions.cs:197-201`); the endpoint silently skips
  contribution when the service is null, so "no work" and "service not registered" look identical.
  Position/asset-event projections default to an in-memory dictionary
  (`WorkstationServiceCollectionExtensions.cs:232-248`). The OMS integration surface keeps
  messages, audit queue, and signing keys in process memory and seeds a hardcoded HMAC key,
  accepting unsigned requests (`src/Meridian.Ui.Services/Services/Integrations/OmsIntegrationApiHandler.cs:17-25,174`)
  — and its name lets it slip past the ADR-019 in-memory guard.
- **Concurrent edits are last-write-wins on ~96% of mutating endpoints.** Of ~484 mutating routes,
  only 21 sites use `ExpectedVersion` (all in reporting governance and statement casework); no
  `ETag`/`If-Match` handling exists anywhere in `Meridian.Ui.Shared`; the JSON file stores
  serialize with an in-process `SemaphoreSlim` only (`src/Meridian.Storage/Store/JsonFileSnapshotStore.cs:17`),
  so a second host process can lose writes entirely. Two operators editing the same saved view or
  schedule silently clobber each other.
- **The break queue is a single rewritten JSON file** — the sole `IReconciliationBreakQueueRepository`
  implementation, one process-wide semaphore, no Postgres variant
  (`FileReconciliationBreakQueueRepository.cs:17-53`) — a serialization bottleneck exactly where
  volume concentrates.
- **Rate limiting collapses under multi-user deployment.** The mutation limiter partitions by
  remote IP at 10/minute (`src/Meridian.Ui.Shared/Endpoints/UiEndpoints.cs:333`); behind a reverse
  proxy the whole organization shares one budget. The per-user branch is dead code because
  `LoginSessionMiddleware` never populates `HttpContext.User`.
- **Migrations run implicitly at startup, forward-only, with no operator control and no
  host↔schema compatibility gate** — nine schemas call `EnsureMigratedAsync` during composition;
  the installer promotes any payload over any version without comparison
  (`src/Meridian.Setup/InstallationTransaction.cs:142-181`), so a downgrade silently runs an older
  host against a newer schema, and the documented rollback requires the backup §1 shows the user
  cannot take.

## 6. Daily-driver UX quality

- **Navigable dead ends.** `/portfolio/family-office` is mounted with no props, so its data input
  defaults to null and it unconditionally renders "Family office data is not connected"
  (`src/Meridian.Ui/dashboard/src/app.tsx:763`, `family-office-screen.tsx:80,175`) — while
  remaining a promoted Portfolio nav item. Quant Lab's Formulas tab is a hardcoded empty state
  while a complete 319-line formula workbench component sits unmounted, and the command palette
  advertises it ("Author cell-based strategy formulas…",
  `command-palette.view-model.ts:254-258`). The guard designed for exactly this —
  `UNWIRED_WORKSTATION_ROUTES` — is declared and left empty
  (`src/Meridian.Ui/dashboard/src/lib/workspace.ts:141-149`): populating it is a one-line fix the
  code was already designed for.
- **Silent wrong answers on API failure.** Asset Detail swallows three of four sibling requests'
  failures into empty values (`asset-detail-screen.tsx:247-252`): when the corporate-actions
  service is down, the screen shows the security has *no corporate actions* — indistinguishable
  from clean. In a fund-accounting product this is the most dangerous failure class. 22 catch
  sites in `src/screens/` discard the error object; 14 of 21 error-handling screens offer no
  retry; the design system's own `AsyncRegion` primitive (skeletons, contained errors, retry,
  per-region boundary) is adopted by 1 of 68 screens, so a render error blanks the route and
  ejects the operator to the home screen.
- **Operators see status codes instead of reasons.** 395 endpoints return `{ error: "..." }`
  while the client's normalizer reads only `detail`/`message`/`title`
  (`src/Meridian.Ui/dashboard/src/lib/api-errors.ts:55`), so a failed import renders "Request
  failed (400)" instead of "Symbol is required". A one-line client fallback fixes the majority of
  cases immediately.
- **Mostly polling, minimal push.** Three SSE endpoints exist; reconciliation breaks, approvals,
  ledger postings, and inbox items all poll. The general `/api/events/stream` re-serializes the
  entire status payload every 2 seconds regardless of change
  (`src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:225-258`). The well-built
  `StreamBroadcaster` fan-out already exists to do this correctly.
- **Sprawl and weight.** 81 routes, 53 nav destinations, four levels to content, 13 legacy
  redirect tombstones; `settings-screen.tsx` is 7,397 lines in one file and the Accounting route
  costs ~782 KB of JS before render (`wwwroot/workstation/` chunk sizes); no `manualChunks`
  strategy (`vite.config.ts:173-176`).
- **Deliverable fidelity.** XLSX statements export text cells, not numbers, for every statement
  except partners' capital (`src/Meridian.Documents/FinancialReportDocumentRenderer.cs:295`) — a
  controller cannot sum a column without retyping, which defeats an XLSX deliverable. Evidence
  Vault has no document extraction (no OCR/PDF text layer anywhere; `ExtractedFields` is
  caller-supplied, `src/Meridian.Ui.Shared/Evidence/EvidenceWorkflowDtos.cs:565`), so the
  six-state extraction lifecycle is ceremony around hand-keyed data.
- **Statement intake robustness.** All five connectors decode as hard-coded UTF-8 — including OFX,
  whose discarded SGML header is where the charset is declared
  (`src/Meridian.FinancialOperations/Reconciliation/Connectors/Ofx/OfxDocumentParser.cs:265-269`)
  — so Windows-1252 exports silently mojibake into reconciliation matching. Dedupe is file-level
  only (no `FITID`/transaction-level idempotency), so overlapping or amended statements
  double-import (`src/Meridian.Domain/Reconciliation/BrokerStatementModels.cs:39-51`); drift
  detection never activates for built-in profiles
  (`StatementMappingProfileCatalog.cs:69-115`).

## 7. Verification theater: the assurance layer overstates itself

The 14,000-test headline materially overstates delivered assurance:

- **`ci.yml` runs no tests on pull requests** — its dotnet, browser, and docs jobs all carry
  `if: github.event_name != 'pull_request'` (`.github/workflows/ci.yml:31,190,249`); only
  secret-scan runs. PR signal comes from the duplicate `meridian-ci.yml`, which doubles compute on
  main and splits the source of truth. The roll-up `quality-gate` has never been activated as a
  required check (`docs/engineering/production-certification-evidence-chain.md:131-145`).
- **The real-database lane runs weekly and certifies ~5.5% of the estate.** Docker-gated
  PostgreSQL suites are disabled in both CI workflows (`MERIDIAN_DISABLE_DOCKER_TESTS: "true"`),
  integration-category tests are filtered out of `scripts/ci.sh:155`, and the weekly Production
  Certification run certifies 788 tests — after a July history of 541 failed / 218 passed and
  earlier runs whose schema evidence was header-only.
- **1,728 tests — the entire WPF lane plus installer and supervisor — never run on the main
  gate**; `Meridian.Wpf.Tests` compiles as an empty stub off-Windows
  (`build/scripts/ci/run-dotnet-ci-tests.py:25-33`), the Windows workflow is path-filtered, and
  `Meridian.Setup.Tests` runs in no lane at all — the comment admits it was added to the
  exemption list after failing the coverage gate. The code that can destroy a customer's install
  (`InstallationTransaction.Promote/Recover`) is covered by tests that never execute.
- **Quarantine instead of repair.** Six accounting-readiness tests were `[Fact(Skip)]`-ed against
  the same commit that shipped "complete, critical" `W9-ASSET-010`
  (`tests/Meridian.Tests/Ui/AccountingSystemIntegrationServiceTests.cs:770,1075,1474,1647,5461`);
  one documents that a readiness control count silently regressed from 23 to 2 because a helper
  became a no-op stub.
- **The repo violates its own test-quality rules at scale**: 117 tautological assertions, 59 bare
  catches in test bodies, 24 base-`Exception` assertions — all patterns `ai-known-errors.md`
  marks "fixed" — plus tests asserting that scripts and READMEs contain literal strings.
- **The dashboard suite cannot see the defect class users actually hit.** No e2e; nothing mounts
  every route; nothing asserts buttons have handlers or API exports have consumers — which is
  precisely why §2's findings shipped repeatedly. Two cheap structural tests (mount-every-route
  smoke, orphan-export gate) would have caught most of them.

## Program-level concerns

- **Effort allocation is inverted.** In the last 300 commits, `docs/` files changed 1,001 times vs
  635 for `src/` — documentation churn exceeds product churn in a program whose registry says no
  P0 is closed. The registry meta-layer (registries, renderers, validators, hash checks) is
  institutionally impressive but is absorbing the marginal hour that the first mile needs.
- **Two co-equal UI lanes is a cost the program is not paying evenly.** 15 of 29 browser screens
  are "Partial" on WPF and 2 are hard gaps (`docs/development/wpf-web-ui-alignment-plan.md`); a
  desktop-only operator cannot approve, close, reopen, or accept evidence. Until the parity matrix
  is honest in-product (screens labeled browser-first), the second lane multiplies every §6
  finding.
- **Status documents disagree with each other.** The P0 tracker says 21 rows and "nine open W9
  rows planned" while the registry shows 6 `ready_for_acceptance`/3 other; the evidence-chain doc
  says 18 P0 rows; the README's capability table reads "Complete baseline" nine times to a
  stakeholder who never reaches the disclaimer. Pick one generator and let it own every number.

## Prioritized improvement list (by end-user value uplift)

1. **Ship the first mile.** Close PRD-013/014/016 (publish → sign → install evidence, required-check
   activation), add `--create-user`/`--hash-password`, fix or archive `deploy/`, one-line
   devcontainer durability, supervisor `backup`/`restore`. Until this lands, real user count is
   structurally zero and every other improvement is invisible.
2. **Do the dead-control sweep now (safety first).** Wire or visibly demote WPF
   Pause/Stop/Flatten/CancelAll; give the browser a breaker button next to cancel-all; then the
   fat-finger/collar rules. Finish `W9-SAFETY-007`'s exit criterion: *no dead safety buttons*.
3. **Close the promotion-evidence loophole.** Register the walk-forward service in the workstation
   host, require `SourceReference` to resolve to a retained report, extend the checklist
   cross-check to live targets, derive gate booleans from artifacts. This is the difference
   between governance and theater.
4. **Wire the casework verbs and bulk actions** (assign/comment/root-cause/sign-off/reopen + bulk
   with the existing dry-run) — one case drawer lights up nine endpoints and converts the
   product's wedge persona from viewer to operator.
5. **Make live risk real.** Seed `IPortfolioState` from broker positions in live mode; refuse live
   routing without the sync.
6. **Finish truth discipline end-to-end**: provenance from the actual tape (streaming + historical
   provider set, not persistence bindings); `MarketEvent` provenance field and removal of the
   `"IB"`/`"ALPACA"` defaults; retry on the demo-mode probe with an `unknown` state; delete the
   WPF fake version-check/activity feed; `501` from Lean endpoints; delete the 1970 dashboards.
7. **Activate the fund-economics and multi-currency kernels** on the automated-journal path
   (`FundEconomicsJournalFactory` → intake runner; FX revaluation → period close). The math is
   done and tested; the wiring is the value.
8. **Durability + concurrency floor**: durable operator inbox and projections (or fail loudly),
   `ETag`/`If-Match` over the JSON snapshot stores, Postgres break queue, per-user rate limiting,
   DB-level journal immutability trigger + balance constraint.
9. **Authorization coverage**: global deny-without-metadata filter, the promised coverage test,
   fail-closed tenancy defaults. (First half of `W9-GOV-008`, separable and cheap.)
10. **Make CI verify what users run**: Postgres suites per-PR, a Windows job in the gate (WPF +
    Setup tests), delete the duplicate workflow, a skip-delta gate, mount-every-route and
    orphan-export structural tests, and the client error-envelope one-liner while in there.

Quick wins worth doing this week regardless of sequencing: populate `UNWIRED_WORKSTATION_ROUTES`
(one line), the `api-errors.ts` fallback (one line), devcontainer `MERIDIAN_DATABASE_URL` (one
line), wire or disable the Strategy Designer buttons, delete the seven 1970 dashboards, delete the
StockSharp sample-config block.

## What is genuinely strong (do not regress it)

- **ADR-019 composition policy** — three-way prohibited-implementation matching, final-graph
  re-validation at host start, and forced `Simulated` provenance when money-path stores are
  in-memory (`src/Meridian.Application/Composition/ProductionServiceRegistrationPolicy.cs`). Rare
  rigor; §3's findings are coverage gaps in it, not design flaws.
- **The backtester's bias-disclosure report** (`src/Meridian.Backtesting/Engine/BacktestEngine.cs:606-710`)
  attaches a severity-ordered honesty report to every result and defaults conservative. Most
  backtesters flatter; this one argues against itself.
- **The reconciliation matching engine** (`src/Meridian.FinancialOperations/.../ReconciliationMatchingEngine.cs`)
  — versioned tolerance profiles, weighted scoring, N-to-1 splits, evidence-carrying decisions —
  institutional-grade work; it needs the live path, not a rewrite.
- **OMS pre-trade enforcement** — `CompositeRiskValidator`'s fail-closed breaker latch and
  governed-escalation outcomes are real enforcement (`src/Meridian.Risk/CompositeRiskValidator.cs:32-140`).
- **Durability primitives** — `AtomicFileWriter` with directory fsync, checksummed WAL replay,
  and the installer/supervisor's receipt-gated, hash-verified, reversible promotion
  (`src/Meridian.Setup/InstallationTransaction.cs:110-194`).
- **Auth primitives and the evidence vault's fail-closed tenancy shims**; the enforced
  bundle-freshness gate on the committed workstation build; zero TODO markers and a real
  design-system contract test in the dashboard.

## Relationship to existing planning

This review **corroborates** the W9/W10 ordering (truth → demo → realism → fills → reporting →
economics → safety → governance → ingestion; then activation-heavy depth work) and **adds** the
following that no register currently carries: the acceptance-vs-wired drift on `ready_for_acceptance`
rows; the walk-forward evidence loophole and live-exempt checklist cross-check; the hardcoded $100k
live risk baseline; tick-source mislabeling at the collector layer; statement-connector encoding
and transaction-level dedupe gaps; the empty `UNWIRED_WORKSTATION_ROUTES` guard; the client error-
envelope mismatch; last-write-wins concurrency; the missing authorization-coverage test the code
claims to have; the credential-vault key permissions on POSIX; the broken `deploy/` manifests; the
absent backup path; and the CI structure findings (`ci.yml` PR skip, unrun installer tests,
quarantined-on-"done" rows). Candidates for new registry rows rather than silent fixes: the
promotion-evidence loophole, live portfolio-state seeding, endpoint authorization coverage, and
statement transaction-level idempotency.
