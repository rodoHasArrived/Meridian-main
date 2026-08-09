# Adversarial Program Review — Meridian (2026-07-26)

**Status:** archived point-in-time assessment; independent review input, not a governance or roadmap-status document
**Owner:** review author (independent adversarial pass)
**Reviewed:** 2026-07-26
**Scope:** whole-program follow-up review of Meridian's high-level functionality, focused on end-user value
**Method:** eight parallel adversarial audits over the wired code paths at commit `6dcd7b0bc` —
(1) product claims vs registry evidence, (2) market data and providers, (3) strategy/backtest/
execution/risk, (4) fund operations and accounting, (5) browser workstation, (6) WPF workstation,
(7) platform/security/durability, (8) onboarding and end-to-end journey. Every material claim is
anchored to `file:line` at this checkout. This review deliberately re-tests the
[2026-07-21 adversarial review](adversarial-program-review-2026-07-21.md) before adding new
findings, so remediated items are credited rather than re-litigated.

## Headline

The 2026-07-21 review's theme was **"built but not wired."** Five days later, several of its top
wires are genuinely connected (sided reconciliation matcher, Alpaca fill stream, client-grade
report renderers, partners-capital statement). The sharpest current finding is different:

> **Meridian is now a strong operator workbench wrapped in a broken first mile and an unsupported
> last mile.** The browser workstation is real — no mock screens, fail-closed endpoints, working
> mutations — but a new user cannot cleanly start the product (the advertised demo path fails on a
> fresh clone), a non-developer cannot install it at all, and a team cannot legitimately deploy it
> (the only supported production posture is a single-operator Windows desktop, monitoring is
> decorative, tenancy reads fail open). In between sit persistent "truth by default" gaps — silent
> in-memory books, unlabeled synthetic prices, string-match approval evidence, fabricated
> risk-guardrail text — that contradict the "prove the number" brand, plus three loops that start
> but never close (designer strategies never trade, workbook imports never commit, reconciliation
> never sees the ledger side).

## Scorecard: what moved since 2026-07-21

| 07-21 priority | Status today | Evidence |
| --- | --- | --- |
| 1. Wire sided reconciliation matcher into the live path | **Largely done.** `StatementMatchingEngine` now runs in the live workflow with variance-computed `ToleranceBreached` | `src/Meridian.FinancialOperations/Reconciliation/StatementRunMatchingService.cs:79`, `StatementReconciliationService.cs:910` |
| — remaining | Ledger-transaction population is deliberately empty (cash+positions only); no FX provider; casework persists as JSON files | `src/Meridian.Application/Reconciliation/RetainedInternalReconciliationPopulationProvider.cs:36-44`; `ReconciliationServiceRegistration.cs:27-29` |
| 2. Seed durable sample data + empty states | **Partial.** Real, CI-smoke-tested `--seed-demo` with provenance-labeled data — but it seeds only 2 of 7 nav areas, and the quickstart path breaks on a fresh clone (below) | `src/Meridian/DemoWorkspaceCli.cs`; `DemoWorkspaceSeeder.cs:63-90`; `.github/workflows/demo-smoke.yml` |
| 3. Truthful default run | **Mostly open.** Degraded-mode banner and seeded-provenance badges exist; but money-path stores still fall back to silent in-memory, and per-datum provenance is still absent | `src/Meridian/UiServer.cs:128-137`; `src/Meridian.Ui.Shared/Endpoints/LiveDataEndpoints.cs` (no provenance field on quote DTOs) |
| 4. Client-grade renderer + fund math wiring | **Largely done.** QuestPDF/ClosedXML renderers referenced by production reporting; ledger-derived statements; partners-capital statement landed (PR #2522). Fee-proration fix not re-verified this pass | `src/Meridian.Documents` refs in reporting; `src/Meridian.Ledger/LedgerFinancialStatementBuilder.cs:22-161` |
| 5. Fail-closed tenancy, hash-chained ledger, SoD on money paths, blanket RBAC | **Mostly open.** Reads fail open by design; write gate is opt-in (`MERIDIAN_FUND_SCOPED_WRITE_TENANT_REQUIRED`); journal rows have no hash chain and no DB-level immutability; RBAC still non-uniform (41 of 133 endpoint files reference permission checks). SoD is genuinely server-enforced on posting/report/continuity paths | `src/Meridian.Contracts/Tenancy/TenantReadPredicate.cs:15`; `src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.cs:19-35`; `AccountingPostingCandidatePostService.cs:61-63` |
| 6. Alpaca fill loop, paper realism, safety buttons | **Partial.** `AlpacaTradeUpdatesClient` (durable dedup) now exists and is DI-wired; paper fills remain instant, complete, and cost-free. WPF shell safety buttons not re-verified | `src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaTradeUpdatesClient.cs:11`, `AlpacaProviderModule.cs:138-139`; `src/Meridian.Execution/Adapters/PaperTradingGateway.cs:244-335` |
| 7. Focus: one end-to-end slice, defer breadth (incl. WPF parity) | **Not adopted.** WPF parity lane continues; web outpaces WPF ~2:1 (183 vs 89 commits since June); none of parity waves P1–P5 have landed 20 days after the plan's assessment | `docs/development/wpf-web-ui-alignment-plan.md:32-61`; `git log` counts |

The remediation stream is real — roughly 50 `codex/fix-*` PRs merged in five days. But it is
micro-fix-shaped; none of the structural items (default-run truth, deployment envelope, tenancy
posture, strategy-execution loop) moved.

## Findings by end-user value area

### A. First mile: the advertised evaluation path fails (new, high)

- The quickstart promises "the first screen you see is a populated one" via `--seed-demo`, but
  `src/Meridian.Ui/wwwroot/` is not checked in and the docs never mention the required
  `npm --prefix src/Meridian.Ui/dashboard run build`; the host's required readiness check then
  fails with "Workstation bundle is unavailable" (`src/Meridian/UiServer.cs:887-904` vs
  `docs/start/README.md:57-98`). Every fresh-clone evaluation breaks at step one.
- The demo seeds reconciliation + one strategy run only (`DemoWorkspaceSeeder.cs:63-90`);
  Portfolio, Accounting, Reporting, and Data first impressions are empty states. An accountant
  evaluating report packs sees nothing. 58 dashboard files reference dev fixtures that are
  compiled out of production builds.
- The first-run wizard's "Explore with sample data" option still writes `sample-pack.json` that no
  code reads back (`src/Meridian.Ui.Shared/Services/FirstRunExperienceService.cs:180` is the only
  reference).
- The Excel onboarding workbook — the stated onboarding wedge — implements template download and
  per-sheet preview/review but has **no commit endpoint anywhere**
  (`WorkstationEndpoints.DataUploadWorkbook.cs`); holdings/entity/asset uploads stop at preview.
  The only committing upload is statement import (`WorkstationEndpoints.DataUploads.cs:138`).
  An operator can validate their book and then cannot load it.
- There is no adoptable path for the target buyer: the supported deployment is a Windows installer
  whose binaries exist only in CI (`src/Meridian.Setup/`), and `deploy/docker|k8s|systemd` are
  explicitly experimental.

### B. Truth by default (brand-critical; carried forward, still open)

- **Two ledgers, one brand.** Without per-domain connection strings, ledger/accounting stores
  never register and books run in-memory for the process lifetime; UI endpoints degrade gracefully
  rather than refuse (`StorageFeatureRegistration.cs:218`, `LedgerEndpoints.cs:1804-1805`,
  `UiServer.cs:128-137`). Disclosure is a banner; the workflow itself still completes — an
  operator can post journals for weeks and lose everything on restart. Capability absence is
  logged at Debug level.
- **Prices carry no per-datum provenance.** `AppConfig.DataSource` defaults to synthetic
  (`src/Meridian.Core/Config/AppConfig.cs:48`); the configuration wizard defaults to `ib`, which in
  standard builds (no vendor SDK) silently wraps a random-walk simulator still described as
  "Interactive Brokers TWS/Gateway for real-time market data"
  (`Adapters/InteractiveBrokers/IBMarketDataClient.cs:20-21,61-68`). Quote/trade DTOs have no
  provenance field, so simulated prices render indistinguishably from real ones; synthetic bars
  persist through the same sinks and read back as genuine history.
- **Data-quality verdicts gate nothing.** Freshness/gap/spike validators emit monitoring events,
  but no reconciliation, NAV, or report path consults them — a flagged-bad bar flows downstream
  unmarked (no references to the `DataQuality` namespace from `Meridian.FinancialOperations` or
  `Meridian.Reporting`).
- **Fabricated risk-guardrail text.** With no risk engine registered, the Trading risk panel
  serves hardcoded `riskState "Healthy"` plus invented control claims ("Single-name concentration
  cap set at 30% notional", "Auto-throttle above 70% buying power")
  (`WorkstationEndpoints.Trading.cs:113-120`). An operator can read compliance controls that do
  not exist.

### C. Deployment and operability: the unsupported last mile (new, high)

- **Product/envelope mismatch.** Positioning says self-hosted platform for RIA/fund-ops teams; the
  supported production envelope is one operator on one Windows desktop (ADR-019), with
  DPAPI-protected secrets (`LifecycleSupervisorRuntime.cs:1164,1206`). The container path is dead
  on arrival by design: non-loopback HTTP + Production env throws at startup
  (`UiServer.cs:775-783`), compose bundles no Postgres so accounting never registers, and the
  healthcheck curls `/health`, which is neither mapped nor auth-exempt.
- **Decorative monitoring.** `deploy/monitoring/` ships Prometheus config, two Grafana dashboards,
  and alert rules targeting `meridian:8080/metrics` — no host serves `/metrics`;
  `StatusHttpServer` has zero instantiations. Operators believe they have alerting; they have none.
- **Backup is strong but manual**, PowerShell-only, with no scheduler and no UI surface
  (`build/scripts/recovery/invoke-production-recovery.ps1`).
- Identity, roles, scoped access, and sessions live in JSON files with plain (atomic, un-chained)
  JSONL audit (`src/Meridian.Identity/Infrastructure/UserAccountStore.cs:66-67,447-453`); the real
  `AuditChainService` hash chain is wired only to storage checksums.

### D. Loops that start but never close (new, high)

- **Designer strategies never trade.** Promotion of a Strategy Designer run succeeds, records
  governance, then defers forever: launch resolves via `LiveStrategyCatalog`, which knows only
  `buy-and-hold`, `ma-crossover`, and `pluginAssembly` (`LiveStrategyCatalog.cs:100-107`); designer
  runs carry only `designerDocumentId` (`WorkstationEndpoints.Strategy.cs:240-246`), so
  `TryCreate` fails and the paper run is silently deferred with a log warning
  (`PromotionService.cs:498-524`). The advertised prove→paper→live loop is real only for two
  reference strategies and hand-compiled DLLs — and strategy authoring itself is off by default
  (`QuantLab:Enabled`, justified by in-process arbitrary C#).
- **LEAN endpoints fabricate a lifecycle.** Start stores `"queued"` in a static dictionary,
  nothing ever runs, and results would return hardcoded zeros
  (`Endpoints/LeanEndpoints.cs:250-320`). Most misleading single surface found this pass.
- **Reconciliation's third side is missing.** Transaction-level matching is deliberately
  unsourced, so "reconciled" means cash+positions only; owner-scoped position snapshots are
  invisible to the internal-book provider; non-GUID fund-account labels resolve an empty book and
  produce 100% breaks (`RetainedInternalReconciliationPopulationProvider.cs:27-44`).
- **Statement "connectors" mostly don't connect.** CSV/OFX/CAMT.053/BAI2/IB-Flex parsers are real
  but file-import only (`SupportsRemoteFetch:false`); only Alpaca fetches remotely. No IB Flex Web
  Service pull despite the fund-ops positioning; MT940/SWIFT absent.

### E. Trust-chain enforcement gaps (new depth on carried-forward theme)

The durable Postgres chain is better than the 07-21 review could see: balanced-entry validation
(`JournalValidation.fs:22-39`), append-only journal store with period-status guards and
residual-balance-checked hard close (`PostgresLedgerJournalStore.cs:1612`), idempotent posting,
reversal-only corrections, SHA-256-signed report packs with Draft→Approved→Published lifecycle.
The gaps are now precise:

- **Evidence gates are string matches.** `LedgerPeriodPostingGuard` "verifies" Security-Master
  approval via substring checks like `approved:true` on caller-supplied tags
  (`LedgerPeriodPostingGuard.cs:200-235,346-359`); `AdjustmentApproval` is caller-supplied strings
  with no foreign key to any workflow record. Soft-close adjustments are self-attestable.
- **Journal rows are mutable at the DB layer.** No UPDATE/DELETE denial trigger, `ON DELETE
  CASCADE` on legs, no per-entry debit=credit DB constraint, no hash chain over rows — file-level
  tamper-evidence does not cover the ledger of record.
- **Actor identity is spoofable in `optional` auth mode** (the dev default):
  `ResolveMutationActor` falls back to the request body's `Actor`
  (`AccountingSystemEndpoints.cs:699-704`), so SoD compares attacker-chosen strings.
- **Tenancy filters, it does not enforce** (see scorecard row 5); no Postgres RLS; fund-account
  sub-tables and fund-structure not tenant-partitioned.

### F. Live-ops responsiveness (new, medium)

SSE exists only for quotes (good implementation: heartbeat, coalescing) and report-run progress.
Orders/fills, reconciliation break queues, and approval inboxes ride 30-second polling or manual
refresh (`use-workstation-data.ts:1031-1051`) — noticeable in any multi-operator approval
workflow. The broadcaster abstraction (`IQuoteStreamBroadcaster`, `StreamTopic`) already exists;
this is extension, not invention. Restart also loses the "live" read models (recent-trade rings,
session stats) even though the underlying ticks are persisted — nothing rehydrates them.

### G. The two-UI tax, measured (carried forward, sharpened)

- WPF is not a Potemkin app (~95 real pages, 211 test files) — but the plan's own matrix shows
  ~10 Full / ~12 Partial / 6 Gap, all six gap screens have zero WPF code, and one shipped
  navigation target dead-ends today: `FundLedgerViewModel` emits `EvidenceWorkbench:{subject}`
  deep links with no `EvidenceWorkbenchPage` behind them — users land on "Workflow unavailable"
  (`ShellRouteRegistry.cs:109`, `WorkspaceShellFallbackContentFactory.cs:21`).
- **The authoritative merge gate never compiles WPF.** `Meridian CI / quality-gate` runs
  `ubuntu-latest` only; on non-Windows the csproj compiles an empty stub and the 211-file test
  project compiles zero tests (`meridian-ci.yml:31`). Windows workflows exist but are not the
  named merge gate — WPF breakage can merge cleanly.
- **Forked composition risks number drift**: `src/Meridian.Wpf/Services/FundLedgerReadService.cs`
  aggregates fund-ledger views desktop-side while the web consumes server-computed
  `/api/workstation/*` read models; only ~6 WPF files touch the workstation API, and the csproj
  directly references 12 domain assemblies. Two data paths can disagree on the same fund's
  numbers — in an accounting product.
- Divergence runs both directions: ~25+ desktop-only legacy pages have no web counterpart and no
  parity story; the parity tracker is itself missing web's `report-run-governance-screen`.

### H. Registry and catalog truthfulness (new, medium)

- **Circular acceptance evidence**: W2-PROMO-001 and W3-CONT-001 are "done, evidence complete"
  with the generated roadmap summary — rendered from the same registry — as their only evidence
  (`roadmap-items.yml:101-103,131-133`).
- **All-green facade**: `ROADMAP_SUMMARY.md` shows every row green while the program is `blocked`
  with 0 of 18 P0s production-certified; the risk register lists 3 risks, none of them the
  fail-open tenancy, plaintext credentials, or silent in-memory books the repo itself documents.
- **Unattainable provider surfaces**: a complete NYSE adapter (OAuth+WS) targets
  `api.nyse.com/v1` / `wss://stream.nyse.com/v1` — endpoints with no public offering and no
  onboarding doc — yet is selectable (`Adapters/NYSE/NYSEOptions.cs:29-35`). Finnhub backfill uses
  the paid-only `/stock/candle` (free keys 403) but appears as a valid candidate. Robinhood's
  unofficial mobile API (manual bearer token, ToS risk) registers as a first-class brokerage
  gateway. TradeStation/Tradier are payload mappers with no transport.
- Provider credentials read plaintext config directly although the platform vault exists
  (`Meridian.Platform/Secrets`).

### I. Smaller items worth fixing

- `EventPipeline` default backpressure mode is `DropOldest` — silent tick loss under load with
  optional audit (`EventPipeline.cs:131`).
- Backfill request queue is not durable across restart; running jobs force-pause at shutdown
  (`BackfillWorkerService.cs:237-247`).
- `/api/workstation/reporting` returns the full accounting payload
  (`WorkstationEndpoints.cs:494-500`) — over-fetch and domain coupling.
- Orphaned duplicate surface: `components/accounting/JournalEntryForm.tsx` is referenced only by
  its test; the real workbench is a separate implementation.
- DB-backed test suites conditionally skip without PostgreSQL and `MERIDIAN_DISABLE_DOCKER_TESTS`
  defaults true in CI — persistence paths are under-exercised by the default gate.
- Docs remain maintainer/AI-facing at roughly 4:1 over operator-facing; there is no single
  operator "zero-to-trusted" page (Postgres setup, user-hash generation, provider credentials).

## What is genuinely strong (so fixes do not regress it)

The browser workstation is a real workbench: 33 routes, no mock or shell screens, fail-closed
503s instead of sample payloads, ~468 mutation endpoints actually wired (journals, breaks,
approvals, report governance, order tickets), SSE quote streaming, provenance badging for seeded
data, 263 frontend test files including six a11y suites. The backtest engine is
institutional-grade (bar-midpoint/order-book/market-impact fill models, corporate actions,
delisting policy, walk-forward, bias disclosure). The ingestion pipeline is durable
(WAL-before-sink, dedup ledger, dead-letter, resumable disk-persisted backfill with failover
chains and gap remediation). The provider fleet is mostly real (Alpaca/Polygon/Finnhub/Coinbase
streaming; nine historical providers; CSV/OFX/CAMT.053/BAI2/IB-Flex parsers with format-drift
detection). The durable ledger path is genuinely sound when configured, and report packs carry
SHA-256 signatures with server-enforced approver≠preparer. Authentication quality is high
(PBKDF2 210k, hashed atomic sessions, CSRF, rate limiter, HTTPS transport guard, fail-closed
outside dev). Test scale is real (~12,500 facts/theories, CI demo smoke asserting populated
screens), and the internal P0 tracker candidly names most of these gaps.

## Prioritized improvement list (by end-user value)

| # | Improvement | Why it is high-value to the end user | Effort |
| --- | --- | --- | --- |
| 1 | **Fix the first mile**: make `--seed-demo` self-sufficient (build or bundle workstation assets, or fail with the exact command); extend the seed to all seven nav areas; ship the workbook preview→commit endpoint so an operator can load their actual book | Every evaluation currently breaks at step one, and the onboarding wedge dead-ends at preview | M |
| 2 | **Truth by default — persistence**: in non-dev postures, block or watermark money-path mutations when the domain store is in-memory; make Postgres the required accounting default; fail loudly (readiness, not Debug logs) on missing connection strings | "Prove the number" cannot coexist with books that silently evaporate on restart | M |
| 3 | **Truth by default — provenance**: stamp per-datum provenance (live/delayed/synthetic/seeded) from collectors through storage into DTOs and reports; make certification refuse or watermark flagged/synthetic data; replace the fabricated risk-guardrail fallback text with "not configured" | Removes every path by which fabricated numbers render as real — the brand's one non-negotiable | M |
| 4 | **Ship a supported team deployment**: Linux/container posture with bundled Postgres, reverse-proxy TLS guidance, working healthcheck; serve a real `/metrics` endpoint and validate the shipped alert rules; scheduled, operator-visible backups | The actual buyer (an ops team) cannot legitimately deploy the product today; monitoring is decorative | L |
| 5 | **Close the authored-strategy loop**: a QuantScript/designer live-strategy source so promoted runs actually paper-trade; delete or truly implement the LEAN endpoints; sandbox QuantScript so authoring can default on; reuse backtest fill models in the paper gateway | The flagship prove→paper→live loop currently works only for two demo strategies; LEAN fabricates results | M–L |
| 6 | **Finish reconciliation's third side**: source the ledger-transaction population; add an FX provider; add IB Flex Web Service remote fetch with scheduled pulls; move casework from JSON files to the Postgres lane | Turns "reconciled" from cash+positions into the full control-tower promise the product sells | M–L |
| 7 | **Make evidence referential, not lexical**: approval ids must resolve against workflow/audit stores; server-stamped actors only (no body fallback); retire `approved:true` string-match gates | Self-attestable evidence undermines the entire governance story under audit | M |
| 8 | **Harden the ledger of record**: DB triggers denying UPDATE/DELETE, deferred per-entry balance constraint, hash-chain journal rows and the identity/governance audit trails (the chain service already exists) | Tamper-evidence currently stops at the exact layer an auditor would test | M |
| 9 | **Default-on tenancy**: fail-closed reads, mandatory write gate, Postgres RLS, partition the remaining fund-account/fund-structure tables | Gates every multi-client RIA deployment; today it is filtering, not enforcement | L |
| 10 | **Push operational topics over SSE** (orders/fills, break queue, approval inbox) and rehydrate live read models from persisted ticks on restart | Multi-operator workflows on 30-second polls feel broken; the broadcaster seam already exists | M |
| 11 | **Decide the WPF question honestly**: fix or reroute the Evidence Workbench dead-end now; add a Windows compile+test job to the authoritative merge gate; migrate `FundLedgerReadService`-style local composition onto shared read models; then either fund parity to done or narrow WPF to its desk niche (docking, hotkeys, ticker/quote float) and say so | A second UI that can dead-end, drift on numbers, and break invisibly in CI is a tax users pay in trust | M, then strategic |
| 12 | **Registry and catalog truthfulness**: replace circular evidence rows with real anchors; sync the risk register with the P0 tracker; drop or hard-gate unattainable providers (NYSE), mark paid-only capabilities (Finnhub), demote Robinhood; route provider credentials through the vault | Status surfaces that read opposite to ground truth erode exactly the trust the product monetizes | S–M |

Items 1–3 are the same "connect-and-truth" ROI the 07-21 review identified — still the cheapest
value on the table. Item 4 is the strategic unlock: without a deployable team posture, every other
improvement serves an audience of one operator per Windows machine.

## Relationship to existing planning

Nearly every finding maps to an existing W9 slate row (`W9-TRUTH-001`, `W9-DEMO-002`,
`W9-PAPER-003`, `W9-INGEST-009`) or P0 tracker item (PRD-001/002/003/004/005/007/009), and this
review confirms the registry's own rank-1/rank-2 choices (truthful default run; non-empty first
hour) with fresh evidence. Three things this review adds beyond the existing slate: the
fresh-clone demo breakage (A), the decorative monitoring stack (C), and the designer-strategy
promotion dead-end (D) do not currently appear as roadmap rows or P0s anywhere — they should.
