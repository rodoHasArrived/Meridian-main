<!--
generated: true
generator: build/scripts/docs/render-roadmap-docs.py
generator_version: 2.0.0
render_contract: meridian.generated-docs.v1
schema_versions:
  - meridian.roadmap-items@1.1.0
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

Snapshot date: 2026-08-10

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

Delivered 2026-07-02, completed operator scheduled-fetch coverage 2026-07-18, and deepened brokerage-account evidence 2026-07-18. Statement connectors ship as data, not code - declarative versioned CSV/OFX mapping-profile documents (operator-editable, atomic-write persisted, drift-detected), uploaded or remotely fetched IB Flex Report XML, OFX 1.x/2.x bank and investment statements, and a fetch-capable Alpaca activity plus portfolio connector reusing the existing brokerage gateway and credential vault. Alpaca activity uses bounded complete cursor pagination; IB Flex uses the documented v3 request/retrieve flow. Every connector classifies canonical records and retains a structured sidecar for account margin, activity subtype/completeness, option lifecycle, tax-lot, and borrow evidence. The Accounting Import Statement surface previews that evidence before committing into the existing reconciliation queue. Margin Control rolls evidence up across accounts and prime brokers, keeps provider margin authoritative, labels Meridian calculations as diagnostic shadows, and gates durable EOD certification on freshness, completeness, severity, and operator permission. Duplicate-key idempotency is preserved; transient scheduled-fetch failures do not advance the successful watermark or expose exception messages.

### Exit Criteria

- Operators onboard a new custodian CSV or OFX layout by authoring a mapping profile document without a release.
- Statement imports preview detected columns with per-column mapping confidence and per-kind (position, transaction, cash, fee, dividend) record breakdowns before commit.
- Committed imports enter the existing reconciliation queue with retained raw and canonical evidence and duplicate-key idempotency.
- IB Flex XML, OFX bank and investment, Alpaca activity plus portfolio, and profile-driven CSV statements all normalize through one connector seam with golden-file regression coverage.
- Format drift against a profile's last accepted layout is surfaced as a warning before rows map incorrectly.
- Provider account margin, complete activity cursors, option lifecycle, tax-lot, and borrow evidence are retained without widening the legacy reconciliation CSV contract.
- Multi-account and multi-prime evidence rolls into a provider-authoritative Margin Control Center with diagnostic shadow calculations and permission-checked EOD certification.

### Source Modules

- `SRC-CONTRACTS`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`

## W5X-EVIDENCE-001 - Evidence Vault productization
| Field | Value |
| --- | --- |
| Wave | W5X |
| Status | done |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-08-03 |

### Current Summary

Closed 2026-08-03 as the bounded browser-first Evidence Vault productization baseline. Retained document identity, intake, request/document queries, extracted-field review, source records and hashes, object links, immutable manifests, reviewer/audit state, and canonical deep links now support both local and production-authority statement intake. Broader document portal and collaboration expansion remains deferred, and WPF presentation remains a separate parity item.

### Exit Criteria

- Evidence Vault retains imported source documents with immutable vault identity, manifest route, source hash, source record, reviewer state, and audit trail.
- Request-list and document-list queries can filter retained documents by subject, classification, review status, and object links.
- Document authority stays bounded to support, block, suggest, and link; documents cannot approve, post, certify, or release.
- The canonical browser Reporting Evidence Workbench route can deep-link into retained support for a selected subject; legacy Accounting and Data evidence routes redirect to that one owner surface without WPF-specific UI work.

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
| Status | done |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-08-03 |

### Current Summary

Closed 2026-08-03 as the browser-first statement reconciliation onboarding wedge. Connector commits return canonical Evidence Workbench and reconciliation routes; production composition retains immutable Reporting authority while projecting the authority-verified Statement source, hash, reviewer/audit state, extracted counts, and run/account/period/import/case links into the queryable Evidence Vault. Break-bearing imports fail into review-required posture. WPF presentation remains a separate parity item.

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
| Status | done |
| Health | green |
| Priority | medium |
| Owner lane | Strategy Analytics |
| Evidence posture | complete |
| Last reviewed | 2026-08-03 |

### Current Summary

Closed 2026-08-03 as a bounded, governed evidence loop on the host-composed browser Covered Call path. Before queueing, the request is budget-bounded, binds exact tenant/company strategy-run lineage, and resolves at least one strict canonical Evidence Vault manifest inside that scope. Strategy and Trading consume the same scoped run state. The four canonical Backtest-to-Paper checklist items remain review-required until a durable approved promotion records operator/time/audit authority, keyed evidence that exactly matches the source run, and an exact same-scope Paper child with matching parent and strategy identity; the Strategy surface uses that governed promotion instead of creating a generic paper session. BacktestStudioRunOrchestrator is not host-composed and Strategy Designer fails closed without one captured result, so neither is closure evidence. Broader Studio UX remains deferred.

### Exit Criteria

- The host-composed browser Covered Call request binds its native backtest to exact tenant/company canonical strategy-run lineage before execution.
- Count, value-length, and aggregate budgets fail before Vault I/O; at least one strict canonical Evidence Vault manifest reference must resolve inside the authenticated scope before queue admission.
- Operator-facing acceptance criteria are retained as requirements, while the four canonical Paper checklist items become Ready only from an approved durable promotion with operator/time/audit authority and keyed evidence exactly matching the source run.
- The approved Paper target is durably retained in the same tenant/company scope with exact parent-run and strategy lineage; missing, rejected, foreign, or mismatched authority stays review-required or rejected.
- Strategy promotion uses the governed promotion endpoint, and Strategy review plus Trading readiness consume scoped runs without a generic paper-session bypass or legacy fallback.
- The shared strategy-run store, replay, detail, review packet, evidence graph, and browser surfaces preserve the governed loop without client-local readiness rules.
- Source READMEs explain module ownership and test lanes.
- Scope remains limited to the supported Covered Call evidence and Paper-promotion loop unless a later roadmap change promotes broader Studio scope.

### Source Modules

- `SRC-BACKTESTING`
- `SRC-STRATEGIES`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`

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

Reduces standalone browser-workstation screens by folding closely related tools into deeper host screens behind the seven charter root workspaces. Phase 1 folds Trial Balance into the Ledger Explorer; its Formula Workbench fold was withdrawn on 2026-08-11 because the folded tab rendered only a permanent not-connected placeholder, and that fold completes when the built formula workbench is mounted against a real catalog endpoint; Phase 2 canonicalizes the Evidence Workbench to a single Reporting home; Phase 3 merges Live Quotes, Watchlist, and Price Alerts into one Market Data desk. Every retired route remains a redirect that preserves query and hash scope, and the WPF parity matrix is refreshed in the same change as each fold. Reporting run-flow consolidation and reconciliation module extraction are sequenced as later phases.

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
| Status | ready_for_acceptance |
| Health | green |
| Priority | critical |
| Owner lane | Data Confidence and Validation |
| Evidence posture | implementation_complete |
| Last reviewed | 2026-08-09 |

### Current Summary

Implementation completed 2026-08-09; the three items that remained after the 2026-08-08 core slice are closed. Per-screen WPF labeling - the shared workspace context strip's Environment badge now resolves the server-reported provenance token fail-closed (a connected seeded backend can never read "Live"; unknown tokens degrade to SIMULATED), and the Portfolio and Reporting shells, which previously rendered an empty strip, now compose the shared context service so all seven workspaces carry the badge. Hard entry-time blocks - reconciliation-break intake refuses an unmarked item whose source declares a simulated/seeded/sample origin at the single durable chokepoint (FileReconciliationBreakQueueRepository, mirroring the ledger append boundary; carried marks are normalized to the canonical vocabulary), and report packs that cite simulated/seeded runs inherit the strongest non-real mark, always fail validation with a Critical provenance issue (ResolveStatus can never return Validated), and the durable report-pack boundary refuses to persist a marked pack in any approvable/exported/retained state. Startup wiring - the ADR-019 startup guard now runs the supported-local durability assertion (an unlabeled local composition with in-memory durable-role bindings refuses startup naming the bindings; a pinned non-real provenance declaration is the sanctioned labeled alternative), UiServer pins the composed provenance into the graph (seeded for demo hosts, forced-simulated for in-memory local durables), and /api/demo/mode plus the status-poll parser surface the pinned label to both workstation shells even when demo heuristics say disabled.

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
| Status | ready_for_acceptance |
| Health | green |
| Priority | critical |
| Owner lane | Workstation Shell and UX |
| Evidence posture | implementation_complete |
| Last reviewed | 2026-08-08 |

### Current Summary

Implementation completed 2026-08-08 on the pre-existing --seed-demo/--demo/--reset-demo spine. One documented command now provisions all five required domains over durable file-backed stores rooted at the guarded demo workspace - deterministic provider market history (90 sessions x 3 symbols of trade-event JSONL the Data desk reads), a sample fund account plus a durable portfolio position snapshot, balanced draft journal entries in the accounting drafts queue (accounting records seed as drafts for human review; the demo never fabricates posted ledger entries, and posted money-path stores remain PostgreSQL-gated), the existing reconciliation casework, and a review-required governed sample report pack whose provenance carries the seeded mark. Every seeded record carries the W9-TRUTH-001 seeded provenance token, re-seeding is idempotent, restart-modelled durability is proven per domain, and the guarded one-command reset is unchanged. The demo-smoke CI lane runs the extended Meridian.Tests.Demo suite including the five-domain durability test.

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
| Status | ready_for_acceptance |
| Health | green |
| Priority | critical |
| Owner lane | Execution and Fund Accounts |
| Evidence posture | implementation_complete |
| Last reviewed | 2026-08-08 |

### Current Summary

Implementation completed 2026-08-08. Both paper gateways now match through the shared documented PaperOrderMatchingPolicy (paper-match/1) - market orders transact against the observed ask/bid (falling back to last trade, then bar close) and never at placeholder prices, limit orders fill only at or better than their limit and otherwise rest, stops trigger per the documented trade-preferred policy with a quote fallback, and resting orders re-evaluate event-driven as the market event tap records fresh trades, quotes, and bars (bars now feed the observed envelope). Every fill applies the PaperTradingCostModel (paper-cost/1) commission/fee/slippage schedule with per-fill spread reporting, costs flow into paper-session economics (portfolio cost booking, per-fill report fields, session TradingCosts totals), and an FsCheck-backed regression suite proves no fill can print outside the observed market-data envelope for the bar or tick in effect. Paper sessions durably record the matching and cost model versions, and paper-to-live promotions now require the PAPER_EXECUTION_MODEL_REVIEWED evidence reference recording those versions, validated at approval and at the durable promotion-record store. The explicitly opted-in scaffold pricing escape hatch (default off, loudly warned) is unchanged.

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
| Status | ready_for_acceptance |
| Health | green |
| Priority | high |
| Owner lane | Execution and Fund Accounts |
| Evidence posture | implementation_complete |
| Last reviewed | 2026-08-10 |

### Current Summary

Implementation verified complete 2026-08-10; the 2026-07-21 premise that the order feedback loop was broken no longer matches source. The authenticated trade_updates stream (AlpacaTradeUpdatesClient) normalizes every broker event into an ExecutionReport and admits it to a durable content-hashed inbox that deduplicates broker event ids, survives restart, and replays unacknowledged envelopes; AlpacaBrokerageGateway exposes that stream as the execution-gateway report stream, blocks live submission while the stream is unhealthy, and after every authenticated reconnect reconciles the REST order snapshot plus exact FILL activities from the acknowledged watermark so fills missed during a disconnect backfill without polling. The OrderManagementSystem consumes the gateway stream from construction, applies partial fills, cancels, and rejects to tracked order state with monotonic cumulative-fill handling and terminal-state latching, converts cumulative broker quantities into deduplicated increments, and routes each genuine increment through the existing durable trade-fill posting and ledger handoff path with retained-failure replay; host composition wires the trade-updates client into the gateway and the trade-fill ledger posting scope. The 2026-08-10 acceptance candidate closes the exit-criterion evidence gap by adding out-of-order delivery tests at the stream client (admission without loss, backfill watermark never regresses, duplicate and out-of-order REST fill replay admits each exact fill once) and at the OMS (a stale lower cumulative publishes no increment and never regresses portfolio or accounting), plus an end-to-end loop suite driving raw Alpaca trade_updates JSON through the client into a real OMS asserting lifecycle transitions and exactly-once accounting handoff under duplicate, out-of-order, cancel, reject, and reconnect REST-replay delivery. Acceptance caveats recorded 2026-08-10 from automated review, all pre-existing rather than introduced by this candidate - the hosted submission health gate treats trade-update recency as stream liveness, so an idle or order-free live account blocks new submissions once the 30-second stale window lapses (submission-side only); reconnect FILL-activity backfill starts exactly at the acknowledged watermark with no overlap window, so a fill the stream skipped beneath an already-acknowledged newer event is recovered only through the order-snapshot lane, which restores quantity at snapshot average-price attribution rather than exact per-fill economics; and a durably admitted fill replayed into a freshly restarted host is acknowledged without reaching the accounting handoff when the restarted OMS no longer tracks its order, leaving recovery to the brokerage activity-sync lane. These are acceptance-review inputs and follow-up candidates, not exit-criterion regressions.

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
| Status | ready_for_acceptance |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | implementation_complete |
| Last reviewed | 2026-08-10 |

### Current Summary

Implementation completed 2026-08-10. The phase-1 client-grade rendering lane had already merged - Meridian.Documents renders governed ledger report packs to deterministic client-presentable PDF (QuestPDF) and XLSX (ClosedXML) with fixed metadata and canonical zip ordering so re-rendering reproduces the bytes, the certified reporting path binds every primary document to an exact checkpoint-bound canonical ledger presentation and fails closed without one, and certified packages retain report-pack signatures and provenance manifests alongside the artifacts. This acceptance candidate completes the deferred presentation half by absorbing and validating the stalled bespoke partners-capital layout (supersedes PR 2525) against current main - PartnersCapitalStatementLayout classifies each capital account by partner role from stable ledger account names, computes ownership shares that foot to 100 percent while excluding non-partner equity, anchors the statement to the fund's ledger-backed net asset value with an explicit reconciliation flag, and never alters a ledger figure; the renderer emits a purpose-built PDF section (NAV context strip, role-labelled per-partner roll-forward, ownership column, reconciliation footnote) and a dedicated typed-numeric XLSX sheet with accounting/percent formats plus a NAV anchor block so operators can sum and pivot without retyping. Certification tests were updated to the bespoke sheet shape while keeping their governance intent - primary bytes must equal the canonical Documents renderer output and display-row rebuild fingerprints remain forbidden.

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
| Status | ready_for_acceptance |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | implementation_complete |
| Last reviewed | 2026-08-10 |

### Current Summary

Implementation verified complete 2026-08-10; the calculation surface the 2026-07-21 summary said "still lives in Excel" has been ledger-backed source for some time and the missing piece was the exit-criterion golden-file evidence. Unitization - ShareClassUnitRegisterProjector folds dated subscriptions/redemptions into a pure, deterministic per-class unit register (units outstanding, per-investor holdings, NAV per unit via NavPerUnitCalculator, running high-water mark, and single-NAV equalisation credits/contingent redemptions via EqualizationCalculator for equalising classes), with the movement-level register as the auditable calculation trail and restatement handled by re-projection plus the report-pack restatement lifecycle. Fee economics - the Meridian.FSharp.Ledger FundEconomics kernels compute day-weighted management fees, straight-line expense accruals, and performance fees with hurdle, high-water mark, and crystallization treatment, and FundEconomicsJournalFactory converts fee, NAV, and waterfall outcomes into governed journal drafts. Waterfall and commitments - EuropeanDistributionWaterfall runs return-of-capital, preferred-return, solved GP catch-up (including sub-100 percent catch-up rates), and carried-interest tiers with cumulative threading across distributions; CapitalCallScheduleDraftBuilder, CapitalCallPlanBuilder, and CommitmentRollForwardCalculator maintain the net-called plus uncalled plus expired equals total invariant with recallable-distribution handling; and the ledger partners-capital reconciliation suite ties these to the W9-REPORT-005 statement. The 2026-08-10 acceptance candidate closes criterion four by adding a golden-file worked-example pack (tests/fixtures/fund-economics/golden) with hand-computed figures independent of the implementation - day-count fee and expense examples, a four-period threaded performance-fee cycle proving the high-water mark moves only on crystallization and only to post-fee NAV, a three-distribution threaded waterfall whose cumulative GP take is exactly its carry share of profit, a sub-100 percent catch-up example pinning the documented pool-rounding drift, equalisation credit/contingent cases, and a full-year unit-register scenario whose aggregate holding value ties to class NAV - all asserted step by step against the real kernels.

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
| Status | in_progress |
| Health | green |
| Priority | high |
| Owner lane | Execution and Fund Accounts |
| Evidence posture | in_progress |
| Last reviewed | 2026-08-11 |

### Current Summary

Source-verified 2026-08-10 and substantially further along than the 2026-07-21 premise, with the remaining gaps now named precisely. Already implemented - the durable execution circuit breaker (versioned atomic snapshot, pending-trip file, operator endpoint) blocks order submission through the operator-controls gate the OMS consults, manual overrides are supported kinds with durable state, breaker activation and override events append to the execution audit trail, the OMS exposes a concurrent CancelAllAsync sweep behind the governed cancel-all endpoint, and the mandatory CompositeRiskValidator enforces drawdown-guardrail, order-rate, position-limit, gross-exposure, symbol-concentration, and max-order-notional rules (with escalation banding and fail-closed handling of unmeasurable orders) fed live from RiskRuleRuntimeService. The 2026-08-10 change closed the criterion-one COUPLING gap - opening the breaker now issues the kill-switch cancel-all sweep after the durable breaker flip, with sweep outcomes audited separately from the activation, proven at the endpoint seam. Criterion one itself is NOT discharged by that, and the 2026-08-11 review corrected the record: OrderManagementSystem.CancelAllAsync awaits CancelOrderCoreAsync per order but discards each OrderResult, so a broker that rejects an individual cancellation leaves that order working while the sweep completes normally and ExecutionEndpoints audits CircuitBreakerCancelAll as Completed - the Failed branch fires only on a thrown exception, and the existing endpoint test asserts only that CancelAllAsync was invoked, never that the open book emptied. Criterion one requires cancellation to succeed, so it stays open until the sweep aggregates per-order outcomes and surfaces a failed or partial kill-switch state naming the orders still working, with a test that asserts the book rather than the call. The 2026-08-11 change lands the fat-finger half of the criterion-two rule catalogue - FatFingerRule enforces an absolute per-order quantity ceiling and a directional price-deviation band in the mandatory CompositeRiskValidator, running at negative priority so a mistyped order is attributed to the typo rather than to whichever exposure ceiling its inflated size breached. The price limb is deliberately one-sided (a buy paying above the reference or a sell hitting below it) because a symmetric band would reject the entire resting book. Successive review rounds corrected several scope errors that would each have rejected ordinary desk activity, so each price is now measured against the reference that price is actually meaningful to and each order type contributes only the prices it genuinely puts at risk - a plain Limit order's limit against the current touch, a StopMarket or StopLimit trigger against the trigger reference described below - NOT the touch, because those references disagree on wide books - and a StopLimit's limit against its own trigger rather than today's market (a sell stop at 90 with an 89 limit is 1.1% from its trigger though 11% from a 100 market, and an earlier revision rejected it). The 2026-08-14 correction closes the auction-limit gap: LimitOnOpen and LimitOnClose now contribute their broker-enforced limit price to the shipped rule. Because the runtime has no indicative-auction or prior-close reference seam, the rule refuses those orders as FAT_FINGER_UNMEASURABLE while the deviation band is configured instead of comparing an auction price with the continuous BBO or routing it unchecked. AlpacaBrokerageGateway.MapOrderType maps both types to a plain broker-enforced limit, MapTimeInForce carries the auction timing separately as opg/cls, BuildOrderPayload writes limit_price unconditionally, and BrokerageGatewayAdapter requires a positive limit price for both. A future auction-reference seam can replace the fail-closed posture when supported. Market orders contribute none because the paper gateway passes a simulated observation through LimitPrice, TrailingStop contributes none because its trigger is derived by the broker and moves with the market, and multi-leg packages contribute none because a net debit or credit is not comparable to a quote for the top-level symbol. The 2026-08-11 revision also closes a live safety hole the earlier blanket stop exclusion left open - a stop trigger's wrong side is the exact mirror of a limit's, because PaperOrderMatchingPolicy fires a buy stop once the market reaches or passes above it and a sell stop once it reaches or falls below it, so a buy stop typed beneath the market is already crossed on arrival and a stop-market order that triggers on acceptance routes as an unbounded market order. Trigger deviation is therefore measured on the wrong side only, under the same operator-configured band and a distinct FAT_FINGER_STOP_TRIGGER_WRONG_SIDE code, so a protective stop placed correctly never breaches however far away it sits. The trigger is measured through a new IPortfolioExposureProvider.TryGetTriggerReferencePrice seam that reads THE MATCHER'S OWN OBSERVATION rather than rebuilding one - the production provider captures PaperMarketObservation from the live feed and resolves through PaperMarketObservation.ResolveStopTriggerPrice, now the single definition of the LastTradePrice ?? BarClose ?? crossing-side precedence that PaperOrderMatchingPolicy itself calls. Sharing one method rather than maintaining two matching implementations is deliberate, because each reconstruction attempt diverged somewhere: consulting the quote first falsely rejects a resting stop when the print trails the quote and falsely approves an already-triggered one when it leads; omitting the bar-close leg reproduces the false approval on a bar-driven session with no print; and applying the provider's own staleness filter drops a six-minute-old print the matcher will still fire from. The age filter that protects every valuation accessor is deliberately absent on this one, because the question is not what the order is worth but whether the engine will fire it, and where guard and engine disagree the engine is the authority - but only while the paper matcher IS the engine, so the unfiltered observation is used solely in a paper composition. Against a live broker nobody in-process decides the fill and the feed cache retains prints indefinitely, so the same precedence runs over current observations instead; a stale 50 print must lose to a fresh 100 ask or a buy stop at 60 reads as resting while the broker sees it already crossed. The posture defaults to live, because that is the side on which guessing wrong routes an unbounded order. Both thresholds are read as one value per evaluation so an order cannot straddle a two-field configuration update and observe a threshold pair that never existed, which the rule would treat as entirely unconfigured. The quantity limb likewise skips broker-notional orders, whose Quantity carries dollars rather than a unit count. It also measures against a new IPortfolioExposureProvider.TryGetTouchPrice seam rather than TryGetExecutablePrice - the latter deliberately returns the larger of mark and touch so sizing never under-measures a short, which puts a sell reference at the midpoint and would have rejected an ordinary marketable sell at the bid. A definite quantity breach is reported as such even when the quote is missing, so pricing gaps never mask a mistake that is not in doubt. Both the F# band and the reported evidence saturate rather than overflow, so an extreme price against a tiny reference yields the structured breach it plainly is instead of a generic evaluation failure. A priced order whose symbol has no reference price is refused as unmeasurable rather than approved, matching OrderNotionalRule, so a pricing gap cannot trip the breaker. Thresholds persist through the RiskRuleRuntimeService snapshot, are surfaced on the rules dashboard and config endpoints, and are carried on the browser RiskRuleConfig contract so the co-equal lane can read and set them. The 2026-08-14 correction also closes the fixed-income face-value sizing gap. The active gateway now resolves face-value percentage-of-par sizing through an explicit capability, so generic asset-class labels cannot import another broker's quantity semantics; the OMS carries that server-owned fact through initial risk, amendments, and working-order reserves. Alpaca Treasury and corporate face-value orders skip the share/contract quantity ceiling, while OrderNotionalRule and the working reserve measure their economic size as absolute quantity times price divided by 100 and ignore broker-notional metadata that the gateway does not route. The risk-status read routes are also not account-scoped - RiskEndpoints checks the global ViewTrades bit with EndpointAuthorization.HasPermission and never calls HasScopedPermissionAsync, so an operator whose ViewTrades is limited to particular accounts still sees every rule's RecentViolations, and those messages carry symbols, quantities, and entered prices for every account. This predates the fat-finger rule - the position-limit, concentration, and notional messages already carried the same detail - and the fix is endpoint-level scoping or per-entry redaction rather than a property of any one rule, so it is recorded here rather than folded into the rule change. Attached bracket, OCO, and OTO legs are also unmeasured - the rule reads the top-level LimitPrice and StopPrice only, while AlpacaBrokerageGateway routes operator-entered take_profit.limit_price, stop_loss.stop_price, and stop_loss.limit_price from metadata, so a market bracket order bypasses the price limbs entirely and a sound parent can carry a severely mistyped exit that activates the moment it fills; the fix reads the same metadata aliases the gateway reads and is deferred because it is a broker-specific seam on order classes this row's limbs were not scoped to. The 2026-08-14 correction also closes the amendment bypass: OrderManagementSystem now sends every explicitly supplied NewLimitPrice or NewStopPrice through the same composite validator and reservation path before gateway mutation, regardless of numeric direction, while null still means omitted and quantity-only reductions remain ungated. Focused regressions cover the dangerous sell-limit and buy-stop reductions plus an explicitly unchanged price, so stale equality cannot bypass revalidation. BLOCKING - percentage-of-par scaling reaches pre-trade measurement but NOT fill economics, so this row must not advance while both are true at once. The 2026-08-14 face-value work scales a fixed-income order's price by 100 in OrderNotionalResolver, the amendment resolver, and the portfolio exposure provider, so 100,000 face at 101.25 is admitted and measured as $101,250. Nothing carries that classification past acceptance: TradeExecutedEvent.GrossValue is FilledQuantity * FillPrice with no scaling, LedgerPostingConsumer posts that value directly to the securities and cash accounts, and PaperTradingPortfolio.ApplyFill takes a contract multiplier but no face-value flag. The same fill therefore books $10,125,000 - a hundred times the economic value - into cash, positions, exposure, and the ledger, on an order pre-trade validation explicitly endorsed at the correct size. This asymmetry is worse than scaling neither side, because the discrepancy now carries a passed risk check in front of it. Closing it means carrying the sizing classification through the execution report into fill processing and applying the same conversion to portfolio and accounting economics, with round-trip proof that a face-value fill books the same number pre-trade measured. Until then the face-value sizing path should be treated as unshippable rather than merely incomplete. Also remaining - both fat-finger thresholds default to null, meaning the shipped rule approves every order until an operator configures it, and NEITHER threshold can be set from the browser workstation: risk-control-panel.view-model.ts binds a single hard-coded DRAWDOWN_RULE_NAME and renders only the drawdown editor, so the panel surfaces the fat-finger status row without any control that enables it. This is a pre-existing shape rather than a regression - maxOrderNotional, the sibling threshold of the OrderNotionalRule this rule is patterned on, is equally absent from the panel and appears only in the TypeScript contract - so it is owned by the two-lane safety-control sweep below rather than by the rule change, but it bounds what the shipped rule guarantees on a fresh install and the criterion must not be discharged over it; the price-collar rule does not yet exist (it is the escalate-severity counterpart, expressible now that the risk engine treats severity as decisional); and the WPF and browser safety-control sweep (every button wired to the shared execution-control service or explicitly demoted) has not been audited, which requires a browser-lane inventory as well as the WPF rewire even though the browser cancel-all is already wired, and whose acceptance additionally requires a named Windows WPF build-and-test result because the quality gate's lanes run on Ubuntu where the WPF project builds as a stub. Deliberately deferred out of this row - the risk-engine blueprint's pre-trade decision journal (PR 2), its /api/risk/decisions read surface over the WAL (PR 3), and their WPF parity (PR 4) remain design-only. That blueprint's PR 1 already shipped the decisional-severity model the rule catalogue needed, so this row does not depend on them; the deferral is recorded here rather than only in planning documents so the follow-on work is neither silently lost nor mistaken for a W9 acceptance requirement.

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
| Last reviewed | 2026-08-10 |

### Current Summary

Rank 8 of the 2026-07 first-order improvement slate. Source-verification snapshot 2026-08-10 - substantial implementation already exists and the row needs criterion-level verification rather than greenfield build. RoleAuthorizationTests and the per-workspace tenant-scope endpoint suites (for example WorkstationEndpointsTests.StrategyTenantScope) cover authorization and tenancy behavior, and Meridian.Storage AuditChainService provides the hash-chained audit seam; The 2026-08-10 change lands the criterion-one instrument - EndpointAuthorizationCoverageTests mechanically enumerates every mapped mutating route from the composed application's endpoint graph and proves a zero-permission caller is rejected, with a five-route documented permissionless allowlist and a frozen 152-route remediation baseline ratchet: the sweep found 152 mutating routes (largely the pre-workstation /api surface) that today process permissionless requests - unguarded 200/201 writes, validate-before-authorize orderings, and test-host registration gaps - so the baseline tolerates exactly those known-debt routes, fails on any newly mapped unguarded route, and fails when a remediated route is not removed. The 2026-08-10 burn-down guarded the 40 highest-risk success-status routes (diagnostics, export, lean, messaging, storage, storage-quality, analytics repair, environment designer, provider failover, quant-lab parameter extraction) with Require endpoint filters mapped to family permissions, shrinking the baseline from 152 to 112. Remaining before acceptance - burn down the residual 112-route baseline (each fix shrinks it), prove fail-closed tenancy rejection for requests without resolvable scope across all write surfaces, and prove cross-process audit-chain serialization with verification tooling and tamper-detection tests aligned with the PRD-007 evidence-chain notes. Corrected 2026-08-11 - the accounting audit half of this row cannot be discharged against the PostgreSQL store alone, because IAccountingActionAuditStore resolves to PostgresAccountingConfigurationStore only where the database composition is registered and the workstation and WPF compositions fall through to FileAccountingConfigurationStore, whose AppendAsync adds the event with no predecessor hash and no chain verification; that file-backed path is what runs when nobody has stood up PostgreSQL, so it must be chained and verified too, or those mutations disabled in that posture, with local and WPF proof. Tenancy scope corrected 2026-08-11 - the remaining proof is not write-side only, and the fund-structure store is in scope rather than deferred as defence-in-depth: PostgresFundStructureService has no tenant column and no predicate, LoadSnapshotAsync takes no scope and loads every organization, business, fund, and relationship, /api/fund-structure/graph serves that snapshot, and mutations resolve parents from the same global view - so a tenant-A administrator can read tenant-B structure and link or mutate tenant-B nodes by id while RequireFundScopedWriteTenant proves only that the caller has some tenant. The criterion is categorical about reads as well, and two read paths are deliberately fail-open today: RequireFundProfileTenantScope passes a blank fund, a caller with no tenant scope, or an unavailable guard, and the storage TenantReadPredicate emits tenant_id is null or ... so unstamped legacy rows stay visible. Both must be tightened with regression tests before the row advances, and tightening the predicate needs a data migration BEFORE it needs a test - V_ledger_020, V_ledger_021, and fund-account migration 003 backfill from the fund_profile_tenancy registry and their own headers state that rows they cannot attribute stay null and fail-open, so rejecting null tenants without attributing them first hides fund-account, journal/period, and operations-continuity records from every scoped reader on an existing deployment. Change 9 therefore carries a deterministic backfill for the remaining unstamped rows plus a quarantine or upgrade-validation path for whatever it still cannot attribute. The same applies to accounts - InitialAccountBootstrapService creates the ordinary first-run administrator with a null company and UserAccountConfig.CompanyId is optional for stored accounts and MDC_USERS alike, so enforcement 403s the normal administrator of an existing local installation, not just the legacy MDC_USERNAME fallback. And enabling FundScopedWriteTenantOptions affects only routes already decorated with RequireFundScopedWriteTenant, which FundStructureEndpoints carries nowhere, so the undecorated writes beneath the /api/fund-structure group must be enumerated and gated or explicitly allowlisted as bootstrap.

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
| Last reviewed | 2026-08-10 |

### Current Summary

Rank 9 of the 2026-07 first-order improvement slate. Source-verification snapshot 2026-08-10 - the core surface already exists: Camt053StatementConnector and Bai2StatementConnector normalize through the W5X-CONNECT-001 connector seam alongside the statement-import command path, and the reconciliation lane carries a dedicated match kernel with sided statement-versus-ledger populations (ReconciliationMatchKernel, StatementRunMatcher, StatementRunMatchingService, ReconciliationMatchingEngine). Corrected 2026-08-11 after source verification retracted the evidence-only premise above - the standalone kernels do exist, but the LIVE path does not use them, so this row is implementation work and not verification alone. The retained internal population provider returns an empty ledger-transaction population by design (its own remarks say it awaits an authorized period-scoped ledger source and an agreed journal-to-transaction projection), and StatementRunMatcher invokes the one-to-one StatementMatchingEngine rather than anything split-capable. camt.053 and BAI2 are transaction statements, so with an empty transaction population every bank TRANSACTION row fails closed to a break - balance records are matched today, since both connectors emit StatementRecordKind.CashBalance, the retained provider loads internal cash balances, and MatchCash pairs them, and one-to-many outcomes can never reach live casework however the standalone kernels behave - tests written against those kernels would pass while the shipping path matched nothing. In scope before acceptance, therefore - source the ledger-transaction population by propagating the authority-verified StatementAccountingScope (FundProfileId, LedgerBookId, AccountingPeriodId, AsOfDate) already carried on the run request into InternalReconciliationPopulationContext, which currently drops it, rather than inventing a parallel unscoped lookup; decide the disposition of the direct POST /api/workstation/reconciliation/statement-runs caller, whose create contract carries no accounting scope at all and so cannot supply one once ledger reads require it; give the live path deterministic split matching by extending StatementMatchingEngine with the ReconciliationMatchKernel split primitives or adapting them behind it, since ReconciliationMatchingEngine handles only positions and cash and has no transaction model to swap to; carry group membership through the result path as well, because StatementMatchResult holds one broker and one internal evidence reference, StatementRunMatcher discards each result after incrementing MatchCount, and StatementRunMatchArtifact persists only that count - so two different split assignments of the same size produce an identical durable artifact and the row's own determinism and idempotency evidence is unobtainable no matter how correct the matching is; and implement bounded schema-aware parsing for both connectors, which currently decode the whole payload before parsing and enforce no record limit. Evidence still outstanding on top of that implementation work - golden-file regression coverage for both formats was not found in the FinancialOperations test tree, and the deterministic one-to-one/one-to-many/unmatched-break semantics with stable tie-breakers and idempotent re-runs need focused evidence against the live path rather than the standalone kernels. docs/product/w9-close-out-delivery-plan-2026-08.md carries the full source audit behind this correction.

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

## W10-MARK-001 - Fail-closed stale-mark policy and mark-age surfacing
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | planned |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-07-31 |

### Current Summary

Rank 1 of the 2026-07 W10 depth slate. Valuation freshness is not enforced by default, so a mark of any age can price a valuation without saying so, and freshness is governed by two overlapping controls rather than one. This row makes the fail-closed posture the default, resolves freshness to a single owner, and makes mark age visible wherever positions appear on both workstation lanes. It discharges RISK-STALE-MARK-001 and follows the same fail-closed truth doctrine as W9-TRUTH-001, which is why it is pulled ahead of the rest of the slate. An aged mark is real data presenting as current rather than simulated data presenting as real, so it is tracked as its own risk instead of counting against the simulation risk. Known source constraints, including how the current assessment treats a mark dated after the valuation, are recorded in docs/product/w10-depth-slate-2026-07.md.

### Exit Criteria

- A valuation cannot rest on a mark whose age or observation date falls outside policy, and the default posture blocks rather than accepts.
- Freshness is governed by one policy owner rather than two independently configured controls, and consolidation preserves every non-age gate the stricter control enforces today - minimum confidence, complete coverage, and a required observation date - rather than collapsing to age alone.
- Valuations blocked on mark freshness render as review-required with the offending positions named, on both the browser and desktop workstations.
- Mark age and observation date are visible wherever positions appear, and an override is bound to the position, mark observation, valuation date, and policy version it was approved for, expiring or requiring renewed review as the charter's override strategy requires, so it cannot become a standing bypass.
- Enabling the new default is preceded by a preview of how many current valuations it would block.
- Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the policy, both workstation surfaces, and the fail-closed tests.

### Source Modules

- `SRC-LEDGER`
- `SRC-APP`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W10-RECON-001 - Durable break lineage identity and run-over-run break diff
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | planned |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-07-31 |

### Current Summary

Rank 2 of the 2026-07 W10 depth slate. Reconciliation breaks have no identity that survives a run, so the same underlying break is unrecognizable between runs once its amount or as-of date moves. Without that identity an operator cannot see what is new, what is aging, or what cleared, and break age cannot drive escalation. This row establishes a stable break lineage and uses it to make queue state legible. It is sequenced ahead of clustering because grouping and aging are unsound without it. Known source constraints on the current identity derivation are recorded in docs/product/w10-depth-slate-2026-07.md.

### Exit Criteria

- A break keeps one identity across runs even when its amount, tolerance, or as-of date changes.
- A break that clears and later recurs is recognizable as the same lineage while remaining a distinct occurrence, so a recurrence neither inherits the age of the original nor overwrites the interval during which it was clear.
- The queue shows what is new, what remains open and for how long, and what cleared since the prior run, without hiding open work behind a default filter. A break reads as cleared only when the successor run durably recorded that reconciliation itself completed over the same account, source, period, and both the mapping and tolerance profile at a known matching version - a completed run that still carries open breaks counts, while a run whose recorded outcome is failed, validation-failed, or cancelled does not, and neither an unrecorded profile version nor a run status merely inferred from an absence of breaks is treated as agreement; after an inconclusive or scope-divergent run the prior break stays open rather than disappearing.
- Break age derived from that identity drives escalation before an SLA is missed, measured against the business and holiday calendars the SLA policy names rather than a weekends-only approximation.
- Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the lineage identity, the diff projection, the queue surfaces on both workstation lanes, and the identity-stability and calendar-boundary tests.

### Source Modules

- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-STRATEGIES`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W10-PROV-001 - Ledger-amount evidence subject and shared proof drawer
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | planned |
| Health | green |
| Priority | high |
| Owner lane | Workstation Shell and UX |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-07-31 |

### Current Summary

Rank 3 of the 2026-07 W10 depth slate and the platform bet the rest of the slate depends on. Both halves of amount-level provenance are already built and neither is connected, so an operator looking at a number cannot reach the evidence behind it. This row makes provenance reachable from any amount through one shared surface rather than a per-screen variant, and is the first concrete slice of W5X-OEG-001 rather than a duplicate of it. Rows 6, 7, 10, and 11 each depend on the same drawer. Known source constraints, including which service and component exist unwired today, are recorded in docs/product/w10-depth-slate-2026-07.md.

### Exit Criteria

- Any amount an operator sees can open its provenance from the surface it appears on, through one shared component rather than a per-screen variant.
- Provenance is served through the existing subject-addressed evidence surface rather than a parallel endpoint family beside it.
- The passport reflects retained evidence linked by stable subject and evidence identifiers scoped to the fund, ledger book, and period it belongs to, rather than inferring from surface labels or matching on names, symbols, and prose.
- Missing or stale provenance renders as review-required or blocked rather than an empty view that reads as proven.
- This item feeds W5X-OEG-001 rather than duplicating it, and states its WPF slice or an explicit deferral.
- Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the evidence subject, the shared drawer, the browser surface, and the missing-evidence tests.

### Source Modules

- `SRC-UI-SHARED`
- `SRC-CONTRACTS`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W10-RECON-002 - Break clustering and bulk-resolution activation
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | planned |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-07-31 |

### Current Summary

Rank 4 of the 2026-07 W10 depth slate and predominantly activation rather than construction. Bulk casework is already implemented end to end and no screen calls it, so resolving a hundred breaks with one shared cause still costs a hundred operator actions. Break classification also does not survive in a form the queue can group on. This row groups a run by cause and wires the existing bulk rails into the queue under the approval separation material breaks already require. Known source constraints, including what the classifier retains today, are recorded in docs/product/w10-depth-slate-2026-07.md.

### Exit Criteria

- An operator can resolve a group of related breaks in one governed action instead of one action per break.
- Every member of a resolved group retains its own case transition, actor, and evidence alongside the shared justification.
- A group requires the same approval separation an individual material break requires whenever any member is material or high-risk, and any additional group threshold is measured on gross exposure rather than a signed net that can offset a material member below it. That exposure is only ever aggregated across members whose amounts are genuinely comparable - value exposure within one currency or translated under a governed rate, and quantity exposure only across members that name the same instrument under a governed quantity unit - and a member whose instrument, unit, or rate cannot be established holds the group at the stricter approval rather than being summed into a total that mixes shares of one security with shares of another, or shares with cash.
- A preview shows the effect of a group action before any state changes, and the action that follows applies only to the breaks the preview showed in the state it showed them; drift returns the operator to a fresh preview rather than through.
- Break classification is queryable on a stored break rather than readable only in prose.
- Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the persisted classification, the grouping projection, the wired bulk surface, and the per-break evidence retention tests.

### Source Modules

- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-STRATEGIES`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W10-JRNL-001 - Durable recurring journal schedules and draft runner
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | planned |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-07-31 |

### Current Summary

Rank 5 of the 2026-07 W10 depth slate. The recurring journal primitive is complete and the service that owns it is unwired and holds its state in process memory, so planned occurrences never become journals and would not survive a restart if they did. A durable schedule store, a time-provider-driven worker, and an idempotent evidence-carrying intake path all already exist for monthly automated journals and are the pattern to follow. Occurrences become drafts for human approval and never post directly. Known source constraints, including which dependencies are currently in-memory, are recorded in docs/product/w10-depth-slate-2026-07.md.

### Exit Criteria

- Recurring schedules, their posting history, the template versions occurrences materialize from, and the period-lock state they honor all survive a restart, and the runner fails closed when any of that durable state is unavailable rather than treating a locked period as open.
- A due occurrence becomes a draft for human approval and never posts on its own, and a repeated run can produce neither a duplicate posting nor a second approval draft for the same occurrence - a retry claims or returns the draft that already exists, provided that draft was materialized from the same schedule and template definition the retry resolves. An occurrence claim that names an earlier definition fails closed until the existing draft is explicitly superseded or corrected, rather than silently returning a draft built from content the schedule no longer has.
- A recurring draft cannot reach approval without retained source evidence, and that evidence stays attached through posting.
- Occurrences blocked by a locked period name the lock owner and the governed reopen path rather than failing silently.
- The accounting draft queue shows what the calendar generated, what awaits approval, and what a period lock blocked.
- Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the durable store, the worker, the evidence gate, the draft queue on both workstation lanes, and the restart and idempotency tests.

### Source Modules

- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-LEDGER`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W10-TAX-001 - Tax character, wash-sale, and lot-relief operator surface
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | planned |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-07-31 |

### Current Summary

Rank 6 of the 2026-07 W10 depth slate. The wash-sale and tax-character engine landed in 2026-07 and terminates in a single report-pack artifact that no endpoint serves and no screen reads, so the character split and wash-sale impact it computes are invisible to every operator. This row makes that output operator-visible and adds a relief-method comparison for a pending disposal. Two source constraints bound it - the engine does not yet decompose wash-sale deferral within a mixed gain and loss disposal, and account relief policy is not effective-dated - and both are recorded with their owning types in docs/product/w10-depth-slate-2026-07.md.

### Exit Criteria

- Tax character, holding period, and wash-sale impact are visible on the positions and disposals they belong to rather than only inside an export artifact.
- An operator comparing cost-basis relief methods for a pending disposal sees realized gain, character split, and wash-sale exposure per method, and any figure the engine cannot yet compute completely renders as incomplete rather than as a settled zero. A disposal that could still attract a wash sale while its replacement window is open - one a wash-sale policy actually governs on its own sale date, that either realizes a loss or holds mixed gain and loss lots meaning a loss cannot yet be excluded, and whose known replacement acquisitions do not yet cover the entire quantity sold - is labelled provisional with the remaining window shown and is re-evaluated or governed-finalized once that window closes. A disposal no governing policy reaches, whose every relieved lot realizes a gain, or whose matched quantity and disallowed loss have already reached their maximum, reads as settled rather than being marked provisional for a window that cannot change it.
- Reopening or regenerating an earlier period reproduces the tax figures originally reported, including after a relief-policy change.
- Changing an account standing relief policy requires approval and a retained rationale, and every surface presents the comparison as decision support rather than tax advice.
- Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the operator surfaces on both workstation lanes, the relief comparison, the reproducibility guarantee, and the character and wash-sale tests.

### Source Modules

- `SRC-LEDGER`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W10-SEAM-001 - Unified close-readiness projection behind one shared contract
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | planned |
| Health | green |
| Priority | high |
| Owner lane | Workstation Shell and UX |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-07-31 |

### Current Summary

Rank 7 of the 2026-07 W10 depth slate. Close readiness has several independent owners encoding it incompatible ways, and the cross-lane operator console aggregates them in the browser client, so a controller can get different answers from different screens and the desktop lane is scheduled to reimplement the same aggregation rather than consume it. This row makes one readiness projection the shared source both workstation lanes consume. Known source constraints, including which services own which encoding today, are recorded in docs/product/w10-depth-slate-2026-07.md.

### Exit Criteria

- One close-readiness projection is the shared source both workstation lanes consume rather than each lane deriving its own, and the caller or an explicit operator selection establishes the complete close scope - fund profile, ledger book, fund account, entity, and period - rather than the projection inferring any dimension from whichever workflow it happened to select; every contribution answers for that declared scope, and a dimension that is missing, ambiguous, or mismatched blocks the projection rather than being merged into it.
- Every blocker names its type, count, severity, owner, and the records causing it.
- A contributing lane that is unregistered, failing, stale, or out of scope makes the projection incomplete and blocking rather than silently absent, so readiness is never reported because a contributor did not answer.
- The contributing services report into that projection instead of publishing independent readiness vocabularies, and the asset-class coverage service no longer reads as close readiness.
- The sequencing handshake with W8-WPF-PARITY-001 is recorded so the client-side aggregation is retired rather than duplicated into the desktop lane.
- Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the shared contract, the contributing services, both workstation consumers, and the blocker-projection tests.

### Source Modules

- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-UI-SHARED`
- `SRC-CONTRACTS`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W10-RECON-003 - Unified tolerance model and what-if replay workbench
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | planned |
| Health | green |
| Priority | medium |
| Owner lane | Accounting and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-07-31 |

### Current Summary

Rank 8 of the 2026-07 W10 depth slate and the largest row in the reconciliation arc. Editing a matching tolerance today is a blind change discovered on the next production run, and several tolerances an operator can express never reach the matching engine at all. Two prerequisites stand in front of the preview - one tolerance model the engine actually consumes, and retained run artifacts a replay can run against - and both are larger than the preview itself, which is why this row is the slate's most likely candidate for splitting or deferral. Known source constraints, including the competing tolerance shapes and the retention repositories that already exist unwired, are recorded in docs/product/w10-depth-slate-2026-07.md.

### Exit Criteria

- One tolerance model reaches the matching engine, so every tolerance an operator can configure is one the engine actually applies.
- Tolerances can be scoped to the account, currency, and transaction type they belong to, and activating one is a governed policy change under the charter's straight-through conditions - versioned approval with materiality caps, retained evidence reversible through governed correction, sampling review and a kill switch - rather than an operator-local edit, with material or high-risk classes still resolved per item.
- An operator can see the effect of a proposed tolerance change against retained runs before committing it, and what commits is the profile version the simulation previewed rather than whatever the profile has become since; drift returns the operator to a fresh preview, and the retained simulation is the justification for that exact committed profile.
- A replayed result differs from the original run only because the tolerance differs, which requires both sides of that run to be retained as of it - the external statement population and the internal positions, cash, and ledger activity it was matched against - and replay determinism is proven before any simulation result reaches an operator.
- Run-artifact retention reuses the existing stores rather than introducing a second artifact vocabulary and storage path.
- Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the unified model, the retained artifacts, the determinism proof, and the preview surface.

### Source Modules

- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W10-RECON-004 - Operator-taught match rules with promotion gate
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | planned |
| Health | green |
| Priority | medium |
| Owner lane | Accounting and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-07-31 |

### Current Summary

Rank 9 of the 2026-07 W10 depth slate. Every manual match an operator makes today is discarded, so a recurring counterparty pattern is as unmatched on its tenth appearance as on its first. This row retains what an operator teaches as a candidate rule that never acts until promoted, keeping the system inside governed autonomy rather than deciding on its own. Known source constraints, including how match attribution is persisted today and which identity predicates the matcher enforces, are recorded in docs/product/w10-depth-slate-2026-07.md.

### Exit Criteria

- A manual match teaches a candidate rule that never acts until an operator promotes it, and a promoted rule runs only inside the straight-through conditions the design charter requires - a human-approved versioned policy defining the eligible class with materiality caps, full retained evidence that stays reversible through governed correction, sampling review and a kill switch, and material or high-risk breaks still resolved per item.
- Promotion requires adjudicated outcomes meeting a stated sample size and precision bound, and a raw hit count is never sufficient on its own.
- A promoted rule retains every immutable identity predicate its match kind enforces - record kind, instrument, and currency - so it can never match across a boundary the underlying matcher keeps separate.
- Every match a learned rule produces names that rule and its promoting operator in retained evidence that survives a restart.
- Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the candidate capture, the promotion gate, the engine seam, and the attribution tests.

### Source Modules

- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-DOMAIN`
- `SRC-STRATEGIES`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W10-PERF-001 - Portfolio and investor return measurement
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | planned |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-07-31 |

### Current Summary

Rank 10 of the 2026-07 W10 depth slate and one of its four new-capability rows, alongside W10-RECON-003, W10-RECON-004, and W10-CONSOL-001. A brokerage-sourced single-period return already exists for a linked account, so this row must extend or deliberately supersede that seam rather than build beside it. What is absent is ledger-derived return measurement, time-weighted return, money-weighted return honoring cash-flow timing, and investor-level return over capital-account activity. It is ranked this late because it depends on the mark discipline in W10-MARK-001 and the proof drawer in W10-PROV-001 to be honest. Known source constraints, including the missing pricing series and residual value, are recorded in docs/product/w10-depth-slate-2026-07.md.

### Exit Criteria

- Operators and investors can see portfolio and investor-level returns derived from Meridian's own records rather than only from a brokerage feed.
- The existing brokerage performance seam is extended or explicitly superseded with the reason recorded, and no parallel performance API is introduced beside it.
- Reported returns are correct across periods containing external capital flows, and any convention approximation is documented and labeled on the figure.
- A return spanning more than one currency is either reported per currency or translated into one declared presentation currency at each flow's and the terminal value's as-of rate; nominal amounts in different currencies are never summed.
- Returns resting on activity the ledger has not posted, or on incomplete inputs including a missing translation rate, render as labeled pro-forma or review-required rather than as the reported return, and every figure names its convention, period, currency, and completeness. A money-weighted return that fails to converge, or whose cash-flow series admits more than one rate, is review-required under a declared convention rather than a single reported number.
- Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the brokerage-seam disposition, the shared kernel, the operator surface, and golden-file tests of the return kernels against worked examples.

### Source Modules

- `SRC-BACKTESTING`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-FSHARP`
- `SRC-LEDGER`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W10-CONSOL-001 - Intercompany elimination on consolidated ledger views
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | planned |
| Health | green |
| Priority | medium |
| Owner lane | Accounting and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-07-31 |

### Current Summary

Rank 11 of the 2026-07 W10 depth slate. Consolidation elimination already exists as an accounting treatment kind selected through the accounting policy rule, but no rule produces eliminations and the consolidated trial balance sums its sub-ledgers with no elimination step, so consolidated views double-count intercompany balances. This row defines that treatment on the existing policy seam rather than introducing a parallel discriminator, and adds an unmatched-intercompany report. Scope is deliberately limited to wholly owned fully consolidated entities so the work stops short of the deferred capital-structure modeling boundary; partial consolidation and minority interest are excluded. Known source constraints, including how entity and counterparty dimensions relate and where authoritative ownership lives, are recorded in docs/product/w10-depth-slate-2026-07.md.

### Exit Criteria

- Consolidated views no longer double-count intercompany balances and state whether they are presenting gross or eliminated figures, and an eliminated figure reflects only approved and posted eliminations, with a proposed draft visible solely as a labeled preview.
- Eliminations are proposed as reviewable drafts on the existing approval rail and never post automatically, driven by a rule on the existing policy seam with no parallel discriminator beside it. Approval fails closed when either source balance has moved since the draft was computed, and a posted elimination is corrected through a linked reversing or adjusting entry rather than replaced in place.
- Rerunning the same perimeter and as-of date cannot produce a duplicate elimination for the same pair, and a pair is an exact reciprocal match on both the posting entity and its counterparty rather than the counterparty alone, with a missing or ambiguous dimension blocking the draft.
- The consolidation perimeter is enforced from authoritative ownership data rather than asserted, and an entity outside scope is rejected rather than silently eliminated.
- Balances are compared and eliminated in one declared consolidation currency translated at governed as-of rates, with translation differences retained separately and elimination blocked when a rate is unavailable, so a currency gap never reads as an intercompany mismatch. Balances that still do not agree between the two sides surface as their own reported break class rather than being netted away.
- Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the treatment rule, the perimeter enforcement, the elimination drafts, and the unmatched-intercompany tests.

### Source Modules

- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-LEDGER`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`
