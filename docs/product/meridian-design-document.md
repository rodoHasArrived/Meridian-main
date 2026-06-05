# Meridian Design Document — Version 0.15

**Status:** canonical  
**Owner:** core-team  
**Reviewed:** 2026-06-04
**Source:** Draft v1.0 imported from `C:\Users\Andrew James Rowden\.codex\attachments\2bedc368-4dca-449f-923b-b098cf8bb4d5\pasted-text.txt`; Version 0.15 extends the roadmap and source-module registry evidence with the v0.15 accounting records and operational evidence release package.

## 1. Product Vision

Meridian is a modular, configurable financial operations platform for fund administrators, registered investment advisors, family offices, and other investment organizations. The platform helps financial operations professionals acquire, validate, reconcile, govern, analyze, forecast, and report on financial data through a single auditable workflow.

Meridian should not initially try to replace every external system. Instead, it should become the operational system of record for validated workflows, evidence, reconciliations, decisions, and certified reporting outputs. For ledger records specifically, Meridian is the source of all ledger truth; external accounting systems contribute read-only evidence and reconciliation signals unless an approved publishing workflow explicitly exports Meridian-owned entries.

The current product scope is deliberately narrower than the full long-term domain catalog. Active product work should strengthen data confidence, reconciliation, approvals, accounting records, retained evidence, and governed reporting before expanding Backtesting Studio, live trading, full payments, forecasting, enterprise risk, client portal, no-code workflow design, mobile, or other broad platform lanes.

### Core Vision Statement

> Meridian helps financial operations professionals transform fragmented financial data into trusted, auditable operational outcomes.

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

---

## 2. Target Customer Organizations

Meridian is intended to support several related customer types through one configurable platform rather than separate products.

### Primary Customer Types

| Customer Type                  | Primary Needs                                                                                                       |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------------- |
| Fund Administrators            | Reconciliation, NAV support, investor reporting, capital activity, audit evidence, workflow management              |
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
| Financial Operations | Implemented evidence | Reconciliation, casework, accounting close, evidence routing, and W4 ledger review flows. |
| Treasury & Payments | Supported foundation | Cash-flow views, payment-oriented workflow design, and account/ledger seams exist; full payment execution remains later productization. |
| Portfolio & Investment Operations | Implemented evidence | Portfolio, fund-structure, brokerage sync, fund accounts, positions, paper-session, and ledger-backed workflows. |
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
| Administration & Governance | Implemented evidence | Settings, policy, provider setup, audit trail, approval controls, and governed stage gates exist. |
| Audit, Compliance & Regulatory | Implemented evidence | Audit events, evidence manifests, report provenance, approval history, and controlled close/report workflows exist. |
| Workflow & Process Automation | Supported foundation | Shared workflow DTOs, route targets, operator queues, lifecycle transitions, and acceptance gates exist; no-code workflow design remains future work. |
| Document & Knowledge Management | Design-led foundation | Evidence links and report artifacts exist; full document vault and knowledge-management features remain future work. |
| Reporting & Analytics Platform | Implemented evidence | Report-pack workflow, line provenance, trial-balance reporting, report freshness, and export evidence exist. |

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
* Operational dashboards
* Evidence packages
* Approval history

---

## 5.3 Treasury & Payments

### Purpose

Manage cash movement, liquidity, payment workflows, and capital activity.

### Core Flow

```text
Request Payment
→ Validate Payment
→ Approve Payment
→ Execute Payment
→ Reconcile Payment
→ Report Payment
```

### Capabilities

* Bank accounts
* Cash balances
* Liquidity monitoring
* Cash forecasting
* ACH processing
* Wire processing
* Internal transfers
* Payment approvals
* Capital calls
* Distributions
* Investor payments
* Fee payments
* Positive pay support
* Bank integration

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
* Trustees
* Custodians
* Banks
* Lenders
* Borrowers
* Relationship graph
* Ownership percentages
* Authority tracking
* Authorized signers

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
* Structured products
* MBS / ABS / CLO / CMBS
* Fund interests
* Capital commitments
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
* Capital stack modeling
* Senior / junior / mezzanine / equity layers
* Refinancing analysis
* Debt service coverage
* Interest coverage
* LTV analysis
* DSCR analysis
* Covenant tracking
* Borrowing base analysis

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
* Board packets
* Audit packages
* Report templates
* Report packages
* Report approvals
* Distribution rules
* Client portal support
* Document delivery

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
| Financing & Capital Structure     | Debt facilities, loan agreements, capital stacks, covenants, debt schedules, leverage analysis                         |
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
Account
        ↓
Position / Contract / Obligation
        ↓
Expected Cash Flow
        ↓
Actual Transaction / Actual Cash Flow
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
| Instrument         | Defines what something is                      | Bond, stock, loan, lease, swap, real estate asset             |
| Contract           | Defines rights and obligations                 | Loan agreement, bond indenture, lease, credit facility        |
| Obligation         | Future duty or right to pay or receive         | Coupon, principal, rent, capital call, distribution           |
| Expected Cash Flow | Forecasted cash movement from terms            | Scheduled interest, maturity payment, rent payment            |
| Transaction        | Actual observed activity                       | Trade, wire, coupon receipt, journal entry                    |
| Position           | Ownership or exposure at a point in time       | Shares, par value, LP interest, loan balance                  |
| Valuation          | Value assigned to an object                    | Market value, NAV, appraisal, fair value                      |
| Reconciliation     | Comparison between sources                     | Custodian vs internal, bank vs ledger, expected vs actual     |
| Exception          | Difference requiring resolution                | Missing trade, price break, cash variance                     |
| Document           | Supporting evidence                            | Statement, invoice, confirmation, agreement                   |
| Task               | Work assigned to a user                        | Review break, approve payment, validate import                |
| Report Package     | Final output for review/distribution           | Investor report, audit package, board packet                  |
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
| Accounting Entries | GL / accounting system         | Meridian adjustment          | Limited          |
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

---

## 11. Extensibility Strategy

Meridian should have a stable financial operations core with configurable workflows, rules, data mappings, reports, permissions, and domain extensions layered around it.

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
| Reports         | Templates, schedules, recipients, sections                    |
| Permissions     | Roles, data scopes, approval authority                        |
| Classifications | Asset classes, strategies, categories                         |
| Custom Fields   | Tenant-specific attributes                                    |
| Source Priority | Which source wins for prices, positions, cash, terms          |
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
Investor / Vehicle / Account
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
→ Release to Bank
→ Bank Confirmation
→ Reconciliation
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
Contract
Transaction
Reconciliation
Exception
Payment
Report Package
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
| Users bypass controls                      | Segregation of duties enforced by permissions and workflow |
| Reprocessing changes historical data       | Import replay must preserve lineage and prior versions     |
| Rules become untestable                    | Rules require test cases before activation                 |
| Tenant-specific one-off behavior grows     | Use profiles and templates instead of custom code          |

---

## 24. Updated Design Thesis

> Meridian is designed as a modular, configurable financial operations platform. Its core financial model is intentionally stable, centered on entities, accounts, instruments, contracts, obligations, cash flows, transactions, positions, reconciliations, documents, workflows, reports, and audit events. Around that stable core, Meridian provides tenant-specific configuration for workflows, rules, integrations, source-of-record policies, reporting, permissions, and custom attributes. This allows Meridian to support fund administrators, RIAs, family offices, and other investment organizations without creating separate products or sacrificing auditability.

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

---

## 26. Foundational Product Slice

The foundational product slice remains:

# Data Operations + Reconciliation Foundation

This slice is no longer only a recommendation. It is the product baseline that W1-W5 prove through trusted data, paper validation, research continuity, ledger reconciliation, accounting records, approvals, multi-asset operational coverage, and governed reporting.

### Includes

* Tenant profile
* Entity model
* Account model
* Provider setup
* File/API import
* Raw data preservation
* Mapping profile
* Validation rules
* Normalized transactions / positions / balances
* Reconciliation run
* Exception queue
* Workflow approvals
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
