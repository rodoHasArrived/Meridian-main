# Meridian Design Document — Version 0.18

**Status:** canonical  
**Owner:** core-team  
**Reviewed:** 2026-06-09
**Source:** Draft v1.0 imported from `C:\Users\Andrew James Rowden\.codex\attachments\2bedc368-4dca-449f-923b-b098cf8bb4d5\pasted-text.txt`; Version 0.16 extends the roadmap and source-module registry evidence with the v0.15 accounting records package plus current Carta Fund ERP, Carta Data Warehouse, Carta Management Company Administration, FundStudio fund administrator, FundStudio managed-services, FundStudio general-ledger/accounting, and Modern Treasury ledger research. Version 0.17 adds the shared Financial Record Explorer productization target from `C:\Users\Andrew James Rowden\.codex\attachments\e76a7c8a-33a1-45f6-bf2e-036d6635920d\pasted-text.txt`. Version 0.18 incorporates the operational proof layer market-gap update from `C:\Users\Andrew James Rowden\.codex\attachments\7c4bee43-4269-4284-8747-2bdeadf0287b\pasted-text.txt`.

## 1. Product Vision

Meridian is a modular, configurable financial operations platform for fund administrators, registered investment advisors, family offices, and other investment organizations. The platform helps financial operations professionals acquire, validate, reconcile, govern, analyze, forecast, and report on financial data through a single auditable workflow.

Meridian should not initially try to replace every external system. Instead, it should become the operational system of record for validated workflows, evidence, reconciliations, decisions, and certified reporting outputs. For ledger records specifically, Meridian is the source of all ledger truth; external accounting systems contribute read-only evidence and reconciliation signals unless an approved publishing workflow explicitly exports Meridian-owned entries.

The current product scope is deliberately narrower than the full long-term domain catalog. Active product work should strengthen data confidence, reconciliation, approvals, accounting records, retained evidence, and governed reporting before expanding Backtesting Studio, live trading, full payments, forecasting, enterprise risk, client portal, no-code workflow design, mobile, or other broad platform lanes.

### Core Vision Statement

> Meridian helps financial operations professionals transform fragmented financial data into trusted, auditable operational outcomes.

Meridian should not merely show an operational number. It should prove the number by preserving the
chain from source evidence through normalization, validation, reconciliation, ledger impact,
capital-account impact, report usage, delivery evidence, and audit history.

### Core User Objective

The primary user hires Meridian to know:

* What happened
* Why it happened
* Whether it can be trusted
* What needs to be reviewed, approved, reconciled, paid, forecasted, or reported

### Active Scope Gate

A new capability belongs in the active product scope only when it helps an operator prove what
happened, why it happened, whether it can be trusted, and what was reconciled, approved, recorded,
or reported. Capabilities outside that operational record workflow remain deferred unless the
roadmap registry explicitly moves them into scope.

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
| [Modern Treasury Ledgers](https://docs.moderntreasury.com/ledgers/docs/overview), [ledger guarantees](https://docs.moderntreasury.com/ledgers/docs/ledgers-guarantees), and ledger engineering posts on [transaction models](https://www.moderntreasury.com/journal/how-to-scale-a-ledger-part-iii), [immutability and double-entry](https://www.moderntreasury.com/journal/how-to-scale-a-ledger-part-v), and [optimistic locking](https://www.moderntreasury.com/journal/designing-ledgers-with-optimistic-locking) | Immutable double-entry ledgering, idempotent writes, atomic transactions, per-currency balancing, pending/posted/archived transaction states, append-only versions, and concurrency controls. | Make Meridian-owned ledger records treasury-grade: posted entries are immutable, corrections use reversing or adjusting journals, writes are idempotent and atomic, balance-affecting records are per-currency balanced, and authoritative ledger writes fail closed under stale versions or missing evidence. |

Meridian should not treat this as permission to build a full cap-table system, outsourced services
operation, live payment processor, broad investor portal, or autonomous-agent workflow that bypasses
operator evidence. Those remain separate product decisions unless the roadmap moves them into scope.

### External Functionality Translation Requirements

External offerings should translate into Meridian-owned software capabilities, not copied service
promises:

* Fund events should become first-class operational records: formation/closing, subscription packet,
  capital call, contribution receipt, investment, distribution, valuation, fee/expense, tax request,
  audit request, and dissolution/wind-down support.
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
* FundStudio-style admin controls should drive fund/book/period/report administration: multi-book
  ledgers, locked periods, period reopen evidence, journal templates, recurring journals,
  year-end-close workflows, portfolio-specific pricing rules, fund cloning/onboarding templates, and
  immutable logs for every posting, lock, export, and delivery event.
* Middle-office managed-service patterns should become internal workflow primitives: T+0 booking,
  T+1 trade/cash/position reconciliation, true-break escalation, SLA timers, normalized file
  distribution to admins/custodians/counterparties, and archived delivery logs.
* AI or agent-like automation is acceptable only as reviewed discrepancy detection, extraction, or
  draft-preparation assistance; it cannot bypass operator approval, evidence, ledger controls, or
  period locks.
* Fund events should become end-to-end operating objects that connect evidence, workflow, treasury,
  ledger, capital accounts, reconciliation, reporting, delivery, tax, and audit impact.
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
* Payment work should begin as payment intent and cash evidence, not premature live execution:
  request, approval, expected cash movement, bank confirmation, ledger intent, reconciliation, and
  report linkage are the near-term product surface.
* Authority must be scoped by tenant, organization, fund, legal entity, account, book, period,
  document, report package, delivery record, amount limit, and segregation-of-duties posture.

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

### Planned and Gated Baselines

W6 and W7 remain planned, not complete:

| Wave | Planned Capability | Gate |
| --- | --- | --- |
| W6 | Backtesting studio evidence loop | Backtesting Studio is deferred behind the W1-W5 operational record baseline; backtest results must link to strategy lineage and operator-facing acceptance criteria before paper promotion expansion. |
| W7 | Live-readiness governance | Live operation remains gated by trusted data, paper validation, reconciliation, approvals, governed reporting evidence, accounting records, and explicit governance sign-off. |

### Active Product Surfaces

Meridian has two active operator UI surfaces:

* `src/Meridian.Ui/dashboard/` is the browser workstation source, with built host-served assets under `src/Meridian.Ui/wwwroot/workstation/`.
* `src/Meridian.Wpf/` is the active Windows desktop operator workstation.
* `src/Meridian.Ui.Services/`, `src/Meridian.Ui.Shared/`, and `src/Meridian.Contracts/` provide shared endpoint, read-model, and DTO support so browser and desktop do not invent separate product state.
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
| Document & Knowledge Management | Design-led foundation | Evidence links and report artifacts exist; full document vault and knowledge-management features remain future work. |
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
| Private-capital close cockpit | W5 accounting records, close-lane blockers, fund-event reconstruction, capital-account subledger impact, ledger evidence, report-pack lineage, and shared browser/WPF read models. | NAV readiness, period lock/reopen posture, administrator tie-out, journal/reversal boundaries, reviewer ownership, and report readiness are defined as product objects. | Complete close cockpit with fund/book/period dashboards, close-state ladder, SLA ownership, statement release, amendment, restatement, and tax/K-1 support workflows. |
| Financial Record Explorers | Shared accounting/reporting/portfolio evidence routes, ledger and report pack foundations, private-capital review endpoints, and multi-asset operational coverage. | Explorer shell concepts, proof drawers, saved views, right-side record inspection, proof ribbons, audit timelines, and record graphs are productized design patterns. | Ledger Explorer, Portfolio Explorer, and Security & Instrument Explorer as complete shared browser/WPF product surfaces. |
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
* Shared publication into storage, accounting, reporting, strategy, audit, browser workstation, and WPF surfaces

### Operating Requirements

* UI surfaces must consume shared contracts and read models for provider posture, validation state, and publication status instead of owning provider-trust logic.
* Missing, stale, or unsupported source evidence must create review-required or blocked states rather than plausible-looking operational data.
* New provider work should start from ProviderSdk contracts and Infrastructure adapters, then publish through shared services before adding browser or WPF presentation.
* Channel expansion is active-scope work only when it strengthens the W1-W5 operational record baseline: data confidence, retained evidence, reconciliation, approvals, accounting records, multi-asset coverage, or governed reports.

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

### Roadmap Productization

`W5X-FINOPS-001` tracks the planned Financial Operations control center that turns
reconciliation, exception management, accounting operations, close support, workflow control, and
audit evidence into a shared Accounting/Reporting operator surface. This is planned productization,
not a completion claim; W1-W5 remain the closed evidence baseline.

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
  blocked report line, or late delivery item is still open for the applicable fund, book, period,
  account, or recipient package.

Drill-through is part of the cockpit contract, not a separate reporting convenience. A queue row or
status tile should open the relevant proof surface with the same fund/book/period/account/report
context preserved:

* **Ledger Explorer** for journal detail, account activity, trial-balance impact, reversal chains,
  approval state, and close-lock effect.
* **Evidence Vault** for retained source files, request lists, extraction status, missing-support
  tasks, frozen manifests, and legal-hold or retention signals.
* **Fund Event Command Center** for capital calls, distributions, fees, expenses, subscriptions,
  redemptions, transfers, valuation updates, treasury expectations, and event-level completion
  blockers.
* **Report-Line Provenance Explorer** for report-line inputs, source records, reconciliations,
  journals, approvals, template version, delivery package, and restatement lineage.

Browser and WPF experiences should share the same cockpit read-model state while optimizing for
different work styles. The browser workstation should emphasize role-based triage, lightweight
queue review, cross-workspace drill-through, comments, assignments, and governed release decisions
from `src/Meridian.Ui/dashboard/`. The WPF desktop surface should emphasize dense workpaper
execution: virtualized grids, frozen columns, keyboard-first filtering, account-level tie-out,
bulk assignment, evidence matching, journal review, and side-by-side reconciliation workpapers in
`src/Meridian.Wpf/`. Neither surface should own a divergent close state; both should consume shared
Accounting/Reporting read models from `src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/` so
operator actions, approval blockers, and release readiness remain consistent.

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

---

## 24. Updated Design Thesis

> Meridian is designed as a modular, configurable financial operations platform. Its core financial model is intentionally stable, centered on entities, accounts, capital accounts, ledger accounts, instruments, contracts, obligations, cash flows, transactions, journal entries, positions, reconciliations, documents, workflows, reports, delivery records, and audit events. Around that stable core, Meridian provides tenant-specific configuration for workflows, rules, integrations, source-of-record policies, reporting, permissions, ledger controls, and custom attributes. This allows Meridian to support fund administrators, private fund managers, RIAs, family offices, and other investment organizations without creating separate products or sacrificing auditability.

---

## 25. Design Backlog and Remaining Productization Work

The following artifacts should be maintained or expanded to keep the design document aligned with the existing implementation and remaining productization work.

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

Defines the product screen inventory across the active browser workstation and WPF desktop lanes.

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

The package should preserve the shared-first UI direction: browser and WPF surfaces consume shared contracts, endpoint read models, and services rather than inventing separate accounting state.

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
  semantics, proof-ribbon summaries, and audit vocabulary across browser and WPF surfaces.
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
manager/controller flows. The WPF desktop should emphasize high-density workpaper modes, bulk
review, frozen columns, large reconciliation or journal grids, import validation, and evidence
review. Both surfaces must share filters, saved views, status definitions, proof-ribbon states,
record identifiers, audit events, evidence links, approval states, and read models. Presentation can
differ; business state cannot.

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

This is a planned productization target, not a completion claim. The roadmap item
`W5X-FREX-001` tracks the delivery slice that turns the existing accounting-record and
multi-asset coverage evidence into these shared explorers.

The companion planned roadmap item `W5X-FINOPS-001` turns Financial Operations into the operator
control center for reconciliation queues, exception casework, close checklists, workflow controls,
and audit evidence packet readiness over the same W1-W5 evidence baseline.

### v0.18 Product Direction Addendum: Operational Proof Layer

The v0.18 design direction sharpens Meridian's market wedge: the product should not compete as
another standalone fund accounting system, reconciliation tool, investor portal, treasury dashboard,
or document extractor. Those categories already have mature point solutions. Meridian should make
the proof chain across those categories the product.

The operating chain to own is:

```text
Source document / provider file
→ normalized record
→ validation
→ reconciliation
→ exception resolution
→ journal / ledger impact
→ capital account impact
→ close package
→ report line
→ delivery record
→ audit evidence
```

This reframes Meridian as a service-neutral private-capital and investment-operations control plane.
It should help a GP, fund CFO, family office, RIA, investment accountant, or fund administrator keep
internal control over the operating record without requiring the customer to replace every external
administrator, custodian, GL, bank, tax provider, audit provider, BI tool, or investor portal.

The central v0.18 product object is the Operational Evidence Graph:

| Layer | Meridian-owned proof |
| --- | --- |
| Source | file, API payload, document, provider record, source hash, receipt timestamp |
| Normalization | mapping version, import run, validation result, extraction confidence, reviewer state |
| Reconciliation | match rule, break, true-break narrative, resolution code, owner, SLA, approval |
| Ledger | draft journal, posted journal, posting policy, reversal chain, period lock, version |
| Capital accounts | commitment, contribution, distribution, allocation, NAV impact, statement lineage |
| Close | readiness lane, blocker status, period lock, reopen evidence, late adjustment |
| Reporting | dataset version, report line, template version, package approval, restatement lineage |
| Delivery | recipient list, package version, timestamp, channel, delivery evidence |
| Audit | immutable event trail, retained support package, request list, evidence manifest |

The highest-value feature gaps should be prioritized in this order:

| Tier | Product target | Product intent |
| --- | --- | --- |
| 1 | Operational Evidence Graph | Differentiate Meridian from dashboards and point tools by proving the chain from source to output. |
| 1 | Fund Event Command Center | Make capital calls, distributions, investments, fees, valuations, closings, tax requests, audit requests, and wind-downs the universal operating spine. |
| 1 | Capital Account Workbench | Treat capital accounts as governed ledger projections with investor-level evidence, allocation rules, statements, restatements, and audit support. |
| 1 | Private-Capital Close Cockpit | Connect data receipt, reconciliation, journals, capital accounts, NAV support, valuation evidence, reporting, delivery, and period locks. |
| 1 | Evidence Vault with Request Lists | Turn document intake and extraction into close, tax, audit, and reporting support packages with frozen manifests. |
| 2 | Shadow NAV and Admin Tie-Out Workbench | Explain administrator-versus-Meridian differences through source records, evidence, reviewer state, ledger impact, close effect, and report effect. |
| 2 | Certified Operational Data Marts | Publish certified cash, positions, transactions, journal entries, capital accounts, fund events, valuations, trial balance, report lines, and evidence indexes with row-level lineage. |
| 2 | SLA-Driven Exception Operations | Make each break operational by showing owner, SLA, materiality, root cause, supporting evidence, approval state, and blocked outputs. |
| 2 | Payment Intent and Cash Evidence Layer | Capture payment requests, approval chains, expected cash movement, bank confirmations, return/reversal evidence, ledger intent, reconciliation, and report linkage before live payment execution. |
| 2 | Scoped Access Assignment Console | Govern authority by role, permission, scope kind, scope ID, approval limit, segregation-of-duties rule, effective date, version, revocation evidence, and audit event. |
| 3 | Management Company Administration Lite | Support expense allocation, intercompany balances, management fees, bank/card evidence, budget snapshots, cash-plan snapshots, and reimbursements without becoming full ERP. |
| 3 | Report-Line Provenance Explorer | Let operators drill from a report number to source records, reconciliations, journals, approvals, delivery history, and restatements. |
| 3 | Reviewed Automation Assistant | Use AI only for extraction, suggested matches, variance explanations, duplicate detection, journal-template drafts, evidence summaries, missing-support flags, report commentary drafts, and audit request lists. |
| 3 | Hybrid Tenant Profiles | Serve fund administrators, private fund CFOs, RIAs, family offices, and insurance investment accounting teams through one core model with profile-specific emphasis. |

The Fund Event Command Center should make each event navigable by evidence, workflow, ledger impact,
capital-account impact, treasury expectation, reconciliation status, report usage, delivery record,
tax support, and audit history. The event is not complete merely because accounting output exists;
it is complete when the event's evidence, approvals, journals, investor impact, reporting outputs,
delivery records, and support package can be reconstructed.

The Private-Capital Close Cockpit should operate by fund, book, period, and entity. A close lane is
ready only when required data arrived, imports validated, reconciliation blockers cleared, journals
posted, reversals approved, recurring journals completed, capital-account roll-forwards tied out,
valuation support attached, stale marks resolved, shadow NAV tied out, statements approved,
packages delivered, and period locks or reopen evidence are retained.

The Evidence Vault should not be a passive document store. It should manage request lists by event,
close, audit, tax, and report package; capture documents through upload, email, API, portal download,
or SFTP; extract fields with confidence and review state; validate extracted values against expected
records; link evidence to fund events, journals, reconciliations, and report lines; and freeze
manifests for downstream support packages.

The Admin-Neutral Control Plane is a design constraint: external administrators, GLs, custodians,
banks, tax providers, audit providers, BI tools, and investor portals remain valid external systems,
but Meridian owns the verification, evidence, reconciliation, approval, ledger impact, report
provenance, delivery history, and audit trail that prove whether outputs can be trusted.

Reviewed automation must remain ledger-safe. It can suggest, classify, extract, match, summarize,
draft, and flag. It cannot post material journals without approval, override period locks, approve
its own work, release payments, publish reports, edit posted entries, or erase evidence.

The v0.18 product promise is:

```text
Meridian does not just show the number.
Meridian proves the number.
```

This is a planned product direction, not a completion claim. Roadmap and implementation claims still
require registry-backed acceptance evidence before stakeholder-facing status can move from planned
or supported to complete.

---

## 26. Foundational Product Slice

The foundational product slice remains:

# Data Operations + Reconciliation Foundation

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
* Shared browser and WPF workstation read models for operator workflows
* Shared manual journal entry workbench projection of retained private-capital fund events and capital-account activity from treasury-ledger context, plus a first-class private-capital activity review endpoint for reporting and audit consumers

### Remaining Expansion Work

The baseline does not yet imply completion of:

* Full treasury payment execution
* Full alternative asset operations
* Full forecasting engine
* Full enterprise risk engine
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
