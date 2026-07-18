# Data Provider & Accounting Code Brainstorm — Grounded Improvement Lanes (2026-07)

> **Mode:** Domain-Focused — the request asks for concrete, highly valuable improvements to two
> specific subsystems (data providers and accounting), so this session goes deep on code-level
> findings rather than broad product exploration.
>
> **Grounding:** fresh code exploration of `src/Meridian.ProviderSdk/`,
> `src/Meridian.Infrastructure/Adapters/`, `src/Meridian.Application/Backfill/`,
> `src/Meridian.DataIntegration/Monitoring/DataQuality/`, `src/Meridian.Ledger/`,
> `src/Meridian.FSharp.Ledger/`, `src/Meridian.FinancialOperations/`, and the endpoint/UI surfaces
> that consume them, plus `.claude/skills/_shared/project-context.md` and the competitive-landscape
> reference.
>
> **Continuity:** prior sessions covered provider *lifecycle management* (setup wizard, hot-reload,
> credential vault — 2026-06-25) and market-researched product lanes (TCA, statement connectors,
> reconciliation agent, DuckDB, Python client — 2026-07-01,
> `docs/product/high-value-code-brainstorm-2026-07.md`). This session deliberately targets the
> layer underneath those lanes: structural correctness, consistency, and integration gaps found in
> the code itself. Every idea below anchors to a specific verified finding.

---

## Status Update (2026-07-15)

The implementation pass for ideas #1–#10 is complete as of 2026-07-15. The narratives below remain
the point-in-time analysis from 2026-07-05; dated notes record how each premise changed. Independent
correctness, durability, isolation, provenance, performance, and shutdown audits tightened the
completion criteria, and the resulting #6, #7, and #10 hardening is now closed. Focused local
validation covers the shared .NET graph, execution durability, accounting/close, WPF, browser, and
IB API surfaces. GitHub Actions remains the merge authority and is pending for the final branch.

| # | Idea | Status | Completion evidence / remaining validation |
|---|------|--------|--------------|
| 1 | Streaming unification + honest status | Complete (2026-07-15); focused local validation passed | Provider diagnostics now live at the ProviderSdk contract boundary; NYSE, Robinhood polling, live IB, and direct IB simulation paths report supervised state honestly. Subscription replay, bounded heartbeat teardown, explicit caller cancellation, and `unknown`/`unavailable` endpoint behavior have focused coverage. GitHub Actions is pending. |
| 2 | Canonical symbol spine | Complete (2026-07-15); focused local validation passed | `SecurityId` is the durable registry identity across normalized and provider-scoped aliases. Comparison/canonical modes, production backfill resolution, atomic migration fingerprints, ownership-preserving backfill, restart no-op, malformed-input rejection, and reload coverage are present. GitHub Actions is pending. |
| 3 | Unified data quality + browser dashboard | Complete (2026-07-15); focused local validation passed | Shared stored/streaming/adapter scoring, stable gap identity, exact remediation scope, WPF composition, and the browser Data Quality region are implemented. Partial and unavailable evidence remains explicit, including accessible disabled-action reasons. Browser, .NET, and WPF surface checks passed; GitHub Actions is pending. |
| 4 | Backfill feedback loop | Complete (2026-07-15); focused local validation passed | Typed progress and bounded retained execution/SLA history flow through shared contracts to browser and WPF. Completed backfills refresh final history with stale-response protection. Browser, .NET, and WPF surface checks passed; GitHub Actions is pending. |
| 5 | Failure & rate-limit hardening | Complete (2026-07-15); focused local validation passed | Provider catalog failures remain immutable and sanitized; recursive classification preserves provider attribution and `Retry-After`. NYSE and Alpha Vantage symbol/corporate-action paths map HTTP 429 and quota payloads to typed failures, while historical and streaming rate state share the lock-guarded clock model. The IB smoke and shared Release build passed; GitHub Actions is pending. |
| 6 | Mark-to-market wiring | Complete (2026-07-15); focused local validation passed | Daily deltas, per-security lineage, all-member lifecycle, same-day corrections, atomic conditional snapshot append, greatest-as-of reads, exact-owner history, strict recorded-as-of Security Master reads, bounded provider clock skew, historical alias validity, authoritative flat-book `NoAdjustment`, and production brokerage-sync capture are present. Focused valuation/snapshot coverage passed; GitHub Actions is pending. |
| 7 | Automated journal drafts | Complete (2026-07-15); focused local validation passed | Recurring schedules, durable restart/CAS/rearm behavior, exact corporate-action currency, immutable draft identity, actual-execution-time evidence gates, exact browser/WPF scope, and server-owned ledger capital-account reconciliation are present. Reviewed readiness now requires durable governed approval or certification provenance. Focused accounting/close and WPF coverage passed; GitHub Actions is pending. |
| 8 | Closing entries + retained-earnings roll | Complete (2026-07-15); focused local validation passed | Prepare, approve, post, hard close, and governed reopen are distinct durable transitions. JIT readiness/version checks, atomic correction pairs, exact reopen convergence, source-linked reversals, strict ownership, transactional Postgres close guards, exact server-owned `HardClosed` authority, automatic `CloseLocked` transition, and retry reconciliation are present. Focused accounting/close coverage passed; GitHub Actions is pending. |
| 9 | One ledger spine | Complete (2026-07-15); focused local validation passed | Chronological as-of indexes preserve out-of-order and dimensional reads. Durable posting rejects global journal/command collisions and aggregate-scoped source/idempotency collisions, validates exact book context, and compares a canonical full-command fingerprint before treating crash retries as equivalent. Architecture/contract coverage passed; GitHub Actions is pending. |
| 10 | Fill-to-ledger durability | Complete (2026-07-15); focused local validation passed | Broker-accepted fills reach a durable accounting admission before cancelable session/audit work, remain pending until idempotent posting succeeds, and replay after restart. Primary and fallback stores bind the complete scope/aggregate/period/book/policy identity, reject ambiguous legacy state in exact mode, and drain admitted writes. Multiwriter, shutdown, malformed-fill evidence, DI, host, and restart coverage passed; GitHub Actions is pending. |

## Final Local Validation Evidence (2026-07-15)

- The final serialized Release build of `tests/Meridian.Tests/Meridian.Tests.csproj`, including its
  production dependency graph, completed with 0 errors. The execution durability/composition slice
  passed 57/57, the broader execution slice passed 51/51, OMS regressions passed 30/30, and the
  architecture/contract slice passed 37/37.
- The valuation, Security Master, position-history, and snapshot slice for #6 passed 79/79. The
  accounting/close slice for #7 and #8 passed 107/107, including the authoritative-Security-Master
  fail-closed regression. Focused WPF restore/build validation passed with 18/18 tests.
- The provider-accounting browser slice passed 14/14, the accounting/private-capital browser slice
  passed 39/39, the dashboard production build completed, and the IB API Release smoke completed
  with 0 errors.
- #10's final 57-test slice includes complete policy-identity mismatch, legacy snapshot/WAL
  compatibility, independent-store concurrency, admitted-operation drain, durable malformed-fill
  evidence, host fail-fast composition, and restart replay scenarios.
- Residual characteristics are explicit rather than hidden: #6's greatest-as-of JSONL lookup uses
  constant retained memory but remains a linear scan with copy-on-write persistence, and the
  5,000-record regression is not a throughput benchmark. #7's reviewed reconciliation confidence
  is a governed-derived 0.95 signal, not an independent 100% certification. #10's host tests prove
  configuration, DI, durable admission, and replay without claiming a live Postgres integration;
  non-cooperative dependencies can extend teardown, and a book, period, or accounting-policy
  rollover requires a new exact durable partition and host restart.

## The Two Headline Findings

**Data providers:** the subsystem is feature-complete but structurally fractured. Only Alpaca and
Polygon streaming clients use the shared `WebSocketProviderBase`; NYSE, Robinhood, and IB carry
their own reconnect logic and emit no connection diagnostics, so the UI falls back to showing
"enabled" as "connected." Symbol identity lives in three disconnected mechanisms, and one of the
three composite-provider construction sites silently omits the symbol resolver entirely.

**Accounting:** the domain is unusually complete at the component level — but split into two
parallel stacks. `src/Meridian.Ledger/` holds a rich, fully unit-tested projector library
(daily pricing, fixed-income amortization, tax-lot relief, multi-currency, partnership waterfalls,
automated fee/dividend drafts, shadow NAV) with **zero live callers**. The governed
`Meridian.FinancialOperations` path (posting rules → drafts → Postgres `ILedgerJournalStore` →
close → certified report packs) is what the Accounting workspace actually runs on, and it never
touches the projectors. Practical consequence: securities post at cost, nothing posts
mark-to-market adjustments, and NAV is effectively NAV-at-cost.

> **Update (2026-07-06):** both headline findings have narrowed — see the status table above.
> `DailyMarkToMarketService` now drives the pricing projector into governed drafts, and the three
> streaming providers named above emit connection diagnostics. The ledger-spine work is now closed;
> remaining structural fractures are provider-side symbol-registry convergence, quality-model
> unification, streaming-side rate-limit tracking, and reconnect/heartbeat/resubscribe
> consolidation.

> **Completion update (2026-07-15):** the provider fractures in #1–#5 are now closed through
> contract-level diagnostics and supervision, `SecurityId`-backed aliases, the shared quality
> contract, typed progress/history, and typed rate-limit state. The governed accounting and ledger
> foundations in #6–#9 and the durable execution handoff in #10 are also complete. The original
> headline remains the 2026-07-05 discovery baseline; the status table and final validation record
> now supersede its implementation-gap claims while preserving the historical analysis.

---

## Ideas at a Glance

| # | Idea | Effort | Audience | Impact | Depends On |
|---|------|--------|----------|--------|------------|
| 1 | Streaming provider unification + honest connection status | M | all | High | — |
| 2 | Canonical symbol-resolution spine (one identity model) | M | I, Q | High | — |
| 3 | Unified data-quality model + browser Data Quality dashboard | L | all | High | 2 (partial) |
| 4 | Backfill feedback loop: live progress, typed SLA metadata | S–M | all | Med-High | — |
| 5 | Provider failure & rate-limit hardening (kill the silent catches) | S–M | all | Med-High | — |
| 6 | Mark-to-market wiring: pricing projector → governed postings → true NAV | M | I | High | — |
| 7 | Automated journal drafts (dividends, fees, withholding) in the close cockpit | M–L | I | High | 6 |
| 8 | Period-close closing entries + retained-earnings roll | M | I | Med-High | — |
| 9 | One ledger spine: unify projector library with `ILedgerJournalStore` | L | I | High | 6, 7 |
| 10 | Fill-to-ledger durability fix in `LedgerPostingConsumer` | S | I, H | High | — |

Effort: **S** = days, **M** = 1–2 weeks, **L** = 1+ month. Audience: **H** = hobbyist quant,
**Q** = academic, **I** = institutional/fund-ops.

---

## Data Provider Ideas

### 1. Streaming Provider Unification + Honest Connection Status

`WebSocketProviderBase`'s own documentation says it "consolidates logic previously duplicated
across Alpaca, Polygon, and NYSE" — but only Alpaca (`Adapters/Alpaca/AlpacaMarketDataClient.cs`)
and Polygon (`Adapters/Polygon/PolygonMarketDataClient.cs`) actually inherit it. NYSE
(`Adapters/NYSE/NyseMarketDataClient.cs`), Robinhood, and the IB streaming clients implement
`IMarketDataClient` directly with their own reconnect, heartbeat, and resubscribe logic. The
user-visible cost is real: only base-class providers emit `IProviderConnectionDiagnosticsSource`
diagnostics, so `ProviderEndpoints.cs` (in `src/Meridian.Ui.Shared/Endpoints/`) falls back to
reporting `IsConnected` from `s.Enabled` — the Data workspace can show a provider as connected
when it has merely been configured.

The work: migrate NYSE, then Robinhood, then IB streaming onto `WebSocketProviderBase` (IB last —
its socket model differs most), and make emission of connection diagnostics a contract-level
expectation rather than a base-class side effect. The existing recorded-session and reconnect test
suites for each provider (`tests/Meridian.Tests/Infrastructure/Providers/`) become the behavioral
safety net for the migration.

The operator moment: the provider status strip in the Data workspace becomes trustworthy — a
provider chip shows *Connected / Reconnecting (attempt 3) / Degraded / Disabled* uniformly for
every provider, with last-heartbeat and reconnect-count on hover, because every provider now
reports through the same diagnostics channel. "Enabled but silently dead" stops being a state the
UI can't distinguish.

Tradeoffs: reconnect semantics are the riskiest thing to change per provider — each migration
needs its recorded-session replay tests run before/after, and IB's simulation client
(`IBSimulationClient.cs`) needs to stay behaviorally identical. Do one provider per PR, never a
big-bang migration.

> **Update (2026-07-13):** implemented. `ProviderConnectionSupervisor` now owns a complete
> connection transaction (transport, protocol readiness, and subscription replay) for the shared
> WebSocket manager and `PollingProviderBase`. NYSE and IB retain and replay live subscription
> intent, and failed replay consumes bounded supervised retries. Provider health, dashboard, and
> test routes return nullable `unknown`/`unavailable` states instead of equating enabled with
> connected when runtime diagnostics are absent. Focused proof passed the supervisor harness 5/5
> and both the default and IBAPI smoke-stub Infrastructure builds. Added NYSE/IB replay/rate and
> endpoint-honesty tests were not executed because of shared contention; aggregate CI has not run.

> **Completion-audit closure (2026-07-15):** contract-level diagnostics now cover NYSE,
> Robinhood polling, live IB, and direct IB simulation. Subscription replay, bounded heartbeat
> teardown, explicit caller cancellation, and honest `unknown`/`unavailable` endpoint projection
> close the remaining audit cases without forcing unlike transports through one inheritance model.

### 2. Canonical Symbol-Resolution Spine

Symbol identity currently lives in three places that don't talk to each other: per-provider static
formatting (`SymbolNormalization.cs` — `NormalizeForStooq`, `NormalizeForYahoo`, etc.), the
OpenFIGI-backed `ISymbolResolver` (`Adapters/Core/SymbolResolution/`), and the UI's own
`SymbolMappingService`. Worse, the resolver is wired inconsistently: `ProviderFactory` and
`BackfillCoordinator` pass it into `CompositeHistoricalDataProvider`, but
`BackfillWorkerService.cs:759` constructs the composite **without any resolver**, so the
background backfill path silently does no cross-provider symbol translation. And the composite's
opt-in cross-validation (`ValidateBarsAsync`) calls the validation provider with the *unresolved*
symbol — guaranteed false discrepancies against providers like Stooq that need `aapl.us`.

The work has a cheap correctness core and a structural follow-on. Core (days): thread the resolver
through `BackfillWorkerService`, resolve per-provider inside cross-validation, and add a
regression test for the worker path. Structural (the M–L part): make the existing
`Storage/CanonicalSymbolRegistry` the single identity authority — static `NormalizeSymbol`
becomes a *formatting* concern layered on a canonical identity, the resolver populates the
registry, and the UI `SymbolMappingService` reads from it instead of maintaining a parallel map.

The user moment: cross-provider features stop lying. The cross-provider comparison view shows real
discrepancies instead of symbol-translation artifacts; a backfill routed through the background
worker returns the same data as one routed through the coordinator; and the Data workspace's
symbol browser can show one canonical row per instrument with its per-provider aliases expanded
underneath — instead of the same instrument appearing under three spellings.

Tradeoffs: identity resolution is the classic place where a "unification" quietly changes lookup
behavior for symbols that only worked by accident. Ship the registry convergence behind a
comparison mode first (log where old and new resolution disagree, act on the report), then flip.

> **Update (2026-07-13):** implemented. `CanonicalSymbolRegistry` now treats `SecurityId` as the
> durable identity and retains normalized, provider-scoped outbound symbols plus alias provenance.
> `CanonicalRegistrySymbolResolver` supports `Legacy`, `Compare`, and `Canonical` modes; comparison
> mode preserves the legacy result while recording disagreements. A hosted, idempotent migration
> imports legacy mappings without deleting rollback inputs, and the browser Data workspace shows
> identity, provider aliases, comparison evidence, and migration receipts. Focused browser registry
> tests passed 5/5; Contracts, Storage, and Application builds plus contract-impact,
> generated-route, and schema checks passed. Added .NET endpoint/collision tests await a serialized
> rerun; aggregate CI has not run.

> **Completion-audit closure (2026-07-15):** `SecurityId` is now the durable registry key across
> normalized and provider-scoped aliases, and production backfill and cross-validation use
> provider-aware resolution. Compare/canonical modes, atomic migration fingerprints,
> ownership-preserving backfill, restart no-op, malformed-input rejection, and reload coverage
> close the identity migration lane.

### 3. Unified Data-Quality Model + Browser Data Quality Dashboard

There are three overlapping data-quality code paths: the streaming monitor
(`src/Meridian.DataIntegration/Monitoring/DataQuality/DataQualityMonitoringService.cs` composing
gap, completeness, anomaly, sequence, latency, and cross-provider analyzers), stored-bar scoring
(`Storage/Services/DataQualityService.cs` + `DataQualityScoringService.cs`), and adapter-level
gap analysis (`Adapters/Core/GapAnalysis/DataQualityMonitor.cs`). Each has its own models and
none share a score. Meanwhile the surface asymmetry is stark: ~35 REST endpoints exist in
`DataQualityEndpoints.cs` and WPF has a full `DataQualityPage`, but the browser workstation has
**no data-quality dashboard component at all**.

The work: define one shared quality read-model (per-symbol composite health drawing from all
three sources — streaming freshness, stored completeness, adapter-detected gaps) in
`Meridian.Ui.Shared`, adapt the three subsystems to feed it, and build the React Data Quality
view in `src/Meridian.Ui/dashboard/` on the endpoints that already exist. This is the W1
data-trust story finally becoming visible in the primary operator surface.

The user moment: the Data workspace gains a quality board — every collected symbol as a row with
a single 0–100 health score dot (RAG-colored), expanding to the drill-down: completeness vs
expected session ticks, open gaps with a one-click "backfill this gap" action (wired to the
existing `AutoGapRemediationService` remediation path), anomaly count, and per-provider freshness.
The browser operator finally sees what the WPF operator sees, from the same read-model.

Tradeoffs: this is the largest provider-side item, and the unification risks becoming a rewrite.
Sequence it as read-model + React view over *existing* endpoints first (the browser dashboard is
pure additive value), then converge the three scoring paths behind the read-model one at a time.

> **Update (2026-07-13):** implemented. `CompositeDataQualityReadService` produces one typed read
> model from stored completeness, streaming freshness, and adapter gap integrity, preserving
> partial or unavailable evidence instead of inventing scores. Stable opaque gap IDs and dashboard
> versions let the server resolve an action to the exact symbol, provider, and range before calling
> `AutoGapRemediationService`. Browser and WPF now show the composite score, source components,
> stable gaps, and contextual remediation from the shared contract. Focused browser quality tests
> passed 18/18, and the Application, Ui.Shared, and Ui.Services builds passed. Aggregate CI has not
> run.

> **Completion-audit closure (2026-07-15):** stored completeness, streaming freshness, and adapter
> gaps now feed one shared quality contract. Stable gap identity lets the server resolve exact
> remediation scope, while browser and WPF preserve partial or unavailable evidence and explain
> disabled actions instead of inventing readiness.

### 4. Backfill Feedback Loop: Live Progress and Typed SLA Metadata

Two verified pieces of unfinished plumbing blunt the backfill experience. First,
`CompositeHistoricalDataProvider.OnProgressUpdate` is declared and documented but **never raised**
(`#pragma warning disable CS0067 — "Reserved for future extensibility"`), so nothing downstream
can show real progress while the fallback chain works through providers. Second,
`AutoGapRemediationService` stores its SLA tier decisions (Standard 48h vs SameBusinessDay 8h)
as `key=value` strings stuffed into the execution log's `Warnings` list and re-parses them with
`ParseWarningMetadata` — stringly-typed state masquerading as warnings, invisible to any typed
consumer. Third, the remediation provider is hard-coded to `"stooq"`.

The work: raise `OnProgressUpdate` from the composite (symbol × range × provider-attempt
granularity), flow it through `BackfillProgressTracker` to the existing provider endpoints; add a
typed `RemediationSlaMetadata` field to the execution log record (keeping a one-release
read-compatibility shim for the string format); make the remediation provider a configuration
option via the standard Options pattern.

The user moment: a running backfill in the Data workspace shows *which provider* is currently
serving *which slice*, with fallback hops visible ("polygon rate-limited → trying tiingo") instead
of an opaque spinner. The remediation queue shows each gap's SLA tier and deadline as first-class
columns the operator can sort by — "what breaches SLA today" becomes a one-click view.

Tradeoffs: progress events on the hot fallback path must be cheap and never block the fetch (fire
through the existing event-pipeline policy channels, drop-oldest). The SLA metadata migration
touches persisted execution logs — route through the WAL/`AtomicFileWriter` patterns and keep the
parser as fallback for old records.

> **Update (2026-07-13):** implemented. `CompositeHistoricalDataProvider` publishes bounded live
> observations with symbol range, current provider, fallback attempt, retry round, status, and bar
> count through the typed progress endpoint. Browser and WPF project the same range/provider/attempt
> evidence. `BackfillExecutionHistory` atomically persists a bounded JSON snapshot, while the typed
> execution contract carries remediation SLA tier/status/deadline, provider, attempt, outcome, and
> compatibility-derived legacy evidence. Focused browser view-model tests passed 39/39, rendered
> screen tests passed 26/26, the Contracts build passed, and `BackfillPage.xaml` parsed. The added
> durable-history/.NET/WPF filters were not executed because of shared MSBuild contention; aggregate
> CI has not run.

> **Completion-audit closure (2026-07-15):** bounded typed progress, retained execution/SLA
> history, final-history refresh, and stale-response protection now flow through the same shared
> contracts to browser and WPF. No string-parsed SLA state or UI-specific history fork remains in
> the delivered path.

### 5. Provider Failure & Rate-Limit Hardening

Three small findings that share a theme — failures that vanish. `DataSourceRegistry.cs` swallows
all module-activation exceptions in three separate `catch { continue; }` blocks, so a provider
whose module fails to register simply doesn't exist, with no trace. Rate-limit detection in the
composite falls back to string-matching exception messages (`"429"`, `"rate limit"`) alongside the
typed `RateLimitException`. And rate-limit tracking (`ProviderRateLimitTracker`) exists only on
the historical path — streaming providers have no equivalent, and the `ProviderRateLimitState`
sliding-window reads are only partially lock-guarded.

The work: collect module registration failures into a typed `ProviderRegistrationReport` surfaced
through the existing provider catalog endpoint (and logged structurally); push
`RateLimitException` mapping down into each adapter's HTTP error handling in
`BaseHistoricalDataProvider` so the composite never string-matches; extend rate-limit tracking to
the streaming side of `WebSocketProviderBase` (a natural follow-on to idea 1); and fix the
partial locking.

The user moment: a provider that failed to load appears in the Data workspace's provider list as
*Failed to register* with the actual exception message — instead of being silently absent while
the operator wonders why their configured provider isn't offered. Rate-limit state becomes
visible per provider ("resets in 3m 12s"), streaming included.

Tradeoffs: surfacing previously-swallowed exceptions can be noisy on hosts with optional modules —
distinguish "assembly not present" (info) from "module threw during activation" (error). The
string-matching removal needs one pass over every adapter's error mapping to confirm each API's
429 shape is already translated.

> **Update (2026-07-13):** implemented. `DataSourceRegistry` publishes an immutable registration
> report with typed activation/registration failures, and the provider catalog exposes a sanitized
> operator projection. `ProviderRateLimitTracker` and provider snapshots are fully lock-guarded and
> `TimeProvider`-based across historical and streaming surfaces; NYSE maps HTTP 429/`Retry-After`
> through `NyseHttpResponseGuard` to typed `RateLimitException`. The typed current-state endpoint,
> browser Data workspace, and WPF provider-management surface show requests, remaining capacity,
> reset countdown, failure reason, connection availability, and retry posture without inferring
> missing runtime state; all explicitly state that rate-limit history is not retained. Focused
> ProviderSdk/Infrastructure builds and 24 browser tests passed. Added .NET/WPF filters await a
> serialized rerun after shared build contention; aggregate CI has not run.

> **Completion-audit closure (2026-07-15):** recursive failure classification preserves provider
> attribution and `Retry-After`, and NYSE plus Alpha Vantage symbol and corporate-action paths
> translate HTTP 429 and quota payloads into typed rate-limit failures. Catalog failures remain
> immutable and sanitized, and streaming/historical rate state shares the lock-guarded,
> `TimeProvider`-based model.

---

## Accounting Ideas

### 6. Mark-to-Market Wiring: Pricing Projector → Governed Postings → True NAV

The single highest-leverage accounting finding: `DailyPortfolioPricingProjector`
(`src/Meridian.Ledger/`) — policy-driven fair-value marks producing balanced unrealized-P&L
journal lines with price-source evidence, fully covered in `LedgerIntegrationTests` — has **no
live caller**. The live path (`LedgerPostingConsumer` in `src/Meridian.Execution/Events/`) posts
securities at cost, and `NavAttributionService` (`src/Meridian.Reporting/`) computes NAV off those
cost balances. Two consequences: NAV is NAV-at-cost, and the shadow-NAV close gate is validating
against an unmarked book. There's also a straight correctness issue to fix on the way:
`NavAttributionService` computes `totalNav` by summing *every* account component's balance rather
than assets − liabilities — verify and correct the semantics with a dedicated test.

The work: a scheduled end-of-day valuation service (host-side, following the existing
operational-scheduler patterns) that pulls closing marks from the historical provider chain the
platform already owns, runs the pricing projector per fund book, and submits the resulting lines
as **drafts into the governed FinancialOperations posting path** — not directly into the ledger —
so they flow through the same approval and period-close controls as every other journal. Then
point `NavAttributionService` at the marked book and fix its aggregation.

The user moment: the Accounting workspace's close cockpit gains a "Daily valuation" row — marks
proposed, priced-from evidence attached (provider, timestamp, price source), one approval posts
the batch. NAV in the fund dashboard changes from a cost figure nobody should trust to a
marked-to-market figure with lineage. This is also where the data-provider and accounting halves
of the platform finally meet: the collection layer becomes the pricing source for the books.

Tradeoffs: pricing policy is where funds genuinely differ (listed vs OTC, stale-price fallbacks,
fair-value hierarchies) — the projector already models policy, but the service needs explicit
missing-price handling (grade the valuation run by data confidence; never silently carry forward).
Posting through the governed path means the projector's output shape must map onto the
posting-rule/draft contracts — that mapping layer is the real design work, and it's the pilot for
idea 9.

> **Update (2026-07-13):** implemented. Closing marks from registered historical providers retain
> provider/source, observed-date, and confidence evidence; missing, stale, low-confidence, or
> incomplete required coverage blocks the valuation instead of silently carrying a mark forward.
> `FileDailyValuationPortfolioSource` persists explicitly scoped fund/book/period/position work,
> while `DailyValuationSchedulerHostedService` and the due-run route execute scheduled valuations.
> Accepted runs become governed workbench drafts for human approval and durable posting, restart
> hydration rebuilds marked statements and NAV, and the existing close cockpit now exposes a
> "Daily valuation" lane with schedule, draft, and blocking evidence. `NavAttributionService`
> already computes NAV as assets − liabilities. The focused `Meridian.Application` and
> `Meridian.FinancialOperations` builds passed (the latter with one existing analyzer warning), as
> did generated-route, static, and diff checks. The focused scheduler/E2E, cockpit-lane, and
> stale/low-confidence mark tests were added but have not executed yet; `Meridian.Ui.Shared`
> compiled the new scheduler, DI, and cockpit code before stopping on two unrelated sibling
> `BackfillCoordinator` ambiguities. Aggregate CI has not run.

> **Completion-audit correction (2026-07-15):** the 2026-07-13 foundation did not yet prove a
> postable, non-compounding multi-security batch. The audit reopened this item after finding that
> full cumulative unrealized P&L could be posted again on a later day, aggregate drafts lacked the
> single-security lineage required by the posting guard, configured position lists could become
> stale, and an older posted draft could mask a blocked current run. The status table above tracks
> the delta-carrying, position-freshness, Security Master, batch/correction, tenant-isolation, and
> cockpit-precedence work now in progress; this section must not be read as complete until those
> paths pass their focused end-to-end tests.

> **Hardening update (2026-07-15):** daily carrying-value deltas, per-security lineage, all-member
> batch lifecycle, isolated same-day corrections, current-run cockpit precedence, latest exact-owner
> snapshots at or before cutoff, authoritative flat-book `NoAdjustment`, and strict recorded-as-of
> Security Master reads are implemented. Successful brokerage syncs capture exact
> tenant/company/fund/book/entity/currency history after ownership/history preflight. The final
> audit then moved same-timestamp conflict enforcement into an atomic store operation, bounded
> provider clock skew, filtered aliases by historical validity, and removed full-file latest-read
> memory growth; the closure below records the completed result.

> **Completion-audit closure (2026-07-15):** the snapshot boundary now exposes conditional append
> results, treats economically equivalent same-timestamp writes as idempotent, rejects conflicts,
> and coordinates cooperating instances/processes through a persistent per-path lock. Greatest-as-of
> reads retain constant memory while preserving deterministic exact-owner ordering; the remaining
> JSONL scan and copy-on-write I/O cost is documented above rather than hidden. Provider observations
> are bounded to five minutes of future skew, and Security Master aliases are filtered by recorded
> validity at the requested as-of time. Concurrency, equivalent/conflicting writes, future-time,
> historical-alias, 5,000-record, flat-snapshot, brokerage-capture, and valuation regressions passed.

### 7. Automated Journal Drafts in the Close Cockpit

`AutomatedJournalDraftProjector` + `AutomatedJournalApproval` model exactly the postings the live
path currently ignores — dividends declared/received, cash interest, corporate-action
income/expense, management/performance fee accruals, commission accruals, withholding tax — with
a full submit/approve/reject lifecycle and evidence attachments. All of it unit-tested, none of it
wired. Meanwhile the live `LedgerPostingConsumer` handles trades only, and the manual journal
workbench (`LedgerEndpoints.cs` manual-entry routes) is where operators hand-key everything else.

The work: an accrual-generation service that runs the draft projector on schedule (fee accruals
monthly, dividend capture off corporate-action data the providers already collect —
`Edgar`/corporate-action test coverage shows the ingestion exists), and lands the output in the
**existing manual-journal workbench approval queue** rather than inventing a new surface. The
projector's approval lifecycle maps onto the workbench's draft → validate → submit-approval →
post flow that already has endpoints and UI.

The user moment: on the first of the month, the fund accountant opens the close cockpit and finds
the management-fee accrual, the performance-fee accrual against the high-water mark, and last
month's dividend receipts already drafted with evidence — needing review and one approval each,
not spreadsheet math and hand-keying. The close checklist's "accruals" tasks go from *do the work*
to *approve the work*.

Tradeoffs: fee calculations touch investor money — the partnership projector's high-water-mark
logic must be reconciled against how capital accounts are actually maintained in the
private-capital subledger before its numbers appear in a draft. Corporate-action-driven drafts
depend on corporate-action data quality; grade each draft with its evidence confidence and let
low-confidence drafts land as *needs investigation* rather than *ready to approve*.

> **Hardening update (2026-07-15):** recurring fee and corporate-action schedules now create
> governed workbench drafts with durable CAS/restart behavior, immutable identity, exact currency,
> and evidence gates evaluated at actual execution or review time. Rearming clears stale readiness.
> `LedgerCapitalAccountReconciliationResolver` derives NAV, capital balances, and independent
> NAV/capital-account high-water marks from exact server-owned
> tenant/company/fund/book/entity/currency/period journal scope, and browser/WPF submit that exact
> scope. The completion audit then required a durable approval/certification provenance gate so
> commandless or arbitrary evidence could not self-certify a high-confidence fee basis; the closure
> below records the completed result.

> **Completion-audit closure (2026-07-15):** reviewed fee-basis readiness now requires either a
> normalized `Approved` accounting posting command with a canonical fingerprint and bound approval
> evidence, or governed adjustment-approval provenance. Arbitrary evidence and commandless journal
> mutations fail closed. Reviewer, review time, source version, exact ownership scope, and high-water
> marks are derived server-side; shared browser and WPF composition resolve the same service. The
> resulting 0.95 reviewed confidence is deliberately a governed-derived signal rather than a claim
> of independent 100% certification. Resolver, schedule, posting-command, accounting/close, shared
> DI, and WPF registration regressions passed.

### 8. Period-Close Closing Entries + Retained-Earnings Roll

The F# `PeriodManagement.fs` state machine (Open → SoftClosed → HardClosed, prior-period ordering
guards) is real and wired into close management — but closing a period is **status-only**. No
closing journals are ever posted: `RetainedEarnings` exists in the chart,
`LedgerFinancialStatementBuilder` computes net income, and nothing rolls revenue/expense into
equity at period end. Any multi-period book accumulates income-statement balances forever, and
"period P&L" only works by date-filtered queries rather than by the books actually closing.

The work: a closing-entry projector (following the established static-projector pattern in
`Meridian.Ledger`) that generates the revenue/expense → retained-earnings roll for a book at
period end, invoked as a final gated step in `AccountingCloseManagementService`'s hard-close
sequence — after sign-offs, before the period lock. Restatement handling reuses the existing
report-pack restatement lineage: reopening a hard-closed period (already controller-gated)
reverses the closing batch with full audit linkage.

The user moment: the close cockpit's hard-close step gains a "Post closing entries" gate showing
the computed net-income roll before the operator confirms; the following period opens with clean
income-statement accounts and an equity section that actually reflects accumulated earnings.
Year-over-year balance sheets become directly comparable without query gymnastics.

Tradeoffs: dimensional books complicate the roll (close per fund/entity/sleeve dimension set, not
just per account), and the interaction between closing entries and late adjustments needs a rule:
late adjustments to a closed period must trigger a delta re-roll, which the existing
late-adjustment review workflow can host. Get the F#-validated period ordering to also assert
"cannot hard-close with unclosed income-statement balances."

> **Completion-audit closure (2026-07-15):** closing-entry preparation, approval, posting, hard
> close, and governed reopen are distinct durable transitions. JIT version/readiness checks, atomic
> correction pairs, exact reopen receipts, source-linked reversals, tenant/company ownership, and
> the transactional Postgres temporary-balance/period-CAS guard close the original roll-forward
> gap. After hard close, the bridge moves the exact posted closing batch to `CloseLocked`; only an
> exact server-owned `HardClosed` period can grant that transition, caller flags cannot, retries
> converge, and only governed reopen recovery can release and reverse it.

### 9. One Ledger Spine: Unify the Projector Library with `ILedgerJournalStore`

The structural root under ideas 6–8: `Meridian.Ledger` is an in-memory library (`List<JournalEntry>`
with O(n) full-journal scans in `GetBalanceAsOf`/`TrialBalanceAsOf`), while the durable, governed
path writes to Postgres through `ILedgerJournalStore` — two posting paths that share validation
(both call the F# kernel) but not storage, so every projector integration has to invent its own
bridge. There's also a latent correctness coupling to retire on the way: `Posting.fs` hardcodes
C# enum ordinals (`AssetOrdinal = 0`, `ExpenseOrdinal = 4`) with no compile-time guard.

The work, staged: (a) extract a posting-target abstraction so projector output can be submitted
identically to an in-memory `Ledger` (backtests, what-if) or as governed drafts to the store —
idea 6's mapping layer generalized; (b) make `Ledger` hydratable from `ILedgerJournalStore` for a
book/period so read surfaces (statements, trial balance, NAV) compute off the durable journal via
the same query API; (c) add balance snapshots/indexing so as-of queries stop scanning the full
journal; (d) a one-line unit test asserting the F# ordinals match the C# enum, killing the silent
coupling.

The user moment: indirect but compounding — every projector becomes wireable in days instead of
weeks, backtest ledgers and live books produce statements from literally the same code, and the
"two accounting stacks" documentation problem disappears. The operator-visible symptom: trial
balances and statements in the UI are provably computed from the certified journal store, which
strengthens the report-pack evidence story.

Tradeoffs: this is the long-pole item and it must not become a rewrite of the working
FinancialOperations path. The discipline: FinancialOperations remains the mutation authority;
`Meridian.Ledger` becomes the computation/projection library over it. Sequence strictly after
idea 6 proves the draft-mapping seam on one projector.

> **Update (2026-07-08):** implemented. `IAutomatedJournalPostingTarget` and
> `DurableAutomatedJournalPoster` provide the shared posting seam, with durable
> `ILedgerJournalStore.AppendAsync` happening before any in-memory projection update.
> `LedgerJournalStoreHydrationExtensions` rebuilds ledger projections through
> `ILedgerJournalStore.QueryAsync`, including as-of and book/period helpers, and
> `PostgresLedgerBookService` uses the book/period hydration path before period-close financial
> summaries. `Ledger` now maintains account-balance and posting-count snapshots with binary-search
> point-in-time lookups. The proof set includes `DurableAutomatedJournalPosterTests`,
> `LedgerJournalStoreHydrationTests`, `LedgerIntegrationTests.Ledger_AsOfBalanceSnapshots_HandleOutOfOrderPostings`,
> and `LedgerIntegrationTests.LedgerAccountTypeOrdinals_MatchFSharpPostingKernelContract`.

> **Completion-audit closure (2026-07-15):** chronological as-of indexes preserve out-of-order
> and dimensional reads. Durable posting rejects global journal/command collisions and
> aggregate-scoped source/idempotency collisions, validates exact book context, and compares a
> canonical full-command fingerprint before treating a crash retry as equivalent. The ledger spine
> is implementation-complete and remains a regression surface rather than future sequencing work.

### 10. Fill-to-Ledger Durability Fix in `LedgerPostingConsumer`

Small, sharp, and arguably a bug: `LedgerPostingConsumer`
(`src/Meridian.Execution/Events/LedgerPostingConsumer.cs`) documents that trade events "are never
silently discarded" and configures its bounded channel with `FullMode = Wait` — but its `Publish`
method uses non-blocking `TryWrite` and merely logs a warning when the channel is full. Under
backpressure, executed fills are dropped **before they reach the books**. For an
evidence-led accounting platform, a fill that executed but never posted is the worst kind of
silent break — it surfaces later as an unexplained reconciliation discrepancy.

The work: make the publish path await channel capacity (honoring the configured `Wait` semantics)
or, better, follow the repo's own lifecycle-sensitive persistence guardrail — durably append the
event (WAL pattern from `src/Meridian.Storage/Archival/WriteAheadLog.cs`) before acking it, so a
crash between execution and posting replays instead of losing the fill. Add a backpressure test
that proves no loss under a saturated channel, and align the XML doc with actual behavior.

The user moment: none on the happy path — which is the point. The moment it matters, the operator
sees a delayed posting instead of a phantom position at reconciliation time, and the
broker-vs-ledger reconciliation lane (already built) stops having to catch a self-inflicted
break class.

Tradeoffs: awaiting capacity moves backpressure upstream into the execution event path — the
gateway must tolerate a briefly-blocking publish, or the WAL-append variant decouples it at the
cost of one more durable write per fill. Given fill volumes (not tick volumes), the durable
append is cheap insurance.

> **Update (2026-07-06):** implemented — `Publish` now takes a blocking slow path
> (`WaitToWriteAsync` loop) when the channel is full instead of dropping the fill, and a
> regression test covers the full-channel case. This idea is done; the WAL-append variant remains
> available as a future hardening option if the synchronous block ever becomes a problem at the
> gateway.

> **Hardening update (2026-07-15):** the WAL option described as future work above is now
> implemented. Accepted fills enter an execution-owned, scope-partitioned WAL and pending snapshot
> before acknowledgement, remain pending until idempotent posting succeeds, replay after restart,
> and retain per-fill failure/compaction state. Recovery fallback requires an exact-scope
> `IScopedTradeEventPublisher`; coordinated disposal rejects new orders and awaits report and
> retained-handoff pumps. The completion audit therefore added an admitted-operation drain so a
> broker submission already in flight cannot resume after accounting dependencies are released;
> the closure below records the completed result.

> **Completion-audit closure (2026-07-15):** accounting admission now precedes cancelable
> session/audit/channel work for synchronous and streamed broker fills, so accepted broker truth is
> not rewritten as rejection by downstream bookkeeping. Malformed pre-publication fills fail closed
> and retain durable unresolved-handoff audit evidence. The OMS and fallback store drain admitted
> operations during disposal, and independent fallback writers reload under a persistent lock before
> mutation. Primary WAL/snapshot and fallback snapshot formats bind posting scope, aggregate, period,
> ledger book, accounting-policy id, and policy version; exact production composition rejects
> mismatched or ambiguous legacy data while explicit label-only construction preserves legacy
> compatibility. The configuration-gated UiServer host composes the governed durable target, primary
> WAL, fallback store, publisher, and OMS and fails fast without an authoritative ledger connection.
> Backpressure, ordering, multiwriter, shutdown, complete-identity, legacy-format, host, and restart
> scenarios passed. Non-cooperative dependencies can still extend teardown, and policy/book/period
> rollover requires a new exact durable partition and restart.

---

## Synthesis

**Highest-leverage delivered foundation: #6, mark-to-market wiring.** It converted the tested
projector library into governed operator value, connected provider marks to the books, and
established the mapping seam reused by automated journals and the ledger spine. The production
snapshot, recorded-as-of identity, and provider-time boundaries are now hardened and covered.

**Completed platform foundations:** #2 and #9 closed the provider identity and accounting
storage/projection fractures around `CanonicalSymbolRegistry` and `ILedgerJournalStore`. #1, #3,
#4, and #5 make provider state, progress, quality, and failure evidence visible and typed; #8 makes
close behavior governed and recoverable.

**Current completion state:** #1–#10 are implementation-complete and have focused local validation.
The dated completion-audit notes preserve the findings that tightened #6, #7, and #10; those items
are now regression surfaces rather than open implementation lanes. GitHub Actions remains the
authoritative integration result for the final branch.

**Cross-cutting theme: silent failure is the common enemy.** Swallowed module registrations,
string-matched rate limits, dropped fills under backpressure, "enabled" rendered as "connected,"
marks that never post, periods that "close" without closing entries — every idea here replaces a
silent gap with either correct behavior or a visible, typed signal. That is the code-level
expression of Meridian's evidence-led identity.

**Completion record:**

1. **Provider supervision and identity:** #1, #2, and #5 — complete.
2. **Data trust and operator feedback:** #3 and #4 — complete.
3. **Governed accounting lifecycle:** #6, #7, #8, and #9 — complete.
4. **Durable execution handoff:** #10 — complete.

Remaining release work is GitHub-hosted proof and human review. The explicit residual
characteristics above are operational/performance constraints, not missing implementations from
the original ten suggestions.

**Competitive signals:** the fund-ops incumbents (Enfusion/Clearwater, Arcesium, SS&C) sell
mark-to-market books, automated accrual capture, and hard period-close discipline as the baseline
of "real" fund accounting — ideas 6–8 are precisely the gap between Meridian's demo-grade books
and that baseline, self-hosted. On the data side, Bloomberg's data-lineage tagging and Databento's
per-provider transparency are the patterns worth borrowing: idea 2's canonical identity plus
idea 3's unified quality score give Meridian the multi-provider reconciliation story none of the
per-query vendors can offer, because none of them own more than one feed.
