# Meridian 6-Week Roadmap

**Last Updated:** 2026-05-21

## Current Execution TODOs

- [ ] Keep the DK1 packet, provider-validation matrix, kernel-readiness dashboard, and contract-compatibility matrix synchronized after any provider evidence change.
- [ ] Drive W2 cockpit acceptance through replay freshness, operator-inbox routing, promotion controls, and browser Trading scenario evidence.
- [ ] Convert shared run, portfolio, ledger, brokerage/account, and reconciliation support into W3 continuity evidence across the seven-workspace path.
- [ ] Prove W4 casework, approvals, report-pack lifecycle, provenance, and close/reopen controls with durable evidence before marking governance readiness complete.
- [ ] Assign owners and target sprints to provider capability gaps and keep adapter-readiness follow-up tied to the capability matrix.
- [ ] Use [`wave-implementation-checklists.md`](wave-implementation-checklists.md) as the working TODO ledger for W1 maintenance and W2-W4 blocker cleanup.

**Horizon:** 2026-05-18 through 2026-06-28
**Status:** Short-horizon execution slice derived from the canonical roadmap and current DK readiness dashboard

This document is the six-week execution slice of [`ROADMAP.md`](../status/ROADMAP.md), interpreted through the consolidated current-direction entry point in [`current-direction-and-status.md`](current-direction-and-status.md). It is intentionally narrower than the canonical roadmap and advances the active Wave 2-4 core operator-readiness path while keeping the closed Wave 1 trust gate synchronized.

Planning review note 2026-05-18: the plan remains the active short-horizon execution slice after
the full `docs/plans` review and consolidated current-direction pass. It does not promote
later-wave Backtest Studio, live-readiness, optional advanced research, or UFL target-state work
ahead of the Wave 2 cockpit, Wave 3 continuity, Wave 4 governance, and Wave 1 trust-gate
maintenance path.

Use this with [`waves-2-4-operator-readiness-addendum.md`](waves-2-4-operator-readiness-addendum.md) when assigning owners, sequencing dependencies, or checking workstream-level exit criteria inside the active Waves 2-4 path.

Implementation stance for this plan:

- keep this artifact as a **technical dependency plan**, not owner-assigned sprint slices
- include detailed W3/W4 pre-work even when the execution horizon is six weeks
- allow limited parallel W3 pre-work before DK2 is fully green, while keeping DK2 entry and exit criteria as the release-claim gate

---

## Summary

The next six weeks should focus on four outcomes:

1. keep the closed Wave 1 provider-confidence and checkpoint-evidence gate green and preserve the signed DK1 pilot parity packet around the emitted Alpaca/Robinhood/Yahoo `pilotReplaySampleSet`, freshly generated date-stamped parity-packet artifacts, and valid packet-bound sign-off evidence
2. harden the Wave 2 paper-trading cockpit that is already visible through shared web and
   active desktop/browser surfaces
3. deepen Wave 3 shared run / portfolio / ledger continuity across workspaces while validating browser and WPF operator workflows through shared contracts
4. land the first Wave 4 governance and fund-operations slices on top of the delivered Security Master baseline

The commercial filter for these outcomes is the **Meridian Assurance Loop**: Data Trust Passport -> Run Evidence Graph -> Promotion Passport -> Accounting-Grade Paper Trading -> Governed Report Pack. This filter should sharpen the six-week scope; it should not create a new wave or allow planned commercial modules to be described as complete before shared contracts, retained evidence, and browser-visible workflows exist.

The accounting-led extension for this window is narrow: use Books Before Broker, Transaction Lab, Close the Books, broker statement reconciliation, Controls-as-Code, buyer demo mode, and evidence packet actions as prioritization language only. Six-week work should favor shared-contract definitions and browser-dashboard readiness previews over new module claims.

The Evidence OS extension is also prioritization language only. The shared evidence packet/graph APIs and browser Evidence Workbench are accepted support evidence for packet inspection, completeness validation, and manifest export, but proof certificates, strategy-to-ledger drilldowns, report-line provenance, close readiness, instrument passports, Evidence Vault, Evidence SLA, Decision Memory, Assurance Score, and governed no-orphan-evidence enforcement should remain shared-contract/readiness targets until narrower slices have accepted evidence.

Explicit non-goals in this window:

- Wave 5 Backtest Studio unification
- broader Wave 6 live integration readiness expansion beyond clarifying prerequisites
- optional advanced research / scale tracks such as deeper QuantScript expansion beyond the delivered local run-history handoff, Quant Notebook helper support, and gated browser Quant Lab support slices, L3 inference, multi-instance coordination, preferred-equity follow-ons, and Phase 16 performance work
- broad cleanup or parallel UX programs that do not directly move Waves 1-4

---

## Repo Constraints

This plan starts from the current repo state:

- the web dashboard in `src/Meridian.Ui/dashboard/` and the WPF shell in `src/Meridian.Wpf/` are both active operator UI lanes; shared contracts, local/web API endpoints, and shared read models should carry behavior before either client composes it
- current browser workstation support now includes shared workflow library/preset commands in grouped command-palette sections with hash-aware `/settings#alpaca-provider-setup` routing, dedicated Portfolio brokerage-sync evidence with account-filter focus restoration and next actions into provider repair, Trading readiness, and cockpit review, Portfolio position/run dense tables with row-driven detail panels, Overview portfolio-at-a-glance plus Today panel and status/refresh view-model state, sidebar sub-items plus richer menu/command grouping, menu-linked screenshot-route coverage, Reporting report-pack task state plus GET-versus-POST endpoint-link safety and the `/reporting/evidence` workbench, Settings backend capability coverage plus Alpaca paper-key verification, provider-setup success handoffs into live quotes/backfill/readiness/Security Master validation routes, Data Security Master print-packet readiness, Data backfill dense-table queue selection with keyboard-expanded detail, browser Security Master search-result selection with identity drill-ins, details/lots/operator overrides plus conflict refresh/retry state, browser Accounting reconciliation dense-table selection, keyboard-expanded detail, no-host break-queue fixture, and empty-state guidance, Trading readiness UTC as-of labels, Trading loading-state panel and selectable Recent Fills detail support, full-console readiness checkpoint gates with provider-setup repair handoffs for BrokerageSync blockers, live quote/order-book/watchlist/Price Alerts workflows with multi-symbol quote snapshots and view-model-owned no-quote/empty-value/busy copy, selectable recent-trade detail inspection, click-to-trade order-ticket staging, intraday and historical price charts, a gated `/strategy/quant-lab` browser surface backed by `/api/quant/run`, `/api/quant/parameters`, and `/api/quant/templates`, Quant Notebook cell/data-fetch helpers, visual Strategy Designer at `/strategy/designer`, Covered Call support at `/strategy/covered-call` with dense chain preview, saved-run history, and selectable trade-timeline detail state, hardened command-palette/mega-menu focus handling, Meridian Design System reference workbench/tokenized-color support, shared API clients for portfolio aggregate/exposure, and shared evidence subject/packet/graph/validation/manifest-export APIs. Treat these as Wave 2-4 support evidence, not as completed cockpit, reporting, Backtest Studio, Evidence Vault, report-line provenance, durable reconciliation casework, or live-readiness claims
- current brokerage/account-sync support includes fund-account brokerage links, positions/activity reads, cash-adjusted performance, cash-flow summaries, household portfolio rollups, Alpaca paper connection verification/revocation, and a read-only Robinhood aggregation adapter. Use it to strengthen Wave 3/Wave 4 continuity while keeping external-statement acceptance, reconciliation casework, and controlled live-readiness open
- current governance/accounting support now also includes ledger `posting_kind` preservation with
  period posting guards, report-pack validation/lifecycle metadata, evidence-vault manifest
  identity/index/lookup, hardened reconciliation case storage/audit/status transitions,
  account-sync history/readiness DTOs, and an in-flight Security Master validation-gate/snapshot
  service. Use these to strengthen W3/W4 acceptance evidence without claiming durable close, report
  publication, full Evidence Vault, or live-readiness completion
- current operations-continuity support adds shared account-period close-lane contracts and
  workstation routes for broker import/normalization, Security Master resolution and override
  approval, ledger draft/validate/post, reconciliation, approval, close, governed reopen, and
  hash-chained timeline inspection, with optional transactional journal/audit/workflow commit
  support. Use this to define the next browser/operator close workflow slice without claiming full
  close readiness, external statement/custodian acceptance, or governed publication controls
- current browser-workstation operator-safety support now also includes provider setup feedback
  that exposes provider-routing connection and binding metadata, credential source, environment,
  warnings, and Settings trust-snapshot refresh posture; Strategy Designer backend action metadata
  that separates browser-openable GET routes from reference-only POST validation, preview, and
  run-backtest mutations; and Reporting export commands that abort superseded same-profile or
  profile-switch requests. Use these to reduce operator confusion and stale async state without
  claiming cockpit acceptance, Backtest Studio, governed report-pack lifecycle, or live-readiness
  completion
- current security/release hardening also includes fail-closed brokerage order placement behind
  validation/sign-off artifacts, ledger and promotion endpoint authorization checks, execution
  metadata sanitization, CI security scanning hardening, web-workstation installer config repair
  for preserved provider settings, and browser session/role/disabled-field recovery text. Use
  these as support evidence for operator trust and release hygiene; do not count them as W2/W3/W4
  exit criteria until the corresponding pilot-readiness stages and browser/operator scenarios pass
- current strategy/contract/documentation-control support also includes Strategy Engine
  definitions, parameter schemas, data-dependency policy, pre-run validation, evidence hashes, and
  workstation definitions/validate-run endpoints for Covered Call and visual-designer flows;
  additive continuity payload compatibility guards for ledger/reconciliation/strategy read models;
  a provider capability matrix for adapter-readiness owners and next actions; and structured
  roadmap/source registries with stale-doc/hash validation. Use these to keep shared contracts,
  provider follow-up, and docs freshness explicit without treating them as cockpit, continuity,
  close/report, or live-readiness exits
- buyer-facing demo paths, role-based views, readiness dashboards, close workflow previews, and evidence packet actions should be planned from shared contracts first, then composed into the browser or WPF surface that best fits the operator journey
- the web Research run library already has a support slice for retained-run review, two-run compare/diff readiness, selected run-detail inspection, promotion-history decision detail, command-error alerts, component/view-model coverage, and refreshed built assets; the gated Quant Lab browser surface adds execution/parameter/template and plot-rendering support evidence, with plot geometry and accessibility state now extracted into a reusable view-model module, but strategy-aware launch/preflight, persisted sweep grouping, and Backtest Studio unification remain open
- the WPF workstation shell is already organized around workspace groupings
  such as `Research`, `Trading`, `Data Operations`, and `Governance`; the visible product shell is
  now the seven-workspace browser model (`Data`, `Strategy`, `Trading`, `Portfolio`,
  `Accounting`, `Reporting`, `Settings`), with legacy aliases retained for compatibility; WPF
  remains useful support evidence for active desktop workflows and shared workstation contracts
  rather than the default lane for new operator features
- the current repo contains the WPF shell/navigation baseline in `ShellNavigationCatalog`, workspace shell pages, `MainPageViewModel`, `DesktopLaunchArguments` startup/deep-link parsing, deep-page hosting, context strips, shell/navigation smoke tests, and focused coverage for Batch Backtest results empty-state guidance, Position Blotter, Notification Center history recovery, Trading Hours session and holiday-calendar guidance, Welcome, Storage archive posture, System Health triage, Activity Log triage/export/clear state, Watchlist posture plus pinned-first display, Messaging Hub delivery posture with refresh recency, StrategyRuns filter-aware recovery/run-scope presentation and comparison guidance, QuantScript run-history handoffs, Security Master runtime/search recovery, Fund Accounts account-queue/provider-routing/shared-data and balance-evidence briefing states, workspace queue tone styles, the workspace shell context strip, route-aware operator queue button state with shell-context attention cues, the Trading desk briefing hero's stale-replay count detail, the Research desk briefing hero's run-detail / portfolio / promotion-review handoffs, the feature-owned Data shell's provider / backfill / storage / session / export / environment-mode handoffs, the Provider Health posture briefing, local single-instance mutex plus launch-argument forwarding behavior, and workflow page-state automation markers, so this window should validate workflow value rather than start a second desktop UX track
- the latest shell-support evidence adds Welcome readiness progress for connection/symbol/storage posture, Storage archive posture for daily growth/capacity/last-scan handoffs, Storage preview scope/guidance for archive-path decisions, OrderBook order-flow posture for depth/tape/spread monitoring, compact shared deep-page command chrome that preserves related-workflow and trust-state context, actionable shell-context attention details, a provider-degradation next action that opens `ProviderHealth`, brokerage-sync queue routing into `AccountPortfolio`, bounded run review-packet queue items, and Trading hero attention states for warning or critical shared work items; treat these as validation support for operator orientation and routing, not as separate readiness exits
- fixture/offline desktop workflow mode is now presented as neutral demo data and isolated workflow automation restores shared project assets without pinning the WPF target framework before building the desktop shell with the pinned WPF framework and confirming page tags, so test evidence should distinguish demo-state validation from operational readiness
- the WPF screenshot-refresh workflow now has scheduled, push, and manual dispatch coverage for catalog/manual capture groups, least-privilege default permissions, diagnostic artifacts, and a single post-matrix commit job; use this as screenshot/manual evidence plumbing, not as proof of Wave 2-4 workflow acceptance
- the paper-trading cockpit is partially productized, not greenfield, and now has a shared `/api/workstation/trading/readiness` contract for session, replay consistency/freshness, controls, recent risk/control audit evidence, missing-field explainability warnings, promotion, DK1 trust-gate packet/sign-off projection, brokerage-sync, acceptance-gate/overall-readiness posture, and stable operator work items, plus an initial `/api/workstation/operator/inbox` aggregation contract for readiness, actionable latest-run review-packet, and reconciliation work items that the WPF main shell consumes through route-aware queue-button navigation, Account Portfolio routing for brokerage-sync blockers, run review-packet routing, and active-account `fundAccountId` propagation; `PromotionApprovalChecklist` defines required review items for paper and live promotion approvals, and the run review packet now emits stable route-aware work items for cross-workspace blockers
- shared `StrategyRun`, portfolio, and ledger read services already exist and feed workstation surfaces; the Ledger-compatible CLI journal report path is useful local accounting support evidence, but broader shared ledger continuity and governance reporting remain Wave 3/Wave 4 work
- promotion endpoints and workstation promotion surfaces are already in code
- Security Master is already the authoritative instrument-definition baseline across workstation and governance surfaces, with current support evidence from browser search-result selection, identity drill-ins, details/lots/operator overrides, UFL/reference-data projections for bonds, options, equities, futures, FX spot, swaps, commodities, crypto, deposits, money-market funds, and certificates of deposit, plus factor-schedule accounting treatment for MBS, ABS, loan, and amortizing-loan instruments
- governance already has concrete seams for reconciliation, cash-flow summaries, reporting profiles, and direct-lending foundations
- the closed Wave 1 trust gate remains the first release gate for every downstream claim
- DK1 provider-trust status is ready for Wave 2 handoff: the 2026-04-27 parity packet is signed by Data Operations, Provider Reliability, and Trading with valid packet binding, all pilot samples ready, validated evidence documents, and validated explainability/calibration contracts. Future provider-evidence changes still require a fresh date-stamped packet and matching `packetReview` sign-off; promotion handoff is early in progress through cockpit audit-feedback hardening, export is early in progress through governed report-pack schema/version checks, and reconciliation DK2 is now early in progress through a file-backed break queue with review, resolve/dismiss, audit-history routes, seeded exception-route/tolerance/sign-off metadata, and calibration-summary profile rollups

---

## Wave Alignment

### Active in this window

- **Wave 1:** closed trust-gate maintenance
- **Wave 2:** paper-trading cockpit hardening
- **Wave 3:** shared run / portfolio / ledger continuity
- **Wave 4:** governance and fund-operations productization on top of the delivered Security Master baseline

### Delivery guardrails in this window

- keep WPF workflow-first consolidation and MVVM extraction limited to work that directly supports active Wave 2-4 flows
- keep desktop launch/deep-link routing, screenshot workflows, single-instance forwarding, UI-automation page-state markers, and fixture/demo-mode cues aligned to WPF workspace shell routes so compatibility automation verifies the same desktop support surfaces operators can still open; screenshot-refresh CI should remain a validation evidence lane with diagnostic artifact retention and a single final commit step
- keep validation and documentation synchronized with executable evidence, not summary language; the DK1 `pilotReplaySampleSet` is now part of that evidence contract
- keep shared DTOs, read models, workflow services, and export seams as the integration boundary across active work
- treat current browser and WPF workflow validation as open until the delivered shell/navigation baselines are clearly wired into run-centered workflows through shared contracts; the simplified browser rail/header and seeded `/data/quotes?symbol=AAPL` demo fixtures improve demo and orientation quality, but do not satisfy cockpit, reporting, reconciliation, or live-readiness gates

### Explicitly deferred beyond this window

- **Wave 5:** Backtest Studio unification across native and Lean
- **Wave 6:** live integration readiness, except where Wave 1-2 work clarifies prerequisites
- optional advanced research / scale tracks

### Detailed pre-work beyond this six-week window (required planning depth)

The six-week horizon remains the execution target, but detailed pre-work is required now for W3/W4 so follow-on implementation can start without architectural drift.

#### W3 pre-work package (dependency-first)

- keep shared run-lineage, continuity-warning, and Strategy Engine pre-run payload contracts as the single cross-workspace source
- finish service-backed projection boundaries that separate operator payload builders from fixture/fallback builders
- lock brokerage/custodian raw-snapshot versus normalized-projection persistence boundaries and freshness/divergence semantics
- pre-validate operating-context propagation rules for fund/entity/sleeve/vehicle/account routing across deep links and shell handoffs

#### W4 pre-work package (dependency-first)

- define durable governance casework state model (assign/review/resolve/dismiss/reopen + SLA + audit fields)
- lock governed report-pack lifecycle states and approval/rejection/provenance transitions before wider UI expansion
- define one fund-operations projection contract that joins account posture, reconciliation posture, and report readiness
- define inbox-v2 remediation-playbook contract fields (owner, severity, route, next action, evidence link) before multi-surface rollout

#### Parallelism policy for W3 during DK2 entry

- allowed: contract shaping, projection extraction, deterministic scenario harnesses, and read-only continuity seams
- not allowed: Wave 3 completion claims, governance handoff claims, or UI flows that depend on unresolved DK2 parity/calibration outcomes
- required: every parallel W3 item must declare its dependency on pending DK2 gates and remain release-neutral until DK2 is green

---

## Six-Week Outcomes

### Outcome 1: The closed Wave 1 trust gate stays reproducible and synchronized

- Alpaca and Yahoo remain repo-closed, Robinhood remains explicitly runtime-bounded, and deferred providers stay clearly outside the active gate
- the DK1 pilot replay/sample-set contract is emitted by `scripts/dev/run-wave1-provider-validation.ps1`, packaged by `scripts/dev/generate-dk1-pilot-parity-packet.ps1`, bound to operator approvals by `scripts/dev/prepare-dk1-operator-signoff.ps1 -PacketPath`, and reviewed through the DK1 pilot parity runbook
- backfill checkpoints, gap detection, and Parquet L2 flush behavior remain on the passing command matrix instead of drifting back into assumed reliability
- the active Wave 1 scope stays synchronized with the provider-validation matrix, provider-confidence language, generated validation summaries, and the signed 2026-04-27 DK1 packet; future generated `artifacts/provider-validation/` packets remain run-specific evidence and require matching sign-off when used for review
- the WPF Data shell and Provider Health posture briefing remain consumers of shared provider, backfill, storage, session, and export state instead of becoming separate operational-readiness models
- Storage archive posture remains a Data Operations support improvement: it should project daily growth, capacity horizon, last-scan, and scan-failure guidance from already-loaded analytics without becoming a separate storage readiness gate

### Outcome 2: Wave 2 paper trading is dependable, not just visible

- the shared workstation cockpit is tightened around positions, orders, fills, replay, sessions, risk flows, and Position Blotter selection/action-readiness flows already in code
- the WPF Trading desk briefing hero is validated as a consumer of shared active-run, workflow-summary, and operator-readiness state rather than a separate cockpit model
- `Backtest -> Paper` remains explicit, auditable, and easier to exercise end to end
- session persistence, replay behavior, and stale-replay recovery have clearer operator acceptance criteria
- the trading cockpit now surfaces a single operator acceptance contract for session persistence, replay confidence, audit/control evidence, risk/control explainability warnings, promotion-review readiness, DK1 trust posture, brokerage-sync posture, overall readiness, and operator work items, with an initial shared operator-inbox endpoint, route-aware WPF shell queue-button consumption, Account Portfolio routing for brokerage-sync blockers, run review-packet routing for actionable latest-run blockers, and shell-context attention cues available for readiness, Security Master, and reconciliation queue aggregation
- the local replay-audit hardening slice now records replay consistency, compared fill/order/ledger evidence counts, last-persisted timestamps, and primary mismatch reason so readiness reconstruction has durable audit metadata to read from, and the readiness gate drops back to review-required when those compared counts no longer match the active session

### Outcome 3: Wave 3 shared-model continuity is stronger across workspaces

- `Strategy`, `Trading`, `Portfolio`, `Accounting`, and `Reporting` rely more consistently on the shared run, portfolio, and ledger model
- run comparison, fills, attribution, ledger, cash-flow, and reconciliation flows feel more like one system than adjacent slices
- brokerage/account posture, household portfolio rollups, performance, and cash-flow summaries reinforce the same shared model without becoming a live-readiness shortcut
- WPF refinements in scope reinforce the same shared orchestration seams instead of introducing new page-local logic
- the Research desk briefing hero remains a shared-model consumer for selected runs, portfolio drill-ins, and `Backtest -> Paper` promotion review instead of becoming a separate research-only orchestration path
- StrategyRuns filter recovery and comparison guidance remain shared-run support improvements: they should recover already-loaded run rows, clarify visible-versus-recorded scope, and prevent invalid compare pairs without becoming a separate run-store workflow
- QuantScript Run History remains a shared-run support improvement: it should expose local execution records and only hand off to Strategy Runs, run detail, or compare flows when a mirrored run exists
- RunCashFlow empty-state guidance remains a shared-run support improvement: it should explain selected-run, missing-run, no-event, and loaded retained cash-flow states without becoming a substitute for governance-wide cash-flow modeling
- current shell-navigation work is validated as a workflow-first improvement rather than just a visual reshuffle
- desktop launch/deep-link and screenshot workflow evidence uses the same canonical workspace tags operators use (`ResearchShell`, `TradingShell`, `DataOperationsShell`, `GovernanceShell`) and confirms page state through the hidden-but-present `ShellAutomationState` marker
- screenshot/manual catalog automation can now be selected through workflow dispatch and committed once after matrix capture, which reduces evidence churn but does not change the operator-readiness gates

### Outcome 4: Wave 4 governance work shows up as product, not just planning

- Security Master remains the delivered baseline while account/entity, reconciliation, cash-flow, multi-ledger, and reporting-adjacent workflows deepen on top of it
- the next governance slice is defined in terms of shared DTOs, read models, export seams, account/provider-routing evidence, brokerage account posture, report-pack task state, reconciliation break-queue state, calibration-summary rollups, seeded exception-route/tolerance/sign-off metadata, browser dense-table detail-queue selection, keyboard-expanded detail, no-host break-queue fixture, and empty-state projection, and operator surfaces rather than a parallel governance stack
- the operations-continuity workflow should become the concrete close-lane backbone for this window:
  broker intake, Security Master resolution, ledger posting, reconciliation, approval, close, and
  reopen are now shared API states that need browser/operator acceptance, external input adapters,
  and report publication controls
- accounting-led and Evidence OS commercial slices should start as shared acceptance definitions for accounting-impact previews, close checklist/readiness, statement-import reconciliation cases, Security Master confidence, instrument passport projections, proof/certificate projections, strategy-to-ledger lineage, report-line provenance, break explanation summaries, report restatement tracking, controls-policy summaries, evidence SLA freshness, decision memory, no-orphan-evidence validation, and evidence packet readiness; current asset-class reference-data projections are useful inputs, not completion of the instrument passport or confidence-score modules

---

## Week-by-Week Plan

| Week | Focus | Goals | Deliverables |
| --- | --- | --- | --- |
| 1 | DK1 / Wave 1 closeout confirmation | preserve the signed trust gate and remove planning contradictions around the active provider set and pilot replay/sample standard | refreshed validation summary with `pilotReplaySampleSet`; signed 2026-04-27 DK1 parity packet; valid packet-bound sign-off evidence; synchronized provider/runtime evidence list; explicit deferred-provider wording; dashboard evidence links; future-review rerun guidance |
| 2 | Wave 2 entry | keep the trust gate green while starting cockpit hardening | cockpit hardening checklist; operator acceptance targets tied back to the passing Wave 1 gate |
| 3 | Wave 2 operator lane | tighten the existing trading cockpit into a more dependable operator workflow | session and replay acceptance criteria; promotion approval/rejection rationale checklist; cockpit operator-path checklist |
| 4 | Wave 3 continuity | reduce cross-workspace seams between Research, Trading, and Governance while validating browser and WPF workflows through shared contracts | run-model continuity backlog; fills/attribution/ledger/reconciliation linkage notes; web/workstation validation targets tied to active flows |
| 5 | Wave 4 governance slice | connect the delivered Security Master baseline to concrete governance product slices | account/entity and strategy-structure targets; first multi-ledger/cash-flow/reconciliation slice decisions; reporting/profile follow-ons |
| 6 | Hardening and closeout | make the six-week baseline easy to continue from without widening scope | docs/status refresh; acceptance-criteria review; narrowed follow-on backlog that still stays within Waves 1-4 |

---

## Workstreams

### Workstream A: Wave 1 trust-gate maintenance

Priorities:

- keep Alpaca and Yahoo explicit as repo-closed rows and Robinhood explicit as the only runtime-bounded active row
- keep the DK1 `pilotReplaySampleSet` synchronized across the validation script, generated summaries, signed generated parity packet, packet-bound sign-off evidence, provider-validation matrix, and pilot parity runbook
- keep deferred-provider guidance synchronized so Polygon, Interactive Brokers, NYSE, and StockSharp do not drift back into the active gate by prose alone
- rerun `run-wave1-provider-validation.ps1` whenever provider, checkpoint, or Parquet proof surfaces change
- keep provider-confidence docs, generated runtime outputs, and validation summaries synchronized

### Workstream B: Wave 2 paper-trading cockpit hardening

Priorities:

- harden the existing execution and promotion flows through the shared workstation readiness contract
- keep replay, session, audit, and risk behavior tied to realistic operator use
- keep recent risk/control audit evidence explainable with actor, scope, rationale, and operator-visible missing-field warnings
- prefer reliability and workflow continuity over new cockpit surface area
- define operator-visible acceptance criteria for the paper workflow already in code
- keep the cockpit readiness contract aligned with DK1 acceptance language so session, replay, audit, promotion-review, brokerage-sync, operator-work-item, or future trust-packet/sign-off gaps are visible during daily operation with stable work-item IDs across refreshes
- keep run review-packet work items in the operator inbox bounded to actionable warning/critical latest-run blockers so the queue improves triage without becoming a broad run-history browser

### Workstream C: Wave 3 shared run / portfolio / ledger continuity

Priorities:

- deepen shared run services beyond a mostly backtest-first feel
- improve research-to-trading and trading-to-governance continuity
- keep Security Master enrichment tied to the same shared read-model seam
- use WPF workflow work only where it reinforces the same run-centered orchestration path

### Workstream D: Wave 4 governance and fund-operations productization

Priorities:

- keep Security Master authoritative while extending its use across governance workflows
- define the next concrete slices for account/entity, multi-ledger, cash-flow, calibrated reconciliation, and reporting work
- keep Books Before Broker, Transaction Lab, insurance accounting, broker statement reconciliation, shadow books, Evidence Vault, Report Factory, report-line provenance, instrument passports, decision memory, and controls as roadmap targets unless a slice has shared contracts, retained evidence, and browser-visible workflow support; treat Close the Books as partially seeded by the operations-continuity API while browser/operator acceptance and external statement intake remain open
- treat the Fund Accounts operator brief as Wave 4 support evidence only while it remains a projection of shared account, provider-routing, retained balance-history, and shared-data-access state
- keep governance work grounded in shared DTOs, read models, and export seams rather than a separate subsystem

### Supporting discipline: Retained WPF consolidation and validation

Priorities:

- keep WPF page and shell work tied to shared contracts, active cockpit, shared-model, or governance workflows rather than client-local business logic
- continue MVVM extraction where pages still depend heavily on code-behind orchestration in active areas
- keep navigation, command-palette entries, and workspace framing aligned with the same workstation model used by the WPF shell and desktop-local API contracts
- treat the Trading desk briefing hero, OrderBook posture strip, and Position Blotter selection-review rail as Wave 2 support evidence only when they continue to reflect shared execution, readiness, replay, controls, trust-gate, depth/tape, and brokerage-sync posture without duplicating service logic
- treat the Research desk briefing hero as Wave 3 support evidence only when it keeps selected-run, run-detail, portfolio, and paper-promotion handoffs tied to shared workstation read models
- treat StrategyRuns filter recovery and comparison guidance as Wave 3 support evidence only when they clarify shared run scope, recover hidden retained rows, and prevent invalid compare pairs without duplicating run-read service state
- treat BatchBacktest results empty guidance as Wave 3 support evidence only when it helps operators distinguish idle, validation-blocked, running, failed, cancelled, and populated sweep states without inventing a separate results model
- treat QuantScript Run History as Wave 3 support evidence only when it keeps script/notebook execution records local while using shared Research surfaces for mirrored run handoffs
- treat the WPF Data shell and Provider Health posture briefing as Wave 1/DK1 support evidence only when they keep provider, backfill, storage, session, and export handoffs tied to shared operational services
- treat Storage archive posture as Data Operations support evidence only when it reflects already-loaded storage analytics and helps operators decide retention, packaging, or backfill next steps without replacing storage validation gates
- treat System Health triage as support-triage evidence only when it summarizes provider, storage, and retained event posture and distinguishes pending scans from confirmed empty snapshots without substituting for readiness gates, provider validation, or durable incident queues
- treat Notification Center filter recovery as governance/operator-triage support evidence only when it helps recover retained history without substituting for durable work-item queues
- validate the current `ShellNavigationCatalog`, workspace-shell, deep-page host, and shell-context-strip baseline against active run-centered workflows before widening it further
- keep isolated desktop workflow restore/build behavior deterministic so shared library assets and WPF target-framework builds do not drift during screenshot/manual evidence capture
- keep fixture/demo-mode state explicit in desktop workflow evidence so demo payloads help reproduce UI states without satisfying readiness exit criteria
- pull validation and contradiction checks forward whenever workstation or governance surfaces expand

---

## Risks

### Risk 1: Cockpit polish outruns execution/read-model contracts

Mitigation:

- keep the closed Wave 1 trust gate ahead of broad cockpit claims and tie Wave 2 acceptance criteria to real evidence

### Risk 2: Provider trust remains documentation-only

Mitigation:

- require replay evidence, runtime proof, or explicit gap documentation for every provider claim carried forward

### Risk 3: Governance stays blueprint-heavy

Mitigation:

- require each Wave 4 step to name at least one shared read-model seam and one operator-facing surface

### Risk 4: Workstation polish outruns shared contracts

Mitigation:

- favor workflow services and view-model extraction over page-local orchestration and limit WPF work to active-wave support

### Risk 5: Too much broad cleanup crowds out product movement

Mitigation:

- keep cleanup adjacency-driven and focused on areas already changing for trust, cockpit, shared-model, or governance work

---

## Exit Criteria After 6 Weeks

- provider/runtime guidance for the active Wave 1 gate remains reproducible and contradiction-free, including the emitted DK1 pilot replay/sample-set contract, generated parity packet, packet-bound sign-off template, and explicit operator-signoff status
- backfill checkpoint and gap-handling confidence remains backed by passing evidence instead of only document claims
- the paper-trading cockpit has a tighter, more dependable operator story
- shared run, portfolio, ledger, cash-flow, and reconciliation flows are easier to follow across workspaces
- the active browser and WPF directions are better validated against real workflows, and both remain subordinate to Waves 1-4 rather than becoming parallel programs
- accounting-led commercial additions remain scoped to shared-contract and browser-dashboard readiness targets unless evidence proves a narrower implemented slice
- at least one concrete governance slice is clearly defined or landed on top of the delivered Security Master baseline
- the next follow-on slice remains clearly bounded to Waves 1-4, with Wave 5+, broader live-readiness claims, and optional advanced research / scale tracks still deferred
