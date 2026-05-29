# Meridian — Feature Inventory

**Version:** 1.7.21
**Date:** 2026-05-29
**Purpose:** Comprehensive inventory of every functional area, its current implementation status, and the remaining work required to reach full implementation.

Use this document alongside [`ROADMAP.md`](ROADMAP.md) (delivery waves and sequencing), [`../plans/current-direction-and-status.md`](../plans/current-direction-and-status.md) (consolidated current direction, status, and plan-file roles), [`../plans/evidence-backed-investment-operations-plan.md`](../plans/evidence-backed-investment-operations-plan.md) (2026-04-29 differentiation filter and archive rule), [`IMPROVEMENTS.md`](IMPROVEMENTS.md) (normalized improvement/backlog tracking), and [`FULL_IMPLEMENTATION_TODO.md`](FULL_IMPLEMENTATION_TODO.md) (consolidated non-assembly execution backlog).

---

## Legend

| Symbol | Meaning |
| -------- | --------- |
| ✅ | Fully implemented and tested |
| ⚠️ | Partially implemented — functional with caveats |
| 🔑 | Requires external credentials / build flag |
| 🔄 | Framework in place, one or more sub-features pending |
| 📝 | Planned, not yet started |

---

## Commercial Product Modules

These modules describe Meridian's sellable product direction. They are conservative status markers, not new completion claims.

| Module | Status | Current evidence and remaining work |
| --- | --- | --- |
| Meridian Assurance Loop | 🔄 | Product framing for trusted data -> research -> paper trading -> ledger/reconciliation -> governed report pack. `PilotAcceptanceHarnessTests` now provides executable support evidence for the eight-stage golden path and writes `artifacts/pilot-acceptance/latest/pilot-readiness.json`; operator/browser acceptance remains open. |
| Meridian Evidence OS | 🔄 | Umbrella product positioning for the evidence graph, vault, completeness scoring, decision lineage, approval packets, replay verification, report-line provenance, and governed report packs. Shared evidence APIs and a browser Evidence Workbench are now support evidence, but this is not a delivered platform layer. |
| Run Evidence Graph | 🔄 | Shared evidence services now expose subject, packet, graph, validation, template, and manifest-export seams for strategy-run, paper-readiness, reconciliation-review, report-pack, provider-trust, analysis-export, Security Master conflict, and operations approval subjects. The pilot acceptance artifact now records cross-stage evidence graph edges and source evidence IDs; broader report-line drilldown remains open. |
| Governed Report Pack Studio | 🔄 | Support exists through report-pack schema/version checks, export/reporting seams, governed output direction, a browser Reporting task panel, shared workflow DTOs, and shared API/service state for draft, validate, submit, approve, publish, restate, archive, and history. Publication requires sign-off, evidence hash, retained manifest metadata, retained evidence links, and no-orphan report-line evidence. Full browser studio mutation UX and persisted workflow recovery remain open. |
| Reconciliation Casework Module | 🔄 | In progress through file-backed break queues, seeded owner/tolerance/sign-off metadata, audit history, calibration-summary rollups, operations-continuity break resolution, durable case fields for owner/SLA/aging/comments/disposition/attachments/break explanations, provider-ledger unresolved Security Master identity and stale resolved mapping case routing to Security Master steward ownership, and provider-ledger corporate-action/factor candidate case seeding for steward review. Full close workflow acceptance and broader operator casework surfaces remain open. |
| Accounting-Grade Paper Trading | 🔄 | Support evidence exists through paper sessions, replay/audit metadata, order/fill/ledger count checks, readiness gates, sanitized execution metadata, and fail-closed brokerage order-placement gating that requires validation/sign-off artifacts before broader order placement. Paper books, journal-entry review, trial-balance impact, and reconciliation-ready accounting views remain open. |
| Accounting-Aware Backtesting | 📝 | Planned research extension that projects journal entries, realized/unrealized bridge, cash ledger movement, fees, financing accruals, and trial-balance preview from a run. Existing backtest and ledger seams are prerequisites. |
| Strategy-to-Ledger Drilldown | 📝 | Planned lineage path from strategy run to fill, position, P&L movement, journal entry, trial-balance impact, and report line item. Current run, ledger, and report seams are inputs only. |
| Books Before Broker Mode | 🔄 | Shared preview support now exists through `InvestmentAccountingPreviewModeDto.BooksBeforeBroker`: Transaction Lab previews return balanced journal impact, trial-balance movement, reconciliation expectations, evidence/source blockers, required accounting and broker-routing approvals, and expected broker action before paper/live movement is staged. Product UX, persisted preview sessions, and full promotion integration remain open. |
| Investment Accounting Sandbox / Transaction Lab | 🔄 | Shared Transaction Lab preview support now covers trades, dividends, accruals, fees, corporate actions, and broker reconciliation examples with balanced expected-journal candidates, trial-balance impact, ledger-impact flags, evidence warnings, and reconciliation expectations. Full product UI and persisted lab sessions remain open. |
| Insurance Investment Accounting Pack | 📝 | Planned specialized package for book value, market value, unrealized gain/loss, income accrual, classification, impairment/watchlist, and insurance-style exports. Ledger, Security Master, and reporting foundations exist, but the pack is not delivered. |
| Close the Books Workflow | 🔄 | Partial support exists through the shared operations-continuity workflow and `/api/workstation/operations/continuity*` routes for account-period broker import/normalization, Security Master resolution and override approval, ledger draft/validate/post, reconciliation, approval, close, reopen, and hash-chained timeline reads. The browser Accounting Continuity surface at `/accounting/operations-continuity` now consumes these contracts for workflow list/detail, gates, blockers, close checklist, timeline, and highest-value next-action routing with disabled reasons. Shared workflow actions now make close workflow routing explicit: `AccountingReviewOperationsContinuity` targets `OperationsContinuity`, `AccountingReviewCloseReadiness` targets `OperationsClose`, the browser resolves both to `/accounting/operations-continuity`, and WPF aliases both to Fund Operations. The latest support hardening adds transactional workflow/journal/audit commit checks, explicit endpoint authorization in related ledger/promotion routes, sanitized execution metadata, broker/custodian statement intake that creates reconciliation cases with retained statement-row evidence, and governed close-package publication metadata with signer, sign-off rationale, retained manifest id/route, evidence hash, report pack id, evidence links, and checklist approvals on closed workflows. Broader SLA/casework depth, mutation handling, and operator-approved calibration remain open. |
| Close Readiness Score | 🔄 | Shared close-readiness scoring now exists for operations-continuity workflows and fund-account provider-ledger close review. `/api/fund-accounts/{accountId}/close-readiness` aggregates provider freshness, Security Master completeness, corporate-action/factor-schedule readiness, ledger balance/posting evidence, latest provider-ledger reconciliation, retained shadow-book comparison breaks, open account casework, held-security Security Master exception casework, and approval/sign-off blockers into weighted components, blockers, and next actions. Fixed-income and structured positions now require matched Security Master schedule feed evidence that can update Security Master history and support ledger valuation before the corporate-action/factor component can be marked ready, and provider-ledger reconciliation checks a dedicated `FactorSchedule` provider capability before treating factor/coupon/principal/loan-schedule candidates as routable. Closed operations-continuity workflows now retain the governed close-package manifest metadata that the readiness path gates before close. Broader report-family coverage and browser/WPF scoring UX remain open. |
| Broker Statement Reconciliation | 🔄 | Initial broker/custodian statement intake now validates canonical external statement files, auto-matches position rows, creates durable reconciliation cases for cash, fee, dividend, and transaction breaks, and links each case to retained statement-row evidence. Mapping UI, ledger/order/fill linkage, and full custodian operating workflow remain open. |
| Explain the Break | 🔄 | Initial statement-break explanations now include summary, source systems, probable cause, ledger impact, suggested next action, required sign-off role, and evidence links on durable reconciliation cases. Similar-break history and richer source-run/session/order/fill backreferences remain open. |
| Evidence Completeness Score | 🔄 | Shared `EvidenceCompletenessSummaryDto` is emitted by trading readiness, and the Evidence Workbench can validate packet completeness for supported evidence subjects. Broader close/report-pack scoring across reconciliation cases, report previews, and approval workflows remains planned. |
| Meridian Assurance Score | 🔄 | Initial shared `MeridianAssuranceScoreDto` support now ships inside evidence packet completeness results, scoring required evidence components and lowering status for critical issues, review warnings, stale SLA breaches, and missing nodes. The browser Evidence Workbench now renders the packet-scoped assurance score, component rows, validation issue counts, and no-orphan posture from the shared contract. Broader dashboard rollups across close readiness, report packs, promotions, and operational command-center views remain open. |
| Evidence SLA | 🔄 | Shared evidence validation now carries default freshness policies and `EvidenceSlaAssessmentDto` results for provider validation, paper/run replay checks, reconciliation runs, approval/promotion review, report packs, and report exports. SLA breaches create validation issues, feed the Meridian Assurance Score, and are now visible in the browser Evidence Workbench. Policy configuration UI, expiration lifecycle controls, and manager-facing SLA aging views remain open. |
| Evidence Vault | 🔄 | Initial durable file-backed vault support now retains exported evidence manifests plus local retained artifact payloads for packets, reports, statements, screenshots, approvals, and validation outputs when evidence nodes provide file-backed artifact refs. Vault identities record storage kind, artifact hash, size, source route, and canonical subject linkage. The vault write boundary now rejects retained payloads without canonical subject linkage or with unsupported subject kinds. Generalized lifecycle controls, role-based vault browsing, and broad producer integration remain open. |
| Evidence Vault Manifest Retention | 🔄 | Retained manifest exports now carry vault identity, index, lookup, host-served manifest route, and `file-manifest`/`file-bundle` storage modes. Route-only evidence remains manifest-only, while retained local payloads are copied into the vault bundle with hash and size metadata. |
| Security Master Validation Gate | 🔄 | Active worktree support adds workflow-scoped validation results and optional snapshots for strategy preflight, ledger posting, reconciliation intake, report-pack evidence, and override approval. Ledger posting now rejects every instrument-bearing journal line that lacks Security Master symbol, resolved id, explicit approval reference, provenance, or ledger-mapping evidence before the durable journal append boundary, and it blocks journal/line Security Master provenance that does not reference the resolved id, line symbols that diverge from the journal Security Master symbol, or generic ledger mappings that do not name the resolved symbol or Security Master id. Broader confidence scoring and close-workflow acceptance remain open. |
| Ledger Posting Discipline | 🔄 | Ledger writes now preserve `posting_kind`, enforce open/soft-closed/hard-closed period rules at the store boundary, and block instrument-bearing operations-continuity journal candidates unless each line proves Security Master resolution, approval reference, provenance, ledger mapping, and id/symbol/mapping-coherent lineage against the journal metadata. Multi-ledger operator workflows and close acceptance remain open. |
| Decision Memory | 📝 | Planned decision record with evidence used, approver, rationale, expected result, later outcome, related exceptions, and review date. Current approval metadata is support evidence only. |
| No Orphan Evidence Rule | 🔄 | Evidence packet graph validation warns when nodes are not linked into the packet graph, retained artifact validation now blocks vault writes unless each copied payload links to a supported canonical subject such as run, account, fund, strategy, instrument, reconciliation case, report pack, or approval, and the browser Evidence Workbench displays orphan-evidence ids from packet completeness. Broader producer-by-producer adoption and manager-facing orphan evidence reporting remain open. |
| Operator Readiness Console | 🔄 | Read-only browser support exists at `/trading/readiness` using shared readiness payloads, full-console checkpoint gates, and report-pack readiness cautioning. BrokerageSync blockers now route to the Settings provider setup repair anchor instead of a passive brokerage-sync review page. The browser command palette now loads shared workflow library/preset commands with hash-aware routing for the Alpaca setup anchor, exported page-tag route catalog coverage, and visible backend workflow action states from shared action tones. Data and Trading routes now add live quote, order-book, selectable recent-trade detail, watchlist, local Price Alerts, order-ticket staging, selectable Recent Fills detail, quick-trade accepted/rejected readiness handoffs, and historical-chart context that can support readiness review, but manager command-center scope, SLA aging, and report-pack lifecycle controls remain open. |
| Strategy / Promotion Passport | 🔄 | Promotion checklist and review-packet support exist, and the browser Strategy workspace now includes a visual Strategy Designer support surface for multi-leg payoff/participation exploration with backend action metadata that separates browser-openable GET evidence from reference-only POST validation, preview, and run-backtest commands, plus Covered Call chain-preview, saved-run-history, and selectable trade-timeline detail support evidence. A shared Strategy Engine pre-run foundation now adds explicit strategy definitions, typed parameter schemas, data-dependency policy, request validation, evidence hashes, and workstation definitions/validate-run endpoints for Covered Call and visual designer flows. A canonical passport projection with owner, approved use, required evidence, open exceptions, and approval history remains planned. |
| Proof of Strategy Certificate | 📝 | Planned static certificate for one strategy version or run showing identity, version hash, parameters, approved universe, data trust, backtest, paper, replay, risk, ledger consistency, blockers, and approval history. |
| Data Trust Passport | 🔄 | DK1 provider trust packets, validation evidence, freshness/completeness posture, and data-quality services provide support evidence. Dataset-level passport projection remains planned. |
| Meridian Data Trust | 🔄 | Commercial package for provider confidence, coverage heatmaps, stale-data warnings, symbol mappings, corporate-action completeness, replay readiness, and validation evidence. Provider-ledger detail now retains corporate-action/factor-schedule evidence candidates from provider positions, explicit provider corporate-action/factor/loan-schedule events, dividend/interest cash activity, and principal/paydown cash activity with Security Master attribution status; retained ledger-effect rows classify provider factor and loan-schedule events as valuation inputs, attributed dividend/interest cash as cash/income journal-preview support, and attributed principal/paydown cash as cash/principal journal-preview support. The retained readiness payload now also projects Security Master schedule feed rows with target feed kind, required provider feed, provider event, factor/cash amounts, attribution status, and booleans for Security Master history update and ledger valuation support. Degraded provider-ledger casework now carries that ledger-effect metadata for provenance drilldowns. Provider-ledger capability enforcement now includes held asset-class account-position support and historical quote/valuation-mark support in addition to corporate-action and factor-schedule routing, but dataset-level trust UX and broader acceptance remain open. |
| Security Master Confidence Score | 📝 | Planned confidence projection based on identifier completeness, source agreement, pricing availability, corporate-action coverage, classification completeness, account usage, reconciliation history, and unresolved conflicts. Browser search-result selection, identity drill-ins, details/lots tracking, server-side operator overrides, and UFL/reference-data projections are inputs only. |
| Instrument Passport | 🔄 | Shared `InstrumentPassportDto` and `/api/workstation/security-master/securities/{securityId}/passport` now compose identifiers, aliases, provider mappings, provider-confidence rows, lifecycle events, corporate actions, trading parameters/pricing readiness, trust posture, and downstream usage from the Security Master workbench service. Provider-confidence rows include source, freshness, confidence score, identifier-conflict IDs/summaries, and override history; provider-ledger reconciliation passports now retain the same override-history context for resolved provider positions, cap confidence with a `PROVIDER_EVIDENCE_STALE` issue when the supporting provider projection is stale, and seed steward-owned stale-mapping casework when break-queue storage is available. Reconciliation-history drilldown, browser/WPF dedicated passport UX, and retained evidence links remain open. |
| Shadow NAV / Shadow Books | 🔄 | Provider-ledger reconciliation now retains a shadow-book comparison section for account cash, aggregate position market value, total equity, income/accrual, pending settlement availability, realized P&L, unrealized P&L, custodian-statement versus provider-position quantity/market-value rows, and retained bank-statement cash/income cash-flow rows when statement lines exist for the snapshot date. Non-primary shadow-book variances are promoted into accounting-grade break records with owner, tolerance, aging, sign-off, explanation, and optional durable casework metadata, while primary cash/securities/equity dimensions stay on the top-level provider-ledger checks to avoid duplicate tasks. Dedicated shadow NAV generation, browser/WPF workflow UX, and broader close-comparison workflow remain open. |
| Fund Close Checklist | 🔄 | Operations Continuity now derives shared close checklist tasks from server gates and exposes them in the browser Accounting Continuity UI with required evidence, owners, due dates, approval counts, evidence pointers, remediation routes, and acknowledgement state. Close commands retain governed close-package publication metadata with sign-off rationale, evidence hash, retained manifest id/route, report pack id, linked evidence, and checklist approvals. Broader mutation handling, WPF parity polish, and operator-approved calibration remain open. |
| Fund Operating Model Templates | 📝 | Planned setup templates for research-to-paper strategy accounts, long/short equity shadow books, direct-lending portfolios, family-office SMAs, and model-portfolio programs. |
| Audit Trail Explorer | 🔄 | Shared `AuditTrailExplorerResultDto` and `/api/execution/audit/search` now normalize retained execution, promotion, order, operator-action, control, and Operations Continuity timeline records into searchable cross-object rows for close, reconciliation, approval, run, and control review. Manual override rows are keyed by override id as operator actions, while circuit-breaker rows keep execution-control object kind and direct control routes. Browser/WPF explorer UI and broader non-execution audit producers remain open. |
| Portfolio Impact Simulator | 📝 | Planned after shared portfolio/ledger continuity is stable. Pre-approval exposure, cash, liquidity, ledger, and report-pack impact modeling remains open. |
| Meridian Control Center | 🔄 | Planned manager landing page for blockers, owners, aging, missing evidence, report risk, and waiting promotions. The read-only Operator Readiness Console is support evidence, not full control-center scope. |
| Meridian Report Factory | 🔄 | Initial shared contracts and services now cover template registration/rendering, report-pack period workflow states, approval/publication controls, retained manifests, line provenance, restatement metadata, and archive transitions. Template authoring UX, production scheduler, persisted lifecycle store, and broad report family coverage remain planned. |
| Report Line Provenance | 🔄 | Shared `LedgerAmountProvenanceDetailDto` and `/api/fund-structure/report-packs/{reportId}/ledger-provenance` now return retained report-line drilldown for ledger amount, provider-event evidence from retained lineage or related provider-ledger casework, structured provider corporate-action/factor event metadata, provider-ledger ledger-effect/journal-support metadata, Security Master link with durable security id when retained, Security Master exception casework for the same instrument, reconciliation state/cases with owner/status/sign-off posture, approval state, report usage, and structured strategy/run links with run id, label, route, source, capture time, and line-scope posture. Browser/WPF click-through UX, calculation-assumption drilldown, and broader report-family coverage remain open. |
| Report Restatement Tracker | 🔄 | Initial workflow support records restatement reason, approver, prior version report id, changed lines, and required evidence links for changed values. The shared workspace reporting summary can now surface report-pack workflow records, and the browser report-pack task panel renders restatement reason, approver, prior version, changed lines, and evidence links from that shared DTO. Persisted recovery and similar-restatement analytics remain open. |
| Controls-as-Code / Policy Mapping | 📝 | Planned configurable control layer for promotion, reconciliation, reporting, materiality, dual review, period locking, evidence expiration, policy references, and evidence-based permissions. Current approval/readiness checks are inputs, not a policy engine. |
| Evidence Packet Actions | 🔄 | Browser links now open `/reporting/evidence` from Trading readiness, Strategy/Portfolio selected-run evidence, Accounting reconciliation review and dense-table detail selection, and Reporting report-pack context. The Evidence Workbench now renders retained vault bundle metadata after export, including vault id, storage kind, copied artifact paths, hash, size, source route, and canonical subject, plus packet-scoped Meridian Assurance, Evidence SLA breach, and orphan-evidence panels. Shared packet support now covers Security Master conflict subjects with open-conflict casework links and operations approval subjects with close-workflow approval/audit/checklist/report-pack evidence. |
| Buyer Demo Mode / Role-Based Demo Views | 🔄 | Browser-first demo packaging has support evidence through the `Demo data` banner, sample-flow links into watchlist, seeded `AAPL` live quotes, readiness, Alpaca paper setup, Overview portfolio-at-a-glance plus Today panel, Price Alerts, Strategy Designer, Covered Call no-host chain/run-history/trade-timeline fixtures, menu-linked screenshot route coverage, and no-host quote/trade/depth/symbol/snapshot/historical-bar fixtures that populate `/data/quotes?symbol=AAPL`. Screenshot capture workflows now run shared validation that fails missing, stale, wrong-route/wrong-page, blank, or low-entropy PNG outputs before upload or commit. Seeded funds, accounts, strategies, paper sessions, breaks, approvals, evidence packets, and role-specific CIO/trader/controller/operations/compliance/auditor views remain open. |

Evidence-backed narrative, client-facing commentary drafting, and evidence-aware AI summaries remain future parking-lot concepts. Keep them outside commercial module scope until the evidence graph, vault, provenance, permissions, reconciliation, and report-pack structures can ground generated text in Meridian evidence.

---

## 1. Core Infrastructure

| Feature | Status | Notes |
| --------- | -------- | ------- |
| Event Pipeline (`System.Threading.Channels`) | ✅ | Durable-streaming bounded channel, explicit backpressure (`Wait`), 50 K capacity, adaptive batch consume, and nanosecond timing |
| Injectable `IEventMetrics` | ✅ | Static dependency removed; `TracedEventMetrics` decorator available |
| `CompositeSink` fan-out | ✅ | Per-sink fault isolation; JSONL + Parquet simultaneously |
| Write-Ahead Log (WAL) | ✅ | SHA-256 checksums, streaming recovery, uncommitted-size warnings, and sink flush before commit |
| Provider Registry & DI | ✅ | `[DataSource]` scanning, `ProviderRegistry`, `ServiceCompositionRoot` |
| Config Validation Pipeline | ✅ | `ConfigValidationPipeline` with composable stages; obsoletes `ConfigValidationHelper` |
| Graceful Shutdown | ✅ | `GracefulShutdownService`, provider disconnect, flush-to-disk before exit |
| Category-accurate exit codes | ✅ | `ErrorCode.FromException()` maps to codes 3–7 for CI/CD differentiation |
| Dry-run mode (`--dry-run`) | ✅ | Full validation without starting collection; `--dry-run --offline` skips connectivity |
| Configuration hot-reload (`--watch-config`) | ✅ | `ConfigWatcher` triggers live config update |
| Persistent deduplication ledger | ✅ | `PersistentDedupLedger`; disk-backed dedup tracking that survives restarts |
| Ingestion job management | ✅ | `IngestionJobService`; per-symbol ingestion job lifecycle, status, and scheduling |

---

## 2. Streaming Data Providers

| Provider | Status | Remaining Work |
| ---------- | -------- | ---------------- |
| **Alpaca** | ✅ | Credential validation, automatic resubscription on reconnect, quote routing |
| **Interactive Brokers** | 🔑 | Real runtime requires `-p:DefineConstants=IBAPI` plus the official `IBApi` surface; non-`IBAPI` builds expose simulation/setup guidance instead of broker connectivity |
| **Polygon** | ⚠️ | Real connection when API key present; committed replay fixtures close the parser path, while live reconnect/websocket throttling remain explicitly runtime-bounded |
| **Robinhood** | 🔑 | Unofficial broker-backed quote polling plus brokerage reads/orders, options chains, and historical daily bars when `ROBINHOOD_ACCESS_TOKEN` is present; runtime bounds must be regenerated or attached for the review run because generated `artifacts/provider-validation/` packets are no longer retained in git |
| **NYSE** | 🔑 | Requires NYSE Connect credentials; L1/shared-lifecycle evidence is strong, but generated runtime packets are no longer retained in git |
| **StockSharp** | 🔑 | Requires StockSharp connector-specific credentials + connector type config. Runtime evidence for the validated adapter set is generated outside the retained repo tree, while the active Wave 1 gate keeps StockSharp outside the core provider set as future validation inventory; crypto connectors remain optional/example paths |
| **Failover-Aware Client** | ✅ | `FailoverAwareMarketDataClient` with `ProviderDegradationScorer`, per-provider health |
| **Streaming Failover Service** | ✅ | `StreamingFailoverService` + `StreamingFailoverRegistry`; runtime failover orchestration with configurable rules and health evaluation |
| **IB Simulation Client** | ✅ | `IBSimulationClient` for testing without live connection |
| **NoOp Client** | ✅ | `NoOpMarketDataClient` for dry-run / test harness scenarios |

Provider validation matrix and evidence guidance now live in `docs/status/provider-validation-matrix.md` and `docs/providers/provider-confidence-baseline.md`, with `scripts/dev/run-wave1-provider-validation.ps1` as the offline gate runner and `artifacts/provider-validation/` treated as generated run output rather than retained source.
Adapter capability readiness is canonically tracked in `docs/status/provider-capability-matrix.md`.

### Remaining work to reach full provider coverage

- **Polygon**: Validate WebSocket message parsing against Polygon v2 feed schema (trades, quotes, aggregates, status messages). Add round-trip integration test with a recorded WebSocket session replay.
- **Robinhood**: Quote polling, historical bars, symbol search, options chains, and brokerage paths are in code; remaining work is explicit runtime evidence for bounded broker-session scenarios, regenerated or attached for each DK1 review run.
- **StockSharp**: Runtime connector guidance and unsupported-path recovery messaging are now aligned; remaining work is moving the validated adapter set from bounded to captured runtime evidence without broadening the Wave 1 set.
- **IB**: Scripted setup instructions, version-bound tests, and a compile-only smoke-build path now exist; remaining work is keeping the vendor-runtime path validated against real IB API releases and entitlements.

---

## 3. Historical Backfill Providers

| Provider | Status | Notes |
| ---------- | -------- | ------- |
| Alpaca | ✅ | Daily bars, trades, quotes; credentials required |
| Polygon | ✅ | Daily bars and aggregates; API key required |
| Robinhood | 🔑 | Daily bars via unofficial Robinhood API; access token required |
| Tiingo | ✅ | Daily bars; token required |
| Yahoo Finance | ✅ | Daily bars; unofficial API, no credentials |
| Stooq | ✅ | Daily bars; free, no credentials |
| Finnhub | ✅ | Daily bars; token required |
| Alpha Vantage | ✅ | Daily bars; API key required |
| FRED Economic Data | 🔑 | Economic time series mapped to synthetic daily bars by series ID; API key required |
| Nasdaq Data Link (Quandl) | ✅ | Various; API key required |
| Interactive Brokers | 🔑 | Full implementation behind `IBAPI`; smoke builds remain compile-only and are not operator-ready historical access |
| StockSharp | ✅ | Via StockSharp connectors; runtime/historical coverage depends on connector setup, package surface, and entitlement |
| **Composite Provider** | ✅ | Priority-based fallback chain, rate-limit tracking, per-provider health |
| **Gap Backfill Service** | ✅ | `GapBackfillService` triggered on reconnect; uses `WebSocketReconnectionHelper` gap window with Wave 1 repo-backed proof in `GapBackfillServiceTests` |
| **Backfill Rate Limiting** | ✅ | `ProviderRateLimitTracker` per provider; exponential backoff with `Retry-After` parsing |
| **Backfill Scheduling** | ✅ | Cron-based `ScheduledBackfillService`; `BackfillScheduleManager` with CRUD API |
| **Backfill Progress Reporting** | ✅ | `BackfillProgressTracker`, per-symbol %, exposed at `/api/backfill/progress` |
| **Priority Backfill Queue** | ✅ | `PriorityBackfillQueue`, `BackfillJobManager`, `BackfillJob`; priority-ordered job execution |
| **Gap Analysis (Infrastructure)** | ✅ | `DataGapAnalyzer`, `DataGapRepair`, `DataQualityMonitor`; storage scan, gap detection, automated repair |

---

## 4. Symbol Search

| Provider | Status | Notes |
| ---------- | -------- | ------- |
| Alpaca | ✅ | `AlpacaSymbolSearchProviderRefactored`; US equities + crypto |
| Robinhood | ✅ | `RobinhoodSymbolSearchProvider`; public instruments API, no authentication required |
| Finnhub | ✅ | `FinnhubSymbolSearchProviderRefactored`; US + international |
| Polygon | ✅ | `PolygonSymbolSearchProvider`; US equities |
| OpenFIGI | ✅ | `OpenFigiClient`; global instrument ID mapping |
| EDGAR | ✅ | `EdgarSymbolSearchProvider`; SEC `company_tickers.json` cache for US company lookup and issuer detail enrichment |
| StockSharp | ✅ | `StockSharpSymbolSearchProvider`; multi-exchange |
| **Symbol Import/Export** | ✅ | CSV import/export via `SymbolImportExportService`; portfolio import |
| **Symbol Registry** | ✅ | `CanonicalSymbolRegistry` with persistence; `SymbolRegistryService` |
| **Symbol Normalization** | ✅ | `SymbolNormalization` utility; PCG-PA, BRK.A, ^GSPC, =SPX patterns |

---

## 5. Data Canonicalization

| Component | Status | Notes |
| ----------- | -------- | ------- |
| Design document & field audit | ✅ | `docs/architecture/deterministic-canonicalization.md` |
| `MarketEvent` canonical fields | ✅ | `CanonicalSymbol`, `CanonicalizationVersion`, `CanonicalVenue`, `EffectiveSymbol` |
| `EventCanonicalizer` implementation | ✅ | Symbol resolution, venue normalization, typed payload extraction |
| `ConditionCodeMapper` — Alpaca (17 codes) | ✅ | CTA plan codes → `CanonicalTradeCondition`; `FrozenDictionary` |
| `ConditionCodeMapper` — Polygon (19 codes) | ✅ | SEC numeric codes → canonical |
| `ConditionCodeMapper` — IB (8 codes) | ✅ | IB field codes → canonical |
| `VenueMicMapper` — Alpaca (29 venues) | ✅ | Text names → ISO 10383 MIC |
| `VenueMicMapper` — Polygon (17 venues) | ✅ | Numeric IDs → MIC |
| `VenueMicMapper` — IB (17 venues) | ✅ | Routing names → MIC |
| `CanonicalizingPublisher` decorator | ✅ | Wraps `IMarketEventPublisher`; dual-write mode; lock-free metrics |
| Canonicalization metrics & API endpoints | ✅ | `/api/canonicalization/status`, `/parity`, `/parity/{provider}`, `/config` |
| Golden fixture test suite | Complete | 8 curated `.json` fixtures + `CanonicalizationGoldenFixtureTests`; PR checks now emit a canonicalization drift report and a manual maintenance workflow supports fixture upkeep |

### Remaining work

- Continue expanding fixture coverage as new providers or venue/condition edge cases are onboarded.

---

## 6. Storage & Data Management

| Feature | Status | Notes |
| --------- | -------- | ------- |
| JSONL storage sink | ✅ | Append-only, gzip-compressed, configurable naming conventions |
| Parquet storage sink | ✅ | Columnar, compressed; enabled via `EnableParquetSink` config. Wave 1 repo-backed tests now cover L2 snapshot flush, final dispose flush, and atomic temp-file cleanup |
| Tiered storage (hot/warm/cold) | ✅ | `TierMigrationService` with configurable retention per tier |
| Scheduled archive maintenance | ✅ | `ScheduledArchiveMaintenanceService`; tasks: integrity, orphan cleanup, index rebuild, compression |
| Portable data packaging | ✅ | `PortableDataPackager`; ZIP/tar.gz with manifest, checksums, SQL loaders |
| Package import | ✅ | `--import-package`, merge mode |
| Package validation | ✅ | SHA-256 integrity, schema compatibility checks |
| Storage quota enforcement | ✅ | `QuotaEnforcementService`; configurable max total and per-symbol limits |
| Data lifecycle policies | ✅ | `LifecyclePolicyEngine`; tag-based retention policies |
| Storage checksums | ✅ | `StorageChecksumService`; per-file SHA-256 tracking |
| Metadata tagging | ✅ | `MetadataTagService`; background save pattern; tag-based search |
| Analysis export (JSONL/Parquet/Arrow/XLSX/CSV) | ✅ | `AnalysisExportService`; configurable format, symbol filter, date range |
| Storage catalog | ✅ | `StorageCatalogService`; file inventory, symbol listing |
| Event replay | ✅ | `JsonlReplayer`, `MemoryMappedJsonlReader`, `EventReplayService`; pause/resume/seek; CLI `--replay` |
| File permissions service | ✅ | `FilePermissionsService`; cross-platform directory permission checks |
| Data lineage tracking | ✅ | `DataLineageService`; provenance chain per data file |
| Data quality scoring | ✅ | `DataQualityScoringService`; per-symbol quality scores |
| Shared UI JSON persistence | ✅ | `ConfigService`, `ExportPresetServiceBase`, and `QualityArchiveStore` now route file-backed JSON writes through `AtomicFileWriter`; export preset packaging clones presets before assigning exported IDs so operator preset state is not mutated by export |

---

## 7. Data Quality Monitoring

| Feature | Status | Notes |
| --------- | -------- | ------- |
| Completeness scoring | ✅ | `CompletenessScoreCalculator`; expected vs. received events |
| Gap analysis | ✅ | `GapAnalyzer`; liquidity-adjusted severity (Minor → Critical) |
| Anomaly detection | ✅ | `AnomalyDetector`; price/volume outliers |
| Sequence error tracking | ✅ | `SequenceErrorTracker`; out-of-order and duplicate event detection |
| Cross-provider comparison | ✅ | `CrossProviderComparisonService` |
| Latency distribution | ✅ | `LatencyHistogram`; p50/p90/p99 tracking |
| Data freshness SLA monitoring | ✅ | `DataFreshnessSlaMonitor`; configurable thresholds, violation API |
| Quality report generation | ✅ | `DataQualityReportGenerator`; daily/on-demand reports |
| Dropped event audit trail | ✅ | `DroppedEventAuditTrail`; JSONL log + `/api/quality/drops` API |
| Bad tick filter | ✅ | `BadTickFilter`; placeholder price detection, spread sanity |
| Tick size validation | ✅ | `TickSizeValidator` |
| Spread monitoring | ✅ | `SpreadMonitor`; bid/ask spread alerts |
| Clock skew estimation | ✅ | `ClockSkewEstimator` |
| Timestamp monotonicity checking | ✅ | `TimestampMonotonicityChecker` |
| Backpressure alerts | ✅ | `BackpressureAlertService`; `/api/backpressure` endpoint |
| Provider degradation scoring | ✅ | `ProviderDegradationScorer`; composite health from latency, errors, reconnects |
| Liquidity profile | ✅ | `LiquidityProfileProvider`; symbol-level liquidity classification for gap severity calibration |
| SLO definition registry | ✅ | `SloDefinitionRegistry`; runtime SLO definitions, compliance scoring, alert threshold mapping |

---

## 8. API Surface (HTTP)

| Area | Routes | Status |
| ------ | -------- | -------- |
| Status & health | `/api/status`, `/api/health`, `/healthz`, `/readyz`, `/livez` | ✅ |
| Configuration | `/api/config/*` (8 endpoints) | ✅ |
| Providers | `/api/providers/*`, `/api/connections` | ✅ |
| Failover | `/api/failover/*` | ✅ |
| Backfill | `/api/backfill/*` (13 endpoints) | ✅ |
| Quality | `/api/quality/*`, `/api/sla/*` | ✅ |
| Maintenance | `/api/maintenance/*` | ✅ |
| Storage | `/api/storage/*` | ✅ |
| Symbols | `/api/symbols/*` | ✅ |
| Live data | `/api/live/*` | ✅ |
| Export | `/api/export/*` | ✅ |
| Packaging | `/api/packaging/*` | ✅ |
| Canonicalization | `/api/canonicalization/*` | ✅ |
| Diagnostics | `/api/diagnostics/*` | ✅ |
| Subscriptions | `/api/subscriptions/*` | ✅ |
| Historical | `/api/historical/*` | ✅ |
| Sampling | `/api/sampling/*` | ✅ |
| Alignment | `/api/alignment/*` | ✅ |
| IB-specific | `/api/ib/*` | ✅ |
| Direct lending | `/api/loans/*` | ✅ |
| Workstation and reconciliation | `/api/workstation/*` | ✅ |
| Metrics (Prometheus) | `/api/metrics` | ✅ |
| SSE stream | `/api/events/stream` | ✅ |
| OpenAPI / Swagger | `/swagger` | ✅ |
| API authentication | `X-Api-Key` header only (no query-string auth) | ✅ |
| Rate limiting | 120 req/min per key, sliding window | ✅ |
| **Total route constants** | **479** | **0 stubs remaining** |

### OpenAPI annotations

| Endpoint family | Typed `Produces<T>` | Descriptions | Status |
| ----------------- | --------------------- | -------------- | -------- |
| Status | ✅ | ✅ | ✅ |
| Health | ✅ | ✅ | ✅ |
| Config | ✅ | ✅ | ✅ |
| Backfill / Schedules | ✅ | ✅ | ✅ |
| Providers / Extended | ✅ | ✅ | ✅ |
| All other families | ✅ | ✅ | ✅ |

---

## 9. Web Workstation And Local API Surface

The React/Vite dashboard in `src/Meridian.Ui/dashboard/` is now the active operator UI delivery
lane, with built assets served from `src/Meridian.Ui/wwwroot/workstation/`. The older local web/API
surface remains useful for diagnostics, API-backed workflows, and support tooling, while WPF is
retained for desktop compatibility, shared-contract regression checks, and support fixes.

| Feature | Status | Notes |
| --------- | -------- | ------- |
| Web workstation dashboard | ⚠️ | React/Vite source in `src/Meridian.Ui/dashboard/` with built workstation assets in `src/Meridian.Ui/wwwroot/workstation/`; current screens cover overview, strategy, trading, portfolio, data, accounting, reporting, settings, and retained legacy paths, and `buildAppShellViewState` centralizes active workspace resolution plus loading, partial-degradation, retry, bootstrap-failure status panels, and query-string route-focus keys for subject/symbol handoffs. Canonical seven-workspace navigation is present, the command palette now consumes shared workflow library/preset payloads, renders grouped command sections, treats hash-targeted setup routes such as `/settings#alpaca-provider-setup` distinctly from the Settings root, exposes the page-tag route catalog for parity review, and renders shared workflow action tones as visible Ready/Review/Blocked command states. Global workstation refreshes suppress stale bootstrap responses, Overview manual refreshes suppress stale or unmounted status responses and now projects portfolio at-a-glance, Today panel day P&L/movers/orders/fills, and status-banner/refresh-command presentation from the view model, Portfolio position and run-evidence lists now use shared dense selectable rows with accessible expanded detail-panel state, sidebar sub-items and richer mega-menu/command grouping are present, menu-linked screenshot-route coverage exists, Data includes live quotes/order book with selectable recent-trade detail inspection, local Price Alerts with UTC trigger/poll labels, single/bulk watchlist management with view-model-owned no-quote/empty-value/busy labels, stale-safe backfill preview/run controls with shared dense-table queue selection and keyboard expanded detail, provider-setup success actions that route to live quotes/backfill/readiness/Security Master validation, live bid/ask/last quote snapshots, click-to-trade order-ticket staging with accepted/rejected Trading readiness handoffs, transient refresh-failure alerts that preserve the last good market snapshot, and intraday/historical price charts backed by shared `/api/data/*` and `/api/historical` routes, Trading includes selectable Recent Fills dense rows with a linked detail panel, Strategy includes a gated Quant Lab browser surface backed by `/api/quant/run`, `/api/quant/parameters`, and `/api/quant/templates` with plot geometry/accessibility state split into a reusable view model, Quant Notebook cell execution and historical-data fetch helpers, a visual Strategy Designer at `/strategy/designer`, Covered Call chain-preview and saved-run-history support at `/strategy/covered-call`, plus run-library selected run and promotion-history detail state, Reporting includes report-pack profile preview/export-analysis actions and the `/reporting/evidence` Evidence Workbench for packet completeness, lineage, validation, manifest export, and retained vault artifact bundle inspection, browser Security Master lots use selectable dense rows with detail state, command-palette/mega-menu/Portfolio filter focus management has been hardened, the Meridian Design System reference workbench and tokenized-color support are available, and placeholder routes are being replaced by dedicated task panels, while full cockpit/governance acceptance remains open |
| Operator Readiness Console | ⚠️ | Browser-first read-only console at `/trading/readiness` aggregates latest runs, active paper session, DK1/provider trust, reconciliation breaks, promotion blockers, governance report-pack readiness, full-console checkpoint gates, and operator-inbox work items from `/api/workstation/*` payloads; evidence completeness diagnostics now include warning vs blocking issue counts and orphan-evidence IDs with retained-artifact canonical-subject enforcement across run/account/fund/strategy/instrument/reconciliation/report/approval scopes; current Data live quote/order-book/watchlist/Price Alerts routes, recent-trade detail inspectors, click-to-trade staging, and historical charts add market-context support, but this is still the first API-first web readiness surface, not a WPF replacement or full manager command center |
| Browser Portfolio brokerage support | ⚠️ | Portfolio screen/view-model support now projects brokerage connection state, household account rows, account-kind filters, brokerage positions, selected-run evidence, shared dense-table position/run selection with expanded detail-panel semantics, run-linked portfolio comparison summary across selected rank, best/weakest P&L, best Sharpe, and mode/engine coverage, and on-demand selected-run drill-in summaries from shared attribution, drawdown/equity-curve, cash-flow, and fill endpoints. It also keeps backend links from shared API payloads and next actions for provider setup repair, Trading readiness review, and Trading cockpit review. It is read-only portfolio/accounting continuity evidence, not live trading readiness |
| Browser Reporting task panel | ⚠️ | Reporting screen/view-model support now exposes report-pack workflow task state, approval-profile selection, target readiness, selected-profile preview/export-analysis actions, backend endpoint links that mark concrete GET routes as browser-navigable and POST/template routes as reference-only, abort-backed export commands, and stale export-response suppression when the selected approval profile changes or the same profile is restarted mid-run. Governed report-pack generation, approval, restatement, and publication lifecycle remain open |
| Browser Settings capability coverage | ⚠️ | Settings screen/view-model support now shows backend capability coverage for canonical routes/endpoints, can verify or clear Alpaca paper API keys through shared brokerage connection endpoints, and refreshes provider-routing connections, bindings, and trust snapshots after provider setup plus during lightweight live refresh. TradeDesk role sessions can perform this paper brokerage credential setup without broader configuration or user-management rights. Broader credential policy, evidence-path governance, and live-account readiness remain open |
| Web Research run library | ⚠️ | `ResearchScreen` and `useResearchRunLibraryViewModel` expose retained run review, two-run compare/diff readiness, promotion-history loading, stale run-scoped compare/diff/promotion response suppression, accessible command-error alerts, and component/view-model coverage. Browser run comparison/diff now consumes shared artifact completeness, compatibility warnings, engine/mode context, final-equity deltas, drawdown deltas, Sharpe deltas, strategy id/version metadata, lineage relation, and compatibility level for richer cross-version and cross-engine portfolio evidence. The run library now also summarizes retained-run count, backtest/paper/live coverage, paper/live-adjacent lineage, and normalized native/Lean engine coverage from shared run records. Native and QuantConnect Lean results now share an SDK canonical `BacktestResult` normalization seam; Lean imports retain summary-only coverage warnings when fill, cash-flow, attribution, or ledger artifacts are absent. Strategy-aware launch/preflight and full Backtest Studio orchestration remain open |
| HTML dashboard (auto-refreshing) | ✅ | `HtmlTemplateGenerator`; SSE-powered live updates |
| Server-Sent Events stream | ✅ | `/api/events/stream`; 2-second push cycle |
| Configuration wizard UI | ✅ | Interactive provider setup, credential entry, symbol config, and browser provider-setup next-validation actions |
| Backfill controls | ✅ | Provider select, symbol list, date range, run/preview; browser dialog disables request fields while commands settle and suppresses stale preview/run responses |
| Symbol management | ✅ | Add/remove symbols, status per symbol, and browser watchlist action-error alerts |
| Provider comparison table | ✅ | Feature matrix across all providers |
| Options chain display | ✅ | Derivatives configuration and data display |

---

## 10. WPF Desktop Application

> **Status:** Code present in `src/Meridian.Wpf/` and `tests/Meridian.Wpf.Tests/`, both included in the solution build. Builds full WPF desktop app on Windows; produces a CI-compatible stub on Linux/macOS.

The registry-backed WPF screen inventory is maintained in [`desktop-application-screens.md`](desktop-application-screens.md), with workspace/page tag, visibility tier, related tags, implementation status, known gaps, and retained screenshot evidence where available.

### Shell & Navigation (Complete baseline)

- Workspace model now persists built-in `Research`, `Trading`, `Data Operations`, and `Governance` workspaces, including legacy workspace ID migration for older saved sessions.
- Main workstation shell is metadata-driven through `ShellNavigationCatalog`, with workspace home pages, primary/secondary/overflow navigation tiers, recent pages, command-palette search keywords, and related-workflow links.
- Workspace home pages now act as shell-first operator launchpads (`ResearchShell`, `TradingShell`, `DataOperationsShell`, `GovernanceShell`) instead of a long page-directory entry model.
- Desktop launch and workflow automation now normalize page/deep-link startup actions through `DesktopLaunchArguments`, forward secondary launch arguments to the primary instance through the single-instance named pipe, keep `ShellAutomationState` available as a hidden-but-present page marker, and split isolated restore/build arguments so shortcuts and screenshot/manual workflows target the same canonical workspace tags operators use without target-framework asset drift. The GitHub screenshot-refresh workflow now captures catalog/manual WPF workflows with scheduled, push, or manual dispatch inputs, uploads per-workflow diagnostics, and commits generated PNG updates once after the capture matrix. The shared operator inbox also includes actionable warning/critical run review-packet items from the latest runs alongside readiness and reconciliation work items.
- Command palette (`Ctrl+K`), keyboard shortcuts, workspace-tile switching, and governance/fund-ops aliases keep low-frequency pages reachable without promoting them to top-level roots.
- Workspace shell context strips now standardize scope, environment, freshness, review-state, alert, and currency cues across the four workstation shells; current shell orchestration can promote active operator-inbox review items into the critical context lane without replacing the shared readiness source.
- Shell context attention banners now expand warning or danger badges into actionable detail with severity, owning workspace, signal source, and the expected next action.
- `WelcomePage` now summarizes first-run readiness progress across provider connection, symbol inventory, and storage-path posture, then points the operator toward the next shell decision without counting demo or fixture state as readiness evidence.
- `TradingWorkspaceShellPage` now includes a desk briefing hero that projects current desk focus, readiness tone, next handoff, and primary/secondary actions from active-run, workflow-summary, and shared operator-readiness inputs.
- The Trading desk briefing hero treats warning or critical shared readiness work items as blockers before it can show a ready active-run state, routing those work items to concrete review targets such as account portfolio, Security Master, reconciliation, report pack, audit trail, or alerts.
- `OrderBookPage` now includes an order-flow posture strip that summarizes selected-symbol scope, bid/ask depth availability, spread, cumulative delta, recent-trade tape readiness, pressure state, and one next monitoring handoff before an operator reads the depth ladder or heatmap.
- `TradingHoursPage` now includes a session briefing card that distinguishes regular-session live risk, pre-market staging, after-hours review, and closed-planning handoffs from market-calendar state before an operator stages trading work; its holiday-calendar pane now distinguishes loading, unavailable, no-row, and loaded closure states instead of leaving an empty calendar table.
- `ResearchWorkspaceShellPage` now includes a research desk briefing hero that projects market briefing, selected-run posture, run-detail and portfolio drill-ins, and paper-promotion review handoffs from shared workstation run data.
- `DataWorkspaceShellPage` now lives under `src/Meridian.Wpf/Features/Data/` and includes a data desk briefing hero backed by feature-module registration, a snapshot service, a presentation adapter, and `DataWorkspaceShellViewModel`; it projects provider health, resumable backfills, storage health, collection sessions, export jobs, operational blockers, loading/error state, and next-handoff actions from shared service data while keeping page code-behind WPF-only.
- `WatchlistPage` now includes watchlist posture guidance for saved list count, pinned list coverage, symbol coverage, visible search scope, pinned-first card ordering, pinned badges, and empty-state handoffs before an operator loads or imports symbol sets.
- `StoragePage` now opens with an archive-posture card for daily growth, capacity horizon, last metrics scan, empty archives, capacity pressure, stable growth, and scan failures, then annotates the file-structure preview with selected root, naming convention, compression mode, preview guidance, stable automation IDs, and a normalized sample tree before an operator runs backfill, export, or packaging jobs.
- `ProviderHealthPage` now includes a provider-posture briefing that condenses stale snapshots, disconnected streaming sessions, mixed-provider states, and blocked backfill coverage into one next handoff before the operator scans individual provider cards.
- `SystemHealthPage` now includes a system triage briefing that condenses provider health, storage posture, disk pressure, and retained event severity into one next handoff before the operator scans diagnostics panels; provider and recent-event empty states distinguish pending scans from confirmed empty snapshots.
- `NotificationCenterPage` now supports history recovery when search, unread-only, or severity filters hide retained notifications; `NotificationCenterViewModel` resets those filters against the already-loaded history window.
- `ActivityLogPage` now includes a triage strip that summarizes visible entries, retained error and warning counts, latest entry posture, and active filters before the operator scans retained log rows, plus header export and clear actions that stay disabled until visible or retained log state supports them.
- `StrategyRunsPage` now distinguishes an empty run library from filters that hide retained runs, shows visible-versus-recorded run scope beside search, exposes a reset-filters recovery action against the already-loaded run browser rows, and guides compare-run selection when only one visible run exists or the comparison matches the primary run.
- `RunCashFlowPage` now hides empty ladder/event grids and projects selected-run, missing-run, no-cash-flow, and loaded-state guidance from the retained run cash-flow summary before an operator interprets cash movements.
- Fixture/offline workflow mode is explicitly separated from operational readiness: the shell presents deterministic fixture state as neutral demo data, while Data Operations carries environment-mode context when provider telemetry is absent.
- Provider Health is owned by Data Operations navigation and workflow summaries, while Diagnostics and System Health remain Governance-owned operational support surfaces.
- Legacy deep pages now route through `WorkspaceDeepPageHostPage` in both standalone and docked presentations, so direct navigation and workspace docks share the same workspace title, reachability metadata, related-workflow chrome, and trust-state posture without removing the underlying page functionality.
- Hosted deep-page command chrome now supports a compact surface mode, preserving the shared command bar and related workflow context without reintroducing duplicate page framing.
- Legacy deep pages can now suppress duplicate inner hero/title chrome through `WorkspaceShellChromeState` plus embedded-shell styles (`EmbeddedShellHeroCardStyle`, `EmbeddedShellHeaderGridStyle`, and `EmbeddedShellHeaderStackPanelStyle`), tightening density when pages are already hosted inside the shared workstation shell.
- Action-heavy hosted pages including `MessagingHubPage`, `NotificationCenterPage`, `SecurityMasterPage`, `ServiceManagerPage`, and `PositionBlotterPage` now collapse decorative identity chrome while preserving their page-specific commands, status badges, and trust signals inside the shared shell host.
- `PositionBlotterPage`, `SecurityMasterPage`, and `ServiceManagerPage` now go beyond top-band cleanup and render as workflow-native workbenches with persistent inspector rails for selection state, filters/runtime posture, and operator actions while preserving their existing commands and service integrations.
- The WPF landing page now starts from the four operator workspaces, and the main shell exposes a density toggle so operators can switch between standard and compact workstation framing without leaving the shell.
- `NotificationCenterPage` now behaves more like an operator inbox baseline, with search, unread-only filtering, severity filters, per-item read state, and richer empty/history states.
- The WPF main shell now consumes `/api/workstation/operator/inbox` through `WorkstationOperatorInboxApiClient`, shows a queue button with review counts and severity tone, and routes the primary work item to its target page; this is initial shell consumption, while end-to-end queue workflow acceptance remains open.
- `MessagingHubPage` now projects message-flow posture, subscriber readiness, retained activity scope, refresh recency, and clear-activity command state through `MessagingHubViewModel` instead of code-behind handlers.
- `GovernanceWorkspaceShellPage` now routes command-bar wiring, lane summary shaping, queue composition, hero-state decisions, and dock-action mapping through `GovernanceWorkspacePresentationService`, keeping page code-behind focused on lifecycle/event wiring and visual updates.
- `PositionBlotterPage` now includes a selected-position review rail with long/short/gross/net quantity, eligibility counts, and preview rows for batch-action review.
- `BatchBacktestPage` and `BatchBacktestViewModel` now run real request-level parameter sweeps through `IBatchBacktestService` with validation, progress, cancellation, result metrics, and focused ViewModel tests; strategy selection and persisted sweep grouping remain open Research work.
- Dock-hosted workspace pages are wrapped in `Frame` containers so WPF page content can be embedded safely inside the workstation docking surface.
- Theme switching, notification center, info bar
- Offline indicator (single notification + warning on backend unreachable)
- Session state persistence (active workspace, last page, window bounds)
- Shell-first regression coverage now includes DI registration checks, workspace-shell smoke tests, dock-hosting smoke tests, compact-host chrome assertions for representative legacy pages, isolated `MainPage` workflow automation, Provider Health posture-state tests, Trading Hours session-briefing and holiday-empty-state tests, System Health triage-state tests, Activity Log triage/export/clear-state tests, Watchlist posture and pinned-first display tests, StrategyRuns comparison-guidance tests, local single-instance mutex and launch-argument forwarding coverage, and a full registered-page navigation sweep in `tests/Meridian.Wpf.Tests/`.

### Pages with live service connections (Implemented)

| Page | Primary Service | Function |
| ------ | ---------------- | --------- |
| DashboardPage | StatusService, ConnectionService | System overview, provider status |
| BackfillPage | BackfillService, BackfillApiService | Trigger/schedule backfills |
| DataSourcesPage | ConfigService, ProviderManagementService | Provider configuration |
| ProviderPage | ProviderManagementService | Provider detail + credentials |
| ProviderHealthPage | ProviderHealthService | Per-provider health metrics with posture briefing |
| SettingsPage | ConfigService, ThemeService | App settings |
| SymbolsPage | SymbolManagementService | Symbol list management |
| SymbolStoragePage | StorageServiceBase | Per-symbol storage view |
| SymbolMappingPage | SymbolMappingService | Cross-provider symbol mapping |
| DataQualityPage | DataQualityServiceBase | Quality metrics dashboard with symbol-filter scope and empty-state guidance |
| DataSamplingPage | DataSamplingService | Data sampling configuration |
| DataCalendarPage | DataCalendarService | Calendar heat-map of collected dates |
| TradingHoursPage | ApiClientService | Market session status with trading-desk briefing and holiday-calendar empty states |
| DataBrowserPage | ArchiveBrowserService | Browse stored data files |
| DataExportPage | AnalysisExportService | Export stored data |
| AnalysisExportPage | AnalysisExportService | Advanced export options |
| AnalysisExportWizardPage | AnalysisExportWizardService | Guided export workflow |
| ChartingPage | ChartingService | OHLCV chart display |
| LiveDataViewerPage | LiveDataService | Real-time tick viewer |
| OrderBookPage | OrderBookVisualizationService | L2 order book display with order-flow posture |
| CollectionSessionPage | CollectionSessionService | Active session summary |
| ActivityLogPage | ApiClientService | Live event log with triage posture plus export/clear actions |
| DiagnosticsPage | NavigationService, NotificationService | System diagnostics |
| SetupWizardPage | SetupWizardService | First-run onboarding |
| PackageManagerPage | PortablePackagerService | Create/import packages |
| ScheduleManagerPage | ScheduleManagerService | Backfill schedules |
| ServiceManagerPage | BackendServiceManagerBase | Backend service status with control-lane and runtime inspector |
| StorageOptimizationPage | StorageOptimizationAdvisorService | Storage optimization advice |
| ArchiveHealthPage | ArchiveHealthService | Archive integrity status |
| SystemHealthPage | SystemHealthService | Comprehensive health view with triage briefing |
| AdvancedAnalyticsPage | AdvancedAnalyticsServiceBase | Advanced analytics |
| EventReplayPage | EventReplayService | Historical event replay |
| ExportPresetsPage | ExportPresetServiceBase | Saved export profiles |
| LeanIntegrationPage | LeanIntegrationService | QuantConnect Lean integration |
| MessagingHubPage | (messaging infrastructure) | WebSocket messaging hub |
| NotificationCenterPage | NotificationService | Notification history |
| OptionsPage | (options infrastructure) | Options/derivatives data |
| PortfolioImportPage | PortfolioImportService | Portfolio CSV import |
| RetentionAssurancePage | (RetentionAssuranceService) | Retention policy status |
| TimeSeriesAlignmentPage | TimeSeriesAlignmentService | Multi-symbol time alignment |
| WorkspacePage | WorkspaceService | Workspace management |

### Trading workstation migration target (Implemented baseline / workflow acceptance in progress)

The current WPF app exposes broad capability coverage and the active shell baseline now organizes those capabilities into seven canonical operator workspaces:

- **Trading** - live monitoring, orders, fills, positions, and strategy operation
- **Portfolio** - account, aggregate, fund, lending, and import workflows
- **Accounting** - ledger, cash, reconciliation, trial balance, and audit workflows
- **Reporting** - report packs, dashboards, analysis exports, and export presets
- **Strategy** - backtests, Lean engine flows, charts, replay, experiment comparison, and run workflows
- **Data** - providers, provider health, symbols, backfills, schedules, storage, exports, and quality
- **Settings** - preferences, credentials, diagnostics, services, alerts, help, and workspace layouts

Legacy Research, Data Operations, and Governance names remain compatibility aliases for the corresponding canonical roots.

This migration is tracked in [`../plans/trading-workstation-migration-blueprint.md`](../plans/trading-workstation-migration-blueprint.md), [`ROADMAP.md`](ROADMAP.md) Waves 1-4, and the current registry inventory in [`desktop-application-screens.md`](desktop-application-screens.md). The remaining work is workflow acceptance and deeper cockpit/shared-model/governance continuity, not a new shell taxonomy migration.

### Shared run / portfolio / ledger / reconciliation baseline (In progress)

- Shared workstation DTOs now exist for run summaries/details, portfolio summaries/positions, ledger summaries, journal rows, trial balance rows, and run comparison views.
- `StrategyRunReadService`, `PortfolioReadService`, and `LedgerReadService` now derive those models from recorded strategy/backtest results.
- `StrategyRunReviewPacketService` now emits stable run-scoped operator work-item IDs with workspace route, route URL, and page-tag hints for promotion review, Security Master coverage, continuity, and brokerage-sync blockers.
- WPF now includes a first-pass `StrategyRuns` browser plus `RunDetail`, `RunPortfolio`, and `RunLedger` drill-in pages, completed backtests are mirrored into that shared workstation flow, and QuantScript exposes local execution history with run-browser, run-detail, and compare handoffs when a mirrored Strategy Run exists.
- Run-scoped reconciliation contracts and service flows now exist through `ReconciliationRunRequest`, `ReconciliationRunSummary`, `ReconciliationRunDetail`, `ReconciliationRunService`, and `/api/workstation/reconciliation/*`.
- Governance fund operations now exposes explicit fund cash-flow projection ladders/events and account-linked multi-ledger views across consolidated, entity, sleeve, and vehicle dimensions.
- **Allowed sync data-flow path:** brokerage/provider sync ingestion must flow through fund-account scope first (`/api/fund-accounts/*` + `WorkstationBrokerageSyncStatusDto`), then shared run/portfolio/ledger/reconciliation services, and finally continuity/governance workstation DTOs (`/api/workstation/trading/readiness`, `/api/workstation/operator/inbox`, and reconciliation queue projections). Avoid introducing standalone sync projection endpoint contracts that bypass fund-account and shared services.
- Current account-sync support now includes explicit brokerage account kinds, fund-account brokerage links, positions/activity reads, cash-adjusted performance, cash-flow summaries, household portfolio rollups, Alpaca paper connection verification/revocation, and a read-only Robinhood aggregation adapter that consumes configured normalized endpoints without storing Robinhood passwords.
- Options chain routing now uses deterministic `IEnumerable<IOptionsChainProvider>` priority ordering, provider-health gating, and failover provenance/metrics in `OptionsChainService`; remaining extensions are richer user-surface provenance payloads and policy-tunable partial-response thresholds.
- The remaining gap is broader paper/live data-source adoption, richer portfolio/ledger analytics, deeper per-entity/per-sleeve/per-vehicle posting fidelity, richer reconciliation UX, and more complete cockpit-style workflow integration.

### Known WPF limitations

- `DiagnosticsPage` reads from local process/environment; not connected to remote backend API.
- Current functionality still relies on many existing pages under the hood, but the desktop taxonomy is now aligned around the visible `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings` roots while legacy `Research`, `Data Operations`, and `Governance` names remain aliases or historical groupings; the remaining gap is no longer basic run-browser adoption, but deeper paper/live and cockpit-level workflow integration on top of the new shared run / portfolio / ledger model.

### Desktop gaps and TBI register

This register is the canonical desktop triage list for gaps and TBIs discovered while reconciling
`src/Meridian.Wpf/` with the current planning and acceptance sources. It does not reopen the
closed W1-W4 evidence baselines; it records whether each item is a true implementation TODO, a
planned future wave, a screenshot/evidence gap, or a documentation-only reconciliation item.
Classification values are intentionally narrow:

- **True TODO:** implementation or migration work is still required before the screen/workflow can
  satisfy its stated acceptance behavior.
- **Planned future wave:** the work belongs to W5, W6, or a later productization wave and must not
  be counted as a W1-W4 blocker.
- **Screenshot/evidence gap:** behavior may be present, but the required automated tests,
  artifacts, screenshots, or acceptance packet have not been attached to the claim.
- **Documentation-only reconciliation item:** the code path is not being changed; the remaining
  work is to keep status, naming, ownership, or evidence language aligned.

Current source documents for this register:
[`../plans/current-direction-and-status.md`](../plans/current-direction-and-status.md),
[`ROADMAP.md`](ROADMAP.md),
[`../plans/desktop-ui-workflow-acceptance-matrix.md`](../plans/desktop-ui-workflow-acceptance-matrix.md),
[`workstation-cockpit-acceptance-matrix.md`](workstation-cockpit-acceptance-matrix.md), and
[`../../src/Meridian.Wpf/README.md`](../../src/Meridian.Wpf/README.md). The registry-backed screen
list in [`desktop-application-screens.md`](desktop-application-screens.md) remains the page-tag
inventory; this section is the product-gap and evidence triage layer above it.

#### Navigation/shell

| Item | Status | Affected screen/page tag | Source document | Implementation owner area | Acceptance evidence needed | Classification |
| --- | --- | --- | --- | --- | --- | --- |
| Keep legacy desktop taxonomy references from drifting back into visible-root navigation language after this inventory reconciliation. | Reconciled here; route aliases remain intentionally supported. | Shell navigation / `MainPage` / page-tag catalog | `current-direction-and-status.md`; `src/Meridian.Wpf/README.md`; this inventory's WPF section | Workstation Shell and UX | Route-registry parity evidence proving legacy names stay aliases, not visible roots, plus future doc updates that use the seven canonical roots. | Documentation-only reconciliation item |
| Keep shared workflow target parity from becoming browser-only or desktop-only as new workflow entries/actions are added. | Active regression guard; not a missing feature. | Command palette / workflow action routes / `EvidenceWorkbench` / `OperationsContinuity` / `OperationsClose` | `desktop-ui-workflow-acceptance-matrix.md`; `src/Meridian.Wpf/README.md` | Shared workflow catalog; WPF shell routing; browser route catalog | `ShellRouteRegistryTests` and browser route-catalog tests for every built-in workflow target. | Screenshot/evidence gap when parity proof is not attached to a workflow claim |
| Continue migrating modal and command-palette chrome through shared shell primitives instead of page-local styling. | In progress by surface; provider API key setup, watchlist saving, and scheduled-job editing already use shared dialog chrome. | Modal surfaces / command palette / workspace command surface | `src/Meridian.Wpf/README.md` | WPF shared controls and workspace composition | Focused WPF control/page tests with stable automation IDs for each migrated modal; screenshot only when user-visible chrome changes. | True TODO for remaining page-local modals |

#### Trading

| Item | Status | Affected screen/page tag | Source document | Implementation owner area | Acceptance evidence needed | Classification |
| --- | --- | --- | --- | --- | --- | --- |
| Preserve Lane A W2 paper-trading cockpit readiness across desktop and browser while adding maintenance changes. | Closed baseline; ongoing regression obligation. | `Trading` / `/trading/readiness` / operator inbox | `current-direction-and-status.md`; `desktop-ui-workflow-acceptance-matrix.md`; `workstation-cockpit-acceptance-matrix.md` | Trading workstation; shared readiness services; WPF Trading shell | Shared W2 readiness/operator-inbox tests, focused WPF Lane A tests, browser Trading parity tests, and pilot `TrustedData`, `PaperPromotion`, and `PaperSession` gates. | Screenshot/evidence gap if a claim lacks the current acceptance packet |
| Keep live integration read-only/paper-first until W6 explicitly opens live-readiness acceptance. | Planned; not part of W1-W4 closure. | Trading cockpit / order placement / brokerage actions | `current-direction-and-status.md`; `ROADMAP.md` | Execution and brokerage; Trading workstation | W6 live-readiness tests, fail-closed order-placement evidence, broker/session recovery proof, and explicit sign-off artifacts. | Planned future wave |
| Extend deeper paper/live data-source adoption and cockpit-level integration on top of the shared run/portfolio/ledger model. | Open product gap. | Trading terminal / positions / fills / paper-live review | `ROADMAP.md`; `current-direction-and-status.md` | Trading workstation; provider integration; shared UI services | Shared endpoint/read-model tests, WPF dense-table/inspector tests, browser parity checks, and pilot-readiness stage preservation. | True TODO |

#### Portfolio

| Item | Status | Affected screen/page tag | Source document | Implementation owner area | Acceptance evidence needed | Classification |
| --- | --- | --- | --- | --- | --- | --- |
| Preserve Lane B W3 run -> portfolio -> ledger continuity after desktop portfolio changes. | Closed baseline; ongoing regression obligation. | `Portfolio` / account portfolio / aggregate portfolio / selected run context | `current-direction-and-status.md`; `desktop-ui-workflow-acceptance-matrix.md` | Portfolio read services; WPF Portfolio view models; shared continuity services | `StrategyRunPortfolioViewModelTests`, `AccountPortfolioViewModelTests`, `AggregatePortfolioViewModelTests`, shared continuity tests, browser Portfolio parity, and pilot `PortfolioLedgerReview` evidence. | Screenshot/evidence gap if the desktop claim lacks parity proof |
| Add richer portfolio analytics beyond the shared baseline without forking shared read-model semantics. | Open product gap. | Portfolio cockpit / positions / account scope / run comparison | `ROADMAP.md`; `current-direction-and-status.md` | Portfolio services; shared UI services; WPF Portfolio | New shared DTO/service coverage first, then WPF and browser consumer tests showing identical blocker and selected-run semantics. | True TODO |

#### Accounting

| Item | Status | Affected screen/page tag | Source document | Implementation owner area | Acceptance evidence needed | Classification |
| --- | --- | --- | --- | --- | --- | --- |
| Preserve Lane C W4 reconciliation casework, close posture, and governed accounting evidence. | Closed baseline; ongoing regression obligation. | `Accounting` / Fund Ledger / Fund Operations / reconciliation cases | `current-direction-and-status.md`; `desktop-ui-workflow-acceptance-matrix.md`; `workstation-cockpit-acceptance-matrix.md`; `src/Meridian.Wpf/README.md` | Accounting/Fund Ops services; WPF Fund Ledger; operations continuity | WPF `Category=W4Acceptance`, `FundOpsCloseLaneScenarioTests`, shared operations-continuity/report-pack tests, browser W4 parity, and pilot `Reconciliation` / `GovernedReportPack` gates. | Screenshot/evidence gap if acceptance artifacts are not attached |
| Deepen per-entity, per-sleeve, and per-vehicle posting fidelity and reconciliation UX. | Open product gap. | Fund Ledger / Fund Accounts / reconciliation workbench | `ROADMAP.md`; `current-direction-and-status.md` | Ledger services; reconciliation services; WPF Accounting | Shared ledger/reconciliation contract tests, durable case/audit evidence, WPF queue/detail tests, and browser parity for blocker/recovery semantics. | True TODO |

#### Reporting

| Item | Status | Affected screen/page tag | Source document | Implementation owner area | Acceptance evidence needed | Classification |
| --- | --- | --- | --- | --- | --- | --- |
| Keep report-pack readiness, publication, restatement, and Evidence Workbench routing aligned between browser and desktop. | Closed W4 support baseline; active parity guard. | `Reporting` / report packs / `EvidenceWorkbench` / Fund Audit Trail | `current-direction-and-status.md`; `desktop-ui-workflow-acceptance-matrix.md`; `workstation-cockpit-acceptance-matrix.md`; `src/Meridian.Wpf/README.md` | Reporting services; evidence services; WPF reporting/audit trail | Report-pack endpoint tests, browser W4 parity, WPF W4 acceptance tests, route-registry parity, and pilot `GovernedReportPack` evidence. | Screenshot/evidence gap if route/evidence proof is missing |
| Expand governed report-pack UX beyond readiness/approval evidence into broader studio workflows. | Planned after current W4 baseline; productization remains broader than desktop proof. | Reporting cockpit / report pack task panels / evidence vault | `ROADMAP.md`; `current-direction-and-status.md` | Reporting product services; browser and WPF consumers | New shared workflow DTOs, retained manifest/evidence links, publication/restate recovery tests, and desktop/browser parity evidence. | Planned future wave |

#### Strategy

| Item | Status | Affected screen/page tag | Source document | Implementation owner area | Acceptance evidence needed | Classification |
| --- | --- | --- | --- | --- | --- | --- |
| Plan and implement W5 Backtest Studio unification without pulling live-readiness scope forward. | Planned next wave. | `Strategy` / Backtest Studio / Strategy Runs / run comparison | `current-direction-and-status.md`; `ROADMAP.md`; `desktop-ui-workflow-acceptance-matrix.md` | Backtesting; Strategy services; WPF Strategy workbench; browser Strategy | W5 blueprint/checklist, canonical result normalization tests, run-diff/comparison tests, WPF Strategy Runs tests, browser parity, and pilot-stage preservation. | Planned future wave |
| Complete strategy-aware launch/preflight and full Backtest Studio orchestration. | Open implementation gap. | Strategy Designer / Strategy Runs / Backtest Studio | `desktop-ui-workflow-acceptance-matrix.md`; `ROADMAP.md` | Strategy Engine; backtesting orchestration; shared UI services | Shared launch/preflight contracts, orchestration tests, WPF workbench tests, browser Strategy tests, and artifact completeness evidence. | True TODO |

#### Data

| Item | Status | Affected screen/page tag | Source document | Implementation owner area | Acceptance evidence needed | Classification |
| --- | --- | --- | --- | --- | --- | --- |
| Preserve Data terminal provider, backfill, storage, and data-quality queue behavior while using shared dense-table and decision-queue controls. | Partially implemented; ongoing surface migration and regression obligation. | `Data` / Data Quality / Backfill / provider queues / storage queues | `src/Meridian.Wpf/README.md`; `desktop-ui-workflow-acceptance-matrix.md` | Data workstation; provider/backfill services; WPF shared controls | Focused WPF view-model/control tests for selected-row drilldown, queue empty/loading/error states, and shared read-model parity for provider/backfill decisions. | True TODO for remaining unmigrated surfaces |
| Close richer provider provenance and partial-response threshold UX for options-chain/provider failover. | Open product gap. | Data providers / options chain / provider health | `ROADMAP.md`; `current-direction-and-status.md` | Provider SDK; data services; WPF/Data UI | Provider-health gating tests, failover provenance DTO evidence, policy-threshold tests, and desktop/browser presentation parity. | True TODO |

#### Settings

| Item | Status | Affected screen/page tag | Source document | Implementation owner area | Acceptance evidence needed | Classification |
| --- | --- | --- | --- | --- | --- | --- |
| Keep runtime desktop capability toggles and Settings/Admin readiness panels aligned with shared state-panel semantics. | Partially implemented; ongoing regression obligation. | `Settings` / Admin / feature capabilities / schedule and cleanup readiness | `src/Meridian.Wpf/README.md`; `current-direction-and-status.md` | Settings/Admin; WPF shell services; shared controls | Focused WPF Settings/Admin tests, capability-gate tests, and evidence that blockers/confirmation posture use `WorkspaceTone` consistently. | Screenshot/evidence gap if a Settings claim lacks UI proof |
| Keep provider setup and credentials flows in shared dialog chrome without creating desktop-only credential behavior. | Partially implemented; additional provider/setup dialogs may remain. | Settings provider setup / API key setup / credential dialogs | `src/Meridian.Wpf/README.md`; `ROADMAP.md` | Provider management; Settings UI; credential services | Dialog automation-ID tests, secure credential handling tests, provider validation evidence, and screenshot only for visible workflow changes. | True TODO for remaining page-local setup dialogs |

#### Cross-workspace/shared services

| Item | Status | Affected screen/page tag | Source document | Implementation owner area | Acceptance evidence needed | Classification |
| --- | --- | --- | --- | --- | --- | --- |
| Keep shared services/read models as the source of business behavior before WPF or browser composition. | Active architecture rule. | All workspace tags | `current-direction-and-status.md`; `desktop-ui-workflow-acceptance-matrix.md`; `src/Meridian.Wpf/README.md` | `src/Meridian.Ui.Services`; `src/Meridian.Ui.Shared`; WPF/browser consumers | Shared contract tests first, then WPF and browser parity tests tied to the affected scenario. | Documentation-only reconciliation item when a status claim needs ownership clarification |
| Preserve W1-W4 as closed evidence baselines while classifying new work as maintenance, hardening, W5, or W6. | Active planning TODO. | W2/W3/W4 acceptance lanes; all desktop workspaces | `current-direction-and-status.md`; `ROADMAP.md`; `desktop-ui-workflow-acceptance-matrix.md` | Program planning; workflow owners | Matching pilot-readiness stage posture, lane-specific tests, and status docs that separate support evidence from exit claims. | Documentation-only reconciliation item |
| Extract remaining business logic from page code-behind into view models per the WPF MVVM migration. | In progress. | Remaining WPF pages using code-behind business logic | `src/Meridian.Wpf/README.md`; `current-direction-and-status.md` | WPF MVVM owners; workspace page owners | Focused view-model tests and no page-local business-rule forks from shared services. | True TODO |

#### Evidence/screenshots/tests

| Item | Status | Affected screen/page tag | Source document | Implementation owner area | Acceptance evidence needed | Classification |
| --- | --- | --- | --- | --- | --- | --- |
| Attach the correct acceptance packet before claiming desktop workflow completion. | Active release gate. | Lane A/B/C desktop scenarios | `current-direction-and-status.md`; `desktop-ui-workflow-acceptance-matrix.md` | Workstation Shell and UX; CI/evidence owners | Lane-specific shared tests, focused WPF tests, browser parity checks, and pilot-readiness artifacts named in the matrix. | Screenshot/evidence gap |
| Run and preserve cockpit/governance matrix validation before claiming cockpit/governance evidence. | Active release gate. | Workstation cockpit/governance routes and APIs | `workstation-cockpit-acceptance-matrix.md` | Browser workstation; shared endpoint owners; evidence owners | `python3 scripts/dev/validate_workstation_cockpit_acceptance_matrix.py` plus the criterion-specific tests and artifact pointers listed by the matrix. | Screenshot/evidence gap |
| Re-run structured roadmap/source stale-doc and hash checks after docs or source changes, then reconcile generated evidence only when intentionally reviewed. | Open planning TODO. | Docs/source inventories and source README evidence | `current-direction-and-status.md`; `src/Meridian.Wpf/README.md` | Docs automation; source README owners | Stale-doc/hash validation output, updated source README/registry only when behavior or ownership changed, and clear final-result notes. | Documentation-only reconciliation item |

### WPF MVVM progress

| Area | Status | Notes |
| ------ | -------- | ------- |
| `DashboardViewModel` | ✅ | Extracted from `DashboardPage` code-behind; `BindableBase`, bindable properties, timer management |
| Remaining pages | 🔄 | Other pages still use code-behind for business logic; ViewModel extraction ongoing per ADR-017 |

---

## 11. CLI

| Feature | Status | Notes |
| --------- | -------- | ------- |
| Real-time collection | ✅ | `--symbols`, `--no-trades`, `--no-depth`, `--depth-levels` |
| Backfill | ✅ | `--backfill`, `--backfill-provider`, `--backfill-symbols`, `--backfill-from/to` |
| Data packaging | ✅ | `--package`, `--import-package`, `--list-package`, `--validate-package` |
| Configuration wizard | ✅ | `--wizard`, `--auto-config`, `--detect-providers`, `--validate-credentials` |
| Dry-run | ✅ | `--dry-run`, `--dry-run --offline` |
| Self-test | ✅ | `--selftest` |
| Schema check | ✅ | `--check-schemas`, `--validate-schemas`, `--strict-schemas` |
| Configuration watch | ✅ | `--watch-config` |
| Contextual help | ✅ | `--help <topic>` for 7 topics |
| Symbol management | ✅ | `--symbols-add`, `--symbols-remove`, `--symbol-status` |
| Query | ✅ | `--query` for stored data |
| Event replay | ✅ | `--replay` |
| Generate loader | ✅ | `--generate-loader` |
| Progress reporting | ✅ | `ProgressDisplayService`; progress bars, spinners, checklists, tables |
| Error codes reference | ✅ | `--error-codes` |

---

## 12. Observability & Operations

| Feature | Status | Notes |
| --------- | -------- | ------- |
| Prometheus metrics export | ✅ | `/api/metrics`; event throughput, provider health, backpressure, error rates |
| OpenTelemetry pipeline instrumentation | ✅ | `TracedEventMetrics` decorator; `Meridian.Pipeline` meter |
| Activity spans (batch consume, backfill, WAL recovery) | ✅ | `MarketDataTracing` extension methods |
| End-to-end trace context propagation | Complete | Collector ingress creates/preserves `Activity` context and `EventPipeline` carries it through queueing, consumption, and storage append |
| Correlation IDs in structured logs | Complete | `EventPipeline` log scopes now include correlation, trace, span, event type/source, symbol, and sequence |
| API key authentication | ✅ | `ApiKeyMiddleware`; `MDC_API_KEY` env var; constant-time comparison |
| API rate limiting | ✅ | 120 req/min sliding window; `Retry-After` header on 429 |
| Kubernetes health probes | ✅ | `/healthz`, `/readyz`, `/livez` |
| Grafana/Prometheus deployment assets | ✅ | `deploy/monitoring/` with alert rules and dashboard provisioning |
| systemd service unit | ✅ | `deploy/systemd/meridian.service` |
| Docker image | ✅ | `deploy/docker/Dockerfile` + `docker-compose.yml` |
| Daily summary webhook | ✅ | `DailySummaryWebhook`; configurable endpoint |
| Connection status webhook | ✅ | `ConnectionStatusWebhook`; provider events |
| Alert dispatcher | ✅ | `AlertDispatcher`; centralized alert publishing and subscription management |
| Alert runbook registry | ✅ | `AlertRunbookRegistry`; runbook references per alert rule |
| Health check aggregator | ✅ | `HealthCheckAggregator`; parallel health check execution with per-provider timeout |

### Remaining observability work

- **OTLP / Jaeger / Zipkin docs**: Initial operator guide now lives in `docs/development/otlp-trace-visualization.md`; extend it as more hosts auto-bind tracing configuration.

---

## 13. F# Domain & Calculations

| Module | Status | Notes |
| -------- | -------- | ------- |
| `MarketEvents.fs` — F# event types | ✅ | Discriminated union: `Trade`, `Quote`, `DepthUpdate`, `Bar`, `Heartbeat` |
| `Sides.fs` — bid/ask/neutral | ✅ | Type-safe aggressor side |
| `Integrity.fs` — sequence validation | ✅ | Gap detection, out-of-order |
| `Spread.fs` — bid-ask spread | ✅ | Absolute and relative spread calculations |
| `Imbalance.fs` — order book imbalance | ✅ | Bid/ask depth imbalance metric |
| `Aggregations.fs` — OHLCV | ✅ | Streaming bar aggregation |
| `Transforms.fs` — pipeline transforms | ✅ | Map, filter, window transforms |
| `QuoteValidator.fs` | ✅ | Price/size range validation |
| `TradeValidator.fs` | ✅ | Trade sequence and sanity validation |
| `ValidationPipeline.fs` | ✅ | Composable validation pipeline |
| C# Interop generated types | ✅ | `Meridian.FSharp.Interop.g.cs` |

---

## 14. QuantConnect Lean Integration

| Feature | Status | Notes |
| --------- | -------- | ------- |
| Custom data types | ✅ | `LeanDataTypes.cs` — `Trade`, `Quote`, `OrderBook` Lean wrappers |
| `IDataProvider` implementation | ✅ | Reads stored JSONL/Parquet files as Lean data |
| Integration page (WPF) | ✅ | `LeanIntegrationPage` wires `LeanIntegrationService` |
| `LeanIntegrationService` | ✅ | Manages Lean engine connection and data feed |

---

## 14a. MCP Server

Two MCP (Model Context Protocol) server projects provide AI-agent tooling over the Meridian platform.

| Project | Status | Notes |
| --------- | -------- | ------- |
| `Meridian.Mcp` | ✅ | Repo-tooling MCP server: `AdrTools`, `AuditTools`, `ConventionTools`, `KnownErrorTools`, `ProviderTools`; ADR/convention/template resources and code-review/test-writer prompts |

---

## 15. Execution & Brokerage

| Feature | Status | Notes |
| --------- | -------- | ------- |
| Paper trading gateway | ✅ | `PaperTradingGateway` in `Meridian.Execution`; zero-risk strategy validation |
| Order management system | ✅ | `OrderManagementSystem`, `OrderLifecycleManager`; durable execution audit entries now cover submitted, rejected, cancelled, cancel-rejected, modified, and modify-rejected order lifecycle outcomes with broker/order/symbol/scope/reason metadata for operations review |
| Risk validation framework | ✅ | `CompositeRiskValidator` with `IRiskRule` implementations |
| Position limit rule | ✅ | `PositionLimitRule`; configurable per-symbol and total position limits |
| Drawdown circuit breaker | ✅ | `DrawdownCircuitBreaker`; automatic stop on drawdown threshold |
| Order rate throttle | ✅ | `OrderRateThrottle`; configurable order frequency limits |
| **Brokerage gateway framework** | ✅ | `IBrokerageGateway`, `BaseBrokerageGateway`, `BrokerageGatewayAdapter`; shared UI services now add paper-first Alpaca connection verification and separate read-only Robinhood portfolio aggregation support, and the hosted runtime surface catalog now verifies keyed DI registration, concrete gateway type, account/portfolio/activity sync support, order-modification and partial-fill capability, supported asset classes, and explicit optional StockSharp missing-runtime notes. This remains offline runtime-surface evidence; controlled live-readiness and credentialed vendor smoke stay gated |
| **Alpaca brokerage gateway** | ✅ | `AlpacaBrokerageGateway`; fractional quantity support, client order ID mapping, and hosted runtime-surface validation for keyed gateway plus account/portfolio/activity sync registration |
| **Robinhood brokerage gateway** | ✅ | `RobinhoodBrokerageGateway`; unofficial API, equity + option order support, cancel-via-resubmit semantics, and stable `/api/execution/*` seam coverage |
| **IB brokerage gateway** | 🔑 | `IBBrokerageGateway`; conditional on IBAPI build flag, with hosted `ib` and `ibkr` keyed runtime-surface validation when registered |
| **StockSharp brokerage gateway** | 🔑 | `StockSharpBrokerageGateway`; connector-dependent, with optional hosted registration validation when the connector runtime type is present and explicit not-present evidence otherwise |
| **Template brokerage gateway** | ✅ | `TemplateBrokerageGateway`; scaffold for new adapters |
| Brokerage DI registration | ✅ | `BrokerageServiceRegistration`; `BrokerageConfiguration` options |
| Execution SDK | ✅ | `Meridian.Execution.Sdk`; `IExecutionGateway`, `IOrderManager`, `IPositionTracker` |
| Paper trading portfolio | ✅ | `PaperTradingPortfolio`; simulated position and cash tracking |
| CppTrader order gateway | ✅ | `CppTraderOrderGateway`; native C++ matching engine integration |
| CppTrader live feed adapter | ✅ | `CppTraderLiveFeedAdapter`; real-time data from CppTrader host |

### Remaining execution work

- Wire brokerage gateways into the shared workstation paper-trading cockpit and active browser and WPF operator lanes, while keeping WPF compatibility checks for active desktop workstation support
- Validate brokerage adapters against credentialed live Alpaca, Interactive Brokers, and StockSharp vendor/API sessions
- Complete cockpit-visible `Backtest → Paper → Live` workflow hardening and audit UX
- Complete paper-trading session persistence and replay operator flows

---

## 16. Testing

| Test Project | Test Files | Methods | Focus |
| --- | --- | --- | --- |
| `Meridian.Tests` | 329 | ~4,172 | Core: backfill, storage, pipeline, monitoring, providers, credentials, serialization, domain, integration endpoints, execution |
| `Meridian.FSharp.Tests` | 12 | ~233 | F# domain validation, calculations, transforms, trading transitions, ledger, risk, direct lending interop |
| `Meridian.Ui.Tests` | 55 | ~975 | UI services (API client, backfill, fixtures, forms, health, watchlist) |
| `Meridian.Wpf.Tests` | 84 | ~656 | WPF desktop services (navigation, config, status, connection, ViewModels) |
| `Meridian.Backtesting.Tests` | 19 | ~243 | Backtest engine, fill models, portfolio simulation, XIRR |
| `Meridian.DirectLending.Tests` | 5 | ~29 | Direct lending services, workflows, PostgreSQL integration |
| `Meridian.FundStructure.Tests` | 3 | ~24 | Governance shared-data access and in-memory fund-structure services |
| `Meridian.QuantScript.Tests` | 8 | ~93 | Script compiler, runner, statistics engine, plot queue, portfolio builder |
| **Total** | **515** | **~6,425** | |

### Key test infrastructure

| Feature | Status |
| --------- | -------- |
| `EndpointTestFixture` base (WebApplicationFactory) | ✅ |
| Negative-path endpoint tests (40+) | ✅ |
| Response schema validation tests (15+) | ✅ |
| `FixtureMarketDataClient` integration harness | ✅ |
| `InMemoryStorageSink` for pipeline integration | ✅ |
| Provider-specific test files (18 files, all providers + streaming failover) | ✅ |
| **IB order fixture tests** (`IBOrderSampleTests`, 5 JSON fixtures) | ✅ |
| Canonicalization golden fixtures (8 curated files) | ✅ |
| Priority backfill queue tests (`PriorityBackfillQueueTests`) | ✅ |
| Rate limiter tests (`RateLimiterTests`) | ✅ |
| Streaming failover service tests (`StreamingFailoverServiceTests`) | ✅ |
| Liquidity profile tests (`LiquidityProfileTests`) | ✅ |
| SLO definition registry tests (`SloDefinitionRegistryTests`) | ✅ |
| Golden-master pipeline replay tests (`GoldenMasterPipelineReplayTests`) | ✅ |
| WAL + event pipeline tests (`WalEventPipelineTests`) | ✅ |
| Ingestion job tests (`IngestionJobTests`, `IngestionJobServiceTests`) | ✅ |
| Data quality unit tests (AnomalyDetector, CompletenessScoreCalculator, GapAnalyzer, SequenceErrorTracker) | ✅ |
| Drift-canary CI job | Complete |

---

## 17. Configuration Schema Validation

| Feature | Status | Notes |
| --------- | -------- | ------- |
| `SchemaValidationService` — stored data format validation | ✅ | `--validate-schemas`, `--strict-schemas`, `--check-schemas` |
| `SchemaVersionManager` | ✅ | Per-event-type schema versioning |
| JSON Schema generation from C# config models | Complete | `--generate-config-schema` produces the checked-in `config/appsettings.schema.json`; sample config references it and CI validates drift |

---

## 18. Trading Workstation Product Surfaces

This section inventories the workflow-centric product model that now sits above the older page inventory.

### Aliases and canonical workspace names

The desktop shell keeps older shell/navigation terms as compatibility aliases while the visible
workspace model remains `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and
`Settings`. Current alias resolution is registered in the shell page catalog and feature-owned
Data module; layout lookup also normalizes the legacy workspace ids so deep links and automation
land on the canonical workspace layout. The registry-backed WPF screen list is maintained in
[`docs/status/desktop-application-screens.md`](desktop-application-screens.md) for page-level
workspace, page tag, visibility, related-tag, implementation, gap, and screenshot evidence.

| Alias / legacy name | Canonical workspace or page | Relevant page tags | Where the alias is resolved |
| --- | --- | --- | --- |
| `Research` / `ResearchWorkspace` | `Strategy` | Canonical `StrategyShell`; aliases `ResearchShell`, `ResearchWorkspace`; workspace id `strategy`; keywords include `strategy` and `research`. | `src/Meridian.Wpf/Models/ShellNavigationCatalog.Research.cs` registers the Strategy shell page and aliases; `src/Meridian.Wpf/Models/ShellNavigationCatalog.Layouts.cs` maps the legacy `research` layout id to `strategy`. |
| `Governance` / `GovernanceWorkspace` | `Accounting` | Canonical `AccountingShell`; aliases `GovernanceShell`, `GovernanceWorkspace`; workspace id `accounting`; related fund-accounting tags include `FundLedger`, `FundReconciliation`, `FundTrialBalance`, and `FundAuditTrail`. | `src/Meridian.Wpf/Models/ShellNavigationCatalog.Governance.cs` registers the Accounting shell page and aliases; `src/Meridian.Wpf/Models/ShellNavigationCatalog.Layouts.cs` maps the legacy `governance` layout id to `accounting`. |
| `DataOperations` / `Data Operations` | `Data` | Canonical `DataShell`; aliases `DataOperationsShell`, `DataOperationsWorkspace`; workspace id `data`; keywords include `data` and `data operations`. | `src/Meridian.Wpf/Features/Data/DataFeatureModule.cs` registers the Data shell page and aliases; `src/Meridian.Wpf/Models/ShellNavigationCatalog.Layouts.cs` maps `data-operations` and `data operations` layout ids to `data`. |
| `OperationsContinuity` / `OperationsClose` | Fund operations | Canonical `FundLedger`; aliases `FundOperations`, `OperationsContinuity`, `OperationsClose`; workspace id `accounting`; title `Fund operations`. | `src/Meridian.Wpf/Models/ShellNavigationCatalog.Governance.cs` registers the Fund operations page and close/continuity aliases. |
| `EvidenceWorkbench` | Fund audit trail | Canonical `FundAuditTrail`; alias `EvidenceWorkbench`; workspace id `accounting`; title `Fund audit trail`. | `src/Meridian.Wpf/Models/ShellNavigationCatalog.Governance.cs` registers the Fund audit trail page and Evidence Workbench alias. |

| Surface | Status | Notes |
| --------- | -------- | ------- |
| Research workspace taxonomy | Partial | The active browser and WPF workstations now has a Research run library with retained-run review, two-run compare/diff readiness, promotion-history loading, command-error alerts, and refreshed built workstation assets; desktop vocabulary also aligns on `Research`, and the WPF Research shell has a desk briefing hero for selected-run, run-detail, portfolio, and `Backtest -> Paper` promotion-review handoffs. Deeper research workflow acceptance, strategy-aware launch/preflight, persisted sweep grouping, and Backtest Studio unification remain open |
| Trading workspace taxonomy | Partial | Command palette and shell terminology align on `Trading`, the Trading shell now keeps run-scoped versus account-scoped portfolio drill-ins inside the cockpit instead of bouncing operators back to `Research`, and the desk briefing hero projects context-required, replay-mismatch, controls-blocked, paper-review, and live-oversight handoffs from shared readiness inputs; the shared trading-readiness endpoint gives the cockpit one acceptance contract with recent risk/control audit evidence and missing-field explainability warnings, while cockpit-grade execution UX remains pending |
| Data Operations workspace taxonomy | Partial | Operational pages are grouped consistently, the retained Data shell now has feature-owned registration, snapshot, presentation, and view-model seams plus a desk briefing hero for provider, backfill, storage, session, export, blocker, and next-handoff posture, and `StoragePage` now shows selected root/layout/compression preview scope before archive-path work; deeper workflow acceptance and cross-workspace handoff proof remain open |
| Governance workspace taxonomy | Partial | Portfolio/ledger/diagnostics/settings surfaces are grouped conceptually, Security Master/reconciliation drill-ins are live, browser Accounting reconciliation now exposes a shared dense-table detail queue with keyboard-accessible selected/expanded state, seeded no-host break-queue fixture coverage, and empty-detail guidance, Fund Accounts now has a stateful operator brief for account-queue, provider-routing, shared-data, balance-evidence snapshot posture, and reconciliation readiness states, and Notification Center can recover hidden retained notification history after aggressive filters; broader governance-first product flows remain incomplete |
| Governance fund-ops workspace API baseline | Partial | `/api/fund-structure/workspace-view`, `/api/fund-structure/report-pack-preview`, and `/api/fund-structure/report-packs` now aggregate fund-account state, banking, ledger, reconciliation, NAV attribution, reporting previews, and local-first governed report-pack artifacts for a `fundProfileId`; reconciliation break queue items now carry exception route, tolerance profile/band, required sign-off role, and sign-off status metadata, and `/api/workstation/reconciliation/calibration-summary` aggregates those breaks into Ready/ReviewRequired/Blocked tolerance-profile posture. `/api/workstation/operations/continuity*` adds an account-period close-lane contract for broker intake, Security Master resolution/override approval, ledger draft/validate/post, reconciliation, approval, close, reopen, and audit timeline inspection, with a browser Accounting Continuity surface now exposing workflow list/detail, gates, blockers, timeline, and next-action routing. The Governance WPF shell and Fund Accounts workbench now reuse shared account/provider evidence, while deeper browser close workflow acceptance, mutation handling, workstation-shell polish, durable generalized casework, and broader board/investor/compliance packaging remain open. Guardrail: Security Master is the sole instrument source, and governance DTOs with instrument terms must carry Security Master identity/provenance references. Trial-balance and reconciliation symbol metadata now reuse canonical `WorkstationSecurityReference` records (same layer already used by run portfolio/ledger surfaces) rather than a separate classification-only projection. |
| Shared `StrategyRun` DTO/read-model baseline | Partial | Shared run summary/detail/comparison models and stable route-aware review-packet work items exist; paper/live history expansion remains |
| Shared portfolio read-model baseline | Partial | Portfolio summaries/positions derived from recorded runs exist; equity-history and broader source coverage remain |
| Shared ledger read-model baseline | Partial | Ledger summaries, journal rows, and trial balance rows exist; account-summary and richer reconciliation UX remain |
| Reconciliation run baseline | Partial | Run-scoped reconciliation service, history, Security Master coverage issue detection, and a file-backed reconciliation break queue now exist. The queue seeds run-scoped breaks and supports review, resolve/dismiss, audit-history, and calibration-summary routes with profile rollups for tolerance/sign-off posture; the browser Accounting surface now projects run selection through shared dense rows, keyboard-accessible selected/expanded row state, open-break copy, and no-run detail guidance from a view model. Broader non-run, external-statement/custodian, SLA, and operator-approved calibrated exception workflows remain. |
| Security Master platform baseline | Complete | The current Security Master mechanics are delivered and workstation productization is live: hardened WPF activation, search/runtime recovery, canonical `WorkstationSecurityReference` coverage/provenance, shared research/trading/governance/portfolio/ledger propagation, browser search-result selection, identity drill-ins, details/lots tracking, server-side operator overrides, and UFL/reference-data projection support across bonds, options, equities, futures, FX spot, swaps, commodities, crypto, deposits, money-market funds, and certificates of deposit |
| Security Master — UFL reference-data projections | ✅ | Asset-class-specific projection services, DTOs, Postgres stores, migrations, reference-data endpoints, and tests now exist for bonds, options, equities, futures, FX spot, swaps, commodities, crypto, deposits, money-market funds, and certificates of deposit. Full instrument passport/confidence-score workflows remain planned commercial modules. |
| Security Master — custom asset profile promotion candidates | ⚠️ | Shared governance support can score approved custom asset profiles, return promotion readiness, recommended first-class package ids, and explanatory signals through `/api/security-master/asset-profiles/promotion-candidates`. This is assessment support only; operator promotion workflow, indexed profile-field projection lineage, and actual UFL package graduation remain open. |
| Security Master — browser details and overrides | ⚠️ | Browser Security Master now includes selectable search-result rows that open identity drill-ins and keep corporate-action/trading-parameter/detail panels tied to the selected security, an extended details panel, local lots tracker with selectable dense rows and lot-detail state, server-side operator override GET/PATCH support, and a view-model-owned conflict refresh/retry command for recoverable identifier-conflict loads. Pending operator overrides now seed durable Security Master steward casework in the shared break queue when that repository is registered. Governed override approval UX and broader acceptance remain open. |
| Security Master — bond term richness | ✅ | Extended `SecurityEconomicDefinition` with coupon rate, maturity, day-count convention, seniority, callable flag, and issue price |
| Security Master — trading parameters | ✅ | Per-instrument lot size, tick size; `PaperTradingGateway` lot-size validation and `BacktestEngine` tick-size rounding wired; `GET /api/security-master/{id}/trading-parameters` |
| Security Master — corporate action events | ✅ | `Dividend`, `StockSplit`, `SpinOff`, `MergerAbsorption` domain events; `CorporateActionAdjustmentService` applies split-adjusted bar prices in backtest replay and exposes per-bar adjustment so large mixed replay streams do not buffer event windows; `GET /api/security-master/{id}/corporate-actions` |
| Security Master — exchange bulk ingest | ✅ | CSV + JSON bulk-ingest via `SecurityMasterImportService`; idempotent dedup; CLI `--security-master-ingest`; `POST /api/security-master/import`; typed `GET /api/security-master/ingest/status` polling surface |
| Security Master — EDGAR ingest provider | ✅ | `EdgarSecurityMasterIngestProvider`; SEC company-ticker and submission enrichment flow with provenance capture and SEC rate-limit-aware ingest behavior |
| Security Master — golden record conflict resolution | ✅ | `SecurityMasterConflictService` detects ingest-time identifier conflicts automatically; `GET /api/security-master/conflicts` list + `POST /api/security-master/conflicts/{id}/resolve`; workstation conflict queue and operator resolution path are live. Open conflicts now seed durable Security Master steward casework in the shared break queue, and resolved/dismissed conflicts transition the related case through review and sign-off state when casework storage is registered. |
| Security Master — WPF browser | ✅ | `SecurityMasterPage` + `SecurityMasterViewModel` (BindableBase); search, runtime-unavailable recovery, bound `Clear Search`, results/detail/inspector workbench, ingest polling, conflict queue, corporate action timeline, trading params, import/backfill posture, and governance drill-ins |
| Direct lending vertical slice | Partial | Postgres-backed direct-lending services, migrations, workflow support, and `/api/loans/*` endpoints are live; broader governance/reporting integration remains |
| WPF run browser/detail/portfolio/ledger/cash-flow surfaces | In progress | Code present in `src/Meridian.Wpf/`; StrategyRuns now has visible-versus-recorded run scope, filter-aware empty-state recovery, and comparison-picker guidance, BatchBacktest has stateful results empty guidance for idle, validation-blocked, running, failed, cancelled, and populated sweep states, and RunCashFlow now distinguishes selected-run, missing-run, no-event, and loaded cash-flow evidence states while broader paper/live history continuity remains open |
| Backtest Studio unification | Partial | Native Backtest Studio output and QuantConnect Lean result ingestion now normalize through the shared SDK `BacktestResult` model with engine metadata, external run id, result coverage kind, and warnings. Native runs are marked full coverage; Lean imports are summary-only until fill, cash-flow, attribution, and ledger artifacts are imported. Unified launch/preflight, cancellation, and operator lifecycle UX remain open |
| Paper-trading cockpit | Partial | The Wave 2 cockpit baseline is accepted as of 2026-05-27 and should be preserved through regression evidence rather than reopened by support-only changes. Trading workspace surfaces now cover positions, orders, selectable Recent Fills detail, replay, sessions, promotion flows, replay-audit metadata with stale-coverage detection, in-shell portfolio/accounting drill-ins, Position Blotter grouped selection review/action-readiness evidence, the WPF desk briefing hero, a shared `/api/workstation/trading/readiness` contract for session/replay/control/promotion/signed DK1 trust-gate/brokerage/work-item posture with stable work-item IDs and explicit UTC as-of labels in the browser summary, an initial `/api/workstation/operator/inbox` aggregation contract for readiness work items plus open reconciliation breaks, route-aware WPF shell queue-button consumption of the primary work item with active-account `fundAccountId` propagation, browser market-context support through live quotes/order book, recent-trade detail inspection, watchlists, click-to-trade staging, and historical charts, and durable execution audit coverage for submitted/rejected/cancelled/modified order lifecycle outcomes. Future hardening should keep the W2 evidence pack green while W4 close/report and broader broker/live validation remain open. |
| Promotion workflow (`Backtest -> Paper -> Live`) | Partial | Endpoint layer and dashboard flows exist, promotion approvals now carry an explicit approval checklist for DK1 trust packet, lineage, portfolio/ledger continuity, and risk-control review, and the browser promotion gate suppresses stale run evaluation/decision responses before enabling paper promotion; safety-gated lifecycle hardening, broader operator acceptance, and full live-readiness remain open |

### Additional governance and platform tracks

- **Cash-flow modeling surfaces:** RunCashFlow now has drill-in empty-state guidance for retained run evidence, but broader governance-oriented cash-movement and projection views are not yet productized.
- **Multi-ledger tracking:** governance workflows do not yet expose multiple ledgers, ledger groups, or cross-ledger consolidation explicitly.
- **Reconciliation engine expansion:** run-scoped reconciliation now exists for recorded strategy runs, and provider-ledger reconciliation now compares provider position marks against retained internal unrealized P&L plus explicit provider fill realized P&L when the account balance snapshot and provider activity feed carry those values; broader position, cash, NAV, external statement, and exception-queue workflows remain incomplete.
- **Fund-account operator briefing:** WPF now projects fund context, account queues, provider route previews, blocked routes, shared-data access gaps, retained balance-evidence snapshot posture, and ready-for-reconciliation state from loaded account/provider/balance-history evidence; durable account/entity casework and external-account acceptance remain Wave 4 work.
- **Governance architecture review check:** flag governance-local instrument definitions unless they are adapter-only intermediates with explicit mapping to Security Master IDs/provenance before downstream DTO/service exposure.
- **Reviewer search guidance:** for governance DTO/service diffs, search for instrument terms (`Symbol`, `Cusip`, `Isin`, `Coupon`, `Maturity`, `Issuer`, `Venue`, `AssetClass`) and confirm paired Security Master reference/provenance fields.
- **Report generation tools:** export infrastructure exists and fund-scoped report-pack APIs now deliver the first governed artifact slice: manifest/provenance JSON, trial-balance and asset-class JSON/CSV files, XLSX workbook output, checksums, history, and detail retrieval. Full investor, board, compliance, and expanded fund-ops templates remain future work.

### Remaining work

- Validate the delivered workspace shells, command routing, and quick actions against high-traffic operator journeys instead of expanding shell taxonomy for its own sake.
- Extend the shared run/portfolio/ledger model to paper/live history, cash-flow views, multi-ledger tracking, richer reconciliation views, and operator-visible operations-continuity close workflows.
- Keep the delivered Security Master baseline central while Wave 4 governance work deepens account/entity, cash-flow, multi-ledger, reconciliation, and reporting workflows on top of it.
- Treat [`archive/docs/plans/security-master-productization-roadmap.md`](../../archive/docs/plans/security-master-productization-roadmap.md) as the delivered-baseline reference for Security Master mechanics; canonical wave status remains in [`PROGRAM_STATE.md`](PROGRAM_STATE.md) and [`ROADMAP.md`](ROADMAP.md).
- Expand the current reconciliation seam from the delivered run-scoped break queue into richer match rules, calibrated exception workflows, external-statement/custodian coverage, and non-run governance use cases.
- Extend the direct-lending slice into governance-grade projections, reconciliation hooks, and reporting outputs.
- Expand report generation tools beyond the delivered trial-balance artifact slice into cash-flow, reconciliation-detail, portfolio, board, investor, compliance, and publication workflows.
- Keep replacing page-by-page mental models with workstation-native journeys for research, trading, data ops, and governance, using the delivered shell baseline as the starting point.

---

## 19. Flagship Planned Capabilities

These areas are part of the documented implementation scope even though they are not yet productized in the current repo state.

| Capability | Status | Notes |
| ------------ | -------- | ------- |
| QuantScript library/project | ✅ | `src/Meridian.QuantScript` — Roslyn scripting API, PriceSeries/ReturnSeries domain types, StatisticsEngine, BacktestProxy, QuantDataContext, PlotQueue |
| QuantScript WPF editor/surface | ✅ | `QuantScriptPage.xaml` + `QuantScriptViewModel` — AvalonEdit editor, three-column layout, Console/Charts/Metrics/Trades/Diagnostics/Run History tabs, ScottPlot charting, local execution-history evidence, and Research handoffs for mirrored runs |
| QuantScript browser Quant Lab surface | ⚠️ | `/strategy/quant-lab` and `/api/quant/run`, `/api/quant/parameters`, and `/api/quant/templates` now provide a gated browser execution path with parameter side-panel support, Security Master wiring, and plot rendering including heatmap/candlestick evidence. Plot geometry, ticks, unsupported states, labels, and reusable helpers now live in a view-model module for focused coverage, and Quant Notebook helpers can execute cells through the same Quant run path and fetch historical bars through shared data APIs; broader strategy-aware launch/preflight, persisted sweep grouping, and Backtest Studio unification remain open |
| Strategy Engine pre-run foundation | ⚠️ | `src/Meridian.Contracts/StrategyEngine`, `StrategyEngineRegistry`, `StrategyEngineValidationService`, and `/api/workstation/strategy/engine/definitions` plus `/api/workstation/strategy/engine/validate-run` provide shared definitions, data dependencies, parameter validation, evidence hashing, and browser workstation validation for Covered Call and visual designer flows. This is execution-control support evidence; durable strategy passport approval, persisted sweep grouping, full Backtest Studio unification, and operator acceptance remain open. |
| QuantScript tests/sample scripts/docs | ✅ | `tests/Meridian.QuantScript.Tests` (compiler, runner, stats, plot-queue); `scripts/example-sharpe.csx` sample script |
| L3 reconstruction timeline | Partial | The existing `--simulate-execution` path orders stored historical records by timestamp and source file for each symbol and feeds the baseline inference artifact pipeline. Stable file-offset tie-breakers, cross-symbol merged reconstruction, integrity annotations, and calibration-window reconstruction remain open. |
| L3 inference model | Partial | Baseline inferred queue state now derives displayed size from L2/depth payloads, depletes estimated queue-ahead from trade quantity, emits confidence score/grade and inference reason per event, and marks low-data periods with summary warnings. Probabilistic calibration, residual-error scoring, confidence thresholds, and venue/regime profiles remain open. |
| Queue-aware execution simulator | Partial | Bar-midpoint fills support per-bar participation caps, market-impact fills now apply the same `BacktestRequest.MaxParticipationRate` cap while preserving full-fill behavior at `0`, order-book fills support bounded queue-ahead depth, and simulation artifacts now include `isInferred`, inferred queue-ahead, confidence, fill reason, and queue diagnostics. Latency simulation, order-intent files, cancel/replace, parquet export, and calibrated L3 fills remain open. |
| Simulation CLI workflow | ✅ | `--simulate-execution` now dispatches through dedicated `SimulationCommands`, supports dry-run/date/time-window/output options, and emits deterministic artifacts (`fill-tape.jsonl`, `order-lifecycle.jsonl`, `summary.json`, and `queue-diagnostics.jsonl`) with inferred labels, confidence grade, fill rate, avg slippage bps, warnings, displayed size, trade quantity, estimated queue-ahead, and inference reason. |
| Simulation WPF explorer | ðŸ“ | Dedicated simulation page and progress/results UX remain planned |

### Remaining work

- Convert both blueprints into real projects, contracts, tests, docs, and operator-facing entry points.
- Ensure these capabilities land on top of shared workstation models rather than as isolated feature islands.

---

## Summary: Remaining Work to Full Implementation

### High priority (blocking full provider coverage)

| ID | Area | Effort | Description |
| ---- | ------ | -------- | ------------- |
| ✅ | Polygon validation | Medium | Recorded-session replay fixture validates trade, quote, and aggregate parsing without live network access |

### Medium priority (observability & developer experience)

| ID | Area | Effort | Description |
| ---- | ------ | -------- | ------------- |
| ✅ | OTLP trace visualization docs | Low | `docs/development/otlp-trace-visualization.md` documents collector/export wiring and local Jaeger flow |

### Low priority (architecture debt)

| ID | Area | Effort | Description |
| ---- | ------ | -------- | ------------- |
| H2 | Multi-instance coordination | High | Distributed locking for symbol subscriptions across multiple collector instances |
| — | WPF ViewModel extraction | Medium | Extract remaining page code-behind logic into `BindableBase` ViewModels (ADR-017) |
| — | DailySummaryWebhook state | Low | Persist `_dailyHistory` to disk using `MetadataTagService` save pattern |
| — | StockSharp connector expansion | Low | Extend connector examples/validation coverage beyond the currently documented baseline |
| — | IB vendor-DLL validation | Low | Keep the scripted setup and smoke-build path aligned with the official IB API release surface |

---

## Target End Product Snapshot

Meridian’s intended end state is a comprehensive fund management platform rather than a loose collection of pages and utilities.

- `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings` should operate as durable top-level operator surfaces in the active web workstation, with legacy `Research`, `Data Operations`, and `Governance` names retained only where compatibility or transitional routing still requires them.
- Backtests, paper sessions, and live-facing history should share one recognizable run model with first-class portfolio and ledger drill-ins.
- Account, entity, strategy-implementation, and trade-management workflows should be part of the same connected product surface.
- Security Master now serves as the authoritative instrument-definition layer across research, trading, governance, portfolio, and ledger workflows; the current repo already delivers that baseline.
- Governance should expose cash-flow modeling, trial-balance analysis, and multi-ledger tracking as first-class capabilities.
- Governance should include a reconciliation engine comparable to fund-operations tooling, plus report generation tools for audit, investor, and compliance outputs.
- Provider, replay, storage, diagnostics, and observability capabilities should support that operator workflow end to end.
- Optional scale-out and assembly-level optimization work can deepen the platform, but they are not required for the non-assembly product baseline to feel complete.

---

## How to Read This Document

- **✅ Complete**: No action required; tested and in production code paths.
- **⚠️ Partial**: Works with caveats; see "Remaining Work" column.
- **🔑 Credentials/build flag required**: Implementation is complete but requires external setup (credentials, IBAPI download, StockSharp license).
- **🔄 Framework in place**: Core structure exists; specific sub-feature is incomplete (for example, the workstation taxonomy is in place but deeper workspace-native shells and operator flows still remain).
- **📝 Planned**: Not started; see ROADMAP.md wave schedule.

---

_Last Updated: 2026-05-29_
