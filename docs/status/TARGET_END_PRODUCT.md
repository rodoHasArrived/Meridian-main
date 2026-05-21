# Meridian - Target End Product

**Last Updated:** 2026-05-21
**Status:** Current end-state product summary aligned to the canonical roadmap, DK1/DK2 readiness wrapper, signed DK1 parity-packet evidence with packet-bound sign-off validation, cockpit readiness projection, the active browser workstation lane, the web Research run-library, Quant Notebook helpers, gated Quant Lab support slices, shared Strategy Engine pre-run validation, shared workflow command-palette commands with hash-aware provider-setup routing, browser Portfolio/Reporting/Settings/Data task panels plus Portfolio brokerage-sync next actions, browser Accounting reconciliation dense-table detail-queue support with keyboard selection and no-host break-queue fixtures, the browser Evidence Workbench and shared evidence packet/graph APIs, live quote/order-book/watchlist/Price Alerts market-context routes with selectable recent-trade detail inspection and Watchlist no-quote/empty-state copy, Overview Today panel, visual Strategy Designer, Covered Call chain preview, saved-run history, and selectable trade-timeline detail support, Trading Recent Fills detail support, full-console readiness checkpoint gates with provider-setup repair handoffs for BrokerageSync blockers, Meridian Design System reference workbench/tokenized-color support, browser Security Master search-result selection, identity drill-ins, details/lots/operator-override support, UFL/reference-data projection endpoints, paper-first Alpaca connection verification plus provider-setup next-validation handoffs, read-only Robinhood brokerage aggregation support, stable route-aware run review-packet work items surfaced through operator inbox when actionable, seeded reconciliation exception-route/tolerance/sign-off metadata plus calibration-summary rollups, crash-safe shared UI persistence, retained WPF shell support evidence including the Data shell feature module, workflow automation hardening, scheduled/manual WPF screenshot evidence capture, simplified browser rail/header chrome, demo-data fixture semantics with seeded market-data demo paths, provider capability matrix governance, additive workstation continuity guards, and structured roadmap/source documentation controls

---

## Snapshot

Meridian's target end state is a self-hosted evidence-backed investment operations platform organized around the visible `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings` workspaces.

`Data` establishes evidence-backed provider trust through reproducible provider, replay, checkpoint, DK1 pilot sample-set, parity-packet evidence, Security Master print-packet readiness, UFL/reference-data projections across the expanded asset-class set, live quote/order-book/watchlist/Price Alerts market context with recent-trade detail inspection, provider-setup validation handoffs, and operator-visible sign-off posture. `Strategy` turns that data into reviewed runs, comparisons, visual strategy design, Covered Call chain/run/trade evidence review, Strategy Engine pre-run definitions/validation, and gated Quant Lab/Quant Notebook support evidence with testable plot-rendering and cell/data-fetch state, `Trading` promotes approved runs into paper workflows with market context, staged order-ticket support, selectable fill-detail review, and full-console readiness checkpoint gates whose BrokerageSync blockers route to provider setup repair, `Portfolio` and `Accounting` review the resulting positions, brokerage account posture, household rollups, ledger, cash-flow, performance, Security Master search-result drill-ins, details/lots/operator overrides, and reconciliation evidence with a browser detail queue for loaded runs, `Reporting` turns the governed evidence set into retained report packs and now hosts the first Evidence Workbench for packet completeness, graph lineage, validation, and manifest export, and `Settings` keeps capability coverage and paper-first credential checks reproducible.

The product promise is continuity: one operator can move from data trust to research, paper trading, portfolio and ledger review, and governance workflows without leaving Meridian or losing audit context. Commercially, Meridian should become the system of record for investment decision evidence. New operator UI delivery should prove that continuity in the web dashboard, while retained WPF surfaces remain compatibility and support evidence.

The accounting-led product question is: can Meridian prove, book, reconcile, approve, and report an investment decision? Books Before Broker, Transaction Lab, insurance accounting views, Close the Books, broker statement reconciliation, report restatement tracking, controls, and evidence packets should be treated as planned product modules unless the roadmap already names partial support evidence.

The umbrella product story is **Meridian Evidence OS**: a governed investment operating system that connects evidence graph, vault, completeness scoring, decision lineage, approval packets, replay verification, report-line provenance, and governed report packs. Shared evidence APIs and the first browser Evidence Workbench are current support evidence; the full Evidence OS remains positioning language, not a delivered platform layer.

Current May 19 support evidence narrows part of that path: evidence manifests can now be retained
and reopened through a vault index/lookup route, report packs carry validation/lifecycle metadata,
ledger postings preserve originating-versus-adjustment discipline, reconciliation case storage has
stronger audit/status-transition handling, and Security Master validation gates can produce workflow
snapshots in the active worktree. These strengthen the target path but do not complete the full
Evidence OS, report-line provenance, close workflow, or live-readiness story.

Current browser-workstation safety evidence also narrows the target path without completing it:
provider setup now makes provider-routing connections, bindings, credential-source, environment,
warnings, and trust-snapshot refresh posture visible in Data and Settings; Strategy Designer now
separates browser-openable GET route evidence from reference-only POST validation, preview, and
run-backtest commands; and Reporting export actions now abort superseded profile exports. These are
the right direction for an explainable workstation, but report-pack lifecycle, Backtest Studio, and
live-readiness acceptance remain open.

Current operations-continuity evidence narrows the close path without completing it: the shared
account-period workflow now models broker import/normalization, Security Master resolution and
override approval, ledger draft/validate/post, reconciliation, approval, close, governed reopen,
and hash-chained audit timeline inspection through workstation API routes. The target close
workflow still needs browser/operator acceptance, external statement/custodian intake, durable
generalized casework, report publication controls, and operator-approved calibration.

Current May 20 support evidence narrows the safety and operability path without completing it:
order placement now has fail-closed validation/sign-off gates, ledger and promotion writes require
explicit endpoint authorization, execution metadata is sanitized before exposure, the web
workstation installer can repair invalid provider configs during upgrade, and browser forms now
surface expired-session, forbidden-role, and disabled-field states in accessible text. Security
Master accounting also recognizes MBS, ABS, loan, and amortizing-loan instruments as
factor-schedule fixed-income support evidence. The target product still needs accepted cockpit,
close, report-pack, Evidence Vault, provenance, and live-readiness workflows.

Current May 21 support evidence narrows the strategy and documentation-control path without
completing it: Strategy Engine definitions and validate-run endpoints can block missing data or bad
parameters before a run becomes durable evidence; continuity payload compatibility guards protect
shared ledger/reconciliation/strategy read models; the provider capability matrix names adapter
owners and next actions; and roadmap/source registries make generated status and module docs
deterministic. The target product still needs accepted strategy-to-ledger lineage, Backtest Studio,
close, report-pack, Evidence Vault, provenance, and live-readiness workflows.

For the release-level scenario that ties those workspaces together, use [`../plans/meridian-pilot-workflow.md`](../plans/meridian-pilot-workflow.md).
For the product-category filter and documentation archive rule, use [`../plans/evidence-backed-investment-operations-plan.md`](../plans/evidence-backed-investment-operations-plan.md).
For the consolidated current planning interpretation, use [`../plans/current-direction-and-status.md`](../plans/current-direction-and-status.md).

Planning review note 2026-05-18: this target-state document was reconciled with the active
`docs/plans` index and the consolidated current-direction entry point. The seven-workspace browser
workstation model remains the visible product shape, WPF remains retained support, and
mobile-specific product surfaces remain out of scope.

---

## Operator Workflow

1. **Data** establishes trusted provider coverage, backfill health, symbol readiness, checkpoint confidence, and export confidence.
2. **Strategy** uses that trusted data to run, compare, and review strategy runs across engines and modes.
3. **Trading** promotes approved runs into paper operation, manages orders and positions, and keeps replay, risk, and session history visible.
4. **Portfolio** and **Accounting** turn the same runs and positions into account posture, portfolio review, ledger, reconciliation, cash-flow, and sign-off workflows.
5. **Reporting** packages the governed evidence into retained outputs.

The finished product should feel like one lifecycle, not isolated tools.

---

## Product Surfaces

### Data

Data is where operators manage providers, symbols, backfills, data quality, storage health, and operational exports.

### Strategy

Strategy is where operators validate datasets, run experiments, compare results, inspect fills and attribution, and review promotion readiness.

### Trading

Trading is where operators run paper workflows, manage sessions, review orders and fills, monitor positions and exposure through the blotter, and apply explicit promotion controls.

### Portfolio

Portfolio is where operators inspect account posture, positions, exposure, attribution, brokerage sync evidence, household rollups, and balance evidence created by research, paper, and read-only account-sync workflows. Current browser support should keep position and run-evidence tables selectable, accessible, and tied to detail panels rather than leaving portfolio evidence as passive summaries; brokerage-sync review should also hand operators to provider setup repair, Trading readiness, or the Trading cockpit when those are the next trust-building steps.

### Accounting

Accounting is where operators review ledger outcomes, cash-flow questions, trial-balance posture, reconciliation breaks, browser reconciliation detail-queue state, calibration-summary posture, operations-continuity close gates, and sign-off casework.

### Reporting

Reporting is where operators preview, generate, approve, retain, and restate governed report packs built from shared evidence.

### Settings

Settings is where operators manage credentials, environment posture, storage roots, provider setup, backend capability coverage, and evidence paths that make the pilot workflow reproducible. Successful provider setup should hand the operator directly to the next relevant validation route instead of ending at a passive confirmation.

---

## Commercial Product Packages

These package names clarify the sellable product story without changing the canonical wave order:

- **Meridian Core:** Security Master, account/entity setup, provider/data trust, portfolio views, and audit-trail direction.
- **Meridian Research Assurance:** data trust, run comparison, Run Evidence Graph, Data Trust Passport, and promotion evidence for research teams.
- **Meridian PaperOps:** paper-session dashboarding, replay verification, order/fill/ledger consistency, promotion readiness, Books Before Broker direction, and accounting-grade paper evidence.
- **Meridian FundOps:** portfolio accounting, Transaction Lab direction, reconciliation casework, shadow-book direction, close readiness, insurance accounting pack direction, fund-close workflow direction, broker statement reconciliation, and governed report-pack support for accounting and operations teams.
- **Meridian Controls:** strategy promotion records, Proof of Strategy certificates, Controls-as-Code direction, approval workflow, policy mapping, evidence vault direction, immutable decision-ledger direction, audit trail exploration, report restatement tracking, and governed report packs for oversight teams.
- **Meridian Report Factory:** report templates, periods, approval workflow, pack generation, versioning, restatements, report-line provenance, exports, and archival.
- **Meridian Command Center:** web-first readiness console, buyer demo mode direction, role-based demo views, blocker queue, evidence SLA direction, Assurance Score direction, report-pack status, and operational health for managers.

Only the underlying support evidence already described in the roadmap should be treated as current implementation. Package names are commercial grouping language until their shared contracts, browser workflows, evidence retention, and governed outputs are accepted.

---

## First-Class Capabilities

- evidence-backed provider trust and checkpoint confidence
- shared run history across backtest, paper, and live-aware modes
- explicit `Backtest -> Paper -> Live` promotion workflow with auditability, stable readiness work items, an initial shared operator-inbox route for readiness, actionable run review-packet, and reconciliation blockers, route-aware WPF shell queue routing for the primary work item, shell-context attention cues for active reviews, and operator-visible action readiness
- portfolio, fills, attribution, ledger, cash-flow, and reconciliation visibility from the same run-centered model
- the delivered Security Master baseline as the authoritative instrument-definition layer, now with support evidence from asset-class reference-data projections, browser search-result selection, identity drill-ins, details/lots tracking, and server-side operator overrides
- governance and fund-operations workflows treated as core product surfaces rather than optional add-ons
- a primary web operator dashboard and retained WPF/local API support surfaces that reinforce the same operator model instead of diverging from it; the current web Research run library already supports retained-run review, two-run compare/diff readiness, selected run-detail inspection, promotion-history decision detail, and operator-visible command errors, and the gated Quant Lab surface adds browser execution, parameter, template, plot-rendering, and Quant Notebook helper support evidence through reusable view-model/API seams. The browser lane also now includes Overview Today panel, Price Alerts, visual Strategy Designer, Covered Call chain preview, saved-run history, and trade-timeline detail state, Trading Recent Fills detail state, full-console readiness checkpoint gates with provider-setup repair handoffs for BrokerageSync blockers, and Meridian Design System reference workbench/tokenized-color support, while the retained WPF shell/navigation baseline plus Trading, Research, and feature-owned Data shell briefing heroes, Trading Hours session and holiday-calendar guidance, OrderBook order-flow posture, Storage archive posture plus preview scope/guidance, Provider Health posture briefing, System Health triage with pending-scan versus confirmed-empty guidance, Notification Center filter recovery, Activity Log triage/export/clear support, Watchlist posture with pinned-first list loading, Messaging Hub delivery posture with refresh recency, StrategyRuns filter-aware recovery/run-scope presentation and comparison guidance, BatchBacktest results empty guidance, stable route-aware run review-packet work items, account-scoped operator-inbox routing with shell-context attention cues, QuantScript local execution-history handoffs to shared Research surfaces for mirrored runs, Security Master runtime/search recovery, Fund Accounts account-queue/provider-routing/shared-data and balance-evidence briefing, canonical workspace launch/deep-link routing, single-instance launch-argument forwarding, workflow automation hardening, simplified browser rail/header chrome, and clear demo-data fixture cues are present as support evidence. Shared UI-service persistence now uses atomic file writes for config, export preset, and quality archive JSON snapshots. Workflow-level acceptance remains tied to Waves 2-4.
- browser workstation workflow commands with grouped command-palette sections, query-string route focus for subject/symbol handoffs, and hash-aware provider-setup route matching, Portfolio brokerage-sync review with account-filter focus restoration plus provider-repair/readiness/cockpit next actions, Overview portfolio-at-a-glance plus status/refresh view-model state, sidebar sub-items plus richer menu/command grouping, menu-linked screenshot coverage, Reporting report-pack task state and safe endpoint-link semantics, `/reporting/evidence` workbench support, Settings backend capability coverage and Alpaca paper-key verification, provider-setup validation handoffs, Data Security Master print-packet readiness, Data backfill dense-table queue selection with keyboard-expanded detail, Accounting reconciliation dense-table selection with keyboard-expanded detail and no-host break-queue fixture coverage, Security Master conflict refresh/retry state, Trading readiness UTC as-of labels, Trading loading-state panel support, live quote/order-book/watchlist routes with multi-symbol quote snapshots, Watchlist no-quote/empty-value/busy labels, selectable recent-trade detail inspectors, click-to-trade order-ticket staging, intraday and historical price charts, no-host seeded quote/trade/depth/symbol/snapshot/historical-bar fixtures for `/data/quotes?symbol=AAPL`, fund-account brokerage links, household portfolio rollups, read-only Robinhood aggregation, UFL/reference-data projection endpoints, and Ledger-compatible CLI journal reports are current support evidence for the seven-workspace product path; they do not by themselves close live-readiness, report-pack approval, Backtest Studio, Evidence Vault, report-line provenance, or durable reconciliation casework gates
- release-evidence automation that captures WPF catalog/manual screenshots through scheduled, push, or manually selected workflow groups, publishes diagnostic artifacts, and commits screenshot changes once after the capture matrix while leaving product acceptance tied to the wave gates
- first-run, storage, context-strip, and hosted-page orientation that keeps the workstation coherent: Welcome readiness progress for provider, symbol, and storage posture; Storage archive posture for growth, capacity, and last-scan state; Storage preview scope/guidance for archive-path decisions; compact shared deep-page command chrome; related-workflow and trust-state cues around hosted legacy pages; actionable attention details with severity/owner/source/action; provider-degradation next actions that route to `ProviderHealth`; and Trading hero attention states for warning or critical shared work items

---

## Path To Core Operator-Readiness

### Wave 1: Provider confidence and checkpoint evidence

Prove provider trust and checkpoint reliability with replay, runtime, auth, and validation evidence. The current DK1 wrapper extends this into an Alpaca/Robinhood/Yahoo pilot parity packet with an emitted `pilotReplaySampleSet`, signed 2026-04-27 date-stamped parity-packet artifacts, valid packet-bound sign-off evidence, trust rationale mapping, threshold calibration, and future-review rules for regenerated packets.

### Wave 2: Paper-trading cockpit hardening

Harden the paper-trading cockpit already in code into a dependable operator workflow, using the shared trading-readiness contract and the initial operator-inbox endpoint as acceptance infrastructure rather than proof that cockpit operations are complete.

### Wave 3: Shared run / portfolio / ledger continuity

Make shared run, portfolio, ledger, cash-flow, and reconciliation continuity the backbone of the product across workspaces.

Current WPF evidence now includes RunCashFlow empty-state guidance for selected-run, missing-run, no-event, and loaded retained summaries. That improves run-centered continuity, while full governance cash-flow projection and reporting remain part of the unfinished Wave 4 product layer.

### Wave 4: Governance and fund-operations productization on top of the delivered Security Master baseline

Deepen governance and fund-operations workflows on top of the delivered Security Master baseline using shared contracts, read models, operations-continuity close-lane APIs, and export seams.

Waves 1-4 define **core operator-readiness**.

---

## After Core Operator-Readiness

### Wave 5: Backtest Studio unification

Unify native and Lean backtesting into one Backtest Studio experience.

### Wave 6: Live integration readiness

Validate controlled live integration readiness without overstating broad live-trading completion.

Waves 5-6 deepen the product and widen later claims, but they are not prerequisites for core operator-readiness.

---

## Optional Advanced Research / Scale Tracks

- deeper QuantScript libraries and workflow integration beyond the delivered local execution-history and mirrored-run handoff slice
- L3 inference and queue-aware simulation
- multi-instance coordination
- Phase 16 performance work
- broader advanced research tooling after the core workstation product is trustworthy and coherent

These improve Meridian's ceiling, but they are not required for the core end-state product to feel complete.

---

## One-Paragraph Narrative

When Meridian is finished, an operator can trust their data, run research, promote strategies into paper trading, manage orders and positions, inspect account, portfolio, and ledger outcomes, resolve Security Master and reconciliation issues, and generate governed outputs from one self-hosted workstation product. `Data`, `Strategy`, `Trading`, `Portfolio`, `Accounting`, `Reporting`, and `Settings` are separate workspaces, but they share one model of runs, instruments, account posture, portfolio state, and audit evidence. That shared evidence model is the commercial product: Meridian becomes the system of record for investment decision evidence.
