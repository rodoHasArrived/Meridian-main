# Meridian Design Document — Version 0.25

**Status:** canonical  
**Owner:** core-team  
**Reviewed:** 2026-07-13
**Source:** Draft v1.0 imported from `C:\Users\Andrew James Rowden\.codex\attachments\2bedc368-4dca-449f-923b-b098cf8bb4d5\pasted-text.txt`; Version 0.16 extends the roadmap and source-module registry evidence with the v0.15 accounting records package plus current Carta Fund ERP, Carta Data Warehouse, Carta Management Company Administration, FundStudio fund administrator, FundStudio managed-services, FundStudio general-ledger/accounting, and Modern Treasury ledger research. Version 0.17 adds the shared Financial Record Explorer productization target from `C:\Users\Andrew James Rowden\.codex\attachments\e76a7c8a-33a1-45f6-bf2e-036d6635920d\pasted-text.txt`. Version 0.18 incorporates the operational proof layer market-gap update from `C:\Users\Andrew James Rowden\.codex\attachments\7c4bee43-4269-4284-8747-2bdeadf0287b\pasted-text.txt`. Version 0.19 adds the no-code provider integration manifest design from `C:\Users\Andrew James Rowden\.codex\attachments\ad0040bf-8757-4233-8689-ae400f822b75\pasted-text.txt`. Version 0.20 clarifies the customer-neutral operational-finance architecture: fund operations remain first-class, but the core model starts from organization, entity, portfolio, account, book, period, operational event, evidence, approval, journal, report, and audit trail. Version 0.21 incorporates the LedgerGraph OS / Close, Data and Evidence Control Tower product-positioning concept supplied on 2026-06-22, emphasizing evidence-native operating-system positioning for private funds and family wealth, whole-balance-sheet contract modeling, Number Passports, shadow-mode adoption, and deterministic controls around AI. Version 0.22 reconciles the design document with current roadmap and implementation-tracker evidence: W1-W5, `W5X-FREX-001`, and `W5X-FINOPS-001` are completed evidence-backed baselines, while `W6-BTSTUDIO-001` and `W7-LIVE-001` remain planned or gated productization work. Version 0.23 promotes the bounded `W7-LIVE-001` governance gate to complete while keeping broader live execution productization and live portfolio operations outside the completed baseline. Version 0.24 marked WPF product/UI work deferred while retaining existing WPF compatibility, validation, and maintenance support. Version 0.25 reactivates the WPF desktop workstation as an active, co-equal product/UI lane alongside the browser workstation, with an immediate focus on web-UI parity over shared contracts and read models, tracked as `W8-WPF-PARITY-001` and detailed in `docs/development/wpf-web-ui-alignment-plan.md`. Version 0.25 supersedes every earlier "WPF product/UI work is deferred" or "WPF parity is deferred" statement in this document: those areas are now active parity work, not deferred lanes, while the shared-first rule (browser and WPF both consume `Meridian.Ui.Services`, `Meridian.Ui.Shared`, and `Meridian.Contracts` rather than forking product state) is retained unchanged.

## 1. Product Vision

Meridian is a modular, configurable financial operations platform for fund administrators, registered investment advisors, family offices, and other investment organizations. The platform helps financial operations professionals acquire, validate, reconcile, govern, analyze, forecast, and report on financial data through a single auditable workflow.

Fund management is a supported specialization, not the platform root model. Core product language,
contracts, and new architecture should use customer-neutral concepts such as organization, entity,
portfolio, account, book, period, transaction, operational event, obligation, evidence, approval,
journal, report, and audit trail. Use fund, investor, capital account, and fund event only where
the workflow is explicitly fund/private-capital specific.

Meridian should not initially try to replace every external system. Instead, it should become the operational system of record for validated workflows, evidence, reconciliations, decisions, and certified reporting outputs. For ledger records specifically, Meridian is the source of all ledger truth; external accounting systems contribute read-only evidence and reconciliation signals unless an approved publishing workflow explicitly exports Meridian-owned entries.

The current product scope is deliberately narrower than the full long-term domain catalog. Completed
W1-W5, shared Financial Record Explorer, Financial Operations control-center, statement connector,
and bounded W7 live-readiness governance work establish the current evidence-backed baseline.
Evidence Vault productization, statement reconciliation onboarding, and WPF workstation parity are
active work. Other capabilities can proceed when current source, roadmap, or user direction supports
them; the prior baselines remain evidence and sequencing anchors rather than development ceilings.
The WPF desktop workstation is an active co-equal UI lane whose immediate focus is web-UI parity
over shared contracts (`W8-WPF-PARITY-001`).

### Core Vision Statement

> Meridian helps financial operations professionals transform fragmented financial data into trusted, auditable operational outcomes.

Meridian should not merely show an operational number. It should prove the number by preserving the
chain from source evidence through normalization, validation, reconciliation, ledger impact,
capital-account impact, report usage, delivery evidence, and audit history.

### Version 0.21 Positioning Addendum: Evidence-Native Financial Operating Layer

Meridian's sharper product position is an evidence-native operating layer for private funds,
family offices, and adjacent investment organizations. A useful internal working label is
`LedgerGraph OS`, but the naming is less important than the operating principle: Meridian should
not be another portfolio dashboard. It should be the governed financial system underneath the
dashboard, connecting every entity, asset, liability, obligation, document, approval, ledger impact,
and report value so users can ask any question and verify every answer.

This addendum is planned product direction, not an implementation-complete claim. It narrows the
near-term market entry wedge to a **Close, Data and Evidence Control Tower** that can sit above
existing spreadsheets, general ledgers, portfolio systems, banks, custodians, GP portals, and
document stores before Meridian becomes the customer's native accounting system.

Differentiation should emphasize five themes:

1. **Verifiable financial data**: provenance, evidence retention, reconciliation state, approvals,
   and report-line lineage are first-class user-visible product objects.
2. **Whole-balance-sheet modeling**: assets, liabilities, commitments, guarantees, collateral, tax
   obligations, intercompany balances, and contingent exposures are modeled as equal citizens.
3. **Lower-risk implementation**: shadow-mode onboarding produces read-only parallel views,
   opening-balance reconciliations, evidence-backed consolidation reports, and close-readiness
   scores before a customer migrates official books.
4. **Contract-driven extensibility**: new asset and liability coverage should arrive through
   governed packs that define schemas, lifecycle events, valuation methods, accounting rules,
   validations, and reporting taxonomy without redesigning the core ledger.
5. **Collaborative distribution**: external auditor, tax, attorney, investment-manager, valuation,
   banking, investor, family-member, and advisor roles should be inexpensive or free enough that
   evidence review, document requests, approvals, and report delivery expose the platform to the
   customer's professional network.

The concise product promise is:

> Meridian is the evidence-native financial operating system for private funds and family wealth.
> It connects every entity, asset, liability, document, and approval so finance teams can close
> faster, model anything, and verify every number.

### Core User Objective

The primary user hires Meridian to know:

* What happened
* Why it happened
* Whether it can be trusted
* What needs to be reviewed, approved, reconciled, paid, forecasted, or reported

### Active Scope Gate

A new capability belongs in active product scope when current source evidence, roadmap status, or
user direction supports it. Operational proof remains a core product strength, but it is not a
development ceiling for other Meridian capabilities.

### External Service Inspiration

Meridian should draw product inspiration from adjacent private-capital, fund-administration, and
treasury-ledger platforms without copying their product boundaries or making unverified
implementation claims. Research checked on 2026-06-08 and continued on 2026-06-09 points to useful
operational patterns:
connected fund records, event-based fund accounting, stakeholder-ready evidence, management-company
administration, real-time portfolio control, administrator-grade close discipline,
treasury-grade ledger invariants, document-to-accounting evidence, private-capital close control,
admin-neutral verification, governed automation, and cross-domain proof chains.

| Reference | Useful Product Pattern | Meridian Translation |
| --- | --- | --- |
| [Carta Fund Administration](https://carta.com/fund-management/fund-administration/), [Carta Fund Management](https://carta.com/fund-management/), [Carta Data Warehouse](https://docs.carta.com/api-platform/docs/overview), and [Carta Management Company Administration](https://carta.com/fund-management/manco-administration/) | A connected private-capital operating suite for fund administration, event-based fund accounting, capital calls, distributions, investments, LP closings and support, KYC/AML, tax and K-1 support, SPVs, portfolio valuations, management-company expense allocation, intercompany balances, cash reconciliation, budget/cash planning, and queryable fund intelligence. | Strengthen Meridian as the private-capital operational record: first-class fund events, capital account evidence, LP/stakeholder report packages, tax and audit support files, portfolio valuation inputs, management-company operating records, certified operational datasets, and report-line provenance. |
| [FundStudio Fund Administrators](https://fundstudio.com/fund-administrators/), [FundStudio Managed Services](https://fundstudio.com/managed-services/), [FundStudio General Ledger/Accounting](https://fundstudio.com/general-ledger-accounting/), and [FundStudio Portfolio Management](https://fundstudio.com/portfolio-management/) | Administrator-grade middle/back-office control across portfolios, custodians, primes, reconciliation, shadow NAV, multi-book/multi-currency accounting, locked periods, recurring journals, year-end close, capital-account and shadow-NAV packs, role-based JE/report/period-lock permissions, immutable logs, onboarding templates, T+0 capture, T+1 reconciliation, file distribution, SLA tracking, and reporting. | Strengthen Meridian's portfolio/accounting control plane: multi-asset operations, cash and collateral monitoring, reconciliation queues, close packages, versioned NAV support, journal evidence, fund/book/period/report admin scopes, delivery logs, exception SLAs, and drill-through reporting. |
| [eFront Platform](https://www.efront.com/) and [SS&C Advent Geneva](https://www.advent.com/geneva/) | Private-markets platforms emphasizing broad asset-class coverage, reporting, auditability, and front-to-back workflows that support both asset managers and asset servicers. | Keep breadth goals scoped and proof-first: a fund-event and ledger-first control graph where each object is evidence-reconstructable from source to report and delivery. |
| [Modern Treasury Ledgers](https://docs.moderntreasury.com/ledgers/docs/overview), [ledger guarantees](https://docs.moderntreasury.com/ledgers/docs/ledgers-guarantees), and ledger engineering posts on [transaction models](https://www.moderntreasury.com/journal/how-to-scale-a-ledger-part-iii), [immutability and double-entry](https://www.moderntreasury.com/journal/how-to-scale-a-ledger-part-v), and [optimistic locking](https://www.moderntreasury.com/journal/designing-ledgers-with-optimistic-locking) | Immutable double-entry ledgering, idempotent writes, atomic transactions, per-currency balancing, pending/posted/archived transaction states, append-only versions, and concurrency controls. | Make Meridian-owned ledger records treasury-grade: posted entries are immutable, corrections use reversing or adjusting journals, writes are idempotent and atomic, balance-affecting records are per-currency balanced, and authoritative ledger writes fail closed under stale versions or missing evidence. |
| [BlackLine Financial Close](https://www.blackline.com/products/financial-close/) | Enterprise close-management tools focus on centralized close tasks, role-based controls, exception handling, and audit-compliant workflow visibility. | Bring BlackLine-style governance discipline into FinOps: explicit task ownership, SLA status, approval/state transitions, and evidence-ready close readiness signals mapped into the operational record graph. |

Meridian should not treat this as permission to build a full cap-table system, outsourced services
operation, live payment processor, broad investor portal, or autonomous-agent workflow that bypasses
operator evidence. Those remain separate product decisions unless the roadmap moves them into scope.

### External Functionality Translation Requirements

External offerings should translate into Meridian-owned software capabilities, not copied service
promises:

* Operational events should become first-class operational records across customer types: trades,
  cash movements, invoices, fees, subscriptions, redemptions, transfers, corporate actions,
  capital events, valuations, reconciliations, adjustments, approvals, close tasks, and fund-event
  specializations such as formation/closing, capital calls, distributions, tax requests, audit
  requests, and dissolution/wind-down support.
* Capital accounts should be governed ledger projections with commitment, contribution,
  distribution, allocation, NAV, statement, and evidence lineage.
* LP support should start as governed package production and delivery evidence: capital notices,
  distribution notices, statements, K-1/tax support packages, audit packages, stakeholder recipient
  lists, and amendment/restatement trails. A broad LP portal remains deferred.
* Data warehouse functionality should map to certified operational data marts, queryable evidence,
  report-line provenance, refresh cadence metadata, and secure exports into BI tools.
* Management-company administration should cover expense allocation, intercompany balances,
  management-fee evidence, bank/card feeds, cash reconciliation, budget/cash-plan snapshots, and
  bill-pay or payment-intent linkage. Native live bill pay remains later productization.
* FundStudio-style admin controls should drive organization/entity/portfolio/account/book/period/report
  administration: multi-book ledgers, locked periods, period reopen evidence, journal templates,
  recurring journals, year-end-close workflows, portfolio-specific pricing rules, onboarding
  templates, and immutable logs for every posting, lock, export, and delivery event.
* Middle-office managed-service patterns should become internal workflow primitives: T+0 booking,
  T+1 trade/cash/position reconciliation, true-break escalation, SLA timers, normalized file
  distribution to admins/custodians/counterparties, and archived delivery logs.
* AI or agent-like automation is acceptable only as reviewed discrepancy detection, extraction, or
  draft-preparation assistance; it cannot bypass operator approval, evidence, ledger controls, or
  period locks.
* Fund events should remain fund/private-capital specializations of the broader operational-event
  command spine that connects evidence, workflow, treasury, ledger, capital accounts,
  reconciliation, reporting, delivery, tax, and audit impact.
* Shadow accounting and administrator tie-outs should be evidence-native: every variance needs
  source records, a root-cause explanation, reviewer state, ledger impact, close effect, and report
  effect before it can be treated as resolved.
* Private-capital close support should connect data receipt, reconciliation, journals, capital
  accounts, valuation support, NAV tie-out, investor statements, package delivery, and period locks
  into one readiness model.
* Document extraction should become accounting-grade evidence only after it is validated, linked to
  fund events or journals, reviewed, and frozen into close, tax, audit, or reporting manifests.
* Reconciliation should act as a close-blocking operating control: exceptions must expose owner,
  SLA, materiality, root cause, supporting evidence, approval state, and the specific NAV, close,
  capital-account, tax, audit, or report outputs they block.
* Close governance should be operationally safe by default: each exception state change must capture owner, age, due date, and policy check outcomes before release-related actions are allowed.
* Payment work should begin as payment intent and cash evidence, not premature live execution:
  request, approval, expected cash movement, bank confirmation, ledger intent, reconciliation, and
  report linkage are the near-term product surface.
* Authority must be scoped by tenant, organization, legal entity, portfolio, account, book, period,
  document, report package, delivery record, amount limit, segregation-of-duties posture, and fund
  or investor scope where the workflow is fund/private-capital specific.

---

## 2. Target Customer Organizations

Meridian is intended to support several related customer types through one configurable platform rather than separate products.

### Primary Customer Types

| Customer Type                  | Primary Needs                                                                                                       |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------------- |
| Fund Administrators            | Reconciliation, NAV support, investor reporting, capital activity, audit evidence, workflow management              |
| Private Fund Managers / Fund CFOs | Fund operations, capital accounts, fund events, portfolio valuations, tax/audit support, LP reporting, data exports |
| Registered Investment Advisors | Portfolio operations, client reporting, data aggregation, performance review, advisor workflows, compliance support |
| Single Family Offices          | Entity management, trust and beneficiary reporting, alternative assets, treasury, payments, consolidated reporting  |
| Hybrid / Institutional Users   | Configurable workflows across operations, reporting, financing, planning, and governance                            |

### Product Strategy

Meridian should be one platform with configurable tenant profiles, not separate applications for each customer type.

Example profiles:

* Fund Administrator Profile
* RIA Profile
* Single Family Office Profile
* Private Credit / Alternative Asset Profile
* Hybrid Institutional Profile

The platform must also support scoped authority inside those profiles. A user is not only
"Accounting" or "Administrator"; production authorization must be able to answer whether that user
has that role or permission for a specific tenant, organization, fund, portfolio, legal entity, or
account.

---

## 3. User and Stakeholder Model

### Primary Operator Persona

## Financial Operations Professional

A person responsible for ensuring financial data is accurate, complete, reconciled, auditable, and available to support investment and business decisions.

This persona includes:

* Fund Administrators
* Investment Accountants
* Operations Analysts
* Portfolio Operations Specialists
* Treasury Operations Personnel
* RIA Operations Staff
* Family Office Operations Staff
* Reconciliation Specialists
* Reporting Analysts

### Primary Operator Workflow

```text
Import
→ Validate
→ Reconcile
→ Investigate
→ Approve
→ Report
```

### Secondary User Personas

These users may use Meridian regularly, but they are not the primary operational users.

| Persona                    | Primary Goal                                                             |
| -------------------------- | ------------------------------------------------------------------------ |
| Investment Analyst         | Research investments, analyze securities, build investment theses        |
| Portfolio Manager          | Review holdings, exposures, performance, and allocation decisions        |
| Trader                     | Review positions, liquidity, and execution-related information           |
| Risk Manager               | Monitor exposure, concentration, credit, liquidity, and operational risk |
| CIO / Investment Principal | Oversee portfolio decisions and approve strategic recommendations        |

### Stakeholder Personas

These users primarily consume information rather than operate workflows.

| Persona                             | Primary Goal                                                            |
| ----------------------------------- | ----------------------------------------------------------------------- |
| Fund Investor / Limited Partner     | View performance, statements, capital activity, and documents           |
| RIA Client                          | Understand portfolio performance, allocation, and advisor communication |
| Family Office Beneficiary           | Understand family assets, distributions, and relevant reports           |
| Trustee                             | Review fiduciary information, approvals, and reporting packages         |
| Board / Investment Committee Member | Review governance materials and strategic reporting                     |
| Auditor                             | Review evidence, reconciliations, approvals, and source documentation   |

---

## 4. Persona Matrix

| Persona                           | Category                     | Goals                                                        | Data They Care About                                               | Actions They Can Take                                     | Frequency           |
| --------------------------------- | ---------------------------- | ------------------------------------------------------------ | ------------------------------------------------------------------ | --------------------------------------------------------- | ------------------- |
| Financial Operations Professional | Primary Operator             | Ensure financial data is accurate, reconciled, and auditable | Transactions, positions, cash, prices, exceptions, reconciliations | Import, validate, reconcile, investigate, approve, report | Daily               |
| Investment Accountant             | Primary Operator             | Produce accurate accounting and reporting support            | Journals, holdings, income, accruals, paydowns, realized activity  | Reconcile, classify, adjust, document, export             | Daily               |
| Reconciliation Analyst            | Primary Operator             | Resolve breaks quickly and clearly                           | Source records, custodian data, bank data, ledger data             | Match, explain, assign, resolve, escalate                 | Daily               |
| Fund Accountant                   | Primary Operator             | Support NAV, fund reporting, and investor activity           | Fund positions, capital activity, valuations, expenses             | Review, calculate, approve, report                        | Daily / Monthly     |
| Operations Manager                | Primary Operator / Manager   | Monitor operational health and team workload                 | KPIs, breaks, workflow queues, aging, SLA metrics                  | Assign, approve, escalate, monitor                        | Daily               |
| Data Operations Analyst           | Primary Operator             | Ensure data pipelines and provider feeds are healthy         | Import runs, provider status, file/API data, data quality issues   | Validate, rerun, monitor, investigate                     | Daily               |
| Treasury Operations Specialist    | Primary Operator             | Manage liquidity and cash movement                           | Cash balances, payments, bank accounts, wires, ACH, distributions  | Initiate, review, approve, reconcile                      | Daily               |
| Reporting Analyst                 | Primary Operator             | Produce accurate reports and packages                        | Approved data, templates, statements, evidence, distribution lists | Build, schedule, generate, distribute                     | Daily / Monthly     |
| Portfolio Manager                 | Investment User              | Monitor and manage portfolio outcomes                        | Positions, exposures, performance, benchmarks, risk                | Analyze, review, approve, monitor                         | Daily / Weekly      |
| Investment Analyst                | Investment User              | Research investments and opportunities                       | Securities, fundamentals, market data, research notes              | Research, compare, evaluate, draft memos                  | Daily               |
| Quantitative Researcher           | Investment User              | Develop and validate strategies                              | Historical data, alternative data, strategy results                | Backtest, simulate, optimize                              | Daily / Weekly      |
| Trader                            | Investment User              | Execute or monitor trading activity                          | Orders, positions, liquidity, execution data                       | Submit, modify, review, monitor                           | Daily               |
| Risk Manager                      | Governance / Investment User | Monitor investment and operational risk                      | Exposures, limits, concentrations, stress results                  | Review, investigate, escalate, report                     | Daily / Weekly      |
| CFO                               | Executive                    | Oversee financial accuracy and liquidity                     | Cash, reports, accounting summaries, exceptions                    | Review, approve, direct                                   | Weekly / Monthly    |
| CIO                               | Executive                    | Oversee portfolio strategy and risk                          | Performance, allocations, risk, recommendations                    | Review, approve, direct                                   | Weekly / Monthly    |
| Controller                        | Governance                   | Ensure accounting governance and audit readiness             | Journals, reconciliations, reports, evidence                       | Review, sign off, approve                                 | Weekly / Monthly    |
| Compliance Officer                | Governance                   | Ensure policies and controls are followed                    | Audit trails, approvals, access logs, policies                     | Review, audit, approve, escalate                          | Weekly / Monthly    |
| Fund Investor / LP                | Stakeholder                  | Monitor investment performance and capital activity          | Statements, returns, documents, capital account activity           | View, download, message                                   | Monthly / Quarterly |
| RIA Client                        | Stakeholder                  | Understand personal portfolio and advisor reports            | Performance, allocations, holdings, reports                        | View, download, communicate                               | Monthly / Quarterly |
| Family Beneficiary                | Stakeholder                  | Understand family assets and distributions                   | Portfolio summaries, distributions, reports, documents             | View, review                                              | Monthly / Quarterly |
| Trustee                           | Stakeholder                  | Exercise fiduciary oversight                                 | Reports, distributions, approvals, legal documents                 | Review, approve                                           | Monthly / Quarterly |
| Auditor                           | External / Governance        | Verify accuracy and evidence                                 | Source data, reconciliations, audit trails, approvals              | Inspect, request, review                                  | Quarterly / Annual  |
| System Administrator              | Administration               | Maintain platform health and access                          | Users, logs, integrations, settings                                | Configure, monitor, manage                                | Daily / Weekly      |
| Security Administrator            | Administration               | Protect platform and manage permissions                      | Roles, access logs, policies, user scopes                          | Grant, revoke, review, audit                              | Daily / Weekly      |
| Integration Administrator         | Administration               | Maintain provider and system connections                     | API credentials, SFTP settings, mappings, import runs              | Configure, test, monitor                                  | Weekly              |

### Persona-Specific Operating Journeys

The persona matrix identifies who Meridian serves. The operating journeys define how those personas
move through the proof chain and what Meridian must retain for each decision.

| Journey | Trigger | Inputs | Primary surface | Decisions and approvals | Blocked states | Output | Retained evidence |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Data Operations Daily Workflow | Scheduled provider/file arrival, manual import request, provider-health alert, schema-drift alert, or downstream close/report blocker. | Provider payloads, SFTP/API files, credentials status, mapping profile, schema version, prior import run, validation rules, lineage manifest, downstream dependency map. | `Data` workspace import-run queue and provider validation packet, shared into Accounting and Reporting blockers. | Accept/reject import, classify severity, choose repair/replay path, escalate provider issue, approve remapped schema, release certified dataset. | Missing source, stale provider credentials, schema drift, validation failure, duplicate source key, incomplete lineage, unresolved severity-high issue, or downstream blast radius not acknowledged. | Certified import run, rejected run with repair instructions, replay request, provider incident, or blocked downstream output. | Import Run Evidence Contract with source hash, payload location, mapping version, validation results, reviewer, repair/replay actions, alert severity, affected accounting/reporting/close objects, and audit event IDs. |
| Fund Accountant Monthly Close Workflow | Period close opens, NAV support due, administrator package arrives, late activity appears, or capital activity requires statement support. | Trial balance, positions, cash, valuations, accruals, fund events, capital activity, administrator package, reconciliation cases, journal drafts, period-lock policy, report package checklist. | `Accounting` close cockpit, ledger explorer, capital account workbench, and report-pack readiness view. | Approve/reject journals, approve reversals or adjustments, certify NAV support, assign reviewer, release investor statements, approve amendment or restatement path. | Required source missing, unreconciled cash/positions, stale marks, unresolved valuation exceptions, unapproved journals, capital-account roll-forward mismatch, missing reviewer, period lock conflict, or statement support incomplete. | NAV Readiness Packet, closed period, investor statements, amendment/restatement packet, tax/K-1 support package, or blocked-close report. | NAV Readiness Packet with close-state ladder, reviewer ownership, journal/reversal approval chain, capital-account lineage, statement contents, administrator tie-out, blocked outputs, period-lock state, and support manifest. |
| CFO Weekly / Monthly Control Review | Weekly control meeting, month-end close checkpoint, material exception, liquidity concern, report package deadline, or board/investor package review. | Cash confidence, bank evidence, material breaks, close readiness, NAV readiness, report package status, approval queue, stale-data report, liquidity watchlist, blocked outputs. | Executive financial control brief in `Accounting`, `Reporting`, and `Portfolio` review routes. | Approve material adjustments, direct escalation, approve package release, require restatement, authorize scoped payment request, approve exception waiver, or hold downstream reporting. | Unclear cash confidence, unapproved material journal, stale data beyond SLA, unresolved material break, missing report-package approval, incomplete delivery evidence, or blocked stakeholder output. | Executive Financial Control Brief, approved action list, held package, escalation memo, or board-ready control summary. | Control brief with materiality thresholds, owner, SLA, approval need, stale-data flags, blocked outputs, reviewer comments, decision history, and linked audit events. |
| Compliance Officer Review Workflow | Scheduled access certification, policy review, audit request, exception escalation, permission change, legal hold, or report delivery review. | Audit events, scoped access assignments, policy mappings, approval history, retention policy, legal-hold markers, user/fund/entity/report scopes, delivery logs. | `Settings` access review, audit timeline, evidence vault, and compliance-filtered queues; no separate root Governance workspace. | Certify/revoke access, approve exception, map policy to action, place legal hold, approve retention disposition, request additional evidence, or escalate segregation-of-duties breach. | Missing audit event, orphan permission, stale certification, policy/action mismatch, retention conflict, legal-hold ambiguity, unowned exception, or evidence manifest gap. | Scoped Access Review Packet, audit request response, access revocation, policy exception, or legal-hold evidence set. | Audit Event Catalog plus access review packet with actor, action, scope, before/after state, reason, approval, source object, retention class, legal-hold state, and route mapping. |
| Portfolio Manager Daily Review | Start-of-day review, material position change, benchmark underperformance, stale mark, breach alert, unreconciled position, liquidity watchlist event, or valuation exception. | Holdings, positions, exposure, performance, benchmarks, valuations, liquidity, breaches, unreconciled positions, stale marks, source confidence, evidence drill-down. | `Portfolio` workspace daily review, portfolio explorer, proof drawer, risk and reconciliation links. | Approve watchlist classification, acknowledge breach, request valuation review, approve non-accounting commentary, escalate unresolved position, or hold investment-facing report commentary. | Stale marks, unreconciled positions, unresolved breach, missing source evidence, benchmark underperformance without explanation, valuation exception, or liquidity watchlist item without owner. | Portfolio Daily Review Packet, escalation, valuation-review request, commentary draft, or held report input. | Daily packet with material changes, benchmark variance, stale marks, breaches, liquidity watchlist, valuation exceptions, PM acknowledgements, permissible approvals, and links to reconciliation/ledger evidence. |
| LP / Stakeholder Report Package Journey | Capital call, distribution, quarterly statement, K-1/tax support, audit support request, board package, amendment, restatement, or recipient-list change. | Approved report data, capital account activity, statement contents, notices, recipient list, delivery rules, package version, amendment/restatement lineage, stakeholder entitlement scope. | `Reporting` package builder, delivery evidence view, stakeholder package history, and request workflow; broad self-service portal remains deferred. | Approve package, approve recipient list, release delivery, amend/restated package, respond to request, limit stakeholder view to entitled outputs. | Unapproved report line, incomplete recipient approval, missing delivery evidence, stale capital account, unresolved restatement lineage, entitlement mismatch, or missing support package. | Stakeholder Delivery / Restatement Packet, delivered statement package, held package, amended package, or request response. | Delivery packet with package contents, capital activity support, recipient list, channel, timestamp, approval chain, view/request permissions, delivery evidence, amendment/restatement lifecycle, and audit event IDs. |

### Reusable Evidence Packet Objects

Evidence packets are first-class product objects, not ad hoc attachments. They should be durable,
versioned, permission-scoped, and reconstructable from the Operational Evidence Graph.

| Evidence packet | Purpose | Required fields | Owner and approver | Blocked downstream outputs |
| --- | --- | --- | --- | --- |
| Import Run Evidence Contract | Prove that a provider/file/API import is complete, mapped, validated, lineage-safe, and replayable. | Import ID, source kind, source URI or vault reference, source hash, receipt timestamp, provider/account scope, mapping version, schema version, validation results, extraction confidence, repair/replay rule, severity, owner, reviewer, release state, downstream dependency map, audit event IDs. | Data Operations owns; Operations Manager or domain owner approves release when the run affects close/reporting. | Certified dataset, reconciliation run, close package, NAV readiness, report package, and stakeholder delivery. |
| NAV Readiness Packet | Prove that a fund/book/period can support NAV, investor statements, administrator tie-out, and close sign-off. | Fund/book/period/entity, close-state ladder, trial balance, positions, cash, valuations, accruals, journal list, reversal/adjustment approvals, capital-account roll-forward, reconciliation blockers, administrator tie-out, reviewer, period-lock state, statement support, amendment/restatement links. | Fund Accountant owns; Controller or CFO approves material close readiness. | Period lock, investor statements, report package, tax/K-1 support, audit package, and restatement release. |
| Executive Financial Control Brief | Give CFOs a compact, decision-grade control view of cash, exceptions, close, reports, and blocked outputs. | Cash confidence, bank-evidence freshness, material exceptions, close readiness, NAV readiness, report package status, stale data, approval needs, owner, SLA, materiality threshold, blocked outputs, decision log, audit event IDs. | CFO owns review; Controller, Fund Accountant, Data Operations, Treasury, and Reporting supply evidence. | Board package, investor package, executive sign-off, payment authorization, and restatement decision. |
| Audit Event Catalog | Standardize auditable event capture across imports, reconciliation, journals, access, reports, delivery, retention, and automation. | Event ID, timestamp, actor, actor role, action, object type, object ID, scope, before state, after state, reason code, policy mapping, approval reference, source evidence, retention class, legal-hold state, correlation ID. | Compliance Officer owns schema; System/Security Administrators own technical retention; domain owners emit events. | Access certification, audit request response, legal hold, policy exception, package approval, and evidence manifest freeze. |
| Scoped Access Review Packet | Prove that permissions are scoped, justified, reviewed, revoked when needed, and aligned with segregation of duties. | User/group, role, permission, scope kind, scope ID, fund/entity/account/book/period/report scope, amount limit, effective date, expiration, requester, approver, justification, SoD result, certification status, revocation evidence, audit events. | Security Administrator owns mechanics; Compliance Officer certifies; domain owner approves business scope. | Journal approval, report release, package delivery, payment request approval, admin actions, and external evidence access. |
| Portfolio Daily Review Packet | Capture the daily PM control review without giving PMs accounting authority they should not have. | Portfolio/fund/date, material changes, exposure, performance, benchmark variance, stale marks, breaches, liquidity watchlist, unreconciled positions, valuation exceptions, source confidence, PM acknowledgement, escalation state, comments, linked evidence. | Portfolio Manager owns review; Risk, Accounting, and Data Operations own evidence inputs. | Investment commentary, risk escalation closure, valuation acceptance, report commentary, and unresolved-position sign-off. |
| Stakeholder Delivery / Restatement Packet | Prove governed delivery, recipient entitlement, package contents, amendments, and restatements. | Package ID, package type, statement contents, capital activity support, recipient list, entitlement scope, approval chain, dataset version, template version, delivery channel, timestamp, delivery evidence, request history, amendment reason, restatement lineage, audit event IDs. | Reporting Analyst owns package; Controller or CFO approves release/restatement; Compliance reviews entitlement and retention. | Stakeholder publication, investor statement release, board package, amendment, restatement, and audit/tax support response. |

### Operating Ownership Matrix

| Object or workflow | Responsible | Accountable approver | Consulted | Informed |
| --- | --- | --- | --- | --- |
| Journals and reversals | Fund Accountant / Investment Accountant | Controller; CFO for material or late-close items | Data Operations, Portfolio Manager, Compliance | Auditor, Reporting Analyst |
| NAV support and period close | Fund Accountant | Controller or CFO | Data Operations, Portfolio Manager, Treasury, Reporting | LP/stakeholder recipients after approved delivery |
| Report packages | Reporting Analyst | Controller or CFO | Fund Accountant, Compliance, Portfolio Manager | Stakeholders, Auditor |
| Scoped access and entitlement | Security Administrator | Compliance Officer plus domain owner for business scope | System Administrator, Controller, Reporting Analyst | Affected user and auditors |
| Stakeholder delivery | Reporting Analyst | Controller or CFO | Compliance, Fund Accountant, Relationship owner | LPs, trustees, board, RIA clients, beneficiaries |
| Amendments and restatements | Reporting Analyst / Fund Accountant | Controller or CFO | Compliance, Auditor, Portfolio Manager where performance commentary changes | Stakeholder recipients and administrators |
| Payment request and cash evidence | Treasury Operations Specialist | CFO or configured payment approver | Fund Accountant, Compliance, Controller | Reporting Analyst, Auditor |
| Import run certification | Data Operations Analyst | Operations Manager or affected domain owner | Integration Administrator, Fund Accountant, Reporting Analyst | Portfolio Manager, CFO when material |

---

## Current Implementation Baseline

This design document is not a greenfield specification. Meridian already has working foundations that shape the product direction and should be preserved while remaining capability gaps are closed.

### Evidence Sources

Current implementation claims in this section are grounded in:

* `docs/roadmap/data/*.yml` and `docs/roadmap/generated/ROADMAP_SUMMARY.md` for wave status, acceptance posture, and stage gates
* `docs/source/data/source-modules.yml` and registered `src/**/README.md` files for active module responsibilities
* `docs/architecture/module-map.md` and `docs/architecture/project-structure.md` for layer boundaries and supported UI surfaces

Roadmap acceptance is bounded to the named capability. It does not by itself certify every
deployment profile or production release; current readiness claims still require the implementation
tracker, operator preflight, packaging/deployment evidence, and required GitHub Actions checks.

### Closed Baselines

The roadmap registry marks W1-W5 as done, with green health and complete evidence posture:

| Wave | Capability Baseline | Product Meaning |
| --- | --- | --- |
| W1 | Provider trust gate and data confidence baseline | Trusted data operations have provider validation packets and operator sign-off evidence. |
| W2 | Paper trading cockpit reliability | Paper sessions, order-readiness surfaces, and operator acceptance paths are preserved as active baselines. |
| W2 | Paper promotion evidence and operator acceptance | Research-to-paper promotion remains a governed handoff with evidence lineage before acceptance. |
| W3 | Research-to-paper continuity | Strategy research outputs, run comparison, and promotion evidence remain connected to downstream paper validation. |
| W4 | Portfolio ledger reconciliation readiness | Reconciliation queue actions, ledger evidence, accounting casework, and close-lane sign-off are established preservation targets. |
| W4 | Governed report pack readiness | Report packs carry approval, provenance, publication, restatement, export, and evidence-vault support. |
| W5 | Accounting records and operational evidence | Source records, normalized activity, reconciliation cases, ledger evidence, approvals, documents, report packs, exports, and restatement lineage remain linked as audit-ready operational records. |
| W5 | Multi-asset operational coverage proof lane | Security Master posture, provider evidence, ledger classification, reconciliation signals, and close-readiness blockers are exposed through shared browser and WPF read models. |
| W5X | Shared financial record explorers | Ledger, Portfolio, Security & Instrument, and Report-Line Provenance explorers are complete over shared contracts and read models, with endpoint, browser, and WPF proof for saved views, dense-table parity, inspector routing, and cross-explorer proof trails. |
| W5X | Financial operations control center | Operations Continuity, close readiness, approval policy, close calendar, reconciliation break, checklist, audit-evidence, governed reopen, browser Operations Continuity, WPF Fund Ledger, and direct-lending evidence form the accepted Financial Operations control-center boundary. |
| W5X | Statement connector library | Declarative CSV/OFX mapping profiles, IB Flex XML, OFX bank/investment, and Alpaca statement connectors normalize through one reconciliation seam with preview, confidence, drift detection, idempotency, and retained source evidence. |
| W7 | Live-readiness governance | Paper-to-live promotion requires trusted-data review, paper-validation evidence, reconciliation evidence, approvals, accounting-record evidence, governed-reporting evidence, governance sign-off, exception-handling evidence, rollback or kill-switch evidence, audit-retention evidence, a live-promotion manual override, brokerage live-enablement checks, and clear execution controls before a live run can be created. |

### Active and Planned Baselines

The registry distinguishes active productization from work that remains planned:

| Wave | Capability | Current Status and Gate |
| --- | --- | --- |
| W5X | Evidence Vault productization | In progress. Productizes retained-document identity, intake, request/document lists, extracted-field review, object links, immutable manifests, and audit state as a reusable shared evidence layer. |
| W5X | Statement reconciliation onboarding | In progress. Uses the completed connector library for browser-first import, preview, commit, retained Evidence Vault proof, reconciliation routing, and next actions. |
| W6 | Backtesting studio evidence loop | Backtesting Studio remains planned; backtest results should link to strategy lineage and operator-facing acceptance criteria before paper promotion expansion. |
| W8 | WPF desktop workstation parity | In progress. Closes browser-first screen gaps over shared contracts and read models while preserving desktop MVVM, validation, and release workflows. |

### Active Product Surfaces

Meridian has two active operator UI lanes over one shared seam:

* `src/Meridian.Ui/dashboard/` is the browser workstation source, with built host-served assets under `src/Meridian.Ui/wwwroot/workstation/`.
* `src/Meridian.Wpf/` is the active WPF desktop workstation. It projects the seven canonical workspaces over shared contracts and read models; its current lane focus is closing web-UI parity gaps (`W8-WPF-PARITY-001`, see `docs/development/wpf-web-ui-alignment-plan.md`). Existing shell compatibility, tests, validation, and release workflows continue.
* `src/Meridian.Ui.Services/`, `src/Meridian.Ui.Shared/`, and `src/Meridian.Contracts/` provide shared endpoint, read-model, and DTO support so both the browser and WPF workstations consume the same product state instead of forking it.
* `src/Meridian.Ledger/` and `src/Meridian.Reporting/` provide the current accounting and reporting implementation backbone: posted private-capital fund-event reconstruction, capital-account subledger impact, governed report-pack generation/delivery, report-writer grids, saved filters, formulas, and lineage.
* Direct private-capital review routes under `/api/ledger/private-capital/...` are current operational-review surfaces for fund-event, capital-account, ledger-impact, approval, and report-output evidence. They are not a broad LP portal, payment execution, or mobile product lane.

Visible root navigation should remain:

```text
Trading
Portfolio
Accounting
Reporting
Strategy
Data
Settings
```

`Research`, `Data Operations`, and `Governance` remain compatibility groupings or internal route concepts, not visible root workspaces. Meridian has no mobile development lane; responsive browser behavior is allowed only to keep the browser workstation usable.

### Capability Posture by Domain

| Domain | Current Posture | Existing Foundation |
| --- | --- | --- |
| Data & Integration | Implemented evidence | Provider SDK, infrastructure adapters, provider validation, credential/setup flows, source-module validation, and data-confidence gates. |
| Financial Operations | Implemented evidence | Reconciliation, casework, accounting close, evidence routing, W4 ledger review flows, NAV-support posture, and fund-event accounting records. |
| Treasury & Payments | Supported foundation | Cash-flow views, payment-oriented workflow design, account/ledger seams, and treasury-ledger control principles exist; full payment execution remains later productization. |
| Portfolio & Investment Operations | Implemented evidence | Portfolio, fund-structure, brokerage sync, fund accounts, positions, paper-session, valuation evidence, and ledger-backed workflows. |
| Reference Data | Implemented evidence | Security Master contracts, provider-to-security mapping, asset profiles, trust/conflict summaries, and shared multi-asset readiness coverage. |
| Instrument, Contract & Obligation Management | Implemented evidence | Security Master, direct-lending/F# rule kernels, factor/corporate-action evidence, obligation-oriented ledger support, and multi-asset operational blockers. |
| Entity & Relationship Management | Supported foundation | Fund-structure setup, ownership graph, legal-entity, vehicle, account-handoff, and assignment workflows. |
| Alternative Asset Management | Supported foundation | Private-credit/direct-lending models, governed custom asset profiles, and structured/private asset coverage rows exist; new live provider adapters and full alternative-asset operations remain expansion work. |
| Financing & Capital Structure Analysis | Design-led foundation | Capital-structure analysis remains a product design target with partial support through fund, vehicle, account, and ledger models. |
| Planning, Forecasting & Decision Support | Design-led foundation | Strategy, run comparison, and reporting evidence exist; full planning and forecasting engines remain future work. |
| Research & Analytics | Implemented evidence | Strategy lifecycle, QuantScript, backtesting runtime, research continuity, and promotion evidence exist; W6 Backtesting Studio expansion is planned. |
| Risk Management | Supported foundation | Pre-trade risk rules and live-readiness controls exist; full enterprise risk management remains expansion work. |
| Client & Stakeholder Reporting | Implemented evidence | Governed report-pack readiness, provenance, export evidence, and publication/restatement lifecycle are W4 baselines. |
| Collaboration & Communication | Design-led foundation | Workflow assignment, comments, audit events, and queue state exist; broad collaboration tooling remains later work. |
| Administration & Governance | Implemented evidence | Settings, policy, provider setup, audit trail, approval controls, and governed stage gates exist; Carta/FundStudio-style fund, book, period, report, and delivery admin scopes remain productization targets. |
| Audit, Compliance & Regulatory | Implemented evidence | Audit events, evidence manifests, report provenance, approval history, and controlled close/report workflows exist. |
| Workflow & Process Automation | Supported foundation | Shared workflow DTOs, route targets, operator queues, lifecycle transitions, and acceptance gates exist; no-code workflow design remains future work. |
| Document & Knowledge Management | Active productization | Evidence Vault identity, intake, request/document lists, extracted-field review, object links, immutable manifests, audit state, and the statement-import evidence bridge exist; `W5X-EVIDENCE-001` and `W5X-STMT-ONBOARD-001` remain in progress. |
| Reporting & Analytics Platform | Implemented evidence | Report-pack workflow, line provenance, trial-balance reporting, report freshness, and export evidence exist. |

### Current, Supported, and Planned Productization Matrix

Use this matrix when describing what Meridian has today versus what the design document is directing
next. "Implemented evidence" means the repository has accepted evidence for the capability baseline.
"Supported foundation" means underlying models, routes, services, or workflow concepts exist but the
operator product is not complete. "Planned productization" means the design is intentional but must
not be presented as shipped.

| Capability area | Implemented evidence | Supported foundation | Planned productization |
| --- | --- | --- | --- |
| Treasury and payments | Treasury-ledger principles, cash-oriented records, approval evidence, payment-intent language, and ledger linkage expectations. | Payment request, approval, expected cash movement, bank evidence, reconciliation, return/reversal evidence, and audit linkage can be modeled as operating records. | Native live payment execution, bank release automation, full bill pay, and payment processor orchestration. |
| Private-capital close cockpit | W5 accounting records, close-lane blockers, fund-event reconstruction, capital-account subledger impact, ledger evidence, report-pack lineage, and shared browser/WPF read models. | NAV readiness, period lock/reopen posture, administrator tie-out, journal/reversal boundaries, reviewer ownership, and report readiness are defined as product objects. | Complete the broader fund/book/period cockpit, close-state ladder, SLA ownership, statement release, amendment, restatement, and tax/K-1 support workflows; remaining WPF parity is tracked as `W8-WPF-PARITY-001`. |
| Financial Record Explorers | Completed Ledger, Portfolio, Security & Instrument, and Report-Line Provenance explorers over shared contracts/read models, saved views, proof state, evidence links, and audit routing. | Explorer shells, proof drawers, saved views, right-side inspection, proof ribbons, audit timelines, and record graphs remain reusable product patterns. | Close only the workstation parity gaps outside the accepted W5X explorer boundary, tracked through `W8-WPF-PARITY-001`. |
| Statement connectors | Completed profile-driven CSV/OFX, IB Flex XML, OFX bank/investment, and Alpaca connectors with shared preview, confidence, drift, idempotency, and reconciliation handoff. | The shared connector contract supports additional institution-specific profiles without duplicating reconciliation policy. | Add provider and format depth only with retained-source, drift, golden-file, and operator-acceptance evidence. |
| Evidence Vault and statement onboarding | Retained-document identity, manifests, reviewer state, object links, audit primitives, and a statement-import evidence bridge exist. | Browser-first statement onboarding already returns evidence and reconciliation routes over shared DTO/API seams. | Complete `W5X-EVIDENCE-001` and `W5X-STMT-ONBOARD-001`; broader portal, collaboration, extraction, and WPF presentation remain separately gated. |
| Stakeholder delivery | Governed report-pack readiness, provenance, export evidence, publication/restatement lifecycle, and report-pack delivery service foundations. | Recipient lists, entitlement scope, delivery evidence, package versioning, amendment reasons, and restatement lineage are defined evidence requirements. | Broad LP/client portal, self-service stakeholder workspace, and external collaboration workflows beyond governed package delivery. |
| Risk | Pre-trade rules, live-readiness controls, exposure/portfolio evidence, breach-style signals, and operational blockers. | Daily PM review, breach acknowledgement, liquidity watchlist, stale marks, benchmark underperformance, valuation exceptions, and blocked report commentary. | Full enterprise risk engine, stress/scenario suite, independent risk cockpit, and cross-portfolio risk governance program. |
| Forecasting | Strategy analytics, run comparison, reporting evidence, cash-plan snapshots, and design-level forecasting requirements. | Forecast inputs can be retained as evidence and linked to cash, budget, close, and report objects. | Full forecasting engine, scenario engine, autonomous planning workflows, and broad decision-support modeling. |

---

## 5. Functional Domain Catalog

Meridian should be organized around functional domains. These domains describe business capability areas, not necessarily services or database schemas.

## 5.1 Data & Integration

### Purpose

Acquire, normalize, validate, and distribute provider, file, and external-system data while retaining
source evidence, confidence posture, and replayable lineage for Meridian's operational-record
workflows.

### Core Flow

```text
Connect Source
→ Acquire Data
→ Validate Data
→ Normalize Data
→ Store Data
→ Publish Data
```

### Capabilities

* Provider catalog, readiness posture, and capability metadata
* Provider onboarding, credential validation, and secret-safe setup
* API ingestion for market, brokerage, bank, accounting-system, and reference data
* Batch and document intake through SFTP, file uploads, and governed email attachment capture
* Raw source capture with source-row references, source hashes, duplicate keys, and provenance
* Mapping and transformation into canonical positions, transactions, balances, reference records, and provider events
* Data validation, freshness checks, provider capability checks, and confidence scoring
* Deduplication and replay-safe import job handling
* Lineage tracking from source payload to normalized record, reconciliation case, ledger evidence, report line, and export
* Import replay, backfill, and historical reprocessing with prior-version preservation
* Shared publication into storage, accounting, reporting, strategy, audit, browser workstation, and active WPF workstation surfaces

### Operating Requirements

* UI surfaces must consume shared contracts and read models for provider posture, validation state, and publication status instead of owning provider-trust logic.
* Missing, stale, or unsupported source evidence must create review-required or blocked states rather than plausible-looking operational data.
* New provider work should start from ProviderSdk contracts and Infrastructure adapters, then publish through shared services before adding browser presentation; WPF presentation then consumes those same shared services (parity tracked as `W8-WPF-PARITY-001`).
* Channel expansion can proceed when current source, roadmap status, or user direction supports it; new channels must preserve retained evidence, validation, reconciliation, approval, and shared-contract controls appropriate to their scope.

---

## 5.2 Financial Operations

### Purpose

Support reconciliation, exception management, accounting operations, close support, workflow control, and audit evidence.

### Core Flow

```text
Receive Activity
→ Match Records
→ Resolve Exceptions
→ Approve Results
→ Produce Evidence
```

### Capabilities

* Cash reconciliation
* Position reconciliation
* Trade reconciliation
* Income reconciliation
* MBS factor reconciliation
* Bank reconciliation
* GL reconciliation support
* Exception management
* Break assignment and escalation
* Close checklists
* Fund-event accounting support
* Partner capital account tie-outs
* Shadow NAV and NAV-support packages
* Expense, fee, and allocation review
* Period close locks and reopen evidence
* Operational dashboards
* Evidence packages
* Approval history

### Exception Case Model

Financial Operations exceptions should be treated as governed operating cases, not just break rows.
Each exception should expose enough structured metadata for operators, controllers, reviewers, and
auditors to understand who owns the issue, why it matters, what it blocks, and what evidence proves
its resolution.

Required exception fields include:

| Field | Purpose |
| --- | --- |
| Owner | Named accountable operator, team, or reviewer responsible for next action. |
| Queue | Current operating queue or work lane used for triage, assignment, escalation, and reporting. |
| SLA due date | Date and time by which the exception must be resolved, escalated, waived, or re-baselined. |
| Severity | Operational urgency such as informational, low, medium, high, or critical. |
| Materiality | Financial significance using configured amount, percentage, fund, investor, report, or close thresholds. |
| Root cause | Classified cause such as missing evidence, source mismatch, stale valuation, late file, booking error, timing difference, mapping issue, approval delay, or external-system defect. |
| Source system | Originating custodian, administrator, bank, GL, provider, file, API, manual input, or derived Meridian process. |
| Affected fund/book/period | Scoped fund, legal entity, portfolio, account, book, close period, and as-of date impacted by the exception. |
| Blocked outputs | Close lane, journal posting, report approval, package delivery, NAV support, capital account statement, tax package, audit package, certified data mart export, or other output that cannot proceed while the exception is unresolved. |
| Evidence status | Evidence posture such as missing, requested, received, validated, disputed, approved, frozen in manifest, or waived with approval. |

Exception records should link directly to the Evidence Vault for support documents and request-list
status, Ledger Explorer for journal and ledger impact, Report-Line Provenance Explorer for affected
report numbers and packages, and the Operational Evidence Graph for the full source-to-output proof
chain.

### Exception Queue Views

The Financial Operations control center should provide queue views that preserve one shared exception
state model while letting operators focus on the next action required:

| Queue view | Operating meaning |
| --- | --- |
| New | Newly created exceptions that need triage, owner assignment, materiality review, and blocked-output identification. |
| Assigned | Exceptions with an accountable owner or team actively working the case. |
| Waiting on evidence | Exceptions blocked by missing source records, documents, confirmations, administrator files, bank evidence, or reviewer support. |
| Waiting on approval | Exceptions that have a proposed resolution, waiver, journal, report impact, or evidence package awaiting authorized approval. |
| Resolved | Exceptions whose root cause, evidence, ledger/report impact, and audit trail are complete enough to unblock downstream outputs. |
| Reopened | Previously resolved or waived exceptions returned to active work because new evidence, late activity, restatement, failed control, or reviewer challenge changed the conclusion. |
| Waived | Exceptions intentionally accepted under a permissioned waiver with materiality rationale, approver, expiration or review date, blocked-output impact, and retained evidence. |

### Escalation Behavior

Escalation should be automatic and auditable when an exception blocks production financial outputs.
If a case blocks period close, Meridian should mark the affected close lane as blocked, notify the
owner and controller, show the blocker in close readiness, and prevent close sign-off until the case
is resolved or formally waived. If a case blocks journal posting, Meridian should hold the journal in
draft or pending state, prevent posting into locked or unsupported periods, and require evidence and
approval before posting, reversal, or adjustment. If a case blocks report approval, Meridian should
flag affected report lines and packages, route reviewers to the Report-Line Provenance Explorer, and
prevent approval until the exception is resolved, waived, or documented as immaterial under policy.
If a case blocks package delivery, Meridian should stop delivery release for affected recipients or
packages, expose the blocked package in delivery readiness, and retain the escalation, waiver, or
release decision in the audit trail.

Every escalation should preserve the exception owner, queue, SLA due date, severity, materiality,
root cause, source system, affected fund/book/period, blocked outputs, and evidence status so the
Operational Evidence Graph can reconstruct the control decision later.

### Roadmap Acceptance Criteria

If the Financial Operations exception work becomes a committed roadmap item, a delivery row should
be considered acceptable only when:

* Exceptions can be created or derived from reconciliation, close, journal, report, delivery, and
  evidence workflows with all required exception fields captured or explicitly marked unknown.
* Operators can filter and manage new, assigned, waiting-on-evidence, waiting-on-approval, resolved,
  reopened, and waived queues from the Financial Operations control center.
* SLA, severity, materiality, owner, queue, root-cause, source-system, affected-scope,
  blocked-output, and evidence-status changes are audit logged with timestamps and actors.
* Close sign-off, journal posting, report approval, and package delivery surfaces show blocking
  exceptions and enforce configured resolve-or-waive gates before production release.
* Each exception links to relevant Evidence Vault records, Ledger Explorer records, Report-Line
  Provenance Explorer records, and the Operational Evidence Graph without duplicating business
  state across surfaces.
* Waivers require permissioned approval, materiality rationale, retained evidence, and clear output
  impact; reopened exceptions preserve the earlier resolution or waiver history.
* Dashboards expose workload, aging, SLA breach, materiality, blocked-output, source-system, and
  root-cause summaries by tenant, fund, book, period, owner, and queue.
* Tests or acceptance evidence demonstrate at least one exception blocking close, one blocking
  journal posting, one blocking report approval, and one blocking package delivery through resolve
  or waive paths.

### Roadmap Productization

`W5X-FINOPS-001` is the completed Financial Operations control-center milestone that turns
reconciliation, exception management, accounting operations, close support, workflow control, and
audit evidence into a shared Accounting/Reporting operator surface. The accepted boundary is the
shared Operations Continuity and Fund Ledger read-model surface consumed by both workstations;
WPF surfaces this through Fund Ledger today, with remaining Operations Continuity parity tracked as
`W8-WPF-PARITY-001`. Later proof-layer expansions remain separate roadmap decisions.

#### W5X-FINOPS Cockpit Design

The Financial Operations cockpit is the Accounting/Reporting command surface for today's close,
reconciliation, and delivery risk. It should summarize the current operating day across fund,
book, accounting period, ledger account, and report package so a controller can answer which close
items are clean, which items are blocked, and which downstream outputs are unsafe to release. The
summary state should be derived from shared W1-W5 read models rather than from separate browser or
WPF-only workflow state.

The cockpit's primary status bands are:

* **Close/reconciliation state:** fund, book, period, account, and report-package readiness with
  clear states for not started, importing, validating, reconciling, awaiting approval, blocked,
  ready for close, closed, reopened, and delivered.
* **Exception queues:** breaks, missing evidence, stale valuations, unapproved journals, blocked
  report lines, failed imports, and late delivery items. Each queue item must expose owner, age,
  current SLA, materiality, impacted period, impacted report package, blocker reason, and next
  action.
* **Priority model:** queue ordering should combine materiality, SLA proximity or breach, period
  impact, report impact, and approval-blocker state. Material unresolved items that affect a
  closing period, a package scheduled for delivery, or an approval gate must outrank lower-value
  informational breaks even when both are assigned to the same operator.
* **Release safety:** report packages and period-close actions remain unsafe when any material
  reconciliation break, missing evidence item, stale valuation, unapproved journal, failed import,
  blocked report line, or late delivery item is still open for the applicable organization, entity,
  portfolio, account, book, period, fund where applicable, or recipient package.

Drill-through is part of the cockpit contract, not a separate reporting convenience. A queue row or
status tile should open the relevant proof surface with the same organization/entity/portfolio/
account/book/period/fund/report context preserved:

* **Ledger Explorer** for journal detail, account activity, trial-balance impact, reversal chains,
  approval state, and close-lock effect.
* **Evidence Vault** for retained source files, request lists, extraction status, missing-support
  tasks, frozen manifests, and legal-hold or retention signals.
* **Operational Event Command Spine** for trades, cash movements, invoices, fees, subscriptions,
  redemptions, transfers, corporate actions, capital events, valuation updates, fund-event
  specializations, treasury expectations, and event-level completion blockers.
* **Report-Line Provenance Explorer** for report-line inputs, source records, reconciliations,
  journals, approvals, template version, delivery package, and restatement lineage.

Both workstations deliver the cockpit product lane over the same read-model state. The browser
workstation emphasizes role-based triage, lightweight queue review, cross-workspace
drill-through, comments, assignments, and governed release decisions from
`src/Meridian.Ui/dashboard/`, while the WPF desktop workstation delivers the dense-workpaper
presentation of the same shared state (parity gaps tracked as `W8-WPF-PARITY-001`). No client should
own a divergent close state; both the browser and WPF workstations consume shared
Accounting/Reporting read models from
`src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/` so operator actions, approval blockers,
and release readiness remain consistent.

### Fund Event Command Center

The Fund Event Command Center is a planned private-capital operating surface for creating,
reviewing, reconciling, approving, reporting, and locking fund events as auditable operational
records. It should remain roadmap-candidate language until the roadmap registry accepts a matching
item and evidence gates; do not present it as shipped, accepted, or part of the closed W1-W5
baseline before that registry acceptance exists.

#### Supported Event Types

The initial event taxonomy should support these fund-event records:

* Formation
* Close
* Subscription
* Capital call
* Contribution receipt
* Investment
* Distribution
* Valuation
* Fee
* Expense
* Tax request
* Audit request
* Dissolution
* Wind-down

#### Event Lifecycle States

Every command-center event should move through explicit, auditable lifecycle states:

* Draft
* Evidence pending
* Validation blocked
* Reconciliation blocked
* Journal pending
* Approval pending
* Report pending
* Delivered
* Locked
* Amended
* Restated

#### Event Detail Page Layout

Each event detail page should use a consistent evidence-first layout:

* **Event header:** fund, vehicle, entity, event type, materiality, owner, effective date, due date,
  current lifecycle state, and amendment/restatement lineage.
* **Proof ribbon:** compact readiness indicators for evidence completeness, validation,
  reconciliation, journal state, approval state, report use, lock state, and audit coverage.
* **Evidence panel:** source documents, import runs, administrator files, signed notices, source
  hashes, lineage references, reviewer notes, retention class, and legal-hold markers.
* **Ledger impact:** journal drafts, posted journals, reversals, accruals, realized/unrealized
  effects, cash movements, book/period scope, and blocked-posting reasons.
* **Capital-account impact:** investor allocations, commitments, contributions, distributions, fees,
  expenses, tax lots where relevant, capital roll-forward, and statement-line effects.
* **Workflow tasks:** assignments, due dates, blocker reasons, reviewer decisions, approval chain,
  escalation history, and segregation-of-duties checks.
* **Report usage:** report packs, investor statements, notices, tax/audit support files, board
  packages, delivery status, recipient entitlement scope, and amendment/restatement dependencies.
* **Audit timeline:** immutable event history for creation, evidence receipt, validation,
  reconciliation, journal actions, approvals, report generation, delivery, locks, amendments,
  restatements, and evidence-retention actions.

#### Workspace Entry Points

The command center should be reachable from existing root operator workspaces without introducing a
new root navigation lane:

* **Portfolio:** open event records from fund, vehicle, holding, investment, valuation,
  distribution, and liquidity-review contexts.
* **Accounting:** open event records from close cockpit, ledger explorer, journal queues,
  capital-account workbench, reconciliation cases, and period-lock workflows.
* **Reporting:** open event records from package builders, investor statements, notices, tax/audit
  support requests, delivery evidence, amendment, and restatement workflows.
* **Data:** open event records from import runs, administrator files, provider validation, lineage
  manifests, reconciliation inputs, source-evidence repair, and certified dataset release flows.

#### Roadmap Candidate Boundary

Use this feature as a roadmap candidate only until a registry-backed item is accepted. Candidate
acceptance should name the owning roadmap ID, event registry schema, lifecycle transition rules,
workspace routes, evidence requirements, audit-event coverage, reporting dependencies, and validation
commands. Until then, documents and product copy should say "planned Fund Event Command Center" or
"roadmap candidate" rather than "implemented", "accepted", or "available".

---

## 5.3 Treasury & Payments

### Purpose

Manage payment requests, liquidity, expected cash movement, bank evidence, reconciliation, and
capital activity. Near-term treasury work records and proves money-movement intent; live payment
execution remains later productization.

### Core Flow

```text
Request Payment
→ Validate Payment
→ Approve Payment
→ Record Expected Cash Movement
→ Attach Bank Evidence
→ Reconcile Cash and Ledger Impact
→ Report Payment Status
```

### Capabilities

* Bank accounts
* Cash balances
* Liquidity monitoring
* Cash forecasting
* Treasury ledger accounts
* Idempotent payment and settlement intents
* ACH intent and bank evidence
* Wire intent and bank evidence
* Internal transfers
* Payment approvals
* Capital calls
* Distributions
* Investor payments
* Fee payments
* Returns, reversals, and failed-payment evidence
* Positive pay support
* Bank integration

### Operating Requirements

* Payment execution remains later productization unless a roadmap item explicitly moves it forward.
* Near-term treasury work should focus on requests, approvals, expected cash flows, bank evidence,
  reconciliation, and ledger-backed audit records.
* Every money-movement workflow should link payment intent, approval, bank confirmation, return, or
  reversal evidence to an atomic double-entry ledger transaction.
* Posted treasury ledger entries are immutable; corrections must be represented as reversing or
  adjusting entries with explicit lineage.
* Balance-affecting writes must be idempotent, per-currency balanced, and protected by version or
  close-period checks where concurrent activity could change the result.

### Acceptance Criteria

* Each payment request has requester, amount, currency, payee, account scope, business purpose,
  approval policy, expected settlement date, and source evidence.
* Approval state is explicit: draft, submitted, first approval, second approval where required,
  rejected, bank evidence received, reconciled, returned, reversed, or cancelled.
* No workflow copy should imply Meridian has released money unless a future roadmap item explicitly
  implements live payment execution.
* Downstream cash, ledger, close, and report outputs remain blocked when approval, bank evidence,
  reconciliation, or reversal lineage is missing.
* Every state transition emits an audit event with actor, scope, previous state, next state, reason,
  and linked evidence.

---

## 5.4 Portfolio & Investment Operations

### Purpose

Manage holdings, positions, transactions, exposures, valuations, and investment activity.

### Core Flow

```text
Acquire Holdings
→ Process Transactions
→ Monitor Positions
→ Analyze Performance
→ Report Results
```

### Capabilities

* Holdings
* Transactions
* Positions
* Lots
* Cost basis
* Performance measurement
* Exposure analysis
* Allocation analysis
* Benchmarking
* Valuation source hierarchy
* Portfolio company data collection
* Portfolio one-pagers and tear sheets
* Multi-prime, custodian, and account overlap views
* Cash, collateral, margin, and liquidity monitoring
* Versioned valuation and NAV-support inputs
* Portfolio grouping
* Corporate action support

---

## 5.5 Reference Data

### Purpose

Provide authoritative reference information used throughout Meridian. This domain owns identifiers, classifications, calendars, currencies, taxonomies, and metadata, but it does not own instrument contract terms or cash-flow logic.

### Core Flow

```text
Acquire Reference Data
→ Validate
→ Normalize
→ Publish
→ Consume Across Meridian
```

### Capabilities

* CUSIP, ISIN, SEDOL, ticker, FIGI, internal identifiers
* Asset classifications
* Sector and industry classifications
* Currency metadata
* FX metadata
* Business calendars
* Settlement calendars
* Rating metadata
* Counterparty classifications
* Reference data stewardship

---

## 5.6 Instrument, Contract & Obligation Management

### Purpose

Serve as the authoritative source for financial instruments, contractual terms, obligations, rights, schedules, lifecycle events, and expected cash flows.

This domain is the financial engine of Meridian.

### Core Flow

```text
Define Instrument
→ Define Contract Terms
→ Generate Obligations
→ Generate Expected Cash Flows
→ Track Actual Activity
→ Analyze Variances
```

### Core Principle

Meridian should not treat everything as only a security.

Instead:

```text
Instrument = What it is
Contract = What the terms are
Obligation = What must happen
Expected Cash Flow = What should happen
Transaction = What actually happened
Reconciliation = Did expected and actual agree?
```

### Capabilities

* Instrument modeling
* Contract templates
* Contract instances
* Coupon terms
* Interest rates
* Floating rate formulas
* Maturity dates
* Call and put features
* Conversion features
* Covenants
* Amortization schedules
* Payment rules
* Obligations
* Expected cash-flow generation
* Projected cash-flow generation
* Lifecycle events
* Structured product modeling
* Liability modeling
* Scenario modeling

### Supported Financial Objects

* Equities
* Bonds
* Loans
* Mortgages
* MBS
* CMBS
* ABS
* CLOs
* Swaps
* Futures
* Options
* Leases
* Insurance-style liabilities
* Capital commitments
* Credit facilities
* Warehouse facilities
* Private investments
* Real estate debt

---

## 5.7 Entity & Relationship Management

### Purpose

Model people, organizations, ownership structures, legal entities, and economic relationships.

### Core Flow

```text
Create Entity
→ Define Relationships
→ Manage Ownership
→ Track Changes
```

### Capabilities

* Individuals
* Households
* Trusts
* Funds
* LLCs
* SPVs
* Partnerships
* Foundations
* Beneficiaries
* Advisors
* General partners
* Limited partners
* Subscribers
* Trustees
* Custodians
* Banks
* Lenders
* Borrowers
* Relationship graph
* Ownership percentages
* Authority tracking
* Authorized signers
* Commitments and side-letter references
* KYC / AML posture references

---

## 5.8 Alternative Asset Management

### Purpose

Support assets beyond traditional public securities.

### Core Flow

```text
Acquire Asset
→ Track Performance
→ Manage Cash Flows
→ Value Asset
→ Exit Asset
```

### Capabilities

* Real estate assets
* Leases
* Property debt
* Private credit loans
* Amortization
* Covenants
* Credit monitoring
* Private equity investments
* Portfolio companies
* Portfolio company operating metrics
* Structured products
* MBS / ABS / CLO / CMBS
* Fund interests
* SPVs
* Look-through fund interests
* Capital commitments
* Capital account schedules
* Valuation support
* Waterfall modeling

---

## 5.9 Financing & Capital Structure Analysis

### Purpose

Support the evaluation, structuring, monitoring, and optimization of financing arrangements, leverage, and capital structures.

### Core Flow

```text
Evaluate Opportunity
→ Model Financing Structure
→ Analyze Risk & Returns
→ Compare Alternatives
→ Execute Financing
→ Monitor Performance
```

### Capabilities

* Debt analysis
* Term loans
* Revolvers
* Lines of credit
* Warehouse facilities
* Bridge financing
* Mezzanine debt
* Margin facilities
* Mortgage analysis
* Synthetic financing analysis
* Total return swap and contract-for-difference financing
* Repo and reverse repo financing
* Securities lending and stock borrow financing
* Derivatives-based leverage and financing overlays
* Preferred equity, convertible instruments, and payment-in-kind structures
* Subscription lines, NAV facilities, and capital-call facilities
* Asset-backed lending, receivables financing, and factoring
* Sale-leaseback and lease financing
* Securitization and collateralized financing structures
* Intercompany loans and sponsor support arrangements
* Capital stack modeling
* Senior / junior / mezzanine / preferred equity / common equity layers
* Refinancing analysis
* Debt service coverage
* Interest coverage
* LTV analysis
* DSCR analysis
* Covenant tracking
* Borrowing base analysis
* Collateral, haircut, margin, and eligibility analysis
* Financing cost attribution and effective leverage monitoring
* Counterparty exposure and rehypothecation tracking

---

## 5.10 Planning, Forecasting & Decision Support

### Purpose

Provide a forward-looking planning and scenario engine for Meridian.

### Core Flow

```text
Define Assumptions
→ Generate Forecast
→ Compare Scenarios
→ Evaluate Decision
→ Approve Plan
→ Monitor Actuals vs Forecast
```

### Capabilities

* Cash forecasting
* Liquidity forecasting
* Revenue forecasting
* Distribution forecasting
* Debt forecasting
* Expense forecasting
* What-if modeling
* Sensitivity analysis
* Stress testing
* Scenario analysis
* Monte Carlo simulation
* Assumption sets
* Decision records
* Planning approvals
* Forecast variance analysis

---

## 5.11 Research & Analytics

### Purpose

Support investment research, analysis, and decision-making.

### Core Flow

```text
Gather Information
→ Analyze Data
→ Develop Thesis
→ Validate Thesis
→ Make Decision
```

### Capabilities

* Research notes
* Investment memos
* Watchlists
* Market data
* Fundamental data
* Alternative data
* Security analysis
* Manager analysis
* Screening
* Backtesting
* Paper portfolios
* Strategy results
* Benchmark assignment
* Analytical model registry

---

## 5.12 Risk Management

### Purpose

Identify, monitor, and mitigate financial, investment, liquidity, compliance, and operational risks.

### Capabilities

* Portfolio risk
* Credit risk
* Liquidity risk
* Counterparty risk
* Concentration risk
* Operational risk
* Compliance risk
* Limit monitoring
* Breach management
* Stress testing
* Scenario analysis
* Risk reporting

### Acceptance Criteria

* Each risk alert has owner, risk type, scope, materiality, severity, threshold, source evidence,
  affected holdings or reports, SLA, and current state.
* Breach states are explicit: detected, acknowledged, assigned, under review, mitigated, waived,
  escalated, closed, or expired.
* PM acknowledgements cannot approve accounting records, override compliance policy, or close
  unresolved reconciliation blockers.
* Material breaches block downstream investment commentary, report release, or close sign-off until
  owner, decision, approval, and audit event are retained.
* Risk reports must disclose stale marks, missing source evidence, unresolved breaches, liquidity
  watchlist items, and valuation exceptions.

---

## 5.13 Client & Stakeholder Reporting

### Purpose

Deliver information to clients, beneficiaries, LPs, trustees, boards, investment committees, and other stakeholders.

### Core Flow

```text
Collect Approved Information
→ Generate Report
→ Review Report
→ Approve Report
→ Distribute Report
→ Retain Evidence
```

### Capabilities

* Performance reports
* Holdings reports
* Capital account statements
* Family office reports
* Investor reports
* Client reports
* LP statements
* Capital call and distribution notices
* Tax and K-1 support packages
* Schedule of investments packages
* Portfolio company one-pagers and tear sheets
* Board packets
* Audit packages
* Report templates
* Report packages
* Report approvals
* Distribution rules
* Approved stakeholder delivery records
* Client portal support (later; near-term delivery is governed report-package distribution)
* Document delivery

### Acceptance Criteria

* Each report package has package ID, tenant/fund/entity scope, dataset version, template version,
  report-line provenance, recipient list, entitlement scope, owner, reviewer, approver, and delivery
  policy.
* Package states are explicit: draft, evidence incomplete, in review, approved, held, delivered,
  amended, restated, revoked, or archived.
* Report delivery is blocked by missing approval, unresolved material breaks, stale certified data,
  incomplete recipient approval, entitlement mismatch, or missing delivery evidence.
* Amendments and restatements must preserve original package version, reason, approval, recipient
  impact, delivery evidence, and audit events.
* Stakeholders can view or request only their entitled package outputs; broad self-service portal
  behavior remains later productization.

---

## 5.14 Collaboration & Communication

### Purpose

Support coordination between operators, reviewers, managers, clients, advisors, auditors, and other stakeholders.

### Capabilities

* Comments
* Notes
* Tasks
* Secure messaging
* Notifications
* Escalations
* Review requests
* Meeting notes
* Approval requests
* Internal collaboration
* External communication tracking

### Acceptance Criteria

* Every task, comment, review request, notification, or escalation is linked to a business object,
  owner, scope, due date or SLA, current state, and audit event.
* Collaboration states are explicit: open, assigned, waiting on evidence, waiting on approval,
  escalated, resolved, rejected, or archived.
* External communication must retain recipient, channel, timestamp, package or evidence reference,
  entitlement check, and retention class.
* Collaboration cannot substitute for domain approval; journal, report, access, payment request, and
  close decisions must still use their owning approval workflow.
* Blocked collaboration items must name the downstream output they block, such as NAV readiness,
  package delivery, access certification, or close sign-off.

---

## 5.15 Administration & Governance

### Purpose

Manage platform configuration, security, roles, rules, and governance.

### Capabilities

* User management
* Role management
* Permission management
* Tenant configuration
* Business rules
* Feature flags
* Policy management
* System settings
* Configuration versioning
* Configuration approvals
* Fund, book, period, report, and delivery administration scopes
* JE-level, report-level, period-lock, export, and delivery permissions
* Fund onboarding and cloning templates
* Management-company expense allocation and intercompany policy configuration
* Period close, lock, reopen, and year-end workflow controls
* Provider, counterparty, API, SFTP, and file-delivery policies
* Immutable admin logs for postings, locks, exports, and stakeholder deliveries

---

## 5.16 Audit, Compliance & Regulatory

### Purpose

Provide evidence, controls, record retention, compliance workflows, and audit support.

### Capabilities

* Audit trails
* Audit events
* Control evidence
* Compliance reviews
* Policy attestations
* Approval histories
* Evidence packages
* Record retention
* Legal hold support
* Regulatory report support
* Examination support

### Acceptance Criteria

* Every compliance review has policy mapping, scope, owner, due date, evidence manifest, decision,
  approver, retention class, legal-hold state, and audit event IDs.
* Audit events follow the Audit Event Catalog fields: actor, action, object, scope, before/after
  state, reason, evidence, policy mapping, retention class, and correlation ID.
* Access certification states are explicit: pending, certified, exception requested, revoked,
  expired, escalated, or legally held.
* Legal holds override disposal and must be visible in evidence packet, report package, delivery,
  and audit request workflows.
* A compliance blocker prevents report release, stakeholder delivery, payment request approval,
  journal posting, or access grant when policy mapping or evidence is incomplete.

---

## 5.17 Workflow & Process Automation

### Purpose

Coordinate business processes, approvals, task routing, review queues, and recurring workflows.

### Core Flow

```text
Trigger Workflow
→ Assign Tasks
→ Complete Steps
→ Review
→ Approve / Reject
→ Close
→ Archive Evidence
```

### Capabilities

* Workflow templates
* Workflow instances
* Task assignments
* Review queues
* Approval chains
* Escalations
* SLA tracking
* Checklist templates
* Event triggers
* Scheduling
* Process templates

### Acceptance Criteria

* Each workflow instance has trigger, object scope, owner, SLA, required inputs, allowed states,
  approval policy, blocked downstream outputs, and evidence packet target.
* State transitions are explicit and auditable; workflows cannot silently skip review, approval,
  rejection, escalation, or evidence-archive steps.
* Automation may draft, classify, match, summarize, or flag, but cannot approve its own work, post
  material journals, override period locks, release payments, publish reports, or erase evidence.
* SLA breaches produce severity, owner, escalation path, affected outputs, and audit events.
* Workflow templates remain domain-aware; generic process configuration cannot own reconciliation,
  payment request, report, journal, or access-control truth.

---

## 5.18 Document & Knowledge Management

### Purpose

Manage documents, attachments, evidence, extracted metadata, search, and knowledge references.

### Capabilities

* Document storage
* Document metadata
* Document versioning
* Attachments
* Evidence tagging
* Document linking to business objects
* Search
* OCR / text extraction
* Retention policies
* Permission-aware document access
* Knowledge graph references

---

## 5.19 Reporting & Analytics Platform

### Purpose

Provide shared analytics, dashboards, visualizations, exports, and reporting infrastructure across Meridian.

### Capabilities

* Dashboards
* KPI tracking
* Custom reports
* Ad hoc analysis
* Visualizations
* Scheduled reports
* Data exports
* Report snapshots
* Certified datasets
* Certified operational data marts
* Queryable evidence and report-line provenance

### Acceptance Criteria

* Each certified dataset has source run IDs, validation results, reconciliation status, refresh
  cadence, owner, version, release approval, lineage manifest, and permitted consumers.
* Dashboards and reports disclose stale data, incomplete source evidence, unresolved exceptions,
  materiality thresholds, and blocked downstream outputs.
* Report lines drill through to source records, reconciliation cases, journals, approvals, delivery
  records, and restatement history where applicable.
* Scheduled exports require approval, entitlement policy, destination, version, timestamp, delivery
  evidence, and audit event.
* Analytics outputs cannot be marked certified when upstream import, reconciliation, journal,
  access, or package approval states are incomplete.

---

## 6. Bounded Context Map

Functional domains describe business capabilities. Bounded contexts define ownership boundaries for data, business rules, and language.

## 6.1 Core Bounded Contexts

| Bounded Context                   | Owns                                                                                                                   |
| --------------------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| Identity & Access                 | Users, roles, permissions, tenant access, authentication, authorization                                                |
| Entity & Relationship             | Legal entities, people, trusts, funds, SPVs, beneficiaries, ownership relationships                                    |
| Data Provider & Integration       | Providers, connections, credentials references, import jobs, data source metadata, file/API ingestion, provider health |
| Reference Data                    | Identifiers, classifications, currencies, calendars, ratings, taxonomies                                               |
| Instrument, Contract & Obligation | Financial instruments, contract terms, schedules, obligations, lifecycle events, expected cash flows                   |
| Portfolio Records                 | Holdings, positions, transactions, lots, cost basis, income activity, corporate actions                                |
| Financial Operations              | Reconciliations, breaks, exceptions, operational reviews, adjustments, close checklists, evidence packages             |
| Treasury & Payments               | Bank accounts, cash balances, payment requests, ACH, wires, capital calls, distributions, payment approvals            |
| Alternative Assets                | Real estate, private credit, private equity, structured assets, valuation inputs, asset-level cash flows               |
| Financing & Capital Structure     | Debt facilities, synthetic financing, loan agreements, capital stacks, covenants, collateral terms, debt schedules, leverage analysis |
| Planning & Forecasting            | Forecast models, scenarios, assumptions, stress tests, planning cases, decision records                                |
| Research & Analytics              | Research notes, watchlists, investment theses, backtests, strategy runs, analytical workspaces                         |
| Risk Management                   | Risk rules, metrics, limits, exposure calculations, concentration checks, breaches                                     |
| Workflow & Task                   | Tasks, assignments, statuses, approvals, escalations, SLA tracking                                                     |
| Audit & Compliance                | Audit events, control evidence, approval history, compliance checks, retention policies                                |
| Reporting & Client Delivery       | Reports, dashboards, statements, client packages, investor packages, delivery history                                  |
| Document & Knowledge              | Documents, attachments, versions, metadata, extracted text, evidence links, search index                               |

## 6.2 Removed Context

AI & Automation was removed as a standalone business domain. AI may be treated later as a cross-cutting implementation capability, but it should not be part of the formal domain model at this stage.

---

## 7. Context Ownership Matrix

| Bounded Context                   | Owns Data | Owns Rules | Exposes APIs | Has UI |           MVP |
| --------------------------------- | --------: | ---------: | -----------: | -----: | ------------: |
| Identity & Access                 |       Yes |        Yes |          Yes |    Yes |           Yes |
| Entity & Relationship             |       Yes |        Yes |          Yes |    Yes |           Yes |
| Data Provider & Integration       |       Yes |        Yes |          Yes |    Yes |           Yes |
| Reference Data                    |       Yes |        Yes |          Yes |    Yes |           Yes |
| Instrument, Contract & Obligation |       Yes |        Yes |          Yes |    Yes | Yes / Limited |
| Portfolio Records                 |       Yes |        Yes |          Yes |    Yes |           Yes |
| Financial Operations              |       Yes |        Yes |          Yes |    Yes |           Yes |
| Treasury & Payments               |       Yes |        Yes |          Yes |    Yes |         Later |
| Alternative Assets                |       Yes |        Yes |          Yes |    Yes |         Later |
| Financing & Capital Structure     |       Yes |        Yes |          Yes |    Yes |         Later |
| Planning & Forecasting            |       Yes |        Yes |          Yes |    Yes |         Later |
| Research & Analytics              |       Yes |        Yes |          Yes |    Yes |         Later |
| Risk Management                   |       Yes |        Yes |          Yes |    Yes |         Later |
| Workflow & Task                   |       Yes |        Yes |          Yes |    Yes |           Yes |
| Audit & Compliance                |       Yes |        Yes |          Yes |    Yes |           Yes |
| Reporting & Client Delivery       |   Partial |        Yes |          Yes |    Yes |           Yes |
| Document & Knowledge              |       Yes |        Yes |          Yes |    Yes |           Yes |

---

## 8. Recommended MVP Contexts

The first build should focus on the operational foundation.

### MVP Contexts

```text
1. Identity & Access
2. Entity & Relationship
3. Data Provider & Integration
4. Reference Data
5. Instrument, Contract & Obligation — limited version
6. Portfolio Records
7. Financial Operations
8. Workflow & Task
9. Audit & Compliance
10. Reporting & Client Delivery
11. Document & Knowledge
```

### Later Expansion Contexts

```text
1. Treasury & Payments
2. Alternative Assets
3. Financing & Capital Structure
4. Planning & Forecasting
5. Research & Analytics
6. Risk Management
```

---

## 9. Core Business Object Model

## 9.1 Core Object Hierarchy

```text
Tenant / Organization
        ↓
Entity
        ↓
Account / Capital Account / Ledger Account
        ↓
Position / Contract / Obligation
        ↓
Expected Cash Flow
        ↓
Actual Transaction / Actual Cash Flow / Journal Entry
        ↓
Reconciliation / Reporting / Audit
```

## 9.2 Core Objects

| Object             | Purpose                                        | Examples                                                      |
| ------------------ | ---------------------------------------------- | ------------------------------------------------------------- |
| Tenant             | Customer environment                           | Fund administrator, RIA, family office                        |
| Entity             | Legal or economic party                        | Fund, trust, LLC, individual, SPV                             |
| Relationship       | Link between entities                          | Owner, beneficiary, advisor, custodian, lender, borrower      |
| Account            | Container where assets, cash, or activity live | Bank account, custody account, investment account, GL account |
| Capital Account    | Economic record for investor or owner activity | Commitment, contribution, distribution, allocation, NAV share |
| Ledger Account     | Accounting account used for balanced postings  | Cash, receivable, payable, income, expense, capital account   |
| Instrument         | Defines what something is                      | Bond, stock, loan, lease, swap, real estate asset             |
| Contract           | Defines rights and obligations                 | Loan agreement, bond indenture, lease, credit facility        |
| Obligation         | Future duty or right to pay or receive         | Coupon, principal, rent, capital call, distribution           |
| Expected Cash Flow | Forecasted cash movement from terms            | Scheduled interest, maturity payment, rent payment            |
| Fund Event         | Operational event requiring accounting evidence | Closing, investment, capital call, distribution, expense      |
| Transaction        | Actual observed activity                       | Trade, wire, coupon receipt, journal entry                    |
| Journal Entry      | Balanced accounting record owned by Meridian   | Accrual, valuation adjustment, cash receipt, capital activity |
| Position           | Ownership or exposure at a point in time       | Shares, par value, LP interest, loan balance                  |
| Valuation          | Value assigned to an object                    | Market value, NAV, appraisal, fair value                      |
| Reconciliation     | Comparison between sources                     | Custodian vs internal, bank vs ledger, expected vs actual     |
| Exception          | Difference requiring resolution                | Missing trade, price break, cash variance                     |
| Document           | Supporting evidence                            | Statement, invoice, confirmation, agreement                   |
| Task               | Work assigned to a user                        | Review break, approve payment, validate import                |
| Report Package     | Final output for review/distribution           | Investor report, audit package, board packet                  |
| Delivery Record    | Evidence of stakeholder publication            | Recipient list, timestamp, channel, package version           |
| Audit Event        | Immutable history of meaningful actions        | Approved recon, changed terms, imported file                  |

## 9.3 Object Relationship Model

```text
Tenant
 └── Entity
      ├── Account
      │    ├── Position
      │    │    └── Instrument
      │    ├── Transaction
      │    └── Cash Balance
      │
      ├── Contract
      │    ├── Instrument
      │    ├── Obligation
      │    └── Expected Cash Flow
      │
      ├── Document
      └── Relationship
```

---

## 10. System of Record Strategy

## 10.1 Core Principle

Meridian should distinguish between:

```text
Source of Data
Source of Truth
System of Record
```

| Concept          | Meaning                                                               |
| ---------------- | --------------------------------------------------------------------- |
| Source of Data   | Where information came from                                           |
| Source of Truth  | The trusted source for a specific field or purpose                    |
| System of Record | The approved internal record after validation, override, and approval |

## 10.2 Source Priority Model

Meridian should support configurable source hierarchy rules.

| Data Type          | Primary Source                 | Secondary Source             | Override Allowed |
| ------------------ | ------------------------------ | ---------------------------- | ---------------- |
| Bank Cash          | Bank feed                      | Custodian                    | Yes              |
| Custody Positions  | Custodian                      | Investment accounting system | Yes              |
| Security Terms     | Bloomberg / Refinitiv / vendor | Custodian                    | Yes              |
| Prices             | Approved pricing vendor        | Broker quote                 | Yes              |
| Transactions       | Custodian                      | Internal import              | Yes              |
| Accounting Entries | Meridian ledger                | GL / accounting-system export | Via approved reversing or adjusting journal |
| Contract Terms     | Executed agreement             | Data vendor                  | Yes              |
| Entity Ownership   | Legal documents                | Internal admin               | Yes              |

## 10.3 Source-of-Record Layers

### Layer 1: External Raw Sources

Capture exactly what was received.

Examples:

* Custodian files
* Bank feeds
* Broker records
* Market data vendor files
* Accounting system exports
* PDFs
* Contracts
* Statements

### Layer 2: Normalized Operational Records

Convert external data into Meridian’s standard model.

Examples:

* Normalized transaction
* Normalized position
* Normalized cash balance
* Normalized price
* Normalized contract term

### Layer 3: Validated Business Records

Apply validation, mapping, exception checks, and business rules.

Examples:

* Validated transaction
* Validated position
* Approved price
* Validated instrument
* Validated expected cash flow

### Layer 4: Approved System of Record

Represent Meridian’s official internal record for operational workflows, reporting, reconciliation, forecasting, and audit.

## 10.4 Lineage Requirements

Every system-of-record value should carry lineage.

Minimum lineage fields:

```text
Source system
Source file/API
Import run
Received timestamp
Normalized timestamp
Validation status
Approval status
Approved by
Approved timestamp
Override flag
Override reason
Supporting document
```

## 10.5 Override Strategy

Overrides should be allowed but controlled.

| Rule                      | Requirement                                                 |
| ------------------------- | ----------------------------------------------------------- |
| Preserve original value   | Always                                                      |
| Require reason            | Always                                                      |
| Create audit event        | Always                                                      |
| Require approval          | Based on materiality                                        |
| Review or expire override | For prices, ratings, assumptions, and temporary corrections |

## 10.6 Reconciliation Strategy

Meridian should reconcile across three dimensions.

| Reconciliation Type  | Example                                      |
| -------------------- | -------------------------------------------- |
| Source-to-Source     | Custodian position vs administrator position |
| Expected-to-Actual   | Expected coupon vs actual coupon received    |
| Internal-to-Official | Meridian record vs general ledger            |

Reconciliation output should flow into Financial Operations exception casework whenever an
unmatched item, tolerance breach, stale input, missing document, disputed source value, approval
gap, or ledger/report variance can affect close, journal posting, report approval, package
delivery, NAV support, capital accounts, tax support, audit evidence, or certified exports.

Each reconciliation exception should carry the same required case fields: owner, queue, SLA due
date, severity, materiality, root cause, source system, affected fund/book/period, blocked outputs,
and evidence status. These fields make reconciliation queues operationally useful rather than just
analytical, and they allow managers to review workload, aging, breach risk, and production blockers
by fund, book, period, owner, source system, and output type.

Reconciliation queues should include views for new, assigned, waiting on evidence, waiting on
approval, resolved, reopened, and waived exceptions. Resolution should require a root-cause
classification, supporting evidence or approved waiver, explicit ledger/report/close impact, and a
retained audit trail. Reopened exceptions should preserve the original resolution history and explain
what new evidence, late activity, restatement, or reviewer challenge changed the state.

Reconciliation should be linked into the proof surfaces that operators use to complete downstream
work: Evidence Vault for source support and request lists, Ledger Explorer for journal and ledger
impact, Report-Line Provenance Explorer for affected report numbers and packages, and the
Operational Evidence Graph for source-to-output reconstruction.

## 10.7 Treasury Ledger Principles

Modern treasury-ledger design reinforces Meridian's ledger authority. Meridian should use these
principles wherever records affect cash, capital accounts, accounting balances, payment workflows,
or close packages:

| Principle | Meridian Requirement |
| --- | --- |
| Double-entry | Every balance-affecting journal transaction has at least one debit and one credit, and debits equal credits per currency. |
| Atomic write | A journal transaction's entries either all persist or all fail; callers cannot create orphan debit or credit rows. |
| Idempotency | Import runs, payment intents, bank confirmations, and journal requests carry stable source keys so retries cannot duplicate money movement or accounting entries. |
| Posted immutability | Posted entries cannot be edited or deleted; corrections use reversing or adjusting journals linked to the original record. |
| Pending lifecycle | Draft or pending journal transactions can be amended before approval, then posted, archived, or superseded with version history. |
| Versioned balances | Ledger accounts, journal transactions, close packages, and settlements expose versions so past states can be reconstructed precisely. |
| Effective dating | Entries keep effective date, posted timestamp, source timestamp, and approval timestamp separately so operations can distinguish economic date from processing date. |
| Concurrency control | Writes that depend on balance, close status, settlement status, or approval state use optimistic version checks and fail closed on stale state. |
| Payment linkage | Payment requests, bank orders, confirmations, returns, and reversals link to ledger transactions, but payment processors do not become the source of ledger truth. |
| Audit reconstruction | Every ledger balance in a report package can drill back to entries, source records, approvals, documents, and any reversal chain. |

---

## 11. Extensibility Strategy

Meridian should have a stable financial operations core with configurable workflows, rules, data mappings, reports, permissions, and domain extensions layered around it.

Engineering boundaries for this strategy are captured in
[`docs/architecture/core-extensibility-model.md`](../architecture/core-extensibility-model.md).
The shared contract seam is `src/Meridian.Contracts/Extensibility/`.

### Core Extensibility Model

```text
Stable Core
+ Configurable Business Rules
+ Configurable Workflows
+ Pluggable Integrations
+ Extensible Domain Models
+ Tenant-Specific Templates
```

### Stable Core Objects

The following should remain stable across tenants:

```text
Tenant
Entity
Relationship
Account
Instrument
Contract
Obligation
Expected Cash Flow
Transaction
Position
Valuation
Reconciliation
Exception
Capital Account
Ledger Account
Journal Entry
Fund Event
Document
Task
Report Package
Audit Event
```

### Configurable Areas

| Area            | Examples                                                      |
| --------------- | ------------------------------------------------------------- |
| Workflows       | Review steps, approval chains, task queues                    |
| Rules           | Validation rules, matching tolerances, materiality thresholds |
| Integrations    | Provider mappings, file layouts, API connections              |
| Reports         | Templates, schedules, recipients, sections, evidence packages |
| Permissions     | Roles, data scopes, approval authority                        |
| Classifications | Asset classes, strategies, categories                         |
| Custom Fields   | Tenant-specific attributes                                    |
| Source Priority | Which source wins for prices, positions, cash, terms          |
| Ledger Controls | Posting rules, idempotency keys, period locks, reversal policy |
| Notifications   | Alerts, escalations, reminders                                |

### Not Fully Configurable

These should remain governed and consistent:

* Audit trail
* Security model foundation
* Core object identity
* Financial calculation integrity
* Data lineage model
* Approval evidence model
* Immutable record preservation

---

## 12. Configuration Architecture

## 12.1 Configuration Lifecycle

```text
Draft
→ Tested
→ Reviewed
→ Approved
→ Active
→ Superseded
→ Retired
```

## 12.2 Standard Configuration Metadata

Every important configuration record should include:

```text
Configuration ID
Configuration Type
Owning Context
Tenant Scope
Status
Version
Effective Date
Expiration Date
Created By
Created At
Reviewed By
Approved By
Approved At
Change Reason
Linked Audit Event
Rollback Version
```

## 12.3 Configuration Scope Levels

| Scope Level       | Meaning                                                                |
| ----------------- | ---------------------------------------------------------------------- |
| Global            | Applies across all Meridian tenants                                    |
| Tenant            | Applies to one customer environment                                    |
| Entity Group      | Applies to a family group, client group, fund complex, or organization |
| Entity            | Applies to a specific fund, trust, LLC, client, household, or SPV      |
| Account           | Applies to a specific bank, custody, investment, GL, or loan account   |
| Workflow Instance | Applies to a specific operational process                              |
| User              | Applies to individual preferences or assignments                       |

---

## 13. Configuration Architecture Matrix — Summary

| Configuration Area          | Owner Context                  | Approval Required | Versioned | Effective-Dated | Audit Event |     MVP |
| --------------------------- | ------------------------------ | ----------------: | --------: | --------------: | ----------: | ------: |
| Tenant Profile              | Platform Governance / Identity |               Yes |       Yes |             Yes |         Yes |     Yes |
| Enabled Domains             | Platform Governance            |               Yes |       Yes |             Yes |         Yes |     Yes |
| Users and Roles             | Identity & Access              |               Yes |       Yes |             Yes |         Yes |     Yes |
| Data Scope Policies         | Identity & Access              |               Yes |       Yes |             Yes |         Yes |     Yes |
| Approval Authority Limits   | Identity / Workflow            |               Yes |       Yes |             Yes |         Yes |     Yes |
| Entity Types                | Entity & Relationship          |               Yes |       Yes |             Yes |         Yes |     Yes |
| Relationship Types          | Entity & Relationship          |               Yes |       Yes |             Yes |         Yes |     Yes |
| Account Types               | Portfolio / Treasury           |               Yes |       Yes |             Yes |         Yes |     Yes |
| Provider Catalog            | Data Integration               |               Yes |       Yes |             Yes |         Yes |     Yes |
| Provider Connections        | Data Integration               |               Yes |       Yes |             Yes |         Yes |     Yes |
| File Layouts                | Data Integration               |               Yes |       Yes |             Yes |         Yes |     Yes |
| Mapping Profiles            | Data Integration               |               Yes |       Yes |             Yes |         Yes |     Yes |
| Validation Rules            | Data Integration               |               Yes |       Yes |             Yes |         Yes |     Yes |
| Source-of-Record Rules      | Data Governance                |               Yes |       Yes |             Yes |         Yes |     Yes |
| Identifier Schemes          | Reference Data                 |               Yes |       Yes |             Yes |         Yes |     Yes |
| Asset Class Taxonomy        | Reference Data                 |               Yes |       Yes |             Yes |         Yes |     Yes |
| Instrument Type Catalog     | Instrument / Contract          |               Yes |       Yes |             Yes |         Yes |     Yes |
| Contract Type Templates     | Instrument / Contract          |               Yes |       Yes |             Yes |         Yes | Limited |
| Obligation Type Catalog     | Instrument / Contract          |               Yes |       Yes |             Yes |         Yes |     Yes |
| Cash Flow Generation Rules  | Instrument / Contract          |               Yes |       Yes |             Yes |         Yes | Limited |
| Transaction Type Catalog    | Portfolio Records              |               Yes |       Yes |             Yes |         Yes |     Yes |
| Valuation Source Priority   | Portfolio / Reference Data     |               Yes |       Yes |             Yes |         Yes |     Yes |
| Reconciliation Types        | Financial Operations           |               Yes |       Yes |             Yes |         Yes |     Yes |
| Matching Rules              | Financial Operations           |               Yes |       Yes |             Yes |         Yes |     Yes |
| Matching Tolerances         | Financial Operations           |               Yes |       Yes |             Yes |         Yes |     Yes |
| Exception Types             | Financial Operations           |               Yes |       Yes |             Yes |         Yes |     Yes |
| Workflow Templates          | Workflow & Task                |               Yes |       Yes |             Yes |         Yes |     Yes |
| Approval Chains             | Workflow & Task                |               Yes |       Yes |             Yes |         Yes |     Yes |
| SLA Rules                   | Workflow & Task                |               Yes |       Yes |             Yes |         Yes |     Yes |
| Audit Event Catalog         | Audit & Compliance             |               Yes |       Yes |             Yes |         Yes |     Yes |
| Evidence Requirements       | Audit & Compliance             |               Yes |       Yes |             Yes |         Yes |     Yes |
| Retention Policies          | Audit & Compliance             |               Yes |       Yes |             Yes |         Yes |     Yes |
| Report Templates            | Reporting                      |               Yes |       Yes |             Yes |         Yes | Limited |
| Report Definitions          | Reporting                      |               Yes |       Yes |             Yes |         Yes |     Yes |
| Report Package Types        | Reporting                      |               Yes |       Yes |             Yes |         Yes |     Yes |
| Ledger Posting Policy       | Accounting / Ledger            |               Yes |       Yes |             Yes |         Yes |     Yes |
| Period Close Policy         | Accounting / Ledger            |               Yes |       Yes |             Yes |         Yes |     Yes |
| Document Type Catalog       | Document & Knowledge           |               Yes |       Yes |             Yes |         Yes |     Yes |
| Document Metadata Fields    | Document & Knowledge           |               Yes |       Yes |             Yes |         Yes |     Yes |
| Custom Fields               | Platform Governance            |         Sometimes |       Yes |             Yes |         Yes | Limited |
| Payment Approval Matrix     | Treasury & Payments            |               Yes |       Yes |             Yes |         Yes |   Later |
| Scenario Templates          | Planning & Forecasting         |               Yes |       Yes |             Yes |         Yes |   Later |
| Risk Limits                 | Risk Management                |               Yes |       Yes |             Yes |         Yes |   Later |
| Capital Stack Templates     | Financing                      |               Yes |       Yes |             Yes |         Yes |   Later |
| Alternative Asset Templates | Alternative Assets             |               Yes |       Yes |             Yes |         Yes |   Later |

---

## 14. MVP Configuration Scope

## 14.1 Required for MVP

```text
Tenant profiles
Enabled domains
Users and roles
Data scopes
Entity types
Relationship types
Account types
Provider connections
File layouts
Mapping profiles
Validation rules
Source-of-record rules
Instrument type catalog
Obligation type catalog
Transaction type catalog
Ledger posting policy
Journal reversal policy
Reconciliation types
Matching rules
Tolerance rules
Exception types
Workflow templates
Approval chains
Audit event catalog
Evidence requirements
Report package types
Document type catalog
Document metadata fields
```

## 14.2 Limited in MVP

```text
Custom fields
Report templates
Cash flow generation rules
Contract templates
Advanced capital account allocation rules
Close checklist templates
Client portal configuration
Report distribution rules
Workflow designer UI
Expected cash flow assumptions
```

## 14.3 Later

```text
Complex structured product waterfalls
Full payment execution
Risk limit engine
Scenario engine
Research model registry
Capital stack modeling
Forecasting assumption library
No-code calculation builder
Advanced client portal personalization
Full alternative asset configuration
```

---

## 15. Recommended Architecture Style

Meridian should start as a modular monolith with strict bounded-context boundaries, not as microservices from day one.

### Recommended Module Structure

```text
Meridian.Platform
Meridian.Identity
Meridian.Entities
Meridian.DataIntegration
Meridian.ReferenceData
Meridian.Instruments
Meridian.PortfolioRecords
Meridian.FinancialOperations
Meridian.Workflow
Meridian.Audit
Meridian.Reporting
Meridian.Documents
```

These names are the bounded-context module target. Project adaptation is tracked through
[`docs/architecture/design-document-adaptation.md`](../architecture/design-document-adaptation.md),
with physical module conformance covered by
[`docs/architecture/design-module-conformance.md`](../architecture/design-module-conformance.md).
New implementation work should select the relevant bounded-context module first, then use those
maps to choose the physical project, current source owner, contracts, UI surface, and validation
lane.

Each module should have:

```text
Domain model
Application services
Contracts / APIs
Infrastructure
UI components
Tests
```

### Design Rule

Other modules may read through published APIs, views, or events, but they should not directly write another module’s owned records.

---

## 16. Tenancy Strategy

Meridian should support hierarchical multi-tenancy.

```text
Tenant
    ↓
Operating Organization
    ↓
Legal Entity / Client / Fund / Family Group
    ↓
Account / Portfolio / Vehicle
    ↓
Position / Contract / Transaction
```

### Examples

#### Fund Administrator

```text
Tenant: Fund Admin Firm
    ↓
Client Fund Group
    ↓
Fund
    ↓
Investor / Vehicle / Account / Capital Account
```

#### RIA

```text
Tenant: RIA Firm
    ↓
Household
    ↓
Client
    ↓
Investment Accounts
```

#### Family Office

```text
Tenant: Family Office
    ↓
Family Branch
    ↓
Trust / LLC / Individual
    ↓
Accounts / Investments / Properties
```

### Tenant Scope Fields

Every core record should be scoped by:

```text
TenantId
EntityId where applicable
AccountId where applicable
```

Permissions should be filterable by:

```text
Tenant
Entity
Account
Portfolio
Fund
Household
Report Package
Document
```

### Authoritative Consistency Model

Authoritative tenancy, scoped access, fund structure, approvals, ledger, reconciliation, and audit
state should prefer consistency over write availability when Meridian runs multiple instances
against shared data. During store conflicts, stale assignment versions, unavailable authoritative
stores, or ambiguous scope resolution, Meridian should fail closed rather than allowing split-brain
authority or conflicting operational state.

Read models, search indexes, dashboards, and market-data caches may be eventually consistent where
the workflow remains safe, but authorization decisions and governed write paths must consult the
authoritative scope model or a verified fresh cache.

---

## 17. Permission Strategy

Meridian should use both role-based access control and attribute-based access control.

## 17.1 Role-Based Access Control

Defines what a user can do.

Examples:

| Role               | Capabilities                      |
| ------------------ | --------------------------------- |
| Operations Analyst | View, import, reconcile, comment  |
| Operations Manager | Assign, approve, escalate         |
| Portfolio Manager  | View portfolios, analyze holdings |
| Client Viewer      | View approved reports only        |
| Auditor            | View evidence and audit packages  |
| Administrator      | Configure users, roles, workflows |

## 17.2 Attribute-Based Access Control

Defines what data the user can access.

Examples:

```text
User can access Entity A but not Entity B.
User can view reports for Fund 1 but not Fund 2.
User can approve payments under $100,000.
User can view trust documents but not tax documents.
```

### Permission Model

Permissions should include:

```text
Role
Capability
Data Scope
Approval Limit
Segregation of Duties Rule
```

### Scoped Access Assignments

Current implementation note: Meridian already has a fund/company structure model and a role/profile
permission model. The structural gap is the binding between them: which user has which authority
over which organization, business, client, fund, portfolio, legal entity, vehicle, sleeve, or
account.

The target governed access record is:

```text
UserAccessAssignment
PrincipalId
PrincipalKind
ScopeKind
ScopeId
Role / RoleProfileName
PermissionMask
EffectiveFrom / EffectiveTo
GrantedBy
Rationale
CorrelationId
Version
CreatedAtUtc / UpdatedAtUtc
RevokedBy / RevokedAtUtc / RevocationReason
Linked Audit Event
```

Authorization should evaluate:

```text
Can principal P perform permission X on scope S at time T?
```

Global permissions remain useful for local administration and single-company deployments. Multi-
tenant or multi-company operation requires scoped access assignments with optimistic concurrency,
audit evidence, and fail-closed behavior.

---

## 18. Workflow Strategy

Workflows should be configurable but domain-aware.

Do not create a completely generic workflow system that knows nothing about finance. Instead, use configurable workflow templates backed by domain-specific rules.

### Workflow Model

```text
Workflow Template
    ↓
Workflow Instance
    ↓
Step
    ↓
Task
    ↓
Assignment
    ↓
Approval / Rejection / Escalation
```

### Example: Reconciliation Workflow

```text
Data Imported
→ Auto Match
→ Exceptions Generated
→ Analyst Review
→ Manager Approval
→ Evidence Package Created
→ Closed
```

### Example: Payment Workflow

```text
Payment Request
→ Validation
→ First Approval
→ Second Approval
→ Record Expected Cash Movement
→ Attach Bank Instruction or Bank Confirmation Evidence
→ Reconcile Cash and Ledger Intent
→ Retain Payment Evidence Packet
```

### Ownership Rule

Workflow owns:

```text
Tasks
Assignments
Statuses
Approvals
Escalations
SLA tracking
```

Workflow does not own:

```text
Reconciliations
Payments
Reports
Transactions
```

Those remain owned by their domain contexts.

---

## 19. Rules Strategy

Meridian should use a versioned rules framework.

Rules should be configurable, testable, auditable, and effective-dated.

### Rule Categories

| Rule Type              | Example                                             |
| ---------------------- | --------------------------------------------------- |
| Validation Rules       | Required fields, valid account, valid currency      |
| Mapping Rules          | Map custodian field to Meridian field               |
| Matching Rules         | Match transaction by date, amount, account, CUSIP   |
| Source-of-Record Rules | Custodian owns position quantity; vendor owns price |
| Approval Rules         | Payment over $250,000 requires two approvals        |
| Materiality Rules      | Cash break below $10 does not require escalation    |
| Exception Rules        | Missing expected coupon creates exception           |
| Reporting Rules        | Report must include approved valuations only        |
| Entitlement Rules      | Client can only view approved reports               |

### Rule Lifecycle

```text
Draft
→ Tested
→ Approved
→ Active
→ Retired
```

---

## 20. Integration Strategy

Meridian should use a provider adapter model.

### Provider Adapter Model

```text
Provider
    ↓
Connection
    ↓
Dataset
    ↓
Import Run
    ↓
Raw Record
    ↓
Mapping Profile
    ↓
Normalized Record
    ↓
Validation Result
    ↓
Published Business Record
```

### Supported Provider Types

* Custodian
* Bank
* Broker
* Market data vendor
* Fund administrator
* Accounting system
* Treasury or banking platform
* Tax or audit provider
* KYC / AML provider
* Document repository
* Manual upload

### Integration Rule

Meridian should preserve raw source data exactly as received, then transform through controlled layers.

```text
Raw
→ Normalized
→ Validated
→ Approved
→ Published
```

### No-Code Provider Integration Manifests

Meridian should let non-technical operators configure many provider APIs through guided setup
screens, while the system stores the result as a durable, versioned Provider Integration Manifest.
The user experience is configuration, not code authoring; the runtime executes the manifest through
controlled connector, mapping, validation, identity-resolution, and loading components.

```text
No-code setup UI
-> Versioned provider integration manifest
-> Generic connector runtime
-> Raw payload landing zone
-> Mapping and normalization engine
-> Validation and data-quality gates
-> Canonical financial data store
-> Portfolio, monitoring, trading, accounting, and reporting services
```

The design should support an effectively broad set of APIs by constraining them into supported
integration patterns rather than treating "any API" as unconstrained custom behavior.

| Integration Type | Non-Technical Configuration Posture | Product Boundary |
| --- | --- | --- |
| REST API | Supported | Best default for guided setup, endpoint tests, pagination, and mapping. |
| OpenAPI-described REST API | Supported | Preferred import path when the provider publishes a usable specification. |
| GraphQL API | Supported with guardrails | Requires schema introspection, samples, or template-backed mapping. |
| Webhook/event source | Supported with templates | Best for status changes, fills, alerts, and provider events. |
| SFTP/file drop | Supported | Required for custodians, administrators, accounting feeds, and recurring files. |
| CSV/Excel/API hybrid | Supported | Common institutional operations pattern; should share the same mapping and validation engine. |
| Streaming API | Template-driven only | Requires stricter operational monitoring and typed templates. |
| FIX or production trade execution API | Certified adapter required | Must not be activated through generic non-technical mapping alone. |

The generic no-code connector is acceptable for read-only data such as accounts, balances,
positions, holdings, transactions, tax lots, security reference data, market prices, corporate
actions, statements, documents, and alerts. Order preview, order placement, order cancellation,
cash transfer, and other production write capabilities require certified provider modules,
provider-specific validation, sandbox testing, approval workflows, entitlement checks, idempotency,
kill-switch support, audit logging, and reconciliation evidence.

### Provider Integration Manifest Scope

Each configured provider connection should compile into a manifest with stable metadata:

```text
Provider ID
Display name
Integration type
Environment
Authentication type and secret reference
Capabilities
Endpoint definitions
Request parameters and headers
Pagination and retry policy
Dependency chain
Response record paths
Field mappings
Transformations
Validation rules
Identity-resolution rules
Sync schedule and cursor policy
Approval state
Version and effective dates
```

Manifest records should separate reusable provider templates from connection instances:

* Provider templates define common provider behavior: auth type, known endpoints, supported
  capabilities, common mappings, pagination, rate limits, transforms, validations, and sample test
  cases.
* Connection instances define tenant- or account-specific behavior: credentials, selected accounts,
  enabled capabilities, environment, schedule, permissions, owner, approval settings, and sync
  status.

This distinction lets many tenants use the same provider template while preserving tenant-specific
credentials, scopes, schedules, approvals, and operational evidence.

### Guided Setup Experience

The setup workflow should be progressive so operators can connect, test, map, validate, and activate
without completing an entire integration in one sitting.

```text
Choose integration type
-> Select capabilities
-> Configure authentication
-> Import specification, choose template, or build endpoint
-> Fetch sample response
-> Map fields to canonical models
-> Apply safe transformations
-> Validate sample records
-> Run dry-run sync
-> Review reconciliation impact
-> Approve activation
-> Enable scheduled sync
-> Monitor and repair
```

Setup screens should include Provider Catalog, Connection Wizard, API Test Console, Visual Mapper,
Transformation Builder, Data Quality Center, Identity Matching Center, Sync Monitor, Change
Management, and Audit Center views. The API Test Console should support safe actions such as test
authentication, fetch sample accounts, fetch sample positions, preview mapped records, validate
records, run dry-run sync, and enable production sync after approval. Test output should be written
for operations users: success or failure, sample records, missing required fields, warnings, and
suggested fixes, not raw stack traces.

### Visual Mapping and Transformations

The visual mapping surface should show sample provider fields beside Meridian canonical fields.
Templates should prevent users from starting from a blank screen. For each capability, Meridian
should identify required and recommended fields, suggest likely mappings with confidence scores,
auto-accept high-confidence low-risk mappings, require review for medium-confidence mappings, and
block activation when required fields remain unresolved.

Canonical capability contracts should drive mapping requirements. Position mappings require account
identifier, security identifier, quantity, as-of date, and currency when money fields are present.
Transaction mappings require transaction ID, account ID, transaction type, trade or posting date,
amount, currency, and security identifiers when the activity is security-related.

Non-technical transforms should come from a safe library, including date parsing, decimal parsing,
currency defaulting, text normalization, enum mapping, amount sign handling, identifier priority,
constant values, and simple conditional mapping. Arbitrary user-written code should be limited to
admin or developer roles because free-form code in data pipelines creates security, supportability,
and auditability risk.

### Ingestion Runtime and Raw Evidence

The manifest-driven runtime should preserve raw evidence before mapping. For every import, Meridian
should retain the raw provider request, raw provider response, provider name, endpoint, received
timestamp, connection, sync run ID, provider API version when known, mapping version, canonical
result, and validation result.

```text
Connector runner
-> Raw payload store
-> Record extractor
-> Mapping engine
-> Normalization engine
-> Validation engine
-> Identity resolution engine
-> Canonical writer
-> Event bus
-> Portfolio / Accounting / Monitoring / Trading workflows
```

The same runtime should support pull mode, push/webhook mode, file/SFTP mode, manual upload mode,
and hybrid mode. Accepted records move into the canonical store; failed records move into a
quarantine and review workflow with enough raw evidence and mapped context to fix mappings, repair
data rules, or replay the affected records.

### Validation, Quarantine, and Identity Resolution

Each capability needs built-in validation rules. Accounts validate account identity, account type,
currency, and duplicate state. Positions validate numeric quantity, as-of date, at least one
security identifier, required currency for money fields, and value tolerance when quantity and price
are available. Transactions validate transaction ID, amount, mapped type, relevant dates, amount
sign, and security identifiers for security-related activity. Trade-capable connectors add account
approval, tradability, side and quantity checks, limit-price requirements, user permission,
pre-trade checks, and approval evidence.

Invalid mapped records should not silently enter the canonical store. They should be quarantined
with issue type, affected count, raw record, mapped record, validation errors, possible fixes,
reviewer state, and replay status. The review UI should let operators map an alternative field,
choose a default, classify a cash position, ignore a known irrelevant record, or route unresolved
issues to a provider incident.

Identity resolution should be configurable but controlled. Account matching may use provider account
ID, account number, account name, legal entity, portfolio code, custodian account number, or a
manually selected internal account. Security matching should prefer strong identifiers such as
internal security ID, CUSIP, ISIN, SEDOL, FIGI, and provider security ID before ticker plus exchange
and currency; ticker alone is not sufficient for institutional portfolios or fixed income coverage.

### Scheduling, Monitoring, Drift, and Activation

Operators should configure schedules in business terms: manual, hourly, daily, weekly, custom
schedule, full refresh, incremental refresh, or mixed monthly full refresh plus daily incremental
refresh. Behind the scenes, Meridian should store cursor policy such as timestamp, date, cursor
token, page number, offset, watermark, or full snapshot.

Every connection should have a status page with health, last successful sync, records received,
records accepted, records quarantined, average sync time, enabled capabilities, freshness, and
business-language failure reasons. Schema drift detection should compare the current response shape
against the last approved shape and pause affected mappings when required fields, record paths,
date formats, enum values, or pagination contracts change.

Production activation should require authentication test, endpoint test, sample data load, required
mappings, validation pass, dry-run sync, reconciliation review, and approval before scheduled sync
is enabled. Configuration permissions should separate viewer, operator, configurer, approver, admin,
and developer authority. Production mapping changes should create draft versions, require dry-run
validation, capture approval evidence, and support rollback.

Conceptually, the configuration model should include provider templates, provider connections,
connection capabilities, endpoint definitions, field mappings, sync runs, and quarantined records.
Those records must remain versioned, auditable, effective-dated where relevant, and linked to import
run evidence so every downstream portfolio, accounting, trading, or reporting output can explain
which provider payload and mapping version produced it.

---

## 21. Reporting Strategy

Reporting should consume approved records from other domains. Reporting should not own source financial data.

### Reporting Owns

```text
Report templates
Report definitions
Report packages
Schedules
Distribution history
Recipient lists
Rendered outputs
Approval status
Certified dataset definitions
Stakeholder delivery records
```

### Reporting Consumes

```text
Entities
Accounts
Positions
Transactions
Valuations
Reconciliations
Cash flows
Documents
Audit events
Journal entries
Capital accounts
Fund events
```

### Published Report Rule

Published reports should be frozen snapshots.

Each report package should know:

```text
What data was used
When it was generated
Which template version was used
Who approved it
Who received it
Whether it was later amended
Which certified dataset or evidence manifest supported it
```

---

## 22. Document Strategy

Documents should be a cross-domain capability, but the Document & Knowledge context should own document records.

Documents should be linkable to any major business object.

### Document Links

A document may support:

```text
Entity
Account
Capital Account
Contract
Transaction
Journal Entry
Fund Event
Reconciliation
Exception
Payment
Report Package
Delivery Record
Audit Event
```

### Document Metadata

Documents should support:

* Type
* Source
* Version
* Hash
* Linked object
* Retention class
* Confidentiality class
* Evidence tag
* Access policy
* Effective date
* Approval status

---

## 23. Configuration Risk Controls

Configurability creates power, but also risk. Meridian should include guardrails.

| Risk                                       | Control                                                    |
| ------------------------------------------ | ---------------------------------------------------------- |
| Tenant breaks financial logic              | Core financial objects remain stable                       |
| Too many custom fields                     | Custom fields must be typed, permissioned, and reportable  |
| Unapproved config changes affect reports   | Configuration changes require approval and effective dates |
| Mapping changes corrupt data               | Mapping versions are tied to import runs                   |
| Report templates change historical outputs | Published report packages are frozen snapshots             |
| Source priority changes create confusion   | Source-of-record policy changes require audit and approval |
| Ledger retries duplicate money movement    | Idempotency keys and source-event uniqueness are required  |
| Concurrent posts overdraw or corrupt state  | Balance-sensitive writes use version checks and fail closed |
| Posted accounting history is mutated        | Corrections use reversing or adjusting journals only       |
| Users bypass controls                      | Segregation of duties enforced by permissions and workflow |
| Reprocessing changes historical data       | Import replay must preserve lineage and prior versions     |
| Rules become untestable                    | Rules require test cases before activation                 |
| Tenant-specific one-off behavior grows     | Use profiles and templates instead of custom code          |
| Non-technical API setup corrupts imports   | Provider manifests require templates, validation, dry runs, approval, versioning, and rollback |
| Generic connector submits unsafe trades    | Production trade execution requires certified adapters, sandbox proof, approvals, entitlements, and kill-switch controls |
| Provider schema drift breaks mappings      | Drift detection pauses affected syncs until mappings are reviewed and approved |
| Credentials leak through configuration     | Secrets are stored through credential references and never persisted in normal manifest payloads |

---

## 24. Updated Design Thesis

> Meridian is designed as a modular, configurable financial operations platform. Its core financial model is intentionally stable, centered on entities, accounts, capital accounts, ledger accounts, instruments, contracts, obligations, cash flows, transactions, journal entries, positions, reconciliations, documents, workflows, reports, delivery records, and audit events. Around that stable core, Meridian provides tenant-specific configuration for workflows, rules, provider integration manifests, source-of-record policies, reporting, permissions, ledger controls, and custom attributes. This allows Meridian to support fund administrators, private fund managers, RIAs, family offices, and other investment organizations without creating separate products, requiring custom code for every read-only provider integration, or sacrificing auditability.

---

## 25. Design Backlog and Remaining Productization Work

### 25.0 Marketplace Positioning and 2026 Competitor Lens

Meridian is an evidence-backed operational-finance and trading platform whose accepted baseline now extends through W1-W5, completed W5X explorer, Financial Operations, and statement-connector milestones, plus bounded W7 governance. It is not a close-only toolkit, not a generalized fund administrator, and not a replacement for every external operating domain.

### 25.0.1 Implemented vs TODO Register (Design-Document-Sourced)

The canonical execution and readiness tracker is [Implementation and Readiness Tracker](implementation-todo-list.md). Keep detailed implementation, evidence, and readiness status changes in that file so the design document remains the product rationale rather than a duplicate execution tracker.

## Executive Marketecture Deck (Fixed 6-Page Format)

### Page 1 — Executive Positioning and Decision Framing

Meridian must win on one buyer question: who can reduce close risk and reporting uncertainty with less trust debt?

Market reality:

* Buyers operate across fragmented toolchains for data, accounting, close, and investor distribution.
* Fragmentation creates reconciliation lag, policy inconsistency, and version drift.
* Manual stitching increases audit exposure and extends period close.
* The highest-value control systems are those that turn these blind spots into explicit, shared decision state.

Meridian positioning:

* Proof-first over feature-first.
* Evidence-first over dashboard-first.
* Explainability over abstraction.
* Evidence continuity over unsupported breadth.

Meridian must be the only operator environment where close readiness, exception state, and report impact are always traceable from source to output in one governed workflow.

### Page 2 — Competitor Signal Translation

Reference vendors and strongest signals (as of June 2026):

* BlackLine: financial-close workflow, reconciliations, task management, transaction matching, journal controls, reporting linkage.
* Trintech Cadency: high-volume matching, close orchestration, workflow automation, exception management, audit/compliance framing.
* SS&C Advent Geneva: multi-asset portfolio/investor accounting scale, reporting breadth, NAV and compliance operations.
* eFront: private-markets lifecycle and client-facing operations, data consolidation, capital event workflows.

Competitive implication:

* These systems are strong in their chosen domains.
* Meridian must outperform through single-source proof continuity across fund-event, ledger, close, and report impact.

### Page 3 — 2026 Operating Model (Meridian Marketecture)

Meridian’s architecture for market differentiation:

* Source Integrity: immutable source records plus retained imports and attachments.
* Reconciliation Quality: break ownership, severity, aging, and blocker state tied to policy.
* Close Governance: lock/reopen posture and readiness as enforced system state.
* Proof-Ready Reporting: each report-line impact traces back to reconciliations, journals, approvals, and retained evidence.

Design law:

Every operator action must be answerable by one of three questions:

1. What changed?
2. Who approved it?
3. Why did it remain blocked?

### Page 4 — Accepted W5X-FREX-001 Baseline: Shared Financial Record Explorers

| Accepted capability | Competitor evidence | Meridian proof requirement | W5X-FREX gate |
| --- | --- | --- | --- |
| Shared explorer contract | BlackLine/Cadency show standardized close-reconciliation modules that scale operator workflows. | Shared read models and state contracts for scope, filters, saved views, summary strip, proof ribbon, and evidence links. | No FREX UI can launch without the browser consuming the shared DTO model and contract schema; WPF parity is active and tracked as `W8-WPF-PARITY-001`. |
| Explorer-to-proof graph | BlackLine links modules with reporting/drill-down semantics. | `UsedIn`, `Impacts`, report-line lineage, reconciliation lineage, journal lineage per row. | A seeded close case must traverse Fund Event → Reconciliation → Journal → Report Line in one trace path. |
| Multi-asset operator continuity | SS&C advertises broad instrument and valuation coverage across structures. | Multi-asset explorer behavior must preserve source conflict states, classification, and proof markers consistently. | The same user action produces identical proof behavior across at least 6 asset classes through the accepted browser and WPF scope. |
| Deterministic exception context | Competitor close stacks emphasize task visibility and review load balancing. | Exception state must remain visible and auditable from explorer context without local overrides. | Any unresolved blocker linked to close state blocks completion state transition. |
| Report impact safety | Vendor literature stresses reporting/compliance workflows and auditability. | Report usage links must require evidence packet IDs, versioned output linkage, and approval lineage. | Report impact tags cannot appear without complete report-line provenance. |

### Page 5 — Accepted W5X-FINOPS-001 Baseline: Financial Operations Control Center

| Accepted capability | Competitor evidence | Meridian proof requirement | W5X-FINOPS gate |
| --- | --- | --- | --- |
| Unified operations queue | BlackLine and Cadency prioritize centralized close-task and exception orchestration. | FINOPS queue must show blocker type, owner, aging, severity, escalation state, and close impact. | Queue “done” is blocked if any exception remains unresolved for a period-bound close dependency. |
| Close and reopen posture | Close-focused vendors highlight workflow and role-signoff discipline. | Period-lock and reopen evidence lifecycle must be shared by browser and active WPF workstation surfaces. | Close cannot move to “ready” until lock posture, evidence packet completeness, and approver chain are present. |
| High-risk action governance | Competitors combine approvals with compliance controls in close and journal modules. | Policy service required for posting, reopening, override, reassignment, and exception dismissal. | Any high-risk action without approval evidence reverts with immutable denial record. |
| Capital-event to reporting coupling | eFront and SS&C emphasize event and investor-cycle workflows. | FINOPS must surface capital-event impact on close and reporting dependencies in one operator lane. | One capital event, one valuation shock, and one report package must complete dependency validation. |
### Page 6 — Go/No-Go Checklist (Attached)

This checklist records the accepted W5X capability boundary. The roadmap registry now records
`W5X-FREX-001` and `W5X-FINOPS-001` as done with complete evidence posture; it does not turn this
bounded acceptance into blanket production certification.

| Gate | Requirement | Evidence owner | Status |
| --- | --- | --- | --- |
| Go-01 Source-chain | Record lineage exists from source input to proof packet for close-critical entities | Shared contracts + record lineage tests | [x] |
| Go-02 Reconciliation rigor | Open breaks carry owner, blocker cause, and policy threshold visibility | Explorer + FINOPS | [x] |
| Go-03 Closure integrity | No synthetic completion; close-ready logic enforces lock/posting/reopen constraints | FINOPS state machine | [x] |
| Go-04 Approval safety | No high-risk action without approval chain and approver traceability | Policy + services | [x] |
| Go-05 Evidence retention | Every report-impact item retains package ID, source hash, and version lineage | Reporting + audit | [x] |
| Go-06 Cross-surface contract parity | Accepted browser and WPF proof state uses shared contracts under the same filter/time window; remaining screen parity is tracked as `W8-WPF-PARITY-001` | Shared UI contracts | [x] |
| Go-07 Roadmap quality | W5X-FREX and W5X-FINOPS exit with complete evidence gates | PM + QA | [x] |

Go decision is valid only when all seven checks are fully green and evidence artifacts are retained.

Comparable products reviewed for this deck (June 2026):

* BlackLine Financial Close: https://www.blackline.com/solutions/financial-close/
* SS&C Advent Geneva brief: https://cdn.advent.com/cms/pdfs/briefs/PB-Geneva-Asset-Mgrs.pdf
* eFront home and private-equity: https://www.efront.com/ ; https://www.efront.com/en/alternative-investment-solutions/private-equity
* Trintech Cadency and Match: https://www.trintech.com/cadency/ ; https://www.trintech.com/cadency/match/

### Supporting work for design backlog completion

### 1. Master Workflow Inventory

A registry-backed list of the most important Meridian workflows, including both implemented baselines and planned expansion workflows.

Each workflow should define:

* Workflow ID
* Name
* Trigger
* Primary persona
* Inputs
* Outputs
* Frequency
* Criticality
* Primary domain
* Supporting domains

### 2. Context Interaction Matrix

Defines which bounded contexts interact and why.

Example:

| Source Context       | Target Context       | Interaction                                        |
| -------------------- | -------------------- | -------------------------------------------------- |
| Data Integration     | Portfolio Records    | Publishes imported transactions                    |
| Portfolio Records    | Financial Operations | Provides positions and activity for reconciliation |
| Financial Operations | Audit                | Publishes approval and exception events            |
| Reporting            | Portfolio Records    | Reads holdings and performance data                |
| Workflow             | Financial Operations | Assigns exception review tasks                     |

### 3. MVP Screen Inventory

Defines the shared product screen inventory delivered across both active operator UI lanes — the
browser workstation and the reactivated WPF desktop workstation — over shared contracts. WPF web-UI
parity for screens that shipped browser-first is tracked as `W8-WPF-PARITY-001`.

Initial and active operator screens include:

* Home Dashboard
* Provider Center
* Import Runs
* Data Quality
* Entity Directory
* Account Directory
* Instrument / Contract Registry
* Portfolio Records
* Reconciliation Workbench
* Exception Queue
* Workflow Tasks
* Audit Log
* Document Vault
* Reporting Packages
* Administration Settings

### 4. Product Data Model

Defines core entities, relationships, storage boundaries, read models, and contract DTOs that already exist or remain planned.

### 5. Delivery Roadmap

Uses `docs/roadmap/data/*.yml` as the durable source of delivery state, with the design document explaining why each wave matters.

### 6. Competitive Inspiration Translation Register

Maintain a compact register that maps researched external product patterns into Meridian-owned
requirements, scope status, and evidence lanes. Initial rows should cover:

* Carta-inspired fund ERP, fund administration, LP relations, fund events, capital activity,
  tax/audit support, SPVs, portfolio valuations, management-company administration, and fund data
  warehouse patterns.
* FundStudio-inspired fund administrator operations, multi-book general ledger, multi-prime
  reconciliation, shadow NAV, multi-currency accounting, locked periods, recurring journals,
  year-end close, capital-account packs, managed-service workflows, file distribution, SLA tracking,
  and portfolio drill-down.
* Modern Treasury-inspired ledger invariants: double-entry, idempotency, atomicity, immutable
  posted entries, versioned balances, effective dating, and optimistic concurrency controls.

### v0.15 Release Package: Accounting Records and Operational Evidence

The v0.15 release package establishes W1-W5 as the near-term operational record baseline. It delays Backtesting Studio and focuses on accounting, reconciliation, approval, retained-evidence, and record-keeping functionality. This makes Meridian stronger as the operational system of record before expanding research tooling, live-readiness surfaces, full payment execution, forecasting, enterprise risk, client portal, no-code workflow design, mobile, or other broad platform lanes.

v0.15 deepens the `Accounting`, `Reporting`, `Portfolio`, and `Data` workspaces by connecting:

* retained source records
* normalized transactions, positions, balances, and activity
* reconciliation case history
* journal and ledger evidence
* close-package status
* approval history
* document and evidence attachments
* report-pack publication, export, and restatement provenance

The package should preserve the shared-first UI direction: browser and active WPF workstation
surfaces consume shared contracts, endpoint read models, and services rather than inventing
separate accounting state.

Backtesting Studio remains valuable, but it stays behind this accounting records package so that strategy, paper, and later live-readiness work can rely on stronger books, audit, and reporting evidence.

### v0.16 Product Direction Addendum: Private-Capital Operations and Treasury Ledger

The v0.16 design direction keeps W1-W5 as the active baseline but clarifies the next product shape:
Meridian should feel closer to a private-capital operating system than a generic portfolio dashboard.
The priority is not a broad client portal, cap-table replacement, outsourced fund-admin service, or
uncontrolled autonomous-agent layer. The priority is connected fund operations: fund events, capital
accounts, capital calls, distributions, investments, portfolio valuations, tax/audit support
packages, LP-ready report packages, management-company operating records, and queryable certified
datasets, all backed by Meridian ledger truth.

The Carta-inspired design move is to make fund operations navigable through event-based records and
stakeholder-ready evidence: closings, subdocs, capital notices, investor commitments, portfolio
valuation support, tax support, audit support, and data warehouse outputs should all trace back to
the same approved fund-event and ledger lineage.

The FundStudio-inspired design move is to make administration operationally disciplined: fund/book
setup, period locks, recurring journals, year-end close, shadow NAV, multi-book/multi-currency
accounting, capital-account packs, admin/custodian file delivery, and SLA-tracked reconciliation
breaks should be governed workflows with immutable audit logs and scoped permissions.

The treasury-ledger model becomes a product design rule for accounting and money-movement surfaces:
every cash, settlement, capital-activity, fee, valuation, and close-related posting should be
traceable through immutable, idempotent, double-entry journal records with explicit source evidence,
approval state, period-close posture, and reversal lineage.

### v0.17 Product Direction Addendum: Shared Financial Record Explorers

The v0.17 design direction turns the W1-W5 operational-record baseline into a reusable operator
surface pattern. Meridian should provide a family of dedicated Financial Record Explorers instead
of isolated one-off tables:

| Workspace | Explorer | Primary record focus |
| --- | --- | --- |
| Accounting | Ledger Explorer | Journal entries, ledger detail, account activity, trial balance, fund-event impact, capital-account impact, cash ledger, adjustments, close activity, and report usage |
| Portfolio | Portfolio Explorer | Holdings, positions, transactions, lots, cost basis, cash flows, valuations, income and accruals, exposure, reconciliation status, ledger impact, and report usage |
| Portfolio / Data | Security & Instrument Explorer | Instruments, held securities, identifier maps, terms, obligations, valuation inputs, source conflicts, corporate actions, accounting classification, and evidence coverage |
| Reporting | Report-Line Provenance Explorer | Report-line inputs, approved source records, reconciliations, journal impact, evidence packets, template and package versions, approvals, delivery history, restatements, and audit events |

Each explorer should use the same interaction model: scope the record set, choose or save a view,
filter the grid, review summary signals, select a row, inspect a right-side proof drawer, open the
full record page only when deeper review is needed, and drill into evidence, reconciliation, ledger,
report, approval, and audit impact without losing the original context.

The standard depth model is:

| Level | UI pattern | Purpose |
| --- | --- | --- |
| 1 | Explorer grid | Find, filter, group, sort, export, and compare records quickly. |
| 2 | Side drawer or proof panel | Inspect a selected row without leaving the grid. |
| 3 | Full record page | Review approvals, evidence, history, related records, and protected actions. |

The shared explorer shell should follow a standard explorer contract so operators can move across
financial record types without relearning scope, proof, status, or action semantics:

* **Scope bar dimensions:** tenant, fund, legal entity, portfolio, account, book, period, currency,
  and as-of date. Explorers can hide dimensions that are not meaningful for a record type, but they
  should not rename them or create explorer-specific substitutes.
* **Shared states:** evidence missing, unreconciled, approved, blocked, stale, restated, report-used,
  and close-impacting. These states must use the same definitions, icons, tooltips, filtering
  semantics, proof-ribbon summaries, and audit vocabulary across browser and active WPF workstation
  surfaces.
* **Standard actions:** inspect proof, open full record, compare versions, export allowed view,
  attach evidence, assign exception, and request approval. Explorer-specific actions can extend this
  set, provided that the common actions remain visible and permission-aware.
* **Required subcomponents:** `ExplorerShell`, `ScopeBar`, `SavedViewSelector`, `FilterBar`,
  `ExplorerGrid`, `RecordDrawer`, `ProofRibbon`, `ProofPanel`, `ColumnChooser`, and
  `AuditTimeline`. These components are the minimum reusable contract for a Financial Record
  Explorer and should be backed by shared filters, saved views, record identifiers, evidence links,
  approval state, audit events, and read models.
* **Baseline shell affordances:** saved-view selector surfaced near the top, visible basic filters
  plus an advanced filter drawer, removable applied filter chips, a summary strip for totals and
  exception counts, dense configurable grids with default and optional columns, grouping, export
  policy, row actions, saved column layouts, right-side record drawers, and full record pages with
  record headers, proof ribbons, summary cards, tabs, proof panels, audit timelines, Used In,
  Impacts, and Record Graph sections.

Record Graph does not need to be visually elaborate in the first slice. A structured tree is enough
when it clearly shows how a fund event, journal, position, instrument, cash flow, report line,
evidence document, approval, and audit event are connected.

The browser workstation should emphasize review, navigation, saved views, drill-through, and
manager/controller flows. WPF delivers the dense-workpaper presentation of the same shared state,
with parity tracked as `W8-WPF-PARITY-001`. Both workstations must share filters, saved views,
status definitions, proof-ribbon states, record identifiers, audit events, evidence links, approval
states, and read models. Presentation can differ; business state cannot.

The implementation sequence should stay conservative and preserve a clear initial explorer
sequence:

1. Build the reusable explorer framework: `ExplorerShell`, `ScopeBar`, `SavedViewSelector`,
   `FilterBar`, `ExplorerGrid`, `RecordDrawer`, `ProofRibbon`, `ProofPanel`, `ColumnChooser`, and
   `AuditTimeline`.
2. Build Ledger Explorer first with Journal Entries and Ledger Detail views, core filters, saved
   views, journal drawer/detail, evidence, approval, reversal-chain, and report-usage links.
3. Build Portfolio Explorer second with Holdings and Transactions views, position drawer/detail,
   valuation and reconciliation status, ledger-impact links, instrument links, and report usage.
4. Build Security & Instrument Explorer third with instrument list, identifier map, term status,
   source conflicts, held positions, evidence links, valuation status, expected cash flows, and
   accounting classification.
5. Build Report-Line Provenance Explorer fourth so governed reporting can trace each report line
   through calculation inputs, approved source records, ledger impact, evidence packets, versions,
   approvals, restatements, and audit events.
6. Connect the first four explorers through a Proof Trail that can move from Instrument to Position,
   Transaction, Reconciliation, Journal, Report Line, Evidence, and Audit Event.

This productization target is now complete under roadmap item `W5X-FREX-001`. The completed slice
turns the existing accounting-record and multi-asset coverage evidence into shared Ledger,
Portfolio, Security & Instrument, and Report-Line Provenance explorers over the common
contracts/read-model seam. Endpoint, browser, and prior WPF proof cover saved-view handling, dense-table
and inspector parity, Security Master and Asset Operations projections, report-usage projection,
report-line provenance drill-through, and cross-explorer proof-action routing without browser- or
WPF-local business rules.

The companion completed roadmap item `W5X-FINOPS-001` turns Financial Operations into the operator
control center for reconciliation queues, exception casework, close checklists, workflow controls,
and audit evidence packet readiness over the same W1-W5 evidence baseline.

### v0.20 Product Direction Addendum: Customer-Neutral Operational Proof Layer

The v0.20 design direction sharpens Meridian's market wedge: the product should not compete as
another standalone fund accounting system, reconciliation tool, investor portal, treasury dashboard,
corporate close tool, or document extractor. Those categories already have mature point solutions.
Meridian should make the proof chain across those categories the product.

The operating chain to own is:

```text
Source document / provider file
→ normalized record
→ validation
→ reconciliation
→ exception resolution
→ posting candidate
→ journal / ledger impact
→ capital account impact
→ close package
→ report line
→ delivery record
→ audit evidence
```

This reframes Meridian as a service-neutral operational finance control plane. It should help a GP,
fund CFO, family office, RIA, investment accountant, fund administrator, asset owner, corporate
treasury team, wealth platform, auditor, or multi-entity finance team keep internal control over the
operating record without requiring the customer to replace every external administrator, custodian,
GL, bank, tax provider, audit provider, BI tool, ERP, close tool, or stakeholder portal.

The central v0.20 product object is the Operational Evidence Graph:

| Layer | Meridian-owned proof |
| --- | --- |
| Source | file, API payload, document, provider record, source hash, receipt timestamp |
| Normalization | mapping version, import run, validation result, extraction confidence, reviewer state |
| Reconciliation | match rule, break, true-break narrative, resolution code, owner, SLA, approval |
| Posting candidate | previewed journal impact, selected rule/version, reviewer state, disabled reason |
| Ledger | draft journal, posted journal, posting policy, reversal chain, period lock, version |
| Capital accounts | commitment, contribution, distribution, allocation, NAV impact, statement lineage |
| Close | readiness lane, blocker status, period lock, reopen evidence, late adjustment |
| Reporting | dataset version, report line, template version, package approval, restatement lineage |
| Delivery | recipient list, package version, timestamp, channel, delivery evidence |
| Audit | immutable event trail, retained support package, request list, evidence manifest |

The design targets below must be read with the live roadmap. In particular, Report-Line Provenance
Explorer is complete under `W5X-FREX-001`, while Evidence Vault and statement onboarding are active
under `W5X-EVIDENCE-001` and `W5X-STMT-ONBOARD-001`.

| Tier | Product target | Product intent |
| --- | --- | --- |
| 1 | Operational Evidence Graph | Differentiate Meridian from dashboards and point tools by proving the chain from source to output. |
| 1 | Operational Event Command Spine | Make trades, cash movements, invoices, fees, subscriptions, redemptions, transfers, corporate actions, capital events, valuations, reconciliations, adjustments, approvals, close tasks, and fund-event specializations the universal operating spine. |
| 1 | Capital Account Workbench | Treat capital accounts as governed ledger projections with investor-level evidence, allocation rules, statements, restatements, and audit support. |
| 1 | Private-Capital Close Cockpit | Connect data receipt, reconciliation, journals, capital accounts, NAV support, valuation evidence, reporting, delivery, and period locks. |
| 1 | Evidence Vault with Request Lists | Active productization: complete the governed evidence layer and statement-onboarding acceptance path before broader portal or collaboration expansion. |
| 2 | Shadow NAV and Admin Tie-Out Workbench | Explain administrator-versus-Meridian differences through source records, evidence, reviewer state, ledger impact, close effect, and report effect. |
| 2 | Certified Operational Data Marts | Publish certified cash, positions, transactions, journal entries, capital accounts, operational events, fund events, valuations, trial balance, report lines, and evidence indexes with row-level lineage. |
| 2 | SLA-Driven Exception Operations | Make each break operational by showing owner, SLA, materiality, root cause, supporting evidence, approval state, and blocked outputs. |
| 2 | Payment Intent and Cash Evidence Layer | Capture payment requests, approval chains, expected cash movement, bank confirmations, return/reversal evidence, ledger intent, reconciliation, and report linkage before live payment execution. |
| 2 | Scoped Access Assignment Console | Govern authority by role, permission, scope kind, scope ID, approval limit, segregation-of-duties rule, effective date, version, revocation evidence, and audit event. |
| 3 | Management Company Administration Lite | Support expense allocation, intercompany balances, management fees, bank/card evidence, budget snapshots, cash-plan snapshots, and reimbursements without becoming full ERP. |
| 3 | Report-Line Provenance Explorer | Completed W5X baseline: preserve and deepen drill-through from a report number to source records, reconciliations, journals, approvals, delivery history, and restatements. |
| 3 | Reviewed Automation Assistant | Use AI only for extraction, suggested matches, variance explanations, duplicate detection, journal-template drafts, evidence summaries, missing-support flags, report commentary drafts, and audit request lists. |
| 3 | Hybrid Tenant Profiles | Serve fund administrators, private fund CFOs, RIAs, family offices, asset owners, corporate treasury teams, wealth platforms, administrators, auditors, and insurance investment accounting teams through one core model with profile-specific emphasis. |

The Operational Event Command Spine should make each event navigable by evidence, workflow, posting
candidate, journal lifecycle, ledger impact, account or capital-account impact, treasury
expectation, reconciliation status, report usage, delivery record, tax support, and audit history.
Fund events remain private-capital specializations of that spine. The event is not complete merely
because accounting output exists; it is complete when the event's evidence, approvals, journals,
account or investor impact, reporting outputs, delivery records, and support package can be
reconstructed.

### Capital Account Workbench

The Capital Account Workbench should be the operator surface for proving investor economics rather
than a standalone investor portal. It should scope every view by tenant, organization, fund, legal
entity, investor, investment vehicle, class or series, capital account, book, period, and report
package so that a fund accountant can distinguish fund-level economics from investor-specific,
entity-specific, and statement-specific results. Saved views should preserve that scope and expose
whether the user is reviewing production records, an amendment candidate, a restatement package, or
a tax/audit support request.

The workbench should provide coordinated views for the core capital-account lifecycle:

* Commitment view: subscription commitment, remaining commitment, unfunded balance, side-letter or
  class terms, effective dates, subscription-document evidence, and related closing fund event.
* Contribution view: capital calls, contribution notices, expected cash, bank receipt evidence,
  contribution journal entries, true-up activity, and late or short funding exceptions.
* Distribution view: distribution allocations, notices, expected payment intent, bank/custodian
  confirmation, withholding or tax support, distribution journal entries, and returned or amended
  payment evidence.
* Allocation view: income, expense, realized gain/loss, unrealized gain/loss, fee, carry, special
  allocation, and class/series rule application with allocation-rule version and reviewer state.
* Fee view: management fee, incentive fee, carried-interest, waiver, offset, reimbursement, and
  expense-allocation support linked to fee calculations and Meridian-owned journals.
* NAV view: opening capital, period activity, valuation support, NAV share, roll-forward, shadow NAV
  or administrator tie-out variance, close blockers, and reviewer sign-off.
* Statement view: statement contents, template version, dataset version, report-line provenance,
  delivery status, amendment/restatement state, recipient entitlement, and support manifest.

Every capital-account row should tie out to Meridian-owned journal entries and fund events before it
can be treated as reportable. The workbench should show the fund event that created or changed the
activity, the draft or posted journal entries that carry the accounting impact, the reversal or
adjustment chain for corrections, the period-lock state, and any reconciliation case blocking the
capital-account roll-forward. External administrator, bank, tax, audit, or investor-portal records
may provide evidence or comparison points, but the governed capital-account projection is derived
from Meridian-owned fund events, journals, allocation rules, and approved valuations.

Evidence links should be first-class fields, not notes. The workbench should attach and display
subscription documents, investor notices, capital-call support, contribution receipt support,
distribution support, valuation support, tax/K-1 support, audit-request files, and reviewer
workpapers. Each evidence link should retain source, receipt timestamp, hash or immutable document
version, extraction/review state, related fund event, related journal entry, related report line,
and inclusion status in close, tax, audit, or governed reporting manifests.

Investor statement amendments and restatements should be controlled lifecycle events. An amendment
updates a not-yet-final or non-economic presentation issue while preserving the original statement
version, reason, approver, recipient list, and delivery evidence. A restatement changes a released
or economically material statement and must link to the correcting fund event, reversing or
adjusting journal entries, revised capital-account roll-forward, affected report lines, reissued
package, recipient notification, and audit event chain. Neither path may overwrite the original
statement; users should see superseded, current, pending-amendment, and restated states side by side
with variance explanations and release approvals.

Required report links should connect each investor statement and capital-account roll-forward to the
governed reporting package that consumed it. The workbench should expose report package ID, report
line ID, package approval state, template version, dataset version, report-line provenance, delivery
record, restatement lineage, and downstream tax or audit support package usage. A report line is
ready only when the capital-account source rows, journals, fund events, evidence links, and reviewer
approvals can be traced from the statement back through the Operational Evidence Graph.

The Private-Capital Close Cockpit should operate by fund, book, period, and entity. A close lane is
ready only when required data arrived, imports validated, reconciliation blockers cleared, journals
posted, reversals approved, recurring journals completed, capital-account roll-forwards tied out,
valuation support attached, stale marks resolved, shadow NAV tied out, statements approved,
packages delivered, and period locks or reopen evidence are retained.

#### Evidence Vault

The Evidence Vault should not be a passive document store. It should manage request lists by event,
close, audit, tax, and report package; capture documents through upload, email, API, portal download,
or SFTP; extract fields with confidence and review state; validate extracted values against expected
records; link evidence to fund events, journals, reconciliations, and report lines; and freeze
manifests for downstream support packages.

The Admin-Neutral Control Plane is a design constraint: external administrators, GLs, custodians,
banks, tax providers, audit providers, BI tools, and investor portals remain valid external systems,
but Meridian owns the verification, evidence, reconciliation, approval, ledger impact, report
provenance, delivery history, and audit trail that prove whether outputs can be trusted.

### Reviewed Automation Assistant

Reviewed automation must remain ledger-safe and evidence-bound. The Reviewed Automation Assistant is
a cross-workflow assistant for operator review, not an autonomous accounting, payment, reporting, or
evidence-destruction actor. It can accelerate close, reporting, and audit preparation only when every
suggestion remains reviewable, attributable, and reversible before acceptance.

**Allowed actions**

* Extraction suggestions from source evidence, imported files, statements, confirmations, agreements,
  notices, report packs, and operator-provided context.
* Match suggestions between source records, normalized transactions, reconciliations, journals,
  fund events, capital-account impacts, report lines, and retained evidence.
* Variance explanations that summarize probable drivers, materiality context, stale-data signals,
  timing differences, mapping differences, or unresolved source conflicts.
* Duplicate detection for documents, transactions, journal-template candidates, evidence links,
  report-line support, and audit request responses.
* Journal-template drafts that propose debits, credits, descriptions, source links, reversal timing,
  and supporting evidence without posting or approving entries.
* Evidence summaries that condense document contents, extracted fields, lineage, confidence,
  review state, source hash, and downstream dependencies.
* Missing-support flags for absent confirmations, incomplete source documents, unsupported report
  lines, open reconciliations, unresolved extraction conflicts, or missing approval evidence.
* Report commentary drafts that explain approved data, known exceptions, period activity, variance
  drivers, and evidence-backed caveats for human editing and approval.
* Audit request list drafts that group required support by event, period, report package, journal,
  reconciliation, stakeholder delivery, or evidence manifest.

**Prohibited actions**

* Posting material journals.
* Overriding period locks.
* Approving its own work or any downstream workflow that depends on its suggestion.
* Releasing payments.
* Publishing reports.
* Editing posted entries.
* Deleting evidence, evidence manifests, source files, extracted fields, audit events, or lineage
  records.

**Review workflow**

Automation output must move through explicit human-controlled states: `Suggested`, `Reviewed`,
`Accepted`, `Edited`, `Rejected`, and `Escalated`. `Suggested` records are assistant-authored and
non-authoritative. `Reviewed` records have been inspected by an assigned operator. `Accepted`
records may feed the next governed workflow step only when permissions, evidence requirements, and
segregation-of-duties rules allow it. `Edited` records retain both assistant output and human
revision. `Rejected` records remain available for audit and model-quality review. `Escalated`
records require a domain owner, controller, compliance reviewer, or configured approver before they
can influence close, report, audit, or payment workflows.

**Audit requirements**

Every assistant suggestion must retain the model and version, prompt or input context, assigned
reviewer, review timestamp, source evidence references, and resulting action. The audit record must
link the suggestion to source hashes or vault references, affected business objects, confidence or
uncertainty markers, review decision, human edits, downstream workflow transition, and any escalation
or rejection reason.

**UI placement**

* `Data` workflows surface extraction suggestions, match suggestions, duplicate detection,
  variance explanations, missing-support flags, and evidence summaries during import, validation,
  mapping, confidence review, and source-certification steps.
* `Accounting` workflows surface journal-template drafts, reconciliation explanations,
  missing-support flags, duplicate detection, and evidence summaries during close, adjustment,
  fund-event, capital-account, and ledger-support review.
* `Reporting` workflows surface report commentary drafts, report-line support summaries, variance
  explanations, duplicate detection, missing-support flags, and audit request list drafts before
  package approval or publication.
* `Evidence Vault` workflows surface extraction suggestions, evidence summaries, duplicate
  detection, missing-support flags, audit request list drafts, and manifest-readiness warnings while
  preserving document retention and lineage controls.

The v0.18 product promise is:

```text
Meridian does not just show the number.
Meridian proves the number.
```

This is a planned product direction, not a completion claim. Roadmap and implementation claims still
require registry-backed acceptance evidence before stakeholder-facing status can move from planned
or supported to complete.

### v0.18 Addendum: Operational Evidence Graph Product Surface

The Operational Evidence Graph should become the reusable proof surface that browser and WPF
workstations can open from any material operating record. It is not a separate compliance module; it
is the shared navigation, manifest, and proof contract that explains how a retained source became a
ledger, close, report, delivery, or audit object.

#### Standard Graph Layers

Every graph view, proof drawer, and exported manifest should preserve these layers even when a
particular subject has no records in a layer yet. Missing required layers must render as
`review-required` or `blocked`, not as implicit success.

| Layer | Required product meaning | Typical record examples |
| --- | --- | --- |
| Source | Original retained input and receipt proof. | Provider payload, administrator file, bank statement, document, source hash, vault object, receipt timestamp. |
| Normalization | Transformation from source into Meridian-controlled records. | Import run, mapping profile, schema version, extraction confidence, reviewer state, normalized transaction, position, balance, or document field. |
| Validation | Deterministic checks that make the normalized record usable. | Validation rule, data-quality gate, duplicate check, required-field result, tolerance result, validation reviewer, validation exception. |
| Reconciliation | Tie-out between expected, source, portfolio, cash, ledger, administrator, or report records. | Reconciliation run, match rule, break, true-break narrative, resolution code, assignment, SLA, approval. |
| Ledger | Accounting impact and posting control. | Draft journal, posted journal, ledger detail, posting policy, reversal chain, period lock, ledger version. |
| Capital accounts | Investor-level capital projection and statement lineage. | Commitment, contribution, distribution, allocation, capital-account roll-forward, NAV impact, statement line. |
| Close | Period readiness, blockers, lock state, and reopen proof. | Close lane, checklist item, blocker, late adjustment, reopen request, period lock, close package. |
| Reporting | Report number provenance and package approval. | Dataset version, report line, template version, package approval, restatement lineage, report-pack snapshot. |
| Delivery | Stakeholder publication evidence. | Recipient list, package version, timestamp, channel, delivery artifact, delivery exception. |
| Audit | Immutable reconstruction support. | Audit event, retained support package, request list, evidence manifest, export hash, reviewer attestation. |

#### Reusable Browser and WPF UI Pattern

The browser workstation in `src/Meridian.Ui/dashboard/` and the WPF workstation in
`src/Meridian.Wpf/` should consume the same read models and service APIs for all proof surfaces. UI
implementations may differ in layout mechanics, but the business state, action eligibility, layer
labels, link semantics, and manifest payload must remain shared.

| Pattern | Purpose | Minimum behavior |
| --- | --- | --- |
| Compact proof ribbon | Inline confidence strip for grids, drawers, cards, and command-center rows. | Shows layer coverage, blocker count, evidence freshness, approval posture, export availability, and a one-click route to the side proof drawer. |
| Side proof drawer | Contextual proof inspection without leaving the operator task. | Shows the selected subject, ordered layer summary, required versus present evidence, key warnings, first-hop links, audit timeline preview, and actions to validate, export, or open the full graph. |
| Full proof graph page | Deep reconstruction surface for complex investigations and audits. | Provides layered graph navigation, filters by layer/link/status, path tracing from source to output, missing-link highlighting, compare/restatement mode, and stable deep links back to source explorer records. |
| Exportable evidence manifest | Durable handoff artifact for audit, tax, reporting, admin tie-out, or internal approval. | Exports subject identifiers, node identifiers, link types, source hashes or vault references, generated timestamp, schema version, validation warnings, completeness state, and reviewer/export attestations. |

#### Required Entry Points

Operational Evidence Graph entry points should be available anywhere the operator sees a material
record whose trust depends on upstream proof. The first implementation candidates should include:

* **Ledger Explorer:** open proof from journal entries, ledger details, reversal chains, posting
  policies, close locks, report usage, and capital-account impact.
* **Portfolio Explorer:** open proof from positions, lots, transactions, valuations, cash flows,
  reconciliation status, ledger impact, instrument links, and report usage.
* **Report-Line Provenance Explorer:** open proof from a report line, dataset version, package
  approval, restatement lineage, delivery record, and stakeholder output.
* **Fund Event Command Center:** open proof from capital calls, distributions, investments, fees,
  valuations, closings, tax requests, audit requests, wind-downs, approvals, and event blockers.
* **Evidence Vault:** open proof from request-list items, uploaded documents, extracted fields,
  confidence reviews, frozen manifests, support packages, and vault object references.

#### Shared Read-Model and Service API Minimums

Shared read models in `src/Meridian.Ui.Shared/` and service APIs in `src/Meridian.Ui.Services/`
should use stable identifiers and typed links so browser and WPF do not invent incompatible proof
semantics. At minimum, graph-capable DTOs should carry:

* `subjectKind`, `subjectId`, `tenantId`, `fundId`, `entityId`, `bookId`, `periodId`, and optional
  `investorId`, `accountId`, `instrumentId`, `portfolioId`, `reportPackId`, `fundEventId`,
  `evidenceVaultObjectId`, and `auditEventId` when those scopes are relevant.
* Node identifiers: `evidenceId`, `layer`, `recordKind`, `recordId`, `recordVersion`, `status`,
  `generatedAt`, `effectiveAt`, `sourceSystem`, `sourceUri` or vault reference, `sourceHash`,
  `schemaVersion`, `owner`, `reviewer`, `approvalId`, and `warningCodes`.
* Link identifiers: `edgeId`, `fromEvidenceId`, `toEvidenceId`, `linkType`, `linkStatus`,
  `createdAt`, `createdBy`, `ruleId`, `confidence`, and optional `materiality`, `tolerance`,
  `restatementId`, `reversalId`, or `deliveryId`.
* Standard link types: `derived_from`, `normalized_by`, `validated_by`, `matched_to`,
  `reconciled_by`, `break_resolved_by`, `posted_to`, `reversed_by`, `allocated_to`,
  `rolled_forward_to`, `closed_by`, `reopened_by`, `reported_as`, `approved_by`,
  `delivered_as`, `requested_by`, `satisfies_request`, `attested_by`, `exported_as`, and
  `audited_by`.

#### Roadmap Acceptance Language

Roadmap candidates that claim the Operational Evidence Graph should stay `planned` until
implementation evidence exists in the roadmap registry. Candidate acceptance must require shared
read models and service APIs, at least one browser entry point, at least one WPF entry point or an
explicit WPF parity plan, exported manifest validation, retained source or vault references, and
tests proving that missing required evidence remains `review-required` or `blocked`. A candidate may
move beyond `planned` only when `docs/roadmap/data/roadmap-items.yml` links concrete tests, source
modules, and implementation paths that prove the graph layers, UI pattern, entry points, identifiers,
link types, and manifest behavior are implemented rather than only designed.

---

## 26. LedgerGraph OS Target Architecture and Product Wedge

Version 0.21 translates the evidence-native operating-layer concept into Meridian delivery terms.
The durable architecture remains a common financial core with vertical packages for fund
administration and family-office workflows. Approximately 70% of the technology should stay common
across the platform: multi-book ledger records, positions, valuations, documents, workflows,
reconciliations, reporting, source evidence, connectors, permissions, and audit history. The
remaining package-specific layer should adapt terminology, workflows, and templates for fund
administration or family-office operations.

### Product Structure

| Common Platform | Fund Administration Package | Family Office Package |
| --- | --- | --- |
| Multi-book ledger, positions, valuations, source vault, documents, workflows, reconciliations, reporting, data connections, evidence graph, and governed permissions. | Commitments, closings, allocations, equalization, waterfalls, carry, fees, partner capital accounts, NAV workflows, investor onboarding, statements, notices, and administrator service operations. | Entity accounting, consolidations, eliminations, trusts, household views, intercompany activity, liquidity planning, liabilities, guarantees, advisor coordination, principal/family portal views, and succession or concentration analysis. |

### Architecture Pattern

Use a modular monolith for the accounting and transaction core. Ledger posting, position changes,
allocations, consolidations, official locks, and close controls require strong transactional
consistency and should not be fragmented into premature microservices. Separate services remain
appropriate for connector execution, document processing, workflow orchestration, search,
analytics, notifications, and AI workloads.

The authoritative ledger remains the financial source of truth. Object storage preserves raw
payloads and documents. An event bus distributes normalized business events. A columnar warehouse
serves analytics and reporting. A graph projection supports ownership, relationship, and evidence
navigation. A search index supports document, transaction, entity, and commentary discovery. The
graph and warehouse are projections, not competing books of record.

### Contract Packs for Assets and Liabilities

Meridian should model assets and liabilities as economic contracts rather than hard-coded product
categories. The common financial-object spine should define stable identity, ownership, entity,
book, evidence, lifecycle, valuation, accounting, reconciliation, permission, and reporting hooks.
Versioned packs then define contract schema, lifecycle events, valuation methods, accounting rules,
validation rules, and reporting taxonomy.

Launch should prefer wide capture and narrow automation: cash, bank accounts, public equities,
ETFs, fixed income, private funds, partnerships, private loans, credit, real estate, basic
derivatives, FX, mortgages, credit facilities, intercompany loans, unfunded commitments, guarantees,
and a controlled other-asset pack can be recorded early, while deeper lifecycle automation is added
according to customer demand and roadmap acceptance.

### Number Passport

A Number Passport is the signature evidence object for every balance, performance figure, capital
account value, report total, dashboard metric, close variance, or liquidity forecast. It should show
amount, currency, book, accounting basis, effective date, update date, underlying positions, journal
entries, original source records, source documents, extracted fields, transformation or allocation
rules, valuation methodology, reconciliation state, preparer/reviewer/approval history, period-over-
period change, confidence, and freshness.

The user-facing principle is:

> Ask any question. Verify every answer.

### Ingestion Tiers and Connector Contract

The platform should not depend on perfect APIs. Every connector should follow a common lifecycle:
authenticate, retrieve, retain the original payload, detect duplicates, normalize, map identities,
validate, reconcile, publish business events, and monitor freshness or failures. Ingestion should
cover direct APIs and webhooks, structured files and financial messages, documents/email/portal
collection, and governed manual input. Use external standards selectively as interoperability
vocabulary rather than transaction-database shape: FIBO terminology for financial meaning and
relationships, FIGI/ISIN/CUSIP/provider IDs for instruments, ISO 20022 concepts for cash and
payment messaging, and ILPA-compatible private-market reports and notices.

### Responsible AI Boundary

AI should operate above deterministic financial services. It may extract terms, propose entity or
ownership relationships, suggest mappings, match transactions, explain reconciliation differences,
draft commentary, identify missing documents, answer questions with evidence links, and propose
structured policies. It should not post official journal entries, calculate authoritative waterfalls
from free-form language, release payments, change ownership records, replace deterministic tax,
accounting, or performance calculations, or answer without exposing sources and calculation basis.

A future Policy Compiler should convert plain-language policy into proposed structured rules, sample
calculations, edge cases, historical comparisons, user approval, and versioned production rules.

### Market-Entry Wedge and Staged Roadmap

The first packaged product should be the **Close, Data and Evidence Control Tower**. It should work
above existing spreadsheets, accounting systems, and portfolio systems with entity and ownership
graphs, an asset/liability registry, document and source-data vault, CSV/Excel/email/SFTP ingestion,
high-priority bank and custodian connectors, opening-balance and position reconciliations, close
checklists, reviewer workflows, exception management, consolidated balance-sheet and performance
reports, Number Passports, audit-ready close binders, and read-only client/advisor portal views.

Subsequent product stages are to establish trust through the control tower, become the accounting
system with native multi-book ledger and private-capital accounting workflows, and then create the
ecosystem through connector SDKs, certified asset packs, policy/report template marketplace,
scenario modeling, tax-package integrations, opt-in benchmarks, white-label administration support,
and controlled AI agents.

The primary north-star product metric should be **Verified Coverage**: the percentage of reported
assets and liabilities that are current, reconciled, approved, and linked to supporting evidence.

---

## 27. Foundational Product Slice

The foundational product slice remains:

### Data Operations + Reconciliation Foundation

This slice is no longer only a recommendation. It is the product baseline that W1-W5 prove through trusted data, paper validation, research continuity, ledger reconciliation, accounting records, approvals, multi-asset operational coverage, and governed reporting.

### Includes

* Tenant profile
* Entity model
* Account model
* Capital account model
* Ledger account and journal entry model
* Provider setup
* File/API import
* Raw data preservation
* Mapping profile
* Validation rules
* Normalized transactions / positions / balances
* Reconciliation run
* Exception queue
* Workflow approvals
* Capital activity evidence
* Treasury-ledger posting controls
* Audit events
* Basic reporting package

### Existing Evidence

Current repository evidence already covers:

* Provider validation and data-confidence gates
* Paper-session readiness and replayable acceptance evidence
* Promotion review and research-to-paper continuity
* Portfolio ledger reconciliation and close-lane casework
* Governed report-pack approval, provenance, and export evidence
* Accounting record summaries linking source data, normalized activity, reconciliation cases, ledger evidence, approvals, and report-pack lineage
* Multi-asset operational coverage with provider evidence, ledger classification, reconciliation signals, and close blockers
* Shared financial record explorers for ledger records, portfolio holdings and transactions, Security Master instruments, and report-line provenance over common contracts, saved views, proof ribbons, proof panels, record graphs, evidence links, approval state, reconciliation state, report usage, and audit timelines
* Browser and active WPF workstation read models over shared contracts, with remaining desktop screen parity tracked as `W8-WPF-PARITY-001`
* Completed statement connectors plus active Evidence Vault and browser-first statement-onboarding evidence routes
* Shared manual journal entry workbench projection of retained private-capital fund events and capital-account activity from treasury-ledger context, plus a first-class private-capital activity review endpoint for reporting and audit consumers

### Remaining Expansion Work

The baseline does not yet imply completion of:

* Full treasury payment execution
* Full alternative asset operations
* Full forecasting engine
* Full enterprise risk engine
* Follow-on proof-layer expansion beyond the completed W5X Financial Operations control-center boundary
* Complex capital structure modeling
* Full client portal
* Full no-code workflow designer
* Full live-trading readiness
* Backtesting Studio beyond evidence-linking support
* Mobile applications

### Why This Slice Works

It proves Meridian’s core value:

```text
Fragmented data
→ Controlled import
→ Validation
→ Reconciliation
→ Review
→ Approval
→ Evidence
→ Reporting
```

This foundation supports fund administrators, RIAs, and family offices without overbuilding too early.
