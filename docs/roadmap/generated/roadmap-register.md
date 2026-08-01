<!--
generated: true
generator: build/scripts/docs/render-roadmap-docs.py
generator_version: 1.0.0
render_contract: meridian.generated-docs.v1
schema_versions:
  - meridian.roadmap-items@1.0.0
inputs:
  - docs/roadmap/data/decision-log.yml
  - docs/roadmap/data/document-index.yml
  - docs/roadmap/data/program-state.yml
  - docs/roadmap/data/risk-register.yml
  - docs/roadmap/data/roadmap-items.yml
  - docs/roadmap/data/stage-gates.yml
do_not_edit: true
-->

# Roadmap Register

Snapshot date: 2026-07-28

## W1-DATA-001 - Provider trust gate and data confidence baseline
| Field | Value |
| --- | --- |
| Wave | W1 |
| Status | done |
| Health | green |
| Priority | critical |
| Owner lane | Data Confidence and Validation |
| Evidence posture | complete |
| Last reviewed | 2026-05-20 |

### Current Summary

Provider validation packets and DK1 operator sign-off are the baseline evidence for trusted data operations.

### Exit Criteria

- Provider parity packet exists and is linked from reference documentation.
- Operator sign-off evidence is available for DK1 readiness.
- Provider validation matrix remains in `docs/reference/provider-validation-matrix.md`.

### Source Modules

- `SRC-HOST`
- `SRC-APP`
- `SRC-CONTRACTS`

## W2-PROMO-001 - Paper promotion evidence and operator acceptance
| Field | Value |
| --- | --- |
| Wave | W2 |
| Status | done |
| Health | green |
| Priority | high |
| Owner lane | Execution and Fund Accounts |
| Evidence posture | complete |
| Last reviewed | 2026-05-28 |

### Current Summary

Closed with W2 acceptance evidence; PaperPromotion is green in the 2026-05-27 pilot readiness run and promotion review remains the governed handoff into W4 casework/reporting work.

### Exit Criteria

- Promotion candidates show evidence lineage before acceptance.
- Operator approval records link to the paper-session context.
- Follow-up TODOs are registry-backed and assigned.

### Source Modules

- `SRC-APP`
- `SRC-CONTRACTS`
- `SRC-UI-SERVICES`
- `SRC-UI-DASHBOARD`

## W2-TRD-001 - Paper trading cockpit reliability
| Field | Value |
| --- | --- |
| Wave | W2 |
| Status | done |
| Health | green |
| Priority | critical |
| Owner lane | Execution and Fund Accounts |
| Evidence posture | complete |
| Last reviewed | 2026-05-28 |

### Current Summary

Closed in the 2026-05-27 evidence slice through shared readiness/operator-inbox tests, browser Trading parity, focused WPF Lane A tests, and green TrustedData, PaperPromotion, and PaperSession pilot gates.

### Exit Criteria

- Trading readiness endpoint works for global and account-scoped checks.
- Operator inbox exposes actionable readiness and reconciliation routing.
- Paper session replay verification remains durable across restart.
- Browser and WPF workstation surfaces show readiness posture through shared contracts where each surface participates.

### Source Modules

- `SRC-HOST`
- `SRC-APP`
- `SRC-CONTRACTS`
- `SRC-UI-SERVICES`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`

## W3-CONT-001 - Research to paper continuity
| Field | Value |
| --- | --- |
| Wave | W3 |
| Status | done |
| Health | green |
| Priority | high |
| Owner lane | Strategy Analytics |
| Evidence posture | complete |
| Last reviewed | 2026-05-28 |

### Current Summary

Closed in the 2026-05-27 evidence slice through shared brokerage/continuity/pilot tests, focused WPF portfolio/accounting/cash-flow tests, browser route/API parity, and green ResearchRun, RunComparison, PortfolioLedgerReview, and Reconciliation pilot gates.

### Exit Criteria

- Research lineage persists through paper-session handoff.
- Strategy run evidence links to promotion candidates.
- Validation commands are documented in source READMEs.

### Source Modules

- `SRC-APP`
- `SRC-CONTRACTS`
- `SRC-UI-SERVICES`
- `SRC-UI-DASHBOARD`

## W4-RECON-001 - Portfolio ledger reconciliation readiness
| Field | Value |
| --- | --- |
| Wave | W4 |
| Status | done |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-05-29 |

### Current Summary

Closed in the 2026-05-29 W4 evidence slice through operations-continuity close-lane coverage, reconciliation casework, browser Accounting parity, WPF Lane C acceptance, and green PortfolioLedgerReview/Reconciliation pilot gates.

### Exit Criteria

- Reconciliation queue actions link to ledger evidence.
- Break resolution and sign-off states are operator-visible.
- Shared contracts remain compatible across browser and WPF workstation surfaces.

### Source Modules

- `SRC-CONTRACTS`
- `SRC-UI-SERVICES`
- `SRC-UI-SHARED`
- `SRC-WPF`

## W4-RPT-001 - Governed report pack readiness
| Field | Value |
| --- | --- |
| Wave | W4 |
| Status | done |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-05-29 |

### Current Summary

Closed in the 2026-05-29 W4 evidence slice through governed report-pack workflow/provenance, publication/restatement readiness, evidence-vault manifest support, browser Reporting parity, and the green GovernedReportPack pilot gate.

### Exit Criteria

- Report-pack lifecycle includes approval evidence.
- Export output records are linked to source data and acceptance status.
- Documentation states the operator value and validation commands.

### Source Modules

- `SRC-CONTRACTS`
- `SRC-UI-SERVICES`
- `SRC-UI-SHARED`
- `SRC-WPF`

## W5-ACCT-001 - Accounting records and operational evidence
| Field | Value |
| --- | --- |
| Wave | W5 |
| Status | done |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-06-04 |

### Current Summary

Closed 2026-06-02. Accounting record summaries with all six evidence categories (source data, normalized activity, reconciliation cases, ledger evidence, approvals, report-pack lineage) are wired end-to-end through shared contracts, the shared workspace service, and both browser and WPF surfaces. Transaction Lab endpoint request wiring is complete with dashboard test coverage for success and failure paths. Close-package and provenance fields are shared contracts accessible through the full workflow and governance lifecycle projection. This item anchors W1-W5 as the coherent near-term operational record baseline before Backtesting Studio, live-readiness, payments, forecasting, enterprise risk, portal, workflow-designer, mobile, or other expansion lanes.

### Exit Criteria

- Accounting record summaries show retained source data, normalized transactions or positions, reconciliation case history, ledger evidence, approvals, and report-pack links.
- Close-package status and audit/provenance timelines are operator-visible through shared read models consumed by browser and WPF surfaces where each surface participates.
- Report-pack exports and restatements retain source evidence, approval state, and publication provenance.
- Documentation identifies W1-W5 as the coherent operational record baseline and defers Backtesting Studio, live-readiness, payments, forecasting, enterprise risk, portal, workflow-designer, mobile, and other expansion lanes unless they directly strengthen that baseline.

### Source Modules

- `SRC-CONTRACTS`
- `SRC-UI-SERVICES`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W5-MASSET-001 - Multi-asset operational coverage proof lane
| Field | Value |
| --- | --- |
| Wave | W5 |
| Status | done |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-07-02 |

### Current Summary

Completed the shared multi-asset operations proof lane by exposing Security Master validation/profile posture, required provider evidence, ledger classification, reconciliation signals, and close-readiness blockers through `/api/workstation/portfolio/multi-asset-coverage`, with browser Portfolio/Accounting and WPF Portfolio cockpit surfaces rendering the shared read model. The v1 alternative-asset baseline now promotes `StructuredCredit`, `PrivateFundInterest`, `PrivateCompanyEquity`, `RealEstateHolding`, and `CommitmentGuarantee` into first-class Security Master classes while retaining `DirectLoan` and governed `CustomAsset` compatibility fallback rows. Live eFront/Yardi/cap-table/trustee adapter builds, new root workspaces, and core-ledger rewrites remain out of scope.

### Exit Criteria

- Equities, options, futures, FX, fixed income, loans, `StructuredCredit`, `PrivateFundInterest`, `PrivateCompanyEquity`, `RealEstateHolding`, `CommitmentGuarantee`, structured/private `CustomAsset`, and `OtherSecurity` rows declare identifiers, economics, provider evidence, ledger classification, reconciliation signals, and close blockers.
- Structured credit, private fund, private company equity, real estate, and commitment/guarantee rows expose class-specific trustee/servicer, factor schedule, collateral tape, administrator/GP, capital call, distribution, NAV, capital account, cap table, valuation, appraisal, debt-service, fee/accrual, covenant, and release/expiry evidence targets.
- Missing retained provider data remains review-required or blocked evidence rather than fake completeness.
- Browser and WPF surfaces consume the shared DTO without client-local readiness rules.
- Follow-on multi-asset reference-data workbench work extends the existing Security Master detail/passport flow with provider evidence, identifier confidence, terms and obligations, projected cash-flow readiness, ledger classification, and operations handoff without introducing a new route.

### Source Modules

- `SRC-APP`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W5X-CONNECT-001 - Custodian and broker statement connector library
| Field | Value |
| --- | --- |
| Wave | W5X |
| Status | done |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-07-18 |

### Current Summary

Delivered 2026-07-02 and completed operator scheduled-fetch coverage 2026-07-18. Statement connectors ship as data, not code - declarative versioned CSV/OFX mapping-profile documents (operator-editable, atomic-write persisted, drift-detected), an IB Flex Report XML connector, an OFX 1.x/2.x bank and investment connector, and a fetch-capable Alpaca activity plus portfolio connector reusing the existing brokerage gateway and credential vault. Every connector classifies transactional, position, cash-balance, fee, and dividend data into canonical records, previews per-column mapping confidence and per-kind record breakdowns, and commits deterministically rendered canonical-CSV artifacts through the existing statement-run workflow into the reconciliation queue. The Accounting Import Statement surface now accepts file upload or remote provider preview, provides a live mapping-profile editor, and lets operators create, edit, pause, delete, refresh, and run persisted broker- or custodian-classified fetch schedules. Duplicate-key idempotency is preserved; transient scheduled-fetch failures do not advance the successful watermark or expose exception messages.

### Exit Criteria

- Operators onboard a new custodian CSV or OFX layout by authoring a mapping profile document without a release.
- Statement imports preview detected columns with per-column mapping confidence and per-kind (position, transaction, cash, fee, dividend) record breakdowns before commit.
- Committed imports enter the existing reconciliation queue with retained raw and canonical evidence and duplicate-key idempotency.
- IB Flex XML, OFX bank and investment, Alpaca activity plus portfolio, and profile-driven CSV statements all normalize through one connector seam with golden-file regression coverage.
- Format drift against a profile's last accepted layout is surfaced as a warning before rows map incorrectly.

### Source Modules

- `SRC-CONTRACTS`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`

## W5X-EVIDENCE-001 - Evidence Vault productization
| Field | Value |
| --- | --- |
| Wave | W5X |
| Status | in_progress |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | in_progress |
| Last reviewed | 2026-07-02 |

### Current Summary

Active 2026-07-02. Productizes the existing Evidence Vault identity, intake, request-list, document-list, extracted-field review, object-link, immutable-manifest, and audit primitives as a reusable evidence layer. The first implemented acceptance path is browser-first statement reconciliation onboarding; WPF UI parity is intentionally omitted from this v1 slice while shared DTO/API support remains compatible.

### Exit Criteria

- Evidence Vault retains imported source documents with immutable vault identity, manifest route, source hash, source record, reviewer state, and audit trail.
- Request-list and document-list queries can filter retained documents by subject, classification, review status, and object links.
- Document authority stays bounded to support, block, suggest, and link; documents cannot approve, post, certify, or release.
- Browser Accounting, Reporting, and Data evidence workbench routes can deep-link into retained support for a selected subject without WPF-specific UI work.

### Source Modules

- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`

## W5X-FINOPS-001 - Financial operations control center
| Field | Value |
| --- | --- |
| Wave | W5X |
| Status | done |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-06-24 |

### Current Summary

Closed 2026-06-24. Financial Operations control-center acceptance is promoted through the shared Operations Continuity lifecycle, close-readiness, approval-policy, close-calendar, reconciliation-break, checklist, audit-evidence, and governed reopen controls; browser Operations Continuity screens consume those shared DTOs for queue, approval, close, reopen, checklist, and break actions, while WPF Fund Ledger projects the same Financial Operations queue, accounting-record readiness, private-capital close posture, period-lock/reopen evidence, and retained evidence packages from shared read models. Direct-lending endpoint and WPF dense-panel proof remains attached as supporting FINOPS evidence without creating a separate browser route.

### Exit Criteria

- Accounting workspace exposes a financial operations command surface that groups reconciliation posture, exception aging, close checklist state, approval/workflow control, and audit evidence readiness from shared read models.
- Reconciliation cases, breaks, assignments, escalations, approvals, close tasks, and evidence packets can be opened from a unified operator queue with deterministic status, owner, due date, and blocker signals.
- Close support shows period state, lock or reopen posture, NAV-support or report-pack dependencies, unresolved exceptions, required approvals, and retained evidence gaps without posting synthetic completion.
- Workflow controls expose assignment, escalation, approval, reopen, and evidence-retention actions through shared services so browser and WPF surfaces share the same policy decisions.
- Generated roadmap and product docs state the accepted Financial Operations control-center boundary and distinguish later proof-layer expansions from this completed W5X milestone.

### Source Modules

- `SRC-CONTRACTS`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-DESIGN-WORKFLOW`
- `SRC-DESIGN-AUDIT`
- `SRC-DESIGN-REPORTING`
- `SRC-UI-SERVICES`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W5X-FREX-001 - Shared financial record explorers
| Field | Value |
| --- | --- |
| Wave | W5X |
| Status | done |
| Health | green |
| Priority | high |
| Owner lane | Workstation Shell and UX |
| Evidence posture | complete |
| Last reviewed | 2026-06-22 |

### Current Summary

Completed shared Ledger, Portfolio, Security & Instrument, and Report-Line Provenance financial record explorers over the shared contracts/read-model seam. Endpoint, WPF, and browser tests now prove saved-view handling, dense-table and inspector parity, cross-explorer proof-action routing, Security Master/AssetOperations/report-usage projection, and report-line provenance drill-through without browser- or WPF-local business rules.

### Exit Criteria

- Shared explorer framework contracts and read models support scope bars, saved views, filters, summary strips, dense grids, record drawers, proof ribbons, proof panels, column layouts, record graphs, Used In, Impacts, evidence links, approval state, reconciliation state, report usage, and audit timelines without browser- or WPF-local business rules.
- Ledger Explorer exposes Journal Entries and Ledger Detail views with core filters, saved views, journal drawer and detail routing, evidence links, approval posture, reversal-chain context, and report-usage drill-through.
- Portfolio Explorer exposes Holdings and Transactions views with position drawer and detail routing, valuation status, reconciliation status, ledger-impact links, instrument links, evidence posture, and report usage.
- Security & Instrument Explorer exposes instrument list, identifier map, terms and obligations, source conflicts, held positions, evidence links, valuation status, expected cash flows, and accounting classification.
- Report-Line Provenance Explorer exposes report-line inputs, approved source records, reconciliations, journal impact, evidence packets, template and package versions, approvals, delivery history, restatements, and audit events.
- Cross-explorer Proof Trail can move from Instrument to Position or Transaction, Reconciliation, Journal, Report Line, Evidence, and Audit Event, and missing retained source evidence remains review-required or blocked rather than synthetic completeness.

### Source Modules

- `SRC-CONTRACTS`
- `SRC-UI-SERVICES`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W5X-OEG-001 - Operational evidence graph product surface
| Field | Value |
| --- | --- |
| Wave | W5X |
| Status | planned |
| Health | green |
| Priority | high |
| Owner lane | Workstation Shell and UX |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-06-15 |

### Current Summary

Planned productization candidate for a shared Operational Evidence Graph surface spanning source, normalization, validation, reconciliation, ledger, capital accounts, close, reporting, delivery, and audit layers. Status must remain planned until implementation evidence links concrete shared read models, service APIs, browser and WPF entry points, manifest export behavior, and tests.

### Exit Criteria

- Shared read models in SRC-UI-SHARED and service APIs in SRC-UI-SERVICES define stable subject identifiers, node identifiers, layer values, status values, warning codes, and typed evidence links for browser and WPF consumers without UI-local business rules.
- Compact proof ribbon, side proof drawer, full proof graph page, and exportable evidence manifest patterns are implemented from the same contracts with parity across browser and WPF or an explicit WPF parity plan recorded in implementation evidence.
- Ledger Explorer, Portfolio Explorer, Report-Line Provenance Explorer, Fund Event Command Center, and Evidence Vault can open graph context for their material records through stable deep links or routed actions.
- Evidence manifests include source hashes or vault references, graph schema version, generated timestamp, completeness state, validation warnings, reviewer or export attestation, and typed links from source through audit.
- Tests prove that missing required source, validation, reconciliation, ledger, close, reporting, delivery, or audit evidence remains review-required or blocked rather than being represented as complete.
- Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the shared contracts, service endpoints, browser surface, WPF surface or parity plan, manifest export, and missing-evidence tests.

### Source Modules

- `SRC-UI-SERVICES`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W5X-STMT-ONBOARD-001 - Statement reconciliation onboarding wedge
| Field | Value |
| --- | --- |
| Wave | W5X |
| Status | in_progress |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | in_progress |
| Last reviewed | 2026-07-02 |

### Current Summary

Active 2026-07-02. Turns the delivered statement connector library into the browser-first onboarding wedge: operators can import custodian or broker CSV, OFX, IB Flex, or mapped connector files, commit them into reconciliation, and immediately drill into retained Evidence Vault proof for the statement run. WPF implementation is omitted for this v1 wedge.

### Exit Criteria

- Browser statement import commit returns Evidence Vault identity, Evidence workbench route, reconciliation route, and next actions.
- The retained raw statement source is copied into Evidence Vault as a Statement document linked to statement run, fund account, period, and import source.
- Statement imports with reconciliation breaks mark retained statement evidence as review-required and route operators into reconciliation casework.
- The wedge reuses W5X-CONNECT-001 mapping profiles and connector parsing without requiring code releases for CSV/OFX layout onboarding.

### Source Modules

- `SRC-CONTRACTS`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`

## W6-BTSTUDIO-001 - Backtesting studio evidence loop
| Field | Value |
| --- | --- |
| Wave | W6 |
| Status | planned |
| Health | green |
| Priority | medium |
| Owner lane | Strategy Analytics |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-06-04 |

### Current Summary

Backtesting Studio remains planned. Strategy work should link research or backtest results into retained evidence, accounting records, approvals, paper-validation lineage, or governed reporting when those links are relevant, without treating prior baselines or named productization targets as development ceilings.

### Exit Criteria

- Backtest result evidence links to strategy lineage.
- Operator-facing acceptance criteria are checklist-backed.
- Source READMEs explain module ownership and test lanes.
- Scope remains limited to evidence linkage and paper-validation support unless a later roadmap change promotes broader Studio scope.

### Source Modules

- `SRC-APP`
- `SRC-CONTRACTS`

## W7-LIVE-001 - Live-readiness governance
| Field | Value |
| --- | --- |
| Wave | W7 |
| Status | done |
| Health | green |
| Priority | medium |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-07-05 |

### Current Summary

Closed 2026-07-05 as bounded live-readiness governance. Paper-to-live promotion now requires the paper baseline, trusted-data review, paper-validation evidence, reconciliation evidence, accounting-record evidence, governed-reporting evidence, governance sign-off, exception-handling evidence, rollback or kill-switch evidence, audit-retention evidence, an active AllowLivePromotion manual override, brokerage live-enablement checks, and clear execution controls before a live run can be created. This closes the governance gate only; broader live execution productization and live portfolio operations remain separate follow-on work.

### Exit Criteria

- Live action surfaces remain paper-first unless the live approval checklist, evidence references, manual override, and execution-control checks are all green.
- Credential and provider checks stay secret-safe and read-only by default; live promotion is blocked when brokerage configuration is not live-enabled.
- Governance sign-off, rollback or kill-switch posture, exception-handling evidence, and audit-retention evidence are linked before any live-readiness claim.
- The completed slice strengthens operational evidence and approval posture without productizing broader live execution or live portfolio operations.

### Source Modules

- `SRC-HOST`
- `SRC-APP`
- `SRC-CONTRACTS`
- `SRC-EXECUTION`
- `SRC-STRATEGIES`
- `SRC-UI-DASHBOARD`

## W8-UX-CONSOL-001 - Browser workstation screen consolidation
| Field | Value |
| --- | --- |
| Wave | W8 |
| Status | in_progress |
| Health | on_track |
| Priority | medium |
| Owner lane | Workstation Shell and UX |
| Evidence posture | in_progress |
| Last reviewed | 2026-07-19 |

### Current Summary

Reduces standalone browser-workstation screens by folding closely related tools into deeper host screens behind the seven charter root workspaces. Phase 1 folds Trial Balance into the Ledger Explorer and the Formula Workbench into Quant Lab; Phase 2 canonicalizes the Evidence Workbench to a single Reporting home; Phase 3 merges Live Quotes, Watchlist, and Price Alerts into one Market Data desk. Every retired route remains a redirect that preserves query and hash scope, and the WPF parity matrix is refreshed in the same change as each fold. Reporting run-flow consolidation and reconciliation module extraction are sequenced as later phases.

### Exit Criteria

- Retired screen routes redirect to their host screens with query and hash scope preserved.
- Sidebar sub-items, the workstation route catalog, mounted routes, and command palette route commands stay one taxonomy for each fold.
- The WPF web-UI alignment plan parity matrix reflects consolidated browser screen names in the same change that folds them.
- Folds compose tabs, master panes, or context rails without stacking screen content beyond the structural proposal page-height budget.
- No read-model, contract, or endpoint changes ship as part of screen consolidation.

### Source Modules

- `SRC-UI-DASHBOARD`

## W8-WPF-PARITY-001 - WPF desktop workstation reactivation and web-UI parity
| Field | Value |
| --- | --- |
| Wave | W8 |
| Status | in_progress |
| Health | on_track |
| Priority | high |
| Owner lane | Desktop Workstation |
| Evidence posture | in_progress |
| Last reviewed | 2026-07-06 |

### Current Summary

Reactivated 2026-07-06. The WPF desktop workstation returns to the active product/UI lane as a co-equal surface alongside the browser workstation. The desktop shell already projects the seven canonical workspaces (Trading, Portfolio, Accounting, Reporting, Strategy, Data, Settings) over shared contracts and read models. Remaining work closes the parity gap with browser-first screens that shipped while WPF was deferred, tracked in `docs/development/wpf-web-ui-alignment-plan.md`. Both surfaces continue to consume shared `Meridian.Ui.Services`, `Meridian.Ui.Shared`, and `Meridian.Contracts` seams so neither client forks product state.

### Exit Criteria

- The WPF lane is documented as active and co-equal with the browser workstation across the design charter, product README, CLAUDE.md, and desktop policy.
- Every browser workstation screen has a WPF equivalent page/view-model or an explicitly sequenced parity item, recorded in the WPF web-UI alignment plan.
- New WPF parity surfaces consume shared contracts, read models, and workstation endpoints rather than inventing desktop-local product state.
- Desktop parity work preserves existing WPF behavior, MVVM boundaries, cancellation flow, and shared-contract usage, validated through the desktop build and desktop-focused service tests.

### Source Modules

- `SRC-WPF`
- `SRC-UI-SERVICES`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-CONTRACTS`

## W9-TRUTH-001 - Loud fail-closed handling of simulated data and in-memory persistence
| Field | Value |
| --- | --- |
| Wave | W9 |
| Status | planned |
| Health | green |
| Priority | critical |
| Owner lane | Data Confidence and Validation |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-07-21 |

### Current Summary

Rank 1 of the 2026-07 first-order improvement slate. Fake-looking-real output is fatal for a prove-the-number product, so every simulated, sample, or synthetic surface must be loudly labeled and every in-memory or placeholder persistence selection must fail closed in supported production profiles, extending the PRD-000/PRD-005/PRD-007/PRD-012 posture in the production-readiness tracker.

### Exit Criteria

- Every simulated, sample, or synthetic data surface renders a persistent operator-visible simulation label in both the browser and WPF workstations.
- Supported production profiles refuse startup when an in-memory, null, no-op, or placeholder persistence implementation is selected for a durable role, with startup rejection tests per prohibited binding.
- Non-production adapters carry an explicit non-production marker and registration guards keep them out of production composition with focused test coverage.
- No figure derived from simulated or seeded data can enter ledger, reconciliation, report-pack, or promotion evidence without a blocking simulation provenance mark.

### Source Modules

- `SRC-HOST`
- `SRC-APP`
- `SRC-STORAGE`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W9-DEMO-002 - One-command seeded demo with durable storage
| Field | Value |
| --- | --- |
| Wave | W9 |
| Status | planned |
| Health | green |
| Priority | critical |
| Owner lane | Workstation Shell and UX |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-07-21 |

### Current Summary

Rank 2 of the 2026-07 first-order improvement slate. The first evaluation hour currently ends in an empty screen; nothing else matters if evaluation fails, so a single documented command must stand up a seeded demo workspace over durable storage that shows the product working before any manual configuration.

### Exit Criteria

- A single documented command provisions a demo workspace with seeded provider data, portfolios, ledger records, reconciliation cases, and report packs over durable storage rather than in-memory persistence.
- Seeded demo data survives restart and renders with the persistent simulation labeling required by W9-TRUTH-001 everywhere it appears.
- The seeded demo path runs in CI so a broken first-run experience fails a check instead of a first evaluation.
- Demo tear-down or reset is one command and cannot touch non-demo data roots.

### Source Modules

- `SRC-HOST`
- `SRC-APP`
- `SRC-STORAGE`
- `SRC-UI-DASHBOARD`

## W9-PAPER-003 - Paper-trading realism with limit/stop matching and costs
| Field | Value |
| --- | --- |
| Wave | W9 |
| Status | planned |
| Health | green |
| Priority | critical |
| Owner lane | Execution and Fund Accounts |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-07-21 |

### Current Summary

Rank 3 of the 2026-07 first-order improvement slate. The promotion gate currently launders overfit strategies because paper fills ignore limit/stop semantics and trading costs and can print placeholder prices; paper evidence must stop overstating live viability before it feeds promotion review.

### Exit Criteria

- Paper matching honors order-type semantics - limit orders fill only at or better than the limit price, stops trigger per a documented trade/quote policy, and market orders fill from observed market data, never at placeholder prices such as one dollar.
- Commission, fee, slippage, and spread cost models apply to every paper fill and are visible in paper-session economics.
- A regression suite proves no paper fill can occur at a price outside the observed market-data envelope for the bar or tick in effect.
- Promotion evidence records the matching and cost model version used by the paper session it cites.

### Source Modules

- `SRC-EXECUTION`
- `SRC-APP`
- `SRC-STRATEGIES`
- `SRC-CONTRACTS`

## W9-ALPACA-004 - Alpaca fill streaming into order and ledger state
| Field | Value |
| --- | --- |
| Wave | W9 |
| Status | planned |
| Health | green |
| Priority | high |
| Owner lane | Execution and Fund Accounts |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-07-21 |

### Current Summary

Rank 4 of the 2026-07 first-order improvement slate. Alpaca is the only turnkey live venue and its order feedback loop is broken; trade-update and fill events must stream back into order lifecycle state, positions, and the durable trade-fill posting path instead of relying on polling or manual refresh.

### Exit Criteria

- Alpaca trade-update and fill events stream into the execution gateway and drive order lifecycle state, including partial fills, cancels, and rejects, without polling.
- Streamed fills flow into the existing durable trade-fill posting and ledger handoff path.
- Reconnect recovery backfills fills missed during a disconnect, with tests covering duplicate and out-of-order delivery.

### Source Modules

- `SRC-EXECUTION`
- `SRC-INFRASTRUCTURE`
- `SRC-APP`

## W9-REPORT-005 - Client-grade PDF/XLSX exports and partners-capital statement
| Field | Value |
| --- | --- |
| Wave | W9 |
| Status | planned |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-07-21 |

### Current Summary

Rank 5 of the 2026-07 first-order improvement slate. Ops teams currently re-type every deliverable into Excel; governed report packs must export client-presentable PDF and XLSX artifacts, including a partners-capital statement, so the governed output is the deliverable rather than an input to manual reformatting.

### Exit Criteria

- Governed report packs export deterministic client-presentable PDF and XLSX artifacts with retained hash and provenance manifests.
- A partners-capital statement covering opening balance, contributions, distributions, income/expense/gain allocations, fees, and closing balance renders per partner and per period from ledger-backed data.
- Exported artifacts carry the same approval and provenance evidence chain as existing report-pack outputs.

### Source Modules

- `SRC-DESIGN-REPORTING`
- `SRC-LEDGER`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`

## W9-NAV-006 - Unitized NAV and real fee, waterfall, and capital-call economics
| Field | Value |
| --- | --- |
| Wave | W9 |
| Status | planned |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-07-21 |

### Current Summary

Rank 6 of the 2026-07 first-order improvement slate. The hard math a fund accountant needs still lives in Excel; unitized NAV series, fee accruals with hurdles and crystallization, distribution waterfalls, and capital-call and commitment tracking must become ledger-backed first-class calculations.

### Exit Criteria

- Unitized NAV per share class is computed from ledger-backed valuations with an auditable calculation trail and restatement support.
- Management fee, performance fee or carried interest with hurdle, high-water mark, and crystallization treatment, and expense accruals post governed ledger entries.
- Distribution waterfall and capital-call or commitment schedules compute from capital-account records and reconcile to the partners-capital statement delivered by W9-REPORT-005.
- Golden-file tests cover the calculation kernels against worked examples.

### Source Modules

- `SRC-LEDGER`
- `SRC-FSHARP-LEDGER`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-CONTRACTS`

## W9-SAFETY-007 - Kill-switch cancel-all and fat-finger, notional, and collar rules
| Field | Value |
| --- | --- |
| Wave | W9 |
| Status | planned |
| Health | green |
| Priority | high |
| Owner lane | Execution and Fund Accounts |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-07-21 |

### Current Summary

Rank 7 of the 2026-07 first-order improvement slate. Safety surfaces must never overpromise; the kill switch must actually cancel all open orders and halt routing, pre-trade rules must cover fat-finger, max-notional, and price-collar checks, and WPF safety buttons must be wired to the real shared controls or visibly demoted.

### Exit Criteria

- Kill-switch activation cancels all open orders across gateways, blocks new order submission, and persists breaker state fail-closed across restart.
- Pre-trade risk includes fat-finger quantity and price-deviation, max-notional, and price-collar rules enforced by the single mandatory production risk validator.
- Every WPF and browser safety control either invokes the real shared execution-control service or is disabled with an explicit not-wired state, leaving no dead safety buttons.
- Activation, failure, and override events append to the execution audit trail.

### Source Modules

- `SRC-EXECUTION`
- `SRC-RISK`
- `SRC-WPF`
- `SRC-UI-SHARED`

## W9-GOV-008 - Route-level authorization, fail-closed tenancy, and hash-chained accounting audit
| Field | Value |
| --- | --- |
| Wave | W9 |
| Status | planned |
| Health | green |
| Priority | high |
| Owner lane | Platform Security and Governance |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-07-21 |

### Current Summary

Rank 8 of the 2026-07 first-order improvement slate. Governance is the brand and these are the gaps in it; every mapped route needs explicit authorization coverage, tenancy must fail closed instead of defaulting, and accounting audit history needs tamper-evident hash chaining, aligned with PRD-001, PRD-007, and PRD-009.

### Exit Criteria

- An authorization coverage test enumerates every mapped endpoint and fails when a route lacks an explicit policy or permission declaration.
- Cross-tenant reads and writes fail closed with tests, and requests without resolvable tenant scope are rejected rather than defaulted.
- Accounting and ledger audit events append to a hash-chained tamper-evident log with cross-process serialization, verification tooling, and tamper-detection tests.

### Source Modules

- `SRC-UI-SHARED`
- `SRC-DESIGN-IDENTITY`
- `SRC-DESIGN-AUDIT`
- `SRC-STORAGE`
- `SRC-LEDGER`

## W9-INGEST-009 - Institutional file ingestion (camt.053/BAI2) and sided reconciliation matcher
| Field | Value |
| --- | --- |
| Wave | W9 |
| Status | planned |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-07-21 |

### Current Summary

Rank 9 of the 2026-07 first-order improvement slate. Reconciliation value is capped by what can be ingested and trusted; ISO 20022 camt.053 and BAI2 bank statements must normalize through the delivered W5X-CONNECT-001 connector seam, and the reconciliation matcher must become explicitly sided between statement and ledger populations with deterministic match and break semantics.

### Exit Criteria

- camt.053 and BAI2 statement connectors normalize through the W5X-CONNECT-001 connector seam with retained raw and canonical evidence and golden-file regression coverage.
- Bounded schema-aware parsing enforces the PRD-010 ingress limits for both formats.
- The reconciliation matcher distinguishes statement-side and ledger-side populations with deterministic one-to-one, one-to-many, and unmatched-break outcomes, stable tie-breakers, and idempotent re-runs.
- Sided match results feed the existing reconciliation casework queue without synthetic completeness.

### Source Modules

- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-DESIGN-DATA-INTEGRATION`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`

## W9-ASSET-010 - Asset Accounting Event Spine and atomic lot posting
| Field | Value |
| --- | --- |
| Wave | W9 |
| Status | done |
| Health | green |
| Priority | critical |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-07-28 |

### Current Summary

Completed 2026-07-28. One evidence-backed Asset Accounting Event Spine covers acquisition, capitalization, valuation, income, corporate action, impairment, depreciation/amortization, and disposal, resolving Security Master identity, versioned book position, ledger book, period, accounting basis, promoted rule pack, projection lineage, and complete typed retained evidence before candidate drafting. Expected, Projected, Drafted, Approved, Posted, Reconciled, and Reported remain distinct lifecycle states; only a retained immutable journal establishes Posted impact. Acquisition lot creation and versioned selected-lot disposal share one idempotent serializable journal-plus-lot transaction, and readiness and UI projections fail closed when retained evidence identity, hash, source, review, effective-date, version, or scope is incomplete. Focused contract, spine, storage, endpoint, shared-read-model, and readiness suites are green ahead of the authoritative GitHub Actions quality gate.

### Exit Criteria

- All eight canonical asset accounting event kinds resolve Security Master identity, authoritative book position and version, ledger book, period and version, accounting basis, promoted rule pack, projection lineage, and complete retained evidence before candidate drafting.
- Lifecycle contracts and shared read models never collapse Expected or Projected into a candidate, Approved into Posted, or Reported into Published; journal impact is absent unless an immutable journal id, ledger book, period, balanced amounts, currency, and Posted status are retained.
- Acquisition creates its lot with the journal, and disposal consumes explicitly selected lot ids and expected versions with retained selection evidence, relief policy, before/after snapshots, correction lineage, and replay-safe fingerprints in one database transaction.
- Production-readiness flags, service or endpoint availability, navigation links, legacy full tokens, and synthesized obligations cannot substitute for complete typed retained evidence.
- Focused contract, spine, storage, endpoint, shared-read-model, and readiness tests pass before the full repository CI and GitHub Actions authority gates.

### Source Modules

- `SRC-CONTRACTS`
- `SRC-DESIGN-INSTRUMENTS`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-LEDGER`
- `SRC-STORAGE`
- `SRC-UI-SHARED`
