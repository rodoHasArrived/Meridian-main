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

Snapshot date: 2026-07-05

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
| Last reviewed | 2026-07-02 |

### Current Summary

Delivered 2026-07-02. Statement connectors ship as data, not code - declarative versioned CSV/OFX mapping-profile documents (operator-editable, atomic-write persisted, drift-detected), an IB Flex Report XML connector, an OFX 1.x/2.x bank and investment connector, and a fetch-capable Alpaca activity plus portfolio connector reusing the existing brokerage gateway and credential vault. Every connector classifies transactional, position, cash-balance, fee, and dividend data into canonical records, previews per-column mapping confidence and per-kind record breakdowns, and commits deterministically rendered canonical-CSV artifacts through the existing statement-run workflow into the reconciliation queue. Scheduled fetches run through persisted schedules with duplicate-key idempotency; the Accounting workspace gains an Import Statement surface with a live mapping-profile editor.

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
